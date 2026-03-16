#if SILKSONG_BUILD
using BepInEx;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ReplayTimerMod
{
    [BepInDependency("org.silksong-modding.modlist", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInAutoPlugin(id: "io.github.adiprk.replaytimermod")]
    public partial class ReplayTimerModSS : BaseUnityPlugin
    {
        internal static ReplayTimerModSS Instance { get; private set; } = null!;
        private static GameManager? cachedGameManager;

        private FrameRecorder frameRecorder = null!;
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;
        private ReplaySelectionState replaySelectionState = null!;

        private bool lateInitDone = false;

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
            replaySelectionState = new ReplaySelectionState();
            PBManager.SetSelectionState(replaySelectionState);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
            ghostPlayback = new GhostPlayback();
            ghostPlayback.SetSelectionState(replaySelectionState);
            replayUI = new ReplayUI();

            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;
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

            bool saveAllRuns = GhostSettings.SaveAllRunsEnabled;

            // PB-only mode preserves the existing fast path so we avoid paying the
            // cost of frames.ToArray() on missed attempts. Save-all mode opts into
            // materializing every completed run and relies on PBManager dedupe.
            if (!saveAllRuns && !PBManager.WouldBePB(key, lrTime))
            {
                frameRecorder.DiscardRecording();
                return;
            }

            RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);
            if (recording == null)
                return;

            var result = PBManager.Evaluate(recording, saveAllRuns);
            if (result.Kind == ResultKind.FirstRun
                || result.Kind == ResultKind.NewPB
                || result.Kind == ResultKind.SavedHistory)
                replayUI.OnPBUpdated();
        }

        private void OnRecordingDiscarded()
        {
            ghostPlayback.StopPlayback();
            frameRecorder.DiscardRecording();
        }

        private void LateUpdate()
        {
            TryLateInit();

            if (!TryGetGameManager(out _))
                return;

            bool shouldTick = false;
            try { shouldTick = LoadRemover.ShouldTick(); } catch { }

            RoomTracker.Tick(shouldTick);
            frameRecorder.Tick(shouldTick);
            ghostPlayback.Tick(shouldTick);
            replayUI.Tick();
        }

        private void TryLateInit()
        {
            if (lateInitDone) return;
            if (HeroController.instance == null) return;

            lateInitDone = true;
            Logger.LogInfo("Hero ready - setting up UI and ghost");
            ghostPlayback.Setup();
            replayUI.Setup();
        }

        private static bool TryGetGameManager(out GameManager gm)
        {
            if (cachedGameManager != null)
            {
                gm = cachedGameManager;
                return true;
            }

            gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null) return false;

            cachedGameManager = gm;
            return true;
        }
    }
}
#endif