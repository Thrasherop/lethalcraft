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
            ["crossbow"] = "crafting, pillagers (rare), outpost chests", ["ominous_banner"] = "pillager captains, outposts",
            ["experience_bottle"] = "outpost chests", ["goat_horn_ponder"] = "outpost chests", ["goat_horn_sing"] = "outpost chests", ["goat_horn_seek"] = "outpost chests", ["goat_horn_feel"] = "outpost chests",
            ["sentry_armor_trim_smithing_template"] = "outpost chests",
            ["ominous_bottle_1"] = "pillager captains", ["ominous_bottle_2"] = "pillager captains", ["ominous_bottle_3"] = "pillager captains", ["ominous_bottle_4"] = "pillager captains", ["ominous_bottle_5"] = "pillager captains", // (#223)
            ["rotten_flesh"] = "zombies", ["iron_ingot"] = "zombies (rare), smelting ore", ["carrot"] = "zombies (rare), farming", ["potato"] = "zombies (rare), farming",
            ["gunpowder"] = "creepers, ghasts", ["bone"] = "skeletons, Nether loot, fishing", ["string"] = "spiders, fishing", ["ender_pearl"] = "endermen, Nether loot",
            ["blaze_rod"] = "blazes, Nether loot", ["wither_skeleton_skull"] = "wither skeletons, Nether loot",
            ["carved_pumpkin"] = "shears on a pumpkin", ["lava_bucket"] = "a bucket on lava", ["water_bucket"] = "a bucket on water", ["potion_water"] = "a glass bottle at water", ["gold_ingot"] = "piglin barter, Nether loot, smelting",
            ["nether_wart"] = "Nether loot, farming", ["magma_cream"] = "magma cubes", ["slime_ball"] = "slimes, the store", ["raw_porkchop"] = "pigs", ["feather"] = "chickens, parrots", ["honeycomb"] = "shears on the Circuit Bees' hive", ["raw_mutton"] = "sheep", ["raw_rabbit"] = "rabbits", ["rabbit_hide"] = "rabbits", ["rabbit_foot"] = "rabbits (1 in 10)", ["ink_sac"] = "squid", ["phantom_membrane"] = "phantoms", ["iron_horse_armor"] = "Nether loot", ["golden_horse_armor"] = "Nether loot", ["diamond_horse_armor"] = "Nether loot", ["spider_eye"] = "spiders, witches", ["skeleton_skull"] = "charged creepers", ["zombie_head"] = "charged creepers", ["creeper_head"] = "charged creepers", ["piglin_head"] = "charged creepers", ["arrow_slowness"] = "strays", ["arrow_poison"] = "bogged", ["ghast_tear"] = "ghasts", ["golden_axe"] = "crafting, piglin brutes", ["raw_chicken"] = "chickens", ["arrow"] = "crafting, skeletons (rare)", ["raw_beef"] = "cows",
            ["leather"] = "cows, horses, fishing", ["saddle"] = "crafting, fishing treasure", ["wheat"] = "farming", ["wheat_seeds"] = "grass, farming",
            ["pumpkin_seeds"] = "pumpkins", ["sand"] = "ground", ["dirt"] = "ground", ["cobblestone"] = "stone", ["coal"] = "coal ore",
            ["iron_nugget"] = "crafting (an iron ingot)",
            ["oak_sapling"] = "chopped trees, leaves, the store", ["birch_sapling"] = "leaves, the store", ["dark_oak_sapling"] = "chopped trees (Dine), leaves, the store", ["mangrove_propagule"] = "chopped trees (March), mangroves, the store",
            ["birch_log"] = "the store, grown trees", ["dark_oak_log"] = "chopped trees (Dine), the store, grown trees", ["mangrove_log"] = "chopped trees (March), the store, grown trees",
            ["birch_leaves"] = "shears on grown trees", ["dark_oak_leaves"] = "shears on grown trees", ["mangrove_leaves"] = "shears on grown trees", ["mangrove_roots"] = "grown mangroves", ["mud"] = "a water bottle on dirt",
            ["oak_log"] = "trees", ["leaves"] = "trees", ["apple"] = "trees, the store", ["sugar_cane"] = "farming", ["pumpkin"] = "farming",
            ["obsidian"] = "water on lava", ["netherite_scrap"] = "ancient debris (smelted)", ["quartz"] = "nether quartz ore",
            ["lapis_lazuli"] = "lapis ore", ["redstone_dust"] = "redstone ore", ["glowstone"] = "the Nether", ["music_disc"] = "facility scrap",
            ["raw_cod"] = "fishing", ["raw_salmon"] = "fishing", ["pufferfish"] = "fishing", ["tropical_fish"] = "fishing",
            ["golden_apple"] = "the store, fishing treasure", ["totem_of_undying"] = "the store",
            ["vine"] = "the moons' trees (shears)", [Smithing.TemplateKey] = "bastion loot", [Books.Written] = "signing a book and quill", ["granite"] = "the ground", ["diorite"] = "the ground", ["andesite"] = "the ground",
        };

        /// <summary>Deliberately not had the normal way (creative or dev only), as in Minecraft: ores and nylium need silk
        /// touch there; bedrock, fluids as blocks and spawn eggs are creative's.</summary>
        static readonly HashSet<string> CreativeOnly = new HashSet<string>
        {
            "bedrock", "lava", "water", "spawn_egg", "coal_ore", "lapis_ore", "redstone_ore", "nether_quartz_ore", "crimson_nylium", "warped_nylium", "gilded_blackstone",
            "copper_ore", "deepslate_copper_ore", "deepslate_coal_ore", "deepslate_iron_ore", "deepslate_gold_ore", "deepslate_diamond_ore", "deepslate_emerald_ore", "deepslate_redstone_ore", "deepslate_lapis_ore", // (silk touch's, as the stone ores)
        };

        public static string Report(bool all = false)
        {
            var src = new Dictionary<string, List<string>>();
            void Add(string k, string how) { if (string.IsNullOrEmpty(k)) return; k = ItemData.Base(k) ?? k; if (!src.TryGetValue(k, out var l)) src[k] = l = new List<string>(); if (!l.Contains(how)) l.Add(how); }
            foreach (var k in Balance.ShopItems.Keys) Add(k, "store");
            foreach (var kv in Declared) Add(kv.Key, kv.Value);
            foreach (var k in ModItems.ByKey.Keys) if (k.StartsWith("music_disc_")) Add(k, "facility scrap");
            foreach (var kv in Crafting.SmeltResult) Add(kv.Value, "furnace(" + kv.Key + ")");
            foreach (var b in Blocks.All) if (Copper.StageOf(b) > 0) Add(b.Key, "weathering (copper left out)"); // (#160)
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
                                       Blocks.Glowstone, Blocks.NetherQuartzOre, Blocks.Log, Blocks.Gravel, Blocks.Deepslate, Blocks.CopperOre })
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
                // (#159: the smithing table's upgrades)
                if (src.ContainsKey(Smithing.TemplateKey) && src.ContainsKey(Smithing.Addition))
                    foreach (var kv in Smithing.Upgrades)
                        if (src.ContainsKey(kv.Key) && !src.ContainsKey(kv.Value)) { Add(kv.Value, "smithing"); changed = true; }
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
            var nether = new HashSet<EnemyType> { ZombifiedPiglinAI.Type, HoglinAI.Type, BlazeAI.Type, WitherSkeletonAI.Type, PiglinAI.Type, PiglinBruteAI.Type, GhastAI.Type, MagmaCubeAI.Big, SkeletonAI.Type, EndermanAI.Type };
            var other = new Dictionary<EnemyType, string>();
            if (MagmaCubeAI.Medium != null) other[MagmaCubeAI.Medium] = "a big magma cube's split";
            if (MagmaCubeAI.Small != null) other[MagmaCubeAI.Small] = "a medium magma cube's split";
            if (SlimeAI.Medium != null) other[SlimeAI.Medium] = "a big slime's split";
            foreach (var (t, how) in new[] { (HuskAI.Type, "a zombie on a desert moon"), (DrownedAI.Type, "a zombie on a swampy moon"), (BabyZombieAI.BabyType, "a zombie, 1 in 20"),
                     (HuskAI.BabyType, "a husk, 1 in 20"), (DrownedAI.BabyType, "a drowned, 1 in 20"), (ChickenJockeyAI.Type, "a baby zombie, 1 in 20"),
                     (StrayAI.Type, "a skeleton on a snowy moon"), (BoggedAI.Type, "a skeleton on a swampy moon"), (SpiderJockeyAI.Type, "a spider, 1 in 100") })
                if (t != null) other[t] = how;
            if (EndermiteAI.Type != null) other[EndermiteAI.Type] = "ender pearls, 1 in 20";
            if (SquidAI.Type != null) other[SquidAI.Type] = "the moons' water, at landing";
            if (IronGolemAI.Type != null) other[IronGolemAI.Type] = "built: iron blocks and a pumpkin";
            if (SnowGolemAI.Type != null) other[SnowGolemAI.Type] = "built: snow blocks and a pumpkin";
            if (PhantomAI.Type != null) other[PhantomAI.Type] = "after 6 PM, outside";
            // (#160, #152: carts are placed from their items on a rail; the trader comes with its llamas)
            foreach (var (t, key) in new[] { (MinecartAI.Type, "minecart"), (MinecartAI.ChestType, "chest_minecart"), (MinecartAI.HopperType, "hopper_minecart"),
                     (MinecartAI.TntType, "tnt_minecart"), (MinecartAI.FurnaceType, "furnace_minecart") })
                if (t != null) other[t] = "placed on a rail (" + key + ")";
            if (WanderingTraderAI.Type != null) other[WanderingTraderAI.Type] = "near the ship, about 1 moon in 12";
            if (LlamaAI.Type != null) other[LlamaAI.Type] = "with the wandering trader";
            foreach (var kv in AnimalCare.BabyOf) other[kv.Value] = "breeding two " + kv.Key.enemyName.ToLower() + "s";
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
