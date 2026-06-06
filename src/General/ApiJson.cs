using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReplayTimerMod
{
    /// <summary>
    /// Lightweight JSON serialization for API payloads.
    /// Uses the same approach as the existing MiniJson (StringBuilder for
    /// writing, hand-rolled recursive descent for reading) but handles
    /// network-specific types instead of DataStore types.
    ///
    /// Does NOT modify or depend on MiniJson. Clean separation.
    /// </summary>
    internal static class ApiJson
    {
        // ── Serialization ──────────────────────────────────────────────────

        public static string SerializeUpload(UploadPayload p)
        {
            var sb = new StringBuilder(p.ReplayData.Length + 512);
            sb.Append('{');
            AppendKV(sb, "game", p.Game, first: true);
            AppendKV(sb, "scene_name", p.SceneName);
            AppendKV(sb, "entry_from", p.EntryFrom);
            AppendKV(sb, "exit_to", p.ExitTo);
            AppendKVFloat(sb, "total_time", p.TotalTime);
            AppendKVInt(sb, "frame_count", p.FrameCount);
            AppendKV(sb, "captured_at",
                new DateTime(p.CapturedAtUtcTicks, DateTimeKind.Utc)
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            AppendKV(sb, "replay_data", p.ReplayData);
            sb.Append('}');
            return sb.ToString();
        }

        // ── Deserialization ────────────────────────────────────────────────

        public static UploadResponse ParseUploadResponse(string json)
        {
            var r = new UploadResponse { Rank = -1, TotalRunners = -1 };
            var fields = ParseFlat(json);

            if (fields.TryGetValue("run_id", out var rid))
                r.RunId = rid;
            if (fields.TryGetValue("rank", out var rank))
                r.Rank = ParseInt(rank, -1);
            if (fields.TryGetValue("total_runners", out var tr))
                r.TotalRunners = ParseInt(tr, -1);
            if (fields.TryGetValue("is_pb", out var pb))
                r.IsPB = pb == "true";
            if (fields.TryGetValue("display_name", out var dn))
                r.DisplayName = dn;

            return r;
        }

        public static ConfigResponse ParseConfigResponse(string json)
        {
            var r = new ConfigResponse();
            var fields = ParseFlat(json);

            if (fields.TryGetValue("min_mod_version", out var mv))
                r.MinModVersion = mv;
            if (fields.TryGetValue("maintenance", out var m))
                r.Maintenance = m == "true";
            if (fields.TryGetValue("announcement", out var a) && a != null)
                r.Announcement = a;

            return r;
        }

        // ── StringBuilder helpers ──────────────────────────────────────────

        private static void AppendKV(StringBuilder sb, string key, string value,
            bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"');
            sb.Append(key);
            sb.Append("\":");
            AppendString(sb, value);
        }

        private static void AppendKVFloat(StringBuilder sb, string key, float value)
        {
            sb.Append(",\"");
            sb.Append(key);
            sb.Append("\":");
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendKVInt(StringBuilder sb, string key, int value)
        {
            sb.Append(",\"");
            sb.Append(key);
            sb.Append("\":");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }

            sb.Append('"');
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
            sb.Append('"');
        }

        // ── Flat JSON parser ───────────────────────────────────────────────
        //
        // Parses a single-level JSON object into a Dictionary<string, string>.
        // String values are unescaped. Numbers and booleans are returned as
        // their literal text. Nested objects/arrays are skipped. Null values
        // map to the string "null".
        //
        // This is intentionally simple — API responses are flat objects.

        private static Dictionary<string, string> ParseFlat(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return result;
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWs(json, ref i);

                if (i >= json.Length) break;

                char c = json[i];
                if (c == '"')
                {
                    result[key] = ReadString(json, ref i);
                }
                else if (c == 'n' && i + 3 < json.Length
                    && json[i + 1] == 'u' && json[i + 2] == 'l' && json[i + 3] == 'l')
                {
                    result[key] = null;
                    i += 4;
                }
                else if (c == 't' && i + 3 < json.Length
                    && json[i + 1] == 'r' && json[i + 2] == 'u' && json[i + 3] == 'e')
                {
                    result[key] = "true";
                    i += 4;
                }
                else if (c == 'f' && i + 4 < json.Length
                    && json[i + 1] == 'a' && json[i + 2] == 'l'
                    && json[i + 3] == 's' && json[i + 4] == 'e')
                {
                    result[key] = "false";
                    i += 5;
                }
                else if (c == '{' || c == '[')
                {
                    SkipNested(json, ref i);
                }
                else
                {
                    // Number or other literal
                    int start = i;
                    while (i < json.Length && json[i] != ',' && json[i] != '}'
                        && json[i] != ' ' && json[i] != '\n' && json[i] != '\r'
                        && json[i] != '\t')
                        i++;
                    result[key] = json.Substring(start, i - start);
                }
            }

            return result;
        }

        private static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
                return "";
            i++; // skip opening quote

            var sb = new StringBuilder();
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

        private static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'
                || json[i] == '\n' || json[i] == '\r'))
                i++;
        }

        private static void SkipNested(string json, ref int i)
        {
            char open = json[i];
            char close = open == '{' ? '}' : ']';
            int depth = 1;
            i++;
            bool inStr = false;
            while (i < json.Length && depth > 0)
            {
                char c = json[i];
                if (inStr)
                {
                    if (c == '\\') { i++; } // skip escaped char
                    else if (c == '"') { inStr = false; }
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == open) depth++;
                    else if (c == close) depth--;
                }
                i++;
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            int result;
            return int.TryParse(s, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out result) ? result : fallback;
        }
    }
}