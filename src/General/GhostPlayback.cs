using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    // Plays back previously recorded runs as world-space ghosts.
    //
    // Lifecycle:
    //   RoomTracker.OnRoomEnter  → StartPlayback(scene, entryFromScene)
    //   LateUpdate               → Tick() advances playback in LR time
    //   RoomTracker.OnRoomExit / OnRecordingDiscarded → StopPlayback()
    //
    // Sprite rendering:
    //   We create sprite GOs inactive so tk2dSprite.Awake() fires after Collection
    //   is assigned - this prevents the pink-rectangle bug.
    //   Clip name → spriteId is resolved via a Dictionary built once at init so
    //   the hot tick path is a single hash lookup, not GetClipByName().
    public class GhostPlayback
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("GhostPlayback");

        private readonly List<PlaybackInstance> activeInstances =
            new List<PlaybackInstance>();

        private Dictionary<string, tk2dSpriteAnimationClip>? clipCache;
        private tk2dSpriteCollectionData? spriteCollection;
        private int defaultSpriteId = -1;
        private bool spriteInitDone = false;
        private ReplaySelectionState? selectionState;
        private bool playing = false;

        private sealed class PlaybackInstance
        {
            public ReplaySnapshot Snapshot { get; }
            public float PlaybackTime { get; set; }
            public GameObject? SpriteGo { get; set; }
            public tk2dSprite? Sprite { get; set; }
            public GameObject? DiamondGo { get; set; }
            public LineRenderer? DiamondLine { get; set; }
            public Material? DiamondMat { get; set; }

            public RecordedRoom Room => Snapshot.Room;

            public PlaybackInstance(ReplaySnapshot snapshot)
            {
                Snapshot = snapshot;
            }
        }

        public void Setup()
        {
            Log.LogInfo("[Ghost] Setup complete");
        }

        public void SetSelectionState(ReplaySelectionState? state)
        {
            selectionState = state;
        }

        public void StartPlayback(string sceneName, string entryFromScene)
        {
            StopPlayback();

            if (!GhostSettings.GhostEnabled)
                return;

            var snapshots = SelectSnapshots(sceneName, entryFromScene);
            if (snapshots.Count == 0)
            {
                Log.LogInfo($"[Ghost] No playback candidates for {sceneName} <- {entryFromScene}");
                return;
            }

            EnsureSpriteResources();

            foreach (var snapshot in snapshots)
            {
                var instance = new PlaybackInstance(snapshot);
                CreateVisuals(instance);
                activeInstances.Add(instance);
            }

            playing = activeInstances.Count > 0;
            if (playing)
            {
                Log.LogInfo($"[Ghost] Playing {activeInstances.Count} snapshot(s) for {sceneName} <- {entryFromScene}");
            }
        }

        public void StopPlayback()
        {
            playing = false;

            foreach (var instance in activeInstances)
                DestroyVisuals(instance);

            activeInstances.Clear();
        }

        // Accepts the pre-computed shouldTick value from the plugin so that
        // LoadRemover.ShouldTick() is only called once per LateUpdate across
        // all subsystems. Calling it multiple times per frame causes incorrect
        // state transitions because it writes prevGameState on every call.
        public void Tick(bool shouldTick)
        {
            if (!playing || activeInstances.Count == 0)
                return;

            if (!GhostSettings.GhostEnabled)
            {
                StopPlayback();
                return;
            }

            if (!shouldTick)
                return;

            float interval = FrameRecorder.RECORD_INTERVAL;
            float deltaTime = Time.deltaTime;
            float z = HeroController.instance != null
                ? HeroController.instance.transform.position.z
                : 0f;
            Color globalColor = GhostSettings.GhostColor;

            for (int i = activeInstances.Count - 1; i >= 0; i--)
            {
                var instance = activeInstances[i];
                if (!TickInstance(instance, deltaTime, interval, z, globalColor))
                {
                    DestroyVisuals(instance);
                    activeInstances.RemoveAt(i);
                }
            }

            if (activeInstances.Count == 0)
                playing = false;
        }

        private List<ReplaySnapshot> SelectSnapshots(string sceneName, string entryFromScene)
        {
            var candidates = PBManager.GetPlaybackCandidates(sceneName, entryFromScene);
            if (candidates.Count == 0)
                return new List<ReplaySnapshot>();

            var selected = candidates
                .Where(snapshot => selectionState?.IsPlaybackSelected(snapshot.SnapshotId) == true)
                .ToList();
            if (selected.Count > 0)
            {
                Log.LogInfo($"[Ghost] Using selected playback subset ({selected.Count}/{candidates.Count}) for {sceneName} <- {entryFromScene}");
                return selected;
            }

            var best = candidates[0];
            Log.LogInfo($"[Ghost] Using fallback best PB {best.Key}#{best.SnapshotId} for {sceneName} <- {entryFromScene}");
            return new List<ReplaySnapshot> { best };
        }

        private bool TickInstance(PlaybackInstance instance, float deltaTime,
            float interval, float z, Color globalColor)
        {
            var room = instance.Room;
            if (room.FrameCount == 0)
                return false;

            instance.PlaybackTime += deltaTime;
            int frameIdx = Mathf.FloorToInt(instance.PlaybackTime / interval);
            if (frameIdx >= room.FrameCount)
                return false;

            if (room.FrameCount == 1)
            {
                RenderFrame(instance, room.Frames[0], room.Frames[0], 0f, z, globalColor);
                return true;
            }

            if (frameIdx >= room.FrameCount - 1)
                frameIdx = room.FrameCount - 2;

            FrameData a = room.Frames[frameIdx];
            FrameData b = room.Frames[frameIdx + 1];
            float t = Mathf.Clamp01((instance.PlaybackTime - frameIdx * interval) / interval);
            RenderFrame(instance, a, b, t, z, globalColor);
            return true;
        }

        private void RenderFrame(PlaybackInstance instance, FrameData a, FrameData b,
            float t, float z, Color globalColor)
        {
            float x = Mathf.LerpUnclamped(a.x, b.x, t);
            float y = Mathf.LerpUnclamped(a.y, b.y, t);
            FrameData animFrame = t < 0.5f ? a : b;
            Vector3 pos = new Vector3(x, y, z);
            Color color = instance.Snapshot.ResolveGhostColor(globalColor);

            if (!string.IsNullOrEmpty(animFrame.animClip) && instance.Sprite != null)
                RenderSprite(instance, pos, animFrame, color);
            else
                RenderDiamond(instance, pos, color);
        }

        private void EnsureSpriteResources()
        {
            if (spriteInitDone)
                return;

            spriteInitDone = true;

            try
            {
                if (HeroController.instance == null)
                {
                    Log.LogWarning("[Ghost] HeroController null - using diamond");
                    return;
                }

                var heroSprite = HeroController.instance.GetComponent<tk2dSprite>()
                    ?? HeroController.instance.GetComponentInChildren<tk2dSprite>();
                if (heroSprite?.Collection == null)
                {
                    Log.LogWarning("[Ghost] No tk2dSprite/Collection on hero - using diamond");
                    return;
                }

                spriteCollection = heroSprite.Collection;
                defaultSpriteId = heroSprite.spriteId;
                Log.LogInfo($"[Ghost] Sprite ready - collection='{heroSprite.Collection.name}'");

                var heroAnim = heroSprite.GetComponent<tk2dSpriteAnimator>()
                    ?? heroSprite.GetComponentInParent<tk2dSpriteAnimator>()
                    ?? heroSprite.GetComponentInChildren<tk2dSpriteAnimator>()
                    ?? HeroController.instance.GetComponent<tk2dSpriteAnimator>()
                    ?? HeroController.instance.GetComponentInChildren<tk2dSpriteAnimator>();
                if (heroAnim?.Library != null)
                {
                    clipCache = new Dictionary<string, tk2dSpriteAnimationClip>(
                        heroAnim.Library.clips.Length);
                    foreach (var clip in heroAnim.Library.clips)
                        if (!string.IsNullOrEmpty(clip.name))
                            clipCache[clip.name] = clip;
                    Log.LogInfo($"[Ghost] Clip cache: {clipCache.Count} clips");
                }
                else
                {
                    Log.LogWarning("[Ghost] No animator library - sprite will show default frame");
                }
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"[Ghost] Sprite init failed: {ex.Message} - using diamond");
                spriteCollection = null;
                clipCache = null;
                defaultSpriteId = -1;
            }
        }

        private void CreateVisuals(PlaybackInstance instance)
        {
            instance.DiamondGo = new GameObject($"ReplayGhost_Diamond_{instance.Snapshot.SnapshotId}");
            Object.DontDestroyOnLoad(instance.DiamondGo);
            instance.DiamondGo.SetActive(false);

            var diamondLine = instance.DiamondGo.AddComponent<LineRenderer>();
            diamondLine.useWorldSpace = true;
            diamondLine.loop = true;
            diamondLine.positionCount = 4;
            diamondLine.startWidth = 0.06f;
            diamondLine.endWidth = 0.06f;
            diamondLine.numCapVertices = 2;
            diamondLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            diamondLine.receiveShadows = false;

            var diamondMat = new Material(Shader.Find("Sprites/Default"));
            diamondLine.material = diamondMat;
            instance.DiamondLine = diamondLine;
            instance.DiamondMat = diamondMat;

            if (spriteCollection == null || defaultSpriteId < 0)
                return;

            try
            {
                instance.SpriteGo = new GameObject($"ReplayGhost_Sprite_{instance.Snapshot.SnapshotId}");
                instance.SpriteGo.SetActive(false);
                Object.DontDestroyOnLoad(instance.SpriteGo);

                var sprite = instance.SpriteGo.AddComponent<tk2dSprite>();
                sprite.Collection = spriteCollection;
                sprite.spriteId = defaultSpriteId;
                instance.Sprite = sprite;

                instance.SpriteGo.SetActive(true);
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"[Ghost] Sprite instance init failed: {ex.Message} - using diamond");
                if (instance.SpriteGo != null)
                {
                    Object.Destroy(instance.SpriteGo);
                    instance.SpriteGo = null;
                }
                instance.Sprite = null;
            }
        }

        private void DestroyVisuals(PlaybackInstance instance)
        {
            if (instance.SpriteGo != null)
                Object.Destroy(instance.SpriteGo);
            if (instance.DiamondGo != null)
                Object.Destroy(instance.DiamondGo);
            if (instance.DiamondMat != null)
                Object.Destroy(instance.DiamondMat);

            instance.SpriteGo = null;
            instance.Sprite = null;
            instance.DiamondGo = null;
            instance.DiamondLine = null;
            instance.DiamondMat = null;
        }

        private void RenderSprite(PlaybackInstance instance, Vector3 pos,
            FrameData fd, Color color)
        {
            if (instance.Sprite == null || instance.SpriteGo == null)
                return;

            if (clipCache != null && clipCache.TryGetValue(fd.animClip, out var clip)
                && clip.frames.Length > 0)
            {
                instance.Sprite.spriteId = clip.frames[fd.animFrame % clip.frames.Length].spriteId;
            }

            instance.Sprite.color = color;
            instance.SpriteGo.transform.position = pos;

            Vector3 scale = instance.SpriteGo.transform.localScale;
            instance.SpriteGo.transform.localScale = new Vector3(
                fd.facingRight ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x),
                scale.y,
                scale.z);

            instance.SpriteGo.SetActive(true);
            instance.DiamondGo?.SetActive(false);
        }

        private static void RenderDiamond(PlaybackInstance instance, Vector3 center,
            Color color)
        {
            instance.SpriteGo?.SetActive(false);
            if (instance.DiamondGo == null || instance.DiamondLine == null)
                return;

            if (instance.DiamondMat != null)
                instance.DiamondMat.color = color;

            instance.DiamondGo.SetActive(true);
            const float s = 0.25f;
            instance.DiamondLine.SetPosition(0, center + new Vector3(0, s, 0));
            instance.DiamondLine.SetPosition(1, center + new Vector3(s, 0, 0));
            instance.DiamondLine.SetPosition(2, center + new Vector3(0, -s, 0));
            instance.DiamondLine.SetPosition(3, center + new Vector3(-s, 0, 0));
        }
    }
}
