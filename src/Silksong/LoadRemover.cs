#if SILKSONG_BUILD
using UnityEngine;
using GlobalEnums;
using System.Reflection;

namespace ReplayTimerMod
{
    // Ported directly from TimerMod's LoadRemover.
    // Determines whether the in-game clock should be ticking.
    // All the edge-case logic (teleport from menu, cutscenes, hero transition
    // state, etc.) is preserved exactly as-is. The reflection helpers below
    // (added for cross-version member-name drift) cache their MemberInfo
    // handles - ShouldTick runs every frame, so per-call GetProperty/GetField
    // lookups would allocate and scan constantly.
    public static class LoadRemover
    {
        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static GameState prevGameState = GameState.PLAYING;
        private static bool lookForTele = false;

        // Cached reflection handles (resolved once per member, then reused).
        private static bool gameStateResolved;
        private static PropertyInfo? gameStateProp;
        private static FieldInfo? gameStateField;

        private static bool sceneLoadResolved;
        private static PropertyInfo? sceneLoadProp;
        private static FieldInfo? sceneLoadField;

        private static bool activationResolved;
        private static PropertyInfo? activationProp;
        private static FieldInfo? activationField;

        public static bool ShouldTick()
        {
            var gm = GameManager.instance;
            // Early boot: the managers may not exist yet - the timer is gated.
            if (gm == null || gm.ui == null || gm.inputHandler == null)
                return false;

            UIState ui_state = gm.ui.uiState;
            string scene_name = gm.GetSceneNameString();
            string next_scene = gm.nextSceneName;

            bool loading_menu = (scene_name != KnownScenes.MenuTitle && next_scene == "")
                || (scene_name != KnownScenes.MenuTitle && next_scene == KnownScenes.MenuTitle
                    || scene_name == KnownScenes.QuitToMenu);

            GameState game_state = ReadGameState();

            if (game_state == GameState.PLAYING && prevGameState == GameState.MAIN_MENU)
                lookForTele = true;

            if (lookForTele && (game_state != GameState.PLAYING && game_state != GameState.ENTERING_LEVEL))
                lookForTele = false;

            bool accepting_input = gm.inputHandler.acceptingInput;

            HeroTransitionState hero_transition_state;
            try
            {
                hero_transition_state = gm.hero_ctrl.transitionState;
            }
            catch
            {
                hero_transition_state = HeroTransitionState.WAITING_TO_TRANSITION;
            }

            bool scene_load_activation_allowed = ReadSceneLoadActivationAllowed();

            bool r0 = lookForTele;
            bool r1 = (game_state == GameState.PLAYING || game_state == GameState.ENTERING_LEVEL)
                        && ui_state != UIState.PLAYING;
            bool r2 = game_state != GameState.PLAYING && game_state != GameState.CUTSCENE
                        && !accepting_input;
            bool r3 = (game_state == GameState.EXITING_LEVEL && scene_load_activation_allowed)
                        || game_state == GameState.LOADING;
            bool r4 = hero_transition_state == HeroTransitionState.WAITING_TO_ENTER_LEVEL;
            bool r5 = ui_state != UIState.PLAYING
                        && (loading_menu
                            || (ui_state != UIState.PAUSED && ui_state != UIState.CUTSCENE
                                && !(next_scene == "")))
                        && next_scene != scene_name;

            bool is_game_time_paused = r0 || r1 || r2 || r3 || r4 || r5;

            prevGameState = game_state;
            return !is_game_time_paused;
        }

        private static GameState ReadGameState()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm == null) return prevGameState;

                if (!gameStateResolved)
                {
                    gameStateResolved = true;
                    var t = gm.GetType();
                    foreach (string name in new[] { "GameState", "gameState" })
                    {
                        gameStateProp = t.GetProperty(name, AnyInstance);
                        if (gameStateProp != null && gameStateProp.PropertyType == typeof(GameState))
                            break;
                        gameStateProp = null;

                        gameStateField = t.GetField(name, AnyInstance);
                        if (gameStateField != null && gameStateField.FieldType == typeof(GameState))
                            break;
                        gameStateField = null;
                    }
                }

                if (gameStateProp != null)
                    return (GameState)gameStateProp.GetValue(gm, null);
                if (gameStateField != null)
                    return (GameState)gameStateField.GetValue(gm);
            }
            catch { }

            return prevGameState;
        }

        private static bool ReadSceneLoadActivationAllowed()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm == null) return false;

                if (!sceneLoadResolved)
                {
                    sceneLoadResolved = true;
                    var t = gm.GetType();
                    foreach (string name in new[] { "sceneLoad", "SceneLoad" })
                    {
                        sceneLoadProp = t.GetProperty(name, AnyInstance);
                        if (sceneLoadProp != null) break;
                        sceneLoadField = t.GetField(name, AnyInstance);
                        if (sceneLoadField != null) break;
                    }
                }

                object? sceneLoad = sceneLoadProp != null
                    ? sceneLoadProp.GetValue(gm, null)
                    : sceneLoadField?.GetValue(gm);
                if (sceneLoad == null) return false; // no load in progress

                if (!activationResolved)
                {
                    // sceneLoad instances come and go per load but their type
                    // is stable, so the member handle is resolved only once.
                    activationResolved = true;
                    var slt = sceneLoad.GetType();
                    foreach (string name in new[] { "IsActivationAllowed", "isActivationAllowed" })
                    {
                        activationProp = slt.GetProperty(name, AnyInstance);
                        if (activationProp != null && activationProp.PropertyType == typeof(bool))
                            break;
                        activationProp = null;

                        activationField = slt.GetField(name, AnyInstance);
                        if (activationField != null && activationField.FieldType == typeof(bool))
                            break;
                        activationField = null;
                    }
                }

                if (activationProp != null)
                    return (bool)activationProp.GetValue(sceneLoad, null);
                if (activationField != null)
                    return (bool)activationField.GetValue(sceneLoad);
            }
            catch { }

            return false;
        }
    }
}
#endif
