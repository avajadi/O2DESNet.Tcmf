# O2DESNet.Tcmf

Generate TCMF (Transport Collaboration Messaging Format, version 0.0.7)
messages from [O²DES.NET](https://github.com/li-haobin/O2DESNet) discrete-event simulations.

TCMF is the JSON format RISE uses for collaborative decision making between transport actors
(ports, terminals, railways, hauliers). This library sits on top of the O2DESNet framework and turns
simulation events into TCMF messages, stamped with the simulation clock, so a digital twin can feed
the same timelines a live system would.

## Install

```
dotnet add package O2DESNet.Tcmf
```

The package depends on `O2DESNet` 3.7.2 (netstandard2.1) and `System.Text.Json`.
Until it is published, add a project reference to `src/O2DESNet.Tcmf/O2DESNet.Tcmf.csproj`.

Delivery to a broker or an API lives in two separate packages, so you only pull in the client you need:

```
dotnet add package O2DESNet.Tcmf.Kafka   # Confluent.Kafka producer, SASL / OAUTHBEARER / mTLS
dotnet add package O2DESNet.Tcmf.Rest    # HttpClient, OAuth2 client credentials
```

## What is in it

- `TcmfMessage` and the six payload types (`LocationState`, `ServiceState`, `AdministrativeState`,
  `CarrierState`, `AttributeState`, `MessageOperation`), with `ToJson()`, `FromJson()` and schema `Validate()`.
- `TcmfIdentifier` builds the `tcmf:<category>:...` identifiers; `GroupingType` restricts groupings to the types
  the schema allows.
- `TcmfReporter` is attached to any sandbox and publishes `Arrived`, `Departed`, `ServiceCommenced`,
  `BoundTo`, `SetAttribute`, `Invalidate`, ... at the current clock time. `AnchorClock` maps the simulation
  clock onto a calendar date and `SeededMessageIds` gives reproducible message ids.
- Sinks: `TcmfCollector` (in memory), `TcmfJsonLinesSink` (one JSON message per line),
  `TcmfDelegateSink` (wrap any producer, e.g. Kafka), `TcmfCompositeSink` (fan out).
- `ReportTcmf` extension methods hook a standard `Server` (ServiceState commenced/completed) or `Queue`
  (LocationState arrived_to) up to a reporter; `TcmfLoad` is a `Load` that carries its reference object
  and groupings.

## Usage

```csharp
using O2DESNet.Standard;
using O2DESNet.Tcmf;

var sink = new TcmfJsonLinesSink("terminal.jsonl");
var reporter = new TcmfReporter(sim, sink, TcmfIdentifier.User("RISE", "o2des_sim"),
    TcmfReporter.AnchorClock(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc)));

crane.ReportTcmf(reporter, TcmfIdentifier.Service("loading", "container", "crane"), TcmfIdentifier.Location("terminal", "SEGOT"));
crane.OnReadyToDepart += truck => reporter.Departed(
    TcmfStandardExtensions.ReferenceObjectOf(truck), TcmfIdentifier.Location("terminal", "SEGOT"),
    TcmfStandardExtensions.GroupingOf(truck));

gateQueue.RqstEnqueue(new TcmfLoad(
    TcmfIdentifier.ReferenceObject("truck", "registration_plate", "ABC123"),
    TcmfIdentifier.Grouping(GroupingType.Truck, "registration_plate", "ABC123")));
```

Each message produced looks like this:

```json
{"messageId":"tcmf:message:3f2b...","version":"0.0.7","reportedAt":"2026-01-01T06:12:30.000Z",
 "reportedBy":"tcmf:user:RISE:o2des_sim",
 "grouping":["tcmf:grouping:truck:registration_plate:ABC123"],
 "payload":{"@type":"LocationState","time":"2026-01-01T06:12:30.000Z","timeType":"actual",
   "timeSequence":"arrived_to","referenceObject":"tcmf:reference_object:truck:registration_plate:ABC123",
   "location":"tcmf:location:terminal:SEGOT"}}
```

## Delivery

The reporter hands every message to an `ITcmfSink`. The core library ships the in-memory, file and delegate
sinks above and has no broker client; `O2DESNet.Tcmf.Kafka` and `O2DESNet.Tcmf.Rest` add the two network sinks.
Both queue the message and return immediately, deliver in publication order, count delivered and failed
messages, raise `OnDeliveryFailed`, and make `Flush()` throw if anything was lost since the previous flush.
`Dispose()` waits for the queue to drain (up to `ShutdownTimeout`) but never throws, so call `Flush()` first
when you want to know that everything arrived.

A plain `Run()` emits a whole day of messages in milliseconds. For live delivery drive the model with
`Run(speed)` in a loop so messages come out paced against wall-clock time.

### Kafka

`TcmfKafkaSink` produces one JSON record per message, keyed by the first grouping identifier so a timeline
(one truck, one ship) stays on one partition, in order. Records carry `tcmf.messageId`, `tcmf.payloadType`
and `tcmf.version` headers. The producer uses `acks=all` and idempotence by default; anything else in
librdkafka can be set through `ProducerConfig`.

```csharp
using O2DESNet.Tcmf.Kafka;

var options = new TcmfKafkaSinkOptions("broker1:9093,broker2:9093", "tcmf")
{
    Authentication = TcmfKafkaAuthentication.SaslScramSha512("sim", Environment.GetEnvironmentVariable("KAFKA_PASSWORD")),
    CaCertificatePath = "/etc/kafka/ca.pem",
};
using var sink = new TcmfKafkaSink(options);
var reporter = new TcmfReporter(sim, sink, TcmfIdentifier.User("RISE", "o2des_sim"));
sim.Run();
sink.Flush();   // throws TcmfKafkaDeliveryException if a record was rejected
```

Authentication against the cluster, all over TLS:

| Mechanism | Factory |
|---|---|
| SASL/PLAIN (Confluent Cloud API keys, Aiven, Redpanda) | `TcmfKafkaAuthentication.SaslPlain(user, password)` |
| SASL/SCRAM-SHA-256 or -512 (Strimzi, MSK) | `SaslScramSha256(...)`, `SaslScramSha512(...)` |
| SASL/OAUTHBEARER, client credentials fetched and renewed by librdkafka | `OAuthBearer(tokenEndpoint, clientId, clientSecret, scope, extensions)` |
| SASL/OAUTHBEARER with your own token source | `OAuthBearer(ct => Task<OAuthBearerToken>)` |
| Mutual TLS | `MutualTls(certPath, keyPath, keyPassword)`, `MutualTlsPem(certPem, keyPem)` |
| None | `TcmfKafkaAuthentication.None`, with `UseTls = true` for an unauthenticated TLS listener |

Set `CaCertificatePath` or `CaCertificatePem` when the brokers' certificates are not signed by a CA in the
system store. The sink can also wrap a producer you already have: `new TcmfKafkaSink(options, producer)`.

### REST

`TcmfRestSink` posts each message as `application/json` to an endpoint, or a JSON array of up to `BatchSize`
messages when batching. Transport errors, timeouts, 408, 429 and 5xx responses are retried with exponential
backoff (`MaxRetries`, `RetryDelay`, `Retry-After` honoured); other 4xx responses fail the message at once.

```csharp
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
sink.Flush();   // throws TcmfRestDeliveryException if a request was given up on
```

The OAuth2 client-credentials provider caches the token until `ExpiryLeeway` before `expires_in`, sends the
client credentials as an HTTP Basic header (or in the body with `ClientAuthentication = RequestBody`, as Auth0
wants), and supports `Audience` and `AdditionalParameters`. When the API answers 401 the sink fetches a fresh
token and retries once. For a fixed token use `StaticAccessTokenProvider`; for any other grant implement
`IAccessTokenProvider` or wrap a callback in `DelegateAccessTokenProvider`. API keys and other fixed headers
go in `Headers`. Pass your own `HttpClient` (e.g. from `IHttpClientFactory`) as the second constructor argument.

## Demo and tests

`src/O2DESNet.Tcmf/Demos/TcmfContainerTerminal.cs` is a complete model (truck generator, gate queue,
loading crane) that reports arrivals, service start/finish and departures.
`tests/O2DESNet.Tcmf.Tests` covers serialization, validation, the reporter and the demo;
`tests/O2DESNet.Tcmf.Kafka.Tests` and `tests/O2DESNet.Tcmf.Rest.Tests` run the sinks against a fake producer
and a fake HTTP handler (no broker or server needed), including the OAuth2 token flow.

```
dotnet test
```

## TCMF references

- Schema: `transport-collaboration-message.json` (TCMF 0.0.7)
- Identifier grammar: `tcmf:<category>:<part>:...`, groupings limited to
  ship, truck, train, wagon, shipment, airplane, trailer, cargo_carrier.

## License

MIT. O2DESNet itself is MIT licensed by the National University of Singapore.
