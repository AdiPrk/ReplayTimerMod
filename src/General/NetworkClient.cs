using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Central networking class for the replay mod.
    ///
    /// Lifecycle:
    ///   Created in mod entry point (Initialize/Awake)
    ///   Start() called after Setup (hero ready)
    ///   Tick(deltaTime) called every frame from the mod's update loop
    ///   Stop() called on mod teardown / OnDestroy
    ///
    /// Threading:
    ///   All public methods are called from the Unity main thread.
    ///   Background work (uploads, config fetch, leaderboard fetch) runs on worker threads.
    ///   Results are dispatched back to the main thread via _mainCallbacks.
    ///
    /// Compatibility:
    ///   Compiles on net35. Uses only ThreadPool, lock, Queue, HttpWebRequest,
    ///   ManualResetEvent. No Task, no async/await, no ConcurrentQueue.
    /// </summary>
    public sealed class NetworkClient
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("NetworkClient");

        private const int MaxCallbacksPerTick = 4;
        private const int HttpTimeoutMs = 10000;
        private const float PollIntervalSeconds = 5.0f;

        /// <summary>
        /// How often (seconds) to re-fetch the full manifest in the background.
        /// </summary>
        private const float ManifestRefreshSeconds = 60.0f;

        /// <summary>
        /// Longer timeout for the manifest request since it returns more data.
        /// </summary>
        private const int ManifestTimeoutMs = 30000;

        // ── Immutable config ───────────────────────────────────────────────

        private readonly string _deviceId;
        private readonly string _gameTag;       // "hk_1221", "hk_1578", "silksong"
        private readonly string _modVersion;
        private readonly string _apiBaseUrl;

        // ── Main-thread callback queue ─────────────────────────────────────

        private readonly object _callbackLock = new object();
        private readonly Queue<Action> _mainCallbacks = new Queue<Action>();

        // ── Sub-components ─────────────────────────────────────────────────

        private UploadWorker? _uploadWorker;
        private bool _started;

        // ── State ──────────────────────────────────────────────────────────

        private ConfigResponse? _serverConfig;
        private bool _configFetched;
        private bool _maintenanceMode;

        // ── Leaderboard polling (active room) ──────────────────────────────

        private LeaderboardCache? _leaderboardCache;  // owned by ReplayUI, set via setter
        private string? _pollScene;
        private float _pollTimer;
        private bool _pollInFlight;

        // ── Manifest (all rooms) ───────────────────────────────────────────

        public enum ManifestStatus { NotStarted, Loading, Loaded, Failed }

        private float _manifestTimer;
        private float _manifestInterval = 0f;   // 0 = fetch immediately
        private bool _manifestInFlight;
        private bool _manifestFetched;
        private int _manifestFailures;
        private ManifestStatus _manifestStatus = ManifestStatus.NotStarted;
        private string? _manifestError;

        public ManifestStatus CurrentManifestStatus => _manifestStatus;
        public string? LastManifestError => _manifestError;

        // ── Events (fired on main thread) ──────────────────────────────────

        /// <summary>
        /// Fired after a successful upload returns a rank.
        /// </summary>
        public event Action<RankInfo>? OnRankReceived;

        /// <summary>
        /// Fired when the server assigns or updates the display name.
        /// </summary>
        public event Action<string>? OnDisplayNameReceived;

        /// <summary>
        /// Fired when leaderboard data has been updated in the cache.
        /// </summary>
        public event Action? OnLeaderboardUpdated;

        /// <summary>
        /// Fired when the manifest has been fetched (first time or refresh).
        /// </summary>
        public event Action? OnManifestReady;

        /// <summary>
        /// Fired when a manifest fetch fails, so the UI can surface sync
        /// status instead of failing silently. Retries happen automatically
        /// with backoff (10s → 20s → 40s → 60s).
        /// </summary>
        public event Action? OnManifestFailed;

        // ── Construction ───────────────────────────────────────────────────

        public NetworkClient(string deviceId, string gameTag,
            string modVersion, string apiBaseUrl)
        {
            _deviceId = deviceId;
            _gameTag = gameTag;
            _modVersion = modVersion;
            _apiBaseUrl = TrimTrailingSlash(apiBaseUrl);

            Log.LogInfo($"[NetworkClient] Initialized: game={_gameTag} " +
                $"device={_deviceId.Substring(0, 8)}... " +
                $"api={_apiBaseUrl}");
        }

        // ── Lifecycle ──────────────────────────────────────────────────────

        public void Start()
        {
            if (_started) return;
            _started = true;

            // .NET 3.5 / older Mono runtimes (the HK 1.2.2.1 and 1.5.7.8 builds)
            // default ServicePointManager.SecurityProtocol to SSL3 | TLS1.0.
            // Supabase (and basically every modern HTTPS host) rejects that
            // handshake outright, so every HttpWebRequest below would throw
            // "Could not create SSL/TLS secure channel" - caught and logged
            // quietly, leaving the leaderboard cache empty forever (shows
            // "Loading..." indefinitely). 3072 is SecurityProtocolType.Tls12;
            // it's not a named member on net35's reference assembly, but the
            // numeric cast compiles and works fine on the Mono runtimes both
            // games ship with. Silksong's netstandard2.1 runtime already
            // defaults to TLS1.2+, so OR-ing this in there is a harmless no-op.
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch
            {
                // Extremely old runtimes can throw NotSupportedException for
                // protocol bits they don't recognize at all - fall back to
                // whatever the platform default already is.
            }

            _uploadWorker = new UploadWorker(_apiBaseUrl, _deviceId, PostToMain);
            _uploadWorker.OnUploadSuccess += HandleUploadSuccess;
            _uploadWorker.OnDisplayNameReceived += HandleDisplayNameReceived;
            _uploadWorker.Start();

            // Fetch server config on a background thread
            ThreadPool.QueueUserWorkItem(_ => FetchConfig());

            // Kick off the initial manifest fetch
            _manifestTimer = 0f;
            _manifestInterval = 0f;   // immediate first fetch
            _manifestInFlight = false;
            _manifestFetched = false;
            _manifestFailures = 0;
            _manifestStatus = ManifestStatus.NotStarted;
        }

        public void Stop()
        {
            if (!_started) return;

            StopLeaderboardPolling();

            if (_uploadWorker != null)
            {
                _uploadWorker.OnUploadSuccess -= HandleUploadSuccess;
                _uploadWorker.OnDisplayNameReceived -= HandleDisplayNameReceived;
                _uploadWorker.Stop();
                _uploadWorker = null;
            }

            lock (_callbackLock)
            {
                _mainCallbacks.Clear();
            }

            _started = false;
            Log.LogInfo("[NetworkClient] Stopped");
        }

        public void Tick()
        {
            if (!_started) return;

            DrainCallbacks();
            TickPolling();
            TickManifest();
        }

        private void DrainCallbacks()
        {
            int budget = MaxCallbacksPerTick;
            while (budget > 0)
            {
                Action? action = null;
                lock (_callbackLock)
                {
                    if (_mainCallbacks.Count > 0)
                        action = _mainCallbacks.Dequeue();
                }
                if (action == null) break;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.LogError($"[NetworkClient] Callback error: {ex.Message}");
                }
                budget--;
            }
        }

        // ── Upload API ─────────────────────────────────────────────────────

        public void EnqueueUpload(ReplaySnapshot snapshot, EvaluationResult result)
        {
            if (!_started) return;
            if (_maintenanceMode) return;

            if (result.Kind != ResultKind.FirstRun
                && result.Kind != ResultKind.NewPB)
                return;

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

            _uploadWorker?.Enqueue(payload);
        }

        // ── Leaderboard polling (per-room, active room only) ───────────────

        public void SetLeaderboardCache(LeaderboardCache cache)
        {
            _leaderboardCache = cache;
        }

        public void StartLeaderboardPolling(string scene)
        {
            _pollScene = scene;
            _pollTimer = 999f;
            _pollInFlight = false;
        }

        public void StopLeaderboardPolling()
        {
            _pollScene = null;
            _pollInFlight = false;
        }

        public bool IsLeaderboardPolling => _pollScene != null;

        private void TickPolling()
        {
            if (_pollScene == null) return;
            if (_pollInFlight) return;
            if (_leaderboardCache == null) return;

            _pollTimer += Time.unscaledDeltaTime;
            if (_pollTimer < PollIntervalSeconds) return;

            _pollTimer = 0f;
            _pollInFlight = true;

            string scene = _pollScene;
            string game = _gameTag;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                LeaderboardData? data = FetchLeaderboard(game, scene);
                PostToMain(() =>
                {
                    _pollInFlight = false;

                    if (_pollScene != scene) return;

                    if (data != null && _leaderboardCache != null)
                    {
                        _leaderboardCache.Update(game, scene, data);
                        OnLeaderboardUpdated?.Invoke();
                    }
                });
            });
        }

        private LeaderboardData? FetchLeaderboard(string game, string scene)
        {
            try
            {
                string url = _apiBaseUrl + "/leaderboard?game="
                    + Uri.EscapeDataString(game)
                    + "&scene=" + Uri.EscapeDataString(scene);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/json";
                req.Timeout = HttpTimeoutMs;
                req.ReadWriteTimeout = HttpTimeoutMs;
                req.Headers.Add("X-Device-Id", _deviceId);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(),
                    Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    return ApiJson.ParseLeaderboardResponse(json);
                }
            }
            catch (Exception ex)
            {
                Log.LogInfo("[NetworkClient] Leaderboard fetch failed: " + ex.Message);
                return null;
            }
        }

        // ── Manifest (all rooms at once) ───────────────────────────────────

        private void TickManifest()
        {
            if (_manifestInFlight) return;
            if (_leaderboardCache == null) return;

            _manifestTimer += Time.unscaledDeltaTime;
            if (_manifestTimer < _manifestInterval) return;

            _manifestTimer = 0f;
            _manifestInFlight = true;
            if (_manifestStatus != ManifestStatus.Loaded)
                _manifestStatus = ManifestStatus.Loading;

            string game = _gameTag;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? fetchError;
                var manifest = FetchManifest(game, out fetchError);
                PostToMain(() =>
                {
                    _manifestInFlight = false;

                    if (manifest != null && _leaderboardCache != null)
                    {
                        _leaderboardCache.UpdateFromManifest(game, manifest);
                        _manifestFetched = true;
                        _manifestFailures = 0;
                        _manifestStatus = ManifestStatus.Loaded;
                        _manifestError = null;
                        _manifestInterval = ManifestRefreshSeconds;

                        Log.LogInfo($"[NetworkClient] Manifest loaded: " +
                            $"{manifest.Count} rooms");

                        OnManifestReady?.Invoke();
                        OnLeaderboardUpdated?.Invoke();
                    }
                    else
                    {
                        // Failure: retry with backoff 10s → 20s → 40s → 60s
                        // instead of waiting the full refresh interval.
                        _manifestFailures++;
                        _manifestStatus = ManifestStatus.Failed;
                        _manifestError = fetchError;
                        float backoff = 10f * (1 << System.Math.Min(
                            _manifestFailures - 1, 2));
                        _manifestInterval = System.Math.Min(
                            backoff, ManifestRefreshSeconds);

                        Log.LogWarning("[NetworkClient] Manifest fetch failed (" +
                            (fetchError ?? "unknown") + ") — retrying in " +
                            _manifestInterval + "s");

                        OnManifestFailed?.Invoke();
                    }
                });
            });
        }

        private Dictionary<string, LeaderboardData>? FetchManifest(
            string game, out string? error)
        {
            error = null;
            try
            {
                string url = _apiBaseUrl + "/manifest?game="
                    + Uri.EscapeDataString(game);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/json";
                req.Timeout = ManifestTimeoutMs;
                req.ReadWriteTimeout = ManifestTimeoutMs;
                req.Headers.Add("X-Device-Id", _deviceId);
                // Do NOT set AutomaticDecompression: the property exists on
                // net35 but is broken / unimplemented on Unity's old Mono
                // runtime (throws PlatformNotSupportedException or silently
                // produces garbage). Decompress manually below instead.
                req.Headers.Add("Accept-Encoding", "gzip, deflate");

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var baseStream = resp.GetResponseStream())
                using (var stream = DecompressIfNeeded(baseStream, resp.ContentEncoding))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    return ApiJson.ParseManifestResponse(json);
                }
            }
            catch (Exception ex)
            {
                // Include inner exception and type so the log file shows the
                // real failure (e.g. TLS handshake, PlatformNotSupported, etc.)
                error = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException != null)
                    error += " --> " + ex.InnerException.GetType().Name
                             + ": " + ex.InnerException.Message;
                return null;
            }
        }

        /// <summary>
        /// Wraps <paramref name="stream"/> in the appropriate decompression
        /// stream based on the Content-Encoding header. Falls back to the
        /// raw stream if the encoding is unrecognised or decompression isn't
        /// available - better to return corrupted JSON (which the parser
        /// silently ignores) than to throw and kill the fetch entirely.
        /// </summary>
        private static System.IO.Stream DecompressIfNeeded(
            System.IO.Stream stream, string contentEncoding)
        {
            if (string.IsNullOrEmpty(contentEncoding))
                return stream;

            string enc = contentEncoding.ToLowerInvariant().Trim();
            try
            {
                if (enc == "gzip")
                    return new System.IO.Compression.GZipStream(
                        stream, System.IO.Compression.CompressionMode.Decompress);
                if (enc == "deflate")
                    return new System.IO.Compression.DeflateStream(
                        stream, System.IO.Compression.CompressionMode.Decompress);
            }
            catch
            {
                // GZipStream/DeflateStream unavailable on this runtime - fall
                // through and return the raw stream.
            }
            return stream;
        }

        public void InvalidateManifest()
        {
            _manifestTimer = 0f;
            _manifestInterval = 0f; // re-fetch on next tick
        }

        public bool ManifestFetched => _manifestFetched;

        // ── Replay download ─────────────────────────────────────────────────

        public void DownloadReplay(string runId, Action<string?> onComplete)
        {
            if (!_started || string.IsNullOrEmpty(runId)) return;

            string id = runId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? replayData = FetchReplayBlob(id);
                PostToMain(() => onComplete(replayData));
            });
        }

        private string? FetchReplayBlob(string runId)
        {
            try
            {
                string url = _apiBaseUrl + "/replay?run_id="
                    + Uri.EscapeDataString(runId);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Accept = "application/json";
                req.Timeout = HttpTimeoutMs;
                req.ReadWriteTimeout = HttpTimeoutMs;
                req.Headers.Add("X-Device-Id", _deviceId);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(),
                    Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    return ApiJson.ParseReplayData(json);
                }
            }
            catch (Exception ex)
            {
                Log.LogInfo("[NetworkClient] Replay download failed: " + ex.Message);
                return null;
            }
        }

        // ── Config fetch ───────────────────────────────────────────────────

        private void FetchConfig()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(
                    _apiBaseUrl + "/config");
                req.Method = "GET";
                req.Accept = "application/json";
                req.Timeout = HttpTimeoutMs;
                req.ReadWriteTimeout = HttpTimeoutMs;
                req.Headers.Add("X-Device-Id", _deviceId);
                req.Headers.Add("X-Mod-Version", _modVersion);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(),
                    Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    var config = ApiJson.ParseConfigResponse(json);

                    PostToMain(() =>
                    {
                        _serverConfig = config;
                        _configFetched = true;
                        _maintenanceMode = config.Maintenance;

                        if (_maintenanceMode)
                            Log.LogInfo("[NetworkClient] Server in maintenance mode — " +
                                "uploads paused");

                        if (!string.IsNullOrEmpty(config.Announcement))
                            Log.LogInfo($"[NetworkClient] Server announcement: " +
                                config.Announcement);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.LogInfo($"[NetworkClient] Config fetch failed (non-critical): " +
                    ex.Message);
            }
        }

        // ── Internal handlers ──────────────────────────────────────────────

        private void HandleUploadSuccess(UploadPayload payload, UploadResponse response)
        {
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

            InvalidateManifest();
        }

        private void HandleDisplayNameReceived(string name)
        {
            OnDisplayNameReceived?.Invoke(name);
        }

        // ── Thread-safe dispatch ───────────────────────────────────────────

        private void PostToMain(Action action)
        {
            lock (_callbackLock)
            {
                _mainCallbacks.Enqueue(action);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static string TrimTrailingSlash(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            return url.EndsWith("/") ? url.Substring(0, url.Length - 1) : url;
        }

        // ── Public state queries (for UI) ──────────────────────────────────

        public bool IsStarted => _started;
        public bool IsMaintenanceMode => _maintenanceMode;
        public bool HasServerConfig => _configFetched;
        public string GameTag => _gameTag;
        public string? ServerAnnouncement => _serverConfig?.Announcement;
    }
}