#if HOLLOW_KNIGHT_BUILD
using UnityEngine;
using GlobalEnums;

namespace ReplayTimerMod
{
    // Ported from TimerMod's LoadRemover.
    public static class LoadRemover
    {
        private static GameState prevGameState = GameState.PLAYING;
        private static bool lookForTele = false;

        public static bool ShouldTick()
        {
            var gm = GameManager.instance;
            if (gm == null || gm.ui == null || gm.inputHandler == null)
                return false;

            UIState ui_state = gm.ui.uiState;
            string scene_name = gm.GetSceneNameString();
            string next_scene = gm.nextSceneName;

            bool loading_menu = (scene_name != KnownScenes.MenuTitle && next_scene == "")
                || (scene_name != KnownScenes.MenuTitle && next_scene == KnownScenes.MenuTitle
                    || scene_name == KnownScenes.QuitToMenu);

            GameState game_state = gm.gameState;

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

            bool r0 = lookForTele;
            bool r1 = (game_state == GameState.PLAYING || game_state == GameState.ENTERING_LEVEL)
                        && ui_state != UIState.PLAYING;
            bool r2 = game_state != GameState.PLAYING && !accepting_input;
            bool r3 = game_state == GameState.EXITING_LEVEL || game_state == GameState.LOADING;
            bool r4 = hero_transition_state == HeroTransitionState.WAITING_TO_ENTER_LEVEL;
            bool r5 = ui_state != UIState.PLAYING
                        && (loading_menu
                            || (ui_state != UIState.PAUSED
                                && !(next_scene == "")))
                        && next_scene != scene_name;

            bool is_game_time_paused = r0 || r1 || r2 || r3 || r4 || r5;

            prevGameState = game_state;
            return !is_game_time_paused;
        }
    }
}
#endif
