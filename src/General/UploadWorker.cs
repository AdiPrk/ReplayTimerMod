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
    /// Dedicated background thread that processes the upload queue.
    ///
    /// Design:
    /// - Sleeps until signaled by Enqueue() or a 30-second timeout.
    /// - Drains the queue one payload at a time, with a short delay between
    ///   consecutive uploads to avoid hammering the server.
    /// - Failed uploads are re-queued with exponential backoff.
    /// - After MaxRetries failures, the payload is dropped.
    /// - On Stop(), the thread joins within 5 seconds (any in-flight HTTP
    ///   request is abandoned via timeout).
    ///
    /// All callbacks to the main thread go through the provided postToMain
    /// delegate, which must be thread-safe (NetworkClient provides this).
    /// </summary>
    internal sealed class UploadWorker
    {
        private const int MaxRetries = 5;
        private const int InterUploadDelayMs = 200;  // breathe between uploads
        private const int HttpTimeoutMs = 15000;     // 15s per request

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("UploadWorker");

        // Queue
        private readonly object _queueLock = new object();
        private readonly Queue<UploadPayload> _queue = new Queue<UploadPayload>();
        private readonly ManualResetEvent _signal = new ManualResetEvent(false);

        // Config (immutable after construction)
        private readonly string _apiBaseUrl;
        private readonly string _deviceId;
        private readonly Action<Action> _postToMain;

        // Thread lifecycle
        private Thread _thread;
        private volatile bool _alive;

        // Events (invoked on main thread via _postToMain)
        public event Action<UploadPayload, UploadResponse> OnUploadSuccess;
        public event Action<string> OnDisplayNameReceived;

        public UploadWorker(string apiBaseUrl, string deviceId,
            Action<Action> postToMain)
        {
            _apiBaseUrl = apiBaseUrl;
            _deviceId = deviceId;
            _postToMain = postToMain;
        }

        // ── Public API (called from main thread) ───────────────────────────

        public void Start()
        {
            if (_alive) return;
            _alive = true;
            _thread = new Thread(WorkerLoop)
            {
                Name = "RTM_UploadWorker",
                IsBackground = true
            };
            _thread.Start();
            Log.LogInfo("[UploadWorker] Started");
        }

        public void Stop()
        {
            if (!_alive) return;
            _alive = false;
            _signal.Set(); // wake thread so it can exit

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(5000);
                if (_thread.IsAlive)
                    Log.LogWarning("[UploadWorker] Thread did not exit cleanly");
            }

            lock (_queueLock)
            {
                int dropped = _queue.Count;
                _queue.Clear();
                if (dropped > 0)
                    Log.LogInfo($"[UploadWorker] Dropped {dropped} pending uploads on shutdown");
            }

            Log.LogInfo("[UploadWorker] Stopped");
        }

        public void Enqueue(UploadPayload payload)
        {
            lock (_queueLock)
            {
                // Cap queue size to prevent unbounded growth when offline
                if (_queue.Count >= 100)
                {
                    Log.LogWarning("[UploadWorker] Queue full (100), dropping oldest");
                    _queue.Dequeue();
                }
                _queue.Enqueue(payload);
            }
            _signal.Set();
        }

        public int QueueCount
        {
            get { lock (_queueLock) { return _queue.Count; } }
        }

        // ── Worker loop (runs on background thread) ────────────────────────

        private void WorkerLoop()
        {
            while (_alive)
            {
                _signal.WaitOne(TimeSpan.FromSeconds(30));
                _signal.Reset();

                if (!_alive) break;

                while (_alive)
                {
                    UploadPayload payload = null;
                    lock (_queueLock)
                    {
                        if (_queue.Count == 0) break;

                        // Peek to check retry eligibility without dequeuing
                        var candidate = _queue.Peek();
                        if (candidate.RetryAfterTicks > 0
                            && DateTime.UtcNow.Ticks < candidate.RetryAfterTicks)
                        {
                            // Not eligible yet — stop draining. The 30-second
                            // timeout or next Enqueue() will wake us.
                            break;
                        }

                        payload = _queue.Dequeue();
                    }

                    if (payload == null) break;

                    try
                    {
                        UploadResponse response = DoUpload(payload);

                        Log.LogInfo($"[UploadWorker] Uploaded {payload.SceneName}" +
                            $"[{payload.EntryFrom}→{payload.ExitTo}] " +
                            $"{TimeUtil.Format(payload.TotalTime)}" +
                            (response.HasRank
                                ? $" → Rank #{response.Rank}/{response.TotalRunners}"
                                : ""));

                        // Dispatch success to main thread
                        var p = payload;
                        var r = response;
                        _postToMain(() =>
                        {
                            OnUploadSuccess?.Invoke(p, r);

                            // If the server assigned a display name, propagate it
                            if (!string.IsNullOrEmpty(r.DisplayName))
                                OnDisplayNameReceived?.Invoke(r.DisplayName);
                        });
                    }
                    catch (WebException ex)
                    {
                        HandleFailure(payload, ex.Status.ToString(), ex.Message);
                    }
                    catch (Exception ex)
                    {
                        HandleFailure(payload, "Exception", ex.Message);
                    }

                    // Brief pause between consecutive uploads
                    if (_alive) Thread.Sleep(InterUploadDelayMs);
                }
            }
        }

        private void HandleFailure(UploadPayload payload, string status, string message)
        {
            payload.RetryCount++;

            if (payload.RetryCount > MaxRetries)
            {
                Log.LogWarning($"[UploadWorker] Dropping {payload.SceneName} " +
                    $"after {MaxRetries} retries: {status} {message}");
                return;
            }

            // Exponential backoff: 2s, 4s, 8s, 16s, 32s
            int delaySec = 1 << (payload.RetryCount); // 2, 4, 8, 16, 32
            payload.RetryAfterTicks = DateTime.UtcNow.AddSeconds(delaySec).Ticks;

            Log.LogInfo($"[UploadWorker] Retry {payload.RetryCount}/{MaxRetries} " +
                $"for {payload.SceneName} in {delaySec}s: {status}");

            lock (_queueLock)
            {
                _queue.Enqueue(payload);
            }
        }

        // ── HTTP (runs on background thread) ───────────────────────────────

        private UploadResponse DoUpload(UploadPayload payload)
        {
            string body = ApiJson.SerializeUpload(payload);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            var req = (HttpWebRequest)WebRequest.Create(
                _apiBaseUrl + "/runs");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = "application/json";
            req.Timeout = HttpTimeoutMs;
            req.ReadWriteTimeout = HttpTimeoutMs;

            // Custom headers for device identification
            req.Headers.Add("X-Device-Id", _deviceId);
            req.Headers.Add("X-Mod-Version", payload.ModVersion);

            // Set content length explicitly for net35 compatibility
            req.ContentLength = bodyBytes.Length;

            using (var stream = req.GetRequestStream())
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300)
                    throw new WebException("HTTP " + (int)resp.StatusCode);

                using (var reader = new StreamReader(resp.GetResponseStream(),
                    Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    return ApiJson.ParseUploadResponse(json);
                }
            }
        }
    }
}