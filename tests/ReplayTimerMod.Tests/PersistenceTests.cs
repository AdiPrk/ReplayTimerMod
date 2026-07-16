using System.IO;
using System.Linq;
using ReplayTimerMod;
using UnityEngine;
using Xunit;

namespace ReplayTimerMod.Tests
{
    /// <summary>Base for tests that touch the static PBManager / DataStore /
    /// GhostSettings trio: gives each test a fresh temp data dir and resets
    /// the static settings that influence PB logic.</summary>
    public abstract class PersistenceTestBase : System.IDisposable
    {
        protected readonly Rooms.TempDir Dir = new Rooms.TempDir();

        protected PersistenceTestBase()
        {
            // GhostSettings.Save() no-ops while its file path is unset/missing;
            // point it at the temp dir so property sets are harmless, and
            // restore the defaults PB logic depends on.
            Directory.CreateDirectory(Path.Combine(Dir.Path, "ReplayMod"));
            GhostSettings.Init(Dir.Path);
            // GhostSettings is static — reset every field the tests mutate so
            // values can't bleed between tests.
            GhostSettings.MaxSavedReplaysPerRoute = 5;
            GhostSettings.SaveAllRunsEnabled = false;
            GhostSettings.DisplayName = "";
            GhostSettings.OnlineEnabled = false;
            GhostSettings.GhostAlpha = 0.4f;
            GhostSettings.ModifierRequireMask = 0;
            GhostSettings.ModifierExcludeMask = 0;
            GhostSettings.DeviceId = "";

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
            var run = Rooms.Room(frames: 30, time: 5f, modifiers: 0);
            var result = PBManager.Evaluate(run);

            Assert.Equal(ResultKind.FirstRun, result.Kind);
            Assert.NotNull(result.Snapshot);
            Assert.Null(result.OldPBTime);
            Assert.Equal(5f, PBManager.GetPB(run.Key)!.TotalTime);
        }

        [Fact]
        public void FasterRun_IsNewPB_WithImprovement()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 28, time: 4.25f, modifiers: 0, seed: 2));

            Assert.Equal(ResultKind.NewPB, result.Kind);
            Assert.Equal(5f, result.OldPBTime);
            Assert.Equal(0.75f, result.Delta!.Value, 3);
            Assert.Equal(4.25f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }

        [Fact]
        public void SlowerRun_SameMask_IsMissedPB_NotStored()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 40, time: 6f, modifiers: 0, seed: 2));

            Assert.Equal(ResultKind.MissedPB, result.Kind);
            Assert.Null(result.Snapshot);
            Assert.Single(PBManager.GetHistory(Rooms.Key()));
        }

        [Fact]
        public void SlowerRun_NewMask_IsNewMaskPB_StoredAndUploadable()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0b1, seed: 1));
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 40, time: 6f, modifiers: 0b11, seed: 2));

            Assert.Equal(ResultKind.NewMaskPB, result.Kind);
            Assert.NotNull(result.Snapshot);
            Assert.Equal(0b11, result.Snapshot!.Modifiers);
            // Delta is still computed vs the overall PB.
            Assert.Equal(1f, result.Delta!.Value, 3);
            // Overall PB unchanged — mask PB is not the PB snapshot.
            Assert.Equal(5f, PBManager.GetPBSnapshot(Rooms.Key())!.TotalTime);
            Assert.Equal(2, PBManager.GetHistory(Rooms.Key()).Count);
        }

        [Fact]
        public void SlowerRun_UnknownMask_NeverMaskPB()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            var result = PBManager.Evaluate(Rooms.Room(frames: 40, time: 6f,
                modifiers: ModifierMask.Unknown, seed: 2));
            Assert.Equal(ResultKind.MissedPB, result.Kind);
        }

        [Fact]
        public void WouldStoreRun_MatchesEvaluateDecisions()
        {
            var key = Rooms.Key();
            Assert.True(PBManager.WouldStoreRun(key, 9f, 0));       // first run

            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            Assert.True(PBManager.WouldStoreRun(key, 4f, 0));       // faster
            Assert.False(PBManager.WouldStoreRun(key, 6f, 0));      // slower, same mask
            Assert.True(PBManager.WouldStoreRun(key, 6f, 0b10));    // new mask
            Assert.False(PBManager.WouldStoreRun(key, 6f, ModifierMask.Unknown));
        }

        [Fact]
        public void SaveAllRuns_StoresHistory_AndDetectsDuplicates()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));

            var slower = Rooms.Room(frames: 40, time: 6f, modifiers: 0, seed: 2);
            var stored = PBManager.Evaluate(slower, saveAllRuns: true);
            Assert.Equal(ResultKind.SavedHistory, stored.Kind);

            // The exact same replay again → duplicate, not double-stored.
            var dup = PBManager.Evaluate(slower, saveAllRuns: true);
            Assert.Equal(ResultKind.DuplicateRun, dup.Kind);
            Assert.Equal(2, PBManager.GetHistory(Rooms.Key()).Count);
        }

        [Fact]
        public void Prune_KeepsBestN_PlusPerMaskBests()
        {
            GhostSettings.MaxSavedReplaysPerRoute = 2;

            PBManager.Evaluate(Rooms.Room(frames: 30, time: 1.0f, modifiers: 0, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 2.0f, modifiers: 0, seed: 2),
                saveAllRuns: true);
            // Mask-1 best: slower than everything, but prune-exempt.
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 3.0f, modifiers: 1, seed: 3));
            // Beyond the limit and not a mask best → pruned immediately.
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 2.5f, modifiers: 0, seed: 4),
                saveAllRuns: true);

            var history = PBManager.GetHistory(Rooms.Key());
            Assert.Equal(3, history.Count); // 1.0, 2.0 (window) + 3.0 (mask best)
            Assert.Equal(new[] { 1.0f, 2.0f, 3.0f },
                history.Select(s => s.TotalTime).ToArray());
        }

        [Fact]
        public void Prune_MaskExemptions_AreCapped()
        {
            GhostSettings.MaxSavedReplaysPerRoute = 1;

            // 10 distinct masks, ascending times. Only 8 exemptions allowed.
            for (int i = 0; i < 10; i++)
                PBManager.Evaluate(Rooms.Room(frames: 30, time: 1f + i,
                    modifiers: 1 << i, seed: i));

            var history = PBManager.GetHistory(Rooms.Key());
            Assert.Equal(8, history.Count); // MaxMaskBestExemptions
        }

        [Fact]
        public void Persistence_SurvivesReload_IncludingModifierMask()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0b101, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 6f, modifiers: 0b1, seed: 2));

            PBManager.Init(); // reload everything from DataStore

            var history = PBManager.GetHistory(Rooms.Key());
            Assert.Equal(2, history.Count);
            Assert.Equal(0b101, history[0].Modifiers);
            Assert.Equal(0b1, history[1].Modifiers);
            Assert.Equal(5f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }

        [Fact]
        public void ImportPB_DetectsDuplicates_AndFullRoutes()
        {
            var room = Rooms.Room(frames: 30, time: 5f, seed: 1);
            Assert.Equal(PBManager.ImportOutcome.Imported, PBManager.ImportPB(room));
            Assert.Equal(PBManager.ImportOutcome.Duplicate, PBManager.ImportPB(room));

            GhostSettings.MaxSavedReplaysPerRoute = 1;
            // Slower than every kept replay, unknown mask → won't be kept.
            var slow = Rooms.Room(frames: 40, time: 60f, seed: 2);
            Assert.Equal(PBManager.ImportOutcome.RouteFull, PBManager.ImportPB(slow));

            // But a slower run with a NEW known mask is prune-exempt → kept.
            var masked = Rooms.Room(frames: 40, time: 60f, modifiers: 2, seed: 3);
            Assert.Equal(PBManager.ImportOutcome.Imported, PBManager.ImportPB(masked));
        }

        [Fact]
        public void DeleteSnapshot_PromotesNextBest()
        {
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            PBManager.Evaluate(Rooms.Room(frames: 30, time: 4f, modifiers: 0, seed: 2));

            var pb = PBManager.GetPBSnapshot(Rooms.Key())!;
            Assert.True(PBManager.DeleteSnapshot(Rooms.Key(), pb.SnapshotId));
            Assert.Equal(5f, PBManager.GetPB(Rooms.Key())!.TotalTime);
        }

        [Fact]
        public void SetServerIds_PersistsRunIdAndShareCode()
        {
            var result = PBManager.Evaluate(
                Rooms.Room(frames: 30, time: 5f, modifiers: 0, seed: 1));
            var key = Rooms.Key();
            string id = result.Snapshot!.SnapshotId;

            PBManager.SetServerIds(key, id, "run-77", null);
            PBManager.SetServerIds(key, id, null, "aZ9");

            var snap = PBManager.GetSnapshot(key, id)!;
            Assert.Equal("run-77", snap.ServerRunId);  // null left it unchanged
            Assert.Equal("aZ9", snap.ShareCode);

            PBManager.Init();
            snap = PBManager.GetHistory(key).Single();
            Assert.Equal("run-77", snap.ServerRunId);
            Assert.Equal("aZ9", snap.ShareCode);
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
            var room = Rooms.Room(frames: 30, time: 5f, modifiers: 6, seed: 1);
            var snapshot = new ReplaySnapshot("snap01", 638000000000000000L, room,
                encodedData: null, hasVisualOverride: true,
                colorR: 0.25f, colorG: 0.5f, colorB: 0.75f, alpha: 0.9f,
                serverRunId: "run-1", shareCode: "aZ9");
            DataStore.SaveSnapshot(snapshot);

            var loaded = DataStore.LoadAll().Single();
            Assert.Equal("snap01", loaded.SnapshotId);
            Assert.Equal(638000000000000000L, loaded.CapturedAtUtcTicks);
            Assert.Equal(room.Key, loaded.Key);
            Assert.Equal(5f, loaded.TotalTime);
            Assert.Equal(6, loaded.Modifiers);
            Assert.True(loaded.HasVisualOverride);
            Assert.Equal(0.25f, loaded.ColorR);
            Assert.Equal(0.9f, loaded.Alpha);
            Assert.Equal("run-1", loaded.ServerRunId);
            Assert.Equal("aZ9", loaded.ShareCode);
        }

        [Fact]
        public void Load_JsonModifiersField_IsFallbackWhenBlobHasNoTrailer()
        {
            // Pre-feature blob (no RTMX trailer) + hand-set JSON field.
            var room = Rooms.Room(frames: 20, time: 3f,
                modifiers: ModifierMask.Unknown, seed: 1);
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(room));

            string file = Path.Combine(_dataDir, room.Key.SceneName + ".json");
            File.WriteAllText(file, File.ReadAllText(file)
                .Replace("\"modifiers\":-1", "\"modifiers\":3"));

            Assert.Equal(3, DataStore.LoadAll().Single().Modifiers);
        }

        [Fact]
        public void Load_BlobTrailer_WinsOverJsonField()
        {
            var room = Rooms.Room(frames: 20, time: 3f, modifiers: 5, seed: 1);
            DataStore.SaveSnapshot(ReplaySnapshot.CreateNew(room));

            string file = Path.Combine(_dataDir, room.Key.SceneName + ".json");
            File.WriteAllText(file, File.ReadAllText(file)
                .Replace("\"modifiers\":5", "\"modifiers\":9"));

            // The RTM3 blob is the authority.
            Assert.Equal(5, DataStore.LoadAll().Single().Modifiers);
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
            var keyA = Rooms.Key();                                  // scene S, A→B
            var keyB = Rooms.Key(to: "Elsewhere");                   // same scene, A→Elsewhere
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
            GhostSettings.DisplayName = "Hornet";
            GhostSettings.OnlineEnabled = true;
            GhostSettings.GhostAlpha = 0.7f;
            GhostSettings.ModifierRequireMask = 0b101;

            // Wipe in-memory state by loading fresh from the same file.
            GhostSettings.DisplayName = "Hornet"; // ensure a Save happened
            GhostSettings.Init(Dir.Path);

            Assert.Equal("Hornet", GhostSettings.DisplayName);
            Assert.True(GhostSettings.OnlineEnabled);
            Assert.Equal(0.7f, GhostSettings.GhostAlpha, 3);
            Assert.Equal(0b101, GhostSettings.ModifierRequireMask);
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
                "DisplayName=Still Works",
            });
            GhostSettings.Init(Dir.Path);

            Assert.Equal(5, GhostSettings.MaxSavedReplaysPerRoute); // default
            Assert.True(GhostSettings.GhostEnabled);                 // default
            Assert.Equal("Still Works", GhostSettings.DisplayName);
        }

        [Fact]
        public void EnsureDeviceId_Generates32HexOnce()
        {
            GhostSettings.DeviceId = "";
            GhostSettings.EnsureDeviceId();
            string id = GhostSettings.DeviceId;

            Assert.Matches("^[0-9a-f]{32}$", id);
            GhostSettings.EnsureDeviceId();
            Assert.Equal(id, GhostSettings.DeviceId); // stable
        }

        [Fact]
        public void MaxSavedReplays_ClampsToAtLeastOne()
        {
            GhostSettings.MaxSavedReplaysPerRoute = -3;
            Assert.Equal(1, GhostSettings.MaxSavedReplaysPerRoute);
        }
    }
}
