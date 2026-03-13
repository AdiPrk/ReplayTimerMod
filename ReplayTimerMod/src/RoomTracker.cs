using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalEnums;

namespace ReplayTimerMod
{
    public static class RoomTracker
    {
        public const float MAX_ROOM_TIME = 60f;

        private const string MENU_TITLE = "Menu_Title";
        private const string QUIT_TO_MENU = "Quit_To_Menu";

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTracker");

        // ── Public state ─────────────────────────────────────────────────────
        public static bool IsRecording { get; private set; } = false;
        public static string CurrentScene { get; private set; } = "";
        public static string EntryGateName { get; private set; } = "";
        public static string EntryFromScene { get; private set; } = "";
        public static float CurrentRoomTime { get; private set; } = 0f;

        // ── Events ───────────────────────────────────────────────────────────
        // sceneName, entryGate, entryFromScene
        public static event Action<string, string, string>? OnRoomEnter;

        // sceneName, entryGate, exitToScene, lrTime
        public static event Action<string, string, string, float>? OnRoomExit;

        public static event Action? OnRecordingDiscarded;

        // ── Private state ─────────────────────────────────────────────────────
        private static int sceneCount = 0;
        private static bool isReady = false;

        // Set by OnGateTransitionBegin when a real gate fires.
        // Consumed immediately in OnActiveSceneChanged.
        // We use a boolean rather than matching scene names — the name in
        // SceneLoadInfo is not guaranteed to match to.name in all cases.
        private static bool pendingGateTransition = false;
        private static string pendingGateEntryGate = "";

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
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
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
                return _savestateLoadingProp?.GetValue(null) != null;
            }
            catch { return false; }
        }

        // ── Init ─────────────────────────────────────────────────────────────
        public static void Init()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            GameHooks.OnPlayerDead += HandleInvalidation;
            GameHooks.OnGateTransitionBegin += HandleGateTransitionBegin;
        }

        // ── Handlers ─────────────────────────────────────────────────────────
        private static void HandleGateTransitionBegin(string destScene, string entryGate)
        {
            pendingGateTransition = true;
            pendingGateEntryGate = entryGate;
            Log.LogDebug($"[Gate] pending → {destScene} via '{entryGate}'");
        }

        private static void HandleInvalidation()
        {
            if (IsRecording)
            {
                Log.LogInfo($"[RoomTracker] Invalidated in {CurrentScene} — discarding");
                IsRecording = false;
                OnRecordingDiscarded?.Invoke();
            }
            CurrentRoomTime = 0f;
            pendingGateTransition = false;
            pendingGateEntryGate = "";
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            sceneCount++;
            if (sceneCount >= 4) isReady = true;
            if (!isReady) return;

            // Savestate fallback — if DebugMod is mid-load, treat as invalidation
            if (IsDebugModSavestateLoading())
            {
                Log.LogInfo("[RoomTracker] Savestate detected via reflection — invalidating");
                HandleInvalidation();
                // pendingGateTransition already cleared by HandleInvalidation
            }

            // Consume the pending gate flag — regardless of what we decide below,
            // it must be cleared before we return.
            bool arrivedViaGate = pendingGateTransition;
            string arrivedViaGateEntryGate = pendingGateEntryGate;
            pendingGateTransition = false;
            pendingGateEntryGate = "";

            // Transitions from Menu_Title are always spawns, never real gates,
            // even if BeginSceneTransition was called with a vanilla SceneLoadInfo.
            if (from.name == MENU_TITLE || from.name == QUIT_TO_MENU)
                arrivedViaGate = false;

            // Menu destinations are never gameplay rooms.
            bool toMenu = to.name == MENU_TITLE || to.name == QUIT_TO_MENU;

            // ── Close out current recording ───────────────────────────────────
            if (IsRecording)
            {
                bool isTurnaround = arrivedViaGate && (to.name == EntryFromScene);
                bool isOverTime = CurrentRoomTime > MAX_ROOM_TIME;

                if (arrivedViaGate && !isTurnaround && !isOverTime && !toMenu)
                {
                    // Valid natural exit.
                    string exitedScene = CurrentScene;
                    string exitedEntry = EntryGateName;
                    string exitedTo = to.name;
                    float exitedTime = CurrentRoomTime;

                    Log.LogInfo($"[RoomTracker] Exit: {exitedScene} [{exitedEntry}→{exitedTo}] {FormatTime(exitedTime)}");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    OnRoomExit?.Invoke(exitedScene, exitedEntry, exitedTo, exitedTime);
                }
                else
                {
                    if (isTurnaround)
                        Log.LogInfo($"[RoomTracker] Turnaround in {CurrentScene} — discarding");
                    else if (isOverTime)
                        Log.LogInfo($"[RoomTracker] Over time limit in {CurrentScene} — discarding");
                    else if (!arrivedViaGate)
                        Log.LogInfo($"[RoomTracker] Non-gate exit from {CurrentScene} — discarding");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    OnRecordingDiscarded?.Invoke();
                }
            }

            // ── Start new recording ───────────────────────────────────────────
            if (arrivedViaGate && !toMenu)
            {
                CurrentScene = to.name;
                EntryGateName = arrivedViaGateEntryGate;
                EntryFromScene = from.name;
                CurrentRoomTime = 0f;
                IsRecording = true;

                Log.LogInfo($"[RoomTracker] Enter: {CurrentScene} via '{EntryGateName}' from {EntryFromScene}");
                OnRoomEnter?.Invoke(CurrentScene, EntryGateName, EntryFromScene);
            }
            else
            {
                // Spawn, death respawn, savestate, menu — go/stay IDLE.
                CurrentScene = to.name;
                EntryGateName = "";
                EntryFromScene = "";
                CurrentRoomTime = 0f;
                IsRecording = false;

                Log.LogInfo($"[RoomTracker] IDLE in {to.name} (not a gate transition)");
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────────
        public static void Tick()
        {
            if (!isReady || !IsRecording) return;
            try
            {
                if (LoadRemover.ShouldTick())
                    CurrentRoomTime += Time.unscaledDeltaTime;
            }
            catch { }
        }

        private static string FormatTime(float t)
        {
            int millis = (int)(t * 100) % 100;
            int seconds = (int)t % 60;
            int minutes = (int)t / 60;
            return $"{minutes}:{seconds:00}.{millis:00}";
        }
    }
}