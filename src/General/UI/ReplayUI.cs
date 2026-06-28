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
        private GameObject canvasGO = null!;
        private GameObject tabGO = null!;
        private GameObject panelGO = null!;

        // Left panel
        private Transform sceneListContent = null!;
        private ScrollRect sceneListScroll = null!;
        private Text? jumpCurrentLbl;
        private Image? jumpCurrentBg;
        private Text? jumpPreviousLbl;
        private Image? jumpPreviousBg;

        // Right panel - tab bar
        private readonly Dictionary<TabKind, ButtonRef> tabButtons =
            new Dictionary<TabKind, ButtonRef>();

        // Right panel - sub-header
        private Text? rightHeaderLbl;
        private Text? pasteStatusLbl;
        private GameObject? runsActionButtons;

        // Right panel - content area (cleared and rebuilt per tab/selection)
        private Transform? rightContent;

        // Lazily-resolved ScrollRect that owns rightContent
        // (rightContent is Content under Viewport under the ScrollRect GO)
        private ScrollRect? _rightScroll;
        private ScrollRect? RightScroll
        {
            get
            {
                if (_rightScroll == null && rightContent != null)
                    _rightScroll = rightContent.parent.parent.GetComponent<ScrollRect>();
                return _rightScroll;
            }
        }

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
        private Text? chainToggleLbl;
        private Image? chainToggleBg;
        private Text? skipRunsToggleLbl;
        private Image? skipRunsToggleBg;
        private Text? skipTimerToggleLbl;
        private Image? skipTimerToggleBg;
        private Text? alphaLbl;
        private Text? editContextLbl;
        private Image? editContextBg;
        private Text? clearAllCfgLbl;
        private Image? clearAllCfgBg;
        private Text? exportAllCfgLbl;
        private Image? exportAllCfgBg;
        private Text? onlineToggleLbl;
        private Image? onlineToggleBg;
        private InputField? nameInput;
        private Text? nameStatusLbl;
        private Image? nameSaveBg;
        private Text? nameSaveLbl;
        private UnityEngine.Networking.UnityWebRequest? _nameRequest;
        private string _lastSavedName = "";

        /// <summary>
        /// Fired when the user successfully saves a new display name
        /// via the config tab. The mod entry point subscribes to this
        /// to persist the name and start networking if needed.
        /// </summary>
        public event System.Action<string>? OnDisplayNameSet;

        // Layout dimensions (computed once in Setup)
        private int PW, PH, LW, RW, M, RH;

        // Online toggle handler (set by mod entry point)
        private System.Action<bool>? _onOnlineToggle;

        // Leaderboard dependencies
        private readonly LeaderboardCache _leaderboardCache = new LeaderboardCache();
        private string _gameTag = "";
        private NetworkClient? _networkClient;

        // ── Rebuild gating state ───────────────────────────────────────
        // Version stamps of what's currently rendered, so background data
        // refreshes that produce identical content can be skipped entirely
        // (preserving hover states, animations, and scroll position).

        private string? _renderedLbScene;
        private int _renderedLbVersion = -1;
        private int _renderedServerScenesVersion = -1;

        // Last-built view, for deciding whether to preserve scroll position
        private TabKind _lastContentTab = (TabKind)(-1);
        private string? _lastContentScene;

        public void SetOnlineToggleHandler(System.Action<bool> handler)
        {
            _onOnlineToggle = handler;
        }

        public void SetNetworkClient(NetworkClient? client)
        {
            if (_networkClient != null)
            {
                _networkClient.OnLeaderboardUpdated -= HandleLeaderboardUpdated;
                _networkClient.OnManifestReady -= HandleManifestReady;
                _networkClient.OnManifestFailed -= HandleManifestFailed;
                _networkClient.OnRunIdAssigned -= HandleRunIdAssigned;
            }

            _networkClient = client;

            if (_networkClient != null)
            {
                _networkClient.OnLeaderboardUpdated += HandleLeaderboardUpdated;
                _networkClient.OnManifestReady += HandleManifestReady;
                _networkClient.OnManifestFailed += HandleManifestFailed;
                _networkClient.OnRunIdAssigned += HandleRunIdAssigned;
            }
        }

        private void HandleRunIdAssigned(string snapshotId, RoomKey key, string runId)
        {
            PBManager.SetServerIds(key, snapshotId, runId, null);
        }

        public void SetGameTag(string gameTag)
        {
            _gameTag = gameTag ?? "";
        }

        /// <summary>
        /// The leaderboard cache, owned by this ReplayUI instance.
        /// NetworkClient writes into it; BuildLeaderboardContent reads from it.
        /// </summary>
        public LeaderboardCache LeaderboardCacheRef => _leaderboardCache;

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
                canvasGO.SetActive(true);
                tabGO.SetActive(true);
                wasPaused = true;

                // Scene list is only visible (and worth syncing) when expanded.
                if (_networkClient != null)
                    _networkClient.SetMenuOpen(expanded);

                if (expanded)
                    RefreshCurrentView();
            }

            if (!paused && wasPaused)
            {
                canvasGO.SetActive(false);
                if (_networkClient != null)
                {
                    _networkClient.StopLeaderboardPolling();
                    _networkClient.SetMenuOpen(false);
                }
                ResetClearAllConfirm();
                wasPaused = false;
                return;
            }

            if (!paused) return;

            panelGO.SetActive(expanded);

            if (expanded && rebuildPending)
            {
                rebuildPending = false;
                RefreshCurrentView();
            }

            // Revert expired ✓/✗ download states back to idle (in place)
            TickGhostStateExpiry();

            // Poll in-flight name-save request
            TickNameSave();
        }

        /// <summary>
        /// Public entry point to refresh the config tab toggle states.
        /// Called by mod entry point after programmatic state changes.
        /// </summary>
        public void RefreshConfigTab()
        {
            if (activeTab == TabKind.Config)
                RefreshConfigValues();
        }

        public void OnPBUpdated() => rebuildPending = true;

        // ── Network event handlers ─────────────────────────────────────

        /// <summary>
        /// Called when leaderboard data has been written to the cache
        /// (per-room poll or manifest refresh). Rebuilds the content area
        /// ONLY if the data for the currently-viewed room actually changed.
        /// Identical polling responses are skipped entirely, so hover
        /// states, the reveal animation, and scroll position survive.
        /// </summary>
        private void HandleLeaderboardUpdated()
        {
            if (!expanded) return;
            if (activeTab != TabKind.Leaderboard) return;
            if (selectedScene == null) return;

            int version = _leaderboardCache.GetVersion(_gameTag, selectedScene);
            if (selectedScene == _renderedLbScene && version == _renderedLbVersion)
                return; // nothing changed — don't touch the UI

            RebuildLeaderboardContentOnly();
        }

        /// <summary>
        /// Called when the manifest has been fetched or refreshed.
        /// Rebuilds the scene list only when the set of server rooms
        /// actually changed (not on every 60s refresh).
        /// </summary>
        private void HandleManifestReady()
        {
            if (!expanded) return;

            if (_leaderboardCache.ServerScenesVersion == _renderedServerScenesVersion)
                return;

            RebuildSceneList();
        }

        /// <summary>
        /// Called when a manifest fetch fails. Currently a no-op on the UI
        /// side — retries happen automatically with backoff in NetworkClient
        /// and the error is logged there.
        /// </summary>
        private void HandleManifestFailed()
        {
            // Intentionally empty — no footer label to update.
            // The scene list empty-state message already shows sync status.
        }

        // ── Panel & tab management ─────────────────────────────────────

        private void TogglePanel()
        {
            expanded = !expanded;
            panelGO.SetActive(expanded);
            deleteConfirmId = null;
            if (_networkClient != null)
                _networkClient.SetMenuOpen(expanded);
            if (expanded)
                RefreshCurrentView();
            else
            {
                if (_networkClient != null)
                    _networkClient.StopLeaderboardPolling();
                ResetClearAllConfirm();
            }
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

            // Preserve scroll position only when rebuilding the SAME view
            // (same tab + same scene). Tab/scene switches reset to top.
            bool sameView = activeTab == _lastContentTab
                && selectedScene == _lastContentScene;
            float keepScroll = 1f; // 1 = top
            var scroll = RightScroll;
            if (sameView && scroll != null)
                keepScroll = scroll.verticalNormalizedPosition;

            ClearContentDetached(rightContent);

            // Clear config tab references since they'll be stale
            ClearConfigRefs();

            // Always stop polling — the leaderboard branch restarts if needed
            if (_networkClient != null)
                _networkClient.StopLeaderboardPolling();

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
                    // Polling keeps the active room live-updated; the cache
                    // is already populated by the manifest so first paint
                    // is instant.
                    if (GhostSettings.OnlineEnabled
                        && selectedScene != null
                        && _networkClient != null
                        && _networkClient.IsStarted)
                    {
                        _networkClient.StartLeaderboardPolling(selectedScene);
                    }
                    break;

                case TabKind.Config:
                    BuildConfigContent();
                    RefreshConfigValues();
                    break;
            }

            ForceLayout(rightContent);

            if (scroll != null)
                scroll.verticalNormalizedPosition =
                    sameView ? Mathf.Clamp01(keepScroll) : 1f;

            _lastContentTab = activeTab;
            _lastContentScene = selectedScene;
        }

        private void SelectScene(string scene)
        {
            selectedScene = scene;
            deleteConfirmId = null;

            // If this room has no local runs but exists on the server,
            // the Runs tab would be empty — jump straight to the leaderboard.
            if (activeTab == TabKind.Runs
                && GhostSettings.OnlineEnabled
                && !PBManager.AllPBs().Any(p => p.Key.SceneName == scene)
                && _leaderboardCache.GetServerScenes().Contains(scene))
            {
                activeTab = TabKind.Leaderboard;
            }

            RebuildSceneList();
            UpdateTabBarVisuals();
            UpdateRightSubHeader();
            RebuildRightContent();
        }

        private void ClearSelectedScene()
        {
            selectedScene = null;
            UpdateRightSubHeader();
            if (rightContent != null)
            {
                ClearContentDetached(rightContent);
                AddCenteredMessage(rightContent, "Select a room to view runs.");
                ForceLayout(rightContent);
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
                snapshot = PBManager.GetSnapshot(route.Key, snapshotId!);
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