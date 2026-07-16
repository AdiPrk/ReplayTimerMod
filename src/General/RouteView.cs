using System.Collections.Generic;

namespace ReplayTimerMod
{
    /// <summary>
    /// Pure logic for turning a route's cached best-per-(runner, mask)
    /// leaderboard rows into the collapsed, filtered, re-ranked list the UI
    /// displays. Extracted from ReplayUI so it can be unit tested without
    /// Unity (the UI wrapper in ReplayUI.Filters.cs supplies the persisted
    /// filter masks from GhostSettings).
    ///
    /// Must stay net35-compatible (no LINQ, no tuples) — it compiles into all
    /// three game targets.
    /// </summary>
    internal static class RouteView
    {
        /// <summary>A filter is active when it requires or excludes any bit.</summary>
        public static bool FilterActive(int require, int exclude) =>
            require != 0 || exclude != 0;

        /// <summary>Whether a run with this mask passes the filter. Unknown
        /// (pre-feature) masks pass only when no filter is active.</summary>
        public static bool PassesFilter(int mask, int require, int exclude)
        {
            if (!FilterActive(require, exclude)) return true;
            return ModifierMask.Passes(mask, require, exclude);
        }

        /// <summary>
        /// Advances one modifier's filter state through Any -> With ->
        /// Without -> Any (the filter chip click cycle). <paramref name="bit"/>
        /// is the bit MASK (1 &lt;&lt; def.Bit); the require/exclude masks are
        /// updated in place, other bits are untouched, and the bit never ends
        /// up in both masks.
        /// </summary>
        public static void CycleFilterBit(int bit, ref int require, ref int exclude)
        {
            if ((require & bit) != 0)
            {
                require &= ~bit;
                exclude |= bit;
            }
            else if ((exclude & bit) != 0)
            {
                exclude &= ~bit;
            }
            else
            {
                require |= bit;
            }
        }

        /// <summary>
        /// Builds the display view of a route's leaderboard from the cached
        /// best-per-(runner, mask) rows: filter by the modifier filter,
        /// collapse to each runner's best surviving row, sort by time, and
        /// assign contiguous view ranks on CLONED entries (the cached rows are
        /// never mutated). With no filter active this reproduces the classic
        /// best-per-runner board.
        /// </summary>
        public static List<LeaderboardEntry> Build(RouteLeaderboard route,
            int require, int exclude,
            out int yourViewRank, out LeaderboardEntry? yourRow)
        {
            // Collapse best row per runner. IsYou rows collapse under a
            // dedicated key (optimistic local entries have Rid = -1 and would
            // otherwise collide with server rows).
            var bestPerRunner = new Dictionary<string, LeaderboardEntry>();
            foreach (var e in route.Entries)
            {
                if (!PassesFilter(e.Modifiers, require, exclude)) continue;

                string key = e.IsYou ? "you" : "r" + e.Rid;
                if (!bestPerRunner.TryGetValue(key, out var cur)
                    || e.TotalTime < cur.TotalTime)
                    bestPerRunner[key] = e;
            }

            var view = new List<LeaderboardEntry>(bestPerRunner.Count);
            foreach (var e in bestPerRunner.Values)
            {
                view.Add(new LeaderboardEntry
                {
                    RunnerName = e.RunnerName,
                    TotalTime = e.TotalTime,
                    RunId = e.RunId,
                    IsYou = e.IsYou,
                    Modifiers = e.Modifiers,
                    Rid = e.Rid
                });
            }

            view.Sort((a, b) =>
            {
                int c = a.TotalTime.CompareTo(b.TotalTime);
                return c != 0 ? c : a.Rid.CompareTo(b.Rid);
            });

            yourViewRank = -1;
            yourRow = null;
            for (int i = 0; i < view.Count; i++)
            {
                view[i].Rank = i + 1;
                if (view[i].IsYou && yourRow == null)
                {
                    yourViewRank = i + 1;
                    yourRow = view[i];
                }
            }

            return view;
        }
    }
}
