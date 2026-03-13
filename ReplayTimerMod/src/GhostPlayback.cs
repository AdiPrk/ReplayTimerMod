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
    // Ghost selection:
    //   RoomKey is now (SceneName, EntryFromScene, ExitToScene).
    //   We know SceneName and EntryFromScene on entry, but not ExitToScene yet.
    //   So we match on (SceneName, EntryFromScene) and pick the fastest exit.
    //   This is unambiguous — EntryFromScene uniquely identifies direction.
    public class GhostPlayback
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GhostPlayback");

        private const float DIAMOND_SIZE = 0.25f;
        private const float GHOST_ALPHA = 0.5f;
        private static readonly Color GHOST_COLOR =
            new Color(0.4f, 0.8f, 1f, GHOST_ALPHA);

        private GameObject? ghostObj;
        private LineRenderer? line;

        private RecordedRoom? currentPB;
        private float playbackTime = 0f;
        private bool playing = false;

        // ── Setup ─────────────────────────────────────────────────────────────
        public void Setup()
        {
            ghostObj = new GameObject("ReplayGhost");
            Object.DontDestroyOnLoad(ghostObj);

            line = ghostObj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 4;
            line.startWidth = 0.06f;
            line.endWidth = 0.06f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = GHOST_COLOR;
            line.material = mat;

            ghostObj.SetActive(false);
            Log.LogInfo("[Ghost] Setup complete");
        }

        // ── Public API ────────────────────────────────────────────────────────

        // sceneName     — room we just entered
        // entryFromScene — room we came from (the direction discriminator)
        public void StartPlayback(string sceneName, string entryFromScene)
        {
            currentPB = GetBestPB(sceneName, entryFromScene);

            if (currentPB == null)
            {
                Log.LogInfo($"[Ghost] No PB for {sceneName} ← {entryFromScene}");
                ghostObj?.SetActive(false);
                playing = false;
                return;
            }

            playbackTime = 0f;
            playing = true;
            ghostObj?.SetActive(true);

            Log.LogInfo($"[Ghost] Playing {currentPB.Key} " +
                        $"({currentPB.FrameCount} frames, {FormatTime(currentPB.TotalTime)})");
        }

        public void StopPlayback()
        {
            playing = false;
            currentPB = null;
            ghostObj?.SetActive(false);
        }

        public bool IsPlaying => playing;

        // ── Tick ──────────────────────────────────────────────────────────────
        public void Tick()
        {
            if (!playing || currentPB == null || line == null) return;

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

            float t = Mathf.Clamp01(
                (playbackTime - frameIdx * interval) / interval);

            float x = Mathf.LerpUnclamped(a.x, b.x, t);
            float y = Mathf.LerpUnclamped(a.y, b.y, t);
            float z = HeroController.instance != null
                ? HeroController.instance.transform.position.z
                : 0f;

            DrawDiamond(new Vector3(x, y, z));
        }

        // ── Ghost selection ───────────────────────────────────────────────────
        // Match on (SceneName, EntryFromScene) — both known on entry.
        // ExitToScene is unknown until we leave, so if there are multiple
        // saved exits from this direction, pick the fastest.
        // In practice most room→direction combos have exactly one recorded exit.
        private static RecordedRoom? GetBestPB(string scene, string entryFromScene)
        {
            RecordedRoom? best = null;
            foreach (var pair in PBManager.AllPBs())
            {
                var key = pair.Key;
                if (key.SceneName != scene || key.EntryFromScene != entryFromScene)
                    continue;

                if (best == null || pair.Value.TotalTime < best.TotalTime)
                    best = pair.Value;
            }

            if (best != null)
                Log.LogInfo($"[Ghost] Matched {best.Key}");

            return best;
        }

        // ── Drawing ───────────────────────────────────────────────────────────
        private void DrawDiamond(Vector3 center)
        {
            if (line == null) return;
            float s = DIAMOND_SIZE;
            line.SetPosition(0, center + new Vector3(0, s, 0));
            line.SetPosition(1, center + new Vector3(s, 0, 0));
            line.SetPosition(2, center + new Vector3(0, -s, 0));
            line.SetPosition(3, center + new Vector3(-s, 0, 0));
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