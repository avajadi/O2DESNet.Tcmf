using Confluent.Kafka;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Kafka.Tests
{
    public class TcmfKafkaSink_Tests
    {
        private static TcmfMessage Message(int n)
        {
            return new TcmfMessage
            {
                MessageId = "tcmf:message:" + Guid.NewGuid(),
                ReportedAt = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc).AddSeconds(n),
                ReportedBy = "tcmf:user:RISE:test",
                Grouping = { "tcmf:grouping:truck:registration_plate:ABC" + n, "tcmf:grouping:shipment:booking:B1" },
                Payload = new ServiceState
                {
                    Time = new DateTime(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc).AddSeconds(n),
                    TimeType = TimeType.Actual,
                    TimeSequence = ServiceSequence.Commenced,
                    Service = "tcmf:service:loading:container:crane",
                },
            };
        }

        private static string Header(Message<string, string> record, string key)
        {
            return Encoding.UTF8.GetString(record.Headers.GetLastBytes(key));
        }

        [Test]
        public void Produces_Json_Keyed_By_First_Grouping_With_Headers()
        {
            var producer = new FakeProducer();
            var messages = new[] { Message(1), Message(2) };
            using (var sink = new TcmfKafkaSink(new TcmfKafkaSinkOptions("ignored:9092", "tcmf"), producer))
            {
                foreach (var message in messages) sink.Publish(message);
                sink.Flush();
                Assert.AreEqual(2, sink.PublishedCount);
                Assert.AreEqual(2, sink.DeliveredCount);
                Assert.AreEqual(0, sink.FailedCount);
            }

            Assert.AreEqual(2, producer.Records.Count);
            for (int i = 0; i < 2; i++)
            {
                var record = producer.Records[i];
                Assert.AreEqual("tcmf", record.Topic);
                Assert.AreEqual(messages[i].Grouping[0], record.Message.Key);
                Assert.AreEqual(messages[i].ToJson(), record.Message.Value);
                Assert.AreEqual(messages[i].MessageId, Header(record.Message, "tcmf.messageId"));
                Assert.AreEqual("ServiceState", Header(record.Message, "tcmf.payloadType"));
                Assert.AreEqual(TcmfMessage.CurrentVersion, Header(record.Message, "tcmf.version"));
            }
            Assert.IsFalse(producer.Disposed, "a borrowed producer is not disposed");
        }

        [Test]
        public void Key_Selector_And_Headers_Are_Configurable()
        {
            var producer = new FakeProducer();
            var options = new TcmfKafkaSinkOptions("ignored:9092", "tcmf")
            {
                KeySelector = m => m.ReportedBy,
                AddHeaders = false,
            };
            using (var sink = new TcmfKafkaSink(options, producer))
            {
                sink.Publish(Message(1));
                sink.Flush();
            }
            Assert.AreEqual("tcmf:user:RISE:test", producer.Records[0].Message.Key);
            Assert.IsNull(producer.Records[0].Message.Headers);
        }

        [Test]
        public void Delivery_Errors_Are_Counted_Raised_And_Thrown_On_Flush()
        {
            var producer = new FakeProducer
            {
                ErrorFor = p => p.Message.Key.EndsWith("ABC2") ? new Error(ErrorCode.MsgSizeTooLarge, "too big") : null,
            };
            var failures = new List<TcmfKafkaDeliveryFailure>();
            using (var sink = new TcmfKafkaSink(new TcmfKafkaSinkOptions("ignored:9092", "tcmf"), producer))
            {
                sink.OnDeliveryFailed += failures.Add;
                for (int i = 1; i <= 3; i++) sink.Publish(Message(i));
                var ex = Assert.Throws<TcmfKafkaDeliveryException>(() => sink.Flush());
                Assert.AreEqual(1, ex.Failures.Count);
                Assert.AreEqual(ErrorCode.MsgSizeTooLarge, ex.Failures[0].Error.Code);
                Assert.AreEqual(2, sink.DeliveredCount);
                Assert.AreEqual(1, sink.FailedCount);
                Assert.AreEqual(1, failures.Count);
                Assert.AreSame(ex.Failures[0].Message, failures[0].Message);
                Assert.DoesNotThrow(() => sink.Flush(), "failures are reported once");
            }
        }

        [Test]
        public void Queue_Full_Is_Retried_After_Polling()
        {
            var producer = new FakeProducer { QueueFullTimes = 3 };
            using (var sink = new TcmfKafkaSink(new TcmfKafkaSinkOptions("ignored:9092", "tcmf"), producer))
            {
                sink.Publish(Message(1));
                sink.Flush();
                Assert.AreEqual(1, sink.DeliveredCount);
            }
            Assert.AreEqual(3, producer.PollCalls);
            Assert.AreEqual(1, producer.Records.Count);
        }

        [Test]
        public void Dispose_Flushes_The_Producer()
        {
            var producer = new FakeProducer();
            var sink = new TcmfKafkaSink(new TcmfKafkaSinkOptions("ignored:9092", "tcmf"), producer);
            sink.Publish(Message(1));
            sink.Dispose();
            Assert.AreEqual(1, producer.FlushCalls);
            Assert.Throws<ObjectDisposedException>(() => sink.Publish(Message(2)));
        }

        [Test]
        public void Options_Are_Validated()
        {
            Assert.Throws<ArgumentException>(() => new TcmfKafkaSinkOptions("", "tcmf").ToProducerConfig());
            Assert.Throws<ArgumentException>(() => new TcmfKafkaSinkOptions("b:9092", " ").ToProducerConfig());
            Assert.Throws<ArgumentException>(() => new TcmfKafkaSinkOptions("b:9092", "tcmf") { Authentication = null }.ToProducerConfig());
            Assert.Throws<ArgumentException>(() => TcmfKafkaAuthentication.SaslPlain("", "pw"));
            Assert.Throws<ArgumentException>(() => TcmfKafkaAuthentication.MutualTls("", "key.pem"));
        }

        [Test]
        public void Producer_Config_Defaults()
        {
            var config = new TcmfKafkaSinkOptions("b1:9092,b2:9092", "tcmf").ToProducerConfig();
            Assert.AreEqual("b1:9092,b2:9092", config.BootstrapServers);
            Assert.AreEqual("o2desnet-tcmf", config.ClientId);
            Assert.AreEqual(SecurityProtocol.Plaintext, config.SecurityProtocol);
            Assert.AreEqual(Acks.All, config.Acks);
            Assert.IsTrue(config.EnableIdempotence);
            Assert.IsTrue(config.EnableDeliveryReports);
            Assert.IsNull(config.SaslMechanism);
        }

        [Test]
        public void Tls_Without_Authentication()
        {
            var config = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                UseTls = true,
                CaCertificatePath = "/etc/kafka/ca.pem",
                VerifyBrokerCertificate = false,
            }.ToProducerConfig();
            Assert.AreEqual(SecurityProtocol.Ssl, config.SecurityProtocol);
            Assert.AreEqual("/etc/kafka/ca.pem", config.SslCaLocation);
            Assert.IsFalse(config.EnableSslCertificateVerification);
        }

        [Test]
        public void Sasl_Plain_Over_Tls()
        {
            var config = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.SaslPlain("api-key", "api-secret"),
            }.ToProducerConfig();
            Assert.AreEqual(SecurityProtocol.SaslSsl, config.SecurityProtocol);
            Assert.AreEqual(SaslMechanism.Plain, config.SaslMechanism);
            Assert.AreEqual("api-key", config.SaslUsername);
            Assert.AreEqual("api-secret", config.SaslPassword);
        }

        [Test]
        public void Sasl_Scram()
        {
            var sha256 = new TcmfKafkaSinkOptions("b:9093", "tcmf") { Authentication = TcmfKafkaAuthentication.SaslScramSha256("u", "p") }.ToProducerConfig();
            var sha512 = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.SaslScramSha512("u", "p"),
                CaCertificatePem = "-----BEGIN CERTIFICATE-----\nMIIB...\n-----END CERTIFICATE-----",
            }.ToProducerConfig();
            Assert.AreEqual(SaslMechanism.ScramSha256, sha256.SaslMechanism);
            Assert.AreEqual(SaslMechanism.ScramSha512, sha512.SaslMechanism);
            Assert.AreEqual(SecurityProtocol.SaslSsl, sha512.SecurityProtocol);
            StringAssert.StartsWith("-----BEGIN CERTIFICATE-----", sha512.SslCaPem);
        }

        [Test]
        public void Sasl_OAuthBearer_With_Oidc_Client_Credentials()
        {
            var config = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.OAuthBearer(
                    new Uri("https://idp.example.org/realms/tcmf/protocol/openid-connect/token"),
                    "sim-client", "s3cret", scope: "kafka", extensions: "logicalCluster=lkc-1"),
            }.ToProducerConfig();
            Assert.AreEqual(SecurityProtocol.SaslSsl, config.SecurityProtocol);
            Assert.AreEqual(SaslMechanism.OAuthBearer, config.SaslMechanism);
            Assert.AreEqual(SaslOauthbearerMethod.Oidc, config.SaslOauthbearerMethod);
            Assert.AreEqual("https://idp.example.org/realms/tcmf/protocol/openid-connect/token", config.SaslOauthbearerTokenEndpointUrl);
            Assert.AreEqual("sim-client", config.SaslOauthbearerClientId);
            Assert.AreEqual("s3cret", config.SaslOauthbearerClientSecret);
            Assert.AreEqual("kafka", config.SaslOauthbearerScope);
            Assert.AreEqual("logicalCluster=lkc-1", config.SaslOauthbearerExtensions);
        }

        [Test]
        public void Sasl_OAuthBearer_With_Custom_Token_Source()
        {
            var auth = TcmfKafkaAuthentication.OAuthBearer(ct => Task.FromResult(new OAuthBearerToken("jwt", DateTime.UtcNow.AddHours(1), "sim")));
            var config = new TcmfKafkaSinkOptions("b:9093", "tcmf") { Authentication = auth }.ToProducerConfig();
            Assert.AreEqual(SaslMechanism.OAuthBearer, config.SaslMechanism);
            Assert.AreEqual(SaslOauthbearerMethod.Default, config.SaslOauthbearerMethod);
            Assert.IsNull(config.SaslOauthbearerTokenEndpointUrl);
            // The refresh handler is registered on the builder; registering twice would throw, so a fresh builder must accept it once.
            var builder = new ProducerBuilder<string, string>(config);
            Assert.DoesNotThrow(() => auth.Apply(builder));
            Assert.Throws<InvalidOperationException>(() => auth.Apply(builder));
        }

        [Test]
        public void Mutual_Tls()
        {
            var files = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.MutualTls("/certs/client.pem", "/certs/client.key", "pw"),
                CaCertificatePath = "/certs/ca.pem",
            }.ToProducerConfig();
            Assert.AreEqual(SecurityProtocol.Ssl, files.SecurityProtocol);
            Assert.IsNull(files.SaslMechanism);
            Assert.AreEqual("/certs/client.pem", files.SslCertificateLocation);
            Assert.AreEqual("/certs/client.key", files.SslKeyLocation);
            Assert.AreEqual("pw", files.SslKeyPassword);
            Assert.AreEqual("/certs/ca.pem", files.SslCaLocation);

            var pem = new TcmfKafkaSinkOptions("b:9093", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.MutualTlsPem("CERT", "KEY"),
            }.ToProducerConfig();
            Assert.AreEqual("CERT", pem.SslCertificatePem);
            Assert.AreEqual("KEY", pem.SslKeyPem);
            Assert.IsNull(pem.SslKeyPassword);
        }

        [Test]
        public void Raw_Producer_Settings_Override_Everything()
        {
            var options = new TcmfKafkaSinkOptions("b:9092", "tcmf") { Idempotent = true };
            options.ProducerConfig["enable.idempotence"] = "false";
            options.ProducerConfig["compression.type"] = "zstd";
            options.ProducerConfig["linger.ms"] = "50";
            var config = options.ToProducerConfig();
            Assert.IsFalse(config.EnableIdempotence);
            Assert.AreEqual(CompressionType.Zstd, config.CompressionType);
            Assert.AreEqual(50, config.LingerMs);
        }

        [Test]
        public void A_Real_Producer_Can_Be_Built_From_The_Options()
        {
            // Builds the librdkafka client without connecting; proves the generated configuration is accepted.
            var options = new TcmfKafkaSinkOptions("localhost:1", "tcmf")
            {
                Authentication = TcmfKafkaAuthentication.SaslScramSha512("u", "p"),
                ShutdownTimeout = TimeSpan.FromMilliseconds(100),
            };
            using (var sink = new TcmfKafkaSink(options))
            {
                Assert.AreEqual(0, sink.PublishedCount);
            }
        }
    }
}
