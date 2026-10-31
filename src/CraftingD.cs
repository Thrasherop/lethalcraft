using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244 part D) Minecraft 26.3's recipes the mod lacked: storage blocks both ways, the bone, magma and nether wart
    /// blocks, gold from nuggets, copper from the waxed block, polished basalt, the sandstone and quartz shapes from their cut
    /// and chiseled kinds, TNT from red sand, carpets, stained glass and panes, beds in every colour, packed and blue ice,
    /// snow layers, coarse dirt, the lanterns and soul torch, the name tag; charcoal; the smelting and fuels it lacked.
    /// </summary>
    public static partial class Crafting
    {
        static Recipe Any(Recipe r, char sym, params string[] any)
        {
            if (r.Alts == null) r.Alts = new Dictionary<char, string[]>();
            r.Alts[sym] = any;
            return r;
        }

        static void ThoroughDRecipes()
        {
            const string B = "Building", M = "Materials";
            string[] Nine = { "###", "###", "###" };
            // (#66) storage blocks
            S(M, "lapis_block", 1, Nine, ('#', "lapis_lazuli")); L(M, "lapis_lazuli", 9, "lapis_block");
            S(M, "emerald_block", 1, Nine, ('#', WanderingTraderAI.Emerald)); L(M, WanderingTraderAI.Emerald, 9, "emerald_block");
            S(M, "netherite_block", 1, Nine, ('#', "netherite_ingot")); L(M, "netherite_ingot", 9, "netherite_block");
            S(B, "bone_block", 1, Nine, ('#', "bone_meal"));
            S(B, "magma", 1, new[] { "##", "##" }, ('#', "magma_cream"));
            L(B, "nether_wart_block", 1, Enumerable.Repeat("nether_wart", 9).ToArray());
            S(M, "gold_ingot", 1, Nine, ('#', "gold_nugget"));
            L(M, "copper_ingot", 9, "waxed_copper_block");
            // (#70) polished basalt; sandstone and quartz shapes from their cut and chiseled kinds; TNT from red sand
            S(B, "polished_basalt", 4, new[] { "##", "##" }, ('#', "basalt"));
            string[] stairs = { "#  ", "## ", "###" };
            Recipes.Add(Any(Recipe.Shaped(B, "sandstone_stairs", 4, stairs, ('#', "chiseled_sandstone")), '#', "chiseled_sandstone", "cut_sandstone"));
            Recipes.Add(Any(Recipe.Shaped(B, "red_sandstone_stairs", 4, stairs, ('#', "chiseled_red_sandstone")), '#', "chiseled_red_sandstone", "cut_red_sandstone"));
            S(B, "sandstone_slab", 6, new[] { "###" }, ('#', "chiseled_sandstone"));
            S(B, "red_sandstone_slab", 6, new[] { "###" }, ('#', "chiseled_red_sandstone"));
            Recipes.Add(Any(Recipe.Shaped(B, "quartz_stairs", 4, stairs, ('#', "chiseled_quartz_block")), '#', "chiseled_quartz_block", "quartz_pillar"));
            Recipes.Add(Any(Recipe.Shaped(B, "quartz_slab", 6, new[] { "###" }, ('#', "chiseled_quartz_block")), '#', "chiseled_quartz_block", "quartz_pillar"));
            S("Redstone", "tnt", 1, new[] { "gsg", "sgs", "gsg" }, ('g', "gunpowder"), ('s', "red_sand"));
            // (#71) the sixteen colours
            foreach (var c in Dyes.Colors)
            {
                string dye = c + "_dye";
                S(B, c + "_carpet", 3, new[] { "##" }, ('#', "wool_" + c));
                S(B, c + "_stained_glass", 8, new[] { "###", "#d#", "###" }, ('#', "glass"), ('d', dye));
                S(B, c + "_stained_glass_pane", 16, new[] { "###", "###" }, ('#', c + "_stained_glass"));
                S(B, c + "_stained_glass_pane", 8, new[] { "###", "#d#", "###" }, ('#', "glass_pane"), ('d', dye));
                if (c != "red") S(B, c + "_bed", 1, new[] { "www", "ppp" }, ('w', "wool_" + c), ('p', "oak_planks"));
            }
            // (#72) ice and snow; (#73) coarse dirt
            L(B, "packed_ice", 1, Enumerable.Repeat("ice", 9).ToArray());
            L(B, "blue_ice", 1, Enumerable.Repeat("packed_ice", 9).ToArray());
            S(B, "snow", 6, new[] { "###" }, ('#', "snow_block"));
            S(B, "coarse_dirt", 4, new[] { "dg", "gd" }, ('d', "dirt"), ('g', "gravel"));
            // (#76) the lanterns and the soul torch (coal or charcoal, on soul sand or soil)
            S("Basics", "lantern", 1, new[] { "nnn", "ntn", "nnn" }, ('n', "iron_nugget"), ('t', "torch"));
            S("Basics", "soul_lantern", 1, new[] { "nnn", "ntn", "nnn" }, ('n', "iron_nugget"), ('t', "soul_torch"));
            var st = Recipe.Shaped("Basics", "soul_torch", 4, new[] { "c", "|", "s" }, ('c', "coal"), ('|', "stick"), ('s', "soul_sand"));
            Any(st, 'c', "coal", "charcoal"); Any(st, 's', "soul_sand", "soul_soil");
            Recipes.Add(st);
            // (#68) torches take charcoal too
            foreach (var r in Recipes.Where(x => x.Result == "torch" && x.Map != null && x.Map.ContainsValue("coal")).ToList())
                Any(r, r.Map.First(kv => kv.Value == "coal").Key, "coal", "charcoal");
            // (#83) the name tag: paper and a metal nugget (Minecraft 1.21's)
            Recipes.Add(Any(Recipe.Shaped(M, NameTags.Key, 1, new[] { "n", "p" }, ('n', "iron_nugget"), ('p', "paper")), 'n', "iron_nugget", "gold_nugget"));

            // (#67) smelting: basalt smooth; iron and gold tools and armor to nuggets; ore blocks (Silk Touch's) to theirs
            SmeltResult["basalt"] = "smooth_basalt";
            foreach (var t in new[] { "pickaxe", "shovel", "axe", "hoe", "sword", "helmet", "chestplate", "leggings", "boots", "horse_armor" })
            {
                SmeltResult["iron_" + t] = "iron_nugget";
                SmeltResult["golden_" + t] = "gold_nugget";
                if (t != "horse_armor") SmeltResult["copper_" + t] = "copper_nugget"; // (#244, Minecraft 1.21.9's)
                if (Armor.Get("chainmail_" + t) != null) SmeltResult["chainmail_" + t] = "iron_nugget";
            }
            foreach (var (ore, to) in new[] { ("coal_ore", "coal"), ("lapis_ore", "lapis_lazuli"), ("redstone_ore", "redstone_dust"), ("copper_ore", "copper_ingot"), ("diamond_ore", "diamond"), ("emerald_ore", WanderingTraderAI.Emerald), ("ancient_debris", "netherite_scrap") })
            {
                SmeltResult[ore] = to;
                if (Blocks.Get("deepslate_" + ore) != null) SmeltResult["deepslate_" + ore] = to;
            }
            SmeltResult["deepslate_iron_ore"] = "iron_ingot"; SmeltResult["deepslate_gold_ore"] = "gold_ingot";
            // (#69) fuels, Minecraft's burn times (its ticks / 50: a coal's 1600 is our 32 s, eight items)
            FuelSeconds["charcoal"] = 32f; FuelSeconds["lava_bucket"] = 400f; FuelSeconds["blaze_rod"] = 48f;
            foreach (var k in new[] { "chest", "barrel", "ladder", "jukebox", "note_block", "lectern", "composter", "loom", "smithing_table", "bow", "crossbow", "fishing_rod" })
                FuelSeconds[k] = 6f;
            foreach (var c in Dyes.Colors) { FuelSeconds[c + "_banner"] = 6f; FuelSeconds["wool_" + c] = 2f; FuelSeconds[c + "_carpet"] = 1.34f; }
            foreach (var k in new[] { "wooden_pickaxe", "wooden_shovel", "wooden_axe", "wooden_hoe", "wooden_sword" }) FuelSeconds[k] = 4f;
            foreach (var w in Blocks.Woods) if (w.Sapling != null) FuelSeconds[w.Sapling.Key] = 2f;
        }
    }
}
