using System.Collections.Generic;

namespace ReplayTimerMod
{
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

        public static string For(DebugAbilityKind kind) =>
            ByKind.TryGetValue(kind, out var msg) ? msg : Default;
    }
}
