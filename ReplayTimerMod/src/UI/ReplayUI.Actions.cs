using System.Linq;
using UnityEngine;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Copy all (header) - clipboard ────────────────────────────────────

        private void OnExportAllClicked()
        {
            var all = PBManager.AllPBs().Select(p => p.Value).ToList();
            if (all.Count == 0)
            {
                ShowExportFeedback("Nothing to copy", UIStyle.Subtext);
                return;
            }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.EncodeCollection(all);
            ShowExportFeedback($"✓ {all.Count} copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Copied {all.Count} replays to clipboard");
        }

        // ── Download all (header) - writes file to disk ───────────────────────

        private void OnDownloadAllClicked()
        {
            var all = PBManager.AllPBs().Select(p => p.Value).ToList();
            if (all.Count == 0)
            {
                ShowDownloadFeedback("Nothing to save", UIStyle.Subtext);
                return;
            }

            try
            {
                string dir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                    "export");
                System.IO.Directory.CreateDirectory(dir);

                string datePart = System.DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                string countPart = $"{all.Count}";
                string fileName = $"Re_{datePart}_{countPart}.rtmc.txt";

                string path = System.IO.Path.Combine(dir, fileName);

                System.IO.File.WriteAllText(path, ReplayShareEncoder.EncodeCollection(all));

                ShowDownloadFeedback($"Saved {all.Count} to /export/", UIStyle.Accent);
                Log.LogInfo($"[ReplayUI] Saved {all.Count} replays to {path}");
            }
            catch (System.Exception ex)
            {
                ShowDownloadFeedback("Save failed", UIStyle.Red);
                Log.LogError($"[ReplayUI] Download all failed: {ex.Message}");
            }
        }

        // Opens the export folder in Windows Explorer / File Browser
        private void OnOpenExportFolderClicked()
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location)!,
                "export");

            if (System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(dir);
            }
            else
            {
                ShowDownloadFeedback("Nothing Exported", UIStyle.Red);
            }
        }

        private void ShowExportFeedback(string msg, Color color)
        {
            if (exportAllBtnLbl == null) return;
            exportAllBtnLbl.text = msg;
            exportAllBtnLbl.color = color;
            if (exportAllBtnImg != null) exportAllBtnImg.color = color with { a = 0.30f };
        }

        private void ShowDownloadFeedback(string msg, Color color)
        {
            if (downloadAllBtnLbl == null) return;
            downloadAllBtnLbl.text = msg;
            downloadAllBtnLbl.color = color;
            if (downloadAllBtnImg != null) downloadAllBtnImg.color = color with { a = 0.30f };
        }

        // ── Export scene (sub-header) ─────────────────────────────────────────

        private void OnExportSceneClicked()
        {
            if (selectedScene == null)
            {
                ShowPasteStatus("Select a scene first", UIStyle.Subtext);
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
            ShowPasteStatus($"✓ {entries.Count} routes copied", UIStyle.Accent);
            Log.LogInfo($"[ReplayUI] Exported {entries.Count} routes for {selectedScene}");
        }

        // ── Per-entry ─────────────────────────────────────────────────────────

        private void CopyReplay(RoomKey key)
        {
            var pb = PBManager.GetPB(key);
            if (pb == null) { Log.LogWarning($"[ReplayUI] No PB for {key}"); return; }
            GUIUtility.systemCopyBuffer = ReplayShareEncoder.Encode(pb);
            Log.LogInfo($"[ReplayUI] Copied {key}");
        }

        private void DeleteEntry(RoomKey key)
        {
            PBManager.DeletePB(key);
            if (selectedScene != null) RebuildRight(selectedScene);
            RebuildLeft();
        }

        // ── Clear scene (sub-header) ──────────────────────────────────────────

        private void OnClearSceneClicked()
        {
            if (selectedScene == null) return;
            PBManager.DeleteScene(selectedScene);
            selectedScene = null;
            ClearRight();
            RebuildLeft();
        }

        // ── Global clear-all (header) - two-click confirm ─────────────────────

        private void OnClearAllClicked()
        {
            if (!clearAllPending)
            {
                clearAllPending = true;
                if (clearAllBtnLbl != null) clearAllBtnLbl.text = "Are you sure?";
                if (clearAllBtnImg != null) clearAllBtnImg.color = UIStyle.Red with { a = 0.55f };
            }
            else
            {
                PBManager.DeleteAll();
                selectedScene = null;
                ClearRight();
                RebuildLeft();
                ResetClearAllConfirm();
                Log.LogInfo("[ReplayUI] All replays cleared");
            }
        }

        private void ResetClearAllConfirm()
        {
            clearAllPending = false;
            if (clearAllBtnLbl != null) clearAllBtnLbl.text = "Clear all";
            if (clearAllBtnImg != null) clearAllBtnImg.color = UIStyle.Red with { a = 0.22f };
            if (exportAllBtnLbl != null) exportAllBtnLbl.text = "Copy all";
            if (exportAllBtnImg != null) exportAllBtnImg.color = UIStyle.Accent with { a = 0.22f };
            if (downloadAllBtnLbl != null) downloadAllBtnLbl.text = "Download all";
            if (downloadAllBtnImg != null) downloadAllBtnImg.color = UIStyle.Accent with { a = 0.15f };
        }

        // ── Paste ─────────────────────────────────────────────────────────────

        private void OnPasteClicked()
        {
            string clip = GUIUtility.systemCopyBuffer ?? "";
            if (string.IsNullOrWhiteSpace(clip))
            {
                ShowPasteStatus("✕ Clipboard empty", UIStyle.Red);
                return;
            }

            var collection = ReplayShareEncoder.DecodeCollection(clip);
            if (collection != null)
            {
                foreach (var r in collection)
                    PBManager.ImportPB(r);
                if (collection.Count > 0) selectedScene = collection[0].Key.SceneName;
                RebuildLeft();
                if (selectedScene != null) RebuildRight(selectedScene);
                ShowPasteStatus($"✓ {collection.Count} replays", UIStyle.Gold);
                Log.LogInfo($"[ReplayUI] Pasted collection: {collection.Count} replays");
                return;
            }

            RecordedRoom? room = null;
            try { room = ReplayShareEncoder.Decode(clip); }
            catch { }

            if (room == null)
            {
                ShowPasteStatus("✕ Invalid data", UIStyle.Red);
                return;
            }

            PBManager.ImportPB(room);
            selectedScene = room.Key.SceneName;
            RebuildLeft();
            RebuildRight(selectedScene);
            ShowPasteStatus($"✓ {room.Key.SceneName}", UIStyle.Gold);
            Log.LogInfo($"[ReplayUI] Pasted {room.Key}");
        }

        private void ShowPasteStatus(string msg, Color color)
        {
            if (pasteStatus == null) return;
            pasteStatus.text = msg;
            pasteStatus.color = color;
        }

        // ── Jump to current room (left sub-header) ────────────────────────────
        // Selects and scrolls to the scene the player is currently in.
        // Shows brief feedback on the button itself if no PB exists for it yet.

        private void OnJumpToCurrentClicked()
        {
            string scene = RoomTracker.CurrentScene;

            if (string.IsNullOrEmpty(scene))
            {
                ShowJumpFeedback("Not in a room", UIStyle.Subtext);
                return;
            }

            bool hasPB = PBManager.AllPBs().Any(p => p.Key.SceneName == scene);
            if (!hasPB)
            {
                ShowJumpFeedback($"No PB for {scene}", UIStyle.Subtext);
                return;
            }

            // Reset any feedback text before rebuilding (RebuildLeft recreates rows,
            // so the button label is not touched, but we want a clean state).
            ResetJumpFeedback();

            SelectScene(scene);
            ScrollToScene(scene);

            Log.LogInfo($"[ReplayUI] Jumped to current room: {scene}");
        }

        private void ShowJumpFeedback(string msg, Color color)
        {
            if (jumpToCurrentBtnLbl == null) return;
            jumpToCurrentBtnLbl.text = msg;
            jumpToCurrentBtnLbl.color = color;
            if (jumpToCurrentBtnImg != null)
                jumpToCurrentBtnImg.color = color with { a = 0.18f };
        }

        private void ResetJumpFeedback()
        {
            if (jumpToCurrentBtnLbl == null) return;
            jumpToCurrentBtnLbl.text = "● Go to current room";
            jumpToCurrentBtnLbl.color = UIStyle.Gold;
            if (jumpToCurrentBtnImg != null)
                jumpToCurrentBtnImg.color = UIStyle.Gold with { a = 0.18f };
        }

        // ── Ghost settings ────────────────────────────────────────────────────

        private void OnGhostToggle()
        {
            GhostSettings.GhostEnabled = !GhostSettings.GhostEnabled;
            if (ghostToggleLbl == null) return;
            ghostToggleLbl.text = GhostSettings.GhostEnabled ? "ON" : "OFF";
            ghostToggleLbl.color = GhostSettings.GhostEnabled ? UIStyle.Accent : UIStyle.Subtext;
        }

        private void OnAlphaMinus()
        {
            GhostSettings.GhostAlpha = Mathf.Round((GhostSettings.GhostAlpha - 0.05f) * 20f) / 20f;
            if (alphaLbl != null) alphaLbl.text = AlphaString();
        }

        private void OnAlphaPlus()
        {
            GhostSettings.GhostAlpha = Mathf.Round((GhostSettings.GhostAlpha + 0.05f) * 20f) / 20f;
            if (alphaLbl != null) alphaLbl.text = AlphaString();
        }

        private static void OnColorSwatch(Color rgb) =>
            GhostSettings.GhostColor = new Color(rgb.r, rgb.g, rgb.b, GhostSettings.GhostAlpha);

        private static string AlphaString() =>
            GhostSettings.GhostAlpha.ToString("0.00");
    }
}