using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Sidecar store mapping snapshot IDs to the runner who originally set
    /// the run, for replays downloaded from the leaderboard. Locally
    /// recorded runs have no entry here.
    ///
    /// Kept separate from DataStore on purpose: it avoids touching the
    /// snapshot persistence schema (EntryIndex / MiniJson), so existing
    /// save files are untouched and the feature is fully self-contained.
    /// Persisted to ReplayMod/owners.json next to the data directory.
    /// </summary>
    internal static class ReplayOwners
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayOwners");

        private static readonly object _lock = new object();
        private static Dictionary<string, string> _map =
            new Dictionary<string, string>();
        private static bool _loaded;
        private static string? _path;

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>Runner name for a snapshot, or null if locally recorded.</summary>
        public static string? Get(string snapshotId)
        {
            if (string.IsNullOrEmpty(snapshotId)) return null;
            lock (_lock)
            {
                EnsureLoaded();
                return _map.TryGetValue(snapshotId, out var name) ? name : null;
            }
        }

        /// <summary>Tags a snapshot as downloaded from the given runner.</summary>
        public static void Set(string snapshotId, string runnerName)
        {
            if (string.IsNullOrEmpty(snapshotId)
                || string.IsNullOrEmpty(runnerName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                _map[snapshotId] = runnerName;
                Save();
            }
        }

        /// <summary>Removes a tag (e.g. when the snapshot is deleted).</summary>
        public static void Remove(string snapshotId)
        {
            if (string.IsNullOrEmpty(snapshotId)) return;
            lock (_lock)
            {
                EnsureLoaded();
                if (_map.Remove(snapshotId))
                    Save();
            }
        }

        // ── Persistence ────────────────────────────────────────────────

        private static string FilePath()
        {
            if (_path == null)
            {
                string baseDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? ".";
                string dir = Path.Combine(baseDir, "ReplayMod");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "owners.json");
            }
            return _path;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;
                _map = ParseFlatMap(File.ReadAllText(path));
                Log.LogInfo($"[ReplayOwners] Loaded {_map.Count} owner tags");
            }
            catch (Exception ex)
            {
                Log.LogWarning("[ReplayOwners] Load failed: " + ex.Message);
                _map = new Dictionary<string, string>();
            }
        }

        private static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('{');
                bool first = true;
                foreach (var kvp in _map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    JsonText.AppendQuoted(sb, kvp.Key);
                    sb.Append(':');
                    JsonText.AppendQuoted(sb, kvp.Value);
                }
                sb.Append('}');
                File.WriteAllText(FilePath(), sb.ToString());
            }
            catch (Exception ex)
            {
                Log.LogWarning("[ReplayOwners] Save failed: " + ex.Message);
            }
        }

        // ── Minimal flat string-map JSON ───────────────────────────────

        private static Dictionary<string, string> ParseFlatMap(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return result;
            i++;

            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWs(json, ref i);
                string value = ReadString(json, ref i);

                if (!string.IsNullOrEmpty(key))
                    result[key] = value;
            }

            return result;
        }

        private static string ReadString(string json, ref int i)
        {
            if (i >= json.Length || json[i] != '"') return "";
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
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < json.Length)
                            {
                                sb.Append((char)Convert.ToInt32(
                                    json.Substring(i + 1, 4), 16));
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
    }
}