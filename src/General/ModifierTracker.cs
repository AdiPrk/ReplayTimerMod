using System.Collections.Generic;
using BepInEx.Logging;

namespace ReplayTimerMod
{
    /// <summary>
    /// Per-run modifier accumulator. RoomTracker calls <see cref="Reset"/>
    /// when a room recording starts and <see cref="Poll"/> every frame while
    /// recording; bits OR-accumulate (sticky for the room) so "possessed /
    /// equipped at any point during the run" is what gets recorded. Mirrors
    /// the DebugModBridge cheat poll structurally, but tags runs instead of
    /// cancelling them. See <see cref="ModifierMask"/> for bit semantics.
    /// </summary>
    public static class ModifierTracker
    {
        private static readonly ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("ModifierTracker");

        /// <summary>Mask accumulated for the room run in progress. Valid while
        /// RoomTracker.IsRecording and, for the just-finished run, during
        /// OnRoomExit handling (Reset happens on the next room enter).</summary>
        public static int CurrentMask { get; private set; }

#if SILKSONG_BUILD
        // One-time diagnostics so the Silksong crest table can be finalized
        // from in-game testing without spamming the log.
        private static readonly HashSet<string> _reportedUnmappedCrests =
            new HashSet<string>();

        // Cached names of the currently equipped tools (ToolItem asset
        // codenames, e.g. "Sprintmaster" for Silkspeed Anklets). Refreshed at
        // most every 0.5s; the whole set is logged at Info whenever it
        // changes so the ModifierRegistry alias tables can be verified and
        // finalized directly from the log.
        private static readonly HashSet<string> _equippedToolNames =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        private static float _toolsRefreshedAt = -1f;
        private static string _lastLoggedToolSet = "";
        private const float ToolRefreshInterval = 0.5f;
#endif

        public static void Reset()
        {
            CurrentMask = 0;
        }

        public static void Poll()
        {
            foreach (var def in ModifierRegistry.All)
            {
                int bit = 1 << def.Bit;
                if ((CurrentMask & bit) != 0) continue; // already sticky
                try
                {
                    if (def.Probe()) CurrentMask |= bit;
                }
                catch
                {
                    // Game object torn down mid-read (scene load, etc.) -
                    // retry next frame.
                }
            }

#if SILKSONG_BUILD
            PollSilksongDiagnostics();
#endif
        }

#if SILKSONG_BUILD
        /// <summary>
        /// True when any of the given tool-name aliases is currently
        /// equipped. Primary source is the enumerated equipped-tool set
        /// (matches whatever ToolItem.name actually is, case-insensitively);
        /// ToolItemManager.IsToolEquipped is tried as a fallback in case the
        /// enumeration misses a slot type. Called by the registry tool
        /// probes.
        /// </summary>
        internal static bool IsSilksongToolEquipped(string[] aliases)
        {
            RefreshEquippedTools();

            foreach (string alias in aliases)
                if (_equippedToolNames.Contains(alias))
                    return true;

            foreach (string alias in aliases)
            {
                try
                {
                    if (ToolItemManager.IsToolEquipped(alias)) return true;
                }
                catch
                {
                    // Unknown tool name / manager not ready - ignore.
                }
            }

            return false;
        }

        private static void RefreshEquippedTools()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_toolsRefreshedAt >= 0f
                && now - _toolsRefreshedAt < ToolRefreshInterval)
                return;
            _toolsRefreshedAt = now;

            try
            {
                _equippedToolNames.Clear();
                foreach (ToolItem t in ToolItemManager.GetCurrentEquippedTools())
                    if (t != null && !string.IsNullOrEmpty(t.name))
                        _equippedToolNames.Add(t.name);

                // Log the set whenever it changes - this is how the
                // ModifierRegistry tool alias table gets verified/finalized.
                var sorted = new List<string>(_equippedToolNames);
                sorted.Sort(System.StringComparer.OrdinalIgnoreCase);
                string joined = sorted.Count > 0
                    ? string.Join(", ", sorted.ToArray()) : "(none)";
                if (joined != _lastLoggedToolSet)
                {
                    _lastLoggedToolSet = joined;
                    Log.LogInfo("[ModifierTracker] Equipped tools: " + joined);
                }
            }
            catch
            {
                // Manager not ready (menus, scene loads) - keep the last set.
            }
        }

        private static void PollSilksongDiagnostics()
        {
            try
            {
                // Surface crest ids missing from the registry table exactly once.
                string? crestId = PlayerData.instance?.CurrentCrestID;
                if (!string.IsNullOrEmpty(crestId))
                {
                    ModifierRegistry.CrestBitFor(crestId, out bool unmapped);
                    if (unmapped && _reportedUnmappedCrests.Add(crestId!))
                        Log.LogWarning(
                            $"[ModifierTracker] Unmapped crest id '{crestId}' - add it to ModifierRegistry.CrestIdToBit");
                }
            }
            catch
            {
                // Diagnostics only - never let them affect the run.
            }
        }
#endif
    }
}
