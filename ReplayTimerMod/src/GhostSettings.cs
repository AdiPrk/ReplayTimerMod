using UnityEngine;

namespace ReplayTimerMod
{
    // Global ghost rendering settings. All fields are plain static properties so
    // both GhostPlayback (reads every tick) and ReplayUI (writes on user action)
    // can access them without any coupling between those two classes.
    public static class GhostSettings
    {
        public static bool GhostEnabled { get; set; } = true;

        // Full RGBA colour. Alpha is the opacity slider value.
        // Default: white at 40 % opacity (matches the original hardcoded values).
        public static Color GhostColor { get; set; } = new Color(1f, 1f, 1f, 0.4f);

        // Convenience accessor so the UI can adjust alpha without touching RGB.
        public static float GhostAlpha
        {
            get => GhostColor.a;
            set
            {
                var c = GhostColor;
                GhostColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(value));
            }
        }
    }
}