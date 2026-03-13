using UnityEngine;
using UnityEngine.UI;

namespace ReplayTimerMod
{
    // Shared visual constants and scaling helpers for ReplayUI.
    // Palette: Catppuccin Macchiato.
    public static class UIStyle
    {
        // ── Palette ───────────────────────────────────────────────────────────
        public static readonly Color Base = RGB(36, 39, 58);   // panel bg
        public static readonly Color Surface = RGB(49, 52, 76);   // row bg
        public static readonly Color Overlay = RGB(73, 77, 100);   // hover / selected
        public static readonly Color Border = RGB(202, 211, 245);  // borders
        public static readonly Color Text = RGB(202, 211, 245);  // primary text
        public static readonly Color Subtext = RGB(128, 135, 162);  // secondary text
        public static readonly Color Accent = RGB(138, 173, 244);  // buttons / highlights
        public static readonly Color Gold = RGB(238, 212, 159);  // PB times
        public static readonly Color Red = RGB(237, 135, 150);  // missed PB / delete

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
    }
}