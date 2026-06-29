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
    /// Scene-index lifecycle is surfaced to ReplayUI via OnSceneIndexReady /
    /// OnSceneIndexFailed plus the CurrentSceneIndexStatus / LastSceneIndexError /
    /// SceneIndexFetched queries.
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
        private const float RoomPollInterval_Active = 5f;
        private const float RoomPollInterval_Default = 8f;
        private const float RoomPollInterval_Idle = 12f;
        private const float RoomFreshnessThreshold = 30f;

        // Sentinel assigned to a poll timer to force its next tick to fire
        // immediately (any value past the largest poll interval works).
        private const float PollNow = 999f;

        // ── Immutable config ───────────────────────────────────────────────

        private readonly string _deviceId;
        private readonly string _gameTag;
        private readonly string _modVersion;
        private readonly string _apiBaseUrl;

        // Header dictionaries are immutable per client (device id never
        // changes), so build them once instead of allocating a new dict per
        // request. _headers is the default; _initHeaders also carries the mod
        // version (sent on /init alongside uploads). HttpService only reads
        // these, so sharing one instance across concurrent requests is safe.
        private readonly Dictionary<string, string> _headers;
        private readonly Dictionary<string, string> _initHeaders;

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

        public enum SceneIndexStatus { NotStarted, Loading, Loaded, Failed }

        private LeaderboardCache? _leaderboardCache;
        private float _sceneIndexTimer;
        private bool _sceneIndexInFlight;
        private SceneIndexStatus _sceneIndexStatus = SceneIndexStatus.NotStarted;
        private string? _sceneIndexError;

        // Scene-index status queries (read by the panel's scene list)
        public SceneIndexStatus CurrentSceneIndexStatus => _sceneIndexStatus;
        public string? LastSceneIndexError => _sceneIndexError;
        public bool SceneIndexFetched => _sceneIndexStatus == SceneIndexStatus.Loaded;

        // ── Room leaderboard polling ───────────────────────────────────────

        private string? _pollScene;
        private float _pollTimer;
        private int _consecutiveNoChange;   // for adaptive interval
        private bool _lastPollWasChange;
        private bool _menuOpen;             // panel open? gates scene-index polling

        // ── Request deduplication ──────────────────────────────────────────

        private readonly HashSet<string> _roomFetchInFlight = new HashSet<string>();

        // ── Events (fired on main thread) ──────────────────────────────────

        public event Action<RankInfo>? OnRankReceived;
        public event Action<string>? OnDisplayNameReceived;
        public event Action? OnLeaderboardUpdated;
        public event Action? OnSceneIndexReady;   // scene index loaded/changed
        public event Action? OnSceneIndexFailed;  // scene index fetch failed

        /// <summary>
        /// Fired after a successful upload once the server run id is known
        /// (snapshotId, route key, runId). Lets the snapshot cache its run id
        /// so the replay can be shared by pointer later without re-uploading.
        /// </summary>
        public event Action<string, RoomKey, string>? OnRunIdAssigned;

        // ── Construction ───────────────────────────────────────────────────

        public NetworkClient(string deviceId, string gameTag,
            string modVersion, string apiBaseUrl)
        {
            _deviceId = deviceId;
            _gameTag = gameTag;
            _modVersion = modVersion;
            _apiBaseUrl = TrimTrailingSlash(apiBaseUrl);

            _headers = new Dictionary<string, string>
            {
                { "X-Device-Id", _deviceId }
            };
            _initHeaders = new Dictionary<string, string>
            {
                { "X-Device-Id", _deviceId },
                { "X-Mod-Version", _modVersion }
            };
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
            _pollTimer = PollNow;
            _consecutiveNoChange = 0;
            _lastPollWasChange = false;

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
        }

        public bool IsLeaderboardPolling => _pollScene != null;

        /// <summary>
        /// Notify NetworkClient whether the replay panel is open. The scene
        /// index is only consumed by the panel's scene list, so it is polled
        /// ONLY while the panel is open — during gameplay the request is
        /// skipped entirely. Opening the panel forces an immediate refresh so
        /// the list re-syncs with whatever changed since it was last open.
        /// </summary>
        public void SetMenuOpen(bool open)
        {
            if (_menuOpen == open) return;
            _menuOpen = open;
            if (open)
                _sceneIndexTimer = PollNow; // refresh scene index on next tick
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

            _sceneIndexStatus = SceneIndexStatus.Loading;

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

                    _sceneIndexStatus = SceneIndexStatus.Loaded;
                    _sceneIndexError = null;

                    Log.LogInfo("[NetworkClient] Init loaded: "
                        + init.Scenes.Count + " scenes");

                    OnSceneIndexReady?.Invoke();
                    OnLeaderboardUpdated?.Invoke();
                }
                else
                {
                    _health.RecordFailure();
                    _sceneIndexStatus = SceneIndexStatus.Failed;
                    _sceneIndexError = body;

                    Log.LogWarning("[NetworkClient] Init failed: " + body);
                    OnSceneIndexFailed?.Invoke();
                }
            }, _initHeaders);
        }

        // ── Scene index polling: GET /scenes ───────────────────────────────

        private void TickSceneIndex()
        {
            if (_http == null || _leaderboardCache == null) return;
            // The scene index only feeds the panel's scene list, so there is
            // nothing to keep fresh while the panel is closed. Skipping the
            // request entirely during gameplay saves a poll every 60s for the
            // whole session.
            if (!_menuOpen) return;
            if (_sceneIndexInFlight) return;
            if (!_health.ShouldAttemptPolling) return;

            _sceneIndexTimer += Time.unscaledDeltaTime;

            // Failure backoff is handled by the health multiplier (1x/2x/10x),
            // and ShouldAttemptPolling stops us entirely once the server is Down.
            float interval = SceneIndexInterval_MenuOpen * _health.PollMultiplier;
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

                        _sceneIndexStatus = SceneIndexStatus.Loaded;
                        _sceneIndexError = null;

                        OnSceneIndexReady?.Invoke();
                        OnLeaderboardUpdated?.Invoke();
                    }
                    // If !Changed, server confirmed our version is current. No-op.
                }
                else
                {
                    _health.RecordFailure();
                }
            }, _headers);
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
            }, _headers);
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

        public void DownloadReplay(string runId, Action<byte[]?> onComplete)
        {
            if (!_started || _http == null || string.IsNullOrEmpty(runId)) return;

            string url = _apiBaseUrl + "/replay?run_id="
                + Uri.EscapeDataString(runId);

            // Raw compressed RTM3 bytes — no JSON/base64 envelope on the wire.
            _http.GetBinary(url, HttpTimeoutSec, (success, status, data, error) =>
            {
                if (success)
                {
                    _health.RecordSuccess();
                    onComplete(data);
                }
                else
                {
                    _health.RecordFailure();
                    Log.LogInfo("[NetworkClient] Replay download failed: " + error);
                    onComplete(null);
                }
            }, _headers);
        }

        // ── Replay sharing (share-by-pointer) ───────────────────────────────

        /// <summary>Mint (or fetch the existing) share code for an uploaded run.</summary>
        internal void CreateShareByRunId(string runId, Action<ShareResponse?> onComplete)
        {
            if (!_started || _http == null || string.IsNullOrEmpty(runId))
            {
                onComplete(null);
                return;
            }

            _http.Post(_apiBaseUrl + "/share",
                ApiJson.SerializeShareByRunId(runId), HttpTimeoutSec,
                (success, status, body) =>
                {
                    if (success)
                    {
                        _health.RecordSuccess();
                        onComplete(ApiJson.ParseShareResponse(body));
                    }
                    else
                    {
                        _health.RecordFailure();
                        Log.LogInfo("[NetworkClient] Share (run) failed: " + body);
                        onComplete(null);
                    }
                }, _headers);
        }

        /// <summary>Mint (or fetch the existing) share code for an inline replay.</summary>
        internal void CreateShareByData(ReplaySnapshot snapshot,
            Action<ShareResponse?> onComplete)
        {
            if (!_started || _http == null || snapshot == null)
            {
                onComplete(null);
                return;
            }

            string body = ApiJson.SerializeShareByData(
                _gameTag,
                snapshot.Key.SceneName,
                snapshot.Key.EntryFromScene,
                snapshot.Key.ExitToScene,
                snapshot.TotalTime,
                snapshot.Room.FrameCount,
                snapshot.EncodedData);

            _http.Post(_apiBaseUrl + "/share", body, HttpTimeoutSec,
                (success, status, resp) =>
                {
                    if (success)
                    {
                        _health.RecordSuccess();
                        onComplete(ApiJson.ParseShareResponse(resp));
                    }
                    else
                    {
                        _health.RecordFailure();
                        Log.LogInfo("[NetworkClient] Share (data) failed: " + resp);
                        onComplete(null);
                    }
                }, _headers);
        }

        /// <summary>Resolve a share code to its replay bytes, or null.</summary>
        internal void ResolveShare(string code, Action<byte[]?> onComplete)
        {
            if (!_started || _http == null || string.IsNullOrEmpty(code))
            {
                onComplete(null);
                return;
            }

            string url = _apiBaseUrl + "/share?code=" + Uri.EscapeDataString(code);
            _http.GetBinary(url, HttpTimeoutSec, (success, status, data, error) =>
            {
                if (success)
                {
                    _health.RecordSuccess();
                    onComplete(data);
                }
                else
                {
                    _health.RecordFailure();
                    Log.LogInfo("[NetworkClient] Resolve share failed: " + error);
                    onComplete(null);
                }
            }, _headers);
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

            // Surface the server run id so the originating snapshot can cache it
            // (enables instant share-by-pointer for this replay).
            if (!string.IsNullOrEmpty(response.RunId))
            {
                OnRunIdAssigned?.Invoke(
                    payload.SnapshotId,
                    new RoomKey(payload.SceneName, payload.EntryFrom, payload.ExitTo),
                    response.RunId);
            }
        }

        private void HandleDisplayNameReceived(string name)
        {
            OnDisplayNameReceived?.Invoke(name);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string TrimTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return url.EndsWith("/") ? url.Substring(0, url.Length - 1) : url;
        }

        // ── Force refresh (after name change, etc.) ────────────────────────

        /// <summary>
        /// Invalidates all cached versions so the next poll fetches fresh
        /// data. Also resets the scene index timer for an immediate re-check.
        /// Call after a display name change so updated names appear quickly.
        /// </summary>
        public void ForceRefreshAll()
        {
            if (_leaderboardCache != null)
                _leaderboardCache.InvalidateAllRoomVersions();

            _sceneIndexTimer = PollNow;

            // If currently viewing a room, the next poll (5-12s) will
            // fetch fresh data. Reset the adaptive interval to be fast.
            _consecutiveNoChange = 0;
            _lastPollWasChange = true;
            _pollTimer = PollNow;
        }

        // ── Public state queries ───────────────────────────────────────────

        public bool IsStarted => _started;
        public bool IsMaintenanceMode => _maintenanceMode;
        public bool HasServerConfig => _configFetched;
        public string GameTag => _gameTag;
        public string ApiBaseUrl => _apiBaseUrl;
        public string? ServerAnnouncement => _serverConfig?.Announcement;
    }
}