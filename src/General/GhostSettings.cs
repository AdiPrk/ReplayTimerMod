using BepInEx.Configuration;
using UnityEngine;

namespace ReplayTimerMod
{
    // Global ghost rendering settings. Backed by BepInEx ConfigEntry<T> so
    // values persist across sessions in BepInEx/config/io.github.adiprk.replaytimermod.cfg.
    //
    // Call GhostSettings.Init(Config) from the plugin's Awake() before any
    // other code reads these properties.
    public static class GhostSettings
    {
        private static ConfigEntry<bool>? _ghostEnabled;
        private static ConfigEntry<float>? _r;
        private static ConfigEntry<float>? _g;
        private static ConfigEntry<float>? _b;
        private static ConfigEntry<float>? _alpha;

        // ── Init ──────────────────────────────────────────────────────────────

        public static void Init(ConfigFile config)
        {
            _ghostEnabled = config.Bind(
                section: "Ghost",
                key: "Enabled",
                defaultValue: true,
                description: "Show the ghost during playback.");

            _r = config.Bind(
                section: "Ghost",
                key: "ColorR",
                defaultValue: 1f,
                new ConfigDescription("Ghost colour red channel (0-1).",
                    new AcceptableValueRange<float>(0f, 1f)));

            _g = config.Bind(
                section: "Ghost",
                key: "ColorG",
                defaultValue: 1f,
                new ConfigDescription("Ghost colour green channel (0-1).",
                    new AcceptableValueRange<float>(0f, 1f)));

            _b = config.Bind(
                section: "Ghost",
                key: "ColorB",
                defaultValue: 1f,
                new ConfigDescription("Ghost colour blue channel (0-1).",
                    new AcceptableValueRange<float>(0f, 1f)));

            _alpha = config.Bind(
                section: "Ghost",
                key: "Alpha",
                defaultValue: 0.4f,
                new ConfigDescription("Ghost opacity (0 = invisible, 1 = fully opaque).",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // ── Properties ────────────────────────────────────────────────────────
        // Fall back to hardcoded defaults when Init() hasn't been called yet
        // (e.g. in unit-test contexts or before Awake).

        public static bool GhostEnabled
        {
            get => _ghostEnabled?.Value ?? true;
            set { if (_ghostEnabled != null) _ghostEnabled.Value = value; }
        }

        public static Color GhostColor
        {
            get => new Color(
                _r?.Value ?? 1f,
                _g?.Value ?? 1f,
                _b?.Value ?? 1f,
                _alpha?.Value ?? 0.4f);
            set
            {
                if (_r != null) _r.Value = value.r;
                if (_g != null) _g.Value = value.g;
                if (_b != null) _b.Value = value.b;
                if (_alpha != null) _alpha.Value = value.a;
            }
        }

        // Convenience accessor for alpha
        public static float GhostAlpha
        {
            get => _alpha?.Value ?? 0.4f;
            set { if (_alpha != null) _alpha.Value = Mathf.Clamp01(value); }
        }
    }
}