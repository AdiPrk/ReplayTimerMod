using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Drives the per-room record → evaluate → upload pipeline in response to
    /// <see cref="RoomTracker"/> events. Shared by both platform entry points
    /// (Silksong and Hollow Knight), which previously held byte-identical copies
    /// of this logic and had to be kept in sync by hand.
    ///
    /// The entry point constructs one of these, points
    /// <see cref="RoomTracker"/>'s events at its handlers, and updates
    /// <see cref="Network"/> as the network client starts/stops.
    /// </summary>
    internal sealed class RoomLifecycle
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomLifecycle");

        private readonly FrameRecorder _recorder;
        private readonly GhostPlayback _ghost;
        private readonly ReplayUI _ui;

        /// <summary>
        /// The active network client, or null while offline. The entry point
        /// assigns this once the client is created; a stopped client is left in
        /// place (its own methods no-op until restarted).
        /// </summary>
        public NetworkClient? Network { get; set; }

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
            Network?.PrefetchRoom(sceneName);
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

            // Belt-and-suspenders: RoomTracker cancels the run the instant a
            // DebugMod cheat/debug ability is detected, so this should never
            // actually be true here - but if it ever is, never save/upload it.
            if (RoomTracker.RoomUsedDebugAbilities)
            {
                Log.LogInfo("[RoomLifecycle] Discarding room exit - debug abilities were used");
                _recorder.DiscardRecording();
                return;
            }

            var key = new RoomKey(sceneName, entryFromScene, exitToScene);

            // Option: don't save runs that exit back through the same
            // transition they entered from (exitTo == entryFrom).
            if (GhostSettings.SkipBacktrackRuns
                && !string.IsNullOrEmpty(entryFromScene)
                && exitToScene == entryFromScene)
            {
                _recorder.DiscardRecording();
                return;
            }

            bool saveAllRuns = GhostSettings.SaveAllRunsEnabled;

            if (!saveAllRuns && !PBManager.WouldBePB(key, lrTime))
            {
                _recorder.DiscardRecording();
                return;
            }

            RecordedRoom? recording = _recorder.FinishRecording(key, lrTime);
            if (recording == null) return;

            var result = PBManager.Evaluate(recording, saveAllRuns);
            if (result.Kind == ResultKind.FirstRun
                || result.Kind == ResultKind.NewPB
                || result.Kind == ResultKind.SavedHistory)
                _ui.OnPBUpdated();

            if (Network != null
                && (result.Kind == ResultKind.FirstRun
                    || result.Kind == ResultKind.NewPB))
            {
                var snapshot = PBManager.GetPBSnapshot(key);
                if (snapshot != null)
                    Network.EnqueueUpload(snapshot, result);
            }
        }

        public void HandleRecordingDiscarded()
        {
            // Cheat-cancelled runs invalidate the recording but the player
            // hasn't left the room - keep the ghost replay going so it can
            // still be watched.
            if (!RoomTracker.KeepGhostPlaybackOnDiscard)
                _ghost.StopPlayback();

            _recorder.DiscardRecording();
        }
    }
}
