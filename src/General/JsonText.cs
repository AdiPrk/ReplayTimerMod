using System;
using System.Text;

namespace ReplayTimerMod
{
    /// <summary>
    /// Shared JSON string primitives. The mod has several small hand-rolled
    /// JSON writers and readers (network DTOs in <see cref="ApiJson"/>, on-disk
    /// data in <see cref="MiniJson"/>, the owners sidecar) that must work on
    /// net35; string escaping and the ref-int string/whitespace readers live
    /// here once instead of being copy-pasted per file. (MiniJson's stateful
    /// parser is the deliberate exception.)
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

        /// <summary>
        /// Reads a quoted JSON string starting at <paramref name="i"/> and
        /// advances the cursor past the closing quote. Returns "" if the
        /// cursor is not on an opening quote.
        /// </summary>
        public static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
                return "";
            i++;

            // Fast path: the vast majority of fields (scene names, run ids,
            // most display names) contain no escape sequences, so scan to the
            // closing quote and slice once — no StringBuilder, no per-char
            // copy. Only fall back to the escape-aware path when a '\' appears.
            int start = i;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"')
                {
                    string s = json.Substring(start, i - start);
                    i++;
                    return s;
                }
                if (c == '\\') break; // contains an escape — slow path below
                i++;
            }
            if (i >= json.Length)
                return json.Substring(start, i - start); // unterminated

            // Slow path: seed the builder with the prefix already scanned, then
            // decode escapes for the remainder.
            var sb = new StringBuilder(json, start, i - start, (i - start) + 16);
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"') { i++; break; }
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                string hex = json.Substring(i + 1, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                i += 4;
                            }
                            break;
                        default: sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                i++;
            }
            return sb.ToString();
        }

        /// <summary>Advances the cursor past any JSON whitespace.</summary>
        public static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'
                || json[i] == '\n' || json[i] == '\r'))
                i++;
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
