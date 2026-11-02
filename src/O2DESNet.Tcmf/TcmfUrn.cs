using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace O2DESNet.Tcmf
{
    /// <summary>
    /// Builders for the colon-separated "tcmf:&lt;category&gt;:&lt;part&gt;:&lt;part&gt;..." identifiers used
    /// throughout the Transport Collaboration Messaging Format, e.g.
    /// <c>TcmfUrn.Location("port", "SEGOT", 71, "a")</c> gives <c>tcmf:location:port:SEGOT:71:a</c>.
    /// Numbers are formatted with the invariant culture and whitespace inside a part becomes '_'.
    /// </summary>
    public static class TcmfUrn
    {
        public const string Prefix = "tcmf";

        public static string Location(params object[] parts) { return Build("location", parts); }
        public static string ReferenceObject(params object[] parts) { return Build("reference_object", parts); }
        public static string Service(params object[] parts) { return Build("service", parts); }
        public static string Carrier(params object[] parts) { return Build("carrier", parts); }
        public static string Attribute(params object[] parts) { return Build("attribute", parts); }

        /// <summary>
        /// A grouping identifier. The schema only allows a fixed set of transport-unit types as the first part.
        /// </summary>
        public static string Grouping(GroupingType type, params object[] parts)
        {
            var all = new object[parts.Length + 1];
            all[0] = TcmfEnum.ToSnakeCase(type);
            Array.Copy(parts, 0, all, 1, parts.Length);
            return Build("grouping", all);
        }

        /// <summary>"tcmf:user:&lt;organisation&gt;[:&lt;userName&gt;]"</summary>
        public static string User(string organisation, string userName = null)
        {
            return userName == null ? Build("user", organisation) : Build("user", organisation, userName);
        }

        /// <summary>"tcmf:message:&lt;uuid&gt;"</summary>
        public static string Message(Guid id)
        {
            return Prefix + ":message:" + id.ToString("D").ToLowerInvariant();
        }

        public static string NewMessageId() { return Message(Guid.NewGuid()); }

        /// <summary>
        /// Joins a category and its parts into a TCMF identifier. Each part may itself contain colons,
        /// which the format permits for nested identifiers.
        /// </summary>
        public static string Build(string category, params object[] parts)
        {
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category is required", nameof(category));
            if (parts == null || parts.Length == 0) throw new ArgumentException("At least one part is required", nameof(parts));
            var sb = new StringBuilder(Prefix).Append(':').Append(Part(category));
            foreach (var part in parts) sb.Append(':').Append(Part(part));
            return sb.ToString();
        }

        /// <summary>
        /// Formats a single identifier part: invariant-culture for numbers, trimmed, inner whitespace replaced by '_'.
        /// </summary>
        public static string Part(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value), "An identifier part cannot be null");
            string text;
            if (value is IFormattable formattable) text = formattable.ToString(null, CultureInfo.InvariantCulture);
            else text = value.ToString();
            text = text.Trim();
            if (text.Length == 0) throw new ArgumentException("An identifier part cannot be empty", nameof(value));
            return Whitespace.Replace(text, "_");
        }

        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
    }
}
