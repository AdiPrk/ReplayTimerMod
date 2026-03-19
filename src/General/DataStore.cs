using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    // Persists replays to disk, one JSON file per scene.
    //
    // File layout: <DataDirectory>/<sceneName>.json
    // JSON: { "entries": [ { sceneName, entryFromScene, exitToScene,
    //                         totalTime, data }, ... ] }
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
                return MiniJson.Deserialize(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index read failed {path}: {ex.Message}");
                return new SceneIndex();
            }
        }

        private static void WriteIndex(string path, SceneIndex idx)
        {
            try
            {
                if (idx.entries.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                File.WriteAllText(path, MiniJson.Serialize(idx));
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index write failed {path}: {ex.Message}");
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static void SaveEntry(RecordedRoom room)
        {
            string path = FilePath(room.Key.SceneName);
            try
            {
                var idx = LoadIndex(path);
                int i = idx.entries.FindIndex(e =>
                    e.sceneName == room.Key.SceneName &&
                    e.entryFromScene == room.Key.EntryFromScene &&
                    e.exitToScene == room.Key.ExitToScene);

                string encoded = ReplayShareEncoder.Encode(room);
                var ei = new EntryIndex
                {
                    sceneName = room.Key.SceneName,
                    entryFromScene = room.Key.EntryFromScene,
                    exitToScene = room.Key.ExitToScene,
                    totalTime = room.TotalTime,
                    data = encoded
                };

                if (i >= 0) idx.entries[i] = ei;
                else idx.entries.Add(ei);

                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Saved {room.Key} ({room.FrameCount} frames, {encoded.Length} chars)");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Save failed {room.Key}: {ex.Message}");
            }
        }

        public static void DeleteEntry(RoomKey key)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var idx = LoadIndex(path);
                idx.entries.RemoveAll(e =>
                    e.sceneName == key.SceneName &&
                    e.entryFromScene == key.EntryFromScene &&
                    e.exitToScene == key.ExitToScene);
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] Deleted {key}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] DeleteEntry failed {key}: {ex.Message}");
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

        public static Dictionary<RoomKey, RecordedRoom> LoadAll()
        {
            var result = new Dictionary<RoomKey, RecordedRoom>();
            if (!Directory.Exists(DataDirectory)) return result;

            foreach (string path in Directory.GetFiles(DataDirectory, "*.json"))
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                foreach (var e in LoadIndex(path).entries)
                {
                    try
                    {
                        var room = ReplayShareEncoder.Decode(e.data);
                        if (room != null) result[room.Key] = room;
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"[DataStore] Skipping corrupt entry in {sceneName}: {ex.Message}");
                    }
                }
            }

            Log.LogInfo($"[DataStore] Loaded {result.Count} total entries");
            return result;
        }
    }
}
