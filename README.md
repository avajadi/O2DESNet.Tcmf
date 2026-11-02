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

## What is in it

- `TcmfMessage` and the six payload types (`LocationState`, `ServiceState`, `AdministrativeState`,
  `CarrierState`, `AttributeState`, `MessageOperation`), with `ToJson()`, `FromJson()` and schema `Validate()`.
- `TcmfUrn` builds the `tcmf:<category>:...` identifiers; `GroupingType` restricts groupings to the types
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
var reporter = new TcmfReporter(sim, sink, TcmfUrn.User("RISE", "o2des_sim"),
    TcmfReporter.AnchorClock(new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc)));

crane.ReportTcmf(reporter, TcmfUrn.Service("loading", "container", "crane"), TcmfUrn.Location("terminal", "SEGOT"));
crane.OnReadyToDepart += truck => reporter.Departed(
    TcmfStandardExtensions.ReferenceObjectOf(truck), TcmfUrn.Location("terminal", "SEGOT"),
    TcmfStandardExtensions.GroupingOf(truck));

gateQueue.RqstEnqueue(new TcmfLoad(
    TcmfUrn.ReferenceObject("truck", "registration_plate", "ABC123"),
    TcmfUrn.Grouping(GroupingType.Truck, "registration_plate", "ABC123")));
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

The reporter hands every message to an `ITcmfSink`. The library has no broker client; to publish to
Kafka, MQTT or HTTP, wrap your producer in a `TcmfDelegateSink` or implement `ITcmfSink`:

```csharp
var sink = new TcmfDelegateSink(m =>
    producer.Produce("tcmf", new Message<string, string> { Key = m.Grouping[0], Value = m.ToJson() }));
```

A plain `Run()` emits a whole day of messages in milliseconds. For live delivery drive the model with
`Run(speed)` in a loop so messages come out paced against wall-clock time.

## Demo and tests

`src/O2DESNet.Tcmf/Demos/TcmfContainerTerminal.cs` is a complete model (truck generator, gate queue,
loading crane) that reports arrivals, service start/finish and departures.
`tests/O2DESNet.Tcmf.Tests` covers serialization, validation, the reporter and the demo.

```
dotnet test
```

## TCMF references

- Schema: `transport-collaboration-message.json` (TCMF 0.0.7)
- Identifier grammar: `tcmf:<category>:<part>:...`, groupings limited to
  ship, truck, train, wagon, shipment, airplane, trailer, cargo_carrier.

## License

MIT. O2DESNet itself is MIT licensed by the National University of Singapore.
