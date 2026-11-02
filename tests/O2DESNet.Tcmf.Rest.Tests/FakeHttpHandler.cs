using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Rest.Tests
{
    /// <summary>Records every request (with its body, read before disposal) and answers through a callback.</summary>
    public sealed class FakeHttpHandler : HttpMessageHandler
    {
        public sealed class Recorded
        {
            public HttpMethod Method;
            public Uri Uri;
            public string Body;
            public string ContentType;
            public string Authorization;
            public Dictionary<string, string> Headers = new Dictionary<string, string>();
        }

        private readonly List<Recorded> _requests = new List<Recorded>();
        public Func<Recorded, int, HttpResponseMessage> Respond { get; set; } = (_, __) => Json(HttpStatusCode.OK, "{}");

        public IReadOnlyList<Recorded> Requests { get { lock (_requests) return _requests.ToArray(); } }

        public static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new Recorded
            {
                Method = request.Method,
                Uri = request.RequestUri,
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(),
                ContentType = request.Content?.Headers.ContentType?.ToString(),
                Authorization = request.Headers.Authorization?.ToString(),
            };
            foreach (var header in request.Headers) recorded.Headers[header.Key] = string.Join(",", header.Value);
            int index;
            lock (_requests) { _requests.Add(recorded); index = _requests.Count; }
            return Respond(recorded, index);
        }
    }
}
