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
                if (t == typeof(bool))  return bool.Parse(val);
                if (t == typeof(int))   return int.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }
    }
}