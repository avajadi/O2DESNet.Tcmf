using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// Destination for generated TCMF messages: a list, a JSON-lines file, a message broker, ...
    /// </summary>
    public interface ITcmfSink
    {
        void Publish(TcmfMessage message);
    }

    /// <summary>Keeps every published message in memory, in publication order.</summary>
    public sealed class TcmfCollector : ITcmfSink
    {
        private readonly List<TcmfMessage> _messages = new List<TcmfMessage>();
        public IReadOnlyList<TcmfMessage> Messages { get { return _messages.AsReadOnly(); } }
        public int Count { get { return _messages.Count; } }
        public void Publish(TcmfMessage message) { _messages.Add(message); }
        public void Clear() { _messages.Clear(); }

        /// <summary>All messages as newline-delimited JSON.</summary>
        public string ToJsonLines()
        {
            var sb = new StringBuilder();
            foreach (var message in _messages) sb.Append(message.ToJson()).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Writes one compact JSON message per line to a <see cref="TextWriter"/> or a file.</summary>
    public sealed class TcmfJsonLinesSink : ITcmfSink, IDisposable
    {
        private readonly TextWriter _writer;
        private readonly bool _leaveOpen;

        /// <summary>Flush after every message (default), so a crash or a forgotten Dispose loses nothing.</summary>
        public bool AutoFlush { get; set; } = true;
        public int Count { get; private set; }

        public TcmfJsonLinesSink(TextWriter writer, bool leaveOpen = false)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _leaveOpen = leaveOpen;
        }

        public TcmfJsonLinesSink(string path, bool append = false)
            : this(new StreamWriter(path, append, new UTF8Encoding(false))) { }

        public void Publish(TcmfMessage message)
        {
            _writer.Write(message.ToJson());
            _writer.Write('\n');
            Count++;
            if (AutoFlush) _writer.Flush();
        }

        public void Flush() { _writer.Flush(); }

        public void Dispose()
        {
            _writer.Flush();
            if (!_leaveOpen) _writer.Dispose();
        }
    }

    /// <summary>Forwards each message to a callback, e.g. a Kafka or MQTT producer.</summary>
    public sealed class TcmfDelegateSink : ITcmfSink
    {
        private readonly Action<TcmfMessage> _handler;
        public TcmfDelegateSink(Action<TcmfMessage> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }
        public void Publish(TcmfMessage message) { _handler(message); }
    }

    /// <summary>Publishes to several sinks in order.</summary>
    public sealed class TcmfCompositeSink : ITcmfSink
    {
        private readonly ITcmfSink[] _sinks;
        public TcmfCompositeSink(params ITcmfSink[] sinks) { _sinks = sinks ?? new ITcmfSink[0]; }
        public void Publish(TcmfMessage message) { foreach (var sink in _sinks) sink.Publish(message); }
    }
}
