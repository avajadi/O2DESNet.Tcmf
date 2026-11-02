using System;
using System.Collections.Generic;
using System.Net.Http;

namespace O2DESNet.Tcmf.Rest
{
    /// <summary>Where and how <see cref="TcmfRestSink"/> delivers messages.</summary>
    public sealed class TcmfRestSinkOptions
    {
        /// <summary>Absolute URL that receives the messages.</summary>
        public Uri Endpoint { get; set; }

        /// <summary>HTTP method, POST by default.</summary>
        public HttpMethod Method { get; set; } = HttpMethod.Post;

        /// <summary>
        /// Source of the bearer token for the Authorization header; null sends no Authorization header.
        /// Use <see cref="OAuth2ClientCredentialsTokenProvider"/> for OAuth2, or <see cref="StaticAccessTokenProvider"/> for a fixed token.
        /// </summary>
        public IAccessTokenProvider AccessTokenProvider { get; set; }

        /// <summary>Authorization scheme used with the token, "Bearer" by default.</summary>
        public string AuthorizationScheme { get; set; } = "Bearer";

        /// <summary>Extra request headers, e.g. an X-Api-Key.</summary>
        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Messages per request. 1 (default) sends each message as a JSON object; larger values send a JSON array
        /// of up to this many messages, which is what a bulk endpoint expects.
        /// </summary>
        public int BatchSize { get; set; } = 1;

        /// <summary>How long to wait for more messages to fill a batch before sending a partial one.</summary>
        public TimeSpan BatchLinger { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Retries after a transport error, timeout, 408, 429 or 5xx. 0 disables retries.</summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>Delay before the first retry; doubles on every attempt up to <see cref="MaxRetryDelay"/>. A Retry-After header overrides it.</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Timeout for a single request.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Messages buffered before <see cref="TcmfRestSink.Publish"/> blocks and waits for delivery to catch up.</summary>
        public int QueueCapacity { get; set; } = 10000;

        /// <summary>How long <see cref="TcmfRestSink.Dispose"/> waits for queued messages to be delivered.</summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Make <see cref="TcmfRestSink.Flush"/> throw when deliveries failed since the previous flush. On by default.</summary>
        public bool ThrowOnFlushFailure { get; set; } = true;

        public TcmfRestSinkOptions() { }

        public TcmfRestSinkOptions(string endpoint) : this(new Uri(endpoint, UriKind.Absolute)) { }

        public TcmfRestSinkOptions(Uri endpoint) { Endpoint = endpoint; }

        public void Validate()
        {
            if (Endpoint == null) throw new ArgumentException("Endpoint is required");
            if (!Endpoint.IsAbsoluteUri) throw new ArgumentException("Endpoint must be an absolute URI");
            if (Method == null) throw new ArgumentException("Method is required");
            if (string.IsNullOrEmpty(AuthorizationScheme) && AccessTokenProvider != null) throw new ArgumentException("AuthorizationScheme is required");
            if (BatchSize < 1) throw new ArgumentException("BatchSize must be at least 1");
            if (MaxRetries < 0) throw new ArgumentException("MaxRetries must not be negative");
            if (RetryDelay < TimeSpan.Zero || MaxRetryDelay < TimeSpan.Zero) throw new ArgumentException("Retry delays must not be negative");
            if (RequestTimeout <= TimeSpan.Zero) throw new ArgumentException("RequestTimeout must be positive");
            if (QueueCapacity < 1) throw new ArgumentException("QueueCapacity must be at least 1");
        }
    }
}
