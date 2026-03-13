using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayTimerMod
{
    // Plays back a previously recorded PB run as a ghost in world space.
    //
    // Lifecycle:
    //   RoomTracker.OnRoomEnter  → StartPlayback() with best PB for this entry
    //   LateUpdate               → Tick() advances playback in LR time
    //   RoomTracker.OnRoomExit / OnRecordingDiscarded → StopPlayback()
    //
    // The ghost is a small diamond shape made from a LineRenderer so it
    // works in world space without needing any sprites or assets.
    // It is semi-transparent to not obstruct gameplay.
    public class GhostPlayback
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GhostPlayback");

        // Size of the diamond in world units.
        private const float DIAMOND_SIZE = 0.25f;
        private const float GHOST_ALPHA = 0.5f;
        private static readonly Color GHOST_COLOR =
            new Color(0.4f, 0.8f, 1f, GHOST_ALPHA); // light blue

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

            // Use the Sprites/Default shader which works without any asset setup.
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = GHOST_COLOR;
            line.material = mat;

            ghostObj.SetActive(false);
            Log.LogInfo("[Ghost] Setup complete");
        }

        // ── Public API ────────────────────────────────────────────────────────
        // Find and start the best PB for this (sceneName, entryGate) combo.
        // We don't know the exit yet so we pick the fastest across all exits.
        public void StartPlayback(string sceneName, string entryGate)
        {
            currentPB = GetBestPBForEntry(sceneName, entryGate);

            if (currentPB == null)
            {
                Log.LogInfo($"[Ghost] No PB found for {sceneName} [{entryGate}]");
                ghostObj?.SetActive(false);
                playing = false;
                return;
            }

            playbackTime = 0f;
            playing = true;
            ghostObj?.SetActive(true);

            Log.LogInfo($"[Ghost] Playing back {currentPB.Key} " +
                        $"({currentPB.FrameCount} frames, {FormatTime(currentPB.TotalTime)})");
        }

        public void StopPlayback()
        {
            playing = false;
            currentPB = null;
            ghostObj?.SetActive(false);
        }

        // Called every LateUpdate. Only advances when ShouldTick().
        public void Tick()
        {
            if (!playing || currentPB == null || line == null) return;

            try { if (!LoadRemover.ShouldTick()) return; }
            catch { return; }

            playbackTime += Time.unscaledDeltaTime;

            // Find the two frames that bracket the current playback time.
            // Frame timestamps are implicit: frame[i] starts at i * RECORD_INTERVAL.
            float interval = FrameRecorder.RECORD_INTERVAL;
            int frameIdx = Mathf.FloorToInt(playbackTime / interval);

            if (frameIdx >= currentPB.FrameCount - 1)
            {
                // Playback finished — hold last position briefly then hide.
                if (frameIdx >= currentPB.FrameCount)
                {
                    StopPlayback();
                    return;
                }
                frameIdx = currentPB.FrameCount - 2;
            }

            FrameData a = currentPB.Frames[frameIdx];
            FrameData b = currentPB.Frames[frameIdx + 1];

            float t = (playbackTime - frameIdx * interval) / interval;
            t = Mathf.Clamp01(t);

            float x = Mathf.LerpUnclamped(a.x, b.x, t);
            float y = Mathf.LerpUnclamped(a.y, b.y, t);

            // Keep ghost at same Z as Hornet to ensure correct render order.
            float z = HeroController.instance != null
                ? HeroController.instance.transform.position.z
                : 0f;

            DrawDiamond(new Vector3(x, y, z));
        }

        public bool IsPlaying => playing;

        // ── Helpers ───────────────────────────────────────────────────────────
        private void DrawDiamond(Vector3 center)
        {
            if (line == null) return;
            float s = DIAMOND_SIZE;
            line.SetPosition(0, center + new Vector3(0, s, 0)); // top
            line.SetPosition(1, center + new Vector3(s, 0, 0)); // right
            line.SetPosition(2, center + new Vector3(0, -s, 0)); // bottom
            line.SetPosition(3, center + new Vector3(-s, 0, 0)); // left
        }

        private static RecordedRoom? GetBestPBForEntry(string scene, string entryGate)
        {
            RecordedRoom? best = null;
            foreach (var pair in PBManager.AllPBs())
            {
                if (pair.Key.SceneName == scene && pair.Key.EntryGate == entryGate)
                {
                    if (best == null || pair.Value.TotalTime < best.TotalTime)
                        best = pair.Value;
                }
            }
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