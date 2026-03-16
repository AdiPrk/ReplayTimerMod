using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using GlobalEnums;

namespace ReplayTimerMod
{
    public static class RoomTracker
    {
        public const float MAX_ROOM_TIME = 180f;

        private const string MENU_TITLE = "Menu_Title";
        private const string QUIT_TO_MENU = "Quit_To_Menu";

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTracker");

        // ── Public state ─────────────────────────────────────────────────────
        public static bool IsRecording { get; private set; } = false;
        public static string CurrentScene { get; private set; } = "";
        public static string EntryFromScene { get; private set; } = "";
        public static float CurrentRoomTime { get; private set; } = 0f;

        // ── Events ───────────────────────────────────────────────────────────
        // sceneName, entryFromScene
        public static event Action<string, string>? OnRoomEnter;

        // sceneName, entryFromScene, exitToScene, lrTime
        public static event Action<string, string, string, float>? OnRoomExit;

        public static event Action? OnRecordingDiscarded;

        // ── Private state ────────────────────────────────────────────────────
        private static string lastSceneName = "";

        // Set by OnGateTransitionBegin; consumed and cleared in OnActiveSceneChanged.
        private static bool pendingGateTransition = false;

        // ── Savestate reflection ──────────────────────────────────────────────
        private static PropertyInfo? _savestateLoadingProp;
        private static bool _savestateReflectionResolved = false;

        private static bool IsDebugModSavestateLoading()
        {
            try
            {
                if (!_savestateReflectionResolved)
                {
                    _savestateReflectionResolved = true;
                    Log.LogInfo("attempt");
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Log.LogInfo("looking thoguht" + asm.GetName().Name);
                        if (asm.GetName().Name == "DebugMod")
                        {
                            var t = asm.GetType("SaveState")
                                 ?? asm.GetType("DebugMod.SaveStates.SaveState");
                            if (t != null)
                                _savestateLoadingProp = t.GetProperty(
                                    "loadingSavestate",
                                    BindingFlags.Public | BindingFlags.Static);
                            break;
                        }
                    }
                }
                return _savestateLoadingProp?.GetValue(null, null) != null;
            }
            catch { return false; }
        }

        // ── Init ──────────────────────────────────────────────────────────────

        public static void Init()
        {
            lastSceneName = "";
            GameHooks.OnPlayerDead += HandleInvalidation;
            GameHooks.OnGateTransitionBegin += HandleGateTransitionBegin;
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        private static void HandleGateTransitionBegin(string destScene, string entryGate)
        {
            pendingGateTransition = true;
            Log.LogDebug($"[Gate] pending -> {destScene} via '{entryGate}'");
        }

        private static void HandleInvalidation()
        {
            if (IsRecording)
            {
                Log.LogInfo($"[RoomTracker] Invalidated in {CurrentScene} - discarding");
                IsRecording = false;
                OnRecordingDiscarded?.Invoke();
            }
            CurrentRoomTime = 0f;
            pendingGateTransition = false;
        }

        private static void OnActiveSceneChanged(string fromName, string toName)
        {
            if (IsDebugModSavestateLoading())
            {
                Log.LogInfo("[RoomTracker] Savestate detected - invalidating");
                HandleInvalidation();
            }

            bool arrivedViaGate = pendingGateTransition;
            pendingGateTransition = false;

            if (fromName == MENU_TITLE || fromName == QUIT_TO_MENU)
                arrivedViaGate = false;

            bool toMenu = toName == MENU_TITLE || toName == QUIT_TO_MENU;

            // ── Close out current recording ───────────────────────────────────
            if (IsRecording)
            {
                if (arrivedViaGate && !isOverTime() && !toMenu)
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
                    if (isOverTime())
                        Log.LogInfo($"[RoomTracker] Over time limit in {CurrentScene} - discarding");
                    else if (!arrivedViaGate)
                        Log.LogInfo($"[RoomTracker] Non-gate exit from {CurrentScene} - discarding");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    OnRecordingDiscarded?.Invoke();
                }
            }

            // ── Start new recording ───────────────────────────────────────────
            if (arrivedViaGate && !toMenu)
            {
                CurrentScene = toName;
                EntryFromScene = fromName;
                CurrentRoomTime = 0f;
                IsRecording = true;

                Log.LogInfo($"[RoomTracker] Enter: {CurrentScene} from {EntryFromScene}");
                OnRoomEnter?.Invoke(CurrentScene, EntryFromScene);
            }
            else
            {
                CurrentScene = toName;
                EntryFromScene = "";
                CurrentRoomTime = 0f;
                IsRecording = false;

                Log.LogInfo($"[RoomTracker] IDLE in {toName}");
            }

            bool isOverTime() => CurrentRoomTime > MAX_ROOM_TIME;
        }

        // ── Tick ──────────────────────────────────────────────────────────────
        public static void Tick(bool shouldTick)
        {
            string currentSceneName = GetCurrentSceneName();
            if (!string.IsNullOrEmpty(currentSceneName) && currentSceneName != lastSceneName)
            {
                string fromName = lastSceneName;
                lastSceneName = currentSceneName;
                OnActiveSceneChanged(fromName, currentSceneName);
            }

            if (!IsRecording) return;
            if (shouldTick)
                CurrentRoomTime += Time.unscaledDeltaTime;
        }

        private static string GetCurrentSceneName()
        {
            try
            {
                return GameManager.instance?.GetSceneNameString() ?? "";
            }
            catch
            {
                return "";
            }
        }

    }
}
