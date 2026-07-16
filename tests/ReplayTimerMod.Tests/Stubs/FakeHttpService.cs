// Test double for src/General/HttpService.cs. The real one is built on
// UnityWebRequest and can't run off-game, so the test project compiles this
// recording fake instead (same namespace/type name and the exact public
// surface the shared code calls). Tests inspect .Requests and complete them
// manually, which drives UploadWorker's callbacks synchronously.
//
// KEEP THE SIGNATURES IN SYNC with src/General/HttpService.cs.

using System.Collections.Generic;

namespace ReplayTimerMod
{
    internal sealed class HttpService
    {
        public delegate void HttpCallback(bool success, long statusCode, string body);

        public delegate void HttpBinaryCallback(bool success, long statusCode,
            byte[]? data, string? error);

        public sealed class SentRequest
        {
            public string Method = "";
            public string Url = "";
            public string Body = "";
            public int TimeoutSeconds;
            public Dictionary<string, string>? Headers;
            public HttpCallback? Callback;
            public HttpBinaryCallback? BinaryCallback;
            public bool Completed;

            public void Complete(bool success, long statusCode, string body)
            {
                Completed = true;
                Callback?.Invoke(success, statusCode, body);
            }

            public void CompleteBinary(bool success, long statusCode,
                byte[]? data, string? error)
            {
                Completed = true;
                BinaryCallback?.Invoke(success, statusCode, data, error);
            }
        }

        public readonly List<SentRequest> Requests = new List<SentRequest>();

        public void Get(string url, int timeoutSeconds, HttpCallback callback,
            Dictionary<string, string>? headers = null)
        {
            Requests.Add(new SentRequest
            {
                Method = "GET",
                Url = url,
                TimeoutSeconds = timeoutSeconds,
                Headers = headers,
                Callback = callback
            });
        }

        public void GetBinary(string url, int timeoutSeconds,
            HttpBinaryCallback callback, Dictionary<string, string>? headers = null)
        {
            Requests.Add(new SentRequest
            {
                Method = "GET",
                Url = url,
                TimeoutSeconds = timeoutSeconds,
                Headers = headers,
                BinaryCallback = callback
            });
        }

        public void Post(string url, string jsonBody, int timeoutSeconds,
            HttpCallback callback, Dictionary<string, string>? headers = null)
        {
            Requests.Add(new SentRequest
            {
                Method = "POST",
                Url = url,
                Body = jsonBody,
                TimeoutSeconds = timeoutSeconds,
                Headers = headers,
                Callback = callback
            });
        }

        public void Tick() { }

        public void AbortAll() { }
    }
}
