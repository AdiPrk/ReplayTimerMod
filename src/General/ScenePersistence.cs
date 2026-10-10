using UnityEngine;

namespace ReplayTimerMod
{
    // HK 1.2.2.1 unloads rooms additively, so DontDestroyOnLoad alone isn't enough there.
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
            }
#endif
        }
    }
}
