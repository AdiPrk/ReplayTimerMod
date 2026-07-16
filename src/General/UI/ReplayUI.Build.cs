using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void BuildTab()
        {
            tabGO = MakeGO("ReplayTab", canvasGO!.transform);
            Img(tabGO, UIStyle.Surface);
            Btn(tabGO, TogglePanel);

            var rt = tabGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M);
            rt.sizeDelta = new Vector2(UIStyle.TabBtnWidth, UIStyle.TabBtnHeight);

            MakeLbl(tabGO.transform, "Replays", UIStyle.FontSizeSm,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            AddButtonHover(tabGO);
        }

        private void BuildPanel()
        {
            panelGO = MakeGO("ReplayPanel", canvasGO!.transform);
            Img(panelGO, UIStyle.Base);

            var rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M + UIStyle.TabBtnHeight + M);
            rt.sizeDelta = new Vector2(PW, PH);

            int HDR = UIStyle.HeaderHeight;
            int SRCH = UIStyle.SearchBarHeight;
            int TABH = UIStyle.TabBarHeight;
            int SUBH = UIStyle.SubHeaderHeight;
            int FOOT = UIStyle.FooterHeight;

            BuildPanelHeader(HDR);
            HLine(panelGO.transform, 0, HDR, PW);

            int bodyY = HDR + 1;

            // Left panel
            int searchY = bodyY;
            int footerY = PH - FOOT;
            int sceneListY = searchY + SRCH + 1;
            int sceneListH = footerY - 1 - sceneListY;

            BuildSearchBar(searchY, SRCH);
            HLine(panelGO.transform, 0, sceneListY - 1, LW);

            sceneListContent = BuildScrollArea(panelGO.transform, "SceneListScroll",
                0, sceneListY, LW, sceneListH);
            sceneListScroll = sceneListContent.parent.parent.GetComponent<ScrollRect>();

            HLine(panelGO.transform, 0, footerY - 1, LW);
            BuildLeftFooter(footerY, FOOT);

            // Vertical divider
            VLine(panelGO.transform, LW, bodyY, PH - bodyY);

            // Right panel
            int tabBarY = bodyY;
            int rightSubY = tabBarY + TABH + 1;
            int rightContentY = rightSubY + SUBH + 1;
            int rightContentH = PH - rightContentY;

            BuildTabBar(tabBarY, TABH);
            HLine(panelGO.transform, LW + 1, tabBarY + TABH, RW);
            BuildRightSubHeader(rightSubY, SUBH);
            HLine(panelGO.transform, LW + 1, rightContentY - 1, RW);

            rightContent = BuildScrollArea(panelGO.transform, "RightContentScroll",
                LW + 1, rightContentY, RW, rightContentH);
        }

        private void BuildPanelHeader(int height)
        {
            var hdr = MakeGO("Header", panelGO!.transform);
            Img(hdr, UIStyle.Surface);
            Rect(hdr, 0, 0, PW, height);

            int btnW = UIStyle.W(48);
            int btnH = UIStyle.H(20);
            var collapse = MakeGO("Collapse", hdr.transform);
            Img(collapse, UIStyle.Overlay with { a = 0.6f });
            Btn(collapse, TogglePanel);
            Rect(collapse, PW - btnW - M, (height - btnH) / 2, btnW, btnH);
            MakeLbl(collapse.transform, "Hide", UIStyle.FontSizeBtn,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            AddButtonHover(collapse);

            MakeLbl(hdr.transform, "Replay Timer", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: PW - btnW - M * 3, h: height);
        }

        private void BuildSearchBar(int y, int h)
        {
            MakeSearchInput(panelGO!.transform, "Filter rooms...",
                0, y, LW, h, OnSearchChanged);
        }

        private void OnSearchChanged(string value)
        {
            searchFilter = value ?? "";
            RebuildSceneList();
        }

        private void BuildLeftFooter(int y, int h)
        {
            var footer = MakeGO("LeftFooter", panelGO!.transform);
            Img(footer, UIStyle.Surface);
            Rect(footer, 0, y, LW, h);

            int btnH = UIStyle.H(18);
            int btnY = (h - btnH) / 2;
            int gap = UIStyle.Gap;
            int btnW = (LW - M * 2 - gap) / 2;

            var curRef = MakeButton(footer.transform, "JumpCurrent", "Current",
                UIStyle.FontSizeBtn, UIStyle.Gold, UIStyle.BtnBg(UIStyle.Gold),
                M, btnY, btnW, btnH, OnJumpToCurrentClicked);
            jumpCurrentBg = curRef.bg;
            jumpCurrentLbl = curRef.label;

            var prevRef = MakeButton(footer.transform, "JumpPrevious", "Previous",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                M + btnW + gap, btnY, btnW, btnH, OnJumpToLastClicked);
            jumpPreviousBg = prevRef.bg;
            jumpPreviousLbl = prevRef.label;
        }

        private void BuildTabBar(int y, int h)
        {
            var bar = MakeGO("TabBar", panelGO!.transform);
            Img(bar, UIStyle.Surface);
            Rect(bar, LW + 1, y, RW, h);

            int btnW = UIStyle.W(80);
            int x = 0;

            tabButtons.Clear();
            tabButtons[TabKind.Runs] = AddTabButton(bar.transform, "Runs", x, h, btnW,
                () => SwitchTab(TabKind.Runs));
            x += btnW;

            tabButtons[TabKind.Leaderboard] = AddTabButton(bar.transform, "Leaderboard", x, h, UIStyle.W(96),
                () => SwitchTab(TabKind.Leaderboard));
            x += UIStyle.W(96);

            tabButtons[TabKind.Config] = AddTabButton(bar.transform, "Config", x, h, btnW,
                () => SwitchTab(TabKind.Config));
        }

        private static ButtonRef AddTabButton(
            Transform parent, string text, int x, int h, int w,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = MakeGO("Tab_" + text, parent);
            var bg = go.AddComponent<Image>();
            bg.color = Color.clear;
            go.AddComponent<Button>().onClick.AddListener(onClick);
            Rect(go, x, 0, w, h);

            var lbl = MakeLbl(go.transform, text, UIStyle.FontSizeRow,
                UIStyle.Subtext, TextAnchor.MiddleCenter, fill: true);
            AddButtonHover(go);

            ButtonRef r;
            r.bg = bg;
            r.label = lbl;
            return r;
        }

        private void BuildRightSubHeader(int y, int h)
        {
            var hdr = MakeGO("RightSubHeader", panelGO!.transform);
            Img(hdr, UIStyle.Surface);
            Rect(hdr, LW + 1, y, RW, h);
            rightSubHeader = hdr;

            int btnH = UIStyle.H(20);
            int btnY = (h - btnH) / 2;

            runsActionButtons = MakeGO("RunsActions", hdr.transform);
            Fill(runsActionButtons);

            // Right-packed action cluster, each button sized to its text
            // (same fit-the-content idiom as the filter chips). Clear is
            // sized to also fit its "Sure?" confirm state so the button
            // doesn't jump when armed.
            int padX = UIStyle.W(9);
            int clearW = Mathf.CeilToInt(Mathf.Max(
                MeasureTextWidth("Clear", UIStyle.FontSizeBtn),
                MeasureTextWidth("Sure?", UIStyle.FontSizeBtn))) + padX * 2;
            int pasteW = Mathf.CeilToInt(
                MeasureTextWidth("Paste", UIStyle.FontSizeBtn)) + padX * 2;
            int expW = Mathf.CeilToInt(
                MeasureTextWidth("Export", UIStyle.FontSizeBtn)) + padX * 2;

            int clearX = RW - clearW - M;
            var clearRef = MakeButton(runsActionButtons.transform, "ClearScene", "Clear",
                UIStyle.FontSizeBtn, UIStyle.Red, UIStyle.BtnBgStrong(UIStyle.Red),
                clearX, btnY, clearW, btnH, OnClearSceneClicked);
            sceneClearLbl = clearRef.label;
            sceneClearBg = clearRef.bg;

            int pasteX = clearX - M - pasteW;
            MakeButton(runsActionButtons.transform, "Paste", "Paste",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                pasteX, btnY, pasteW, btnH, OnPasteClicked);

            int expX = pasteX - M - expW;
            MakeButton(runsActionButtons.transform, "ExportScene", "Export",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                expX, btnY, expW, btnH, OnExportSceneClicked);

            // The Filters toggle (built on demand by ShowFilterToggle) packs
            // against the left of the cluster on the Runs tab.
            filterRunsRightEdge = expX - M;

            int statusW = UIStyle.W(100);
            pasteStatusLbl = MakeLbl(runsActionButtons.transform, "",
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleRight,
                x: filterRunsRightEdge - statusW, w: statusW, h: h);

            rightHeaderLbl = MakeLbl(hdr.transform, "Select a room",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M, w: RW / 2, h: h);
        }

        private static Transform BuildScrollArea(Transform parent, string name,
            float x, float y, float w, float h)
        {
            var sr = MakeGO(name, parent);
            Img(sr, Color.clear);
            Rect(sr, x, y, w, h);

            var vp = MakeGO("Viewport", sr.transform);
            vp.AddComponent<RectMask2D>();
            var vpRT = vp.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;

            var ct = MakeGO("Content", vp.transform);
            var ctRT = ct.GetComponent<RectTransform>();
            ctRT.anchorMin = new Vector2(0, 1);
            ctRT.anchorMax = new Vector2(1, 1);
            ctRT.pivot = new Vector2(0.5f, 1f);
            ctRT.offsetMin = ctRT.offsetMax = Vector2.zero;
            ctRT.sizeDelta = Vector2.zero;

            var csf = ct.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var vlg = ct.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1;

            var scroll = sr.AddComponent<ScrollRect>();
            scroll.content = ctRT;
            scroll.viewport = vpRT;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;

            return ct.transform;
        }
    }
}