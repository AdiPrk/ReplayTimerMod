using System;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// File logger for ReplayTimerMod. All ManualLogSource instances in the
    /// mod are created via <see cref="CreateSource"/> rather than
    /// BepInEx.Logging.Logger.CreateLogSource so we can subscribe to their
    /// LogEvent at construction time and tee every message to a plain text
    /// file in the mod folder.
    ///
    /// The file is written to &lt;logDirectory&gt;/ReplayMod.log.
    /// The previous session is kept as ReplayMod.log.bak.
    ///
    /// Call <see cref="Init"/> as early as possible (before any other mod
    /// code runs) so that sources created afterwards are captured. Sources
    /// created before Init() are still registered with BepInEx normally;
    /// they just won't appear in the file (there shouldn't be any, since
    /// all sources in this project are created via CreateSource).
    /// </summary>
    public static class ModLog
    {
        private static StreamWriter? _writer;
        private static readonly object _lock = new object();

        // ── Lifecycle ────────────────────────────────────────────────────────

        public static void Init(string logDirectory)
        {
            if (_writer != null) return;
            try
            {
                Directory.CreateDirectory(logDirectory);
                string logPath = Path.Combine(logDirectory, "ReplayMod.log");
                string bakPath = logPath + ".bak";

                if (File.Exists(logPath))
                {
                    try { File.Copy(logPath, bakPath, overwrite: true); } catch { }
                }

                _writer = new StreamWriter(
                    new FileStream(logPath, FileMode.Create, FileAccess.Write,
                        FileShare.ReadWrite), Encoding.UTF8)
                { AutoFlush = true };

                WriteRaw(
                    "========================================" + Environment.NewLine +
                    $"  ReplayTimerMod  {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    "========================================" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ReplayTimerMod] ModLog.Init failed: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                try { _writer?.Flush(); _writer?.Close(); } catch { }
                _writer = null;
            }
        }

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>
        /// Drop-in replacement for BepInEx.Logging.Logger.CreateLogSource.
        /// Creates the source normally (so BepInEx sees it) and additionally
        /// subscribes to its LogEvent to tee output to the mod's log file.
        /// </summary>
        public static ManualLogSource CreateSource(string sourceName)
        {
            var source = Logger.CreateLogSource(sourceName);
            source.LogEvent += OnLogEvent;
            return source;
        }

        // ── Event handler ────────────────────────────────────────────────────

        private static void OnLogEvent(object sender, LogEventArgs e)
        {
            if (_writer == null) return;
            try
            {
                WriteRaw(
                    $"[{DateTime.Now:HH:mm:ss.fff}] [{e.Level,-7}:{e.Source.SourceName,12}] {e.Data}" +
                    Environment.NewLine);
            }
            catch { }
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private static void WriteRaw(string text)
        {
            if (_writer == null) return;
            try { lock (_lock) { _writer.Write(text); } } catch { }
        }
    }
}