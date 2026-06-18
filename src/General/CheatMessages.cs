using System.Collections.Generic;

namespace ReplayTimerMod
{
    // ============================================================================
    //  EDIT ME
    //
    //  This is the one place to customize the little "run cancelled" banner
    //  text that pops up in the timer corner when a DebugMod cheat is used.
    //  Every entry below is just a plain string - change them to whatever you
    //  like (in-jokes, memes, whatever). Nothing else needs to change.
    //
    //  - "Default" is shown if a cheat is detected that isn't listed below,
    //    or if DebugMod added a new ability ReplayTimerMod doesn't know about.
    //  - Each DebugAbilityKind entry overrides Default for that specific cheat.
    //  - Messages are shown for a few seconds and then disappear automatically -
    //    keep them short so they fit next to the timer.
    // ============================================================================
    public static class CheatMessages
    {
        public const string Default = "Run cancelled - debug tools detected";

        public static readonly Dictionary<DebugAbilityKind, string> ByKind =
            new Dictionary<DebugAbilityKind, string>
            {
                { DebugAbilityKind.Noclip,               "Run cancelled (noclip)" },
                { DebugAbilityKind.Invincible,           "Run cancelled (invincibility)" },
                { DebugAbilityKind.InfiniteHP,           "Run cancelled (inf hp)" },
                { DebugAbilityKind.InfiniteSilk,         "Run cancelled (inf silk)" },
                { DebugAbilityKind.InfiniteTools,        "Run cancelled (inf tools)" },
                { DebugAbilityKind.InfiniteJump,         "Run cancelled (inf jump)" },
                { DebugAbilityKind.HeroColliderDisabled, "Run cancelled (collider disabled)" },
                { DebugAbilityKind.TimeFrozen,           "Run cancelled (timescale)" },
                { DebugAbilityKind.TimeScale,            "Run cancelled (timescale)" },
            };

        /// <summary>Looks up the banner text for a given cheat, falling back to <see cref="Default"/>.</summary>
        public static string For(DebugAbilityKind kind) =>
            ByKind.TryGetValue(kind, out var msg) ? msg : Default;
    }
}