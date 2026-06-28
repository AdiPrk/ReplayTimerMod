using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // -- Config tab: Copy all --

        private void OnExportAllClicked()
        {
            var all = PBManager.AllPBs().Select(p => p.Value).ToList();
            if (all.Count == 0)
            {
                ShowButtonFeedback(exportAllCfgLbl, exportAllCfgBg, "Nothing to copy", UIStyle.Subtext);
                return;
            }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.EncodeCollection(all);
            ShowButtonFeedback(exportAllCfgLbl, exportAllCfgBg, all.Count + " copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Copied {all.Count} replays to clipboard");
        }

        // -- Config tab: Export All (save to disk) --

        private void OnDownloadAllClicked()
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
            if (!clearAllPending)
            {
                clearAllPending = true;
                ShowButtonFeedback(clearAllCfgLbl, clearAllCfgBg, "Are you sure?", UIStyle.Red);
                if (clearAllCfgBg != null) clearAllCfgBg.color = UIStyle.Red with { a = 0.55f };
                return;
            }

            PBManager.DeleteAll();
            InvalidateDownloadStates();
            selectedScene = null;
            clearAllPending = false;
            RefreshCurrentView();
            Log.LogInfo("[ReplayUI] All replays cleared");
        }

        private void ResetClearAllConfirm()
        {
            clearAllPending = false;
            ShowButtonFeedback(clearAllCfgLbl, clearAllCfgBg, "Clear all data", UIStyle.Red);
            if (clearAllCfgBg != null) clearAllCfgBg.color = UIStyle.Red with { a = 0.15f };
            ShowButtonFeedback(exportAllCfgLbl, exportAllCfgBg, "Copy all", UIStyle.Accent);
            if (exportAllCfgBg != null) exportAllCfgBg.color = UIStyle.Accent with { a = 0.18f };
        }

        private static void ShowButtonFeedback(Text? label, Image? bg, string msg, Color color)
        {
            if (label != null) { label.text = msg; label.color = color; }
        }

        // -- Scene-level actions --

        private void OnExportSceneClicked()
        {
            if (selectedScene == null)
            {
                ShowPasteStatus("Select a room first", UIStyle.Subtext);
                return;
            }
            var entries = PBManager.AllPBs()
                .Where(p => p.Key.SceneName == selectedScene)
                .Select(p => p.Value)
                .ToList();
            if (entries.Count == 0)
            {
                ShowPasteStatus("No entries", UIStyle.Subtext);
                return;
            }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.EncodeCollection(entries);
            ShowPasteStatus(entries.Count + " routes copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Exported {entries.Count} routes for {selectedScene}");
        }

        private void OnClearSceneClicked()
        {
            if (selectedScene == null) return;
            PBManager.DeleteScene(selectedScene);
            InvalidateDownloadStates();
            selectedScene = null;
            ClearSelectedScene();
            RebuildSceneList();
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

                ShowPasteStatus("Resolving\u2026", UIStyle.Subtext);
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
                        ShowPasteStatus("Route full \u2014 raise the per-route limit in Config",
                            UIStyle.Red);
                        Log.LogInfo($"[ReplayUI] Resolved share {code} but route is full");
                        return;
                    }

                    selectedScene = room.Key.SceneName;
                    RebuildSceneList();
                    UpdateRightSubHeader();
                    RebuildRightContent();

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

            selectedScene = rooms[0].Key.SceneName;
            RebuildSceneList();
            UpdateRightSubHeader();
            RebuildRightContent();

            string status;
            if (rooms.Count == 1)
                status = imported > 0 ? rooms[0].Key.SceneName
                    : full > 0 ? "Route full \u2014 not saved"
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
            if (pasteStatusLbl != null)
            {
                pasteStatusLbl.text = msg;
                pasteStatusLbl.color = color;
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

            if (feedback != null) feedback.text = "\u2026"; // …

            _networkClient.CreateShareByRunId(runId, resp =>
            {
                if (resp != null && resp.HasCode)
                {
                    GUIUtility.systemCopyBuffer = ReplaySharing.BuildShareText(resp.Code);
                    if (feedback != null)
                    {
                        feedback.text = "\u2713"; // ✓
                        feedback.color = UIStyle.Gold;
                    }
                    Log.LogInfo($"[ReplayUI] Copied link for run {runId}: {resp.Code}");
                }
                else if (feedback != null)
                {
                    feedback.text = "\u2717"; // ✗
                    feedback.color = UIStyle.Red;
                }
            });
        }

        private void DeleteSnapshot(RoomKey key, string snapshotId)
        {
            PBManager.DeleteSnapshot(key, snapshotId);
            InvalidateDownloadStates();
            RebuildSceneList();
            if (selectedScene != null && activeTab == TabKind.Runs)
                RebuildRightContent();
        }

        private void DeleteRoute(RoomKey key)
        {
            PBManager.DeletePB(key);
            InvalidateDownloadStates();
            RebuildSceneList();
            if (selectedScene != null && activeTab == TabKind.Runs)
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

        private void SelectSnapshotForEditing(RoomKey key, string snapshotId)
        {
            var snapshot = PBManager.GetSnapshot(key, snapshotId);
            if (snapshot == null) return;

            if (!snapshot.HasVisualOverride)
            {
                Color color = snapshot.ResolveGhostColor(CurrentGlobalGhostColor);
                if (!PBManager.UpdateSnapshotVisuals(key, snapshotId, true, color))
                    return;
            }

            SelectionState?.SelectSnapshot(snapshotId);
            if (activeTab == TabKind.Runs && selectedScene == key.SceneName)
                RebuildRightContent();
            if (activeTab == TabKind.Config)
                RefreshConfigValues();
        }

        private void ToggleSnapshotPlayback(RoomKey key, string snapshotId)
        {
            SelectionState?.TogglePlayback(snapshotId);
            if (activeTab == TabKind.Runs && selectedScene == key.SceneName)
                RebuildRightContent();
        }

        // -- Jump navigation --

        private void OnJumpToCurrentClicked()
        {
            string scene = RoomTracker.CurrentScene;
            if (string.IsNullOrEmpty(scene))
            {
                ShowButtonFeedback(jumpCurrentLbl, jumpCurrentBg, "Not in a room", UIStyle.Subtext);
                return;
            }
            if (!PBManager.AllPBs().Any(p => p.Key.SceneName == scene))
            {
                ShowButtonFeedback(jumpCurrentLbl, jumpCurrentBg, "No PB", UIStyle.Subtext);
                return;
            }

            ResetJumpFeedback();
            if (activeTab != TabKind.Runs)
                SwitchTab(TabKind.Runs);
            SelectScene(scene);
            ScrollToScene(scene);
        }

        private void OnJumpToLastClicked()
        {
            string scene = RoomTracker.PreviousScene;
            if (string.IsNullOrEmpty(scene))
            {
                ShowButtonFeedback(jumpPreviousLbl, jumpPreviousBg, "No previous", UIStyle.Subtext);
                return;
            }
            if (!PBManager.AllPBs().Any(p => p.Key.SceneName == scene))
            {
                ShowButtonFeedback(jumpPreviousLbl, jumpPreviousBg, "No PB", UIStyle.Subtext);
                return;
            }

            ResetJumpLastFeedback();
            if (activeTab != TabKind.Runs)
                SwitchTab(TabKind.Runs);
            SelectScene(scene);
            ScrollToScene(scene);
        }

        private void ResetJumpFeedback()
        {
            if (jumpCurrentLbl != null) { jumpCurrentLbl.text = "Current"; jumpCurrentLbl.color = UIStyle.Gold; }
            if (jumpCurrentBg != null) jumpCurrentBg.color = UIStyle.Gold with { a = 0.18f };
        }

        private void ResetJumpLastFeedback()
        {
            if (jumpPreviousLbl != null) { jumpPreviousLbl.text = "Previous"; jumpPreviousLbl.color = UIStyle.Accent; }
            if (jumpPreviousBg != null) jumpPreviousBg.color = UIStyle.Accent with { a = 0.18f };
        }

        // -- Settings toggles --

        private void OnTrackingToggle()
        {
            GhostSettings.TrackingEnabled = !GhostSettings.TrackingEnabled;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnGhostToggle()
        {
            GhostSettings.GhostEnabled = !GhostSettings.GhostEnabled;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSavePolicyToggle()
        {
            GhostSettings.SaveAllRunsEnabled = !GhostSettings.SaveAllRunsEnabled;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnOnlineToggle()
        {
            bool enabling = !GhostSettings.OnlineEnabled;
            GhostSettings.OnlineEnabled = enabling;
            _onOnlineToggle?.Invoke(enabling);
            // Rebuild (not just refresh) because the name input row is
            // conditionally created based on OnlineEnabled.
            if (activeTab == TabKind.Config) RebuildRightContent();
        }

        private void OnMaxSavedReplaysMinus() => AdjustMaxSaved(-1);
        private void OnMaxSavedReplaysPlus() => AdjustMaxSaved(1);

        private void AdjustMaxSaved(int delta)
        {
            GhostSettings.MaxSavedReplaysPerRoute += delta;
            PBManager.PruneAllHistories(GhostSettings.MaxSavedReplaysPerRoute, persist: true);
            if (activeTab == TabKind.Config) RefreshConfigValues();
            if (activeTab == TabKind.Runs && selectedScene != null)
                RebuildRightContent();
        }

        private void OnEditGlobalContext()
        {
            SelectionState?.SelectSnapshot(null);
            if (activeTab == TabKind.Config) RefreshConfigValues();
            if (activeTab == TabKind.Runs && selectedScene != null)
                RebuildRightContent();
        }

        private void OnAlphaMinus() => AdjustAlpha(-0.05f);
        private void OnAlphaPlus() => AdjustAlpha(0.05f);

        private void AdjustAlpha(float delta)
        {
            if (TryGetSelectedSnapshot(out var key, out var snapshot) && snapshot != null)
            {
                if (!snapshot.HasVisualOverride) return;

                Color color = snapshot.ResolveGhostColor(CurrentGlobalGhostColor);
                color.a = Mathf.Clamp01(Mathf.Round((color.a + delta) * 20f) / 20f);
                PBManager.UpdateSnapshotVisuals(key, snapshot.SnapshotId, true, color);
            }
            else
            {
                GhostSettings.GhostAlpha = Mathf.Round((GhostSettings.GhostAlpha + delta) * 20f) / 20f;
            }

            if (activeTab == TabKind.Config) RefreshConfigValues();
            if (activeTab == TabKind.Runs && selectedScene != null)
                RebuildRightContent();
        }

        private void OnColorSwatch(Color rgb)
        {
            if (TryGetSelectedSnapshot(out var key, out var snapshot) && snapshot != null)
            {
                if (!snapshot.HasVisualOverride) return;

                Color color = snapshot.ResolveGhostColor(CurrentGlobalGhostColor);
                color.r = rgb.r;
                color.g = rgb.g;
                color.b = rgb.b;
                PBManager.UpdateSnapshotVisuals(key, snapshot.SnapshotId, true, color);
            }
            else
            {
                GhostSettings.GhostColor = new Color(rgb.r, rgb.g, rgb.b, GhostSettings.GhostAlpha);
            }

            if (activeTab == TabKind.Config) RefreshConfigValues();
            if (activeTab == TabKind.Runs && selectedScene != null)
                RebuildRightContent();
        }

        private void OnTimerToggleClicked()
        {
            GhostSettings.TimerHudEnabled = !GhostSettings.TimerHudEnabled;
            if (!GhostSettings.TimerHudEnabled) timerHud?.Disarm();
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnChainTimersToggleClicked()
        {
            GhostSettings.ChainRoomTimers = !GhostSettings.ChainRoomTimers;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSkipBacktrackRunsToggle()
        {
            GhostSettings.SkipBacktrackRuns = !GhostSettings.SkipBacktrackRuns;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }

        private void OnSkipBacktrackTimerToggle()
        {
            GhostSettings.SkipBacktrackTimer = !GhostSettings.SkipBacktrackTimer;
            if (activeTab == TabKind.Config) RefreshConfigValues();
        }
    }
}