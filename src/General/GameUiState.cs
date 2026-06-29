using GlobalEnums;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Small shared queries about the host game's UI state, so multiple
    /// subsystems don't each reimplement the same guarded lookups.
    /// (Named to avoid colliding with the game's own <c>GameState</c> enum.)
    /// </summary>
    internal static class GameUiState
    {
        /// <summary>True when the in-game pause menu is open.</summary>
        public static bool IsPaused()
        {
            try
            {
                return GameManager.instance != null
                    && GameManager.instance.ui != null
                    && GameManager.instance.ui.uiState == UIState.PAUSED;
            }
            catch { return false; } // UI not yet constructed during early boot
        }
    }
}
