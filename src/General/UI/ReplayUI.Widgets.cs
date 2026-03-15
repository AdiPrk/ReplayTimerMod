using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    public partial class ReplayUI
    {
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

        // Non-interactable text label. CanvasGroup prevents it intercepting clicks.
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
            t.font = UIStyle.Arial ?? UIStyle.Trajan;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.text = text;

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