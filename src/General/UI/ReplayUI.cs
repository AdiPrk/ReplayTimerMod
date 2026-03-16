using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ─────────────────────────────────────────────────────────────────────────
    // ReplayUI - pause-only two-column replay browser.
    //
    // Left column  : alphabetical scene list. Click to populate right column.
    //   Sub-header : [● Go to current room]
    //   Each row   : scene name (gold + ● prefix when it's the current room)
    // Right column : routes for the selected scene.
    //   Sub-header : scene name | [Paste] [Clear scene]
    //   Each row   : route  time  [Copy]  [✕]
    // Header       : "Replay Times" | [Clear all / Are you sure?] [-]
    // Bottom strip : Ghost [ON/OFF]  Alpha [−] 0.40 [+]  Color ■ ■ ■ ■ ■ ■
    //
    // [Clear all]  - two-click confirm; first click shows "Are you sure?",
    //               second click deletes every replay across all scenes.
    //               Resets to default state whenever the panel is closed.
    // [Clear scene]- immediately deletes all entries for the selected scene.
    // [Paste]      - reads clipboard, decodes RTM3 string, imports replay.
    // [Copy]       - encodes entry to RTM3 string, writes to clipboard.
    // [✕]          - deletes that single entry.
    //
    // Canvas is enabled only while the game is paused.
    // ─────────────────────────────────────────────────────────────────────────
    public partial class ReplayUI
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayUI");

        // ── State ─────────────────────────────────────────────────────────────
        private bool isSetup = false;
        private bool expanded = false;
        private string? selectedScene = null;

        // Set by OnPBUpdated() whenever a new PB is recorded
        private bool rebuildPending = false;

        private bool wasPaused = false;

        // Two-click confirm for the global clear-all button.
        private bool clearAllPending = false;
        private Image? clearAllBtnImg;
        private Text? clearAllBtnLbl;

        // Export button feedback refs.
        private Image? exportAllBtnImg;
        private Text? exportAllBtnLbl;
        private Image? downloadAllBtnImg;
        private Text? downloadAllBtnLbl;

        // ── Unity objects ─────────────────────────────────────────────────────
        private GameObject? canvasGO;
        private GameObject? tabGO;
        private GameObject? panelGO;

        private Transform? leftContent;
        private Transform? rightContent;
        private Text? rightHeader;          // scene name in right sub-header
        private Text? pasteStatus;          // brief feedback next to [Paste]

        // Left column scroll control
        private ScrollRect? leftScrollRect;

        // "Go to current room" button label
        private Text? jumpToCurrentBtnLbl;
        private Image? jumpToCurrentBtnImg;

        // ── Pixel sizes (computed once in Setup from screen resolution) ───────
        private int PW, PH;    // panel width/height
        private int LW, RW;    // left/right column widths
        private int RH;        // row height
        private int M;         // general margin
        private int TW, TH;    // tab button width/height
        private int HDR;       // panel header height
        private int SUBHDR;    // sub-header height (both columns share this value)
        private int STGSH;     // settings strip height

        // ── Settings strip live refs ──────────────────────────────────────────
        private Text? ghostToggleLbl;
        private Image? ghostToggleBtnImg;
        private Text? alphaLbl;
        private Text? trackingToggleLbl;
        private Image? trackingToggleBtnImg;
        private Text? multiReplayToggleLbl;
        private Image? multiReplayToggleBtnImg;
        private Text? savePolicyLbl;
        private Image? savePolicyBtnImg;
        private Text? settingsContextLbl;
        private Text? overrideToggleLbl;
        private Image? overrideToggleBtnImg;

        // ─────────────────────────────────────────────────────────────────────
        // SETUP
        // ─────────────────────────────────────────────────────────────────────
        public void Setup()
        {
            UIStyle.LoadFonts();

            M = UIStyle.H(8);
            RH = UIStyle.H(26);
            TW = UIStyle.W(44);
            TH = UIStyle.H(28);
            HDR = UIStyle.H(34);
            SUBHDR = UIStyle.H(28);
            STGSH = UIStyle.H(64);
            PW = UIStyle.W(680);
            PH = UIStyle.H(576);
            LW = UIStyle.W(200);
            RW = PW - LW - 1;

            canvasGO = new GameObject("ReplayModCanvas");
            Object.DontDestroyOnLoad(canvasGO);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.SetActive(false);

            BuildTab();
            BuildPanel();

            isSetup = true;
            Log.LogInfo("[ReplayUI] Setup complete");
        }

        // ─────────────────────────────────────────────────────────────────────
        // TICK
        // ─────────────────────────────────────────────────────────────────────
        public void Tick()
        {
            if (!isSetup) return;

            bool paused = IsPaused();

            if (paused && !wasPaused)
            {
                canvasGO!.SetActive(true);
                tabGO!.SetActive(true);
                wasPaused = true;
            }

            if (!paused && wasPaused)
            {
                canvasGO!.SetActive(false);
                expanded = false;
                ResetClearAllConfirm();
                wasPaused = false;
                return;
            }

            if (!paused) return;

            panelGO!.SetActive(expanded);

            if (expanded && rebuildPending)
            {
                rebuildPending = false;
                RebuildLeft();
                if (selectedScene != null) RebuildRight(selectedScene);
            }
        }

        private static bool IsPaused()
        {
            try
            {
                return GameManager.instance != null
                    && GameManager.instance.ui != null
                    && GameManager.instance.ui.uiState == UIState.PAUSED;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────────
        // PUBLIC API
        // ─────────────────────────────────────────────────────────────────────
        public void OnPBUpdated()
        {
            rebuildPending = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PANEL TOGGLE
        // ─────────────────────────────────────────────────────────────────────
        private void TogglePanel()
        {
            expanded = !expanded;
            panelGO!.SetActive(expanded);
            if (expanded)
            {
                RebuildLeft();
                RefreshSettingsBar();
            }
            else
                ResetClearAllConfirm();
        }

        private ReplaySelectionState? SelectionState => PBManager.SelectionState;

        private string? SelectedSnapshotId => SelectionState?.SelectedSnapshotId;

        private static Color CurrentGlobalGhostColor => GhostSettings.GhostColor;

        private bool IsEditingSnapshot(out ReplaySnapshot? snapshot)
        {
            snapshot = null;
            return TryGetSelectedSnapshot(out _, out snapshot);
        }

        private bool TryGetSelectedSnapshot(out RoomKey key, out ReplaySnapshot? snapshot)
        {
            key = default;
            snapshot = null;

            string? snapshotId = SelectedSnapshotId;
            if (string.IsNullOrEmpty(snapshotId))
                return false;

            foreach (var route in PBManager.AllHistories())
            {
                snapshot = PBManager.GetSnapshot(route.Key, snapshotId);
                if (snapshot == null)
                    continue;

                key = route.Key;
                return true;
            }

            return false;
        }

        private static Color GetResolvedSnapshotColor(ReplaySnapshot snapshot) =>
            snapshot.ResolveGhostColor(CurrentGlobalGhostColor);

        private static string SavePolicyLabel() =>
            GhostSettings.SaveAllRunsEnabled ? "Save all" : "PB only";

        private void RefreshSettingsBar()
        {
            if (trackingToggleLbl != null)
            {
                bool trackingEnabled = GhostSettings.TrackingEnabled;
                trackingToggleLbl.text = trackingEnabled ? "ON" : "OFF";
                trackingToggleLbl.color = trackingEnabled ? UIStyle.Accent : UIStyle.Red;
                if (trackingToggleBtnImg != null)
                    trackingToggleBtnImg.color = trackingEnabled
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Red with { a = 0.22f };
            }

            if (ghostToggleLbl != null)
            {
                bool ghostEnabled = GhostSettings.GhostEnabled;
                ghostToggleLbl.text = ghostEnabled ? "ON" : "OFF";
                ghostToggleLbl.color = ghostEnabled ? UIStyle.Accent : UIStyle.Subtext;
                if (ghostToggleBtnImg != null)
                    ghostToggleBtnImg.color = ghostEnabled
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }

            if (multiReplayToggleLbl != null)
            {
                bool multiReplayEnabled = GhostSettings.MultiReplayEnabled;
                multiReplayToggleLbl.text = multiReplayEnabled ? "ON" : "OFF";
                multiReplayToggleLbl.color = multiReplayEnabled ? UIStyle.Accent : UIStyle.Subtext;
                if (multiReplayToggleBtnImg != null)
                    multiReplayToggleBtnImg.color = multiReplayEnabled
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }

            if (savePolicyLbl != null)
            {
                bool saveAllRunsEnabled = GhostSettings.SaveAllRunsEnabled;
                savePolicyLbl.text = SavePolicyLabel();
                savePolicyLbl.color = saveAllRunsEnabled ? UIStyle.Accent : UIStyle.Gold;
                if (savePolicyBtnImg != null)
                    savePolicyBtnImg.color = saveAllRunsEnabled
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Gold with { a = 0.18f };
            }

            if (IsEditingSnapshot(out var snapshot))
            {
                if (settingsContextLbl != null)
                    settingsContextLbl.text = FindSnapshotContextLabel(snapshot!);

                if (overrideToggleLbl != null)
                {
                    bool hasOverride = snapshot!.HasVisualOverride;
                    overrideToggleLbl.text = hasOverride ? "ON" : "OFF";
                    overrideToggleLbl.color = hasOverride ? UIStyle.Accent : UIStyle.Subtext;
                    if (overrideToggleBtnImg != null)
                        overrideToggleBtnImg.color = hasOverride
                            ? UIStyle.Accent with { a = 0.22f }
                            : UIStyle.Overlay;
                }

                if (alphaLbl != null)
                    alphaLbl.text = snapshot!.ResolveGhostColor(CurrentGlobalGhostColor).a.ToString("0.00");
            }
            else
            {
                if (settingsContextLbl != null)
                    settingsContextLbl.text = "Edit: Global";

                if (overrideToggleLbl != null)
                {
                    overrideToggleLbl.text = "—";
                    overrideToggleLbl.color = UIStyle.Subtext;
                    if (overrideToggleBtnImg != null)
                        overrideToggleBtnImg.color = UIStyle.Overlay with { a = 0.35f };
                }

                if (alphaLbl != null)
                    alphaLbl.text = AlphaString();
            }
        }

        private string FindSnapshotContextLabel(ReplaySnapshot snapshot)
        {
            foreach (var route in PBManager.AllHistories())
            {
                for (int i = 0; i < route.Snapshots.Count; i++)
                {
                    if (route.Snapshots[i].SnapshotId != snapshot.SnapshotId)
                        continue;

                    return $"Edit: {SnapshotLabel(route.Snapshots[i], i)}";
                }
            }

            return "Edit: Snapshot";
        }
    }
}