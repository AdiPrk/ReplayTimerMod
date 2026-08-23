using System.Collections.Generic;
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
            _onlineToggleLbl = null;
            _onlineToggleBg = null;
            _warpToggleLbl = null;
            _warpToggleBg = null;
            _camFollowToggleLbl = null;
            _camFollowToggleBg = null;
            _nameInput = null;
            _nameStatusLbl = null;
            _nameSaveBg = null;
            _nameSaveLbl = null;
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

            AddSectionHeader(_rightContent, "Online");

            var onlineRow = AddConfigRow(_rightContent, "Upload PBs", rowH, labelW);
            br = MakeButton(onlineRow.transform, "OnlineToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnOnlineToggle);
            _onlineToggleBg = br.bg;
            _onlineToggleLbl = br.label;

            if (GhostSettings.OnlineEnabled)
            {
                var nameRow = AddConfigRow(_rightContent, "Name", rowH, labelW);
                int inputW = UIStyle.W(130);
                int inputH = btnH + UIStyle.H(4);
                int saveBtnW = UIStyle.W(40);

                var inputGO = MakeGO("NameInput", nameRow.transform);
                Img(inputGO, UIStyle.Surface);
                Rect(inputGO, labelW, (rowH - inputH) / 2, inputW, inputH);

                var textGO = MakeGO("Text", inputGO.transform);
                var textComp = textGO.AddComponent<Text>();
                textComp.font = UIStyle.Arial;
                textComp.fontSize = UIStyle.FontSizeRow;
                textComp.color = UIStyle.Text;
                textComp.alignment = TextAnchor.MiddleLeft;
                textComp.supportRichText = false;
                var textRT = textGO.GetComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(UIStyle.H(4), 1);
                textRT.offsetMax = new Vector2(-UIStyle.H(4), -1);

                var phGO = MakeGO("Placeholder", inputGO.transform);
                var phText = phGO.AddComponent<Text>();
                phText.font = UIStyle.Arial;
                phText.fontSize = UIStyle.FontSizeRow;
                phText.color = UIStyle.Subtext with { a = 0.5f };
                phText.fontStyle = FontStyle.Italic;
                phText.alignment = TextAnchor.MiddleLeft;
                phText.text = "Enter name...";
                var phRT = phGO.GetComponent<RectTransform>();
                phRT.anchorMin = Vector2.zero;
                phRT.anchorMax = Vector2.one;
                phRT.offsetMin = new Vector2(UIStyle.H(4), 1);
                phRT.offsetMax = new Vector2(-UIStyle.H(4), -1);

                _nameInput = inputGO.AddComponent<InputField>();
                _nameInput.textComponent = textComp;
                _nameInput.placeholder = phText;
                _nameInput.characterLimit = NameValidator.MaxLength;
                _nameInput.text = GhostSettings.DisplayName ?? "";
                _lastSavedName = _nameInput.text;
                _nameInput.onEndEdit.AddListener(OnNameEndEdit);

                int saveX = labelW + inputW + gap;
                br = MakeButton(nameRow.transform, "NameSave", "Save",
                    UIStyle.FontSizeBtn, UIStyle.Base, UIStyle.Accent,
                    saveX, (rowH - btnH) / 2, saveBtnW, btnH, OnNameSave);
                _nameSaveBg = br.bg;
                _nameSaveLbl = br.label;

                var statusRow = MakeGO("NameStatus", _rightContent);
                Img(statusRow, Color.clear);
                var statusLE = statusRow.AddComponent<LayoutElement>();
                statusLE.minHeight = statusLE.preferredHeight = UIStyle.H(16);
                _nameStatusLbl = MakeLbl(statusRow.transform, "",
                    UIStyle.FontSizeBtn, UIStyle.Subtext,
                    TextAnchor.MiddleLeft,
                    x: UIStyle.W(8), w: UIStyle.W(280), h: UIStyle.H(16));

                if (string.IsNullOrEmpty(GhostSettings.DisplayName))
                    _nameStatusLbl.text = "Set a name to start uploading";
            }

            AddSectionSeparator(_rightContent);

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

            AddSectionSeparator(_rightContent);

            AddSectionHeader(_rightContent, "Experimental");

            var warpRow = AddConfigRow(_rightContent, "Room warp", rowH, labelW);
            br = MakeButton(warpRow.transform, "RoomWarpToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnRoomWarpToggle);
            _warpToggleBg = br.bg;
            _warpToggleLbl = br.label;

            var warpHintRow = MakeGO("RoomWarpHint", _rightContent);
            Img(warpHintRow, Color.clear);
            var warpHintLE = warpHintRow.AddComponent<LayoutElement>();
            warpHintLE.minHeight = warpHintLE.preferredHeight = UIStyle.H(16);
            MakeLbl(warpHintRow.transform,
                "Adds Warp buttons to route headers.",
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: UIStyle.W(8), w: UIStyle.W(320), h: UIStyle.H(16));

            var camFollowRow = AddConfigRow(_rightContent, "Camera follow", rowH, labelW);
            br = MakeButton(camFollowRow.transform, "CameraFollowToggle", "OFF",
                UIStyle.FontSizeRow, UIStyle.Subtext, UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnCameraFollowFeatureToggle);
            _camFollowToggleBg = br.bg;
            _camFollowToggleLbl = br.label;

            var camFollowHintRow = MakeGO("CameraFollowHint", _rightContent);
            Img(camFollowHintRow, Color.clear);
            var camFollowHintLE = camFollowHintRow.AddComponent<LayoutElement>();
            camFollowHintLE.minHeight = camFollowHintLE.preferredHeight = UIStyle.H(16);
            MakeLbl(camFollowHintRow.transform,
                "Adds camera buttons to runs; the camera tracks that ghost.",
                UIStyle.FontSizeBtn, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: UIStyle.W(8), w: UIStyle.W(320), h: UIStyle.H(16));
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

            StyleToggle(_onlineToggleLbl, _onlineToggleBg, GhostSettings.OnlineEnabled);
            StyleToggle(_warpToggleLbl, _warpToggleBg, GhostSettings.RoomWarpEnabled);
            StyleToggle(_camFollowToggleLbl, _camFollowToggleBg, GhostSettings.CameraFollowEnabled);
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

        // ── Name save logic ────────────────────────────────────────────────

        private void OnNameEndEdit(string text)
        {
            // Submit on Enter key
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnNameSave();
        }

        private void OnNameSave()
        {
            if (_nameInput == null) return;
            if (_nameSaveInFlight) return;

            string name = _nameInput.text.Trim();

            if (name == _lastSavedName && !string.IsNullOrEmpty(name))
            {
                SetNameStatus("No changes", UIStyle.Subtext);
                return;
            }

            var check = NameValidator.Validate(name);
            if (!check.Valid)
            {
                SetNameStatus(check.Error, UIStyle.Red);
                return;
            }
            // Use the canonical form so what we send matches what passed validation.
            name = check.Canonical;

            SetNameStatus("Saving...", UIStyle.Subtext);
            SetNameSaveEnabled(false);

            // The name save runs BEFORE networking starts (StartNetworking
            // waits for a display name), so it uses its own HttpService
            // instance rather than NetworkClient's. HttpService owns the
            // cross-version quirks - most importantly the manual V1221
            // timeout, without which a stalled request would lock name-saving
            // forever.
            GhostSettings.EnsureDeviceId();
            _nameHttp ??= new HttpService();
            _nameSaveInFlight = true;

            var headers = new Dictionary<string, string>
            {
                { "X-Device-Id", GhostSettings.DeviceId }
            };

            _nameHttp.Post(GhostSettings.ApiBaseUrl + "/set-name",
                ApiJson.SerializeSetName(name), 10,
                (success, status, body) =>
                {
                    _nameSaveInFlight = false;

                    if (success)
                    {
                        string? confirmed =
                            ApiJson.ParseTopLevelString(body, "display_name");
                        if (confirmed != null)
                        {
                            _lastSavedName = confirmed;
                            if (_nameInput != null)
                                _nameInput.text = confirmed;
                            SetNameStatus("Saved!", UIStyle.Green);
                            OnDisplayNameSet?.Invoke(confirmed);
                        }
                        else
                        {
                            SetNameStatus("Unexpected response", UIStyle.Red);
                        }
                    }
                    else
                    {
                        string msg = ApiJson.ParseTopLevelString(body, "error")
                            ?? "Connection failed. Try again.";
                        SetNameStatus(msg, UIStyle.Red);
                    }

                    SetNameSaveEnabled(true);
                }, headers);
        }

        /// <summary>
        /// Pumps the name-save HttpService. Called from Tick(); the panel's
        /// own instance because the shared NetworkClient may not exist yet.
        /// </summary>
        private void TickNameSave()
        {
            _nameHttp?.Tick();
        }

        private void SetNameStatus(string text, Color color)
        {
            if (_nameStatusLbl != null)
            {
                _nameStatusLbl.text = text;
                _nameStatusLbl.color = color;
            }
        }

        private void SetNameSaveEnabled(bool enabled)
        {
            if (_nameSaveBg != null)
                _nameSaveBg.color = enabled ? UIStyle.Accent : UIStyle.Overlay;
            if (_nameSaveLbl != null)
                _nameSaveLbl.color = enabled ? UIStyle.Base : UIStyle.Subtext;
        }
    }
}