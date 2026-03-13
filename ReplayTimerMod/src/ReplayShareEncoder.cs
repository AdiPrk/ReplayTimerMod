using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    // ── RTM3 share format ─────────────────────────────────────────────────────
    //
    // Chain: RecordedRoom → RTM3 binary → raw Deflate → Base64 string
    //
    // Binary layout (before Deflate):
    //   [4]     magic "RTM3"
    //   [1]     version = 0x01
    //   [2+N]   sceneName       (uint16 length prefix + UTF-8)
    //   [2+N]   entryFromScene  (uint16 length prefix + UTF-8)
    //   [2+N]   exitToScene     (uint16 length prefix + UTF-8)
    //   [4]     totalTime       float32
    //   [4]     frameCount N    int32
    //   [2+xL]  x 2nd-order SVLQ stream
    //   [2+yL]  y 2nd-order SVLQ stream
    //   [⌈N/8⌉] facing bitfield  MSB-first, 1 = facingRight
    //
    // Share strings intentionally omit animation clip data — ghost playback from
    // an imported string falls back to the diamond renderer.
    // ─────────────────────────────────────────────────────────────────────────

    public static class ReplayShareEncoder
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ShareEncoder");

        private static readonly byte[] MagicRTM3 =
            { (byte)'R', (byte)'T', (byte)'M', (byte)'3' };

        private const byte Version = 0x01;

        // ── Public API ────────────────────────────────────────────────────────

        public static string Encode(RecordedRoom room)
        {
            byte[] binary = WriteBinary(room);
            byte[] compressed = DeflateCompress(binary);
            string result = Convert.ToBase64String(compressed);
            Log.LogInfo($"[RTM3] {room.Key}: {room.FrameCount} frames → " +
                        $"binary={binary.Length}B deflate={compressed.Length}B str={result.Length}ch");
            return result;
        }

        public static RecordedRoom? Decode(string encoded)
        {
            try
            {
                byte[] compressed = Convert.FromBase64String(encoded);
                byte[] raw = DeflateDecompress(compressed);

                if (raw.Length >= 4 &&
                    raw[0] == 'R' && raw[1] == 'T' && raw[2] == 'M' && raw[3] == '3')
                    return ReadBinary(raw);

                Log.LogError($"[ShareEncoder] Unrecognised payload — " +
                             $"got '{(char)raw[0]}{(char)raw[1]}{(char)raw[2]}{(char)raw[3]}'");
                return null;
            }
            catch (Exception ex)
            {
                Log.LogError($"[ShareEncoder] Decode failed: {ex.Message}");
                return null;
            }
        }

        // ── RTM3 encode ───────────────────────────────────────────────────────

        private static byte[] WriteBinary(RecordedRoom room)
        {
            int n = room.FrameCount;
            byte[] xStream = FrameCodec.Encode2ndOrder(room.Frames, getX: true);
            byte[] yStream = FrameCodec.Encode2ndOrder(room.Frames, getX: false);
            int facingBytes = (n + 7) / 8;

            using var ms = new MemoryStream(80 + n * 3 + facingBytes);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(MagicRTM3);
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
            }
            return ms.ToArray();
        }

        // ── RTM3 decode ───────────────────────────────────────────────────────

        private static RecordedRoom ReadBinary(byte[] raw)
        {
            using var ms = new MemoryStream(raw);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            for (int i = 0; i < 4; i++)
                if (r.ReadByte() != MagicRTM3[i])
                    throw new InvalidDataException("Bad RTM3 magic");

            r.ReadByte(); // version

            string sceneName = FrameCodec.ReadString(r);
            string entryFromScene = FrameCodec.ReadString(r);
            string exitToScene = FrameCodec.ReadString(r);
            float totalTime = r.ReadSingle();
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

            return new RecordedRoom(
                new RoomKey(sceneName, entryFromScene, exitToScene),
                totalTime, frames);
        }

        // ── Compression ───────────────────────────────────────────────────────

        private static byte[] DeflateCompress(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var df = new DeflateStream(ms, CompressionLevel.Optimal))
                df.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        private static byte[] DeflateDecompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var output = new MemoryStream();
            using (var df = new DeflateStream(input, CompressionMode.Decompress))
                df.CopyTo(output);
            return output.ToArray();
        }
    }
}