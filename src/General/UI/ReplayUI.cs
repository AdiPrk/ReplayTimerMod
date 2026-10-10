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

        private GameObject _canvasGO = null!;
        private GameObject _tabGO = null!;
        private GameObject _panelGO = null!;

        private Transform _sceneListContent = null!;
        private ScrollRect _sceneListScroll = null!;
        private Text? _jumpCurrentLbl;
        private Image? _jumpCurrentBg;
        private Text? _jumpPreviousLbl;
        private Image? _jumpPreviousBg;

        private readonly Dictionary<TabKind, ButtonRef> _tabButtons =
            new Dictionary<TabKind, ButtonRef>();

        private Text? _rightHeaderLbl;
        private Text? _pasteStatusLbl;
        private GameObject? _runsActionButtons;
        private Text? _sceneClearLbl;
        private Image? _sceneClearBg;

        private Transform? _rightContent;

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

        private Text? _ghostToggleLbl;
        private Image? _ghostToggleBg;
        private Text? _reeseToggleLbl;
        private Image? _reeseToggleBg;
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
        private Text? _cheatCancelToggleLbl;
        private Image? _cheatCancelToggleBg;
        private Text? _skipCheatedToggleLbl;
        private Image? _skipCheatedToggleBg;
        private Text? _skipGhostReentryToggleLbl;
        private Image? _skipGhostReentryToggleBg;
        private Image? _cfgGhostColorFill;
        private Text? _cfgGhostAlphaLbl;
        private Text? _clearAllCfgLbl;
        private Image? _clearAllCfgBg;
        private Text? _copyAllCfgLbl;
        private Image? _copyAllCfgBg;

        private int PW, PH, LW, RW, M, RH;

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
                if (_canvasGO == null) return;
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
                GhostSettings.Flush();
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

        public void RefreshConfigTab()
        {
            if (_activeTab == TabKind.Config)
                RefreshConfigValues();
        }

        public void OnPBUpdated() => _rebuildPending = true;

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

            ResetSceneClearConfirm();
        }

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

            bool sameView = _activeTab == _lastContentTab
                && _selectedScene == _lastContentScene;
            float keepOffset = 0f;
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
                    scroll.verticalNormalizedPosition = 1f;
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
