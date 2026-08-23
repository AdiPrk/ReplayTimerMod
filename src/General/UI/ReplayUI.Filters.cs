using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Modifier filter (shared by the Runs and Leaderboard tabs) ─────
        //
        // The filter lives in a floating dropdown popup (a canvas-root panel
        // like the color picker, but NON-modal: no scrim, so the list, the
        // mod panel, and the game's own pause menu stay fully interactive
        // while it's open - outside clicks are detected in TickFilterPopup
        // and close it). It opens from a Filters toggle in the right
        // sub-header (left of the Export/Paste/Clear cluster on the Runs
        // tab, flush right on the Leaderboard tab). Because the popup is
        // NOT part of the scrolling content, opening it never reflows the
        // list, and filter changes only rebuild the rows underneath while
        // the popup persists.
        //
        // Inside the popup every ability is one tri-state chip that cycles
        // Any -> With -> Without on click, indicated by color alone (accent
        // = with, red = without, dim = any); the mutually-exclusive crest
        // bits are a single-select chip row. Chips are sized to their text
        // and flow left-to-right, wrapping within the popup width. A footer
        // shows the live result count and Reset. Filter state persists in
        // GhostSettings (ModifierRequireMask / ModifierExcludeMask); the
        // cycle logic is RouteView.CycleFilterBit (pure, unit-tested).

        private static int FilterRequire => GhostSettings.ModifierRequireMask;
        private static int FilterExclude => GhostSettings.ModifierExcludeMask;

        private static bool ModifierFilterActive =>
            RouteView.FilterActive(FilterRequire, FilterExclude);

        /// <summary>Whether a run with this mask passes the current filter.
        /// Unknown (pre-feature) masks pass only when no filter is active.</summary>
        private static bool PassesModifierFilter(int mask) =>
            RouteView.PassesFilter(mask, FilterRequire, FilterExclude);

        // ── Popup state ────────────────────────────────────────────────────

        private GameObject? _filterPopupGO;
        private GameObject? _filterToggleGO;   // sub-header toggle, rebuilt on demand
        private int _filterRunsRightEdge;      // toggle's right edge on the Runs tab
        private RectTransform? _filterToggleRT;
        private RectTransform? _filterCaretRT;
        private Text? _filterCountLbl;
        private Image? _filterResetBg;
        private Text? _filterResetLbl;
        private int _filterPopupW, _filterPopupH;

        // Live references so a state change restyles chips IN PLACE instead
        // of rebuilding the popup under the pointer.
        private sealed class FilterChipRef
        {
            public int bitMask;          // ability: 1<<Bit; crest "Any": 0
            public Image bg = null!;
            public Text label = null!;
        }

        private readonly List<FilterChipRef> abilityChips = new List<FilterChipRef>();
        private readonly List<FilterChipRef> crestChips = new List<FilterChipRef>();

        // Result counts for the popup footer, published by the tab builders
        // on every content (re)build so they are always current.
        private int _filterShownCount;
        private int _filterTotalCount;
        private string _filterCountUnit = "runs";

        // ── Sub-header toggle ──────────────────────────────────────────────

        /// <summary>
        /// (Re)builds the Filters toggle in the right sub-header: a button
        /// that shows how many modifiers are constrained ("Filters (2)"),
        /// lights up (accent) when any filter is set, shows the full
        /// selection as a hover tooltip, and opens the filter popup. The
        /// Runs/Leaderboard content builders call this on their filterable
        /// paths; every other view leaves it hidden (RebuildRightContent
        /// hides it before each build).
        /// </summary>
        private void ShowFilterToggle()
        {
            if (_rightSubHeader == null) return;
            HideFilterToggle();
            SanitizeCrestFilterBits();

            bool active = ModifierFilterActive;
            bool open = _filterPopupGO != null && _filterPopupGO.activeSelf;
            int count = ActiveFilterCount();

            int btnH = UIStyle.H(20);
            int btnY = (UIStyle.SubHeaderHeight - btnH) / 2;

            // The button hugs its content: caret + gap + measured text + pad.
            string toggleText = count > 0 ? "Filters (" + count + ")" : "Filters";
            int caretS = UIStyle.H(8);
            int caretX = UIStyle.W(8);
            int textW = Mathf.CeilToInt(
                MeasureTextWidth(toggleText, UIStyle.FontSizeBtn));
            int textX = caretX + caretS + UIStyle.W(5);
            int toggleW = textX + textW + UIStyle.W(9);

            // Flush right on the Leaderboard tab; left of the
            // Export/Paste/Clear cluster on the Runs tab.
            int rightEdge = _activeTab == TabKind.Runs
                ? _filterRunsRightEdge
                : RW - M;

            var toggle = MakeGO("FilterToggle", _rightSubHeader.transform);
            _filterToggleGO = toggle;
            Img(toggle, active ? UIStyle.BtnBgStrong(UIStyle.Accent) : Color.clear);
            Rect(toggle, rightEdge - toggleW, btnY, toggleW, btnH);
            // TickFilterPopup exempts the toggle from outside-click closing
            // (its own click handler toggles the popup).
            _filterToggleRT = toggle.GetComponent<RectTransform>();

            // Drawn caret: right = closed, down = popup open.
            _filterCaretRT = AddCaret(toggle.transform,
                caretX + caretS / 2f, btnH / 2f, caretS,
                active ? UIStyle.Accent : UIStyle.Subtext,
                open ? -90f : 0f);

            MakeLbl(toggle.transform, toggleText, UIStyle.FontSizeBtn,
                active ? UIStyle.Accent : UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: textX, w: textW + UIStyle.W(2), h: btnH);

            var toggleGO = toggle;
            Btn(toggle, () => ToggleFilterPopup(toggleGO));
            AddButtonHover(toggle);

            if (active)
                AttachTooltip(toggle, BuildFilterSummary());

            // The transient paste status text right-aligns against whatever
            // is leftmost in the Runs cluster - with the toggle shown, that
            // is this toggle.
            if (_activeTab == TabKind.Runs)
                PositionPasteStatus(rightEdge - toggleW - M);
        }

        /// <summary>Removes the sub-header Filters toggle (idempotent). The
        /// popup is left alone: content rebuilds triggered by chip clicks
        /// hide-then-show the toggle, and the popup must survive those.
        /// Views that genuinely lose the toggle close the popup via the
        /// existing SwitchTab/SelectScene/TogglePanel hooks.</summary>
        private void HideFilterToggle()
        {
            if (_filterToggleGO != null)
            {
                // Detach before the (deferred) Destroy so the replacement
                // toggle never overlaps a destroyed-but-pending one for a
                // frame (same trick as ClearContentDetached).
                _filterToggleGO.transform.SetParent(null, false);
                Object.Destroy(_filterToggleGO);
                _filterToggleGO = null;
                _filterToggleRT = null;
                _filterCaretRT = null;
            }
            PositionPasteStatus(_filterRunsRightEdge);
        }

        /// <summary>Right-aligns the paste status label so it ends at
        /// <paramref name="rightEdge"/> (sub-header x). Must match the width
        /// the label was created with (see PasteStatusWidth).</summary>
        private void PositionPasteStatus(int rightEdge)
        {
            if (_pasteStatusLbl == null) return;
            var rt = _pasteStatusLbl.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(
                rightEdge - PasteStatusWidth, rt.anchoredPosition.y);
        }

        /// <summary>Paste-status label width - shared by the creation site
        /// (ReplayUI.Build) and PositionPasteStatus so the right edge can't
        /// silently drift when one of them changes.</summary>
        private static int PasteStatusWidth => UIStyle.W(100);

        /// <summary>Number of constrained attributes: each required or
        /// excluded ability counts once, the crest choice counts once.</summary>
        private static int ActiveFilterCount()
        {
            int n = CountBits(FilterRequire & ~ModifierRegistry.CrestBitsMask)
                  + CountBits(FilterExclude);
            if ((FilterRequire & ModifierRegistry.CrestBitsMask) != 0) n++;
            return n;
        }

        private static int CountBits(int v)
        {
            int c = 0;
            while (v != 0) { c++; v &= v - 1; }
            return c;
        }

        /// <summary>Plain-text description of the active filter, e.g.
        /// "with Swift Step | without Cling Grip | Hunter Crest" - shown as
        /// the Filters button's hover tooltip.</summary>
        private static string BuildFilterSummary()
        {
            var sb = new System.Text.StringBuilder();
            AppendFilterNames(sb, "with ",
                FilterRequire & ~ModifierRegistry.CrestBitsMask);
            AppendFilterNames(sb, "without ", FilterExclude);

            foreach (var def in ModifierRegistry.All)
            {
                if (!def.IsCrest) continue;
                if ((FilterRequire & (1 << def.Bit)) == 0) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(def.DisplayName);
                break;
            }

            return sb.ToString();
        }

        private static void AppendFilterNames(System.Text.StringBuilder sb,
            string label, int mask)
        {
            if (mask == 0) return;
            if (sb.Length > 0) sb.Append(" | ");
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

        // ── Popup open / close ─────────────────────────────────────────────

        private void ToggleFilterPopup(GameObject anchor)
        {
            if (_filterPopupGO != null && _filterPopupGO.activeSelf)
                CloseFilterPopup();
            else
                OpenFilterPopup(anchor);
        }

        private void OpenFilterPopup(GameObject anchor)
        {
            EnsureFilterPopup();
            if (_filterPopupGO == null) return;

            RefreshFilterPopup();

            // Keep it on top of everything else on the canvas
            _filterPopupGO.transform.SetAsLastSibling();
            _filterPopupGO.SetActive(true);

            // Drop down from the toggle button with right edges aligned
            // (the toggle sits at the right side of the sub-header), clamped
            // on-screen. Same screen-position convention as the color picker.
            var art = anchor.GetComponent<RectTransform>();
            Vector3 p = art.position; // pivot (top-left) in screen px
            float x = p.x + art.rect.width - _filterPopupW;
            x = Mathf.Clamp(x, 4, Screen.width - _filterPopupW - 4);
            float yTop = p.y - art.rect.height - UIStyle.H(4);
            yTop = Mathf.Clamp(yTop, _filterPopupH + 4, Screen.height - 4);
            _filterPopupGO.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(x, yTop);

            if (_filterCaretRT != null)
                _filterCaretRT.localEulerAngles = new Vector3(0, 0, -90);
        }

        /// <summary>Closes the filter popup if open. Safe to call any time;
        /// hooked into panel close, tab switches, scene selection, and
        /// unpause (alongside ClosePicker).</summary>
        private void CloseFilterPopup()
        {
            if (_filterPopupGO != null) _filterPopupGO.SetActive(false);
            if (_filterCaretRT != null)
                _filterCaretRT.localEulerAngles = Vector3.zero;
        }

        private void RefreshFilterPopupIfOpen()
        {
            if (_filterPopupGO != null && _filterPopupGO.activeSelf)
                RefreshFilterPopup();
        }

        /// <summary>Called every frame from Tick. The popup is non-modal
        /// (everything behind it stays interactive, including the game's
        /// own pause menu), so outside clicks are detected here: a
        /// left-click that lands neither on the popup nor on the Filters
        /// toggle closes it. The click itself still goes through to
        /// whatever was clicked.</summary>
        private void TickFilterPopup()
        {
            if (_filterPopupGO == null || !_filterPopupGO.activeSelf) return;
            if (!Input.GetMouseButtonDown(0)) return;

            Vector2 mouse = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)_filterPopupGO.transform, mouse))
                return;
            if (_filterToggleRT != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    _filterToggleRT, mouse))
                return; // the toggle's own click handler closes it

            CloseFilterPopup();
        }

        // ── Popup interaction ──────────────────────────────────────────────

        private void OnAbilityChipClicked(int bitMask)
        {
            int require = FilterRequire;
            int exclude = FilterExclude;
            RouteView.CycleFilterBit(bitMask, ref require, ref exclude);
            GhostSettings.ModifierRequireMask = require;
            GhostSettings.ModifierExcludeMask = exclude;
            RebuildActiveTabContentOnly(); // also refreshes the open popup
        }

        private void OnCrestChipClicked(int bitMask)
        {
            GhostSettings.ModifierRequireMask = RouteView.SelectCrestBit(
                FilterRequire, ModifierRegistry.CrestBitsMask, bitMask);
            RebuildActiveTabContentOnly();
        }

        private void OnFilterResetClicked()
        {
            if (!ModifierFilterActive) return;
            GhostSettings.ModifierRequireMask = 0;
            GhostSettings.ModifierExcludeMask = 0;
            RebuildActiveTabContentOnly();
        }

        // ── Popup construction ─────────────────────────────────────────────

        private void EnsureFilterPopup()
        {
            if (_filterPopupGO != null) return;
            if (_canvasGO == null) return;

            abilityChips.Clear();
            crestChips.Clear();

            _filterPopupGO = MakeGO("FilterPopup", _canvasGO.transform);

            // 1px frame, same construction as the tooltip / color picker
            var borderImg = _filterPopupGO.AddComponent<Image>();
            borderImg.color = UIStyle.Overlay with { a = 0.9f };

            var rt = _filterPopupGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero; // bottom-left anchored
            rt.pivot = new Vector2(0f, 1f);             // position = top-left

            var inner = MakeGO("Inner", _filterPopupGO.transform);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = UIStyle.Base with { a = 0.98f };
            var innerRt = inner.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(1, 1);
            innerRt.offsetMax = new Vector2(-1, -1);

            int pad = UIStyle.W(12);
            int lblH = UIStyle.H(14);
            int pw = UIStyle.W(320);
            int y = UIStyle.H(10);

            // Legend doubles as the title: the tri-state cycle isn't
            // discoverable without it.
            MakeLbl(_filterPopupGO.transform,
                "Abilities",
                UIStyle.FontSizeTiny, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: pad, y: y, w: pw - pad * 2, h: lblH);
            y += lblH + UIStyle.H(6);

            var flow = new ChipFlow(pad, pw - pad, y);
            foreach (var def in NonCrestDefs())
            {
                int bitMask = 1 << def.Bit;
                abilityChips.Add(AddFilterChip(_filterPopupGO.transform,
                    "Chip_" + def.Id, def.DisplayName, bitMask, flow,
                    () => OnAbilityChipClicked(bitMask)));
            }
            y = flow.End + UIStyle.H(10);

            var crests = CrestDefs();
            if (crests.Count > 0)
            {
                MakeLbl(_filterPopupGO.transform, "Crest",
                    UIStyle.FontSizeTiny, UIStyle.Subtext, TextAnchor.MiddleLeft,
                    x: pad, y: y, w: pw - pad * 2, h: lblH);
                y += lblH + UIStyle.H(6);

                flow = new ChipFlow(pad, pw - pad, y);
                crestChips.Add(AddFilterChip(_filterPopupGO.transform,
                    "Crest_any", "Any crest", 0, flow,
                    () => OnCrestChipClicked(0)));
                foreach (var def in crests)
                {
                    int bitMask = 1 << def.Bit;
                    crestChips.Add(AddFilterChip(_filterPopupGO.transform,
                        "Crest_" + def.Id, def.DisplayName, bitMask, flow,
                        () => OnCrestChipClicked(bitMask)));
                }
                y = flow.End + UIStyle.H(10);
            }

            HLine(_filterPopupGO.transform, pad, y, pw - pad * 2);
            y += UIStyle.H(8);

            int footH = UIStyle.H(20);
            int resetW = UIStyle.W(52);
            _filterCountLbl = MakeLbl(_filterPopupGO.transform, "",
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: pad, y: y, w: pw - pad * 2 - resetW - UIStyle.Gap, h: footH);
            var resetRef = MakeButton(_filterPopupGO.transform, "FilterReset",
                "Reset", UIStyle.FontSizeBtn, UIStyle.Red,
                UIStyle.BtnBg(UIStyle.Red),
                pw - pad - resetW, y, resetW, footH, OnFilterResetClicked);
            _filterResetBg = resetRef.bg;
            _filterResetLbl = resetRef.label;
            y += footH + UIStyle.H(10);

            _filterPopupW = pw;
            _filterPopupH = y;
            rt.sizeDelta = new Vector2(pw, y);

            _filterPopupGO.SetActive(false);
        }

        /// <summary>Left-to-right chip placement that wraps within
        /// [left, right]. Chips are sized to their text by AddFilterChip;
        /// this only hands out positions.</summary>
        private sealed class ChipFlow
        {
            public static int ChipH => UIStyle.H(22);

            private readonly int left;
            private readonly int right;
            private int x;
            private int y;

            public ChipFlow(int left, int right, int top)
            {
                this.left = left;
                this.right = right;
                x = left;
                y = top;
            }

            /// <summary>Reserves a slot of the given width, wrapping to the
            /// next row first when it wouldn't fit. Row spacing equals the
            /// horizontal chip spacing (UIStyle.Gap both ways).</summary>
            public void Place(int w, out int px, out int py)
            {
                if (x > left && x + w > right)
                {
                    x = left;
                    y += ChipH + UIStyle.Gap;
                }
                px = x;
                py = y;
                x += w + UIStyle.Gap;
            }

            /// <summary>Bottom edge of the flowed content.</summary>
            public int End => y + ChipH;
        }

        /// <summary>One chip, sized to its centered text and placed by the
        /// flow. State is shown by color alone; styling is applied by
        /// RefreshFilterPopup.</summary>
        private FilterChipRef AddFilterChip(Transform parent, string name,
            string text, int bitMask, ChipFlow flow,
            UnityEngine.Events.UnityAction onClick)
        {
            int padX = UIStyle.W(9);
            int w = Mathf.CeilToInt(MeasureTextWidth(text, UIStyle.FontSizeRow))
                + padX * 2;
            flow.Place(w, out int x, out int y);

            var go = MakeGO(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = UIStyle.Surface with { a = 0.5f };
            Btn(go, onClick);
            Rect(go, x, y, w, ChipFlow.ChipH);

            var lbl = MakeLbl(go.transform, text,
                UIStyle.FontSizeRow, UIStyle.Subtext, TextAnchor.MiddleCenter,
                fill: true);

            AddButtonHover(go);

            return new FilterChipRef { bitMask = bitMask, bg = bg, label = lbl };
        }

        // ── Text measurement ───────────────────────────────────────────────

        private Text? _measureLbl;

        /// <summary>
        /// Width of rendered text in canvas px (the canvas uses
        /// ConstantPixelSize, so preferredWidth is directly usable), via a
        /// hidden reusable Text. Used to size chips and the Filters toggle
        /// to their content.
        /// </summary>
        private float MeasureTextWidth(string text, int fontSize)
        {
            if (_measureLbl == null)
            {
                // Kept active with clear color: preferred-size queries are
                // safest on an active Text across Unity versions, and a
                // fully transparent zero-size label renders nothing.
                var go = MakeGO("MeasureLbl", _canvasGO.transform);
                _measureLbl = go.AddComponent<Text>();
                _measureLbl.font = UIStyle.Arial;
                _measureLbl.color = Color.clear;
                _measureLbl.raycastTarget = false;
                _measureLbl.supportRichText = false;
                _measureLbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                _measureLbl.verticalOverflow = VerticalWrapMode.Overflow;
                Rect(go, 0, 0, 0, 0);
            }

            _measureLbl.fontSize = fontSize;
            _measureLbl.text = text;
            return _measureLbl.preferredWidth;
        }

        /// <summary>Restyles every chip and the footer from the current
        /// filter state and result counts - in place, no rebuild.</summary>
        private void RefreshFilterPopup()
        {
            if (_filterPopupGO == null) return;

            foreach (var chip in abilityChips)
                StyleFilterChip(chip,
                    (FilterRequire & chip.bitMask) != 0,
                    (FilterExclude & chip.bitMask) != 0);

            int crestSel = FilterRequire & ModifierRegistry.CrestBitsMask;
            foreach (var chip in crestChips)
                StyleFilterChip(chip,
                    chip.bitMask == 0 ? crestSel == 0
                                      : (crestSel & chip.bitMask) != 0,
                    without: false);

            bool active = ModifierFilterActive;
            if (_filterCountLbl != null)
                _filterCountLbl.text = active
                    ? _filterShownCount + " of " + _filterTotalCount + " "
                        + _filterCountUnit + " shown"
                    : _filterTotalCount + " " + _filterCountUnit;
            if (_filterResetLbl != null)
                _filterResetLbl.color = active ? UIStyle.Red : UIStyle.Subtext;
            if (_filterResetBg != null)
                _filterResetBg.color = active
                    ? UIStyle.BtnBg(UIStyle.Red)
                    : UIStyle.Surface with { a = 0.5f };
        }

        /// <summary>Chip state is color alone: accent = with, red = without,
        /// dim surface = any.</summary>
        private static void StyleFilterChip(FilterChipRef chip,
            bool with, bool without)
        {
            if (with)
            {
                chip.bg.color = UIStyle.BtnBgStrong(UIStyle.Accent);
                chip.label.color = UIStyle.Accent;
            }
            else if (without)
            {
                chip.bg.color = UIStyle.BtnBgStrong(UIStyle.Red);
                chip.label.color = UIStyle.Red;
            }
            else
            {
                chip.bg.color = UIStyle.Surface with { a = 0.5f };
                chip.label.color = UIStyle.Subtext;
            }
        }

        // ── Registry helpers ───────────────────────────────────────────────

        /// <summary>Crest bits are single-choice via the chip row; drop any
        /// stale multi-crest/exclude state (e.g. from older versions). The
        /// rules live in RouteView (unit tested); this wrapper only persists
        /// what actually changed.</summary>
        private static void SanitizeCrestFilterBits()
        {
            int require = FilterRequire;
            int exclude = FilterExclude;
            RouteView.SanitizeCrestBits(ModifierRegistry.CrestBitsMask,
                ref require, ref exclude);

            if (exclude != FilterExclude)
                GhostSettings.ModifierExcludeMask = exclude;
            if (require != FilterRequire)
                GhostSettings.ModifierRequireMask = require;
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
                UIStyle.FontSizeTiny, UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);

            AttachModifierTooltip(marker, mask);
            return markerW;
        }

        /// <summary>Rebuilds just the active tab's content area (lightweight,
        /// scroll preserved) after a filter change, then syncs the open
        /// filter popup with the fresh state and result counts.</summary>
        private void RebuildActiveTabContentOnly()
        {
            if (_activeTab == TabKind.Leaderboard)
                RebuildLeaderboardContentOnly();
            else if (_activeTab == TabKind.Runs)
                RebuildRunsContentOnly();
        }

        /// <summary>
        /// Lightweight rebuild of just the Runs content area — the Runs-tab
        /// counterpart of <see cref="RebuildLeaderboardContentOnly"/>:
        /// detached clear, no polling changes, scroll preserved.
        /// </summary>
        private void RebuildRunsContentOnly()
        {
            if (_rightContent == null || _selectedScene == null) return;

            var scroll = RightScroll;
            float keepScroll = scroll != null ? ScrollOffsetFromTop(scroll) : 0f;

            ClearContentDetached(_rightContent);
            BuildRunsContent(_selectedScene);
            ForceLayout(_rightContent);

            if (scroll != null)
                RestoreScrollOffsetFromTop(scroll, keepScroll);

            RefreshFilterPopupIfOpen();
        }

        // ── Leaderboard view ranking ───────────────────────────────────────

        /// <summary>
        /// Builds the display view of a route's leaderboard from the cached
        /// best-per-(runner, mask) rows. The logic lives in
        /// <see cref="RouteView.Build"/> (pure, unit-tested); this wrapper
        /// supplies the persisted filter masks.
        /// </summary>
        private static List<LeaderboardEntry> BuildRouteView(
            RouteLeaderboard route,
            out int yourViewRank, out LeaderboardEntry? yourRow) =>
            RouteView.Build(route, FilterRequire, FilterExclude,
                out yourViewRank, out yourRow);
    }
}
