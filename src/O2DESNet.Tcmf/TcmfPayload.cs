using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// Base of the six TCMF payload types. Serialized with an "@type" discriminator as the first property.
    /// </summary>
    [JsonConverter(typeof(TcmfPayloadConverter))]
    public abstract class TcmfPayload
    {
        /// <summary>The "@type" value, e.g. "LocationState".</summary>
        [JsonIgnore]
        public abstract string TypeName { get; }

        public string ToJson(bool indented = false)
        {
            return JsonSerializer.Serialize<TcmfPayload>(this, TcmfJson.Options(indented));
        }

        /// <summary>Schema violations of this payload; empty when valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            Validate(errors);
            return errors;
        }

        internal abstract void WriteFields(Utf8JsonWriter writer);
        internal abstract void ReadField(string name, JsonElement value);
        internal abstract void Validate(List<string> errors);

        internal static TcmfPayload Create(string typeName)
        {
            switch (typeName)
            {
                case LocationState.Type: return new LocationState();
                case ServiceState.Type: return new ServiceState();
                case AdministrativeState.Type: return new AdministrativeState();
                case CarrierState.Type: return new CarrierState();
                case AttributeState.Type: return new AttributeState();
                case MessageOperation.Type: return new MessageOperation();
                default: throw new JsonException(string.Format("Unknown TCMF payload @type '{0}'", typeName));
            }
        }
    }

    /// <summary>Movement of a reference object at a location: arrived_to, departed_from or passed_by.</summary>
    public sealed class LocationState : TcmfPayload
    {
        public const string Type = "LocationState";
        public override string TypeName { get { return Type; } }

        public DateTime Time { get; set; }
        public TimeType TimeType { get; set; }
        public LocationSequence TimeSequence { get; set; }
        public string ReferenceObject { get; set; }
        public string Location { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteTime(w, "time", Time);
            w.WriteString("timeType", TcmfEnum.ToSnakeCase(TimeType));
            w.WriteString("timeSequence", TcmfEnum.ToSnakeCase(TimeSequence));
            TcmfJson.WriteOptional(w, "referenceObject", ReferenceObject);
            TcmfJson.WriteOptional(w, "location", Location);
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "time": Time = TcmfJson.ReadTime(v); break;
                case "timeType": TimeType = TcmfEnum.Parse<TimeType>(v.GetString()); break;
                case "timeSequence": TimeSequence = TcmfEnum.Parse<LocationSequence>(v.GetString()); break;
                case "referenceObject": ReferenceObject = v.GetString(); break;
                case "location": Location = v.GetString(); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.referenceObject", ReferenceObject, TcmfPatterns.ReferenceObject);
            TcmfPatterns.Require(errors, "payload.location", Location, TcmfPatterns.Location);
        }
    }

    /// <summary>A service commenced or completed, optionally for a reference object at a location.</summary>
    public sealed class ServiceState : TcmfPayload
    {
        public const string Type = "ServiceState";
        public override string TypeName { get { return Type; } }

        public DateTime Time { get; set; }
        public TimeType TimeType { get; set; }
        public ServiceSequence TimeSequence { get; set; }
        public string Service { get; set; }
        public string ReferenceObject { get; set; }
        public string Location { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteTime(w, "time", Time);
            w.WriteString("timeType", TcmfEnum.ToSnakeCase(TimeType));
            w.WriteString("timeSequence", TcmfEnum.ToSnakeCase(TimeSequence));
            TcmfJson.WriteOptional(w, "service", Service);
            TcmfJson.WriteOptional(w, "referenceObject", ReferenceObject);
            TcmfJson.WriteOptional(w, "location", Location);
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "time": Time = TcmfJson.ReadTime(v); break;
                case "timeType": TimeType = TcmfEnum.Parse<TimeType>(v.GetString()); break;
                case "timeSequence": TimeSequence = TcmfEnum.Parse<ServiceSequence>(v.GetString()); break;
                case "service": Service = v.GetString(); break;
                case "referenceObject": ReferenceObject = v.GetString(); break;
                case "location": Location = v.GetString(); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.service", Service, TcmfPatterns.Service);
            TcmfPatterns.Optional(errors, "payload.referenceObject", ReferenceObject, TcmfPatterns.ReferenceObject);
            TcmfPatterns.Optional(errors, "payload.location", Location, TcmfPatterns.Location);
        }
    }

    /// <summary>An administrative step for a service (requested, confirmed, cancelled, ...). Has no time type.</summary>
    public sealed class AdministrativeState : TcmfPayload
    {
        public const string Type = "AdministrativeState";
        public override string TypeName { get { return Type; } }

        public DateTime Time { get; set; }
        public AdministrativeSequence TimeSequence { get; set; }
        public string Service { get; set; }
        public string ReferenceObject { get; set; }
        public string Location { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteTime(w, "time", Time);
            w.WriteString("timeSequence", TcmfEnum.ToSnakeCase(TimeSequence));
            TcmfJson.WriteOptional(w, "service", Service);
            TcmfJson.WriteOptional(w, "referenceObject", ReferenceObject);
            TcmfJson.WriteOptional(w, "location", Location);
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "time": Time = TcmfJson.ReadTime(v); break;
                case "timeSequence": TimeSequence = TcmfEnum.Parse<AdministrativeSequence>(v.GetString()); break;
                case "service": Service = v.GetString(); break;
                case "referenceObject": ReferenceObject = v.GetString(); break;
                case "location": Location = v.GetString(); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.service", Service, TcmfPatterns.Service);
            TcmfPatterns.Optional(errors, "payload.referenceObject", ReferenceObject, TcmfPatterns.ReferenceObject);
            TcmfPatterns.Optional(errors, "payload.location", Location, TcmfPatterns.Location);
        }
    }

    /// <summary>A reference object bound to or unbound from a carrier (container on truck, wagon in train, ...).</summary>
    public sealed class CarrierState : TcmfPayload
    {
        public const string Type = "CarrierState";
        public override string TypeName { get { return Type; } }

        public DateTime Time { get; set; }
        public TimeType TimeType { get; set; }
        public CarrierSequence TimeSequence { get; set; }
        public string ReferenceObject { get; set; }
        public string Carrier { get; set; }
        public string Location { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteTime(w, "time", Time);
            w.WriteString("timeType", TcmfEnum.ToSnakeCase(TimeType));
            w.WriteString("timeSequence", TcmfEnum.ToSnakeCase(TimeSequence));
            TcmfJson.WriteOptional(w, "referenceObject", ReferenceObject);
            TcmfJson.WriteOptional(w, "carrier", Carrier);
            TcmfJson.WriteOptional(w, "location", Location);
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "time": Time = TcmfJson.ReadTime(v); break;
                case "timeType": TimeType = TcmfEnum.Parse<TimeType>(v.GetString()); break;
                case "timeSequence": TimeSequence = TcmfEnum.Parse<CarrierSequence>(v.GetString()); break;
                case "referenceObject": ReferenceObject = v.GetString(); break;
                case "carrier": Carrier = v.GetString(); break;
                case "location": Location = v.GetString(); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.referenceObject", ReferenceObject, TcmfPatterns.ReferenceObject);
            TcmfPatterns.Require(errors, "payload.carrier", Carrier, TcmfPatterns.Carrier);
            TcmfPatterns.Optional(errors, "payload.location", Location, TcmfPatterns.Location);
        }
    }

    /// <summary>An attribute set on or unset from a reference object; the value is encoded in the attribute identifier.</summary>
    public sealed class AttributeState : TcmfPayload
    {
        public const string Type = "AttributeState";
        public override string TypeName { get { return Type; } }

        public DateTime Time { get; set; }
        public TimeType TimeType { get; set; }
        public AttributeSequence TimeSequence { get; set; }
        public string ReferenceObject { get; set; }
        public string Attribute { get; set; }
        public string Location { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteTime(w, "time", Time);
            w.WriteString("timeType", TcmfEnum.ToSnakeCase(TimeType));
            w.WriteString("timeSequence", TcmfEnum.ToSnakeCase(TimeSequence));
            TcmfJson.WriteOptional(w, "referenceObject", ReferenceObject);
            TcmfJson.WriteOptional(w, "attribute", Attribute);
            TcmfJson.WriteOptional(w, "location", Location);
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "time": Time = TcmfJson.ReadTime(v); break;
                case "timeType": TimeType = TcmfEnum.Parse<TimeType>(v.GetString()); break;
                case "timeSequence": TimeSequence = TcmfEnum.Parse<AttributeSequence>(v.GetString()); break;
                case "referenceObject": ReferenceObject = v.GetString(); break;
                case "attribute": Attribute = v.GetString(); break;
                case "location": Location = v.GetString(); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.referenceObject", ReferenceObject, TcmfPatterns.ReferenceObject);
            TcmfPatterns.Require(errors, "payload.attribute", Attribute, TcmfPatterns.Attribute);
            TcmfPatterns.Optional(errors, "payload.location", Location, TcmfPatterns.Location);
        }
    }

    /// <summary>Operation on a previous message; in 0.0.7 only "invalidate".</summary>
    public sealed class MessageOperation : TcmfPayload
    {
        public const string Type = "MessageOperation";
        public override string TypeName { get { return Type; } }

        /// <summary>Id of the message operated on.</summary>
        public string MessageId { get; set; }
        public MessageOperationType Operation { get; set; }

        internal override void WriteFields(Utf8JsonWriter w)
        {
            TcmfJson.WriteOptional(w, "messageId", MessageId);
            w.WriteString("operation", TcmfEnum.ToSnakeCase(Operation));
        }
        internal override void ReadField(string name, JsonElement v)
        {
            switch (name)
            {
                case "messageId": MessageId = v.GetString(); break;
                case "operation": Operation = TcmfEnum.Parse<MessageOperationType>(v.GetString()); break;
            }
        }
        internal override void Validate(List<string> errors)
        {
            TcmfPatterns.Require(errors, "payload.messageId", MessageId, TcmfPatterns.MessageId);
        }
    }

    /// <summary>
    /// Converter factory so that every payload type, whether declared as <see cref="TcmfPayload"/> or as a
    /// concrete subclass, is written with "@type" first and read back into the right subclass.
    /// </summary>
    public sealed class TcmfPayloadConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(TcmfPayload).IsAssignableFrom(typeToConvert);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return (JsonConverter)Activator.CreateInstance(typeof(Inner<>).MakeGenericType(typeToConvert));
        }

        private sealed class Inner<T> : JsonConverter<T> where T : TcmfPayload
        {
            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw new JsonException("TCMF payload must be a JSON object");
                    if (!root.TryGetProperty("@type", out var typeElement)) throw new JsonException("TCMF payload is missing '@type'");
                    var payload = TcmfPayload.Create(typeElement.GetString());
                    if (!(payload is T typed))
                        throw new JsonException(string.Format("Payload @type '{0}' is not a {1}", payload.TypeName, typeof(T).Name));
                    foreach (var property in root.EnumerateObject())
                        if (property.Name != "@type") payload.ReadField(property.Name, property.Value);
                    return typed;
                }
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("@type", value.TypeName);
                value.WriteFields(writer);
                writer.WriteEndObject();
            }
        }
    }

    /// <summary>Regular expressions from the TCMF 0.0.7 JSON schema.</summary>
    public static class TcmfPatterns
    {
        public const string Version = "0.0.7";
        public static readonly Regex MessageId = new Regex(@"^tcmf:message:[0-9a-f]{8}-([0-9a-f]{4}-){3}[0-9a-f]{12}$", RegexOptions.Compiled);
        public static readonly Regex User = new Regex(@"^tcmf:user:(.*)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex Grouping = new Regex(@"^tcmf:grouping:(ship|truck|train|wagon|shipment|airplane|trailer|cargo_carrier)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex ReferenceObject = new Regex(@"^tcmf:reference_object:(.*)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex Service = new Regex(@"^tcmf:service:(.*)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex Location = new Regex(@"^tcmf:location:(.*)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex Carrier = new Regex(@"^tcmf:carrier:(.*)(:.*)*$", RegexOptions.Compiled);
        public static readonly Regex Attribute = new Regex(@"^tcmf:attribute:(.*)(:.*)*$", RegexOptions.Compiled);

        internal static void Require(List<string> errors, string field, string value, Regex pattern)
        {
            if (value == null) errors.Add(field + " is required");
            else if (!pattern.IsMatch(value)) errors.Add(string.Format("{0} '{1}' does not match {2}", field, value, pattern));
        }

        internal static void Optional(List<string> errors, string field, string value, Regex pattern)
        {
            if (value != null && !pattern.IsMatch(value)) errors.Add(string.Format("{0} '{1}' does not match {2}", field, value, pattern));
        }
    }

    /// <summary>JSON helpers shared by the TCMF types.</summary>
    public static class TcmfJson
    {
        /// <summary>ISO 8601 with millisecond precision in UTC, as used in TCMF examples.</summary>
        public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        private static readonly JsonSerializerOptions Compact = Create(false);
        private static readonly JsonSerializerOptions Indented = Create(true);

        public static JsonSerializerOptions Options(bool indented) { return indented ? Indented : Compact; }

        private static JsonSerializerOptions Create(bool indented)
        {
            return new JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
            };
        }

        /// <summary>Formats a time stamp as UTC. Local times are converted; unspecified kinds are taken as UTC.</summary>
        public static string FormatTime(DateTime time)
        {
            if (time.Kind == DateTimeKind.Local) time = time.ToUniversalTime();
            return time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseTime(string text)
        {
            return DateTime.Parse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        internal static void WriteTime(Utf8JsonWriter w, string name, DateTime time) { w.WriteString(name, FormatTime(time)); }
        internal static DateTime ReadTime(JsonElement v) { return ParseTime(v.GetString()); }
        internal static void WriteOptional(Utf8JsonWriter w, string name, string value) { if (value != null) w.WriteString(name, value); }
    }

    /// <summary>Writes <see cref="DateTime"/> properties in TCMF time format.</summary>
    public sealed class TcmfDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TcmfJson.ParseTime(reader.GetString());
        }
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(TcmfJson.FormatTime(value));
        }
    }

    /// <summary>Writes nullable <see cref="DateTime"/> properties in TCMF time format.</summary>
    public sealed class TcmfNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return TcmfJson.ParseTime(reader.GetString());
        }
        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(TcmfJson.FormatTime(value.Value));
        }
    }
}
