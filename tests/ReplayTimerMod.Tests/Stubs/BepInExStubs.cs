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
        public static readonly List<string> Messages = new List<string>();

        public static ManualLogSource CreateLogSource(string sourceName) =>
            new ManualLogSource(sourceName);
    }
}
