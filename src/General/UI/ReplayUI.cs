using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public enum TabKind { Runs, Config }

    public partial class ReplayUI
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ReplayUI");

        private bool _isSetup;
        private bool _expanded;
        private bool _rebuildPending;
        private bool _wasPaused;
        private bool _clearAllPending;

        private string? _selectedScene;
        private string _searchFilter = "";
        private TabKind _activeTab = TabKind.Runs;
        private string? _deleteConfirmId;
        private string? _routeClearConfirmKey;
        private bool _sceneClearPending;

        private RoomTimerHUD? _timerHud;

        // Panel structure (persistent, never rebuilt)
        private GameObject _canvasGO = null!;
        private GameObject _tabGO = null!;
        private GameObject _panelGO = null!;

        // Left panel
        private Transform _sceneListContent = null!;
        private ScrollRect _sceneListScroll = null!;
        private Text? _jumpCurrentLbl;
        private Image? _jumpCurrentBg;
        private Text? _jumpPreviousLbl;
        private Image? _jumpPreviousBg;

        // Right panel - tab bar
        private readonly Dictionary<TabKind, ButtonRef> _tabButtons =
            new Dictionary<TabKind, ButtonRef>();

        // Right panel - sub-header
        private Text? _rightHeaderLbl;
        private Text? _pasteStatusLbl;
        private GameObject? _runsActionButtons;
        private Text? _sceneClearLbl;
        private Image? _sceneClearBg;

        // Right panel - content area (cleared and rebuilt per tab/selection)
        private Transform? _rightContent;

        // Lazily-resolved ScrollRect that owns _rightContent
        // (_rightContent is Content under Viewport under the ScrollRect GO)
        private ScrollRect? _rightScroll;
        private ScrollRect? RightScroll
        {
            get
            {
                if (_rightScroll == null && _rightContent != null)
                    _rightScroll = _rightContent.parent.parent.GetComponent<ScrollRect>();
                return _rightScroll;
            }
        }

        // Config tab references (only valid when config tab is active)
        private Text? _ghostToggleLbl;
        private Image? _ghostToggleBg;
        private Text? _trackingToggleLbl;
        private Image? _trackingToggleBg;
        private Text? _savePolicyLbl;
        private Image? _savePolicyBg;
        private Text? _maxSavedLbl;
        private Text? _timerToggleLbl;
        private Image? _timerToggleBg;
        private Text? _chainToggleLbl;
        private Image? _chainToggleBg;
        private Text? _skipRunsToggleLbl;
        private Image? _skipRunsToggleBg;
        private Text? _skipTimerToggleLbl;
        private Image? _skipTimerToggleBg;
        private Image? _cfgGhostColorFill;
        private Text? _cfgGhostAlphaLbl;
        private Text? _clearAllCfgLbl;
        private Image? _clearAllCfgBg;
        private Text? _copyAllCfgLbl;
        private Image? _copyAllCfgBg;

        // Layout dimensions (computed once in Setup)
        private int PW, PH, LW, RW, M, RH;

        // Last-built view, for deciding whether to preserve scroll position
        private TabKind _lastContentTab = (TabKind)(-1);
        private string? _lastContentScene;

        public void Setup()
        {
            UIStyle.LoadFonts();

            M = UIStyle.Margin;
            RH = UIStyle.RowHeight;
            PW = UIStyle.PanelWidth;
            PH = UIStyle.PanelHeight;
            LW = UIStyle.LeftWidth;
            RW = PW - LW - 1;

            _canvasGO = new GameObject("ReplayModCanvas");
            ScenePersistence.Apply(_canvasGO);
            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            _canvasGO.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ConstantPixelSize;
            _canvasGO.AddComponent<GraphicRaycaster>();
            _canvasGO.SetActive(false);

            BuildTab();
            BuildPanel();

            _isSetup = true;
            Log.LogInfo("[ReplayUI] Setup complete");
        }

        public void SetTimerHUD(RoomTimerHUD hud) => _timerHud = hud;

        public void Tick()
        {
            if (!_isSetup) return;

            // The host game destroyed the canvas (HK 1221's additive scene
            // unload can do this despite DontDestroyOnLoad). Rebuild the
            // whole panel; Setup() recreates every GameObject reference and
            // BuildTabBar clears/refills _tabButtons.
            if (_canvasGO == null)
            {
                Log.LogWarning("[ReplayUI] Canvas was destroyed externally - rebuilding");
                _expanded = false;
                _wasPaused = false;
                _rebuildPending = false;
                _rightScroll = null;
                _lastContentTab = (TabKind)(-1);
                _lastContentScene = null;
                ClearConfigRefs();
                Setup();
                if (_canvasGO == null) return; // Setup always assigns
            }

            bool paused = GameUiState.IsPaused();

            if (paused && !_wasPaused)
            {
                _canvasGO.SetActive(true);
                _tabGO.SetActive(true);
                _wasPaused = true;

                if (_expanded)
                    RefreshCurrentView();
            }

            if (!paused && _wasPaused)
            {
                _canvasGO.SetActive(false);
                ResetClearAllConfirm();
                ClosePicker();
                GhostSettings.Flush(); // write any throttled color/alpha change
                _wasPaused = false;
                return;
            }

            if (!paused) return;

            _panelGO.SetActive(_expanded);

            if (_expanded && _rebuildPending)
            {
                _rebuildPending = false;
                RefreshCurrentView();
            }

            TickTooltip();
        }

        /// <summary>
        /// Public entry point to refresh the config tab toggle states.
        /// Called by mod entry point after programmatic state changes.
        /// </summary>
        public void RefreshConfigTab()
        {
            if (_activeTab == TabKind.Config)
                RefreshConfigValues();
        }

        public void OnPBUpdated() => _rebuildPending = true;

        // ── Panel & tab management ─────────────────────────────────────

        private void TogglePanel()
        {
            _expanded = !_expanded;
            _panelGO.SetActive(_expanded);
            _deleteConfirmId = null;
            _routeClearConfirmKey = null;
            ResetSceneClearConfirm();
            ClosePicker();
            if (_expanded)
                RefreshCurrentView();
            else
                ResetClearAllConfirm();
        }

        private void SwitchTab(TabKind tab)
        {
            if (_activeTab == tab) return;
            _activeTab = tab;
            _deleteConfirmId = null;
            _routeClearConfirmKey = null;
            ClosePicker();
            UpdateTabBarVisuals();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void UpdateTabBarVisuals()
        {
            foreach (var kvp in _tabButtons)
            {
                bool active = kvp.Key == _activeTab;
                kvp.Value.bg.color = active
                    ? UIStyle.BtnBg(UIStyle.Accent)
                    : Color.clear;
                kvp.Value.label.color = active ? UIStyle.Accent : UIStyle.Subtext;
            }
        }

        private void UpdateRightSubHeader()
        {
            if (_rightHeaderLbl == null) return;

            switch (_activeTab)
            {
                case TabKind.Runs:
                    _rightHeaderLbl.text = _selectedScene ?? "Select a room";
                    _rightHeaderLbl.color = _selectedScene != null ? UIStyle.Text : UIStyle.Subtext;
                    break;
                case TabKind.Config:
                    _rightHeaderLbl.text = "Settings";
                    _rightHeaderLbl.color = UIStyle.Text;
                    break;
            }

            if (_pasteStatusLbl != null)
                _pasteStatusLbl.text = "";

            if (_runsActionButtons != null)
                _runsActionButtons.SetActive(_activeTab == TabKind.Runs);

            // A pending scene-Clear confirm doesn't survive tab/scene changes
            ResetSceneClearConfirm();
        }

        /// <summary>Reverts the sub-header Clear button from its "Sure?"
        /// confirm state back to idle.</summary>
        private void ResetSceneClearConfirm()
        {
            _sceneClearPending = false;
            if (_sceneClearLbl != null)
            {
                _sceneClearLbl.text = "Clear";
                _sceneClearLbl.color = UIStyle.Red;
            }
            if (_sceneClearBg != null)
                _sceneClearBg.color = UIStyle.BtnBgStrong(UIStyle.Red);
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
            if (_rightContent == null) return;

            // Preserve scroll position only when rebuilding the SAME view
            // (same tab + same scene). Tab/scene switches reset to top.
            // Pixel offset, not normalized fraction - same-view rebuilds can
            // change the content height (snapshot deleted, confirm row armed)
            // and the same fraction of a different height drifts.
            bool sameView = _activeTab == _lastContentTab
                && _selectedScene == _lastContentScene;
            float keepOffset = 0f; // pixels from the top
            var scroll = RightScroll;
            if (sameView && scroll != null)
                keepOffset = ScrollOffsetFromTop(scroll);

            ClearContentDetached(_rightContent);

            ClearConfigRefs();

            switch (_activeTab)
            {
                case TabKind.Runs:
                    if (_selectedScene != null)
                        BuildRunsContent(_selectedScene);
                    else
                        AddCenteredMessage(_rightContent, "Select a room to view runs.");
                    break;

                case TabKind.Config:
                    BuildConfigContent();
                    RefreshConfigValues();
                    break;
            }

            ForceLayout(_rightContent);

            if (scroll != null)
            {
                if (sameView)
                    RestoreScrollOffsetFromTop(scroll, keepOffset);
                else
                    scroll.verticalNormalizedPosition = 1f; // top
            }

            _lastContentTab = _activeTab;
            _lastContentScene = _selectedScene;
        }

        private void SelectScene(string scene)
        {
            _selectedScene = scene;
            _deleteConfirmId = null;
            _routeClearConfirmKey = null;
            ClosePicker();

            RebuildSceneList();
            UpdateTabBarVisuals();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void ClearSelectedScene()
        {
            _selectedScene = null;
            UpdateRightSubHeader();
            if (_rightContent != null)
            {
                ClearContentDetached(_rightContent);
                AddCenteredMessage(_rightContent, "Select a room to view runs.");
                ForceLayout(_rightContent);
            }
        }

        /// <summary>
        /// Clears children, detaching them from the parent BEFORE the
        /// (deferred) Destroy so the layout group doesn't see destroyed-but-
        /// pending children for a frame. Eliminates the one-frame visual
        /// jump on rebuilds.
        /// </summary>
        private static void ClearContentDetached(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        private ReplaySelectionState? SelectionState => PBManager.SelectionState;

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