using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Central networking class for the replay mod.
    ///
    /// Lifecycle:
    ///   Created in mod entry point (Initialize/Awake)
    ///   Start() called after Setup (hero ready)
    ///   Tick() called every frame from the mod's update loop
    ///   Stop() called on mod teardown / OnDestroy
    ///
    /// Threading:
    ///   All public methods are called from the Unity main thread.
    ///   Background work (uploads, config fetch) runs on worker threads.
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

        // ── Immutable config ───────────────────────────────────────────────

        private readonly string _deviceId;
        private readonly string _gameTag;       // "hk_1221", "hk_1578", "silksong"
        private readonly string _modVersion;
        private readonly string _apiBaseUrl;

        // ── Main-thread callback queue ─────────────────────────────────────

        private readonly object _callbackLock = new object();
        private readonly Queue<Action> _mainCallbacks = new Queue<Action>();

        // ── Sub-components ─────────────────────────────────────────────────

        private UploadWorker _uploadWorker;
        private bool _started;

        // ── State ──────────────────────────────────────────────────────────

        private ConfigResponse _serverConfig;
        private bool _configFetched;
        private bool _maintenanceMode;

        // ── Events (fired on main thread) ──────────────────────────────────

        /// <summary>
        /// Fired after a successful upload returns a rank.
        /// Consumed by RoomTimerHUD to display rank overlay.
        /// </summary>
        public event Action<RankInfo> OnRankReceived;

        /// <summary>
        /// Fired when the server assigns or updates the display name.
        /// Consumed by the mod entry point to persist in settings.
        /// </summary>
        public event Action<string> OnDisplayNameReceived;

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

        /// <summary>
        /// Starts the upload worker and fetches server config.
        /// Called once, after the hero is ready and UI is set up.
        /// </summary>
        public void Start()
        {
            if (_started) return;
            _started = true;

            _uploadWorker = new UploadWorker(_apiBaseUrl, _deviceId, PostToMain);
            _uploadWorker.OnUploadSuccess += HandleUploadSuccess;
            _uploadWorker.OnDisplayNameReceived += HandleDisplayNameReceived;
            _uploadWorker.Start();

            // Fetch server config on a background thread
            ThreadPool.QueueUserWorkItem(_ => FetchConfig());
        }

        /// <summary>
        /// Shuts down the upload worker and clears pending callbacks.
        /// </summary>
        public void Stop()
        {
            if (!_started) return;

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

        /// <summary>
        /// Drains the main-thread callback queue. Call every frame from the
        /// mod's update loop (alongside replayUI.Tick() etc).
        /// </summary>
        public void Tick()
        {
            if (!_started) return;

            int budget = MaxCallbacksPerTick;
            while (budget > 0)
            {
                Action action = null;
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

        /// <summary>
        /// Enqueues a completed run for background upload.
        /// Called from the mod entry point after PBManager.Evaluate().
        ///
        /// Only uploads new PBs and first runs. Missed PBs, duplicates,
        /// and history saves are NOT uploaded.
        ///
        /// Cost on the critical path: one object allocation + one lock +
        /// one ManualResetEvent.Set(). Total: sub-microsecond.
        /// </summary>
        public void EnqueueUpload(ReplaySnapshot snapshot, EvaluationResult result)
        {
            if (!_started) return;
            if (_maintenanceMode) return;

            // Only upload PBs and first runs
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
                // Config fetch failure is silent — everything still works.
                // Defaults: no maintenance, no announcement.
            }
        }

        // ── Internal handlers ──────────────────────────────────────────────

        private void HandleUploadSuccess(UploadPayload payload, UploadResponse response)
        {
            // Already on main thread (dispatched by UploadWorker via PostToMain)
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
        }

        private void HandleDisplayNameReceived(string name)
        {
            // Already on main thread
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
        public string ServerAnnouncement =>
            _serverConfig != null ? _serverConfig.Announcement : null;
    }
}