using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>Recipes, furnace simulation (server) and inventory helpers.</summary>
    public static partial class Crafting
    {
        public static readonly List<Recipe> Recipes = new List<Recipe>();

        static void S(string cat, string result, int count, string[] rows, params (char, string)[] map) => Recipes.Add(Recipe.Shaped(cat, result, count, rows, map));
        /// <summary>Minecraft's stone_tool_materials and stone_crafting_materials (the same three): what a stone tool, a
        /// furnace or a brewing stand is made of, mixed as you like (#217).</summary>
        public static readonly string[] StoneMaterials = { "cobblestone", "cobbled_deepslate", "blackstone" };
        /// <summary>One recipe per material (the recipe book shows each), each taking any mix in the grid.</summary>
        static void SAny(string cat, string result, int count, string[] rows, char sym, string[] any, params (char, string)[] rest)
        {
            foreach (var first in any)
                Recipes.Add(Recipe.ShapedAny(cat, result, count, rows, sym, new[] { first }.Concat(any.Where(k => k != first)).ToArray(), rest));
        }
        static void L(string cat, string result, int count, params string[] ingredients) => Recipes.Add(Recipe.ShapelessOf(cat, result, count, ingredients));

        /// <summary>(#226) A recipe that can be used now: the bow's and the crossbow's only while bows are on (the host's
        /// setting). They're in every machine's list either way: recipes go by their place in it.</summary>
        public static bool Allowed(Recipe r) => r != null && (Bow.On || (r.Result != Bow.Key && r.Result != Crossbow.Key));

        /// <summary>Minecraft recipes and patterns. Shaped recipes match anywhere in the grid and mirrored.</summary>
        public static void Init()
        {
            if (Recipes.Count > 0) return;
            const string P = "oak_planks", St = "stick", C = "cobblestone", I = "iron_ingot", G = "gold_ingot", D = "scrap_diamond_ore", R = "redstone_dust";
            // basics
            L("Basics", "oak_planks", 4, "oak_log");
            S("Basics", "stick", 4, new[] { "#", "#" }, ('#', P));
            S("Basics", "crafting_table", 1, new[] { "##", "##" }, ('#', P));
            S("Basics", "torch", 4, new[] { "c", "|" }, ('c', "coal"), ('|', St));
            SAny("Basics", "furnace", 1, new[] { "###", "# #", "###" }, '#', StoneMaterials);
            S("Building", "glass_pane", 16, new[] { "###", "###" }, ('#', "glass"));
            S("Building", "oak_door", 3, new[] { "##", "##", "##" }, ('#', P));
            S("Building", "ladder", 3, new[] { "| |", "|||", "| |" }, ('|', St));
            S("Building", "oak_stairs", 4, new[] { "#  ", "## ", "###" }, ('#', P));
            // Minecraft's book on top (#116: there was no book; a bookshelf from the store still does), diamonds mined or crafted (#119)
            foreach (var book in new[] { "book", "bookshelf" })
                foreach (var dia in new[] { "diamond", D })
                    S("Building", "enchanting_table", 1, new[] { " B ", "DOD", "OOO" }, ('B', book), ('D', dia), ('O', "obsidian"));
            S("Building", "bookshelf", 1, new[] { "###", "bbb", "###" }, ('#', P), ('b', "book"));
            S("Building", "oak_slab", 6, new[] { "###" }, ('#', P));
            S("Building", "cobblestone_slab", 6, new[] { "###" }, ('#', C));
            S("Building", "stone_brick_slab", 6, new[] { "###" }, ('#', "stone_bricks"));
            S("Building", "stone_slab", 6, new[] { "###" }, ('#', "stone"));
            S("Building", "oak_trapdoor", 2, new[] { "###", "###" }, ('#', P));
            S("Building", "cobblestone_stairs", 4, new[] { "#  ", "## ", "###" }, ('#', C));
            S("Building", "stone_brick_stairs", 4, new[] { "#  ", "## ", "###" }, ('#', "stone_bricks"));
            // dark planks work wherever planks do (Minecraft accepts any planks)
            S("Basics", "stick", 4, new[] { "#", "#" }, ('#', "dark_planks"));
            S("Basics", "crafting_table", 1, new[] { "##", "##" }, ('#', "dark_planks"));
            S("Basics", "chest", 1, new[] { "###", "# #", "###" }, ('#', P));
            S("Redstone", "observer", 1, new[] { "###", "RRQ", "###" }, ('#', C), ('R', R), ('Q', "quartz")); // (#244: Minecraft's, quartz)
            S("Tools", "bucket", 1, new[] { "# #", " # " }, ('#', "iron_ingot"));
            // (a mined diamond too, like the tools: #119, the diamond from the ground is scrap, and only the crafted one counted)
            foreach (var dia in new[] { "diamond", D }) S("Building", "jukebox", 1, new[] { "###", "#D#", "###" }, ('#', P), ('D', dia));
            S("Redstone", "repeater", 1, new[] { "TRT", "SSS" }, ('T', "redstone_torch"), ('R', R), ('S', "stone"));
            S("Redstone", "comparator", 1, new[] { " T ", "TQT", "SSS" }, ('T', "redstone_torch"), ('Q', "quartz"), ('S', "stone")); // (#244: Minecraft's, quartz)
            S("Basics", "chest", 1, new[] { "###", "# #", "###" }, ('#', "dark_planks"));
            // tools (pickaxe, shovel, axe) in wood, stone, iron and diamond
            foreach (var (mat, key) in new[] { ("wooden", P), ("wooden", "dark_planks"), ("copper", "copper_ingot"), ("iron", I), ("golden", G), ("diamond", D), ("diamond", "diamond") })
            {
                string pick = mat == "diamond" ? "pickaxe" : mat + "_pickaxe";
                S("Tools", pick, 1, new[] { "###", " | ", " | " }, ('#', key), ('|', St));
                S("Tools", mat + "_shovel", 1, new[] { "#", "|", "|" }, ('#', key), ('|', St));
                S("Tools", mat + "_axe", 1, new[] { "##", "#|", " |" }, ('#', key), ('|', St));
                S("Tools", mat + "_hoe", 1, new[] { "##", " |", " |" }, ('#', key), ('|', St));
            }
            // stone tools: cobblestone, cobbled deepslate or blackstone (Minecraft's stone_tool_materials, #217)
            SAny("Tools", "stone_pickaxe", 1, new[] { "###", " | ", " | " }, '#', StoneMaterials, ('|', St));
            SAny("Tools", "stone_shovel", 1, new[] { "#", "|", "|" }, '#', StoneMaterials, ('|', St));
            SAny("Tools", "stone_axe", 1, new[] { "##", "#|", " |" }, '#', StoneMaterials, ('|', St));
            SAny("Tools", "stone_hoe", 1, new[] { "##", " |", " |" }, '#', StoneMaterials, ('|', St));
            SAny("Tools", "stone_sword", 1, new[] { "#", "#", "|" }, '#', StoneMaterials, ('|', St));
            L("Tools", "flint_and_steel", 1, I, "flint"); // (Minecraft's)
            // arrows and the bow (Minecraft's)
            S("Materials", "leather", 1, new[] { "##", "##" }, ('#', "rabbit_hide")); // (#149, Minecraft's)
            L("Materials", "wool_black", 1, "wool_white", "ink_sac"); // (#150: Minecraft's black dye, from a squid's ink)
            S("Materials", "snow_block", 1, new[] { "ss", "ss" }, ('s', "snowball")); // (#140, Minecraft's)
            // (#145, Minecraft's) two leads from four string and a slimeball; leather horse armor
            S("Tools", "lead", 2, new[] { "ss ", "sb ", "  s" }, ('s', "string"), ('b', "slime_ball"));
            S("Tools", "leather_horse_armor", 1, new[] { "l l", "lll", "l l" }, ('l', "leather"));
            S("Tools", Bow.ArrowKey, 4, new[] { "f", "|", "e" }, ('f', "flint"), ('|', St), ('e', "feather"));
            S("Tools", Bow.Key, 1, new[] { " #s", "# s", " #s" }, ('#', St), ('s', "string"));
            // (#223, Minecraft's recipe/crossbow)
            S("Tools", Crossbow.Key, 1, new[] { "|i|", "sts", " | " }, ('|', St), ('i', I), ('s', "string"), ('t', "tripwire_hook"));
            // swords
            foreach (var (key, mat) in new[] { ("wooden_sword", P), ("wooden_sword", "dark_planks"), ("copper_sword", "copper_ingot"), ("iron_sword", I), ("golden_sword", G), ("diamond_sword", D), ("diamond_sword", "diamond") })
                S("Tools", key, 1, new[] { "#", "#", "|" }, ('#', mat), ('|', St));
            // materials
            S("Materials", "iron_block", 1, new[] { "###", "###", "###" }, ('#', I));
            L("Materials", "iron_ingot", 9, "iron_block");
            S("Materials", "gold_block", 1, new[] { "###", "###", "###" }, ('#', G));
            L("Materials", "gold_ingot", 9, "gold_block");
            S("Materials", "diamond_block", 1, new[] { "###", "###", "###" }, ('#', D));
            S("Materials", "diamond_block", 1, new[] { "###", "###", "###" }, ('#', "diamond"));
            L("Materials", "diamond", 9, "diamond_block"); // a plain material: mined diamonds are the scrap worth selling
            S("Materials", "coal_block", 1, new[] { "###", "###", "###" }, ('#', "coal"));
            L("Materials", "coal", 9, "coal_block");
            S("Materials", "stone_bricks", 4, new[] { "##", "##" }, ('#', "stone"));
            L("Materials", "bricks", 1, C, "dirt");
            S("Materials", "bricks", 1, new[] { "bb", "bb" }, ('b', "brick")); // (Minecraft's)
            S("Redstone", "decorated_pot", 1, new[] { " b ", "b b", " b " }, ('b', "brick")); // (Minecraft's: four bricks, #160)
            S("Materials", "glowstone", 1, new[] { "##", "##" }, ('#', "glowstone_dust")); // (#244: Minecraft's, from its dust)
            // (#244) brewing's: the glistering melon, the fermented spider eye; melons; bowls and stews; honey
            S("Materials", "glistering_melon_slice", 1, new[] { "###", "#m#", "###" }, ('#', "gold_nugget"), ('m', "melon_slice"));
            L("Materials", "fermented_spider_eye", 1, "spider_eye", "brown_mushroom", "sugar");
            L("Food", "melon", 1, "melon_slice", "melon_slice", "melon_slice", "melon_slice", "melon_slice", "melon_slice", "melon_slice", "melon_slice", "melon_slice");
            L("Food", "melon_seeds", 1, "melon_slice");
            S("Food", "bowl", 4, new[] { "# #", " # " }, ('#', P));
            L("Food", "mushroom_stew", 1, "brown_mushroom", "red_mushroom", "bowl");
            L("Food", "mushroom_stew", 1, "brown_mushroom", "brown_mushroom", "bowl");
            L("Food", "mushroom_stew", 1, "red_mushroom", "red_mushroom", "bowl");
            L("Food", "rabbit_stew", 1, "cooked_rabbit", "carrot", "baked_potato", "brown_mushroom", "bowl");
            L("Food", "rabbit_stew", 1, "cooked_rabbit", "carrot", "baked_potato", "red_mushroom", "bowl");
            L("Food", "sugar", 3, "honey_bottle");
            S("Redstone", "honey_block", 1, new[] { "##", "##" }, ('#', "honey_bottle"));
            L("Food", "honey_bottle", 4, "honey_block", "glass_bottle", "glass_bottle", "glass_bottle", "glass_bottle");
            // redstone
            S("Redstone", "redstone_block", 1, new[] { "###", "###", "###" }, ('#', R));
            L("Redstone", R, 9, "redstone_block");
            S("Redstone", "lever", 1, new[] { "|", "#" }, ('|', St), ('#', C));
            L("Redstone", "button", 1, "stone");
            S("Redstone", "pressure_plate", 1, new[] { "##" }, ('#', "stone"));
            S("Redstone", "redstone_torch", 1, new[] { "r", "|" }, ('r', R), ('|', St));
            S("Redstone", "piston", 1, new[] { "PPP", "#i#", "#r#" }, ('P', P), ('#', C), ('i', I), ('r', R));
            S("Redstone", "sticky_piston", 1, new[] { "s", "p" }, ('s', "slime_ball"), ('p', "piston"));
            S("Redstone", "slime", 1, new[] { "sss", "sss", "sss" }, ('s', "slime_ball"));
            S("Redstone", "tnt", 1, new[] { "gsg", "sgs", "gsg" }, ('g', "gunpowder"), ('s', "sand"));
            L("Redstone", "slime_ball", 9, "slime");
            S("Redstone", "redstone_lamp", 1, new[] { " r ", "rgr", " r " }, ('r', R), ('g', "glowstone"));
            S("Redstone", "note_block", 1, new[] { "PPP", "PrP", "PPP" }, ('P', P), ('r', R));
            // armor
            foreach (var ad in Armor.Defs.Values.Where(d => d.Ingredient != null))
                foreach (var mat in ad.Material == "diamond" ? new[] { "diamond", D } : new[] { ad.Ingredient })
                {
                    var shape = ad.Slot == 0 ? new[] { "###", "# #" } : ad.Slot == 1 ? new[] { "# #", "###", "###" } : ad.Slot == 2 ? new[] { "###", "# #", "# #" } : new[] { "# #", "# #" };
                    S("Armor", ad.Key, 1, shape, ('#', mat));
                }
            // food
            S("Food", "golden_apple", 1, new[] { "ggg", "gag", "ggg" }, ('g', G), ('a', "apple"));
            S("Food", "bread", 1, new[] { "###" }, ('#', "wheat"));
            S("Materials", "paper", 3, new[] { "###" }, ('#', "sugar_cane"));
            // pumpkins (#130)
            S("Tools", "shears", 1, new[] { " #", "# " }, ('#', "iron_ingot"));
            S("Materials", "pumpkin_seeds", 4, new[] { "#" }, ('#', "pumpkin"));
            S("Building", "jack_o_lantern", 1, new[] { "P", "T" }, ('P', "carved_pumpkin"), ('T', "torch"));
            // Minecraft's book is three paper and leather; there's no leather in this world, so string binds it (#116)
            L("Materials", "book", 1, "paper", "paper", "paper", "string");
            L("Materials", "book", 1, "paper", "paper", "paper", "leather"); // (Minecraft's, now there's leather: #95; shapeless, #244)
            S("Tools", "saddle", 1, new[] { " X ", "X#X" }, ('X', "leather"), ('#', I)); // (Minecraft 1.21.6's)
            S("Tools", Fishing.RodKey, 1, new[] { "  #", " #s", "# s" }, ('#', St), ('s', "string")); // (Minecraft's)
            S("Materials", "sugar", 1, new[] { "#" }, ('#', "sugar_cane"));
            // netherite (#66): four scrap and four gold make an ingot; an ingot with a diamond tool makes a netherite one
            // brewing (#88)
            SAny("Basics", "brewing_stand", 1, new[] { " b ", "###" }, '#', StoneMaterials, ('b', "blaze_rod"));
            S("Materials", "glass_bottle", 3, new[] { "# #", " # " }, ('#', "glass"));
            S("Materials", "blaze_powder", 2, new[] { "#" }, ('#', "blaze_rod"));
            L("Materials", "magma_cream", 1, "blaze_powder", "slime_ball"); // (#244: shapeless, Minecraft's)
            S("Materials", "gold_nugget", 9, new[] { "#" }, ('#', G));
            // the skeleton's and the spider's drops (#59): bone meal for crops, string into wool
            S("Materials", "bone_meal", 3, new[] { "#" }, ('#', "bone"));
            S("Materials", "bone_meal", 9, new[] { "#" }, ('#', "bone_block"));
            S("Building", "wool_white", 1, new[] { "##", "##" }, ('#', "string"));
            S("Food", "golden_carrot", 1, new[] { "nnn", "ncn", "nnn" }, ('n', "gold_nugget"), ('c', "carrot"));
            S("Building", "red_bed", 1, new[] { "www", "ppp" }, ('w', "wool_red"), ('p', P));
            S("Building", "iron_door", 3, new[] { "ii", "ii", "ii" }, ('i', I)); // (#160, Minecraft's)
            S("Building", "iron_trapdoor", 1, new[] { "ii", "ii" }, ('i', I));
            foreach (var (pre, mat) in new[] { ("oak", P), ("dark_oak", "dark_planks"), ("polished_blackstone", "polished_blackstone") })
            {
                L("Redstone", pre + "_button", 1, mat);
                S("Redstone", pre + "_pressure_plate", 1, new[] { "##" }, ('#', mat));
            }
            S("Redstone", "light_weighted_pressure_plate", 1, new[] { "##" }, ('#', G));
            S("Redstone", "heavy_weighted_pressure_plate", 1, new[] { "##" }, ('#', I));
            S("Redstone", "tripwire_hook", 2, new[] { "i", "|", "p" }, ('i', I), ('|', St), ('p', P));
            L("Redstone", "trapped_chest", 1, "chest", "tripwire_hook");
            S("Redstone", "oak_fence_gate", 1, new[] { "|#|", "|#|" }, ('|', St), ('#', P));
            S("Redstone", "dark_oak_fence_gate", 1, new[] { "|#|", "|#|" }, ('|', St), ('#', "dark_planks"));
            S("Redstone", "hopper", 1, new[] { "i i", "ici", " i " }, ('i', I), ('c', "chest"));
            S("Redstone", "crafter", 1, new[] { "iii", "ici", "rdr" }, ('i', I), ('c', "crafting_table"), ('r', R), ('d', "dropper"));
            S("Redstone", "composter", 1, new[] { "s s", "s s", "sss" }, ('s', "oak_slab"));
            S("Redstone", "cauldron", 1, new[] { "i i", "i i", "iii" }, ('i', I));
            S("Redstone", "rail", 16, new[] { "i i", "i|i", "i i" }, ('i', I), ('|', St));
            S("Redstone", "powered_rail", 6, new[] { "g g", "g|g", "grg" }, ('g', G), ('|', St), ('r', R));
            S("Redstone", "detector_rail", 6, new[] { "i i", "ipi", "iri" }, ('i', I), ('p', "pressure_plate"), ('r', R));
            S("Redstone", "activator_rail", 6, new[] { "i|i", "iti", "i|i" }, ('i', I), ('|', St), ('t', "redstone_torch"));
            S("Redstone", "minecart", 1, new[] { "i i", "iii" }, ('i', I));
            foreach (var (cartKey, block) in new[] { ("chest_minecart", "chest"), ("hopper_minecart", "hopper"), ("tnt_minecart", "tnt"), ("furnace_minecart", "furnace") })
                L("Redstone", cartKey, 1, "minecart", block);
            S("Redstone", "chiseled_bookshelf", 1, new[] { "###", "sss", "###" }, ('#', P), ('s', "oak_slab"));
            L("Materials", Books.Writable, 1, "book", "ink_sac", "feather");
            S("Redstone", "lectern", 1, new[] { "sss", " b ", " s " }, ('s', "oak_slab"), ('b', "bookshelf"));
            S("Redstone", "dropper", 1, new[] { "ccc", "c c", "crc" }, ('c', C), ('r', R));
            S("Redstone", "dispenser", 1, new[] { "ccc", "cbc", "crc" }, ('c', C), ('b', "bow"), ('r', R));
            S("Building", "barrel", 1, new[] { "psp", "p p", "psp" }, ('p', P), ('s', "oak_slab"));
            S("Building", "hay_block", 1, new[] { "###", "###", "###" }, ('#', "wheat"));
            L("Food", "wheat", 9, "hay_block");
            S("Redstone", "target", 1, new[] { " r ", "rhr", " r " }, ('r', R), ('h', "hay_block"));
            S("Redstone", "daylight_detector", 1, new[] { "ggg", "qqq", "sss" }, ('g', "glass"), ('q', "quartz"), ('s', "oak_slab"));
            L("Materials", "netherite_ingot", 1, "netherite_scrap", "netherite_scrap", "netherite_scrap", "netherite_scrap", G, G, G, G); // (#244: shapeless, Minecraft's)
            // (#159: the netherite upgrade is the smithing table's now, Smithing.cs; the template is copied with diamonds, Minecraft's)
            foreach (var dia in new[] { D, "diamond" }) // (a diamond: mined, or out of a block of diamond)
                S("Tools", Smithing.TemplateKey, 2, new[] { "dtd", "dnd", "ddd" }, ('d', dia), ('t', Smithing.TemplateKey), ('n', "netherrack"));
            // (#224: a trim template copied the same way; #244: on its own block, Minecraft's)
            foreach (var dia in new[] { D, "diamond" })
                foreach (var (pat, on) in ArmorTrims.AllTemplates)
                    if (on != null)
                        foreach (var blk in on)
                            S("Tools", ArmorTrims.TemplateKey(pat), 2, new[] { "dtd", "dcd", "ddd" }, ('d', dia), ('t', ArmorTrims.TemplateKey(pat)), ('c', blk));
            S("Building", "smithing_table", 1, new[] { "ii", "pp", "pp" }, ('i', I), ('p', P));
            L("Tools", Elytra.RocketKey, 3, "paper", "gunpowder"); // (#244: anywhere in the grid, Minecraft's)
            ThoroughDRecipes(); // (#244 part D: CraftingD.cs)
            ThoroughERecipes(); // (#244 part E: CraftingE.cs)
            FamilyRecipes(); // (#154, #156, #157: CraftingFamilies.cs)
            WoodRecipes(); // (#223: CraftingWoods.cs, after every other recipe: it gives them Minecraft's planks tags)
        }

        // ------------------------------------------------------------------ smelting
        public static readonly ItemData.TypeMap<string> SmeltResult = new ItemData.TypeMap<string> // (#228: by type: a named raw iron smelts)
        {
            ["scrap_iron_ore"] = "iron_ingot", ["scrap_gold_ore"] = "gold_ingot", ["iron_ore"] = "iron_ingot", ["gold_ore"] = "gold_ingot",
            ["sand"] = "glass", ["dirt"] = "brick", ["cobblestone"] = "stone", ["oak_log"] = "charcoal", ["potato"] = "baked_potato", ["scrap_ancient_debris"] = "netherite_scrap", ["nether_quartz_ore"] = "quartz",
            ["raw_porkchop"] = "porkchop", ["raw_beef"] = "steak", ["raw_chicken"] = "cooked_chicken", ["raw_mutton"] = "cooked_mutton", ["raw_rabbit"] = "cooked_rabbit", // (#95)
            ["raw_cod"] = "cooked_cod", ["raw_salmon"] = "cooked_salmon", // (fishing)
        };
        public static readonly ItemData.TypeMap<float> FuelSeconds = new ItemData.TypeMap<float> // (#228: a named coal burns)
        {
            ["coal"] = 32f, ["coal_block"] = 320f, ["oak_planks"] = 6f, ["dark_planks"] = 6f, ["oak_log"] = 6f, ["stick"] = 2f, ["crafting_table"] = 6f, ["bookshelf"] = 6f,
        };
        public const float SmeltSeconds = 4f;

        /// <summary>(#244) The XP of a smelted item, Minecraft's recipes' (0.1 for glass and stone up to 1 for gold, 2 for netherite
        /// scrap); it had been half a point an item, whatever it was.</summary>
        public static float SmeltXpOf(string outKey)
        {
            string k = ItemData.TypeOf(outKey) ?? "";
            switch (k)
            {
                case "netherite_scrap": return 2f;
                case "gold_ingot": case "diamond": case "emerald": case "green_dye": return 1f;
                case "iron_ingot": case "copper_ingot": return 0.7f;
                case "baked_potato": case "porkchop": case "cooked_porkchop": case "steak": case "cooked_beef": case "cooked_chicken": case "cooked_mutton":
                case "cooked_rabbit": case "cooked_cod": case "cooked_salmon": case "terracotta": return 0.35f;
                case "brick": case "redstone_dust": case "redstone": return 0.3f;
                case "quartz": case "lapis_lazuli": return 0.2f;
                case "coal": case "charcoal": return 0.15f; // (#244: charcoal from logs, Minecraft's)
                case "sponge": return 0.15f;
                default: return 0.1f;
            }
        }

        public class Furnace
        {
            public string In; public int InCount;
            public int Fuel; public string FuelKey;
            public string Out; public int OutCount;
            public float Burn, BurnMax, Progress;
            public bool Lit => Burn > 0f;
        }

        public static readonly Dictionary<BlockKey, Furnace> Furnaces = new Dictionary<BlockKey, Furnace>();  // server truth; mirrored on clients
        static float syncTimer;

        public static void Reset() => Furnaces.Clear();

        public static string Describe(BlockKey k)
        {
            if (!Furnaces.TryGetValue(k, out var f) || (f.InCount == 0 && f.Fuel == 0 && f.OutCount == 0 && !f.Lit))
                return "Furnace (empty) - [E] to open";
            var parts = new List<string>();
            if (f.InCount > 0) parts.Add($"{NameOf(f.In)} x{f.InCount}{(f.Lit ? $" {Mathf.RoundToInt(f.Progress / SmeltSeconds * 100)}%" : "")}");
            if (f.Fuel > 0 || f.Lit) parts.Add($"fuel {(f.FuelKey != null ? NameOf(f.FuelKey) : "")} x{f.Fuel}");
            if (f.OutCount > 0) parts.Add($"-> {NameOf(f.Out)} x{f.OutCount}");
            return "Furnace: " + string.Join(", ", parts);
        }

        public static string NameOf(string key)
        {
            if (key == null) return "";
            var custom = ItemNames.Of(key); if (custom != null) return custom; // (#228: its anvil name, as Minecraft shows it)
            if (key.StartsWith("scrap_") && ItemData.Of(key) > 0 && ModItems.ByKey.TryGetValue(key, out var ore)) return $"{ore.itemName} (${ItemData.Of(key)})"; // (#208: what it sells for)
            int ench = Enchants.EnchOf(ItemData.Of(key));
            var trim = ArmorTrims.Describe(key); // (#224)
            if (ModItems.ByKey.TryGetValue(key, out var it))
            {
                var extra = string.Join("; ", new[] { Enchants.Any(ench) ? Enchants.Describe(ench) : null, trim, Fireworks.Describe(key) }.Where(x => x != null)); // (#244: a rocket's flight and stars)
                return extra.Length > 0 ? $"{it.itemName} ({extra})" : it.itemName;
            }
            var b = Blocks.Get(ItemData.TypeOf(key));
            return b != null ? b.Name : key;
        }

        /// <summary>
        /// Server, the furnace screen (#83): items into a slot (0 smelting, 1 fuel; -1: wherever they go, a shift-click).
        /// Returns what comes back: what didn't fit, or what was there before (a swap).
        /// </summary>
        public static (string back, int n) ServerPut(BlockKey k, int slot, string given, int n, bool swap)
        {
            var world = BlockWorld.Instance;
            if (world == null || world.DefAt(k) != Blocks.Furnace || given == null || n <= 0) return (given, n);
            string key = Plain(given); // (raw gold to be smelted: its value goes, like Minecraft's ore into an ingot; what's turned away goes back as it came, #228)
            if (!Furnaces.TryGetValue(k, out var f)) Furnaces[k] = f = new Furnace();
            bool smelt = SmeltResult.ContainsKey(key), fuel = FuelSeconds.ContainsKey(key);
            if (slot < 0) slot = smelt && (f.InCount == 0 || f.In == key) ? 0 : fuel && (f.Fuel == 0 || f.FuelKey == key) ? 1 : -1;
            if (slot == 0 && smelt)
            {
                if (f.InCount == 0 || f.In == key)
                {
                    int t = Mathf.Min(n, 64 - f.InCount);
                    f.In = key; f.InCount += t;
                    Sync(k, f);
                    return (given, n - t);
                }
                if (!swap) return (given, n);
                var old = (f.In, f.InCount);
                f.In = key; f.InCount = Mathf.Min(n, 64);
                Sync(k, f);
                return old;
            }
            if (slot == 1 && fuel)
            {
                if (f.Fuel == 0 || f.FuelKey == key)
                {
                    int t = Mathf.Min(n, 64 - f.Fuel);
                    f.FuelKey = key; f.Fuel += t;
                    Sync(k, f);
                    return (given, n - t);
                }
                if (!swap) return (given, n);
                var old = (f.FuelKey, f.Fuel);
                f.FuelKey = key; f.Fuel = Mathf.Min(n, 64);
                Sync(k, f);
                return old;
            }
            return (given, n);
        }

        /// <summary>Host: one of a furnace's results, for a hopper under it (#160); null: none.</summary>
        public static string ServerTakeResult(BlockKey k)
        {
            if (!Furnaces.TryGetValue(k, out var f) || f.OutCount <= 0 || f.Out == null) return null;
            string key = f.Out;
            f.OutCount--; if (f.OutCount == 0) f.Out = null;
            Sync(k, f);
            return key;
        }

        /// <summary>Server, the furnace screen: items out of a slot (taking the result gives experience, like Minecraft).</summary>
        public static (string key, int n) ServerTakeSlot(ulong client, BlockKey k, int slot, int n)
        {
            if (!Furnaces.TryGetValue(k, out var f) || n <= 0) return (null, 0);
            string key; int got;
            switch (slot)
            {
                case 0: key = f.In; got = Mathf.Min(n, f.InCount); f.InCount -= got; if (f.InCount == 0) f.In = null; break;
                case 1: key = f.FuelKey; got = Mathf.Min(n, f.Fuel); f.Fuel -= got; if (f.Fuel == 0) f.FuelKey = null; break;
                case 2:
                    key = f.Out; got = Mathf.Min(n, f.OutCount); f.OutCount -= got; if (f.OutCount == 0) f.Out = null;
                    if (got > 0)
                    {
                        // (#244) Minecraft's XP per recipe, its fraction a point by chance (AbstractFurnaceBlockEntity.createExperience)
                        float tot = got * SmeltXpOf(key); int xp = Mathf.FloorToInt(tot);
                        if (Random.value < tot - xp) xp++;
                        if (xp > 0) BlockNet.ServerXp(client, xp, BlockWorld.Instance != null ? BlockWorld.Instance.WorldCenter(k) : Vector3.zero);
                    }
                    break;
                default: return (null, 0);
            }
            if (got > 0) Sync(k, f);
            return (key, got);
        }

        /// <summary>Furnace was broken: everything inside drops.</summary>
        public static void ServerDropContents(BlockKey k, Vector3 pos)
        {
            if (!Furnaces.TryGetValue(k, out var f)) return;
            void Drop(string key, int n)
            {
                if (key == null || n <= 0 || !ModItems.ByKey.TryGetValue(key, out var item)) return;
                if (item.spawnPrefab.GetComponent<StackItem>() != null) ItemNames.ServerApply(ModItems.ServerSpawnStack(item, n, pos), key);
                else for (int i = 0; i < n; i++) ModItems.ServerSpawnPlainKeyed(key, pos);
            }
            Drop(f.In, f.InCount); Drop(f.FuelKey, f.Fuel); Drop(f.Out, f.OutCount);
            Furnaces.Remove(k);
            BlockNet.ServerFurnace(k, null);
        }

        public static void ServerTick(float dt)
        {
            var world = BlockWorld.Instance;
            if (world == null || Furnaces.Count == 0) return;
            syncTimer -= dt;
            bool syncNow = syncTimer <= 0f;
            if (syncNow) syncTimer = 0.5f;
            foreach (var kv in Furnaces.ToList())
            {
                var k = kv.Key; var f = kv.Value;
                var bi = world.Get(k);
                if (bi == null || bi.Data.Def != Blocks.Furnace) { Furnaces.Remove(k); continue; }
                bool wasLit = f.Lit;
                string result = f.InCount > 0 && SmeltResult.TryGetValue(f.In, out var r) ? r : null;
                bool canOut = result != null && (f.OutCount == 0 || (f.Out == result && f.OutCount < 64));
                if (canOut && f.Burn <= 0f && f.Fuel > 0 && FuelSeconds.ContainsKey(f.FuelKey ?? ""))
                {
                    bool lava = ItemData.Is(f.FuelKey, "lava_bucket");
                    f.Fuel--;
                    f.BurnMax = f.Burn = FuelSeconds.TryGetValue(f.FuelKey ?? "", out var fs) ? fs : 4f;
                    if (f.Fuel == 0) { f.FuelKey = lava ? "bucket" : null; if (lava) f.Fuel = 1; } // (#244: Minecraft's: its bucket stays)
                }
                if (f.Burn > 0f)
                {
                    f.Burn -= dt;
                    if (canOut)
                    {
                        f.Progress += dt;
                        if (f.Progress >= SmeltSeconds)
                        {
                            f.Progress = 0f;
                            f.InCount--;
                            f.Out = result;
                            f.OutCount++;
                            if (f.InCount == 0) f.In = null;
                            BlockNet.ServerSound(world.WorldCenter(k), "pop", 1.5f, 0.4f);
                        }
                    }
                }
                else f.Progress = 0f;
                if (f.Lit != wasLit)
                {
                    var d = bi.Data; d.State = (byte)((d.State & ~1) | (f.Lit ? 1 : 0));
                    BlockNet.ServerBroadcastOp(Op.State(k, d));
                    Sync(k, f);
                }
                else if (syncNow && (f.Lit || f.InCount > 0)) Sync(k, f);
            }
        }

        static void Sync(BlockKey k, Furnace f) => BlockNet.ServerFurnace(k, f);

        // ------------------------------------------------------------------ inventory (client side, local player)
        public static string KeyOf(GrabbableObject g)
        {
            if (g == null || g.itemProperties == null) return null;
            if (g is StackItem st && !string.IsNullOrEmpty(st.ItemKey)) return ItemData.WithName(ItemData.With(st.ItemKey, StackData.Of(g)), ItemNames.IdOf(g)); // (#228: its name too; #244: its number)
            var n = g.itemProperties.name;
            if (n == null || !n.StartsWith("LMC_")) return null;
            // a worn tool's key carries its wear, wherever it's moved to (#56); an ore's its value (#208: a chest gave
            // raw gold, diamonds and emeralds back worth $0)
            int data = g is OreScrapItem ore ? ore.scrapValue : g is ToolItem t ? t.GetItemDataToSave() : g is ArmorItem ai ? ai.GetItemDataToSave() : g is DiscItem dsc ? dsc.GetItemDataToSave() : g is SpawnEggItem egg ? egg.GetItemDataToSave() : g is BookItem bk ? bk.GetItemDataToSave() : g is BannerItem bnr ? bnr.GetItemDataToSave() : 0;
            return ItemData.WithName(data > 0 ? ItemData.With(n.Substring(4), data) : n.Substring(4), ItemNames.IdOf(g));
        }

        public static int CountOf(GrabbableObject g) => g is StackItem st ? Mathf.Max(0, st.Count) : 1;

        /// <summary>A key as the furnace keeps it: an ore's value left off (#208: "scrap_diamond_ore#87" is a diamond) and
        /// its name (#228), a tool's wear kept. (What it is: ItemData.TypeOf.)</summary>
        public static string Plain(string key) => key != null && key.StartsWith("scrap_") ? ItemData.Base(key) : ItemData.Unnamed(key);

        public static int Have(PlayerControllerB p, string key)
        {
            int n = 0;
            foreach (var g in p.ItemSlots) if (g != null && ItemData.Is(KeyOf(g), key)) n += CountOf(g); // (#228: worn, valued or named, by type)
            return n;
        }

        public static bool CanCraft(PlayerControllerB p, Recipe r, int times = 1) => r.Needs.All(n => Have(p, n.key) >= n.n * times);

        /// <summary>Owner client: removes the ingredients from the hotbar, then asks the server for the result(s).</summary>
        public static bool TryCraft(PlayerControllerB p, Recipe r, int times = 1)
        {
            if (times < 1 || !CanCraft(p, r, times)) return false;
            foreach (var (key, per) in r.Needs)
            {
                int left = per * times;
                for (int i = 0; i < p.ItemSlots.Length && left > 0; i++)
                {
                    var g = p.ItemSlots[i];
                    if (g == null || !ItemData.Is(KeyOf(g), key)) continue;
                    int c = CountOf(g);
                    int take = Mathf.Min(c, left);
                    left -= take;
                    if (take >= c) p.DestroyItemInSlotAndSync(i);
                    else BlockNet.RequestConsume((StackItem)g, take);
                }
            }
            BlockNet.RequestCraft(Recipes.IndexOf(r), times);
            return true;
        }

        /// <summary>Server: hand the crafted result to the player (dropped at their feet).</summary>
        public static void ServerCraft(ulong client, int idx, int times = 1)
        {
            if (idx < 0 || idx >= Recipes.Count) return;
            var r = Recipes[idx];
            if (!Allowed(r)) return;
            var p = ServerLogic.PlayerFor(client);
            if (p == null || !ModItems.ByKey.TryGetValue(r.Result, out var item)) return;
            var pos = p.transform.position + Vector3.up * 0.6f;
            int total = r.Count * times;
            if (item.isScrap) for (int i = 0; i < total; i++) ModItems.ServerSpawnScrapItem(r.Result, pos);
            else if (item.spawnPrefab.GetComponent<StackItem>() != null)
                for (int left = total; left > 0; left -= 64) ModItems.ServerSpawnStack(item, Mathf.Min(64, left), pos);
            else for (int i = 0; i < total; i++) ModItems.ServerSpawnPlain(item, pos);
            BlockNet.ServerSound(pos, "pop", 1.2f, 0.6f);
        }
    }
}
