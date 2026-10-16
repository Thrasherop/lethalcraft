using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>Builds the block texture atlas + materials (HDRP).</summary>
    public static class Atlas
    {
        public const int Tile = 16;
        public const int Cols = 32; // (1024 tiles: at 16 the atlas was full, and new tiles pushed fire animation frames out)
        public static Texture2D Texture, Emission;
        public static Dictionary<string, int> Tiles = new Dictionary<string, int>();
        public static Material Opaque, Cutout, Emissive, CutoutEmissive, Crack, Outline, Particle, OpaqueAmb, CutoutAmb, Water, Portal, Gel;
        public static float AmbientEV = 1f;
        public static float EmissiveEV = 5f;
        public static Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        static Texture2D genAtlas, genEmit;

        // our tile name -> (minecraft texture path, tint)
        static readonly Dictionary<string, (string path, Color32? tint)> McMap = new Dictionary<string, (string, Color32?)>
        {
            ["grass_top"] = ("block/grass_block_top", new Color32(124, 189, 91, 255)),
            ["water"] = ("block/water_still", new Color32(63, 118, 228, 255)), // (grey in the jar: Minecraft tints it blue)
            ["farmland"] = ("block/farmland", null), ["farmland_moist"] = ("block/farmland_moist", null), ["sugar_cane"] = ("block/sugar_cane", null),
            ["wheat_stage0"] = ("block/wheat_stage0", null), ["wheat_stage1"] = ("block/wheat_stage1", null), ["wheat_stage2"] = ("block/wheat_stage2", null), ["wheat_stage3"] = ("block/wheat_stage3", null),
            ["wheat_stage4"] = ("block/wheat_stage4", null), ["wheat_stage5"] = ("block/wheat_stage5", null), ["wheat_stage6"] = ("block/wheat_stage6", null), ["wheat_stage7"] = ("block/wheat_stage7", null),
            ["item_wheat"] = ("item/wheat", null), ["item_wheat_seeds"] = ("item/wheat_seeds", null), ["item_sugar_cane"] = ("item/sugar_cane", null), ["item_paper"] = ("item/paper", null), ["item_book"] = ("item/book", null),
            ["item_wooden_hoe"] = ("item/wooden_hoe", null), ["item_stone_hoe"] = ("item/stone_hoe", null), ["item_iron_hoe"] = ("item/iron_hoe", null), ["item_diamond_hoe"] = ("item/diamond_hoe", null),
            ["carrots_stage0"] = ("block/carrots_stage0", null), ["carrots_stage1"] = ("block/carrots_stage1", null), ["carrots_stage2"] = ("block/carrots_stage2", null), ["carrots_stage3"] = ("block/carrots_stage3", null),
            ["potatoes_stage0"] = ("block/potatoes_stage0", null), ["potatoes_stage1"] = ("block/potatoes_stage1", null), ["potatoes_stage2"] = ("block/potatoes_stage2", null), ["potatoes_stage3"] = ("block/potatoes_stage3", null),
            ["food_carrot"] = ("item/carrot", null), ["food_potato"] = ("item/potato", null), ["food_baked_potato"] = ("item/baked_potato", null), ["item_sugar"] = ("item/sugar", null),
            ["item_elytra"] = ("item/elytra", null), ["item_firework_rocket"] = ("item/firework_rocket", null),
            ["item_bucket"] = ("item/bucket", null), ["item_water_bucket"] = ("item/water_bucket", null), ["item_lava_bucket"] = ("item/lava_bucket", null),
            ["grass_side"] = ("block/grass_block_side", null),
            ["dirt"] = ("block/dirt", null),
            ["stone"] = ("block/stone", null),
            ["cobblestone"] = ("block/cobblestone", null),
            ["oak_planks"] = ("block/oak_planks", null),
            ["log_side"] = ("block/oak_log", null),
            ["log_top"] = ("block/oak_log_top", null),
            ["glass"] = ("block/glass", null),
            ["sand"] = ("block/sand", null),
            ["gravel"] = ("block/gravel", null),
            ["bricks"] = ("block/bricks", null),
            ["tnt_side"] = ("block/tnt_side", null),
            ["tnt_top"] = ("block/tnt_top", null),
            ["tnt_bottom"] = ("block/tnt_bottom", null),
            ["piston_side"] = ("block/piston_side", null),
            ["piston_top"] = ("block/piston_top", null),
            ["sticky_top"] = ("block/piston_top_sticky", null),
            ["piston_bottom"] = ("block/piston_bottom", null),
            ["piston_inner"] = ("block/piston_inner", null),
            ["redstone_block"] = ("block/redstone_block", null),
            ["lamp_off"] = ("block/redstone_lamp", null),
            ["lamp_on"] = ("block/redstone_lamp_on", null),
            ["glowstone"] = ("block/glowstone", null),
            ["obsidian"] = ("block/obsidian", null),
            ["diamond_ore"] = ("block/diamond_ore", null),
            ["gold_ore"] = ("block/gold_ore", null),
            ["iron_ore"] = ("block/iron_ore", null),
            ["coal_ore"] = ("block/coal_ore", null),
            ["emerald_ore"] = ("block/emerald_ore", null),
            ["redstone_ore"] = ("block/redstone_ore", null),
            ["lapis_ore"] = ("block/lapis_ore", null), ["item_lapis_lazuli"] = ("item/lapis_lazuli", null),
            ["enchanting_table_top"] = ("block/enchanting_table_top", null), ["enchanting_table_side"] = ("block/enchanting_table_side", null),
            ["enchanting_table_bottom"] = ("block/enchanting_table_bottom", null),
            ["lava"] = ("block/lava_still", null),
            // brewing (#88)
            ["nether_wart_stage0"] = ("block/nether_wart_stage0", null), ["nether_wart_stage1"] = ("block/nether_wart_stage1", null), ["nether_wart_stage2"] = ("block/nether_wart_stage2", null),
            ["item_nether_wart"] = ("item/nether_wart", null), ["item_glass_bottle"] = ("item/glass_bottle", null), ["item_blaze_powder"] = ("item/blaze_powder", null),
            ["item_magma_cream"] = ("item/magma_cream", null), ["item_gold_nugget"] = ("item/gold_nugget", null), ["food_golden_carrot"] = ("item/golden_carrot", null),
            ["brewing_stand"] = ("block/brewing_stand", null), ["brewing_stand_base"] = ("block/brewing_stand_base", null), ["item_brewing_stand"] = ("item/brewing_stand", null),
            // the Nether (#66)
            ["item_bone"] = ("item/bone", null), ["item_bone_meal"] = ("item/bone_meal", null), ["item_string"] = ("item/string", null), ["item_arrow"] = ("item/arrow", null), ["item_bow"] = ("item/bow", null),
            ["item_blaze_rod"] = ("item/blaze_rod", null), ["item_quartz"] = ("item/quartz", null), ["item_nether_brick"] = ("item/nether_brick", null), ["item_netherite_scrap"] = ("item/netherite_scrap", null), ["item_netherite_ingot"] = ("item/netherite_ingot", null),
            ["item_netherite_pickaxe"] = ("item/netherite_pickaxe", null), ["item_netherite_shovel"] = ("item/netherite_shovel", null), ["item_netherite_axe"] = ("item/netherite_axe", null),
            ["item_netherite_hoe"] = ("item/netherite_hoe", null), ["item_netherite_sword"] = ("item/netherite_sword", null), ["item_fire_charge"] = ("item/fire_charge", null),
            ["nether_bricks"] = ("block/nether_bricks", null), ["cracked_nether_bricks"] = ("block/cracked_nether_bricks", null), ["chiseled_nether_bricks"] = ("block/chiseled_nether_bricks", null),
            ["netherrack"] = ("block/netherrack", null), ["soul_sand"] = ("block/soul_sand", null), ["magma"] = ("block/magma", null),
            ["nether_quartz_ore"] = ("block/nether_quartz_ore", null), ["ancient_debris_side"] = ("block/ancient_debris_side", null), ["ancient_debris_top"] = ("block/ancient_debris_top", null),
            ["crimson_nylium"] = ("block/crimson_nylium", null), ["crimson_nylium_side"] = ("block/crimson_nylium_side", null), ["crimson_stem"] = ("block/crimson_stem", null), ["crimson_stem_top"] = ("block/crimson_stem_top", null),
            ["shroomlight"] = ("block/shroomlight", null), ["warped_nylium"] = ("block/warped_nylium", null), ["warped_nylium_side"] = ("block/warped_nylium_side", null),
            ["warped_stem"] = ("block/warped_stem", null), ["warped_stem_top"] = ("block/warped_stem_top", null), ["warped_wart_block"] = ("block/warped_wart_block", null),
            ["magma"] = ("block/magma", null),
            ["soul_soil"] = ("block/soul_soil", null), ["basalt_side"] = ("block/basalt_side", null), ["basalt_top"] = ("block/basalt_top", null),
            ["bone_block_side"] = ("block/bone_block_side", null), ["bone_block_top"] = ("block/bone_block_top", null),
            ["blackstone"] = ("block/blackstone", null), ["blackstone_top"] = ("block/blackstone_top", null), ["polished_blackstone_bricks"] = ("block/polished_blackstone_bricks", null),
            ["cracked_polished_blackstone_bricks"] = ("block/cracked_polished_blackstone_bricks", null), ["gilded_blackstone"] = ("block/gilded_blackstone", null), ["spawner"] = ("block/spawner", null), ["nether_wart_block"] = ("block/nether_wart_block", null),
            ["diamond_block"] = ("block/diamond_block", null),
            ["gold_block"] = ("block/gold_block", null),
            ["iron_block"] = ("block/iron_block", null),
            ["leaves"] = ("block/oak_leaves", new Color32(98, 160, 50, 255)),
            ["vine"] = ("block/vine", new Color32(72, 145, 30, 255)), ["item_vine"] = ("block/vine", new Color32(72, 145, 30, 255)), // (#158: grey in the jar, tinted like leaves)
            ["wool_white"] = ("block/white_wool", null),
["wool_orange"] = ("block/orange_wool", null), ["wool_magenta"] = ("block/magenta_wool", null), ["wool_light_blue"] = ("block/light_blue_wool", null), ["wool_lime"] = ("block/lime_wool", null), ["wool_pink"] = ("block/pink_wool", null), ["wool_gray"] = ("block/gray_wool", null), ["wool_light_gray"] = ("block/light_gray_wool", null), ["wool_cyan"] = ("block/cyan_wool", null), ["wool_purple"] = ("block/purple_wool", null), ["wool_brown"] = ("block/brown_wool", null), ["wool_green"] = ("block/green_wool", null), ["wool_black"] = ("block/black_wool", null),
            ["wool_red"] = ("block/red_wool", null),
            ["wool_blue"] = ("block/blue_wool", null),
            ["wool_yellow"] = ("block/yellow_wool", null),
            ["ladder"] = ("block/ladder", null),
            ["note_block"] = ("block/note_block", null),
            ["jack_face"] = ("block/jack_o_lantern", null),
            ["carved_pumpkin_face"] = ("block/carved_pumpkin", null),
            ["pumpkin_stem"] = ("block/pumpkin_stem", new Color32(110, 190, 50, 255)), ["attached_pumpkin_stem"] = ("block/attached_pumpkin_stem", new Color32(200, 170, 40, 255)),
            ["item_pumpkin_seeds"] = ("item/pumpkin_seeds", null), ["item_shears"] = ("item/shears", null),
            ["pumpkin_side"] = ("block/pumpkin_side", null),
            ["pumpkin_top"] = ("block/pumpkin_top", null),
            ["ice"] = ("block/ice", null),
            ["slime"] = ("block/slime_block", null),
            ["bookshelf"] = ("block/bookshelf", null),
            ["oak_planks_dark"] = ("block/dark_oak_planks", null),
            // (#223) the woods' leaves, tinted as Minecraft does (birch and mangrove their own colours, dark oak the foliage's),
            // and their doors' items
            ["birch_leaves"] = ("block/birch_leaves", new Color32(0x80, 0xA7, 0x55, 255)), ["dark_oak_leaves"] = ("block/dark_oak_leaves", new Color32(98, 160, 50, 255)),
            ["mangrove_leaves"] = ("block/mangrove_leaves", new Color32(0x92, 0xC6, 0x48, 255)),
            ["item_birch_door"] = ("item/birch_door", null), ["item_dark_oak_door"] = ("item/dark_oak_door", null), ["item_mangrove_door"] = ("item/mangrove_door", null),
            ["stone_bricks"] = ("block/stone_bricks", null),
            ["diorite"] = ("block/diorite", null),
            ["glass_pane_top"] = ("block/glass_pane_top", null),
            ["item_glass_pane"] = ("block/glass", null), // (the pane's icon is flat glass, as in Minecraft)
            ["oak_door_bottom"] = ("block/oak_door_bottom", null),
            ["oak_trapdoor"] = ("block/oak_trapdoor", null),
            ["iron_door_bottom"] = ("block/iron_door_bottom", null), ["iron_door_top"] = ("block/iron_door_top", null), ["item_iron_door"] = ("item/iron_door", null),
            ["iron_trapdoor"] = ("block/iron_trapdoor", null),
            ["item_tripwire_hook"] = ("block/tripwire_hook", null),
            ["item_raw_copper"] = ("item/raw_copper", null), ["item_copper_ingot"] = ("item/copper_ingot", null),
            ["hopper_outside"] = ("block/hopper_outside", null), ["hopper_top"] = ("block/hopper_top", null), ["hopper_inside"] = ("block/hopper_inside", null), ["item_hopper"] = ("item/hopper", null),
            ["crafter_top"] = ("block/crafter_top", null), ["crafter_bottom"] = ("block/crafter_bottom", null), ["crafter_north"] = ("block/crafter_north", null), ["crafter_east"] = ("block/crafter_east", null),
            ["composter_side"] = ("block/composter_side", null), ["composter_top"] = ("block/composter_top", null), ["composter_bottom"] = ("block/composter_bottom", null),
            ["composter_compost"] = ("block/composter_compost", null), ["composter_ready"] = ("block/composter_ready", null),
            ["cauldron_side"] = ("block/cauldron_side", null), ["cauldron_top"] = ("block/cauldron_top", null), ["cauldron_bottom"] = ("block/cauldron_bottom", null), ["cauldron_inner"] = ("block/cauldron_inner", null),
            ["item_cauldron"] = ("item/cauldron", null),
            ["rail"] = ("block/rail", null), ["rail_corner"] = ("block/rail_corner", null), ["powered_rail"] = ("block/powered_rail", null), ["powered_rail_on"] = ("block/powered_rail_on", null),
            ["detector_rail"] = ("block/detector_rail", null), ["detector_rail_on"] = ("block/detector_rail_on", null), ["activator_rail"] = ("block/activator_rail", null), ["activator_rail_on"] = ("block/activator_rail_on", null),
            ["item_rail"] = ("block/rail", null), ["item_powered_rail"] = ("block/powered_rail", null), ["item_detector_rail"] = ("block/detector_rail", null), ["item_activator_rail"] = ("block/activator_rail", null),
            ["item_minecart"] = ("item/minecart", null), ["item_chest_minecart"] = ("item/chest_minecart", null), ["item_hopper_minecart"] = ("item/hopper_minecart", null),
            ["item_tnt_minecart"] = ("item/tnt_minecart", null), ["item_furnace_minecart"] = ("item/furnace_minecart", null),
            ["lectern_base"] = ("block/lectern_base", null), ["lectern_front"] = ("block/lectern_front", null), ["lectern_sides"] = ("block/lectern_sides", null), ["lectern_top"] = ("block/lectern_top", null),
            ["item_writable_book"] = ("item/writable_book", null), ["item_written_book"] = ("item/written_book", null), ["item_lectern"] = ("item/lectern", null),
            ["chiseled_bookshelf_side"] = ("block/chiseled_bookshelf_side", null), ["chiseled_bookshelf_top"] = ("block/chiseled_bookshelf_top", null),
            ["chiseled_bookshelf_empty"] = ("block/chiseled_bookshelf_empty", null), ["chiseled_bookshelf_occupied"] = ("block/chiseled_bookshelf_occupied", null),
            ["dropper_front"] = ("block/dropper_front", null), ["dispenser_front"] = ("block/dispenser_front", null),
            ["dropper_front_vertical"] = ("block/dropper_front_vertical", null), ["dispenser_front_vertical"] = ("block/dispenser_front_vertical", null), ["tripwire"] = ("block/tripwire", null),
            ["barrel_side"] = ("block/barrel_side", null), ["barrel_top"] = ("block/barrel_top", null), ["barrel_bottom"] = ("block/barrel_bottom", null),
            ["hay_block_side"] = ("block/hay_block_side", null), ["hay_block_top"] = ("block/hay_block_top", null),
            ["target_side"] = ("block/target_side", null), ["target_top"] = ("block/target_top", null),
            ["daylight_detector_top"] = ("block/daylight_detector_top", null), ["daylight_detector_inverted_top"] = ("block/daylight_detector_inverted_top", null), ["daylight_detector_side"] = ("block/daylight_detector_side", null),
            ["honey_block_top"] = ("block/honey_block_top", null), ["honey_block_side"] = ("block/honey_block_side", null),
            ["honey_block_bottom"] = ("block/honey_block_bottom", null),
            ["jukebox_top"] = ("block/jukebox_top", null), ["jukebox_side"] = ("block/jukebox_side", null),
            ["item_music_disc_13"] = ("item/music_disc_13", null), ["item_music_disc_cat"] = ("item/music_disc_cat", null),
            ["item_music_disc_blocks"] = ("item/music_disc_blocks", null), ["item_music_disc_chirp"] = ("item/music_disc_chirp", null),
            ["item_music_disc_far"] = ("item/music_disc_far", null), ["item_music_disc_mall"] = ("item/music_disc_mall", null),
            ["item_music_disc_mellohi"] = ("item/music_disc_mellohi", null), ["item_music_disc_stal"] = ("item/music_disc_stal", null),
            ["item_music_disc_strad"] = ("item/music_disc_strad", null), ["item_music_disc_ward"] = ("item/music_disc_ward", null),
            ["item_music_disc_11"] = ("item/music_disc_11", null), ["item_music_disc_wait"] = ("item/music_disc_wait", null),
            ["item_music_disc_pigstep"] = ("item/music_disc_pigstep", null), ["item_music_disc_otherside"] = ("item/music_disc_otherside", null),
            ["item_music_disc_5"] = ("item/music_disc_5", null), ["item_music_disc_relic"] = ("item/music_disc_relic", null),
            ["item_music_disc_creator"] = ("item/music_disc_creator", null), ["item_music_disc_creator_music_box"] = ("item/music_disc_creator_music_box", null),
            ["item_music_disc_precipice"] = ("item/music_disc_precipice", null), ["item_music_disc_tears"] = ("item/music_disc_tears", null),
            ["item_music_disc_lava_chicken"] = ("item/music_disc_lava_chicken", null),
            ["oak_door_top"] = ("block/oak_door_top", null),
            ["item_oak_door"] = ("item/oak_door", null),
            // (#169: the copper doors' flat icons, a stage each; the waxed ones look the same)
            ["item_copper_door"] = ("item/copper_door", null), ["item_exposed_copper_door"] = ("item/exposed_copper_door", null),
            ["item_weathered_copper_door"] = ("item/weathered_copper_door", null), ["item_oxidized_copper_door"] = ("item/oxidized_copper_door", null),
            ["item_ladder"] = ("block/ladder", null),
            ["item_oak_stairs"] = ("block/oak_planks", null), ["item_cobblestone_stairs"] = ("block/cobblestone", null), ["item_stone_brick_stairs"] = ("block/stone_bricks", null),
            ["bedrock"] = ("block/bedrock", null),
            ["crafting_table_top"] = ("block/crafting_table_top", null),
            ["crafting_table_front"] = ("block/crafting_table_front", null),
            ["crafting_table_side"] = ("block/crafting_table_side", null),
            ["furnace_front"] = ("block/furnace_front", null),
            ["furnace_front_on"] = ("block/furnace_front_on", null),
            ["furnace_side"] = ("block/furnace_side", null),
            ["furnace_top"] = ("block/furnace_top", null),
            ["fire_0"] = ("block/fire_0", null),
            ["observer_front"] = ("block/observer_front", null),
            ["observer_side"] = ("block/observer_side", null),
            ["observer_top"] = ("block/observer_top", null),
            ["observer_back"] = ("block/observer_back", null),
            ["observer_back_on"] = ("block/observer_back_on", null),
            ["item_wooden_pickaxe"] = ("item/wooden_pickaxe", null),
            ["item_totem_of_undying"] = ("item/totem_of_undying", null),
            ["item_wooden_shovel"] = ("item/wooden_shovel", null),
            ["item_wooden_axe"] = ("item/wooden_axe", null),
            ["item_stone_pickaxe"] = ("item/stone_pickaxe", null),
            ["item_iron_pickaxe"] = ("item/iron_pickaxe", null),
            ["item_stone_shovel"] = ("item/stone_shovel", null),
            ["item_iron_shovel"] = ("item/iron_shovel", null),
            ["item_diamond_shovel"] = ("item/diamond_shovel", null),
            ["item_stone_axe"] = ("item/stone_axe", null),
            ["item_iron_axe"] = ("item/iron_axe", null),
            ["item_diamond_axe"] = ("item/diamond_axe", null),
            ["item_stick"] = ("item/stick", null),
            ["item_coal"] = ("item/coal", null),
            ["item_iron_ingot"] = ("item/iron_ingot", null),
            ["item_gold_ingot"] = ("item/gold_ingot", null),
            ["item_diamond"] = ("item/diamond", null),
            ["item_wooden_sword"] = ("item/wooden_sword", null), ["item_stone_sword"] = ("item/stone_sword", null), ["item_iron_sword"] = ("item/iron_sword", null),
            ["item_golden_sword"] = ("item/golden_sword", null), ["item_golden_axe"] = ("item/golden_axe", null), ["item_diamond_sword"] = ("item/diamond_sword", null),
            ["item_golden_helmet"] = ("item/golden_helmet", null), ["item_golden_chestplate"] = ("item/golden_chestplate", null),
            ["item_golden_leggings"] = ("item/golden_leggings", null), ["item_golden_boots"] = ("item/golden_boots", null),
            ["item_iron_helmet"] = ("item/iron_helmet", null), ["item_iron_chestplate"] = ("item/iron_chestplate", null),
            ["item_iron_leggings"] = ("item/iron_leggings", null), ["item_iron_boots"] = ("item/iron_boots", null),
            ["item_diamond_helmet"] = ("item/diamond_helmet", null), ["item_diamond_chestplate"] = ("item/diamond_chestplate", null),
            ["item_diamond_leggings"] = ("item/diamond_leggings", null), ["item_diamond_boots"] = ("item/diamond_boots", null),
            ["item_netherite_helmet"] = ("item/netherite_helmet", null), ["item_netherite_chestplate"] = ("item/netherite_chestplate", null),
            ["item_netherite_leggings"] = ("item/netherite_leggings", null), ["item_netherite_boots"] = ("item/netherite_boots", null),
            ["item_netherite_upgrade_smithing_template"] = ("item/netherite_upgrade_smithing_template", null),
            ["smithing_table_top"] = ("block/smithing_table_top", null), ["smithing_table_side"] = ("block/smithing_table_side", null),
            ["smithing_table_front"] = ("block/smithing_table_front", null), ["smithing_table_bottom"] = ("block/smithing_table_bottom", null),
            ["gui_smithing"] = ("gui/container/smithing", null),
            ["slot_helmet"] = ("gui/sprites/container/slot/helmet", null), ["slot_chestplate"] = ("gui/sprites/container/slot/chestplate", null),
            ["slot_leggings"] = ("gui/sprites/container/slot/leggings", null), ["slot_boots"] = ("gui/sprites/container/slot/boots", null),
            ["coal_block"] = ("block/coal_block", null),
            ["item_ender_pearl"] = ("item/ender_pearl", null),
            ["item_slime_ball"] = ("item/slime_ball", null),
            ["item_gunpowder"] = ("item/gunpowder", null),
            ["snow"] = ("block/snow", null),
            ["item_torch"] = ("block/torch", null),
            ["redstone_torch"] = ("block/redstone_torch", null),
            ["redstone_torch_off"] = ("block/redstone_torch_off", null),
            ["repeater"] = ("block/repeater", null), ["repeater_on"] = ("block/repeater_on", null), ["item_repeater"] = ("item/repeater", null),
            ["comparator"] = ("block/comparator", null), ["comparator_on"] = ("block/comparator_on", null), ["item_comparator"] = ("item/comparator", null),
            ["lever_handle"] = ("block/lever", null),
            ["item_pickaxe"] = ("item/diamond_pickaxe", null),
            ["item_flint_and_steel"] = ("item/flint_and_steel", null),
            ["item_redstone_dust"] = ("item/redstone", null),
            ["food_bread"] = ("item/bread", null),
            ["food_steak"] = ("item/cooked_beef", null),
            ["food_apple"] = ("item/apple", null), ["food_rotten_flesh"] = ("item/rotten_flesh", null),
            ["food_golden_apple"] = ("item/golden_apple", null),
            ["food_cookie"] = ("item/cookie", null),
            ["food_porkchop"] = ("item/cooked_porkchop", null), ["food_raw_porkchop"] = ("item/porkchop", null), ["food_raw_beef"] = ("item/beef", null),
            ["item_leather"] = ("item/leather", null), ["item_saddle"] = ("item/saddle", null), ["item_fishing_rod"] = ("item/fishing_rod", null),
            ["item_flint"] = ("item/flint", null), ["item_rabbit_hide"] = ("item/rabbit_hide", null), ["item_ink_sac"] = ("item/ink_sac", null), ["item_lead"] = ("item/lead", null), ["item_honeycomb"] = ("item/honeycomb", null), ["item_snowball"] = ("item/snowball", null), ["item_phantom_membrane"] = ("item/phantom_membrane", null),
            ["item_leather_horse_armor"] = ("item/leather_horse_armor", null), ["item_leather_horse_armor_overlay"] = ("item/leather_horse_armor_overlay", null), ["item_iron_horse_armor"] = ("item/iron_horse_armor", null),
            ["item_golden_horse_armor"] = ("item/golden_horse_armor", null), ["item_diamond_horse_armor"] = ("item/diamond_horse_armor", null), ["item_rabbit_foot"] = ("item/rabbit_foot", null),
            ["food_raw_mutton"] = ("item/mutton", null), ["food_cooked_mutton"] = ("item/cooked_mutton", null), ["food_raw_rabbit"] = ("item/rabbit", null), ["food_cooked_rabbit"] = ("item/cooked_rabbit", null), ["item_ghast_tear"] = ("item/ghast_tear", null), ["food_spider_eye"] = ("item/spider_eye", null),
            ["item_golden_pickaxe"] = ("item/golden_pickaxe", null), ["item_golden_shovel"] = ("item/golden_shovel", null),
            ["item_golden_axe"] = ("item/golden_axe", null), ["item_golden_hoe"] = ("item/golden_hoe", null), ["item_feather"] = ("item/feather", null),
            ["item_bow_pulling_0"] = ("item/bow_pulling_0", null), ["item_bow_pulling_1"] = ("item/bow_pulling_1", null), ["item_bow_pulling_2"] = ("item/bow_pulling_2", null),
            // (#223) the crossbow: at rest, its pull frames, loaded
            ["item_ominous_bottle"] = ("item/ominous_bottle", null), ["item_experience_bottle"] = ("item/experience_bottle", null), ["item_goat_horn"] = ("item/goat_horn", null),
            ["item_sentry_armor_trim_smithing_template"] = ("item/sentry_armor_trim_smithing_template", null),
            ["item_crossbow_standby"] = ("item/crossbow_standby", null), ["item_crossbow_arrow"] = ("item/crossbow_arrow", null),
            ["item_crossbow_pulling_0"] = ("item/crossbow_pulling_0", null), ["item_crossbow_pulling_1"] = ("item/crossbow_pulling_1", null), ["item_crossbow_pulling_2"] = ("item/crossbow_pulling_2", null),
            ["food_raw_chicken"] = ("item/chicken", null), ["food_cooked_chicken"] = ("item/cooked_chicken", null),
            ["food_raw_cod"] = ("item/cod", null), ["food_raw_salmon"] = ("item/salmon", null), ["food_cooked_cod"] = ("item/cooked_cod", null),
            ["food_cooked_salmon"] = ("item/cooked_salmon", null), ["food_pufferfish"] = ("item/pufferfish", null), ["food_tropical_fish"] = ("item/tropical_fish", null),
            ["scrap_diamond"] = ("item/diamond", null),
            ["scrap_emerald"] = ("item/emerald", null),
            ["scrap_raw_gold"] = ("item/raw_gold", null),
            ["scrap_raw_iron"] = ("item/raw_iron", null),
            ["scrap_coal"] = ("item/coal", null),
        };

        public static int T(string name)
        {
            if (name != null && Tiles.TryGetValue(name, out int i)) return i;
            return Tiles.TryGetValue("stone", out int s) ? s : 0;
        }

        public static Rect UV(string name)
        {
            int i = T(name);
            int col = i % Cols, row = i / Cols;
            float s = 1f / Cols;
            // row 0 is the TOP of the image; Unity UV origin is bottom-left
            const float inset = 0.0005f;
            return new Rect(col * s + inset, 1f - (row + 1) * s + inset, s - 2 * inset, s - 2 * inset);
        }

        static Texture2D LoadEmbedded(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream(name))
            {
                if (st == null) return null;
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    t.LoadImage(ms.ToArray(), false);
                    t.filterMode = FilterMode.Point;
                    return t;
                }
            }
        }

        public static Texture2D LoadEmbeddedIcon(string name) => LoadEmbedded("icons." + name + ".png");

        static string ReadEmbeddedText(string name)
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(st)) return r.ReadToEnd();
        }

        public static void Build()
        {
            genAtlas = LoadEmbedded("atlas.png");
            genEmit = LoadEmbedded("atlas_emit.png");
            var genTiles = new Dictionary<string, int>();
            foreach (var p in JObject.Parse(ReadEmbeddedText("tiles.json")).Properties()) genTiles[p.Name] = (int)p.Value;

            Texture = new Texture2D(Tile * Cols, Tile * Cols, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0, name = "LMC_Atlas" };
            Emission = new Texture2D(Tile * Cols, Tile * Cols, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0, name = "LMC_AtlasEmit" };
            var clear = new Color32[Tile * Cols * Tile * Cols];
            Texture.SetPixels32(clear);
            var black = new Color32[clear.Length];
            for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            Emission.SetPixels32(black);

            // spawn eggs (#85): Minecraft's per-mob eggs
            foreach (var egg in SpawnEggs.McEggs) McMap["egg_" + egg] = ("item/" + egg + "_spawn_egg", null);
            // the block families' textures (#154)
            foreach (var kv in Blocks.FamilyTiles) if (!McMap.ContainsKey(kv.Key)) McMap[kv.Key] = ("block/" + kv.Value, null);
            // all names: generated set + mc-only extras
            var names = new List<string>(genTiles.Keys);
            foreach (var k in McMap.Keys) if (!names.Contains(k)) names.Add(k);
            for (int s = 0; s < 10; s++) if (!names.Contains("crack_" + s)) names.Add("crack_" + s);
            if (McAssets.Available)
                foreach (var dn in new[] { "dust_line_on", "dust_line_off", "dust_dot_on", "dust_dot_off", "dust_cross_on", "dust_cross_off" })
                    names.Add(dn);
            if (McAssets.Available) names.Add("item_wither_skull");
            if (McAssets.Available) names.AddRange(new[] { "decorated_pot_side", "decorated_pot_top", "decorated_pot_neck", "decorated_pot_neck_top" }); // (#160, PotFace)
            if (McAssets.Available) foreach (var bn in BedTiles.Keys) names.Add(bn);
            if (McAssets.Available) foreach (var pk in Potions.Defs.Keys) names.Add("item_" + pk);
            if (McAssets.Available) names.Add("item_" + Banners.OminousKey); // (#223: put together from its patterns)
            if (McAssets.Available) { names.Add("item_arrow_slowness"); names.Add("item_arrow_poison"); foreach (var h in MobHeads.All.Keys) names.Add("item_" + h); }
            if (McAssets.Available)
                for (int f = 0; f < MaxFireFrames; f++) { names.Add("fire_0_f" + f); names.Add("fire_1_f" + f); }
            if (McAssets.Available)
                for (int f = 0; f < MaxPortalFrames; f++) names.Add("portal_f" + f);

            int next = 0;
            int mcCount = 0;
            foreach (var name in names)
            {
                Color32[] px = null;
                Color32[] em = null;
                if (McAssets.Available)
                {
                    px = LoadMcTile(name);
                    if (px != null) mcCount++;
                }
                if (px == null)
                {
                    string gen = name;
                    if (!genTiles.ContainsKey(gen))
                    {
                        // fallbacks for mc-only names
                        if (name == "redstone_torch" || name == "redstone_torch_off") gen = "item_torch";
                        else if (name == "lever_handle") gen = "item_torch";
                        else if (name == "observer_top") gen = "observer_side";
                        else if (name == "fire_0" || name == "lava") gen = "glowstone";
                        else if (name == "observer_back_on") gen = "observer_back";
                        else if (name.StartsWith("food_") || name.StartsWith("scrap_")) gen = null;
                        else gen = null;
                    }
                    if (gen != null && genTiles.TryGetValue(gen, out int gi))
                    {
                        px = ReadTile(genAtlas, gi);
                        em = ReadTile(genEmit, gi);
                    }
                }
                if (px == null) continue;
                if (em == null) em = MakeEmission(name, px);
                Tiles[name] = next;
                WriteTile(Texture, next, px);
                WriteTile(Emission, next, em);
                next++;
                if (next >= Cols * Cols) break;
            }
            Texture.Apply(false, false);
            Emission.Apply(false, false);
            foreach (var t in fireStrips.Values) if (t != null) Object.Destroy(t);
            fireStrips.Clear();
            FireFrames = 0;
            while (FireFrames < MaxFireFrames && Tiles.ContainsKey("fire_0_f" + FireFrames) && Tiles.ContainsKey("fire_1_f" + FireFrames)) FireFrames++;
            PortalFrames = 0;
            while (PortalFrames < MaxPortalFrames && Tiles.ContainsKey("portal_f" + PortalFrames)) PortalFrames++;
            Plugin.Log.LogInfo($"Atlas built: {Tiles.Count} tiles ({mcCount} from Minecraft; fire animation {FireFrames} frames)");

            BuildMaterials();
            BuildIcons();
        }

        static Color32[] MakeEmission(string name, Color32[] px)
        {
            var em = new Color32[px.Length];
            for (int i = 0; i < em.Length; i++) em[i] = new Color32(0, 0, 0, 255);
            bool full = name == "lamp_on" || name == "glowstone" || name.StartsWith("fire_") || name.StartsWith("portal_f") || (name.StartsWith("dust_") && name.EndsWith("_on"));
            bool bright = name == "jack_face" || name == "item_torch" || name == "redstone_torch" || name == "furnace_front_on";
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 10) continue;
                if (full) em[i] = c;
                else if (bright)
                {
                    float lum = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
                    bool warm = c.r > 200 && c.g > 120 && c.b < 140;
                    bool red = name == "redstone_torch" && c.r > 150 && c.g < 80;
                    if ((lum > 0.62f && warm) || red || ((name == "jack_face" || name == "furnace_front_on") && c.r > 200 && c.g > 120)) em[i] = c;
                }
            }
            return em;
        }

        public const int MaxFireFrames = 32;
        /// <summary>Frames of the fire animation in the atlas (0: no animation, fire uses the still tile).</summary>
        public static int FireFrames;
        public const int MaxPortalFrames = 32;
        /// <summary>Frames of the Nether portal's animation in the atlas (0: none, it shows obsidian).</summary>
        public static int PortalFrames;
        static readonly Dictionary<string, Texture2D> fireStrips = new Dictionary<string, Texture2D>();

        /// <summary>Frame n (top to bottom) of an animation strip, scaled to a tile; null past its last frame.</summary>
        static Color32[] StripFrame(Texture2D t, int n)
        {
            int w = t.width, frames = t.height / w;
            if (n >= frames) return null;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    outp[y * Tile + x] = (Color32)t.GetPixel(x * w / Tile, t.height - (n + 1) * w + y * w / Tile);
            return outp;
        }

        static Texture2D potionBottle, potionOverlay;

        /// <summary>A potion's icon, Minecraft's way: the bottle, its liquid (the overlay) tinted the potion's colour.</summary>
        /// <summary>A mob head's icon: its face, from the mob's texture.</summary>
        static Color32[] HeadTile(MobHeads.Head h)
        {
            var t = McAssets.LoadTexture(h.Texture);
            if (t == null) return null;
            // (Minecraft's head layout: the face is the 8 px square right of the side, below the top)
            int fu = h.U + 8, fv = h.V + 8, fw = h.W;
            float sx = t.width / 64f, sy = t.height / (h.Texture.EndsWith("skeleton") || h.Texture.EndsWith("creeper") ? 32f : 64f);
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    int px = (int)((fu + x * fw / (float)Tile) * sx), py = (int)((fv + (Tile - 1 - y) * 8f / Tile) * sy);
                    var c = t.GetPixel(px, t.height - 1 - py); c.a = 1f;
                    outp[y * Tile + x] = c;
                }
            return outp;
        }

        /// <summary>A tipped arrow: Minecraft's arrow base with its head tinted the potion's colour.</summary>
        static Color32[] TippedArrowTile(Color32 tint)
        {
            var b = McAssets.LoadTexture("item/tipped_arrow_base"); var h = McAssets.LoadTexture("item/tipped_arrow_head");
            if (b == null || h == null) return null;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    Color bc = b.GetPixel(x * b.width / Tile, y * b.height / Tile), hc = h.GetPixel(x * h.width / Tile, y * h.height / Tile);
                    var head = new Color(hc.r * tint.r / 255f, hc.g * tint.g / 255f, hc.b * tint.b / 255f, hc.a);
                    outp[y * Tile + x] = hc.a > 0.01f ? (Color32)Color.Lerp(bc, head, hc.a) : (Color32)bc;
                }
            return outp;
        }

        static Color32[] PotionTile(string key)
        {
            if (!Potions.Defs.TryGetValue(key, out var d)) return null;
            if (potionBottle == null) potionBottle = McAssets.LoadTexture("item/potion");
            if (potionOverlay == null) potionOverlay = McAssets.LoadTexture("item/potion_overlay");
            if (potionBottle == null || potionOverlay == null) return null;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    Color b = potionBottle.GetPixel(x * potionBottle.width / Tile, y * potionBottle.height / Tile);
                    Color o = potionOverlay.GetPixel(x * potionOverlay.width / Tile, y * potionOverlay.height / Tile);
                    Color tint = (Color)d.Color;
                    var liquid = new Color(o.r * tint.r, o.g * tint.g, o.b * tint.b, o.a);
                    outp[y * Tile + x] = o.a > 0.01f ? (Color32)Color.Lerp(b, liquid, o.a) : (Color32)b;
                }
            return outp;
        }

        static Texture2D bedTex;

        /// <summary>
        /// The bed's textures, as Minecraft 26.1 has them (its bed became a block model: block/red_bed_head_up and the rest,
        /// #195), and where each comes from in the older bed entity texture (entity/bed/red.png, 64x64), so that a bed looks
        /// the same with any Minecraft: rectangles (x0, x1, y0, y1 in the 16x16 tile, from the top left) taken from the
        /// entity texture at (ox, oy), turned or flipped by k (as BedPx). Found by matching the two, pixel for pixel.
        /// </summary>
        static readonly Dictionary<string, (string block, int[][] rects)> BedTiles = new Dictionary<string, (string, int[][])>
        {
            ["bed_head_up"] = ("red_bed_head_up", new[] { new[] { 0, 16, 0, 16, 0, 6, 6 } }),
            ["bed_foot_up"] = ("red_bed_foot_up", new[] { new[] { 0, 16, 0, 16, 0, 6, 28 } }),
            ["bed_down"] = ("bed_down", new[] { new[] { 0, 16, 0, 16, 3, 28, 6 } }),
            ["bed_head_east"] = ("red_bed_head_east", new[] { new[] { 0, 16, 7, 13, 6, 22, 6 }, new[] { 7, 10, 13, 16, 1, 56, 0 }, new[] { 10, 13, 13, 16, 0, 59, 3 }, new[] { 13, 16, 13, 16, 0, 50, 3 } }),
            ["bed_head_west"] = ("red_bed_head_west", new[] { new[] { 0, 16, 7, 13, 5, 0, 6 }, new[] { 0, 3, 13, 16, 0, 53, 3 }, new[] { 3, 6, 13, 16, 0, 56, 3 }, new[] { 6, 9, 13, 16, 5, 56, 0 } }),
            ["bed_head_north"] = ("bed_head_north", new[] { new[] { 0, 16, 7, 13, 3, 6, 0 }, new[] { 0, 3, 13, 16, 0, 53, 3 }, new[] { 3, 6, 13, 16, 0, 56, 3 }, new[] { 10, 13, 13, 16, 0, 59, 3 }, new[] { 13, 16, 13, 16, 0, 50, 3 } }),
            ["bed_foot_east"] = ("red_bed_foot_east", new[] { new[] { 0, 16, 7, 13, 6, 22, 28 }, new[] { 0, 3, 13, 16, 0, 53, 3 }, new[] { 3, 6, 13, 16, 0, 56, 3 }, new[] { 6, 9, 13, 16, 4, 56, 0 } }),
            ["bed_foot_west"] = ("red_bed_foot_west", new[] { new[] { 0, 16, 7, 13, 5, 0, 28 }, new[] { 7, 10, 13, 16, 0, 56, 0 }, new[] { 10, 13, 13, 16, 0, 59, 3 }, new[] { 13, 16, 13, 16, 0, 50, 3 } }),
            ["bed_foot_south"] = ("red_bed_foot_south", new[] { new[] { 0, 16, 7, 13, 2, 22, 22 }, new[] { 0, 3, 13, 16, 0, 53, 3 }, new[] { 3, 6, 13, 16, 0, 56, 3 }, new[] { 10, 13, 13, 16, 0, 59, 3 }, new[] { 13, 16, 13, 16, 0, 50, 3 } }),
        };

        /// <summary>The bed's textures (#195): Minecraft 26.1's own, or the same cut out of an older Minecraft's bed entity
        /// texture. Tile rows are bottom-up; what the rectangles leave is see-through (the gaps between the legs).</summary>
        static Color32[] BedTile(string name)
        {
            if (!BedTiles.TryGetValue(name, out var spec)) return null;
            var bt = McAssets.LoadTexture("block/" + spec.block);
            var outp = new Color32[Tile * Tile];
            if (bt != null)
            {
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++) outp[y * Tile + x] = bt.GetPixel(x * bt.width / Tile, y * bt.height / Tile);
                return outp;
            }
            if (bedTex == null) bedTex = McAssets.LoadTexture("entity/bed/red");
            var t = bedTex;
            if (t == null) return null;
            float k = t.width / 64f;
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    // (the pixel of the 16x16 texture, from the top left)
                    int u = x * 16 / Tile, v = (Tile - 1 - y) * 16 / Tile;
                    var c = new Color32(0, 0, 0, 0);
                    foreach (var r in spec.rects)
                    {
                        if (u < r[0] || u >= r[1] || v < r[2] || v >= r[3]) continue;
                        int w = r[1] - r[0], h = r[3] - r[2], bx = u - r[0], by = v - r[2], a, b;
                        switch (r[4])
                        {
                            case 1: a = w - 1 - bx; b = by; break;
                            case 2: a = bx; b = h - 1 - by; break;
                            case 3: a = w - 1 - bx; b = h - 1 - by; break;
                            case 4: a = by; b = bx; break;
                            case 5: a = h - 1 - by; b = bx; break;
                            case 6: a = by; b = w - 1 - bx; break;
                            case 7: a = h - 1 - by; b = w - 1 - bx; break;
                            default: a = bx; b = by; break;
                        }
                        c = t.GetPixel(Mathf.FloorToInt((r[5] + a) * k), t.height - 1 - Mathf.FloorToInt((r[6] + b) * k));
                    }
                    outp[y * Tile + x] = c;
                }
            return outp;
        }

        static Color32[] LoadMcTile(string name)
        {
            if (name.StartsWith("fire_0_f") || name.StartsWith("fire_1_f"))
            {
                string strip = name.Substring(0, 6); // fire_0 / fire_1
                if (!fireStrips.TryGetValue(strip, out var st)) fireStrips[strip] = st = McAssets.LoadTexture("block/" + strip);
                if (st == null) return null;
                int n = int.Parse(name.Substring(8));
                int frames = st.height / st.width;
                // fire_0 plays its strip starting halfway (its .mcmeta), so the two flicker out of step like Minecraft's
                if (strip == "fire_0") n = (n + frames / 2) % Mathf.Max(1, frames);
                return StripFrame(st, n);
            }
            if (name.StartsWith("bed_")) return BedTile(name);
            if (name.StartsWith("item_potion_")) return PotionTile(name.Substring(5));
            if (name.StartsWith("item_") && MobHeads.All.TryGetValue(name.Substring(5), out var mh)) return HeadTile(mh);
            if (name == "item_" + Banners.OminousKey) return Banners.Tile(Tile);
            if (name == "item_arrow_slowness") return TippedArrowTile(new Color32(0x5A, 0x6C, 0x81, 255));
            if (name == "item_arrow_poison") return TippedArrowTile(new Color32(0x87, 0xA3, 0x63, 255));
            if (name.StartsWith("portal_f"))
            {
                if (!fireStrips.TryGetValue("nether_portal", out var ps)) fireStrips["nether_portal"] = ps = McAssets.LoadTexture("block/nether_portal");
                return ps == null ? null : StripFrame(ps, int.Parse(name.Substring(8)));
            }
            if (name.StartsWith("crack_"))
            {
                var t = McAssets.LoadTexture("block/destroy_stage_" + name.Substring(6));
                return t == null ? null : FirstFrame(t, null);
            }
            if (name.StartsWith("dust_"))
            {
                var dot = McAssets.LoadTexture("block/redstone_dust_dot");
                var line = McAssets.LoadTexture("block/redstone_dust_line0");
                if (dot == null || line == null) return null;
                var d = FirstFrame(dot, null);
                var l = FirstFrame(line, null);
                bool on = name.EndsWith("_on");
                var tint = on ? new Color32(255, 40, 20, 255) : new Color32(105, 0, 0, 255);
                bool cross = name == "dust_on" || name == "dust_off" || name.StartsWith("dust_cross");
                bool onlyLine = name.StartsWith("dust_line");
                bool onlyDot = name.StartsWith("dust_dot");
                var outp = new Color32[Tile * Tile];
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        int i = y * Tile + x;
                        // cross = line0 (vertical) + line0 rotated 90 degrees + dot
                        Color32 a = l[i];
                        Color32 b = l[x * Tile + (Tile - 1 - y)];
                        Color32 c = d[i];
                        Color32 m = default;
                        if (onlyLine) m = a;
                        else if (onlyDot) m = c;
                        else if (cross) m = a.a > 0 ? a : (b.a > 0 ? b : c);
                        if (m.a > 0) outp[i] = Mul(m, tint);
                    }
                return outp;
            }
            if (name == "chest_front" || name == "chest_side" || name == "chest_top") return ChestFace(name);
            if (name.StartsWith("decorated_pot_")) return PotFace(name);
            if (name == "item_wither_skull") return SkullFace("entity/skeleton/wither_skeleton");
            if (!McMap.TryGetValue(name, out var map)) return null;
            var tex = McAssets.LoadTexture(map.path);
            if (tex == null) return null;
            return FirstFrame(tex, map.tint);
        }

        /// <summary>A mob's face (the front of its 8x8x8 head at 0,0 of its texture), doubled to a tile: a skull's icon.</summary>
        static Color32[] SkullFace(string texture)
        {
            var t = McAssets.LoadTexture(texture);
            if (t == null) return null;
            int sc = Mathf.Max(1, t.width / 64);
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    outp[y * Tile + x] = t.GetPixel((8 + x / 2) * sc, t.height - 1 - (8 + (Tile - 1 - y) / 2) * sc);
            return outp;
        }

        /// <summary>
        /// A chest face from Minecraft's chest model texture (entity/chest/normal, 64x64): the lid's side (14x5x14 box at
        /// 0,0) over the base's (14x10x14 box at 0,19), the latch (2x4 at 1,1) on the front; the top is the lid's outside.
        /// Stretched from 14 px to the block's 16.
        /// </summary>
        /// <summary>
        /// The decorated pot's faces (#160) from Minecraft's entity textures (entity/decorated_pot): the side (16x16, its
        /// 14 px at 1,0); from the base (32x32) the top (14x14 at 0,13), the neck's sides (8x3 at 8,8), its top (8x8 at
        /// 8,0) and the lip's sides (6x1 at 6,11). Each laid at the pixels its face covers in the model (UVs come from
        /// position), rows here counted from the bottom.
        /// </summary>
        static Color32[] PotFace(string name)
        {
            bool sideTex = name == "decorated_pot_side";
            var t = McAssets.LoadTexture(sideTex ? "entity/decorated_pot/decorated_pot_side" : "entity/decorated_pot/decorated_pot_base");
            if (t == null) return null;
            int sc = Mathf.Max(1, t.width / (sideTex ? 16 : 32));
            Color32 P(int u, int v) { var c = (Color32)t.GetPixel(u * sc, t.height - 1 - v * sc); c.a = 255; return c; }
            var outp = new Color32[Tile * Tile];
            void Put(int x, int row, Color32 c) { if (x >= 0 && x < Tile && row >= 0 && row < Tile) outp[row * Tile + x] = c; }
            switch (name)
            {
                case "decorated_pot_side":
                    for (int row = 0; row < 16; row++) for (int x = 1; x < 15; x++) Put(x, row, P(x, 15 - row));
                    break;
                case "decorated_pot_top":
                    for (int row = 1; row < 15; row++) for (int x = 1; x < 15; x++) Put(x, row, P(x - 1, 13 + (14 - row)));
                    break;
                case "decorated_pot_neck":
                    for (int x = 4; x < 12; x++) for (int row = 1; row <= 3; row++) Put(x, row, P(8 + (x - 4), 8 + (3 - row))); // (y 17-20)
                    for (int x = 5; x < 11; x++) Put(x, 0, P(6 + (x - 5), 11));                                                  // (the lip, y 16-17)
                    break;
                case "decorated_pot_neck_top":
                    for (int x = 4; x < 12; x++) for (int row = 4; row < 12; row++) Put(x, row, P(8 + (x - 4), row - 4));
                    for (int x = 5; x < 11; x++) for (int row = 5; row < 11; row++) if (outp[row * Tile + x].a == 0) Put(x, row, P(8 + (x - 4), row - 4));
                    break;
                default: Object.Destroy(t); return null;
            }
            Object.Destroy(t);
            return outp;
        }

        static Color32[] ChestFace(string name)
        {
            var t = McAssets.LoadTexture("entity/chest/normal");
            if (t == null) return null;
            int sc = Mathf.Max(1, t.width / 64);
            Color32 P(int u, int v) => t.GetPixel(u * sc, t.height - 1 - v * sc);
            int w = 14, h = name == "chest_top" ? 14 : 15;
            var src = new Color32[w * h]; // row 0 = top
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c;
                    if (name == "chest_top") c = P(28 + x, y);                       // the lid's outside (14,0 is its inside)
                    else
                    {
                        // the model is built upside down: in the texture each side strip's top row is its lower edge.
                        // The front is the strip with the latch notches (u 42), the side the one at u 0.
                        int u0 = name == "chest_front" ? 42 : 0;
                        c = y < 5 ? P(u0 + x, 18 - y) : P(u0 + x, 42 - (y - 5));      // lid side, then base side
                        if (name == "chest_front" && x >= 6 && x < 8 && y >= 2 && y < 6) c = P(1 + (x - 6), 1 + (y - 2)); // latch
                    }
                    src[y * w + x] = c;
                }
            Object.Destroy(t);
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    var c = src[(y * h / Tile) * w + x * w / Tile];
                    c.a = 255;
                    outp[(Tile - 1 - y) * Tile + x] = c; // our rows go bottom-up
                }
            return outp;
        }

        static Color32 Mul(Color32 a, Color32 t) => new Color32((byte)(a.r * t.r / 255), (byte)(a.g * t.g / 255), (byte)(a.b * t.b / 255), a.a);

        static Color32[] FirstFrame(Texture2D t, Color32? tint)
        {
            // animated textures are vertical strips; take the top frame. Scale HD packs down to 16x16.
            int w = t.width;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    int sx = x * w / Tile;
                    int sy = t.height - w + (y * w / Tile); // top frame, in texture (bottom-up) coords
                    var c = (Color32)t.GetPixel(sx, sy);
                    if (tint.HasValue) c = Mul(c, tint.Value);
                    outp[y * Tile + x] = c;
                }
            Object.Destroy(t);
            return outp;
        }

        static Color32[] ReadTile(Texture2D atlas, int index)
        {
            int cols = atlas.width / Tile; // (the embedded fallback atlas has its own width)
            int col = index % cols, row = index / cols;
            int x0 = col * Tile, y0 = atlas.height - (row + 1) * Tile;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    outp[y * Tile + x] = atlas.GetPixel(x0 + x, y0 + y);
            return outp;
        }

        static void WriteTile(Texture2D atlas, int index, Color32[] px)
        {
            int col = index % Cols, row = index / Cols;
            int x0 = col * Tile, y0 = atlas.height - (row + 1) * Tile;
            atlas.SetPixels32(x0, y0, Tile, Tile, px);
        }

        public static Color32[] TilePixels(string name) => ReadTile(Texture, T(name));

        // ------------------------------------------------------------------ materials
        static Shader litShader, unlitShader;

        static void BuildMaterials()
        {
            litShader = Shader.Find("HDRP/Lit");
            unlitShader = Shader.Find("HDRP/Unlit");
            if (litShader == null) Plugin.Log.LogError("HDRP/Lit shader not found!");
            Opaque = MakeLit("LMC_Opaque", false, false, false);
            Cutout = MakeLit("LMC_Cutout", true, true, false);
            Emissive = MakeLit("LMC_Emissive", false, false, true);
            CutoutEmissive = MakeLit("LMC_CutoutEmissive", true, true, true);
            Crack = MakeLit("LMC_Crack", true, false, false);
            Crack.SetFloat("_AlphaCutoff", 0.1f);
            Particle = MakeLit("LMC_Particle", true, true, false);
            OpaqueAmb = MakeLit("LMC_OpaqueAmb", false, false, false);
            CutoutAmb = MakeLit("LMC_CutoutAmb", true, true, false);
            SetAmbient(AmbientEV);

            // water: see-through (HDRP transparent, alpha blended)
            Water = MakeLit("LMC_Water", false, true, false);
            HDMaterial.SetSurfaceType(Water, true);
            Water.SetFloat("_SurfaceType", 1f);
            Water.SetFloat("_BlendMode", 0f);
            Water.SetColor("_BaseColor", new Color(1f, 1f, 1f, WaterAlpha));
            Water.SetFloat("_Smoothness", 0.85f);
            Water.SetFloat("_SupportDecals", 0f);
            HDMaterial.ValidateMaterial(Water);
            Water.renderQueue = 3000;

            // slime and honey (#198): see-through where their textures are, as Minecraft's translucent blocks
            Gel = MakeLit("LMC_Gel", false, false, false);
            HDMaterial.SetSurfaceType(Gel, true);
            Gel.SetFloat("_SurfaceType", 1f);
            Gel.SetFloat("_BlendMode", 0f);
            Gel.SetColor("_BaseColor", Color.white);
            Gel.SetFloat("_SupportDecals", 0f);
            HDMaterial.ValidateMaterial(Gel);
            Gel.renderQueue = 3000;

            // a Nether portal: see-through and glowing
            Portal = MakeLit("LMC_Portal", false, true, true);
            HDMaterial.SetSurfaceType(Portal, true);
            Portal.SetFloat("_SurfaceType", 1f);
            Portal.SetFloat("_BlendMode", 0f);
            Portal.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.82f));
            Portal.SetFloat("_SupportDecals", 0f);
            HDMaterial.ValidateMaterial(Portal);
            Portal.renderQueue = 3001;

            Outline = new Material(unlitShader ?? litShader) { name = "LMC_Outline" };
            Outline.SetColor("_UnlitColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            Outline.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            HDMaterial.ValidateMaterial(Outline);
        }

        /// <summary>No decal lands on this renderer (its decal-layer bits cleared; its light layers stay): the moons' decals
        /// (leaf shadows, stains, blood) are meant for the game's surfaces. Our materials still "support" decals so that
        /// HDRP draws them in its early depth pass (#40).</summary>
        public static void NoDecals(Renderer r) { if (r != null) r.renderingLayerMask &= ~0xFF00u; }

        public static Material MakeLit(string name, bool cutout, bool doubleSided, bool emissive)
        {
            var m = new Material(litShader) { name = name };
            m.SetTexture("_BaseColorMap", Texture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            if (cutout)
            {
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.5f);
                HDMaterial.SetAlphaClipping(m, true);
                HDMaterial.SetAlphaCutoff(m, 0.5f);
            }
            if (doubleSided)
            {
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_CullMode", 0f);
                m.SetFloat("_CullModeForward", 0f);
            }
            if (emissive)
            {
                // Emission maps are stripped from the game's shader variants, so glow = albedo * intensity instead
                // (only glowing geometry uses these materials).
                m.SetFloat("_AlbedoAffectEmissive", 1f);
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, EmissiveEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                m.SetFloat("_EmissiveExposureWeight", 0.6f);
            }
            // Blocks take decals like the game's own surfaces (#40). Turned off, HDRP draws their depth after the decal pass
            // and the game's build has no shader variant without decals, so a stain on the floor behind a block (the
            // facility's puddle meshes) was painted onto the block's face: seen "through" it. On, blocks are in the early
            // depth pass and hide what's behind them. (No decal paints our renderers themselves: NoDecals.)
            m.SetFloat("_SupportDecals", 1f);
            HDMaterial.ValidateMaterial(m);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Faint albedo-tinted self-illumination so block sides aren't pitch black in shade (outdoors/ship only).</summary>
        public static void SetAmbient(float ev)
        {
            AmbientEV = ev;
            foreach (var m in new[] { OpaqueAmb, CutoutAmb })
            {
                m.SetFloat("_AlbedoAffectEmissive", 1f);
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, ev, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            }
        }

        public static Material Ambient(Material m) => m == Opaque ? OpaqueAmb : m == Cutout ? CutoutAmb : m;

        public static float WaterAlpha = 0.7f;

        public static Material ForRender(RenderKind k, bool emissiveState = false)
        {
            switch (k)
            {
                case RenderKind.Translucent: return Water;
                case RenderKind.Gel: return Gel;
                case RenderKind.Portal: return Portal;
                case RenderKind.Cutout: return emissiveState ? CutoutEmissive : Cutout;
                case RenderKind.Emissive: return Emissive;
                default: return emissiveState ? Emissive : Opaque;
            }
        }

        // ------------------------------------------------------------------ icons
        /// <summary>
        /// The sprite a block's item is in Minecraft when that item is flat (item/generated: torches, the lever, doors,
        /// ladders, panes, the repeater and comparator, rails, the hopper, the cauldron, the tripwire hook...), or null
        /// when its item is the block's own model. Its icon and, #195, what you hold (they had been the block's model in
        /// hand: a lever was a dark box, a hopper a block).
        /// </summary>
        public static string FlatItemTile(BlockDef b)
        {
            switch (b.Shape)
            {
                case BlockShape.Torch: return b == Blocks.RedstoneTorch ? "redstone_torch" : "item_torch";
                case BlockShape.Repeater: return "item_repeater";
                case BlockShape.Comparator: return "item_comparator";
                case BlockShape.Dust: return "item_redstone_dust";
                case BlockShape.Lever: return "lever_handle";
                case BlockShape.Pane:
                case BlockShape.Door:
                case BlockShape.Ladder: return "item_" + (b.Key.StartsWith("waxed_") ? b.Key.Substring(6) : b.Key);
                case BlockShape.Cane: return "item_sugar_cane";
                case BlockShape.Banner: return "item_" + b.Key; // (#223: its cloth's front)
                case BlockShape.Sapling: return b.TileSide; // (#223: its own picture)
                case BlockShape.BrewingStand: return "item_brewing_stand";
                case BlockShape.Crop: return b == Blocks.CarrotCrop ? "food_carrot" : b == Blocks.PotatoCrop ? "food_potato" : b == Blocks.NetherWart ? "item_nether_wart" : b == Blocks.PumpkinStem ? "item_pumpkin_seeds" : "item_wheat_seeds";
                case BlockShape.Hook: return "item_tripwire_hook";
                case BlockShape.Rail: return "item_" + b.Key;
                case BlockShape.Hopper: return "item_hopper";
                case BlockShape.Basin: return b == Blocks.Cauldron ? "item_cauldron" : null;
            }
            return null;
        }

        static void BuildIcons()
        {
            foreach (var b in Blocks.All)
            {
                if (b.Shape == BlockShape.PistonHead) continue;
                Texture2D tex = null;
                if (McAssets.Available)
                {
                    switch (b.Shape)
                    {
                        // (the redstone lamp's item is the lamp off, as Minecraft's: it had been drawn lit, #195)
                        case BlockShape.Cube: tex = MeshIcon(b); break;
                        // flat items in Minecraft's inventory
                        case BlockShape.Torch:
                        case BlockShape.Repeater:
                        case BlockShape.Comparator:
                        case BlockShape.Dust:
                        case BlockShape.Lever:
                        case BlockShape.Pane:
                        case BlockShape.Door:
                        case BlockShape.Ladder:
                        case BlockShape.Cane:
                        case BlockShape.BrewingStand:
                        case BlockShape.Crop: tex = SpriteIcon(FlatItemTile(b)); break;
                        // a slab with the back half on it, the stair's profile on the left face: Minecraft's stairs model shows
                        // its high half at the back left (its gui display turns it 135 degrees, not 225; ours had it at the
                        // back right, #195)
                        case BlockShape.Stairs:
                            tex = McModelIcon(b.TileTop, b.TileSide, b.TileBottom, TurnedGui, Bx(0, 0, 0, 16, 8, 16), Bx(8, 8, 0, 16, 16, 16));
                            break;
                        case BlockShape.Slab:
                            tex = McModelIcon(b.TileTop, b.TileSide, b.TileBottom, BlockGui, Bx(0, 0, 0, 16, 8, 16));
                            break;
                        // (Minecraft's inventory wall: two posts and the arm between; a fence the same, thinner)
                        case BlockShape.Wall:
                            // (Minecraft's wall_inventory model: the post and its two arms, #195; it was drawn as a solid slab)
                            tex = McModelIcon(b.TileTop, b.TileSide, null, TurnedGui, Bx(4, 0, 4, 12, 16, 12), Bx(5, 0, 0, 11, 13, 16));
                            break;
                        case BlockShape.Fence:
                            // (Minecraft's fence_inventory: two posts and the two bars between, sticking out past them)
                            tex = McModelIcon(b.TileTop, b.TileSide, null, TurnedGui, Bx(6, 0, 0, 10, 16, 4), Bx(6, 0, 12, 10, 16, 16), Bx(7, 12, 0, 9, 15, 16), Bx(7, 6, 0, 9, 9, 16),
                                Bx(7, 12, -2, 9, 15, 0), Bx(7, 12, 16, 9, 15, 18), Bx(7, 6, -2, 9, 9, 0), Bx(7, 6, 16, 9, 9, 18));
                            break;
                        case BlockShape.Trapdoor:
                            tex = McModelIcon(b.TileTop, b.TileTop, null, BlockGui, Bx(0, 0, 0, 16, 3, 16));
                            break;
                        // (#160: each kind of button and plate in its own material)
                        case BlockShape.Hook: tex = SpriteIcon(FlatItemTile(b)); break;
                        case BlockShape.Lectern: tex = MeshIcon(b); break;
                        case BlockShape.Pot: tex = MeshIcon(b); break;
                        case BlockShape.Shelf: tex = MeshIcon(b); break;
                        case BlockShape.Rail: tex = SpriteIcon(FlatItemTile(b)); break;
                        case BlockShape.Hopper: tex = SpriteIcon(FlatItemTile(b)); break;
                        case BlockShape.Basin: tex = b == Blocks.Cauldron ? SpriteIcon(FlatItemTile(b)) : MeshIcon(b) ?? IsoIcon(b.TileTop, b.TileSide, b.TileSide); break; // (the composter from its mesh: its open top showed through, #195)
                        case BlockShape.Bed: tex = BedIcon(b); break;
                        case BlockShape.Rod: tex = MeshIcon(b); break;
                        case BlockShape.Gate:
                            // (Minecraft's fence gate: its two posts, the two middle uprights and the four rails, seen side on)
                            tex = McModelIcon(b.TileTop, b.TileSide, null, GateGui, Bx(0, 5, 7, 2, 16, 9), Bx(14, 5, 7, 16, 16, 9), Bx(6, 6, 7, 10, 15, 9),
                                Bx(2, 6, 7, 6, 9, 9), Bx(2, 12, 7, 6, 15, 9), Bx(10, 6, 7, 14, 9, 9), Bx(10, 12, 7, 14, 15, 9));
                            break;
                        case BlockShape.Detector:
                            tex = McModelIcon(b.TileTop, b.TileSide, b.TileBottom, BlockGui, Bx(0, 0, 0, 16, 6, 16));
                            break;
                        case BlockShape.Table:
                            tex = McModelIcon(b.TileTop, b.TileSide, b.TileBottom, BlockGui, Bx(0, 0, 0, 16, 12, 16));
                            break;
                        case BlockShape.Button:
                            // (Minecraft's button_inventory: the button standing in the middle of the slot; ours lay at the bottom)
                            tex = McModelIcon(b.TileSide, b.TileSide, null, BlockGui, Bx(5, 6, 6, 11, 10, 10));
                            break;
                        case BlockShape.Plate:
                            tex = McModelIcon(b.TileSide, b.TileSide, null, BlockGui, Bx(1, 0, 1, 15, 1, 15));
                            break;
                    }
                }
                if (tex == null) tex = LoadEmbeddedIcon(b.IconName);
                if (tex == null) tex = IsoIcon(b.TileTop, b.TileSide, b.TileSide);
                Icons[b.Key] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            foreach (var s in new[] { "pickaxe", "flint_and_steel" })
            {
                Texture2D tex = McAssets.Available ? SpriteIcon("item_" + s) : null;
                if (tex == null) tex = LoadEmbeddedIcon(s);
                Icons[s] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        public static Sprite IconFor(string key)
        {
            if (Icons.TryGetValue(key, out var s)) return s;
            Texture2D tex = null;
            // (#195) mob heads in 3D, as Minecraft's inventory shows a skull, not the flat face
            if (key.StartsWith("item_") && MobHeads.All.TryGetValue(key.Substring(5), out var mh)) tex = HeadModelIcon(mh.Key, mh.Texture) ?? HeadIcon(mh.Texture, mh.U, mh.V, mh.W);
            else if (key == "item_wither_skull") tex = HeadModelIcon("wither_skeleton_skull", "entity/skeleton/wither_skeleton") ?? HeadIcon("entity/skeleton/wither_skeleton", 0, 0, 8);
            // (#195) leather is dyed: Minecraft's undyed colour on the base, the overlay as it is
            else if (key == "item_leather_horse_armor" && Tiles.ContainsKey(key) && Tiles.ContainsKey("item_leather_horse_armor_overlay"))
            {
                var b = TilePixels(key); var o = TilePixels("item_leather_horse_armor_overlay");
                var px = new Color32[b.Length];
                for (int i = 0; i < b.Length; i++)
                    px[i] = o[i].a > 20 ? o[i] : new Color32((byte)(b[i].r * 0xA0 / 255), (byte)(b[i].g * 0x65 / 255), (byte)(b[i].b * 0x40 / 255), b[i].a);
                tex = SpriteIconPx(px);
            }
            if (tex == null) tex = Tiles.ContainsKey(key) ? SpriteIcon(key) : null;
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            Icons[key] = s;
            return s;
        }

        /// <summary>Upscaled flat 16x16 sprite with a 1px dark drop outline, 128x128.</summary>
        public static Texture2D SpriteIcon(string tile) => Tiles.ContainsKey(tile) ? SpriteIconPx(TilePixels(tile)) : null;

        public static Texture2D SpriteIconPx(Color32[] px)
        {
            const int S = 128, scale = 7, off = 8;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var outp = new Color32[S * S];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    var c = px[y * Tile + x];
                    if (c.a < 20) continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            outp[(off + y * scale + dy) * S + off + x * scale + dx] = c;
                }
            tex.SetPixels32(outp);
            tex.Apply();
            return tex;
        }

        /// <summary>Software-rendered isometric cube icon (128x128) from three atlas tiles.</summary>
        static Color32[] atlasPx;

        /// <summary>A block's inventory icon drawn from its own mesh (its real faces and texture coordinates), the way
        /// Minecraft's inventory draws the block model: small, as its 16-pixel slots, then scaled up pixel for pixel so
        /// the texels stay crisp (the 128-pixel drawing made them uneven). Null when the block has no mesh.</summary>
        public static Texture2D MeshIcon(BlockDef def, byte state = 0, int variant = 0)
        {
            Mesh mesh;
            // (a block with a face, as its item model: facing north, turned so; the observer likewise, #195)
            bool faced = (MeshBuilder.HasFront(def) || def == Blocks.Observer) && def != Blocks.Barrel;
            if (faced && variant == 0) variant = (int)Face.North + 1;
            if (def == Blocks.Barrel && variant == 0) variant = (int)Face.Up + 1; // (its item model stands up: the lid on top)
            try { mesh = MeshBuilder.For(def, state, variant); } catch { return null; }
            if (mesh == null || mesh.vertexCount == 0) return null;
            if (faced) return MeshPartsIcon(new[] { (mesh, Vector3.zero) }, 1f, Vector3.zero, Quaternion.FromToRotation(Vector3.up, Faces.Dir[(int)Face.North]));
            // (as its item model: a block with a front faces north, shown on the right; pillars, the barrel and pistons
            // stand up, #195: the front had been turned up-to-forward for any directional block, which laid pillars down)
            return MeshPartsIcon(new[] { (mesh, Vector3.zero) }, 1f, Vector3.zero, Quaternion.identity);
        }

        /// <summary>The bed (#195): Minecraft's item, the head and the foot one block south of it (items/red_bed.json), seen
        /// as its template_bed model's "gui" display has it: turned 30 and 340 degrees, moved (2, 3) pixels, scaled 0.5325.
        /// It had been the two halves in the blocks' view, small and the other way round.</summary>
        public static Texture2D BedIcon(BlockDef def)
        {
            Mesh foot, head;
            try { foot = MeshBuilder.For(def, 0, 0); head = MeshBuilder.For(def, 1, 0); } catch { return null; }
            if (foot == null || head == null) return null;
            // (our z is Minecraft's turned over: one block south is -z here)
            return MeshPartsIcon(new[] { (head, Vector3.zero), (foot, new Vector3(0f, 0f, -1f)) }, 1f, Vector3.zero, Quaternion.identity,
                new McGui { Rx = 30f, Ry = 340f, Translation = new Vector3(2f, 3f, 0f), Scale = 0.5325f });
        }

        /// <summary>A Minecraft block model's boxes (its pixels and its z) as its "gui" display shows it (#195): `top` on the
        /// boxes' tops and bottoms (`bottom` if given), `side` round them, the textures where each box is on the block.</summary>
        static Texture2D McModelIcon(string top, string side, string bottom, McGui gui, params (Vector3 from, Vector3 to)[] boxes)
        {
            var mb = new MeshBuilder();
            foreach (var (a, b) in boxes) mb.McBox(a, b, top, side, bottom);
            var mesh = mb.ToMesh("icon");
            try { return MeshPartsIcon(new[] { (mesh, Vector3.zero) }, 1f, Vector3.zero, Quaternion.identity, gui); }
            finally { Object.Destroy(mesh); }
        }

        static (Vector3, Vector3) Bx(float x0, float y0, float z0, float x1, float y1, float z1) => (new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

        /// <summary>The views of Minecraft's models: blocks (block.json), and what stairs, walls and fences turn to (135),
        /// fence gates (45, bigger, a pixel down).</summary>
        static readonly McGui BlockGui = new McGui(), TurnedGui = new McGui { Ry = 135f }, GateGui = new McGui { Ry = 45f, Scale = 0.8f, Translation = new Vector3(0f, -1f, 0f) };

        /// <summary>A Minecraft item model's "gui" display: its rotation (x, then y, degrees), translation (pixels) and scale.</summary>
        public class McGui { public float Rx = 30f, Ry = 225f, Scale = 0.625f; public Vector3 Translation; }

        /// <summary>A vector in Minecraft's coordinates turned as an item display turns it (rotation x then y: Rx(Ry(v))),
        /// into the inventory's view: x right, y up, z towards you.</summary>
        static Vector3 McTurn(Vector3 v, float rx, float ry)
        {
            float cy = Mathf.Cos(ry * Mathf.Deg2Rad), sy = Mathf.Sin(ry * Mathf.Deg2Rad), cx = Mathf.Cos(rx * Mathf.Deg2Rad), sx = Mathf.Sin(rx * Mathf.Deg2Rad);
            var a = new Vector3(v.x * cy + v.z * sy, v.y, -v.x * sy + v.z * cy);
            return new Vector3(a.x, a.y * cx - a.z * sx, a.y * sx + a.z * cx);
        }

        /// <summary>
        /// Minecraft's lighting of items in the inventory (#195), for a normal in the inventory's view: its shader's two
        /// lights (0.6 of the light that falls on the face from each, 0.4 all round, at most 1) pointing as
        /// Lighting.setupFor3DItems turns them. A block's top 1, its left side 0.81, its right 0.62 (ours had been 0.86
        /// and 0.72, which made every block look flat).
        /// </summary>
        static float GuiLight(Vector3 n)
        {
            var l0 = new Vector3(-0.2225f, 0.1715f, 0.9597f);
            var l1 = new Vector3(-0.2150f, 0.9718f, 0.0966f);
            return Mathf.Min(1f, (Mathf.Max(0f, Vector3.Dot(l0, n)) + Mathf.Max(0f, Vector3.Dot(l1, n))) * 0.6f + 0.4f);
        }

        /// <summary>
        /// The blocks' inventory view in a 128-pixel icon, as Minecraft's block model "gui" display has it (turned 30 and
        /// 225 degrees, scaled 0.625, the slot 16 pixels to a block): a cube's top corner to corner 2 * IsoW across and
        /// 2 * IsoH high, its sides IsoSide high, its top corner IsoTop down (#195: ours had been 55, 27.5, 62 and 8,
        /// the sides a tenth too short).
        /// </summary>
        const float IsoW = 56.57f, IsoH = 28.28f, IsoSide = 69.28f, IsoTop = 1.08f;

        /// <summary>A block's side in the blocks' inventory view: Minecraft's light on the left (east) and right (north) sides.</summary>
        public static readonly float LeftLight = 0.811f, RightLight = 0.622f;

        /// <summary>Meshes drawn into one icon: each part's vertices (block space, -0.5..0.5) turned, moved by its offset,
        /// then the lot scaled about the middle and shifted (in the icon's 0..1 space); in the blocks' view, or as a
        /// Minecraft item display (`gui`) has it.</summary>
        static Texture2D MeshPartsIcon((Mesh mesh, Vector3 offset)[] parts, float scale, Vector3 shift, Quaternion turn, McGui gui = null, Texture2D skin = null)
        {
            if (atlasPx == null || atlasPx.Length != Texture.width * Texture.height) atlasPx = Texture.GetPixels32();
            // (the block atlas, or an entity's own texture: a mob head, #197)
            var texPx = skin != null ? skin.GetPixels32() : atlasPx;
            const int S = 32, Up = 4;
            // the isometric view of IsoBoxes, at 32 pixels: (x, y, z) in 0..1 at X = cx + (x - z) w, Y = top + (x + z) h + (1 - y) side
            float cx = S / 2f, w = IsoW / 128f * S, h = IsoH / 128f * S, topY = IsoTop / 128f * S, sideH = IsoSide / 128f * S, a = 2f * h / sideH;
            var depth = new float[S * S]; for (int i = 0; i < depth.Length; i++) depth[i] = float.NegativeInfinity;
            var small = new Color32[S * S];
            int AW = skin != null ? skin.width : Texture.width, AH = skin != null ? skin.height : Texture.height;
            var view = new Vector3(1f, a, 1f);
            foreach (var (mesh, offset) in parts)
            {
                var vs = mesh.vertices; var uv = mesh.uv; var ns = mesh.normals;
                for (int i = 0; i < vs.Length; i++)
                {
                    var q = turn * vs[i] + offset + new Vector3(0.5f, 0.5f, 0.5f);
                    vs[i] = (q - new Vector3(0.5f, 0.5f, 0.5f)) * scale + new Vector3(0.5f, 0.5f, 0.5f) * scale + shift;
                    ns[i] = turn * ns[i];
                }
                // (Minecraft's inventory view: east on the left, north, a block's front, on the right; the old formula
                // drew the mirror image, #195)
                // as an item display: in Minecraft's coordinates (z turned over), about the middle, scaled, turned and moved;
                // 16 pixels of the slot to a block
                Vector3 G(Vector3 q) => new Vector3(gui.Translation.x / 16f, gui.Translation.y / 16f, gui.Translation.z / 16f)
                    + McTurn((new Vector3(q.x, q.y, 1f - q.z) - new Vector3(0.5f, 0.5f, 0.5f)) * gui.Scale, gui.Rx, gui.Ry);
                Vector2 P(Vector3 q)
                {
                    if (gui == null) return new Vector2(cx + (q.z - q.x) * w, topY + (q.x + q.z) * h + (1f - q.y) * sideH);
                    var g = G(q); return new Vector2(S / 2f + g.x * S, S / 2f - g.y * S);
                }
                float Dp(Vector3 q) => gui == null ? q.x + q.z + a * q.y : G(q).z;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                        var n = ns[i0];
                        // the normal in the view (Minecraft's coordinates, turned): faces turned away aren't drawn
                        var nv = McTurn(new Vector3(n.x, n.y, -n.z), gui?.Rx ?? 30f, gui?.Ry ?? 225f);
                        if (gui == null ? Vector3.Dot(n, view) <= 0f : nv.z <= 1e-4f) continue;
                        float shade = GuiLight(nv);
                        Vector2 p0 = P(vs[i0]), p1 = P(vs[i1]), p2 = P(vs[i2]);
                        float d0 = Dp(vs[i0]), d1 = Dp(vs[i1]), d2 = Dp(vs[i2]);
                        float area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
                        if (Mathf.Abs(area) < 1e-6f) continue;
                        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)))), x1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))));
                        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)))), y1 = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))));
                        for (int py = y0; py <= y1; py++)
                            for (int px = x0; px <= x1; px++)
                            {
                                float X = px + 0.5f, Y = py + 0.5f;
                                float b0 = ((p1.x - X) * (p2.y - Y) - (p2.x - X) * (p1.y - Y)) / area;
                                float b1 = ((p2.x - X) * (p0.y - Y) - (p0.x - X) * (p2.y - Y)) / area;
                                float b2 = 1f - b0 - b1;
                                if (b0 < -1e-4f || b1 < -1e-4f || b2 < -1e-4f) continue;
                                float d = b0 * d0 + b1 * d1 + b2 * d2;
                                int idx = (S - 1 - py) * S + px;
                                if (d <= depth[idx]) continue;
                                var u = uv[i0] * b0 + uv[i1] * b1 + uv[i2] * b2;
                                var c = texPx[Mathf.Clamp((int)(u.y * AH), 0, AH - 1) * AW + Mathf.Clamp((int)(u.x * AW), 0, AW - 1)];
                                if (c.a < 10) continue;
                                if (c.a < 250)
                                {
                                    // (see-through, #198: over what's behind it, which the model draws first; it hides nothing)
                                    var dst = small[idx]; float al = c.a / 255f, dl = dst.a / 255f * (1f - al), oa = al + dl;
                                    small[idx] = new Color32((byte)((c.r * shade * al + dst.r * dl) / oa), (byte)((c.g * shade * al + dst.g * dl) / oa), (byte)((c.b * shade * al + dst.b * dl) / oa), (byte)(oa * 255f));
                                    continue;
                                }
                                depth[idx] = d;
                                small[idx] = new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
                            }
                    }
                }
            }
            var tex = new Texture2D(S * Up, S * Up, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var big = new Color32[S * Up * S * Up];
            for (int y = 0; y < S * Up; y++)
                for (int x = 0; x < S * Up; x++) big[y * S * Up + x] = small[(y / Up) * S + x / Up];
            tex.SetPixels32(big); tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// A mob head's icon from Minecraft's own head model (#197): the skull's 8 px box from the mob's texture (the zombie's
        /// with its hat layer, a quarter pixel out; the piglin's 10 px wide, with its snout, tusks and ears), set upright as
        /// the head item has it (turned 180 about x, moved half a block in) and seen as template_skull's gui display (turned
        /// 30 and 45, 3 px up). The piglin's had been a plain box.
        /// </summary>
        public static Texture2D HeadModelIcon(string key, string texture)
        {
            var skin = McAssets.LoadTexture(texture);
            if (skin == null) return null;
            var boxes = MobHeads.ModelBoxes(key);
            var mb = new MeshBuilder();
            foreach (var bx in boxes)
            {
                // model pixels to the item's block space: upright (180 about x), half a block in
                var m = Matrix4x4.Translate(new Vector3(0.5f, 0f, 0.5f)) * Matrix4x4.Scale(new Vector3(1f, -1f, -1f) / 16f) * bx.pose;
                mb.McCube(m, bx.u, bx.v, bx.from, bx.size, bx.grow, skin.width, skin.height);
            }
            var mesh = mb.ToMesh("head");
            try { return MeshPartsIcon(new[] { (mesh, Vector3.zero) }, 1f, Vector3.zero, Quaternion.identity, new McGui { Rx = 30f, Ry = 45f, Translation = new Vector3(0f, 3f, 0f), Scale = 1f }, skin); }
            finally { Object.Destroy(mesh); }
        }

        /// <summary>A mob head in 3D (#195), as Minecraft's inventory shows a skull: the head's own top, face and side from
        /// its skin; the face on the right, as the inventory's view turns it.</summary>
        public static Texture2D HeadIcon(string texture, int u0, int v0, int w)
        {
            var t = McAssets.LoadTexture(texture);
            if (t == null) return null;
            float sx = t.width / 64f, sy = t.height / (t.height * 2 == t.width ? 32f : 64f);
            Color32[] Region(int ru, int rv, int rw, int rh, bool rotate)
            {
                var outp = new Color32[Tile * Tile];
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        // (tile rows bottom-up: row y is (Tile - 1 - y) from the top)
                        float fu = x / (float)Tile, fv = (Tile - 1 - y) / (float)Tile;
                        if (rotate) { var tmp = fu; fu = 1f - fv; fv = tmp; }
                        int px = (int)((ru + fu * rw) * sx), py = (int)((rv + fv * rh) * sy);
                        var c = t.GetPixel(Mathf.Clamp(px, 0, t.width - 1), Mathf.Clamp(t.height - 1 - py, 0, t.height - 1)); c.a = 1f;
                        outp[y * Tile + x] = c;
                    }
                return outp;
            }
            // Minecraft's head layout: top (u0+8, v0, w x 8), face (u0+8, v0+8), sides 8 x 8 either side of the face
            var top = Region(u0 + 8, v0, w, 8, true);
            var face = Region(u0 + 8, v0 + 8, w, 8, false);
            var side = Region(u0 + 8 + w, v0 + 8, 8, 8, false);
            return IsoBoxesPx(top, side, face, new[] { (new Vector3(0.12f, 0.12f, 0.12f), new Vector3(0.88f, 0.88f, 0.88f)) });
        }

        public static Texture2D IsoIcon(string top, string left, string right)
        {
            const int S = 128;
            var tp = TilePixels(top); var lp = TilePixels(left); var rp = TilePixels(right);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var outp = new Color32[S * S];
            float cx = S / 2f, w = IsoW, h = IsoH, topY = IsoTop, sideH = IsoSide;
            // image y grows downward in these formulas; texture rows are bottom-up so flip at write time
            for (int py = 0; py < S; py++)
                for (int px = 0; px < S; px++)
                {
                    float X = px + 0.5f, Y = py + 0.5f;
                    Color32 c = default; bool hit = false;
                    // top face: diamond with corners (cx,topY) (cx+w,topY+h) (cx,topY+2h) (cx-w,topY+h)
                    {
                        // basis: u along (cx,topY)->(cx+w,topY+h), v along (cx,topY)->(cx-w,topY+h)
                        float dx = X - cx, dy = Y - topY;
                        // solve dx = u*w - v*w ; dy = u*h + v*h
                        float u = (dx / w + dy / h) * 0.5f, v = (dy / h - dx / w) * 0.5f;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1)
                        {
                            c = Sample(tp, u, v, 1f); hit = c.a > 10;
                        }
                    }
                    if (!hit && X < cx)
                    {
                        // left face: from (cx-w, topY+h) right-down to (cx, topY+2h), down sideH
                        float u = (X - (cx - w)) / w;
                        float yTop = topY + h + u * h;
                        float v = (Y - yTop) / sideH;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1) { c = Sample(lp, u, v, LeftLight); hit = c.a > 10; } // (the lighter, as Minecraft's)
                    }
                    if (!hit && X >= cx)
                    {
                        float u = (X - cx) / w;
                        float yTop = topY + 2 * h - u * h;
                        float v = (Y - yTop) / sideH;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1) { c = Sample(rp, u, v, RightLight); hit = c.a > 10; }
                    }
                    if (hit) outp[(S - 1 - py) * S + px] = c;
                }
            tex.SetPixels32(outp);
            tex.Apply();
            return tex;
        }

        /// <summary>Isometric icon (128x128) of boxes inside the unit cube (min, max corners), drawn the way IsoIcon draws a
        /// whole cube: x runs down-right on the right face, z down-left on the left face, y up. Each pixel takes the face
        /// of the box nearest the viewer along its line of sight.</summary>
        public static Texture2D IsoBoxes(string top, string left, string right, (Vector3 min, Vector3 max)[] boxes)
            => IsoBoxesPx(TilePixels(top), TilePixels(left), TilePixels(right), boxes, false);

        /// <summary>IsoBoxes from pixel tiles; `perBox`: each box's faces take the whole tile (a head), rather than the
        /// part of a block's face where the box is (stairs, walls).</summary>
        public static Texture2D IsoBoxesPx(Color32[] tp, Color32[] lp, Color32[] rp, (Vector3 min, Vector3 max)[] boxes, bool perBox = true)
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var outp = new Color32[S * S];
            float cx = S / 2f, w = IsoW, h = IsoH, topY = IsoTop, sideH = IsoSide;
            // a point (x, y, z) is drawn at X = cx + (x - z) w, Y = topY + (x + z) h + (1 - y) sideH; along (1, a, 1) it
            // stays on the same pixel, towards the viewer
            float a = 2f * h / sideH;
            for (int py = 0; py < S; py++)
                for (int px = 0; px < S; px++)
                {
                    float X = px + 0.5f, Y = py + 0.5f;
                    // the line of sight through the pixel, from where it crosses y = 0
                    float d = (X - cx) / w, sum = (Y - topY - sideH) / h; // (d = z - x: Minecraft's view, #195)
                    float x0 = (sum - d) * 0.5f, z0 = (sum + d) * 0.5f;
                    float best = float.NegativeInfinity; int face = -1; Vector3 at = default;
                    foreach (var (mn, mx) in boxes)
                    {
                        // where the line is inside the box: t in [lo, hi]; the viewer sees its exit at hi
                        float lo = Mathf.Max(mn.x - x0, Mathf.Max(mn.y / a, mn.z - z0));
                        float tx = mx.x - x0, ty = mx.y / a, tz = mx.z - z0;
                        float hi = Mathf.Min(tx, Mathf.Min(ty, tz));
                        if (hi <= lo || hi <= best) continue;
                        best = hi; face = hi == ty ? 0 : hi == tz ? 1 : 2;
                        at = new Vector3(x0 + hi, a * hi, z0 + hi);
                        if (perBox) { var size = mx - mn; at = new Vector3((at.x - mn.x) / size.x, (at.y - mn.y) / size.y, (at.z - mn.z) / size.z); }
                    }
                    if (face < 0) continue;
                    // (the +z face now on the right, the +x on the left; the left the lighter, as Minecraft's)
                    Color32 c = face == 0 ? Sample(tp, at.x, at.z, 1f) : face == 1 ? Sample(rp, 1f - at.x, 1f - at.y, RightLight) : Sample(lp, at.z, 1f - at.y, LeftLight);
                    if (c.a > 10) outp[(S - 1 - py) * S + px] = c;
                }
            tex.SetPixels32(outp);
            tex.Apply();
            return tex;
        }

        static Color32 Sample(Color32[] tile, float u, float v, float shade)
        {
            int x = Mathf.Clamp((int)(u * Tile), 0, Tile - 1);
            int y = Mathf.Clamp((int)(v * Tile), 0, Tile - 1);
            var c = tile[(Tile - 1 - y) * Tile + x]; // tile arrays are bottom-up rows
            return new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), c.a);
        }
    }
}
