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
            _reeseToggleLbl = null;
            _reeseToggleBg = null;
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
            _cheatCancelToggleLbl = null;
            _cheatCancelToggleBg = null;
            _skipCheatedToggleLbl = null;
            _skipCheatedToggleBg = null;
            _skipGhostReentryToggleLbl = null;
            _skipGhostReentryToggleBg = null;
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

            AddSectionHeader(_rightContent, "Recording");

            var trackRow = AddConfigRow(_rightContent, "Record runs", rowH, labelW);
            br = MakeButton(trackRow.transform, "TrackToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnTrackingToggle);
            _trackingToggleBg = br.bg;
            _trackingToggleLbl = br.label;

            var saveRow = AddConfigRow(_rightContent, "Save runs", rowH, labelW);
            br = MakeButton(saveRow.transform, "SaveToggle", "PB only",
                UIStyle.FontSizeRow, UIStyle.Gold, UIStyle.BtnBg(UIStyle.Gold),
                labelW, (rowH - btnH) / 2, UIStyle.W(72), btnH, OnSavePolicyToggle);
            _savePolicyBg = br.bg;
            _savePolicyLbl = br.label;

            var keepRow = AddConfigRow(_rightContent, "Runs per route", rowH, labelW);
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

            var skipRunRow = AddConfigRow(_rightContent, "Skip re-entries", rowH, labelW);
            br = MakeButton(skipRunRow.transform, "SkipBacktrackRuns", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnSkipBacktrackRunsToggle);
            _skipRunsToggleBg = br.bg;
            _skipRunsToggleLbl = br.label;

            var cheatRow = AddConfigRow(_rightContent, "Cancel on debug", rowH, labelW);
            AttachTooltip(cheatRow, "When off, runs that used DebugMod are kept and marked with a small tab on their right edge.");
            br = MakeButton(cheatRow.transform, "CheatCancelToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnCheatCancelToggle);
            _cheatCancelToggleBg = br.bg;
            _cheatCancelToggleLbl = br.label;

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Ghost");

            var ghostRow = AddConfigRow(_rightContent, "Enable Playback", rowH, labelW);
            br = MakeButton(ghostRow.transform, "GhostToggle", "ON",
                UIStyle.FontSizeRow, UIStyle.Accent, UIStyle.BtnBgStrong(UIStyle.Accent),
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnGhostToggle);
            _ghostToggleBg = br.bg;
            _ghostToggleLbl = br.label;

            var skipCheatedRow = AddConfigRow(_rightContent, "Skip debug runs", rowH, labelW);
            AttachTooltip(skipCheatedRow, "The ghost uses your best run that didn't use DebugMod. Runs you pick in the Runs tab still play.");
            br = MakeButton(skipCheatedRow.transform, "SkipCheatedToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnSkipCheatedRunsToggle);
            _skipCheatedToggleBg = br.bg;
            _skipCheatedToggleLbl = br.label;

            var skipReentryRow = AddConfigRow(_rightContent, "Skip re-entries", rowH, labelW);
            AttachTooltip(skipReentryRow, "The ghost and timer ignore runs that leave through the door you came in. Runs you pick in the Runs tab still play.");
            br = MakeButton(skipReentryRow.transform, "SkipBacktrackPlayback", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnSkipBacktrackPlaybackToggle);
            _skipGhostReentryToggleBg = br.bg;
            _skipGhostReentryToggleLbl = br.label;

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
                OpacityText(gc.a),
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: labelW + chipW + UIStyle.Gap, w: UIStyle.W(120), h: rowH);

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Timer");

            var hudRow = AddConfigRow(_rightContent, "Room timer", rowH, labelW);
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

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Data");

            var dataRow1 = MakeGO("DataRow1", _rightContent);
            Img(dataRow1, Color.clear);
            var d1LE = dataRow1.AddComponent<LayoutElement>();
            d1LE.minHeight = d1LE.preferredHeight = rowH;

            int dataBtnW = UIStyle.W(90);
            int dataY = (rowH - btnH) / 2;
            int dataX = UIStyle.W(8);

            br = MakeButton(dataRow1.transform, "CopyAllCfg", "Copy all",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                dataX, dataY, dataBtnW, btnH, OnCopyAllClicked);
            _copyAllCfgBg = br.bg;
            _copyAllCfgLbl = br.label;
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "ExportAllCfg", "Export all",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                dataX, dataY, dataBtnW, btnH, OnExportAllClicked);
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "OpenExportsCfg", "Open exports",
                UIStyle.FontSizeBtn, UIStyle.Text, UIStyle.Overlay with { a = 0.6f },
                dataX, dataY, dataBtnW, btnH, OnOpenExportFolderClicked);

            var dataRow2 = MakeGO("DataRow2", _rightContent);
            Img(dataRow2, Color.clear);
            var d2LE = dataRow2.AddComponent<LayoutElement>();
            d2LE.minHeight = d2LE.preferredHeight = rowH;

            br = MakeButton(dataRow2.transform, "ClearAllCfg", "Clear all data",
                UIStyle.FontSizeBtn, UIStyle.Red, UIStyle.BtnBg(UIStyle.Red),
                UIStyle.W(8), dataY, UIStyle.W(100), btnH, OnClearAllClicked);
            _clearAllCfgBg = br.bg;
            _clearAllCfgLbl = br.label;

            AddSectionSeparator(_rightContent);

            var reeseRow = AddConfigRow(_rightContent, "Reese", rowH, labelW);
            br = MakeButton(reeseRow.transform, "ReeseToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnReeseToggle);
            _reeseToggleBg = br.bg;
            _reeseToggleLbl = br.label;
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
            StyleToggle(_reeseToggleLbl, _reeseToggleBg, GhostSettings.ReeseEnabled);

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
            StyleToggle(_cheatCancelToggleLbl, _cheatCancelToggleBg, GhostSettings.CancelRunOnCheats);
            StyleToggle(_skipCheatedToggleLbl, _skipCheatedToggleBg, GhostSettings.SkipCheatedRuns);
            StyleToggle(_skipGhostReentryToggleLbl, _skipGhostReentryToggleBg, GhostSettings.SkipBacktrackPlayback);

            RefreshGhostColorChip();
        }

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
                UIStyle.Subtext, TextAnchor.MiddleLeft,
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
            var le = sep.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(11);
            var line = MakeGO("Line", sep.transform);
            Img(line, UIStyle.Overlay with { a = 0.4f });
            var rt = line.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(0f, 1f);
        }
    }
}
