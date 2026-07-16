using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Makes a mod-created GameObject survive the host game's scene changes.
    ///
    /// On Silksong / HK 1.5.78 plain <see cref="Object.DontDestroyOnLoad"/>
    /// is enough. HK 1.2.2.1 (Unity 5.4) needs more: gameplay transitions
    /// load the next room ADDITIVELY and then call
    /// SceneManager.UnloadScene(oldScene) (see GameManager.LoadSceneAdditive).
    /// DontDestroyOnLoad only shields an object from single-mode loads - an
    /// object still belonging to the unloaded gameplay scene is destroyed
    /// with it, DDOL flag or not. The fix is to move the object into the
    /// persistent scene GameManager itself lives in, which is never unloaded.
    /// </summary>
    internal static class ScenePersistence
    {
        internal static void Apply(GameObject go)
        {
            Object.DontDestroyOnLoad(go);

#if V1221
            try
            {
                var gm = GameManager.instance;
                if (gm != null)
                {
                    var persistentScene = gm.gameObject.scene;
                    if (persistentScene.IsValid() && go.scene != persistentScene)
                        UnityEngine.SceneManagement.SceneManager
                            .MoveGameObjectToScene(go, persistentScene);
                }
            }
            catch
            {
                // Scene API unavailable/hostile - the self-healing rebuild
                // paths in RoomTimerHUD/ReplayUI cover object loss.
            }
#endif
        }
    }
}
