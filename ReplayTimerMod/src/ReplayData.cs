namespace ReplayTimerMod
{
    public struct FrameData
    {
        public float x;
        public float y;
        public bool facingRight;
        public float deltaTime;

        // Animation state — populated at record time, empty string on old recordings.
        public string animClip;   // tk2dSpriteAnimationClip.name
        public int animFrame;     // integer frame index within that clip
    }

    // Uniquely identifies a route through a room.
    // EntryFromScene (not EntryGate) is used as the entry discriminator —
    // it's unambiguous: the same gate name may appear on both sides of a
    // boundary, but the source scene is always unique.
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
            $"{SceneName}[{EntryFromScene}→{ExitToScene}]";

        public override bool Equals(object? obj) =>
            obj is RoomKey other &&
            SceneName == other.SceneName &&
            EntryFromScene == other.EntryFromScene &&
            ExitToScene == other.ExitToScene;

        public override int GetHashCode() =>
            System.HashCode.Combine(SceneName, EntryFromScene, ExitToScene);
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