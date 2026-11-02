# O2DESNet.Tcmf.Rest

REST sink for [O2DESNet.Tcmf](https://github.com/avajadi/O2DESNet.Tcmf): posts the TCMF
(Transport Collaboration Messaging Format) messages an O²DES.NET simulation reports to an HTTP endpoint.

```
dotnet add package O2DESNet.Tcmf.Rest
```

- Messages are queued and sent in order from a background worker, one JSON object per request or a JSON
  array of up to `BatchSize` messages.
- Transport errors, timeouts, 408, 429 and 5xx are retried with exponential backoff and `Retry-After`;
  other 4xx responses fail the message at once.
- OAuth2 client-credentials authentication with token caching and a forced refresh on 401; static tokens,
  custom token sources and API-key headers are also supported.
- `Flush()` throws `TcmfRestDeliveryException` when a request was given up on; `OnDeliveryFailed` reports each one.

```csharp
using O2DESNet.Tcmf;
using O2DESNet.Tcmf.Rest;

var oauth = new OAuth2ClientCredentialsOptions
{
    TokenEndpoint = new Uri("https://idp.example.org/realms/tcmf/protocol/openid-connect/token"),
    ClientId = "o2des-sim",
    ClientSecret = Environment.GetEnvironmentVariable("TCMF_CLIENT_SECRET"),
    Scope = "tcmf:write",
};
var options = new TcmfRestSinkOptions("https://tcmf.example.org/api/messages")
{
    AccessTokenProvider = new OAuth2ClientCredentialsTokenProvider(oauth),
    BatchSize = 100,
};
using var sink = new TcmfRestSink(options);
var reporter = new TcmfReporter(sim, sink, TcmfIdentifier.User("RISE", "o2des_sim"));
sim.Run();
sink.Flush();
```

Full documentation, the Kafka sink and the core library: https://github.com/avajadi/O2DESNet.Tcmf

MIT licensed by RISE Research Institutes of Sweden.
