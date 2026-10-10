using System;
using System.IO;
using ReplayTimerMod;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReplayTimerMod.Tests
{
    internal static class RepoPaths
    {
        public static string SharedDir
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "tests", "shared");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                throw new InvalidOperationException(
                    "Could not locate tests/shared above " + AppContext.BaseDirectory);
            }
        }
    }

    public static class Rooms
    {
        public static RoomKey Key(string scene = "Bone_East_10",
            string from = "Bone_East_09", string to = "Bone_East_11") =>
            new RoomKey(scene, from, to);

        public static RecordedRoom Room(int frames = 90, float time = 3f,
            RoomKey? key = null, int seed = 12345)
        {
            var rng = new Random(seed);
            var data = new FrameData[frames];
            float x = 10f, y = 20f;
            bool facing = true;
            for (int i = 0; i < frames; i++)
            {
                x += (float)(rng.NextDouble() - 0.4) * 0.3f;
                y += (float)(rng.NextDouble() - 0.5) * 0.2f;
                if (rng.Next(20) == 0) facing = !facing;
                data[i] = new FrameData
                {
                    x = x,
                    y = y,
                    facingRight = facing,
                    animClip = i % 30 < 15 ? "Run" : "Idle",
                    animFrame = i % 12
                };
            }
            return new RecordedRoom(key ?? Key(), time, data);
        }

        public sealed class TempDir : IDisposable
        {
            public string Path { get; }

            public TempDir()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "rtm-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); }
                catch { }
            }
        }
    }
}
