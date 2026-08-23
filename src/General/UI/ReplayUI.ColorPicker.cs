using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    /// <summary>
    /// Pointer-drag surface for the color picker's SV square and bars.
    /// Reports a normalized (0..1, 0..1) position on press/drag; onRelease
    /// fires when the pointer lifts. The picker only persists on release,
    /// so dragging never hammers the settings file / data store.
    /// </summary>
    internal sealed class PickerDrag : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public System.Action<float, float>? onValue;
        public System.Action? onRelease;

        public void OnPointerDown(PointerEventData e) => Report(e);
        public void OnDrag(PointerEventData e) => Report(e);
        public void OnPointerUp(PointerEventData e) => onRelease?.Invoke();

        private void Report(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, e.position, e.pressEventCamera, out lp))
                return;
            Rect r = rt.rect;
            onValue?.Invoke(
                Mathf.Clamp01((lp.x - r.xMin) / Mathf.Max(1f, r.width)),
                Mathf.Clamp01((lp.y - r.yMin) / Mathf.Max(1f, r.height)));
        }
    }

    public partial class ReplayUI
    {
        // ── Ghost color picker ─────────────────────────────────────────────
        //
        // One popup edits every ghost color in the mod. It opens anchored to
        // whichever swatch was clicked and edits that swatch's target
        // directly: the global color (Config tab chip) or a single run's
        // override (Runs tab swatch). Changes preview live inside the picker
        // and are committed (saved + applied) when a drag ends or a preset
        // is clicked; the ghost resolves its color every rendered frame, so
        // committed values show in-game immediately.
        //
        // Built lazily once per canvas (like the tooltip) and reused. A
        // full-screen transparent scrim behind it catches outside clicks to
        // close, and blocks the panel while open.

        private static readonly Color[] GhostColorPresets =
        {
            new Color(1.00f, 1.00f, 1.00f),
            new Color(0.40f, 0.80f, 1.00f),
            new Color(0.93f, 0.83f, 0.62f),
            new Color(0.40f, 0.85f, 0.40f),
            new Color(0.93f, 0.53f, 0.59f),
            new Color(0.75f, 0.55f, 1.00f),
        };

        private const float DefaultGhostAlpha = 0.4f; // GhostSettings default

        private GameObject? _pickerGO;
        private GameObject? _pickerScrim;
        private Text? _pickerContextLbl;
        private Text? _pickerAlphaValueLbl;
        private Image? _pickerPreviewFill;
        private RawImage? _pickerSVImg;
        private RawImage? _pickerAlphaImg;
        private RectTransform? _pickerSVRect;
        private RectTransform? _pickerHueRect;
        private RectTransform? _pickerAlphaRect;
        private RectTransform? _pickerSVHandle;
        private RectTransform? _pickerHueHandle;
        private RectTransform? _pickerAlphaHandle;
        private GameObject? _pickerActionGO;
        private Text? _pickerActionLbl;
        private Texture2D? _pickerSVTex;
        private Texture2D? _pickerHueTex;
        private Texture2D? _pickerAlphaTex;

        // The footer action ("Use global color" / "Reset to default") is
        // hidden when it doesn't apply; the panel shrinks with it so there's
        // never a dead gap at the bottom.
        private int _pickerWidth;
        private int pickerHeight;        // current (matches action visibility)
        private int _pickerHeightFull;    // with footer action
        private int _pickerHeightCompact; // without footer action

        // Working color (HSV + alpha) and edit target
        private float _pickerHue, _pickerSat, _pickerVal, _pickerA;
        private bool _pickerIsGlobal;
        private RoomKey _pickerKey;
        private string? _pickerSnapshotId;

        // ── Opening / closing ──────────────────────────────────────────────

        private void OpenGlobalColorPicker(GameObject anchor)
        {
            EnsurePicker();
            _pickerIsGlobal = true;
            _pickerSnapshotId = null;

            if (_pickerContextLbl != null)
                _pickerContextLbl.text = "Global ghost color";
            SetPickerAction("Reset to default");

            LoadPickerColor(GhostSettings.GhostColor);
            ShowPicker(anchor);
        }

        private void OpenSnapshotColorPicker(GameObject anchor,
            RoomKey key, string snapshotId)
        {
            var snapshot = PBManager.GetSnapshot(key, snapshotId);
            if (snapshot == null) return;

            EnsurePicker();
            _pickerIsGlobal = false;
            _pickerKey = key;
            _pickerSnapshotId = snapshotId;

            UpdatePickerContext(snapshot.HasVisualOverride);

            LoadPickerColor(snapshot.ResolveGhostColor(GhostSettings.GhostColor));
            ShowPicker(anchor);
        }

        /// <summary>Closes the picker if open. Safe to call any time; hooked
        /// into panel close, tab switches, scene selection, and unpause.</summary>
        private void ClosePicker()
        {
            if (_pickerScrim != null) _pickerScrim.SetActive(false);
            if (_pickerGO != null) _pickerGO.SetActive(false);
            _pickerSnapshotId = null;
            GhostSettings.Flush(); // slider drags save throttled; commit now
        }

        private void ShowPicker(GameObject anchor)
        {
            if (_pickerGO == null || _pickerScrim == null) return;

            // Keep both on top of everything else on the canvas
            _pickerScrim.transform.SetAsLastSibling();
            _pickerGO.transform.SetAsLastSibling();
            _pickerScrim.SetActive(true);
            _pickerGO.SetActive(true);

            // Anchor beside the clicked swatch, clamped on-screen. The canvas
            // is ScreenSpaceOverlay + ConstantPixelSize, so a transform's
            // world position is its screen position and the picker uses the
            // same bottom-left/top-left-pivot convention as the tooltip.
            var art = anchor.GetComponent<RectTransform>();
            Vector3 p = art.position; // pivot (top-left) in screen px
            float x = p.x + art.rect.width + UIStyle.W(10);
            float yTop = p.y + UIStyle.H(6);
            x = Mathf.Clamp(x, 4, Screen.width - _pickerWidth - 4);
            yTop = Mathf.Clamp(yTop, pickerHeight + 4, Screen.height - 4);
            _pickerGO.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(x, yTop);
        }

        // ── Working-color state ────────────────────────────────────────────

        private void LoadPickerColor(Color c)
        {
            Color.RGBToHSV(c, out _pickerHue, out _pickerSat, out _pickerVal);
            _pickerA = c.a;
            RegenSVTexture();
            UpdatePickerVisuals();
        }

        private Color PickerColor()
        {
            Color c = Color.HSVToRGB(_pickerHue, _pickerSat, _pickerVal);
            c.a = _pickerA;
            return c;
        }

        private void UpdatePickerContext(bool hasOverride)
        {
            if (_pickerContextLbl != null)
                _pickerContextLbl.text = hasOverride
                    ? "Custom for this run"
                    : "Following global color";
            // "Use global color" only makes sense once an override exists
            SetPickerAction(hasOverride ? "Use global color" : null);
        }

        /// <summary>Shows/hides the footer action button and resizes the
        /// panel so a hidden action never leaves an empty gap.</summary>
        private void SetPickerAction(string? label)
        {
            if (_pickerGO == null || _pickerActionGO == null) return;

            _pickerActionGO.SetActive(label != null);
            if (label != null && _pickerActionLbl != null)
                _pickerActionLbl.text = label;

            pickerHeight = label != null ? _pickerHeightFull : _pickerHeightCompact;
            var rt = _pickerGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(_pickerWidth, pickerHeight);

            // If the panel grew while open (override created near the bottom
            // of the screen), keep its bottom edge on-screen.
            if (_pickerGO.activeSelf)
            {
                Vector2 pos = rt.anchoredPosition;
                pos.y = Mathf.Max(pos.y, pickerHeight + 4);
                rt.anchoredPosition = pos;
            }
        }

        // ── Input handlers ─────────────────────────────────────────────────

        private void OnPickerSV(float nx, float ny)
        {
            _pickerSat = nx;
            _pickerVal = ny;
            UpdatePickerVisuals();
        }

        private void OnPickerHue(float nx, float _)
        {
            _pickerHue = Mathf.Min(nx, 0.999f); // hue 1.0 aliases back to 0
            RegenSVTexture();
            UpdatePickerVisuals();
        }

        private void OnPickerAlpha(float nx, float _)
        {
            _pickerA = Mathf.Round(nx * 100f) / 100f;
            UpdatePickerVisuals();
        }

        private void OnPickerPreset(Color rgb)
        {
            Color.RGBToHSV(rgb, out _pickerHue, out _pickerSat, out _pickerVal);
            RegenSVTexture();
            UpdatePickerVisuals();
            CommitPickerColor();
        }

        private void OnPickerAction()
        {
            if (_pickerIsGlobal)
            {
                LoadPickerColor(new Color(1f, 1f, 1f, DefaultGhostAlpha));
                CommitPickerColor();
            }
            else if (_pickerSnapshotId != null)
            {
                // Drop the override; the run follows the global color again
                PBManager.UpdateSnapshotVisuals(_pickerKey, _pickerSnapshotId,
                    false, GhostSettings.GhostColor);
                LoadPickerColor(GhostSettings.GhostColor);
                UpdatePickerContext(false);
                RefreshAfterPickerCommit();
            }
        }

        // ── Commit ─────────────────────────────────────────────────────────

        private void CommitPickerColor()
        {
            Color c = PickerColor();
            if (_pickerIsGlobal)
            {
                GhostSettings.GhostColor = c;
            }
            else if (_pickerSnapshotId != null)
            {
                PBManager.UpdateSnapshotVisuals(_pickerKey, _pickerSnapshotId,
                    true, c);
                UpdatePickerContext(true);
            }
            RefreshAfterPickerCommit();
        }

        private void RefreshAfterPickerCommit()
        {
            UpdatePickerVisuals();
            if (_pickerIsGlobal)
            {
                RefreshGhostColorChip();
            }
            else if (_activeTab == TabKind.Runs
                && _selectedScene == _pickerKey.SceneName)
            {
                // Update the row swatch (the picker itself survives rebuilds:
                // it lives on the canvas root, not in the content area)
                RebuildRunsContentOnly();
            }
        }

        /// <summary>Syncs the Config tab's "Ghost color" chip with the
        /// current global setting (no-op when the chip isn't built).</summary>
        private void RefreshGhostColorChip()
        {
            Color g = GhostSettings.GhostColor;
            if (_cfgGhostColorFill != null)
                _cfgGhostColorFill.color = new Color(g.r, g.g, g.b, 1f);
            if (_cfgGhostAlphaLbl != null)
                _cfgGhostAlphaLbl.text = "alpha " + g.a.ToString("0.00");
        }

        // ── Visual sync ────────────────────────────────────────────────────

        private void UpdatePickerVisuals()
        {
            Color rgb = Color.HSVToRGB(_pickerHue, _pickerSat, _pickerVal);

            if (_pickerSVHandle != null && _pickerSVRect != null)
                _pickerSVHandle.anchoredPosition = new Vector2(
                    _pickerSat * _pickerSVRect.rect.width,
                    _pickerVal * _pickerSVRect.rect.height);

            if (_pickerHueHandle != null && _pickerHueRect != null)
                _pickerHueHandle.anchoredPosition = new Vector2(
                    _pickerHue * _pickerHueRect.rect.width,
                    _pickerHueRect.rect.height / 2f);

            if (_pickerAlphaHandle != null && _pickerAlphaRect != null)
                _pickerAlphaHandle.anchoredPosition = new Vector2(
                    _pickerA * _pickerAlphaRect.rect.width,
                    _pickerAlphaRect.rect.height / 2f);

            // Alpha bar gradient is a white alpha ramp tinted by the color
            if (_pickerAlphaImg != null)
                _pickerAlphaImg.color = new Color(rgb.r, rgb.g, rgb.b, 1f);

            if (_pickerPreviewFill != null)
                _pickerPreviewFill.color = new Color(rgb.r, rgb.g, rgb.b, 1f);

            if (_pickerAlphaValueLbl != null)
                _pickerAlphaValueLbl.text = _pickerA.ToString("0.00");
        }

        // ── Textures ───────────────────────────────────────────────────────

        private const int SVTexSize = 48;

        private void RegenSVTexture()
        {
            if (_pickerSVTex == null)
            {
                _pickerSVTex = new Texture2D(SVTexSize, SVTexSize,
                    TextureFormat.RGBA32, false);
                _pickerSVTex.wrapMode = TextureWrapMode.Clamp;
                if (_pickerSVImg != null) _pickerSVImg.texture = _pickerSVTex;
            }
            var px = new Color[SVTexSize * SVTexSize];
            for (int y = 0; y < SVTexSize; y++)
            {
                float v = y / (float)(SVTexSize - 1);
                for (int x = 0; x < SVTexSize; x++)
                    px[y * SVTexSize + x] = Color.HSVToRGB(_pickerHue,
                        x / (float)(SVTexSize - 1), v);
            }
            _pickerSVTex.SetPixels(px);
            _pickerSVTex.Apply();
        }

        private Texture2D EnsureHueTexture()
        {
            if (_pickerHueTex == null)
            {
                const int n = 128;
                _pickerHueTex = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                _pickerHueTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n];
                for (int x = 0; x < n; x++)
                    px[x] = Color.HSVToRGB(x / (float)(n - 1), 1f, 1f);
                _pickerHueTex.SetPixels(px);
                _pickerHueTex.Apply();
            }
            return _pickerHueTex;
        }

        private Texture2D EnsureAlphaTexture()
        {
            if (_pickerAlphaTex == null)
            {
                const int n = 64;
                _pickerAlphaTex = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                _pickerAlphaTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n];
                for (int x = 0; x < n; x++)
                    px[x] = new Color(1f, 1f, 1f, x / (float)(n - 1));
                _pickerAlphaTex.SetPixels(px);
                _pickerAlphaTex.Apply();
            }
            return _pickerAlphaTex;
        }

        // ── Construction ───────────────────────────────────────────────────

        private void EnsurePicker()
        {
            if (_pickerGO != null) return;
            if (_canvasGO == null) return;

            // Scrim first so the picker draws above it. Transparent but
            // raycast-blocking: clicking anywhere outside the picker closes
            // it, and the panel behind can't be interacted with meanwhile.
            _pickerScrim = MakeGO("PickerScrim", _canvasGO.transform);
            var scrimImg = _pickerScrim.AddComponent<Image>();
            scrimImg.color = Color.clear;
            Btn(_pickerScrim, ClosePicker);
            Fill(_pickerScrim);

            _pickerGO = MakeGO("ColorPicker", _canvasGO.transform);

            int pad = UIStyle.W(12);
            int pw = UIStyle.W(214);
            int innerW = pw - pad * 2;

            // 1px frame, same construction as the tooltip
            var borderImg = _pickerGO.AddComponent<Image>();
            borderImg.color = UIStyle.Overlay with { a = 0.9f };

            var rt = _pickerGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero; // bottom-left anchored
            rt.pivot = new Vector2(0f, 1f);             // position = top-left

            var innerBg = MakeGO("Inner", _pickerGO.transform);
            var innerImg = innerBg.AddComponent<Image>();
            innerImg.color = UIStyle.Base with { a = 0.98f };
            var innerRt = innerBg.GetComponent<RectTransform>();
            innerRt.anchorMin = Vector2.zero;
            innerRt.anchorMax = Vector2.one;
            innerRt.offsetMin = new Vector2(1, 1);
            innerRt.offsetMax = new Vector2(-1, -1);

            int y = UIStyle.H(10);

            // Header, one row: status text | preview chip | Close.
            // The status doubles as the title ("Global ghost color",
            // "Following global color", "Custom for this run") — the picker
            // opens anchored to the swatch it edits, so it needs no more.
            int hdrH = UIStyle.H(18);
            int closeW = UIStyle.W(44);
            int prevS = hdrH;

            _pickerContextLbl = MakeLbl(_pickerGO.transform, "",
                UIStyle.FontSizeTiny, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: pad, y: y, w: innerW - prevS - closeW - UIStyle.Gap * 2,
                h: hdrH);

            var preview = MakeGO("Preview", _pickerGO.transform);
            Img(preview, UIStyle.Overlay with { a = 0.9f });
            Rect(preview, pw - pad - closeW - UIStyle.Gap - prevS, y, prevS, hdrH);
            var prevFill = MakeGO("Fill", preview.transform);
            Img(prevFill, Color.white);
            Rect(prevFill, 1, 1, prevS - 2, hdrH - 2);
            prevFill.GetComponent<Graphic>().raycastTarget = false;
            _pickerPreviewFill = prevFill.GetComponent<Image>();

            MakeButton(_pickerGO.transform, "Close", "Close",
                UIStyle.FontSizeBtn, UIStyle.Text, UIStyle.Overlay with { a = 0.6f },
                pw - pad - closeW, y, closeW, hdrH, ClosePicker);
            y += hdrH + UIStyle.H(8);

            int svH = UIStyle.H(96);
            _pickerSVRect = AddPickerSurface(_pickerGO.transform, "SV",
                pad, y, innerW, svH, out _pickerSVImg,
                OnPickerSV, CommitPickerColor);
            RegenSVTexture();
            _pickerSVImg.texture = _pickerSVTex;
            _pickerSVHandle = MakePickerMarker(_pickerSVRect,
                UIStyle.H(10), UIStyle.H(10));
            y += svH + UIStyle.H(8);

            int barH = UIStyle.H(12);
            _pickerHueRect = AddPickerSurface(_pickerGO.transform, "Hue",
                pad, y, innerW, barH, out RawImage hueImg,
                OnPickerHue, CommitPickerColor);
            hueImg.texture = EnsureHueTexture();
            _pickerHueHandle = MakePickerMarker(_pickerHueRect,
                UIStyle.W(5), barH + UIStyle.H(4));
            y += barH + UIStyle.H(8);

            // Alpha bar (dark base behind a tinted alpha ramp) + value
            int alphaLblW = UIStyle.W(40);
            int alphaBarW = innerW - alphaLblW - UIStyle.Gap;
            _pickerAlphaRect = AddPickerSurface(_pickerGO.transform, "Alpha",
                pad, y, alphaBarW, barH, out _pickerAlphaImg,
                OnPickerAlpha, CommitPickerColor);
            _pickerAlphaImg.texture = EnsureAlphaTexture();
            _pickerAlphaHandle = MakePickerMarker(_pickerAlphaRect,
                UIStyle.W(5), barH + UIStyle.H(4));
            _pickerAlphaValueLbl = MakeLbl(_pickerGO.transform, "0.40",
                UIStyle.FontSizeBtn, UIStyle.Text, TextAnchor.MiddleRight,
                x: pad + alphaBarW + UIStyle.Gap, y: y,
                w: alphaLblW, h: barH);
            y += barH + UIStyle.H(10);

            // Preset swatches, distributed evenly across the full width so
            // the row lines up flush with the bars above it
            int swS = UIStyle.H(16);
            int swStep = (innerW - swS) / (GhostColorPresets.Length - 1);
            for (int i = 0; i < GhostColorPresets.Length; i++)
            {
                Color c = GhostColorPresets[i];
                var sw = MakeGO("Preset", _pickerGO.transform);
                Img(sw, UIStyle.Overlay with { a = 0.9f });
                Btn(sw, () => OnPickerPreset(c));
                Rect(sw, pad + i * swStep, y, swS, swS);
                var fill = MakeGO("Fill", sw.transform);
                Img(fill, c);
                Rect(fill, 1, 1, swS - 2, swS - 2);
                fill.GetComponent<Graphic>().raycastTarget = false;
                AddButtonHover(sw);
            }
            y += swS + UIStyle.H(10);

            // Footer action: "Use global color" (run override) or
            // "Reset to default" (global). Hidden — and the panel shortened —
            // when neither applies (run still following global).
            _pickerHeightCompact = y;
            int actH = UIStyle.H(20);
            var actRef = MakeButton(_pickerGO.transform, "PickerAction", "",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                pad, y, innerW, actH, OnPickerAction);
            _pickerActionGO = actRef.bg.gameObject;
            _pickerActionLbl = actRef.label;
            _pickerHeightFull = y + actH + UIStyle.H(10);

            _pickerWidth = pw;
            pickerHeight = _pickerHeightFull;
            rt.sizeDelta = new Vector2(pw, pickerHeight);

            _pickerScrim.SetActive(false);
            _pickerGO.SetActive(false);
        }

        /// <summary>Bordered draggable RawImage surface (SV square / bars).
        /// A dark backing sits behind the texture so alpha gradients read.</summary>
        private static RectTransform AddPickerSurface(Transform parent,
            string name, int x, int y, int w, int h, out RawImage img,
            System.Action<float, float> onValue, System.Action onRelease)
        {
            var border = MakeGO(name + "Border", parent);
            Img(border, UIStyle.Overlay with { a = 0.9f });
            Rect(border, x, y, w, h);
            border.GetComponent<Graphic>().raycastTarget = false;

            var back = MakeGO("Back", border.transform);
            Img(back, UIStyle.Base);
            Rect(back, 1, 1, w - 2, h - 2);
            back.GetComponent<Graphic>().raycastTarget = false;

            var surf = MakeGO(name, border.transform);
            img = surf.AddComponent<RawImage>();
            Rect(surf, 1, 1, w - 2, h - 2);

            var drag = surf.AddComponent<PickerDrag>();
            drag.onValue = (nx, ny) => onValue(nx, ny);
            drag.onRelease = () => onRelease();

            return surf.GetComponent<RectTransform>();
        }

        /// <summary>Small light-on-dark position marker, center-pivoted and
        /// bottom-left anchored so anchoredPosition = (nx*w, ny*h).</summary>
        private static RectTransform MakePickerMarker(Transform parent,
            float w, float h)
        {
            var go = MakeGO("Marker", parent);
            var img = go.AddComponent<Image>();
            img.color = UIStyle.Base with { a = 0.95f };
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);

            var inner = MakeGO("Inner", go.transform);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = UIStyle.Text;
            innerImg.raycastTarget = false;
            var irt = inner.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(1, 1);
            irt.offsetMax = new Vector2(-1, -1);
            return rt;
        }
    }
}
