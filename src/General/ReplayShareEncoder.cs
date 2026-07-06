using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    // ── RTM3 format ───────────────────────────────────────────────────────────
    // RecordedRoom -> RTM3 binary -> Deflate -> Base64.
    //
    // Binary layout (before Deflate):
    //   [4]     magic "RTM3"
    //   [1]     version = 0x02
    //   [2+N]   sceneName       (uint16 length prefix + UTF-8)
    //   [2+N]   entryFromScene
    //   [2+N]   exitToScene
    //   [4]     totalTime       float32
    //   [4]     frameCount N    int32
    //   [2+xL]  x 2nd-order SVLQ stream
    //   [2+yL]  y 2nd-order SVLQ stream
    //   [⌈N/8⌉] facing bitfield  MSB-first, 1 = facingRight
    //   [1]     clipCount C     uint8  (0 = no animation data)
    //   if C > 0:
    //     C × [2+N]  clipName  (uint16 len + UTF-8)
    //     [N]  clipIndex[]     uint8  (0xFF = no clip for this frame)
    //     [N]  animFrame[]     uint8  (saturated at 255)
    //
    // SVLQ = ZigZag(n) -> ULEB128. See FrameCodec.cs.
    //
    // Optional trailing extension section (appended after the anim block; the
    // reader above never checks EOF, so decoders that predate it - including
    // the server's verifyReplayConsistency, which parses the header only -
    // simply ignore it):
    //   [4]     section magic "RTMX"
    //   then TLV records until end of blob:
    //     [1]   type            uint8
    //     [2]   payload length  uint16 LE
    //     [len] payload
    //   type 0x01: modifier mask, payload = int32 LE (see ModifierMask)
    // The section is written only when the run has a known modifier mask;
    // absence (or any malformed/truncated trailer) decodes as
    // ModifierMask.Unknown. Unknown TLV types are skipped by length.
    // ─────────────────────────────────────────────────────────────────────────

    public static class ReplayShareEncoder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ShareEncoder");

        private static readonly byte[] Magic =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'3' };

        private const byte Version = 0x02;

        // Trailing extension section - see the format comment above.
        private static readonly byte[] ExtMagic =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'X' };

        private const byte ExtTypeModifiers = 0x01;

        // Frame-count ceiling — bounds allocation sizing for untrusted replays.
        private const int MaxFrames = 20000;

        // Pasted share-string ceiling, checked before any base64-decode/inflate.
        private const int MaxShareStringLength = 16 * 1024 * 1024;

        // ── Public API ────────────────────────────────────────────────────────

        public static string Encode(RecordedRoom room)
        {
            byte[] binary = WriteBinary(room);
            byte[] compressed = Compress.CompressData(binary);
            string result = Convert.ToBase64String(compressed);
            Log.LogInfo($"[RTM3] {room.Key}: {room.FrameCount} frames -> " +
                        $"binary={binary.Length}B deflate={compressed.Length}B str={result.Length}ch");
            return result;
        }

        public static RecordedRoom? Decode(string encoded)
        {
            try
            {
                return ReadBinary(Compress.DecompressData(Convert.FromBase64String(encoded)));
            }
            catch (Exception ex)
            {
                Log.LogError($"[ShareEncoder] Decode failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decode straight from the compressed RTM3 bytes (the raw payload the
        /// server returns for downloads), skipping the base64 step that the
        /// string overload performs.
        /// </summary>
        public static RecordedRoom? Decode(byte[] compressed)
        {
            try
            {
                return ReadBinary(Compress.DecompressData(compressed));
            }
            catch (Exception ex)
            {
                Log.LogError($"[ShareEncoder] Decode failed: {ex.Message}");
                return null;
            }
        }

        // ── Write ─────────────────────────────────────────────────────────────

        private static byte[] WriteBinary(RecordedRoom room)
        {
            int n = room.FrameCount;
            byte[] xStream = FrameCodec.Encode2ndOrder(room.Frames, getX: true);
            byte[] yStream = FrameCodec.Encode2ndOrder(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            // Build deduplicated clip table.
            var clipTable = new List<string>();
            var clipIndex = new byte[n];
            var animFrames = new byte[n];
            bool hasAnim = false;

            for (int i = 0; i < n; i++)
            {
                string clip = room.Frames[i].animClip ?? "";
                if (clip.Length == 0)
                {
                    clipIndex[i] = 0xFF;
                }
                else
                {
                    hasAnim = true;
                    int idx = clipTable.IndexOf(clip);
                    if (idx < 0) { idx = clipTable.Count; clipTable.Add(clip); }
                    clipIndex[i] = (byte)Math.Min(idx, 254); // 0xFF reserved
                }
                animFrames[i] = (byte)Math.Min(room.Frames[i].animFrame, 255);
            }

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(Version);
                FrameCodec.WriteString(w, room.Key.SceneName);
                FrameCodec.WriteString(w, room.Key.EntryFromScene);
                FrameCodec.WriteString(w, room.Key.ExitToScene);
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

                byte C = (byte)(hasAnim ? clipTable.Count : 0);
                w.Write(C);
                if (C > 0)
                {
                    foreach (string name in clipTable)
                        FrameCodec.WriteString(w, name);
                    w.Write(clipIndex);
                    w.Write(animFrames);
                }

                // Optional RTMX trailer - only for runs with a known mask, so
                // legacy data round-trips byte-identically.
                if (ModifierMask.IsKnown(room.Modifiers))
                {
                    w.Write(ExtMagic);
                    w.Write(ExtTypeModifiers);
                    w.Write((ushort)4);
                    w.Write(room.Modifiers);
                }
            }
            return ms.ToArray();
        }

        // ── Read ──────────────────────────────────────────────────────────────

        private static RecordedRoom ReadBinary(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8);

            for (int i = 0; i < 4; i++)
                if (r.ReadByte() != Magic[i])
                    throw new Exception("Bad RTM3 magic");

            byte ver = r.ReadByte();
            if (ver != Version)
                throw new Exception($"Unsupported RTM3 version 0x{ver:X2}");

            string sceneName = FrameCodec.ReadString(r);
            string entryFromScene = FrameCodec.ReadString(r);
            string exitToScene = FrameCodec.ReadString(r);
            float totalTime = r.ReadSingle();
            int n = r.ReadInt32();
            if (n < 0 || n > MaxFrames)
                throw new Exception($"Implausible frame count: {n}");

            short[] xs = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            short[] ys = FrameCodec.Decode2ndOrder(r.ReadBytes(r.ReadUInt16()), n);
            byte[] facingBits = r.ReadBytes((n + 7) / 8);

            byte C = r.ReadByte();
            string[]? clipNames = null;
            byte[]? clipIndexes = null;
            byte[]? animFrames = null;

            if (C > 0)
            {
                clipNames = new string[C];
                for (int i = 0; i < C; i++)
                    clipNames[i] = FrameCodec.ReadString(r);
                clipIndexes = r.ReadBytes(n);
                animFrames = r.ReadBytes(n);
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
                    animFrame = animFrames![i];
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

            int modifiers = TryReadExtensions(r, raw.Length);

            return new RecordedRoom(
                new RoomKey(sceneName, entryFromScene, exitToScene),
                totalTime, frames, modifiers);
        }

        /// <summary>
        /// Reads the optional RTMX trailer positioned right after the anim
        /// block. Absence, truncation, or any malformed content decodes as
        /// <see cref="ModifierMask.Unknown"/> - a bad trailer must never fail
        /// a decode that would previously have succeeded.
        /// </summary>
        private static int TryReadExtensions(BinaryReader r, int totalLength)
        {
            int modifiers = ModifierMask.Unknown;
            try
            {
                long pos = r.BaseStream.Position;
                if (totalLength - pos < 4) return modifiers;
                for (int i = 0; i < 4; i++)
                    if (r.ReadByte() != ExtMagic[i]) return modifiers;

                while (totalLength - r.BaseStream.Position >= 3)
                {
                    byte type = r.ReadByte();
                    ushort len = r.ReadUInt16();
                    if (totalLength - r.BaseStream.Position < len)
                        return modifiers; // truncated record

                    if (type == ExtTypeModifiers && len == 4)
                    {
                        int value = r.ReadInt32();
                        if (ModifierMask.IsKnown(value)) modifiers = value;
                    }
                    else
                    {
                        r.BaseStream.Position += len; // unknown type - skip
                    }
                }
            }
            catch
            {
                // Malformed trailer - treat as absent.
            }
            return modifiers;
        }

        // ── Collection API ────────────────────────────────────────────────────
        //
        // RTMC1 binary layout (before Deflate):
        //   [4]  magic "RTMC"
        //   [1]  version = 0x01
        //   [4]  count N   int32
        //   N ×  [4] blobLength + RTM3 binary blob (uncompressed)
        //
        // The collection is Deflate-compressed as a whole, so repeated clips
        // and similar motion patterns across rooms compress well together.

        private static readonly byte[] MagicCollection =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'C' };
        private const byte VersionCollection = 0x01;

        public static string EncodeCollection(IEnumerable<RecordedRoom> rooms)
        {
            var list = rooms.ToList();
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(MagicCollection);
                w.Write(VersionCollection);
                w.Write(list.Count);
                foreach (var room in list)
                {
                    byte[] blob = WriteBinary(room);
                    w.Write(blob.Length);
                    w.Write(blob);
                }
            }
            string result = Convert.ToBase64String(Compress.CompressData(ms.ToArray()));
            Log.LogInfo($"[RTMC1] Encoded {list.Count} rooms -> {result.Length} chars");
            return result;
        }

        public static List<RecordedRoom>? DecodeCollection(string encoded)
        {
            try
            {
                return ReadCollection(Compress.DecompressData(Convert.FromBase64String(encoded)));
            }
            catch (Exception ex)
            {
                Log.LogError($"[RTMC1] Decode failed: {ex.Message}");
                return null;
            }
        }

        // Parses a decompressed RTMC byte array. Extracted so DecodeShareString
        // can call it without going through base64 -> decompress again.
        private static List<RecordedRoom> ReadCollection(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8);

            for (int i = 0; i < 4; i++)
                if (r.ReadByte() != MagicCollection[i])
                    throw new Exception("Bad RTMC magic");

            byte ver = r.ReadByte();
            if (ver != VersionCollection)
                throw new Exception($"Unsupported RTMC version 0x{ver:X2}");

            int count = r.ReadInt32();
            if (count < 0 || count > 100000)
                throw new Exception($"Implausible count: {count}");

            var rooms = new List<RecordedRoom>(count);
            for (int i = 0; i < count; i++)
            {
                int blobLen = r.ReadInt32();
                byte[] blob = r.ReadBytes(blobLen);
                rooms.Add(ReadBinary(blob));
            }

            Log.LogInfo($"[RTMC1] Decoded {rooms.Count} rooms");
            return rooms;
        }

        // Works for a single RTM3 string, a single RTMC string, and any number
        // of either format concatenated (e.g. a .rtmc.txt file pasted as text,
        // or two clipboard strings merged).
        public static List<RecordedRoom> DecodeShareString(string str)
        {
            var result = new List<RecordedRoom>();

            if (str.Length > MaxShareStringLength)
            {
                Log.LogWarning("[ShareEncoder] Share string too large -- ignoring");
                return result;
            }

            string raw = Regex.Replace(str, @"\s+", "");
            if (raw.Length == 0) return result;

            // Split after each padding '=' that is immediately followed by a
            // base64 character. The lookbehind keeps the '=' with the blob before it.
            string[] chunks = Regex.Split(raw, @"(?<==)(?=[A-Za-z0-9+/])");

            foreach (string chunk in chunks)
            {
                if (chunk.Length == 0) continue;
                try
                {
                    byte[] decompressed = Compress.DecompressData(Convert.FromBase64String(chunk));

                    if (decompressed.Length >= 4 &&
                        decompressed[0] == MagicCollection[0] &&
                        decompressed[1] == MagicCollection[1] &&
                        decompressed[2] == MagicCollection[2] &&
                        decompressed[3] == MagicCollection[3])
                    {
                        result.AddRange(ReadCollection(decompressed));
                    }
                    else if (decompressed.Length >= 4 &&
                             decompressed[0] == Magic[0] &&
                             decompressed[1] == Magic[1] &&
                             decompressed[2] == Magic[2] &&
                             decompressed[3] == Magic[3])
                    {
                        result.Add(ReadBinary(decompressed));
                    }
                    else
                    {
                        Log.LogWarning("[ShareEncoder] Unknown magic in chunk -- skipping");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[ShareEncoder] Chunk decode failed: {ex.Message}");
                }
            }

            return result;
        }

    }
}

