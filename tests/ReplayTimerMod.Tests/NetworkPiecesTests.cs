using System;
using System.Collections.Generic;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class ConnectionHealthTests
    {
        [Fact]
        public void StartsHealthy()
        {
            var h = new ConnectionHealth();
            Assert.Equal(ConnectionHealth.State.Healthy, h.CurrentState);
            Assert.Equal(1f, h.PollMultiplier);
            Assert.True(h.ShouldAttemptPolling);
        }

        [Fact]
        public void DegradesAtThree_DownAtEight()
        {
            var h = new ConnectionHealth();
            for (int i = 0; i < 2; i++) h.RecordFailure();
            Assert.Equal(ConnectionHealth.State.Healthy, h.CurrentState);

            h.RecordFailure(); // 3rd
            Assert.Equal(ConnectionHealth.State.Degraded, h.CurrentState);
            Assert.Equal(2f, h.PollMultiplier);
            Assert.True(h.ShouldAttemptPolling);

            for (int i = 0; i < 4; i++) h.RecordFailure(); // 7th
            Assert.Equal(ConnectionHealth.State.Degraded, h.CurrentState);

            h.RecordFailure(); // 8th
            Assert.Equal(ConnectionHealth.State.Down, h.CurrentState);
            Assert.Equal(10f, h.PollMultiplier);
            Assert.False(h.ShouldAttemptPolling);
        }

        [Fact]
        public void SingleSuccess_FullyRecovers()
        {
            var h = new ConnectionHealth();
            for (int i = 0; i < 20; i++) h.RecordFailure();
            Assert.Equal(ConnectionHealth.State.Down, h.CurrentState);

            h.RecordSuccess();
            Assert.Equal(ConnectionHealth.State.Healthy, h.CurrentState);
            Assert.Equal(0, h.ConsecutiveFailures);
            Assert.True(h.ShouldAttemptPolling);
        }
    }

    public class UploadWorkerTests
    {
        private static UploadPayload Payload(string scene = "Bone_East_10") =>
            new UploadPayload
            {
                SnapshotId = "snap",
                Game = "silksong",
                SceneName = scene,
                EntryFrom = "A",
                ExitTo = "B",
                TotalTime = 3.21f,
                FrameCount = 96,
                CapturedAtUtcTicks = DateTime.UtcNow.Ticks,
                ReplayData = "QUJDRA==",
                ModVersion = "1.2.3",
                ModifierMask = 5,
            };

        private static (UploadWorker worker, HttpService http, ConnectionHealth health)
            Make()
        {
            var http = new HttpService();
            var health = new ConnectionHealth();
            var worker = new UploadWorker(http, "https://api.test/functions/v1",
                "0123456789abcdef0123456789abcdef", health);
            return (worker, http, health);
        }

        [Fact]
        public void Tick_SendsOneRequest_WithIdentityHeaders()
        {
            var (worker, http, _) = Make();
            worker.Enqueue(Payload());
            worker.Tick();

            var req = Assert.Single(http.Requests);
            Assert.Equal("POST", req.Method);
            Assert.Equal("https://api.test/functions/v1/runs", req.Url);
            Assert.Equal("0123456789abcdef0123456789abcdef",
                req.Headers!["X-Device-Id"]);
            Assert.Equal("1.2.3", req.Headers["X-Mod-Version"]);
            Assert.Contains("\"modifiers\":5", req.Body);
        }

        [Fact]
        public void SingleFlight_SecondTickSendsNothingWhileInFlight()
        {
            var (worker, http, _) = Make();
            worker.Enqueue(Payload("one"));
            worker.Enqueue(Payload("two"));

            worker.Tick();
            worker.Tick();
            worker.Tick();
            Assert.Single(http.Requests);

            http.Requests[0].Complete(true, 200,
                "{\"run_id\":\"r1\",\"rank\":1,\"total_runners\":1,\"is_pb\":true}");
            worker.Tick();
            Assert.Equal(2, http.Requests.Count);
        }

        [Fact]
        public void Success_FiresEvents_AndRecordsHealth()
        {
            var (worker, http, health) = Make();
            UploadResponse? got = null;
            string? name = null;
            worker.OnUploadSuccess += (p, r) => got = r;
            worker.OnDisplayNameReceived += n => name = n;

            worker.Enqueue(Payload());
            worker.Tick();
            http.Requests[0].Complete(true, 200,
                "{\"run_id\":\"r9\",\"rank\":2,\"total_runners\":10," +
                "\"is_pb\":true,\"display_name\":\"Hornet\"}");

            Assert.NotNull(got);
            Assert.Equal("r9", got!.RunId);
            Assert.Equal(2, got.Rank);
            Assert.Equal("Hornet", name);
            Assert.Equal(ConnectionHealth.State.Healthy, health.CurrentState);
        }

        [Fact]
        public void Failure_RequeuesWithBackoff_ThenRetriesWhenEligible()
        {
            var (worker, http, health) = Make();
            var payload = Payload();
            worker.Enqueue(payload);
            worker.Tick();

            long before = DateTime.UtcNow.Ticks;
            http.Requests[0].Complete(false, 500, "boom");

            Assert.Equal(1, payload.RetryCount);
            Assert.Equal(1, worker.QueueCount);
            // First retry waits 1<<1 = 2 s.
            Assert.InRange(payload.RetryAfterTicks,
                before + TimeSpan.FromSeconds(1.5).Ticks,
                before + TimeSpan.FromSeconds(3).Ticks);
            Assert.Equal(1, health.ConsecutiveFailures);

            // Not eligible yet — nothing sent.
            worker.Tick();
            Assert.Single(http.Requests);

            // Force eligibility; it resends.
            payload.RetryAfterTicks = 0;
            worker.Tick();
            Assert.Equal(2, http.Requests.Count);
        }

        [Fact]
        public void Failure_DropsAfterMaxRetries()
        {
            var (worker, http, _) = Make();
            var payload = Payload();
            worker.Enqueue(payload);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                payload.RetryAfterTicks = 0;
                worker.Tick();
                http.Requests[^1].Complete(false, 500, "boom");
            }

            // 6th failure exceeded MaxRetries=5 — payload dropped for good.
            Assert.Equal(0, worker.QueueCount);
            payload.RetryAfterTicks = 0;
            worker.Tick();
            Assert.Equal(6, http.Requests.Count);
        }

        [Fact]
        public void QueueCapsAt100_DroppingOldest()
        {
            var (worker, _, _) = Make();
            for (int i = 0; i < 105; i++)
                worker.Enqueue(Payload("scene" + i));
            Assert.Equal(100, worker.QueueCount);
        }
    }

    public class ReplaySharingTests
    {
        [Theory]
        [InlineData("aZ9", "aZ9")]
        [InlineData("  aZ9  ", "aZ9")]
        [InlineData("a", "a")]
        [InlineData("AbCdEfGh1234", "AbCdEfGh1234")] // 12 = max
        [InlineData("https://ex.com/r/aZ9", "aZ9")]
        [InlineData("https://ex.com/r/aZ9/", "aZ9")]
        [InlineData("http://ex.com/aZ9", "aZ9")]
        [InlineData("https://ex.com/share?code=aZ9", "aZ9")]
        [InlineData("https://ex.com/share?code=aZ9&x=1", "aZ9")]
        [InlineData("https://ex.com/r/aZ9#frag", "aZ9")]
        public void TryExtractCode_Accepts(string input, string expected)
        {
            Assert.True(ReplaySharing.TryExtractCode(input, out string code));
            Assert.Equal(expected, code);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("AbCdEfGh12345")]       // 13 chars — too long for a code
        [InlineData("has spaces")]
        [InlineData("code=aZ9")]            // not a URL — no scanning
        [InlineData("ftp://ex.com/r/aZ9")]
        [InlineData("https://ex.com/")]
        public void TryExtractCode_Rejects(string? input) =>
            Assert.False(ReplaySharing.TryExtractCode(input, out _));

        [Fact]
        public void ReplayBlobs_NeverMatchAsCodes()
        {
            string blob = ReplayShareEncoder.Encode(Rooms.Room(frames: 30));
            Assert.False(ReplaySharing.TryExtractCode(blob, out _));
        }

        [Fact]
        public void BuildShareText_IsBareCode() =>
            Assert.Equal("aZ9", ReplaySharing.BuildShareText("aZ9"));
    }

    public class TimeUtilTests
    {
        [Theory]
        [InlineData(0f, "0:00.00")]
        [InlineData(5.25f, "0:05.25")]
        [InlineData(65.43f, "1:05.43")]
        [InlineData(600f, "10:00.00")]
        public void Format(float t, string expected) =>
            Assert.Equal(expected, TimeUtil.Format(t));

        [Theory]
        [InlineData(0f, false, "+0.00")]
        [InlineData(0f, true, "+00.00")]
        [InlineData(3.5f, false, "+3.50")]
        [InlineData(3.5f, true, "+03.50")]
        [InlineData(-3.5f, false, "-3.50")]
        [InlineData(65.43f, false, "+1:05.43")]
        [InlineData(-65.43f, false, "-1:05.43")]
        public void FormatDelta(float t, bool pad, string expected) =>
            Assert.Equal(expected, TimeUtil.FormatDelta(t, pad));
    }
}
