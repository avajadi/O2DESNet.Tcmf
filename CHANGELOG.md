# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the packages use
[Semantic Versioning](https://semver.org/). Each package (`O2DESNet.Tcmf`, `O2DESNet.Tcmf.Kafka`,
`O2DESNet.Tcmf.Rest`) is versioned independently; entries name the package they apply to.

## [Unreleased]

## [0.1.0-preview.1] - 2026-11-02

First preview of the three packages. Nothing was published before this version.

### Added

- **O2DESNet.Tcmf**: `TcmfMessage` and the six TCMF 0.0.7 payload types (`LocationState`, `ServiceState`,
  `AdministrativeState`, `CarrierState`, `AttributeState`, `MessageOperation`) with `ToJson()`, `FromJson()`
  and schema `Validate()`.
- **O2DESNet.Tcmf**: `TcmfIdentifier` builders for `tcmf:<category>:...` identifiers and `GroupingType` for
  the allowed groupings.
- **O2DESNet.Tcmf**: `TcmfReporter`, attached to an O2DESNet sandbox, publishes `Arrived`, `Departed`,
  `ServiceCommenced`, `BoundTo`, `SetAttribute`, `Invalidate` and the other sequences at the current clock
  time; `AnchorClock` maps the simulation clock onto a calendar date and `SeededMessageIds` gives
  reproducible message ids.
- **O2DESNet.Tcmf**: sinks `TcmfCollector` (in memory), `TcmfJsonLinesSink` (one JSON message per line),
  `TcmfDelegateSink` (callback) and `TcmfCompositeSink` (fan out); `ReportTcmf` extension methods for the
  standard `Server` and `Queue`; `TcmfLoad`; a container terminal demo model.
- **O2DESNet.Tcmf.Kafka**: `TcmfKafkaSink` produces one JSON record per message to a Kafka topic, keyed by
  the first grouping identifier, with `tcmf.messageId`, `tcmf.payloadType` and `tcmf.version` headers.
  `acks=all` and idempotence are on by default; `ProducerConfig` passes raw librdkafka settings.
- **O2DESNet.Tcmf.Kafka**: `TcmfKafkaAuthentication` for cluster authentication over TLS: SASL/PLAIN,
  SASL/SCRAM-SHA-256 and -512, SASL/OAUTHBEARER with OIDC client credentials renewed by librdkafka,
  SASL/OAUTHBEARER with a custom token callback, and mutual TLS from PEM files or PEM strings.
  `CaCertificatePath`/`CaCertificatePem` and `UseTls` for the transport.
- **O2DESNet.Tcmf.Rest**: `TcmfRestSink` posts messages as `application/json` from a background worker, in
  order, optionally batched into a JSON array (`BatchSize`), with exponential backoff on transport errors,
  timeouts, 408, 429 and 5xx (`Retry-After` honoured) and immediate failure on other 4xx.
- **O2DESNet.Tcmf.Rest**: OAuth2 client-credentials authentication (`OAuth2ClientCredentialsTokenProvider`)
  with token caching, Basic-header or request-body client authentication, scope, audience and extra
  parameters, and a forced refresh plus one retry on 401. `StaticAccessTokenProvider`,
  `DelegateAccessTokenProvider` and `IAccessTokenProvider` for other token sources; `Headers` for API keys.
- Both network sinks report `PublishedCount`, `DeliveredCount` and `FailedCount`, raise `OnDeliveryFailed`,
  and make `Flush()` throw (`TcmfKafkaDeliveryException` / `TcmfRestDeliveryException`) when deliveries
  failed since the previous flush. `Dispose()` drains the queue up to `ShutdownTimeout` without throwing.
- Test projects for the core and both sinks; the sinks run against a fake producer and a fake HTTP handler.
- Release tooling: shared package metadata in `Directory.Build.props` (license, copyright, Source Link,
  symbol packages, embedded READMEs and a package icon), a CI workflow, a tag-triggered release workflow
  and `RELEASING.md`.

[Unreleased]: https://github.com/avajadi/O2DESNet.Tcmf/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/avajadi/O2DESNet.Tcmf/releases/tag/v0.1.0-preview.1
