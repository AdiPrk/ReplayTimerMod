using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    /// <summary>
    /// Modal dialog for entering/changing the display name.
    /// Self-contained: creates its own Canvas, manages its own HTTP request,
    /// and destroys itself when done.
    ///
    /// Usage:
    ///   var dialog = new NameEntryDialog();
    ///   dialog.Show(apiBaseUrl, deviceId, currentName,
    ///       onSuccess: name => { ... },
    ///       onCancel:  () => { ... });
    ///   // Call dialog.Tick() every frame while visible.
    ///
    /// Works before NetworkClient.Start() — uses its own UnityWebRequest.
    /// </summary>
    internal sealed class NameEntryDialog
    {
        private GameObject? _root;
        private InputField? _input;
        private Text? _errorLbl;
        private Text? _charCountLbl;
        private Text? _statusLbl;
        private Button? _confirmBtn;
        private CanvasGroup? _inputGroup;

        private UnityWebRequest? _request;
        private string _apiBaseUrl = "";
        private string _deviceId = "";
        private Action<string>? _onSuccess;
        private Action? _onCancel;

        public bool IsVisible => _root != null;

        public void Show(string apiBaseUrl, string deviceId, string currentName,
            Action<string> onSuccess, Action onCancel)
        {
            if (_root != null) return; // already visible

            _apiBaseUrl = apiBaseUrl;
            _deviceId = deviceId;
            _onSuccess = onSuccess;
            _onCancel = onCancel;

            BuildUI(currentName ?? "");
        }

        /// <summary>
        /// Call every frame while visible. Polls the in-flight HTTP request.
        /// </summary>
        public void Tick()
        {
            if (_request == null || !_request.isDone) return;

            bool success;
#if V1221
            success = !_request.isError;
#else
            success = _request.result == UnityWebRequest.Result.Success;
#endif

            if (success)
            {
                string body = _request.downloadHandler?.text ?? "";
                _request.Dispose();
                _request = null;

                // Parse display_name from response
                string? name = ParseDisplayName(body);
                if (name != null)
                {
                    var cb = _onSuccess;
                    Dismiss();
                    cb?.Invoke(name);
                }
                else
                {
                    SetError("Unexpected server response.");
                    SetInputEnabled(true);
                }
            }
            else
            {
                string err = _request.downloadHandler?.text ?? _request.error ?? "";
                _request.Dispose();
                _request = null;

                // Try to extract error message from JSON response
                string msg = ParseErrorMessage(err);
                SetError(msg);
                SetInputEnabled(true);
            }
        }

        public void Dismiss()
        {
            if (_request != null)
            {
                try { _request.Abort(); } catch { }
                try { _request.Dispose(); } catch { }
                _request = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }

            _input = null;
            _errorLbl = null;
            _charCountLbl = null;
            _statusLbl = null;
            _confirmBtn = null;
            _inputGroup = null;
        }

        // ── UI construction ────────────────────────────────────────────────

        private void BuildUI(string initialText)
        {
            // Standalone Canvas at sort order 999 (above everything)
            _root = new GameObject("NameEntryDialog");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            _root.AddComponent<CanvasScaler>();
            _root.AddComponent<GraphicRaycaster>();

            // Dark overlay
            var overlay = MakeChild("Overlay", _root.transform);
            var overlayImg = overlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.7f);
            FillParent(overlay);
            // Clicking overlay = cancel
            var overlayBtn = overlay.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(OnCancel);

            // Dialog panel
            int pw = UIStyle.W(400);
            int ph = UIStyle.H(260);
            var panel = MakeChild("Panel", overlay.transform);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = UIStyle.Base;
            CenterRect(panel, pw, ph);
            // Stop clicks on panel from reaching overlay
            panel.AddComponent<Button>().transition = Selectable.Transition.None;

            int pad = UIStyle.H(16);
            int y = pad;
            int innerW = pw - pad * 2;
            int rowH = UIStyle.H(28);
            int gap = UIStyle.H(8);

            // Title
            var title = MakeLabel(panel.transform, "Set Display Name",
                UIStyle.FontSizeLg, UIStyle.Text, TextAnchor.MiddleCenter);
            SetRect(title.gameObject, pad, y, innerW, rowH);
            y += rowH + gap;

            // Subtitle
            var sub = MakeLabel(panel.transform,
                "Choose a name for leaderboards. " + NameValidator.MinLength
                + "–" + NameValidator.MaxLength + " chars, letters & numbers.",
                UIStyle.FontSizeSm - 1, UIStyle.Subtext, TextAnchor.MiddleCenter);
            SetRect(sub.gameObject, pad, y, innerW, rowH);
            y += rowH + gap;

            // Input field
            int inputH = UIStyle.H(32);
            var inputGO = MakeChild("InputField", panel.transform);
            var inputBg = inputGO.AddComponent<Image>();
            inputBg.color = UIStyle.Surface;
            SetRect(inputGO, pad, y, innerW, inputH);

            _inputGroup = inputGO.AddComponent<CanvasGroup>();

            // Text child
            var textGO = MakeChild("Text", inputGO.transform);
            var textComp = textGO.AddComponent<Text>();
            textComp.font = UIStyle.Arial;
            textComp.fontSize = UIStyle.FontSizeSm;
            textComp.color = UIStyle.Text;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.supportRichText = false;
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(UIStyle.H(8), 2);
            textRT.offsetMax = new Vector2(-UIStyle.H(8), -2);

            // Placeholder child
            var placeholderGO = MakeChild("Placeholder", inputGO.transform);
            var phText = placeholderGO.AddComponent<Text>();
            phText.font = UIStyle.Arial;
            phText.fontSize = UIStyle.FontSizeSm;
            phText.color = UIStyle.Subtext with { a = 0.5f };
            phText.fontStyle = FontStyle.Italic;
            phText.alignment = TextAnchor.MiddleLeft;
            phText.text = "Enter your name...";
            var phRT = placeholderGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(UIStyle.H(8), 2);
            phRT.offsetMax = new Vector2(-UIStyle.H(8), -2);

            // InputField component
            _input = inputGO.AddComponent<InputField>();
            _input.textComponent = textComp;
            _input.placeholder = phText;
            _input.text = initialText;
            _input.characterLimit = NameValidator.MaxLength;
            _input.onValueChanged.AddListener(OnInputChanged);
            _input.onEndEdit.AddListener(OnEndEdit);

            y += inputH + gap / 2;

            // Character count (right-aligned)
            _charCountLbl = MakeLabel(panel.transform,
                initialText.Length + "/" + NameValidator.MaxLength,
                UIStyle.FontSizeSm - 2, UIStyle.Subtext,
                TextAnchor.MiddleRight);
            SetRect(_charCountLbl.gameObject, pad, y, innerW, UIStyle.H(18));
            y += UIStyle.H(18) + gap / 2;

            // Error label
            _errorLbl = MakeLabel(panel.transform, "",
                UIStyle.FontSizeSm - 1, UIStyle.Red, TextAnchor.MiddleCenter);
            SetRect(_errorLbl.gameObject, pad, y, innerW, rowH);
            y += rowH + gap;

            // Buttons row
            int btnW = UIStyle.W(120);
            int btnH = UIStyle.H(30);
            int btnGap = UIStyle.W(16);
            int btnRowW = btnW * 2 + btnGap;
            int btnX = (pw - btnRowW) / 2;

            // Cancel button
            var cancelGO = MakeChild("Cancel", panel.transform);
            var cancelBg = cancelGO.AddComponent<Image>();
            cancelBg.color = UIStyle.Surface;
            SetRect(cancelGO, btnX, y, btnW, btnH);
            var cancelLbl = MakeLabel(cancelGO.transform, "Cancel",
                UIStyle.FontSizeSm, UIStyle.Subtext, TextAnchor.MiddleCenter);
            FillParent(cancelLbl.gameObject);
            var cancelBtn = cancelGO.AddComponent<Button>();
            cancelBtn.onClick.AddListener(OnCancel);

            // Confirm button
            var confirmGO = MakeChild("Confirm", panel.transform);
            var confirmBg = confirmGO.AddComponent<Image>();
            confirmBg.color = UIStyle.Accent;
            SetRect(confirmGO, btnX + btnW + btnGap, y, btnW, btnH);
            var confirmLbl = MakeLabel(confirmGO.transform, "Confirm",
                UIStyle.FontSizeSm, UIStyle.Base, TextAnchor.MiddleCenter);
            FillParent(confirmLbl.gameObject);
            _confirmBtn = confirmGO.AddComponent<Button>();
            _confirmBtn.onClick.AddListener(OnConfirm);

            // Status label (shows "Saving..." during HTTP)
            y += btnH + gap / 2;
            _statusLbl = MakeLabel(panel.transform, "",
                UIStyle.FontSizeSm - 2, UIStyle.Subtext, TextAnchor.MiddleCenter);
            SetRect(_statusLbl.gameObject, pad, y, innerW, UIStyle.H(18));

            // Focus the input field
            _input.ActivateInputField();
            _input.Select();

            // Run initial validation
            OnInputChanged(initialText);
        }

        // ── Event handlers ─────────────────────────────────────────────────

        private void OnInputChanged(string text)
        {
            if (_charCountLbl != null)
                _charCountLbl.text = text.Length + "/" + NameValidator.MaxLength;

            // Clear error on typing
            if (_errorLbl != null && !string.IsNullOrEmpty(_errorLbl.text))
                _errorLbl.text = "";
        }

        private void OnEndEdit(string text)
        {
            // Submit on Enter key
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                OnConfirm();
        }

        private void OnConfirm()
        {
            if (_input == null) return;
            string name = _input.text.Trim();

            var check = NameValidator.Validate(name);
            if (!check.Valid)
            {
                SetError(check.Error);
                return;
            }
            // Use the canonical form so what we send matches what passed validation.
            name = check.Canonical;

            SetError("");
            SetInputEnabled(false);
            if (_statusLbl != null) _statusLbl.text = "Saving...";

            // Send HTTP request. Use ApiJson.EscapeString (handles control
            // chars) rather than the local minimal escaper.
            string json = "{\"display_name\":\"" + ApiJson.EscapeString(name) + "\"}";
            byte[] body = Encoding.UTF8.GetBytes(json);

            _request = new UnityWebRequest(_apiBaseUrl + "/set-name", "POST");
            _request.uploadHandler = new UploadHandlerRaw(body);
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            _request.SetRequestHeader("X-Device-Id", _deviceId);

#if V1221
            _request.Send();
#else
            _request.timeout = 10;
            _request.SendWebRequest();
#endif
        }

        private void OnCancel()
        {
            var cb = _onCancel;
            Dismiss();
            cb?.Invoke();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void SetError(string msg)
        {
            if (_errorLbl != null) _errorLbl.text = msg;
            if (_statusLbl != null) _statusLbl.text = "";
        }

        private void SetInputEnabled(bool enabled)
        {
            if (_inputGroup != null)
            {
                _inputGroup.interactable = enabled;
                _inputGroup.alpha = enabled ? 1f : 0.5f;
            }
            if (_confirmBtn != null)
                _confirmBtn.interactable = enabled;
        }

        private static string? ParseDisplayName(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int idx = json.IndexOf("\"display_name\"");
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx + 14);
            if (colon < 0) return null;
            int qStart = json.IndexOf('"', colon + 1);
            if (qStart < 0) return null;
            int qEnd = json.IndexOf('"', qStart + 1);
            if (qEnd < 0) return null;
            return json.Substring(qStart + 1, qEnd - qStart - 1);
        }

        private static string ParseErrorMessage(string response)
        {
            // Try to extract "error" field from JSON response
            if (string.IsNullOrEmpty(response))
                return "Connection failed. Please try again.";

            int idx = response.IndexOf("\"error\"");
            if (idx >= 0)
            {
                int colon = response.IndexOf(':', idx + 7);
                if (colon >= 0)
                {
                    int qStart = response.IndexOf('"', colon + 1);
                    if (qStart >= 0)
                    {
                        int qEnd = response.IndexOf('"', qStart + 1);
                        if (qEnd >= 0)
                            return response.Substring(qStart + 1, qEnd - qStart - 1);
                    }
                }
            }

            return "Connection failed. Please try again.";
        }

        // ── UI construction helpers ────────────────────────────────────────

        private static GameObject MakeChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static Text MakeLabel(Transform parent, string text,
            int fontSize, Color color, TextAnchor alignment)
        {
            var go = MakeChild("Lbl", parent);
            var t = go.AddComponent<Text>();
            t.font = UIStyle.Arial;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = alignment;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void SetRect(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void CenterRect(GameObject go, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}