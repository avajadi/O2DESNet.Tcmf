using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Rest
{
    /// <summary>
    /// Delivers TCMF messages to an HTTP endpoint as JSON. <see cref="Publish"/> only queues the message; a
    /// background worker sends them in order, retrying transient failures, and attaches a bearer token obtained
    /// from the configured <see cref="IAccessTokenProvider"/> (e.g. OAuth2 client credentials).
    /// Call <see cref="Flush"/> to wait for delivery, and dispose the sink when done.
    /// </summary>
    public sealed class TcmfRestSink : ITcmfSink, IDisposable
    {
        private static readonly MediaTypeHeaderValue JsonMediaType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        private readonly TcmfRestSinkOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly BlockingCollection<TcmfMessage> _queue;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly Task _worker;
        private readonly object _gate = new object();
        private readonly List<TcmfRestDeliveryFailure> _unreportedFailures = new List<TcmfRestDeliveryFailure>();
        private int _pending;
        private bool _disposed;

        public TcmfRestSinkOptions Options { get { return _options; } }

        /// <summary>Messages accepted by <see cref="Publish"/>.</summary>
        public long PublishedCount { get; private set; }
        /// <summary>Messages acknowledged with a 2xx response.</summary>
        public long DeliveredCount { get; private set; }
        /// <summary>Messages given up on.</summary>
        public long FailedCount { get; private set; }
        /// <summary>HTTP requests sent, including retries.</summary>
        public long RequestCount { get; private set; }
        /// <summary>Messages queued or in flight.</summary>
        public int PendingCount { get { lock (_gate) return _pending; } }

        /// <summary>Raised on the worker thread for every batch the sink gives up on.</summary>
        public event Action<TcmfRestDeliveryFailure> OnDeliveryFailed = failure => { };

        /// <summary>Creates a sink with its own <see cref="HttpClient"/>.</summary>
        public TcmfRestSink(TcmfRestSinkOptions options) : this(options, null) { }

        public TcmfRestSink(string endpoint, IAccessTokenProvider accessTokenProvider = null)
            : this(new TcmfRestSinkOptions(endpoint) { AccessTokenProvider = accessTokenProvider }) { }

        /// <summary>
        /// Creates a sink that sends through <paramref name="httpClient"/> (e.g. one from an IHttpClientFactory, or
        /// a test double). The client is not disposed with the sink.
        /// </summary>
        public TcmfRestSink(TcmfRestSinkOptions options, HttpClient httpClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            _ownsHttp = httpClient == null;
            _http = httpClient ?? new HttpClient();
            _queue = new BlockingCollection<TcmfMessage>(options.QueueCapacity);
            _worker = Task.Factory.StartNew(RunAsync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        /// <summary>Queues the message. Blocks only when <see cref="TcmfRestSinkOptions.QueueCapacity"/> messages are already waiting.</summary>
        public void Publish(TcmfMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (_disposed) throw new ObjectDisposedException(nameof(TcmfRestSink));
            lock (_gate) { _pending++; PublishedCount++; }
            try
            {
                _queue.Add(message);
            }
            catch
            {
                lock (_gate) { _pending--; PublishedCount--; Monitor.PulseAll(_gate); }
                throw;
            }
        }

        /// <summary>
        /// Waits until every queued message has been delivered or given up on. Throws
        /// <see cref="TcmfRestDeliveryException"/> if any delivery failed since the previous flush (see
        /// <see cref="TcmfRestSinkOptions.ThrowOnFlushFailure"/>) and <see cref="TimeoutException"/> if the queue
        /// does not drain within <paramref name="timeout"/>.
        /// </summary>
        public void Flush(TimeSpan? timeout = null)
        {
            var deadline = timeout.HasValue ? Stopwatch.StartNew() : null;
            lock (_gate)
            {
                while (_pending > 0)
                {
                    if (_worker.IsCompleted) break;
                    if (deadline == null) Monitor.Wait(_gate, 100);
                    else
                    {
                        var remaining = timeout.Value - deadline.Elapsed;
                        if (remaining <= TimeSpan.Zero)
                            throw new TimeoutException(string.Format("{0} TCMF message(s) still pending after {1}", _pending, timeout.Value));
                        Monitor.Wait(_gate, remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100));
                    }
                }
            }
            ThrowIfFailed();
        }

        /// <summary>Asynchronous counterpart of <see cref="Flush"/>.</summary>
        public async Task FlushAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var deadline = Stopwatch.StartNew();
            while (PendingCount > 0 && !_worker.IsCompleted)
            {
                if (timeout.HasValue && deadline.Elapsed > timeout.Value)
                    throw new TimeoutException(string.Format("{0} TCMF message(s) still pending after {1}", PendingCount, timeout.Value));
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
            ThrowIfFailed();
        }

        /// <summary>Failures since the previous call to <see cref="Flush"/> or this method.</summary>
        public IReadOnlyList<TcmfRestDeliveryFailure> TakeFailures()
        {
            lock (_gate)
            {
                var failures = _unreportedFailures.ToArray();
                _unreportedFailures.Clear();
                return failures;
            }
        }

        private void ThrowIfFailed()
        {
            if (!_options.ThrowOnFlushFailure) { TakeFailures(); return; }
            var failures = TakeFailures();
            if (failures.Count > 0) throw new TcmfRestDeliveryException(failures);
        }

        /// <summary>Waits up to <see cref="TcmfRestSinkOptions.ShutdownTimeout"/> for queued messages, then releases resources. Does not throw on delivery failures; call <see cref="Flush"/> first to surface those.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CompleteAdding();
            if (!_worker.Wait(_options.ShutdownTimeout)) _shutdown.Cancel();
            try { _worker.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            _shutdown.Dispose();
            _queue.Dispose();
            if (_ownsHttp) _http.Dispose();
            var disposableProvider = _options.AccessTokenProvider as IDisposable;
            if (disposableProvider != null) disposableProvider.Dispose();
        }

        private async Task RunAsync()
        {
            var batch = new List<TcmfMessage>(_options.BatchSize);
            while (TakeBatch(batch))
            {
                try
                {
                    await SendBatchAsync(batch).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RecordFailure(batch, null, null, ex, 0);
                }
                finally
                {
                    lock (_gate) { _pending -= batch.Count; Monitor.PulseAll(_gate); }
                    batch.Clear();
                }
            }
        }

        private bool TakeBatch(List<TcmfMessage> batch)
        {
            TcmfMessage first;
            try
            {
                first = _queue.Take();
            }
            catch (InvalidOperationException)
            {
                return false; // completed and drained
            }
            batch.Add(first);
            if (_options.BatchSize > 1)
            {
                var linger = Stopwatch.StartNew();
                TcmfMessage next;
                while (batch.Count < _options.BatchSize)
                {
                    var remaining = _options.BatchLinger - linger.Elapsed;
                    var wait = remaining > TimeSpan.Zero ? (int)remaining.TotalMilliseconds : 0;
                    if (!_queue.TryTake(out next, wait)) break;
                    batch.Add(next);
                }
            }
            return true;
        }

        private string Serialize(List<TcmfMessage> batch)
        {
            if (_options.BatchSize == 1) return batch[0].ToJson();
            var sb = new StringBuilder("[");
            for (int i = 0; i < batch.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(batch[i].ToJson());
            }
            return sb.Append(']').ToString();
        }

        private async Task SendBatchAsync(List<TcmfMessage> batch)
        {
            var body = Serialize(batch);
            var ct = _shutdown.Token;
            int attempt = 0;
            bool refreshToken = false, tokenRetried = false;

            while (true)
            {
                attempt++;
                HttpStatusCode? status = null;
                string responseBody = null;
                Exception error = null;
                TimeSpan? retryAfter = null;
                bool retryable;

                try
                {
                    using (var request = new HttpRequestMessage(_options.Method, _options.Endpoint))
                    {
                        if (_options.AccessTokenProvider != null)
                        {
                            var token = await _options.AccessTokenProvider.GetAccessTokenAsync(refreshToken, ct).ConfigureAwait(false);
                            request.Headers.Authorization = new AuthenticationHeaderValue(_options.AuthorizationScheme, token);
                            refreshToken = false;
                        }
                        foreach (var header in _options.Headers)
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        request.Content = new StringContent(body, Encoding.UTF8);
                        request.Content.Headers.ContentType = JsonMediaType;

                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            timeout.CancelAfter(_options.RequestTimeout);
                            lock (_gate) RequestCount++;
                            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    lock (_gate) DeliveredCount += batch.Count;
                                    return;
                                }
                                status = response.StatusCode;
                                responseBody = await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
                                if (response.Headers.RetryAfter != null)
                                {
                                    if (response.Headers.RetryAfter.Delta.HasValue) retryAfter = response.Headers.RetryAfter.Delta;
                                    else if (response.Headers.RetryAfter.Date.HasValue) retryAfter = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                                }
                            }
                        }
                    }

                    if (status == HttpStatusCode.Unauthorized && _options.AccessTokenProvider != null && !tokenRetried)
                    {
                        // The token may have been revoked or expired early: get a fresh one and try once more, without counting it as a retry.
                        tokenRetried = true;
                        refreshToken = true;
                        attempt--;
                        continue;
                    }
                    var code = (int)status.Value;
                    retryable = code == 408 || code == 429 || code >= 500;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    RecordFailure(batch, null, null, new OperationCanceledException("Sink disposed before the message was delivered"), attempt);
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    error = new TimeoutException(string.Format("Request timed out after {0}", _options.RequestTimeout), ex);
                    retryable = true;
                }
                catch (HttpRequestException ex)
                {
                    error = ex;
                    retryable = true;
                }
                catch (OAuth2TokenException ex)
                {
                    error = ex;
                    retryable = ex.IsTransient;
                }

                if (!retryable || attempt > _options.MaxRetries)
                {
                    RecordFailure(batch, status, responseBody, error, attempt);
                    return;
                }

                var delay = retryAfter ?? Backoff(attempt);
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    RecordFailure(batch, status, responseBody, error ?? new OperationCanceledException("Sink disposed before the message was delivered"), attempt);
                    return;
                }
            }
        }

        private TimeSpan Backoff(int attempt)
        {
            var delay = TimeSpan.FromTicks(_options.RetryDelay.Ticks * (1L << Math.Min(attempt - 1, 20)));
            return delay > _options.MaxRetryDelay ? _options.MaxRetryDelay : delay;
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.Content == null) return null;
            try
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return body.Length > 2000 ? body.Substring(0, 2000) + "..." : body;
            }
            catch (Exception) { return null; }
        }

        private void RecordFailure(List<TcmfMessage> batch, HttpStatusCode? status, string responseBody, Exception error, int attempts)
        {
            var failure = new TcmfRestDeliveryFailure(batch.ToArray(), status, responseBody, error, attempts);
            lock (_gate)
            {
                FailedCount += batch.Count;
                _unreportedFailures.Add(failure);
            }
            try { OnDeliveryFailed(failure); } catch (Exception) { }
        }
    }
}
