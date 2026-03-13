using HarmonyLib;
using System;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    [HarmonyPatch]
    internal static class GameHooks
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GameHooks");

        // ── Death ─────────────────────────────────────────────────────────────
        public static event Action? OnPlayerDead;
        private static bool pendingDeath = false;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.PlayerDead))]
        [HarmonyPrefix]
        private static void GameManager_PlayerDead()
        {
            Log.LogInfo("[GameHooks] PlayerDead fired");
            pendingDeath = true;
            OnPlayerDead?.Invoke();
        }

        // ── Gate transitions ──────────────────────────────────────────────────
        // Fired for ALL BeginSceneTransition calls except death respawns.
        // This includes regular gate transitions (which use subclasses of
        // SceneLoadInfo), vanilla spawns (which use the base class), AND
        // savestate loads (which also use a subclass).
        //
        // Savestate loads are filtered out in RoomTracker.OnActiveSceneChanged
        // via the DebugMod reflection check - by the time that runs,
        // pendingGateTransition gets cleared before it can start a recording.
        //
        // Vanilla spawns (from.name == Menu_Title) are filtered in RoomTracker.
        public static event Action<string, string>? OnGateTransitionBegin;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.BeginSceneTransition))]
        [HarmonyPrefix]
        private static void GameManager_BeginSceneTransition(object __0)
        {
            if (__0 == null) return;

            if (pendingDeath)
            {
                Log.LogInfo("[GameHooks] BeginSceneTransition - death respawn, skipping");
                pendingDeath = false;
                return;
            }

            // Extract SceneName and EntryGateName via reflection so we don't
            // need to know the exact subclass type at compile time.
            string destScene = "";
            string entryGate = "";
            try
            {
                var type = __0.GetType();
                destScene = type.GetProperty("SceneName")?.GetValue(__0) as string ?? "";
                entryGate = type.GetProperty("EntryGateName")?.GetValue(__0) as string ?? "";
            }
            catch { }

            Log.LogInfo($"[GameHooks] BeginSceneTransition → '{destScene}' via '{entryGate}' (type={__0.GetType().Name})");
            OnGateTransitionBegin?.Invoke(destScene, entryGate);
        }
    }
}