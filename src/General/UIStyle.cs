using UnityEngine;
using UnityEngine.UI;
using BepInEx.Logging;
using System.Linq;

namespace ReplayTimerMod
{
    public static class UIStyle
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("UIStyle");

        // Palette: Catppuccin Macchiato
        public static readonly Color Base = RGB(36, 39, 58);
        public static readonly Color Surface = RGB(49, 52, 76);
        public static readonly Color Overlay = RGB(73, 77, 100);
        public static readonly Color Border = RGB(202, 211, 245);
        public static readonly Color Text = RGB(202, 211, 245);
        public static readonly Color Subtext = RGB(128, 135, 162);
        public static readonly Color Accent = RGB(138, 173, 244);
        public static readonly Color Gold = RGB(238, 212, 159);
        public static readonly Color Red = RGB(237, 135, 150);
        public static readonly Color Green = RGB(166, 218, 149);

        private static Color RGB(int r, int g, int b) =>
            new Color(r / 255f, g / 255f, b / 255f);

        // Scaling (1080p reference)
        public static int W(int px) => (int)(px * Screen.width / 1920f);
        public static int H(int px) => (int)(px * Screen.height / 1080f);

        public static int PanelWidth => W(680);
        public static int PanelHeight => H(576);
        public static int LeftWidth => W(200);
        public static int RowHeight => H(26);
        public static int HeaderHeight => H(34);
        public static int SubHeaderHeight => H(28);
        public static int TabBarHeight => H(28);
        public static int SearchBarHeight => H(26);
        public static int FooterHeight => H(24);
        public static int Margin => H(6);
        public static int FontSizeLg => H(15);
        public static int FontSizeSm => H(13);

        // Tab toggle button
        public static int TabBtnWidth => W(44);
        public static int TabBtnHeight => H(28);

        // Fonts
        private static Font? _arial;
        public static Font? Arial => _arial;

        public static void LoadFonts()
        {
            try
            {
                string tmpPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "Arial.ttf");

                if (!System.IO.File.Exists(tmpPath))
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    string resourceName = asm.GetManifestResourceNames()
                        .First(n => n.EndsWith("Arial.ttf"));
                    using (var stream = asm.GetManifestResourceStream(resourceName))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, bytesRead);
                        System.IO.File.WriteAllBytes(tmpPath, ms.ToArray());
                    }
                }

                _arial = Font.CreateDynamicFontFromOSFont(tmpPath, 14);
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[UIStyle] Font load failed: {ex.Message}");
            }
        }
    }
}