using System.Reflection;
using UnityEngine;

namespace ReplayTimerMod
{
    public sealed class GhostSettingsData
    {
        public bool  TrackingEnabled         = true;
        public bool  GhostEnabled            = true;
        public float ColorR                  = 1f;
        public float ColorG                  = 1f;
        public float ColorB                  = 1f;
        public float Alpha                   = 0.4f;
        public bool  MultiReplayEnabled      = false;
        public bool  SaveAllRunsEnabled      = false;
        public int   MaxSavedReplaysPerRoute = 5;
        public bool  TimerHudEnabled         = true;
        public bool  ChainRoomTimers         = false;
        public bool  SkipBacktrackRuns       = false;
        public bool  SkipBacktrackTimer      = false;
        public bool   OnlineEnabled  = false;
        public string DeviceId       = "";
        public string DisplayName    = "";
        public string ApiBaseUrl     = "https://oqsfhqbakarleqahxiyo.supabase.co/functions/v1";
        // Modifier filter (shared by Runs + Leaderboard tabs; see ModifierMask).
        public int ModifierRequireMask = 0;
        public int ModifierExcludeMask = 0;
        // Experimental features (Config > Experimental).
        public bool RoomWarpEnabled = false;
        public bool CameraFollowEnabled = false;
    }

    public static class GhostSettings
    {
        private static string _filePath = "";
        private static readonly GhostSettingsData _d = new GhostSettingsData();

        // ── Properties ────────────────────────────────────────────────────────

        public static bool TrackingEnabled
        {
            get => _d.TrackingEnabled;
            set { _d.TrackingEnabled = value; Save(); }
        }

        public static bool GhostEnabled
        {
            get => _d.GhostEnabled;
            set { _d.GhostEnabled = value; Save(); }
        }

        public static bool MultiReplayEnabled
        {
            get => _d.MultiReplayEnabled;
            set { _d.MultiReplayEnabled = value; Save(); }
        }

        public static bool SaveAllRunsEnabled
        {
            get => _d.SaveAllRunsEnabled;
            set { _d.SaveAllRunsEnabled = value; Save(); }
        }

        public static int MaxSavedReplaysPerRoute
        {
            get => Mathf.Max(1, _d.MaxSavedReplaysPerRoute);
            set { _d.MaxSavedReplaysPerRoute = Mathf.Max(1, value); Save(); }
        }

        public static Color GhostColor
        {
            get => new Color(_d.ColorR, _d.ColorG, _d.ColorB, _d.Alpha);
            set { _d.ColorR = value.r; _d.ColorG = value.g; _d.ColorB = value.b; _d.Alpha = value.a; Save(); }
        }

        public static float GhostAlpha
        {
            get => _d.Alpha;
            set { _d.Alpha = Mathf.Clamp01(value); Save(); }
        }

        public static bool TimerHudEnabled
        {
            get => _d.TimerHudEnabled;
            set { _d.TimerHudEnabled = value; Save(); }
        }

        /// <summary>
        /// When true, the room timer HUD keeps the just-finished room's card
        /// on screen and rolls in a second card for the next room, showing up
        /// to two rooms at once before sliding the older one off.
        /// </summary>
        public static bool ChainRoomTimers
        {
            get => _d.ChainRoomTimers;
            set { _d.ChainRoomTimers = value; Save(); }
        }

        /// <summary>
        /// When true, runs that exit back through the same transition they
        /// entered from (exitTo == entryFrom) are not saved or uploaded.
        /// </summary>
        public static bool SkipBacktrackRuns
        {
            get => _d.SkipBacktrackRuns;
            set { _d.SkipBacktrackRuns = value; Save(); }
        }

        /// <summary>
        /// When true, the room timer HUD ignores runs that exit back through
        /// the same transition they entered from (exitTo == entryFrom): no
        /// finished time is shown and the live card just clears.
        /// </summary>
        public static bool SkipBacktrackTimer
        {
            get => _d.SkipBacktrackTimer;
            set { _d.SkipBacktrackTimer = value; Save(); }
        }

        public static bool OnlineEnabled
        {
            get => _d.OnlineEnabled;
            set { _d.OnlineEnabled = value; Save(); }
        }

        public static string DeviceId
        {
            get => _d.DeviceId;
            set { _d.DeviceId = value; Save(); }
        }

        public static string DisplayName
        {
            get => _d.DisplayName;
            set { _d.DisplayName = value; Save(); }
        }

        public static string ApiBaseUrl
        {
            get => _d.ApiBaseUrl;
            set { _d.ApiBaseUrl = value; Save(); }
        }

        /// <summary>Modifier bits a run must HAVE to pass the UI filter.</summary>
        public static int ModifierRequireMask
        {
            get => _d.ModifierRequireMask;
            set { _d.ModifierRequireMask = value; Save(); }
        }

        /// <summary>Modifier bits a run must NOT have to pass the UI filter.</summary>
        public static int ModifierExcludeMask
        {
            get => _d.ModifierExcludeMask;
            set { _d.ModifierExcludeMask = value; Save(); }
        }

        /// <summary>
        /// Experimental: room warping. When false (the default) the warp
        /// buttons are hidden everywhere and warping is unavailable.
        /// </summary>
        public static bool RoomWarpEnabled
        {
            get => _d.RoomWarpEnabled;
            set { _d.RoomWarpEnabled = value; Save(); }
        }

        /// <summary>
        /// Experimental: camera follow. When false (the default) the camera
        /// buttons in the Runs tab are hidden and the camera never leaves
        /// the player.
        /// </summary>
        public static bool CameraFollowEnabled
        {
            get => _d.CameraFollowEnabled;
            set { _d.CameraFollowEnabled = value; Save(); }
        }

        /// <summary>
        /// Ensures a device ID exists, generating one if needed.
        /// Called when online features are first enabled.
        /// </summary>
        public static void EnsureDeviceId()
        {
            if (!string.IsNullOrEmpty(_d.DeviceId)) return;
            _d.DeviceId = System.Guid.NewGuid().ToString("N");
            Save();
        }

        // ── Init ─────────────────────────────────────────────────────────────

        public static void Init(string baseDirectory)
        {
            _filePath = System.IO.Path.Combine(
                System.IO.Path.Combine(baseDirectory, "ReplayMod"), "settings.txt");
            Load();
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        public static void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                foreach (var f in typeof(GhostSettingsData).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    lines.Add($"{f.Name}={System.Convert.ToString(f.GetValue(_d), System.Globalization.CultureInfo.InvariantCulture)}");
                System.IO.File.WriteAllLines(_filePath, lines.ToArray());
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[GhostSettings] Save failed: {ex.Message}");
            }
        }

        private static void Load()
        {
            if (!System.IO.File.Exists(_filePath)) return;
            try
            {
                var defaults = new GhostSettingsData();
                foreach (string line in System.IO.File.ReadAllLines(_filePath))
                {
                    int sep = line.IndexOf('=');
                    if (sep < 0) continue;
                    string key = line.Substring(0, sep).Trim();
                    string val = line.Substring(sep + 1).Trim();

                    var f = typeof(GhostSettingsData).GetField(key,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (f == null) continue;

                    try { f.SetValue(_d, ParseField(f.FieldType, val, f.GetValue(defaults))); }
                    catch { /* leave default */ }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[GhostSettings] Load failed: {ex.Message}");
            }
        }

        private static object ParseField(System.Type t, string val, object fallback)
        {
            try
            {
                if (t == typeof(bool))   return bool.Parse(val);
                if (t == typeof(int))    return int.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(float))  return float.Parse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(string)) return val;
            }
            catch { }
            return fallback;
        }
    }
}