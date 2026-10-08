#if SILKSONG_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ReplayTimerMod
{
    [BepInDependency("org.silksong-modding.modlist", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInAutoPlugin(id: "io.github.adiprk.replaytimermod")]
    public partial class ReplayTimerModSS : BaseUnityPlugin
    {
        private static GameManager? cachedGameManager;

        private FrameRecorder frameRecorder = null!;
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;
        private RoomTimerHUD roomTimerHUD = null!;
        private ReplaySelectionState replaySelectionState = null!;
        private RoomLifecycle roomLifecycle = null!;
        private bool lateInitDone = false;

        private void Awake()
        {
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
            try { shouldTick = LoadRemover.ShouldTick(); } catch (Exception ex) { LogTickError("LoadRemover", ex); }

            try { RoomTracker.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTracker", ex); }
            try { frameRecorder.Tick(shouldTick); } catch (Exception ex) { LogTickError("FrameRecorder", ex); }
            try { ghostPlayback.Tick(shouldTick); } catch (Exception ex) { LogTickError("GhostPlayback", ex); }
            try { replayUI.Tick(); } catch (Exception ex) { LogTickError("ReplayUI", ex); }
            try { roomTimerHUD.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTimerHUD", ex); }
        }

        private readonly Dictionary<string, float> _lastTickErrorLog =
            new Dictionary<string, float>();

        private void LogTickError(string subsystem, Exception ex)
        {
            float now = Time.realtimeSinceStartup;
            if (_lastTickErrorLog.TryGetValue(subsystem, out float last) && now - last < 5f)
                return;
            _lastTickErrorLog[subsystem] = now;
            Logger.LogError("[Tick] " + subsystem + " failed: " + ex);
        }

        private void TryLateInit()
        {
            if (lateInitDone) return;
            if (HeroController.instance == null) return;

            lateInitDone = true;
            Logger.LogInfo("Hero ready - setting up UI and ghost");
            replayUI.Setup();
            roomTimerHUD.Setup();
        }

        private void OnDestroy()
        {
            roomTimerHUD.Teardown();
            GhostSettings.Flush();
        }

        private static bool TryGetGameManager(out GameManager? gm)
        {
            if (cachedGameManager != null)
            {
                gm = cachedGameManager;
                return true;
            }

            gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            if (gm == null) return false;

            cachedGameManager = gm;
            return true;
        }
    }
}
#endif
