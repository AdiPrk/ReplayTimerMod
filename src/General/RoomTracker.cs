using System;
using BepInEx.Logging;
using UnityEngine;
using GlobalEnums;

namespace ReplayTimerMod
{
    public static class RoomTracker
    {
        public const float MAX_ROOM_TIME = 180f;

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTracker");

        public static bool IsRecording { get; private set; } = false;
        public static string CurrentScene { get; private set; } = "";
        public static string PreviousScene { get; private set; } = "";
        public static string EntryFromScene { get; private set; } = "";
        public static float CurrentRoomTime { get; private set; } = 0f;

        public static bool RoomUsedDebugAbilities { get; private set; } = false;

        public static event Action<string, string>? OnRoomEnter;
        public static event Action<string, string, string, float>? OnRoomExit;
        public static event Action? OnRecordingDiscarded;

        public static bool KeepGhostPlaybackOnDiscard { get; private set; } = false;

        public static event Action<string>? OnRunCancelled;

        private static bool _initialized = false;
        private static string _lastSceneName = "";
        private static bool _pendingGateTransition = false;
        private static bool _menuTraversalPending = false;
        private static bool _wasLoadingSavestate = false;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            GameHooks.OnPlayerDead += HandleInvalidation;
            GameHooks.OnGateTransitionBegin += HandleGateTransitionBegin;

            DebugModBridge.TryHook();
        }

        private static void HandleGateTransitionBegin(string destScene, string entryGate)
        {
            if (destScene == KnownScenes.MenuTitle || destScene == KnownScenes.QuitToMenu)
            {
                _pendingGateTransition = false;
                _menuTraversalPending = true;
                Log.LogDebug($"[Gate] menu load -> '{destScene}' - clearing pending gate");
                return;
            }

#if V1221
            // 1.2.2.1 fires this for every load; only gate transitions have an entry gate.
            if (string.IsNullOrEmpty(entryGate))
            {
                _pendingGateTransition = false;
                Log.LogDebug($"[Gate] gateless load -> '{destScene}' - not a gate transition");
                return;
            }
#endif

            _pendingGateTransition = true;
            Log.LogDebug($"[Gate] pending -> {destScene} via '{entryGate}'");
        }

        private static void HandleInvalidation() => HandleInvalidation(keepGhostPlayback: false);

        private static void HandleInvalidation(bool keepGhostPlayback)
        {
            if (IsRecording)
            {
                Log.LogInfo($"[RoomTracker] Invalidated in {CurrentScene} - discarding");
                IsRecording = false;
                KeepGhostPlaybackOnDiscard = keepGhostPlayback;
                OnRecordingDiscarded?.Invoke();
            }
            CurrentRoomTime = 0f;
            _pendingGateTransition = false;
        }

        private static void OnActiveSceneChanged(string fromName, string toName)
        {
            if (DebugModBridge.IsLoadingSavestate || fromName == "Room_Mender_House" || toName == "Room_Mender_House")
            {
                Log.LogInfo("[RoomTracker] Savestate detected - invalidating");
                HandleInvalidation();
            }

            bool arrivedViaGate = _pendingGateTransition;
            _pendingGateTransition = false;

            if (_menuTraversalPending)
            {
                _menuTraversalPending = false;
                arrivedViaGate = false;
            }

            if (string.IsNullOrEmpty(fromName)
                || fromName == KnownScenes.MenuTitle || fromName == KnownScenes.QuitToMenu)
                arrivedViaGate = false;

            bool toMenu = toName == KnownScenes.MenuTitle || toName == KnownScenes.QuitToMenu;

            if (IsRecording)
            {
                if (arrivedViaGate && !IsOverTime() && !toMenu)
                {
                    string exitedScene = CurrentScene;
                    string exitedFromScene = EntryFromScene;
                    string exitedTo = toName;
                    float exitedTime = CurrentRoomTime;

                    Log.LogInfo($"[RoomTracker] Exit: {exitedScene} [{exitedFromScene}->{exitedTo}] {TimeUtil.Format(exitedTime)}");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    OnRoomExit?.Invoke(exitedScene, exitedFromScene, exitedTo, exitedTime);
                }
                else
                {
                    if (IsOverTime())
                        Log.LogInfo($"[RoomTracker] Over time limit in {CurrentScene} - discarding");
                    else if (!arrivedViaGate)
                        Log.LogInfo($"[RoomTracker] Non-gate exit from {CurrentScene} - discarding");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    KeepGhostPlaybackOnDiscard = false;
                    OnRecordingDiscarded?.Invoke();
                }
            }

#if SILKSONG_BUILD
            bool wasWarping = QuickWarp.IsWarping;
#else
            bool wasWarping = false;
#endif

            if (arrivedViaGate && !toMenu && !wasWarping)
            {
                PreviousScene = CurrentScene;
                CurrentScene = toName;
                EntryFromScene = fromName;
                CurrentRoomTime = 0f;
                IsRecording = true;
                RoomUsedDebugAbilities = false;

                Log.LogInfo($"[RoomTracker] Enter: {CurrentScene} from {EntryFromScene}");
                OnRoomEnter?.Invoke(CurrentScene, EntryFromScene);
            }
            else
            {
                PreviousScene = CurrentScene;
                CurrentScene = toName;
                EntryFromScene = "";
                CurrentRoomTime = 0f;
                IsRecording = false;

                if (wasWarping)
                    Log.LogInfo($"[RoomTracker] Warp arrival in {toName} - not recording");
                else
                    Log.LogInfo($"[RoomTracker] IDLE in {toName}");
            }

#if SILKSONG_BUILD
            if (wasWarping)
                QuickWarp.NotifyArrival();
#endif

            bool IsOverTime() => CurrentRoomTime > MAX_ROOM_TIME;
        }

        public static void Tick(bool shouldTick)
        {
            if (!DebugModBridge.IsAvailable)
                DebugModBridge.TryHook();

            bool isLoadingNow = DebugModBridge.IsLoadingSavestate;
            if (isLoadingNow != _wasLoadingSavestate)
            {
                _wasLoadingSavestate = isLoadingNow;
                Log.LogInfo($"[RoomTracker] Savestate load {(isLoadingNow ? "started" : "finished")} - invalidating");
                HandleInvalidation();
            }

            string currentSceneName = GetCurrentSceneName();
            if (!string.IsNullOrEmpty(currentSceneName) && currentSceneName != _lastSceneName)
            {
                string fromName = _lastSceneName;
                _lastSceneName = currentSceneName;
                OnActiveSceneChanged(fromName, currentSceneName);
            }

            if (!IsRecording) return;

            if (!RoomUsedDebugAbilities)
            {
                DebugAbilityKind? ability = DebugModBridge.GetActiveDebugAbility();
                if (ability.HasValue)
                {
                    RoomUsedDebugAbilities = true;
                    if (GhostSettings.CancelRunOnCheats)
                    {
                        Log.LogInfo($"[RoomTracker] Debug ability '{ability.Value}' detected during {CurrentScene} - cancelling run");
                        HandleInvalidation(keepGhostPlayback: true);
                        OnRunCancelled?.Invoke(CheatMessages.For(ability.Value));
                        return;
                    }
                    Log.LogInfo($"[RoomTracker] Debug ability '{ability.Value}' detected during {CurrentScene} - flagging run");
                }
            }

            if (shouldTick)
            {
                CurrentRoomTime += Time.unscaledDeltaTime;
            }
        }

        private static string GetCurrentSceneName()
        {
            try { return GameManager.instance?.GetSceneNameString() ?? ""; }
            catch { return ""; }
        }
    }
}
