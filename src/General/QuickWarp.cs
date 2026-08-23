using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Cross-platform warp utility for Hollow Knight and Silksong.
    ///
    /// Resolves warp targets from a static, bundled transition map — the same
    /// authoritative door data that Benchwarp / RandomizerMod use. The map is
    /// embedded as a resource and loaded once at startup, so warps work
    /// immediately for any route (including shared/leaderboard routes the
    /// player has never visited). No runtime scanning, no learned cache, no
    /// room visits required.
    ///
    /// The map covers every known (sourceScene, destScene) transition. When a
    /// room has two doors leading to the same neighbour, the map stores one of
    /// them (deterministically, the lowest-sorted gate name). Every stored gate
    /// provably leads to its keyed destination, so a warp always lands the
    /// player in the correct previous room before a real transition into the
    /// run room — even if it isn't the exact door the recorded run used.
    ///
    /// A warp goes to EntryFromScene (the previous room) at the door leading
    /// into SceneName (the run room), placing the player right before the
    /// transition that starts the run.
    /// </summary>
    public static class QuickWarp
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("QuickWarp");

        // Map: "SourceScene|DestScene" -> gate name IN SourceScene that leads
        // to DestScene. Loaded from the embedded transition resource.
        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>();

        private static bool _loaded;

        /// <summary>
        /// True while a warp transition is in progress. RoomTracker checks
        /// this to suppress recording for warp-triggered scene changes.
        /// </summary>
        public static bool IsWarping { get; private set; }

        // ── Init ────────────────────────────────────────────────────────────

        /// <summary>
        /// Load the embedded transition map. Call once from the mod entry
        /// point. The resource loaded depends on the build (SS vs HK).
        /// </summary>
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

        // ── Gate resolution ─────────────────────────────────────────────────

        /// <summary>
        /// Get the gate in sourceScene that leads to destScene, or null if the
        /// transition is unknown. When multiple doors connect the two scenes,
        /// returns a deterministically-chosen one that provably reaches
        /// destScene.
        /// </summary>
        public static string? ResolveExitGate(string sourceScene, string destScene)
        {
            if (string.IsNullOrEmpty(sourceScene) || string.IsNullOrEmpty(destScene))
                return null;

            string? gate;
            if (_map.TryGetValue(sourceScene + "|" + destScene, out gate))
                return gate;
            return null;
        }

        /// <summary>
        /// Resolve the exit gate for a route's entry: the gate in
        /// EntryFromScene that leads into SceneName.
        /// </summary>
        public static string? ResolveExitGate(RoomKey routeKey)
        {
            return ResolveExitGate(routeKey.EntryFromScene, routeKey.SceneName);
        }

        /// <summary>
        /// Whether this route has a warp target — true when the
        /// (EntryFromScene -> SceneName) transition exists in the map. Spawn
        /// routes (no previous room) return false.
        /// </summary>
        public static bool CanWarp(RoomKey routeKey)
        {
            if (string.IsNullOrEmpty(routeKey.EntryFromScene))
                return false; // spawn routes have no previous room
            return ResolveExitGate(routeKey) != null;
        }

        // ── Warp ────────────────────────────────────────────────────────────

        /// <summary>
        /// Warp to the entry transition for a route: goes to EntryFromScene
        /// at a door that leads into SceneName. Returns false if the route has
        /// no known target (spawn route, or a transition absent from the map).
        /// </summary>
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

        /// <summary>
        /// Warp to <paramref name="sceneName"/> at <paramref name="gateName"/>.
        /// Sets <see cref="IsWarping"/> to suppress run recording until the
        /// destination scene actually activates (cleared by RoomTracker via
        /// <see cref="NotifyArrival"/>, with a watchdog fallback).
        /// </summary>
        public static void WarpToTransition(string sceneName, string gateName)
        {
            if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(gateName))
            {
                Log.LogWarning("[QuickWarp] WarpToTransition: empty scene/gate");
                return;
            }

            // Ignore re-entrant warp requests while one is already running,
            // so spam-clicking can't fire multiple overlapping transitions.
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

        /// <summary>
        /// Called by RoomTracker when it consumes the warp arrival (the
        /// destination scene has activated). Ends the recording-suppression
        /// window. Safe to call when not warping.
        /// </summary>
        public static void NotifyArrival()
        {
            if (IsWarping)
            {
                IsWarping = false;
                Log.LogDebug("[QuickWarp] Warp arrival consumed");
            }
        }

        // ── Coroutine ───────────────────────────────────────────────────────

        private static IEnumerator WarpCoroutine(GameManager gm,
            string sceneName, string gateName)
        {
            // The UI is only interactable while paused, so unpause first.
            if (IsGamePaused(gm))
            {
#if SILKSONG_BUILD
                // Fully drain the unpause coroutine before warping (matches
                // QuickWarp/Benchwarp). Getting the iterator is wrapped in
                // try/catch; the draining yield is outside it (C# forbids
                // yield inside try/catch).
                IEnumerator? unpause = GetUnpauseIterator(gm);
                if (unpause != null)
                {
                    while (unpause.MoveNext())
                        yield return unpause.Current;
                }
#else
                // HK: TogglePauseGame is synchronous.
                TryUnpauseHK(gm);
                yield return null;
#endif
            }

            yield return null; // safety frame before transition

            DoWarp(gm, sceneName, gateName);

            // IsWarping stays true until RoomTracker consumes the arrival
            // (NotifyArrival) once the destination scene activates — which can
            // be many frames later, since BeginSceneTransition is async.
            // This watchdog only clears the flag if that arrival never comes
            // (e.g. a failed/aborted transition), so the flag can't get stuck.
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

#if SILKSONG_BUILD
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
#else
        private static void TryUnpauseHK(GameManager gm)
        {
            try
            {
                object uiMgr = typeof(GameManager)
                    .GetProperty("ui",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public)
                    .GetValue(gm, null);

                if (uiMgr != null)
                {
                    System.Reflection.MethodInfo toggle =
                        uiMgr.GetType().GetMethod("TogglePauseGame");
                    if (toggle != null)
                        toggle.Invoke(uiMgr, null);
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[QuickWarp] Unpause failed: {ex.Message}");
            }
        }
#endif

        private static void DoWarp(GameManager gm, string sceneName, string gateName)
        {
#if SILKSONG_BUILD
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
#elif V1221
            try { gm.entryGateName = gateName; } catch { }
            try
            {
                Type sliType = typeof(GameManager).GetNestedType("SceneLoadInfo");
                if (sliType != null)
                {
                    object info = Activator.CreateInstance(sliType);
                    SetMember(info, "SceneName", sceneName);
                    SetMember(info, "EntryGateName", gateName);
                    SetMember(info, "PreventCameraFadeOut", true);
                    SetMember(info, "WaitForSceneTransitionCameraFade", false);
                    SetMember(info, "AlwaysUnloadUnusedAssets", true);
                    System.Reflection.MethodInfo m = typeof(GameManager)
                        .GetMethod("BeginSceneTransition", new Type[] { sliType });
                    if (m != null) { m.Invoke(gm, new object[] { info }); return; }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[QuickWarp] v1221 SceneLoadInfo path failed: {ex.Message}");
            }
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
#else
            gm.BeginSceneTransition(new GameManager.SceneLoadInfo
            {
                SceneName = sceneName,
                EntryGateName = gateName,
                PreventCameraFadeOut = true,
                WaitForSceneTransitionCameraFade = false,
                Visualization = GameManager.SceneLoadVisualizations.Default,
                AlwaysUnloadUnusedAssets = true,
                IsFirstLevelForPlayer = false
            });
#endif
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static bool IsGamePaused(GameManager gm)
        {
            try { return gm.IsGamePaused(); }
            catch
            {
                try { return gm.ui != null && gm.ui.uiState == GlobalEnums.UIState.PAUSED; }
                catch { return false; }
            }
        }

#if V1221
        private static void SetMember(object obj, string name, object value)
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            Type type = obj.GetType();
            System.Reflection.PropertyInfo p = type.GetProperty(name, F);
            if (p != null) { p.SetValue(obj, value, null); return; }
            System.Reflection.FieldInfo f = type.GetField(name, F);
            if (f != null) f.SetValue(obj, value);
        }
#endif

        // ── Flat JSON parser ────────────────────────────────────────────────
        // Parses {"key":"value","key2":"value2",...} where keys and values
        // are plain strings (no nesting, no escapes beyond \" and \\).
        // Avoids a hard Newtonsoft dependency that differs across builds.

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
            return null; // unterminated
        }
    }
}