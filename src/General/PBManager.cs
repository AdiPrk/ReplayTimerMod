using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;

namespace ReplayTimerMod
{
    // In-memory PB store. Loaded from disk on Init(), persisted on every new PB.
    // All calls happen on the Unity main thread - no thread-safety needed.
    public static class PBManager
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("PBManager");

        private static readonly Dictionary<RoomKey, List<ReplaySnapshot>> histories =
            new Dictionary<RoomKey, List<ReplaySnapshot>>();

        private static readonly Dictionary<RoomKey, ReplaySnapshot> currentPbs =
            new Dictionary<RoomKey, ReplaySnapshot>();

        public static IEnumerable<KeyValuePair<RoomKey, RecordedRoom>> AllPBs() =>
            currentPbs.Select(kvp =>
                new KeyValuePair<RoomKey, RecordedRoom>(kvp.Key, kvp.Value.Room));

        public static IEnumerable<RouteReplayHistory> AllHistories() =>
            histories
                .OrderBy(kvp => kvp.Key.SceneName)
                .ThenBy(kvp => kvp.Key.EntryFromScene)
                .ThenBy(kvp => kvp.Key.ExitToScene)
                .Select(kvp =>
                {
                    var ordered = OrderSnapshots(kvp.Value);
                    return new RouteReplayHistory(kvp.Key, ordered, ordered[0]);
                });

        public static void Init()
        {
            histories.Clear();
            currentPbs.Clear();

            foreach (var snapshot in DataStore.LoadAll())
                AddSnapshot(snapshot, persist: false, allowDuplicate: false);

            Log.LogInfo($"[PBManager] Loaded {currentPbs.Count} active PBs from disk ({histories.Values.Sum(list => list.Count)} snapshots)");
        }

        // ── Read ──────────────────────────────────────────────────────────────

        public static RecordedRoom? GetPB(RoomKey key)
        {
            currentPbs.TryGetValue(key, out var snapshot);
            return snapshot?.Room;
        }

        public static IReadOnlyList<ReplaySnapshot> GetHistory(RoomKey key)
        {
            if (!histories.TryGetValue(key, out var history))
                return System.Array.Empty<ReplaySnapshot>();
            return OrderSnapshots(history);
        }

        public static RouteReplayHistory? GetRouteHistory(RoomKey key)
        {
            if (!histories.TryGetValue(key, out var history) || history.Count == 0)
                return null;

            var ordered = OrderSnapshots(history);
            return new RouteReplayHistory(key, ordered, ordered[0]);
        }

        // Returns true if the given time would be stored by Evaluate() - i.e.
        // it's either the first run for this key or faster than the existing PB.
        public static bool WouldBePB(RoomKey key, float time)
        {
            if (!currentPbs.TryGetValue(key, out var existing)) return true;
            return time < existing.TotalTime;
        }

        // ── Evaluate (called after a live run) ────────────────────────────────

        public static EvaluationResult Evaluate(RecordedRoom run)
        {
            float newTime = run.TotalTime;

            if (currentPbs.TryGetValue(run.Key, out var existing))
            {
                if (newTime < existing.TotalTime)
                {
                    float improvement = existing.TotalTime - newTime;
                    var snapshot = ReplaySnapshot.CreateNew(run);
                    AddSnapshot(snapshot, persist: true, allowDuplicate: false);
                    Log.LogInfo($"[PBManager] New PB! {run.Key} {TimeUtil.Format(newTime)} " +
                                $"(was {TimeUtil.Format(existing.TotalTime)}, -{TimeUtil.Format(improvement)})");
                    return new EvaluationResult(ResultKind.NewPB, newTime, existing.TotalTime, improvement);
                }

                float delta = newTime - existing.TotalTime;
                Log.LogInfo($"[PBManager] Missed PB for {run.Key}: {TimeUtil.Format(newTime)} (+{TimeUtil.Format(delta)})");
                return new EvaluationResult(ResultKind.MissedPB, newTime, existing.TotalTime, delta);
            }

            var firstSnapshot = ReplaySnapshot.CreateNew(run);
            AddSnapshot(firstSnapshot, persist: true, allowDuplicate: false);
            Log.LogInfo($"[PBManager] First run for {run.Key}: {TimeUtil.Format(newTime)}");
            return new EvaluationResult(ResultKind.FirstRun, newTime, null, null);
        }

        // ── Import ────────────────────────────────────────────────────────────
        // Appends a decoded replay to local history (used for clipboard paste).

        public static bool ImportPB(RecordedRoom room)
        {
            var snapshot = ReplaySnapshot.CreateNew(room);
            bool added = AddSnapshot(snapshot, persist: true, allowDuplicate: false);
            if (!added)
            {
                Log.LogInfo($"[PBManager] Skipped duplicate import for {room.Key} ({TimeUtil.Format(room.TotalTime)})");
                return false;
            }

            bool isCurrent = currentPbs.TryGetValue(room.Key, out var current)
                && current.SnapshotId == snapshot.SnapshotId;
            Log.LogInfo($"[PBManager] Imported {room.Key} ({room.FrameCount} frames, {TimeUtil.Format(room.TotalTime)})"
                + (isCurrent ? " [current]" : " [history]"));
            return true;
        }

        // ── Delete ────────────────────────────────────────────────────────────

        public static bool DeleteSnapshot(RoomKey key, string snapshotId)
        {
            if (!histories.TryGetValue(key, out var history)) return false;

            int removed = history.RemoveAll(snapshot => snapshot.SnapshotId == snapshotId);
            if (removed == 0) return false;

            DataStore.DeleteSnapshot(key, snapshotId);
            RefreshCurrent(key, history);
            Log.LogInfo($"[PBManager] Deleted snapshot {key}#{snapshotId}");
            return true;
        }

        public static bool DeletePB(RoomKey key)
        {
            if (!histories.Remove(key)) return false;
            currentPbs.Remove(key);
            DataStore.DeleteRoute(key);
            Log.LogInfo($"[PBManager] Deleted route {key}");
            return true;
        }

        public static int DeleteScene(string sceneName)
        {
            var keys = histories.Keys.Where(k => k.SceneName == sceneName).ToList();
            int removedSnapshots = keys.Sum(key => histories[key].Count);

            foreach (var key in keys)
            {
                histories.Remove(key);
                currentPbs.Remove(key);
            }

            DataStore.DeleteScene(sceneName);
            Log.LogInfo($"[PBManager] Deleted {keys.Count} routes ({removedSnapshots} snapshots) for scene {sceneName}");
            return removedSnapshots;
        }

        public static void DeleteAll()
        {
            var scenes = histories.Keys.Select(k => k.SceneName).Distinct().ToList();
            histories.Clear();
            currentPbs.Clear();
            foreach (var scene in scenes) DataStore.DeleteScene(scene);
            Log.LogInfo($"[PBManager] Deleted all entries ({scenes.Count} scenes)");
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static bool AddSnapshot(ReplaySnapshot snapshot, bool persist,
            bool allowDuplicate)
        {
            if (!histories.TryGetValue(snapshot.Key, out var history))
            {
                history = new List<ReplaySnapshot>();
                histories[snapshot.Key] = history;
            }

            if (!allowDuplicate && HasDuplicate(history, snapshot))
                return false;

            history.Add(snapshot);
            RefreshCurrent(snapshot.Key, history);
            if (persist) DataStore.SaveSnapshot(snapshot);
            return true;
        }

        private static bool HasDuplicate(List<ReplaySnapshot> history,
            ReplaySnapshot candidate) =>
            history.Any(existing => existing.EncodedData == candidate.EncodedData);

        private static ReplaySnapshot[] OrderSnapshots(List<ReplaySnapshot> history) =>
            history
                .OrderBy(snapshot => snapshot.TotalTime)
                .ThenBy(snapshot => snapshot.HasCapturedAt ? 0 : 1)
                .ThenBy(snapshot => snapshot.CapturedAtUtcTicks)
                .ThenBy(snapshot => snapshot.SnapshotId)
                .ToArray();

        private static void RefreshCurrent(RoomKey key, List<ReplaySnapshot> history)
        {
            if (history.Count == 0)
            {
                histories.Remove(key);
                currentPbs.Remove(key);
                return;
            }

            currentPbs[key] = OrderSnapshots(history)[0];
        }
    }

    public enum ResultKind { FirstRun, NewPB, MissedPB }

    public class EvaluationResult
    {
        public ResultKind Kind { get; }
        public float NewTime { get; }
        public float? OldPBTime { get; }
        public float? Delta { get; }

        public EvaluationResult(ResultKind kind, float newTime, float? oldPBTime, float? delta)
        {
            Kind = kind;
            NewTime = newTime;
            OldPBTime = oldPBTime;
            Delta = delta;
        }
    }
}