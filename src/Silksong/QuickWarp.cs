#if SILKSONG_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    public static class QuickWarp
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("QuickWarp");

        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>();

        private static bool _loaded;

        public static bool IsWarping { get; private set; }

        public static void Init()
        {
            if (_loaded) return;
            _loaded = true;

            const string resourceSuffix = "transitions.json";
            try
            {
                System.Reflection.Assembly asm =
                    System.Reflection.Assembly.GetExecutingAssembly();

                string? resourceName = null;
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName == null)
                {
                    Log.LogError("[QuickWarp] Transition resource not found in assembly");
                    return;
                }

                string json;
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }

                ParseFlatStringMap(json, _map);
                Log.LogInfo($"[QuickWarp] Loaded {_map.Count} transitions from {resourceName}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[QuickWarp] Failed to load transition map: {ex.Message}");
            }
        }

        public static string? ResolveExitGate(string sourceScene, string destScene)
        {
            if (string.IsNullOrEmpty(sourceScene) || string.IsNullOrEmpty(destScene))
                return null;

            string? gate;
            if (_map.TryGetValue(sourceScene + "|" + destScene, out gate))
                return gate;
            return null;
        }

        public static string? ResolveExitGate(RoomKey routeKey)
        {
            return ResolveExitGate(routeKey.EntryFromScene, routeKey.SceneName);
        }

        public static bool CanWarp(RoomKey routeKey)
        {
            if (string.IsNullOrEmpty(routeKey.EntryFromScene))
                return false;
            return ResolveExitGate(routeKey) != null;
        }

        public static bool WarpToRoute(RoomKey routeKey)
        {
            string? gate = ResolveExitGate(routeKey);
            if (gate == null)
            {
                Log.LogWarning($"[QuickWarp] No known gate for {routeKey}");
                return false;
            }
            WarpToTransition(routeKey.EntryFromScene, gate);
            return true;
        }

        public static void WarpToTransition(string sceneName, string gateName)
        {
            if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(gateName))
            {
                Log.LogWarning("[QuickWarp] WarpToTransition: empty scene/gate");
                return;
            }

            if (IsWarping)
            {
                Log.LogInfo("[QuickWarp] Warp already in progress - ignoring");
                return;
            }

            Log.LogInfo($"[QuickWarp] Warping to {sceneName} via '{gateName}'");

            try
            {
                GameManager gm = GameManager.instance;
                if (gm == null)
                {
                    Log.LogWarning("[QuickWarp] GameManager not available");
                    return;
                }

                IsWarping = true;
                gm.StartCoroutine(WarpCoroutine(gm, sceneName, gateName));
            }
            catch (Exception ex)
            {
                IsWarping = false;
                Log.LogError($"[QuickWarp] Warp failed: {ex.Message}");
            }
        }

        public static void NotifyArrival()
        {
            if (IsWarping)
            {
                IsWarping = false;
                Log.LogDebug("[QuickWarp] Warp arrival consumed");
            }
        }

        private static IEnumerator WarpCoroutine(GameManager gm,
            string sceneName, string gateName)
        {
            if (IsGamePaused(gm))
            {
                IEnumerator? unpause = GetUnpauseIterator(gm);
                if (unpause != null)
                {
                    while (unpause.MoveNext())
                        yield return unpause.Current;
                }
            }

            yield return null;

            DoWarp(gm, sceneName, gateName);

            float deadline = Time.unscaledTime + WarpWatchdogSeconds;
            while (IsWarping && Time.unscaledTime < deadline)
                yield return null;

            if (IsWarping)
            {
                Log.LogWarning("[QuickWarp] Warp watchdog timeout - clearing flag");
                IsWarping = false;
            }
        }

        private const float WarpWatchdogSeconds = 15f;

        private static IEnumerator? GetUnpauseIterator(GameManager gm)
        {
            try
            {
                return gm.PauseGameToggleByMenu();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[QuickWarp] Unpause failed: {ex.Message}");
                return null;
            }
        }

        private static void DoWarp(GameManager gm, string sceneName, string gateName)
        {
            GameManager.SceneLoadInfo info = new GameManager.SceneLoadInfo
            {
                SceneName = sceneName,
                EntryGateName = gateName,
                PreventCameraFadeOut = true,
                WaitForSceneTransitionCameraFade = false,
                Visualization = GameManager.SceneLoadVisualizations.Default,
                AlwaysUnloadUnusedAssets = true,
                IsFirstLevelForPlayer = false
            };
            GameManager.UnsafeInstance.BeginSceneTransition(info);
        }

        private static bool IsGamePaused(GameManager gm)
        {
            try { return gm.IsGamePaused(); }
            catch
            {
                try { return gm.ui != null && gm.ui.uiState == GlobalEnums.UIState.PAUSED; }
                catch { return false; }
            }
        }

        private static void ParseFlatStringMap(string json,
            Dictionary<string, string> into)
        {
            if (string.IsNullOrEmpty(json)) return;

            int i = 0;
            int n = json.Length;

            SkipWs(json, ref i);
            if (i >= n || json[i] != '{') return;
            i++;

            while (i < n)
            {
                SkipWs(json, ref i);
                if (i < n && json[i] == '}') { i++; break; }

                string? key = ParseString(json, ref i);
                if (key == null) return;

                SkipWs(json, ref i);
                if (i >= n || json[i] != ':') return;
                i++;

                SkipWs(json, ref i);
                string? val = ParseString(json, ref i);
                if (val == null) return;

                into[key] = val;

                SkipWs(json, ref i);
                if (i < n && json[i] == ',') { i++; continue; }
                if (i < n && json[i] == '}') { i++; break; }
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        private static string? ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    if (e == '"') sb.Append('"');
                    else if (e == '\\') sb.Append('\\');
                    else if (e == '/') sb.Append('/');
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else sb.Append(e);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return null;
        }
    }
}
#endif
