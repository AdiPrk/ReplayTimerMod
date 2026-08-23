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
            AppendKVInt(sb, "modifiers", p.ModifierMask);
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

            if (fields.TryGetValue("maintenance", out var m))
                r.Maintenance = m == "true";
            if (fields.TryGetValue("announcement", out var a) && a != null)
                r.Announcement = a;

            return r;
        }

        // ── Deserialization — leaderboard ──────────────────────────────────

        /// <summary>
        /// Parses a "routes" JSON array (<paramref name="i"/> must point at the
        /// opening '[') into <paramref name="result"/>, leaving it just past the
        /// closing ']'. Lets the versioned leaderboard parser consume the array
        /// inline in a single pass instead of re-scanning the whole body.
        /// </summary>
        private static void ParseRouteArray(string json, ref int i,
            LeaderboardData result)
        {
            if (i >= json.Length || json[i] != '[') return;
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == ']') { i++; break; }
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '{') break;

                var route = ParseRoute(json, ref i);
                if (route != null)
                    result.Routes.Add(route);
            }
        }

        // ── Shared route / entry parsers ──────────────────────────────────

        private static RouteLeaderboard ParseRoute(string json, ref int i)
        {
            var route = new RouteLeaderboard();
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
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
            i++;

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
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
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
                    case "modifiers":
                        entry.Modifiers = ReadJsonInt(json, ref i);
                        break;
                    case "rid":
                        entry.Rid = ReadJsonInt(json, ref i);
                        break;
                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return entry;
        }

        // ── Serialization — share ──────────────────────────────────────────

        public static string SerializeShareByRunId(string runId)
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            AppendKV(sb, "run_id", runId, first: true);
            sb.Append('}');
            return sb.ToString();
        }

        public static string SerializeSetName(string displayName)
        {
            var sb = new StringBuilder(64);
            sb.Append('{');
            AppendKV(sb, "display_name", displayName, first: true);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Returns the string value of a top-level key in a JSON object, or
        /// null if absent/not a string. Scans forward to the first '{' so it
        /// also works on error strings that embed a JSON body (see
        /// HttpService.GetErrorString's "err | {json}" format).
        /// </summary>
        public static string? ParseTopLevelString(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return null;

            int i = text.IndexOf('{');
            if (i < 0) return null;
            i++;

            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] == '}') break;
                if (text[i] == ',') { i++; continue; }

                string k = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') break;
                i++;
                SkipWs(text, ref i);

                if (k == key)
                    return i < text.Length && text[i] == '"'
                        ? ReadString(text, ref i)
                        : null;

                SkipValue(text, ref i);
            }

            return null;
        }

        public static string SerializeShareByData(string game, string sceneName,
            string entryFrom, string exitTo, float totalTime, int frameCount,
            string replayData)
        {
            var sb = new StringBuilder(replayData.Length + 256);
            sb.Append('{');
            AppendKV(sb, "game", game, first: true);
            AppendKV(sb, "scene_name", sceneName);
            AppendKV(sb, "entry_from", entryFrom);
            AppendKV(sb, "exit_to", exitTo);
            AppendKVFloat(sb, "total_time", totalTime);
            AppendKVInt(sb, "frame_count", frameCount);
            AppendKV(sb, "replay_data", replayData);
            sb.Append('}');
            return sb.ToString();
        }

        public static ShareResponse ParseShareResponse(string json)
        {
            var r = new ShareResponse();
            if (string.IsNullOrEmpty(json)) return r;
            var fields = ParseFlat(json);
            if (fields.TryGetValue("code", out var c) && c != null) r.Code = c;
            if (fields.TryGetValue("url", out var u) && u != null) r.Url = u;
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

        private static void AppendString(StringBuilder sb, string value) =>
            JsonText.AppendQuoted(sb, value);

        /// <summary>
        /// Escapes a string for use inside a JSON value (without quotes).
        /// </summary>
        public static string EscapeString(string s) => JsonText.Escape(s);

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

        // String reading and whitespace skipping live in JsonText (shared
        // with the other hand-rolled readers).
        private static string ReadString(string json, ref int i)
            => JsonText.ReadString(json, ref i);

        private static void SkipWs(string json, ref int i)
            => JsonText.SkipWs(json, ref i);

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

        // ── Endpoint response parsers ───────────────────────────────────────

        /// <summary>
        /// Parses GET /init response:
        ///   { "config": { ... }, "scenes": { "v": N, "scenes": [...] } }
        ///
        /// Uses recursive-descent (not IndexOf) because the config
        /// announcement field can contain arbitrary text.
        /// </summary>
        public static InitResponse ParseInitResponse(string json)
        {
            var r = new InitResponse();
            if (string.IsNullOrEmpty(json)) return r;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return r;
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

                switch (key)
                {
                    case "config":
                        if (i < json.Length && json[i] == '{')
                        {
                            int start = i;
                            SkipNested(json, ref i);
                            r.Config = ParseConfigResponse(
                                json.Substring(start, i - start));
                        }
                        else SkipValue(json, ref i);
                        break;

                    case "scenes":
                        if (i < json.Length && json[i] == '{')
                        {
                            int start = i;
                            SkipNested(json, ref i);
                            var sceneResp = ParseSceneIndexResponse(
                                json.Substring(start, i - start));
                            r.SceneIndexVersion = sceneResp.Version;
                            r.Scenes = sceneResp.Scenes;
                        }
                        else SkipValue(json, ref i);
                        break;

                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return r;
        }

        /// <summary>
        /// Parses GET /scenes response.
        /// { "v": N }                    → Changed=false (16 bytes, skip).
        /// { "v": N, "scenes": [...] }   → Changed=true  (parse scene array).
        /// </summary>
        public static SceneIndexResponse ParseSceneIndexResponse(string json)
        {
            var r = new SceneIndexResponse();
            if (string.IsNullOrEmpty(json)) return r;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return r;
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

                switch (key)
                {
                    case "v":
                        r.Version = ReadJsonInt(json, ref i);
                        break;

                    case "scenes":
                        if (i < json.Length && json[i] == '[')
                        {
                            r.Changed = true;
                            r.Scenes = ParseSceneArray(json, ref i);
                        }
                        else SkipValue(json, ref i);
                        break;

                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return r;
        }

        // Single pass, ref-int style (same as ParseRouteArray) — the caller's
        // cursor lands just past the closing ']'.
        private static List<SceneInfo> ParseSceneArray(string json, ref int i)
        {
            var result = new List<SceneInfo>();
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == ']') { i++; break; }
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '{') break;

                var info = ParseSceneInfo(json, ref i);
                if (info != null)
                    result.Add(info);
            }

            return result;
        }

        private static SceneInfo? ParseSceneInfo(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '{') return null;
            var info = new SceneInfo();
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWs(json, ref i);

                switch (key)
                {
                    case "s":
                        info.SceneName = ReadString(json, ref i);
                        break;
                    case "r":
                        info.RouteCount = ReadJsonInt(json, ref i);
                        break;
                    case "n":
                        info.RunnerCount = ReadJsonInt(json, ref i);
                        break;
                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return string.IsNullOrEmpty(info.SceneName) ? null : info;
        }

        /// <summary>
        /// Parses GET /leaderboard response with version support, in a single
        /// pass (the routes array is parsed inline when encountered):
        /// { "v": N }                   → Changed=false.
        /// { "v": N, "routes": [...] }  → Changed=true, routes parsed into Data.
        /// </summary>
        public static VersionedLeaderboardResponse ParseVersionedLeaderboardResponse(
            string json)
        {
            var r = new VersionedLeaderboardResponse();
            if (string.IsNullOrEmpty(json)) return r;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return r;
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

                switch (key)
                {
                    case "v":
                        r.Version = ReadJsonInt(json, ref i);
                        break;

                    case "routes":
                        if (i < json.Length && json[i] == '[')
                        {
                            r.Changed = true;
                            ParseRouteArray(json, ref i, r.Data);
                        }
                        else SkipValue(json, ref i);
                        break;

                    default:
                        SkipValue(json, ref i);
                        break;
                }
            }

            return r;
        }
    }
}