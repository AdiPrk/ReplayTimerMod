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

        // ── Settings strip: global toggles + context-aware visual editor ─────
        private void BuildSettingsBar()
        {
            var bar = MakeGO("SettingsBar", panelGO!.transform);
            Img(bar, UIStyle.Surface);
            Rect(bar, 0, PH - STGSH, PW, STGSH);

            int btnH = UIStyle.H(22);
            int topY = UIStyle.H(6);
            int bottomY = UIStyle.H(34);
            int stepW = UIStyle.W(22);
            int valueW = UIStyle.W(38);
            int toggleW = UIStyle.W(46);
            int saveValueW = UIStyle.W(84);
            int keepValueW = UIStyle.W(36);
            int separatorW = UIStyle.W(20);
            int trackingLabelW = UIStyle.W(52);
            int ghostLabelW = UIStyle.W(38);
            int saveLabelW = UIStyle.W(34);
            int keepLabelW = UIStyle.W(34);
            int halfGap = M / 2;

            int topContentW = trackingLabelW + halfGap + toggleW + separatorW
                + ghostLabelW + halfGap + toggleW + separatorW
                + saveLabelW + halfGap + saveValueW + separatorW
                + keepLabelW + halfGap + stepW + halfGap + keepValueW + halfGap + stepW;
            int x = Mathf.Max(M, (PW - topContentW) / 2);

            MakeLbl(bar.transform, "Tracking:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: x, y: topY, w: trackingLabelW, h: btnH);
            x += trackingLabelW + halfGap;

            var trackingBtn = MakeGO("TrackingToggle", bar.transform);
            Img(trackingBtn, UIStyle.Overlay);
            Btn(trackingBtn, OnTrackingToggle);
            Rect(trackingBtn, x, topY, toggleW, btnH);
            trackingToggleLbl = MakeLbl(trackingBtn.transform, "ON",
                UIStyle.FontSizeSm - 1, UIStyle.Accent,
                TextAnchor.MiddleCenter, fill: true);
            trackingToggleBtnImg = trackingBtn.GetComponent<Image>();
            x += toggleW;

            x = BarSeparator(bar.transform, x, topY, btnH);

            MakeLbl(bar.transform, "Ghost:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: x, y: topY, w: ghostLabelW, h: btnH);
            x += ghostLabelW + halfGap;

            var ghostBtn = MakeGO("GhostToggle", bar.transform);
            Img(ghostBtn, UIStyle.Overlay);
            Btn(ghostBtn, OnGhostToggle);
            Rect(ghostBtn, x, topY, toggleW, btnH);
            ghostToggleLbl = MakeLbl(ghostBtn.transform, "ON",
                UIStyle.FontSizeSm - 1, UIStyle.Accent,
                TextAnchor.MiddleCenter, fill: true);
            ghostToggleBtnImg = ghostBtn.GetComponent<Image>();
            x += toggleW;

            x = BarSeparator(bar.transform, x, topY, btnH);

            MakeLbl(bar.transform, "Save:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: x, y: topY, w: saveLabelW, h: btnH);
            x += saveLabelW + halfGap;

            var saveBtn = MakeGO("SavePolicyToggle", bar.transform);
            Img(saveBtn, UIStyle.Overlay);
            Btn(saveBtn, OnSavePolicyToggle);
            Rect(saveBtn, x, topY, saveValueW, btnH);
            savePolicyLbl = MakeLbl(saveBtn.transform, SavePolicyLabel(),
                UIStyle.FontSizeSm - 2, UIStyle.Gold,
                TextAnchor.MiddleCenter, fill: true);
            savePolicyBtnImg = saveBtn.GetComponent<Image>();
            x += saveValueW;

            x = BarSeparator(bar.transform, x, topY, btnH);

            MakeLbl(bar.transform, "Keep:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: x, y: topY, w: keepLabelW, h: btnH);
            x += keepLabelW + halfGap;

            var keepMinusBtn = MakeGO("MaxSavedReplaysMinus", bar.transform);
            Img(keepMinusBtn, UIStyle.Overlay);
            Btn(keepMinusBtn, OnMaxSavedReplaysMinus);
            Rect(keepMinusBtn, x, topY, stepW, btnH);
            MakeLbl(keepMinusBtn.transform, "−", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            x += stepW + halfGap;

            maxSavedReplaysLbl = MakeLbl(bar.transform, MaxSavedReplaysString(), UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter,
                x: x, y: topY, w: keepValueW, h: btnH);
            x += keepValueW + halfGap;

            var keepPlusBtn = MakeGO("MaxSavedReplaysPlus", bar.transform);
            Img(keepPlusBtn, UIStyle.Overlay);
            Btn(keepPlusBtn, OnMaxSavedReplaysPlus);
            Rect(keepPlusBtn, x, topY, stepW, btnH);
            MakeLbl(keepPlusBtn.transform, "+", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);

            int contextW = UIStyle.W(272);
            int alphaLabelW = UIStyle.W(38);
            int colorLabelW = UIStyle.W(38);
            int swatchW = UIStyle.W(22);
            int swatchGap = UIStyle.W(4);
            Color[] swatches =
            {
                new Color(1.00f, 1.00f, 1.00f),
                new Color(0.40f, 0.80f, 1.00f),
                new Color(0.93f, 0.83f, 0.62f),
                new Color(0.40f, 0.85f, 0.40f),
                new Color(0.93f, 0.53f, 0.59f),
                new Color(0.75f, 0.55f, 1.00f),
            };
            int swatchesW = swatches.Length * swatchW + (swatches.Length - 1) * swatchGap;
            int bottomContentW = contextW + separatorW
                + alphaLabelW + halfGap + stepW + halfGap + valueW + halfGap + stepW + separatorW
                + colorLabelW + halfGap + swatchesW;
            int bottomX = Mathf.Max(M, (PW - bottomContentW) / 2);

            var contextBtn = MakeGO("SettingsContext", bar.transform);
            Img(contextBtn, UIStyle.Overlay with { a = 0.55f });
            Btn(contextBtn, OnEditGlobalContext);
            Rect(contextBtn, bottomX, bottomY, contextW, btnH);
            settingsContextLbl = MakeLbl(contextBtn.transform, "Edit: Global",
                UIStyle.FontSizeSm - 1, UIStyle.Text,
                TextAnchor.MiddleCenter, fill: true);
            settingsContextBtnImg = contextBtn.GetComponent<Image>();
            bottomX += contextW;

            bottomX = BarSeparator(bar.transform, bottomX, bottomY, btnH);

            MakeLbl(bar.transform, "Alpha:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: bottomX, y: bottomY, w: alphaLabelW, h: btnH);
            bottomX += alphaLabelW + halfGap;

            var minusBtn = MakeGO("AlphaMinus", bar.transform);
            Img(minusBtn, UIStyle.Overlay);
            Btn(minusBtn, OnAlphaMinus);
            Rect(minusBtn, bottomX, bottomY, stepW, btnH);
            MakeLbl(minusBtn.transform, "−", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            bottomX += stepW + halfGap;

            alphaLbl = MakeLbl(bar.transform, AlphaString(), UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter,
                x: bottomX, y: bottomY, w: valueW, h: btnH);
            bottomX += valueW + halfGap;

            var plusBtn = MakeGO("AlphaPlus", bar.transform);
            Img(plusBtn, UIStyle.Overlay);
            Btn(plusBtn, OnAlphaPlus);
            Rect(plusBtn, bottomX, bottomY, stepW, btnH);
            MakeLbl(plusBtn.transform, "+", UIStyle.FontSizeSm - 1,
                UIStyle.Text, TextAnchor.MiddleCenter, fill: true);
            bottomX += stepW;

            bottomX = BarSeparator(bar.transform, bottomX, bottomY, btnH);

            MakeLbl(bar.transform, "Color:", UIStyle.FontSizeSm - 1,
                UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: bottomX, y: bottomY, w: colorLabelW, h: btnH);
            bottomX += colorLabelW + halfGap;

            foreach (var swatch in swatches)
            {
                var sw = MakeGO("Swatch", bar.transform);
                Color c = swatch;
                Img(sw, c);
                Btn(sw, () => OnColorSwatch(c));
                Rect(sw, bottomX, bottomY, swatchW, btnH);
                bottomX += swatchW + swatchGap;
            }

            RefreshSettingsBar();
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