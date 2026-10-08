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

        private readonly List<FrameData> _frames = new List<FrameData>();
        private bool _recording = false;
        private float _accumulatedTime = 0f;

        private tk2dSpriteAnimator? _cachedAnim = null;

        public void StartRecording()
        {
            _frames.Clear();
            _recording = true;
            _cachedAnim = null;
            // Pre-fill the accumulator so the very first Tick() captures a frame immediately
            _accumulatedTime = RECORD_INTERVAL;
        }

        public void DiscardRecording()
        {
            _frames.Clear();
            _recording = false;
            _accumulatedTime = 0f;
            _cachedAnim = null;
        }

        public RecordedRoom? FinishRecording(RoomKey key, float totalLRTime)
        {
            if (!_recording || _frames.Count == 0)
            {
                _frames.Clear();
                _recording = false;
                _cachedAnim = null;
                return null;
            }

            _recording = false;
            _cachedAnim = null;
            var result = new RecordedRoom(key, totalLRTime, _frames.ToArray());
            _frames.Clear();
            return result;
        }

        // Called every LateUpdate if LoadRemover.ShouldTick()
        public void Tick(bool shouldTick)
        {
            if (!_recording) return;
            if (HeroController.instance == null) return;
            if (!shouldTick) return;

            // Use Time.deltaTime (scaled) to match the playback cursor
            _accumulatedTime += Time.deltaTime;
            if (_accumulatedTime < RECORD_INTERVAL) return;

            // Drain EVERY whole interval, not just one. Below RECORD_FPS a
            // single capture per game frame would let the accumulator grow
            // without bound: frame i would no longer mean time i/RECORD_FPS,
            // the ghost would replay faster than real time, and the recorded
            // frame count would drift from TotalTime (which the server
            // cross-checks). Duplicating the current sample is correct - the
            // player really was here for that whole stretch of time.
            bool facingRight = HeroController.instance.transform.localScale.x > 0f;
            Vector3 pos = HeroController.instance.transform.position;

            if (_cachedAnim == null)
                _cachedAnim = ResolveHeroAnimator();

            string clipName = "";
            int clipFrame = 0;
            try
            {
                if (_cachedAnim?.CurrentClip != null)
                {
                    clipName = _cachedAnim.CurrentClip.name;
                    clipFrame = _cachedAnim.CurrentFrame;
                }
                else
                {
                    // Some states swap sprite/animator ownership; re-resolve when clip is missing.
                    _cachedAnim = ResolveHeroAnimator();
                    if (_cachedAnim?.CurrentClip != null)
                    {
                        clipName = _cachedAnim.CurrentClip.name;
                        clipFrame = _cachedAnim.CurrentFrame;
                    }
                }
            }
            catch { _cachedAnim = null; } // animator was torn down mid-read; re-resolve next tick

            while (_accumulatedTime >= RECORD_INTERVAL)
            {
                _accumulatedTime -= RECORD_INTERVAL;
                _frames.Add(new FrameData
                {
                    x = pos.x,
                    y = pos.y,
                    facingRight = facingRight,
                    animClip = clipName,
                    animFrame = clipFrame
                });
            }
        }

        public bool IsRecording => _recording;
        public int FrameCount => _frames.Count;

        private static tk2dSpriteAnimator? ResolveHeroAnimator()
        {
            var hero = HeroController.instance;
            if (hero == null) return null;

            // Prefer animator on the same GO as the visible hero sprite.
            var heroSprite = hero.GetComponent<tk2dSprite>()
                          ?? hero.GetComponentInChildren<tk2dSprite>();

            if (heroSprite != null)
            {
                var spriteAnim = heroSprite.GetComponent<tk2dSpriteAnimator>()
                              ?? heroSprite.GetComponentInParent<tk2dSpriteAnimator>()
                              ?? heroSprite.GetComponentInChildren<tk2dSpriteAnimator>();
                if (spriteAnim != null) return spriteAnim;
            }

            return hero.GetComponent<tk2dSpriteAnimator>()
                ?? hero.GetComponentInChildren<tk2dSpriteAnimator>();
        }
    }
}