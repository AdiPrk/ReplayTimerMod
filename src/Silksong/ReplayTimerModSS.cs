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
        private RoomTimerHUD roomTimerHUD = null!;
        private ReplaySelectionState replaySelectionState = null!;
        private NetworkClient? networkClient;
        private bool lateInitDone = false;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"Plugin {Name} ({Id}) has loaded!");

            new Harmony(Id).PatchAll(Assembly.GetExecutingAssembly());

            string baseDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? ".";

            GhostSettings.Init(baseDirectory);
            QuickWarp.Init();

            string dataDir = Path.Combine(baseDirectory, "ReplayMod", "data");
            DataStore.Init(dataDir);
            replaySelectionState = new ReplaySelectionState();
            PBManager.SetSelectionState(replaySelectionState);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
            ghostPlayback = new GhostPlayback();
            ghostPlayback.SetSelectionState(replaySelectionState);
            replayUI = new ReplayUI();
            roomTimerHUD = new RoomTimerHUD();
            replayUI.SetTimerHUD(roomTimerHUD);

            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;
        }

        private void OnRoomEnter(string sceneName, string entryFromScene)
        {
            if (!GhostSettings.TrackingEnabled)
            {
                ghostPlayback.StartPlayback(sceneName, entryFromScene);
                networkClient?.PrefetchRoom(sceneName);
                return;
            }

            frameRecorder.StartRecording();
            ghostPlayback.StartPlayback(sceneName, entryFromScene);
            networkClient?.PrefetchRoom(sceneName);
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

            // Belt-and-suspenders: RoomTracker cancels the run the instant a
            // DebugMod cheat/debug ability is detected, so this should never
            // actually be true here - but if it ever is, never save/upload it.
            if (RoomTracker.RoomUsedDebugAbilities)
            {
                Logger.LogInfo("[ReplayTimerModSS] Discarding room exit - debug abilities were used");
                frameRecorder.DiscardRecording();
                return;
            }

            RoomKey key = new RoomKey(sceneName, entryFromScene, exitToScene);

            // Option: don't save runs that exit back through the same
            // transition they entered from (exitTo == entryFrom).
            if (GhostSettings.SkipBacktrackRuns
                && !string.IsNullOrEmpty(entryFromScene)
                && exitToScene == entryFromScene)
            {
                frameRecorder.DiscardRecording();
                return;
            }

            bool saveAllRuns = GhostSettings.SaveAllRunsEnabled;

            if (!saveAllRuns && !PBManager.WouldBePB(key, lrTime))
            {
                frameRecorder.DiscardRecording();
                return;
            }

            RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);
            if (recording == null) return;

            var result = PBManager.Evaluate(recording, saveAllRuns);
            if (result.Kind == ResultKind.FirstRun
                || result.Kind == ResultKind.NewPB
                || result.Kind == ResultKind.SavedHistory)
                replayUI.OnPBUpdated();

            if (networkClient != null
                && (result.Kind == ResultKind.FirstRun
                    || result.Kind == ResultKind.NewPB))
            {
                var snapshot = PBManager.GetPBSnapshot(key);
                if (snapshot != null)
                    networkClient.EnqueueUpload(snapshot, result);
            }
        }

        private void OnRecordingDiscarded()
        {
            // Cheat-cancelled runs invalidate the recording but the player
            // hasn't left the room - keep the ghost replay going so it can
            // still be watched.
            if (!RoomTracker.KeepGhostPlaybackOnDiscard)
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
            roomTimerHUD.Tick(shouldTick);
            if (networkClient != null) networkClient.Tick();
        }

        private void TryLateInit()
        {
            if (lateInitDone) return;
            if (HeroController.instance == null) return;

            lateInitDone = true;
            Logger.LogInfo("Hero ready - setting up UI and ghost");
            ghostPlayback.Setup();
            replayUI.Setup();
            roomTimerHUD.Setup();

            // Set game tag for leaderboard cache keys
            replayUI.SetGameTag("silksong");

            // Wire the online toggle handler
            replayUI.SetOnlineToggleHandler(OnOnlineToggled);

            replayUI.OnDisplayNameSet += OnDisplayNameSet;

            if (GhostSettings.OnlineEnabled
                && !string.IsNullOrEmpty(GhostSettings.DisplayName))
                StartNetworking();
        }

        private void StartNetworking()
        {
            if (networkClient != null && networkClient.IsStarted)
                return;

            GhostSettings.EnsureDeviceId();

            if (networkClient == null)
            {
                string version = Assembly.GetExecutingAssembly()
                    .GetName().Version?.ToString() ?? "0.0.0";

                networkClient = new NetworkClient(
                    GhostSettings.DeviceId,
                    "silksong",
                    version,
                    GhostSettings.ApiBaseUrl);
                networkClient.OnRankReceived += roomTimerHUD.ShowRank;
                networkClient.OnDisplayNameReceived += name =>
                {
                    if (string.IsNullOrEmpty(GhostSettings.DisplayName))
                        GhostSettings.DisplayName = name;
                };

                // Wire leaderboard: NetworkClient writes to ReplayUI's cache.
                // ReplayUI subscribes to events internally via SetNetworkClient.
                networkClient.SetLeaderboardCache(replayUI.LeaderboardCacheRef);
                replayUI.SetNetworkClient(networkClient);
            }

            networkClient.Start();
            Logger.LogInfo("Online features started");
        }

        private void OnOnlineToggled(bool enabled)
        {
            if (enabled)
            {
                if (!string.IsNullOrEmpty(GhostSettings.DisplayName))
                    StartNetworking();
            }
            else if (networkClient != null)
            {
                networkClient.Stop();
                Logger.LogInfo("Online features stopped");
            }
        }

        private void OnDisplayNameSet(string name)
        {
            GhostSettings.DisplayName = name;
            GhostSettings.Save();
            Logger.LogInfo("Display name set: " + name);

            if (GhostSettings.OnlineEnabled)
                StartNetworking();

            if (networkClient != null)
                networkClient.ForceRefreshAll();
        }

        private void OnDestroy()
        {
            if (networkClient != null) networkClient.Stop();
            roomTimerHUD.Teardown();
        }

        private static bool TryGetGameManager(out GameManager? gm)
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