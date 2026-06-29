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
        private RoomLifecycle roomLifecycle = null!;
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
            roomLifecycle = new RoomLifecycle(frameRecorder, ghostPlayback, replayUI);

            RoomTracker.Init();

            RoomTracker.OnRoomEnter += roomLifecycle.HandleRoomEnter;
            RoomTracker.OnRoomExit += roomLifecycle.HandleRoomExit;
            RoomTracker.OnRecordingDiscarded += roomLifecycle.HandleRecordingDiscarded;
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

            roomLifecycle.Network = networkClient;
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