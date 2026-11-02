# O2DESNet.Tcmf.Kafka

Kafka sink for [O2DESNet.Tcmf](https://github.com/avajadi/O2DESNet.Tcmf): publishes the TCMF
(Transport Collaboration Messaging Format) messages an O²DES.NET simulation reports to a Kafka topic.

```
dotnet add package O2DESNet.Tcmf.Kafka
```

- One JSON record per message, keyed by the first grouping identifier so each timeline stays ordered on one partition.
- `tcmf.messageId`, `tcmf.payloadType` and `tcmf.version` record headers.
- `acks=all` and idempotence on by default; any librdkafka setting can be overridden through `ProducerConfig`.
- Cluster authentication over TLS: SASL/PLAIN, SASL/SCRAM-SHA-256/512, SASL/OAUTHBEARER (OIDC client
  credentials, or your own token callback) and mutual TLS.
- `Flush()` throws `TcmfKafkaDeliveryException` when a record was rejected; `OnDeliveryFailed` reports each one.

```csharp
using O2DESNet.Tcmf;
using O2DESNet.Tcmf.Kafka;

var options = new TcmfKafkaSinkOptions("broker1:9093,broker2:9093", "tcmf")
{
    Authentication = TcmfKafkaAuthentication.SaslScramSha512("sim", Environment.GetEnvironmentVariable("KAFKA_PASSWORD")),
    CaCertificatePath = "/etc/kafka/ca.pem",
};
using var sink = new TcmfKafkaSink(options);
var reporter = new TcmfReporter(sim, sink, TcmfIdentifier.User("RISE", "o2des_sim"));
sim.Run();
sink.Flush();
```

For OAuth2 against the brokers:

```csharp
Authentication = TcmfKafkaAuthentication.OAuthBearer(
    new Uri("https://idp.example.org/realms/tcmf/protocol/openid-connect/token"),
    "sim-client", clientSecret, scope: "kafka")
```

Full documentation, the REST sink and the core library: https://github.com/avajadi/O2DESNet.Tcmf

MIT licensed by RISE Research Institutes of Sweden.
