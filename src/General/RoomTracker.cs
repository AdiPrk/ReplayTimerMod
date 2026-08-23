using System;
using BepInEx.Logging;
using UnityEngine;
using GlobalEnums;

namespace ReplayTimerMod
{
    public static class RoomTracker
    {
        public const float MAX_ROOM_TIME = 180f;

        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("RoomTracker");

        // ── Public state ─────────────────────────────────────────────────────
        public static bool IsRecording { get; private set; } = false;
        public static string CurrentScene { get; private set; } = "";
        public static string PreviousScene { get; private set; } = "";
        public static string EntryFromScene { get; private set; } = "";
        public static float CurrentRoomTime { get; private set; } = 0f;

        /// <summary>
        /// True if a DebugMod debug ability (noclip, invincibility, infinite
        /// resources, timescale changes, etc.) was detected during the room
        /// currently being recorded. The moment this becomes true, the
        /// in-progress recording is cancelled (see <see cref="OnRunCancelled"/>)
        /// and <see cref="IsRecording"/> goes false, so <see cref="OnRoomExit"/>
        /// will not fire for this room. Resets to false when a new room
        /// recording starts. Kept available as a sticky per-room record/guard
        /// for consumers that want an extra belt-and-suspenders check.
        /// </summary>
        public static bool RoomUsedDebugAbilities { get; private set; } = false;

        /// <summary>
        /// Modifier bitmask accumulated for the room run in progress (see
        /// <see cref="ModifierMask"/>). Valid while <see cref="IsRecording"/>
        /// and, for the just-finished run, during <see cref="OnRoomExit"/>
        /// handling - the mask resets when the next room's recording starts.
        /// </summary>
        public static int CurrentRunModifierMask => ModifierTracker.CurrentMask;

        // ── Events ───────────────────────────────────────────────────────────
        public static event Action<string, string>? OnRoomEnter;
        public static event Action<string, string, string, float>? OnRoomExit;
        /// <summary>
        /// Fired when an in-progress room recording is discarded for any
        /// reason (death, non-gate exit, over time, savestate load, cheat
        /// cancellation, etc.). Consumers should always discard/stop their
        /// own recording state here. Check
        /// <see cref="KeepGhostPlaybackOnDiscard"/> at the same time to decide
        /// whether ghost playback should also be stopped.
        /// </summary>
        public static event Action? OnRecordingDiscarded;

        /// <summary>
        /// Valid only for the duration of an <see cref="OnRecordingDiscarded"/>
        /// invocation (set immediately beforehand). True when the discard was
        /// caused by a cheat-cancellation (see <see cref="OnRunCancelled"/>):
        /// the in-progress recording is invalid and must be discarded, but the
        /// player isn't leaving the room, so ghost playback should keep
        /// running uninterrupted. False for every other discard reason (death,
        /// non-gate exit, over time, savestate load, etc.), where ghost
        /// playback should stop as before.
        /// </summary>
        public static bool KeepGhostPlaybackOnDiscard { get; private set; } = false;

        /// <summary>
        /// Fired when an in-progress room recording is cancelled because a
        /// DebugMod cheat/debug ability was detected mid-room. The string is
        /// a short, human-readable reason suitable for display in the timer
        /// UI. <see cref="OnRecordingDiscarded"/> always fires first (with
        /// <see cref="KeepGhostPlaybackOnDiscard"/> set to true), so the
        /// cancelled run is never saved or uploaded, but ghost playback for
        /// the room keeps going.
        /// </summary>
        public static event Action<string>? OnRunCancelled;

        // ── Private state ────────────────────────────────────────────────────
        private static bool _initialized = false;
        private static string _lastSceneName = "";
        private static bool _pendingGateTransition = false;
        private static bool _menuTraversalPending = false;
        private static bool _wasLoadingSavestate = false;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            GameHooks.OnPlayerDead += HandleInvalidation;
            GameHooks.OnGateTransitionBegin += HandleGateTransitionBegin;

            DebugModBridge.TryHook();
        }

        private static void HandleGateTransitionBegin(string destScene, string entryGate)
        {
            // A load INTO the menu is never a gate transition, and it also
            // means the next observed scene change (loading a save) must not
            // start a recording. On HK the mod only ticks while the hero
            // exists, so the menu scenes themselves are never observed by
            // Tick() - this flag bridges that gap.
            if (destScene == KnownScenes.MenuTitle || destScene == KnownScenes.QuitToMenu)
            {
                _pendingGateTransition = false;
                _menuTraversalPending = true;
                Log.LogDebug($"[Gate] menu load -> '{destScene}' - clearing pending gate");
                return;
            }

#if V1221
            // HK 1.2.2.1 fires BeforeSceneLoadHook for EVERY LoadScene call,
            // including save loads and respawns - those carry no entry gate
            // (GameManager.entryGateName is empty), while real gate
            // transitions always set gate.entryPoint before loading. A
            // gateless full-scene load also invalidates any stale pending
            // flag.
            if (string.IsNullOrEmpty(entryGate))
            {
                _pendingGateTransition = false;
                Log.LogDebug($"[Gate] gateless load -> '{destScene}' - not a gate transition");
                return;
            }
#endif

            _pendingGateTransition = true;
            Log.LogDebug($"[Gate] pending -> {destScene} via '{entryGate}'");
        }

        /// <summary>Standard invalidation - ghost playback should stop (see
        /// <see cref="KeepGhostPlaybackOnDiscard"/>). Used directly as an
        /// Action handler (GameHooks.OnPlayerDead, etc.), so this overload
        /// must stay parameterless.</summary>
        private static void HandleInvalidation() => HandleInvalidation(keepGhostPlayback: false);

        private static void HandleInvalidation(bool keepGhostPlayback)
        {
            if (IsRecording)
            {
                Log.LogInfo($"[RoomTracker] Invalidated in {CurrentScene} - discarding");
                IsRecording = false;
                KeepGhostPlaybackOnDiscard = keepGhostPlayback;
                OnRecordingDiscarded?.Invoke();
            }
            CurrentRoomTime = 0f;
            _pendingGateTransition = false;
        }

        private static void OnActiveSceneChanged(string fromName, string toName)
        {
            // Primary same-room/cross-room savestate detection happens every
            // frame in Tick() via IsLoadingSavestate transitions. This is a
            // defensive fallback in case a load is somehow still reported as
            // "in progress" right at the moment of a scene change, plus the
            // long-standing "Room_Mender_House" special case (HollowKnight.
            // DebugMod's savestate loader bounces through this scene as a
            // fast-loading dummy room while restoring state).
            if (DebugModBridge.IsLoadingSavestate || fromName == "Room_Mender_House" || toName == "Room_Mender_House")
            {
                Log.LogInfo("[RoomTracker] Savestate detected - invalidating");
                HandleInvalidation();
            }

            bool arrivedViaGate = _pendingGateTransition;
            _pendingGateTransition = false;

            // A trip through the main menu happened since the last observed
            // scene (set by HandleGateTransitionBegin; on HK the menu scenes
            // are never seen by Tick because the hero doesn't update there).
            if (_menuTraversalPending)
            {
                _menuTraversalPending = false;
                arrivedViaGate = false;
            }

            // An empty fromName means this is the first scene the mod has
            // ever observed - a save-load spawn, never a gate arrival.
            if (string.IsNullOrEmpty(fromName)
                || fromName == KnownScenes.MenuTitle || fromName == KnownScenes.QuitToMenu)
                arrivedViaGate = false;

            bool toMenu = toName == KnownScenes.MenuTitle || toName == KnownScenes.QuitToMenu;

            if (IsRecording)
            {
                if (arrivedViaGate && !IsOverTime() && !toMenu)
                {
                    string exitedScene = CurrentScene;
                    string exitedFromScene = EntryFromScene;
                    string exitedTo = toName;
                    float exitedTime = CurrentRoomTime;

                    Log.LogInfo($"[RoomTracker] Exit: {exitedScene} [{exitedFromScene}->{exitedTo}] {TimeUtil.Format(exitedTime)} mods=0x{ModifierTracker.CurrentMask:X} [{ModifierMask.ToBadge(ModifierTracker.CurrentMask, 32)}]");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    OnRoomExit?.Invoke(exitedScene, exitedFromScene, exitedTo, exitedTime);
                }
                else
                {
                    if (IsOverTime())
                        Log.LogInfo($"[RoomTracker] Over time limit in {CurrentScene} - discarding");
                    else if (!arrivedViaGate)
                        Log.LogInfo($"[RoomTracker] Non-gate exit from {CurrentScene} - discarding");

                    IsRecording = false;
                    CurrentRoomTime = 0f;
                    // The player IS leaving the room here, so the ghost must
                    // stop - clear any stale keep-alive left over from an
                    // earlier cheat-cancellation discard.
                    KeepGhostPlaybackOnDiscard = false;
                    OnRecordingDiscarded?.Invoke();
                }
            }

            bool wasWarping = QuickWarp.IsWarping;

            if (arrivedViaGate && !toMenu && !wasWarping)
            {
                PreviousScene = CurrentScene;
                CurrentScene = toName;
                EntryFromScene = fromName;
                CurrentRoomTime = 0f;
                IsRecording = true;
                RoomUsedDebugAbilities = false;
                ModifierTracker.Reset();

                Log.LogInfo($"[RoomTracker] Enter: {CurrentScene} from {EntryFromScene}");
                OnRoomEnter?.Invoke(CurrentScene, EntryFromScene);
            }
            else
            {
                PreviousScene = CurrentScene;
                CurrentScene = toName;
                EntryFromScene = "";
                CurrentRoomTime = 0f;
                IsRecording = false;

                if (wasWarping)
                    Log.LogInfo($"[RoomTracker] Warp arrival in {toName} - not recording");
                else
                    Log.LogInfo($"[RoomTracker] IDLE in {toName}");
            }

            // End the warp-suppression window now that the arrival has been
            // handled. Tying this to the actual scene activation (rather than
            // a frame count in QuickWarp) is what guarantees a warp never
            // starts a run, since BeginSceneTransition activates async.
            if (wasWarping)
                QuickWarp.NotifyArrival();

            bool IsOverTime() => CurrentRoomTime > MAX_ROOM_TIME;
        }

        public static void Tick(bool shouldTick)
        {
            // Lazily hook into DebugMod, retrying periodically in case it loads
            // after this mod (TryHook throttles its own assembly scans). Once
            // hooked this is a no-op.
            if (!DebugModBridge.IsAvailable)
                DebugModBridge.TryHook();

            // Poll for savestate-load start/finish every frame, regardless of
            // scene changes. This is the only mechanism that catches "set +
            // load savestate in the same room" (no scene-change event fires
            // for a same-scene reload), and it works identically for
            // cross-room loads. Unlike load/finish *events* - which
            // HollowKnight.DebugMod doesn't expose at all - this only relies
            // on the simple loadingSavestate flag both mods provide.
            bool isLoadingNow = DebugModBridge.IsLoadingSavestate;
            if (isLoadingNow != _wasLoadingSavestate)
            {
                _wasLoadingSavestate = isLoadingNow;
                Log.LogInfo($"[RoomTracker] Savestate load {(isLoadingNow ? "started" : "finished")} - invalidating");
                HandleInvalidation();
            }

            string currentSceneName = GetCurrentSceneName();
            if (!string.IsNullOrEmpty(currentSceneName) && currentSceneName != _lastSceneName)
            {
                string fromName = _lastSceneName;
                _lastSceneName = currentSceneName;
                OnActiveSceneChanged(fromName, currentSceneName);
            }

            if (!IsRecording) return;

            if (!RoomUsedDebugAbilities)
            {
                DebugAbilityKind? ability = DebugModBridge.GetActiveDebugAbility();
                if (ability.HasValue)
                {
                    RoomUsedDebugAbilities = true;
                    Log.LogInfo($"[RoomTracker] Debug ability '{ability.Value}' detected during {CurrentScene} - cancelling run");

                    HandleInvalidation(keepGhostPlayback: true);
                    OnRunCancelled?.Invoke(CheatMessages.For(ability.Value));
                    return;
                }
            }

            // Accumulate the run's modifier loadout every frame (regardless of
            // shouldTick - possession/equipment can change while the timer is
            // gated, e.g. during an in-room bench menu).
            ModifierTracker.Poll();

            if (shouldTick)
            {
                CurrentRoomTime += Time.unscaledDeltaTime;
            }
        }

        private static string GetCurrentSceneName()
        {
            try { return GameManager.instance?.GetSceneNameString() ?? ""; }
            catch { return ""; }
        }
    }
}