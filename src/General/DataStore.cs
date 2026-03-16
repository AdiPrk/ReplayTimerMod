using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ReplayTimerMod
{
    // Persists replays to disk, one JSON file per scene.
    //
    // File layout: <DataDirectory>/<sceneName>.json
    // JSON: { "entries": [ { snapshotId, capturedAtUtcTicks, sceneName,
    //                         entryFromScene, exitToScene, totalTime, data }, ... ] }
    //
    // "data" is an RTM3 string - identical to what [Copy] puts on the clipboard.
    // Loading or saving a replay goes through ReplayShareEncoder exclusively.

    [Serializable]
    internal class SceneIndex
    {
        public List<EntryIndex> entries = new List<EntryIndex>();
    }

    [Serializable]
    internal class EntryIndex
    {
        public string snapshotId = "";
        public long capturedAtUtcTicks = 0;
        public string sceneName = "";
        public string entryFromScene = "";
        public string exitToScene = "";
        public float totalTime = 0f;
        public string data = "";   // RTM3 string
    }

    public static class DataStore
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DataStore");

        private static readonly JsonSerializerSettings JsonCfg =
            new JsonSerializerSettings { Formatting = Formatting.None };

        private static string DataDirectory = "";

        // ── Init ──────────────────────────────────────────────────────────────

        public static void Init(string baseDirectory)
        {
            DataDirectory = Path.Combine(baseDirectory, "ReplayMod");
            Directory.CreateDirectory(DataDirectory);
            Log.LogInfo($"[DataStore] Directory: {DataDirectory}");
        }

        // ── Index I/O ─────────────────────────────────────────────────────────

        private static string FilePath(string sceneName) =>
            Path.Combine(DataDirectory, $"{sceneName}.json");

        private static SceneIndex LoadIndex(string path)
        {
            if (!File.Exists(path)) return new SceneIndex();
            try
            {
                var idx = JsonConvert.DeserializeObject<SceneIndex>(
                    File.ReadAllText(path), JsonCfg) ?? new SceneIndex();
                idx.entries ??= new List<EntryIndex>();
                return idx;
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index read failed {path}: {ex.Message}");
                return new SceneIndex();
            }
        }

        private static SceneIndex LoadIndexAndUpgrade(string path)
        {
            var idx = LoadIndex(path);
            bool changed = false;
            foreach (var entry in idx.entries)
                changed |= NormalizeMetadata(entry);
            if (changed)
                WriteIndex(path, idx);
            return idx;
        }

        private static void WriteIndex(string path, SceneIndex idx)
        {
            try
            {
                idx.entries ??= new List<EntryIndex>();
                if (idx.entries.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                foreach (var entry in idx.entries)
                    NormalizeMetadata(entry);

                File.WriteAllText(path, JsonConvert.SerializeObject(idx, JsonCfg));
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index write failed {path}: {ex.Message}");
            }
        }

        private static bool NormalizeMetadata(EntryIndex entry)
        {
            bool changed = false;
            if (string.IsNullOrWhiteSpace(entry.snapshotId))
            {
                entry.snapshotId = Guid.NewGuid().ToString("N");
                changed = true;
            }
            return changed;
        }

        private static bool MatchesRoute(EntryIndex entry, RoomKey key) =>
            entry.sceneName == key.SceneName
            && entry.entryFromScene == key.EntryFromScene
            && entry.exitToScene == key.ExitToScene;

        private static EntryIndex ToEntryIndex(ReplaySnapshot snapshot) =>
            new EntryIndex
            {
                snapshotId = snapshot.SnapshotId,
                capturedAtUtcTicks = snapshot.CapturedAtUtcTicks,
                sceneName = snapshot.Key.SceneName,
                entryFromScene = snapshot.Key.EntryFromScene,
                exitToScene = snapshot.Key.ExitToScene,
                totalTime = snapshot.TotalTime,
                data = snapshot.EncodedData
            };

        // ── Public API ────────────────────────────────────────────────────────

        public static void SaveSnapshot(ReplaySnapshot snapshot)
        {
            string path = FilePath(snapshot.Key.SceneName);
            try
            {
                var idx = LoadIndexAndUpgrade(path);
                idx.entries.Add(ToEntryIndex(snapshot));
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Saved {snapshot.Key}#{snapshot.SnapshotId} ({snapshot.Room.FrameCount} frames, {snapshot.EncodedData.Length} chars)");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Save failed {snapshot.Key}#{snapshot.SnapshotId}: {ex.Message}");
            }
        }

        public static void DeleteSnapshot(RoomKey key, string snapshotId)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var idx = LoadIndexAndUpgrade(path);
                idx.entries.RemoveAll(e => MatchesRoute(e, key) && e.snapshotId == snapshotId);
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Deleted snapshot {key}#{snapshotId}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] DeleteSnapshot failed {key}#{snapshotId}: {ex.Message}");
            }
        }

        public static void DeleteRoute(RoomKey key)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var idx = LoadIndexAndUpgrade(path);
                idx.entries.RemoveAll(e => MatchesRoute(e, key));
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Deleted route {key}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] DeleteRoute failed {key}: {ex.Message}");
            }
        }

        public static void DeleteScene(string sceneName)
        {
            string path = FilePath(sceneName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.LogInfo($"[DataStore] Deleted scene file {sceneName}");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] DeleteScene failed {sceneName}: {ex.Message}");
            }
        }

        public static List<ReplaySnapshot> LoadAll()
        {
            var result = new List<ReplaySnapshot>();
            if (!Directory.Exists(DataDirectory)) return result;

            foreach (string path in Directory.GetFiles(DataDirectory, "*.json"))
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                foreach (var entry in LoadIndexAndUpgrade(path).entries)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(entry.data))
                        {
                            Log.LogWarning($"[DataStore] Skipping empty entry in {sceneName}");
                            continue;
                        }

                        NormalizeMetadata(entry);
                        var room = ReplayShareEncoder.Decode(entry.data);
                        if (room == null)
                        {
                            Log.LogWarning($"[DataStore] Skipping corrupt entry in {sceneName}");
                            continue;
                        }

                        result.Add(new ReplaySnapshot(
                            entry.snapshotId,
                            entry.capturedAtUtcTicks,
                            room,
                            entry.data));
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"[DataStore] Skipping corrupt entry in {sceneName}: {ex.Message}");
                    }
                }
            }

            Log.LogInfo($"[DataStore] Loaded {result.Count} total snapshots");
            return result;
        }
    }
}