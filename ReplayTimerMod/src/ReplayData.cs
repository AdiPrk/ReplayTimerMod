namespace ReplayTimerMod
{
    public struct FrameData
    {
        public float x;
        public float y;
        public bool facingRight;

        // Animation state - populated at record time, empty string when unavailable.
        public string animClip;   // tk2dSpriteAnimationClip.name
        public int animFrame;     // integer frame index within that clip
    }

    // Uniquely identifies a route through a room.
    // EntryFromScene (not gate name) is the entry discriminator - the same gate
    // name can appear on both sides of a boundary, but the source scene is unique.
    public readonly struct RoomKey
    {
        public readonly string SceneName;
        public readonly string EntryFromScene;
        public readonly string ExitToScene;

        public RoomKey(string sceneName, string entryFromScene, string exitToScene)
        {
            SceneName = sceneName;
            EntryFromScene = entryFromScene;
            ExitToScene = exitToScene;
        }

        public override string ToString() =>
            $"{SceneName}[{EntryFromScene}->{ExitToScene}]";

        public override bool Equals(object? obj) =>
            obj is RoomKey other &&
            SceneName == other.SceneName &&
            EntryFromScene == other.EntryFromScene &&
            ExitToScene == other.ExitToScene;

        public override int GetHashCode() =>
            System.HashCode.Combine(SceneName, EntryFromScene, ExitToScene);
    }

    public static class TimeUtil
    {
        public static string Format(float t)
        {
            int ms = (int)(t * 100) % 100;
            int s = (int)t % 60;
            int min = (int)t / 60;
            return $"{min}:{s:00}.{ms:00}";
        }
    }

    public class RecordedRoom
    {
        public RoomKey Key { get; }
        public float TotalTime { get; }
        public FrameData[] Frames { get; }
        public int FrameCount => Frames.Length;

        public RecordedRoom(RoomKey key, float totalTime, FrameData[] frames)
        {
            Key = key;
            TotalTime = totalTime;
            Frames = frames;
        }
    }
}