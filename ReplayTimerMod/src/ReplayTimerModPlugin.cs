using BepInEx;
using System.IO;
using System.Reflection;
using HarmonyLib;
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
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;

        private int sceneCount = 0;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            // Bind GhostSettings to BepInEx config before any other system
            // reads those properties.
            GhostSettings.Init(Config);

            string dataDir = Path.Combine(
                Path.GetDirectoryName(Info.Location)!, "..", "..", "data");
            DataStore.Init(dataDir);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
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
                Logger.LogInfo("Scene 4 - setting up UI and ghost");
                ghostPlayback.Setup();
                replayUI.Setup();
            }
        }

        private void OnRoomEnter(string sceneName, string entryFromScene)
        {
            if (!GhostSettings.TrackingEnabled)
            {
                // Start playback, but don't record
                ghostPlayback.StartPlayback(sceneName, entryFromScene);
                return;
            }

            frameRecorder.StartRecording();
            ghostPlayback.StartPlayback(sceneName, entryFromScene);
        }

        private void OnRoomExit(string sceneName, string entryFromScene,
                                 string exitToScene, float lrTime)
        {
            ghostPlayback.StopPlayback();

            if (!GhostSettings.TrackingEnabled)
            {
                frameRecorder.DiscardRecording();
                return;
            }

            RoomKey key = new RoomKey(sceneName, entryFromScene, exitToScene);

            // Check before calling FinishRecording so we don't pay the cost of
            // frames.ToArray() (up to ~25KB) on every missed attempt. For a
            // speedrunner retrying the same room hundreds of times this avoids
            // GC pressure that would otherwise cause periodic hitches.
            if (PBManager.WouldBePB(key, lrTime))
            {
                RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);
                if (recording != null)
                {
                    PBManager.Evaluate(recording);
                    replayUI.OnPBUpdated();
                }
            }
            else
            {
                frameRecorder.DiscardRecording();
            }
        }

        private void OnRecordingDiscarded()
        {
            ghostPlayback.StopPlayback();
            frameRecorder.DiscardRecording();
        }

        private void LateUpdate()
        {
            bool shouldTick = false;
            try { shouldTick = LoadRemover.ShouldTick(); } catch { }

            RoomTracker.Tick(shouldTick);
            frameRecorder.Tick(shouldTick);
            ghostPlayback.Tick(shouldTick);
            replayUI.Tick();
        }
    }
}