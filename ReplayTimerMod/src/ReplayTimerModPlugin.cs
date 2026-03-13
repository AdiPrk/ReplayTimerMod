using BepInEx;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace ReplayTimerMod
{
    [BepInDependency(DependencyGUID: "org.silksong-modding.modlist")]
    [BepInAutoPlugin(id: "io.github.adiprk.replaytimermod")]
    public partial class ReplayTimerModPlugin : BaseUnityPlugin
    {
        internal static ReplayTimerModPlugin Instance { get; private set; }

        private FrameRecorder frameRecorder;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            frameRecorder = new FrameRecorder();

            // RoomTracker.Init() subscribes to activeSceneChanged,
            // OnPlayerDead, and OnSavestateLoad — must happen before we
            // subscribe below so its state is cleaned up first.
            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;

            // Both death and savestate loads should discard the recording.
            // RoomTracker has already cleared its own state by the time these fire.
            GameHooks.OnPlayerDead += OnRecordingInvalidated;
        }

        private void OnRoomEnter(string sceneName)
        {
            Logger.LogInfo($"[RoomTracker] Entered room: {sceneName}");
            frameRecorder.StartRecording();
        }

        private void OnRoomExit(string sceneName, float lrTime, bool wasNaturalExit)
        {
            Logger.LogInfo($"[RoomTracker] Exited room: {sceneName} | LR time: {FormatTime(lrTime)} | Natural: {wasNaturalExit}");

            RecordedRoom? recording = frameRecorder.FinishRecording(sceneName, lrTime);

            if (recording != null)
            {
                Logger.LogInfo($"[FrameRecorder] Captured {recording.FrameCount} frames for {sceneName}");

                if (wasNaturalExit)
                {
                    // TODO: pass to PBManager once implemented
                    Logger.LogInfo($"[PBManager] Would evaluate PB for {sceneName}: {FormatTime(lrTime)}");
                }
                else
                {
                    Logger.LogInfo($"[PBManager] Discarding — quit to menu mid-room");
                }
            }
            else
            {
                Logger.LogInfo($"[FrameRecorder] No valid recording for {sceneName} (discarded or empty)");
            }
        }

        private void OnRecordingInvalidated()
        {
            // Covers both death and savestate loads.
            // FinishRecording will return null since DiscardRecording clears the
            // frames — this is intentional, no PB evaluation happens.
            Logger.LogInfo("[ReplayMod] Recording invalidated (death or savestate) — discarding");
            frameRecorder.DiscardRecording();
        }

        private void LateUpdate()
        {
            RoomTracker.Tick();
            frameRecorder.Tick();
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