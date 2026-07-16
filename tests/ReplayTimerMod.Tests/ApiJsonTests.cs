using System.Globalization;
using System.Linq;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class ApiJsonTests
    {
        // ── Upload serialization ────────────────────────────────────────────

        private static UploadPayload Payload() => new UploadPayload
        {
            Game = "silksong",
            SceneName = "Bone_East_10",
            EntryFrom = "Bone_East_09",
            ExitTo = "Bone_East_11",
            TotalTime = 12.34f,
            FrameCount = 370,
            ModifierMask = 0b101,
            CapturedAtUtcTicks = new System.DateTime(2026, 7, 6, 12, 30, 15,
                500, System.DateTimeKind.Utc).Ticks,
            ReplayData = "QUJDRA==",
            ModVersion = "1.0.0",
        };

        [Fact]
        public void SerializeUpload_ProducesExpectedJson()
        {
            string json = ApiJson.SerializeUpload(Payload());
            Assert.Equal(
                "{\"game\":\"silksong\",\"scene_name\":\"Bone_East_10\"," +
                "\"entry_from\":\"Bone_East_09\",\"exit_to\":\"Bone_East_11\"," +
                "\"total_time\":12.34,\"frame_count\":370,\"modifiers\":5," +
                "\"captured_at\":\"2026-07-06T12:30:15.500Z\"," +
                "\"replay_data\":\"QUJDRA==\"}",
                json);
        }

        [Fact]
        public void SerializeUpload_EscapesSceneNames()
        {
            var p = Payload();
            p.SceneName = "Weird\"Scene\\Name\n";
            string json = ApiJson.SerializeUpload(p);
            Assert.Contains("\"scene_name\":\"Weird\\\"Scene\\\\Name\\n\"", json);
        }

        [Fact]
        public void SerializeUpload_IsCultureInvariant()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                // de-DE writes decimals as "12,34" — the wire format must not.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string json = ApiJson.SerializeUpload(Payload());
                Assert.Contains("\"total_time\":12.34,", json);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // ── Upload / config / share responses ───────────────────────────────

        [Fact]
        public void ParseUploadResponse_FullPayload()
        {
            var r = ApiJson.ParseUploadResponse(
                "{\"run_id\":\"abc-123\",\"rank\":4,\"total_runners\":17," +
                "\"is_pb\":true,\"display_name\":\"Hornet\"}");
            Assert.Equal("abc-123", r.RunId);
            Assert.Equal(4, r.Rank);
            Assert.Equal(17, r.TotalRunners);
            Assert.True(r.IsPB);
            Assert.Equal("Hornet", r.DisplayName);
            Assert.True(r.HasRank);
        }

        [Fact]
        public void ParseUploadResponse_MissingFields_UseSentinels()
        {
            var r = ApiJson.ParseUploadResponse("{}");
            Assert.Equal(-1, r.Rank);
            Assert.Equal(-1, r.TotalRunners);
            Assert.False(r.HasRank);
            Assert.Equal("", r.RunId);
        }

        [Fact]
        public void ParseUploadResponse_NullRunId_IsTolerated()
        {
            var r = ApiJson.ParseUploadResponse("{\"run_id\":null,\"rank\":-1}");
            Assert.Equal("", r.RunId);
            Assert.False(r.HasRank);
        }

        [Fact]
        public void ParseConfigResponse_ReadsMaintenanceAndAnnouncement()
        {
            var r = ApiJson.ParseConfigResponse(
                "{\"maintenance\":true,\"announcement\":\"Down for a bit\"}");
            Assert.True(r.Maintenance);
            Assert.Equal("Down for a bit", r.Announcement);

            var none = ApiJson.ParseConfigResponse("{\"maintenance\":false}");
            Assert.False(none.Maintenance);
            Assert.Null(none.Announcement);
        }

        [Fact]
        public void ParseShareResponse_ReadsCodeAndOptionalUrl()
        {
            var r = ApiJson.ParseShareResponse(
                "{\"code\":\"aZ9\",\"url\":\"https://ex.com/r/aZ9\"}");
            Assert.Equal("aZ9", r.Code);
            Assert.Equal("https://ex.com/r/aZ9", r.Url);
            Assert.True(r.HasCode);

            var bare = ApiJson.ParseShareResponse("{\"code\":\"b\"}");
            Assert.Equal("b", bare.Code);
            Assert.Null(bare.Url);
        }

        [Fact]
        public void SerializeShare_BothModes()
        {
            Assert.Equal("{\"run_id\":\"r-1\"}", ApiJson.SerializeShareByRunId("r-1"));

            string byData = ApiJson.SerializeShareByData("hk_1578", "Crossroads_01",
                "Tut_01", "Crossroads_02", 9.87f, 296, "QUJD");
            Assert.Equal(
                "{\"game\":\"hk_1578\",\"scene_name\":\"Crossroads_01\"," +
                "\"entry_from\":\"Tut_01\",\"exit_to\":\"Crossroads_02\"," +
                "\"total_time\":9.87,\"frame_count\":296,\"replay_data\":\"QUJD\"}",
                byData);
        }

        // ── /init and /scenes ───────────────────────────────────────────────

        [Fact]
        public void ParseInitResponse_ConfigAndScenes()
        {
            var r = ApiJson.ParseInitResponse(
                "{\"config\":{\"maintenance\":false,\"announcement\":\"hi \\\"all\\\"\"}," +
                "\"scenes\":{\"v\":42,\"scenes\":[" +
                "{\"s\":\"Bone_East_10\",\"r\":3,\"n\":12}," +
                "{\"s\":\"Dust_05\",\"r\":1,\"n\":4}]}}");

            Assert.False(r.Config.Maintenance);
            Assert.Equal("hi \"all\"", r.Config.Announcement);
            Assert.Equal(42, r.SceneIndexVersion);
            Assert.Equal(2, r.Scenes.Count);
            Assert.Equal("Bone_East_10", r.Scenes[0].SceneName);
            Assert.Equal(3, r.Scenes[0].RouteCount);
            Assert.Equal(12, r.Scenes[0].RunnerCount);
        }

        [Fact]
        public void ParseSceneIndexResponse_NoChange()
        {
            var r = ApiJson.ParseSceneIndexResponse("{\"v\":7}");
            Assert.Equal(7, r.Version);
            Assert.False(r.Changed);
            Assert.Empty(r.Scenes);
        }

        [Fact]
        public void ParseSceneIndexResponse_FullUpdate()
        {
            var r = ApiJson.ParseSceneIndexResponse(
                "{\"v\":8,\"scenes\":[{\"s\":\"A\",\"r\":1,\"n\":2}]}");
            Assert.Equal(8, r.Version);
            Assert.True(r.Changed);
            Assert.Single(r.Scenes);
        }

        [Fact]
        public void ParseSceneIndexResponse_SkipsNamelessEntries()
        {
            var r = ApiJson.ParseSceneIndexResponse(
                "{\"v\":1,\"scenes\":[{\"r\":1,\"n\":2},{\"s\":\"B\",\"r\":0,\"n\":0}]}");
            Assert.Single(r.Scenes);
            Assert.Equal("B", r.Scenes[0].SceneName);
        }

        // ── /leaderboard ────────────────────────────────────────────────────

        private const string LeaderboardJson =
            "{\"v\":99,\"routes\":[" +
            "{\"entry_from\":\"Bone_East_09\",\"exit_to\":\"Bone_East_11\"," +
            "\"total_runners\":3,\"your_rank\":2," +
            "\"entries\":[" +
            "{\"rank\":1,\"runner_name\":\"Lace\",\"total_time\":4.5," +
            "\"run_id\":\"r1\",\"is_you\":false,\"modifiers\":0,\"rid\":11}," +
            "{\"rank\":2,\"runner_name\":\"You\",\"total_time\":5.25," +
            "\"run_id\":\"r2\",\"is_you\":true,\"modifiers\":5,\"rid\":22}]," +
            "\"your_entry\":{\"rank\":2,\"runner_name\":\"You\"," +
            "\"total_time\":5.25,\"run_id\":\"r2\",\"is_you\":true," +
            "\"modifiers\":5,\"rid\":22}}," +
            "{\"entry_from\":\"X\",\"exit_to\":\"Y\",\"total_runners\":1," +
            "\"your_rank\":null,\"entries\":[],\"your_entry\":null}]}";

        [Fact]
        public void ParseVersionedLeaderboard_FullUpdate()
        {
            var r = ApiJson.ParseVersionedLeaderboardResponse(LeaderboardJson);
            Assert.True(r.Changed);
            Assert.Equal(99, r.Version);
            Assert.Equal(2, r.Data.Routes.Count);

            var route = r.Data.Routes[0];
            Assert.Equal("Bone_East_09", route.EntryFrom);
            Assert.Equal(3, route.TotalRunners);
            Assert.Equal(2, route.YourRank);
            Assert.Equal(2, route.Entries.Count);
            Assert.Equal("Lace", route.Entries[0].RunnerName);
            Assert.Equal(4.5f, route.Entries[0].TotalTime);
            Assert.Equal(11, route.Entries[0].Rid);
            Assert.True(route.Entries[1].IsYou);
            Assert.Equal(5, route.Entries[1].Modifiers);
            Assert.NotNull(route.YourEntry);
            Assert.Equal(5, route.YourEntry!.Modifiers);

            var second = r.Data.Routes[1];
            Assert.Equal(-1, second.YourRank);   // null → -1
            Assert.Empty(second.Entries);
            Assert.Null(second.YourEntry);
        }

        [Fact]
        public void ParseVersionedLeaderboard_NoChange()
        {
            var r = ApiJson.ParseVersionedLeaderboardResponse("{\"v\":99}");
            Assert.False(r.Changed);
            Assert.Equal(99, r.Version);
            Assert.Empty(r.Data.Routes);
        }

        [Fact]
        public void ParseVersionedLeaderboard_UnknownFieldsAreSkipped()
        {
            var r = ApiJson.ParseVersionedLeaderboardResponse(
                "{\"v\":1,\"future_field\":{\"nested\":[1,2,{\"x\":\"}]\"}]}," +
                "\"routes\":[{\"entry_from\":\"A\",\"exit_to\":\"B\"," +
                "\"total_runners\":1,\"your_rank\":1,\"surprise\":true," +
                "\"entries\":[{\"rank\":1,\"runner_name\":\"N\"," +
                "\"total_time\":1.5,\"run_id\":\"r\",\"is_you\":false," +
                "\"modifiers\":0,\"rid\":1,\"extra\":\"ignored\"}]," +
                "\"your_entry\":null}]}");
            Assert.True(r.Changed);
            Assert.Single(r.Data.Routes);
            Assert.Single(r.Data.Routes[0].Entries);
        }

        [Fact]
        public void ParseVersionedLeaderboard_ScientificFloats()
        {
            var r = ApiJson.ParseVersionedLeaderboardResponse(
                "{\"v\":1,\"routes\":[{\"entry_from\":\"A\",\"exit_to\":\"B\"," +
                "\"total_runners\":1,\"your_rank\":1,\"entries\":[" +
                "{\"rank\":1,\"runner_name\":\"N\",\"total_time\":1.25E+1," +
                "\"run_id\":\"r\",\"is_you\":false,\"modifiers\":0,\"rid\":1}]," +
                "\"your_entry\":null}]}");
            Assert.Equal(12.5f, r.Data.Routes[0].Entries[0].TotalTime);
        }

        [Fact]
        public void ParseVersionedLeaderboard_ParsingIsCultureInvariant()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var r = ApiJson.ParseVersionedLeaderboardResponse(LeaderboardJson);
                Assert.Equal(4.5f, r.Data.Routes[0].Entries[0].TotalTime);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{\"v\":")]
        [InlineData("{\"routes\":\"nope\"}")]
        public void Parsers_ToleratateMalformedInput(string json)
        {
            // Must not throw — resilience over strictness on the client.
            ApiJson.ParseUploadResponse(json);
            ApiJson.ParseConfigResponse(json);
            ApiJson.ParseShareResponse(json);
            ApiJson.ParseInitResponse(json);
            ApiJson.ParseSceneIndexResponse(json);
            ApiJson.ParseVersionedLeaderboardResponse(json);
        }
    }

    public class JsonTextTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("with \"quotes\"", "with \\\"quotes\\\"")]
        [InlineData("back\\slash", "back\\\\slash")]
        [InlineData("line\nbreak\r\ttab", "line\\nbreak\\r\\ttab")]
        [InlineData("", "")]
        public void Escape_HandlesSpecials(string input, string expected) =>
            Assert.Equal(expected, JsonText.Escape(input));

        [Fact]
        public void Escape_ControlCharsBelow0x20_UseUnicodeEscape() =>
            Assert.Equal("\\" + "u0001ctl", JsonText.Escape(((char)1) + "ctl"));

        [Fact]
        public void AppendQuoted_NullBecomesLiteralNull()
        {
            var sb = new System.Text.StringBuilder();
            JsonText.AppendQuoted(sb, null);
            Assert.Equal("null", sb.ToString());
        }

        [Fact]
        public void AppendQuoted_WrapsAndEscapes()
        {
            var sb = new System.Text.StringBuilder();
            JsonText.AppendQuoted(sb, "a\"b");
            Assert.Equal("\"a\\\"b\"", sb.ToString());
        }
    }
}
