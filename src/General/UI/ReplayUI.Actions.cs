using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
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
            _selectedScene = null;
            _clearAllPending = false;
            RefreshCurrentView();
            Log.LogInfo("[ReplayUI] All replays cleared");
        }

        private void ResetClearAllConfirm()
        {
            _clearAllPending = false;
            ShowButtonFeedback(_clearAllCfgLbl, "Clear all data", UIStyle.Red);
            if (_clearAllCfgBg != null) _clearAllCfgBg.color = UIStyle.BtnBg(UIStyle.Red);
            ShowButtonFeedback(_copyAllCfgLbl, "Copy all", UIStyle.Accent);
            if (_copyAllCfgBg != null) _copyAllCfgBg.color = UIStyle.BtnBg(UIStyle.Accent);

            ResetJumpFeedback();
            ResetJumpLastFeedback();
        }

        private static void ShowButtonFeedback(Text? label, string msg, Color color)
        {
            if (label != null) { label.text = msg; label.color = color; }
        }

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
            ClearSelectedScene();
            RebuildSceneList();
            ResetSceneClearConfirm();
        }

        private void OnPasteClicked()
        {
            string clip = GUIUtility.systemCopyBuffer ?? "";
            if (string.IsNullOrEmpty(clip))
            {
                ShowPasteStatus("Clipboard empty", UIStyle.Red);
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

        private void CopyReplay(RoomKey key, string snapshotId)
        {
            var snapshot = PBManager.GetSnapshot(key, snapshotId);
            if (snapshot == null)
            {
                Log.LogWarning($"[ReplayUI] No snapshot for {key}#{snapshotId}");
                return;
            }

            GUIUtility.systemCopyBuffer = snapshot.EncodedData;
            Log.LogInfo($"[ReplayUI] Copied full replay for {key}#{snapshotId}");
        }

        private void DeleteSnapshot(RoomKey key, string snapshotId)
        {
            PBManager.DeleteSnapshot(key, snapshotId);
            RebuildSceneList();
            if (_selectedScene != null && _activeTab == TabKind.Runs)
                RebuildRightContent();
        }

        private void DeleteRoute(RoomKey key)
        {
            PBManager.DeletePB(key);
            RebuildSceneList();
            if (_selectedScene != null && _activeTab == TabKind.Runs)
                RebuildRightContent();
        }

#if SILKSONG_BUILD

        private void OnRouteWarpClicked(RoomKey key)
        {
            if (!QuickWarp.WarpToRoute(key))
                Log.LogInfo($"[ReplayUI] Warp unavailable for {key}");
        }
#endif

        private void ToggleSnapshotPlayback(RoomKey key, string snapshotId)
        {
            SelectionState?.TogglePlayback(snapshotId);
            if (_activeTab == TabKind.Runs && _selectedScene == key.SceneName)
                RebuildRunsContentOnly();
        }

        private void OnCameraFollowClicked(RoomKey key, string snapshotId)
        {
            if (SelectionState == null)
                return;

            if (SelectionState.ToggleCameraFollow(snapshotId))
                SelectionState.SetPlaybackSelected(snapshotId, true);
            if (_activeTab == TabKind.Runs && _selectedScene == key.SceneName)
                RebuildRunsContentOnly();
        }

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

        private void OnCheatCancelToggle()
        {
            GhostSettings.CancelRunOnCheats = !GhostSettings.CancelRunOnCheats;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSkipBacktrackTimerToggle()
        {
            GhostSettings.SkipBacktrackTimer = !GhostSettings.SkipBacktrackTimer;
            if (_activeTab == TabKind.Config) RefreshConfigValues();
        }
    }
}
