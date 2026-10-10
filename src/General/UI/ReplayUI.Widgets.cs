using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReplayTimerMod
{
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
            if (_current == _target) return;

            _current = Mathf.MoveTowards(_current, _target,
                Time.unscaledDeltaTime * Speed);
            if (overlay != null)
                overlay.color = SetAlpha(overlay.color, _current);
        }

        private static Color SetAlpha(Color c, float a) =>
            new Color(c.r, c.g, c.b, a);
    }

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
            _current = PressAlpha;
            Apply();
        }

        private float Target => _pressed ? PressAlpha : (_hovered ? HoverAlpha : 0f);

        private void Update()
        {
            float target = Target;
            if (_current == target) return;

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

        private struct RowRightCluster
        {
            private int cursor;
            private bool hasButtons;

            public static RowRightCluster Begin(int rowWidth) => new RowRightCluster
            {
                cursor = rowWidth - UIStyle.Margin,
                hasButtons = false,
            };

            public int AddButton(int width)
            {
                cursor -= width;
                int x = cursor;
                cursor -= UIStyle.Gap;
                hasButtons = true;
                return x;
            }

            public int AddTime(int width)
            {
                if (hasButtons)
                    cursor -= UIStyle.Gap;
                cursor -= width;
                int x = cursor;
                cursor -= UIStyle.Gap * 2;
                return x;
            }

            public int LabelEnd => cursor;
        }

        private static int TimeColumnWidth(IEnumerable<float> times)
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
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
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
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.text = text;
            t.alignByGeometry = anchor == TextAnchor.MiddleCenter;

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

        private static void AddButtonHover(GameObject buttonGO)
        {
            var overlayGO = MakeGO("HoverOverlay", buttonGO.transform);
            var img = overlayGO.AddComponent<Image>();
            img.color = new Color(UIStyle.Text.r, UIStyle.Text.g, UIStyle.Text.b, 0f);
            img.raycastTarget = false;
            Fill(overlayGO);

            buttonGO.AddComponent<ButtonHover>().overlay = img;
        }

        private static Texture2D? _playMarkerTex;

        private static Texture2D PlayMarkerTexture()
        {
            if (_playMarkerTex == null)
            {
                const int n = 24;
                _playMarkerTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                _playMarkerTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                {
                    float xEdge = n - 2f * Mathf.Abs(y + 0.5f - n / 2f);
                    for (int x = 0; x < n; x++)
                        px[y * n + x] = new Color(1f, 1f, 1f,
                            Mathf.Clamp01(xEdge - x));
                }
                _playMarkerTex.SetPixels(px);
                _playMarkerTex.Apply();
            }
            return _playMarkerTex;
        }

        private static Texture2D? _cameraMarkerTex;

        private static Texture2D CameraMarkerTexture()
        {
            if (_cameraMarkerTex == null)
            {
                const int n = 24;
                _cameraMarkerTex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                _cameraMarkerTex.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                {
                    float yc = y + 0.5f;
                    for (int x = 0; x < n; x++)
                    {
                        float xc = x + 0.5f;
                        float body = Mathf.Min(
                            Mathf.Min(xc - 0.5f, 13.5f - xc),
                            Mathf.Min(yc - 4.5f, 19.5f - yc));
                        float half = 1f + (xc - 13f) * 0.6f;
                        float wedge = Mathf.Min(
                            Mathf.Min(xc - 13f, 23.5f - xc),
                            half - Mathf.Abs(yc - 12f));
                        px[y * n + x] = new Color(1f, 1f, 1f,
                            Mathf.Clamp01(Mathf.Max(body, wedge)));
                    }
                }
                _cameraMarkerTex.SetPixels(px);
                _cameraMarkerTex.Apply();
            }
            return _cameraMarkerTex;
        }

        private Text? _measureLbl;

        private float MeasureTextWidth(string text, int fontSize)
        {
            if (_measureLbl == null)
            {
                var go = MakeGO("MeasureLbl", _canvasGO.transform);
                _measureLbl = go.AddComponent<Text>();
                _measureLbl.font = UIStyle.Arial;
                _measureLbl.color = Color.clear;
                _measureLbl.raycastTarget = false;
                _measureLbl.supportRichText = false;
                _measureLbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                _measureLbl.verticalOverflow = VerticalWrapMode.Overflow;
                Rect(go, 0, 0, 0, 0);
            }

            _measureLbl.fontSize = fontSize;
            _measureLbl.text = text;
            return _measureLbl.preferredWidth;
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
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            Rect(go, x, y, w, h);
            var lbl = MakeLbl(go.transform, text, fontSize, textColor,
                TextAnchor.MiddleCenter, fill: true);
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

        private static void ForceLayout(Transform content)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                content.GetComponent<RectTransform>());
        }

        private static float ScrollOffsetFromTop(ScrollRect scroll)
        {
            float range = ScrollRange(scroll);
            return range <= 0f
                ? 0f
                : (1f - scroll.verticalNormalizedPosition) * range;
        }

        private static void RestoreScrollOffsetFromTop(ScrollRect scroll, float offset)
        {
            float range = ScrollRange(scroll);
            scroll.verticalNormalizedPosition = range <= 0f
                ? 1f
                : Mathf.Clamp01(1f - offset / range);
        }

        private static float ScrollRange(ScrollRect scroll)
        {
            var viewport = scroll.viewport != null
                ? scroll.viewport
                : (RectTransform)scroll.transform;
            return scroll.content.rect.height - viewport.rect.height;
        }
    }
}
