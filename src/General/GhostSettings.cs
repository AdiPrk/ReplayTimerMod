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
        private static ConfigEntry<bool>? _multiReplayEnabled;
        private static ConfigEntry<bool>? _saveAllRunsEnabled;
        private static ConfigEntry<int>? _maxSavedReplaysPerRoute;

        // ── Init ──────────────────────────────────────────────────────────────

        private static ConfigEntry<bool>? _trackingEnabled;

        public static void Init(ConfigFile config)
        {
            _trackingEnabled = config.Bind(
                section: "Mod",
                key: "TrackingEnabled",
                defaultValue: true,
                description: "Master switch. When false, no rooms are recorded and no PBs are updated.");

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

            _multiReplayEnabled = config.Bind(
                section: "Ghost",
                key: "MultiReplayEnabled",
                defaultValue: false,
                description: "Allow multiple selected replays to play at the same time.");

            _saveAllRunsEnabled = config.Bind(
                section: "Recording",
                key: "SaveAllRunsEnabled",
                defaultValue: false,
                description: "Save every completed non-duplicate run instead of PB-only.");

            _maxSavedReplaysPerRoute = config.Bind(
                section: "Recording",
                key: "MaxSavedReplaysPerRoute",
                defaultValue: 5,
                new ConfigDescription("Maximum number of saved replays to keep per route.",
                    new AcceptableValueRange<int>(1, int.MaxValue)));
        }

        // ── Properties ────────────────────────────────────────────────────────
        // Fall back to hardcoded defaults when Init() hasn't been called yet

        public static bool TrackingEnabled
        {
            get => _trackingEnabled?.Value ?? true;
            set { if (_trackingEnabled != null) _trackingEnabled.Value = value; }
        }

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

        public static bool MultiReplayEnabled
        {
            get => _multiReplayEnabled?.Value ?? false;
            set { if (_multiReplayEnabled != null) _multiReplayEnabled.Value = value; }
        }

        public static bool SaveAllRunsEnabled
        {
            get => _saveAllRunsEnabled?.Value ?? false;
            set { if (_saveAllRunsEnabled != null) _saveAllRunsEnabled.Value = value; }
        }

        public static int MaxSavedReplaysPerRoute
        {
            get => Mathf.Max(1, _maxSavedReplaysPerRoute?.Value ?? 5);
            set
            {
                if (_maxSavedReplaysPerRoute != null)
                    _maxSavedReplaysPerRoute.Value = Mathf.Max(1, value);
            }
        }
    }
}