using System;
using System.IO;
using System.Text;

namespace ReplayTimerMod
{
    // Shared binary primitives used by DataStore (on-disk format) and
    // ReplayShareEncoder (clipboard format). Both formats use the same
    // position scaling, 2nd-order DPCM streams, and SVLQ encoding.
    internal static class FrameCodec
    {
        // World-space float -> fixed-point int16 (1 unit = 100 ticks).
        public const float PosScale = 100f;

        // ── 2nd-order DPCM ────────────────────────────────────────────────────
        // Encodes positions as: absolute anchor, 1st-order delta for frame 1,
        // then 2nd-order residuals (acceleration) for frames 2+.
        // During constant-velocity motion residuals are 0 -> single 0x00 bytes
        // -> maximally compressible.

        public static byte[] Encode2ndOrder(FrameData[] frames, bool getX)
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

        public static short[] Decode2ndOrder(byte[] stream, int n)
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

        // ── SVLQ = ZigZag(n) -> ULEB128 ───────────────────────────────────────
        // ZigZag maps signed -> unsigned preserving small magnitudes:
        //   0->0  -1->1  1->2  -2->3  …
        // ULEB128 encodes unsigned ints 7 bits per byte, high bit = "more follows".
        // Values 0–127 fit in one byte.

        public static void WriteSVLQ(BinaryWriter w, int v)
        {
            uint u = v >= 0 ? (uint)(v << 1) : (uint)((-v << 1) - 1);
            do
            {
                byte b = (byte)(u & 0x7F);
                u >>= 7;
                if (u != 0) b |= 0x80;
                w.Write(b);
            } while (u != 0);
        }

        public static int ReadSVLQ(BinaryReader r)
        {
            uint u = 0; int shift = 0; byte b;
            do { b = r.ReadByte(); u |= (uint)(b & 0x7F) << shift; shift += 7; }
            while ((b & 0x80) != 0);
            return (u & 1) == 0 ? (int)(u >> 1) : -(int)(u >> 1) - 1;
        }

        // ── Length-prefixed UTF-8 string ──────────────────────────────────────

        public static void WriteString(BinaryWriter w, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            w.Write((ushort)b.Length);
            w.Write(b);
        }

        public static string ReadString(BinaryReader r) =>
            Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16()));

        // ── World-space float -> int16 ─────────────────────────────────────────

        public static short ToShort(float world) =>
            (short)Math.Max(short.MinValue,
                   Math.Min(short.MaxValue, (int)Math.Round(world * PosScale)));
    }
}