using BepInEx.Logging;

namespace ReplayTimerMod
{
    internal sealed class RoomLifecycle
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomLifecycle");

        private readonly FrameRecorder _recorder;
        private readonly GhostPlayback _ghost;
        private readonly ReplayUI _ui;

        public RoomLifecycle(FrameRecorder recorder, GhostPlayback ghost, ReplayUI ui)
        {
            _recorder = recorder;
            _ghost = ghost;
            _ui = ui;
        }

        public void HandleRoomEnter(string sceneName, string entryFromScene)
        {
            if (GhostSettings.TrackingEnabled)
                _recorder.StartRecording();

            _ghost.StartPlayback(sceneName, entryFromScene);
        }

        public void HandleRoomExit(string sceneName, string entryFromScene,
            string exitToScene, float lrTime)
        {
            _ghost.StopPlayback();

            if (!GhostSettings.TrackingEnabled)
            {
                _recorder.DiscardRecording();
                return;
            }

            bool usedCheats = RoomTracker.RoomUsedDebugAbilities;
            if (usedCheats && GhostSettings.CancelRunOnCheats)
            {
                Log.LogInfo("[RoomLifecycle] Discarding room exit - debug abilities were used");
                _recorder.DiscardRecording();
                return;
            }

            var key = new RoomKey(sceneName, entryFromScene, exitToScene);

            if (GhostSettings.SkipBacktrackRuns
                && !string.IsNullOrEmpty(entryFromScene)
                && exitToScene == entryFromScene)
            {
                _recorder.DiscardRecording();
                return;
            }

            bool saveAllRuns = GhostSettings.SaveAllRunsEnabled;

            if (!saveAllRuns && !PBManager.WouldStoreRun(key, lrTime))
            {
                _recorder.DiscardRecording();
                return;
            }

            RecordedRoom? recording = _recorder.FinishRecording(key, lrTime);
            if (recording == null) return;

            var result = PBManager.Evaluate(recording, saveAllRuns, usedCheats);
            if (result.Kind == ResultKind.FirstRun
                || result.Kind == ResultKind.NewPB
                || result.Kind == ResultKind.SavedHistory)
                _ui.OnPBUpdated();
        }

        public void HandleRecordingDiscarded()
        {
            if (!RoomTracker.KeepGhostPlaybackOnDiscard)
                _ghost.StopPlayback();

            _recorder.DiscardRecording();
        }
    }
}
