using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private void ClearConfigRefs()
        {
            _ghostToggleLbl = null;
            _ghostToggleBg = null;
            _trackingToggleLbl = null;
            _trackingToggleBg = null;
            _savePolicyLbl = null;
            _savePolicyBg = null;
            _maxSavedLbl = null;
            _timerToggleLbl = null;
            _timerToggleBg = null;
            _chainToggleLbl = null;
            _chainToggleBg = null;
            _skipRunsToggleLbl = null;
            _skipRunsToggleBg = null;
            _skipTimerToggleLbl = null;
            _skipTimerToggleBg = null;
            _cfgGhostColorFill = null;
            _cfgGhostAlphaLbl = null;
            _clearAllCfgLbl = null;
            _clearAllCfgBg = null;
            _copyAllCfgLbl = null;
            _copyAllCfgBg = null;
            _clearAllPending = false;
        }

        private void BuildConfigContent()
        {
            if (_rightContent == null) return;

            int rowH = UIStyle.H(24);
            int btnH = UIStyle.H(20);
            int labelW = UIStyle.W(108);
            int toggleW = UIStyle.W(46);
            int stepW = UIStyle.W(22);
            int valueW = UIStyle.W(36);
            int gap = UIStyle.W(4);

            ButtonRef br;

            // All toggles are built with placeholder text/colors; the
            // RefreshConfigValues() call that always follows BuildConfigContent
            // paints the real state (one idiom for every toggle).

            AddSectionHeader(_rightContent, "Recording");

            var trackRow = AddConfigRow(_rightContent, "Tracking", rowH, labelW);
            br = MakeButton(trackRow.transform, "TrackToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnTrackingToggle);
            _trackingToggleBg = br.bg;
            _trackingToggleLbl = br.label;

            var saveRow = AddConfigRow(_rightContent, "Save policy", rowH, labelW);
            br = MakeButton(saveRow.transform, "SaveToggle", "PB only",
                UIStyle.FontSizeRow, UIStyle.Gold, UIStyle.BtnBg(UIStyle.Gold),
                labelW, (rowH - btnH) / 2, UIStyle.W(72), btnH, OnSavePolicyToggle);
            _savePolicyBg = br.bg;
            _savePolicyLbl = br.label;

            var skipRunRow = AddConfigRow(_rightContent, "Skip backtrack", rowH, labelW);
            br = MakeButton(skipRunRow.transform, "SkipBacktrackRuns", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnSkipBacktrackRunsToggle);
            _skipRunsToggleBg = br.bg;
            _skipRunsToggleLbl = br.label;

            var keepRow = AddConfigRow(_rightContent, "Keep per route", rowH, labelW);
            int keepX = labelW;
            MakeButton(keepRow.transform, "KeepMinus", "-",
                UIStyle.FontSizeRow, UIStyle.Text, UIStyle.Overlay,
                keepX, (rowH - btnH) / 2, stepW, btnH, OnMaxSavedReplaysMinus);
            keepX += stepW + gap;
            _maxSavedLbl = MakeLbl(keepRow.transform,
                GhostSettings.MaxSavedReplaysPerRoute.ToString(),
                UIStyle.FontSizeRow, UIStyle.Text, TextAnchor.MiddleCenter,
                x: keepX, w: valueW, h: rowH);
            keepX += valueW + gap;
            MakeButton(keepRow.transform, "KeepPlus", "+",
                UIStyle.FontSizeRow, UIStyle.Text, UIStyle.Overlay,
                keepX, (rowH - btnH) / 2, stepW, btnH, OnMaxSavedReplaysPlus);

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Playback");

            var ghostRow = AddConfigRow(_rightContent, "Ghost", rowH, labelW);
            br = MakeButton(ghostRow.transform, "GhostToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnGhostToggle);
            _ghostToggleBg = br.bg;
            _ghostToggleLbl = br.label;

            var hudRow = AddConfigRow(_rightContent, "Timer HUD", rowH, labelW);
            br = MakeButton(hudRow.transform, "HUDToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnTimerToggleClicked);
            _timerToggleBg = br.bg;
            _timerToggleLbl = br.label;

            var chainRow = AddConfigRow(_rightContent, "Chain rooms", rowH, labelW);
            br = MakeButton(chainRow.transform, "ChainToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnChainTimersToggleClicked);
            _chainToggleBg = br.bg;
            _chainToggleLbl = br.label;

            var skipTimerRow = AddConfigRow(_rightContent, "Hide backtrack", rowH, labelW);
            br = MakeButton(skipTimerRow.transform, "SkipBacktrackTimer", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnSkipBacktrackTimerToggle);
            _skipTimerToggleBg = br.bg;
            _skipTimerToggleLbl = br.label;

            // Ghost color: one chip that opens the color picker for the
            // global color. Per-run overrides are set from the Runs tab.
            var colorRow = AddConfigRow(_rightContent, "Ghost color", rowH, labelW);
            int chipW = UIStyle.W(46);
            var chip = MakeGO("GhostColorChip", colorRow.transform);
            Img(chip, UIStyle.Overlay with { a = 0.9f });
            Rect(chip, labelW, (rowH - btnH) / 2, chipW, btnH);
            var chipFill = MakeGO("Fill", chip.transform);
            Color gc = GhostSettings.GhostColor;
            Img(chipFill, new Color(gc.r, gc.g, gc.b, 1f));
            Rect(chipFill, 1, 1, chipW - 2, btnH - 2);
            chipFill.GetComponent<Graphic>().raycastTarget = false;
            _cfgGhostColorFill = chipFill.GetComponent<Image>();
            Btn(chip, () => OpenGlobalColorPicker(chip));
            AddButtonHover(chip);
            _cfgGhostAlphaLbl = MakeLbl(colorRow.transform,
                "alpha " + gc.a.ToString("0.00"),
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: labelW + chipW + UIStyle.Gap, w: UIStyle.W(120), h: rowH);

            var colorHintRow = MakeGO("GhostColorHint", _rightContent);
            Img(colorHintRow, Color.clear);
            var colorHintLE = colorHintRow.AddComponent<LayoutElement>();
            colorHintLE.minHeight = colorHintLE.preferredHeight = UIStyle.H(16);
            MakeLbl(colorHintRow.transform,
                "Click a run's color swatch in the Runs tab to give it its own color.",
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: UIStyle.W(8), w: UIStyle.W(320), h: UIStyle.H(16));

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Data");

            var dataRow1 = MakeGO("DataRow1", _rightContent);
            Img(dataRow1, Color.clear);
            var d1LE = dataRow1.AddComponent<LayoutElement>();
            d1LE.minHeight = d1LE.preferredHeight = UIStyle.H(30);

            int dataBtnW = UIStyle.W(90);
            int dataBtnH = UIStyle.H(22);
            int dataY = UIStyle.H(4);
            int dataX = UIStyle.W(8);

            br = MakeButton(dataRow1.transform, "CopyAllCfg", "Copy all",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                dataX, dataY, dataBtnW, dataBtnH, OnCopyAllClicked);
            _copyAllCfgBg = br.bg;
            _copyAllCfgLbl = br.label;
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "ExportAllCfg", "Export all",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                dataX, dataY, dataBtnW, dataBtnH, OnExportAllClicked);
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "OpenExportsCfg", "Open exports",
                UIStyle.FontSizeBtn, UIStyle.Text, UIStyle.Overlay with { a = 0.6f },
                dataX, dataY, dataBtnW, dataBtnH, OnOpenExportFolderClicked);

            var dataRow2 = MakeGO("DataRow2", _rightContent);
            Img(dataRow2, Color.clear);
            var d2LE = dataRow2.AddComponent<LayoutElement>();
            d2LE.minHeight = d2LE.preferredHeight = UIStyle.H(30);

            br = MakeButton(dataRow2.transform, "ClearAllCfg", "Clear all data",
                UIStyle.FontSizeBtn, UIStyle.Red, UIStyle.BtnBg(UIStyle.Red),
                UIStyle.W(8), dataY, UIStyle.W(100), dataBtnH, OnClearAllClicked);
            _clearAllCfgBg = br.bg;
            _clearAllCfgLbl = br.label;
        }

        private void RefreshConfigValues()
        {
            if (_trackingToggleLbl != null)
            {
                bool on = GhostSettings.TrackingEnabled;
                _trackingToggleLbl.text = on ? "ON" : "OFF";
                _trackingToggleLbl.color = on ? UIStyle.Accent : UIStyle.Red;
                if (_trackingToggleBg != null)
                    _trackingToggleBg.color = on
                        ? UIStyle.BtnBgStrong(UIStyle.Accent)
                        : UIStyle.BtnBgStrong(UIStyle.Red);
            }

            StyleToggle(_ghostToggleLbl, _ghostToggleBg, GhostSettings.GhostEnabled);

            if (_savePolicyLbl != null)
            {
                bool all = GhostSettings.SaveAllRunsEnabled;
                _savePolicyLbl.text = all ? "Save all" : "PB only";
                _savePolicyLbl.color = all ? UIStyle.Accent : UIStyle.Gold;
                if (_savePolicyBg != null)
                    _savePolicyBg.color = all
                        ? UIStyle.BtnBgStrong(UIStyle.Accent)
                        : UIStyle.BtnBg(UIStyle.Gold);
            }

            if (_maxSavedLbl != null)
                _maxSavedLbl.text = GhostSettings.MaxSavedReplaysPerRoute.ToString();

            StyleToggle(_timerToggleLbl, _timerToggleBg, GhostSettings.TimerHudEnabled);
            StyleToggle(_chainToggleLbl, _chainToggleBg, GhostSettings.ChainRoomTimers);
            StyleToggle(_skipRunsToggleLbl, _skipRunsToggleBg, GhostSettings.SkipBacktrackRuns);
            StyleToggle(_skipTimerToggleLbl, _skipTimerToggleBg, GhostSettings.SkipBacktrackTimer);

            RefreshGhostColorChip();
        }

        /// <summary>
        /// Applies the standard ON/OFF colouring to a config toggle: accent when
        /// on, subtext/overlay when off. Toggles with bespoke off-states (e.g.
        /// tracking, save policy) are styled inline instead.
        /// </summary>
        private static void StyleToggle(Text? label, Image? bg, bool on)
        {
            if (label != null)
            {
                label.text = on ? "ON" : "OFF";
                label.color = on ? UIStyle.Accent : UIStyle.Subtext;
            }
            if (bg != null)
                bg.color = on ? UIStyle.BtnBgStrong(UIStyle.Accent) : UIStyle.Overlay;
        }

        private static void AddSectionHeader(Transform parent, string title)
        {
            var row = MakeGO("Section_" + title, parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(22);
            MakeLbl(row.transform, title, UIStyle.FontSizeBtn,
                UIStyle.Subtext, TextAnchor.LowerLeft,
                x: UIStyle.W(8), w: UIStyle.W(200), h: UIStyle.H(22));
        }

        private static GameObject AddConfigRow(Transform parent, string label, int rowH, int labelW)
        {
            var row = MakeGO("Cfg_" + label, parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = rowH;
            MakeLbl(row.transform, label, UIStyle.FontSizeRow,
                UIStyle.Text, TextAnchor.MiddleLeft,
                x: UIStyle.W(8), w: labelW - UIStyle.W(8), h: rowH);
            return row;
        }

        private static void AddSectionSeparator(Transform parent)
        {
            var sep = MakeGO("Separator", parent);
            Img(sep, UIStyle.Overlay with { a = 0.4f });
            var le = sep.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1;
        }
    }
}