using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private static readonly Color[] GhostColorSwatches =
        {
            new Color(1.00f, 1.00f, 1.00f),
            new Color(0.40f, 0.80f, 1.00f),
            new Color(0.93f, 0.83f, 0.62f),
            new Color(0.40f, 0.85f, 0.40f),
            new Color(0.93f, 0.53f, 0.59f),
            new Color(0.75f, 0.55f, 1.00f),
        };

        private void ClearConfigRefs()
        {
            ghostToggleLbl = null;
            ghostToggleBg = null;
            trackingToggleLbl = null;
            trackingToggleBg = null;
            savePolicyLbl = null;
            savePolicyBg = null;
            maxSavedLbl = null;
            timerToggleLbl = null;
            timerToggleBg = null;
            alphaLbl = null;
            editContextLbl = null;
            editContextBg = null;
            clearAllCfgLbl = null;
            clearAllCfgBg = null;
            exportAllCfgLbl = null;
            exportAllCfgBg = null;
            clearAllPending = false;
            onlineToggleLbl = null;
            onlineToggleBg = null;
            nameInput = null;
            nameStatusLbl = null;
            nameSaveBg = null;
            nameSaveLbl = null;
        }

        private void BuildConfigContent()
        {
            if (rightContent == null) return;

            int rowH = UIStyle.H(24);
            int btnH = UIStyle.H(20);
            int labelW = UIStyle.W(108);
            int toggleW = UIStyle.W(46);
            int stepW = UIStyle.W(22);
            int valueW = UIStyle.W(36);
            int gap = UIStyle.W(4);

            ButtonRef br;

            // -- Online --
            AddSectionHeader(rightContent, "Online");

            var onlineRow = AddConfigRow(rightContent, "Upload PBs", rowH, labelW);
            br = MakeButton(onlineRow.transform, "OnlineToggle",
                GhostSettings.OnlineEnabled ? "ON" : "OFF",
                UIStyle.FontSizeSm - 1,
                GhostSettings.OnlineEnabled ? UIStyle.Accent : UIStyle.Subtext,
                GhostSettings.OnlineEnabled
                    ? UIStyle.Accent with { a = 0.22f }
                    : UIStyle.Overlay,
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnOnlineToggle);
            onlineToggleBg = br.bg;
            onlineToggleLbl = br.label;

            // Display name row (visible when online is enabled)
            if (GhostSettings.OnlineEnabled)
            {
                var nameRow = AddConfigRow(rightContent, "Name", rowH, labelW);
                int inputW = UIStyle.W(130);
                int inputH = btnH + UIStyle.H(4);
                int saveBtnW = UIStyle.W(40);

                // InputField background
                var inputGO = MakeGO("NameInput", nameRow.transform);
                Img(inputGO, UIStyle.Surface);
                Rect(inputGO, labelW, (rowH - inputH) / 2, inputW, inputH);

                // Text child
                var textGO = MakeGO("Text", inputGO.transform);
                var textComp = textGO.AddComponent<Text>();
                textComp.font = UIStyle.Arial;
                textComp.fontSize = UIStyle.FontSizeSm - 1;
                textComp.color = UIStyle.Text;
                textComp.alignment = TextAnchor.MiddleLeft;
                textComp.supportRichText = false;
                var textRT = textGO.GetComponent<RectTransform>();
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(UIStyle.H(4), 1);
                textRT.offsetMax = new Vector2(-UIStyle.H(4), -1);

                // Placeholder child
                var phGO = MakeGO("Placeholder", inputGO.transform);
                var phText = phGO.AddComponent<Text>();
                phText.font = UIStyle.Arial;
                phText.fontSize = UIStyle.FontSizeSm - 1;
                phText.color = UIStyle.Subtext with { a = 0.5f };
                phText.fontStyle = FontStyle.Italic;
                phText.alignment = TextAnchor.MiddleLeft;
                phText.text = "Enter name...";
                var phRT = phGO.GetComponent<RectTransform>();
                phRT.anchorMin = Vector2.zero;
                phRT.anchorMax = Vector2.one;
                phRT.offsetMin = new Vector2(UIStyle.H(4), 1);
                phRT.offsetMax = new Vector2(-UIStyle.H(4), -1);

                // InputField component
                nameInput = inputGO.AddComponent<InputField>();
                nameInput.textComponent = textComp;
                nameInput.placeholder = phText;
                nameInput.characterLimit = NameValidator.MaxLength;
                nameInput.text = GhostSettings.DisplayName ?? "";
                _lastSavedName = nameInput.text;
                nameInput.onEndEdit.AddListener(OnNameEndEdit);

                // Save button
                int saveX = labelW + inputW + gap;
                br = MakeButton(nameRow.transform, "NameSave", "Save",
                    UIStyle.FontSizeSm - 2, UIStyle.Base, UIStyle.Accent,
                    saveX, (rowH - btnH) / 2, saveBtnW, btnH, OnNameSave);
                nameSaveBg = br.bg;
                nameSaveLbl = br.label;

                // Status row (for feedback: "Saved!", errors, etc.)
                var statusRow = MakeGO("NameStatus", rightContent);
                Img(statusRow, Color.clear);
                var statusLE = statusRow.AddComponent<LayoutElement>();
                statusLE.minHeight = statusLE.preferredHeight = UIStyle.H(16);
                nameStatusLbl = MakeLbl(statusRow.transform, "",
                    UIStyle.FontSizeSm - 2, UIStyle.Subtext,
                    TextAnchor.MiddleLeft,
                    x: UIStyle.W(8), w: UIStyle.W(280), h: UIStyle.H(16));

                // Show hint if no name set yet
                if (string.IsNullOrEmpty(GhostSettings.DisplayName))
                    nameStatusLbl.text = "Set a name to start uploading";
            }

            AddSectionSeparator(rightContent);

            // -- Recording --
            AddSectionHeader(rightContent, "Recording");

            var trackRow = AddConfigRow(rightContent, "Tracking", rowH, labelW);
            br = MakeButton(trackRow.transform, "TrackToggle", "ON",
                UIStyle.FontSizeSm - 1, UIStyle.Accent, UIStyle.Accent with { a = 0.22f },
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnTrackingToggle);
            trackingToggleBg = br.bg;
            trackingToggleLbl = br.label;

            var saveRow = AddConfigRow(rightContent, "Save policy", rowH, labelW);
            br = MakeButton(saveRow.transform, "SaveToggle",
                GhostSettings.SaveAllRunsEnabled ? "Save all" : "PB only",
                UIStyle.FontSizeSm - 1, UIStyle.Gold, UIStyle.Gold with { a = 0.18f },
                labelW, (rowH - btnH) / 2, UIStyle.W(72), btnH, OnSavePolicyToggle);
            savePolicyBg = br.bg;
            savePolicyLbl = br.label;

            var keepRow = AddConfigRow(rightContent, "Keep per route", rowH, labelW);
            int keepX = labelW;
            MakeButton(keepRow.transform, "KeepMinus", "-",
                UIStyle.FontSizeSm - 1, UIStyle.Text, UIStyle.Overlay,
                keepX, (rowH - btnH) / 2, stepW, btnH, OnMaxSavedReplaysMinus);
            keepX += stepW + gap;
            maxSavedLbl = MakeLbl(keepRow.transform,
                GhostSettings.MaxSavedReplaysPerRoute.ToString(),
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleCenter,
                x: keepX, w: valueW, h: rowH);
            keepX += valueW + gap;
            MakeButton(keepRow.transform, "KeepPlus", "+",
                UIStyle.FontSizeSm - 1, UIStyle.Text, UIStyle.Overlay,
                keepX, (rowH - btnH) / 2, stepW, btnH, OnMaxSavedReplaysPlus);

            AddSectionSeparator(rightContent);

            // -- Playback --
            AddSectionHeader(rightContent, "Playback");

            var ghostRow = AddConfigRow(rightContent, "Ghost", rowH, labelW);
            br = MakeButton(ghostRow.transform, "GhostToggle", "ON",
                UIStyle.FontSizeSm - 1, UIStyle.Accent, UIStyle.Accent with { a = 0.22f },
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnGhostToggle);
            ghostToggleBg = br.bg;
            ghostToggleLbl = br.label;

            var hudRow = AddConfigRow(rightContent, "Timer HUD", rowH, labelW);
            br = MakeButton(hudRow.transform, "HUDToggle", "ON",
                UIStyle.FontSizeSm - 1, UIStyle.Accent, UIStyle.Accent with { a = 0.22f },
                labelW, (rowH - btnH) / 2, toggleW, btnH, OnTimerToggleClicked);
            timerToggleBg = br.bg;
            timerToggleLbl = br.label;

            var ctxRow = AddConfigRow(rightContent, "Editing", rowH, labelW);
            br = MakeButton(ctxRow.transform, "EditContext", "Global",
                UIStyle.FontSizeSm - 1, UIStyle.Text, UIStyle.Overlay with { a = 0.55f },
                labelW, (rowH - btnH) / 2, UIStyle.W(160), btnH, OnEditGlobalContext);
            editContextBg = br.bg;
            editContextLbl = br.label;

            var alphaRow = AddConfigRow(rightContent, "Alpha", rowH, labelW);
            int alphaX = labelW;
            MakeButton(alphaRow.transform, "AlphaMinus", "-",
                UIStyle.FontSizeSm - 1, UIStyle.Text, UIStyle.Overlay,
                alphaX, (rowH - btnH) / 2, stepW, btnH, OnAlphaMinus);
            alphaX += stepW + gap;
            alphaLbl = MakeLbl(alphaRow.transform,
                GhostSettings.GhostAlpha.ToString("0.00"),
                UIStyle.FontSizeSm - 1, UIStyle.Text, TextAnchor.MiddleCenter,
                x: alphaX, w: valueW, h: rowH);
            alphaX += valueW + gap;
            MakeButton(alphaRow.transform, "AlphaPlus", "+",
                UIStyle.FontSizeSm - 1, UIStyle.Text, UIStyle.Overlay,
                alphaX, (rowH - btnH) / 2, stepW, btnH, OnAlphaPlus);

            var colorRow = AddConfigRow(rightContent, "Color", rowH, labelW);
            int swatchSize = UIStyle.W(22);
            int swatchGap = UIStyle.W(4);
            int swatchX = labelW;
            foreach (var swatch in GhostColorSwatches)
            {
                Color c = swatch;
                var sw = MakeGO("Swatch", colorRow.transform);
                Img(sw, c);
                Btn(sw, () => OnColorSwatch(c));
                Rect(sw, swatchX, (rowH - btnH) / 2, swatchSize, btnH);
                swatchX += swatchSize + swatchGap;
            }

            AddSectionSeparator(rightContent);

            // -- Data --
            AddSectionHeader(rightContent, "Data");

            var dataRow1 = MakeGO("DataRow1", rightContent);
            Img(dataRow1, Color.clear);
            var d1LE = dataRow1.AddComponent<LayoutElement>();
            d1LE.minHeight = d1LE.preferredHeight = UIStyle.H(30);

            int dataBtnW = UIStyle.W(90);
            int dataBtnH = UIStyle.H(22);
            int dataY = UIStyle.H(4);
            int dataX = UIStyle.W(8);

            br = MakeButton(dataRow1.transform, "ExportAllCfg", "Copy all",
                UIStyle.FontSizeSm - 2, UIStyle.Accent, UIStyle.Accent with { a = 0.18f },
                dataX, dataY, dataBtnW, dataBtnH, OnExportAllClicked);
            exportAllCfgBg = br.bg;
            exportAllCfgLbl = br.label;
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "DownloadCfg", "Export All",
                UIStyle.FontSizeSm - 2, UIStyle.Accent, UIStyle.Accent with { a = 0.12f },
                dataX, dataY, dataBtnW, dataBtnH, OnDownloadAllClicked);
            dataX += dataBtnW + gap;

            MakeButton(dataRow1.transform, "OpenExportsCfg", "Open exports",
                UIStyle.FontSizeSm - 2, UIStyle.Text, UIStyle.Overlay with { a = 0.6f },
                dataX, dataY, dataBtnW, dataBtnH, OnOpenExportFolderClicked);

            var dataRow2 = MakeGO("DataRow2", rightContent);
            Img(dataRow2, Color.clear);
            var d2LE = dataRow2.AddComponent<LayoutElement>();
            d2LE.minHeight = d2LE.preferredHeight = UIStyle.H(30);

            br = MakeButton(dataRow2.transform, "ClearAllCfg", "Clear all data",
                UIStyle.FontSizeSm - 2, UIStyle.Red, UIStyle.Red with { a = 0.15f },
                UIStyle.W(8), dataY, UIStyle.W(100), dataBtnH, OnClearAllClicked);
            clearAllCfgBg = br.bg;
            clearAllCfgLbl = br.label;
        }

        private void RefreshConfigValues()
        {
            if (trackingToggleLbl != null)
            {
                bool on = GhostSettings.TrackingEnabled;
                trackingToggleLbl.text = on ? "ON" : "OFF";
                trackingToggleLbl.color = on ? UIStyle.Accent : UIStyle.Red;
                if (trackingToggleBg != null)
                    trackingToggleBg.color = on
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Red with { a = 0.22f };
            }

            if (ghostToggleLbl != null)
            {
                bool on = GhostSettings.GhostEnabled;
                ghostToggleLbl.text = on ? "ON" : "OFF";
                ghostToggleLbl.color = on ? UIStyle.Accent : UIStyle.Subtext;
                if (ghostToggleBg != null)
                    ghostToggleBg.color = on
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }

            if (savePolicyLbl != null)
            {
                bool all = GhostSettings.SaveAllRunsEnabled;
                savePolicyLbl.text = all ? "Save all" : "PB only";
                savePolicyLbl.color = all ? UIStyle.Accent : UIStyle.Gold;
                if (savePolicyBg != null)
                    savePolicyBg.color = all
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Gold with { a = 0.18f };
            }

            if (maxSavedLbl != null)
                maxSavedLbl.text = GhostSettings.MaxSavedReplaysPerRoute.ToString();

            if (timerToggleLbl != null)
            {
                bool on = GhostSettings.TimerHudEnabled;
                timerToggleLbl.text = on ? "ON" : "OFF";
                timerToggleLbl.color = on ? UIStyle.Accent : UIStyle.Subtext;
                if (timerToggleBg != null)
                    timerToggleBg.color = on
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }

            if (TryGetSelectedSnapshot(out _, out var snap) && snap != null)
            {
                if (editContextLbl != null)
                {
                    editContextLbl.text = FindSnapshotEditLabel(snap);
                    editContextLbl.color = UIStyle.Accent;
                }
                if (editContextBg != null)
                    editContextBg.color = UIStyle.Accent with { a = 0.22f };
                if (alphaLbl != null)
                    alphaLbl.text = snap.ResolveGhostColor(CurrentGlobalGhostColor).a.ToString("0.00");
            }
            else
            {
                if (editContextLbl != null)
                {
                    editContextLbl.text = "Global";
                    editContextLbl.color = UIStyle.Text;
                }
                if (editContextBg != null)
                    editContextBg.color = UIStyle.Overlay with { a = 0.55f };
                if (alphaLbl != null)
                    alphaLbl.text = GhostSettings.GhostAlpha.ToString("0.00");
            }

            if (onlineToggleLbl != null)
            {
                bool on = GhostSettings.OnlineEnabled;
                onlineToggleLbl.text = on ? "ON" : "OFF";
                onlineToggleLbl.color = on ? UIStyle.Accent : UIStyle.Subtext;
                if (onlineToggleBg != null)
                    onlineToggleBg.color = on
                        ? UIStyle.Accent with { a = 0.22f }
                        : UIStyle.Overlay;
            }
        }

        private string FindSnapshotEditLabel(ReplaySnapshot snapshot)
        {
            foreach (var route in PBManager.AllHistories())
            {
                for (int i = 0; i < route.Snapshots.Count; i++)
                {
                    if (route.Snapshots[i].SnapshotId == snapshot.SnapshotId)
                        return "PB #" + (i + 1) + " - " + route.Key.SceneName;
                }
            }
            return "Snapshot";
        }

        private static void AddSectionHeader(Transform parent, string title)
        {
            var row = MakeGO("Section_" + title, parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = UIStyle.H(22);
            MakeLbl(row.transform, title, UIStyle.FontSizeSm - 2,
                UIStyle.Subtext, TextAnchor.LowerLeft,
                x: UIStyle.W(8), w: UIStyle.W(200), h: UIStyle.H(22));
        }

        private static GameObject AddConfigRow(Transform parent, string label, int rowH, int labelW)
        {
            var row = MakeGO("Cfg_" + label, parent);
            Img(row, Color.clear);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = rowH;
            MakeLbl(row.transform, label, UIStyle.FontSizeSm - 1,
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
            if (nameInput == null) return;
            if (_nameRequest != null) return; // already saving

            string name = nameInput.text.Trim();

            // Don't save if unchanged
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

            GhostSettings.EnsureDeviceId();
            string json = "{\"display_name\":\"" + ApiJson.EscapeString(name) + "\"}";
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            _nameRequest = new UnityEngine.Networking.UnityWebRequest(
                GhostSettings.ApiBaseUrl + "/set-name", "POST");
            _nameRequest.uploadHandler =
                new UnityEngine.Networking.UploadHandlerRaw(body);
            _nameRequest.downloadHandler =
                new UnityEngine.Networking.DownloadHandlerBuffer();
            _nameRequest.SetRequestHeader("Content-Type",
                "application/json; charset=utf-8");
            _nameRequest.SetRequestHeader("X-Device-Id",
                GhostSettings.DeviceId);

#if V1221
            _nameRequest.Send();
#else
            _nameRequest.timeout = 10;
            _nameRequest.SendWebRequest();
#endif
        }

        /// <summary>
        /// Polls the in-flight name-save request. Called from Tick().
        /// </summary>
        private void TickNameSave()
        {
            if (_nameRequest == null || !_nameRequest.isDone) return;

            bool success;
#if V1221
            success = !_nameRequest.isError;
#else
            success = _nameRequest.result ==
                UnityEngine.Networking.UnityWebRequest.Result.Success;
#endif

            if (success)
            {
                string resp = _nameRequest.downloadHandler?.text ?? "";
                _nameRequest.Dispose();
                _nameRequest = null;

                // Parse the confirmed display_name from response
                string? confirmedName = ParseNameFromResponse(resp);
                if (confirmedName != null)
                {
                    _lastSavedName = confirmedName;
                    if (nameInput != null)
                        nameInput.text = confirmedName;
                    SetNameStatus("Saved!", UIStyle.Green);
                    OnDisplayNameSet?.Invoke(confirmedName);
                }
                else
                {
                    SetNameStatus("Unexpected response", UIStyle.Red);
                }
            }
            else
            {
                string err = _nameRequest.downloadHandler?.text
                    ?? _nameRequest.error ?? "";
                _nameRequest.Dispose();
                _nameRequest = null;

                string msg = ParseErrorFromResponse(err);
                SetNameStatus(msg, UIStyle.Red);
            }

            SetNameSaveEnabled(true);
        }

        private void SetNameStatus(string text, Color color)
        {
            if (nameStatusLbl != null)
            {
                nameStatusLbl.text = text;
                nameStatusLbl.color = color;
            }
        }

        private void SetNameSaveEnabled(bool enabled)
        {
            if (nameSaveBg != null)
                nameSaveBg.color = enabled ? UIStyle.Accent : UIStyle.Overlay;
            if (nameSaveLbl != null)
                nameSaveLbl.color = enabled ? UIStyle.Base : UIStyle.Subtext;
        }

        private static string? ParseNameFromResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int idx = json.IndexOf("\"display_name\"");
            if (idx < 0) return null;
            int q1 = json.IndexOf('"', json.IndexOf(':', idx + 14) + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            return q2 > q1 ? json.Substring(q1 + 1, q2 - q1 - 1) : null;
        }

        private static string ParseErrorFromResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "Connection failed. Try again.";
            int idx = response.IndexOf("\"error\"");
            if (idx >= 0)
            {
                int q1 = response.IndexOf('"', response.IndexOf(':', idx + 7) + 1);
                if (q1 >= 0)
                {
                    int q2 = response.IndexOf('"', q1 + 1);
                    if (q2 > q1)
                        return response.Substring(q1 + 1, q2 - q1 - 1);
                }
            }
            return "Connection failed. Try again.";
        }
    }
}