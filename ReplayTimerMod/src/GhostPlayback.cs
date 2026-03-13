using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    // Plays back a previously recorded PB run as a ghost in world space.
    //
    // Lifecycle:
    //   RoomTracker.OnRoomEnter  → StartPlayback(scene, entryFromScene)
    //   LateUpdate               → Tick() advances playback in LR time
    //   RoomTracker.OnRoomExit / OnRecordingDiscarded → StopPlayback()
    //
    // Sprite rendering:
    //   We create a minimal GO while inactive so tk2dSprite.Awake() is deferred
    //   until after we assign Collection — this prevents the pink-rectangle bug.
    //   Clip name → spriteId is resolved via a Dictionary built once at init
    //   so the hot tick path is a single hash lookup, not GetClipByName() which
    //   does a linear scan every frame.
    public class GhostPlayback
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GhostPlayback");

        // ── Ghost objects ─────────────────────────────────────────────────────
        private GameObject? ghostSpriteGo;
        private tk2dSprite? ghostSprite;

        private GameObject? diamondGo;
        private LineRenderer? diamondLine;
        private Material? diamondMat;

        // Built once at sprite init — avoids per-tick GetClipByName linear scan.
        private Dictionary<string, tk2dSpriteAnimationClip>? clipCache;

        private bool spriteInitDone = false;

        private RecordedRoom? currentPB;
        private float playbackTime = 0f;
        private bool playing = false;

        // ── Setup ─────────────────────────────────────────────────────────────

        public void Setup()
        {
            diamondGo = new GameObject("ReplayGhost_Diamond");
            Object.DontDestroyOnLoad(diamondGo);
            diamondGo.SetActive(false);

            diamondLine = diamondGo.AddComponent<LineRenderer>();
            diamondLine.useWorldSpace = true;
            diamondLine.loop = true;
            diamondLine.positionCount = 4;
            diamondLine.startWidth = 0.06f;
            diamondLine.endWidth = 0.06f;
            diamondLine.numCapVertices = 2;
            diamondLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            diamondLine.receiveShadows = false;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = GhostSettings.GhostColor;
            diamondLine.material = mat;
            diamondMat = mat;

            Log.LogInfo("[Ghost] Setup complete");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void StartPlayback(string sceneName, string entryFromScene)
        {
            if (!GhostSettings.GhostEnabled)
            {
                HideAll();
                playing = false;
                return;
            }

            currentPB = GetBestPB(sceneName, entryFromScene);
            if (currentPB == null)
            {
                Log.LogInfo($"[Ghost] No PB for {sceneName} <- {entryFromScene}");
                HideAll();
                playing = false;
                return;
            }

            playbackTime = 0f;
            playing = true;
            Log.LogInfo($"[Ghost] Playing {currentPB.Key} ({currentPB.FrameCount} frames, {FormatTime(currentPB.TotalTime)})");
        }

        public void StopPlayback()
        {
            playing = false;
            currentPB = null;
            HideAll();
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick()
        {
            if (!playing || currentPB == null) return;
            if (!GhostSettings.GhostEnabled) { HideAll(); return; }
            try { if (!LoadRemover.ShouldTick()) return; }
            catch { return; }

            playbackTime += Time.unscaledDeltaTime;

            float interval = FrameRecorder.RECORD_INTERVAL;
            int frameIdx = Mathf.FloorToInt(playbackTime / interval);

            if (frameIdx >= currentPB.FrameCount - 1)
            {
                if (frameIdx >= currentPB.FrameCount) { StopPlayback(); return; }
                frameIdx = currentPB.FrameCount - 2;
            }

            FrameData a = currentPB.Frames[frameIdx];
            FrameData b = currentPB.Frames[frameIdx + 1];
            float t = Mathf.Clamp01((playbackTime - frameIdx * interval) / interval);

            float x = Mathf.LerpUnclamped(a.x, b.x, t);
            float y = Mathf.LerpUnclamped(a.y, b.y, t);
            float z = HeroController.instance != null
                ? HeroController.instance.transform.position.z : 0f;

            FrameData animFrame = t < 0.5f ? a : b;
            var pos = new Vector3(x, y, z);

            if (!string.IsNullOrEmpty(animFrame.animClip) && TryInitSprite())
                RenderSprite(pos, animFrame);
            else
                RenderDiamond(pos);
        }

        // ── Sprite init ───────────────────────────────────────────────────────
        // Creates the GO inactive so tk2dSprite.Awake() fires after Collection
        // is assigned — this is what prevents the pink rectangle.

        private bool TryInitSprite()
        {
            if (ghostSprite != null) return true;
            if (spriteInitDone) return false;
            spriteInitDone = true;

            try
            {
                if (HeroController.instance == null)
                {
                    Log.LogWarning("[Ghost] HeroController null — using diamond");
                    return false;
                }

                var heroSprite = HeroController.instance.GetComponent<tk2dSprite>()
                              ?? HeroController.instance.GetComponentInChildren<tk2dSprite>();
                if (heroSprite?.Collection == null)
                {
                    Log.LogWarning("[Ghost] No tk2dSprite/Collection on hero — using diamond");
                    return false;
                }

                ghostSpriteGo = new GameObject("ReplayGhost_Sprite");
                ghostSpriteGo.SetActive(false);
                Object.DontDestroyOnLoad(ghostSpriteGo);

                ghostSprite = ghostSpriteGo.AddComponent<tk2dSprite>();
                ghostSprite.Collection = heroSprite.Collection;
                ghostSprite.spriteId = heroSprite.spriteId;

                ghostSpriteGo.SetActive(true);   // Awake fires here with Collection set
                ghostSprite.color = GhostSettings.GhostColor;

                Log.LogInfo($"[Ghost] Sprite ready — collection='{heroSprite.Collection.name}'");

                var heroAnim = HeroController.instance.GetComponent<tk2dSpriteAnimator>();
                if (heroAnim?.Library != null)
                {
                    clipCache = new Dictionary<string, tk2dSpriteAnimationClip>(
                        heroAnim.Library.clips.Length);
                    foreach (var clip in heroAnim.Library.clips)
                        if (!string.IsNullOrEmpty(clip.name))
                            clipCache[clip.name] = clip;
                    Log.LogInfo($"[Ghost] Clip cache: {clipCache.Count} clips");
                }
                else
                {
                    Log.LogWarning("[Ghost] No animator library — sprite will show default frame");
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"[Ghost] Sprite init failed: {ex.Message} — using diamond");
                if (ghostSpriteGo != null) { Object.Destroy(ghostSpriteGo); ghostSpriteGo = null; }
                ghostSprite = null;
                return false;
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void RenderSprite(Vector3 pos, FrameData fd)
        {
            if (ghostSprite == null || ghostSpriteGo == null) return;

            if (clipCache != null && clipCache.TryGetValue(fd.animClip, out var clip)
                && clip.frames.Length > 0)
            {
                ghostSprite.spriteId = clip.frames[fd.animFrame % clip.frames.Length].spriteId;
            }

            ghostSprite.color = GhostSettings.GhostColor;
            ghostSpriteGo.transform.position = pos;

            Vector3 s = ghostSpriteGo.transform.localScale;
            ghostSpriteGo.transform.localScale = new Vector3(
                fd.facingRight ? Mathf.Abs(s.x) : -Mathf.Abs(s.x), s.y, s.z);

            ghostSpriteGo.SetActive(true);
            diamondGo?.SetActive(false);
        }

        private void RenderDiamond(Vector3 center)
        {
            ghostSpriteGo?.SetActive(false);
            if (diamondGo == null || diamondLine == null) return;
            if (diamondMat != null) diamondMat.color = GhostSettings.GhostColor;
            diamondGo.SetActive(true);
            const float s = 0.25f;
            diamondLine.SetPosition(0, center + new Vector3(0, s, 0));
            diamondLine.SetPosition(1, center + new Vector3(s, 0, 0));
            diamondLine.SetPosition(2, center + new Vector3(0, -s, 0));
            diamondLine.SetPosition(3, center + new Vector3(-s, 0, 0));
        }

        private void HideAll()
        {
            ghostSpriteGo?.SetActive(false);
            diamondGo?.SetActive(false);
        }

        // ── Ghost selection ───────────────────────────────────────────────────

        private static RecordedRoom? GetBestPB(string scene, string entryFromScene)
        {
            RecordedRoom? best = null;
            foreach (var pair in PBManager.AllPBs())
            {
                var key = pair.Key;
                if (key.SceneName != scene || key.EntryFromScene != entryFromScene) continue;
                if (best == null || pair.Value.TotalTime < best.TotalTime) best = pair.Value;
            }
            if (best != null) Log.LogInfo($"[Ghost] Matched {best.Key}");
            return best;
        }

        private static string FormatTime(float t)
        {
            int millis = (int)(t * 100) % 100;
            int seconds = (int)t % 60;
            int minutes = (int)t / 60;
            return $"{minutes}:{seconds:00}.{millis:00}";
        }
    }
}