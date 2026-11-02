using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace O2DESNet.Tcmf.Kafka.Tests
{
    /// <summary>Records produced records and completes their delivery reports from a callback.</summary>
    public sealed class FakeProducer : IProducer<string, string>
    {
        public sealed class Produced
        {
            public string Topic;
            public Message<string, string> Message;
        }

        public List<Produced> Records { get; } = new List<Produced>();
        public int PollCalls { get; private set; }
        public int FlushCalls { get; private set; }
        public bool Disposed { get; private set; }

        /// <summary>Error to report for a record, or null for success.</summary>
        public Func<Produced, Error> ErrorFor { get; set; } = _ => null;

        /// <summary>Throw QueueFull for this many Produce calls before accepting.</summary>
        public int QueueFullTimes { get; set; }

        public void Produce(string topic, Message<string, string> message, Action<DeliveryReport<string, string>> deliveryHandler = null)
        {
            if (QueueFullTimes > 0)
            {
                QueueFullTimes--;
                throw new ProduceException<string, string>(new Error(ErrorCode.Local_QueueFull, "queue full"),
                    new DeliveryResult<string, string> { Message = message, Topic = topic });
            }
            var produced = new Produced { Topic = topic, Message = message };
            Records.Add(produced);
            var error = ErrorFor(produced) ?? new Error(ErrorCode.NoError);
            deliveryHandler?.Invoke(new DeliveryReport<string, string>
            {
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(Records.Count - 1),
                Message = message,
                Error = error,
                Status = error.IsError ? PersistenceStatus.NotPersisted : PersistenceStatus.Persisted,
            });
        }

        public void Produce(TopicPartition topicPartition, Message<string, string> message, Action<DeliveryReport<string, string>> deliveryHandler = null)
        {
            Produce(topicPartition.Topic, message, deliveryHandler);
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(string topic, Message<string, string> message, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(TopicPartition topicPartition, Message<string, string> message, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public int Poll(TimeSpan timeout) { PollCalls++; return 0; }
        public int Flush(TimeSpan timeout) { FlushCalls++; return 0; }
        public void Flush(CancellationToken cancellationToken = default) { FlushCalls++; }
        public void InitTransactions(TimeSpan timeout) { throw new NotSupportedException(); }
        public void BeginTransaction() { throw new NotSupportedException(); }
        public void CommitTransaction(TimeSpan timeout) { throw new NotSupportedException(); }
        public void CommitTransaction() { throw new NotSupportedException(); }
        public void AbortTransaction(TimeSpan timeout) { throw new NotSupportedException(); }
        public void AbortTransaction() { throw new NotSupportedException(); }
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { throw new NotSupportedException(); }
        public Handle Handle { get { throw new NotSupportedException(); } }
        public string Name { get { return "fake"; } }
        public int AddBrokers(string brokers) { return 0; }
        public void SetSaslCredentials(string username, string password) { }
        public void Dispose() { Disposed = true; }
    }
}
