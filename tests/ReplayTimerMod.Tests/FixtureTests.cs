using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    // Regenerate after an intentional format change: RTM_WRITE_FIXTURES=1 dotnet test
    public class FixtureTests
    {
        private sealed record FixtureSpec(string Name, string Kind,
            RecordedRoom[] Rooms);

        private static FixtureSpec[] Specs() => new[]
        {
            new FixtureSpec("basic", "rtm3", new[]
            {
                Tests.Rooms.Room(frames: 90, time: 3f, seed: 101),
            }),
            new FixtureSpec("single-frame", "rtm3", new[]
            {
                Tests.Rooms.Room(frames: 1, time: 0.033f, seed: 303),
            }),
            new FixtureSpec("collection", "rtmc", new[]
            {
                Tests.Rooms.Room(frames: 30, time: 1f, seed: 404),
                Tests.Rooms.Room(frames: 45, time: 1.5f, seed: 505,
                    key: Tests.Rooms.Key("Dust_05", "Dust_04", "Dust_06")),
            }),
        };

        private static string FixturePath =>
            Path.Combine(RepoPaths.SharedDir, "rtm3-fixtures.json");

        [Fact]
        public void Fixtures_ExistAndAreCurrent_OrRegenerate()
        {
            if (Environment.GetEnvironmentVariable("RTM_WRITE_FIXTURES") == "1")
            {
                WriteFixtures();
            }

            Assert.True(File.Exists(FixturePath),
                "tests/shared/rtm3-fixtures.json missing — run with " +
                "RTM_WRITE_FIXTURES=1 to generate it.");
        }

        [Fact]
        public void CommittedFixtures_DecodeWithMatchingMetadata()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
            int seen = 0;

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                seen++;
                string name = f.GetProperty("name").GetString()!;
                string kind = f.GetProperty("kind").GetString()!;
                string base64 = f.GetProperty("base64").GetString()!;

                if (kind == "rtm3")
                {
                    var room = ReplayShareEncoder.Decode(base64);
                    Assert.True(room != null, $"fixture '{name}' failed to decode");
                    Assert.Equal(f.GetProperty("time").GetSingle(),
                        room!.TotalTime, 3);
                    Assert.Equal(f.GetProperty("frames").GetInt32(),
                        room.FrameCount);
                    Assert.Equal(f.GetProperty("scene").GetString(),
                        room.Key.SceneName);
                }
                else if (kind == "rtmc")
                {
                    var rooms = ReplayShareEncoder.DecodeCollection(base64);
                    Assert.True(rooms != null, $"fixture '{name}' failed to decode");
                    Assert.Equal(f.GetProperty("count").GetInt32(), rooms!.Count);
                }
            }

            Assert.True(seen >= 3, "fixture file unexpectedly small");
        }

        private static void WriteFixtures()
        {
            var list = new List<object>();
            foreach (var spec in Specs())
            {
                if (spec.Kind == "rtm3")
                {
                    var room = spec.Rooms[0];
                    list.Add(new
                    {
                        name = spec.Name,
                        kind = spec.Kind,
                        base64 = ReplayShareEncoder.Encode(room),
                        time = room.TotalTime,
                        frames = room.FrameCount,
                        scene = room.Key.SceneName,
                        entryFrom = room.Key.EntryFromScene,
                        exitTo = room.Key.ExitToScene,
                    });
                }
                else
                {
                    list.Add(new
                    {
                        name = spec.Name,
                        kind = spec.Kind,
                        base64 = ReplayShareEncoder.EncodeCollection(spec.Rooms),
                        count = spec.Rooms.Length,
                    });
                }
            }

            File.WriteAllText(FixturePath, JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
