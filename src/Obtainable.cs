using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (dev) The owner's rule (2026-10-12, CLAUDE.md): everything added must be obtainable the normal way. This audits it:
    /// every item's sources (the store, a recipe whose ingredients are themselves obtainable, the furnace, a block's drop,
    /// the ground, fishing, and the drops and loot declared below), and every one of this mod's mobs against the moons'
    /// spawn lists and what spawns them otherwise. What it flags is either fixed or deliberately dev-only.
    /// Keep <see cref="Declared"/> in step with the code that spawns drops and loot.
    /// </summary>
    public static class Obtainable
    {
        /// <summary>Sources the code hands out that can't be read off a table: mob drops, loot, trees, plants.</summary>
        static readonly Dictionary<string, string> Declared = new Dictionary<string, string>
        {
            ["rotten_flesh"] = "zombies", ["iron_ingot"] = "zombies (rare), smelting ore", ["carrot"] = "zombies (rare), farming", ["potato"] = "zombies (rare), farming",
            ["gunpowder"] = "creepers, ghasts", ["bone"] = "skeletons, Nether loot, fishing", ["string"] = "spiders, fishing", ["ender_pearl"] = "endermen, Nether loot",
            ["blaze_rod"] = "blazes, Nether loot", ["wither_skeleton_skull"] = "wither skeletons, Nether loot",
            ["carved_pumpkin"] = "shears on a pumpkin", ["lava_bucket"] = "a bucket on lava", ["water_bucket"] = "a bucket on water", ["potion_water"] = "a glass bottle at water", ["gold_ingot"] = "piglin barter, Nether loot, smelting",
            ["nether_wart"] = "Nether loot, farming", ["magma_cream"] = "magma cubes", ["slime_ball"] = "slimes, the store", ["raw_porkchop"] = "pigs", ["feather"] = "chickens", ["raw_chicken"] = "chickens", ["arrow"] = "crafting, skeletons (rare)", ["raw_beef"] = "cows",
            ["leather"] = "cows, horses, fishing", ["saddle"] = "crafting, fishing treasure", ["wheat"] = "farming", ["wheat_seeds"] = "grass, farming",
            ["pumpkin_seeds"] = "pumpkins", ["sand"] = "ground", ["dirt"] = "ground", ["cobblestone"] = "stone", ["coal"] = "coal ore",
            ["oak_log"] = "trees", ["leaves"] = "trees", ["apple"] = "trees, the store", ["sugar_cane"] = "farming", ["pumpkin"] = "farming",
            ["obsidian"] = "water on lava", ["netherite_scrap"] = "ancient debris (smelted)", ["quartz"] = "nether quartz ore",
            ["lapis_lazuli"] = "lapis ore", ["redstone_dust"] = "redstone ore", ["glowstone"] = "the Nether", ["music_disc"] = "facility scrap",
            ["raw_cod"] = "fishing", ["raw_salmon"] = "fishing", ["pufferfish"] = "fishing", ["tropical_fish"] = "fishing",
            ["golden_apple"] = "the store, fishing treasure", ["totem_of_undying"] = "the store",
        };

        /// <summary>Deliberately not had the normal way (creative or dev only), as in Minecraft: ores and nylium need silk
        /// touch there; bedrock, fluids as blocks and spawn eggs are creative's.</summary>
        static readonly HashSet<string> CreativeOnly = new HashSet<string>
        {
            "bedrock", "lava", "water", "spawn_egg", "coal_ore", "lapis_ore", "redstone_ore", "nether_quartz_ore", "crimson_nylium", "warped_nylium", "gilded_blackstone",
        };

        public static string Report(bool all = false)
        {
            var src = new Dictionary<string, List<string>>();
            void Add(string k, string how) { if (string.IsNullOrEmpty(k)) return; k = ItemData.Base(k) ?? k; if (!src.TryGetValue(k, out var l)) src[k] = l = new List<string>(); if (!l.Contains(how)) l.Add(how); }
            foreach (var k in Balance.ShopItems.Keys) Add(k, "store");
            foreach (var kv in Declared) Add(kv.Key, kv.Value);
            foreach (var k in ModItems.ByKey.Keys) if (k.StartsWith("music_disc_")) Add(k, "facility scrap");
            foreach (var kv in Crafting.SmeltResult) Add(kv.Value, "furnace(" + kv.Key + ")");
            // blocks: what the ground is made of, and what blocks drop
            foreach (var b in Blocks.All)
            {
                if (b.DropAs != null) Add(b.DropAs.Key, "breaking " + b.Key);
                if (b.DropKey != null) Add(b.DropKey, "breaking " + b.Key);
                if (b.ChanceDropKey != null) Add(b.ChanceDropKey, "breaking " + b.Key + " (sometimes)");
                if (b.DropsScrap) Add("scrap_" + b.Key, "mining " + b.Key);
            }
            foreach (var b in new[] { Blocks.Grass, Blocks.Dirt, Blocks.Stone, Blocks.Snow, Blocks.StoneBricks, Blocks.Planks, Blocks.Netherrack, Blocks.NetherBricks, Blocks.SoulSand,
                                       Blocks.SoulSoil, Blocks.Basalt, Blocks.Blackstone, Blocks.PolishedBlackstoneBricks, Blocks.GildedBlackstone, Blocks.BoneBlock, Blocks.CrimsonNylium,
                                       Blocks.CrimsonStem, Blocks.WarpedNylium, Blocks.WarpedStem, Blocks.NetherWartBlock, Blocks.WarpedWartBlock, Blocks.Shroomlight, Blocks.Magma,
                                       Blocks.Glowstone, Blocks.NetherQuartzOre, Blocks.Log, Blocks.Gravel })
                if (b != null && b.DropAs == null && b.DropKey == null) Add(b.Key, "the ground");
            // recipes and brews: only once every ingredient is obtainable (to a fixed point)
            var pending = Crafting.Recipes.ToList();
            var brews = Potions.Brews.ToList();
            for (bool changed = true; changed; )
            {
                changed = false;
                foreach (var b in brews.ToList())
                {
                    if (!src.ContainsKey(b.Key.Item1) || !src.ContainsKey(ItemData.Base(b.Key.Item2) ?? b.Key.Item2)) continue;
                    Add(b.Value, "brewing"); brews.Remove(b); changed = true;
                }
                foreach (var r in pending.ToList())
                {
                    if (!r.Needs.All(n => src.ContainsKey(ItemData.Base(n.key) ?? n.key))) continue;
                    Add(r.Result, "crafting"); pending.Remove(r); changed = true;
                }
            }
            var missing = ModItems.ByKey.Keys.Where(k => !src.ContainsKey(k) && !CreativeOnly.Contains(k)).OrderBy(k => k).ToList();
            var blocked = pending.Select(r => $"{r.Result}(needs {string.Join("+", r.Needs.Where(n => !src.ContainsKey(ItemData.Base(n.key) ?? n.key)).Select(n => n.key))})").Distinct().ToList();
            var sb = new System.Text.StringBuilder();
            sb.Append($"items {ModItems.ByKey.Count}: no source for {missing.Count}: [{string.Join(", ", missing)}] | recipes with an unobtainable ingredient: [{string.Join(", ", blocked)}]");
            sb.Append(" | mobs: ").Append(Mobs());
            if (all) sb.Append(" | ").Append(string.Join("; ", src.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + string.Join("/", kv.Value))));
            return sb.ToString();
        }

        /// <summary>This mod's mobs: on how many moons' spawn lists (inside, outside, daytime), or what else spawns them.</summary>
        static string Mobs()
        {
            var nether = new HashSet<EnemyType> { BlazeAI.Type, WitherSkeletonAI.Type, PiglinAI.Type, PiglinBruteAI.Type, GhastAI.Type, MagmaCubeAI.Big, SkeletonAI.Type, EndermanAI.Type };
            var other = new Dictionary<EnemyType, string>();
            if (MagmaCubeAI.Medium != null) other[MagmaCubeAI.Medium] = "a big magma cube's split";
            if (MagmaCubeAI.Small != null) other[MagmaCubeAI.Small] = "a medium magma cube's split";
            if (SlimeAI.Medium != null) other[SlimeAI.Medium] = "a big slime's split";
            if (SlimeAI.Small != null) other[SlimeAI.Small] = "a medium slime's split";
            var levels = StartOfRound.Instance != null ? StartOfRound.Instance.levels : new SelectableLevel[0];
            var ours = Resources.FindObjectsOfTypeAll<EnemyType>().Where(t => t != null && t.name.StartsWith("LMC_")).OrderBy(t => t.name).ToList();
            var parts = new List<string>();
            foreach (var t in ours)
            {
                int Count(System.Func<SelectableLevel, List<SpawnableEnemyWithRarity>> f) => levels.Count(l => l != null && f(l) != null && f(l).Any(e => e.enemyType == t && e.rarity > 0));
                int inside = Count(l => l.Enemies), outside = Count(l => l.OutsideEnemies), day = Count(l => l.DaytimeEnemies);
                var how = new List<string>();
                if (inside > 0) how.Add($"inside on {inside} moons");
                if (outside > 0) how.Add($"outside on {outside}");
                if (day > 0) how.Add($"by day on {day}");
                if (nether.Contains(t)) how.Add("the Nether");
                if (other.TryGetValue(t, out var o)) how.Add(o);
                parts.Add($"{t.enemyName}{(t.name.Contains("Outside") ? " (outside)" : "")}: {(how.Count > 0 ? string.Join(", ", how) : "NOTHING SPAWNS IT")}");
            }
            return string.Join("; ", parts);
        }
    }
}
