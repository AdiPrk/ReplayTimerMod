using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    public static class ReplayShareEncoder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ShareEncoder");

        private static readonly byte[] Magic =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'3' };

        private const byte Version = 0x02;

        private const int MaxFrames = 20000;

        private const int MaxShareStringLength = 16 * 1024 * 1024;

        public static string Encode(RecordedRoom room)
        {
            byte[] binary = WriteBinary(room);
            byte[] compressed = Compress.CompressData(binary);
            string result = Convert.ToBase64String(compressed);
            Log.LogInfo($"[ShareEncoder] RTM3 {room.Key}: {room.FrameCount} frames -> " +
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

        private static byte[] WriteBinary(RecordedRoom room)
        {
            int n = room.FrameCount;
            byte[] xStream = FrameCodec.Encode2ndOrder(room.Frames, getX: true);
            byte[] yStream = FrameCodec.Encode2ndOrder(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            var clipTable = new List<string>();
            var clipLookup = new Dictionary<string, int>();
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
                    if (!clipLookup.TryGetValue(clip, out int idx))
                    {
                        if (clipTable.Count < 255)
                        {
                            idx = clipTable.Count;
                            clipTable.Add(clip);
                        }
                        else
                        {
                            idx = 254;
                        }
                        clipLookup[clip] = idx;
                    }
                    clipIndex[i] = (byte)Math.Min(idx, 254);
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
            }
            return ms.ToArray();
        }

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

            return new RecordedRoom(
                new RoomKey(sceneName, entryFromScene, exitToScene),
                totalTime, frames);
        }

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
            Log.LogInfo($"[ShareEncoder] RTMC1 encoded {list.Count} rooms -> {result.Length} chars");
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
                Log.LogError($"[ShareEncoder] RTMC1 decode failed: {ex.Message}");
                return null;
            }
        }

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

            Log.LogInfo($"[ShareEncoder] RTMC1 decoded {rooms.Count} rooms");
            return rooms;
        }

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
