using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // -- Config tab: Copy all (to clipboard) --

        private void OnCopyAllClicked()
        {
            var all = PBManager.AllPBs().Select(p => p.Value).ToList();
            if (all.Count == 0)
            {
                ShowButtonFeedback(_copyAllCfgLbl, "Nothing to copy", UIStyle.Subtext);
                return;
            }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.EncodeCollection(all);
            ShowButtonFeedback(_copyAllCfgLbl, all.Count + " copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Copied {all.Count} replays to clipboard");
        }

        // -- Config tab: Export all (save to disk) --

        private void OnExportAllClicked()
        {
            var all = PBManager.AllPBs().Select(p => p.Value).ToList();
            if (all.Count == 0) return;

            try
            {
                string dir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                    "export");
                System.IO.Directory.CreateDirectory(dir);

                string stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                string fileName = $"Re_{stamp}_{all.Count}.rtmc.txt";
                string path = System.IO.Path.Combine(dir, fileName);

                System.IO.File.WriteAllText(path, ReplayShareEncoder.EncodeCollection(all));
                Log.LogInfo($"[ReplayUI] Saved {all.Count} replays to {path}");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[ReplayUI] Download all failed: {ex.Message}");
            }
        }

        private void OnOpenExportFolderClicked()
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "export");

            if (System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start(dir);
        }

        // -- Config tab: Clear all (two-click confirm) --

        private void OnClearAllClicked()
        {
            if (!_clearAllPending)
            {
                _clearAllPending = true;
                ShowButtonFeedback(_clearAllCfgLbl, "Are you sure?", UIStyle.Red);
                if (_clearAllCfgBg != null) _clearAllCfgBg.color = UIStyle.Red with { a = 0.55f };
                return;
            }

            PBManager.DeleteAll();
            InvalidateDownloadStates();
            _selectedScene = null;
            _clearAllPending = false;
            RefreshCurrentView();
            Log.LogInfo("[ReplayUI] All replays cleared");
        }

        /// <summary>Reverts every transient button state (confirm arms and
        /// feedback flashes) back to idle. Called on unpause.</summary>
        private void ResetClearAllConfirm()
        {
            _clearAllPending = false;
            ShowButtonFeedback(_clearAllCfgLbl, "Clear all data", UIStyle.Red);
            if (_clearAllCfgBg != null) _clearAllCfgBg.color = UIStyle.BtnBg(UIStyle.Red);
            ShowButtonFeedback(_copyAllCfgLbl, "Copy all", UIStyle.Accent);
            if (_copyAllCfgBg != null) _copyAllCfgBg.color = UIStyle.BtnBg(UIStyle.Accent);

            // The jump buttons live in the persistent left footer (never
            // rebuilt), so their "No PB"/"Not in a room" feedback would
            // otherwise stick across close/reopen forever.
            ResetJumpFeedback();
            ResetJumpLastFeedback();
        }

        private static void ShowButtonFeedback(Text? label, string msg, Color color)
        {
            if (label != null) { label.text = msg; label.color = color; }
        }

        // -- Scene-level actions --

        private void OnExportSceneClicked()
        {
            if (_selectedScene == null)
            {
                ShowPasteStatus("Select a room first", UIStyle.Subtext);
                return;
            }
            var entries = PBManager.AllPBs()
                .Where(p => p.Key.SceneName == _selectedScene)
                .Select(p => p.Value)
                .ToList();
            if (entries.Count == 0)
            {
                ShowPasteStatus("No entries", UIStyle.Subtext);
                return;
            }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.EncodeCollection(entries);
            ShowPasteStatus(entries.Count + " routes copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Exported {entries.Count} routes for {_selectedScene}");
        }

        private void OnClearSceneClicked()
        {
            if (_selectedScene == null) return;

            // Two-click confirm: deleting every run in the room is too
            // destructive for a single click.
            if (!_sceneClearPending)
            {
                _sceneClearPending = true;
                if (_sceneClearLbl != null)
                {
                    _sceneClearLbl.text = "Sure?";
                    _sceneClearLbl.color = UIStyle.Text;
                }
                if (_sceneClearBg != null)
                    _sceneClearBg.color = UIStyle.Red with { a = 0.55f };
                return;
            }

            PBManager.DeleteScene(_selectedScene);
            InvalidateDownloadStates();
            ClearSelectedScene();
            RebuildSceneList();
            ResetSceneClearConfirm();
        }

        // -- Paste --

        private void OnPasteClicked()
        {
            string clip = GUIUtility.systemCopyBuffer ?? "";
            if (string.IsNullOrEmpty(clip))
            {
                ShowPasteStatus("Clipboard empty", UIStyle.Red);
                return;
            }

            // Share code / link → resolve via the server, then import.
            if (ReplaySharing.TryExtractCode(clip, out string code))
            {
                if (_networkClient == null || !_networkClient.IsStarted)
                {
                    ShowPasteStatus("Go online to import links", UIStyle.Red);
                    return;
                }

                ShowPasteStatus("Resolving...", UIStyle.Subtext);
                _networkClient.ResolveShare(code, replayBytes =>
                {
                    if (replayBytes == null || replayBytes.Length == 0)
                    {
                        ShowPasteStatus("Link not found", UIStyle.Red);
                        return;
                    }

                    var room = ReplayShareEncoder.Decode(replayBytes);
                    if (room == null)
                    {
                        ShowPasteStatus("Invalid data", UIStyle.Red);
                        return;
                    }

                    var outcome = PBManager.ImportPB(room);
                    if (outcome == PBManager.ImportOutcome.RouteFull)
                    {
                        ShowPasteStatus("Route full - raise the per-route limit in Config",
                            UIStyle.Red);
                        Log.LogInfo($"[ReplayUI] Resolved share {code} but route is full");
                        return;
                    }

                    SelectScene(room.Key.SceneName);

                    bool ok = outcome == PBManager.ImportOutcome.Imported;
                    ShowPasteStatus(ok ? room.Key.SceneName : "Duplicate replay",
                        ok ? UIStyle.Gold : UIStyle.Subtext);
                    Log.LogInfo($"[ReplayUI] Resolved share {code}: " +
                        (ok ? "imported" : "duplicate"));
                });
                return;
            }

            var rooms = ReplayShareEncoder.DecodeShareString(clip);
            if (rooms.Count == 0)
            {
                ShowPasteStatus("Invalid data", UIStyle.Red);
                return;
            }

            int imported = 0, duplicates = 0, full = 0;
            foreach (var room in rooms)
            {
                switch (PBManager.ImportPB(room))
                {
                    case PBManager.ImportOutcome.Imported:  imported++;   break;
                    case PBManager.ImportOutcome.Duplicate: duplicates++; break;
                    case PBManager.ImportOutcome.RouteFull: full++;       break;
                }
            }

            SelectScene(rooms[0].Key.SceneName);

            string status;
            if (rooms.Count == 1)
                status = imported > 0 ? rooms[0].Key.SceneName
                    : full > 0 ? "Route full - not saved"
                    : "Duplicate replay";
            else
            {
                status = imported > 0 ? imported + " imported" : "No new replays";
                if (duplicates > 0 || full > 0)
                {
                    status += " (";
                    if (duplicates > 0) status += duplicates + " dup";
                    if (duplicates > 0 && full > 0) status += ", ";
                    if (full > 0) status += full + " full";
                    status += ")";
                }
            }

            ShowPasteStatus(status,
                imported > 0 ? UIStyle.Gold
                : full > 0 ? UIStyle.Red
                : UIStyle.Subtext);
            Log.LogInfo($"[ReplayUI] Pasted {rooms.Count} replay(s): {imported} imported, {duplicates} duplicates, {full} full");
        }

        private void ShowPasteStatus(string msg, Color color)
        {
            if (_pasteStatusLbl != null)
            {
                _pasteStatusLbl.text = msg;
                _pasteStatusLbl.color = color;
            }
        }

        // -- Snapshot actions --

        private void CopyReplay(RoomKey key, string snapshotId)
        {
            var snapshot = PBManager.GetSnapshot(key, snapshotId);
            if (snapshot == null)
            {
                Log.LogWarning($"[ReplayUI] No snapshot for {key}#{snapshotId}");
                return;
            }

            // 1. Already have a code → copy instantly, no network round trip.
            if (!string.IsNullOrEmpty(snapshot.ShareCode))
            {
                GUIUtility.systemCopyBuffer =
                    ReplaySharing.BuildShareText(snapshot.ShareCode!);
                Log.LogInfo($"[ReplayUI] Copied share code for {key}#{snapshotId}");
                return;
            }

            // 2. Online → mint a code (by run id if uploaded, else by data),
            //    cache it on the snapshot, then copy.
            if (_networkClient != null && _networkClient.IsStarted
                && GhostSettings.OnlineEnabled)
            {
                System.Action<ShareResponse?> onShared = resp =>
                {
                    if (resp != null && resp.HasCode)
                    {
                        PBManager.SetServerIds(key, snapshotId, null, resp.Code);
                        GUIUtility.systemCopyBuffer =
                            ReplaySharing.BuildShareText(resp.Code);
                        Log.LogInfo($"[ReplayUI] Shared {key}#{snapshotId} as {resp.Code}");
                    }
                    else
                    {
                        // Fall back to the full blob so the user still gets something.
                        GUIUtility.systemCopyBuffer = snapshot.EncodedData;
                        Log.LogWarning($"[ReplayUI] Share failed for {key}#{snapshotId}; copied full replay");
                    }
                };

                if (!string.IsNullOrEmpty(snapshot.ServerRunId))
                    _networkClient.CreateShareByRunId(snapshot.ServerRunId!, onShared);
                else
                    _networkClient.CreateShareByData(snapshot, onShared);
                return;
            }

            // 3. Offline → copy the full self-contained blob (legacy behavior).
            GUIUtility.systemCopyBuffer = snapshot.EncodedData;
            Log.LogInfo($"[ReplayUI] Offline: copied full replay for {key}#{snapshotId}");
        }

        /// <summary>
        /// Leaderboard "copy link": mints a share code for a run id and copies it.
        /// Optionally flashes a label with the outcome.
        /// </summary>
        private void OnCopyLinkClicked(string runId, Text? feedback)
        {
            if (_networkClient == null || !_networkClient.IsStarted
                || string.IsNullOrEmpty(runId))
                return;

            if (feedback != null) feedback.text = "...";

            _networkClient.CreateShareByRunId(runId, resp =>
            {
                if (resp != null && resp.HasCode)
                {
                    GUIUtility.systemCopyBuffer = ReplaySharing.BuildShareText(resp.Code);
                    if (feedback != null)
                    {
                        feedback.text = "Copied";
                        feedback.color = UIStyle.Gold;
                    }
                    Log.LogInfo($"[ReplayUI] Copied link for run {runId}: {resp.Code}");
                }
                else if (feedback != null)
                {
                    feedback.text = "Failed";
                    feedback.color = UIStyle.Red;
                }
            });
        }

        private void DeleteSnapshot(RoomKey key, string snapshotId)
        {
            PBManager.DeleteSnapshot(key, snapshotId);
            InvalidateDownloadStates();
            RebuildSceneList();
            if (_selectedScene != null && _activeTab == TabKind.Runs)
                RebuildRightContent();
        }

        private void DeleteRoute(RoomKey key)
        {
            PBManager.DeletePB(key);
            InvalidateDownloadStates();
            RebuildSceneList();
            if (_selectedScene != null && _activeTab == TabKind.Runs)
                RebuildRightContent();
        }

        // -- Route warp --
        // Warps to the previous room (EntryFromScene) at a door that leads
        // into the run room, placing the player right before the transition
        // that starts the run. The button only renders when CanWarp is true,
        // so the null path here is just defensive.

        private void OnRouteWarpClicked(RoomKey key)
        {
            if (!QuickWarp.WarpToRoute(key))
                Log.LogInfo($"[ReplayUI] Warp unavailable for {key}");
        }

        private void ToggleSnapshotPlayback(RoomKey key, string snapshotId)
        {
            SelectionState?.TogglePlayback(snapshotId);
            if (_activeTab == TabKind.Runs && _selectedScene == key.SceneName)
                RebuildRunsContentOnly();
        }

        /// <summary>
        /// Experimental camera-follow toggle for a run. Following implies
        /// playback: engaging the slot also enables the run's ghost so
        /// there is always something for the camera to track. Takes effect
        /// immediately if that ghost is already playing, otherwise on the
        /// next entry into its room.
        /// </summary>
        private void OnCameraFollowClicked(RoomKey key, string snapshotId)
        {
            if (SelectionState == null)
                return;

            if (SelectionState.ToggleCameraFollow(snapshotId))
                SelectionState.SetPlaybackSelected(snapshotId, true);
            if (_activeTab == TabKind.Runs && _selectedScene == key.SceneName)
                RebuildRunsContentOnly();
        }

        // -- Jump navigation --

        private void OnJumpToCurrentClicked()
        {
            string scene = RoomTracker.CurrentScene;
            if (string.IsNullOrEmpty(scene))
            {
                ShowButtonFeedback(_jumpCurrentLbl, "Not in a room", UIStyle.Subtext);
                return;
            }
            if (!PBManager.AllPBs().Any(p => p.Key.SceneName == scene))
            {
                ShowButtonFeedback(_jumpCurrentLbl, "No PB", UIStyle.Subtext);
                return;
            }

            ResetJumpFeedback();
            if (_activeTab != TabKind.Runs)
                SwitchTab(TabKind.Runs);
            SelectScene(scene);
            ScrollToScene(scene);
        }

        private void OnJumpToLastClicked()
        {
            string scene = RoomTracker.PreviousScene;
            if (string.IsNullOrEmpty(scene))
            {
                ShowButtonFeedback(_jumpPreviousLbl, "No previous", UIStyle.Subtext);
                return;
            }
            if (!PBManager.AllPBs().Any(p => p.Key.SceneName == scene))
            {
                ShowButtonFeedback(_jumpPreviousLbl, "No PB", UIStyle.Subtext);
                return;
            }

            ResetJumpLastFeedback();
            if (_activeTab != TabKind.Runs)
                SwitchTab(TabKind.Runs);
            SelectScene(scene);
            ScrollToScene(scene);
        }

        private void ResetJumpFeedback()
        {
            if (_jumpCurrentLbl != null) { _jumpCurrentLbl.text = "Current"; _jumpCurrentLbl.color = UIStyle.Gold; }
            if (_jumpCurrentBg != null) _jumpCurrentBg.color = UIStyle.BtnBg(UIStyle.Gold);
        }

        private void ResetJumpLastFeedback()
        {
            if (_jumpPreviousLbl != null) { _jumpPreviousLbl.text = "Previous"; _jumpPreviousLbl.color = UIStyle.Accent; }
            if (_jumpPreviousBg != null) _jumpPreviousBg.color = UIStyle.BtnBg(UIStyle.Accent);
        }

        // -- Settings toggles --

        private void OnTrackingToggle()
        {
            GhostSettings.TrackingEnabled = !GhostSettings.TrackingEnabled;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnGhostToggle()
        {
            GhostSettings.GhostEnabled = !GhostSettings.GhostEnabled;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSavePolicyToggle()
        {
            GhostSettings.SaveAllRunsEnabled = !GhostSettings.SaveAllRunsEnabled;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnOnlineToggle()
        {
            bool enabling = !GhostSettings.OnlineEnabled;
            GhostSettings.OnlineEnabled = enabling;
            _onOnlineToggle?.Invoke(enabling);
            // Rebuild (not just refresh) because the name input row is
            // conditionally created based on OnlineEnabled.
            if (_activeTab == TabKind.Config) RebuildRightContent();
        }

        private void OnMaxSavedReplaysMinus() => AdjustMaxSaved(-1);
        private void OnMaxSavedReplaysPlus() => AdjustMaxSaved(1);

        private void AdjustMaxSaved(int delta)
        {
            GhostSettings.MaxSavedReplaysPerRoute += delta;
            PBManager.PruneAllHistories(GhostSettings.MaxSavedReplaysPerRoute, persist: true);
            if (_activeTab == TabKind.Config) RefreshConfigValues();
            if (_activeTab == TabKind.Runs && _selectedScene != null)
                RebuildRightContent();
        }

        private void OnTimerToggleClicked()
        {
            GhostSettings.TimerHudEnabled = !GhostSettings.TimerHudEnabled;
            if (!GhostSettings.TimerHudEnabled) _timerHud?.Disarm();
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnChainTimersToggleClicked()
        {
            GhostSettings.ChainRoomTimers = !GhostSettings.ChainRoomTimers;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSkipBacktrackRunsToggle()
        {
            GhostSettings.SkipBacktrackRuns = !GhostSettings.SkipBacktrackRuns;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSkipBacktrackTimerToggle()
        {
            GhostSettings.SkipBacktrackTimer = !GhostSettings.SkipBacktrackTimer;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        /// <summary>
        /// Experimental room-warp toggle. Warp buttons in the Runs and
        /// Leaderboard tabs only render when this is on, so no extra rebuild
        /// is needed here - those tabs rebuild on switch.
        /// </summary>
        private void OnRoomWarpToggle()
        {
            GhostSettings.RoomWarpEnabled = !GhostSettings.RoomWarpEnabled;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        /// <summary>
        /// Experimental camera-follow feature toggle. Turning it off
        /// releases the camera on the next ghost tick (GhostPlayback
        /// reconciles the follow slot against this setting every frame);
        /// the Runs tab camera buttons only render while it is on.
        /// </summary>
        private void OnCameraFollowFeatureToggle()
        {
            GhostSettings.CameraFollowEnabled = !GhostSettings.CameraFollowEnabled;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }
    }
}