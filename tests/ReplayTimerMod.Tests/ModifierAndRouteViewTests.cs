using System.Collections.Generic;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    // NOTE: no game constant is defined in the test build, so
    // ModifierRegistry.All is empty. Registry-dependent rendering (badge
    // names, crest collapsing) can only be exercised in-game; these tests
    // cover the pure mask semantics every platform shares.
    public class ModifierMaskTests
    {
        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(int.MaxValue, true)]
        public void IsKnown(int mask, bool expected) =>
            Assert.Equal(expected, ModifierMask.IsKnown(mask));

        [Theory]
        // no filter bits → any known mask passes
        [InlineData(0, 0, 0, true)]
        [InlineData(0b101, 0, 0, true)]
        // unknown masks never pass Passes()
        [InlineData(-1, 0, 0, false)]
        [InlineData(-1, 1, 0, false)]
        // require: all bits must be present
        [InlineData(0b101, 0b001, 0, true)]
        [InlineData(0b101, 0b111, 0, false)]
        [InlineData(0b101, 0b010, 0, false)]
        // exclude: no bit may be present
        [InlineData(0b101, 0, 0b010, true)]
        [InlineData(0b101, 0, 0b100, false)]
        // combined
        [InlineData(0b101, 0b001, 0b010, true)]
        [InlineData(0b111, 0b001, 0b010, false)]
        public void Passes(int mask, int require, int exclude, bool expected) =>
            Assert.Equal(expected, ModifierMask.Passes(mask, require, exclude));

        [Fact]
        public void Badges_HandleSentinels()
        {
            Assert.Equal("?", ModifierMask.ToBadge(ModifierMask.Unknown));
            Assert.Equal("", ModifierMask.ToBadge(0));
            Assert.Contains("unknown", ModifierMask.ToTooltip(ModifierMask.Unknown));
            Assert.Equal("No modifiers", ModifierMask.ToTooltip(0));
        }
    }

    public class RouteViewTests
    {
        private static LeaderboardEntry Entry(string runner, int rid, float time,
            int mask, bool isYou = false, string runId = "") =>
            new LeaderboardEntry
            {
                Rank = 999, // raw server rank — must be ignored/replaced
                RunnerName = runner,
                TotalTime = time,
                RunId = runId,
                IsYou = isYou,
                Modifiers = mask,
                Rid = rid,
            };

        /// <summary>Best-per-(runner, mask) rows the server would return.</summary>
        private static RouteLeaderboard SampleRoute() => new RouteLeaderboard
        {
            EntryFrom = "A",
            ExitTo = "B",
            TotalRunners = 3,
            Entries = new List<LeaderboardEntry>
            {
                Entry("Lace", rid: 1, time: 5.0f, mask: 0b000),
                Entry("Lace", rid: 1, time: 4.5f, mask: 0b101),
                Entry("Sherma", rid: 2, time: 4.8f, mask: 0b000),
                // Your rows: one optimistic local (rid -1), one from the server.
                Entry("You", rid: -1, time: 4.9f, mask: 0b101, isYou: true),
                Entry("You", rid: 3, time: 5.2f, mask: 0b000, isYou: true),
            },
        };

        [Fact]
        public void NoFilter_CollapsesToBestPerRunner_AndReRanks()
        {
            var view = RouteView.Build(SampleRoute(), 0, 0,
                out int yourRank, out var yourRow);

            Assert.Equal(3, view.Count);
            Assert.Equal(new[] { "Lace", "Sherma", "You" },
                new[] { view[0].RunnerName, view[1].RunnerName, view[2].RunnerName });
            Assert.Equal(4.5f, view[0].TotalTime);
            Assert.Equal(1, view[0].Rank);
            Assert.Equal(2, view[1].Rank);
            Assert.Equal(3, view[2].Rank);
            Assert.Equal(3, yourRank);
            Assert.NotNull(yourRow);
            Assert.Equal(4.9f, yourRow!.TotalTime); // both IsYou rows collapse
        }

        [Fact]
        public void RequireFilter_DropsNonMatchingRows_BeforeCollapsing()
        {
            // Require bit 0: only the 0b101 rows survive.
            var view = RouteView.Build(SampleRoute(), 0b001, 0,
                out int yourRank, out var yourRow);

            Assert.Equal(2, view.Count);
            Assert.Equal("Lace", view[0].RunnerName);
            Assert.Equal(4.5f, view[0].TotalTime);
            Assert.Equal(2, yourRank);
            Assert.Equal(4.9f, yourRow!.TotalTime);
        }

        [Fact]
        public void ExcludeFilter_FallsBackToSlowerRows()
        {
            // Exclude bit 2: the fast 0b101 rows drop, mask-0 rows remain.
            var view = RouteView.Build(SampleRoute(), 0, 0b100,
                out int yourRank, out var yourRow);

            Assert.Equal(3, view.Count);
            Assert.Equal("Sherma", view[0].RunnerName); // 4.8 now leads
            Assert.Equal("Lace", view[1].RunnerName);   // falls back to 5.0
            Assert.Equal(3, yourRank);
            Assert.Equal(5.2f, yourRow!.TotalTime);     // server-mask-0 row
        }

        [Fact]
        public void UnknownMaskRows_PassWithoutFilter_DropWithAnyFilter()
        {
            var route = new RouteLeaderboard
            {
                Entries = new List<LeaderboardEntry>
                {
                    Entry("Old", rid: 9, time: 1f, mask: ModifierMask.Unknown),
                },
            };

            Assert.Single(RouteView.Build(route, 0, 0, out _, out _));
            Assert.Empty(RouteView.Build(route, 1, 0, out _, out _));
            Assert.Empty(RouteView.Build(route, 0, 1, out _, out _));
        }

        [Fact]
        public void TiedTimes_BreakByRid_Deterministically()
        {
            var route = new RouteLeaderboard
            {
                Entries = new List<LeaderboardEntry>
                {
                    Entry("B", rid: 7, time: 2f, mask: 0),
                    Entry("A", rid: 3, time: 2f, mask: 0),
                },
            };

            var view = RouteView.Build(route, 0, 0, out _, out _);
            Assert.Equal(3, view[0].Rid);
            Assert.Equal(7, view[1].Rid);
        }

        [Fact]
        public void CachedRows_AreNeverMutated()
        {
            var route = SampleRoute();
            RouteView.Build(route, 0, 0, out _, out _);

            // Raw ranks on the cached rows stay untouched (view uses clones).
            foreach (var e in route.Entries)
                Assert.Equal(999, e.Rank);
        }

        [Fact]
        public void NoEntries_YieldsEmptyView_AndNoYourRank()
        {
            var view = RouteView.Build(new RouteLeaderboard(), 0, 0,
                out int yourRank, out var yourRow);
            Assert.Empty(view);
            Assert.Equal(-1, yourRank);
            Assert.Null(yourRow);
        }

        [Fact]
        public void FilterActive_MirrorsMaskState()
        {
            Assert.False(RouteView.FilterActive(0, 0));
            Assert.True(RouteView.FilterActive(1, 0));
            Assert.True(RouteView.FilterActive(0, 1));

            // PassesFilter: inactive filter admits unknown masks too.
            Assert.True(RouteView.PassesFilter(ModifierMask.Unknown, 0, 0));
            Assert.False(RouteView.PassesFilter(ModifierMask.Unknown, 1, 0));
        }

        // ── CycleFilterBit (the filter chip click cycle) ───────────────────

        [Fact]
        public void CycleFilterBit_AdvancesAnyWithWithoutAny()
        {
            int require = 0, exclude = 0;
            const int bit = 0b100;

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(bit, require); // Any -> With
            Assert.Equal(0, exclude);

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(0, require);   // With -> Without
            Assert.Equal(bit, exclude);

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(0, require);   // Without -> Any
            Assert.Equal(0, exclude);
        }

        [Fact]
        public void CycleFilterBit_LeavesOtherBitsUntouched()
        {
            int require = 0b0011, exclude = 0b1000;
            const int bit = 0b0100;

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(0b0111, require);
            Assert.Equal(0b1000, exclude);

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(0b0011, require);
            Assert.Equal(0b1100, exclude);

            RouteView.CycleFilterBit(bit, ref require, ref exclude);
            Assert.Equal(0b0011, require);
            Assert.Equal(0b1000, exclude);
        }

        [Fact]
        public void CycleFilterBit_NeverPutsABitInBothMasks()
        {
            int require = 0, exclude = 0;
            const int bit = 0b10;
            for (int i = 0; i < 6; i++)
            {
                RouteView.CycleFilterBit(bit, ref require, ref exclude);
                Assert.Equal(0, require & exclude);
            }
        }

        // ── CountCollapsed (the filter footer's unfiltered total) ──────────

        [Fact]
        public void CountCollapsed_MatchesUnfilteredBuildCount()
        {
            var route = SampleRoute();
            Assert.Equal(RouteView.Build(route, 0, 0, out _, out _).Count,
                RouteView.CountCollapsed(route));
        }

        [Fact]
        public void CountCollapsed_EmptyRouteIsZero()
        {
            Assert.Equal(0, RouteView.CountCollapsed(new RouteLeaderboard()));
        }

        // ── Crest single-select rules ──────────────────────────────────────

        [Fact]
        public void SelectCrestBit_ReplacesOtherCrest_TogglesOff_ClearsOnAny()
        {
            const int crests = 0b1110; // three crest bits
            int require = 0b0001 | 0b0010; // one ability bit + one crest bit

            // Selecting a different crest replaces the current one.
            int r = RouteView.SelectCrestBit(require, crests, 0b0100);
            Assert.Equal(0b0001 | 0b0100, r);

            // Re-selecting the current crest clears it.
            r = RouteView.SelectCrestBit(r, crests, 0b0100);
            Assert.Equal(0b0001, r);

            // "Any crest" (bitMask 0) clears whatever crest is selected.
            r = RouteView.SelectCrestBit(0b0001 | 0b1000, crests, 0);
            Assert.Equal(0b0001, r);
        }

        [Fact]
        public void SanitizeCrestBits_DropsExcludes_AndMultiCrestRequire()
        {
            const int crests = 0b1110;

            // Crest bits never live in the exclude mask.
            int require = 0b0010, exclude = 0b0100 | 0b0001;
            RouteView.SanitizeCrestBits(crests, ref require, ref exclude);
            Assert.Equal(0b0010, require);
            Assert.Equal(0b0001, exclude);

            // Multiple required crest bits (stale state) clear together;
            // ability bits survive.
            require = 0b0110 | 0b0001;
            exclude = 0;
            RouteView.SanitizeCrestBits(crests, ref require, ref exclude);
            Assert.Equal(0b0001, require);

            // A single required crest is valid and untouched.
            require = 0b0100 | 0b0001;
            RouteView.SanitizeCrestBits(crests, ref require, ref exclude);
            Assert.Equal(0b0100 | 0b0001, require);
        }
    }
}
