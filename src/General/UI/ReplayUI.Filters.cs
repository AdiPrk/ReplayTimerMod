using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Modifier filter (shared by the Runs and Leaderboard tabs) ─────
        //
        // The filter panel uses the same label + button-row idiom as the
        // Config tab: every ability gets an [Any | With | Without] segmented
        // row, and the mutually-exclusive crest bits collapse into a single
        // "Crest" selector row. State persists in GhostSettings
        // (ModifierRequireMask / ModifierExcludeMask); the panel's
        // expanded/collapsed state is session-only.

        private bool _filterPanelExpanded;

        private static int FilterRequire => GhostSettings.ModifierRequireMask;
        private static int FilterExclude => GhostSettings.ModifierExcludeMask;

        private static bool ModifierFilterActive =>
            FilterRequire != 0 || FilterExclude != 0;

        /// <summary>Whether a run with this mask passes the current filter.
        /// Unknown (pre-feature) masks pass only when no filter is active.</summary>
        private static bool PassesModifierFilter(int mask)
        {
            if (!ModifierFilterActive) return true;
            return ModifierMask.Passes(mask, FilterRequire, FilterExclude);
        }

        // ── Filter bar widget ──────────────────────────────────────────────

        /// <summary>
        /// Adds the modifier filter bar as the first child of a tab's content
        /// area. Collapsed: one summary row. Expanded: an [Any|With|Without]
        /// row per ability plus a crest selector row.
        /// </summary>
        private void AddModifierFilterBar(Transform parent)
        {
            SanitizeCrestFilterBits();

            var abilities = NonCrestDefs();
            bool hasCrests = ModifierRegistry.CrestBitsMask != 0;

            int headerH = RH + 2;
            int rowCount = _filterPanelExpanded
                ? abilities.Count + (hasCrests ? 1 : 0)
                : 0;
            int totalH = headerH + rowCount * RH;

            var bar = MakeGO("ModifierFilterBar", parent);
            Img(bar, Color.clear);
            var le = bar.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = totalH;

            AddFilterHeader(bar.transform, headerH);

            if (!_filterPanelExpanded) return;

            int y = headerH;
            bool stripe = false;
            foreach (var def in abilities)
            {
                AddAbilityFilterRow(bar.transform, def, y, stripe);
                y += RH;
                stripe = !stripe;
            }

            if (hasCrests)
                AddCrestFilterRow(bar.transform, y, stripe);
        }

        private void AddFilterHeader(Transform parent, int headerH)
        {
            var header = MakeGO("FilterHeader", parent);
            Img(header, UIStyle.Overlay with { a = 0.3f });
            Rect(header, 0, 0, RW, headerH);

            int btnH = UIStyle.H(20);
            int btnY = (headerH - btnH) / 2;
            int toggleW = UIStyle.W(74);
            bool active = ModifierFilterActive;

            MakeButton(header.transform, "FilterToggle",
                (_filterPanelExpanded ? "▾" : "▸") + " Filters",
                UIStyle.FontSizeSm - 2,
                active ? UIStyle.Accent : UIStyle.Subtext,
                active ? UIStyle.Accent with { a = 0.15f } : Color.clear,
                M / 2, btnY, toggleW, btnH,
                () =>
                {
                    _filterPanelExpanded = !_filterPanelExpanded;
                    RebuildActiveTabContentOnly();
                });

            int resetW = UIStyle.W(46);
            if (active)
            {
                MakeButton(header.transform, "FilterReset", "Reset",
                    UIStyle.FontSizeSm - 2, UIStyle.Red, UIStyle.Red with { a = 0.15f },
                    RW - resetW - M, btnY, resetW, btnH,
                    () =>
                    {
                        GhostSettings.ModifierRequireMask = 0;
                        GhostSettings.ModifierExcludeMask = 0;
                        RebuildActiveTabContentOnly();
                    });
            }

            int summaryX = M / 2 + toggleW + M;
            int summaryEnd = active ? RW - resetW - M * 2 : RW - M;
            MakeLbl(header.transform, BuildFilterSummary(),
                UIStyle.FontSizeSm - 2,
                active ? UIStyle.Text : UIStyle.Subtext,
                TextAnchor.MiddleLeft,
                x: summaryX, w: summaryEnd - summaryX, h: headerH);
        }

        /// <summary>One ability row: name on the left, [Any|With|Without]
        /// segmented buttons on the right (Config-tab idiom).</summary>
        private void AddAbilityFilterRow(Transform parent, ModifierDef def,
            int y, bool stripe)
        {
            var row = MakeGO("Filter_" + def.Id, parent);
            Img(row, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            Rect(row, 0, y, RW, RH);

            int bit = 1 << def.Bit;
            bool with = (FilterRequire & bit) != 0;
            bool without = (FilterExclude & bit) != 0;

            int segW = UIStyle.W(52);
            int segH = UIStyle.H(18);
            int segY = y + (RH - segH) / 2;
            int sp = UIStyle.W(2);
            int segX = RW - M - segW * 3 - sp * 2;

            MakeLbl(row.transform, def.DisplayName,
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: segX - M * 2, h: RH);

            AddFilterSegment(parent, "Any", !with && !without,
                segX, segY, segW, segH,
                () => SetAbilityFilter(bit, requireIt: false, excludeIt: false));
            AddFilterSegment(parent, "With", with,
                segX + segW + sp, segY, segW, segH,
                () => SetAbilityFilter(bit, requireIt: true, excludeIt: false));
            AddFilterSegment(parent, "Without", without,
                segX + (segW + sp) * 2, segY, segW, segH,
                () => SetAbilityFilter(bit, requireIt: false, excludeIt: true));
        }

        private void AddFilterSegment(Transform parent, string label,
            bool selected, int x, int y, int w, int h,
            UnityEngine.Events.UnityAction onClick)
        {
            MakeButton(parent, "Seg" + label, label,
                UIStyle.FontSizeSm - 2,
                selected ? UIStyle.Accent : UIStyle.Subtext,
                selected ? UIStyle.Accent with { a = 0.2f }
                         : UIStyle.Surface with { a = 0.5f },
                x, y, w, h, onClick);
        }

        private void SetAbilityFilter(int bit, bool requireIt, bool excludeIt)
        {
            GhostSettings.ModifierRequireMask =
                requireIt ? FilterRequire | bit : FilterRequire & ~bit;
            GhostSettings.ModifierExcludeMask =
                excludeIt ? FilterExclude | bit : FilterExclude & ~bit;
            RebuildActiveTabContentOnly();
        }

        /// <summary>The crest selector row: crests are mutually exclusive, so
        /// the filter is a single choice - Any, or one specific crest - cycled
        /// with prev/next arrows around the current value.</summary>
        private void AddCrestFilterRow(Transform parent, int y, bool stripe)
        {
            var row = MakeGO("Filter_Crest", parent);
            Img(row, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            Rect(row, 0, y, RW, RH);

            var crests = CrestDefs();
            int selectedIdx = -1; // -1 = Any
            for (int i = 0; i < crests.Count; i++)
                if ((FilterRequire & (1 << crests[i].Bit)) != 0) { selectedIdx = i; break; }

            int arrowW = UIStyle.W(22);
            int valueW = UIStyle.W(124); // fits the longest crest name
            int segH = UIStyle.H(18);
            int segY = y + (RH - segH) / 2;
            int sp = UIStyle.W(2);
            int groupX = RW - M - arrowW * 2 - valueW - sp * 2;

            MakeLbl(row.transform, "Crest",
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: groupX - M * 2, h: RH);

            bool anyCrest = selectedIdx < 0;
            string valueText = anyCrest ? "Any" : crests[selectedIdx].DisplayName;

            MakeButton(parent, "CrestPrev", "◂",
                UIStyle.FontSizeSm - 2, UIStyle.Subtext, UIStyle.Surface with { a = 0.5f },
                groupX, segY, arrowW, segH,
                () => StepCrestFilter(-1));

            MakeButton(parent, "CrestValue", valueText,
                UIStyle.FontSizeSm - 2,
                anyCrest ? UIStyle.Subtext : UIStyle.Accent,
                anyCrest ? UIStyle.Surface with { a = 0.5f }
                         : UIStyle.Accent with { a = 0.2f },
                groupX + arrowW + sp, segY, valueW, segH,
                () => StepCrestFilter(1));

            MakeButton(parent, "CrestNext", "▸",
                UIStyle.FontSizeSm - 2, UIStyle.Subtext, UIStyle.Surface with { a = 0.5f },
                groupX + arrowW + valueW + sp * 2, segY, arrowW, segH,
                () => StepCrestFilter(1));
        }

        /// <summary>Advances the crest selection: Any → crest1 → ... → Any.</summary>
        private void StepCrestFilter(int direction)
        {
            var crests = CrestDefs();
            if (crests.Count == 0) return;

            int selectedIdx = -1;
            for (int i = 0; i < crests.Count; i++)
                if ((FilterRequire & (1 << crests[i].Bit)) != 0) { selectedIdx = i; break; }

            // -1 (Any) .. crests.Count-1, wrapping through Any.
            int next = selectedIdx + direction;
            if (next < -1) next = crests.Count - 1;
            if (next >= crests.Count) next = -1;

            int require = FilterRequire & ~ModifierRegistry.CrestBitsMask;
            if (next >= 0) require |= 1 << crests[next].Bit;
            GhostSettings.ModifierRequireMask = require;
            RebuildActiveTabContentOnly();
        }

        /// <summary>Crest bits are single-choice via the selector; drop any
        /// stale multi-crest/exclude state (e.g. from older versions).</summary>
        private static void SanitizeCrestFilterBits()
        {
            int crestBits = ModifierRegistry.CrestBitsMask;
            if (crestBits == 0) return;

            if ((FilterExclude & crestBits) != 0)
                GhostSettings.ModifierExcludeMask = FilterExclude & ~crestBits;

            int crestRequire = FilterRequire & crestBits;
            if (crestRequire != 0 && (crestRequire & (crestRequire - 1)) != 0)
                GhostSettings.ModifierRequireMask = FilterRequire & ~crestBits;
        }

        private static string BuildFilterSummary()
        {
            if (!ModifierFilterActive) return "off";

            var sb = new StringBuilder();
            AppendFilterNames(sb, "with ", FilterRequire & ~ModifierRegistry.CrestBitsMask);
            AppendFilterNames(sb, "without ", FilterExclude);

            foreach (var def in ModifierRegistry.All)
            {
                if (!def.IsCrest) continue;
                if ((FilterRequire & (1 << def.Bit)) == 0) continue;
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(def.DisplayName);
                break;
            }

            return sb.Length > 0 ? sb.ToString() : "off";
        }

        private static void AppendFilterNames(StringBuilder sb, string label, int mask)
        {
            if (mask == 0) return;
            if (sb.Length > 0) sb.Append("  ·  ");
            sb.Append(label);
            bool first = true;
            foreach (var def in ModifierRegistry.All)
            {
                if ((mask & (1 << def.Bit)) == 0) continue;
                if (!first) sb.Append(", ");
                sb.Append(def.DisplayName);
                first = false;
            }
        }

        private static List<ModifierDef> NonCrestDefs()
        {
            var list = new List<ModifierDef>();
            foreach (var def in ModifierRegistry.All)
                if (!def.IsCrest) list.Add(def);
            return list;
        }

        private static List<ModifierDef> CrestDefs()
        {
            var list = new List<ModifierDef>();
            foreach (var def in ModifierRegistry.All)
                if (def.IsCrest) list.Add(def);
            return list;
        }

        // ── Row modifier marker ────────────────────────────────────────────
        //
        // Every run row gets a small "?" marker; hovering the marker (and
        // only the marker, not the whole row) shows the run's full loadout
        // in the shared tooltip.

        /// <summary>
        /// Draws the "?" modifier marker for one row, right-aligned ending at
        /// <paramref name="rightEdge"/>, and attaches the loadout tooltip to
        /// it. Returns the marker's width.
        /// </summary>
        private int AddModifierMarker(Transform parent, int rightEdge, int rowH,
            int mask)
        {
            int markerH = UIStyle.H(16);
            int markerW = UIStyle.W(18);

            var marker = MakeGO("ModMarker", parent);
            var img = marker.AddComponent<Image>();
            img.color = UIStyle.Overlay with { a = 0.35f };
            // Raycast target: the marker itself is the tooltip hover area.
            Rect(marker, rightEdge - markerW, (rowH - markerH) / 2,
                markerW, markerH);

            MakeLbl(marker.transform, "?",
                UIStyle.FontSizeSm - 3, UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);

            AttachModifierTooltip(marker, mask);
            return markerW;
        }

        /// <summary>Rebuilds just the active tab's content area (lightweight,
        /// scroll preserved) after a filter change.</summary>
        private void RebuildActiveTabContentOnly()
        {
            if (activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();
            else if (activeTab == TabKind.Runs)
                RebuildRunsContentOnly();
        }

        /// <summary>
        /// Lightweight rebuild of just the Runs content area — the Runs-tab
        /// counterpart of <see cref="RebuildLeaderboardContentOnly"/>:
        /// detached clear, no polling changes, scroll preserved.
        /// </summary>
        private void RebuildRunsContentOnly()
        {
            if (rightContent == null || selectedScene == null) return;

            var scroll = RightScroll;
            float keepScroll = scroll != null
                ? scroll.verticalNormalizedPosition : 1f;

            ClearContentDetached(rightContent);
            BuildRunsContent(selectedScene);
            ForceLayout(rightContent);

            if (scroll != null)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(keepScroll);
        }

        // ── Leaderboard view ranking ───────────────────────────────────────

        /// <summary>
        /// Builds the display view of a route's leaderboard from the cached
        /// best-per-(runner, mask) rows: filter by the current modifier
        /// filter, collapse to each runner's best surviving row, sort by
        /// time, and assign contiguous view ranks on CLONED entries (the
        /// cached rows are never mutated). With no filter active this
        /// reproduces the classic best-per-runner board.
        /// </summary>
        private static List<LeaderboardEntry> BuildRouteView(
            RouteLeaderboard route,
            out int yourViewRank, out LeaderboardEntry? yourRow)
        {
            // Collapse best row per runner. IsYou rows collapse under a
            // dedicated key (optimistic local entries have Rid = -1 and would
            // otherwise collide with server rows).
            var bestPerRunner = new Dictionary<string, LeaderboardEntry>();
            foreach (var e in route.Entries)
            {
                if (!PassesModifierFilter(e.Modifiers)) continue;

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
