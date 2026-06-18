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

        // ── Deserialization — upload / config ──────────────────────────────

        public static UploadResponse ParseUploadResponse(string json)
        {
            var r = new UploadResponse { Rank = -1, TotalRunners = -1 };
            var fields = ParseFlat(json);

            if (fields.TryGetValue("run_id", out var rid) && rid != null)
                r.RunId = rid;
            if (fields.TryGetValue("rank", out var rank))
                r.Rank = ParseInt(rank, -1);
            if (fields.TryGetValue("total_runners", out var tr))
                r.TotalRunners = ParseInt(tr, -1);
            if (fields.TryGetValue("is_pb", out var pb))
                r.IsPB = pb == "true";
            if (fields.TryGetValue("display_name", out var dn) && dn != null)
                r.DisplayName = dn;

            return r;
        }

        public static ConfigResponse ParseConfigResponse(string json)
        {
            var r = new ConfigResponse();
            var fields = ParseFlat(json);

            if (fields.TryGetValue("min_mod_version", out var mv) && mv != null)
                r.MinModVersion = mv;
            if (fields.TryGetValue("maintenance", out var m))
                r.Maintenance = m == "true";
            if (fields.TryGetValue("announcement", out var a) && a != null)
                r.Announcement = a;

            return r;
        }

        // ── Deserialization — leaderboard ──────────────────────────────────

        public static LeaderboardData ParseLeaderboardResponse(string json)
        {
            var result = new LeaderboardData();
            if (string.IsNullOrEmpty(json)) return result;

            // Response: { "routes": [ {route}, {route}, ... ] }
            int routesIdx = json.IndexOf("\"routes\"");
            if (routesIdx < 0) return result;

            int arrStart = json.IndexOf('[', routesIdx);
            if (arrStart < 0) return result;

            int i = arrStart + 1;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == ']') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '{') break;

                var route = ParseRoute(json, ref i);
                if (route != null)
                    result.Routes.Add(route);
            }

            return result;
        }

        // ── Deserialization — manifest ────────────────────────────────────
        //
        // Response: { "rooms": [ { "scene": "X", "routes": [...] }, ... ] }
        //
        // Each room's "routes" array has the exact same structure as the
        // per-room /leaderboard response, so we reuse ParseRoute.

        /// <summary>
        /// Parses the manifest response into a dictionary of scene → LeaderboardData.
        /// Returns an empty dictionary on any parse failure.
        /// </summary>
        public static Dictionary<string, LeaderboardData> ParseManifestResponse(
            string json)
        {
            var result = new Dictionary<string, LeaderboardData>();
            if (string.IsNullOrEmpty(json)) return result;

            // Find "rooms" array
            int roomsIdx = json.IndexOf("\"rooms\"");
            if (roomsIdx < 0) return result;

            int arrStart = json.IndexOf('[', roomsIdx);
            if (arrStart < 0) return result;

            int i = arrStart + 1;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == ']') break;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '{') break;

                // Parse one room object: { "scene": "X", "routes": [...] }
                string? sceneName = null;
                var data = new LeaderboardData();
                ParseManifestRoom(json, ref i, ref sceneName, data);

                if (!string.IsNullOrEmpty(sceneName))
                    result[sceneName!] = data;
            }

            return result;
        }

        private static void ParseManifestRoom(string json, ref int i,
            ref string? sceneName, LeaderboardData data)
        {
            if (i >= json.Length || json[i] != '{') return;
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWs(json, ref i);

                switch (key)
                {
                    case "scene":
                        sceneName = ReadString(json, ref i);
                        break;

                    case "routes":
                        // Reuse the existing route array parser
                        if (i < json.Length && json[i] == '[')
                        {
                            i++; // skip '['
                            while (i < json.Length)
                            {
                                SkipWs(json, ref i);
                                if (i >= json.Length || json[i] == ']')
                                {
                                    i++; break;
                                }
                                if (json[i] == ',') { i++; continue; }
                                if (json[i] != '{') break;

                                var route = ParseRoute(json, ref i);
                                if (route != null)
                                    data.Routes.Add(route);
                            }
                        }
                        else
                        {
                            SkipValue(json, ref i);
                        }
                        break;

                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }
        }

        // ── Shared route / entry parsers ──────────────────────────────────

        private static RouteLeaderboard ParseRoute(string json, ref int i)
        {
            var route = new RouteLeaderboard();
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWs(json, ref i);

                switch (key)
                {
                    case "entry_from":
                        route.EntryFrom = ReadString(json, ref i);
                        break;
                    case "exit_to":
                        route.ExitTo = ReadString(json, ref i);
                        break;
                    case "total_runners":
                        route.TotalRunners = ReadJsonInt(json, ref i);
                        break;
                    case "your_rank":
                        route.YourRank = ReadJsonIntOrNull(json, ref i);
                        break;
                    case "entries":
                        route.Entries = ParseEntries(json, ref i);
                        break;
                    case "your_entry":
                        if (i < json.Length && json[i] == 'n')
                        {
                            i += 4; // skip "null"
                            route.YourEntry = null;
                        }
                        else
                        {
                            route.YourEntry = ParseEntry(json, ref i);
                        }
                        break;
                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return route;
        }

        private static List<LeaderboardEntry> ParseEntries(string json, ref int i)
        {
            var entries = new List<LeaderboardEntry>();
            if (i >= json.Length || json[i] != '[') return entries;
            i++; // skip '['

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == ']') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                var entry = ParseEntry(json, ref i);
                if (entry != null) entries.Add(entry);
            }

            return entries;
        }

        private static LeaderboardEntry? ParseEntry(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '{') return null;
            var entry = new LeaderboardEntry();
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++; // skip ':'
                SkipWs(json, ref i);

                switch (key)
                {
                    case "rank":
                        entry.Rank = ReadJsonInt(json, ref i);
                        break;
                    case "runner_name":
                        entry.RunnerName = ReadString(json, ref i);
                        break;
                    case "total_time":
                        entry.TotalTime = ReadJsonFloat(json, ref i);
                        break;
                    case "run_id":
                        entry.RunId = ReadString(json, ref i);
                        break;
                    case "is_you":
                        entry.IsYou = ReadJsonBool(json, ref i);
                        break;
                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return entry;
        }

        // ── Deserialization — replay download ─────────────────────────────

        public static string? ParseReplayData(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var fields = ParseFlat(json);
            return fields.TryGetValue("replay_data", out var data)
                ? NormalizeReplayBase64(data)
                : null;
        }

        /// <summary>
        /// Defensive normalization for the replay payload. If the server ever
        /// returns a bytea column serialized by PostgREST, the value arrives
        /// as Postgres hex ("\x654a7a..." — the backslash may already be
        /// consumed by JSON unescaping, leaving "x654a7a..."). Hex digits are
        /// all valid base64 characters, so without this check the string
        /// base64-decodes into garbage and the inflater throws
        /// "Corrupted data ReadInternal".
        ///
        /// Detection is unambiguous: a real base64 RTM3 payload always
        /// contains characters outside [0-9a-fA-F] (g-z, +, /), so a long
        /// hex-prefixed all-hex string can only be PostgREST bytea output.
        /// </summary>
        private static string? NormalizeReplayBase64(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;

            string s = raw!.Trim();

            // Identify a hex prefix: "\x...", "x...", or "X..."
            string hexBody;
            if (s.StartsWith("\\x") || s.StartsWith("\\X"))
                hexBody = s.Substring(2);
            else if (s.StartsWith("x") || s.StartsWith("X"))
                hexBody = s.Substring(1);
            else
                return s; // no prefix — pass through untouched

            // Only treat as bytea hex when the ENTIRE body is hex digits.
            // (A legit base64 string starting with 'x' fails this check
            // almost immediately and passes through unchanged.)
            if (hexBody.Length < 16 || hexBody.Length % 2 != 0
                || !IsAllHex(hexBody))
                return s;

            byte[] bytes = HexToBytes(hexBody);

            // The blob bytes are either:
            //  (a) the UTF-8 of the original base64 string -> return that text
            //  (b) the raw compressed binary -> re-encode to base64
            string asText = Encoding.UTF8.GetString(bytes);
            if (LooksLikeBase64(asText))
                return asText;

            return Convert.ToBase64String(bytes);
        }

        private static bool IsAllHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((HexNibble(hex[i * 2]) << 4)
                    | HexNibble(hex[i * 2 + 1]));
            }
            return bytes;
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return c - 'A' + 10;
        }

        private static bool LooksLikeBase64(string s)
        {
            if (s.Length < 16) return false;
            int pad = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '=')
                {
                    pad++;
                    if (pad > 2) return false;
                    continue;
                }
                if (pad > 0) return false; // '=' only allowed at the end
                bool ok = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '+' || c == '/';
                if (!ok) return false;
            }
            return true;
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

        private static Dictionary<string, string?> ParseFlat(string json)
        {
            var result = new Dictionary<string, string?>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return result;
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
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

        // ── Low-level parsing helpers ──────────────────────────────────────

        private static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
                return "";
            i++;

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

        private static int ReadJsonInt(string json, ref int i)
        {
            int start = i;
            if (i < json.Length && json[i] == '-') i++;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9') i++;
            string s = json.Substring(start, i - start);
            return ParseInt(s, 0);
        }

        private static int ReadJsonIntOrNull(string json, ref int i)
        {
            if (i < json.Length && json[i] == 'n')
            {
                i += 4; // skip "null"
                return -1;
            }
            return ReadJsonInt(json, ref i);
        }

        private static float ReadJsonFloat(string json, ref int i)
        {
            int start = i;
            while (i < json.Length && (json[i] == '-' || json[i] == '.'
                || json[i] == 'e' || json[i] == 'E' || json[i] == '+'
                || (json[i] >= '0' && json[i] <= '9')))
                i++;
            string s = json.Substring(start, i - start);
            float.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float result);
            return result;
        }

        private static bool ReadJsonBool(string json, ref int i)
        {
            if (i + 3 < json.Length && json[i] == 't')
            {
                i += 4; return true;
            }
            if (i + 4 < json.Length && json[i] == 'f')
            {
                i += 5; return false;
            }
            return false;
        }

        private static void SkipValue(string json, ref int i)
        {
            if (i >= json.Length) return;
            char c = json[i];
            if (c == '"') { ReadString(json, ref i); }
            else if (c == '{' || c == '[') { SkipNested(json, ref i); }
            else if (c == 't') { i += 4; }
            else if (c == 'f') { i += 5; }
            else if (c == 'n') { i += 4; }
            else
            {
                while (i < json.Length && json[i] != ',' && json[i] != '}'
                    && json[i] != ']' && json[i] != ' ' && json[i] != '\n')
                    i++;
            }
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
                    if (c == '\\') { i++; }
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

        private static int ParseInt(string? s, int fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return int.TryParse(s, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int result) ? result : fallback;
        }
    }
}