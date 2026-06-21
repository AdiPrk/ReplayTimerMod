using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ── Interaction MonoBehaviours ──────────────────────────────────────
    //
    // IMPORTANT: these components must stay ENABLED. Unity's EventSystem
    // (ExecuteEvents.ShouldSendToComponent) skips disabled Behaviours, so
    // a disabled component never receives OnPointerEnter/Exit. The Update
    // methods early-out when settled instead

    /// <summary>
    /// Subtle hover highlight on any interactive row.
    /// </summary>
    internal sealed class RowHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Image? overlay;

        private float _current;
        private float _target;

        private const float HoverAlpha = 0.055f;
        private const float Speed = 14f;

        public void Init()
        {
            _current = 0f;
            _target = 0f;
            if (overlay != null)
                overlay.color = SetAlpha(overlay.color, 0f);
        }

        public void OnPointerEnter(PointerEventData e) { _target = HoverAlpha; }
        public void OnPointerExit(PointerEventData e) { _target = 0f; }

        private void Update()
        {
            if (_current == _target) return; // settled — skip all work

            _current = Mathf.MoveTowards(_current, _target,
                Time.unscaledDeltaTime * Speed);
            if (overlay != null)
                overlay.color = SetAlpha(overlay.color, _current);
        }

        private static Color SetAlpha(Color c, float a) =>
            new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// Animates the ghost download button: slide-in-from-the-right + fade
    /// on hover, reverse on exit. When <see cref="pinned"/> is set (active
    /// download / done / failed), the button stays fully visible regardless
    /// of hover.
    /// </summary>
    internal sealed class RowHoverReveal : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public CanvasGroup? ghostCg;
        public RectTransform? ghostRt;
        public float shownX;
        public float hiddenX;

        /// <summary>Forces the button visible (downloading/done/failed).</summary>
        public bool pinned;

        /// <summary>
        /// Forces the button permanently visible regardless of hover. Used so
        /// the download button is always reachable — hover-only reveal was
        /// unreliable (pointer/raycast quirks could leave it stuck hidden).
        /// </summary>
        public bool alwaysShown;

        private float _progress;
        private bool _hovered;

        private const float Speed = 10f;

        /// <param name="startShown">
        /// Start fully revealed (for buttons rebuilt in a pinned state) so
        /// content rebuilds don't replay the slide-in animation on every
        /// existing ✓/✗ button.
        /// </param>
        public void Init(bool startShown = false)
        {
            _progress = startShown ? 1f : 0f;
            _hovered = false;
            pinned = false;
            Apply(_progress);
        }

        public void OnPointerEnter(PointerEventData e) { _hovered = true; }
        public void OnPointerExit(PointerEventData e) { _hovered = false; }

        private void Update()
        {
            float target = (alwaysShown || pinned || _hovered) ? 1f : 0f;
            if (_progress == target) return; // settled — skip all work

            _progress = Mathf.MoveTowards(_progress, target,
                Time.unscaledDeltaTime * Speed);
            Apply(_progress);
        }

        private void Apply(float p)
        {
            if (ghostCg != null)
            {
                ghostCg.alpha = p;
                ghostCg.blocksRaycasts = p > 0.5f;
                ghostCg.interactable = p > 0.5f;
            }

            if (ghostRt != null)
            {
                float x = Mathf.Lerp(hiddenX, shownX, p);
                ghostRt.anchoredPosition = new Vector2(x, ghostRt.anchoredPosition.y);
            }
        }
    }

    /// <summary>
    /// Bridges hover events from a child element (ghost button) back to
    /// the parent row's hover components, so the reveal doesn't collapse
    /// when the cursor moves onto the button to click it.
    /// </summary>
    internal sealed class ChildHoverBridge : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public RowHoverReveal? reveal;
        public RowHover? hover;

        public void OnPointerEnter(PointerEventData e)
        {
            if (reveal != null) reveal.OnPointerEnter(e);
            if (hover != null) hover.OnPointerEnter(e);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (reveal != null) reveal.OnPointerExit(e);
            if (hover != null) hover.OnPointerExit(e);
        }
    }

    // ── Leaderboard tab ────────────────────────────────────────────────

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
        // leave a permanent ✗ on the row.
        private readonly Dictionary<string, float> _stateExpiry =
            new Dictionary<string, float>();

        private const float DoneRevertSeconds = 4f;
        private const float FailedRevertSeconds = 6f;

        /// <summary>
        /// Drops all transient download states (✓ Done / ✗ Failed / derived
        /// Full). Called whenever local replays are deleted, so the next
        /// leaderboard build re-derives every button from ground truth
        /// (actual local capacity) instead of leaving a stale ✓ or "full"
        /// that blocks re-downloading.
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
            public CanvasGroup cg = null!;
            public RowHoverReveal? reveal;
            public RoomKey roomKey;     // for capacity re-checks on revert
            public float entryTime;
        }

        private readonly Dictionary<string, GhostBtnRefs> _ghostBtnRefs =
            new Dictionary<string, GhostBtnRefs>();

        private static RowHover AddHoverEffect(GameObject row)
        {
            var overlayGO = MakeGO("HoverOverlay", row.transform);
            var img = overlayGO.AddComponent<Image>();
            img.color = new Color(UIStyle.Text.r, UIStyle.Text.g, UIStyle.Text.b, 0f);
            img.raycastTarget = false;
            Fill(overlayGO);

            var hover = row.AddComponent<RowHover>();
            hover.overlay = img;
            hover.Init();
            return hover;
        }

        private void BuildLeaderboardContent()
        {
            if (rightContent == null) return;

            // Stale refs from the previous build are invalid now
            _ghostBtnRefs.Clear();

            // Record what data version this build reflects so identical
            // polling responses can skip rebuilds entirely.
            if (selectedScene != null)
            {
                _renderedLbScene = selectedScene;
                _renderedLbVersion =
                    _leaderboardCache.GetVersion(_gameTag, selectedScene);
            }

            if (!GhostSettings.OnlineEnabled)
            {
                AddCenteredMessage(rightContent,
                    "Enable online features in Config to view leaderboards.");
                return;
            }

            if (selectedScene == null)
            {
                AddCenteredMessage(rightContent,
                    "Select a room to view leaderboards.");
                return;
            }

            LeaderboardData? cached = _leaderboardCache.Get(_gameTag, selectedScene);

            if (cached == null)
            {
                bool polling = _networkClient != null && _networkClient.IsStarted;
                AddCenteredMessage(rightContent,
                    polling ? "Loading..." : "Leaderboard unavailable.");
                return;
            }

            if (cached.Routes.Count == 0)
            {
                AddCenteredMessage(rightContent,
                    "No leaderboard data for this room yet.");
                return;
            }

            bool stripe = false;
            foreach (var route in cached.Routes)
            {
                AddLeaderboardRouteGroup(rightContent, route, stripe);
                stripe = !stripe;
            }
        }

        private void AddLeaderboardRouteGroup(Transform parent,
            RouteLeaderboard route, bool stripe)
        {
            string routeKey = route.EntryFrom + ">" + route.ExitTo;
            bool isExpanded = _expandedRoutes.Contains(routeKey);
            int showCount = isExpanded
                ? route.Entries.Count
                : System.Math.Min(LeaderboardCollapsedCount, route.Entries.Count);

            bool showYourEntryBelow = false;
            LeaderboardEntry? yourEntryToShow = null;

            if (route.YourEntry != null)
            {
                int maxDisplayedRank = showCount > 0
                    ? route.Entries[showCount - 1].Rank : 0;
                if (route.YourEntry.Rank > maxDisplayedRank)
                {
                    showYourEntryBelow = true;
                    yourEntryToShow = route.YourEntry;
                }
            }

            if (!showYourEntryBelow && !isExpanded)
            {
                for (int i = showCount; i < route.Entries.Count; i++)
                {
                    if (route.Entries[i].IsYou)
                    {
                        showYourEntryBelow = true;
                        yourEntryToShow = route.Entries[i];
                        break;
                    }
                }
            }

            bool youAlreadyVisible = false;
            for (int i = 0; i < showCount; i++)
            {
                if (route.Entries[i].IsYou) { youAlreadyVisible = true; break; }
            }
            if (youAlreadyVisible) showYourEntryBelow = false;

            int headerH = RH + 2;
            int rowCount = showCount;
            if (showYourEntryBelow) rowCount += 2;

            bool showExpandBtn = route.Entries.Count > LeaderboardCollapsedCount;
            int expandBtnH = showExpandBtn ? RH : 0;
            int totalH = headerH + rowCount * RH + expandBtnH;

            var group = MakeGO("LBRouteGroup", parent);
            Img(group, stripe ? UIStyle.Surface with { a = 0.3f } : Color.clear);
            var le = group.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = totalH;

            AddLeaderboardRouteHeader(group.transform, route, routeKey,
                isExpanded, showExpandBtn, headerH);

            float wrTime = route.Entries.Count > 0
                ? route.Entries[0].TotalTime : 0f;

            for (int i = 0; i < showCount; i++)
                AddLeaderboardRow(group.transform, route, route.Entries[i],
                    wrTime, i, headerH);

            int nextY = headerH + showCount * RH;

            if (showYourEntryBelow && yourEntryToShow != null)
            {
                int lastShownRank = showCount > 0
                    ? route.Entries[showCount - 1].Rank : 0;
                int gapCount = yourEntryToShow.Rank - lastShownRank - 1;
                AddLeaderboardGapRow(group.transform, gapCount, nextY);
                nextY += RH;
                AddLeaderboardRow(group.transform, route, yourEntryToShow,
                    wrTime, -1, nextY, forceYou: true);
                nextY += RH;
            }

            if (showExpandBtn)
                AddLeaderboardExpandButton(group.transform, routeKey,
                    isExpanded, route.Entries.Count, nextY);
        }

        private void AddLeaderboardRouteHeader(Transform parent,
            RouteLeaderboard route, string routeKey, bool isExpanded,
            bool hasExpandBtn, int h)
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

            string arrow = hasExpandBtn
                ? (isExpanded ? "\u25BE " : "\u25B8 ") : "  ";
            string from = string.IsNullOrEmpty(route.EntryFrom)
                ? "spawn" : route.EntryFrom;

            // Warp button (far right) — only when the entry transition into
            // this room is known. Its own Button consumes the click, so it
            // doesn't trigger the row-wide expand toggle. Same warp action as
            // the Runs tab.
            int rightEdge = RW - M;
            if (selectedScene != null)
            {
                var warpKey = new RoomKey(selectedScene, route.EntryFrom, route.ExitTo);
                if (QuickWarp.CanWarp(warpKey))
                {
                    int warpW = UIStyle.W(24);
                    int warpH = UIStyle.H(18);
                    int warpX = RW - warpW - M / 2;
                    int warpY = (h - warpH) / 2;
                    RoomKey wk = warpKey;
                    MakeButton(row.transform, "LBWarp", "\u25B6",
                        UIStyle.FontSizeSm - 2, UIStyle.Green,
                        UIStyle.Green with { a = 0.18f },
                        warpX, warpY, warpW, warpH,
                        () => OnRouteWarpClicked(wk));
                    rightEdge = warpX - M / 4;
                }
            }

            MakeLbl(row.transform, arrow + from + " \u2192 " + route.ExitTo,
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: RW / 2, h: h);

            int runnersX = RW / 2;
            MakeLbl(row.transform,
                route.TotalRunners + " runner" + (route.TotalRunners != 1 ? "s" : ""),
                UIStyle.FontSizeSm - 2, UIStyle.Subtext, TextAnchor.MiddleRight,
                x: runnersX, w: rightEdge - runnersX, h: h);
        }

        private void AddLeaderboardRow(Transform parent, RouteLeaderboard route,
            LeaderboardEntry entry, float wrTime, int visualIndex, int yOffset,
            bool forceYou = false)
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
                UIStyle.FontSizeSm - 1,
                isWR ? UIStyle.Gold : UIStyle.Text,
                TextAnchor.MiddleRight, x: x, w: rankW, h: h);
            x += rankW + M;

            // ── Layout: time is ALWAYS flush right ─────────────────────
            //
            // [#] [Name .............] [↓ ghost] [Δdelta] [Time]
            //                            slides in          flush
            //                            from the right     right

            int timeW = UIStyle.W(56);
            int timeX = RW - timeW - M / 2;

            // ── Delta vs WR ─────────────────────────────────────────────

            int deltaW = UIStyle.W(52);
            int deltaX = timeX - deltaW - M / 4;

            bool hasDelta = !isWR && wrTime > 0f;
            if (hasDelta)
            {
                float delta = entry.TotalTime - wrTime;
                MakeLbl(row.transform, FormatDelta(delta),
                    UIStyle.FontSizeSm - 2, UIStyle.Subtext, TextAnchor.MiddleRight,
                    x: deltaX, w: deltaW, h: h);
            }

            // ── Ghost download button (left of delta/time) ─────────────

            int ghostW = UIStyle.W(26);

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

            bool showGhost = !isYou && !string.IsNullOrEmpty(entry.RunId);

            // Capacity check (derived fresh every build — raising the limit
            // in Config immediately re-enables these buttons)
            if (showGhost && dlState == GhostDownloadState.Idle
                && selectedScene != null)
            {
                var routeRoomKey = new RoomKey(selectedScene,
                    route.EntryFrom, route.ExitTo);
                if (!RouteHistoryHasCapacity(routeRoomKey, entry.TotalTime))
                    dlState = GhostDownloadState.Full;
            }

            int ghostRightEdge = hasDelta ? deltaX : timeX;
            int ghostShownX = ghostRightEdge - ghostW;
            int slideOffset = ghostW + M / 2;
            int ghostHiddenX = ghostShownX + slideOffset;

            if (showGhost)
            {
                // Pinned states stay visible without hover
                bool pinned = dlState == GhostDownloadState.Downloading
                    || dlState == GhostDownloadState.Done
                    || dlState == GhostDownloadState.Failed;

                var ghostGO = MakeGO("LBGhost", row.transform);
                var ghostImg = ghostGO.AddComponent<Image>();
                // Always positioned at the shown spot — the button is
                // permanently visible (no hover dependency).
                Rect(ghostGO, ghostShownX, 0, ghostW, h);

                var ghostCg = ghostGO.AddComponent<CanvasGroup>();

                var ghostLbl = MakeLbl(ghostGO.transform, "",
                    UIStyle.FontSizeSm - 1, UIStyle.Accent, TextAnchor.MiddleCenter,
                    w: ghostW, h: h);

                // Click handler (Idle and Failed are actionable; the state
                // guard in OnGhostDownloadClicked covers in-place transitions)
                if (_networkClient != null && _networkClient.IsStarted
                    && dlState != GhostDownloadState.Full)
                {
                    string rid = entry.RunId;
                    string rname = entry.RunnerName ?? "";
                    bool isMe = entry.IsYou;
                    Btn(ghostGO, () => OnGhostDownloadClicked(rid, rname, isMe));
                    var ghostBtn = ghostGO.GetComponent<Button>();
                    if (ghostBtn != null)
                    {
                        ghostBtn.transition = Selectable.Transition.None;
                        ghostBtn.navigation = new Navigation
                        { mode = Navigation.Mode.None };
                    }
                }

                // Reveal component kept for the bg-pin semantics, but forced
                // permanently shown so the button can never get stuck hidden.
                var reveal = row.AddComponent<RowHoverReveal>();
                reveal.ghostCg = ghostCg;
                reveal.ghostRt = ghostGO.GetComponent<RectTransform>();
                reveal.shownX = ghostShownX;
                reveal.hiddenX = ghostHiddenX;
                reveal.alwaysShown = true;
                reveal.Init(startShown: true);

                // Register refs + paint the state
                var refs = new GhostBtnRefs
                {
                    go = ghostGO,
                    bg = ghostImg,
                    label = ghostLbl,
                    cg = ghostCg,
                    reveal = reveal,
                    roomKey = selectedScene != null
                        ? new RoomKey(selectedScene, route.EntryFrom, route.ExitTo)
                        : default,
                    entryTime = entry.TotalTime
                };
                _ghostBtnRefs[entry.RunId] = refs;
                SetGhostVisual(refs, dlState);
            }

            // ── Time ───────────────────────────────────────────────────

            MakeLbl(row.transform, TimeUtil.Format(entry.TotalTime),
                UIStyle.FontSizeSm,
                isWR ? UIStyle.Gold : UIStyle.Text,
                TextAnchor.MiddleRight, x: timeX, w: timeW, h: h);

            // ── Runner name (fills remaining space) ─────────────────────
            // Always shows the actual runner name; your own entry is
            // indicated by accent color + the row highlight instead of
            // replacing the name with "You".

            int nameEnd = hasDelta ? deltaX : timeX;
            if (showGhost)
                nameEnd = ghostShownX;
            int nameW = nameEnd - x - M / 2;

            string displayName = string.IsNullOrEmpty(entry.RunnerName)
                ? "???" : entry.RunnerName!;

            MakeLbl(row.transform, displayName,
                UIStyle.FontSizeSm - 1,
                isYou ? UIStyle.Accent : UIStyle.Text,
                TextAnchor.MiddleLeft, x: x, w: nameW, h: h);
        }

        private void AddLeaderboardGapRow(Transform parent, int gapCount, int yOffset)
        {
            var row = MakeGO("LBGap", parent);
            Img(row, Color.clear);
            Rect(row, 0, yOffset, RW, RH);

            string text = gapCount > 0
                ? "...  " + gapCount + " more runner"
                    + (gapCount != 1 ? "s" : "") + "  ..."
                : "...";

            MakeLbl(row.transform, text,
                UIStyle.FontSizeSm - 2, UIStyle.Subtext, TextAnchor.MiddleCenter,
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

            string text = isExpanded
                ? "\u25B4 Show less"
                : "\u25BE Show " + (entryCount - LeaderboardCollapsedCount) + " more";

            MakeLbl(row.transform, text,
                UIStyle.FontSizeSm - 2, UIStyle.Accent, TextAnchor.MiddleCenter,
                x: 0, w: RW, h: RH);
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

            if (activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();
        }

        /// <summary>
        /// Lightweight rebuild of just the leaderboard content area:
        /// detached clear (no one-frame layout glitch), no polling
        /// stop/restart, no config ref clearing, scroll preserved.
        /// </summary>
        private void RebuildLeaderboardContentOnly()
        {
            if (rightContent == null) return;

            var scroll = RightScroll;
            float keepScroll = scroll != null
                ? scroll.verticalNormalizedPosition : 1f;

            ClearContentDetached(rightContent);
            BuildLeaderboardContent();
            ForceLayout(rightContent);

            if (scroll != null)
                scroll.verticalNormalizedPosition = Mathf.Clamp01(keepScroll);
        }

        private static string FormatDelta(float delta)
        {
            if (delta <= 0f) return "";
            int cs = Mathf.RoundToInt(delta * 100f);
            int min = cs / 6000;
            int sec = (cs / 100) % 60;
            int rem = cs % 100;
            return min > 0
                ? string.Format("+{0}:{1:00}.{2:00}", min, sec, rem)
                : string.Format("+{0}.{1:00}", sec, rem);
        }

        // ── Capacity ───────────────────────────────────────────────────

        /// <summary>
        /// Whether a downloaded replay with the given time would survive
        /// import into this route's history. PBManager prunes each route
        /// to the best MaxSavedReplaysPerRoute by time, so a replay slower
        /// than the worst kept snapshot would be silently pruned when the
        /// route is at capacity.
        /// </summary>
        private static bool RouteHistoryHasCapacity(RoomKey key, float time)
        {
            int max = GhostSettings.MaxSavedReplaysPerRoute;
            var history = PBManager.GetHistory(key); // ordered best → worst
            if (history.Count < max) return true;
            return time < history[history.Count - 1].TotalTime;
        }

        // ── Ghost button visuals (in-place updates) ────────────────────

        /// <summary>
        /// Paints a ghost button for the given state. Used both at build
        /// time and for in-place updates during downloads — state changes
        /// never trigger a content rebuild.
        /// </summary>
        private void SetGhostVisual(GhostBtnRefs refs, GhostDownloadState state)
        {
            string label;
            Color color;
            int fontSize = UIStyle.FontSizeSm - 1;
            bool pinned = false;

            switch (state)
            {
                case GhostDownloadState.Downloading:
                    label = "...";
                    color = UIStyle.Subtext;
                    pinned = true;
                    break;
                case GhostDownloadState.Done:
                    label = "\u2713";
                    color = UIStyle.Green;
                    pinned = true;
                    break;
                case GhostDownloadState.Failed:
                    label = "\u2717";
                    color = UIStyle.Red;
                    pinned = true;
                    break;
                case GhostDownloadState.Full:
                    label = "full";
                    color = UIStyle.Overlay;
                    fontSize = UIStyle.FontSizeSm - 3;
                    break;
                default: // Idle
                    label = "\u2193";
                    color = UIStyle.Accent;
                    break;
            }

            refs.label.text = label;
            refs.label.color = color;
            refs.label.fontSize = fontSize;

            refs.bg.color = pinned
                ? UIStyle.Surface with { a = 0.5f }
                : Color.clear;

            if (refs.reveal != null)
                refs.reveal.pinned = pinned;

            if (pinned)
            {
                // Make sure it's interactive/visible immediately even if
                // the reveal animation is still catching up
                refs.cg.blocksRaycasts = true;
                refs.cg.interactable = true;
            }
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
                && !RouteHistoryHasCapacity(refs.roomKey, refs.entryTime))
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
        /// Reverts expired ✓ (Done) and ✗ (Failed) states back to Idle —
        /// Done so replays can be re-downloaded (e.g. after deleting the
        /// imported copy), Failed so errors don't stick forever. Updates
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
                TryUpdateGhostVisual(runId); // back to ↓ (or "full")
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
            if (!TryUpdateGhostVisual(runId) && activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();

            _networkClient.DownloadReplay(runId, replayData =>
            {
                if (string.IsNullOrEmpty(replayData))
                {
                    SetGhostFailed(runId, "download failed");
                    return;
                }

                RecordedRoom? room = ReplayShareEncoder.Decode(replayData!);
                if (room == null)
                {
                    SetGhostFailed(runId, "decode failed");
                    return;
                }

                // Capacity re-check with the actual decoded time. PBManager
                // prunes routes to the best N by time, so importing a replay
                // slower than the worst kept snapshot would silently vanish.
                if (!RouteHistoryHasCapacity(room.Key, room.TotalTime))
                {
                    _downloadStates.Remove(runId); // Full is derived, not stored
                    Log.LogInfo("[Leaderboard] Ghost not imported — route history " +
                        "is full (" + GhostSettings.MaxSavedReplaysPerRoute +
                        " max). Raise 'Max saved replays' in Config to keep " +
                        "slower replays.");
                    if (!TryUpdateGhostVisual(runId, GhostDownloadState.Full)
                        && activeTab == TabKind.Leaderboard)
                        RebuildLeaderboardContentOnly();
                    return;
                }

                bool imported = PBManager.ImportPB(room);
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
            if (!TryUpdateGhostVisual(runId) && activeTab == TabKind.Leaderboard)
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