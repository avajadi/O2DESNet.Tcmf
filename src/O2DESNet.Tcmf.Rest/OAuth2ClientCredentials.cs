using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Rest
{
    /// <summary>How the client proves its identity to the token endpoint (RFC 6749 §2.3.1).</summary>
    public enum OAuth2ClientAuthentication
    {
        /// <summary>HTTP Basic header with client_id:client_secret. The RFC default; Keycloak, Entra ID, Okta accept it.</summary>
        BasicHeader,
        /// <summary>client_id and client_secret as form fields in the request body. Required by e.g. Auth0.</summary>
        RequestBody,
    }

    /// <summary>Settings for the OAuth2 client-credentials grant (RFC 6749 §4.4).</summary>
    public sealed class OAuth2ClientCredentialsOptions
    {
        /// <summary>The authorization server's token endpoint, e.g. https://idp.example.com/realms/tcmf/protocol/openid-connect/token</summary>
        public Uri TokenEndpoint { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        /// <summary>Space-separated scopes; omitted from the request when null.</summary>
        public string Scope { get; set; }
        /// <summary>Sent as the non-standard "audience" parameter some servers (Auth0, Keycloak) use; omitted when null.</summary>
        public string Audience { get; set; }
        /// <summary>Any further form fields to send with the token request.</summary>
        public Dictionary<string, string> AdditionalParameters { get; } = new Dictionary<string, string>();
        public OAuth2ClientAuthentication ClientAuthentication { get; set; } = OAuth2ClientAuthentication.BasicHeader;
        /// <summary>A cached token is renewed this long before its expires_in runs out.</summary>
        public TimeSpan ExpiryLeeway { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>Lifetime assumed when the token response carries no expires_in.</summary>
        public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromMinutes(5);

        public void Validate()
        {
            if (TokenEndpoint == null) throw new ArgumentException("TokenEndpoint is required");
            if (!TokenEndpoint.IsAbsoluteUri) throw new ArgumentException("TokenEndpoint must be an absolute URI");
            if (string.IsNullOrEmpty(ClientId)) throw new ArgumentException("ClientId is required");
            if (ClientSecret == null) throw new ArgumentException("ClientSecret is required");
            if (ExpiryLeeway < TimeSpan.Zero) throw new ArgumentException("ExpiryLeeway must not be negative");
            if (DefaultLifetime <= TimeSpan.Zero) throw new ArgumentException("DefaultLifetime must be positive");
        }
    }

    /// <summary>
    /// Obtains access tokens with the client-credentials grant and caches each one until shortly before it expires.
    /// Safe to share between threads.
    /// </summary>
    public sealed class OAuth2ClientCredentialsTokenProvider : IAccessTokenProvider, IDisposable
    {
        private readonly OAuth2ClientCredentialsOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private string _token;
        private DateTime _renewAt = DateTime.MinValue;

        /// <summary>Source of the current UTC time; replaceable for tests.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>Number of token requests made so far.</summary>
        public int TokenRequests { get; private set; }

        /// <param name="options">Token endpoint and client credentials.</param>
        /// <param name="httpClient">Client used for the token endpoint; a private one is created when null.</param>
        public OAuth2ClientCredentialsTokenProvider(OAuth2ClientCredentialsOptions options, HttpClient httpClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            _ownsHttp = httpClient == null;
            _http = httpClient ?? new HttpClient();
        }

        public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh && IsCachedTokenValid()) return _token;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!forceRefresh && IsCachedTokenValid()) return _token;
                var response = await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
                _token = response.AccessToken;
                _renewAt = Clock() + response.Lifetime - _options.ExpiryLeeway;
                return _token;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Drops the cached token so the next call requests a new one.</summary>
        public void Invalidate()
        {
            _token = null;
            _renewAt = DateTime.MinValue;
        }

        private bool IsCachedTokenValid()
        {
            return _token != null && Clock() < _renewAt;
        }

        private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            };
            if (_options.Scope != null) form.Add(new KeyValuePair<string, string>("scope", _options.Scope));
            if (_options.Audience != null) form.Add(new KeyValuePair<string, string>("audience", _options.Audience));
            foreach (var parameter in _options.AdditionalParameters)
                form.Add(new KeyValuePair<string, string>(parameter.Key, parameter.Value));

            using (var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint))
            {
                if (_options.ClientAuthentication == OAuth2ClientAuthentication.BasicHeader)
                {
                    var credentials = Uri.EscapeDataString(_options.ClientId) + ":" + Uri.EscapeDataString(_options.ClientSecret);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
                }
                else
                {
                    form.Add(new KeyValuePair<string, string>("client_id", _options.ClientId));
                    form.Add(new KeyValuePair<string, string>("client_secret", _options.ClientSecret));
                }
                request.Content = new FormUrlEncodedContent(form);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                TokenRequests++;
                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new OAuth2TokenException("Token endpoint unreachable: " + ex.Message, null, null, ex);
                }
                using (response)
                {
                    var body = response.Content == null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new OAuth2TokenException(
                            string.Format("Token endpoint returned {0} ({1}): {2}", (int)response.StatusCode, response.ReasonPhrase, Describe(body)),
                            response.StatusCode, body, null);
                    return TokenResponse.Parse(body, _options.DefaultLifetime);
                }
            }
        }

        private static string Describe(string body)
        {
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
                    {
                        var text = error.ToString();
                        if (root.TryGetProperty("error_description", out var description)) text += " - " + description.ToString();
                        return text;
                    }
                }
            }
            catch (JsonException) { }
            return body.Length > 500 ? body.Substring(0, 500) + "..." : body;
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
            _gate.Dispose();
        }

        private struct TokenResponse
        {
            public string AccessToken;
            public TimeSpan Lifetime;

            public static TokenResponse Parse(string body, TimeSpan defaultLifetime)
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(body); }
                catch (JsonException ex) { throw new OAuth2TokenException("Token response is not JSON", null, body, ex); }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String)
                        throw new OAuth2TokenException("Token response has no access_token", null, body, null);
                    if (root.TryGetProperty("token_type", out var type) && type.ValueKind == JsonValueKind.String
                        && !string.Equals(type.GetString(), "bearer", StringComparison.OrdinalIgnoreCase))
                        throw new OAuth2TokenException("Unsupported token_type '" + type.GetString() + "'", null, body, null);

                    var lifetime = defaultLifetime;
                    if (root.TryGetProperty("expires_in", out var expires))
                    {
                        double seconds;
                        if (expires.ValueKind == JsonValueKind.Number && expires.TryGetDouble(out seconds)) lifetime = TimeSpan.FromSeconds(seconds);
                        else if (expires.ValueKind == JsonValueKind.String
                                 && double.TryParse(expires.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                            lifetime = TimeSpan.FromSeconds(seconds);
                    }
                    return new TokenResponse { AccessToken = token.GetString(), Lifetime = lifetime };
                }
            }
        }
    }

    /// <summary>The token endpoint refused or failed the request.</summary>
    public sealed class OAuth2TokenException : Exception
    {
        /// <summary>HTTP status of the token response; null if no response was received.</summary>
        public HttpStatusCode? StatusCode { get; }
        public string ResponseBody { get; }

        /// <summary>True when retrying later may help (no response, or a 5xx/429 from the token endpoint).</summary>
        public bool IsTransient
        {
            get
            {
                if (StatusCode == null) return true;
                var code = (int)StatusCode.Value;
                return code >= 500 || code == 429 || code == 408;
            }
        }

        public OAuth2TokenException(string message, HttpStatusCode? statusCode, string responseBody, Exception inner)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}
