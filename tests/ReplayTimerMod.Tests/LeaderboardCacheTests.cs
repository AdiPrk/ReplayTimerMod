using System.Collections.Generic;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class LeaderboardCacheTests
    {
        private static SceneInfo Scene(string name, int routes = 1, int runners = 1) =>
            new SceneInfo { SceneName = name, RouteCount = routes, RunnerCount = runners };

        private static LeaderboardData Board(params (string runner, int rid,
            float time, int mask, bool isYou)[] entries)
        {
            var route = new RouteLeaderboard { EntryFrom = "A", ExitTo = "B" };
            int rank = 1;
            foreach (var e in entries)
                route.Entries.Add(new LeaderboardEntry
                {
                    Rank = rank++,
                    RunnerName = e.runner,
                    TotalTime = e.time,
                    Modifiers = e.mask,
                    IsYou = e.isYou,
                    Rid = e.rid,
                });
            return new LeaderboardData { Routes = new List<RouteLeaderboard> { route } };
        }

        // ── Scene index ─────────────────────────────────────────────────────

        [Fact]
        public void UpdateSceneIndex_TracksVersionAndSetChanges()
        {
            var cache = new LeaderboardCache();
            Assert.False(cache.SceneIndexLoaded);

            bool changed = cache.UpdateSceneIndex(5,
                new List<SceneInfo> { Scene("X"), Scene("Y") });
            Assert.True(changed);
            Assert.True(cache.SceneIndexLoaded);
            Assert.Equal(5, cache.SceneIndexVersion);
            int v1 = cache.ServerScenesVersion;

            // Same set, new server version → set unchanged, local version stays.
            changed = cache.UpdateSceneIndex(6,
                new List<SceneInfo> { Scene("X", 2, 9), Scene("Y") });
            Assert.False(changed);
            Assert.Equal(6, cache.SceneIndexVersion);
            Assert.Equal(v1, cache.ServerScenesVersion);
            // ...but per-scene info updates.
            Assert.Equal(2, cache.GetSceneInfo("X")!.RouteCount);

            // New scene → set changed, local version bumps.
            changed = cache.UpdateSceneIndex(7,
                new List<SceneInfo> { Scene("X"), Scene("Y"), Scene("Z") });
            Assert.True(changed);
            Assert.Equal(v1 + 1, cache.ServerScenesVersion);
            Assert.Contains("Z", cache.GetServerScenes());
        }

        [Fact]
        public void UpdateSceneIndex_DetectsReplacementAtSameCount()
        {
            var cache = new LeaderboardCache();
            cache.UpdateSceneIndex(1, new List<SceneInfo> { Scene("X") });
            int v = cache.ServerScenesVersion;

            bool changed = cache.UpdateSceneIndex(2, new List<SceneInfo> { Scene("Q") });
            Assert.True(changed);
            Assert.Equal(v + 1, cache.ServerScenesVersion);
            Assert.DoesNotContain("X", cache.GetServerScenes());
        }

        // ── Per-room versioning / signatures ────────────────────────────────

        [Fact]
        public void UpdateRoom_BumpsVersionOnlyWhenContentChanges()
        {
            var cache = new LeaderboardCache();
            Assert.Equal(0, cache.GetVersion("silksong", "S"));

            Assert.True(cache.UpdateRoom("silksong", "S", 10,
                Board(("Lace", 1, 4.5f, 0, false))));
            Assert.Equal(1, cache.GetVersion("silksong", "S"));
            Assert.Equal(10, cache.GetRoomServerVersion("silksong", "S"));

            // Identical content from a later poll: server version updates,
            // local content version must NOT bump (UI skips rebuild).
            Assert.False(cache.UpdateRoom("silksong", "S", 11,
                Board(("Lace", 1, 4.5f, 0, false))));
            Assert.Equal(1, cache.GetVersion("silksong", "S"));
            Assert.Equal(11, cache.GetRoomServerVersion("silksong", "S"));

            // Actually different content bumps.
            Assert.True(cache.UpdateRoom("silksong", "S", 12,
                Board(("Lace", 1, 4.4f, 0, false))));
            Assert.Equal(2, cache.GetVersion("silksong", "S"));
        }

        [Fact]
        public void Rooms_AreKeyedPerGame()
        {
            var cache = new LeaderboardCache();
            cache.UpdateRoom("silksong", "S", 1, Board(("A", 1, 1f, 0, false)));
            cache.UpdateRoom("hk_1578", "S", 1, Board(("B", 1, 2f, 0, false)));

            Assert.Equal("A", cache.Get("silksong", "S")!.Routes[0].Entries[0].RunnerName);
            Assert.Equal("B", cache.Get("hk_1578", "S")!.Routes[0].Entries[0].RunnerName);
        }

        [Fact]
        public void Freshness_FollowsClock()
        {
            var cache = new LeaderboardCache();
            UnityEngine.Time.realtimeSinceStartup = 100f;
            cache.UpdateRoom("silksong", "S", 1, Board(("A", 1, 1f, 0, false)));

            UnityEngine.Time.realtimeSinceStartup = 120f;
            Assert.True(cache.IsRoomFresh("silksong", "S", maxAgeSec: 30f));
            Assert.False(cache.IsRoomFresh("silksong", "S", maxAgeSec: 10f));
            Assert.False(cache.IsRoomFresh("silksong", "Other", maxAgeSec: 999f));
            Assert.True(cache.HasRoomData("silksong", "S"));
        }

        [Fact]
        public void InvalidateAllRoomVersions_ForcesRefetchButKeepsData()
        {
            var cache = new LeaderboardCache();
            UnityEngine.Time.realtimeSinceStartup = 50f;
            cache.UpdateRoom("silksong", "S", 33, Board(("A", 1, 1f, 0, false)));
            cache.UpdateSceneIndex(9, new List<SceneInfo> { Scene("S") });

            cache.InvalidateAllRoomVersions();

            Assert.Equal(0, cache.GetRoomServerVersion("silksong", "S"));
            Assert.Equal(0, cache.SceneIndexVersion);
            Assert.False(cache.IsRoomFresh("silksong", "S", 999f));
            Assert.NotNull(cache.Get("silksong", "S")); // data itself survives
        }

        [Fact]
        public void Clear_ResetsEverything()
        {
            var cache = new LeaderboardCache();
            cache.UpdateRoom("silksong", "S", 1, Board(("A", 1, 1f, 0, false)));
            cache.UpdateSceneIndex(1, new List<SceneInfo> { Scene("S") });

            cache.Clear();
            Assert.Null(cache.Get("silksong", "S"));
            Assert.False(cache.SceneIndexLoaded);
            Assert.Empty(cache.GetServerScenes());
        }

        // ── Optimistic upload ───────────────────────────────────────────────

        [Fact]
        public void OptimisticUpload_InsertsSorted_AndReplacesOnlySameMaskRow()
        {
            var cache = new LeaderboardCache();
            cache.UpdateRoom("silksong", "S", 1, Board(
                ("Lace", 1, 4.0f, 0, false),
                ("You", 2, 5.0f, 0, true),      // your mask-0 row
                ("You", 2, 6.0f, 5, true)));    // your mask-5 row
            int version = cache.GetVersion("silksong", "S");

            // Upload a faster mask-5 run: replaces ONLY the mask-5 row.
            Assert.True(cache.ApplyOptimisticUpload("silksong", "S", "A", "B",
                totalTime: 4.5f, rank: 2, totalRunners: 3,
                displayName: "Hornet", modifierMask: 5));

            var route = cache.Get("silksong", "S")!.Routes[0];
            Assert.Equal(3, route.Entries.Count);
            Assert.Equal(new[] { 4.0f, 4.5f, 5.0f },
                new[] { route.Entries[0].TotalTime, route.Entries[1].TotalTime,
                        route.Entries[2].TotalTime });

            var mine = route.Entries[1];
            Assert.True(mine.IsYou);
            Assert.Equal(5, mine.Modifiers);
            Assert.Equal(-1, mine.Rid);            // optimistic sentinel
            Assert.Equal("Hornet", mine.RunnerName);

            // The mask-0 row survived.
            Assert.Contains(route.Entries,
                e => e.IsYou && e.Modifiers == 0 && e.TotalTime == 5.0f);

            // Raw ranks re-numbered 1..n; version bumped for UI rebuild.
            Assert.Equal(new[] { 1, 2, 3 },
                new[] { route.Entries[0].Rank, route.Entries[1].Rank,
                        route.Entries[2].Rank });
            Assert.True(cache.GetVersion("silksong", "S") > version);
            Assert.Equal(2, route.YourRank);
            Assert.Equal(3, route.TotalRunners);
        }

        [Fact]
        public void OptimisticUpload_UncachedRoom_CreatesStubAndScene()
        {
            var cache = new LeaderboardCache();
            int scenesV = cache.ServerScenesVersion;

            Assert.True(cache.ApplyOptimisticUpload("silksong", "NewScene",
                "A", "B", 3f, rank: 1, totalRunners: 1,
                displayName: "", modifierMask: 0));

            var route = cache.Get("silksong", "NewScene")!.Routes[0];
            var entry = Assert.Single(route.Entries);
            Assert.Equal("You", entry.RunnerName);  // fallback display name
            Assert.Contains("NewScene", cache.GetServerScenes());
            Assert.Equal(scenesV + 1, cache.ServerScenesVersion);
        }
    }
}
