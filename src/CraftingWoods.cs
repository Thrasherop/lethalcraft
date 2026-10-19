using System.Linq;

namespace LethalMinecraft
{
    public static partial class Crafting
    {
        /// <summary>
        /// (#223) The woods' recipes, Minecraft's: four planks from any log or wood of a kind, three wood from four logs,
        /// doors, trapdoors, buttons, plates and gates of each; then Minecraft's tags: wherever a recipe that isn't one
        /// wood's own asks for oak planks (or dark oak), any planks do, mixed as you like (sticks, the crafting table,
        /// chests, wooden tools...), and any wooden slab where it asks for oak slabs. The mud family: packed mud (mud and
        /// wheat), mud bricks, muddy mangrove roots. Every log burns into charcoal (coal here); wood burns as Minecraft's.
        /// </summary>
        static void WoodRecipes()
        {
            const string B = "Building";
            foreach (var w in Blocks.Woods)
            {
                string planks = w.Planks.Key;
                foreach (var log in new[] { w.Log, w.Wood, w.StrippedLog, w.StrippedWood })
                    if (!(log == Blocks.Log && w == Blocks.Oak)) L("Basics", planks, 4, log.Key); // (oak log's was there)
                S(B, w.Wood.Key, 3, new[] { "##", "##" }, ('#', w.Log.Key));
                S(B, w.StrippedWood.Key, 3, new[] { "##", "##" }, ('#', w.StrippedLog.Key));
                if (w != Blocks.Oak)
                {
                    if (!Recipes.Any(r => r.Result == w.Door.Key)) S(B, w.Door.Key, 3, new[] { "##", "##", "##" }, ('#', planks));
                    if (!Recipes.Any(r => r.Result == w.Trapdoor.Key)) S(B, w.Trapdoor.Key, 2, new[] { "###", "###" }, ('#', planks));
                    if (!Recipes.Any(r => r.Result == w.Button.Key)) L("Redstone", w.Button.Key, 1, planks);
                    if (!Recipes.Any(r => r.Result == w.Plate.Key)) S("Redstone", w.Plate.Key, 1, new[] { "##" }, ('#', planks));
                    if (!Recipes.Any(r => r.Result == w.Gate.Key)) S("Redstone", w.Gate.Key, 1, new[] { "|#|", "|#|" }, ('|', "stick"), ('#', planks));
                }
                if (w.Nether) continue; // (#244: the Nether's woods don't burn, Minecraft's)
                foreach (var log in new[] { w.Log, w.Wood, w.StrippedLog, w.StrippedWood }) SmeltResult[log.Key] = "charcoal"; // (#244: Minecraft's)
                foreach (var d in new[] { w.Log, w.Wood, w.StrippedLog, w.StrippedWood, w.Planks, w.Stairs, w.Fence, w.Gate, w.Trapdoor, w.Plate })
                    if (!FuelSeconds.ContainsKey(d.Key)) FuelSeconds[d.Key] = 6f;
                if (!FuelSeconds.ContainsKey(w.Slab.Key)) FuelSeconds[w.Slab.Key] = 3f;
                if (!FuelSeconds.ContainsKey(w.Door.Key)) FuelSeconds[w.Door.Key] = 4f;
                if (!FuelSeconds.ContainsKey(w.Button.Key)) FuelSeconds[w.Button.Key] = 2f;
            }
            FuelSeconds["mangrove_roots"] = 6f;
            // signs (Minecraft's: six planks and a stick, three; two iron chains and six stripped logs, six hanging signs)
            foreach (var w in Blocks.Woods)
            {
                S(B, w.Key + "_sign", 3, new[] { "###", "###", " | " }, ('#', w.Planks.Key), ('|', "stick"));
                S(B, w.Key + "_hanging_sign", 6, new[] { "C C", "###", "###" }, ('C', "iron_chain"), ('#', w.StrippedLog.Key));
                if (!w.Nether) { FuelSeconds[w.Key + "_sign"] = 4f; FuelSeconds[w.Key + "_hanging_sign"] = 16f; } // (#244: Minecraft's 800 ticks, four items)
            }
            S(B, "iron_chain", 1, new[] { "N", "I", "N" }, ('N', "iron_nugget"), ('I', "iron_ingot"));
            // (#225) dyes, Minecraft's recipes from what the mod has: the flowers, an ink sac, lapis, bone meal, and mixing
            foreach (var kv in Dyes.FlowerDye) L("Materials", Dyes.Key(kv.Value), 1, kv.Key);
            L("Materials", "black_dye", 1, "ink_sac"); L("Materials", "blue_dye", 1, "lapis_lazuli"); L("Materials", "white_dye", 1, "bone_meal");
            foreach (var (result, n, ing) in new[] {
                ("orange", 2, new[] { "red", "yellow" }), ("pink", 2, new[] { "red", "white" }), ("light_blue", 2, new[] { "blue", "white" }), ("purple", 2, new[] { "blue", "red" }),
                ("cyan", 2, new[] { "blue", "green" }), ("gray", 2, new[] { "black", "white" }), ("light_gray", 2, new[] { "gray", "white" }), ("light_gray", 3, new[] { "black", "white", "white" }),
                ("lime", 2, new[] { "green", "white" }), ("magenta", 2, new[] { "purple", "pink" }), ("magenta", 3, new[] { "blue", "red", "pink" }), ("magenta", 4, new[] { "blue", "red", "red", "white" }) })
                L("Materials", Dyes.Key(result), n, ing.Select(Dyes.Key).ToArray());
            // the loom (two string over two planks) and the pattern items from paper and their item
            S("Building", "loom", 1, new[] { "@@", "##" }, ('@', "string"), ('#', "oak_planks"));
            S("Building", "anvil", 1, new[] { "III", " i ", "iii" }, ('I', "iron_block"), ('i', "iron_ingot")); // (#228)
            foreach (var (pat, with) in new[] { ("flower", "oxeye_daisy"), ("creeper", "creeper_head"), ("skull", "wither_skeleton_skull"), ("field_masoned", "bricks"), ("bordure_indented", "vine") })
                L("Materials", pat + "_banner_pattern", 1, "paper", with);
            // banners (Minecraft's: six wool of a colour and a stick)
            for (int bc = 0; bc < 16; bc++) S("Building", BannerDesigns.BlockKeyFor(bc), 1, new[] { "###", "###", " | " }, ('#', Dyes.Wool(Dyes.Colors[bc])), ('|', "stick"));
            // wool takes any dye (Minecraft's dye_<colour>_wool: a dye and wool of another colour)
            foreach (var c in Dyes.Colors)
                foreach (var other in Dyes.Colors.Where(o => o != c))
                    L("Building", Dyes.Wool(c), 1, Dyes.Key(c), Dyes.Wool(other));
            // shelves (Minecraft's: six stripped logs, six)
            foreach (var w in Blocks.Woods) { S(B, w.Key + "_shelf", 6, new[] { "###", "   ", "###" }, ('#', w.StrippedLog.Key)); if (!w.Nether) FuelSeconds[w.Key + "_shelf"] = 6f; }
            // boats (Minecraft's: five planks; a chest boat a boat and a chest)
            foreach (var w in Blocks.Woods.Where(x => !x.Nether))
            {
                S("Tools", BoatKeys.ItemKey(w, false), 1, new[] { "# #", "###" }, ('#', w.Planks.Key));
                L("Tools", BoatKeys.ItemKey(w, true), 1, BoatKeys.ItemKey(w, false), "chest");
                FuelSeconds[BoatKeys.ItemKey(w, false)] = 24f;
            }
            L("Materials", "iron_nugget", 9, "iron_ingot");
            L("Materials", "copper_nugget", 9, "copper_ingot"); // (#244, Minecraft 1.21.9's)
            S("Materials", "copper_ingot", 1, new[] { "###", "###", "###" }, ('#', "copper_nugget"));
            S("Materials", "iron_ingot", 1, new[] { "###", "###", "###" }, ('#', "iron_nugget"));
            L(B, "packed_mud", 1, "mud", "wheat");
            S(B, "mud_bricks", 4, new[] { "##", "##" }, ('#', "packed_mud"));
            L(B, "muddy_mangrove_roots", 1, "mud", "mangrove_roots");

            // Minecraft's #planks and #wooden_slabs, for every recipe that isn't one wood's own
            var allPlanks = Blocks.Woods.Select(x => x.Planks.Key).ToArray();
            var allSlabs = Blocks.Woods.Select(x => x.Slab.Key).ToArray();
            var woodPrefixes = Blocks.Woods.Select(x => x.Key + "_").Concat(new[] { "stripped_" }).ToArray();
            foreach (var r in Recipes)
            {
                if (r.Shapeless || r.Map == null || woodPrefixes.Any(p => r.Result.StartsWith(p))) continue;
                foreach (var kv in r.Map.ToList())
                {
                    string[] any = allPlanks.Contains(kv.Value) ? allPlanks : allSlabs.Contains(kv.Value) ? allSlabs : null;
                    if (any == null) continue;
                    if (r.Alts == null) r.Alts = new System.Collections.Generic.Dictionary<char, string[]>();
                    if (!r.Alts.ContainsKey(kv.Key)) r.Alts[kv.Key] = any;
                }
            }
        }
    }
}
