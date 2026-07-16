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

        [Fact]
        public void Decode_RawBytesOverload_MatchesStringOverload()
        {
            var room = Rooms.Room(frames: 50, time: 1.7f);
            string encoded = ReplayShareEncoder.Encode(room);
            byte[] compressed = Convert.FromBase64String(encoded);

            AssertRoomsEquivalent(room, ReplayShareEncoder.Decode(compressed));
        }

        // ── Modifier trailer (RTMX) ─────────────────────────────────────────

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(0b1011)]
        [InlineData(0x7FFFFFFF)]
        public void Modifiers_KnownMask_RoundTrips(int mask)
        {
            var room = Rooms.Room(frames: 30, time: 1f, modifiers: mask);
            var decoded = ReplayShareEncoder.Decode(ReplayShareEncoder.Encode(room));
            Assert.Equal(mask, decoded!.Modifiers);
        }

        [Fact]
        public void Modifiers_UnknownMask_WritesNoTrailer_DecodesUnknown()
        {
            var room = Rooms.Room(frames: 30, time: 1f,
                modifiers: ModifierMask.Unknown);
            string encoded = ReplayShareEncoder.Encode(room);

            var decoded = ReplayShareEncoder.Decode(encoded);
            Assert.Equal(ModifierMask.Unknown, decoded!.Modifiers);

            // And the blob is byte-identical to a mask-free encode (legacy
            // data must round-trip unchanged).
            var legacyTwin = new RecordedRoom(room.Key, room.TotalTime, room.Frames);
            Assert.Equal(ReplayShareEncoder.Encode(legacyTwin), encoded);
        }

        [Fact]
        public void Modifiers_MaskedBlob_IsDecodableByTrailerIgnorantReader()
        {
            // Old clients read to the end of the anim block and stop; the
            // trailer must strictly append. Verify a masked and an unmasked
            // encode share an identical prefix.
            var frames = Rooms.Room(frames: 40, time: 2f).Frames;
            var plain = new RecordedRoom(Rooms.Key(), 2f, frames);
            var masked = new RecordedRoom(Rooms.Key(), 2f, frames, 0b101);

            byte[] plainRaw = Inflate(ReplayShareEncoder.Encode(plain));
            byte[] maskedRaw = Inflate(ReplayShareEncoder.Encode(masked));

            Assert.True(maskedRaw.Length > plainRaw.Length);
            Assert.Equal(plainRaw, maskedRaw.Take(plainRaw.Length).ToArray());

            // Trailer = "RTMX" + type 0x01 + len 4 LE + int32 LE mask.
            byte[] trailer = maskedRaw.Skip(plainRaw.Length).ToArray();
            Assert.Equal(new byte[] { (byte)'R', (byte)'T', (byte)'M', (byte)'X',
                0x01, 0x04, 0x00, 0x05, 0x00, 0x00, 0x00 }, trailer);
        }

        [Fact]
        public void Modifiers_TruncatedTrailer_DecodesAsUnknown()
        {
            var room = Rooms.Room(frames: 20, time: 1f, modifiers: 7);
            byte[] raw = Inflate(ReplayShareEncoder.Encode(room));

            // Chop the trailer mid-record (leave "RTMX" + type but cut payload).
            byte[] truncated = raw.Take(raw.Length - 4).ToArray();
            var decoded = ReplayShareEncoder.Decode(Deflate(truncated));
            Assert.NotNull(decoded);
            Assert.Equal(ModifierMask.Unknown, decoded!.Modifiers);
        }

        [Fact]
        public void Modifiers_UnknownTlvType_IsSkipped_MaskStillRead()
        {
            var room = Rooms.Room(frames: 20, time: 1f);
            byte[] raw = Inflate(ReplayShareEncoder.Encode(room));

            // Hand-append: RTMX, unknown type 0x7E (3-byte payload), then the
            // modifiers record.
            using var ms = new MemoryStream();
            ms.Write(raw, 0, raw.Length);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(new[] { (byte)'R', (byte)'T', (byte)'M', (byte)'X' });
                w.Write((byte)0x7E);
                w.Write((ushort)3);
                w.Write(new byte[] { 1, 2, 3 });
                w.Write((byte)0x01);
                w.Write((ushort)4);
                w.Write(42);
            }

            var decoded = ReplayShareEncoder.Decode(Deflate(ms.ToArray()));
            Assert.Equal(42, decoded!.Modifiers);
        }

        [Fact]
        public void Modifiers_NegativeValueInTrailer_DecodesAsUnknown()
        {
            var room = Rooms.Room(frames: 20, time: 1f);
            byte[] raw = Inflate(ReplayShareEncoder.Encode(room));

            using var ms = new MemoryStream();
            ms.Write(raw, 0, raw.Length);
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(new[] { (byte)'R', (byte)'T', (byte)'M', (byte)'X' });
                w.Write((byte)0x01);
                w.Write((ushort)4);
                w.Write(-5);
            }

            var decoded = ReplayShareEncoder.Decode(Deflate(ms.ToArray()));
            Assert.Equal(ModifierMask.Unknown, decoded!.Modifiers);
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
                Rooms.Room(frames: 60, time: 2f, seed: 2, modifiers: 5,
                    key: Rooms.Key("Dust_05", "Dust_04", "Dust_06")),
                Rooms.Room(frames: 90, time: 3f, seed: 3),
            };

            var decoded = ReplayShareEncoder.DecodeCollection(
                ReplayShareEncoder.EncodeCollection(rooms));
            Assert.NotNull(decoded);
            Assert.Equal(3, decoded!.Count);
            for (int i = 0; i < 3; i++)
                AssertRoomsEquivalent(rooms[i], decoded[i]);
            Assert.Equal(5, decoded[1].Modifiers);
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
