using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ReplayTimerMod
{
    // ── On-disk format RTM1v3 ─────────────────────────────────────────────────
    //
    // One JSON file per scene: <scene>.json
    // JSON: { "entries": [ { sceneName, entryFromScene, exitToScene,
    //                         totalTime, data }, ... ] }
    //
    // "data" = base64( GZip( RTM1v3-binary ) )
    //
    // RTM1v3 binary layout:
    //   [6]     magic "RTM1v3"
    //   [4]     float  totalTime
    //   [4]     int32  frameCount N
    //   [2+xL]  x 2nd-order SVLQ stream  (uint16 len + bytes)
    //   [2+yL]  y 2nd-order SVLQ stream  (uint16 len + bytes)
    //   [⌈N/8⌉] facing bitfield  (MSB-first, 1 = facingRight)
    //   [1]     clipCount C  (uint8; 0 = no animation data)
    //   if C > 0:
    //     C × [2+N] clipName  (uint16 len + UTF-8)
    //     [N]  clipIndex[]    (uint8; 0xFF = no clip for this frame)
    //     [N]  animFrame[]    (uint8; saturated at 255)
    //
    // SVLQ = ZigZag(n) → ULEB128.  See FrameCodec.cs.
    //
    // Backward compat: RTM1v2 entries (no animation data) are decoded as-is
    // and upgraded to RTM1v3 on the next write.
    // ─────────────────────────────────────────────────────────────────────────

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
        public string data = "";   // base64( GZip( RTM1v3 binary ) )
    }

    public static class DataStore
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DataStore");

        private static readonly JsonSerializerSettings JsonCfg =
            new JsonSerializerSettings { Formatting = Formatting.None };

        private static string DataDirectory = "";

        private static readonly byte[] MagicV3 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'1', (byte)'v', (byte)'3' };
        private static readonly byte[] MagicV2 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'1', (byte)'v', (byte)'2' };

        // ── Init ──────────────────────────────────────────────────────────────

        public static void Init(string baseDirectory)
        {
            DataDirectory = Path.Combine(baseDirectory, "ReplayMod");
            Directory.CreateDirectory(DataDirectory);
            Log.LogInfo($"[DataStore] Directory: {DataDirectory}");
        }

        // ── File path ─────────────────────────────────────────────────────────

        private static string FilePath(string sceneName) =>
            Path.Combine(DataDirectory, $"{sceneName}.json");

        // ── Index I/O ─────────────────────────────────────────────────────────

        private static SceneIndex LoadIndex(string path)
        {
            if (!File.Exists(path)) return new SceneIndex();
            try
            {
                var result = JsonConvert.DeserializeObject<SceneIndex>(
                    File.ReadAllText(path), JsonCfg);
                return result ?? new SceneIndex();
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
                File.WriteAllText(path, JsonConvert.SerializeObject(idx, JsonCfg));
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Index write failed {path}: {ex.Message}");
            }
        }

        // ── RTM1v3 encode ─────────────────────────────────────────────────────

        private static string EncodeFrames(RecordedRoom room)
        {
            int n = room.FrameCount;

            byte[] xStream = FrameCodec.Encode2ndOrder(room.Frames, getX: true);
            byte[] yStream = FrameCodec.Encode2ndOrder(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            // Build deduplicated clip name table.
            var clipTable = new List<string>();
            var clipIndex = new byte[n];
            var animFrames = new byte[n];
            bool hasAnimData = false;

            for (int i = 0; i < n; i++)
            {
                string clip = room.Frames[i].animClip ?? "";
                if (clip.Length == 0)
                {
                    clipIndex[i] = 0xFF;
                }
                else
                {
                    hasAnimData = true;
                    int idx = clipTable.IndexOf(clip);
                    if (idx < 0) { idx = clipTable.Count; clipTable.Add(clip); }
                    clipIndex[i] = (byte)Math.Min(idx, 254); // 0xFF reserved
                }
                animFrames[i] = (byte)Math.Min(room.Frames[i].animFrame, 255);
            }

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(MagicV3);
                w.Write(room.TotalTime);
                w.Write(n);
                w.Write((ushort)xStream.Length); w.Write(xStream);
                w.Write((ushort)yStream.Length); w.Write(yStream);

                for (int b = 0; b < facingBytes; b++)
                {
                    byte bits = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int fi = b * 8 + bit;
                        if (fi < n && room.Frames[fi].facingRight)
                            bits |= (byte)(0x80 >> bit);
                    }
                    w.Write(bits);
                }

                byte C = (byte)(hasAnimData ? clipTable.Count : 0);
                w.Write(C);
                if (C > 0)
                {
                    foreach (string name in clipTable)
                        FrameCodec.WriteString(w, name);
                    w.Write(clipIndex);
                    w.Write(animFrames);
                }
            }

            return Convert.ToBase64String(GZipCompress(ms.ToArray()));
        }

        // ── Decode ────────────────────────────────────────────────────────────

        private static FrameData[] DecodeFrames(string b64)
        {
            byte[] raw = GZipDecompress(Convert.FromBase64String(b64));

            if (raw.Length >= 6 && raw[0] == 'R' && raw[1] == 'T' && raw[2] == 'M' && raw[3] == '1')
            {
                if (raw[4] == 'v' && raw[5] == '3') return DecodeRTM1v3(raw);
                if (raw[4] == 'v' && raw[5] == '2') return DecodeRTM1v2Legacy(raw);
            }

            throw new InvalidDataException("Unknown frame format — delete old save files.");
        }

        private static FrameData[] DecodeRTM1v3(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            ms.Seek(MagicV3.Length, SeekOrigin.Begin);
            r.ReadSingle();   // totalTime (already in EntryIndex)
            int n = r.ReadInt32();

            short[] xs = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            short[] ys = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            byte[] facingBits = r.ReadBytes((n + 7) / 8);

            byte clipCount = r.ReadByte();
            string[]? clipNames = null;
            byte[]? clipIndexes = null;
            byte[]? animFrameBytes = null;

            if (clipCount > 0)
            {
                clipNames = new string[clipCount];
                for (int i = 0; i < clipCount; i++)
                    clipNames[i] = FrameCodec.ReadString(r);
                clipIndexes = r.ReadBytes(n);
                animFrameBytes = r.ReadBytes(n);
            }

            var frames = new FrameData[n];
            for (int i = 0; i < n; i++)
            {
                string clip = "";
                int animFrame = 0;
                if (clipNames != null && clipIndexes![i] != 0xFF)
                {
                    int ci = clipIndexes[i];
                    if (ci < clipNames.Length) clip = clipNames[ci];
                    animFrame = animFrameBytes![i];
                }

                frames[i] = new FrameData
                {
                    x = xs[i] / FrameCodec.PosScale,
                    y = ys[i] / FrameCodec.PosScale,
                    facingRight = (facingBits[i / 8] & (0x80 >> (i % 8))) != 0,
                    animClip = clip,
                    animFrame = animFrame
                };
            }
            return frames;
        }

        // Decodes RTM1v2 saves (no animation data). Upgraded to v3 on next write.
        private static FrameData[] DecodeRTM1v2Legacy(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            ms.Seek(MagicV2.Length, SeekOrigin.Begin);
            r.ReadSingle();   // totalTime
            int n = r.ReadInt32();

            short[] xs = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            short[] ys = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            byte[] facingBits = r.ReadBytes((n + 7) / 8);

            var frames = new FrameData[n];
            for (int i = 0; i < n; i++)
                frames[i] = new FrameData
                {
                    x = xs[i] / FrameCodec.PosScale,
                    y = ys[i] / FrameCodec.PosScale,
                    facingRight = (facingBits[i / 8] & (0x80 >> (i % 8))) != 0,
                    animClip = "",
                    animFrame = 0
                };
            return frames;
        }

        // ── Compression ───────────────────────────────────────────────────────

        private static byte[] GZipCompress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal))
                gz.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] GZipDecompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
                gz.CopyTo(output);
            return output.ToArray();
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

                string encoded = EncodeFrames(room);
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

        private static List<RecordedRoom> LoadScene(string sceneName)
        {
            string path = FilePath(sceneName);
            var result = new List<RecordedRoom>();
            if (!File.Exists(path)) return result;

            try
            {
                var idx = LoadIndex(path);
                foreach (var e in idx.entries)
                {
                    try
                    {
                        var frames = DecodeFrames(e.data);
                        var key = new RoomKey(e.sceneName, e.entryFromScene, e.exitToScene);
                        result.Add(new RecordedRoom(key, e.totalTime, frames));
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"[DataStore] Skipping corrupt entry in {sceneName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[DataStore] Load failed {sceneName}: {ex.Message}");
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

            Log.LogInfo($"[DataStore] Loaded {result.Count} total entries");
            return result;
        }
    }
}