using System;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace O2DESNet.Tcmf.Kafka
{
    /// <summary>
    /// How the producer authenticates to the Kafka cluster. Created through the static factories and assigned to
    /// <see cref="TcmfKafkaSinkOptions.Authentication"/>; each one sets the matching librdkafka
    /// security.protocol / sasl.* / ssl.* settings.
    /// </summary>
    public abstract class TcmfKafkaAuthentication
    {
        /// <summary>No authentication: PLAINTEXT, or SSL when <see cref="TcmfKafkaSinkOptions.UseTls"/> is set.</summary>
        public static readonly TcmfKafkaAuthentication None = new NoAuthentication();

        /// <summary>SASL/PLAIN username and password (e.g. Confluent Cloud API key and secret, Aiven, Redpanda).</summary>
        public static TcmfKafkaAuthentication SaslPlain(string username, string password)
        {
            return new SaslAuthentication(SaslMechanism.Plain, username, password);
        }

        /// <summary>SASL/SCRAM-SHA-256 username and password.</summary>
        public static TcmfKafkaAuthentication SaslScramSha256(string username, string password)
        {
            return new SaslAuthentication(SaslMechanism.ScramSha256, username, password);
        }

        /// <summary>SASL/SCRAM-SHA-512 username and password (the usual choice with Strimzi and Amazon MSK SCRAM).</summary>
        public static TcmfKafkaAuthentication SaslScramSha512(string username, string password)
        {
            return new SaslAuthentication(SaslMechanism.ScramSha512, username, password);
        }

        /// <summary>
        /// SASL/OAUTHBEARER with the OAuth2 client-credentials grant handled by librdkafka itself (OIDC method):
        /// the client fetches a token from <paramref name="tokenEndpoint"/> and renews it before it expires.
        /// </summary>
        /// <param name="tokenEndpoint">The authorization server's token endpoint.</param>
        /// <param name="clientId">OAuth2 client id.</param>
        /// <param name="clientSecret">OAuth2 client secret.</param>
        /// <param name="scope">Space-separated scopes requested from the token endpoint; optional.</param>
        /// <param name="extensions">SASL extensions as "key=value,key2=value2", e.g. "logicalCluster=lkc-...,identityPoolId=pool-..." for Confluent Cloud; optional.</param>
        public static TcmfKafkaAuthentication OAuthBearer(Uri tokenEndpoint, string clientId, string clientSecret, string scope = null, string extensions = null)
        {
            if (tokenEndpoint == null) throw new ArgumentNullException(nameof(tokenEndpoint));
            if (string.IsNullOrEmpty(clientId)) throw new ArgumentException("Client id is required", nameof(clientId));
            if (clientSecret == null) throw new ArgumentNullException(nameof(clientSecret));
            return new OidcAuthentication(tokenEndpoint, clientId, clientSecret, scope, extensions);
        }

        /// <summary>
        /// SASL/OAUTHBEARER with tokens supplied by your own code, for grants or token services librdkafka's OIDC
        /// client cannot do. The callback is invoked whenever the client needs a (new) token.
        /// </summary>
        public static TcmfKafkaAuthentication OAuthBearer(Func<CancellationToken, Task<OAuthBearerToken>> tokenSource)
        {
            if (tokenSource == null) throw new ArgumentNullException(nameof(tokenSource));
            return new CallbackOAuthBearerAuthentication(tokenSource);
        }

        /// <summary>Mutual TLS with a client certificate and private key in PEM files (PKCS#12 keystores are not supported here; use <see cref="TcmfKafkaSinkOptions.ProducerConfig"/> for ssl.keystore.*).</summary>
        public static TcmfKafkaAuthentication MutualTls(string certificatePath, string keyPath, string keyPassword = null)
        {
            if (string.IsNullOrEmpty(certificatePath)) throw new ArgumentException("Certificate path is required", nameof(certificatePath));
            if (string.IsNullOrEmpty(keyPath)) throw new ArgumentException("Key path is required", nameof(keyPath));
            return new MutualTlsAuthentication(certificatePath, keyPath, null, null, keyPassword);
        }

        /// <summary>Mutual TLS with the client certificate and private key given as PEM text (e.g. from environment variables or a secret store).</summary>
        public static TcmfKafkaAuthentication MutualTlsPem(string certificatePem, string keyPem, string keyPassword = null)
        {
            if (string.IsNullOrEmpty(certificatePem)) throw new ArgumentException("Certificate PEM is required", nameof(certificatePem));
            if (string.IsNullOrEmpty(keyPem)) throw new ArgumentException("Key PEM is required", nameof(keyPem));
            return new MutualTlsAuthentication(null, null, certificatePem, keyPem, keyPassword);
        }

        /// <summary>True for every mechanism except <see cref="None"/>; these always run over TLS.</summary>
        public abstract bool RequiresTls { get; }

        /// <summary>Writes the mechanism's settings into the producer configuration.</summary>
        public abstract void Apply(ProducerConfig config, bool useTls);

        /// <summary>Hooks the producer builder, e.g. for an OAuth token refresh callback. Does nothing by default.</summary>
        public virtual void Apply(ProducerBuilder<string, string> builder) { }

        internal static SecurityProtocol Protocol(bool sasl, bool tls)
        {
            if (sasl) return tls ? SecurityProtocol.SaslSsl : SecurityProtocol.SaslPlaintext;
            return tls ? SecurityProtocol.Ssl : SecurityProtocol.Plaintext;
        }

        private sealed class NoAuthentication : TcmfKafkaAuthentication
        {
            public override bool RequiresTls { get { return false; } }
            public override void Apply(ProducerConfig config, bool useTls)
            {
                config.SecurityProtocol = Protocol(false, useTls);
            }
        }

        private sealed class SaslAuthentication : TcmfKafkaAuthentication
        {
            private readonly SaslMechanism _mechanism;
            private readonly string _username, _password;

            public SaslAuthentication(SaslMechanism mechanism, string username, string password)
            {
                if (string.IsNullOrEmpty(username)) throw new ArgumentException("Username is required", nameof(username));
                if (password == null) throw new ArgumentNullException(nameof(password));
                _mechanism = mechanism;
                _username = username;
                _password = password;
            }

            public override bool RequiresTls { get { return true; } }

            public override void Apply(ProducerConfig config, bool useTls)
            {
                config.SecurityProtocol = Protocol(true, useTls);
                config.SaslMechanism = _mechanism;
                config.SaslUsername = _username;
                config.SaslPassword = _password;
            }
        }

        private sealed class OidcAuthentication : TcmfKafkaAuthentication
        {
            private readonly Uri _tokenEndpoint;
            private readonly string _clientId, _clientSecret, _scope, _extensions;

            public OidcAuthentication(Uri tokenEndpoint, string clientId, string clientSecret, string scope, string extensions)
            {
                _tokenEndpoint = tokenEndpoint;
                _clientId = clientId;
                _clientSecret = clientSecret;
                _scope = scope;
                _extensions = extensions;
            }

            public override bool RequiresTls { get { return true; } }

            public override void Apply(ProducerConfig config, bool useTls)
            {
                config.SecurityProtocol = Protocol(true, useTls);
                config.SaslMechanism = SaslMechanism.OAuthBearer;
                config.SaslOauthbearerMethod = SaslOauthbearerMethod.Oidc;
                config.SaslOauthbearerTokenEndpointUrl = _tokenEndpoint.ToString();
                config.SaslOauthbearerClientId = _clientId;
                config.SaslOauthbearerClientSecret = _clientSecret;
                if (_scope != null) config.SaslOauthbearerScope = _scope;
                if (_extensions != null) config.SaslOauthbearerExtensions = _extensions;
            }
        }

        private sealed class CallbackOAuthBearerAuthentication : TcmfKafkaAuthentication
        {
            private readonly Func<CancellationToken, Task<OAuthBearerToken>> _tokenSource;

            public CallbackOAuthBearerAuthentication(Func<CancellationToken, Task<OAuthBearerToken>> tokenSource)
            {
                _tokenSource = tokenSource;
            }

            public override bool RequiresTls { get { return true; } }

            public override void Apply(ProducerConfig config, bool useTls)
            {
                config.SecurityProtocol = Protocol(true, useTls);
                config.SaslMechanism = SaslMechanism.OAuthBearer;
                config.SaslOauthbearerMethod = SaslOauthbearerMethod.Default;
            }

            public override void Apply(ProducerBuilder<string, string> builder)
            {
                builder.SetOAuthBearerTokenRefreshHandler((producer, oauthConfig) =>
                {
                    // librdkafka invokes this on its background thread and expects a synchronous answer.
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                        {
                            var token = _tokenSource(cts.Token).GetAwaiter().GetResult();
                            if (token == null || string.IsNullOrEmpty(token.Value)) throw new InvalidOperationException("Token source returned no token");
                            producer.OAuthBearerSetToken(token.Value, new DateTimeOffset(token.ExpiresAt.ToUniversalTime()).ToUnixTimeMilliseconds(),
                                token.Principal ?? "", token.Extensions);
                        }
                    }
                    catch (Exception ex)
                    {
                        producer.OAuthBearerSetTokenFailure(ex.Message);
                    }
                });
            }
        }

        private sealed class MutualTlsAuthentication : TcmfKafkaAuthentication
        {
            private readonly string _certificatePath, _keyPath, _certificatePem, _keyPem, _keyPassword;

            public MutualTlsAuthentication(string certificatePath, string keyPath, string certificatePem, string keyPem, string keyPassword)
            {
                _certificatePath = certificatePath;
                _keyPath = keyPath;
                _certificatePem = certificatePem;
                _keyPem = keyPem;
                _keyPassword = keyPassword;
            }

            public override bool RequiresTls { get { return true; } }

            public override void Apply(ProducerConfig config, bool useTls)
            {
                config.SecurityProtocol = SecurityProtocol.Ssl;
                if (_certificatePath != null) config.SslCertificateLocation = _certificatePath;
                if (_keyPath != null) config.SslKeyLocation = _keyPath;
                if (_certificatePem != null) config.SslCertificatePem = _certificatePem;
                if (_keyPem != null) config.SslKeyPem = _keyPem;
                if (_keyPassword != null) config.SslKeyPassword = _keyPassword;
            }
        }
    }

    /// <summary>A token handed to librdkafka by a custom OAUTHBEARER callback.</summary>
    public sealed class OAuthBearerToken
    {
        public string Value { get; }
        /// <summary>When the token expires; librdkafka refreshes it ahead of this time.</summary>
        public DateTime ExpiresAt { get; }
        /// <summary>The principal name the token represents; optional.</summary>
        public string Principal { get; }
        /// <summary>SASL extensions sent with the token; optional.</summary>
        public System.Collections.Generic.IDictionary<string, string> Extensions { get; }

        public OAuthBearerToken(string value, DateTime expiresAt, string principal = null, System.Collections.Generic.IDictionary<string, string> extensions = null)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Token value is required", nameof(value));
            Value = value;
            ExpiresAt = expiresAt;
            Principal = principal;
            Extensions = extensions;
        }
    }
}
