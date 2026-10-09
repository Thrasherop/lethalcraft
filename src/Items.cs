using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    public class FoodDef
    {
        public string Key, Name, Tile;
        public int Hunger;
        public float Saturation;
        public int Price, Stack;
        public bool Golden;
        public string Description;
    }

    public static class ModItems
    {
        public static readonly Dictionary<string, Item> ByKey = new Dictionary<string, Item>();
        static readonly Dictionary<byte, Item> byBlock = new Dictionary<byte, Item>();
        public static readonly Dictionary<string, FoodDef> Foods = new Dictionary<string, FoodDef>();
        public static Item Pickaxe, FlintAndSteel;
        static readonly Dictionary<BlockDef, Item> scrapByOre = new Dictionary<BlockDef, Item>();
        static int nextItemId = 41000;
        public static AudioClip GrabSfx, DropSfx, PocketSfx;

        public static void RefreshSfx()
        {
            foreach (var item in ByKey.Values)
            {
                // (no grab/pocket sound: the game plays them on every slot change; the pickup pop is Pickup's)
                item.grabSFX = null; item.pocketSFX = null;
                string fam = "stone";
                var bd = Blocks.Get(item.name.Replace("LMC_", ""));
                if (bd != null) fam = Sounds.Family(bd);
                var drop = Sounds.Get("step." + fam);
                if (drop != null) item.dropSFX = drop;
            }
        }

        public static Item ItemForBlock(BlockDef d) => d != null && byBlock.TryGetValue(d.Id, out var i) ? i : null;

        public static int Price(int baseP) => Mathf.Max(1, Mathf.RoundToInt(baseP * Plugin.PriceMultiplier.Value));

        public static void Register()
        {
            GrabSfx = Sounds.Get("pop");
            DropSfx = Sounds.Get("dig.stone");
            PocketSfx = Sounds.Get("pop");

            foreach (var b in Blocks.All)
            {
                if (b.Shape == BlockShape.PistonHead || b.Shape == BlockShape.Fire || b.ScrapValueMin > 0) continue;
                var item = BlockItem(b, b.Key, b.Name, b.ShopStack);
                ByKey[b.Key] = item;
                byBlock[b.Id] = item;
                Finish(item, b.ShopPrice, $"{b.Name} x{b.ShopStack}. {b.Description}\n\nPlace with [Right-click], break with [Left-click] (bare hands are slow, a Pickaxe is fast). Blocks stack up to 64 per hotbar slot.");
                if (b == Blocks.TNT)
                {
                    // a pack of TNT: the same TNT, cheaper each (its own store entry; it isn't a separate item kind)
                    // (no number in its name: the terminal reads any number in an order as the amount, so "buy tnt x20"
                    // ordered ten crates)
                    var pack = BlockItem(b, "tnt_20", "TNT Crate", Balance.Tnt20Count);
                    Finish(pack, 200, $"TNT x{Balance.Tnt20Count}. A crate of TNT: cheaper each than buying them one at a time.\n\n{b.Description}");
                }
            }

            AddFood(new FoodDef { Key = "bread", Name = "Bread", Tile = "food_bread", Hunger = 5, Saturation = 6f, Price = 6, Stack = 8, Description = "Fresh bread. Restores 2.5 hunger." });
            AddFood(new FoodDef { Key = "steak", Name = "Steak", Tile = "food_steak", Hunger = 8, Saturation = 12.8f, Price = 12, Stack = 6, Description = "Cooked steak. The best food money can buy. Restores 4 hunger." });
            AddFood(new FoodDef { Key = "apple", Name = "Apple", Tile = "food_apple", Hunger = 4, Saturation = 2.4f, Price = 3, Stack = 8, Description = "A crisp apple. Restores 2 hunger." });
            AddFood(new FoodDef { Key = "cookie", Name = "Cookie", Tile = "food_cookie", Hunger = 2, Saturation = 0.4f, Price = 2, Stack = 16, Description = "A tasty snack. Restores 1 hunger." });
            AddFood(new FoodDef { Key = "porkchop", Name = "Cooked Porkchop", Tile = "food_porkchop", Hunger = 8, Saturation = 12.8f, Price = 12, Stack = 6, Description = "Juicy porkchop. Restores 4 hunger." });
            AddFood(new FoodDef { Key = "golden_apple", Name = "Golden Apple", Tile = "food_golden_apple", Hunger = 4, Saturation = 9.6f, Price = 60, Stack = 1, Golden = true, Description = "Grants Regeneration II for 5 seconds and 2 golden Absorption hearts. Expensive, worth it." });

            // tools: pickaxe / shovel / axe in wood, stone, iron and diamond (the old "pickaxe" key is the diamond pickaxe).
            // None are sold: you buy wood, craft a wooden pickaxe and work your way up.
            var toolDefs = new (string key, string name, ToolKind kind, int tier, float speed, int price, string tile)[]
            {
                ("wooden_pickaxe", "Wooden Pickaxe", ToolKind.Pickaxe, 1, 2f, -1, "item_wooden_pickaxe"),
                ("stone_pickaxe", "Stone Pickaxe", ToolKind.Pickaxe, 2, 4f, -1, "item_stone_pickaxe"),
                ("iron_pickaxe", "Iron Pickaxe", ToolKind.Pickaxe, 3, 6f, -1, "item_iron_pickaxe"),
                ("pickaxe", "Diamond Pickaxe", ToolKind.Pickaxe, 4, 8f, -1, "item_pickaxe"),
                ("wooden_shovel", "Wooden Shovel", ToolKind.Shovel, 1, 2f, -1, "item_wooden_shovel"),
                ("stone_shovel", "Stone Shovel", ToolKind.Shovel, 2, 4f, -1, "item_stone_shovel"),
                ("iron_shovel", "Iron Shovel", ToolKind.Shovel, 3, 6f, -1, "item_iron_shovel"),
                ("diamond_shovel", "Diamond Shovel", ToolKind.Shovel, 4, 8f, -1, "item_diamond_shovel"),
                ("wooden_axe", "Wooden Axe", ToolKind.Axe, 1, 2f, -1, "item_wooden_axe"),
                ("stone_axe", "Stone Axe", ToolKind.Axe, 2, 4f, -1, "item_stone_axe"),
                ("iron_axe", "Iron Axe", ToolKind.Axe, 3, 6f, -1, "item_iron_axe"),
                ("diamond_axe", "Diamond Axe", ToolKind.Axe, 4, 8f, -1, "item_diamond_axe"),
            };
            foreach (var td in toolDefs)
            {
                var item = MakeItem(td.key, td.name, td.price, 1);
                float toolDamage = Balance.DamageOf(td.key, td.name, td.kind, td.tier);
                item.isDefensiveWeapon = true;
                item.isConductiveMetal = td.tier == 3; // iron (diamond isn't metal)
                item.weight = td.tier <= 2 ? 1.04f : 1.06f;
                item.holdButtonUse = true;
                item.toolTips = new[] { td.kind == ToolKind.Pickaxe ? "Mine stone & ores / swing : [LMB]" : td.kind == ToolKind.Shovel ? "Dig dirt & sand / swing : [LMB]" : "Chop wood / swing : [LMB]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                var prefab = MakePrefab(item, out var model);
                var t = prefab.AddComponent<ToolItem>();
                t.AttackForce = toolDamage;
                t.Kind = td.kind; t.Tier = td.tier; t.Speed = td.speed; t.ItemKey = td.key;
                Setup(t, item, model);
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey(td.tile) ? td.tile : "item_pickaxe", 0.55f);
                if (td.key == "pickaxe") Pickaxe = item;
                ByKey[td.key] = item;
                string what = td.kind == ToolKind.Pickaxe ? "stone, ores and metal blocks" : td.kind == ToolKind.Shovel ? "dirt, grass, sand, gravel and snow" : "wood, planks and wooden blocks";
                string needs = td.kind == ToolKind.Pickaxe ? (td.tier == 1 ? " Can harvest stone, cobblestone and coal ore." : td.tier == 2 ? " Can harvest iron ore too." : td.tier == 3 ? " Can harvest gold, diamond and emerald ore." : " Can harvest everything, including obsidian.") : "";
                Finish(item, td.price, $"{td.name}. Breaks {what} much faster.{needs}\n\nHold [Left-click] on a block to break it. Swing at monsters to hit them.\n\nBetter tools are crafted at a Crafting Table.");
            }

            // swords: crafted only (never sold). Minecraft's damage over 5 (the shovel hits for 1), a swing every 0.7 s:
            // a wooden one about matches the shovel, a diamond one is a good deal deadlier
            foreach (var (key, name, tier, force, metal) in new[]
            {
                ("wooden_sword", "Wooden Sword", 1, 0.8f, false), ("golden_sword", "Golden Sword", 1, 0.8f, true),
                ("stone_sword", "Stone Sword", 2, 1.0f, false), ("iron_sword", "Iron Sword", 3, 1.2f, true), ("diamond_sword", "Diamond Sword", 4, 1.4f, false),
            })
            {
                var item = MakeItem(key, name, -1, 1);
                item.isDefensiveWeapon = true;
                item.isConductiveMetal = metal;
                item.weight = tier >= 3 ? 1.06f : 1.04f;
                item.holdButtonUse = false;
                item.toolTips = new[] { "Swing : [LMB]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                var prefab = MakePrefab(item, out var model);
                var t = prefab.AddComponent<ToolItem>();
                t.Kind = ToolKind.Sword; t.Tier = tier; t.Speed = 1f; t.ItemKey = key;
                t.AttackForce = Balance.DamageOf(key, name, ToolKind.Sword, tier); t.AttackCooldown = 0.7f;
                Setup(t, item, model);
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_" + key) ? "item_" + key : "item_stick", 0.6f);
                ByKey[key] = item;
                Items.RegisterItem(item);
            }

            // crafting resources (stackable, not sold: you get them by mining, smelting and crafting)
            AddResource("stick", "Stick", "item_stick");
            AddResource("coal", "Coal", "item_coal");
            AddResource("iron_ingot", "Iron Ingot", "item_iron_ingot");
            AddResource("gold_ingot", "Gold Ingot", "item_gold_ingot");
            AddResource("diamond", "Diamond", "item_diamond");
            AddSlimeball();

            // armor: worn in the [I] inventory's armor slots (or right-click it in hand)
            foreach (var ad in Armor.Defs.Values)
            {
                var item = MakeItem(ad.Key, ad.Name, -1, 1);
                item.isConductiveMetal = ad.Metal;
                item.weight = ad.Slot == 1 ? 1.05f : 1.03f;
                item.toolTips = new[] { Plugin.PlaceWithLeftClick.Value ? "Put on : [LMB]" : "Put on : [Right-click]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                var prefab = MakePrefab(item, out var model);
                var ai = prefab.AddComponent<ArmorItem>();
                ai.ItemKey = ad.Key;
                Setup(ai, item, model);
                string tile = "item_" + ad.Key;
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : "item_iron_ingot", 0.45f);
                ByKey[ad.Key] = item;
                Items.RegisterItem(item);
            }

            // flint and steel
            {
                var item = MakeItem("flint_and_steel", "Flint and Steel", 15, 1);
                item.weight = 1.02f;
                item.toolTips = new[] { Plugin.PlaceWithLeftClick.Value ? "Light fire / TNT : [LMB]" : "Light fire / TNT : [Right-click]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                var prefab = MakePrefab(item, out var model);
                var f = prefab.AddComponent<FlintAndSteelItem>();
                Setup(f, item, model);
                BuildSpriteModel(model, "item_flint_and_steel", 0.4f);
                FlintAndSteel = item;
                ByKey["flint_and_steel"] = item;
                Finish(item, 15, "Strike it on the ground or a block to start a fire, or on TNT to light the fuse. Then run.");
            }
            // ender pearls: throwable teleport, found inside and/or sold in the store (configurable)
            if (Plugin.PearlsEnabled.Value) AddPearl();
            // ore scrap
            foreach (var b in Blocks.All.Where(x => x.ScrapValueMin > 0))
            {
                string tile = "scrap_" + b.ScrapName.ToLowerInvariant().Replace(" ", "_");
                bool stacks = b.ScrapValueMin == b.ScrapValueMax; // a fixed value (raw iron): they stack like blocks
                var item = MakeItem("scrap_" + b.Key, b.ScrapName, -1, stacks ? 64 : 1);
                item.isScrap = true;
                item.minValue = b.ScrapValueMin;
                item.maxValue = b.ScrapValueMax;
                item.weight = b == Blocks.CoalOre ? 1.02f : 1.05f;
                item.saveItemVariable = stacks; // (a stack saves its count)
                item.toolTips = new string[0];
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, 0f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.disallowUtilitySlot = true;
                var prefab = MakePrefab(item, out var model);
                if (stacks)
                {
                    var st = prefab.AddComponent<StackItem>();
                    Setup(st, item, model);
                    st.ItemKey = "scrap_" + b.Key; st.DefaultCount = 1; st.UnitValue = b.ScrapValueMin;
                }
                else Setup(prefab.AddComponent<OreScrapItem>(), item, model);
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : b.TileSide, 0.38f);
                var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
                scan.nodeType = 2;
                scrapByOre[b] = item;
                ByKey["scrap_" + b.Key] = item; // (the key everything else uses for it: Crafting.KeyOf, recipes, chests, the grid)
                Items.RegisterItem(item);
            }
            // metal draws lightning in a storm, like the game's own metal scrap and tools
            foreach (var k in Metal)
                if (ByKey.TryGetValue(k, out var mi)) mi.isConductiveMetal = true;
            Plugin.Log.LogInfo($"Registered {ByKey.Count} items");
        }

        public static readonly HashSet<string> Resources = new HashSet<string>();

        /// <summary>Items with metal in them (iron, gold, steel; redstone, which carries current, and the pistons
        /// and torches made with it): lightning goes for them in a storm.</summary>
        public static readonly string[] Metal =
        {
            "iron_ingot", "gold_ingot", "iron_block", "gold_block", "flint_and_steel",
            "redstone_dust", "redstone_block", "redstone_torch", "piston", "sticky_piston",
            "scrap_iron_ore", "scrap_gold_ore",
        }; // (+ iron and golden armor: Armor.Def.Metal)

        static void AddResource(string key, string name, string tile)
        {
            Resources.Add(key);
            var item = MakeItem(key, name, -1, 64);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.rotationOffset = new Vector3(0f, 0f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = key.EndsWith("ingot") || key == "diamond" ? 1.02f : 1.0f;
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = key;
            st.DefaultCount = 1;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : "item_stick", 0.36f);
            ByKey[key] = item;
            Items.RegisterItem(item);
        }

        /// <summary>Slimeballs: found inside facilities now and then (scrap, worth a little), crafted into slime blocks and
        /// sticky pistons like Minecraft's.</summary>
        static void AddSlimeball()
        {
            Resources.Add("slime_ball");
            var item = MakeItem("slime_ball", "Slimeball", -1, 64);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.0f;
            bool inside = Balance.SlimeballRarity > 0;
            if (inside) { item.isScrap = true; item.minValue = 8; item.maxValue = 20; }
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = "slime_ball";
            st.DefaultCount = 1;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_slime_ball") ? "item_slime_ball" : "item_coal", 0.34f);
            if (inside)
            {
                var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
                if (scan != null) scan.nodeType = 2;
            }
            ByKey["slime_ball"] = item;
            Finish(item, -1, "Slimeball. Nine make a slime block; one on a piston makes a sticky piston.");
            if (inside) Items.RegisterScrap(item, Balance.SlimeballRarity, Levels.LevelTypes.All);
        }

        public static Item EnderPearl;

        static void AddPearl()
        {
            bool buy = Plugin.PearlsBuyable.Value, inside = Plugin.PearlsSpawnInside.Value;
            var item = MakeItem("ender_pearl", "Ender Pearl", buy ? Plugin.PearlPrice.Value : -1, 16);
            item.toolTips = new[] { "Throw : [Right-click]", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.rotationOffset = new Vector3(0f, 0f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.0f;
            if (inside)
            {
                // a little scrap value when found in a facility (multiplied by the moon's scrap multiplier at spawn)
                item.isScrap = true;
                item.minValue = 30; item.maxValue = 60;
            }
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = "ender_pearl";
            st.DefaultCount = 1;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_ender_pearl") ? "item_ender_pearl" : "item_coal", 0.34f);
            if (inside)
            {
                var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
                if (scan != null) scan.nodeType = 2;
            }
            ByKey["ender_pearl"] = item;
            EnderPearl = item;
            if (buy) Finish(item, Plugin.PearlPrice.Value, "Ender Pearl. Throw it with [Right-click]: you teleport to wherever it lands and take 2.5 hearts of damage. Stacks up to 16.");
            else Items.RegisterItem(item);
            if (inside) Items.RegisterScrap(item, Plugin.PearlSpawnRarity.Value, Levels.LevelTypes.All);
        }

        static void AddFood(FoodDef fd)
        {
            Foods[fd.Key] = fd;
            var item = MakeItem(fd.Key, fd.Name, fd.Price, 64);
            item.holdButtonUse = true;
            item.toolTips = new[] { "Eat : hold [Right-click]", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.rotationOffset = new Vector3(0f, 0f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = fd.Key;
            st.Food = fd;
            st.DefaultCount = fd.Stack;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey(fd.Tile) ? fd.Tile : "food_bread", 0.36f);
            ByKey[fd.Key] = item;
            Finish(item, fd.Price, $"{fd.Name} x{fd.Stack}. {fd.Description}\n\nHold [Right-click] to eat. Keep your hunger bar up to regenerate health and keep sprinting.");
        }

        /// <summary>A block's item: an item named key (the store entry), holding that block, a stack of `stack`.</summary>
        static Item BlockItem(BlockDef b, string key, string name, int stack)
        {
            var item = MakeItem(key, name, b.ShopPrice, 64);
            item.toolTips = new[] { "Place block : [Right-click]", "" };
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.BlockType = b.Id;
            // a stack weighs a little (LC shows (weight-1)*105 lb): stone/metal ~3-6 lb, light stuff ~1 lb
            item.weight = b.Sound == "stone" || b.Sound == "metal" ? (b == Blocks.Obsidian || b.Sound == "metal" ? 1.06f : 1.03f) : 1.01f;
            st.ItemKey = b.Key;
            st.DefaultCount = stack;
            BuildBlockModel(model, b);
            item.verticalOffset = 0.14f * Plugin.S;
            if (b.Shape == BlockShape.Torch) { item.positionOffset = new Vector3(0f, 0.1f, 0f); item.rotationOffset = new Vector3(15f, 0f, -80f); }
            else { item.positionOffset = new Vector3(0f, 0.17f, 0f); item.rotationOffset = new Vector3(40f, 0f, 15f); }
            st.HeldScale = b.Shape == BlockShape.Cube ? 0.5f : 0.8f;
            item.restingRotation = Vector3.zero;
            return item;
        }

        static Item MakeItem(string key, string name, int price, int maxStack)
        {
            var item = ScriptableObject.CreateInstance<Item>();
            item.name = "LMC_" + key;
            item.itemName = name;
            item.itemId = nextItemId++;
            item.creditsWorth = price > 0 ? Price(price) : 0;
            item.weight = 1.0f;
            item.canBeGrabbedBeforeGameStart = true;
            item.isScrap = false;
            item.itemSpawnsOnGround = true;
            item.requiresBattery = false;
            item.automaticallySetUsingPower = false;
            item.itemIsTrigger = false;
            item.syncUseFunction = false;
            item.syncInteractLRFunction = false;
            item.saveItemVariable = true;
            item.twoHanded = false;
            item.twoHandedAnimation = false;
            item.disallowUtilitySlot = true;
            item.canBeInspected = false;
            item.highestSalePercentage = 80;
            item.grabAnim = "";
            item.useAnim = "";
            item.pocketAnim = "";
            item.throwAnim = "";
            // like Minecraft: a pop when you pick it up (Pickup), nothing when you switch to it or put it away
            // (the game plays grabSFX/pocketSFX on every slot change)
            item.grabSFX = null;
            item.dropSFX = DropSfx;
            item.pocketSFX = null;
            item.spawnPositionTypes = new List<ItemGroup>();
            item.meshVariants = new Mesh[0];
            item.materialVariants = new Material[0];
            Sprite icon = Atlas.IconFor(key);
            if (icon == null && Atlas.Tiles.ContainsKey("item_" + key)) icon = Atlas.IconFor("item_" + key);
            if (icon == null && key == "pickaxe") icon = Atlas.IconFor("item_pickaxe");
            if (icon == null && key.StartsWith("scrap_"))
            {
                var bd = Blocks.Get(key.Substring(6));
                if (bd != null) icon = Atlas.IconFor("scrap_" + bd.ScrapName.ToLowerInvariant().Replace(" ", "_")) ?? Atlas.IconFor(bd.Key);
            }
            if (icon == null && Foods.TryGetValue(key, out var fd)) icon = Atlas.IconFor(fd.Tile);
            item.itemIcon = icon ?? Atlas.IconFor("stone");
            return item;
        }

        static GameObject MakePrefab(Item item, out GameObject model)
        {
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab(item.name);
            // like vanilla items: the game parents props itself (hands, ship), so NGO must not sync parenting
            prefab.GetComponent<NetworkObject>().AutoObjectParentSync = false;
            prefab.tag = "PhysicsProp";
            prefab.layer = 6;
            var col = prefab.AddComponent<BoxCollider>();
            col.size = new Vector3(0.35f, 0.35f, 0.35f) * Mathf.Max(0.6f, Plugin.S);
            col.center = Vector3.zero;
            var audio = prefab.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.maxDistance = 20f;
            model = new GameObject("model");
            model.transform.SetParent(prefab.transform, false);
            model.layer = 6;
            model.AddComponent<MeshFilter>();
            var mr = model.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            // scan node
            var scan = new GameObject("ScanNode");
            scan.transform.SetParent(prefab.transform, false);
            scan.layer = 22;
            var sc = scan.AddComponent<BoxCollider>();
            sc.isTrigger = true;
            sc.size = Vector3.one * 0.5f;
            var sn = scan.AddComponent<ScanNodeProperties>();
            sn.headerText = item.itemName;
            sn.subText = "";
            sn.maxRange = 13;
            sn.minRange = 1;
            sn.requiresLineOfSight = true;
            sn.nodeType = 0;
            sn.creatureScanID = -1;
            item.spawnPrefab = prefab;
            return prefab;
        }

        static void Setup(GrabbableObject g, Item item, GameObject model)
        {
            g.itemProperties = item;
            g.grabbable = true;
            g.grabbableToEnemies = true;
            g.mainObjectRenderer = model.GetComponent<MeshRenderer>();
            g.useCooldown = 0f;
        }

        static void Finish(Item item, int price, string info)
        {
            // the store price comes from the balance config ([Store Prices]; 0 = not sold)
            string key = item.name.StartsWith("LMC_") ? item.name.Substring(4) : item.name;
            price = Balance.PriceOf(key, item.itemName, price);
            item.creditsWorth = price > 0 ? Price(price) : 0;
            if (price > 0)
            {
                Balance.ShopItems[key] = item;
                Balance.ShopPrices[key] = Price(price);
                var node = ScriptableObject.CreateInstance<TerminalNode>();
                node.name = item.name + "_info";
                node.clearPreviousText = true;
                node.displayText = info + "\n\n";
                Items.RegisterShopItem(item, null, null, node, Price(price));
            }
            else Items.RegisterItem(item);
        }

        static void BuildBlockModel(GameObject model, BlockDef b)
        {
            var mf = model.GetComponent<MeshFilter>();
            var mr = model.GetComponent<MeshRenderer>();
            float s = 0.28f * Plugin.S;
            switch (b.Shape)
            {
                case BlockShape.Torch:
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    mr.sharedMaterials = new[] { Atlas.Cutout, Atlas.CutoutEmissive };
                    model.transform.localScale = Vector3.one * 0.6f;
                    model.transform.localPosition = new Vector3(0, 0.05f, 0);
                    break;
                case BlockShape.Dust:
                    mf.sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_redstone_dust") ? "item_redstone_dust" : "dust_on");
                    mr.sharedMaterial = Atlas.Cutout;
                    model.transform.localScale = Vector3.one * 0.32f;
                    break;
                case BlockShape.Lever:
                case BlockShape.Button:
                case BlockShape.Plate:
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    mr.sharedMaterial = Atlas.Opaque;
                    model.transform.localScale = Vector3.one * 0.5f;
                    break;
                default:
                    mf.sharedMesh = MeshBuilder.For(b, b == Blocks.RedstoneLamp ? (byte)1 : (byte)0, 0);
                    if (MeshBuilder.HasGlow(mf.sharedMesh)) mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive };
                    else mr.sharedMaterial = b.Render == RenderKind.Emissive || b == Blocks.RedstoneLamp ? Atlas.Emissive : Atlas.ForRender(b.Render);
                    model.transform.localScale = Vector3.one * s;
                    break;
            }
        }

        static void BuildSpriteModel(GameObject model, string tile, float scale)
        {
            model.GetComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(tile);
            model.GetComponent<MeshRenderer>().sharedMaterial = Atlas.Cutout;
            model.transform.localScale = Vector3.one * scale;
        }

        // ------------------------------------------------------------------ server spawning
        public static StackItem ServerSpawnStack(Item item, int count, Vector3 pos)
        {
            if (!BlockNet.IsServer || item == null) return null;
            var sor = StartOfRound.Instance;
            bool inShip = BlockWorld.InShip(pos);
            var parent = inShip ? sor.elevatorTransform : sor.propsContainer;
            var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.Euler(0, Random.Range(0, 360f), 0), parent);
            var st = go.GetComponent<StackItem>();
            st.Count = count;
            st.SpawnTime = Time.time;
            if (inShip) { st.isInShipRoom = true; st.isInElevator = true; st.scrapPersistedThroughRounds = true; }
            go.GetComponent<NetworkObject>().Spawn();
            BlockNet.ServerStackCount(st);
            return st;
        }

        public static void ServerSpawnScrap(BlockDef ore, Vector3 pos)
        {
            if (!scrapByOre.TryGetValue(ore, out var item)) return;
            if (item.spawnPrefab.GetComponent<StackItem>() != null) { ServerSpawnStack(item, 1, pos); return; } // (its value follows its count)
            var sor = StartOfRound.Instance;
            bool inShip = BlockWorld.InShip(pos);
            var parent = inShip ? sor.elevatorTransform : (RoundManager.Instance.spawnedScrapContainer != null ? RoundManager.Instance.spawnedScrapContainer : sor.propsContainer);
            var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.identity, parent);
            var g = go.GetComponent<GrabbableObject>();
            int value = Random.Range(item.minValue, item.maxValue + 1); // ranges are final sell values
            g.SetScrapValue(value);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn();
            // (not RoundManager.SyncScrapValuesClientRpc: that would overwrite the level's scrap totals)
            BlockNet.ServerScrapValue(no.NetworkObjectId, value);
        }

        public static GrabbableObject ServerSpawnPlain(Item item, Vector3 pos)
        {
            if (!BlockNet.IsServer || item == null) return null;
            var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.identity, StartOfRound.Instance.propsContainer);
            go.GetComponent<NetworkObject>().Spawn();
            return go.GetComponent<GrabbableObject>();
        }

        /// <summary>Server: create a stack item directly in a player's inventory isn't possible, so drop at their feet.</summary>
        public static void ServerGive(PlayerControllerB p, string key, int count)
        {
            if (!ByKey.TryGetValue(key, out var item)) return;
            var pos = p.transform.position + p.transform.forward * 1.0f + Vector3.up * 0.5f;
            if (item.spawnPrefab.GetComponent<StackItem>() != null) ServerSpawnStack(item, count, pos);
            else
            {
                var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.identity, StartOfRound.Instance.propsContainer);
                go.GetComponent<NetworkObject>().Spawn();
            }
        }
    }
}
