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

        // Cached animator reference
        private tk2dSpriteAnimator? cachedAnim = null;

        public void StartRecording()
        {
            frames.Clear();
            recording = true;
            cachedAnim = null;
            // Pre-fill the accumulator so the very first Tick() captures a frame immediately
            accumulatedTime = RECORD_INTERVAL;
        }

        public void DiscardRecording()
        {
            frames.Clear();
            recording = false;
            accumulatedTime = 0f;
            cachedAnim = null;
        }

        public RecordedRoom? FinishRecording(RoomKey key, float totalLRTime)
        {
            if (!recording || frames.Count == 0)
            {
                frames.Clear();
                recording = false;
                cachedAnim = null;
                return null;
            }

            recording = false;
            cachedAnim = null;
            var result = new RecordedRoom(key, totalLRTime, frames.ToArray());
            frames.Clear();
            return result;
        }

        // Called every LateUpdate if LoadRemover.ShouldTick() 
        public void Tick(bool shouldTick)
        {
            if (!recording) return;
            if (HeroController.instance == null) return;
            if (!shouldTick) return;

            // Use Time.deltaTime (scaled) to match the playback cursor
            accumulatedTime += Time.deltaTime;
            if (accumulatedTime < RECORD_INTERVAL) return;
            accumulatedTime -= RECORD_INTERVAL;

            bool facingRight = HeroController.instance.transform.localScale.x > 0f;
            Vector3 pos = HeroController.instance.transform.position;

            if (cachedAnim == null)
                cachedAnim = HeroController.instance.GetComponent<tk2dSpriteAnimator>();

            string clipName = "";
            int clipFrame = 0;
            try
            {
                if (cachedAnim?.CurrentClip != null)
                {
                    clipName = cachedAnim.CurrentClip.name;
                    clipFrame = cachedAnim.CurrentFrame;
                }
            }
            catch { cachedAnim = null; } // guh

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