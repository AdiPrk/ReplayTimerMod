using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ReplayTimerMod
{
    [Serializable]
    public class SerializedFrame
    {
        public float[] x = Array.Empty<float>();
        public float[] y = Array.Empty<float>();
        public byte[] facing = Array.Empty<byte>();
        // deltaTime is not stored — always FrameRecorder.RECORD_INTERVAL on load
    }

    [Serializable]
    public class SerializedEntry
    {
        public string sceneName = "";
        public string entryGate = "";
        public string exitToScene = "";
        public float totalTime = 0f;
        public SerializedFrame frames = new SerializedFrame();
    }

    [Serializable]
    public class SerializedScene
    {
        public List<SerializedEntry> entries = new List<SerializedEntry>();
    }

    public static class DataStore
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DataStore");

        private static readonly JsonSerializerSettings JsonSettings =
            new JsonSerializerSettings { Formatting = Formatting.None };

        private static string DataDirectory = "";

        public static void Init(string baseDirectory)
        {
            DataDirectory = Path.Combine(baseDirectory, "ReplayMod");
            Directory.CreateDirectory(DataDirectory);
            Log.LogInfo($"[DataStore] Directory: {DataDirectory}");
        }

        // ── Conversion ────────────────────────────────────────────────────────
        private static SerializedEntry ToSerialized(RecordedRoom room)
        {
            int n = room.FrameCount;
            var sf = new SerializedFrame
            {
                x = new float[n],
                y = new float[n],
                facing = new byte[n]
            };

            for (int i = 0; i < n; i++)
            {
                sf.x[i] = room.Frames[i].x;
                sf.y[i] = room.Frames[i].y;
                sf.facing[i] = room.Frames[i].facingRight ? (byte)1 : (byte)0;
            }

            return new SerializedEntry
            {
                sceneName = room.Key.SceneName,
                entryGate = room.Key.EntryGate,
                exitToScene = room.Key.ExitToScene,
                totalTime = room.TotalTime,
                frames = sf
            };
        }

        private static RecordedRoom FromSerialized(SerializedEntry e)
        {
            int n = e.frames.x?.Length ?? 0;
            var frames = new FrameData[n];

            for (int i = 0; i < n; i++)
            {
                frames[i] = new FrameData
                {
                    x = e.frames.x[i],
                    y = e.frames.y[i],
                    deltaTime = FrameRecorder.RECORD_INTERVAL,
                    facingRight = e.frames.facing[i] != 0
                };
            }

            return new RecordedRoom(
                new RoomKey(e.sceneName, e.entryGate, e.exitToScene),
                e.totalTime, frames);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string FilePath(string sceneName) =>
            Path.Combine(DataDirectory, $"{sceneName}.json");

        // Load the raw SerializedScene from disk (or return empty if missing).
        private static SerializedScene LoadRaw(string path)
        {
            if (!File.Exists(path)) return new SerializedScene();
            try
            {
                return JsonConvert.DeserializeObject<SerializedScene>(
                           File.ReadAllText(path), JsonSettings)
                       ?? new SerializedScene();
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] Failed to read {path}: {e.Message}");
                return new SerializedScene();
            }
        }

        // Write a SerializedScene back to disk, or delete the file if it's empty.
        private static void WriteRaw(string path, SerializedScene data)
        {
            try
            {
                if (data.entries == null || data.entries.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                File.WriteAllText(path, JsonConvert.SerializeObject(data, JsonSettings));
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] Failed to write {path}: {e.Message}");
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        // Save (or overwrite) a single entry.
        public static void SaveEntry(RecordedRoom room)
        {
            string path = FilePath(room.Key.SceneName);
            try
            {
                var data = LoadRaw(path);
                data.entries ??= new List<SerializedEntry>();

                int idx = data.entries.FindIndex(e =>
                    e.sceneName == room.Key.SceneName &&
                    e.entryGate == room.Key.EntryGate &&
                    e.exitToScene == room.Key.ExitToScene);

                var serialized = ToSerialized(room);
                if (idx >= 0) data.entries[idx] = serialized;
                else data.entries.Add(serialized);

                WriteRaw(path, data);
                Log.LogInfo($"[DataStore] Saved {room.Key} ({room.FrameCount} frames)");
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] Save failed for {room.Key}: {e.Message}");
            }
        }

        // Delete a single route entry. If the file becomes empty, deletes it.
        public static void DeleteEntry(RoomKey key)
        {
            string path = FilePath(key.SceneName);
            try
            {
                var data = LoadRaw(path);
                data.entries ??= new List<SerializedEntry>();

                int removed = data.entries.RemoveAll(e =>
                    e.sceneName == key.SceneName &&
                    e.entryGate == key.EntryGate &&
                    e.exitToScene == key.ExitToScene);

                WriteRaw(path, data); // deletes file if entries now empty
                Log.LogInfo($"[DataStore] DeleteEntry {key} — removed {removed}");
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] DeleteEntry failed for {key}: {e.Message}");
            }
        }

        // Delete every entry for a scene (deletes the whole JSON file).
        public static void DeleteScene(string sceneName)
        {
            string path = FilePath(sceneName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Log.LogInfo($"[DataStore] Deleted scene file for {sceneName}");
                }
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] DeleteScene failed for {sceneName}: {e.Message}");
            }
        }

        // Load all entries for one scene.
        public static List<RecordedRoom> LoadScene(string sceneName)
        {
            string path = FilePath(sceneName);
            var result = new List<RecordedRoom>();
            if (!File.Exists(path)) return result;

            try
            {
                var data = JsonConvert.DeserializeObject<SerializedScene>(
                               File.ReadAllText(path), JsonSettings);
                if (data?.entries == null) return result;

                foreach (var entry in data.entries)
                {
                    try { result.Add(FromSerialized(entry)); }
                    catch (Exception e)
                    {
                        Log.LogWarning($"[DataStore] Skipping corrupt entry " +
                                       $"in {sceneName}: {e.Message}");
                    }
                }
                Log.LogInfo($"[DataStore] Loaded {result.Count} entries for {sceneName}");
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] Load failed for {sceneName}: {e.Message}");
            }

            return result;
        }

        // Load every entry across all scene files.
        public static Dictionary<RoomKey, RecordedRoom> LoadAll()
        {
            var result = new Dictionary<RoomKey, RecordedRoom>();
            if (!Directory.Exists(DataDirectory)) return result;

            foreach (string path in Directory.GetFiles(DataDirectory, "*.json"))
            {
                string sceneName = Path.GetFileNameWithoutExtension(path);
                foreach (var room in LoadScene(sceneName))
                    result[room.Key] = room;
            }

            Log.LogInfo($"[DataStore] Loaded {result.Count} total PB entries");
            return result;
        }
    }
}