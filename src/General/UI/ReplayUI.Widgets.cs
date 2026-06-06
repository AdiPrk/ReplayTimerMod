using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
        private struct ButtonRef
        {
            public Image bg;
            public Text label;
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
            int fontSize = UIStyle.FontSizeSm - 1;

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
    }
}