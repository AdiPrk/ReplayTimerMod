using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
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
        // Experimental features (Config > Experimental).
        public bool RoomWarpEnabled = false;
        public bool CameraFollowEnabled = false;
    }

    public static class GhostSettings
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GhostSettings");

        private static string _filePath = "";
        private static readonly GhostSettingsData _d = new GhostSettingsData();

        // Slider-driven setters (color/alpha) fire every drag frame; writing
        // the file per frame would hammer the disk. Those setters go through
        // SaveThrottled, which defers to a Flush (picker close / menu close).
        private const float MinSaveIntervalSec = 0.5f;
        private static bool _dirty;
        private static float _lastSaveRealtime = float.NegativeInfinity;

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
            set
            {
                _d.ColorR = value.r; _d.ColorG = value.g; _d.ColorB = value.b;
                _d.Alpha = value.a;
                SaveThrottled();
            }
        }

        public static float GhostAlpha
        {
            get => _d.Alpha;
            set { _d.Alpha = Mathf.Clamp01(value); SaveThrottled(); }
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
        /// entered from (exitTo == entryFrom) are not saved.
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

        // ── Init ─────────────────────────────────────────────────────────────

        public static void Init(string baseDirectory)
        {
            _filePath = Path.Combine(
                Path.Combine(baseDirectory, "ReplayMod"), "settings.txt");
            Load();
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        public static void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            try
            {
                var lines = new List<string>();
                foreach (var f in typeof(GhostSettingsData).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    lines.Add($"{f.Name}={Convert.ToString(f.GetValue(_d), CultureInfo.InvariantCulture)}");
                File.WriteAllLines(_filePath, lines.ToArray());
                _dirty = false;
                _lastSaveRealtime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                Log.LogError($"[GhostSettings] Save failed: {ex.Message}");
            }
        }

        private static void SaveThrottled()
        {
            if (Time.realtimeSinceStartup - _lastSaveRealtime >= MinSaveIntervalSec)
            {
                Save();
                return;
            }
            _dirty = true;
        }

        /// <summary>
        /// Write any deferred (throttled) changes to disk. Called when the
        /// color picker or the replay panel closes.
        /// </summary>
        public static void Flush()
        {
            if (_dirty) Save();
        }

        private static void Load()
        {
            if (!File.Exists(_filePath)) return;
            try
            {
                var defaults = new GhostSettingsData();
                foreach (string line in File.ReadAllLines(_filePath))
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
            catch (Exception ex)
            {
                Log.LogError($"[GhostSettings] Load failed: {ex.Message}");
            }
        }

        private static object ParseField(Type t, string val, object fallback)
        {
            try
            {
                if (t == typeof(bool))   return bool.Parse(val);
                if (t == typeof(int))    return int.Parse(val, CultureInfo.InvariantCulture);
                if (t == typeof(float))  return float.Parse(val, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(string)) return val;
            }
            catch { }
            return fallback;
        }
    }
}
