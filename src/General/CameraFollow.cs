using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    public static class CameraFollow
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("CameraFollow");

        private static FieldInfo? _heroTransformField;
        private static bool _fieldLookupDone;
        private static Transform? _currentTarget;

        private static readonly List<CameraLockArea> _zones = new List<CameraLockArea>();
        private static readonly List<Collider2D> _zoneColliders = new List<Collider2D>();
        private static CameraLockArea? _currentZone;

        public static void SetTarget(Transform? target, bool facingRight = true)
        {
            if (target == null && _currentTarget == null)
                return;

            var field = HeroTransformField();
            if (field == null)
            {
                _currentTarget = null;
                return;
            }

            var gameCameras = GameCameras.instance;
            var camTarget = gameCameras != null ? gameCameras.cameraTarget : null;
            if (camTarget == null)
            {
                ClearZones();
                _currentTarget = null;
                return;
            }

            var ctrl = camTarget.cameraCtrl;

            if (target != null)
            {
                if (_currentTarget == null)
                {
                    Log.LogInfo($"[CameraFollow] Following '{target.name}'");
                    GatherZones();
                }

                field.SetValue(camTarget, target);
                ApplyGhostLockZone(camTarget, ctrl, target.position);
                ApplyGhostLookAhead(camTarget, facingRight);
                _currentTarget = target;
                return;
            }

            var hero = HeroController.instance;
            if (hero != null)
                field.SetValue(camTarget, hero.transform);

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
            _currentTarget = null;
        }

        private static void ApplyGhostLockZone(CameraTarget camTarget,
            CameraController? ctrl, Vector3 ghostPos)
        {
            if (ctrl == null)
                return;
            if (camTarget.mode != CameraTarget.TargetMode.FOLLOW_HERO
                && camTarget.mode != CameraTarget.TargetMode.LOCK_ZONE)
                return;

            var zone = FindZoneAt(ghostPos);
            _currentZone = zone;

            if (zone == null)
            {
                camTarget.mode = CameraTarget.TargetMode.FOLLOW_HERO;
                camTarget.xLockMin = 0f;
                camTarget.xLockMax = ctrl.xLimit;
                camTarget.yLockMin = 0f;
                camTarget.yLockMax = ctrl.yLimit;
                return;
            }

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

        private static CameraLockArea? FindZoneAt(Vector3 point)
        {
            CameraLockArea? best = null;
            bool currentStillHolds = false;

            for (int i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                var col = _zoneColliders[i];
                if (zone == null || col == null || !zone.isActiveAndEnabled)
                    continue;
                if (!col.OverlapPoint(point))
                    continue;

                if (zone == _currentZone)
                    currentStillHolds = true;
                if (best == null || HigherPriority(zone, best))
                    best = zone;
            }

            if (currentStillHolds && best != null && _currentZone != null
                && !HigherPriority(best, _currentZone))
                return _currentZone;

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
                _zones.Add(zone);
                _zoneColliders.Add(col);
            }
            Log.LogInfo($"[CameraFollow] {_zones.Count} lock zone(s) in room");
        }

        private static void ClearZones()
        {
            _zones.Clear();
            _zoneColliders.Clear();
            _currentZone = null;
        }

        private static FieldInfo? HeroTransformField()
        {
            if (_fieldLookupDone)
                return _heroTransformField;

            _fieldLookupDone = true;
            _heroTransformField = typeof(CameraTarget).GetField("heroTransform",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_heroTransformField == null)
                Log.LogWarning("[CameraFollow] CameraTarget.heroTransform not found - camera follow unavailable");
            else if (_heroTransformField.FieldType != typeof(Transform))
            {
                Log.LogWarning("[CameraFollow] CameraTarget.heroTransform has unexpected type - camera follow unavailable");
                _heroTransformField = null;
            }

            return _heroTransformField;
        }
    }
}
