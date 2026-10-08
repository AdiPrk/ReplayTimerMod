using System;
using System.Reflection;
using BepInEx.Logging;

#if SILKSONG_BUILD
using HarmonyLib;
#else
using Modding;
#endif

namespace ReplayTimerMod
{
#if SILKSONG_BUILD
    [HarmonyPatch]
    public static class GameHooks
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GameHooks");

        public static event Action? OnPlayerDead;
        private static bool _pendingDeath = false;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.PlayerDead))]
        [HarmonyPrefix]
        private static void GameManager_PlayerDead()
        {
            Log.LogInfo("[GameHooks] PlayerDead fired");
            _pendingDeath = true;
            OnPlayerDead?.Invoke();
        }

        public static event Action<string, string>? OnGateTransitionBegin;

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.BeginSceneTransition))]
        [HarmonyPrefix]
        private static void GameManager_BeginSceneTransition(object __0)
        {
            if (__0 == null) return;

            if (_pendingDeath)
            {
                Log.LogInfo("[GameHooks] BeginSceneTransition - death respawn, skipping");
                _pendingDeath = false;
                return;
            }

            string destScene = ReflectionStringMemberReader.TryReadStringMember(__0, "SceneName", "sceneName", "ToScene", "Scene");
            string entryGate = ReflectionStringMemberReader.TryReadStringMember(__0, "EntryGateName", "EntryGate", "entryGateName", "GateName");

            Log.LogInfo($"[GameHooks] BeginSceneTransition -> '{destScene}' via '{entryGate}' (type={__0.GetType().Name})");
            OnGateTransitionBegin?.Invoke(destScene, entryGate);
        }
    }
#else
    public static class GameHooks
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GameHooks");

        public static event Action? OnPlayerDead;
        public static event Action<string, string>? OnGateTransitionBegin;

        private static bool _pendingDeath = false;
        private static bool _initialized = false;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

#if V1221
            ModHooks.Instance.BeforePlayerDeadHook += GameManager_PlayerDead;
            ModHooks.Instance.BeforeSceneLoadHook += GameManager_BeginSceneTransition;
#else
            On.GameManager.PlayerDead += GameManager_PlayerDead;
            On.GameManager.BeginSceneTransition += GameManager_BeginSceneTransition;
#endif

            Log.LogInfo("[GameHooks] ModHooks installed");
        }

#if V1221
        private static void GameManager_PlayerDead()
        {
            Log.LogInfo("[GameHooks] PlayerDead fired");
            _pendingDeath = true;
            OnPlayerDead?.Invoke();
        }
#else
        private static System.Collections.IEnumerator GameManager_PlayerDead(
            On.GameManager.orig_PlayerDead orig,
            GameManager self,
            float waitTime)
        {
            Log.LogInfo("[GameHooks] PlayerDead fired");
            _pendingDeath = true;
            OnPlayerDead?.Invoke();
            return orig(self, waitTime);
        }
#endif

#if V1221
        private static string GameManager_BeginSceneTransition(string target)
        {
            if (_pendingDeath)
            {
                Log.LogInfo("[GameHooks] BeginSceneTransition - death respawn, skipping");
                _pendingDeath = false;
                return target;
            }

            string destScene = "";
            string entryGate = "";
            try
            {
                destScene = target;
                entryGate = GameManager.instance.entryGateName;
            }
            catch { }

            Log.LogInfo($"[GameHooks] BeginSceneTransition -> '{destScene}' via '{entryGate}' ");
            OnGateTransitionBegin?.Invoke(destScene, entryGate);

            return target;
        }
#else
        private static void GameManager_BeginSceneTransition(
            On.GameManager.orig_BeginSceneTransition orig,
            GameManager self,
            GameManager.SceneLoadInfo info)
        {
            if (info == null)
            {
                orig(self, info);
                return;
            }

            if (_pendingDeath)
            {
                Log.LogInfo("[GameHooks] BeginSceneTransition - death respawn, skipping");
                _pendingDeath = false;
                orig(self, info);
                return;
            }

            string destScene = ReflectionStringMemberReader.TryReadStringMember(info, "SceneName", "sceneName", "ToScene", "Scene");
            string entryGate = ReflectionStringMemberReader.TryReadStringMember(info, "EntryGateName", "EntryGate", "entryGateName", "GateName");

            Log.LogInfo($"[GameHooks] BeginSceneTransition -> '{destScene}' via '{entryGate}' (type={info.GetType().Name})");
            OnGateTransitionBegin?.Invoke(destScene, entryGate);

            orig(self, info);
        }
#endif
    }

#endif

    internal static class ReflectionStringMemberReader
    {
        internal static string TryReadStringMember(object instance, params string[] names)
        {
            if (instance == null) return "";

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = instance.GetType();

            foreach (string name in names)
            {
                try
                {
                    var prop = type.GetProperty(name, Flags);

                    if (prop != null && prop.PropertyType == typeof(string))
                        return prop.GetValue(instance, null) as string ?? "";

                    var field = type.GetField(name, Flags);
                    if (field != null && field.FieldType == typeof(string))
                        return field.GetValue(instance) as string ?? "";
                }
                catch { }
            }

            return "";
        }
    }
}
