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
        private Text? alphaLbl;
        private Text? trackingToggleLbl;
        private Image? trackingToggleBtnImg;

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
            STGSH = UIStyle.H(36);
            PW = UIStyle.W(680);
            PH = UIStyle.H(576);
            LW = UIStyle.W(200);
            RW = PW - LW - 1;

            canvasGO = new GameObject("ReplayModCanvas");
            UnityEngine.Object.DontDestroyOnLoad(canvasGO);
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
            UnityEngine.Object.DontDestroyOnLoad(canvasGO);

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
                RebuildLeft();
            else
                ResetClearAllConfirm();
        }
    }
}
