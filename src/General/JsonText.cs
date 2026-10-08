using System;
using System.Text;

namespace ReplayTimerMod
{
    internal static class JsonText
    {
        public static void AppendQuoted(StringBuilder sb, string? s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            AppendEscaped(sb, s);
            sb.Append('"');
        }

        public static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var sb = new StringBuilder(s!.Length);
            AppendEscaped(sb, s);
            return sb.ToString();
        }

        public static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
                return "";
            i++;

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
                if (c == '\\') break;
                i++;
            }
            if (i >= json.Length)
                return json.Substring(start, i - start);

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
