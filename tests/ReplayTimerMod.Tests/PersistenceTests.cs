using System.IO;
using System.Linq;
using ReplayTimerMod;
using UnityEngine;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public abstract class PersistenceTestBase : System.IDisposable
    {
        protected readonly Rooms.TempDir Dir = new Rooms.TempDir();

        protected PersistenceTestBase()
        {
            Directory.CreateDirectory(Path.Combine(Dir.Path, "ReplayMod"));
            GhostSettings.Init(Dir.Path);
            GhostSettings.MaxSavedReplaysPerRoute = 5;
            GhostSettings.SaveAllRunsEnabled = false;
            GhostSettings.ChainRoomTimers = false;
            GhostSettings.GhostAlpha = 0.4f;

            DataStore.Init(Path.Combine(Dir.Path, "data"));
            PBManager.Init();
            PBManager.SetSelectionState(null);
        }

        public void Dispose() => Dir.Dispose();
    }

    public class PBManagerTests : PersistenceTestBase
    {
        [Fact]
        public void FirstRun_IsStored_AndBecomesPB()
        {
            var run = Rooms.Room(frames: 30, time: 5f);
            var result = PBManager.Evaluate(run);

            Assert.Equal(ResultKind.FirstRun, result.Kind);
            Assert.Null(result.OldPBTime);
            Assert.Equal(5f, PBManager.GetPB(run.Key)!.TotalTime);
        }

        [Fact]
        public void FasterRun_IsNewPB_WithImprovement()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 28, time: 4.25f, seed: 2));

            Assert.Equal(ResultKind.NewPB, result.Kind);
            Assert.Equal(5f, result.OldPBTime);
            Assert.Equal(0.75f, result.Delta!.Value, 3);
            Assert.Equal(4.25f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }

        [Fact]
        public void SlowerRun_IsMissedPB_NotStored()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 40, time: 6f, seed: 2));

            Assert.Equal(ResultKind.MissedPB, result.Kind);
            Assert.Single(PBManager.GetHistory(Rooms.Key()));
        }

        [Fact]
        public void WouldStoreRun_MatchesEvaluateDecisions()
        {
            var key = Rooms.Key();
            Assert.True(PBManager.WouldStoreRun(key, 9f));

            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));
            Assert.True(PBManager.WouldStoreRun(key, 4f));
            Assert.False(PBManager.WouldStoreRun(key, 6f));
        }

        [Fact]
        public void SaveAllRuns_StoresHistory_AndDetectsDuplicates()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));

            var slower = Rooms.Room(frames: 40, time: 6f, seed: 2);
            var stored = PBManager.Evaluate(slower, saveAllRuns: true);
            Assert.Equal(ResultKind.SavedHistory, stored.Kind);

            var dup = PBManager.Evaluate(slower, saveAllRuns: true);
            Assert.Equal(ResultKind.DuplicateRun, dup.Kind);
            Assert.Equal(2, PBManager.GetHistory(Rooms.Key()).Count);
        }

        [Fact]
        public void Prune_KeepsBestN()
        {
            GhostSettings.MaxSavedReplaysPerRoute = 2;

            PBManager.Evaluate(Rooms.Room(frames: 30, time: 1.0f, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 2.0f, seed: 2),
                saveAllRuns: true);
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 2.5f, seed: 4),
                saveAllRuns: true);

            var history = PBManager.GetHistory(Rooms.Key());
            Assert.Equal(new[] { 1.0f, 2.0f },
                history.Select(s => s.TotalTime).ToArray());
        }

        [Fact]
        public void Persistence_SurvivesReload()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 6f, seed: 2),
                saveAllRuns: true);

            PBManager.Init();

            var history = PBManager.GetHistory(Rooms.Key());
            Assert.Equal(2, history.Count);
            Assert.Equal(6f, history[1].TotalTime);
            Assert.Equal(5f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }

        [Fact]
        public void CheatedRun_IsFlagged_AndFlagSurvivesRecolorAndReload()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1), usedCheats: true);
            var pb = PBManager.GetPBSnapshot(Rooms.Key())!;
            Assert.True(pb.UsedCheats);

            PBManager.UpdateSnapshotVisuals(Rooms.Key(), pb.SnapshotId, true, new Color(1f, 0f, 0f, 1f));
            PBManager.Init();

            Assert.True(PBManager.GetPBSnapshot(Rooms.Key())!.UsedCheats);
        }

        [Fact]
        public void ImportPB_DetectsDuplicates_AndFullRoutes()
        {
            var room = Rooms.Room(frames: 30, time: 5f, seed: 1);
            Assert.Equal(PBManager.ImportOutcome.Imported, PBManager.ImportPB(room));
            Assert.Equal(PBManager.ImportOutcome.Duplicate, PBManager.ImportPB(room));

            GhostSettings.MaxSavedReplaysPerRoute = 1;
            var slow = Rooms.Room(frames: 40, time: 60f, seed: 2);
            Assert.Equal(PBManager.ImportOutcome.RouteFull, PBManager.ImportPB(slow));
        }

        [Fact]
        public void DeleteSnapshot_PromotesNextBest()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 4f, seed: 2));

            var pb = PBManager.GetPBSnapshot(Rooms.Key())!;
            Assert.True(PBManager.DeleteSnapshot(Rooms.Key(), pb.SnapshotId));
            Assert.Equal(5f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }
    }

    public class DataStoreTests : PersistenceTestBase
    {
        private readonly string _dataDir;

        public DataStoreTests()
        {
            _dataDir = Path.Combine(Dir.Path, "data");
        }

        [Fact]
        public void SaveAndLoad_RoundTripsAllMetadata()
        {
            var room = Rooms.Room(frames: 30, time: 5f, seed: 1);
            var snapshot = new ReplaySnapshot("snap01", 638000000000000000L, room,
                encodedData: null, hasVisualOverride: true,
                colorR: 0.25f, colorG: 0.5f, colorB: 0.75f, alpha: 0.9f,
                usedCheats: true);
            DataStore.SaveSnapshot(snapshot);

            var loaded = DataStore.LoadAll().Single();
            Assert.Equal("snap01", loaded.SnapshotId);
            Assert.Equal(638000000000000000L, loaded.CapturedAtUtcTicks);
            Assert.Equal(room.Key, loaded.Key);
            Assert.Equal(5f, loaded.TotalTime);
            Assert.True(loaded.HasVisualOverride);
            Assert.Equal(0.25f, loaded.ColorR);
            Assert.Equal(0.9f, loaded.Alpha);
            Assert.True(loaded.UsedCheats);
        }

        [Fact]
        public void Load_SkipsCorruptFilesAndEntries()
        {
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(
                Rooms.Room(frames: 20, time: 3f, seed: 1)));
            File.WriteAllText(Path.Combine(_dataDir, "Broken.json"),
                "{ not json at all");
            File.WriteAllText(Path.Combine(_dataDir, "BadData.json"),
                "{\"entries\":[{\"snapshotId\":\"x\",\"sceneName\":\"BadData\"," +
                "\"entryFromScene\":\"A\",\"exitToScene\":\"B\"," +
                "\"totalTime\":1,\"data\":\"!!!notbase64!!!\"}]}");

            Assert.Single(DataStore.LoadAll());
        }

        [Fact]
        public void DeleteRoute_RemovesOnlyThatRoute()
        {
            var keyA = Rooms.Key();
            var keyB = Rooms.Key(to: "Elsewhere");
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(
                Rooms.Room(frames: 20, key: keyA, seed: 1)));
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(
                Rooms.Room(frames: 20, key: keyB, seed: 2)));

            DataStore.DeleteRoute(keyA);
            var remaining = DataStore.LoadAll().Single();
            Assert.Equal(keyB, remaining.Key);
        }

        [Fact]
        public void DeleteScene_RemovesFile()
        {
            var room = Rooms.Room(frames: 20, seed: 1);
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(room));
            string file = Path.Combine(_dataDir, room.Key.SceneName + ".json");
            Assert.True(File.Exists(file));

            DataStore.DeleteScene(room.Key.SceneName);
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void ReplaceRouteSnapshots_SkipsMismatchedKeys()
        {
            var key = Rooms.Key();
            var good = ReplaySnapshot.CreateNew(Rooms.Room(frames: 20, key: key, seed: 1));
            var wrong = ReplaySnapshot.CreateNew(Rooms.Room(frames: 20,
                key: Rooms.Key("OtherScene"), seed: 2));

            DataStore.ReplaceRouteSnapshots(key, new[] { good, wrong });
            var loaded = DataStore.LoadAll().Single();
            Assert.Equal(key, loaded.Key);
        }
    }

    public class GhostSettingsTests : PersistenceTestBase
    {
        [Fact]
        public void SaveAndLoad_RoundTrip()
        {
            GhostSettings.ChainRoomTimers = true;
            GhostSettings.GhostAlpha = 0.7f;
            GhostSettings.Flush();

            string file = Path.Combine(Dir.Path, "ReplayMod", "settings.txt");
            string saved = File.ReadAllText(file);

            GhostSettings.ChainRoomTimers = false;
            GhostSettings.GhostAlpha = 0.1f;
            GhostSettings.Flush();

            File.WriteAllText(file, saved);
            GhostSettings.Init(Dir.Path);

            Assert.True(GhostSettings.ChainRoomTimers);
            Assert.Equal(0.7f, GhostSettings.GhostAlpha, 3);
        }

        [Fact]
        public void Load_IgnoresGarbageLines_KeepsDefaults()
        {
            string file = Path.Combine(Dir.Path, "ReplayMod", "settings.txt");
            File.WriteAllLines(file, new[]
            {
                "MaxSavedReplaysPerRoute=not-a-number",
                "GhostEnabled=maybe",
                "linewithoutequals",
                "UnknownField=whatever",
            });
            GhostSettings.Init(Dir.Path);

            Assert.Equal(5, GhostSettings.MaxSavedReplaysPerRoute);
            Assert.True(GhostSettings.GhostEnabled);
        }

        [Fact]
        public void MaxSavedReplays_ClampsToAtLeastOne()
        {
            GhostSettings.MaxSavedReplaysPerRoute = -3;
            Assert.Equal(1, GhostSettings.MaxSavedReplaysPerRoute);
        }
    }
}
