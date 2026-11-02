using System;
using System.Collections.Generic;
using System.Net;

namespace O2DESNet.Tcmf.Rest
{
    /// <summary>A batch of messages the sink gave up on, with what went wrong on the last attempt.</summary>
    public sealed class TcmfRestDeliveryFailure
    {
        public IReadOnlyList<TcmfMessage> Messages { get; }
        /// <summary>Status of the last response, or null when no response was received.</summary>
        public HttpStatusCode? StatusCode { get; }
        /// <summary>Beginning of the last response body, if any.</summary>
        public string ResponseBody { get; }
        /// <summary>The exception that ended the last attempt, if any.</summary>
        public Exception Exception { get; }
        public int Attempts { get; }

        public TcmfRestDeliveryFailure(IReadOnlyList<TcmfMessage> messages, HttpStatusCode? statusCode, string responseBody, Exception exception, int attempts)
        {
            Messages = messages;
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Exception = exception;
            Attempts = attempts;
        }

        public override string ToString()
        {
            var reason = StatusCode != null
                ? string.Format("HTTP {0}{1}", (int)StatusCode.Value, string.IsNullOrEmpty(ResponseBody) ? "" : ": " + ResponseBody)
                : Exception != null ? Exception.Message : "unknown error";
            return string.Format("{0} message(s) not delivered after {1} attempt(s): {2}", Messages.Count, Attempts, reason);
        }
    }

    /// <summary>Raised by <see cref="TcmfRestSink.Flush"/> when deliveries failed since the previous flush.</summary>
    public sealed class TcmfRestDeliveryException : Exception
    {
        public IReadOnlyList<TcmfRestDeliveryFailure> Failures { get; }

        public TcmfRestDeliveryException(IReadOnlyList<TcmfRestDeliveryFailure> failures)
            : base(Describe(failures), failures.Count > 0 ? failures[0].Exception : null)
        {
            Failures = failures;
        }

        private static string Describe(IReadOnlyList<TcmfRestDeliveryFailure> failures)
        {
            int messages = 0;
            foreach (var failure in failures) messages += failure.Messages.Count;
            return string.Format("{0} TCMF message(s) in {1} request(s) could not be delivered. First failure: {2}",
                messages, failures.Count, failures.Count > 0 ? failures[0].ToString() : "none");
        }
    }
}
