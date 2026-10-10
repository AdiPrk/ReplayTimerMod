using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    public enum DebugAbilityKind
    {
        Noclip,
        Invincible,
        InfiniteHP,
        InfiniteSilk,
        InfiniteTools,
        InfiniteJump,
        HeroColliderDisabled,
        TimeFrozen,
        TimeScale,
    }

    public static class DebugModBridge
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DebugModBridge");

        private const string AssemblyName = "DebugMod";

        private const BindingFlags StaticAny =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _hooked;
        private static bool _giveUp;

        private const int HookRetryIntervalMs = 1000;
        private static int _nextHookAttemptTick;

        private static PropertyInfo? _loadingSavestateProp;

        private static FieldInfo? _noclipField;
        private static FieldInfo? _invincibleField;
        private static FieldInfo? _infiniteHpField;
        private static FieldInfo? _infiniteSilkField;
        private static FieldInfo? _infiniteToolsField;
        private static FieldInfo? _heroColliderDisabledField;

        private static PropertyInfo? _customTimeScaleProp;
        private static PropertyInfo? _frozenProp;

        private static FieldInfo? _hkFrozenField;
        private static FieldInfo? _hkTimeScaleActiveField;
        private static FieldInfo? _hkCurrentTimeScaleField;

        public static bool IsAvailable
        {
            get { return _hooked; }
        }

        public static void TryHook()
        {
            if (_hooked || _giveUp) return;

            int now = Environment.TickCount;
            if (now - _nextHookAttemptTick < 0) return;
            _nextHookAttemptTick = now + HookRetryIntervalMs;

            Assembly? asm = FindAssembly(AssemblyName);
            if (asm == null) return;

            try
            {
                Type? saveStateType = FindType(asm,
                    "DebugMod.SaveStates.SaveState",
                    "DebugMod.SaveState",
                    "SaveState");

                Type? debugModType = FindType(asm,
                    "DebugMod.DebugMod",
                    "DebugMod");

                Type? timeScaleType = FindType(asm,
                    "DebugMod.MonoBehaviours.TimeScale",
                    "TimeScale");

                if (saveStateType != null)
                {
                    try
                    {
                        _loadingSavestateProp = saveStateType.GetProperty("loadingSavestate", StaticAny);
                        if (_loadingSavestateProp == null)
                            Log.LogWarning($"[DebugModBridge] 'loadingSavestate' not found on {saveStateType.FullName}");
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning($"[DebugModBridge] Failed resolving SaveState members: {e.Message}");
                    }
                }
                else
                {
                    Log.LogWarning("[DebugModBridge] SaveState type not found - savestate-load detection unavailable");
                }

                if (debugModType != null)
                {
                    try
                    {
                        _noclipField               = GetField(debugModType, "noclip");
                        _invincibleField           = GetField(debugModType, "playerInvincible");
                        _infiniteHpField           = GetField(debugModType, "infiniteHP");
                        _infiniteSilkField         = GetField(debugModType, "infiniteSilk", "infiniteSoul");
                        _infiniteToolsField        = GetField(debugModType, "infiniteTools");
                        _heroColliderDisabledField = GetField(debugModType, "heroColliderDisabled");

                        _hkFrozenField           = GetField(debugModType, "PauseGameNoUIActive");
                        _hkTimeScaleActiveField  = GetField(debugModType, "TimeScaleActive");
                        _hkCurrentTimeScaleField = GetField(debugModType, "CurrentTimeScale");
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning($"[DebugModBridge] Failed resolving DebugMod cheat fields: {e.Message}");
                    }
                }
                else
                {
                    Log.LogWarning("[DebugModBridge] DebugMod type not found - cheat-flag checks unavailable");
                }

                if (timeScaleType != null)
                {
                    try
                    {
                        _customTimeScaleProp = GetProperty(timeScaleType, "CustomTimeScale");
                        _frozenProp          = GetProperty(timeScaleType, "Frozen");
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning($"[DebugModBridge] Failed resolving TimeScale members: {e.Message}");
                    }
                }

                _hooked = true;
                Log.LogInfo("[DebugModBridge] Hooked into DebugMod");
            }
            catch (Exception e)
            {
                _giveUp = true;
                Log.LogWarning($"[DebugModBridge] Failed to hook DebugMod, disabling integration: {e}");
            }
        }

        private static Assembly? FindAssembly(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == name)
                    return asm;
            }
            return null;
        }

        private static Type? FindType(Assembly asm, params string[] candidates)
        {
            foreach (string name in candidates)
            {
                Type? t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        private static FieldInfo? GetField(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                FieldInfo? f = type.GetField(name, StaticAny);
                if (f != null) return f;
            }
            return null;
        }

        private static PropertyInfo? GetProperty(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                PropertyInfo? p = type.GetProperty(name, StaticAny);
                if (p != null) return p;
            }
            return null;
        }

        public static bool IsLoadingSavestate
        {
            get
            {
                TryHook();
                try
                {
                    object? value = _loadingSavestateProp?.GetValue(null, null);
                    if (value == null) return false;
                    if (value is bool b) return b;
                    return true;
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[DebugModBridge] IsLoadingSavestate check failed: {e.Message}");
                    return false;
                }
            }
        }

        public static DebugAbilityKind? GetActiveDebugAbility()
        {
            if (IsInfiniteJumpActive()) return DebugAbilityKind.InfiniteJump;

            TryHook();
            if (!_hooked) return null;

            try
            {
                if (GetBool(_noclipField)) return DebugAbilityKind.Noclip;
                if (GetBool(_invincibleField)) return DebugAbilityKind.Invincible;
                if (GetBool(_infiniteHpField)) return DebugAbilityKind.InfiniteHP;
                if (GetBool(_infiniteSilkField)) return DebugAbilityKind.InfiniteSilk;
                if (GetBool(_infiniteToolsField)) return DebugAbilityKind.InfiniteTools;
                if (GetBool(_heroColliderDisabledField)) return DebugAbilityKind.HeroColliderDisabled;

                if (IsTimeFrozen()) return DebugAbilityKind.TimeFrozen;
                if (IsTimeScaleChanged()) return DebugAbilityKind.TimeScale;
            }
            catch (Exception e)
            {
                Log.LogWarning($"[DebugModBridge] GetActiveDebugAbility check failed: {e.Message}");
            }

            return null;
        }

        private static bool IsInfiniteJumpActive()
        {
            try { return PlayerData.instance != null && PlayerData.instance.infiniteAirJump; }
            catch (Exception e)
            {
                Log.LogWarning($"[DebugModBridge] IsInfiniteJumpActive check failed: {e.Message}");
                return false;
            }
        }

        private static bool IsTimeFrozen()
        {
            if (_frozenProp != null
                && _frozenProp.GetValue(null, null) is bool frozen
                && frozen)
            {
                return true;
            }

            if (GetBool(_hkFrozenField)) return true;

            return false;
        }

        private static bool IsTimeScaleChanged()
        {
            if (_customTimeScaleProp != null
                && _customTimeScaleProp.GetValue(null, null) is float ts1
                && Mathf.Abs(ts1 - 1f) > 0.001f)
            {
                return true;
            }

            if (GetBool(_hkTimeScaleActiveField)) return true;

            if (_hkCurrentTimeScaleField != null
                && _hkCurrentTimeScaleField.GetValue(null) is float ts2
                && Mathf.Abs(ts2 - 1f) > 0.001f)
            {
                return true;
            }

            return false;
        }

        public static bool AnyDebugAbilityActive() => GetActiveDebugAbility().HasValue;

        private static bool GetBool(FieldInfo? field)
        {
            if (field == null) return false;
            return field.GetValue(null) is bool b && b;
        }
    }
}
