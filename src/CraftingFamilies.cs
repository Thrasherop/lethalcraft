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
            // chiseled: two slabs, one on the other
            foreach (var (slab, to) in new[] { ("stone_brick_slab", "chiseled_stone_bricks"), ("sandstone_slab", "chiseled_sandstone"), ("red_sandstone_slab", "chiseled_red_sandstone"),
                ("quartz_slab", "chiseled_quartz_block"), ("nether_brick_slab", "chiseled_nether_bricks"), ("polished_blackstone_slab", "chiseled_polished_blackstone"), ("cobbled_deepslate_slab", "chiseled_deepslate") })
                S(B, to, 1, new[] { "#", "#" }, ('#', slab));
            // smelted: smooth, cracked; red sand into glass, netherrack into nether brick
            foreach (var (from, to) in new[] { ("stone", "smooth_stone"), ("stone_bricks", "cracked_stone_bricks"), ("sandstone", "smooth_sandstone"), ("red_sandstone", "smooth_red_sandstone"),
                ("quartz_block", "smooth_quartz"), ("nether_bricks", "cracked_nether_bricks"), ("polished_blackstone_bricks", "cracked_polished_blackstone_bricks"),
                ("red_sand", "glass"), ("netherrack", "nether_brick"),
                ("cobbled_deepslate", "deepslate"), ("deepslate_bricks", "cracked_deepslate_bricks"), ("deepslate_tiles", "cracked_deepslate_tiles") })
                SmeltResult[from] = to;
            foreach (var wood in new[] { "oak_fence", "dark_oak_fence", "dark_oak_stairs", "dark_oak_slab" }) FuelSeconds[wood] = wood.EndsWith("slab") ? 3f : 6f;
        }
    }
}
