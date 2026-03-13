using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // Shared visual constants and factory helpers for ReplayUI.
    // Palette: Catppuccin Macchiato (matches DebugMod).
    public static class UIStyle
    {
        // ── Palette ───────────────────────────────────────────────────────────
        public static readonly Color Base = RGB(36, 39, 58);   // panel bg
        public static readonly Color Surface = RGB(49, 52, 76);   // row bg
        public static readonly Color Overlay = RGB(73, 77, 100);   // hover / selected
        public static readonly Color Border = RGB(202, 211, 245);   // borders
        public static readonly Color Text = RGB(202, 211, 245);   // primary text
        public static readonly Color Subtext = RGB(128, 135, 162);   // secondary text
        public static readonly Color Accent = RGB(138, 173, 244);   // buttons / highlights
        public static readonly Color Gold = RGB(238, 212, 159);   // PB times
        public static readonly Color Red = RGB(237, 135, 150);   // missed PB

        private static Color RGB(int r, int g, int b) =>
            new Color(r / 255f, g / 255f, b / 255f);

        // ── Scaling (1080p reference) ─────────────────────────────────────────
        public static int W(int unscaled) => (int)(unscaled * Screen.width / 1920f);
        public static int H(int unscaled) => (int)(unscaled * Screen.height / 1080f);

        public static int PanelWidth => W(480);
        public static int PanelHeight => H(620);
        public static int RowHeight => H(28);
        public static int RouteHeight => H(24);
        public static int HeaderHeight => H(36);
        public static int SearchHeight => H(30);
        public static int Margin => H(6);
        public static int FontSizeLg => H(15);
        public static int FontSizeSm => H(13);

        // ── Fonts ─────────────────────────────────────────────────────────────
        private static Font? _arial;
        private static Font? _trajan;

        public static Font? Arial => _arial;
        public static Font? Trajan => _trajan;

        public static void LoadFonts()
        {
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f.name == "ARIAL") _arial = f;
                if (f.name == "TrajanPro-Regular") _trajan = f;
            }
        }

        // ── Texture helpers ───────────────────────────────────────────────────
        public static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // ── GameObject factories ──────────────────────────────────────────────
        // Create a panel (RectTransform + Image) as a child of parent.
        public static RectTransform MakePanel(Transform parent, string name,
            Color? bg = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bg ?? Base;

            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            return go.GetComponent<RectTransform>();
        }

        // Create a non-interactable text label.
        public static Text MakeText(Transform parent, string name,
            int fontSize, Color? color = null,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var t = go.AddComponent<Text>();
            t.font = Arial;
            t.fontSize = fontSize;
            t.color = color ?? Text;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        // Create a clickable button.
        public static Button MakeButton(Transform parent, string name,
            string label, int fontSize, Color? bg = null, Color? textColor = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = bg ?? Overlay;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bg ?? Overlay;
            colors.highlightedColor = Accent with { a = 0.4f };
            colors.pressedColor = Accent with { a = 0.6f };
            btn.colors = colors;

            // Label child
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var t = labelGo.AddComponent<Text>();
            t.font = Arial;
            t.fontSize = fontSize;
            t.color = textColor ?? Text;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            t.text = label;

            return btn;
        }

        // Stretch a RectTransform to fill its parent.
        public static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Set a RectTransform to a fixed pixel size anchored top-left.
        public static void SetRect(RectTransform rt,
            float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // Draw a 1px border around a rect by adding an Outline component.
        public static void AddBorder(GameObject go, Color? color = null)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color ?? Border;
            outline.effectDistance = new Vector2(1, -1);
        }
    }
}