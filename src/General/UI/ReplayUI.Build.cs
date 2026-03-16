using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        // ── Tab button (bottom-left ≡) ────────────────────────────────────────
        private void BuildTab()
        {
            tabGO = MakeGO("ReplayTab", canvasGO!.transform);
            Img(tabGO, UIStyle.Surface);
            Btn(tabGO, TogglePanel);

            var rt = tabGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M);
            rt.sizeDelta = new Vector2(TW, TH);

            MakeLbl(tabGO.transform, "≡", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
        }

        // ── Panel ─────────────────────────────────────────────────────────────
        private void BuildPanel()
        {
            panelGO = MakeGO("ReplayPanel", canvasGO!.transform);
            Img(panelGO, UIStyle.Base);

            var rt = panelGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(M, M + TH + M);
            rt.sizeDelta = new Vector2(PW, PH);

            BuildPanelHeader();

            int bodyY = HDR + 1;
            int bodyH = PH - bodyY - 1 - STGSH;
            int scrollBodyH = bodyH - SUBHDR - 1;

            // Both columns now have matching sub-headers. A single HLine at
            // bodyY + SUBHDR spans the full panel width, visually unifying them.
            BuildLeftSubHeader(bodyY);
            BuildRightSubHeader(bodyY);
            HLine(panelGO.transform, 0, bodyY + SUBHDR, PW);

            VLine(panelGO.transform, LW, bodyY, bodyH);

            // Left scroll area starts below its sub-header.
            leftContent = BuildScrollArea(panelGO.transform, "LeftScroll",
                0, bodyY + SUBHDR + 1, LW, scrollBodyH);

            // Grab the ScrollRect from the grandparent of Content:
            //   BuildScrollArea returns Content; hierarchy is sr > Viewport > Content.
            leftScrollRect = leftContent.parent.parent.GetComponent<ScrollRect>();

            rightContent = BuildScrollArea(panelGO.transform, "RightScroll",
                LW + 1, bodyY + SUBHDR + 1, RW, scrollBodyH);

            HLine(panelGO.transform, 0, PH - STGSH - 1, PW);
            BuildSettingsBar();
        }

        // ── Panel header: "Replay Times"  [Export all]  [Clear all]  [-] ──────
        private void BuildPanelHeader()
        {
            var hdr = MakeGO("Header", panelGO!.transform);
            Img(hdr, UIStyle.Surface);
            Rect(hdr, 0, 0, PW, HDR);
            HLine(panelGO.transform, 0, HDR, PW);

            // [-] collapse button - far right
            var collBtn = MakeGO("Collapse", hdr.transform);
            Img(collBtn, UIStyle.Overlay);
            Btn(collBtn, TogglePanel);
            Rect(collBtn, PW - HDR, 0, HDR, HDR);
            MakeLbl(collBtn.transform, "-", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);

            int btnH = UIStyle.H(22);
            int btnY = (HDR - btnH) / 2;

            // [Clear all] - left of collapse
            int clearW = UIStyle.W(76);
            int clearX = PW - HDR - M - clearW;
            var clearGO = MakeGO("ClearAll", hdr.transform);
            Rect(clearGO, clearX, btnY, clearW, btnH);
            clearAllBtnImg = clearGO.AddComponent<Image>();
            clearAllBtnImg.color = UIStyle.Red with { a = 0.22f };
            clearGO.AddComponent<Button>().onClick.AddListener(OnClearAllClicked);
            clearAllBtnLbl = MakeLbl(clearGO.transform, "Clear all",
                UIStyle.FontSizeSm - 2, UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            // [Export all] - copies to clipboard
            int exportW = UIStyle.W(76);
            int exportX = clearX - M - exportW;
            var exportGO = MakeGO("ExportAll", hdr.transform);
            Rect(exportGO, exportX, btnY, exportW, btnH);
            exportAllBtnImg = exportGO.AddComponent<Image>();
            exportAllBtnImg.color = UIStyle.Accent with { a = 0.22f };
            exportGO.AddComponent<Button>().onClick.AddListener(OnExportAllClicked);
            exportAllBtnLbl = MakeLbl(exportGO.transform, "Copy all",
                UIStyle.FontSizeSm - 2, UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // [Download all] - saves to disk
            int dlW = UIStyle.W(82);
            int dlX = exportX - M - dlW;
            var dlGO = MakeGO("DownloadAll", hdr.transform);
            Rect(dlGO, dlX, btnY, dlW, btnH);
            downloadAllBtnImg = dlGO.AddComponent<Image>();
            downloadAllBtnImg.color = UIStyle.Accent with { a = 0.15f };
            dlGO.AddComponent<Button>().onClick.AddListener(OnDownloadAllClicked);
            downloadAllBtnLbl = MakeLbl(dlGO.transform, "Download all",
                UIStyle.FontSizeSm - 2, UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // [Open Folder] - small button to jump to the directory
            int openW = UIStyle.W(82);
            int openX = dlX - M - openW;
            var openGO = MakeGO("OpenFolder", hdr.transform);
            Rect(openGO, openX, btnY, openW, btnH);
            Img(openGO, UIStyle.Overlay);
            openGO.AddComponent<Button>().onClick.AddListener(OnOpenExportFolderClicked);
            MakeLbl(openGO.transform, "Open Exports", UIStyle.FontSizeSm - 2,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);

            // Title - fills remaining left space
            MakeLbl(hdr.transform, "Replay Times", UIStyle.FontSizeLg,
                UIStyle.Text, TextAnchor.MiddleLeft,
                x: M, w: openX - M - M, h: HDR);
        }

        // ── Left sub-header: [● Go to current room] ──────────────────────────
        private void BuildLeftSubHeader(int bodyY)
        {
            var lhdr = MakeGO("LeftSubHeader", panelGO!.transform);
            Img(lhdr, UIStyle.Surface);
            Rect(lhdr, 0, bodyY, LW, SUBHDR);

            int btnH = UIStyle.H(20);
            int btnY = (SUBHDR - btnH) / 2;

            var jumpBtn = MakeGO("JumpToCurrent", lhdr.transform);
            jumpToCurrentBtnImg = jumpBtn.AddComponent<Image>();
            jumpToCurrentBtnImg.color = UIStyle.Gold with { a = 0.18f };
            jumpBtn.AddComponent<Button>().onClick.AddListener(OnJumpToCurrentClicked);
            Rect(jumpBtn, M, btnY, LW - M * 2, btnH);
            jumpToCurrentBtnLbl = MakeLbl(jumpBtn.transform,
                "● Go to current room", UIStyle.FontSizeSm - 2,
                UIStyle.Gold, TextAnchor.MiddleCenter, fill: true);
        }

        // ── Right sub-header: scene name | [Export scene] [Paste] [Clear scene]
        private void BuildRightSubHeader(int bodyY)
        {
            var rhdr = MakeGO("RightSubHeader", panelGO!.transform);
            Img(rhdr, UIStyle.Surface);
            Rect(rhdr, LW + 1, bodyY, RW, SUBHDR);

            int btnH = UIStyle.H(20);
            int btnY = (SUBHDR - btnH) / 2;

            // [Clear scene] - far right
            int clearW = UIStyle.W(76);
            var clearBtn = MakeGO("ClearScene", rhdr.transform);
            Img(clearBtn, UIStyle.Red with { a = 0.25f });
            Btn(clearBtn, OnClearSceneClicked);
            Rect(clearBtn, RW - clearW - M, btnY, clearW, btnH);
            MakeLbl(clearBtn.transform, "Clear scene", UIStyle.FontSizeSm - 2,
                UIStyle.Red, TextAnchor.MiddleCenter, fill: true);

            // [Paste] - left of [Clear scene]
            int pasteW = UIStyle.W(52);
            int pasteX = RW - clearW - M - pasteW - M;
            var pasteBtn = MakeGO("Paste", rhdr.transform);
            Img(pasteBtn, UIStyle.Accent with { a = 0.25f });
            Btn(pasteBtn, OnPasteClicked);
            Rect(pasteBtn, pasteX, btnY, pasteW, btnH);
            MakeLbl(pasteBtn.transform, "Paste", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // [Copy scene] - left of [Paste]
            int expW = UIStyle.W(72);
            int expX = pasteX - M - expW;
            var expBtn = MakeGO("ExportScene", rhdr.transform);
            Img(expBtn, UIStyle.Accent with { a = 0.20f });
            Btn(expBtn, OnExportSceneClicked);
            Rect(expBtn, expX, btnY, expW, btnH);
            MakeLbl(expBtn.transform, "Copy scene", UIStyle.FontSizeSm - 2,
                UIStyle.Accent, TextAnchor.MiddleCenter, fill: true);

            // Status label - brief feedback, left of all buttons
            int statusW = UIStyle.W(110);
            int statusX = expX - M - statusW;
            pasteStatus = MakeLbl(rhdr.transform, "", UIStyle.FontSizeSm - 2,
                UIStyle.Subtext, TextAnchor.MiddleRight,
                x: statusX, w: statusW, h: SUBHDR);

            // Scene name - fills remaining left space
            rightHeader = MakeLbl(rhdr.transform, "Select a room",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: M, w: statusX - M, h: SUBHDR);
        }

        // ── Settings strip: Tracking [ON/OFF] | Ghost [ON/OFF] | Alpha [-] 0.40 [+] | Color ■■■■■■
        private void BuildSettingsBar()
        {
            var bar = MakeGO("SettingsBar", panelGO!.transform);
            Img(bar, UIStyle.Surface);
            Rect(bar, 0, PH - STGSH, PW, STGSH);

            int btnH = UIStyle.H(22);
            int btnY = (STGSH - btnH) / 2;
            int lblW = UIStyle.W(38);
            int stepW = UIStyle.W(22);
            int x = M;

            // Tracking: [ON/OFF] - master switch for recording
            int trackLblW = UIStyle.W(52);
            MakeLbl(bar.transform, "Tracking:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft, x: x, y: 0, w: trackLblW, h: STGSH);
            x += trackLblW + M / 2;

            var trackingBtn = MakeGO("TrackingToggle", bar.transform);
            Img(trackingBtn, GhostSettings.TrackingEnabled
                ? UIStyle.Accent with { a = 0.22f }
                : UIStyle.Red with { a = 0.22f });
            Btn(trackingBtn, OnTrackingToggle);
            Rect(trackingBtn, x, btnY, UIStyle.W(46), btnH);
            trackingToggleLbl = MakeLbl(trackingBtn.transform,
                GhostSettings.TrackingEnabled ? "ON" : "OFF", UIStyle.FontSizeSm - 1,
                GhostSettings.TrackingEnabled ? UIStyle.Accent : UIStyle.Red,
                TextAnchor.MiddleCenter, fill: true);
            trackingToggleBtnImg = trackingBtn.GetComponent<Image>();
            x += UIStyle.W(46);

            x = BarSeparator(bar.transform, x, btnY, btnH);

            // Ghost: [ON/OFF]
            MakeLbl(bar.transform, "Ghost:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft, x: x, y: 0, w: lblW, h: STGSH);
            x += lblW + M / 2;

            var toggleBtn = MakeGO("GhostToggle", bar.transform);
            Img(toggleBtn, UIStyle.Overlay);
            Btn(toggleBtn, OnGhostToggle);
            Rect(toggleBtn, x, btnY, UIStyle.W(46), btnH);
            ghostToggleLbl = MakeLbl(toggleBtn.transform,
                GhostSettings.GhostEnabled ? "ON" : "OFF", UIStyle.FontSizeSm - 1,
                GhostSettings.GhostEnabled ? UIStyle.Accent : UIStyle.Subtext,
                TextAnchor.MiddleCenter, fill: true);
            x += UIStyle.W(46);

            x = BarSeparator(bar.transform, x, btnY, btnH);

            // Alpha: [−] 0.40 [+]
            MakeLbl(bar.transform, "Alpha:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft, x: x, y: 0, w: lblW, h: STGSH);
            x += lblW + M / 2;

            var minusBtn = MakeGO("AlphaMinus", bar.transform);
            Img(minusBtn, UIStyle.Overlay);
            Btn(minusBtn, OnAlphaMinus);
            Rect(minusBtn, x, btnY, stepW, btnH);
            MakeLbl(minusBtn.transform, "−", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            x += stepW + M / 2;

            alphaLbl = MakeLbl(bar.transform, AlphaString(), UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter,
                x: x, y: 0, w: UIStyle.W(38), h: STGSH);
            x += UIStyle.W(38) + M / 2;

            var plusBtn = MakeGO("AlphaPlus", bar.transform);
            Img(plusBtn, UIStyle.Overlay);
            Btn(plusBtn, OnAlphaPlus);
            Rect(plusBtn, x, btnY, stepW, btnH);
            MakeLbl(plusBtn.transform, "+", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            x += stepW;

            x = BarSeparator(bar.transform, x, btnY, btnH);

            // Color: ■ ■ ■ ■ ■ ■
            MakeLbl(bar.transform, "Color:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft, x: x, y: 0, w: lblW, h: STGSH);
            x += lblW + M / 2;

            Color[] swatches =
            {
                new Color(1.00f, 1.00f, 1.00f),  // white
                new Color(0.40f, 0.80f, 1.00f),  // cyan
                new Color(0.93f, 0.83f, 0.62f),  // gold
                new Color(0.40f, 0.85f, 0.40f),  // green
                new Color(0.93f, 0.53f, 0.59f),  // red
                new Color(0.75f, 0.55f, 1.00f),  // purple
            };
            int swatchW = UIStyle.W(22);
            foreach (var swatch in swatches)
            {
                var sw = MakeGO("Swatch", bar.transform);
                Color c = swatch;
                Img(sw, c);
                Btn(sw, () => OnColorSwatch(c));
                Rect(sw, x, btnY, swatchW, btnH);
                x += swatchW + UIStyle.W(4);
            }
        }

        // Draws a vertical bar separator in the settings strip and advances x.
        private int BarSeparator(Transform parent, int x, int btnY, int btnH)
        {
            x += UIStyle.W(10);
            VLine(parent, x, btnY, btnH);
            return x + UIStyle.W(10);
        }

        // ── Scroll area ───────────────────────────────────────────────────────
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
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
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