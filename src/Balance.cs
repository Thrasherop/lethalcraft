using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace LethalMinecraft
{
    /// <summary>
    /// Balance knobs, all in the config file: what everything costs in the store, what ore sells for, how much ore there
    /// is (in dug stone and in the veins inside facilities), and how often blocks blown up by explosions drop. The defaults
    /// are a best guess at a curve where building blocks are cheap-ish, redstone is a real purchase, and the flying-machine
    /// parts (slime, observers, sticky pistons) are a late-game upgrade.
    /// Store prices are applied on each player's own game (the terminal works prices out locally), so everyone should use
    /// the same config; ore spawning and values are decided by the host.
    /// </summary>
    public static class Balance
    {
        static ConfigFile cfg;

        /// <summary>Credits per purchase (one purchase gives the item's stack). 0 = not sold.</summary>
        static readonly Dictionary<string, int> DefaultPrices = new Dictionary<string, int>
        {
            // building blocks (per stack); wood is 10 credits per 16 planks' worth (a log is 4 planks)
            ["grass"] = 20, ["dirt"] = 15, ["stone"] = 30, ["cobblestone"] = 25, ["oak_planks"] = 20, ["dark_planks"] = 20,
            ["oak_log"] = 40, ["birch_log"] = 40, ["dark_oak_log"] = 40, ["mangrove_log"] = 40, // (#223: every log in the store, the owner's) ["glass"] = 30, ["sand"] = 20, ["gravel"] = 0, ["bow"] = 200, ["bricks"] = 45, ["leaves"] = 12,
            ["stone_bricks"] = 35, ["diorite"] = 30, ["glass_pane"] = 15, ["oak_door"] = 30, ["ladder"] = 10,
            ["oak_stairs"] = 15, ["oak_slab"] = 8, ["cobblestone_slab"] = 10, ["stone_brick_slab"] = 12, ["stone_slab"] = 10, ["oak_trapdoor"] = 20, ["cobblestone_stairs"] = 20, ["stone_brick_stairs"] = 25, ["snow_block"] = 15, ["ice"] = 25, ["bookshelf"] = 40,
            ["wool_white"] = 20, ["wool_red"] = 20, ["wool_blue"] = 20, ["wool_yellow"] = 20,
["wool_orange"] = 20, ["wool_magenta"] = 20, ["wool_light_blue"] = 20, ["wool_lime"] = 20, ["wool_pink"] = 20, ["wool_gray"] = 20, ["wool_light_gray"] = 20, ["wool_cyan"] = 20, ["wool_purple"] = 20, ["wool_brown"] = 20, ["wool_green"] = 20, ["wool_black"] = 20,
            // light: torches are cheap, light blocks cost more (per 32)
            ["torch"] = 10, ["glowstone"] = 100, ["jack_o_lantern"] = 100,
            // utility
            ["crafting_table"] = 50, ["furnace"] = 80, ["chest"] = 60, ["flint_and_steel"] = 30, ["obsidian"] = 250, ["totem_of_undying"] = 200,
            // TNT: 20 for one; the 20-pack (tnt_20) is 200
            ["tnt"] = 20, ["tnt_20"] = 200,
            // the rest of the redstone stays cheap
            ["redstone_dust"] = 10, ["redstone_torch"] = 10, ["redstone_block"] = 20, ["redstone_lamp"] = 18,
            ["lever"] = 8, ["button"] = 6, ["pressure_plate"] = 10, ["note_block"] = 12,
            // the flying-machine parts: 4 pistons, 2 observers and 6 slime blocks come to 1000 credits
            ["piston"] = 100, ["sticky_piston"] = 100, ["observer"] = 100, ["slime"] = 800, ["honey_block"] = 600,
            // material blocks (crafted back into ingots / diamonds / coal for tools and armor)
            ["iron_block"] = 600, ["gold_block"] = 250, ["diamond_block"] = 1200, ["coal_block"] = 200,
            // food: about 8 credits a day per player (a steak is 4 credits; the rest by how much they fill you)
            ["steak"] = 24, ["porkchop"] = 24, ["bread"] = 17, ["apple"] = 10, ["cookie"] = 7, ["golden_apple"] = 60,
        };

        /// <summary>How many one purchase gives (blocks), where that changes from the built-in stacks.</summary>
        static readonly Dictionary<string, int> DefaultStacks = new Dictionary<string, int>
        {
            ["torch"] = 32, ["glowstone"] = 32, ["jack_o_lantern"] = 32, ["tnt"] = 1, ["observer"] = 2, ["slime"] = 6, ["honey_block"] = 6,
            ["piston"] = 4, ["sticky_piston"] = 4,
        };

        public static void Init(ConfigFile config)
        {
            cfg = config;
            OreValues();
            OreSpawning();
            // how many one purchase gives, per block sold in the store
            foreach (var bd in Blocks.All)
            {
                if (bd.ShopPrice <= 0) continue;
                int def = DefaultStacks.TryGetValue(bd.Key, out var st) ? st : bd.ShopStack;
                bd.ShopStack = System.Math.Max(1, cfg.Bind("Store Stacks", bd.Key, def, new ConfigDescription(
                    $"{bd.Name}: how many one store purchase gives.", new AcceptableValueRange<int>(1, 64))).Value);
            }
            Tnt20Count = System.Math.Max(1, cfg.Bind("Store Stacks", "tnt_20", 20, new ConfigDescription(
                "TNT: how many the TNT pack gives.", new AcceptableValueRange<int>(1, 64))).Value);
            FoodPerDay = cfg.Bind("Survival", "FoodPerDay", 2f, new ConfigDescription(
                "How much food a day on a moon uses up just by being there, in steaks (a steak is 8 hunger + 12.8 saturation), from landing at 8am to 6pm; sprinting, jumping and mining use more. 0 = only activity makes you hungry. Hunger never drains in orbit.",
                new AcceptableValueRange<float>(0f, 20f))).Value;
            ArmorPerPoint = cfg.Bind("Armor", "HealthPerArmorPoint", 0.02f, "Extra effective health per armor point, as a fraction (0.02 = 2%). Armor only works by reducing damage: damage / (1 + total). Full iron (15 points) = +30%, full gold (11) = +22%.").Value;
            ArmorPerToughness = cfg.Bind("Armor", "HealthPerToughness", 0.03125f, "Extra effective health per point of toughness (diamond pieces have 2 each). Full diamond (20 points, 8 toughness) = +65%.").Value;
            MetalArmorDrawsLightning = cfg.Bind("Balance", "MetalArmorDrawsLightning", true,
                "In a storm, players wearing iron or gold armor outdoors can be struck by lightning (more pieces, more often), with a few seconds' warning. Diamond isn't metal.").Value;
            ToolDurability = cfg.Bind("Balance", "ToolDurability", true,
                "Tools wear out like Minecraft's: a use per block mined, one per hit (two for a pickaxe or shovel); wood 59 uses, stone 131, iron 250, diamond 1561.").Value;
            DurabilityMultiplier = cfg.Bind("Balance", "DurabilityMultiplier", 1f, new ConfigDescription(
                "How long tools last, relative to Minecraft's (2 = twice as long).", new AcceptableValueRange<float>(0.1f, 20f))).Value;
            StarterSteakPerPlayer = cfg.Bind("Starter", "SteakPerPlayer", 48,
                "Starting food: steak put in the ship's supply chest for each player, the first time they're aboard on a save (0 = none).").Value;
            StarterPlanksPerPlayer = cfg.Bind("Starter", "PlanksPerPlayer", 32,
                "Starting wood: oak planks put in the ship's supply chest for each player, the first time they're aboard on a save (0 = none).").Value;
            StarterAgainOnTeamWipe = cfg.Bind("Starter", "AgainAfterTeamWipe", true,
                "After the whole crew dies, everyone gets their starting supplies again (in the supply chest). Being fired always starts them over.").Value;
            WipeShipChestsOnTeamWipe = cfg.Bind("Balance", "WipeShipChestsOnTeamWipe", true,
                "When the whole crew dies, everything stored in chests on the ship is lost (like scrap in the ship).").Value;
            ExplosionDropChance = cfg.Bind("Balance", "ExplosionDropChance", 0.25f, new ConfigDescription(
                "Chance that a block broken by an explosion drops as an item (0 = explosions destroy everything, 1 = everything drops).",
                new AcceptableValueRange<float>(0f, 1f))).Value;
        }

        public static float ExplosionDropChance = 0.25f;

        // ------------------------------------------------------------------ the host's prices for everyone
        /// <summary>Every store item by its key, and the price this game charges for it (after PriceMultiplier).</summary>
        public static readonly Dictionary<string, Item> ShopItems = new Dictionary<string, Item>();
        public static readonly Dictionary<string, int> ShopPrices = new Dictionary<string, int>();

        /// <summary>Client: the host's store prices (only the host's config counts). An item this game doesn't sell at
        /// all can't be added this way: "not sold" (0) still has to match.</summary>
        public static void ApplyHostPrices(Dictionary<string, int> prices)
        {
            int changed = 0, missing = 0;
            foreach (var kv in prices)
            {
                if (!ShopItems.TryGetValue(kv.Key, out var item)) { missing++; continue; }
                if (item.creditsWorth == kv.Value) continue;
                try { LethalLib.Modules.Items.UpdateShopItemPrice(item, kv.Value); changed++; }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Store price for {kv.Key}: {e.Message}"); }
                ShopPrices[kv.Key] = kv.Value;
            }
            Plugin.Log.LogInfo($"Store prices from the host: {changed} changed" + (missing > 0 ? $", {missing} the host sells that this game doesn't (set them in everyone's config)" : ""));
        }

        /// <summary>Damage per hit against monsters (the shovel's is 1; fractions carry over to the next hit).</summary>
        public static float DamageOf(string key, string name, ToolKind kind, int tier)
        {
            float def;
            if (kind == ToolKind.Sword || kind == ToolKind.Axe) def = tier >= 4 ? 2f : tier == 3 ? 1f : 0.5f; // diamond 2 shovels, iron 1, the rest half
            else def = 0.5f;                                                                                     // pickaxes and shovels: half a shovel
            if (cfg == null) return def;
            return cfg.Bind("Damage", key, def, new ConfigDescription($"{name}: damage per hit on monsters (a shovel hit is 1).",
                new AcceptableValueRange<float>(0f, 20f))).Value;
        }

        /// <summary>Armor: extra effective health per armor point and per point of toughness (full iron +30%, full diamond +65%).</summary>
        public static float ArmorPerPoint = 0.02f, ArmorPerToughness = 0.03125f;
        public static int Tnt20Count = 20;
        public static float FoodPerDay = 2f;
        public static bool WipeShipChestsOnTeamWipe = true;
        public static int StarterSteakPerPlayer = 48, StarterPlanksPerPlayer = 32;
        public static bool StarterAgainOnTeamWipe = true;
        public static bool ToolDurability = true;
        public static float DurabilityMultiplier = 1f;
        public static bool MetalArmorDrawsLightning = true;

        /// <summary>The store price of an item from the config (bound the first time it's asked for).</summary>
        /// <summary>Store defaults that changed, by what they used to be (the owner's prices for the elytra and rockets, #61).</summary>
        static readonly Dictionary<string, int> OldDefaults = new Dictionary<string, int> { ["gravel"] = 20, ["elytra"] = 1000, ["firework_rocket"] = 120, ["totem_of_undying"] = 150 };

        public static int PriceOf(string key, string name, int builtIn)
        {
            if (cfg == null || string.IsNullOrEmpty(key) || key.StartsWith("scrap_") || key == "ender_pearl") return builtIn; // (pearls: their own section)
            int def = DefaultPrices.TryGetValue(key, out var d) ? d : (builtIn > 0 ? builtIn : 0);
            var entry = cfg.Bind("Store Prices", key, def, new ConfigDescription(
                $"{name}: store price in credits for one purchase (a stack for blocks and food). 0 = not sold. (PriceMultiplier in [Store] still applies.)",
                new AcceptableValueRange<int>(0, 100000)));
            // (a default that changed: a config still holding the old default takes the new one; a price someone set stays)
            if (OldDefaults.TryGetValue(key, out var old) && entry.Value == old && old != def) entry.Value = def;
            return entry.Value;
        }

        // ------------------------------------------------------------------ ore values (host)
        static void OreValues()
        {
            foreach (var (def, min, max) in new[] { (Blocks.DiamondOre, 45, 65), (Blocks.EmeraldOre, 75, 125), (Blocks.GoldOre, 30, 50) })
            {
                // (keys renamed in 1.4.9: the 1.4.8 defaults written to everyone's config would otherwise stay)
                int lo = cfg.Bind("Ore Values", def.ScrapName + " value min", min, $"Lowest sell value of {def.ScrapName} (mined from {def.Name}).").Value;
                int hi = cfg.Bind("Ore Values", def.ScrapName + " value max", max, $"Highest sell value of {def.ScrapName} (mined from {def.Name}).").Value;
                def.ScrapValueMin = System.Math.Max(0, System.Math.Min(lo, hi));
                def.ScrapValueMax = System.Math.Max(def.ScrapValueMin, hi);
            }
            // raw iron has one value (that's what lets it stack): a stack sells for count x this. 0: it's for crafting, not selling
            int iron = cfg.Bind("Ore Values", "Raw Iron value", 0, "Sell value of each Raw Iron (they stack; a stack sells for count x this). 0 = worth nothing to the Company: iron is for crafting.").Value;
            Blocks.IronOre.ScrapValueMin = Blocks.IronOre.ScrapValueMax = System.Math.Max(0, iron);
        }

        // ------------------------------------------------------------------ how much ore (host)
        public static float VeinScale = 0.5f;
        public static float[] VeinWeights = { 34, 6, 20, 0, 6 }; // coal, iron, gold, diamond (0: see DiamondsPerPlayer), emerald
        public static float DiamondsPerPlayer = 2f, DiamondRandomness = 0.75f;
        public static int SlimeballRarity = 20;
        public static int FarmLootPerMoon = 3;
        static string riskMultipliers = "D=1, C=1, B=1.15, A=1.3, S=1.5, S+=1.7, S++=1.8", moonMultipliers = "";

        static void OreSpawning()
        {
            const string S = "Ore Spawning";
            // (#220, the owner's: each 4x4x4 chunk of stone holds at most one ore vein, VeinChancePerChunk of the time, times the
            // moon's multiplier; its ore picked by "chance at depth" among those past their min depth, blocks below the surface)
            GroundVeins.VeinChance = cfg.Bind(S, "VeinChancePerChunk", 0.15f, new ConfigDescription(
                "How likely each 4x4x4-block chunk of stone is to hold an ore vein (at most one), before the moon's ore multiplier below. " +
                "Which ore it is: the \"chance at depth\" of each ore deep enough there. 0 = no ore out on the moons.",
                new AcceptableValueRange<float>(0f, 1f))).Value;
            foreach (var k in GroundVeins.Ores)
            {
                string name = k.Ore.ToString();
                // (DiamondMinDepth, LapisMinDepth and RedstoneMinDepth were there before, the same meaning: kept; lapis went from
                // 20 to 30 blocks down: a config still holding the old default takes the new one)
                var md = cfg.Bind(S, $"{name}MinDepth", k.MinDepth, new ConfigDescription(
                    $"{name} ore only turns up in chunks at least this many blocks below the surface (straight up). Anything under world y -100 (the facility's rock) counts as deep.",
                    new AcceptableValueRange<float>(0f, 100f)));
                if (k.Ore == GroundRules.Ore.Lapis && md.Value == 20f) md.Value = k.MinDepth;
                k.MinDepth = md.Value;
                k.Chance = cfg.Bind(S, $"{name} chance at depth", k.Chance, new ConfigDescription(
                    $"How likely a chunk's ore vein is {name.ToLower()}, once past {name}MinDepth: a weight against the other ores deep enough there (0 = none).",
                    new AcceptableValueRange<float>(0f, 1000f))).Value;
                Sizes(k, name);
            }
            foreach (var k in GroundVeins.Layers)
            {
                string name = k.Ore.ToString();
                if (k.Ore == GroundRules.Ore.Lava)
                    k.MinDepth = cfg.Bind(S, "LavaMinDepth", k.MinDepth, new ConfigDescription(
                        "Lava pockets only turn up in chunks at least this many blocks below the surface.", new AcceptableValueRange<float>(0f, 100f))).Value;
                k.Chance = cfg.Bind(S, $"{name} chance per chunk", k.Chance, new ConfigDescription(
                    $"How likely a chunk of stone holds a {(k.Ore == GroundRules.Ore.Lava ? "lava pocket" : name.ToLower() + " patch")}: on its own, besides any ore vein (0 = none).",
                    new AcceptableValueRange<float>(0f, 1f))).Value;
                Sizes(k, name);
            }
            void Sizes(GroundVeins.Kind k, string name)
            {
                k.Min = cfg.Bind(S, $"{name} vein min", k.Min, new ConfigDescription($"Smallest {name.ToLower()} vein (blocks).", new AcceptableValueRange<int>(1, 30))).Value;
                k.Max = System.Math.Max(k.Min, cfg.Bind(S, $"{name} vein max", k.Max, new ConfigDescription($"Biggest {name.ToLower()} vein (blocks).", new AcceptableValueRange<int>(1, 30))).Value);
                k.Avg = UnityEngine.Mathf.Clamp(cfg.Bind(S, $"{name} vein average", k.Avg, $"Average {name.ToLower()} vein size (between min and max).").Value, k.Min, k.Max);
            }
            riskMultipliers = cfg.Bind(S, "Ore multiplier by risk level", riskMultipliers,
                "More ore on harder moons: a multiplier for each moon risk level (ore out on the moon and the facility's veins).").Value;
            moonMultipliers = cfg.Bind(S, "Ore multiplier by moon", moonMultipliers,
                "Per-moon multipliers that override the risk level's, e.g. \"Titan=2, Rend=1.4\" (moon names as the terminal shows them, without the number).").Value;
            VeinScale = cfg.Bind(S, "VeinAmount", 0.5f, new ConfigDescription(
                "How many ore veins spawn along facility walls (the ones you can see from the halls), relative to the original amount (1 = about one per 9 AI nodes, 3-12 per facility; 0 = none). Times the moon's ore multiplier.",
                new AcceptableValueRange<float>(0f, 4f))).Value;
            DiamondsPerPlayer = cfg.Bind(S, "DiamondsPerPlayer", 2f, new ConfigDescription(
                "Diamond ore blocks placed inside each facility on average, per player in the lobby (in their own small veins, on top of the other veins).",
                new AcceptableValueRange<float>(0f, 20f))).Value;
            DiamondRandomness = cfg.Bind(S, "DiamondRandomness", 0.75f, new ConfigDescription(
                "How much the facility's diamonds vary: each facility gets the average times a random factor between 1 - this and 1 + this (0.75: a quarter to 1.75 times; 1: anywhere from none to double).",
                new AcceptableValueRange<float>(0f, 1f))).Value;
            FarmLootPerMoon = cfg.Bind("Farming", "LootPerMoon", 3, new ConfigDescription(
                "How many piles of seeds, sugar cane, carrots or potatoes lie around inside each facility, worth nothing (0 = none; the store sells them too).",
                new AcceptableValueRange<int>(0, 20))).Value;
            SlimeballRarity = cfg.Bind(S, "SlimeballRarity", 20, new ConfigDescription(
                "How often slimeballs turn up as scrap inside facilities (a scrap rarity weight like vanilla's, about 1-100; 0 = never).",
                new AcceptableValueRange<int>(0, 100))).Value;
            string[] names = { "Coal", "Iron", "Gold", "Diamond", "Emerald" };
            for (int i = 0; i < names.Length; i++)
                VeinWeights[i] = cfg.Bind(S, $"Vein Weight {names[i]}", VeinWeights[i], $"How likely a facility vein is {names[i].ToLower()} (relative to the other weights).").Value;
            GroundVeins.Reset();
            Plugin.Log.LogInfo($"Ore veins: {GroundVeins.VeinChance:0.###} a chunk; " + string.Join(", ", GroundVeins.Ores.Select(k => $"{k.Ore} from {k.MinDepth:0} down x{k.Chance:0.#}")) +
                " | " + string.Join(", ", GroundVeins.Layers.Select(k => $"{k.Ore} {k.Chance:0.###} a chunk")));
        }

        /// <summary>Host, when a moon loads: its ore multiplier (risk level, or the per-moon override).</summary>
        public static float OreMultiplierFor(SelectableLevel level)
        {
            if (level == null) return 1f;
            string Name(string s) => new string(s.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            float? Find(string list, System.Func<string, bool> match)
            {
                foreach (var part in (list ?? "").Split(','))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && match(kv[0].Trim()) && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
                }
                return null;
            }
            string moon = Name(level.PlanetName ?? "");
            return Find(moonMultipliers, k => Name(k) == moon)
                // (risk levels compare with their pluses: S+ isn't S)
                ?? Find(riskMultipliers, k => k.Equals((level.riskLevel ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase))
                ?? 1f;
        }
    }
}
