#if SILKSONG_BUILD
using UnityEngine;
using GlobalEnums;
using System.Reflection;

namespace ReplayTimerMod
{
    // Ported directly from TimerMod's LoadRemover.
    // Determines whether the in-game clock should be ticking.
    // All the edge-case logic (teleport from menu, cutscenes, hero transition
    // state, etc.) is preserved exactly as-is.
    public static class LoadRemover
    {
        private const string MENU_TITLE = "Menu_Title";
        private const string QUIT_TO_MENU = "Quit_To_Menu";

        private static GameState prevGameState = GameState.PLAYING;
        private static bool lookForTele = false;

        public static bool ShouldTick()
        {
            UIState ui_state = GameManager.instance.ui.uiState;
            string scene_name = GameManager.instance.GetSceneNameString();
            string next_scene = GameManager.instance.nextSceneName;

            bool loading_menu = (scene_name != MENU_TITLE && next_scene == "")
                || (scene_name != MENU_TITLE && next_scene == MENU_TITLE
                    || scene_name == QUIT_TO_MENU);

            GameState game_state = ReadGameState();

            if (game_state == GameState.PLAYING && prevGameState == GameState.MAIN_MENU)
                lookForTele = true;

            if (lookForTele && (game_state != GameState.PLAYING && game_state != GameState.ENTERING_LEVEL))
                lookForTele = false;

            bool accepting_input = GameManager.instance.inputHandler.acceptingInput;

            HeroTransitionState hero_transition_state;
            try
            {
                hero_transition_state = GameManager.instance.hero_ctrl.transitionState;
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
                object gm = GameManager.instance;
                if (gm == null) return prevGameState;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = gm.GetType();

                foreach (string name in new[] { "GameState", "gameState" })
                {
                    var prop = t.GetProperty(name, flags);
                    if (prop != null && prop.PropertyType == typeof(GameState))
                        return (GameState)prop.GetValue(gm, null);

                    var field = t.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(GameState))
                        return (GameState)field.GetValue(gm);
                }
            }
            catch { }

            return prevGameState;
        }

        private static bool ReadSceneLoadActivationAllowed()
        {
            try
            {
                object gm = GameManager.instance;
                if (gm == null) return false;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var t = gm.GetType();

                object? sceneLoad = null;
                foreach (string name in new[] { "sceneLoad", "SceneLoad" })
                {
                    var prop = t.GetProperty(name, flags);
                    if (prop != null)
                    {
                        sceneLoad = prop.GetValue(gm, null);
                        if (sceneLoad != null) break;
                    }

                    var field = t.GetField(name, flags);
                    if (field != null)
                    {
                        sceneLoad = field.GetValue(gm);
                        if (sceneLoad != null) break;
                    }
                }

                if (sceneLoad == null) return false;

                var slt = sceneLoad.GetType();
                foreach (string name in new[] { "IsActivationAllowed", "isActivationAllowed" })
                {
                    var prop = slt.GetProperty(name, flags);
                    if (prop != null && prop.PropertyType == typeof(bool))
                        return (bool)prop.GetValue(sceneLoad, null);

                    var field = slt.GetField(name, flags);
                    if (field != null && field.FieldType == typeof(bool))
                        return (bool)field.GetValue(sceneLoad);
                }
            }
            catch { }

            return false;
        }
    }
}
#endif
