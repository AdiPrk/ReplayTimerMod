using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Central networking orchestrator.
    ///
    /// Redesigned for efficiency + responsiveness:
    ///   - Combined /init endpoint (one round trip on startup)
    ///   - Lightweight /scenes polling with version (16 bytes when unchanged)
    ///   - On-demand per-room /leaderboard with version (skip query when unchanged)
    ///   - Prefetch on room enter (data ready before menu opens)
    ///   - Optimistic local update after upload (instant rank display)
    ///   - Request deduplication (no redundant fetches)
    ///   - Connection health tracking (back off when server is down)
    ///   - Adaptive polling intervals
    ///
    /// Public API is backward-compatible with ReplayUI:
    ///   OnManifestReady/OnManifestFailed fire for scene index events.
    ///   StartLeaderboardPolling/StopLeaderboardPolling still work.
    ///   ManifestFetched, CurrentManifestStatus, LastManifestError preserved.
    /// </summary>
    public sealed class NetworkClient
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("NetworkClient");

        // ── Timeouts ───────────────────────────────────────────────────────

        private const int HttpTimeoutSec = 10;
        private const int InitTimeoutSec = 15;

        // ── Base polling intervals (before health multiplier) ──────────────

        private const float SceneIndexInterval_MenuOpen = 60f;
        private const float SceneIndexInterval_Gameplay = 120f;
        private const float RoomPollInterval_Active = 5f;
        private const float RoomPollInterval_Default = 8f;
        private const float RoomPollInterval_Idle = 12f;
        private const float RoomFreshnessThreshold = 30f;

        // ── Immutable config ───────────────────────────────────────────────

        private readonly string _deviceId;
        private readonly string _gameTag;
        private readonly string _modVersion;
        private readonly string _apiBaseUrl;

        // ── Sub-components ─────────────────────────────────────────────────

        private HttpService? _http;
        private UploadWorker? _uploadWorker;
        private readonly ConnectionHealth _health = new ConnectionHealth();
        private bool _started;

        // ── Config ─────────────────────────────────────────────────────────

        private ConfigResponse? _serverConfig;
        private bool _configFetched;
        private bool _maintenanceMode;

        // ── Scene index (/scenes) ──────────────────────────────────────────

        public enum ManifestStatus { NotStarted, Loading, Loaded, Failed }

        private LeaderboardCache? _leaderboardCache;
        private float _sceneIndexTimer;
        private float _sceneIndexInterval;
        private bool _sceneIndexInFlight;
        private int _sceneIndexFailures;
        private ManifestStatus _sceneIndexStatus = ManifestStatus.NotStarted;
        private string? _sceneIndexError;

        // Backward-compat properties
        public ManifestStatus CurrentManifestStatus => _sceneIndexStatus;
        public string? LastManifestError => _sceneIndexError;
        public bool ManifestFetched => _sceneIndexStatus == ManifestStatus.Loaded;

        // ── Room leaderboard polling ───────────────────────────────────────

        private string? _pollScene;
        private float _pollTimer;
        private bool _pollInFlight;
        private int _consecutiveNoChange;   // for adaptive interval
        private bool _lastPollWasChange;
        private bool _menuOpen;             // set by UI, controls scene index cadence

        // ── Request deduplication ──────────────────────────────────────────

        private readonly HashSet<string> _roomFetchInFlight = new HashSet<string>();

        // ── Events (fired on main thread) ──────────────────────────────────

        public event Action<RankInfo>? OnRankReceived;
        public event Action<string>? OnDisplayNameReceived;
        public event Action? OnLeaderboardUpdated;
        public event Action? OnManifestReady;   // backward compat: scene index loaded
        public event Action? OnManifestFailed;  // backward compat: scene index failed

        // ── Construction ───────────────────────────────────────────────────

        public NetworkClient(string deviceId, string gameTag,
            string modVersion, string apiBaseUrl)
        {
            _deviceId = deviceId;
            _gameTag = gameTag;
            _modVersion = modVersion;
            _apiBaseUrl = TrimTrailingSlash(apiBaseUrl);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────

        public void Start()
        {
            if (_started) return;
            _started = true;

            _http = new HttpService();

            _uploadWorker = new UploadWorker(_http, _apiBaseUrl, _deviceId, _health);
            _uploadWorker.OnUploadSuccess += HandleUploadSuccess;
            _uploadWorker.OnDisplayNameReceived += HandleDisplayNameReceived;

            // Single startup request: GET /init (config + scene index).
            FetchInit();

            Log.LogInfo("[NetworkClient] Started (redesigned networking)");
        }

        public void Stop()
        {
            if (!_started) return;

            StopLeaderboardPolling();

            if (_uploadWorker != null)
            {
                _uploadWorker.OnUploadSuccess -= HandleUploadSuccess;
                _uploadWorker.OnDisplayNameReceived -= HandleDisplayNameReceived;
                _uploadWorker = null;
            }

            if (_http != null)
            {
                _http.CancelAll();
                _http = null;
            }

            _roomFetchInFlight.Clear();
            _started = false;
            Log.LogInfo("[NetworkClient] Stopped");
        }

        public void Tick()
        {
            if (!_started || _http == null) return;

            _http.Tick();

            if (_uploadWorker != null)
                _uploadWorker.Tick();

            TickSceneIndex();
            TickRoomPoll();
        }

        // ── Upload API ─────────────────────────────────────────────────────

        public void EnqueueUpload(ReplaySnapshot snapshot, EvaluationResult result)
        {
            if (!_started) return;
            if (_maintenanceMode) return;
            if (result.Kind != ResultKind.FirstRun
                && result.Kind != ResultKind.NewPB) return;

            var payload = new UploadPayload
            {
                SnapshotId = snapshot.SnapshotId,
                Game = _gameTag,
                SceneName = snapshot.Key.SceneName,
                EntryFrom = snapshot.Key.EntryFromScene,
                ExitTo = snapshot.Key.ExitToScene,
                TotalTime = snapshot.TotalTime,
                FrameCount = snapshot.Room.FrameCount,
                CapturedAtUtcTicks = snapshot.CapturedAtUtcTicks,
                ReplayData = snapshot.EncodedData,
                ModVersion = _modVersion,
                RetryCount = 0,
                RetryAfterTicks = 0
            };

            if (_uploadWorker != null)
                _uploadWorker.Enqueue(payload);
        }

        // ── Cache + polling API (for ReplayUI) ─────────────────────────────

        public void SetLeaderboardCache(LeaderboardCache cache)
        {
            _leaderboardCache = cache;
        }

        /// <summary>
        /// Start polling a room's leaderboard (while the UI is viewing it).
        /// Shows cached data immediately, then polls for updates.
        /// </summary>
        public void StartLeaderboardPolling(string scene)
        {
            _pollScene = scene;
            _pollTimer = 999f; // trigger immediately
            _pollInFlight = false;
            _consecutiveNoChange = 0;
            _lastPollWasChange = false;
            _menuOpen = true;

            // If we don't have data for this room, trigger an immediate fetch.
            if (_leaderboardCache != null
                && !_leaderboardCache.HasRoomData(_gameTag, scene))
            {
                FetchRoomLeaderboard(scene);
            }
        }

        public void StopLeaderboardPolling()
        {
            _pollScene = null;
            _pollInFlight = false;
            _menuOpen = false;
        }

        public bool IsLeaderboardPolling => _pollScene != null;

        /// <summary>
        /// Notify NetworkClient that the menu is open (affects scene index cadence).
        /// </summary>
        public void SetMenuOpen(bool open)
        {
            _menuOpen = open;
        }

        /// <summary>
        /// Prefetch a room's leaderboard in the background (called on room enter).
        /// If data is already fresh, this is a no-op.
        /// </summary>
        public void PrefetchRoom(string scene)
        {
            if (!_started || _http == null) return;
            if (_leaderboardCache == null) return;

            // Skip if data is fresh enough.
            if (_leaderboardCache.IsRoomFresh(_gameTag, scene, RoomFreshnessThreshold))
                return;

            FetchRoomLeaderboard(scene);
        }

        // ── Startup: GET /init ─────────────────────────────────────────────

        private void FetchInit()
        {
            if (_http == null) return;

            string url = _apiBaseUrl + "/init?game="
                + Uri.EscapeDataString(_gameTag);

            var headers = MakeHeaders();
            headers["X-Mod-Version"] = _modVersion;

            _sceneIndexStatus = ManifestStatus.Loading;

            _http.Get(url, InitTimeoutSec, (success, status, body) =>
            {
                if (success)
                {
                    _health.RecordSuccess();
                    var init = ApiJson.ParseInitResponse(body);

                    // Apply config.
                    _serverConfig = init.Config;
                    _configFetched = true;
                    _maintenanceMode = init.Config.Maintenance;

                    if (_maintenanceMode)
                        Log.LogInfo("[NetworkClient] Maintenance mode — uploads paused");
                    if (!string.IsNullOrEmpty(init.Config.Announcement))
                        Log.LogInfo("[NetworkClient] Announcement: "
                            + init.Config.Announcement);

                    // Apply scene index.
                    if (_leaderboardCache != null)
                    {
                        _leaderboardCache.UpdateSceneIndex(
                            init.SceneIndexVersion, init.Scenes);
                    }

                    _sceneIndexStatus = ManifestStatus.Loaded;
                    _sceneIndexError = null;
                    _sceneIndexFailures = 0;
                    _sceneIndexInterval = SceneIndexInterval_Gameplay;

                    Log.LogInfo("[NetworkClient] Init loaded: "
                        + init.Scenes.Count + " scenes");

                    OnManifestReady?.Invoke();
                    OnLeaderboardUpdated?.Invoke();
                }
                else
                {
                    _health.RecordFailure();
                    _sceneIndexStatus = ManifestStatus.Failed;
                    _sceneIndexError = body;
                    _sceneIndexFailures = 1;
                    _sceneIndexInterval = 10f;

                    Log.LogWarning("[NetworkClient] Init failed: " + body);
                    OnManifestFailed?.Invoke();
                }
            }, headers);
        }

        // ── Scene index polling: GET /scenes ───────────────────────────────

        private void TickSceneIndex()
        {
            if (_http == null || _leaderboardCache == null) return;
            if (_sceneIndexInFlight) return;
            if (!_health.ShouldAttemptPolling) return;

            _sceneIndexTimer += Time.unscaledDeltaTime;

            float interval = (_menuOpen
                ? SceneIndexInterval_MenuOpen
                : SceneIndexInterval_Gameplay) * _health.PollMultiplier;

            if (_sceneIndexTimer < interval) return;

            _sceneIndexTimer = 0f;
            _sceneIndexInFlight = true;

            int cachedVersion = _leaderboardCache.SceneIndexVersion;
            string url = _apiBaseUrl + "/scenes?game="
                + Uri.EscapeDataString(_gameTag)
                + "&v=" + cachedVersion;

            _http.Get(url, HttpTimeoutSec, (success, status, body) =>
            {
                _sceneIndexInFlight = false;

                if (success)
                {
                    _health.RecordSuccess();
                    var resp = ApiJson.ParseSceneIndexResponse(body);

                    if (resp.Changed && _leaderboardCache != null)
                    {
                        _leaderboardCache.UpdateSceneIndex(
                            resp.Version, resp.Scenes);

                        _sceneIndexStatus = ManifestStatus.Loaded;
                        _sceneIndexError = null;
                        _sceneIndexFailures = 0;

                        OnManifestReady?.Invoke();
                        OnLeaderboardUpdated?.Invoke();
                    }
                    // If !Changed, server confirmed our version is current. No-op.
                }
                else
                {
                    _health.RecordFailure();
                    _sceneIndexFailures++;
                    float backoff = 10f * (1 << System.Math.Min(
                        _sceneIndexFailures - 1, 3));
                    _sceneIndexInterval = System.Math.Min(backoff, 120f);
                }
            }, MakeHeaders());
        }

        // ── Room leaderboard: on-demand + polling ──────────────────────────

        private void FetchRoomLeaderboard(string scene)
        {
            if (_http == null || _leaderboardCache == null) return;

            // Deduplication: skip if already in-flight for this scene.
            if (_roomFetchInFlight.Contains(scene)) return;
            _roomFetchInFlight.Add(scene);

            int cachedVersion = _leaderboardCache
                .GetRoomServerVersion(_gameTag, scene);

            string url = _apiBaseUrl + "/leaderboard?game="
                + Uri.EscapeDataString(_gameTag)
                + "&scene=" + Uri.EscapeDataString(scene)
                + "&v=" + cachedVersion;

            _http.Get(url, HttpTimeoutSec, (success, status, body) =>
            {
                _roomFetchInFlight.Remove(scene);

                if (success)
                {
                    _health.RecordSuccess();
                    var resp = ApiJson.ParseVersionedLeaderboardResponse(body);

                    if (resp.Changed && _leaderboardCache != null)
                    {
                        bool contentChanged = _leaderboardCache.UpdateRoom(
                            _gameTag, scene, resp.Version, resp.Data);

                        _lastPollWasChange = true;
                        _consecutiveNoChange = 0;

                        if (contentChanged)
                            OnLeaderboardUpdated?.Invoke();
                    }
                    else
                    {
                        // 304 equivalent: version matched, data unchanged.
                        _lastPollWasChange = false;
                        _consecutiveNoChange++;
                    }
                }
                else
                {
                    _health.RecordFailure();
                    Log.LogInfo("[NetworkClient] Room fetch failed for "
                        + scene + ": " + body);
                }
            }, MakeHeaders());
        }

        private void TickRoomPoll()
        {
            if (_http == null || _pollScene == null) return;
            if (!_health.ShouldAttemptPolling) return;

            _pollTimer += Time.unscaledDeltaTime;

            float interval = ComputeRoomPollInterval() * _health.PollMultiplier;
            if (_pollTimer < interval) return;

            _pollTimer = 0f;
            FetchRoomLeaderboard(_pollScene);
        }

        private float ComputeRoomPollInterval()
        {
            if (_consecutiveNoChange > 3) return RoomPollInterval_Idle;
            if (_lastPollWasChange) return RoomPollInterval_Active;
            return RoomPollInterval_Default;
        }

        // ── Replay download ─────────────────────────────────────────────────

        public void DownloadReplay(string runId, Action<string?> onComplete)
        {
            if (!_started || _http == null || string.IsNullOrEmpty(runId)) return;

            string url = _apiBaseUrl + "/replay?run_id="
                + Uri.EscapeDataString(runId);

            _http.Get(url, HttpTimeoutSec, (success, status, body) =>
            {
                if (success)
                {
                    _health.RecordSuccess();
                    onComplete(ApiJson.ParseReplayData(body));
                }
                else
                {
                    _health.RecordFailure();
                    Log.LogInfo("[NetworkClient] Replay download failed: " + body);
                    onComplete(null);
                }
            }, MakeHeaders());
        }

        // ── Upload success handler ─────────────────────────────────────────

        private void HandleUploadSuccess(UploadPayload payload, UploadResponse response)
        {
            // Show rank on the timer HUD.
            if (response.HasRank)
            {
                var rankInfo = new RankInfo(
                    payload.SceneName,
                    payload.EntryFrom,
                    payload.ExitTo,
                    payload.TotalTime,
                    response.Rank,
                    response.TotalRunners);
                OnRankReceived?.Invoke(rankInfo);
            }

            // Optimistic local cache update — instant leaderboard update,
            // no network request needed.
            if (_leaderboardCache != null && response.HasRank)
            {
                string displayName = !string.IsNullOrEmpty(response.DisplayName)
                    ? response.DisplayName
                    : GhostSettings.DisplayName;

                bool changed = _leaderboardCache.ApplyOptimisticUpload(
                    _gameTag,
                    payload.SceneName,
                    payload.EntryFrom,
                    payload.ExitTo,
                    payload.TotalTime,
                    response.Rank,
                    response.TotalRunners,
                    displayName);

                if (changed)
                    OnLeaderboardUpdated?.Invoke();
            }
        }

        private void HandleDisplayNameReceived(string name)
        {
            OnDisplayNameReceived?.Invoke(name);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private Dictionary<string, string> MakeHeaders()
        {
            return new Dictionary<string, string>
            {
                { "X-Device-Id", _deviceId }
            };
        }

        private static string TrimTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return url.EndsWith("/") ? url.Substring(0, url.Length - 1) : url;
        }

        // ── Public state queries ───────────────────────────────────────────

        public bool IsStarted => _started;
        public bool IsMaintenanceMode => _maintenanceMode;
        public bool HasServerConfig => _configFetched;
        public string GameTag => _gameTag;
        public string? ServerAnnouncement => _serverConfig?.Announcement;
    }
}