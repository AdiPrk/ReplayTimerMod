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
        private RoomLifecycle roomLifecycle = null!;
        private bool lateInitDone = false;

        public override string GetVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        public override void Initialize()
        {
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
            catch (Exception ex) { LogTickError("LoadRemover", ex); }

            // Each subsystem ticks in its own guard so one failure can't
            // take the whole mod down for the rest of the session (the old
            // Modding API logs the exception but everything after the throw
            // is skipped, every frame). Mirrored in ReplayTimerModSS.
            try { RoomTracker.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTracker", ex); }
            try { frameRecorder.Tick(shouldTick); } catch (Exception ex) { LogTickError("FrameRecorder", ex); }
            try { ghostPlayback.Tick(shouldTick); } catch (Exception ex) { LogTickError("GhostPlayback", ex); }
            try { replayUI.Tick(); } catch (Exception ex) { LogTickError("ReplayUI", ex); }
            try { roomTimerHUD.Tick(shouldTick); } catch (Exception ex) { LogTickError("RoomTimerHUD", ex); }
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
            replayUI.Setup();
            roomTimerHUD.Setup();
        }

        // No teardown counterpart to ReplayTimerModSS.OnDestroy on purpose:
        // Modding API mods are never unloaded mid-session, and the Modding
        // API has no unload hook to attach one to.
    }
}
#endif