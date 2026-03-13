using System.Collections.Generic;
using UnityEngine;

namespace ReplayTimerMod
{
    public class FrameRecorder
    {
        private readonly List<FrameData> frames = new List<FrameData>();
        private bool recording = false;

        public void StartRecording()
        {
            frames.Clear();
            recording = true;
        }

        public void DiscardRecording()
        {
            frames.Clear();
            recording = false;
        }

        // Returns null if discarded or empty.
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

        public void Tick()
        {
            if (!recording) return;
            if (HeroController.instance == null) return;
            if (!LoadRemover.ShouldTick()) return;

            bool facingRight = HeroController.instance.transform.localScale.x > 0f;
            Vector3 pos = HeroController.instance.transform.position;

            frames.Add(new FrameData
            {
                x = pos.x,
                y = pos.y,
                facingRight = facingRight,
                deltaTime = Time.unscaledDeltaTime
            });
        }

        public bool IsRecording => recording;
        public int FrameCount => frames.Count;
    }
}