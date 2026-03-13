using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;

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

        // Used by DebugOverlay / ReplayUI to iterate all PBs.
        public static IEnumerable<KeyValuePair<RoomKey, RecordedRoom>> AllPBs() => pbs;

        public static void Init()
        {
            pbs.Clear();
            foreach (var kvp in DataStore.LoadAll())
                pbs[kvp.Key] = kvp.Value;

            Log.LogInfo($"[PBManager] Loaded {pbs.Count} PBs from disk");
        }

        // ── Read ──────────────────────────────────────────────────────────────
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

        // ── Evaluate (called after a live run) ────────────────────────────────
        // Saves to disk and updates memory only if it's a new PB.
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
                pbs[run.Key] = run;
                DataStore.SaveEntry(run);
                Log.LogInfo($"[PBManager] First run for {run.Key}: {FormatTime(newTime)}");
                return new EvaluationResult(ResultKind.FirstRun, newTime, null, null);
            }
        }

        // ── Import (paste from clipboard) ─────────────────────────────────────
        // Unconditionally stores a decoded replay, overwriting any existing PB
        // for that route. Used when the user explicitly pastes a shared replay.
        // Returns true if the import succeeded and the caller should refresh UI.
        public static bool ImportPB(RecordedRoom room)
        {
            pbs[room.Key] = room;
            DataStore.SaveEntry(room);
            Log.LogInfo($"[PBManager] Imported {room.Key} " +
                        $"({room.FrameCount} frames, {FormatTime(room.TotalTime)})");
            return true;
        }

        // ── Delete single entry ───────────────────────────────────────────────
        // Removes one route from memory and disk.
        // Returns true if something was actually removed.
        public static bool DeletePB(RoomKey key)
        {
            if (!pbs.Remove(key)) return false;
            DataStore.DeleteEntry(key);
            Log.LogInfo($"[PBManager] Deleted {key}");
            return true;
        }

        // ── Delete all entries for a scene ────────────────────────────────────
        // Removes every route whose SceneName matches, from memory and disk.
        // Returns the number of entries removed.
        public static int DeleteScene(string sceneName)
        {
            var keys = pbs.Keys.Where(k => k.SceneName == sceneName).ToList();
            foreach (var k in keys) pbs.Remove(k);
            DataStore.DeleteScene(sceneName);
            Log.LogInfo($"[PBManager] Deleted {keys.Count} entries for scene {sceneName}");
            return keys.Count;
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
        public float? Delta { get; }

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