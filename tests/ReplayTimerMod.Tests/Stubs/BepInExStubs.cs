// Minimal BepInEx.Logging stand-ins (same approach as the HK builds'
// src/HollowKnight/Compat/BepInExStubs.HK.cs). Messages are collected so a
// test can assert on log output if it ever needs to.

using System.Collections.Generic;

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public string SourceName { get; }

        public ManualLogSource(string sourceName) => SourceName = sourceName;

        public void LogDebug(object data) => Record("DEBUG", data);
        public void LogInfo(object data) => Record("INFO", data);
        public void LogMessage(object data) => Record("MESSAGE", data);
        public void LogWarning(object data) => Record("WARN", data);
        public void LogError(object data) => Record("ERROR", data);

        private void Record(string level, object data) =>
            Logger.Messages.Add($"[{level}] [{SourceName}] {data}");
    }

    public static class Logger
    {
        /// <summary>Everything logged during the test run (test-only).</summary>
        public static readonly List<string> Messages = new List<string>();

        public static ManualLogSource CreateLogSource(string sourceName) =>
            new ManualLogSource(sourceName);
    }
}
