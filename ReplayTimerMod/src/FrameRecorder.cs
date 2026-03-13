using System.Collections.Generic;
using UnityEngine;

namespace ReplayTimerMod
{
    // Captures Hornet's state once per LateUpdate, but only during frames where
    // LoadRemover.ShouldTick() is true. This means the recording contains only
    // real gameplay frames — loads, pauses, and cutscenes are omitted.
    //
    // GhostPlayback will replay these frames at the same LR-tick rate, keeping
    // the ghost in sync regardless of load times on the viewer's machine.
    //
    // Lifecycle per room:
    //   RoomTracker.OnRoomEnter  → StartRecording()
    //   LateUpdate (each frame)  → Tick()
    //   GameHooks.OnPlayerDead   → DiscardRecording()
    //   RoomTracker.OnRoomExit   → FinishRecording() → RecordedRoom (or null)
    public class FrameRecorder
    {
        private readonly List<FrameData> frames = new List<FrameData>();
        private bool recording = false;

        // Begin a fresh recording. Clears any leftover state from the prior room.
        public void StartRecording()
        {
            frames.Clear();
            recording = true;
        }

        // Abandon the current recording without producing a result.
        // Called on death — a partial run should never influence the PB.
        public void DiscardRecording()
        {
            frames.Clear();
            recording = false;
        }

        // Finalise the recording and return a RecordedRoom, or null if the
        // recording was discarded (death) or contains no frames.
        // The caller is responsible for deciding whether to submit to PBManager
        // based on wasNaturalExit from RoomTracker.OnRoomExit.
        public RecordedRoom? FinishRecording(string sceneName, float totalLRTime)
        {
            if (!recording || frames.Count == 0)
            {
                frames.Clear();
                recording = false;
                return null;
            }

            recording = false;
            RecordedRoom result = new RecordedRoom(sceneName, totalLRTime, frames.ToArray());
            frames.Clear();
            return result;
        }

        // Called every LateUpdate by the plugin.
        // Only captures when all of the following are true:
        //   1. We are currently recording (StartRecording was called)
        //   2. HeroController exists (guard for transitions / death animation)
        //   3. LoadRemover.ShouldTick() — actual gameplay time is passing
        public void Tick()
        {
            if (!recording) return;

            // HeroController.instance can be null briefly during transitions.
            if (HeroController.instance == null) return;

            // Only record frames that count as gameplay time.
            // This keeps the frame array aligned with the LR timer in RoomTracker,
            // so ghost playback can advance one frame per LR-tick.
            if (!LoadRemover.ShouldTick()) return;

            // Facing direction: in Silksong (as in HK) the sprite is mirrored by
            // negating the transform's X scale when facing left.
            // Using the transform directly is more reliable than cState.facingRight
            // since it reflects what's actually rendered on screen.
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