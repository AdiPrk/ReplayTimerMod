using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ReplayTimerMod
{
    /// <summary>
    /// Identifies a specific DebugMod cheat/debug ability. Used so the
    /// "run cancelled" banner can show a different message per ability -
    /// see <see cref="CheatMessages"/> to customize the text for each one.
    /// </summary>
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

    /// <summary>
    /// Reflection-based bridge into a "DebugMod"-style companion mod. Supports
    /// both known shapes:
    ///   - hk-speedrunning/Silksong.DebugMod (Silksong)
    ///   - TheMulhima/HollowKnight.DebugMod  (Hollow Knight)
    /// and degrades gracefully (no-ops / returns false/null) for anything else -
    /// DebugMod not installed, not loaded yet, or a future version with a
    /// different API surface.
    ///
    /// Why this exists / design notes:
    ///   - Both mods expose a static SaveState.loadingSavestate, but with
    ///     DIFFERENT TYPES: Silksong's is a `SaveState?` reference (non-null
    ///     while loading), HollowKnight's (TheMulhima) is a plain `bool`.
    ///     IsLoadingSavestate below handles both shapes.
    ///   - HollowKnight.DebugMod does NOT expose BeforeLoad/AfterLoad/OnSave
    ///     events at all (Silksong.DebugMod does). Rather than depend on an
    ///     event API that isn't universal, RoomTracker.Tick() polls
    ///     IsLoadingSavestate every frame and reacts to true/false
    ///     transitions. This is the ONLY mechanism that reliably catches
    ///     "set + load savestate in the same room" across both mods (no
    ///     scene-change event fires for a same-scene reload), and it works
    ///     identically for cross-room loads too.
    ///   - Cheat flag field names differ between mods in a couple of places
    ///     (e.g. Silksong's "infiniteSilk" vs HollowKnight's "infiniteSoul",
    ///     and timescale/frozen state living in different places). GetField/
    ///     GetProperty below try every known name and silently skip ones that
    ///     don't exist on a given version - GetActiveDebugAbility() checks
    ///     whichever handles were actually resolved.
    ///   - All reflection lookups use Public|NonPublic|Static, since
    ///     HollowKnight.DebugMod's cheat flags are `internal static` fields
    ///     (Silksong's equivalents are `public static`).
    /// </summary>
    public static class DebugModBridge
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("DebugModBridge");

        private const string AssemblyName = "DebugMod";

        private const BindingFlags StaticAny =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _hooked;
        private static bool _giveUp;

        // TryHook retry throttle (see TryHook).
        private const int HookRetryIntervalMs = 1000;
        private static int _nextHookAttemptTick;

        // SaveState.loadingSavestate - see IsLoadingSavestate for the type-shape note.
        private static PropertyInfo? _loadingSavestateProp;

        // Cheat flags shared by name across both mods.
        private static FieldInfo? _noclipField;
        private static FieldInfo? _invincibleField;
        private static FieldInfo? _infiniteHpField;
        private static FieldInfo? _infiniteSilkField;          // Silksong "infiniteSilk" / HK "infiniteSoul"
        private static FieldInfo? _infiniteToolsField;         // Silksong only - no HK equivalent
        private static FieldInfo? _heroColliderDisabledField;  // Silksong only - HK folds this into noclip

        // Timescale/frozen state - Silksong exposes these on its TimeScale MonoBehaviour.
        private static PropertyInfo? _customTimeScaleProp;     // Silksong TimeScale.CustomTimeScale (float)
        private static PropertyInfo? _frozenProp;              // Silksong TimeScale.Frozen (bool)

        // Timescale/frozen state - HollowKnight.DebugMod exposes these directly
        // on its DebugMod class instead.
        private static FieldInfo? _hkFrozenField;              // HK DebugMod.PauseGameNoUIActive ("Freeze Game")
        private static FieldInfo? _hkTimeScaleActiveField;      // HK DebugMod.TimeScaleActive
        private static FieldInfo? _hkCurrentTimeScaleField;     // HK DebugMod.CurrentTimeScale (float)

        /// <summary>True once DebugMod's reflection handles have been resolved
        /// (even if some individual members weren't found on this version).</summary>
        public static bool IsAvailable
        {
            get { return _hooked; }
        }

        /// <summary>
        /// Resolves DebugMod's types/members via reflection. Safe to call
        /// repeatedly - it no-ops once it has either succeeded or hit an
        /// unexpected error. If DebugMod's assembly simply isn't loaded yet,
        /// this returns quietly so callers can retry later (handles either
        /// mod-load order). Each section is resolved independently so a
        /// missing/renamed member in one area doesn't prevent the others from
        /// working.
        /// </summary>
        public static void TryHook()
        {
            if (_hooked || _giveUp) return;

            // The state getters call this every frame; when DebugMod simply
            // isn't installed the assembly scan below would otherwise run (and
            // allocate) once per frame forever. Rate-limit retries to ~1/s.
            // Unchecked subtraction stays correct across TickCount wraparound.
            int now = Environment.TickCount;
            if (now - _nextHookAttemptTick < 0) return;
            _nextHookAttemptTick = now + HookRetryIntervalMs;

            Assembly? asm = FindAssembly(AssemblyName);
            if (asm == null) return; // not loaded yet (or not installed) - try again later

            try
            {
                Type? saveStateType = FindType(asm,
                    "DebugMod.SaveStates.SaveState", // Silksong.DebugMod
                    "DebugMod.SaveState",            // HollowKnight.DebugMod (TheMulhima)
                    "SaveState");                    // bare-name fallback for older/other builds

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

        /// <summary>
        /// True while a savestate load is in progress. RoomTracker.Tick()
        /// polls this every frame (not just on scene change) and reacts to
        /// true/false transitions, so a "set + load savestate in the same
        /// room" is caught the instant it happens, even though no
        /// scene-change event fires for a same-scene reload.
        ///
        /// Handles both shapes seen in the wild:
        ///   - Silksong.DebugMod: loadingSavestate is a SaveState? reference,
        ///     non-null while a load is in progress.
        ///   - HollowKnight.DebugMod (TheMulhima): loadingSavestate is a plain
        ///     bool.
        /// A naive "!= null" check would always be true for a boxed bool
        /// (even when false), so the value's runtime type is checked first.
        /// </summary>
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
                    return true; // non-null reference (e.g. Silksong's SaveState instance)
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[DebugModBridge] IsLoadingSavestate check failed: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns the first active DebugMod cheat/debug ability that could
        /// invalidate a legitimate room time - infinite jump, noclip,
        /// invincibility, infinite HP/silk (or HK's "soul")/tools, disabled
        /// hero collider, a frozen timescale, or a non-default timescale - or
        /// null if none are active. Checked in this fixed order, so if
        /// multiple are active at once the first one in the list "wins" for
        /// display purposes. Any individual member that wasn't resolved for
        /// the running DebugMod version is simply skipped.
        /// </summary>
        public static DebugAbilityKind? GetActiveDebugAbility()
        {
            // PlayerData.instance.infiniteAirJump is a base-game field that
            // both Silksong.DebugMod and HollowKnight.DebugMod toggle directly
            // (identical field name in both). Check it unconditionally - no
            // DebugMod hook required - so it's caught even if DebugMod isn't
            // installed/hooked but the flag is set some other way (another
            // mod, save edit, etc.).
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

        /// <summary>Direct (non-reflection) check of the base game's
        /// PlayerData.infiniteAirJump flag - the "Infinite Jump" cheat in both
        /// DebugMod variants.</summary>
        private static bool IsInfiniteJumpActive()
        {
            try { return PlayerData.instance != null && PlayerData.instance.infiniteAirJump; }
            catch (Exception e)
            {
                Log.LogWarning($"[DebugModBridge] IsInfiniteJumpActive check failed: {e.Message}");
                return false;
            }
        }

        /// <summary>"Freeze game" style cheats: Silksong's TimeScale.Frozen,
        /// or HollowKnight.DebugMod's PauseGameNoUIActive.</summary>
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

        /// <summary>Non-default timescale: Silksong's TimeScale.CustomTimeScale,
        /// or HollowKnight.DebugMod's TimeScaleActive/CurrentTimeScale.</summary>
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

        /// <summary>Convenience wrapper - true if <see cref="GetActiveDebugAbility"/> returns non-null.</summary>
        public static bool AnyDebugAbilityActive() => GetActiveDebugAbility().HasValue;

        private static bool GetBool(FieldInfo? field)
        {
            if (field == null) return false;
            return field.GetValue(null) is bool b && b;
        }
    }
}