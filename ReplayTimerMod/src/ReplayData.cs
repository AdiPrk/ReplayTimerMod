namespace ReplayTimerMod
{
    // A single captured gameplay frame.
    // We record only when LoadRemover.ShouldTick() is true, so these frames
    // represent only actual gameplay time — no loads, cutscenes, or pauses.
    //
    // deltaTime is stored per frame because playback frame rate may differ from
    // recording frame rate. GhostPlayback uses it to interpolate correctly.
    public struct FrameData
    {
        public float x;
        public float y;
        public bool facingRight; // derived from transform.localScale.x > 0
        public float deltaTime;  // unscaled LR delta for this frame
    }

    // A completed, validated recording for a single room run.
    // Only created by FrameRecorder.FinishRecording() — never constructed directly.
    public class RecordedRoom
    {
        public string SceneName { get; }
        public float TotalTime { get; }   // total load-removed time in seconds
        public FrameData[] Frames { get; }
        public int FrameCount => Frames.Length;

        public RecordedRoom(string sceneName, float totalTime, FrameData[] frames)
        {
            SceneName = sceneName;
            TotalTime = totalTime;
            Frames = frames;
        }
    }
}