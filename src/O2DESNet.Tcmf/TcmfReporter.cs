using System;
using System.Collections.Generic;
using System.Linq;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// Emits TCMF messages stamped with the simulation clock of a sandbox. Attach it to any
    /// <see cref="ISandbox"/> (the clock is shared through the parent chain) and call the
    /// convenience methods from event handlers, e.g. <c>server.OnStarted += l => reporter.ServiceCommenced(...)</c>.
    /// </summary>
    public class TcmfReporter
    {
        public ISandbox Sandbox { get; private set; }
        public ITcmfSink Sink { get; set; }
        /// <summary>"tcmf:user:&lt;organisation&gt;[:&lt;user&gt;]" put in every message.</summary>
        public string ReportedBy { get; set; }
        /// <summary>
        /// Maps simulation clock time to the time written in messages. Identity by default; see
        /// <see cref="AnchorClock"/> to map a simulation that starts at <see cref="DateTime.MinValue"/> onto a calendar.
        /// </summary>
        public Func<DateTime, DateTime> TimeMap { get; set; }
        /// <summary>Produces message ids; see <see cref="SeededMessageIds"/> for reproducible runs.</summary>
        public Func<string> MessageIdFactory { get; set; }
        /// <summary>Validate every message before publishing and throw <see cref="TcmfValidationException"/> on violations. Default true.</summary>
        public bool Strict { get; set; } = true;
        /// <summary>Groupings added to every message, in addition to those given per call.</summary>
        public List<string> DefaultGrouping { get; private set; } = new List<string>();

        public int Count { get; private set; }
        public TcmfMessage LastMessage { get; private set; }
        public event Action<TcmfMessage> OnPublished = message => { };

        public TcmfReporter(ISandbox sandbox, ITcmfSink sink, string reportedBy, Func<DateTime, DateTime> timeMap = null)
        {
            Sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
            Sink = sink ?? throw new ArgumentNullException(nameof(sink));
            ReportedBy = reportedBy;
            TimeMap = timeMap ?? (t => t);
            MessageIdFactory = TcmfUrn.NewMessageId;
        }

        public TcmfReporter(ISandbox sandbox, Action<TcmfMessage> sink, string reportedBy, Func<DateTime, DateTime> timeMap = null)
            : this(sandbox, new TcmfDelegateSink(sink), reportedBy, timeMap) { }

        /// <summary>Current simulation clock, mapped through <see cref="TimeMap"/>.</summary>
        public DateTime Now { get { return TimeMap(Sandbox.ClockTime); } }

        /// <summary>
        /// A <see cref="TimeMap"/> that places <paramref name="simulationOrigin"/> (default <see cref="DateTime.MinValue"/>,
        /// where a fresh sandbox clock starts) at <paramref name="calendarOrigin"/>.
        /// </summary>
        public static Func<DateTime, DateTime> AnchorClock(DateTime calendarOrigin, DateTime? simulationOrigin = null)
        {
            var origin = simulationOrigin ?? DateTime.MinValue;
            return t => calendarOrigin + (t - origin);
        }

        /// <summary>A <see cref="MessageIdFactory"/> whose UUIDs are drawn from a seeded random stream.</summary>
        public static Func<string> SeededMessageIds(int seed)
        {
            var rs = new Random(seed);
            return () =>
            {
                var bytes = new byte[16];
                rs.NextBytes(bytes);
                bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40); // version 4
                bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
                return TcmfUrn.Message(new Guid(bytes));
            };
        }

        #region Publishing
        /// <summary>Wraps a payload in a message, stamps it with the clock, validates and publishes it.</summary>
        public TcmfMessage Publish(TcmfPayload payload, IEnumerable<string> grouping, string comment = null, string source = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var message = new TcmfMessage
            {
                MessageId = MessageIdFactory(),
                ReportedAt = Now,
                ReportedBy = ReportedBy,
                Comment = comment,
                Source = source,
                Grouping = DefaultGrouping.Concat(grouping ?? Enumerable.Empty<string>()).Distinct().ToList(),
                Payload = payload,
            };
            return Publish(message);
        }

        /// <summary>Publishes a prepared message, filling in id, reportedAt and reportedBy when they are missing.</summary>
        public TcmfMessage Publish(TcmfMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.MessageId == null) message.MessageId = MessageIdFactory();
            if (message.ReportedAt == null) message.ReportedAt = Now;
            if (message.ReportedBy == null) message.ReportedBy = ReportedBy;
            if (Strict) message.EnsureValid();
            Sink.Publish(message);
            Count++;
            LastMessage = message;
            OnPublished.Invoke(message);
            return message;
        }

        private DateTime At(DateTime? time) { return time == null ? Now : TimeMap(time.Value); }
        #endregion

        #region LocationState
        public TcmfMessage Arrived(string referenceObject, string location, IEnumerable<string> grouping,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Location(LocationSequence.ArrivedTo, referenceObject, location, grouping, timeType, time, comment);
        }

        public TcmfMessage Departed(string referenceObject, string location, IEnumerable<string> grouping,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Location(LocationSequence.DepartedFrom, referenceObject, location, grouping, timeType, time, comment);
        }

        public TcmfMessage PassedBy(string referenceObject, string location, IEnumerable<string> grouping,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Location(LocationSequence.PassedBy, referenceObject, location, grouping, timeType, time, comment);
        }

        public TcmfMessage Location(LocationSequence sequence, string referenceObject, string location, IEnumerable<string> grouping,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Publish(new LocationState
            {
                Time = At(time), TimeType = timeType, TimeSequence = sequence,
                ReferenceObject = referenceObject, Location = location,
            }, grouping, comment);
        }
        #endregion

        #region ServiceState
        public TcmfMessage ServiceCommenced(string service, IEnumerable<string> grouping, string referenceObject = null,
            string location = null, TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Service(ServiceSequence.Commenced, service, grouping, referenceObject, location, timeType, time, comment);
        }

        public TcmfMessage ServiceCompleted(string service, IEnumerable<string> grouping, string referenceObject = null,
            string location = null, TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Service(ServiceSequence.Completed, service, grouping, referenceObject, location, timeType, time, comment);
        }

        public TcmfMessage Service(ServiceSequence sequence, string service, IEnumerable<string> grouping, string referenceObject = null,
            string location = null, TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Publish(new ServiceState
            {
                Time = At(time), TimeType = timeType, TimeSequence = sequence,
                Service = service, ReferenceObject = referenceObject, Location = location,
            }, grouping, comment);
        }
        #endregion

        #region AdministrativeState
        public TcmfMessage Administrative(AdministrativeSequence sequence, string service, IEnumerable<string> grouping,
            string referenceObject = null, string location = null, DateTime? time = null, string comment = null)
        {
            return Publish(new AdministrativeState
            {
                Time = At(time), TimeSequence = sequence,
                Service = service, ReferenceObject = referenceObject, Location = location,
            }, grouping, comment);
        }
        #endregion

        #region CarrierState
        public TcmfMessage BoundTo(string referenceObject, string carrier, IEnumerable<string> grouping, string location = null,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Carrier(CarrierSequence.BoundTo, referenceObject, carrier, grouping, location, timeType, time, comment);
        }

        public TcmfMessage UnboundFrom(string referenceObject, string carrier, IEnumerable<string> grouping, string location = null,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Carrier(CarrierSequence.UnboundFrom, referenceObject, carrier, grouping, location, timeType, time, comment);
        }

        public TcmfMessage Carrier(CarrierSequence sequence, string referenceObject, string carrier, IEnumerable<string> grouping,
            string location = null, TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Publish(new CarrierState
            {
                Time = At(time), TimeType = timeType, TimeSequence = sequence,
                ReferenceObject = referenceObject, Carrier = carrier, Location = location,
            }, grouping, comment);
        }
        #endregion

        #region AttributeState
        public TcmfMessage SetAttribute(string referenceObject, string attribute, IEnumerable<string> grouping, string location = null,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Attribute(AttributeSequence.Set, referenceObject, attribute, grouping, location, timeType, time, comment);
        }

        public TcmfMessage UnsetAttribute(string referenceObject, string attribute, IEnumerable<string> grouping, string location = null,
            TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Attribute(AttributeSequence.Unset, referenceObject, attribute, grouping, location, timeType, time, comment);
        }

        public TcmfMessage Attribute(AttributeSequence sequence, string referenceObject, string attribute, IEnumerable<string> grouping,
            string location = null, TimeType timeType = TimeType.Actual, DateTime? time = null, string comment = null)
        {
            return Publish(new AttributeState
            {
                Time = At(time), TimeType = timeType, TimeSequence = sequence,
                ReferenceObject = referenceObject, Attribute = attribute, Location = location,
            }, grouping, comment);
        }
        #endregion

        #region MessageOperation
        /// <summary>Invalidates a previously published message, e.g. a plan that was superseded.</summary>
        public TcmfMessage Invalidate(string messageId, IEnumerable<string> grouping, string comment = null)
        {
            return Publish(new MessageOperation { MessageId = messageId, Operation = MessageOperationType.Invalidate }, grouping, comment);
        }

        public TcmfMessage Invalidate(TcmfMessage message, string comment = null)
        {
            return Invalidate(message.MessageId, message.Grouping, comment);
        }
        #endregion
    }
}
