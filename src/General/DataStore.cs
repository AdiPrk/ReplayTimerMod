using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;

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
        public bool hasVisualOverride = false;
        public float colorR = 1f;
        public float colorG = 1f;
        public float colorB = 1f;
        public float alpha = 0.4f;
    }

    public static class DataStore
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DataStore");

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
                var idx = MiniJson.Deserialize(File.ReadAllText(path));
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

                File.WriteAllText(path, MiniJson.Serialize(idx));
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index write failed {path}: {ex.Message}");
            }
        }

        private static bool NormalizeMetadata(EntryIndex entry)
        {
            bool changed = false;
            if (string.IsNullOrEmpty(entry.snapshotId))
            {
                entry.snapshotId = Guid.NewGuid().ToString("N");
                changed = true;
            }

            float colorR = Clamp01(entry.colorR);
            float colorG = Clamp01(entry.colorG);
            float colorB = Clamp01(entry.colorB);
            float alpha = Clamp01(entry.alpha);
            if (entry.colorR != colorR) { entry.colorR = colorR; changed = true; }
            if (entry.colorG != colorG) { entry.colorG = colorG; changed = true; }
            if (entry.colorB != colorB) { entry.colorB = colorB; changed = true; }
            if (entry.alpha != alpha) { entry.alpha = alpha; changed = true; }
            return changed;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

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
                data = snapshot.EncodedData,
                hasVisualOverride = snapshot.HasVisualOverride,
                colorR = snapshot.ColorR,
                colorG = snapshot.ColorG,
                colorB = snapshot.ColorB,
                alpha = snapshot.Alpha
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

        public static void UpdateSnapshotVisuals(RoomKey key, string snapshotId,
            bool hasVisualOverride, float colorR, float colorG, float colorB, float alpha)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var idx = LoadIndexAndUpgrade(path);
                var entry = idx.entries.Find(e => MatchesRoute(e, key) && e.snapshotId == snapshotId);
                if (entry == null)
                {
                    Log.LogWarning($"[DataStore] UpdateSnapshotVisuals missing {key}#{snapshotId}");
                    return;
                }

                entry.hasVisualOverride = hasVisualOverride;
                entry.colorR = Clamp01(colorR);
                entry.colorG = Clamp01(colorG);
                entry.colorB = Clamp01(colorB);
                entry.alpha = Clamp01(alpha);
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Updated snapshot visuals {key}#{snapshotId}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] UpdateSnapshotVisuals failed {key}#{snapshotId}: {ex.Message}");
            }
        }

        public static void ReplaceRouteSnapshots(RoomKey key,
            ICollection<ReplaySnapshot> retainedSnapshots)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var idx = LoadIndexAndUpgrade(path);
                idx.entries.RemoveAll(e => MatchesRoute(e, key));

                int added = 0;
                foreach (var snapshot in retainedSnapshots)
                {
                    if (!snapshot.Key.Equals(key))
                    {
                        Log.LogWarning($"[DataStore] Skipping mismatched route snapshot during replace: expected {key}, got {snapshot.Key}#{snapshot.SnapshotId}");
                        continue;
                    }

                    idx.entries.Add(ToEntryIndex(snapshot));
                    added++;
                }

                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Replaced route {key} with {added} snapshots");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] ReplaceRouteSnapshots failed {key}: {ex.Message}");
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
                        if (string.IsNullOrEmpty(entry.data))
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
                            entry.data,
                            entry.hasVisualOverride,
                            entry.colorR,
                            entry.colorG,
                            entry.colorB,
                            entry.alpha));
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
