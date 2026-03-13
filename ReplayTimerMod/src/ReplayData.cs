namespace ReplayTimerMod
{
    // A single captured gameplay frame.
    // Only recorded when LoadRemover.ShouldTick() is true.
    public struct FrameData
    {
        public float x;
        public float y;
        public bool facingRight;
        public float deltaTime;
    }

    // Uniquely identifies a route through a room:
    // the room name, which gate you entered through,
    // and which room you exited to.
    // Two runs are only comparable if all three match.
    public readonly struct RoomKey
    {
        public readonly string SceneName;
        public readonly string EntryGate;
        public readonly string ExitToScene;

        public RoomKey(string sceneName, string entryGate, string exitToScene)
        {
            SceneName = sceneName;
            EntryGate = entryGate;
            ExitToScene = exitToScene;
        }

        public override string ToString() => $"{SceneName}[{EntryGate}→{ExitToScene}]";

        public override bool Equals(object? obj) =>
            obj is RoomKey other &&
            SceneName == other.SceneName &&
            EntryGate == other.EntryGate &&
            ExitToScene == other.ExitToScene;

        public override int GetHashCode() =>
            System.HashCode.Combine(SceneName, EntryGate, ExitToScene);
    }

    // A completed, validated recording for a single room run.
    // Only created by FrameRecorder.FinishRecording().
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