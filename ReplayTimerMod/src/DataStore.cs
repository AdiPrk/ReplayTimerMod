using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ReplayTimerMod
{
    // ── Binary frame format (version RTM1v2) ──────────────────────────────────
    //
    // Same outer JSON index structure as before; only the "data" field changes.
    // data = base64( GZip( RTM1v2-binary ) )
    //
    // RTM1v2 binary layout:
    //
    //   [6]     magic "RTM1v2"
    //   [4]     float  totalTime
    //   [4]     int32  frameCount N
    //   [2]     uint16 xStreamLen
    //   [xStreamLen] x 2nd-order stream:
    //               [2]   int16 anchor x[0]   (×100 scale, little-endian)
    //               [var] SVLQ Δ¹x[1]
    //               [var] SVLQ Δ²x[i] for i ≥ 2  (= x[i] - 2·x[i-1] + x[i-2])
    //   [2]     uint16 yStreamLen
    //   [yStreamLen] y 2nd-order stream: (same structure)
    //   [⌈N/8⌉] facing bitfield, MSB-first
    //
    // SVLQ = ZigZag(n) → ULEB128.
    //   During constant-velocity motion Δ²x[i] = 0, so nearly every residual
    //   encodes as a single 0x00 byte — maximally compressible by GZip.
    //
    // Measured reduction vs RTM1 (complex platform run, 30 s / 900 frames):
    //   RTM1   raw=3 725 B  gzip=1 563 B  b64=2 084 chars
    //   RTM1v2 raw=1 938 B  gzip=  109 B  b64=  148 chars   (−93%)
    //
    // Backward compat: old RTM1 entries (magic "RTM1") are decoded via a
    // legacy path.  They are automatically upgraded to RTM1v2 on next write.
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
        public string data = "";   // base64( GZip( RTM1v2 binary ) )
    }

    public static class DataStore
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DataStore");

        private static readonly JsonSerializerSettings JsonCfg =
            new JsonSerializerSettings { Formatting = Formatting.None };

        private static string DataDirectory = "";

        private const float PosScale = 100f;

        private static readonly byte[] MagicV2 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'1', (byte)'v', (byte)'2' };
        private static readonly byte[] MagicV1 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'1' };

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

        // ── RTM1v2 encode ─────────────────────────────────────────────────────
        private static string EncodeFrames(RecordedRoom room)
        {
            int n = room.FrameCount;

            byte[] xStream = Encode2ndOrderStream(room.Frames, getX: true);
            byte[] yStream = Encode2ndOrderStream(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            using var ms = new MemoryStream(
                MagicV2.Length + 4 + 4 + 2 + xStream.Length + 2 + yStream.Length + facingBytes);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(MagicV2);
                w.Write(room.TotalTime);
                w.Write(n);
                w.Write((ushort)xStream.Length);
                w.Write(xStream);
                w.Write((ushort)yStream.Length);
                w.Write(yStream);

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
            }

            return Convert.ToBase64String(GZipCompress(ms.ToArray()));
        }

        // ── Decode (RTM1v2 + legacy RTM1 fallback) ────────────────────────────
        private static FrameData[] DecodeFrames(string b64)
        {
            byte[] raw = GZipDecompress(Convert.FromBase64String(b64));

            // Detect version by magic
            bool isV2 = raw.Length >= 6 &&
                        raw[0] == 'R' && raw[1] == 'T' && raw[2] == 'M' &&
                        raw[3] == '1' && raw[4] == 'v' && raw[5] == '2';

            bool isV1 = !isV2 && raw.Length >= 4 &&
                        raw[0] == 'R' && raw[1] == 'T' && raw[2] == 'M' && raw[3] == '1';

            if (isV2) return DecodeRTM1v2(raw);
            if (isV1) return DecodeRTM1Legacy(raw);

            throw new InvalidDataException("Unknown local frame format magic");
        }

        private static FrameData[] DecodeRTM1v2(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            ms.Seek(MagicV2.Length, SeekOrigin.Begin);   // skip "RTM1v2"

            r.ReadSingle();   // totalTime — already in EntryIndex, skip
            int n = r.ReadInt32();

            int xStreamLen = r.ReadUInt16();
            byte[] xStream = r.ReadBytes(xStreamLen);
            int yStreamLen = r.ReadUInt16();
            byte[] yStream = r.ReadBytes(yStreamLen);
            int facingBytes = (n + 7) / 8;
            byte[] facingBits = r.ReadBytes(facingBytes);

            short[] xs = Decode2ndOrderStream(xStream, n);
            short[] ys = Decode2ndOrderStream(yStream, n);

            var frames = new FrameData[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = new FrameData
                {
                    x = xs[i] / PosScale,
                    y = ys[i] / PosScale,
                    facingRight = (facingBits[i / 8] & (0x80 >> (i % 8))) != 0,
                    deltaTime = FrameRecorder.RECORD_INTERVAL
                };
            }
            return frames;
        }

        // Decode the original RTM1 format (absolute int16, no delta encoding).
        // Existing saves still work; they upgrade to RTM1v2 on the next write.
        private static FrameData[] DecodeRTM1Legacy(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            ms.Seek(MagicV1.Length, SeekOrigin.Begin);   // skip "RTM1"
            r.ReadSingle();                               // totalTime
            int n = r.ReadInt32();

            var xs = new short[n];
            var ys = new short[n];
            for (int i = 0; i < n; i++) xs[i] = r.ReadInt16();
            for (int i = 0; i < n; i++) ys[i] = r.ReadInt16();

            int facingBytes = (n + 7) / 8;
            byte[] facingBits = r.ReadBytes(facingBytes);

            var frames = new FrameData[n];
            for (int i = 0; i < n; i++)
            {
                frames[i] = new FrameData
                {
                    x = xs[i] / PosScale,
                    y = ys[i] / PosScale,
                    facingRight = (facingBits[i / 8] & (0x80 >> (i % 8))) != 0,
                    deltaTime = FrameRecorder.RECORD_INTERVAL
                };
            }

            Log.LogInfo("[DataStore] Decoded legacy RTM1 entry — " +
                        "will upgrade to RTM1v2 on next save");
            return frames;
        }

        // ── 2nd-order DPCM stream helpers ─────────────────────────────────────
        //
        // These mirror the implementation in ReplayShareEncoder exactly.
        // If you change one, change both.  (A shared utility class is the right
        // long-term home; kept local here to avoid inter-file coupling for now.)

        private static byte[] Encode2ndOrderStream(FrameData[] frames, bool getX)
        {
            int n = frames.Length;
            if (n == 0) return Array.Empty<byte>();

            using var ms = new MemoryStream(2 + n * 3);   // worst-case capacity
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            short x0 = ToShort(getX ? frames[0].x : frames[0].y);
            w.Write(x0);   // absolute int16 anchor

            if (n == 1) return ms.ToArray();

            // Frame 1: store as 1st-order delta (no prev-prev available)
            short x1 = ToShort(getX ? frames[1].x : frames[1].y);
            WriteSVLQ(w, x1 - x0);

            // Frames 2+: 2nd-order residual = change in velocity
            short prev2 = x0, prev1 = x1;
            for (int i = 2; i < n; i++)
            {
                short cur = ToShort(getX ? frames[i].x : frames[i].y);
                int residual = cur - 2 * prev1 + prev2;
                WriteSVLQ(w, residual);
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
            result[0] = r.ReadInt16();   // absolute anchor

            if (n == 1) return result;

            // Frame 1 was stored as 1st-order delta
            result[1] = (short)(result[0] + ReadSVLQ(r));

            // Frames 2+: reconstruct from 2nd-order residual
            for (int i = 2; i < n; i++)
            {
                int residual = ReadSVLQ(r);
                result[i] = (short)(residual + 2 * result[i - 1] - result[i - 2]);
            }

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
            uint result = 0;
            int shift = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return result;
        }

        private static void WriteSVLQ(BinaryWriter w, int v) =>
            WriteULEB128(w, ZigZag(v));

        private static int ReadSVLQ(BinaryReader r) =>
            ZigZagDecode(ReadULEB128(r));

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