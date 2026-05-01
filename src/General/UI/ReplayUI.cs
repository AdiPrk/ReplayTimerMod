using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayUI");

        private const string GlobalSettingsContextText = "Edit: Global";

        private bool isSetup = false;
        private bool expanded = false;
        private string? selectedScene = null;
        private bool rebuildPending = false;
        private bool wasPaused = false;
        private bool clearAllPending = false;

        private RoomTimerHUD? timerHud;

        private Image? clearAllBtnImg;
        private Text? clearAllBtnLbl;
        private Image? exportAllBtnImg;
        private Text? exportAllBtnLbl;
        private Image? downloadAllBtnImg;
        private Text? downloadAllBtnLbl;

        private GameObject? canvasGO;
        private GameObject? tabGO;
        private GameObject? panelGO;

        private Transform? leftContent;
        private Transform? rightContent;
        private Text? rightHeader;          
        private Text? pasteStatus;          
        private ScrollRect? leftScrollRect;

        // Jump Buttons
        private Text? jumpToCurrentBtnLbl;
        private Image? jumpToCurrentBtnImg;
        private Text? jumpToLastBtnLbl;
        private Image? jumpToLastBtnImg;

        private int PW, PH;    
        private int LW, RW;    
        private int RH;        
        private int M;         
        private int TW, TH;    
        private int HDR;       
        private int SUBHDR;    
        private int STGSH;     

        private Text? ghostToggleLbl;
        private Image? ghostToggleBtnImg;
        private Text? alphaLbl;
        private Text? trackingToggleLbl;
        private Image? trackingToggleBtnImg;
        private Text? savePolicyLbl;
        private Image? savePolicyBtnImg;
        private Text? maxSavedReplaysLbl;
        private Text? settingsContextLbl;
        private Image? settingsContextBtnImg;
        
        private Text? timerToggleLbl;
        private Image? timerToggleBtnImg;

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

        public void SetTimerHUD(RoomTimerHUD hud)
        {
            timerHud = hud;
        }

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
                
                RefreshSettingsBar();
                
                // Keep data fresh if the panel was left open from last pause
                if (expanded)
                {
                    RebuildLeft();
                    if (selectedScene != null) RebuildRight(selectedScene);
                }
            }

            if (!paused && wasPaused)
            {
                canvasGO!.SetActive(false);
                ResetClearAllConfirm();
                wasPaused = false;
                return;
            }

            if (!paused) return;

            // Maintain visibility state
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

        public void OnPBUpdated() => rebuildPending = true;

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
            if (string.IsNullOrEmpty(snapshotId)) return false;

            foreach (var route in PBManager.AllHistories())
            {
                snapshot = PBManager.GetSnapshot(route.Key, snapshotId);
                if (snapshot == null) continue;

                key = route.Key;
                return true;
            }
            return false;
        }

        private static Color GetResolvedSnapshotColor(ReplaySnapshot snapshot) =>
            snapshot.ResolveGhostColor(CurrentGlobalGhostColor);

        private static string SavePolicyLabel() =>
            GhostSettings.SaveAllRunsEnabled ? "Save all" : "PB only";

        private static string MaxSavedReplaysString() =>
            GhostSettings.MaxSavedReplaysPerRoute.ToString();

        private void OnMaxSavedReplaysMinus() => AdjustMaxSavedReplays(-1);
        private void OnMaxSavedReplaysPlus() => AdjustMaxSavedReplays(1);

        private void AdjustMaxSavedReplays(int delta)
        {
            GhostSettings.MaxSavedReplaysPerRoute += delta;
            int newLimit = GhostSettings.MaxSavedReplaysPerRoute;
            PBManager.PruneAllHistories(newLimit, persist: true);
            RefreshSettingsBar();
            if (selectedScene != null) RebuildRight(selectedScene);
        }

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

            if (maxSavedReplaysLbl != null)
                maxSavedReplaysLbl.text = MaxSavedReplaysString();

            if (IsEditingSnapshot(out var snapshot))
            {
                if (settingsContextLbl != null)
                {
                    settingsContextLbl.text = FindSnapshotContextLabel(snapshot!);
                    settingsContextLbl.color = UIStyle.Accent;
                }
                if (settingsContextBtnImg != null)
                    settingsContextBtnImg.color = UIStyle.Accent with { a = 0.22f };
                if (alphaLbl != null)
                    alphaLbl.text = snapshot!.ResolveGhostColor(CurrentGlobalGhostColor).a.ToString("0.00");
            }
            else
            {
                if (settingsContextLbl != null)
                {
                    settingsContextLbl.text = GlobalSettingsContextText;
                    settingsContextLbl.color = UIStyle.Text;
                }
                if (settingsContextBtnImg != null)
                    settingsContextBtnImg.color = UIStyle.Overlay with { a = 0.55f };
                if (alphaLbl != null)
                    alphaLbl.text = AlphaString();
            }

            if (timerToggleLbl != null)
            {
                bool timerEnabled = GhostSettings.TimerHudEnabled;
                timerToggleLbl.text = timerEnabled ? "ON" : "OFF";
                timerToggleLbl.color = timerEnabled ? UIStyle.Accent : UIStyle.Subtext;
                if (timerToggleBtnImg != null)
                    timerToggleBtnImg.color = timerEnabled
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }
        }

        private string FindSnapshotContextLabel(ReplaySnapshot snapshot)
        {
            foreach (var route in PBManager.AllHistories())
            {
                for (int i = 0; i < route.Snapshots.Count; i++)
                {
                    if (route.Snapshots[i].SnapshotId == snapshot.SnapshotId)
                        return $"Edit: {SnapshotLabel(route.Snapshots[i], i)}";
                }
            }
            return "Edit: Snapshot";
        }
    }
}