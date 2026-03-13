using BepInEx.Logging;
using System.Collections.Generic;

namespace ReplayTimerMod
{
    // In-memory PB store. Loaded from disk on Init(), persisted on every
    // new PB. Thread-safety is not a concern — all calls happen on the
    // Unity main thread.
    public static class PBManager
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("PBManager");

        private static readonly Dictionary<RoomKey, RecordedRoom> pbs =
            new Dictionary<RoomKey, RecordedRoom>();

        public static int Count => pbs.Count;

        // Used by DebugOverlay to scan PBs mid-run (no exit known yet).
        public static IEnumerable<KeyValuePair<RoomKey, RecordedRoom>> AllPBs() => pbs;

        public static void Init()
        {
            pbs.Clear();
            foreach (var kvp in DataStore.LoadAll())
                pbs[kvp.Key] = kvp.Value;

            Log.LogInfo($"[PBManager] Loaded {pbs.Count} PBs from disk");
        }

        // Returns the current PB for a route, or null if none exists.
        public static RecordedRoom? GetPB(RoomKey key)
        {
            pbs.TryGetValue(key, out var pb);
            return pb;
        }

        public static float? GetPBTime(RoomKey key)
        {
            pbs.TryGetValue(key, out var pb);
            return pb?.TotalTime;
        }

        // Compares the incoming run against the stored PB.
        // Saves to disk and updates memory if it's a new PB.
        // Returns the result so callers (debug UI, future UI) can react.
        public static EvaluationResult Evaluate(RecordedRoom run)
        {
            float newTime = run.TotalTime;

            if (pbs.TryGetValue(run.Key, out var existing))
            {
                if (newTime < existing.TotalTime)
                {
                    float improvement = existing.TotalTime - newTime;
                    pbs[run.Key] = run;
                    DataStore.SaveEntry(run);
                    Log.LogInfo($"[PBManager] New PB! {run.Key} {FormatTime(newTime)} " +
                                $"(was {FormatTime(existing.TotalTime)}, " +
                                $"-{FormatTime(improvement)})");
                    return new EvaluationResult(ResultKind.NewPB, newTime,
                                                existing.TotalTime, improvement);
                }
                else
                {
                    float delta = newTime - existing.TotalTime;
                    Log.LogInfo($"[PBManager] Missed PB for {run.Key}: " +
                                $"{FormatTime(newTime)} (+{FormatTime(delta)})");
                    return new EvaluationResult(ResultKind.MissedPB, newTime,
                                                existing.TotalTime, delta);
                }
            }
            else
            {
                // First recorded run for this route — always a PB.
                pbs[run.Key] = run;
                DataStore.SaveEntry(run);
                Log.LogInfo($"[PBManager] First run for {run.Key}: {FormatTime(newTime)}");
                return new EvaluationResult(ResultKind.FirstRun, newTime, null, null);
            }
        }

        private static string FormatTime(float t)
        {
            int millis = (int)(t * 100) % 100;
            int seconds = (int)t % 60;
            int minutes = (int)t / 60;
            return $"{minutes}:{seconds:00}.{millis:00}";
        }
    }

    public enum ResultKind { FirstRun, NewPB, MissedPB }

    public class EvaluationResult
    {
        public ResultKind Kind { get; }
        public float NewTime { get; }
        public float? OldPBTime { get; }
        public float? Delta { get; } // negative = improvement, positive = missed by

        public EvaluationResult(ResultKind kind, float newTime,
                                 float? oldPBTime, float? delta)
        {
            Kind = kind;
            NewTime = newTime;
            OldPBTime = oldPBTime;
            Delta = delta;
        }
    }
}