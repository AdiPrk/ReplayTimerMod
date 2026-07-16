#if HOLLOW_KNIGHT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Modding;

namespace ReplayTimerMod
{
    public class ReplayTimerModHK : Mod
    {
        private FrameRecorder frameRecorder = null!;
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;
        private RoomTimerHUD roomTimerHUD = null!;
        private ReplaySelectionState replaySelectionState = null!;
        private NetworkClient? networkClient;
        private RoomLifecycle roomLifecycle = null!;
        private bool lateInitDone = false;

        public static ReplayTimerModHK Instance { get; private set; } = null!;

        private static string GameTag
        {
            get
            {
#if V1221
                return "hk_1221";
#else
                return "hk_1578";
#endif
            }
        }

        public override string GetVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        public override void Initialize()
        {
            Instance = this;
            Log("Initialize");

            GameHooks.Init();

            string baseDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? ".";

            GhostSettings.Init(baseDirectory);
            QuickWarp.Init();

            string dataDir = Path.Combine(
                Path.Combine(baseDirectory, "ReplayMod"), "data");
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

#if V1221
            ModHooks.Instance.HeroUpdateHook += OnHeroUpdate;
#else
            ModHooks.HeroUpdateHook += OnHeroUpdate;
#endif
        }

        private void OnHeroUpdate()
        {
            TryLateInit();

            bool shouldTick = false;
            try { shouldTick = LoadRemover.ShouldTick(); }
            catch { Log("couldnt check tick timer"); }

            // Each subsystem ticks in its own guard so one failure can't
            // take the whole mod down for the rest of the session (the old
            // Modding API logs the exception but everything after the throw
            // is skipped, every frame). Mirrored in ReplayTimerModSS.
            try { RoomTracker.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTracker", ex); }
            try { frameRecorder.Tick(shouldTick); } catch (Exception ex) { LogTickError("FrameRecorder", ex); }
            try { ghostPlayback.Tick(shouldTick); } catch (Exception ex) { LogTickError("GhostPlayback", ex); }
            try { replayUI.Tick(); } catch (Exception ex) { LogTickError("ReplayUI", ex); }
            try { roomTimerHUD.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTimerHUD", ex); }
            try { if (networkClient != null) networkClient.Tick(); } catch (Exception ex) { LogTickError("NetworkClient", ex); }
        }

        // Throttled per-subsystem error log so a persistent per-frame fault
        // doesn't flood ModLog.txt.
        private readonly Dictionary<string, float> _lastTickErrorLog =
            new Dictionary<string, float>();

        private void LogTickError(string subsystem, Exception ex)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            float last;
            if (_lastTickErrorLog.TryGetValue(subsystem, out last) && now - last < 5f)
                return;
            _lastTickErrorLog[subsystem] = now;
            Log("[Tick] " + subsystem + " failed: " + ex);
        }

        private void TryLateInit()
        {
            if (lateInitDone) return;
            if (HeroController.instance == null) return;

            lateInitDone = true;
            Log("Hero ready - setting up UI and ghost");
            ghostPlayback.Setup();
            replayUI.Setup();
            roomTimerHUD.Setup();

            // Set game tag for leaderboard cache keys
            replayUI.SetGameTag(GameTag);

            // Wire the online toggle handler
            replayUI.SetOnlineToggleHandler(OnOnlineToggled);

            // When name is set via the config tab, start networking
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
                networkClient = new NetworkClient(
                    GhostSettings.DeviceId,
                    GameTag,
                    GetVersion(),
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
            Log("Online features started");
        }

        private void OnOnlineToggled(bool enabled)
        {
            if (enabled)
            {
                // Networking only starts once a display name is set.
                // The config tab shows a name input when online is enabled.
                if (!string.IsNullOrEmpty(GhostSettings.DisplayName))
                    StartNetworking();
            }
            else if (networkClient != null)
            {
                networkClient.Stop();
                Log("Online features stopped");
            }
        }

        private void OnDisplayNameSet(string name)
        {
            GhostSettings.DisplayName = name;
            GhostSettings.Save();
            Log("Display name set: " + name);

            // If online is enabled but networking hasn't started yet
            // (was waiting for the name), start it now.
            if (GhostSettings.OnlineEnabled)
                StartNetworking();

            // Force refresh so leaderboards show the new name immediately
            if (networkClient != null)
                networkClient.ForceRefreshAll();
        }

    }
}
#endif