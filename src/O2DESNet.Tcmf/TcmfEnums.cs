using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// TCMF "timeType": whether a time stamp is a plan, an estimate or an observation.
    /// </summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<TimeType>))]
    public enum TimeType { Planned, Estimated, Actual }

    /// <summary>"timeSequence" values for a LocationState payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<LocationSequence>))]
    public enum LocationSequence { ArrivedTo, DepartedFrom, PassedBy }

    /// <summary>"timeSequence" values for a ServiceState payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<ServiceSequence>))]
    public enum ServiceSequence { Commenced, Completed }

    /// <summary>"timeSequence" values for an AdministrativeState payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<AdministrativeSequence>))]
    public enum AdministrativeSequence { Requested, RequestReceived, Confirmed, Cancelled, Denied, Assigned }

    /// <summary>"timeSequence" values for a CarrierState payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<CarrierSequence>))]
    public enum CarrierSequence { BoundTo, UnboundFrom }

    /// <summary>"timeSequence" values for an AttributeState payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<AttributeSequence>))]
    public enum AttributeSequence { Set, Unset }

    /// <summary>"operation" values for a MessageOperation payload.</summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<MessageOperationType>))]
    public enum MessageOperationType { Invalidate }

    /// <summary>
    /// The transport-unit types allowed in a "grouping" entry by the TCMF 0.0.7 schema.
    /// </summary>
    [JsonConverter(typeof(SnakeCaseEnumConverter<GroupingType>))]
    public enum GroupingType { Ship, Truck, Train, Wagon, Shipment, Airplane, Trailer, CargoCarrier }

    /// <summary>
    /// Maps between PascalCase enum members and the snake_case strings used on the wire,
    /// e.g. <c>RequestReceived</c> &lt;-&gt; <c>request_received</c>.
    /// </summary>
    public static class TcmfEnum
    {
        public static string ToSnakeCase<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            return ToSnakeCase(value.ToString());
        }

        public static string ToSnakeCase(string pascal)
        {
            var sb = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                var c = pascal[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static TEnum Parse<TEnum>(string snake) where TEnum : struct, Enum
        {
            if (TryParse<TEnum>(snake, out var value)) return value;
            throw new ArgumentException(string.Format("'{0}' is not a valid {1}", snake, typeof(TEnum).Name));
        }

        public static bool TryParse<TEnum>(string snake, out TEnum value) where TEnum : struct, Enum
        {
            if (snake != null)
            {
                foreach (TEnum candidate in Enum.GetValues(typeof(TEnum)))
                {
                    if (string.Equals(ToSnakeCase(candidate), snake, StringComparison.Ordinal))
                    {
                        value = candidate;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        /// <summary>All wire names of an enum, in declaration order.</summary>
        public static IEnumerable<string> WireNames<TEnum>() where TEnum : struct, Enum
        {
            foreach (TEnum candidate in Enum.GetValues(typeof(TEnum))) yield return ToSnakeCase(candidate);
        }
    }

    /// <summary>
    /// System.Text.Json converter writing enums as snake_case strings.
    /// </summary>
    public sealed class SnakeCaseEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TcmfEnum.Parse<TEnum>(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(TcmfEnum.ToSnakeCase(value));
        }
    }
}
