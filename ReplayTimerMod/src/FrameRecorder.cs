using System.Collections.Generic;
using UnityEngine;

namespace ReplayTimerMod
{
    // Records Hornet's position at a fixed LR-time rate of RECORD_FPS,
    // regardless of actual frame rate. At 200fps actual, we still only
    // store 30 frames per second of gameplay - 1800 max for a 60s run.
    public class FrameRecorder
    {
        public const float RECORD_FPS = 30f;
        public const float RECORD_INTERVAL = 1f / RECORD_FPS;

        private readonly List<FrameData> frames = new List<FrameData>();
        private bool recording = false;
        private float accumulatedTime = 0f;

        public void StartRecording()
        {
            frames.Clear();
            recording = true;
            accumulatedTime = 0f;
        }

        public void DiscardRecording()
        {
            frames.Clear();
            recording = false;
            accumulatedTime = 0f;
        }

        public RecordedRoom? FinishRecording(RoomKey key, float totalLRTime)
        {
            if (!recording || frames.Count == 0)
            {
                frames.Clear();
                recording = false;
                return null;
            }

            recording = false;
            var result = new RecordedRoom(key, totalLRTime, frames.ToArray());
            frames.Clear();
            return result;
        }

        // Called every LateUpdate. Accumulates LR time and writes a frame
        // once a full RECORD_INTERVAL has elapsed.
        public void Tick()
        {
            if (!recording) return;
            if (HeroController.instance == null) return;
            if (!LoadRemover.ShouldTick()) return;

            accumulatedTime += Time.unscaledDeltaTime;
            if (accumulatedTime < RECORD_INTERVAL) return;
            accumulatedTime -= RECORD_INTERVAL;

            bool facingRight = HeroController.instance.transform.localScale.x > 0f;
            Vector3 pos = HeroController.instance.transform.position;

            // Capture animation state. tk2dSpriteAnimator.CurrentFrame is an
            // integer index into CurrentClip.frames[] - no normalisation required.
            string clipName = "";
            int clipFrame = 0;
            try
            {
                var anim = HeroController.instance.GetComponent<tk2dSpriteAnimator>();
                if (anim?.CurrentClip != null)
                {
                    clipName = anim.CurrentClip.name;
                    clipFrame = anim.CurrentFrame;
                }
            }
            catch { }

            frames.Add(new FrameData
            {
                x = pos.x,
                y = pos.y,
                facingRight = facingRight,
                animClip = clipName,
                animFrame = clipFrame
            });
        }

        public bool IsRecording => recording;
        public int FrameCount => frames.Count;
    }
}