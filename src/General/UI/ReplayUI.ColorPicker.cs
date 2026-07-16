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

        private GameObject? pickerGO;
        private GameObject? pickerScrim;
        private Text? pickerContextLbl;
        private Text? pickerAlphaValueLbl;
        private Image? pickerPreviewFill;
        private RawImage? pickerSVImg;
        private RawImage? pickerAlphaImg;
        private RectTransform? pickerSVRect;
        private RectTransform? pickerHueRect;
        private RectTransform? pickerAlphaRect;
        private RectTransform? pickerSVHandle;
        private RectTransform? pickerHueHandle;
        private RectTransform? pickerAlphaHandle;
        private GameObject? pickerActionGO;
        private Text? pickerActionLbl;
        private Texture2D? pickerSVTex;
        private Texture2D? pickerHueTex;
        private Texture2D? pickerAlphaTex;

        // The footer action ("Use global color" / "Reset to default") is
        // hidden when it doesn't apply; the panel shrinks with it so there's
        // never a dead gap at the bottom.
        private int pickerWidth;
        private int pickerHeight;        // current (matches action visibility)
        private int pickerHeightFull;    // with footer action
        private int pickerHeightCompact; // without footer action

        // Working color (HSV + alpha) and edit target
        private float pickerHue, pickerSat, pickerVal, pickerA;
        private bool pickerIsGlobal;
        private RoomKey pickerKey;
        private string? pickerSnapshotId;

        // ── Opening / closing ──────────────────────────────────────────────

        private void OpenGlobalColorPicker(GameObject anchor)
        {
            EnsurePicker();
            pickerIsGlobal = true;
            pickerSnapshotId = null;

            if (pickerContextLbl != null)
                pickerContextLbl.text = "Global ghost color";
            SetPickerAction("Reset to default");

            LoadPickerColor(GhostSettings.GhostColor);
            ShowPicker(anchor);
        }

        private void OpenSnapshotColorPicker(GameObject anchor,
            RoomKey key, string snapshotId, int index)
        {
            var snapshot = PBManager.GetSnapshot(key, snapshotId);
            if (snapshot == null) return;

            EnsurePicker();
            pickerIsGlobal = false;
            pickerKey = key;
            pickerSnapshotId = snapshotId;

            UpdatePickerContext(snapshot.HasVisualOverride);

            LoadPickerColor(snapshot.ResolveGhostColor(GhostSettings.GhostColor));
            ShowPicker(anchor);
        }

        /// <summary>Closes the picker if open. Safe to call any time; hooked
        /// into panel close, tab switches, scene selection, and unpause.</summary>
        private void ClosePicker()
        {
            if (pickerScrim != null) pickerScrim.SetActive(false);
            if (pickerGO != null) pickerGO.SetActive(false);
            pickerSnapshotId = null;
        }

        private void ShowPicker(GameObject anchor)
        {
            if (pickerGO == null || pickerScrim == null) return;

            // Keep both on top of everything else on the canvas
            pickerScrim.transform.SetAsLastSibling();
            pickerGO.transform.SetAsLastSibling();
            pickerScrim.SetActive(true);
            pickerGO.SetActive(true);

            // Anchor beside the clicked swatch, clamped on-screen. The canvas
            // is ScreenSpaceOverlay + ConstantPixelSize, so a transform's
            // world position is its screen position and the picker uses the
            // same bottom-left/top-left-pivot convention as the tooltip.
            var art = anchor.GetComponent<RectTransform>();
            Vector3 p = art.position; // pivot (top-left) in screen px
            float x = p.x + art.rect.width + UIStyle.W(10);
            float yTop = p.y + UIStyle.H(6);
            x = Mathf.Clamp(x, 4, Screen.width - pickerWidth - 4);
            yTop = Mathf.Clamp(yTop, pickerHeight + 4, Screen.height - 4);
            pickerGO.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(x, yTop);
        }

        // ── Working-color state ────────────────────────────────────────────

        private void LoadPickerColor(Color c)
        {
            Color.RGBToHSV(c, out pickerHue, out pickerSat, out pickerVal);
            pickerA = c.a;
            RegenSVTexture();
            UpdatePickerVisuals();
        }

        private Color PickerColor()
        {
            Color c = Color.HSVToRGB(pickerHue, pickerSat, pickerVal);
            c.a = pickerA;
            return c;
        }

        private void UpdatePickerContext(bool hasOverride)
        {
            if (pickerContextLbl != null)
                pickerContextLbl.text = hasOverride
                    ? "Custom for this run"
                    : "Following global color";
            // "Use global color" only makes sense once an override exists
            SetPickerAction(hasOverride ? "Use global color" : null);
        }

        /// <summary>Shows/hides the footer action button and resizes the
        /// panel so a hidden action never leaves an empty gap.</summary>
        private void SetPickerAction(string? label)
        {
            if (pickerGO == null || pickerActionGO == null) return;

            pickerActionGO.SetActive(label != null);
            if (label != null && pickerActionLbl != null)
                pickerActionLbl.text = label;

            pickerHeight = label != null ? pickerHeightFull : pickerHeightCompact;
            var rt = pickerGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(pickerWidth, pickerHeight);

            // If the panel grew while open (override created near the bottom
            // of the screen), keep its bottom edge on-screen.
            if (pickerGO.activeSelf)
            {
                Vector2 pos = rt.anchoredPosition;
                pos.y = Mathf.Max(pos.y, pickerHeight + 4);
                rt.anchoredPosition = pos;
            }
        }

        // ── Input handlers ─────────────────────────────────────────────────

        private void OnPickerSV(float nx, float ny)
        {
            pickerSat = nx;
            pickerVal = ny;
            UpdatePickerVisuals();
        }

        private void OnPickerHue(float nx, float _)
        {
            pickerHue = Mathf.Min(nx, 0.999f); // hue 1.0 aliases back to 0
            RegenSVTexture();
            UpdatePickerVisuals();
        }

        private void OnPickerAlpha(float nx, float _)
        {
            pickerA = Mathf.Round(nx * 100f) / 100f;
            UpdatePickerVisuals();
        }

        private void OnPickerPreset(Color rgb)
        {
            Color.RGBToHSV(rgb, out pickerHue, out pickerSat, out pickerVal);
            RegenSVTexture();
            UpdatePickerVisuals();
            CommitPickerColor();
        }

        private void OnPickerAction()
        {
            if (pickerIsGlobal)
            {
                LoadPickerColor(new Color(1f, 1f, 1f, DefaultGhostAlpha));
                CommitPickerColor();
            }
            else if (pickerSnapshotId != null)
            {
                // Drop the override; the run follows the global color again
                PBManager.UpdateSnapshotVisuals(pickerKey, pickerSnapshotId,
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
            if (pickerIsGlobal)
            {
                GhostSettings.GhostColor = c;
            }
            else if (pickerSnapshotId != null)
            {
                PBManager.UpdateSnapshotVisuals(pickerKey, pickerSnapshotId,
                    true, c);
                UpdatePickerContext(true);
            }
            RefreshAfterPickerCommit();
        }

        private void RefreshAfterPickerCommit()
        {
            UpdatePickerVisuals();
            if (pickerIsGlobal)
            {
                RefreshGhostColorChip();
            }
            else if (activeTab == TabKind.Runs
                && selectedScene == pickerKey.SceneName)
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
            if (cfgGhostColorFill != null)
                cfgGhostColorFill.color = new Color(g.r, g.g, g.b, 1f);
            if (cfgGhostAlphaLbl != null)
                cfgGhostAlphaLbl.text = "alpha " + g.a.ToString("0.00");
        }

        // ── Visual sync ────────────────────────────────────────────────────

        private void UpdatePickerVisuals()
        {
            Color rgb = Color.HSVToRGB(pickerHue, pickerSat, pickerVal);

            if (pickerSVHandle != null && pickerSVRect != null)
                pickerSVHandle.anchoredPosition = new Vector2(
                    pickerSat * pickerSVRect.rect.width,
                    pickerVal * pickerSVRect.rect.height);

            if (pickerHueHandle != null && pickerHueRect != null)
                pickerHueHandle.anchoredPosition = new Vector2(
                    pickerHue * pickerHueRect.rect.width,
                    pickerHueRect.rect.height / 2f);

            if (pickerAlphaHandle != null && pickerAlphaRect != null)
                pickerAlphaHandle.anchoredPosition = new Vector2(
                    pickerA * pickerAlphaRect.rect.width,
                    pickerAlphaRect.rect.height / 2f);

            // Alpha bar gradient is a white alpha ramp tinted by the color
            if (pickerAlphaImg != null)
                pickerAlphaImg.color = new Color(rgb.r, rgb.g, rgb.b, 1f);

            if (pickerPreviewFill != null)
                pickerPreviewFill.color = new Color(rgb.r, rgb.g, rgb.b, 1f);

            if (pickerAlphaValueLbl != null)
                pickerAlphaValueLbl.text = pickerA.ToString("0.00");
        }

        // ── Textures ───────────────────────────────────────────────────────

        private const int SVTexSize = 48;

        private void RegenSVTexture()
        {
            if (pickerSVTex == null)
            {
                pickerSVTex = new Texture2D(SVTexSize, SVTexSize,
                    TextureFormat.RGBA32, false);
                pickerSVTex.wrapMode = TextureWrapMode.Clamp;
                if (pickerSVImg != null) pickerSVImg.texture = pickerSVTex;
            }
            var px = new Color[SVTexSize * SVTexSize];
            for (int y = 0; y < SVTexSize; y++)
            {
                float v = y / (float)(SVTexSize - 1);
                for (int x = 0; x < SVTexSize; x++)
                    px[y * SVTexSize + x] = Color.HSVToRGB(pickerHue,
                        x / (float)(SVTexSize - 1), v);
            }
            pickerSVTex.SetPixels(px);
            pickerSVTex.Apply();
        }

        private Texture2D EnsureHueTexture()
        {
            if (pickerHueTex == null)
            {
                const int n = 128;
                pickerHueTex = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                pickerHueTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n];
                for (int x = 0; x < n; x++)
                    px[x] = Color.HSVToRGB(x / (float)(n - 1), 1f, 1f);
                pickerHueTex.SetPixels(px);
                pickerHueTex.Apply();
            }
            return pickerHueTex;
        }

        private Texture2D EnsureAlphaTexture()
        {
            if (pickerAlphaTex == null)
            {
                const int n = 64;
                pickerAlphaTex = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                pickerAlphaTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n];
                for (int x = 0; x < n; x++)
                    px[x] = new Color(1f, 1f, 1f, x / (float)(n - 1));
                pickerAlphaTex.SetPixels(px);
                pickerAlphaTex.Apply();
            }
            return pickerAlphaTex;
        }

        // ── Construction ───────────────────────────────────────────────────

        private void EnsurePicker()
        {
            if (pickerGO != null) return;
            if (canvasGO == null) return;

            // Scrim first so the picker draws above it. Transparent but
            // raycast-blocking: clicking anywhere outside the picker closes
            // it, and the panel behind can't be interacted with meanwhile.
            pickerScrim = MakeGO("PickerScrim", canvasGO.transform);
            var scrimImg = pickerScrim.AddComponent<Image>();
            scrimImg.color = Color.clear;
            Btn(pickerScrim, ClosePicker);
            Fill(pickerScrim);

            pickerGO = MakeGO("ColorPicker", canvasGO.transform);

            int pad = UIStyle.W(12);
            int pw = UIStyle.W(214);
            int innerW = pw - pad * 2;

            // 1px frame, same construction as the tooltip
            var borderImg = pickerGO.AddComponent<Image>();
            borderImg.color = UIStyle.Overlay with { a = 0.9f };

            var rt = pickerGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero; // bottom-left anchored
            rt.pivot = new Vector2(0f, 1f);             // position = top-left

            var innerBg = MakeGO("Inner", pickerGO.transform);
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

            pickerContextLbl = MakeLbl(pickerGO.transform, "",
                UIStyle.FontSizeTiny, UIStyle.Subtext, TextAnchor.MiddleLeft,
                x: pad, y: y, w: innerW - prevS - closeW - UIStyle.Gap * 2,
                h: hdrH);

            var preview = MakeGO("Preview", pickerGO.transform);
            Img(preview, UIStyle.Overlay with { a = 0.9f });
            Rect(preview, pw - pad - closeW - UIStyle.Gap - prevS, y, prevS, hdrH);
            var prevFill = MakeGO("Fill", preview.transform);
            Img(prevFill, Color.white);
            Rect(prevFill, 1, 1, prevS - 2, hdrH - 2);
            prevFill.GetComponent<Graphic>().raycastTarget = false;
            pickerPreviewFill = prevFill.GetComponent<Image>();

            MakeButton(pickerGO.transform, "Close", "Close",
                UIStyle.FontSizeBtn, UIStyle.Text, UIStyle.Overlay with { a = 0.6f },
                pw - pad - closeW, y, closeW, hdrH, ClosePicker);
            y += hdrH + UIStyle.H(8);

            // Saturation/value square
            int svH = UIStyle.H(96);
            pickerSVRect = AddPickerSurface(pickerGO.transform, "SV",
                pad, y, innerW, svH, out pickerSVImg,
                OnPickerSV, CommitPickerColor);
            RegenSVTexture();
            pickerSVImg.texture = pickerSVTex;
            pickerSVHandle = MakePickerMarker(pickerSVRect,
                UIStyle.H(10), UIStyle.H(10));
            y += svH + UIStyle.H(8);

            // Hue bar
            int barH = UIStyle.H(12);
            pickerHueRect = AddPickerSurface(pickerGO.transform, "Hue",
                pad, y, innerW, barH, out RawImage hueImg,
                OnPickerHue, CommitPickerColor);
            hueImg.texture = EnsureHueTexture();
            pickerHueHandle = MakePickerMarker(pickerHueRect,
                UIStyle.W(5), barH + UIStyle.H(4));
            y += barH + UIStyle.H(8);

            // Alpha bar (dark base behind a tinted alpha ramp) + value
            int alphaLblW = UIStyle.W(40);
            int alphaBarW = innerW - alphaLblW - UIStyle.Gap;
            pickerAlphaRect = AddPickerSurface(pickerGO.transform, "Alpha",
                pad, y, alphaBarW, barH, out pickerAlphaImg,
                OnPickerAlpha, CommitPickerColor);
            pickerAlphaImg.texture = EnsureAlphaTexture();
            pickerAlphaHandle = MakePickerMarker(pickerAlphaRect,
                UIStyle.W(5), barH + UIStyle.H(4));
            pickerAlphaValueLbl = MakeLbl(pickerGO.transform, "0.40",
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
                var sw = MakeGO("Preset", pickerGO.transform);
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
            pickerHeightCompact = y;
            int actH = UIStyle.H(20);
            var actRef = MakeButton(pickerGO.transform, "PickerAction", "",
                UIStyle.FontSizeBtn, UIStyle.Accent, UIStyle.BtnBg(UIStyle.Accent),
                pad, y, innerW, actH, OnPickerAction);
            pickerActionGO = actRef.bg.gameObject;
            pickerActionLbl = actRef.label;
            pickerHeightFull = y + actH + UIStyle.H(10);

            pickerWidth = pw;
            pickerHeight = pickerHeightFull;
            rt.sizeDelta = new Vector2(pw, pickerHeight);

            pickerScrim.SetActive(false);
            pickerGO.SetActive(false);
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
