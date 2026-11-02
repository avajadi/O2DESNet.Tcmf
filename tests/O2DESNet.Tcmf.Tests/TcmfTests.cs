using NUnit.Framework;
using O2DESNet.Tcmf.Demos;
using O2DESNet.Standard;
using O2DESNet.Tcmf;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace O2DESNet.Tcmf.Tests
{
    public class Tcmf_Tests
    {
        private static readonly string[] TruckGrouping = { "tcmf:grouping:truck:registration_plate:ABC123" };

        [Test]
        public void Urn_Builders_Format_Parts()
        {
            Assert.AreEqual("tcmf:location:port:SEGOT:71:a", TcmfUrn.Location("port", "SEGOT", 71, "a"));
            Assert.AreEqual("tcmf:location:station:Malmö_Freight_Yard", TcmfUrn.Location("station", " Malmö  Freight Yard "));
            Assert.AreEqual("tcmf:attribute:length:21.5:meters", TcmfUrn.Attribute("length", 21.5, "meters"));
            Assert.AreEqual("tcmf:grouping:cargo_carrier:container_number:MSKU123", TcmfUrn.Grouping(GroupingType.CargoCarrier, "container_number", "MSKU123"));
            Assert.AreEqual("tcmf:user:RISE", TcmfUrn.User("RISE"));
            Assert.AreEqual("tcmf:user:RISE:eddie", TcmfUrn.User("RISE", "eddie"));
            Assert.IsTrue(TcmfPatterns.MessageId.IsMatch(TcmfUrn.NewMessageId()));
            Assert.Throws<ArgumentException>(() => TcmfUrn.Location("port", ""));
            Assert.Throws<ArgumentNullException>(() => TcmfUrn.Location("port", null));
        }

        [Test]
        public void Enums_Use_Snake_Case_On_The_Wire()
        {
            Assert.AreEqual("request_received", TcmfEnum.ToSnakeCase(AdministrativeSequence.RequestReceived));
            Assert.AreEqual("cargo_carrier", TcmfEnum.ToSnakeCase(GroupingType.CargoCarrier));
            Assert.AreEqual(LocationSequence.DepartedFrom, TcmfEnum.Parse<LocationSequence>("departed_from"));
            Assert.Throws<ArgumentException>(() => TcmfEnum.Parse<TimeType>("guessed"));
            CollectionAssert.AreEqual(new[] { "planned", "estimated", "actual" }, TcmfEnum.WireNames<TimeType>());
        }

        [Test]
        public void Message_Serializes_Like_The_Specification_Example()
        {
            var message = new TcmfMessage
            {
                MessageId = "tcmf:message:8463495a-027e-49c4-aaa3-4969f168f642",
                ReportedAt = new DateTime(2024, 3, 18, 8, 25, 0, 797, DateTimeKind.Utc),
                ReportedBy = "tcmf:user:RISE:pontus_dev",
                Grouping = { "tcmf:grouping:train:train_id:testtåget" },
                Payload = new LocationState
                {
                    Time = new DateTime(2024, 3, 18, 12, 24, 0, DateTimeKind.Utc),
                    TimeType = TimeType.Planned,
                    TimeSequence = LocationSequence.DepartedFrom,
                    ReferenceObject = "tcmf:reference_object:train:train_id:testtåget",
                    Location = "tcmf:location:station:Mgb:testspåret",
                },
            };
            Assert.IsTrue(message.IsValid, string.Join("; ", message.Validate()));

            var json = message.ToJson();
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                Assert.AreEqual("0.0.7", root.GetProperty("version").GetString());
                Assert.AreEqual("2024-03-18T08:25:00.797Z", root.GetProperty("reportedAt").GetString());
                Assert.IsFalse(root.TryGetProperty("source", out _), "null metadata must be omitted");
                Assert.IsFalse(root.TryGetProperty("comment", out _), "null metadata must be omitted");
                Assert.AreEqual("tcmf:grouping:train:train_id:testtåget", root.GetProperty("grouping")[0].GetString());

                var payload = root.GetProperty("payload");
                Assert.AreEqual("@type", payload.EnumerateObject().First().Name, "@type must come first");
                Assert.AreEqual("LocationState", payload.GetProperty("@type").GetString());
                Assert.AreEqual("planned", payload.GetProperty("timeType").GetString());
                Assert.AreEqual("departed_from", payload.GetProperty("timeSequence").GetString());
                Assert.AreEqual("2024-03-18T12:24:00.000Z", payload.GetProperty("time").GetString());
            }
            StringAssert.Contains("testtåget", json, "non-ASCII must not be escaped");
        }

        [Test]
        public void Every_Payload_Type_Round_Trips_Through_Json()
        {
            var time = new DateTime(2026, 1, 1, 6, 30, 0, DateTimeKind.Utc);
            var payloads = new TcmfPayload[]
            {
                new LocationState { Time = time, TimeType = TimeType.Actual, TimeSequence = LocationSequence.ArrivedTo,
                    ReferenceObject = "tcmf:reference_object:truck:plate:ABC123", Location = "tcmf:location:terminal:SEGOT:gate_1" },
                new ServiceState { Time = time, TimeType = TimeType.Estimated, TimeSequence = ServiceSequence.Completed,
                    Service = "tcmf:service:loading:container" },
                new AdministrativeState { Time = time, TimeSequence = AdministrativeSequence.RequestReceived,
                    Service = "tcmf:service:berth:allocation", Location = "tcmf:location:port:SEGOT:71" },
                new CarrierState { Time = time, TimeType = TimeType.Actual, TimeSequence = CarrierSequence.BoundTo,
                    ReferenceObject = "tcmf:reference_object:cargo_carrier:container_number:MSKU123", Carrier = "tcmf:carrier:truck:plate:ABC123" },
                new AttributeState { Time = time, TimeType = TimeType.Planned, TimeSequence = AttributeSequence.Set,
                    ReferenceObject = "tcmf:reference_object:train:train_id:1234", Attribute = "tcmf:attribute:train_length:100:meters" },
                new MessageOperation { MessageId = TcmfUrn.NewMessageId(), Operation = MessageOperationType.Invalidate },
            };
            foreach (var payload in payloads)
            {
                var message = new TcmfMessage { MessageId = TcmfUrn.NewMessageId(), ReportedAt = time,
                    ReportedBy = "tcmf:user:RISE", Grouping = { TruckGrouping[0] }, Payload = payload };
                Assert.IsTrue(message.IsValid, payload.TypeName + ": " + string.Join("; ", message.Validate()));

                var json = message.ToJson();
                var back = TcmfMessage.FromJson(json);
                Assert.AreEqual(payload.GetType(), back.Payload.GetType());
                Assert.AreEqual(json, back.ToJson(), payload.TypeName + " changed on round trip");

                // concrete declared type still gets the discriminator
                StringAssert.StartsWith("{\"@type\":\"" + payload.TypeName + "\"", payload.ToJson());
            }
        }

        [Test]
        public void Validation_Reports_Schema_Violations()
        {
            var message = new TcmfMessage
            {
                MessageId = "tcmf:message:not-a-uuid",
                Version = "0.0.6",
                ReportedBy = "eddie",
                Grouping = { "tcmf:grouping:bus:line:4" },
                Payload = new LocationState { ReferenceObject = "truck:ABC123" }, // missing location, bad pattern
            };
            var errors = message.Validate();
            Assert.That(errors.Any(e => e.StartsWith("version")));
            Assert.That(errors.Any(e => e.StartsWith("messageId")));
            Assert.That(errors.Any(e => e.StartsWith("reportedBy")));
            Assert.That(errors.Any(e => e.StartsWith("grouping[0]")));
            Assert.That(errors.Any(e => e.StartsWith("payload.referenceObject")));
            Assert.That(errors.Any(e => e == "payload.location is required"));
            Assert.Throws<TcmfValidationException>(() => message.EnsureValid());

            Assert.That(new TcmfMessage().Validate(), Does.Contain("payload is required"));
        }

        [Test]
        public void Reporter_Stamps_Messages_With_The_Simulation_Clock()
        {
            var collector = new TcmfCollector();
            var start = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc);
            using (var server = new Server(new Server.Statics { Capacity = 1, ServiceTime = (rs, l) => TimeSpan.FromHours(1) }, 0))
            {
                var reporter = new TcmfReporter(server, collector, "tcmf:user:RISE:test", TcmfReporter.AnchorClock(start))
                {
                    MessageIdFactory = TcmfReporter.SeededMessageIds(1),
                };
                server.ReportTcmf(reporter, "tcmf:service:loading:container", "tcmf:location:terminal:SEGOT");

                var truck = new TcmfLoad("tcmf:reference_object:truck:registration_plate:ABC123", TruckGrouping);
                server.RqstStart(truck);
                server.Run(TimeSpan.FromHours(2));

                Assert.AreEqual(2, reporter.Count);
                Assert.AreEqual(2, collector.Count);
                var commenced = (ServiceState)collector.Messages[0].Payload;
                var completed = (ServiceState)collector.Messages[1].Payload;
                Assert.AreEqual(ServiceSequence.Commenced, commenced.TimeSequence);
                Assert.AreEqual(ServiceSequence.Completed, completed.TimeSequence);
                Assert.AreEqual(start, commenced.Time);
                Assert.AreEqual(start.AddHours(1), completed.Time);
                Assert.AreEqual(completed.Time, collector.Messages[1].ReportedAt);
                Assert.AreEqual(truck.ReferenceObject, completed.ReferenceObject);
                Assert.AreEqual("tcmf:user:RISE:test", collector.Messages[1].ReportedBy);
                CollectionAssert.AreEqual(TruckGrouping, collector.Messages[1].Grouping);
                Assert.IsTrue(collector.Messages.All(m => m.IsValid));
                Assert.AreNotEqual(collector.Messages[0].MessageId, collector.Messages[1].MessageId);
            }
        }

        [Test]
        public void Reporter_Supports_Future_Times_And_Invalidation()
        {
            var collector = new TcmfCollector();
            using (var server = new Server(new Server.Statics { Capacity = 1 }, 0))
            {
                var reporter = new TcmfReporter(server, collector, "tcmf:user:RISE");
                reporter.DefaultGrouping.Add("tcmf:grouping:ship:imo:9123456");

                var eta = server.ClockTime.AddHours(3);
                var plan = reporter.Arrived("tcmf:reference_object:ship:imo:9123456", "tcmf:location:port:SEGOT:71",
                    new[] { "tcmf:grouping:ship:name:Acropolis" }, TimeType.Estimated, eta, comment: "ETA from pilot");
                Assert.AreEqual(eta, ((LocationState)plan.Payload).Time);
                Assert.AreEqual(server.ClockTime, plan.ReportedAt);
                Assert.AreEqual("ETA from pilot", plan.Comment);
                CollectionAssert.AreEquivalent(new[] { "tcmf:grouping:ship:imo:9123456", "tcmf:grouping:ship:name:Acropolis" }, plan.Grouping);

                var invalidation = reporter.Invalidate(plan, "superseded");
                var op = (MessageOperation)invalidation.Payload;
                Assert.AreEqual(plan.MessageId, op.MessageId);
                Assert.AreEqual(MessageOperationType.Invalidate, op.Operation);
                CollectionAssert.AreEquivalent(plan.Grouping, invalidation.Grouping);
                Assert.AreEqual(2, collector.Count);
            }
        }

        [Test]
        public void Reporter_Strict_Mode_Rejects_Invalid_Messages()
        {
            var collector = new TcmfCollector();
            using (var server = new Server(new Server.Statics { Capacity = 1 }, 0))
            {
                var reporter = new TcmfReporter(server, collector, "tcmf:user:RISE");
                Assert.Throws<TcmfValidationException>(() =>
                    reporter.Arrived("tcmf:reference_object:truck:1", "gate_1", TruckGrouping));
                Assert.AreEqual(0, collector.Count);

                reporter.Strict = false;
                reporter.Arrived("tcmf:reference_object:truck:1", "gate_1", TruckGrouping);
                Assert.AreEqual(1, collector.Count);
            }
        }

        [Test]
        public void Seeded_Message_Ids_Are_Reproducible_And_Well_Formed()
        {
            var a = TcmfReporter.SeededMessageIds(42);
            var b = TcmfReporter.SeededMessageIds(42);
            var first = a();
            Assert.AreEqual(first, b());
            Assert.AreNotEqual(first, a());
            Assert.IsTrue(TcmfPatterns.MessageId.IsMatch(first), first);
            Assert.AreEqual('4', first["tcmf:message:".Length + 14], "UUID version nibble");
        }

        [Test]
        public void JsonLines_Sink_Writes_One_Line_Per_Message()
        {
            var writer = new StringWriter();
            using (var server = new Server(new Server.Statics { Capacity = 1 }, 0))
            using (var sink = new TcmfJsonLinesSink(writer, leaveOpen: true))
            {
                var reporter = new TcmfReporter(server, sink, "tcmf:user:RISE");
                reporter.Arrived("tcmf:reference_object:truck:1", "tcmf:location:terminal:gate_1", TruckGrouping);
                reporter.Departed("tcmf:reference_object:truck:1", "tcmf:location:terminal:gate_1", TruckGrouping);
                Assert.AreEqual(2, sink.Count);
            }
            var lines = writer.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual(LocationSequence.ArrivedTo, ((LocationState)TcmfMessage.FromJson(lines[0]).Payload).TimeSequence);
            Assert.AreEqual(LocationSequence.DepartedFrom, ((LocationState)TcmfMessage.FromJson(lines[1]).Payload).TimeSequence);
        }

        [Test]
        public void Demo_Produces_Valid_Chronological_And_Reproducible_Messages()
        {
            var collector = new TcmfCollector();
            using (var sim = new TcmfContainerTerminal(arrRate: 6, loadRate: 8, craneCapacity: 1, sink: collector, seed: 3))
            {
                sim.Run(TimeSpan.FromHours(8));
                var messages = collector.Messages;
                Assert.Greater(messages.Count, 20);
                Assert.IsTrue(messages.All(m => m.IsValid), string.Join("\n", messages.SelectMany(m => m.Validate())));

                var reportedAt = messages.Select(m => m.ReportedAt.Value).ToList();
                Assert.IsTrue(reportedAt.Zip(reportedAt.Skip(1), (p, n) => n >= p).All(ok => ok), "messages must be chronological");
                Assert.GreaterOrEqual(reportedAt.First(), new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc));

                int arrived = messages.Count(m => m.Payload is LocationState l && l.TimeSequence == LocationSequence.ArrivedTo);
                int commenced = messages.Count(m => m.Payload is ServiceState s && s.TimeSequence == ServiceSequence.Commenced);
                int completed = messages.Count(m => m.Payload is ServiceState s && s.TimeSequence == ServiceSequence.Completed);
                int departed = messages.Count(m => m.Payload is LocationState l && l.TimeSequence == LocationSequence.DepartedFrom);
                Assert.AreEqual(sim.NDeparted, departed);
                Assert.AreEqual(completed, departed);
                Assert.GreaterOrEqual(commenced, completed);
                Assert.GreaterOrEqual(arrived, commenced);
                Assert.IsTrue(messages.All(m => m.Grouping.Single().StartsWith("tcmf:grouping:truck:registration_plate:ABC")));
            }

            var again = new TcmfCollector();
            using (var sim = new TcmfContainerTerminal(arrRate: 6, loadRate: 8, craneCapacity: 1, sink: again, seed: 3))
            {
                sim.Run(TimeSpan.FromHours(8));
            }
            Assert.AreEqual(collector.ToJsonLines(), again.ToJsonLines(), "same seed must give identical output");
        }
    }
}
