using GlobalEnums;
using UnityEngine;

namespace ReplayTimerMod
{
    internal static class GameUiState
    {
        public static bool IsPaused()
        {
            try
            {
                return GameManager.instance != null
                    && GameManager.instance.ui != null
                    && GameManager.instance.ui.uiState == UIState.PAUSED;
            }
            catch { return false; }
        }
    }
}
