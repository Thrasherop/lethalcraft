using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace LethalMinecraft
{
    /// <summary>
    /// The game saves a ship's items as their places in its item list (shipGrabbableItemIDs), and every item a version of
    /// the mod adds in the middle of that list moves the ones after it: 1.53.0 added 23 (the trim templates, the enchanted
    /// book, the ore blocks), and a 1.52 save loaded in it turned its ship items into others. Now each saved item's name goes
    /// with it (LMC_ShipItemNames) and loading puts each back at its name's place. A save without names is read in the list
    /// it was written with: 1.53.0's (it has the enchantment lists, LMC_EnchSets_v1) is this list less what came after it;
    /// anything older is 1.52's, which is this list less 1.53.0's 23 too.
    /// </summary>
    [HarmonyPatch]
    public static class ShipItemIds
    {
        const string IdsKey = "shipGrabbableItemIDs", NamesKey = "LMC_ShipItemNames";
        /// <summary>Items 1.53.0 put in the middle of the list (none since: names are saved from 1.53.1 on).</summary>
        static readonly HashSet<string> New153 = new HashSet<string>
        {
            "LMC_diamond_ore", "LMC_gold_ore", "LMC_iron_ore", "LMC_emerald_ore", "LMC_ancient_debris", "LMC_dune_armor_trim_smithing_template",
            "LMC_coast_armor_trim_smithing_template", "LMC_wild_armor_trim_smithing_template", "LMC_ward_armor_trim_smithing_template",
            "LMC_eye_armor_trim_smithing_template", "LMC_vex_armor_trim_smithing_template", "LMC_tide_armor_trim_smithing_template",
            "LMC_snout_armor_trim_smithing_template", "LMC_rib_armor_trim_smithing_template", "LMC_spire_armor_trim_smithing_template",
            "LMC_wayfinder_armor_trim_smithing_template", "LMC_shaper_armor_trim_smithing_template", "LMC_silence_armor_trim_smithing_template",
            "LMC_raiser_armor_trim_smithing_template", "LMC_host_armor_trim_smithing_template", "LMC_flow_armor_trim_smithing_template",
            "LMC_bolt_armor_trim_smithing_template", "LMC_enchanted_book",
        };
        /// <summary>Items added after 1.53.0 (a 1.53.0 save, which has no names, is read in the list without them): every
        /// item a later version adds goes here.</summary>
        static readonly HashSet<string> NewAfter1530 = new HashSet<string>
        {
            // (#244 part D)
            "LMC_lapis_block", "LMC_emerald_block", "LMC_netherite_block", "LMC_polished_basalt", "LMC_packed_ice",
            "LMC_blue_ice", "LMC_snow", "LMC_coarse_dirt", "LMC_dirt_path", "LMC_lantern", "LMC_soul_lantern",
            "LMC_soul_torch", "LMC_white_carpet", "LMC_white_stained_glass", "LMC_white_stained_glass_pane",
            "LMC_white_bed", "LMC_orange_carpet", "LMC_orange_stained_glass", "LMC_orange_stained_glass_pane",
            "LMC_orange_bed", "LMC_magenta_carpet", "LMC_magenta_stained_glass", "LMC_magenta_stained_glass_pane",
            "LMC_magenta_bed", "LMC_light_blue_carpet", "LMC_light_blue_stained_glass",
            "LMC_light_blue_stained_glass_pane", "LMC_light_blue_bed", "LMC_yellow_carpet", "LMC_yellow_stained_glass",
            "LMC_yellow_stained_glass_pane", "LMC_yellow_bed", "LMC_lime_carpet", "LMC_lime_stained_glass",
            "LMC_lime_stained_glass_pane", "LMC_lime_bed", "LMC_pink_carpet", "LMC_pink_stained_glass",
            "LMC_pink_stained_glass_pane", "LMC_pink_bed", "LMC_gray_carpet", "LMC_gray_stained_glass",
            "LMC_gray_stained_glass_pane", "LMC_gray_bed", "LMC_light_gray_carpet", "LMC_light_gray_stained_glass",
            "LMC_light_gray_stained_glass_pane", "LMC_light_gray_bed", "LMC_cyan_carpet", "LMC_cyan_stained_glass",
            "LMC_cyan_stained_glass_pane", "LMC_cyan_bed", "LMC_purple_carpet", "LMC_purple_stained_glass",
            "LMC_purple_stained_glass_pane", "LMC_purple_bed", "LMC_blue_carpet", "LMC_blue_stained_glass",
            "LMC_blue_stained_glass_pane", "LMC_blue_bed", "LMC_brown_carpet", "LMC_brown_stained_glass",
            "LMC_brown_stained_glass_pane", "LMC_brown_bed", "LMC_green_carpet", "LMC_green_stained_glass",
            "LMC_green_stained_glass_pane", "LMC_green_bed", "LMC_red_carpet", "LMC_red_stained_glass",
            "LMC_red_stained_glass_pane", "LMC_black_carpet", "LMC_black_stained_glass",
            "LMC_black_stained_glass_pane", "LMC_black_bed", "LMC_crimson_hyphae", "LMC_stripped_crimson_stem",
            "LMC_stripped_crimson_hyphae", "LMC_crimson_planks", "LMC_crimson_stairs", "LMC_crimson_slab",
            "LMC_crimson_fence", "LMC_crimson_fence_gate", "LMC_crimson_door", "LMC_crimson_trapdoor",
            "LMC_crimson_button", "LMC_crimson_pressure_plate", "LMC_crimson_sign", "LMC_crimson_hanging_sign",
            "LMC_crimson_shelf", "LMC_warped_hyphae", "LMC_stripped_warped_stem", "LMC_stripped_warped_hyphae",
            "LMC_warped_planks", "LMC_warped_stairs", "LMC_warped_slab", "LMC_warped_fence", "LMC_warped_fence_gate",
            "LMC_warped_door", "LMC_warped_trapdoor", "LMC_warped_button", "LMC_warped_pressure_plate",
            "LMC_warped_sign", "LMC_warped_hanging_sign", "LMC_warped_shelf", "LMC_charcoal",
            // (#244 part E)
            "LMC_leather_helmet", "LMC_leather_chestplate", "LMC_leather_leggings", "LMC_leather_boots", "LMC_copper_helmet",
            "LMC_copper_chestplate", "LMC_copper_leggings", "LMC_copper_boots", "LMC_chainmail_helmet", "LMC_chainmail_chestplate",
            "LMC_chainmail_leggings", "LMC_chainmail_boots", "LMC_copper_pickaxe", "LMC_copper_shovel", "LMC_copper_axe",
            "LMC_copper_hoe", "LMC_copper_sword", "LMC_copper_nugget", "LMC_pumpkin_pie", "LMC_poisonous_potato",
            "LMC_egg", "LMC_cake", "LMC_firework_star",
        };
        public static int Remapped; public static string LastLoad = "-"; // (dev/tests)

        static List<Item> List => StartOfRound.Instance != null && StartOfRound.Instance.allItemsList != null ? StartOfRound.Instance.allItemsList.itemsList : null;

        /// <summary>Host, saving (after the game's own SaveItemsInShip): each saved item's name.</summary>
        public static void Save(string file)
        {
            try
            {
                var list = List;
                if (list == null) return;
                if (!ES3.KeyExists(IdsKey, file)) { if (ES3.KeyExists(NamesKey, file)) ES3.DeleteKey(NamesKey, file); return; }
                var ids = ES3.Load<int[]>(IdsKey, file);
                ES3.Save(NamesKey, ids.Select(i => i >= 0 && i < list.Count && list[i] != null ? list[i].name : "").ToArray(), file);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("ship item names save: " + e.Message); }
        }

        [HarmonyPatch(typeof(StartOfRound), "LoadShipGrabbableItems"), HarmonyPrefix, HarmonyPriority(Priority.First)]
        static void Remap()
        {
            try
            {
                var list = List; var gnm = GameNetworkManager.Instance;
                if (list == null || gnm == null) return;
                string file = gnm.currentSaveFileName;
                if (!ES3.KeyExists(IdsKey, file)) return;
                var ids = ES3.Load<int[]>(IdsKey, file);
                var at = new Dictionary<string, int>();
                for (int i = 0; i < list.Count; i++) if (list[i] != null && !at.ContainsKey(list[i].name)) at[list[i].name] = i;
                var fixedIds = (int[])ids.Clone();
                if (ES3.KeyExists(NamesKey, file))
                {
                    var names = ES3.Load<string[]>(NamesKey, file);
                    if (names.Length != ids.Length) return;
                    for (int i = 0; i < ids.Length; i++) if (!string.IsNullOrEmpty(names[i]) && at.TryGetValue(names[i], out int j)) fixedIds[i] = j;
                    LastLoad = "by name";
                }
                else
                {
                    // (no names: the list it was written in. 1.53.0's or 1.52's: the one whose items take exactly the game's
                    // saved scrap values and item data, one each for every scrap item and every item that saves a number; a
                    // 1.53.0 save first if it has 1.53.0's enchantment lists, though 1.52 can have saved over one since)
                    int scraps = ES3.KeyExists("shipScrapValues", file) ? ES3.Load<int[]>("shipScrapValues", file).Length : 0;
                    int datas = ES3.KeyExists("shipItemSaveData", file) ? ES3.Load<int[]>("shipItemSaveData", file).Length : 0;
                    bool enchSets = ES3.KeyExists("LMC_EnchSets_v1", file);
                    List<Item> Layout(bool v1530) => list.Where(x => x == null || !(NewAfter1530.Contains(x.name) || (!v1530 && New153.Contains(x.name)))).ToList();
                    bool Fits(List<Item> old)
                    {
                        int sc = 0, sd = 0;
                        foreach (int id in ids)
                        {
                            if (id < 0 || id >= old.Count || old[id] == null) return false;
                            if (old[id].isScrap) sc++;
                            if (old[id].saveItemVariable) sd++;
                        }
                        return sc == scraps && sd == datas;
                    }
                    var first = Layout(enchSets); var second = Layout(!enchSets);
                    bool firstFits = Fits(first), secondFits = Fits(second);
                    bool use1530 = firstFits || !secondFits ? enchSets : !enchSets;
                    var old2 = use1530 == enchSets ? first : second;
                    for (int i = 0; i < ids.Length; i++)
                        if (ids[i] >= 0 && ids[i] < old2.Count && old2[ids[i]] != null && at.TryGetValue(old2[ids[i]].name, out int j)) fixedIds[i] = j;
                    LastLoad = (use1530 ? "1.53.0's list" : "1.52's list") + $" (fits: {(enchSets ? "1.53.0" : "1.52")} {firstFits}, the other {secondFits})";
                }
                int moved = 0;
                for (int i = 0; i < ids.Length; i++) if (fixedIds[i] != ids[i]) moved++;
                if (moved > 0) { ES3.Save(IdsKey, fixedIds, file); Remapped += moved; }
                Plugin.Log.LogInfo($"Ship items: {ids.Length} saved, {moved} put back at their places ({LastLoad})");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("ship item ids: " + e.Message); }
        }
    }
}
