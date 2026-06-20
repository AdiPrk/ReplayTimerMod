using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Processes the upload queue one request at a time.
    ///
    /// Tick-based: call Tick() every frame. When idle and the queue
    /// is non-empty, dequeues one payload and sends it via HttpService.
    /// Failed uploads are re-queued with exponential backoff.
    /// </summary>
    internal sealed class UploadWorker
    {
        private const int MaxRetries = 5;
        private const int HttpTimeoutSec = 15;

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("UploadWorker");

        private readonly HttpService _http;
        private readonly string _apiBaseUrl;
        private readonly string _deviceId;
        private readonly ConnectionHealth _health;

        private readonly Queue<UploadPayload> _queue = new Queue<UploadPayload>();
        private bool _inFlight;

        public event Action<UploadPayload, UploadResponse>? OnUploadSuccess;
        public event Action<string>? OnDisplayNameReceived;

        public UploadWorker(HttpService http, string apiBaseUrl,
            string deviceId, ConnectionHealth health)
        {
            _http = http;
            _apiBaseUrl = apiBaseUrl;
            _deviceId = deviceId;
            _health = health;
        }

        public void Enqueue(UploadPayload payload)
        {
            if (_queue.Count >= 100)
            {
                Log.LogWarning("[UploadWorker] Queue full (100), dropping oldest");
                _queue.Dequeue();
            }
            _queue.Enqueue(payload);
        }

        public int QueueCount => _queue.Count;

        public void Tick()
        {
            if (_inFlight) return;
            if (_queue.Count == 0) return;

            var candidate = _queue.Peek();
            if (candidate.RetryAfterTicks > 0
                && DateTime.UtcNow.Ticks < candidate.RetryAfterTicks)
                return;

            var payload = _queue.Dequeue();
            _inFlight = true;

            string body = ApiJson.SerializeUpload(payload);

            var headers = new Dictionary<string, string>
            {
                { "X-Device-Id", _deviceId },
                { "X-Mod-Version", payload.ModVersion }
            };

            var p = payload;

            _http.Post(_apiBaseUrl + "/runs", body, HttpTimeoutSec,
                (success, status, responseBody) =>
            {
                _inFlight = false;

                if (success)
                {
                    _health.RecordSuccess();
                    var response = ApiJson.ParseUploadResponse(responseBody);

                    Log.LogInfo("[UploadWorker] Uploaded " + p.SceneName
                        + "[" + p.EntryFrom + "→" + p.ExitTo + "] "
                        + TimeUtil.Format(p.TotalTime)
                        + (response.HasRank
                            ? " → #" + response.Rank + "/" + response.TotalRunners
                            : ""));

                    OnUploadSuccess?.Invoke(p, response);

                    if (!string.IsNullOrEmpty(response.DisplayName))
                        OnDisplayNameReceived?.Invoke(response.DisplayName);
                }
                else
                {
                    _health.RecordFailure();
                    HandleFailure(p, status.ToString(), responseBody);
                }
            }, headers);
        }

        private void HandleFailure(UploadPayload payload, string status, string message)
        {
            payload.RetryCount++;

            if (payload.RetryCount > MaxRetries)
            {
                Log.LogWarning("[UploadWorker] Dropping " + payload.SceneName
                    + " after " + MaxRetries + " retries: " + status + " " + message);
                return;
            }

            int delaySec = 1 << payload.RetryCount;
            payload.RetryAfterTicks = DateTime.UtcNow.AddSeconds(delaySec).Ticks;

            Log.LogInfo("[UploadWorker] Retry " + payload.RetryCount + "/" + MaxRetries
                + " for " + payload.SceneName + " in " + delaySec + "s: " + status);

            _queue.Enqueue(payload);
        }
    }
}