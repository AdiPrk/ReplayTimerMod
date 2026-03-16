#if HOLLOW_KNIGHT_BUILD
using System.IO;
using System.Reflection;
using Modding;

namespace ReplayTimerMod
{
    public class ReplayTimerModHK : Mod
    {
        private FrameRecorder frameRecorder = null!;
        private GhostPlayback ghostPlayback = null!;
        private ReplayUI replayUI = null!;

        private bool lateInitDone = false;

        public static ReplayTimerModHK? Instance { get; private set; }

        public ReplayTimerModHK() : base("ReplayTimerMod")
        {
        }

        public override string GetVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        public override void Initialize()
        {
            Instance = this;
            Log("Initialize");

            GameHooks.Init();

            // HK build uses lightweight in-memory config backing for now.
            GhostSettings.Init(new BepInEx.Configuration.ConfigFile());

            string saveDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Low",
                "Team Cherry",
                "Hollow Knight");
            if (!Directory.Exists(saveDir))
                saveDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            DataStore.Init(saveDir);
            PBManager.Init();

            frameRecorder = new FrameRecorder();
            ghostPlayback = new GhostPlayback();
            replayUI = new ReplayUI();

            RoomTracker.Init();

            RoomTracker.OnRoomEnter += OnRoomEnter;
            RoomTracker.OnRoomExit += OnRoomExit;
            RoomTracker.OnRecordingDiscarded += OnRecordingDiscarded;

            ModHooks.HeroUpdateHook += OnHeroUpdate;
        }

        private void OnHeroUpdate()
        {
            TryLateInit();

            bool shouldTick = false;
            try { shouldTick = LoadRemover.ShouldTick(); } catch { }

            RoomTracker.Tick(shouldTick);
            frameRecorder.Tick(shouldTick);
            ghostPlayback.Tick(shouldTick);
            replayUI.Tick();
        }

        private void TryLateInit()
        {
            if (lateInitDone) return;
            if (HeroController.instance == null) return;

            lateInitDone = true;
            Log("Hero ready - setting up UI and ghost");
            ghostPlayback.Setup();
            replayUI.Setup();
        }

        private void OnRoomEnter(string sceneName, string entryFromScene)
        {
            frameRecorder.StartRecording();
            ghostPlayback.StartPlayback(sceneName, entryFromScene);
        }

        private void OnRoomExit(string sceneName, string entryFromScene, string exitToScene, float lrTime)
        {
            ghostPlayback.StopPlayback();

            RoomKey key = new RoomKey(sceneName, entryFromScene, exitToScene);
            RecordedRoom? recording = frameRecorder.FinishRecording(key, lrTime);

            if (recording != null)
            {
                PBManager.Evaluate(recording);
                replayUI.OnPBUpdated();
            }
        }

        private void OnRecordingDiscarded()
        {
            ghostPlayback.StopPlayback();
            frameRecorder.DiscardRecording();
        }
    }
}
#endif
