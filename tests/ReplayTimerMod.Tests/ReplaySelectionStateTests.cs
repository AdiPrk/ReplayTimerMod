using System.Collections.Generic;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class ReplaySelectionStateCameraFollowTests
    {
        [Fact]
        public void ToggleCameraFollow_SetsAndClears()
        {
            var s = new ReplaySelectionState();
            Assert.Null(s.CameraFollowSnapshotId);

            Assert.True(s.ToggleCameraFollow("a"));
            Assert.Equal("a", s.CameraFollowSnapshotId);

            Assert.False(s.ToggleCameraFollow("a"));
            Assert.Null(s.CameraFollowSnapshotId);
        }

        [Fact]
        public void ToggleCameraFollow_MovesSingleSlotBetweenRuns()
        {
            var s = new ReplaySelectionState();
            s.ToggleCameraFollow("a");

            Assert.True(s.ToggleCameraFollow("b"));
            Assert.Equal("b", s.CameraFollowSnapshotId);
        }

        [Fact]
        public void ToggleCameraFollow_IgnoresEmptyId()
        {
            var s = new ReplaySelectionState();
            Assert.False(s.ToggleCameraFollow(""));
            Assert.Null(s.CameraFollowSnapshotId);
        }

        [Fact]
        public void RemoveSnapshot_ClearsFollowSlot()
        {
            var s = new ReplaySelectionState();
            s.ToggleCameraFollow("a");

            Assert.True(s.RemoveSnapshot("a"));
            Assert.Null(s.CameraFollowSnapshotId);
        }

        [Fact]
        public void RemoveSnapshot_LeavesOtherFollowSlotAlone()
        {
            var s = new ReplaySelectionState();
            s.SetPlaybackSelected("a", true);
            s.ToggleCameraFollow("b");

            s.RemoveSnapshot("a");
            Assert.Equal("b", s.CameraFollowSnapshotId);
        }

        [Fact]
        public void PruneToExisting_DropsStaleFollowSlot()
        {
            var s = new ReplaySelectionState();
            s.ToggleCameraFollow("gone");

            int removed = s.PruneToExisting(new List<ReplaySnapshot>());
            Assert.Equal(1, removed);
            Assert.Null(s.CameraFollowSnapshotId);
        }

        [Fact]
        public void ClearAll_ClearsFollowSlot()
        {
            var s = new ReplaySelectionState();
            s.ToggleCameraFollow("a");

            s.ClearAll();
            Assert.Null(s.CameraFollowSnapshotId);
        }
    }
}
