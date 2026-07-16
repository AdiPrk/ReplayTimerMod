using System;
using System.IO;
using System.Text;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class FrameCodecTests
    {
        // ── SVLQ (ZigZag + ULEB128) ─────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(63)]
        [InlineData(64)]         // first two-byte value
        [InlineData(-64)]
        [InlineData(-65)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(32767)]
        [InlineData(-32768)]
        [InlineData(131072)]     // max 2nd-order residual magnitude (4×short)
        [InlineData(-131072)]
        [InlineData(1000000)]
        [InlineData(-1000000)]
        public void Svlq_RoundTrips(int value)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            FrameCodec.WriteSVLQ(w, value);
            w.Flush();

            ms.Position = 0;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            Assert.Equal(value, FrameCodec.ReadSVLQ(r));
        }

        [Fact]
        public void Svlq_SmallMagnitudes_AreOneByte()
        {
            for (int v = -64; v <= 63; v++)
            {
                using var ms = new MemoryStream();
                using var w = new BinaryWriter(ms, Encoding.UTF8);
                FrameCodec.WriteSVLQ(w, v);
                w.Flush();
                Assert.Equal(1, ms.Length);
            }
        }

        [Fact]
        public void Svlq_RoundTrips_RandomFuzz()
        {
            var rng = new Random(42);
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            var values = new int[5000];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = rng.Next(-200000, 200001);
                FrameCodec.WriteSVLQ(w, values[i]);
            }
            w.Flush();

            ms.Position = 0;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            for (int i = 0; i < values.Length; i++)
                Assert.Equal(values[i], FrameCodec.ReadSVLQ(r));
        }

        // ── 2nd-order DPCM ──────────────────────────────────────────────────

        private static FrameData[] Frames(params float[] xs)
        {
            var frames = new FrameData[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                frames[i] = new FrameData { x = xs[i], y = -xs[i] };
            return frames;
        }

        [Fact]
        public void Dpcm_EmptyInput_RoundTrips()
        {
            byte[] stream = FrameCodec.Encode2ndOrder(Frames(), getX: true);
            Assert.Empty(stream);
            Assert.Empty(FrameCodec.Decode2ndOrder(stream, 0));
        }

        [Theory]
        [InlineData(new float[] { 5.25f })]
        [InlineData(new float[] { 5.25f, 5.30f })]
        [InlineData(new float[] { 0f, 0f, 0f, 0f })]
        [InlineData(new float[] { 1f, 2f, 3f, 4f, 5f })]          // constant velocity
        [InlineData(new float[] { -10f, 3.5f, 200.02f, -37.9f })] // erratic
        public void Dpcm_RoundTrips(float[] xs)
        {
            var frames = Frames(xs);
            byte[] stream = FrameCodec.Encode2ndOrder(frames, getX: true);
            short[] decoded = FrameCodec.Decode2ndOrder(stream, frames.Length);

            for (int i = 0; i < frames.Length; i++)
                Assert.Equal(FrameCodec.ToShort(xs[i]), decoded[i]);
        }

        [Fact]
        public void Dpcm_XAndYStreams_AreIndependent()
        {
            var frames = Frames(1f, 2f, 4f, 8f);
            short[] xs = FrameCodec.Decode2ndOrder(
                FrameCodec.Encode2ndOrder(frames, getX: true), frames.Length);
            short[] ys = FrameCodec.Decode2ndOrder(
                FrameCodec.Encode2ndOrder(frames, getX: false), frames.Length);

            for (int i = 0; i < frames.Length; i++)
            {
                Assert.Equal(FrameCodec.ToShort(frames[i].x), xs[i]);
                Assert.Equal(FrameCodec.ToShort(frames[i].y), ys[i]);
            }
        }

        [Fact]
        public void Dpcm_ConstantVelocity_IsMaximallyCompact()
        {
            // anchor (2 bytes) + first delta (1 byte) + zero residuals (1 byte each)
            var frames = Frames(0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
            byte[] stream = FrameCodec.Encode2ndOrder(frames, getX: true);
            Assert.Equal(2 + 1 + (frames.Length - 2), stream.Length);
        }

        [Fact]
        public void Dpcm_RandomWalk_RoundTrips()
        {
            var rng = new Random(7);
            var xs = new float[2000];
            float x = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                x += (float)(rng.NextDouble() - 0.5) * 2f;
                xs[i] = x;
            }

            var frames = Frames(xs);
            short[] decoded = FrameCodec.Decode2ndOrder(
                FrameCodec.Encode2ndOrder(frames, getX: true), frames.Length);
            for (int i = 0; i < xs.Length; i++)
                Assert.Equal(FrameCodec.ToShort(xs[i]), decoded[i]);
        }

        // ── ToShort quantization ────────────────────────────────────────────

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(1f, 100)]
        [InlineData(-1f, -100)]
        [InlineData(1.234f, 123)]
        [InlineData(1.235f, 124)]        // rounds (banker's/half-even at .5 exact)
        [InlineData(400f, short.MaxValue)]   // clamps high (40000 > 32767)
        [InlineData(-400f, short.MinValue)]  // clamps low
        [InlineData(1e9f, short.MaxValue)]
        [InlineData(-1e9f, short.MinValue)]
        public void ToShort_ScalesAndClamps(float world, short expected) =>
            Assert.Equal(expected, FrameCodec.ToShort(world));

        // ── Length-prefixed strings ─────────────────────────────────────────

        [Theory]
        [InlineData("")]
        [InlineData("Bone_East_10")]
        [InlineData("Ünïcödé シーン 🎮")]
        public void String_RoundTrips(string s)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8);
            FrameCodec.WriteString(w, s);
            w.Flush();

            ms.Position = 0;
            using var r = new BinaryReader(ms, Encoding.UTF8);
            Assert.Equal(s, FrameCodec.ReadString(r));
        }
    }
}
