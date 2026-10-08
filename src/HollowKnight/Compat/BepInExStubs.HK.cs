#if HOLLOW_KNIGHT_BUILD
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        private readonly string source;

        internal ManualLogSource(string sourceName)
        {
            source = sourceName;
        }

        public void LogInfo(object data) => Logger.Log($"[{source}] {data}");
        public void LogWarning(object data) => Modding.Logger.LogWarn($"[{source}] {data}");
        public void LogError(object data) => Modding.Logger.LogError($"[{source}] {data}");
        public void LogDebug(object data) => Modding.Logger.LogDebug($"[{source}] {data}");
    }

    public static class Logger
    {
        public static ManualLogSource CreateLogSource(string sourceName) => new ManualLogSource(sourceName);

        public static void Log(string data) => Modding.Logger.Log(data);
        public static void LogWarning(string data) => Modding.Logger.LogWarn(data);
        public static void LogError(string data) => Modding.Logger.LogError(data);
        public static void LogDebug(string data) => Modding.Logger.LogDebug(data);
    }
}
#endif
