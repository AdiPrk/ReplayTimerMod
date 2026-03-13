using UnityEngine;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
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

        // ── Global clear-all (header) — two-click confirm ─────────────────────
        // First click: button text → "Are you sure?" and background brightens.
        // Second click: deletes all replays and resets.
        // Resets to default whenever the panel is closed or game unpauses.

        private void OnClearAllClicked()
        {
            if (!clearAllPending)
            {
                clearAllPending = true;
                if (clearAllBtnLbl != null) clearAllBtnLbl.text  = "Are you sure?";
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
            if (clearAllBtnLbl != null) clearAllBtnLbl.text  = "Clear all";
            if (clearAllBtnImg != null) clearAllBtnImg.color = UIStyle.Red with { a = 0.22f };
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
            pasteStatus.text  = msg;
            pasteStatus.color = color;
        }

        // ── Ghost settings ────────────────────────────────────────────────────

        private void OnGhostToggle()
        {
            GhostSettings.GhostEnabled = !GhostSettings.GhostEnabled;
            if (ghostToggleLbl == null) return;
            ghostToggleLbl.text  = GhostSettings.GhostEnabled ? "ON" : "OFF";
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
