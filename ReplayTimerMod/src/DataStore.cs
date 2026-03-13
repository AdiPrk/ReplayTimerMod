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

        // ── Public API ────────────────────────────────────────────────────────
        private static string FilePath(string sceneName) =>
            Path.Combine(DataDirectory, $"{sceneName}.json");

        public static void SaveEntry(RecordedRoom room)
        {
            string path = FilePath(room.Key.SceneName);
            try
            {
                SerializedScene data;
                if (File.Exists(path))
                {
                    data = JsonConvert.DeserializeObject<SerializedScene>(
                               File.ReadAllText(path), JsonSettings)
                           ?? new SerializedScene();
                    data.entries ??= new List<SerializedEntry>();
                }
                else
                {
                    data = new SerializedScene();
                }

                int idx = data.entries.FindIndex(e =>
                    e.sceneName == room.Key.SceneName &&
                    e.entryGate == room.Key.EntryGate &&
                    e.exitToScene == room.Key.ExitToScene);

                var serialized = ToSerialized(room);
                if (idx >= 0) data.entries[idx] = serialized;
                else data.entries.Add(serialized);

                string json = JsonConvert.SerializeObject(data, JsonSettings);
                File.WriteAllText(path, json);

                Log.LogInfo($"[DataStore] Saved {room.Key} " +
                            $"({room.FrameCount} frames, {json.Length} bytes)");
            }
            catch (Exception e)
            {
                Log.LogError($"[DataStore] Save failed for {room.Key}: {e.Message}");
            }
        }

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