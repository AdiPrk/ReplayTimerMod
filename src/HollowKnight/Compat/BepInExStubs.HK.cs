#if HOLLOW_KNIGHT_BUILD
using System;
using System.Collections.Generic;
using Modding;

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

namespace BepInEx.Configuration
{
    public sealed class ConfigFile
    {
        private readonly Dictionary<string, object> entries = new Dictionary<string, object>();

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription configDescription)
        {
            string fullKey = section + "." + key;
            if (!entries.TryGetValue(fullKey, out object existing))
            {
                var created = new ConfigEntry<T>(defaultValue);
                entries[fullKey] = created;
                return created;
            }

            return (ConfigEntry<T>)existing;
        }
    }

    public sealed class ConfigEntry<T>
    {
        public ConfigEntry(T initialValue)
        {
            Value = initialValue;
        }

        public T Value { get; set; }
    }

    public sealed class ConfigDescription
    {
        public ConfigDescription(string description, object? acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }

        public string Description { get; }
        public object? AcceptableValues { get; }
    }

    public sealed class AcceptableValueRange<T>
    {
        public AcceptableValueRange(T minValue, T maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T MinValue { get; }
        public T MaxValue { get; }
    }
}
#endif
