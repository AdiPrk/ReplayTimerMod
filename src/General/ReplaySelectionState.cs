using System.Collections.Generic;
using System.Linq;

namespace ReplayTimerMod
{
    public sealed class ReplaySelectionState
    {
        private readonly HashSet<string> playbackSnapshotIds = new HashSet<string>();

        public string? SelectedSnapshotId { get; private set; }
        public IReadOnlyCollection<string> PlaybackSnapshotIds => playbackSnapshotIds;

        public void SelectSnapshot(string? snapshotId)
        {
            SelectedSnapshotId = string.IsNullOrWhiteSpace(snapshotId)
                ? null
                : snapshotId;
        }

        public bool IsPlaybackSelected(string snapshotId) =>
            !string.IsNullOrWhiteSpace(snapshotId)
            && playbackSnapshotIds.Contains(snapshotId);

        public bool SetPlaybackSelected(string snapshotId, bool selected)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                return false;

            return selected
                ? playbackSnapshotIds.Add(snapshotId)
                : playbackSnapshotIds.Remove(snapshotId);
        }

        public bool TogglePlayback(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                return false;

            if (playbackSnapshotIds.Remove(snapshotId))
                return false;

            playbackSnapshotIds.Add(snapshotId);
            return true;
        }

        public bool RemoveSnapshot(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                return false;

            bool changed = playbackSnapshotIds.Remove(snapshotId);
            if (SelectedSnapshotId == snapshotId)
            {
                SelectedSnapshotId = null;
                changed = true;
            }

            return changed;
        }

        public int RemoveRoute(IEnumerable<ReplaySnapshot> snapshots) =>
            RemoveSnapshots(snapshots.Select(snapshot => snapshot.SnapshotId));

        public int RemoveScene(IEnumerable<RouteReplayHistory> histories) =>
            RemoveSnapshots(histories.SelectMany(history =>
                history.Snapshots.Select(snapshot => snapshot.SnapshotId)));

        public int PruneToExisting(IEnumerable<ReplaySnapshot> snapshots)
        {
            var validIds = new HashSet<string>(snapshots.Select(snapshot => snapshot.SnapshotId));
            int removed = 0;

            foreach (string snapshotId in playbackSnapshotIds.ToArray())
            {
                if (validIds.Contains(snapshotId))
                    continue;

                playbackSnapshotIds.Remove(snapshotId);
                removed++;
            }

            if (SelectedSnapshotId != null && !validIds.Contains(SelectedSnapshotId))
            {
                SelectedSnapshotId = null;
                removed++;
            }

            return removed;
        }

        public void ClearAll()
        {
            SelectedSnapshotId = null;
            playbackSnapshotIds.Clear();
        }

        private int RemoveSnapshots(IEnumerable<string> snapshotIds)
        {
            int removed = 0;
            foreach (string snapshotId in snapshotIds)
            {
                if (RemoveSnapshot(snapshotId))
                    removed++;
            }

            return removed;
        }
    }
}
