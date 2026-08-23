using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace ReplayTimerMod
{
    /// <summary>
    /// Non-blocking HTTP service built on UnityWebRequest.
    ///
    /// Why UnityWebRequest instead of HttpWebRequest?
    ///   HttpWebRequest goes through Mono's managed TLS stack, which is
    ///   broken on .NET 3.5 / old Unity runtimes (HK 1221). The
    ///   SecurityProtocolType.Tls12 hack doesn't work reliably because
    ///   the underlying native backend may not implement TLS 1.2 at all.
    ///   UnityWebRequest bypasses Mono entirely — it uses the platform's
    ///   native HTTP stack (libcurl on desktop, NSURLSession on macOS/iOS,
    ///   Java HTTP on Android). TLS 1.2+ works everywhere, automatically.
    ///
    /// API differences across Unity versions:
    ///   V1221 (Unity ~2017.1, net35):
    ///     Send() instead of SendWebRequest()
    ///     isError instead of isNetworkError/isHttpError
    ///     No timeout property — manual timeout via Abort()
    ///   V1578 + Silksong (Unity 2020+):
    ///     SendWebRequest(), Result enum, native timeout
    ///
    /// Usage:
    ///   Call Get / Post to start requests.
    ///   Call Tick() every frame to complete pending requests.
    ///   Callbacks fire on the main thread — no marshaling needed.
    ///
    /// All public methods must be called from the Unity main thread.
    /// </summary>
    internal sealed class HttpService
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("HttpService");

        /// <param name="success">True if the request completed with 2xx.</param>
        /// <param name="statusCode">HTTP status code, or 0 on network error.</param>
        /// <param name="body">Response body text, or error string on failure.</param>
        public delegate void HttpCallback(bool success, long statusCode, string body);

        /// <summary>
        /// Binary variant of <see cref="HttpCallback"/>. On success
        /// <paramref name="data"/> is the raw response bytes and
        /// <paramref name="error"/> is null; on failure data is null and error
        /// carries the diagnostic string.
        /// </summary>
        public delegate void HttpBinaryCallback(bool success, long statusCode,
            byte[]? data, string? error);

        private sealed class PendingRequest
        {
            public UnityWebRequest Request = null!;
            public HttpCallback? Callback;
            public HttpBinaryCallback? BinaryCallback;
            public float StartTime;
            public int TimeoutSeconds;
        }

        private readonly List<PendingRequest> _active = new List<PendingRequest>();

        // ── Request API ─────────────────────────────────────────────────────

        /// <summary>
        /// Start a GET request. The callback fires during a future Tick().
        /// </summary>
        public void Get(string url, int timeoutSeconds, HttpCallback callback,
            Dictionary<string, string>? headers = null)
        {
            var req = UnityWebRequest.Get(url);
            ApplyAndSend(req, timeoutSeconds, headers, "application/json",
                callback, null);
        }

        /// <summary>
        /// Start a GET request whose response is raw bytes (no JSON/base64
        /// envelope) — used for replay downloads. The callback fires during a
        /// future Tick().
        /// </summary>
        public void GetBinary(string url, int timeoutSeconds,
            HttpBinaryCallback callback, Dictionary<string, string>? headers = null)
        {
            var req = UnityWebRequest.Get(url); // attaches a DownloadHandlerBuffer
            ApplyAndSend(req, timeoutSeconds, headers, "application/octet-stream",
                null, callback);
        }

        /// <summary>
        /// Start a POST request with a JSON body. The callback fires
        /// during a future Tick().
        /// </summary>
        public void Post(string url, string jsonBody, int timeoutSeconds,
            HttpCallback callback, Dictionary<string, string>? headers = null)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

#if V1221
            // Unity ~2017.1 sends POST bodies with Transfer-Encoding: chunked by
            // default, which the runtime/CDN in front of Supabase stalls on — the
            // request never completes and hangs until timeout. Force a plain
            // Content-Length body. The property was removed in Unity 2019.3+, so
            // this is V1221-only; 1578/Silksong (Unity 2020+) already do this.
            req.chunkedTransfer = false;
#endif

            ApplyAndSend(req, timeoutSeconds, headers, "application/json",
                callback, null);
        }

        // ── Tick / lifecycle ────────────────────────────────────────────────

        /// <summary>
        /// Poll pending requests and invoke callbacks for completed ones.
        /// Call this every frame.
        /// </summary>
        public void Tick()
        {
            // Iterate backwards so removals don't shift indices.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                // A callback below may re-enter and call CancelAll(), clearing
                // the list mid-iteration; re-check before indexing.
                if (i >= _active.Count) continue;

                var p = _active[i];

#if V1221
                // Manual timeout — V1221's Unity has no timeout property.
                // Abort() marks the request as done with isError = true.
                if (!p.Request.isDone && p.TimeoutSeconds > 0
                    && Time.realtimeSinceStartup - p.StartTime > p.TimeoutSeconds)
                {
                    p.Request.Abort();
                }
#endif

                if (!p.Request.isDone) continue;

                _active.RemoveAt(i);

                bool success = IsSuccess(p.Request);
                long status = p.Request.responseCode;

                try
                {
                    if (p.BinaryCallback != null)
                    {
                        byte[]? data = success ? p.Request.downloadHandler?.data : null;
                        string? error = success ? null : GetErrorString(p.Request);
                        p.BinaryCallback(success, status, data, error);
                    }
                    else
                    {
                        string body = success
                            ? (p.Request.downloadHandler?.text ?? "")
                            : GetErrorString(p.Request);
                        p.Callback!(success, status, body);
                    }
                }
                catch (Exception ex)
                {
                    // ToString keeps the stack — this catch-all is the only
                    // diagnostic surface for parse errors in callbacks.
                    Log.LogError("[HttpService] Callback threw: " + ex);
                }

                p.Request.Dispose();
            }
        }

        /// <summary>
        /// Abort and dispose every in-flight request WITHOUT firing callbacks.
        /// Call on shutdown; callers must reset their own in-flight flags.
        /// </summary>
        public void CancelAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                try { _active[i].Request.Abort(); } catch { }
                try { _active[i].Request.Dispose(); } catch { }
            }
            _active.Clear();
        }

        // ── Internals ───────────────────────────────────────────────────────

        private void ApplyAndSend(UnityWebRequest req, int timeoutSeconds,
            Dictionary<string, string>? headers, string acceptType,
            HttpCallback? callback, HttpBinaryCallback? binaryCallback)
        {
            req.SetRequestHeader("Accept", acceptType);

            if (headers != null)
            {
                foreach (var kvp in headers)
                    req.SetRequestHeader(kvp.Key, kvp.Value);
            }

#if V1221
            // Unity ~2017.1: no timeout property, use Send() not SendWebRequest().
            req.Send();
#else
            // Unity 2020+ (V1578, Silksong): native timeout + SendWebRequest().
            req.timeout = timeoutSeconds;
            req.SendWebRequest();
#endif

            _active.Add(new PendingRequest
            {
                Request = req,
                Callback = callback,
                BinaryCallback = binaryCallback,
                StartTime = Time.realtimeSinceStartup,
                TimeoutSeconds = timeoutSeconds
            });
        }

        /// <summary>
        /// Cross-version success check.
        ///   V1221 (Unity ~2017.1): only has isError.
        ///   V1578 + Silksong (Unity 2020+): use Result enum
        ///     (isNetworkError/isHttpError deprecated on 1578, removed on SS).
        /// </summary>
        private static bool IsSuccess(UnityWebRequest req)
        {
#if V1221
            return !req.isError;
#else
            return req.result == UnityWebRequest.Result.Success;
#endif
        }

        private static string GetErrorString(UnityWebRequest req)
        {
            string err = req.error ?? "Unknown error";
            string body = req.downloadHandler?.text ?? "";
            // If there's a response body (e.g. server error JSON), include it.
            if (!string.IsNullOrEmpty(body) && body.Length < 500)
                return err + " | " + body;
            return err;
        }
    }
}