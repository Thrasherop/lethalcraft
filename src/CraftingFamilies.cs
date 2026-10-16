using System.Linq;

namespace LethalMinecraft
{
    /// <summary>The block families' recipes and smelting (#154, #156, #157), Minecraft's.</summary>
    public static partial class Crafting
    {
        static void FamilyRecipes()
        {
            const string B = "Building";
            // every family block's stairs (6 -> 4), slab (3 -> 6), wall (6 -> 6) and fence
            foreach (var (block, shaped, shape) in Blocks.FamilyShapes)
            {
                switch (shape)
                {
                    case BlockShape.Stairs: S(B, shaped, 4, new[] { "#  ", "## ", "###" }, ('#', block)); break;
                    case BlockShape.Slab: S(B, shaped, 6, new[] { "###" }, ('#', block)); break;
                    case BlockShape.Wall: S(B, shaped, 6, new[] { "###", "###" }, ('#', block)); break;
                    case BlockShape.Fence:
                        if (block == "nether_bricks") S(B, shaped, 6, new[] { "#n#", "#n#" }, ('#', "nether_bricks"), ('n', "nether_brick"));
                        else S(B, shaped, 3, new[] { "#|#", "#|#" }, ('#', block), ('|', "stick"));
                        break;
                }
            }
            // polished (4 -> 4), cut, bricks
            foreach (var (from, to) in new[] { ("granite", "polished_granite"), ("diorite", "polished_diorite"), ("andesite", "polished_andesite"),
                ("blackstone", "polished_blackstone"), ("polished_blackstone", "polished_blackstone_bricks"), ("sandstone", "cut_sandstone"), ("red_sandstone", "cut_red_sandstone"), ("quartz_block", "quartz_bricks") })
                S(B, to, 4, new[] { "##", "##" }, ('#', from));
            // deepslate (#155): cobbled -> polished -> bricks -> tiles, as Minecraft's
            foreach (var (from, to) in new[] { ("cobbled_deepslate", "polished_deepslate"), ("polished_deepslate", "deepslate_bricks"), ("deepslate_bricks", "deepslate_tiles") })
                S(B, to, 4, new[] { "##", "##" }, ('#', from));
            // granite, diorite and andesite from each other, quartz and cobblestone
            L(B, "granite", 1, "diorite", "quartz");
            S(B, "diorite", 2, new[] { "cq", "qc" }, ('c', "cobblestone"), ('q', "quartz"));
            L(B, "andesite", 2, "diorite", "cobblestone");
            // copper (#160), each stage from its own: blocks of nine ingots, cut, chiseled, grates, bulbs, doors, trapdoors
            L("Materials", "copper_ingot", 9, "copper_block");
            S(B, "copper_block", 1, new[] { "###", "###", "###" }, ('#', "copper_ingot"));
            for (int s = 0; s < 4; s++)
            {
                string pre = Copper.Stages[s], block = s == 0 ? "copper_block" : pre + "copper";
                S(B, pre + "cut_copper", 4, new[] { "##", "##" }, ('#', block));
                S(B, pre + "copper_grate", 4, new[] { " # ", "# #", " # " }, ('#', block));
                S(B, pre + "chiseled_copper", 1, new[] { "#", "#" }, ('#', pre + "cut_copper_slab"));
                S("Redstone", pre + "copper_bulb", 4, new[] { " c ", "cbc", " r " }, ('c', block), ('b', "blaze_rod"), ('r', "redstone_dust"));
            }
            // waxed (honeycomb): any copper block with a honeycomb; and the waxed kinds from each other, like the plain
            foreach (var key in Blocks.All.Where(d => d != null && Copper.Waxable(d)).Select(d => d.Key).ToList())
                L(B, "waxed_" + key, 1, key, "honeycomb");
            for (int s = 0; s < 4; s++)
            {
                string pre = "waxed_" + Copper.Stages[s], block = s == 0 ? "waxed_copper_block" : pre + "copper";
                S(B, pre + "cut_copper", 4, new[] { "##", "##" }, ('#', block));
                S(B, pre + "copper_grate", 4, new[] { " # ", "# #", " # " }, ('#', block));
                S(B, pre + "chiseled_copper", 1, new[] { "#", "#" }, ('#', pre + "cut_copper_slab"));
            }
            S(B, "raw_copper_block", 1, new[] { "###", "###", "###" }, ('#', "raw_copper"));
            L("Materials", "raw_copper", 9, "raw_copper_block");
            S(B, "honeycomb_block", 1, new[] { "##", "##" }, ('#', "honeycomb"));
            S("Redstone", "copper_door", 3, new[] { "##", "##", "##" }, ('#', "copper_ingot"));
            S("Redstone", "copper_trapdoor", 2, new[] { "###", "###" }, ('#', "copper_ingot"));
            S("Redstone", "lightning_rod", 1, new[] { "#", "#", "#" }, ('#', "copper_ingot"));
            // mossy: with a vine (#158)
            L(B, "mossy_cobblestone", 1, "cobblestone", "vine");
            L(B, "mossy_stone_bricks", 1, "stone_bricks", "vine");
            // sandstone from sand, quartz from quartz, nether bricks from nether brick
            S(B, "sandstone", 1, new[] { "##", "##" }, ('#', "sand"));
            S(B, "red_sandstone", 1, new[] { "##", "##" }, ('#', "red_sand"));
            S(B, "quartz_block", 1, new[] { "##", "##" }, ('#', "quartz"));
            S(B, "nether_bricks", 1, new[] { "##", "##" }, ('#', "nether_brick"));
            S(B, "red_nether_bricks", 1, new[] { "wn", "nw" }, ('w', "nether_wart"), ('n', "nether_brick"));
            S(B, "quartz_pillar", 2, new[] { "#", "#" }, ('#', "quartz_block"));
            // resin (#230): a block of nine clumps and back; bricks of four resin brick (a smelted clump)
            S(B, "resin_block", 1, new[] { "###", "###", "###" }, ('#', "resin_clump"));
            L("Materials", "resin_clump", 9, "resin_block");
            S(B, "resin_bricks", 1, new[] { "##", "##" }, ('#', "resin_brick"));
            // chiseled: two slabs, one on the other
            foreach (var (slab, to) in new[] { ("stone_brick_slab", "chiseled_stone_bricks"), ("sandstone_slab", "chiseled_sandstone"), ("red_sandstone_slab", "chiseled_red_sandstone"),
                ("quartz_slab", "chiseled_quartz_block"), ("nether_brick_slab", "chiseled_nether_bricks"), ("polished_blackstone_slab", "chiseled_polished_blackstone"), ("cobbled_deepslate_slab", "chiseled_deepslate"), ("resin_brick_slab", "chiseled_resin_bricks") })
                S(B, to, 1, new[] { "#", "#" }, ('#', slab));
            // smelted: smooth, cracked; red sand into glass, netherrack into nether brick
            foreach (var (from, to) in new[] { ("stone", "smooth_stone"), ("stone_bricks", "cracked_stone_bricks"), ("sandstone", "smooth_sandstone"), ("red_sandstone", "smooth_red_sandstone"),
                ("quartz_block", "smooth_quartz"), ("nether_bricks", "cracked_nether_bricks"), ("polished_blackstone_bricks", "cracked_polished_blackstone_bricks"),
                ("red_sand", "glass"), ("netherrack", "nether_brick"), ("raw_copper", "copper_ingot"), ("copper_ore", "copper_ingot"),
                ("cobbled_deepslate", "deepslate"), ("deepslate_bricks", "cracked_deepslate_bricks"), ("deepslate_tiles", "cracked_deepslate_tiles"), ("resin_clump", "resin_brick") })
                SmeltResult[from] = to;
            foreach (var wood in new[] { "oak_fence", "dark_oak_fence", "dark_oak_stairs", "dark_oak_slab" }) FuelSeconds[wood] = wood.EndsWith("slab") ? 3f : 6f;
        }
    }
}
