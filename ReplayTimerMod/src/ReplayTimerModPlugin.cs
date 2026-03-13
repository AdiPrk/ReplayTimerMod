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

        // Exposed for DebugOverlay to read without coupling it to FrameRecorder.
        public int RecorderFrameCount => frameRecorder.FrameCount;

        private int sceneCount = 0;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            // DataStore needs the BepInEx data directory.
            string dataDir = Path.Combine(
                Path.GetDirectoryName(Info.Location)!, "..", "..", "data");
            DataStore.Init(dataDir);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
            debugOverlay = new DebugOverlay();

            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;

            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnSceneChanged(UnityEngine.SceneManagement.Scene from,
                                    UnityEngine.SceneManagement.Scene to)
        {
            sceneCount++;
            if (sceneCount == 4)
            {
                Logger.LogInfo("Scene 4 reached — setting up debug overlay");
                debugOverlay.Setup();
            }
        }

        private void OnRoomEnter(string sceneName, string entryGate, string entryFromScene)
        {
            debugOverlay.ClearLastResult();
            frameRecorder.StartRecording();
        }

        private void OnRoomExit(string sceneName, string entryGate,
                                 string exitToScene, float lrTime)
        {
            RoomKey key = new RoomKey(sceneName, entryGate, exitToScene);
            RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);

            if (recording != null)
            {
                EvaluationResult result = PBManager.Evaluate(recording);
                debugOverlay.SetLastResult(result);
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
            debugOverlay.Tick();
        }
    }
}