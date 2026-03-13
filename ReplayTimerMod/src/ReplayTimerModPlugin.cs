using BepInEx;
using HarmonyLib;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReplayTimerMod
{
    [BepInDependency(DependencyGUID: "org.silksong-modding.modlist")]
    [BepInAutoPlugin(id: "io.github.adiprk.replaytimermod")]
    public partial class ReplayTimerModPlugin : BaseUnityPlugin
    {
        internal static ReplayTimerModPlugin Instance { get; private set; } = null!;

        private FrameRecorder frameRecorder = null!;
        private DebugOverlay debugOverlay = null!;
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;

        public int RecorderFrameCount => frameRecorder.FrameCount;

        private int sceneCount = 0;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            string dataDir = Path.Combine(
                Path.GetDirectoryName(Info.Location)!, "..", "..", "data");
            DataStore.Init(dataDir);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
            debugOverlay = new DebugOverlay();
            ghostPlayback = new GhostPlayback();
            replayUI = new ReplayUI();

            RoomTracker.Init();
            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;

            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            sceneCount++;
            if (sceneCount == 4)
            {
                Logger.LogInfo("Scene 4 — setting up UI, ghost, overlay");
                debugOverlay.Setup();
                ghostPlayback.Setup();
                replayUI.Setup();
            }
        }

        private void OnRoomEnter(string sceneName, string entryGate, string entryFromScene)
        {
            debugOverlay.ClearLastResult();
            frameRecorder.StartRecording();
            ghostPlayback.StartPlayback(sceneName, entryGate);
            // ReplayUI hides itself every frame when not paused — nothing to do here
        }

        private void OnRoomExit(string sceneName, string entryGate,
                                 string exitToScene, float lrTime)
        {
            ghostPlayback.StopPlayback();

            var key = new RoomKey(sceneName, entryGate, exitToScene);
            var recording = frameRecorder.FinishRecording(key, lrTime);

            if (recording != null)
            {
                var result = PBManager.Evaluate(recording);
                debugOverlay.SetLastResult(result);
                replayUI.OnPBUpdated();
            }
        }

        private void OnRecordingDiscarded()
        {
            ghostPlayback.StopPlayback();
            frameRecorder.DiscardRecording();
        }

        private void LateUpdate()
        {
            RoomTracker.Tick();
            frameRecorder.Tick();
            ghostPlayback.Tick();
            debugOverlay.Tick();
            replayUI.Tick();
        }
    }
}