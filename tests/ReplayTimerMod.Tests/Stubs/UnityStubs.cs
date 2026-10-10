#pragma warning disable IDE0060

namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }
    }

    public static class Mathf
    {
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Abs(float v) => v < 0f ? -v : v;
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static int RoundToInt(float v) =>
            (int)System.Math.Round(v, System.MidpointRounding.AwayFromZero);
    }

    public static class Time
    {
        public static float realtimeSinceStartup { get; set; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}
