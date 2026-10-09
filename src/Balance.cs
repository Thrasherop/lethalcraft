using System.Collections.Generic;
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
            ["oak_log"] = 40, ["glass"] = 30, ["sand"] = 20, ["gravel"] = 20, ["bricks"] = 45, ["leaves"] = 12,
            ["stone_bricks"] = 35, ["snow_block"] = 15, ["ice"] = 25, ["bookshelf"] = 40,
            ["wool_white"] = 20, ["wool_red"] = 20, ["wool_blue"] = 20, ["wool_yellow"] = 20,
            // light: torches are cheap, light blocks cost more (per 32)
            ["torch"] = 10, ["glowstone"] = 100, ["jack_o_lantern"] = 100,
            // utility
            ["crafting_table"] = 50, ["furnace"] = 80, ["chest"] = 60, ["flint_and_steel"] = 30, ["obsidian"] = 250,
            // TNT: 20 for one; the 20-pack (tnt_20) is 200
            ["tnt"] = 20, ["tnt_20"] = 200,
            // the rest of the redstone stays cheap
            ["redstone_dust"] = 10, ["redstone_torch"] = 10, ["redstone_block"] = 20, ["redstone_lamp"] = 18,
            ["lever"] = 8, ["button"] = 6, ["pressure_plate"] = 10, ["note_block"] = 12,
            // the flying-machine parts: 4 pistons, 2 observers and 6 slime blocks come to 1000 credits
            ["piston"] = 100, ["sticky_piston"] = 100, ["observer"] = 100, ["slime"] = 800,
            // material blocks (crafted back into ingots / diamonds / coal for tools and armor)
            ["iron_block"] = 600, ["gold_block"] = 250, ["diamond_block"] = 1200, ["coal_block"] = 200,
            // food: about 8 credits a day per player (a steak is 4 credits; the rest by how much they fill you)
            ["steak"] = 24, ["porkchop"] = 24, ["bread"] = 17, ["apple"] = 10, ["cookie"] = 7, ["golden_apple"] = 60,
        };

        /// <summary>How many one purchase gives (blocks), where that changes from the built-in stacks.</summary>
        static readonly Dictionary<string, int> DefaultStacks = new Dictionary<string, int>
        {
            ["torch"] = 32, ["glowstone"] = 32, ["jack_o_lantern"] = 32, ["tnt"] = 1, ["observer"] = 2, ["slime"] = 6,
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
        public static bool MetalArmorDrawsLightning = true;

        /// <summary>The store price of an item from the config (bound the first time it's asked for).</summary>
        public static int PriceOf(string key, string name, int builtIn)
        {
            if (cfg == null || string.IsNullOrEmpty(key) || key.StartsWith("scrap_") || key == "ender_pearl") return builtIn; // (pearls: their own section)
            int def = DefaultPrices.TryGetValue(key, out var d) ? d : (builtIn > 0 ? builtIn : 0);
            return cfg.Bind("Store Prices", key, def, new ConfigDescription(
                $"{name}: store price in credits for one purchase (a stack for blocks and food). 0 = not sold. (PriceMultiplier in [Store] still applies.)",
                new AcceptableValueRange<int>(0, 100000))).Value;
        }

        // ------------------------------------------------------------------ ore values (host)
        static void OreValues()
        {
            foreach (var (def, min, max) in new[] { (Blocks.DiamondOre, 60, 100), (Blocks.EmeraldOre, 75, 125), (Blocks.GoldOre, 30, 50) })
            {
                int lo = cfg.Bind("Ore Values", def.ScrapName + " Min", min, $"Lowest sell value of {def.ScrapName} (mined from {def.Name}).").Value;
                int hi = cfg.Bind("Ore Values", def.ScrapName + " Max", max, $"Highest sell value of {def.ScrapName} (mined from {def.Name}).").Value;
                def.ScrapValueMin = System.Math.Max(1, System.Math.Min(lo, hi));
                def.ScrapValueMax = System.Math.Max(def.ScrapValueMin, hi);
            }
            // raw iron has one value (that's what lets it stack): a stack sells for count x this
            int iron = cfg.Bind("Ore Values", "Raw Iron", 8, "Sell value of each Raw Iron (they stack; a stack sells for count x this).").Value;
            Blocks.IronOre.ScrapValueMin = Blocks.IronOre.ScrapValueMax = System.Math.Max(1, iron);
        }

        // ------------------------------------------------------------------ how much ore (host)
        public static float VeinScale = 0.5f;
        public static float[] VeinWeights = { 34, 6, 20, 0, 6 }; // coal, iron, gold, diamond (0: see DiamondsPerPlayer), emerald
        public static float DiamondsPerPlayer = 2f;
        public static int SlimeballRarity = 20;

        static void OreSpawning()
        {
            const string S = "Ore Spawning";
            var r = GroundRules.Rates;
            float Pct(string ore, string which, float def, string what) =>
                cfg.Bind(S, $"{ore} {which} %", def, new ConfigDescription(what, new AcceptableValueRange<float>(0f, 100f))).Value / 100f;
            string Near(string ore) => $"Chance (percent) that a dug stone cell is {ore} ore, near open space.";
            string Deep(string ore) => $"Chance (percent) that a dug stone cell is {ore} ore, deep in the rock (about 20+ blocks from open space).";
            r.Coal = Pct("Coal", "Near", 2.5f, Near("coal")); r.CoalDeep = Pct("Coal", "Deep", 2.5f, Deep("coal"));
            r.Iron = Pct("Iron", "Near", 0.5f, Near("iron")); r.IronDeep = Pct("Iron", "Deep", 0.7f, Deep("iron"));
            r.Gold = Pct("Gold", "Near", 0.4f, Near("gold") + " (never right at a wall)"); r.GoldDeep = Pct("Gold", "Deep", 0.9f, Deep("gold"));
            r.Diamond = Pct("Diamond", "Near", 0.15f, Near("diamond") + " (only past DiamondMinDepth)"); r.DiamondDeep = Pct("Diamond", "Deep", 0.15f, Deep("diamond") + " (only past DiamondMinDepth)");
            r.DiamondMinDepth = cfg.Bind(S, "DiamondMinDepth", 30f, new ConfigDescription(
                "Diamond ore only turns up in stone at least this many blocks from any open space (the surface, caves, facility rooms): you dig down for it.",
                new AcceptableValueRange<float>(0f, 60f))).Value;
            r.Emerald = Pct("Emerald", "Near", 0.1f, Near("emerald") + " (never right at a wall)"); r.EmeraldDeep = Pct("Emerald", "Deep", 0.25f, Deep("emerald"));
            VeinScale = cfg.Bind(S, "VeinAmount", 0.5f, new ConfigDescription(
                "How many ore veins spawn along facility walls, relative to the original amount (1 = about one per 9 AI nodes, 3-12 per facility; 0 = none).",
                new AcceptableValueRange<float>(0f, 4f))).Value;
            DiamondsPerPlayer = cfg.Bind(S, "DiamondsPerPlayer", 2f, new ConfigDescription(
                "Diamond ore blocks placed inside each facility, per player in the lobby (in their own small veins, on top of the other veins).",
                new AcceptableValueRange<float>(0f, 20f))).Value;
            SlimeballRarity = cfg.Bind(S, "SlimeballRarity", 20, new ConfigDescription(
                "How often slimeballs turn up as scrap inside facilities (a scrap rarity weight like vanilla's, about 1-100; 0 = never).",
                new AcceptableValueRange<int>(0, 100))).Value;
            string[] names = { "Coal", "Iron", "Gold", "Diamond", "Emerald" };
            for (int i = 0; i < names.Length; i++)
                VeinWeights[i] = cfg.Bind(S, $"Vein Weight {names[i]}", VeinWeights[i], $"How likely a facility vein is {names[i].ToLower()} (relative to the other weights).").Value;
        }
    }
}
