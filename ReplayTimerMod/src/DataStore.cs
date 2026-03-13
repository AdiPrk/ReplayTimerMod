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
    // JSON structure: { "entries": [ { sceneName, entryFromScene, exitToScene,
    //                                   totalTime, data }, ... ] }
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
    //     for i in 0..C-1:  [2+N] clipName[i]  (uint16 len + UTF-8)
    //     [N]  clipIndex[]   (uint8 per frame; 0xFF = no clip)
    //     [N]  animFrame[]   (uint8 per frame; saturated at 255)
    //
    // SVLQ = ZigZag(n) → ULEB128.
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

        private const float PosScale = 100f;

        private static readonly byte[] MagicV3 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'1', (byte)'v', (byte)'3' };

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

            byte[] xStream = Encode2ndOrderStream(room.Frames, getX: true);
            byte[] yStream = Encode2ndOrderStream(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            // Build clip name table (deduplicated, insertion-ordered).
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

                // Animation section.
                byte C = (byte)(hasAnimData ? clipTable.Count : 0);
                w.Write(C);
                if (C > 0)
                {
                    foreach (string name in clipTable)
                        WriteString(w, name);
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

            if (raw.Length >= 6 &&
                raw[0] == 'R' && raw[1] == 'T' && raw[2] == 'M' &&
                raw[3] == '1' && raw[4] == 'v' && raw[5] == '3')
                return DecodeRTM1v3(raw);

            throw new InvalidDataException(
                $"Unrecognised frame format — " +
                $"got '{(char)raw[0]}{(char)raw[1]}{(char)raw[2]}{(char)raw[3]}'." +
                " Delete old save files.");
        }

        private static FrameData[] DecodeRTM1v3(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            ms.Seek(MagicV3.Length, SeekOrigin.Begin);
            r.ReadSingle();   // totalTime (already in EntryIndex)
            int n = r.ReadInt32();

            short[] xs = Decode2ndOrderStream(r.ReadBytes(r.ReadUInt16()), n);
            short[] ys = Decode2ndOrderStream(r.ReadBytes(r.ReadUInt16()), n);
            byte[] facingBits = r.ReadBytes((n + 7) / 8);

            // Animation section.
            byte clipCount = r.ReadByte();
            string[]? clipNames = null;
            byte[]? clipIndexes = null;
            byte[]? animFrameBytes = null;

            if (clipCount > 0)
            {
                clipNames = new string[clipCount];
                for (int i = 0; i < clipCount; i++)
                    clipNames[i] = ReadString(r);
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
                    if (ci < clipNames.Length)
                        clip = clipNames[ci];
                    animFrame = animFrameBytes![i];
                }

                frames[i] = new FrameData
                {
                    x = xs[i] / PosScale,
                    y = ys[i] / PosScale,
                    facingRight = (facingBits[i / 8] & (0x80 >> (i % 8))) != 0,
                    animClip = clip,
                    animFrame = animFrame
                };
            }
            return frames;
        }

        // ── 2nd-order DPCM stream helpers ─────────────────────────────────────
        //
        // Mirror of the implementation in ReplayShareEncoder.
        // If you change one, change both.

        private static byte[] Encode2ndOrderStream(FrameData[] frames, bool getX)
        {
            int n = frames.Length;
            if (n == 0) return Array.Empty<byte>();

            using var ms = new MemoryStream(2 + n * 3);
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            short x0 = ToShort(getX ? frames[0].x : frames[0].y);
            w.Write(x0);
            if (n == 1) return ms.ToArray();

            short x1 = ToShort(getX ? frames[1].x : frames[1].y);
            WriteSVLQ(w, x1 - x0);

            short prev2 = x0, prev1 = x1;
            for (int i = 2; i < n; i++)
            {
                short cur = ToShort(getX ? frames[i].x : frames[i].y);
                WriteSVLQ(w, cur - 2 * prev1 + prev2);
                prev2 = prev1;
                prev1 = cur;
            }
            return ms.ToArray();
        }

        private static short[] Decode2ndOrderStream(byte[] stream, int n)
        {
            if (n == 0) return Array.Empty<short>();

            using var ms = new MemoryStream(stream);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            var result = new short[n];
            result[0] = r.ReadInt16();
            if (n == 1) return result;

            result[1] = (short)(result[0] + ReadSVLQ(r));
            for (int i = 2; i < n; i++)
                result[i] = (short)(ReadSVLQ(r) + 2 * result[i - 1] - result[i - 2]);

            return result;
        }

        // ── ZigZag + ULEB128 (SVLQ) ───────────────────────────────────────────

        private static uint ZigZag(int n) =>
            n >= 0 ? (uint)(n << 1) : (uint)((-n << 1) - 1);

        private static int ZigZagDecode(uint u) =>
            (u & 1) == 0 ? (int)(u >> 1) : -(int)(u >> 1) - 1;

        private static void WriteULEB128(BinaryWriter w, uint v)
        {
            do
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) b |= 0x80;
                w.Write(b);
            } while (v != 0);
        }

        private static uint ReadULEB128(BinaryReader r)
        {
            uint result = 0; int shift = 0; byte b;
            do { b = r.ReadByte(); result |= (uint)(b & 0x7F) << shift; shift += 7; }
            while ((b & 0x80) != 0);
            return result;
        }

        private static void WriteSVLQ(BinaryWriter w, int v) =>
            WriteULEB128(w, ZigZag(v));

        private static int ReadSVLQ(BinaryReader r) =>
            ZigZagDecode(ReadULEB128(r));

        // ── String helpers ─────────────────────────────────────────────────────

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        private static string ReadString(BinaryReader r) =>
            Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));

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

        private static short ToShort(float world) =>
            (short)Math.Max(short.MinValue,
                   Math.Min(short.MaxValue, (int)Math.Round(world * PosScale)));

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
                Log.LogInfo($"[DataStore] Saved {room.Key} " +
                            $"({room.FrameCount} frames, {encoded.Length} chars)");
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
                int removed = idx.entries.RemoveAll(e =>
                    e.sceneName == key.SceneName &&
                    e.entryFromScene == key.EntryFromScene &&
                    e.exitToScene == key.ExitToScene);
                WriteIndex(path, idx);
                Log.LogInfo($"[DataStore] DeleteEntry {key} — removed {removed}");
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

        public static List<RecordedRoom> LoadScene(string sceneName)
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
                        Log.LogWarning($"[DataStore] Skipping corrupt entry " +
                                       $"in {sceneName}: {ex.Message}");
                    }
                }
                Log.LogInfo($"[DataStore] Loaded {result.Count} for {sceneName}");
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