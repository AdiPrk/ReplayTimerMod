using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // ── Interaction MonoBehaviours ──────────────────────────────────────
    //
    // IMPORTANT: these components must stay ENABLED. Unity's EventSystem
    // (ExecuteEvents.ShouldSendToComponent) skips disabled Behaviours, so
    // a disabled component never receives OnPointerEnter/Exit. The Update
    // methods early-out when settled instead.

    /// <summary>
    /// Subtle hover highlight on any interactive row.
    /// </summary>
    internal sealed class RowHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler
    {
        public Image? overlay;

        private float _current;
        private float _target;

        private const float HoverAlpha = 0.055f;
        private const float Speed = 14f;

        public void Init()
        {
            _current = 0f;
            _target = 0f;
            if (overlay != null)
                overlay.color = SetAlpha(overlay.color, 0f);
        }

        public void OnPointerEnter(PointerEventData e) { _target = HoverAlpha; }
        public void OnPointerExit(PointerEventData e) { _target = 0f; }

        private void Update()
        {
            if (_current == _target) return; // settled — skip all work

            _current = Mathf.MoveTowards(_current, _target,
                Time.unscaledDeltaTime * Speed);
            if (overlay != null)
                overlay.color = SetAlpha(overlay.color, _current);
        }

        private static Color SetAlpha(Color c, float a) =>
            new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// Hover + press feedback for buttons: a light overlay fades in on
    /// hover and brightens while pressed. uGUI's built-in Button tint
    /// transition never fires here (no targetGraphic is assigned), so
    /// this overlay is the only visual feedback buttons get.
    /// </summary>
    internal sealed class ButtonHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public Image? overlay;

        private float _current;
        private bool _hovered;
        private bool _pressed;

        private const float HoverAlpha = 0.09f;
        private const float PressAlpha = 0.20f;
        private const float Speed = 16f;

        public void OnPointerEnter(PointerEventData e) { _hovered = true; }
        public void OnPointerExit(PointerEventData e) { _hovered = false; _pressed = false; }
        public void OnPointerUp(PointerEventData e) { _pressed = false; }

        public void OnPointerDown(PointerEventData e)
        {
            _pressed = true;
            // Press feedback must be instant, not eased
            _current = PressAlpha;
            Apply();
        }

        private float Target => _pressed ? PressAlpha : (_hovered ? HoverAlpha : 0f);

        private void Update()
        {
            float target = Target;
            if (_current == target) return; // settled — skip all work

            _current = Mathf.MoveTowards(_current, target,
                Time.unscaledDeltaTime * Speed);
            Apply();
        }

        private void Apply()
        {
            if (overlay != null)
                overlay.color = new Color(UIStyle.Text.r, UIStyle.Text.g,
                    UIStyle.Text.b, _current);
        }
    }

    public partial class ReplayUI
    {
        private struct ButtonRef
        {
            public Image bg;
            public Text label;
        }

        // ── Shared run-row right-side layout ────────────────────────────
        //
        // The Runs tab and the Leaderboard tab lay out the right side of a
        // run row with the same language: action buttons pack flush against
        // the right edge, the time slot sits left of the buttons separated
        // by a double gap (flush right when a row has no buttons), optional
        // extra slots (e.g. the WR delta) sit a gap left of the time, then
        // the "?" loadout marker, then the name/label fills what remains.
        // This struct is the single source of that geometry: row builders
        // declare WHAT the row contains, right to left, and consume the
        // returned columns - so the tabs cannot drift apart.

        private struct RowRightCluster
        {
            private int cursor; // right edge available to the next element
            private bool hasButtons;

            public static RowRightCluster Begin(int rowWidth) => new RowRightCluster
            {
                cursor = rowWidth - UIStyle.Margin,
                hasButtons = false,
            };

            /// <summary>Right-packs a button of the given width; returns its x.</summary>
            public int AddButton(int width)
            {
                cursor -= width;
                int x = cursor;
                cursor -= UIStyle.Gap;
                hasButtons = true;
                return x;
            }

            /// <summary>Places the time slot: a double gap after the
            /// buttons, or flush right when the row has none. Size the slot
            /// with <see cref="TimeColumnWidth"/> so the right-aligned text
            /// fills it and what follows on the left sits the same double
            /// gap from the time text as the buttons do on the right.
            /// Returns its x; render the label right-aligned.</summary>
            public int AddTime(int width)
            {
                if (hasButtons)
                    cursor -= UIStyle.Gap; // second half of the double gap
                cursor -= width;
                int x = cursor;
                cursor -= UIStyle.Gap * 2; // mirror the button-side double gap
                return x;
            }

            /// <summary>Places an extra right-aligned slot (e.g. the WR
            /// delta) left of what came before; returns its x.</summary>
            public int AddSlot(int width)
            {
                cursor -= width;
                int x = cursor;
                cursor -= UIStyle.Gap;
                return x;
            }

            /// <summary>Right edge for the "?" loadout marker.</summary>
            public int MarkerRight => cursor;

            /// <summary>Right edge available to the name/label once the
            /// marker (of the given width) is placed.</summary>
            public int LabelEnd(int markerWidth) =>
                cursor - markerWidth - UIStyle.Gap;
        }

        /// <summary>
        /// Canonical width of the time column for a set of rendered rows:
        /// the widest formatted time among them, so the slot hugs its text
        /// and the "?" marker's distance from the time matches the buttons'
        /// distance on the other side. Time strings only contain digits
        /// (556/1000 em in Arial) and ':' '.' separators (278/1000 em), so
        /// the width is deterministic without live text measurement. Both
        /// tabs must size their time column with this.
        /// </summary>
        private static int TimeColumnWidth(System.Collections.Generic.IEnumerable<float> times)
        {
            float maxEm = 0f;
            foreach (float t in times)
            {
                float em = 0f;
                string s = TimeUtil.Format(t);
                for (int i = 0; i < s.Length; i++)
                    em += s[i] == ':' || s[i] == '.' ? 0.278f : 0.556f;
                if (em > maxEm) maxEm = em;
            }
            return Mathf.CeilToInt(maxEm * UIStyle.FontSizeSm) + 1;
        }

        private static GameObject MakeGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void Img(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
            img.color = c;
        }

        private static void Btn(GameObject go, UnityEngine.Events.UnityAction action)
        {
            var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static void Rect(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Fill(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void HLine(Transform parent, float x, float y, float w)
        {
            var go = MakeGO("HLine", parent);
            Img(go, UIStyle.Overlay);
            Rect(go, x, y, w, 1);
        }

        private static void VLine(Transform parent, float x, float y, float h)
        {
            var go = MakeGO("VLine", parent);
            Img(go, UIStyle.Overlay);
            Rect(go, x, y, 1, h);
        }

        private static Text MakeLbl(Transform parent, string text,
            int fontSize, Color color, TextAnchor anchor,
            float x = 0, float y = 0, float w = 0, float h = 0,
            bool fill = false)
        {
            var go = MakeGO("Lbl", parent);
            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var t = go.AddComponent<Text>();
            t.font = UIStyle.Arial;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            // Never interpret rich-text markup in labels. Display names (and any
            // server-supplied text) render through here; disabling rich text
            // means a name like "<color=#f00>x" or "<size=400>" is shown as
            // literal characters instead of being interpreted. Defense in depth:
            // new names are ASCII-only and can't contain '<'/'>' anyway, but this
            // also neutralizes any legacy name stored before validation existed.
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.text = text;
            t.alignByGeometry = true;

            var rt = go.GetComponent<RectTransform>();
            if (fill)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(x, -y);
                rt.sizeDelta = new Vector2(w, h);
            }
            return t;
        }

        /// <summary>
        /// Subtle animated hover highlight for interactive ROWS
        /// (leaderboard rows, scene list, expand rows).
        /// </summary>
        private static RowHover AddHoverEffect(GameObject row)
        {
            var overlayGO = MakeGO("HoverOverlay", row.transform);
            var img = overlayGO.AddComponent<Image>();
            img.color = new Color(UIStyle.Text.r, UIStyle.Text.g, UIStyle.Text.b, 0f);
            img.raycastTarget = false;
            Fill(overlayGO);

            var hover = row.AddComponent<RowHover>();
            hover.overlay = img;
            hover.Init();
            return hover;
        }

        /// <summary>
        /// Hover + press feedback for BUTTONS. MakeButton attaches this
        /// automatically; call it manually for hand-rolled buttons
        /// (MakeGO + Image + Btn).
        /// </summary>
        private static void AddButtonHover(GameObject buttonGO)
        {
            var overlayGO = MakeGO("HoverOverlay", buttonGO.transform);
            var img = overlayGO.AddComponent<Image>();
            img.color = new Color(UIStyle.Text.r, UIStyle.Text.g, UIStyle.Text.b, 0f);
            img.raycastTarget = false;
            Fill(overlayGO);

            buttonGO.AddComponent<ButtonHover>().overlay = img;
        }

        /// <summary>
        /// Small drawn triangle caret (PlayMarkerTexture, tinted), centered
        /// at (cx, cy) in the parent's top-left space. Points right at
        /// rotation 0; pass -90 for down, 90 for up. Returns the
        /// RectTransform so callers can re-rotate it later. Use this instead
        /// of text glyphs - labels stay plain ASCII.
        /// </summary>
        private static RectTransform AddCaret(Transform parent, float cx,
            float cy, int size, Color color, float rotation = 0f)
        {
            var go = MakeGO("Caret", parent);
            var img = go.AddComponent<RawImage>();
            img.texture = PlayMarkerTexture();
            img.color = color;
            img.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(cx, -cy);
            if (rotation != 0f)
                rt.localEulerAngles = new Vector3(0, 0, rotation);
            return rt;
        }

        private static ButtonRef MakeButton(
            Transform parent, string name, string text,
            int fontSize, Color textColor, Color bgColor,
            float x, float y, float w, float h,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = MakeGO(name, parent);
            var bg = go.AddComponent<Image>();
            bg.color = bgColor;
            go.AddComponent<Button>().onClick.AddListener(onClick);
            Rect(go, x, y, w, h);
            var lbl = MakeLbl(go.transform, text, fontSize, textColor,
                TextAnchor.MiddleCenter, fill: true);
            // Overlay goes last so it draws on top of bg + label
            AddButtonHover(go);

            ButtonRef r;
            r.bg = bg;
            r.label = lbl;
            return r;
        }

        private static InputField MakeSearchInput(
            Transform parent, string placeholder,
            float x, float y, float w, float h,
            UnityEngine.Events.UnityAction<string> onChanged)
        {
            int pad = UIStyle.W(6);
            int fontSize = UIStyle.FontSizeRow;

            var go = MakeGO("SearchInput", parent);
            Img(go, UIStyle.Surface);
            Rect(go, x, y, w, h);

            var textGO = MakeGO("Text", go.transform);
            var text = textGO.AddComponent<Text>();
            text.font = UIStyle.Arial;
            text.fontSize = fontSize;
            text.color = UIStyle.Text;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(pad, 0);
            textRT.offsetMax = new Vector2(-pad, 0);

            var phGO = MakeGO("Placeholder", go.transform);
            var ph = phGO.AddComponent<Text>();
            ph.font = UIStyle.Arial;
            ph.fontSize = fontSize;
            ph.color = UIStyle.Overlay;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            ph.verticalOverflow = VerticalWrapMode.Truncate;
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;
            var phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(pad, 0);
            phRT.offsetMax = new Vector2(-pad, 0);

            var input = go.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.caretColor = UIStyle.Accent;
            input.selectionColor = UIStyle.Accent with { a = 0.3f };
            input.onValueChanged.AddListener(onChanged);

            return input;
        }

        private static void ClearContent(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Object.Destroy(t.GetChild(i).gameObject);
        }

        private static void ForceLayout(Transform content)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                content.GetComponent<RectTransform>());
        }

        // ── Scroll preservation across content rebuilds ────────────────────
        //
        // Preserve the PIXEL offset from the top, not the normalized
        // fraction: a rebuild can change the content height (filter panel
        // expand/collapse, rows filtered away), and the same fraction of a
        // different height lands the view somewhere else entirely.

        /// <summary>Current scroll offset from the top, in pixels.</summary>
        private static float ScrollOffsetFromTop(ScrollRect scroll)
        {
            float range = ScrollRange(scroll);
            return range <= 0f
                ? 0f
                : (1f - scroll.verticalNormalizedPosition) * range;
        }

        /// <summary>Restores a pixel offset captured by
        /// <see cref="ScrollOffsetFromTop"/> after the content was rebuilt
        /// (layout must already be forced so the new height is valid).</summary>
        private static void RestoreScrollOffsetFromTop(ScrollRect scroll, float offset)
        {
            float range = ScrollRange(scroll);
            scroll.verticalNormalizedPosition = range <= 0f
                ? 1f
                : Mathf.Clamp01(1f - offset / range);
        }

        // Scrollable distance: content height minus viewport height.
        private static float ScrollRange(ScrollRect scroll)
        {
            var viewport = scroll.viewport != null
                ? scroll.viewport
                : (RectTransform)scroll.transform;
            return scroll.content.rect.height - viewport.rect.height;
        }
    }
}