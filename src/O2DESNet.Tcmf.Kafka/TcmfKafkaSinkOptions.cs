using System;
using System.Collections.Generic;
using Confluent.Kafka;

namespace O2DESNet.Tcmf.Kafka
{
    /// <summary>Where and how <see cref="TcmfKafkaSink"/> delivers messages.</summary>
    public sealed class TcmfKafkaSinkOptions
    {
        /// <summary>Comma-separated host:port list of brokers.</summary>
        public string BootstrapServers { get; set; }

        /// <summary>Topic every message is produced to.</summary>
        public string Topic { get; set; }

        /// <summary>client.id reported to the brokers.</summary>
        public string ClientId { get; set; } = "o2desnet-tcmf";

        /// <summary>Cluster authentication; <see cref="TcmfKafkaAuthentication.None"/> by default.</summary>
        public TcmfKafkaAuthentication Authentication { get; set; } = TcmfKafkaAuthentication.None;

        /// <summary>
        /// Encrypt the connection. Implied by every authentication mechanism other than None; set it for an
        /// unauthenticated TLS listener.
        /// </summary>
        public bool UseTls { get; set; }

        /// <summary>Path to the CA certificate(s) that signed the brokers' certificates; null uses the system store.</summary>
        public string CaCertificatePath { get; set; }

        /// <summary>CA certificate(s) as PEM text; alternative to <see cref="CaCertificatePath"/>.</summary>
        public string CaCertificatePem { get; set; }

        /// <summary>Verify the broker certificate against the CA. Only turn off for local development.</summary>
        public bool VerifyBrokerCertificate { get; set; } = true;

        /// <summary>
        /// Chooses the record key, which decides the partition and hence ordering. Defaults to the first grouping
        /// identifier so every message of one timeline (truck, ship, ...) lands on the same partition.
        /// </summary>
        public Func<TcmfMessage, string> KeySelector { get; set; } = message =>
            message.Grouping != null && message.Grouping.Count > 0 ? message.Grouping[0] : null;

        /// <summary>Add tcmf.messageId, tcmf.payloadType and tcmf.version record headers. On by default.</summary>
        public bool AddHeaders { get; set; } = true;

        /// <summary>Require acknowledgement from all in-sync replicas (acks=all). On by default.</summary>
        public bool AcknowledgeAllReplicas { get; set; } = true;

        /// <summary>Exactly-once ordering per partition in the face of retries (enable.idempotence). On by default.</summary>
        public bool Idempotent { get; set; } = true;

        /// <summary>Raw librdkafka settings applied last, overriding anything above (e.g. "linger.ms", "compression.type").</summary>
        public Dictionary<string, string> ProducerConfig { get; } = new Dictionary<string, string>();

        /// <summary>How long <see cref="TcmfKafkaSink.Dispose"/> waits for in-flight messages to be acknowledged.</summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Make <see cref="TcmfKafkaSink.Flush"/> throw when deliveries failed since the previous flush. On by default.</summary>
        public bool ThrowOnFlushFailure { get; set; } = true;

        public TcmfKafkaSinkOptions() { }

        public TcmfKafkaSinkOptions(string bootstrapServers, string topic)
        {
            BootstrapServers = bootstrapServers;
            Topic = topic;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BootstrapServers)) throw new ArgumentException("BootstrapServers is required");
            if (string.IsNullOrWhiteSpace(Topic)) throw new ArgumentException("Topic is required");
            if (Authentication == null) throw new ArgumentException("Authentication is required; use TcmfKafkaAuthentication.None for an open cluster");
            if (KeySelector == null) throw new ArgumentException("KeySelector is required");
        }

        /// <summary>The producer configuration these options translate to.</summary>
        public ProducerConfig ToProducerConfig()
        {
            Validate();
            var config = new ProducerConfig
            {
                BootstrapServers = BootstrapServers,
                ClientId = ClientId,
                Acks = AcknowledgeAllReplicas ? Acks.All : Acks.Leader,
                EnableIdempotence = Idempotent,
                EnableDeliveryReports = true,
            };
            Authentication.Apply(config, UseTls || Authentication.RequiresTls);
            if (CaCertificatePath != null) config.SslCaLocation = CaCertificatePath;
            if (CaCertificatePem != null) config.SslCaPem = CaCertificatePem;
            if (!VerifyBrokerCertificate) config.EnableSslCertificateVerification = false;
            foreach (var setting in ProducerConfig) config.Set(setting.Key, setting.Value);
            return config;
        }
    }
}
