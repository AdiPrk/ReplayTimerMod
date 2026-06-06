using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GlobalEnums;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public enum TabKind { Runs, Leaderboard, Config }

    public partial class ReplayUI
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayUI");

        private bool isSetup;
        private bool expanded;
        private bool rebuildPending;
        private bool wasPaused;
        private bool clearAllPending;

        private string? selectedScene;
        private string searchFilter = "";
        private TabKind activeTab = TabKind.Runs;
        private string? deleteConfirmId;

        private RoomTimerHUD? timerHud;

        // Panel structure (persistent, never rebuilt)
        private GameObject? canvasGO;
        private GameObject? tabGO;
        private GameObject? panelGO;

        // Left panel
        private Transform? sceneListContent;
        private ScrollRect? sceneListScroll;
        private Text? jumpCurrentLbl;
        private Image? jumpCurrentBg;
        private Text? jumpPreviousLbl;
        private Image? jumpPreviousBg;
        private Text? sceneCountLbl;

        // Right panel - tab bar
        private readonly Dictionary<TabKind, ButtonRef> tabButtons =
            new Dictionary<TabKind, ButtonRef>();

        // Right panel - sub-header
        private Text? rightHeaderLbl;
        private Text? pasteStatusLbl;
        private GameObject? runsActionButtons;

        // Right panel - content area (cleared and rebuilt per tab/selection)
        private Transform? rightContent;

        // Config tab references (only valid when config tab is active)
        private Text? ghostToggleLbl;
        private Image? ghostToggleBg;
        private Text? trackingToggleLbl;
        private Image? trackingToggleBg;
        private Text? savePolicyLbl;
        private Image? savePolicyBg;
        private Text? maxSavedLbl;
        private Text? timerToggleLbl;
        private Image? timerToggleBg;
        private Text? alphaLbl;
        private Text? editContextLbl;
        private Image? editContextBg;
        private Text? clearAllCfgLbl;
        private Image? clearAllCfgBg;
        private Text? exportAllCfgLbl;
        private Image? exportAllCfgBg;
        private Text? onlineToggleLbl;
        private Image? onlineToggleBg;

        // Layout dimensions (computed once in Setup)
        private int PW, PH, LW, RW, M, RH;
        private System.Action<bool> _onOnlineToggle;

        public void SetOnlineToggleHandler(System.Action<bool> handler)
        {
            _onOnlineToggle = handler;
        }

        public void Setup()
        {
            UIStyle.LoadFonts();

            M = UIStyle.Margin;
            RH = UIStyle.RowHeight;
            PW = UIStyle.PanelWidth;
            PH = UIStyle.PanelHeight;
            LW = UIStyle.LeftWidth;
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

        public void SetTimerHUD(RoomTimerHUD hud) => timerHud = hud;

        public void Tick()
        {
            if (!isSetup) return;
            Object.DontDestroyOnLoad(canvasGO);

            bool paused = IsPaused();

            if (paused && !wasPaused)
            {
                canvasGO!.SetActive(true);
                tabGO!.SetActive(true);
                wasPaused = true;

                if (expanded)
                    RefreshCurrentView();
            }

            if (!paused && wasPaused)
            {
                canvasGO!.SetActive(false);
                ResetClearAllConfirm();
                wasPaused = false;
                return;
            }

            if (!paused) return;

            panelGO!.SetActive(expanded);

            if (expanded && rebuildPending)
            {
                rebuildPending = false;
                RefreshCurrentView();
            }
        }

        public void OnPBUpdated() => rebuildPending = true;

        private void TogglePanel()
        {
            expanded = !expanded;
            panelGO!.SetActive(expanded);
            deleteConfirmId = null;
            if (expanded)
                RefreshCurrentView();
            else
                ResetClearAllConfirm();
        }

        private void SwitchTab(TabKind tab)
        {
            if (activeTab == tab) return;
            activeTab = tab;
            deleteConfirmId = null;
            UpdateTabBarVisuals();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void UpdateTabBarVisuals()
        {
            foreach (var kvp in tabButtons)
            {
                bool active = kvp.Key == activeTab;
                kvp.Value.bg.color = active
                    ? UIStyle.Accent with { a = 0.15f }
                    : Color.clear;
                kvp.Value.label.color = active ? UIStyle.Accent : UIStyle.Subtext;
            }
        }

        private void UpdateRightSubHeader()
        {
            if (rightHeaderLbl == null) return;

            switch (activeTab)
            {
                case TabKind.Runs:
                case TabKind.Leaderboard:
                    rightHeaderLbl.text = selectedScene ?? "Select a room";
                    rightHeaderLbl.color = selectedScene != null ? UIStyle.Text : UIStyle.Subtext;
                    break;
                case TabKind.Config:
                    rightHeaderLbl.text = "Settings";
                    rightHeaderLbl.color = UIStyle.Text;
                    break;
            }

            if (pasteStatusLbl != null)
                pasteStatusLbl.text = "";

            if (runsActionButtons != null)
                runsActionButtons.SetActive(activeTab == TabKind.Runs);
        }

        private void RefreshCurrentView()
        {
            RebuildSceneList();
            UpdateTabBarVisuals();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void RebuildRightContent()
        {
            if (rightContent == null) return;
            ClearContent(rightContent);

            // Clear config tab references since they'll be stale
            ClearConfigRefs();

            switch (activeTab)
            {
                case TabKind.Runs:
                    if (selectedScene != null)
                        BuildRunsContent(selectedScene);
                    else
                        AddCenteredMessage(rightContent, "Select a room to view runs.");
                    break;

                case TabKind.Leaderboard:
                    BuildLeaderboardContent();
                    break;

                case TabKind.Config:
                    BuildConfigContent();
                    RefreshConfigValues();
                    break;
            }

            ForceLayout(rightContent);
        }

        private void SelectScene(string scene)
        {
            selectedScene = scene;
            deleteConfirmId = null;
            RebuildSceneList();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void ClearSelectedScene()
        {
            selectedScene = null;
            UpdateRightSubHeader();
            if (rightContent != null)
            {
                ClearContent(rightContent);
                AddCenteredMessage(rightContent, "Select a room to view runs.");
                ForceLayout(rightContent);
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

        private ReplaySelectionState? SelectionState => PBManager.SelectionState;
        private string? SelectedSnapshotId => SelectionState?.SelectedSnapshotId;
        private static Color CurrentGlobalGhostColor => GhostSettings.GhostColor;

        private bool TryGetSelectedSnapshot(out RoomKey key, out ReplaySnapshot? snapshot)
        {
            key = default;
            snapshot = null;

            string? snapshotId = SelectedSnapshotId;
            if (string.IsNullOrEmpty(snapshotId)) return false;

            foreach (var route in PBManager.AllHistories())
            {
                snapshot = PBManager.GetSnapshot(route.Key, snapshotId);
                if (snapshot != null)
                {
                    key = route.Key;
                    return true;
                }
            }
            return false;
        }

        private static Color GetResolvedSnapshotColor(ReplaySnapshot snapshot) =>
            snapshot.ResolveGhostColor(GhostSettings.GhostColor);

        private static void AddCenteredMessage(Transform parent, string msg)
        {
            var row = MakeGO("MsgRow", parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(40);
            MakeLbl(row.transform, msg, UIStyle.FontSizeSm,
                UIStyle.Subtext, TextAnchor.MiddleCenter, fill: true);
        }
    }
}