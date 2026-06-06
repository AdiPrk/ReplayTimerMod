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

            var rooms = ReplayShareEncoder.DecodeShareString(clip);
            if (rooms.Count == 0)
            {
                ShowPasteStatus("Invalid data", UIStyle.Red);
                return;
            }

            int imported = 0, duplicates = 0;
            foreach (var room in rooms)
            {
                if (PBManager.ImportPB(room)) imported++;
                else duplicates++;
            }

            selectedScene = rooms[0].Key.SceneName;
            RebuildSceneList();
            UpdateRightSubHeader();
            RebuildRightContent();

            string status;
            if (rooms.Count == 1)
                status = imported > 0 ? rooms[0].Key.SceneName : "Duplicate replay";
            else
            {
                status = imported > 0 ? imported + " imported" : "No new replays";
                if (duplicates > 0) status += " (" + duplicates + " dup)";
            }

            ShowPasteStatus(status, imported > 0 ? UIStyle.Gold : UIStyle.Subtext);
            Log.LogInfo($"[ReplayUI] Pasted {rooms.Count} replay(s): {imported} imported, {duplicates} duplicates");
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
            var snapshot = PBManager.GetHistory(key)
                .FirstOrDefault(s => s.SnapshotId == snapshotId);
            if (snapshot == null)
            {
                Log.LogWarning($"[ReplayUI] No snapshot for {key}#{snapshotId}");
                return;
            }
            GUIUtility.systemCopyBuffer = snapshot.EncodedData;
            Log.LogInfo($"[ReplayUI] Copied {key}#{snapshotId}");
        }

        private void DeleteSnapshot(RoomKey key, string snapshotId)
        {
            PBManager.DeleteSnapshot(key, snapshotId);
            RebuildSceneList();
            if (selectedScene != null && activeTab == TabKind.Runs)
                RebuildRightContent();
        }

        private void DeleteRoute(RoomKey key)
        {
            PBManager.DeletePB(key);
            RebuildSceneList();
            if (selectedScene != null && activeTab == TabKind.Runs)
                RebuildRightContent();
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
    }
}