using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    // Experimental: points the game's camera at a ghost replay instead of
    // the player, keeping the game's normal camera feel.
    //
    // Both games drive the camera the same way: CameraTarget.Update()
    // smooth-damps its transform toward a private "heroTransform" field
    // (reset to the hero by CameraTarget.SceneInit() on every scene load),
    // and CameraController.LateUpdate() then damps the actual camera toward
    // that target, clamped to scene and lock-zone bounds. Re-pointing
    // heroTransform at a ghost anchor therefore inherits all of the game's
    // own smoothing, engages/disengages as a smooth pan, and self-heals on
    // scene loads even if a release is ever missed.
    //
    // Two systems normally key off the real player and are re-driven from
    // the ghost here so the camera behaves as if the ghost were the hero:
    //
    //  - Lock zones: CameraLockArea triggers fire on the player's collider,
    //    and in LOCK_ZONE mode CameraTarget clamps its destination to the
    //    active zone's bounds - so a y-locked corridor would follow the
    //    ghost on x but freeze on y. While following, the zone CONTAINING
    //    THE GHOST is found by point test each frame and its bounds/mode
    //    are applied (the same -1 -> scene-limit mapping LockToArea uses);
    //    outside any zone the camera follows freely. On release the
    //    controller's own lock state (still tracked from the player's
    //    triggers the whole time) is re-imposed.
    //
    //  - Facing look-ahead: CameraTarget.xOffset normally drifts toward
    //    the hero's facing direction; that drift is replicated from the
    //    ghost's recorded facing, with the game's lock-bound clamp.
    //
    // Not reproduced (state the recording doesn't carry): dash/superdash
    // look-ahead and the fall catcher.
    public static class CameraFollow
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("CameraFollow");

        private static FieldInfo? heroTransformField;
        private static bool fieldLookupDone;
        private static Transform? currentTarget;

        // Lock areas of the current room, gathered once per engage.
        private static readonly List<CameraLockArea> zones = new List<CameraLockArea>();
        private static readonly List<Collider2D> zoneColliders = new List<Collider2D>();
        private static CameraLockArea? currentZone;

        /// <summary>
        /// Sets (or clears, with null) the transform the camera follows;
        /// <paramref name="facingRight"/> is the followed ghost's current
        /// facing, used for the look-ahead drift. Call every tick while
        /// following - the game's own systems write over the target state
        /// at any time (scene loads, the player's lock-zone triggers) and
        /// are re-driven from the ghost each frame. Passing null restores
        /// the real hero once and then no-ops.
        /// </summary>
        public static void SetTarget(Transform? target, bool facingRight = true)
        {
            if (target == null && currentTarget == null)
                return;

            var field = HeroTransformField();
            if (field == null)
            {
                currentTarget = null;
                return;
            }

            var gameCameras = GameCameras.instance;
            var camTarget = gameCameras != null ? gameCameras.cameraTarget : null;
            if (camTarget == null)
            {
                // Scene tear-down; the next scene's SceneInit re-points the
                // camera at the hero on its own.
                ClearZones();
                currentTarget = null;
                return;
            }

            var ctrl = camTarget.cameraCtrl;

            if (target != null)
            {
                if (currentTarget == null)
                {
                    Log.LogInfo($"[CameraFollow] Following '{target.name}'");
                    GatherZones();
                }

                field.SetValue(camTarget, target);
                ApplyGhostLockZone(camTarget, ctrl, target.position);
                ApplyGhostLookAhead(camTarget, facingRight);
                currentTarget = target;
                return;
            }

            var hero = HeroController.instance;
            if (hero != null)
                field.SetValue(camTarget, hero.transform);

            // Re-impose the controller's own lock state - it kept tracking
            // the player's triggers the whole time. Never stomp FREE (the
            // game asserts it for cutscenes/transitions).
            if (ctrl != null)
            {
                camTarget.xLockMin = ctrl.xLockMin;
                camTarget.xLockMax = ctrl.xLockMax;
                camTarget.yLockMin = ctrl.yLockMin;
                camTarget.yLockMax = ctrl.yLockMax;

                if (camTarget.mode == CameraTarget.TargetMode.FOLLOW_HERO
                    || camTarget.mode == CameraTarget.TargetMode.LOCK_ZONE)
                {
                    bool playerLocked = ctrl.lockZoneList != null
                        && ctrl.lockZoneList.Count > 0;
                    camTarget.mode = playerLocked
                        ? CameraTarget.TargetMode.LOCK_ZONE
                        : CameraTarget.TargetMode.FOLLOW_HERO;
                }
            }

            ClearZones();
            Log.LogInfo("[CameraFollow] Released - camera back on the player");
            currentTarget = null;
        }

        /// <summary>
        /// Applies the lock zone containing the ghost (if any): LOCK_ZONE
        /// mode with the zone's bounds, exactly as the game's LockToArea
        /// would for the player; FOLLOW_HERO with open bounds otherwise.
        /// FREE mode is left alone - the game owns it.
        /// </summary>
        private static void ApplyGhostLockZone(CameraTarget camTarget,
            CameraController? ctrl, Vector3 ghostPos)
        {
            if (ctrl == null)
                return;
            if (camTarget.mode != CameraTarget.TargetMode.FOLLOW_HERO
                && camTarget.mode != CameraTarget.TargetMode.LOCK_ZONE)
                return;

            var zone = FindZoneAt(ghostPos);
            currentZone = zone;

            if (zone == null)
            {
                camTarget.mode = CameraTarget.TargetMode.FOLLOW_HERO;
                camTarget.xLockMin = 0f;
                camTarget.xLockMax = ctrl.xLimit;
                camTarget.yLockMin = 0f;
                camTarget.yLockMax = ctrl.yLimit;
                return;
            }

            // Same -1 -> scene-limit mapping as CameraController.LockToArea.
            float xMin = zone.cameraXMin;
            float xMax = zone.cameraXMax;
            float yMin = zone.cameraYMin;
            float yMax = zone.cameraYMax;
            if (xMin < 0f) xMin = 14.6f;
            if (xMax < 0f) xMax = ctrl.xLimit;
            if (yMin < 0f) yMin = 8.3f;
            if (yMax < 0f) yMax = ctrl.yLimit;

            camTarget.mode = CameraTarget.TargetMode.LOCK_ZONE;
            camTarget.xLockMin = xMin;
            camTarget.xLockMax = xMax;
            camTarget.yLockMin = yMin;
            camTarget.yLockMax = yMax;
        }

        /// <summary>
        /// Replicates CameraTarget's facing look-ahead from the ghost's
        /// facing: xOffset drifts toward +/-xLookAhead at the game's rate,
        /// clamped so it never pushes the view past the lock bounds.
        /// </summary>
        private static void ApplyGhostLookAhead(CameraTarget camTarget,
            bool facingRight)
        {
            float look = camTarget.xLookAhead;
            float wanted = facingRight ? look : -look;
            float xOffset = Mathf.MoveTowards(camTarget.xOffset, wanted,
                Time.deltaTime * 6f);

            if (camTarget.mode == CameraTarget.TargetMode.LOCK_ZONE)
            {
                float targetX = camTarget.transform.position.x;
                if (targetX + xOffset > camTarget.xLockMax)
                    xOffset = camTarget.xLockMax - targetX;
                if (targetX + xOffset < camTarget.xLockMin)
                    xOffset = camTarget.xLockMin - targetX;
            }

            camTarget.xOffset = xOffset;
        }

        /// <summary>The lock area containing the point, preferring the one
        /// already applied (the game keeps the current zone until exit) and
        /// otherwise the highest-priority match.</summary>
        private static CameraLockArea? FindZoneAt(Vector3 point)
        {
            CameraLockArea? best = null;
            bool currentStillHolds = false;

            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                var col = zoneColliders[i];
                if (zone == null || col == null || !zone.isActiveAndEnabled)
                    continue;
                if (!col.OverlapPoint(point))
                    continue;

                if (zone == currentZone)
                    currentStillHolds = true;
                if (best == null || HigherPriority(zone, best))
                    best = zone;
            }

            // Hysteresis: stay in the zone we're in unless a strictly
            // higher-priority one also contains the ghost.
            if (currentStillHolds && best != null && currentZone != null
                && !HigherPriority(best, currentZone))
                return currentZone;

            return best;
        }

        private static bool HigherPriority(CameraLockArea a, CameraLockArea b)
        {
#if SILKSONG_BUILD
            return a.priority > b.priority;
#else
            return a.maxPriority && !b.maxPriority;
#endif
        }

        private static void GatherZones()
        {
            ClearZones();
#if SILKSONG_BUILD
            var found = Object.FindObjectsByType<CameraLockArea>(
                FindObjectsSortMode.None);
#else
            var found = Object.FindObjectsOfType<CameraLockArea>();
#endif
            foreach (var zone in found)
            {
                var col = zone.GetComponent<Collider2D>();
                if (col == null)
                    continue;
                zones.Add(zone);
                zoneColliders.Add(col);
            }
            Log.LogInfo($"[CameraFollow] {zones.Count} lock zone(s) in room");
        }

        private static void ClearZones()
        {
            zones.Clear();
            zoneColliders.Clear();
            currentZone = null;
        }

        private static FieldInfo? HeroTransformField()
        {
            if (fieldLookupDone)
                return heroTransformField;

            fieldLookupDone = true;
            heroTransformField = typeof(CameraTarget).GetField("heroTransform",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (heroTransformField == null)
                Log.LogWarning("[CameraFollow] CameraTarget.heroTransform not found - camera follow unavailable");
            else if (heroTransformField.FieldType != typeof(Transform))
            {
                Log.LogWarning("[CameraFollow] CameraTarget.heroTransform has unexpected type - camera follow unavailable");
                heroTransformField = null;
            }

            return heroTransformField;
        }
    }
}
