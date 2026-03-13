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
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("RoomTracker");

        private const string MENU_TITLE = "Menu_Title";
        private const string QUIT_TO_MENU = "Quit_To_Menu";

        public static event Action<string>? OnRoomEnter;
        public static event Action<string, float, bool>? OnRoomExit;

        public static string CurrentScene { get; private set; } = "";
        public static bool InGameplayRoom { get; private set; } = false;
        public static float CurrentRoomTime { get; private set; } = 0f;

        private static int sceneCount = 0;
        private static bool isReady = false;
        private static bool pendingEntry = false;

        // Reflection handle for DebugMod's SaveState.loadingSavestate.
        // Resolved once on first use — null if DebugMod is not installed.
        // We use this as a fallback to catch savestate loads in case the
        // Harmony patch on BeginSceneTransition doesn't fire (e.g. the
        // savestate routes through a different code path we didn't patch).
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
                            // SaveState is in DebugMod.SaveStates namespace but
                            // its class attribute makes it public at the top level.
                            var t = asm.GetType("SaveState")
                                 ?? asm.GetType("DebugMod.SaveStates.SaveState");
                            if (t != null)
                            {
                                _savestateLoadingProp = t.GetProperty(
                                    "loadingSavestate",
                                    BindingFlags.Public | BindingFlags.Static);
                            }
                            break;
                        }
                    }
                }

                return _savestateLoadingProp?.GetValue(null) != null;
            }
            catch
            {
                return false;
            }
        }

        public static void Init()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            GameHooks.OnPlayerDead += HandleInvalidation;
        }

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            sceneCount++;
            if (sceneCount >= 4)
                isReady = true;

            if (!isReady) return;

            // Fallback check: if the Harmony patch didn't fire OnSavestateLoad
            // (e.g. the event wasn't raised for some reason), check DebugMod's
            // loadingSavestate directly via reflection. If a savestate is mid-load,
            // treat it as an invalidation — not a natural exit worth timing.
            if (IsDebugModSavestateLoading())
            {
                Log.LogInfo("[RoomTracker] Savestate load detected via reflection fallback — invalidating");
                HandleInvalidation();
            }

            if (InGameplayRoom)
            {
                // InGameplayRoom is still true → this is a natural room exit
                // (door/gate). Death and savestate both call HandleInvalidation
                // first, clearing InGameplayRoom before we reach this point.
                string exitedScene = CurrentScene;
                float exitTime = CurrentRoomTime;
                InGameplayRoom = false;
                CurrentRoomTime = 0f;

                bool isQuit = to.name == MENU_TITLE || to.name == QUIT_TO_MENU;
                OnRoomExit?.Invoke(exitedScene, exitTime, !isQuit);
            }
            else
            {
                CurrentRoomTime = 0f;
            }

            pendingEntry = false;
            CurrentScene = to.name;

            if (to.name == MENU_TITLE || to.name == QUIT_TO_MENU)
                return;

            pendingEntry = true;
        }

        private static void HandleInvalidation()
        {
            if (!InGameplayRoom && !pendingEntry) return;

            InGameplayRoom = false;
            pendingEntry = false;
            CurrentRoomTime = 0f;
        }

        public static void Tick()
        {
            if (!isReady) return;

            if (pendingEntry)
            {
                try
                {
                    GameState state = GameManager.instance.GameState;

                    if (state == GameState.PLAYING && GameManager.instance.IsGameplayScene())
                    {
                        pendingEntry = false;
                        InGameplayRoom = true;
                        CurrentRoomTime = 0f;
                        OnRoomEnter?.Invoke(CurrentScene);
                    }
                    else if (state == GameState.MAIN_MENU)
                    {
                        pendingEntry = false;
                    }
                }
                catch { }
                return;
            }

            if (!InGameplayRoom) return;

            try
            {
                if (LoadRemover.ShouldTick())
                    CurrentRoomTime += Time.unscaledDeltaTime;
            }
            catch { }
        }
    }
}