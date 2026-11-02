using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// A Transport Collaboration Messaging Format (TCMF) 0.0.7 message: metadata, grouping and payload.
    /// </summary>
    public sealed class TcmfMessage
    {
        public const string CurrentVersion = TcmfPatterns.Version;

        /// <summary>"tcmf:message:&lt;uuid&gt;"</summary>
        [JsonPropertyName("messageId"), JsonPropertyOrder(0)]
        public string MessageId { get; set; }

        [JsonPropertyName("version"), JsonPropertyOrder(1)]
        public string Version { get; set; } = CurrentVersion;

        /// <summary>When the message was reported; in a simulation normally the clock time at which the event happened.</summary>
        [JsonPropertyName("reportedAt"), JsonPropertyOrder(2), JsonConverter(typeof(TcmfNullableDateTimeConverter))]
        public DateTime? ReportedAt { get; set; }

        /// <summary>"tcmf:user:&lt;organisation&gt;[:&lt;user&gt;]"</summary>
        [JsonPropertyName("reportedBy"), JsonPropertyOrder(3)]
        public string ReportedBy { get; set; }

        /// <summary>Optional id of the original record when the message was mapped from another source.</summary>
        [JsonPropertyName("source"), JsonPropertyOrder(4)]
        public string Source { get; set; }

        [JsonPropertyName("comment"), JsonPropertyOrder(5)]
        public string Comment { get; set; }

        /// <summary>Timelines the message belongs to: "tcmf:grouping:&lt;type&gt;:..."</summary>
        [JsonPropertyName("grouping"), JsonPropertyOrder(6)]
        public List<string> Grouping { get; set; } = new List<string>();

        [JsonPropertyName("payload"), JsonPropertyOrder(7)]
        public TcmfPayload Payload { get; set; }

        public string ToJson(bool indented = false)
        {
            return JsonSerializer.Serialize(this, TcmfJson.Options(indented));
        }

        public static TcmfMessage FromJson(string json)
        {
            return JsonSerializer.Deserialize<TcmfMessage>(json, TcmfJson.Options(false));
        }

        /// <summary>Schema violations; empty when the message is valid TCMF 0.0.7.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (Version != CurrentVersion) errors.Add(string.Format("version must be '{0}'", CurrentVersion));
            TcmfPatterns.Optional(errors, "messageId", MessageId, TcmfPatterns.MessageId);
            TcmfPatterns.Optional(errors, "reportedBy", ReportedBy, TcmfPatterns.User);
            if (Grouping == null) errors.Add("grouping is required");
            else
                for (int i = 0; i < Grouping.Count; i++)
                    TcmfPatterns.Require(errors, "grouping[" + i + "]", Grouping[i], TcmfPatterns.Grouping);
            if (Payload == null) errors.Add("payload is required");
            else Payload.Validate(errors);
            return errors;
        }

        [JsonIgnore]
        public bool IsValid { get { return Validate().Count == 0; } }

        /// <summary>Throws <see cref="TcmfValidationException"/> if the message violates the schema.</summary>
        public TcmfMessage EnsureValid()
        {
            var errors = Validate();
            if (errors.Count > 0) throw new TcmfValidationException(errors);
            return this;
        }

        public override string ToString() { return ToJson(); }
    }

    public class TcmfValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; private set; }
        public TcmfValidationException(IReadOnlyList<string> errors)
            : base("Invalid TCMF message: " + string.Join("; ", errors))
        {
            Errors = errors;
        }
    }
}
