using System;
using System.Collections.Generic;
using System.Text;

namespace ReplayTimerMod
{
    /// <summary>
    /// Helpers for the per-run modifier bitmask: which movement abilities were
    /// unlocked / items equipped at any point during a recorded room run
    /// (possession/loadout semantics - NOT usage).
    ///
    /// ── Bit assignments are STABLE FOREVER ─────────────────────────────────
    /// Masks are persisted in local scene JSON, inside RTM3 replay blobs (and
    /// therefore share codes), and in the server `runs.modifiers` column.
    /// Never reuse or renumber a bit. The game tag always accompanies a mask,
    /// so the game-specific ranges (bit 8+) may overlap between games.
    ///
    ///   bits 0-2   shared    dash, walljump, doublejump
    ///   bits 3-7   shared    reserved for future cross-game concepts
    ///   bits 8-10  Silksong  brolly, harpoon dash, silk soar
    ///   bit  11    Silksong  RETIRED (was sprint-usage; sprinting is part of
    ///                        Swift Step = bit 0 - never reuse this bit, old
    ///                        masks may still carry it and display ignores it)
    ///   bits 12-19 Silksong  crest block (one bit per crest)
    ///   bits 20-27 Silksong  movement-tool block
    ///   bits 8-13  HK        shadow dash, super dash, acid, charms 16/31/37
    ///   bit  31    never assigned (sign bit - keeps -1 unambiguous)
    ///
    /// New shared concepts take bits 3-7; new game-specific modifiers append
    /// upward inside their game's space. Mirror any change in
    /// supabase/schema.md ("Modifier bitmask" section).
    /// </summary>
    public static class ModifierMask
    {
        /// <summary>Sentinel for runs recorded before this feature existed
        /// (old local files, old share blobs). Never uploaded.</summary>
        public const int Unknown = -1;

        public static bool IsKnown(int mask) => mask >= 0;

        /// <summary>True when a known mask satisfies a require/exclude filter:
        /// every `require` bit set, no `exclude` bit set. Unknown masks never
        /// pass (callers show them only when no filter is active).</summary>
        public static bool Passes(int mask, int require, int exclude) =>
            IsKnown(mask) && (mask & require) == require && (mask & exclude) == 0;

        /// <summary>Comma-joined full names of the set bits in bit order,
        /// truncated to <paramref name="maxNames"/> names plus "+n".
        /// Unknown → "?", zero → "". Used for log lines; the UI shows
        /// difference pills + hover tooltips instead.</summary>
        public static string ToBadge(int mask, int maxNames = 4)
        {
            if (!IsKnown(mask)) return "?";
            if (mask == 0) return "";

            var sb = new StringBuilder();
            int shown = 0, extra = 0;
            foreach (var def in ModifierRegistry.All)
            {
                if ((mask & (1 << def.Bit)) == 0) continue;
                if (shown >= maxNames) { extra++; continue; }
                if (shown > 0) sb.Append(", ");
                sb.Append(def.DisplayName);
                shown++;
            }
            if (extra > 0) sb.Append(" +").Append(extra);
            return sb.ToString();
        }

        /// <summary>Full-name description for hover tooltips: one bullet line
        /// per set bit, with explicit text for unknown/empty masks.</summary>
        public static string ToTooltip(int mask)
        {
            if (!IsKnown(mask))
                return "Modifiers unknown\n(old recording)";
            if (mask == 0)
                return "No modifiers";

            var sb = new StringBuilder("Modifiers:");
            foreach (var def in ModifierRegistry.All)
            {
                if ((mask & (1 << def.Bit)) == 0) continue;
                sb.Append("\n- ").Append(def.DisplayName);
            }
            return sb.ToString();
        }
    }

    /// <summary>How a modifier's bit gets set (informational - the probe does
    /// the actual work either way).</summary>
    public enum ModifierDetection
    {
        /// <summary>PlayerData unlock flag (hasDash, ...).</summary>
        PossessionFlag,
        /// <summary>Item equipped (charm / crest / tool).</summary>
        EquippedCheck,
    }

    public sealed class ModifierDef
    {
        /// <summary>Stable bit index, 0-30. See the assignment table on
        /// <see cref="ModifierMask"/>.</summary>
        public readonly int Bit;
        /// <summary>Stable machine id (e.g. "hk_dashmaster").</summary>
        public readonly string Id;
        /// <summary>The in-game item/ability name - the ONLY name shown in
        /// the UI (pills, filter rows, summaries, tooltips).</summary>
        public readonly string DisplayName;
        public readonly ModifierDetection Kind;
        /// <summary>True for the mutually-exclusive Silksong crest bits -
        /// the UI treats the crest as one single-valued attribute (a crest
        /// selector in the filter panel, a crest-name pill on rows) instead
        /// of eight independent toggles.</summary>
        public readonly bool IsCrest;
        /// <summary>Returns true when the modifier is active this frame. Must
        /// tolerate being called at any point in the game lifecycle (probes
        /// are additionally wrapped in try/catch by the tracker).</summary>
        public readonly Func<bool> Probe;

        public ModifierDef(int bit, string id, string displayName,
            ModifierDetection kind, Func<bool> probe, bool isCrest = false)
        {
            Bit = bit;
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            Probe = probe;
            IsCrest = isCrest;
        }
    }

    /// <summary>
    /// The per-game modifier table. Built once; order is bit order (ascending)
    /// so badge strings render consistently.
    /// </summary>
    public static class ModifierRegistry
    {
        public static readonly ModifierDef[] All;

        /// <summary>OR of every bit defined for this game build.</summary>
        public static readonly int KnownBitsMask;

        /// <summary>OR of the mutually-exclusive crest bits (0 when this game
        /// has no crests). The UI treats these as one single-valued
        /// attribute.</summary>
        public static readonly int CrestBitsMask;

#if SILKSONG_BUILD
        // Crest identity block (bits 12-19). PlayerData.CurrentCrestID holds
        // the internal crest id; the Hunter crest's upgrades keep the family
        // on one bit. Ids below were extracted from the game assembly; any
        // unmapped id encountered at runtime is logged once by
        // ModifierTracker so the table can be finalized from in-game testing.
        private static readonly KeyValuePair<string, int>[] CrestIdToBit =
        {
            new KeyValuePair<string, int>("Hunter", 12),
            new KeyValuePair<string, int>("Hunter_v2", 12),
            new KeyValuePair<string, int>("Hunter_v3", 12),
            new KeyValuePair<string, int>("Reaper", 13),
            new KeyValuePair<string, int>("Wanderer", 14),
            new KeyValuePair<string, int>("Warrior", 15),   // Beast crest
            new KeyValuePair<string, int>("Witch", 16),
            new KeyValuePair<string, int>("Toolmaster", 17), // Architect crest
            new KeyValuePair<string, int>("Spell", 18),      // Shaman crest
            new KeyValuePair<string, int>("Cursed", 19),
        };

        /// <summary>Maps the current crest id to its bit, or -1 (also -1 for
        /// null/empty). Unmapped ids are surfaced via <paramref name="unmapped"/>
        /// so the tracker can log them once.</summary>
        internal static int CrestBitFor(string? crestId, out bool unmapped)
        {
            unmapped = false;
            if (string.IsNullOrEmpty(crestId)) return -1;
            foreach (var kv in CrestIdToBit)
                if (kv.Key == crestId) return kv.Value;
            unmapped = true;
            return -1;
        }

        // Movement-affecting tools (bits 20-27), matched case-insensitively
        // against the names of the ACTUALLY EQUIPPED tools
        // (ToolItemManager.GetCurrentEquippedTools), with IsToolEquipped as a
        // fallback. Each entry lists every known alias because the ToolItem
        // asset name is a codename, not the display name - e.g. Silkspeed
        // Anklets' asset is "Sprintmaster" (confirmed by the Sprintmaster_Cave
        // scene; Team Cherry reuses HK charm codenames). ModifierTracker logs
        // the equipped tool names whenever the set changes, so wrong/missing
        // aliases surface immediately in the log. TODO(user): finalize the
        // tool list from in-game testing; bits 21-27 stay reserved.
        internal sealed class ToolBitDef
        {
            public readonly int Bit;
            public readonly string[] Aliases;
            public ToolBitDef(int bit, string[] aliases)
            {
                Bit = bit;
                Aliases = aliases;
            }
        }

        private static readonly ToolBitDef SilkspeedAnklets =
            new ToolBitDef(20, new[] { "Sprintmaster", "Silkspeed Anklets" });
#endif

        static ModifierRegistry()
        {
            var defs = new List<ModifierDef>
            {
#if SILKSONG_BUILD
                new ModifierDef(0, "dash", "Swift Step",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasDash),
                new ModifierDef(1, "walljump", "Cling Grip",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasWalljump),
                new ModifierDef(2, "doublejump", "Faydown Cloak",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasDoubleJump),
                new ModifierDef(8, "ss_brolly", "Drifter's Cloak",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasBrolly),
                new ModifierDef(9, "ss_harpoon", "Clawline",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasHarpoonDash),
                new ModifierDef(10, "ss_silksoar", "Silk Soar",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasSuperJump),
                // Bit 11 is RETIRED: it was "ss_sprint" (usage-tagged
                // isSprinting), but sprinting is just Swift Step (bit 0) in
                // use, not a separate ability. Old masks may still have it
                // set; unregistered bits are ignored everywhere.
                // Crest bits (12-19): one def per bit for display purposes;
                // detection runs through the CrestIdToBit table above. Badges
                // show the crest's plain name.
                new ModifierDef(12, "ss_crest_hunter", "Hunter Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(12), isCrest: true),
                new ModifierDef(13, "ss_crest_reaper", "Reaper Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(13), isCrest: true),
                new ModifierDef(14, "ss_crest_wanderer", "Wanderer Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(14), isCrest: true),
                new ModifierDef(15, "ss_crest_beast", "Beast Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(15), isCrest: true),
                new ModifierDef(16, "ss_crest_witch", "Witch Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(16), isCrest: true),
                new ModifierDef(17, "ss_crest_architect", "Architect Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(17), isCrest: true),
                new ModifierDef(18, "ss_crest_shaman", "Shaman Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(18), isCrest: true),
                new ModifierDef(19, "ss_crest_cursed", "Cursed Crest",
                    ModifierDetection.EquippedCheck, CrestProbe(19), isCrest: true),
                new ModifierDef(20, "ss_tool_silkspeed", "Silkspeed Anklets",
                    ModifierDetection.EquippedCheck, ToolProbe(SilkspeedAnklets)),
#endif
#if HOLLOW_KNIGHT_BUILD
                new ModifierDef(0, "dash", "Mothwing Cloak",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasDash),
                new ModifierDef(1, "walljump", "Mantis Claw",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasWalljump),
                new ModifierDef(2, "doublejump", "Monarch Wings",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasDoubleJump),
                new ModifierDef(8, "hk_shadowdash", "Shade Cloak",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasShadowDash),
                new ModifierDef(9, "hk_superdash", "Crystal Heart",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasSuperDash),
                new ModifierDef(10, "hk_acid", "Isma's Tear",
                    ModifierDetection.PossessionFlag,
                    () => PlayerData.instance != null && PlayerData.instance.hasAcidArmour),
                new ModifierDef(11, "hk_sharpshadow", "Sharp Shadow",
                    ModifierDetection.EquippedCheck,
                    () => PlayerData.instance != null && PlayerData.instance.equippedCharm_16),
                new ModifierDef(12, "hk_dashmaster", "Dashmaster",
                    ModifierDetection.EquippedCheck,
                    () => PlayerData.instance != null && PlayerData.instance.equippedCharm_31),
                new ModifierDef(13, "hk_sprintmaster", "Sprintmaster",
                    ModifierDetection.EquippedCheck,
                    () => PlayerData.instance != null && PlayerData.instance.equippedCharm_37),
#endif
            };

            defs.Sort((a, b) => a.Bit.CompareTo(b.Bit));
            All = defs.ToArray();

            int known = 0, crests = 0;
            foreach (var d in All)
            {
                known |= 1 << d.Bit;
                if (d.IsCrest) crests |= 1 << d.Bit;
            }
            KnownBitsMask = known;
            CrestBitsMask = crests;
        }

#if SILKSONG_BUILD
        private static Func<bool> CrestProbe(int bit) => () =>
        {
            var pd = PlayerData.instance;
            if (pd == null) return false;
            return CrestBitFor(pd.CurrentCrestID, out _) == bit;
        };

        private static Func<bool> ToolProbe(ToolBitDef tool) => () =>
            ModifierTracker.IsSilksongToolEquipped(tool.Aliases);
#endif
    }
}
