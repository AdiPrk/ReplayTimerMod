using HarmonyLib;
using System;

namespace ReplayTimerMod
{
    [HarmonyPatch]
    internal static class GameHooks
    {
        public static event Action? OnPlayerDead;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.PlayerDead))]
        [HarmonyPrefix]
        private static void GameManager_PlayerDead()
        {
            OnPlayerDead?.Invoke();
        }
    }
}