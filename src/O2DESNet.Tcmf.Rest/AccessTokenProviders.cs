using System;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Rest
{
    /// <summary>
    /// Supplies the bearer token the <see cref="TcmfRestSink"/> puts in the Authorization header.
    /// Implementations: <see cref="OAuth2ClientCredentialsTokenProvider"/>, <see cref="StaticAccessTokenProvider"/>,
    /// <see cref="DelegateAccessTokenProvider"/>.
    /// </summary>
    public interface IAccessTokenProvider
    {
        /// <summary>
        /// Returns a token that is expected to be valid now. When <paramref name="forceRefresh"/> is true the
        /// previously returned token was rejected (HTTP 401) and a fresh one must be obtained.
        /// </summary>
        Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
    }

    /// <summary>A fixed token, e.g. a long-lived API token issued out of band.</summary>
    public sealed class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _token;

        public StaticAccessTokenProvider(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("Token is required", nameof(token));
            _token = token;
        }

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            return Task.FromResult(_token);
        }
    }

    /// <summary>Wraps any token-acquisition callback, e.g. a vendor SDK or a different OAuth2 grant.</summary>
    public sealed class DelegateAccessTokenProvider : IAccessTokenProvider
    {
        private readonly Func<bool, CancellationToken, Task<string>> _provider;

        public DelegateAccessTokenProvider(Func<bool, CancellationToken, Task<string>> provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public DelegateAccessTokenProvider(Func<CancellationToken, Task<string>> provider)
            : this((_, ct) => provider(ct))
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
        }

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            return _provider(forceRefresh, cancellationToken);
        }
    }
}
