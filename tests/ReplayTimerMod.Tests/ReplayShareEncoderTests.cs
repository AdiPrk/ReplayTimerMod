using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class ReplayShareEncoderTests
    {
        private const float Quantum = 1f / 100f; // FrameCodec.PosScale

        private static void AssertRoomsEquivalent(RecordedRoom expected,
            RecordedRoom? actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Key, actual!.Key);
            Assert.Equal(expected.TotalTime, actual.TotalTime, 4);
            Assert.Equal(expected.FrameCount, actual.FrameCount);

            for (int i = 0; i < expected.FrameCount; i++)
            {
                // Positions quantize to 1/100 world unit.
                Assert.True(Math.Abs(expected.Frames[i].x - actual.Frames[i].x)
                    <= Quantum / 2 + 1e-4, $"x mismatch at frame {i}");
                Assert.True(Math.Abs(expected.Frames[i].y - actual.Frames[i].y)
                    <= Quantum / 2 + 1e-4, $"y mismatch at frame {i}");
                Assert.Equal(expected.Frames[i].facingRight,
                    actual.Frames[i].facingRight);
                Assert.Equal(expected.Frames[i].animClip ?? "",
                    actual.Frames[i].animClip);
                // animFrame is only meaningful alongside a clip; the decoder
                // zeroes it for clipless frames.
                if (!string.IsNullOrEmpty(expected.Frames[i].animClip))
                    Assert.Equal(Math.Min(expected.Frames[i].animFrame, 255),
                        actual.Frames[i].animFrame);
            }
        }

        // ── Round trips ─────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_TypicalRoom()
        {
            var room = Rooms.Room(frames: 200, time: 6.66f);
            AssertRoomsEquivalent(room, ReplayShareEncoder.Decode(
                ReplayShareEncoder.Encode(room)));
        }

        [Fact]
        public void RoundTrip_SingleFrame()
        {
            var room = Rooms.Room(frames: 1, time: 0.03f);
            AssertRoomsEquivalent(room, ReplayShareEncoder.Decode(
                ReplayShareEncoder.Encode(room)));
        }

        [Fact]
        public void RoundTrip_NoAnimationData()
        {
            var frames = new FrameData[10];
            for (int i = 0; i < 10; i++)
                frames[i] = new FrameData { x = i, y = 0, animClip = "" };
            var room = new RecordedRoom(Rooms.Key(), 0.33f, frames);

            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            AssertRoomsEquivalent(room, decoded);
        }

        [Fact]
        public void RoundTrip_NullAnimClips_DecodeAsEmpty()
        {
            var frames = new FrameData[3];
            for (int i = 0; i < 3; i++)
                frames[i] = new FrameData { x = i, y = i, animClip = null! };
            var room = new RecordedRoom(Rooms.Key(), 0.1f, frames);

            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            Assert.NotNull(decoded);
            Assert.All(decoded!.Frames, f => Assert.Equal("", f.animClip));
        }

        [Fact]
        public void RoundTrip_MixedClipAndNoClipFrames()
        {
            var frames = new FrameData[6];
            for (int i = 0; i < 6; i++)
                frames[i] = new FrameData
                {
                    x = i, y = i,
                    animClip = i % 2 == 0 ? "Run" : "",
                    animFrame = i
                };
            var room = new RecordedRoom(Rooms.Key(), 0.2f, frames);
            AssertRoomsEquivalent(room, ReplayShareEncoder.Decode(
                ReplayShareEncoder.Encode(room)));
        }

        [Fact]
        public void RoundTrip_AnimFrameSaturatesAt255()
        {
            var frames = new[] { new FrameData
                { x = 1, y = 1, animClip = "Run", animFrame = 9999 } };
            var room = new RecordedRoom(Rooms.Key(), 0.03f, frames);

            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            Assert.Equal(255, decoded!.Frames[0].animFrame);
        }

        [Fact]
        public void RoundTrip_FacingBits_ExactPattern()
        {
            // 17 frames (crosses a byte boundary) with an irregular pattern.
            var frames = new FrameData[17];
            for (int i = 0; i < 17; i++)
                frames[i] = new FrameData
                    { x = i, y = 0, facingRight = (i * i) % 3 == 0 };
            var room = new RecordedRoom(Rooms.Key(), 0.5f, frames);

            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            for (int i = 0; i < 17; i++)
                Assert.Equal(frames[i].facingRight, decoded!.Frames[i].facingRight);
        }

        [Fact]
        public void RoundTrip_ManyUniqueClips_DoesNotCorruptBlob()
        {
            // Regression: >255 unique clips used to truncate the count byte
            // ((byte)300 == 44) and produce an undecodable blob. Overflow
            // clips now share table slot 254.
            var frames = new FrameData[300];
            for (int i = 0; i < 300; i++)
                frames[i] = new FrameData
                    { x = i * 0.1f, y = 0, animClip = "Clip" + i, animFrame = 1 };
            var room = new RecordedRoom(Rooms.Key(), 10f, frames);

            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            Assert.NotNull(decoded);
            Assert.Equal(300, decoded!.FrameCount);
            // First 254 clips keep their identity; overflow shares slot 254.
            Assert.Equal("Clip0", decoded.Frames[0].animClip);
            Assert.Equal("Clip253", decoded.Frames[253].animClip);
            Assert.Equal("Clip254", decoded.Frames[254].animClip);
            Assert.Equal("Clip254", decoded.Frames[299].animClip);
        }

        // ── Trailing bytes ──────────────────────────────────────────────────

        [Fact]
        public void Decode_IgnoresTrailingBytes()
        {
            // Older builds appended an "RTMX" modifier section after the anim
            // block; those blobs must keep decoding.
            var room = Rooms.Room(frames: 20, time: 1f);
            byte[] raw = Inflate(ReplayShareEncoder.Encode(room));
            byte[] trailer = { (byte)'R', (byte)'T', (byte)'M', (byte)'X',
                0x01, 0x04, 0x00, 0x05, 0x00, 0x00, 0x00 };

            AssertRoomsEquivalent(room,
                ReplayShareEncoder.Decode(Deflate(raw.Concat(trailer).ToArray())));
        }

        // ── Malformed input ─────────────────────────────────────────────────

        [Fact]
        public void Decode_Garbage_ReturnsNull()
        {
            Assert.Null(ReplayShareEncoder.Decode("not base64 at all!!!"));
            Assert.Null(ReplayShareEncoder.Decode(
                Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 })));
        }

        [Fact]
        public void Decode_WrongMagic_ReturnsNull()
        {
            byte[] raw = Inflate(ReplayShareEncoder.Encode(Rooms.Room(frames: 5)));
            raw[0] = (byte)'X';
            Assert.Null(ReplayShareEncoder.Decode(Deflate(raw)));
        }

        [Fact]
        public void Decode_UnsupportedVersion_ReturnsNull()
        {
            byte[] raw = Inflate(ReplayShareEncoder.Encode(Rooms.Room(frames: 5)));
            raw[4] = 0x63;
            Assert.Null(ReplayShareEncoder.Decode(Deflate(raw)));
        }

        [Fact]
        public void Decode_ImplausibleFrameCount_ReturnsNull()
        {
            byte[] raw = Inflate(ReplayShareEncoder.Encode(Rooms.Room(frames: 5)));

            // frame count sits after magic+version and 3 length-prefixed strings.
            int p = 5;
            for (int i = 0; i < 3; i++)
                p += 2 + BitConverter.ToUInt16(raw, p);
            p += 4; // totalTime
            BitConverter.GetBytes(1_000_000).CopyTo(raw, p);

            Assert.Null(ReplayShareEncoder.Decode(Deflate(raw)));
        }

        // ── Collections & share strings ─────────────────────────────────────

        [Fact]
        public void Collection_RoundTrips()
        {
            var rooms = new List<RecordedRoom>
            {
                Rooms.Room(frames: 30, time: 1f, seed: 1),
                Rooms.Room(frames: 60, time: 2f, seed: 2,
                    key: Rooms.Key("Dust_05", "Dust_04", "Dust_06")),
                Rooms.Room(frames: 90, time: 3f, seed: 3),
            };

            var decoded = ReplayShareEncoder.DecodeCollection(
                ReplayShareEncoder.EncodeCollection(rooms));
            Assert.NotNull(decoded);
            Assert.Equal(3, decoded!.Count);
            for (int i = 0; i < 3; i++)
                AssertRoomsEquivalent(rooms[i], decoded[i]);
        }

        [Fact]
        public void DecodeShareString_SingleReplay()
        {
            var room = Rooms.Room(frames: 25, time: 0.8f);
            var result = ReplayShareEncoder.DecodeShareString(
                ReplayShareEncoder.Encode(room));
            Assert.Single(result);
            AssertRoomsEquivalent(room, result[0]);
        }

        [Fact]
        public void DecodeShareString_ConcatenatedBlobsAndWhitespace()
        {
            // Whitespace is stripped before splitting, so concatenated blobs
            // are only separable at base64 '=' padding boundaries. Pick frame
            // counts whose encodes actually end in '=' (length % 3 != 0).
            string a = EncodeEndingInPadding(seed: 1);
            string b = EncodeEndingInPadding(seed: 2);
            var coll = ReplayShareEncoder.EncodeCollection(
                new[] { Rooms.Room(frames: 25, seed: 3) });

            string text = "  " + a + "\r\n" + b + "\n\t " + coll + " ";
            var result = ReplayShareEncoder.DecodeShareString(text);

            Assert.Equal(3, result.Count);
        }

        private static string EncodeEndingInPadding(int seed)
        {
            for (int frames = 20; frames < 60; frames++)
            {
                string encoded = ReplayShareEncoder.Encode(
                    Rooms.Room(frames: frames, seed: seed));
                if (encoded.EndsWith("=")) return encoded;
            }
            throw new InvalidOperationException(
                "no padded encode found — statistically impossible");
        }

        [Fact]
        public void DecodeShareString_JunkChunk_IsSkippedOthersSurvive()
        {
            var room = Rooms.Room(frames: 20);
            string good = ReplayShareEncoder.Encode(room);
            // Junk that still ends with '=' so the splitter treats it as a chunk.
            string junk = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("this is not a replay at all, sorry"));

            var result = ReplayShareEncoder.DecodeShareString(junk + good);
            Assert.Single(result);
        }

        [Fact]
        public void DecodeShareString_EmptyAndOversized_ReturnEmpty()
        {
            Assert.Empty(ReplayShareEncoder.DecodeShareString(""));
            Assert.Empty(ReplayShareEncoder.DecodeShareString("   \n\t "));
            Assert.Empty(ReplayShareEncoder.DecodeShareString(
                new string('A', 16 * 1024 * 1024 + 1)));
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        // Compress is internal to the product but the sources compile into
        // this test assembly, so it's directly callable.
        private static byte[] Inflate(string base64) =>
            Compress.DecompressData(Convert.FromBase64String(base64));

        private static string Deflate(byte[] raw) =>
            Convert.ToBase64String(Compress.CompressData(raw));
    }
}
