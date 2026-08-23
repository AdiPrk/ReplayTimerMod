using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ── Leaderboard tab ────────────────────────────────────────────────
    // (RowHover / ButtonHover / AddHoverEffect live in ReplayUI.Widgets.cs)

    public partial class ReplayUI
    {
        private readonly HashSet<string> _expandedRoutes = new HashSet<string>();

        // Persisted download states (Idle is implicit / absent).
        // NOTE: Full is never stored here — it's derived from current
        // capacity each build, so raising the limit in Config immediately
        // re-enables the buttons.
        private readonly Dictionary<string, GhostDownloadState> _downloadStates =
            new Dictionary<string, GhostDownloadState>();

        // When each Done/Failed state reverts back to Idle (unscaled time).
        // Done reverts so a replay can be re-downloaded (e.g. after deleting
        // the imported copy); Failed reverts so a transient error doesn't
        // leave a permanent "Retry" on the row.
        private readonly Dictionary<string, float> _stateExpiry =
            new Dictionary<string, float>();

        private const float DoneRevertSeconds = 4f;
        private const float FailedRevertSeconds = 6f;

        /// <summary>
        /// Drops all transient download states (Done / Failed / derived
        /// Full). Called whenever local replays are deleted, so the next
        /// leaderboard build re-derives every button from ground truth
        /// (actual local capacity) instead of leaving a stale "Loaded" or
        /// "Full" that blocks re-downloading.
        /// </summary>
        private void InvalidateDownloadStates()
        {
            if (_downloadStates.Count == 0 && _stateExpiry.Count == 0) return;
            _downloadStates.Clear();
            _stateExpiry.Clear();
        }

        private enum GhostDownloadState { Idle, Downloading, Done, Failed, Full }

        private const int LeaderboardCollapsedCount = 3;

        // Live references to ghost buttons in the current build, keyed by
        // runId. Lets download state changes update the button IN PLACE
        // instead of rebuilding the whole content area.
        private sealed class GhostBtnRefs
        {
            public GameObject go = null!;
            public Image bg = null!;
            public Text label = null!;
            public RoomKey roomKey;     // for capacity re-checks on revert
            public float entryTime;
            public int entryMask;       // modifier mask, for mask-best capacity
        }

        private readonly Dictionary<string, GhostBtnRefs> _ghostBtnRefs =
            new Dictionary<string, GhostBtnRefs>();

        private void BuildLeaderboardContent()
        {
            if (_rightContent == null) return;

            // Hidden unless this build reaches the filterable path below
            // (covers the lightweight ContentOnly rebuilds).
            HideFilterToggle();

            _ghostBtnRefs.Clear();

            // Record what data version this build reflects so identical
            // polling responses can skip rebuilds entirely.
            if (_selectedScene != null)
            {
                _renderedLbScene = _selectedScene;
                _renderedLbVersion =
                    _leaderboardCache.GetVersion(_gameTag, _selectedScene);
            }

            if (!GhostSettings.OnlineEnabled)
            {
                AddCenteredMessage(_rightContent,
                    "Enable online features in Config to view leaderboards.");
                return;
            }

            if (_selectedScene == null)
            {
                AddCenteredMessage(_rightContent,
                    "Select a room to view leaderboards.");
                return;
            }

            LeaderboardData? cached = _leaderboardCache.Get(_gameTag, _selectedScene);

            if (cached == null)
            {
                bool polling = _networkClient != null && _networkClient.IsStarted;
                AddCenteredMessage(_rightContent,
                    polling ? "Loading..." : "Leaderboard unavailable.");
                return;
            }

            if (cached.Routes.Count == 0)
            {
                AddCenteredMessage(_rightContent,
                    "No leaderboard data for this room yet.");
                return;
            }

            ShowFilterToggle();

            // Each route renders a VIEW of its cached rows: filtered by the
            // modifier filter, collapsed to best-per-runner, re-ranked
            // client-side (see BuildRouteView). The server's raw per-mask
            // ranks are never displayed. Views are collected up front so
            // the time column can be sized to the widest time before any
            // row is built (all view entries, not just the visible window,
            // so expanding a route never shifts the columns).
            var shownRoutes = new List<RouteLeaderboard>();
            var shownViews =
                new List<List<LeaderboardEntry>>();
            var shownRanks = new List<int>();
            var shownYourRows = new List<LeaderboardEntry?>();
            var shownTimes = new List<float>();
            _filterShownCount = 0;
            _filterTotalCount = 0;
            _filterCountUnit = "entries";
            foreach (var route in cached.Routes)
            {
                // Footer total: the unfiltered collapsed board (what the
                // route would show with no filter), not raw server rows.
                _filterTotalCount += RouteView.CountCollapsed(route);

                var view = BuildRouteView(route,
                    out int yourViewRank, out var yourRow);
                if (view.Count == 0) continue;

                _filterShownCount += view.Count;
                shownRoutes.Add(route);
                shownViews.Add(view);
                shownRanks.Add(yourViewRank);
                shownYourRows.Add(yourRow);
                foreach (var e in view)
                    shownTimes.Add(e.TotalTime);
            }

            if (shownRoutes.Count == 0)
            {
                AddCenteredMessage(_rightContent,
                    "No leaderboard runs match the modifier filter.");
                return;
            }

            int timeColW = TimeColumnWidth(shownTimes);

            bool stripe = false;
            for (int i = 0; i < shownRoutes.Count; i++)
            {
                AddLeaderboardRouteGroup(_rightContent, shownRoutes[i],
                    shownViews[i], shownRanks[i], shownYourRows[i], stripe,
                    timeColW);
                stripe = !stripe;
            }
        }

        private void AddLeaderboardRouteGroup(Transform parent,
            RouteLeaderboard route,
            List<LeaderboardEntry> view,
            int yourViewRank, LeaderboardEntry? yourRow, bool stripe,
            int timeColW)
        {
            // Scene-qualified: transition names repeat across rooms
            // (left1>right1 exists nearly everywhere), and _expandedRoutes
            // persists across scene selection.
            string routeKey = _selectedScene + "|" + route.EntryFrom + ">" + route.ExitTo;
            bool isExpanded = _expandedRoutes.Contains(routeKey);
            int showCount = isExpanded
                ? view.Count
                : Mathf.Min(LeaderboardCollapsedCount, view.Count);

            // View ranks are contiguous (1..N), so "you" sits below the
            // visible window exactly when your view rank exceeds it.
            bool showYourEntryBelow = yourRow != null && yourViewRank > showCount;

            int headerH = RH + 2;
            int rowCount = showCount;
            if (showYourEntryBelow) rowCount += 2;

            bool showExpandBtn = view.Count > LeaderboardCollapsedCount;
            int expandBtnH = showExpandBtn ? RH : 0;
            int totalH = headerH + rowCount * RH + expandBtnH;

            var group = MakeGO("LBRouteGroup", parent);
            Img(group, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            var le = group.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = totalH;

            AddLeaderboardRouteHeader(group.transform, route, routeKey,
                isExpanded, showExpandBtn, headerH, view.Count);

            float wrTime = view[0].TotalTime;

            for (int i = 0; i < showCount; i++)
                AddLeaderboardRow(group.transform, route, view[i],
                    wrTime, i, headerH, timeColW);

            int nextY = headerH + showCount * RH;

            if (showYourEntryBelow && yourRow != null)
            {
                int gapCount = yourViewRank - showCount - 1;
                AddLeaderboardGapRow(group.transform, gapCount, nextY);
                nextY += RH;
                AddLeaderboardRow(group.transform, route, yourRow,
                    wrTime, -1, nextY, timeColW, forceYou: true);
                nextY += RH;
            }

            if (showExpandBtn)
                AddLeaderboardExpandButton(group.transform, routeKey,
                    isExpanded, view.Count, nextY);
        }

        private void AddLeaderboardRouteHeader(Transform parent,
            RouteLeaderboard route, string routeKey, bool isExpanded,
            bool hasExpandBtn, int h, int viewCount)
        {
            var row = MakeGO("LBRouteHeader", parent);
            Img(row, UIStyle.Overlay with { a = 0.45f });
            Rect(row, 0, 0, RW, h);

            if (hasExpandBtn)
            {
                AddHoverEffect(row);
                string rk = routeKey;
                Btn(row, () => ToggleRouteExpanded(rk));
            }

            // Drawn caret (right = collapsed, down = expanded) on expandable
            // routes; the label indent is reserved either way so all route
            // headers align.
            int caretS = UIStyle.H(8);
            int labelX = M + caretS + UIStyle.W(5);
            if (hasExpandBtn)
                AddCaret(row.transform, M + caretS / 2f, h / 2f, caretS,
                    UIStyle.Subtext, isExpanded ? -90f : 0f);

            string from = string.IsNullOrEmpty(route.EntryFrom)
                ? "spawn" : route.EntryFrom;

            // Warp button (far right; experimental, hidden unless enabled in
            // Config) — only when the entry transition into this room is
            // known. Its own Button consumes the click, so it doesn't trigger
            // the row-wide expand toggle. Same warp action as the Runs tab.
            int rightEdge = RW - M;
            if (GhostSettings.RoomWarpEnabled && _selectedScene != null)
            {
                var warpKey = new RoomKey(_selectedScene, route.EntryFrom, route.ExitTo);
                if (QuickWarp.CanWarp(warpKey))
                {
                    int warpW = UIStyle.W(44);
                    int warpH = UIStyle.H(20);
                    int warpX = RW - warpW - M;
                    int warpY = (h - warpH) / 2;
                    RoomKey wk = warpKey;
                    MakeButton(row.transform, "LBWarp", "Warp",
                        UIStyle.FontSizeBtn, UIStyle.Green,
                        UIStyle.BtnBg(UIStyle.Green),
                        warpX, warpY, warpW, warpH,
                        () => OnRouteWarpClicked(wk));
                    rightEdge = warpX - M * 2;
                }
            }

            MakeLbl(row.transform, from + " to " + route.ExitTo,
                UIStyle.FontSizeRow, UIStyle.Text, TextAnchor.MiddleLeft,
                x: labelX, w: RW / 2 - labelX + M, h: h);

            // Runner count: server truth when unfiltered, matching-view count
            // when a modifier filter narrows the board.
            string runnersText = ModifierFilterActive
                ? viewCount + " matching"
                : route.TotalRunners + " runner" + (route.TotalRunners != 1 ? "s" : "");
            int runnersX = RW / 2;
            MakeLbl(row.transform, runnersText,
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleRight,
                x: runnersX, w: rightEdge - runnersX, h: h);
        }

        private void AddLeaderboardRow(Transform parent, RouteLeaderboard route,
            LeaderboardEntry entry, float wrTime,
            int visualIndex, int yOffset, int timeColW, bool forceYou = false)
        {
            int h = RH;
            int top = visualIndex >= 0 ? yOffset + visualIndex * RH : yOffset;

            bool isYou = entry.IsYou || forceYou;
            bool isWR = entry.Rank == 1;

            Color rowBg;
            if (isYou)
                rowBg = UIStyle.Accent with { a = 0.12f };
            else if (visualIndex >= 0 && visualIndex % 2 == 1)
                rowBg = UIStyle.Surface with { a = 0.35f };
            else
                rowBg = Color.clear;

            var row = MakeGO("LBRow", parent);
            Img(row, rowBg);
            Rect(row, 0, top, RW, h);

            AddHoverEffect(row);

            int x = M / 2;

            // ── Rank ───────────────────────────────────────────────────

            int rankW = UIStyle.W(26);
            MakeLbl(row.transform, "#" + entry.Rank,
                UIStyle.FontSizeRow,
                isWR ? UIStyle.Gold : UIStyle.Text,
                TextAnchor.MiddleRight, x: x, w: rankW, h: h);
            x += rankW + M;

            // ── Layout: RowRightCluster (shared with the Runs tab) owns
            // the right-side geometry so the two tabs stay in lockstep.
            //
            // [#] [Name .......] [?] [delta] [Time] [Copy] [Load]
            //                                             flush right

            int deltaW = UIStyle.W(52);
            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;
            int ghostW = UIStyle.W(46);

            GhostDownloadState dlState = GhostDownloadState.Idle;
            if (!string.IsNullOrEmpty(entry.RunId))
            {
                _downloadStates.TryGetValue(entry.RunId, out dlState);

                // Done/Failed states expire back to Idle so replays can be
                // re-downloaded and transient errors don't stick forever
                if ((dlState == GhostDownloadState.Done
                        || dlState == GhostDownloadState.Failed)
                    && _stateExpiry.TryGetValue(entry.RunId, out float expiry)
                    && Time.unscaledTime >= expiry)
                {
                    _downloadStates.Remove(entry.RunId);
                    _stateExpiry.Remove(entry.RunId);
                    dlState = GhostDownloadState.Idle;
                }
            }

            bool showGhost = !string.IsNullOrEmpty(entry.RunId);

            // Capacity check (derived fresh every build — raising the limit
            // in Config immediately re-enables these buttons)
            if (showGhost && dlState == GhostDownloadState.Idle
                && _selectedScene != null)
            {
                var routeRoomKey = new RoomKey(_selectedScene,
                    route.EntryFrom, route.ExitTo);
                if (!RouteHistoryHasCapacity(routeRoomKey, entry.TotalTime,
                        entry.Modifiers))
                    dlState = GhostDownloadState.Full;
            }

            // Copy-link button sits just left of the ghost button (online only,
            // and only when there is a run id to share).
            bool showCopyLink = showGhost
                && _networkClient != null && _networkClient.IsStarted;
            int linkW = UIStyle.W(46);

            var cluster = RowRightCluster.Begin(RW);
            int ghostX = showGhost ? cluster.AddButton(ghostW) : 0;
            int linkX = showCopyLink ? cluster.AddButton(linkW) : 0;
            int timeX = cluster.AddTime(timeColW);

            bool hasDelta = !isWR && wrTime > 0f;
            if (hasDelta)
            {
                float delta = entry.TotalTime - wrTime;
                MakeLbl(row.transform, FormatDelta(delta),
                    UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleRight,
                    x: cluster.AddSlot(deltaW), w: deltaW, h: h);
            }

            if (showGhost)
            {
                var ghostGO = MakeGO("LBGhost", row.transform);
                var ghostImg = ghostGO.AddComponent<Image>();
                Rect(ghostGO, ghostX, btnY, ghostW, btnH);

                var ghostLbl = MakeLbl(ghostGO.transform, "",
                    UIStyle.FontSizeBtn, UIStyle.Accent, TextAnchor.MiddleCenter,
                    fill: true);

                // Idle and Failed are actionable; the state guard in
                // OnGhostDownloadClicked covers in-place transitions.
                if (_networkClient != null && _networkClient.IsStarted
                    && dlState != GhostDownloadState.Full)
                {
                    string rid = entry.RunId;
                    string rname = entry.RunnerName ?? "";
                    bool isMe = entry.IsYou;
                    Btn(ghostGO, () => OnGhostDownloadClicked(rid, rname, isMe));
                    AddButtonHover(ghostGO);
                }

                var refs = new GhostBtnRefs
                {
                    go = ghostGO,
                    bg = ghostImg,
                    label = ghostLbl,
                    roomKey = _selectedScene != null
                        ? new RoomKey(_selectedScene, route.EntryFrom, route.ExitTo)
                        : default,
                    entryTime = entry.TotalTime,
                    entryMask = entry.Modifiers
                };
                _ghostBtnRefs[entry.RunId] = refs;
                SetGhostVisual(refs, dlState);
            }

            if (showCopyLink)
            {
                var linkGO = MakeGO("LBCopyLink", row.transform);
                Img(linkGO, UIStyle.BtnBg(UIStyle.Accent));
                Rect(linkGO, linkX, btnY, linkW, btnH);

                var linkLbl = MakeLbl(linkGO.transform, "Copy",
                    UIStyle.FontSizeBtn, UIStyle.Accent, TextAnchor.MiddleCenter,
                    fill: true);

                string linkRid = entry.RunId;
                Btn(linkGO, () => OnCopyLinkClicked(linkRid, linkLbl));
                AddButtonHover(linkGO);
            }

            // ── Time ───────────────────────────────────────────────────

            MakeLbl(row.transform, TimeUtil.Format(entry.TotalTime),
                UIStyle.FontSizeSm,
                isWR ? UIStyle.Gold : UIStyle.Text,
                TextAnchor.MiddleRight, x: timeX, w: timeColW, h: h);

            // ── Runner name (fills remaining space) ─────────────────────
            // Always shows the actual runner name; your own entry is
            // indicated by accent color + the row highlight instead of
            // replacing the name with "You".

            // "?" marker right-aligned against the buttons — hovering it
            // shows the run's full loadout; the name label shrinks to fit.
            int markerW = AddModifierMarker(row.transform, cluster.MarkerRight,
                h, entry.Modifiers);

            int nameW = cluster.LabelEnd(markerW) - x;

            string displayName = string.IsNullOrEmpty(entry.RunnerName)
                ? "???" : entry.RunnerName!;

            MakeLbl(row.transform, displayName,
                UIStyle.FontSizeRow,
                isYou ? UIStyle.Accent : UIStyle.Text,
                TextAnchor.MiddleLeft, x: x, w: nameW, h: h);
        }

        private void AddLeaderboardGapRow(Transform parent, int gapCount, int yOffset)
        {
            var row = MakeGO("LBGap", parent);
            Img(row, Color.clear);
            Rect(row, 0, yOffset, RW, RH);

            string text = gapCount > 0
                ? gapCount + " more runner" + (gapCount != 1 ? "s" : "")
                : "...";

            MakeLbl(row.transform, text,
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleCenter,
                x: 0, w: RW, h: RH);
        }

        private void AddLeaderboardExpandButton(Transform parent,
            string routeKey, bool isExpanded, int entryCount, int yOffset)
        {
            var row = MakeGO("LBExpand", parent);
            Img(row, Color.clear);
            Rect(row, 0, yOffset, RW, RH);

            AddHoverEffect(row);

            string rk = routeKey;
            Btn(row, () => ToggleRouteExpanded(rk));

            // Centered caret + text block (drawn caret, up = collapse,
            // down = expand - no text glyphs).
            string text = isExpanded
                ? "Show less"
                : "Show " + (entryCount - LeaderboardCollapsedCount) + " more";
            int caretS = UIStyle.H(8);
            int gap = UIStyle.W(5);
            int textW = Mathf.CeilToInt(
                MeasureTextWidth(text, UIStyle.FontSizeBtn));
            int blockX = (RW - (caretS + gap + textW)) / 2;

            AddCaret(row.transform, blockX + caretS / 2f, RH / 2f, caretS,
                UIStyle.Accent, isExpanded ? 90f : -90f);
            MakeLbl(row.transform, text,
                UIStyle.FontSizeBtn, UIStyle.Accent, TextAnchor.MiddleLeft,
                x: blockX + caretS + gap, w: textW + UIStyle.W(2), h: RH);
        }

        /// <summary>
        /// Toggles a route's expanded state and does a lightweight rebuild
        /// — no polling stop/restart, scroll position preserved.
        /// </summary>
        private void ToggleRouteExpanded(string routeKey)
        {
            if (_expandedRoutes.Contains(routeKey))
                _expandedRoutes.Remove(routeKey);
            else
                _expandedRoutes.Add(routeKey);

            if (_activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();
        }

        /// <summary>
        /// Lightweight rebuild of just the leaderboard content area:
        /// detached clear (no one-frame layout glitch), no polling
        /// stop/restart, no config ref clearing, scroll preserved.
        /// </summary>
        private void RebuildLeaderboardContentOnly()
        {
            if (_rightContent == null) return;

            var scroll = RightScroll;
            float keepScroll = scroll != null ? ScrollOffsetFromTop(scroll) : 0f;

            ClearContentDetached(_rightContent);
            BuildLeaderboardContent();
            ForceLayout(_rightContent);

            if (scroll != null)
                RestoreScrollOffsetFromTop(scroll, keepScroll);

            RefreshFilterPopupIfOpen();
        }

        // Empty for a non-positive delta (ties / the WR row show no delta).
        private static string FormatDelta(float delta) =>
            delta <= 0f ? "" : TimeUtil.FormatDelta(delta);

        // ── Capacity ───────────────────────────────────────────────────

        /// <summary>
        /// Whether a downloaded replay with the given time would survive
        /// import into this route's history. PBManager prunes each route
        /// to the best MaxSavedReplaysPerRoute by time, so a replay slower
        /// than the worst kept snapshot would be silently pruned when the
        /// route is at capacity.
        /// </summary>
        private static bool RouteHistoryHasCapacity(RoomKey key, float time,
            int mask = ModifierMask.Unknown)
            => PBManager.WouldKeepReplay(key, time, mask);

        // ── Ghost button visuals (in-place updates) ────────────────────

        /// <summary>
        /// Paints a ghost button for the given state. Used both at build
        /// time and for in-place updates during downloads — state changes
        /// never trigger a content rebuild.
        /// </summary>
        private void SetGhostVisual(GhostBtnRefs refs, GhostDownloadState state)
        {
            string label;
            Color fg, bg;

            switch (state)
            {
                case GhostDownloadState.Downloading:
                    label = "...";
                    fg = UIStyle.Subtext;
                    bg = UIStyle.Surface with { a = 0.5f };
                    break;
                case GhostDownloadState.Done:
                    label = "Loaded";
                    fg = UIStyle.Green;
                    bg = UIStyle.BtnBg(UIStyle.Green);
                    break;
                case GhostDownloadState.Failed:
                    label = "Retry";
                    fg = UIStyle.Red;
                    bg = UIStyle.BtnBg(UIStyle.Red);
                    break;
                case GhostDownloadState.Full:
                    label = "Full";
                    fg = UIStyle.Subtext;
                    bg = UIStyle.Overlay with { a = 0.35f };
                    break;
                default: // Idle
                    label = "Load";
                    fg = UIStyle.Accent;
                    bg = UIStyle.BtnBg(UIStyle.Accent);
                    break;
            }

            refs.label.text = label;
            refs.label.color = fg;
            refs.bg.color = bg;
        }

        /// <summary>
        /// Updates a ghost button in place from _downloadStates. When the
        /// stored state resolves to Idle, re-derives the Full state from
        /// current capacity (the same logic used at build time). Returns
        /// false if the button isn't part of the current build — callers
        /// fall back to a rebuild only in that case.
        /// </summary>
        private bool TryUpdateGhostVisual(string runId)
        {
            if (!_ghostBtnRefs.TryGetValue(runId, out var refs)
                || refs.go == null) // Unity-destroyed check
                return false;

            GhostDownloadState state = GhostDownloadState.Idle;
            _downloadStates.TryGetValue(runId, out state);

            if (state == GhostDownloadState.Idle
                && !RouteHistoryHasCapacity(refs.roomKey, refs.entryTime,
                        refs.entryMask))
                state = GhostDownloadState.Full;

            SetGhostVisual(refs, state);
            return true;
        }

        private bool TryUpdateGhostVisual(string runId, GhostDownloadState explicitState)
        {
            if (!_ghostBtnRefs.TryGetValue(runId, out var refs)
                || refs.go == null)
                return false;

            SetGhostVisual(refs, explicitState);
            return true;
        }

        /// <summary>
        /// Reverts expired Done ("Loaded") and Failed ("Retry") states back to
        /// Idle — Done so replays can be re-downloaded (e.g. after deleting
        /// the imported copy), Failed so errors don't stick forever. Updates
        /// buttons in place — no rebuild. Called from Tick while paused.
        /// </summary>
        private void TickGhostStateExpiry()
        {
            if (_stateExpiry.Count == 0) return;

            float now = Time.unscaledTime;
            List<string>? expired = null;

            foreach (var kvp in _stateExpiry)
            {
                if (now >= kvp.Value)
                    (expired ??= new List<string>()).Add(kvp.Key);
            }

            if (expired == null) return;

            foreach (string runId in expired)
            {
                _stateExpiry.Remove(runId);
                _downloadStates.Remove(runId);
                TryUpdateGhostVisual(runId); // back to "Load" (or "Full")
            }
        }

        // ── Ghost download flow ────────────────────────────────────────

        private void OnGhostDownloadClicked(string runId, string runnerName, bool isYou)
        {
            if (_networkClient == null || !_networkClient.IsStarted) return;
            if (string.IsNullOrEmpty(runId)) return;

            // Guard against re-entry on in-place updated buttons
            if (_downloadStates.TryGetValue(runId, out var current)
                && (current == GhostDownloadState.Downloading
                    || current == GhostDownloadState.Done))
                return;

            _downloadStates[runId] = GhostDownloadState.Downloading;
            _stateExpiry.Remove(runId);

            // In-place visual update — NO rebuild
            if (!TryUpdateGhostVisual(runId) && _activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();

            _networkClient.DownloadReplay(runId, replayBytes =>
            {
                if (replayBytes == null || replayBytes.Length == 0)
                {
                    SetGhostFailed(runId, "download failed");
                    return;
                }

                RecordedRoom? room = ReplayShareEncoder.Decode(replayBytes);
                if (room == null)
                {
                    SetGhostFailed(runId, "decode failed");
                    return;
                }

                // Capacity re-check with the actual decoded time + mask.
                // PBManager prunes routes to the best N by time (plus
                // per-mask bests), so importing a replay slower than the
                // worst kept snapshot would silently vanish.
                if (!RouteHistoryHasCapacity(room.Key, room.TotalTime,
                        room.Modifiers))
                {
                    _downloadStates.Remove(runId); // Full is derived, not stored
                    Log.LogInfo("[Leaderboard] Ghost not imported — route history " +
                        "is full (" + GhostSettings.MaxSavedReplaysPerRoute +
                        " max). Raise 'Max saved replays' in Config to keep " +
                        "slower replays.");
                    if (!TryUpdateGhostVisual(runId, GhostDownloadState.Full)
                        && _activeTab == TabKind.Leaderboard)
                        RebuildLeaderboardContentOnly();
                    return;
                }

                bool imported = PBManager.ImportPB(room) == PBManager.ImportOutcome.Imported;
                _downloadStates[runId] = GhostDownloadState.Done;
                _stateExpiry[runId] = Time.unscaledTime + DoneRevertSeconds;

                if (imported)
                {
                    // Tag the imported snapshot with its owner so the Runs
                    // tab can show whose run it is — but NOT for your own
                    // replays, which should appear as normal personal runs.
                    if (!isYou)
                        TagImportedSnapshot(room, runnerName);

                    Log.LogInfo("[Leaderboard] Ghost imported: " +
                        room.Key.SceneName + "[" + room.Key.EntryFromScene +
                        "\u2192" + room.Key.ExitToScene + "] " +
                        TimeUtil.Format(room.TotalTime) +
                        (string.IsNullOrEmpty(runnerName)
                            ? "" : " by " + runnerName));
                }
                else
                {
                    Log.LogInfo("[Leaderboard] Ghost already exists locally");
                }

                // In-place tick — the full refresh (scene list gains the
                // room as local, Runs tab data) happens via OnPBUpdated on
                // the next Tick, with scroll position preserved.
                TryUpdateGhostVisual(runId);
                OnPBUpdated();
            });
        }

        /// <summary>
        /// Marks a download as Failed with auto-revert, updating in place.
        /// </summary>
        private void SetGhostFailed(string runId, string reason)
        {
            _downloadStates[runId] = GhostDownloadState.Failed;
            _stateExpiry[runId] = Time.unscaledTime + FailedRevertSeconds;
            Log.LogInfo("[Leaderboard] Ghost " + reason + " for " + runId);
            if (!TryUpdateGhostVisual(runId) && _activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();
        }

        /// <summary>
        /// Finds the snapshot just created by ImportPB (newest CapturedAt
        /// with a matching time in the route's history) and records its
        /// owner in the ReplayOwners sidecar store.
        /// </summary>
        private static void TagImportedSnapshot(RecordedRoom room, string runnerName)
        {
            if (string.IsNullOrEmpty(runnerName)) return;

            var history = PBManager.GetHistory(room.Key);
            ReplaySnapshot? newest = null;
            foreach (var snap in history)
            {
                if (!Mathf.Approximately(snap.TotalTime, room.TotalTime)) continue;
                if (newest == null
                    || snap.CapturedAtUtcTicks > newest.CapturedAtUtcTicks)
                    newest = snap;
            }

            if (newest != null)
                ReplayOwners.Set(newest.SnapshotId, runnerName);
        }
    }
}