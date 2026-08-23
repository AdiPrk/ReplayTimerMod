using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Drives the per-room record → evaluate → upload pipeline in response to
    /// <see cref="RoomTracker"/> events. Shared by both platform entry points
    /// (Silksong and Hollow Knight) so the logic never diverges between them.
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

            if (GhostSettings.SkipBacktrackRuns
                && !string.IsNullOrEmpty(entryFromScene)
                && exitToScene == entryFromScene)
            {
                _recorder.DiscardRecording();
                return;
            }

            bool saveAllRuns = GhostSettings.SaveAllRunsEnabled;

            // The run's modifier mask - valid until the next room's recording
            // starts (ModifierTracker resets on room enter, after this handler).
            int modifierMask = RoomTracker.CurrentRunModifierMask;

            if (!saveAllRuns && !PBManager.WouldStoreRun(key, lrTime, modifierMask))
            {
                _recorder.DiscardRecording();
                return;
            }

            RecordedRoom? recording = _recorder.FinishRecording(key, lrTime, modifierMask);
            if (recording == null) return;

            var result = PBManager.Evaluate(recording, saveAllRuns);
            if (result.Kind == ResultKind.FirstRun
                || result.Kind == ResultKind.NewPB
                || result.Kind == ResultKind.NewMaskPB
                || result.Kind == ResultKind.SavedHistory)
                _ui.OnPBUpdated();

            // Upload the snapshot that was actually stored for THIS run - for
            // NewMaskPB that is not the overall-PB snapshot.
            if (Network != null
                && result.Snapshot != null
                && (result.Kind == ResultKind.FirstRun
                    || result.Kind == ResultKind.NewPB
                    || result.Kind == ResultKind.NewMaskPB))
            {
                Network.EnqueueUpload(result.Snapshot, result);
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
