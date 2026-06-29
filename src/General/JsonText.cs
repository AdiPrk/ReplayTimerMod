using System.Text;

namespace ReplayTimerMod
{
    /// <summary>
    /// Shared JSON string-escaping primitives. The mod has several small
    /// hand-rolled JSON writers (network DTOs in <see cref="ApiJson"/>, on-disk
    /// data in <see cref="MiniJson"/>, the owners sidecar) that must work on
    /// net35; they all escape strings identically, so the escaping lives here
    /// once instead of being copy-pasted per writer.
    /// </summary>
    internal static class JsonText
    {
        /// <summary>
        /// Appends <paramref name="s"/> as a quoted, escaped JSON string. A null
        /// value is written as the literal <c>null</c> (no quotes).
        /// </summary>
        public static void AppendQuoted(StringBuilder sb, string? s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            AppendEscaped(sb, s);
            sb.Append('"');
        }

        /// <summary>
        /// Escapes <paramref name="s"/> for use inside a JSON string, without the
        /// surrounding quotes. Null/empty yields an empty string.
        /// </summary>
        public static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s!.Length);
            AppendEscaped(sb, s);
            return sb.ToString();
        }

        private static void AppendEscaped(StringBuilder sb, string s)
        {
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
        }
    }
}
