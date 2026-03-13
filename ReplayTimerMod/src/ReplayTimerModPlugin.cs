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
        internal static ReplayTimerModPlugin Instance { get; private set; } = null!;

        private FrameRecorder frameRecorder = null!;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            frameRecorder = new FrameRecorder();

            // RoomTracker.Init() must run before we subscribe to its events so
            // that its own handlers (HandleInvalidation) are registered first.
            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;
        }

        // sceneName, entryGate, entryFromScene
        private void OnRoomEnter(string sceneName, string entryGate, string entryFromScene)
        {
            Logger.LogInfo($"[Recorder] START {sceneName} [{entryGate}] from {entryFromScene}");
            frameRecorder.StartRecording();
        }

        // sceneName, entryGate, exitToScene, lrTime
        private void OnRoomExit(string sceneName, string entryGate, string exitToScene, float lrTime)
        {
            RoomKey key = new RoomKey(sceneName, entryGate, exitToScene);
            Logger.LogInfo($"[Recorder] END {key} — {FormatTime(lrTime)}");

            RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);

            if (recording != null)
            {
                Logger.LogInfo($"[Recorder] {recording.FrameCount} frames captured");
                // TODO: pass to PBManager
            }
        }

        private void OnRecordingDiscarded()
        {
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