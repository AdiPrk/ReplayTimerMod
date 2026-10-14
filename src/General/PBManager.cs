using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReplayTimerMod
{
    public static class PBManager
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("PBManager");

        private static readonly Dictionary<RoomKey, List<ReplaySnapshot>> _histories =
            new Dictionary<RoomKey, List<ReplaySnapshot>>();

        private static readonly Dictionary<RoomKey, ReplaySnapshot> _currentPbs =
            new Dictionary<RoomKey, ReplaySnapshot>();

        private static ReplaySelectionState? _selectionState;

        public static IEnumerable<KeyValuePair<RoomKey, RecordedRoom>> AllPBs() =>
            _currentPbs.Select(kvp =>
                new KeyValuePair<RoomKey, RecordedRoom>(kvp.Key, kvp.Value.Room));

        public static IEnumerable<RouteReplayHistory> AllHistories() =>
            _histories
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
            _histories.Clear();
            _currentPbs.Clear();
            _selectionState?.ClearAll();

            foreach (var snapshot in DataStore.LoadAll())
                AddSnapshot(snapshot, persist: false, allowDuplicate: false,
                    enforceLimit: false);

            int pruned = PruneAllHistories(GhostSettings.MaxSavedReplaysPerRoute,
                persist: true);
            Log.LogInfo($"[PBManager] Loaded {_currentPbs.Count} active PBs from disk ({_histories.Values.Sum(list => list.Count)} snapshots)"
                + (pruned > 0 ? $", pruned {pruned} overflow snapshots" : string.Empty));
        }

        public static ReplaySelectionState? SelectionState => _selectionState;

        public static void SetSelectionState(ReplaySelectionState? state)
        {
            _selectionState = state;
            _selectionState?.PruneToExisting(_histories.Values.SelectMany(list => list));
        }

        public static RecordedRoom? GetPB(RoomKey key)
        {
            _currentPbs.TryGetValue(key, out var snapshot);
            return snapshot?.Room;
        }

        public static ReplaySnapshot? GetPBSnapshot(RoomKey key)
        {
            ReplaySnapshot snapshot;
            return _currentPbs.TryGetValue(key, out snapshot) ? snapshot : null;
        }

        public static IList<ReplaySnapshot> GetHistory(RoomKey key)
        {
            if (!_histories.TryGetValue(key, out var history))
                return new ReplaySnapshot[0]; // Array.Empty doesn't work net35
            return OrderSnapshots(history);
        }

        public static RouteReplayHistory? GetRouteHistory(RoomKey key)
        {
            if (!_histories.TryGetValue(key, out var history) || history.Count == 0)
                return null;

            var ordered = OrderSnapshots(history);
            return new RouteReplayHistory(key, ordered, ordered[0]);
        }

        public static ReplaySnapshot? GetSnapshot(RoomKey key, string snapshotId)
        {
            if (!_histories.TryGetValue(key, out var history))
                return null;

            return history.FirstOrDefault(snapshot => snapshot.SnapshotId == snapshotId);
        }

        public static IList<ReplaySnapshot> GetPlaybackCandidates(string sceneName,
            string entryFromScene)
        {
            return OrderSnapshots(_histories
                .Where(kvp => kvp.Key.SceneName == sceneName
                    && kvp.Key.EntryFromScene == entryFromScene)
                .SelectMany(kvp => kvp.Value)
                .ToList());
        }

        public static bool UpdateSnapshotVisuals(RoomKey key, string snapshotId,
            bool hasVisualOverride, Color color)
        {
            if (!_histories.TryGetValue(key, out var history))
                return false;

            int index = history.FindIndex(snapshot => snapshot.SnapshotId == snapshotId);
            if (index < 0)
                return false;

            history[index] = history[index].WithVisualOverride(hasVisualOverride, color);
            RefreshCurrent(key, history);
            DataStore.UpdateSnapshotVisuals(key, snapshotId, hasVisualOverride,
                color.r, color.g, color.b, color.a);
            return true;
        }

        public static bool WouldStoreRun(RoomKey key, float time)
        {
            if (!_currentPbs.TryGetValue(key, out var existing)) return true;
            return time < existing.TotalTime;
        }

        public static EvaluationResult Evaluate(RecordedRoom run, bool saveAllRuns = false,
            bool usedCheats = false)
        {
            float newTime = run.TotalTime;
            var snapshot = ReplaySnapshot.CreateNew(run, usedCheats: usedCheats);

            if (_currentPbs.TryGetValue(run.Key, out var existing))
            {
                if (newTime < existing.TotalTime)
                {
                    float improvement = existing.TotalTime - newTime;
                    if (!AddSnapshot(snapshot, persist: true, allowDuplicate: false))
                    {
                        Log.LogInfo($"[PBManager] Skipped duplicate PB for {run.Key}: {TimeUtil.Format(newTime)}");
                        return new EvaluationResult(ResultKind.DuplicateRun, newTime, existing.TotalTime, improvement);
                    }

                    Log.LogInfo($"[PBManager] New PB! {run.Key} {TimeUtil.Format(newTime)} " +
                                $"(was {TimeUtil.Format(existing.TotalTime)}, -{TimeUtil.Format(improvement)})");
                    return new EvaluationResult(ResultKind.NewPB, newTime, existing.TotalTime, improvement);
                }

                float delta = newTime - existing.TotalTime;
                if (!saveAllRuns)
                {
                    Log.LogInfo($"[PBManager] Missed PB for {run.Key}: {TimeUtil.Format(newTime)} (+{TimeUtil.Format(delta)})");
                    return new EvaluationResult(ResultKind.MissedPB, newTime, existing.TotalTime, delta);
                }

                if (!AddSnapshot(snapshot, persist: true, allowDuplicate: false))
                {
                    Log.LogInfo($"[PBManager] Did not store history for {run.Key}: {TimeUtil.Format(newTime)} (+{TimeUtil.Format(delta)})");
                    return new EvaluationResult(ResultKind.DuplicateRun, newTime, existing.TotalTime, delta);
                }

                Log.LogInfo($"[PBManager] Saved history for {run.Key}: {TimeUtil.Format(newTime)} (+{TimeUtil.Format(delta)})");
                return new EvaluationResult(ResultKind.SavedHistory, newTime, existing.TotalTime, delta);
            }

            if (!AddSnapshot(snapshot, persist: true, allowDuplicate: false))
            {
                Log.LogInfo($"[PBManager] Skipped duplicate first run for {run.Key}: {TimeUtil.Format(newTime)}");
                return new EvaluationResult(ResultKind.DuplicateRun, newTime, null, null);
            }

            Log.LogInfo($"[PBManager] First run for {run.Key}: {TimeUtil.Format(newTime)}");
            return new EvaluationResult(ResultKind.FirstRun, newTime, null, null);
        }

        public enum ImportOutcome { Imported, Duplicate, RouteFull }

        public static bool WouldKeepReplay(RoomKey key, float time)
        {
            int max = Mathf.Max(1, GhostSettings.MaxSavedReplaysPerRoute);
            if (!_histories.TryGetValue(key, out var history) || history.Count < max)
                return true;
            var ordered = OrderSnapshots(history);
            return time < ordered[max - 1].TotalTime;
        }

        public static ImportOutcome ImportPB(RecordedRoom room)
        {
            var snapshot = ReplaySnapshot.CreateNew(room);

            if (_histories.TryGetValue(room.Key, out var history)
                && HasDuplicate(history, snapshot))
            {
                Log.LogInfo($"[PBManager] Skipped duplicate import for {room.Key} ({TimeUtil.Format(room.TotalTime)})");
                return ImportOutcome.Duplicate;
            }

            if (!WouldKeepReplay(room.Key, room.TotalTime))
            {
                Log.LogInfo($"[PBManager] Import skipped — route {room.Key} is full "
                    + $"({GhostSettings.MaxSavedReplaysPerRoute} max) and "
                    + $"{TimeUtil.Format(room.TotalTime)} is slower than all kept replays");
                return ImportOutcome.RouteFull;
            }

            AddSnapshot(snapshot, persist: true, allowDuplicate: true);

            bool isCurrent = _currentPbs.TryGetValue(room.Key, out var current)
                && current.SnapshotId == snapshot.SnapshotId;
            Log.LogInfo($"[PBManager] Imported {room.Key} ({room.FrameCount} frames, {TimeUtil.Format(room.TotalTime)})"
                + (isCurrent ? " [current]" : " [history]"));
            return ImportOutcome.Imported;
        }

        public static int PruneRouteHistory(RoomKey key, List<ReplaySnapshot> history,
            int limit, bool persist)
        {
            int retainedCount = Mathf.Max(1, limit);
            var ordered = OrderSnapshots(history);
            var pruned = ordered.Skip(retainedCount).ToArray();

            if (pruned.Length > 0)
            {
                var prunedIds = new HashSet<string>(pruned.Select(snapshot =>
                    snapshot.SnapshotId));
                history.RemoveAll(snapshot => prunedIds.Contains(snapshot.SnapshotId));

                foreach (var snapshot in pruned)
                    _selectionState?.RemoveSnapshot(snapshot.SnapshotId);
            }

            RefreshCurrent(key, history);

            if (persist && pruned.Length > 0)
                DataStore.ReplaceRouteSnapshots(key, OrderSnapshots(history));

            return pruned.Length;
        }

        public static int PruneAllHistories(int limit, bool persist)
        {
            int pruned = 0;
            foreach (var key in _histories.Keys.ToArray())
            {
                if (!_histories.TryGetValue(key, out var history))
                    continue;

                pruned += PruneRouteHistory(key, history, limit, persist);
            }

            return pruned;
        }

        public static bool DeleteSnapshot(RoomKey key, string snapshotId)
        {
            if (!_histories.TryGetValue(key, out var history)) return false;

            int removed = history.RemoveAll(snapshot => snapshot.SnapshotId == snapshotId);
            if (removed == 0) return false;

            _selectionState?.RemoveSnapshot(snapshotId);
            DataStore.DeleteSnapshot(key, snapshotId);
            RefreshCurrent(key, history);
            Log.LogInfo($"[PBManager] Deleted snapshot {key}#{snapshotId}");
            return true;
        }

        public static bool DeletePB(RoomKey key)
        {
            if (!_histories.TryGetValue(key, out var history)) return false;

            _selectionState?.RemoveRoute(history);
            _histories.Remove(key);
            _currentPbs.Remove(key);
            DataStore.DeleteRoute(key);
            Log.LogInfo($"[PBManager] Deleted route {key}");
            return true;
        }

        public static int DeleteScene(string sceneName)
        {
            var routeHistories = _histories
                .Where(kvp => kvp.Key.SceneName == sceneName)
                .Select(kvp =>
                {
                    var ordered = OrderSnapshots(kvp.Value);
                    return new RouteReplayHistory(kvp.Key, ordered, ordered[0]);
                })
                .ToList();
            int removedSnapshots = routeHistories.Sum(history => history.Count);

            _selectionState?.RemoveScene(routeHistories);

            foreach (var history in routeHistories)
            {
                _histories.Remove(history.Key);
                _currentPbs.Remove(history.Key);
            }

            DataStore.DeleteScene(sceneName);
            Log.LogInfo($"[PBManager] Deleted {routeHistories.Count} routes ({removedSnapshots} snapshots) for scene {sceneName}");
            return removedSnapshots;
        }

        public static void DeleteAll()
        {
            var scenes = _histories.Keys.Select(k => k.SceneName).Distinct().ToList();
            _histories.Clear();
            _currentPbs.Clear();
            _selectionState?.ClearAll();
            foreach (var scene in scenes) DataStore.DeleteScene(scene);
            Log.LogInfo($"[PBManager] Deleted all entries ({scenes.Count} scenes)");
        }

        private static bool AddSnapshot(ReplaySnapshot snapshot, bool persist,
            bool allowDuplicate, bool enforceLimit = true)
        {
            if (!_histories.TryGetValue(snapshot.Key, out var history))
            {
                history = new List<ReplaySnapshot>();
                _histories[snapshot.Key] = history;
            }

            if (!allowDuplicate && HasDuplicate(history, snapshot))
                return false;

            history.Add(snapshot);

            if (!enforceLimit)
            {
                RefreshCurrent(snapshot.Key, history);
                if (persist)
                    DataStore.ReplaceRouteSnapshots(snapshot.Key, OrderSnapshots(history));
                return true;
            }

            int pruned = PruneRouteHistory(snapshot.Key, history,
                GhostSettings.MaxSavedReplaysPerRoute, persist);

            if (persist && pruned == 0)
                DataStore.SaveSnapshot(snapshot);

            return pruned == 0 || history.Contains(snapshot);
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
                _histories.Remove(key);
                _currentPbs.Remove(key);
                return;
            }

            _currentPbs[key] = OrderSnapshots(history)[0];
        }
    }

    public enum ResultKind { FirstRun, NewPB, SavedHistory, MissedPB, DuplicateRun }

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
