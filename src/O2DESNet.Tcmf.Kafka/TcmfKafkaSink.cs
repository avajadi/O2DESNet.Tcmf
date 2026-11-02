using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Confluent.Kafka;

namespace O2DESNet.Tcmf.Kafka
{
    /// <summary>
    /// Produces every TCMF message to a Kafka topic as a JSON string record, keyed by its first grouping so a
    /// timeline keeps its order. <see cref="Publish"/> hands the record to the producer's buffer and returns;
    /// delivery is confirmed asynchronously. Call <see cref="Flush"/> to wait for acknowledgements, and dispose
    /// the sink when done.
    /// </summary>
    public sealed class TcmfKafkaSink : ITcmfSink, IDisposable
    {
        private const int QueueFullRetries = 100;

        private readonly TcmfKafkaSinkOptions _options;
        private readonly IProducer<string, string> _producer;
        private readonly bool _ownsProducer;
        private readonly object _gate = new object();
        private readonly List<TcmfKafkaDeliveryFailure> _unreportedFailures = new List<TcmfKafkaDeliveryFailure>();
        private bool _disposed;

        public TcmfKafkaSinkOptions Options { get { return _options; } }

        /// <summary>Messages handed to the producer.</summary>
        public long PublishedCount { get; private set; }
        /// <summary>Messages acknowledged by the brokers.</summary>
        public long DeliveredCount { get; private set; }
        /// <summary>Messages the producer reported as failed.</summary>
        public long FailedCount { get; private set; }

        /// <summary>Raised on the producer's poll thread for every message that could not be delivered.</summary>
        public event Action<TcmfKafkaDeliveryFailure> OnDeliveryFailed = failure => { };

        /// <summary>Creates a sink with its own producer built from <paramref name="options"/>.</summary>
        public TcmfKafkaSink(TcmfKafkaSinkOptions options) : this(options, null) { }

        public TcmfKafkaSink(string bootstrapServers, string topic, TcmfKafkaAuthentication authentication = null)
            : this(new TcmfKafkaSinkOptions(bootstrapServers, topic) { Authentication = authentication ?? TcmfKafkaAuthentication.None }) { }

        /// <summary>
        /// Creates a sink over an existing producer, e.g. one shared with other parts of the application or a
        /// test double. The producer is not disposed with the sink; only <see cref="TcmfKafkaSinkOptions.Topic"/>,
        /// <see cref="TcmfKafkaSinkOptions.KeySelector"/> and <see cref="TcmfKafkaSinkOptions.AddHeaders"/> are used from the options.
        /// </summary>
        public TcmfKafkaSink(TcmfKafkaSinkOptions options, IProducer<string, string> producer)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (producer == null)
            {
                var config = options.ToProducerConfig();
                var builder = new ProducerBuilder<string, string>(config);
                options.Authentication.Apply(builder);
                _producer = builder.Build();
                _ownsProducer = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.Topic)) throw new ArgumentException("Topic is required");
                if (options.KeySelector == null) throw new ArgumentException("KeySelector is required");
                _producer = producer;
            }
        }

        public void Publish(TcmfMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (_disposed) throw new ObjectDisposedException(nameof(TcmfKafkaSink));

            var record = new Message<string, string>
            {
                Key = _options.KeySelector(message),
                Value = message.ToJson(),
            };
            if (_options.AddHeaders)
            {
                record.Headers = new Headers();
                if (message.MessageId != null) record.Headers.Add("tcmf.messageId", Encoding.UTF8.GetBytes(message.MessageId));
                if (message.Payload != null) record.Headers.Add("tcmf.payloadType", Encoding.UTF8.GetBytes(message.Payload.TypeName));
                if (message.Version != null) record.Headers.Add("tcmf.version", Encoding.UTF8.GetBytes(message.Version));
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    _producer.Produce(_options.Topic, record, report => OnDelivered(message, report));
                    lock (_gate) PublishedCount++;
                    return;
                }
                catch (ProduceException<string, string> ex) when (ex.Error.Code == ErrorCode.Local_QueueFull && attempt < QueueFullRetries)
                {
                    // The local buffer is full: let the producer make progress, then try again.
                    _producer.Poll(TimeSpan.FromMilliseconds(100));
                }
            }
        }

        private void OnDelivered(TcmfMessage message, DeliveryReport<string, string> report)
        {
            if (!report.Error.IsError)
            {
                lock (_gate) DeliveredCount++;
                return;
            }
            var failure = new TcmfKafkaDeliveryFailure(message, report.Error, report.Topic, report.Partition);
            lock (_gate)
            {
                FailedCount++;
                _unreportedFailures.Add(failure);
            }
            try { OnDeliveryFailed(failure); } catch (Exception) { }
        }

        /// <summary>
        /// Waits until every produced message is acknowledged or failed. Throws <see cref="TcmfKafkaDeliveryException"/>
        /// if any delivery failed since the previous flush (see <see cref="TcmfKafkaSinkOptions.ThrowOnFlushFailure"/>)
        /// and <see cref="TimeoutException"/> if messages are still outstanding after <paramref name="timeout"/>.
        /// </summary>
        public void Flush(TimeSpan? timeout = null)
        {
            var remaining = _producer.Flush(timeout ?? Timeout.InfiniteTimeSpan);
            if (remaining > 0) throw new TimeoutException(string.Format("{0} TCMF message(s) still unacknowledged after {1}", remaining, timeout));
            ThrowIfFailed();
        }

        /// <summary>Failures since the previous call to <see cref="Flush"/> or this method.</summary>
        public IReadOnlyList<TcmfKafkaDeliveryFailure> TakeFailures()
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
            var failures = TakeFailures();
            if (_options.ThrowOnFlushFailure && failures.Count > 0) throw new TcmfKafkaDeliveryException(failures);
        }

        /// <summary>Waits up to <see cref="TcmfKafkaSinkOptions.ShutdownTimeout"/> for acknowledgements, then releases the producer if the sink owns it. Does not throw on delivery failures; call <see cref="Flush"/> first to surface those.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _producer.Flush(_options.ShutdownTimeout); } catch (Exception) { }
            if (_ownsProducer) _producer.Dispose();
        }
    }

    /// <summary>A message the brokers did not accept.</summary>
    public sealed class TcmfKafkaDeliveryFailure
    {
        public TcmfMessage Message { get; }
        public Error Error { get; }
        public string Topic { get; }
        public Partition Partition { get; }

        public TcmfKafkaDeliveryFailure(TcmfMessage message, Error error, string topic, Partition partition)
        {
            Message = message;
            Error = error;
            Topic = topic;
            Partition = partition;
        }

        public override string ToString()
        {
            return string.Format("{0} not delivered to {1}: {2} ({3})", Message.MessageId ?? "message", Topic, Error.Reason, Error.Code);
        }
    }

    /// <summary>Raised by <see cref="TcmfKafkaSink.Flush"/> when deliveries failed since the previous flush.</summary>
    public sealed class TcmfKafkaDeliveryException : Exception
    {
        public IReadOnlyList<TcmfKafkaDeliveryFailure> Failures { get; }

        public TcmfKafkaDeliveryException(IReadOnlyList<TcmfKafkaDeliveryFailure> failures)
            : base(string.Format("{0} TCMF message(s) could not be delivered to Kafka. First failure: {1}",
                failures.Count, failures.Count > 0 ? failures[0].ToString() : "none"))
        {
            Failures = failures;
        }
    }
}
