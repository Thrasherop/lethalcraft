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
        public float PoisonSeconds; // (eaten: poisoned that long, Minecraft's spider eye and pufferfish)
        public string Description;
    }

    public static class ModItems
    {
        public static readonly KeyMap ByKey = new KeyMap();

        /// <summary>Items by key; a key carrying a number (a worn tool, ItemData) finds its item too.</summary>
        public class KeyMap : ItemData.TypeMap<Item> { }

        /// <summary>Server: a fresh item spawned for a key: a worn or enchanted item's key gives it its wear and enchantments
        /// back (#56, #46).</summary>
        /// <summary>Server: one of a key's item, whatever it is (a stack of one, or a plain item with its number), named.</summary>
        public static void ServerSpawnPlainKeyedOrStack(string key, Vector3 pos)
        {
            if (!ByKey.TryGetValue(key, out var item)) return;
            if (item.spawnPrefab.GetComponent<StackItem>() != null) ItemNames.ServerApply(ServerSpawnStack(item, 1, pos), key);
            else ServerSpawnPlainKeyed(key, pos);
        }

        public static GrabbableObject ServerSpawnPlainKeyed(string key, Vector3 pos)
        {
            if (!ByKey.TryGetValue(key, out var item)) return null;
            var g = ServerSpawnPlain(item, pos);
            ItemNames.ServerApply(g, key); // (#228: its name)
            int data = ItemData.Of(key);
            if (data > 0 && g is OreScrapItem)
            {
                // (#208) an ore out of a chest or the inventory keeps what it was worth
                g.SetScrapValue(data);
                BlockNet.ServerScrapValue(g.NetworkObjectId, data, counts: false);
                return g;
            }
            if (data > 0 && g != null && (g is ToolItem || g is ArmorItem || g is DiscItem || g is SpawnEggItem || g is BookItem || g is BannerItem))
            {
                g.LoadItemSaveData(data);
                if (g is ToolItem t && t.MaxUses > 0 && t.Used >= t.MaxUses) t.Used = t.MaxUses - 1;
                BlockNet.ServerItemData(g);
            }
            return g;
        }
        static readonly Dictionary<ushort, Item> byBlock = new Dictionary<ushort, Item>();
        public static readonly ItemData.TypeMap<FoodDef> Foods = new ItemData.TypeMap<FoodDef>(); // (#228: by type)
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
                if (b.Shape == BlockShape.PistonHead || b.Shape == BlockShape.Fire || b.DropsScrap || b == Blocks.Farmland || b.Shape == BlockShape.Crop || b.Shape == BlockShape.Portal || b == Blocks.NetherWart || b == Blocks.ShipPlating || b.Shape == BlockShape.Tripwire) continue; // (#160: tripwire is string's)
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
                    pack.itemIcon = Atlas.IconFor(b.Key) ?? pack.itemIcon; // (#125: "tnt_20" has no icon of its own; it showed stone)
                    Finish(pack, 200, $"TNT x{Balance.Tnt20Count}. A crate of TNT: cheaper each than buying them one at a time.\n\n{b.Description}");
                }
            }

            AddFood(new FoodDef { Key = "bread", Name = "Bread", Tile = "food_bread", Hunger = 5, Saturation = 6f, Price = 6, Stack = 8, Description = "Fresh bread. Restores 2.5 hunger." });
            AddFood(new FoodDef { Key = "steak", Name = "Steak", Tile = "food_steak", Hunger = 8, Saturation = 12.8f, Price = 12, Stack = 6, Description = "Cooked steak. The best food money can buy. Restores 4 hunger." });
            AddFood(new FoodDef { Key = "apple", Name = "Apple", Tile = "food_apple", Hunger = 4, Saturation = 2.4f, Price = 3, Stack = 8, Description = "A crisp apple. Restores 2 hunger." });
            AddFood(new FoodDef { Key = "cookie", Name = "Cookie", Tile = "food_cookie", Hunger = 2, Saturation = 0.4f, Price = 2, Stack = 16, Description = "A tasty snack. Restores 1 hunger." });
            AddFood(new FoodDef { Key = "porkchop", Name = "Cooked Porkchop", Tile = "food_porkchop", Hunger = 8, Saturation = 12.8f, Price = 12, Stack = 6, Description = "Juicy porkchop. Restores 4 hunger." });
            // farming (#60): carrots and potatoes are food, and planted on farmland ([Right-click] on it plants, elsewhere eats)
            AddFood(new FoodDef { Key = "carrot", Name = "Carrot", Tile = "food_carrot", Hunger = 3, Saturation = 3.6f, Price = 5, Stack = 8, Description = "A carrot. Restores 1.5 hunger. Plant it on farmland ([Right-click] on farmland) for more." });
            AddFood(new FoodDef { Key = "potato", Name = "Potato", Tile = "food_potato", Hunger = 1, Saturation = 0.6f, Price = 5, Stack = 8, Description = "A raw potato: better baked in a furnace. Plant it on farmland ([Right-click] on farmland) for more." });
            AddFood(new FoodDef { Key = "baked_potato", Name = "Baked Potato", Tile = "food_baked_potato", Hunger = 5, Saturation = 6f, Price = -1, Stack = 8, Description = "A potato baked in a furnace. Restores 2.5 hunger." });
            Plants("carrot", Blocks.CarrotCrop); Plants("potato", Blocks.PotatoCrop);
            Plants("string", Blocks.Tripwire); // (#160: string laid on the ground is a tripwire, Minecraft's)
            AddFood(new FoodDef { Key = "golden_carrot", Name = "Golden Carrot", Tile = "food_golden_carrot", Hunger = 6, Saturation = 14.4f, Price = -1, Stack = 8, Description = "A carrot in gold: very filling. In a brewing stand, it makes an awkward potion into Night Vision." });
            // farm animals (#95): raw meat (the furnace cooks it: a cooked porkchop, a steak) and leather
            AddFood(new FoodDef { Key = "raw_porkchop", Name = "Raw Porkchop", Tile = "food_raw_porkchop", Hunger = 3, Saturation = 1.8f, Price = -1, Stack = 8, Description = "From a pig. Restores 1.5 hunger raw; cook it in a furnace for a lot more." });
            AddFood(new FoodDef { Key = "raw_chicken", Name = "Raw Chicken", Tile = "food_raw_chicken", Hunger = 2, Saturation = 1.2f, Price = -1, Stack = 8, Description = "From a chicken. Restores 1 hunger raw; cook it in a furnace." });
            AddFood(new FoodDef { Key = "cooked_chicken", Name = "Cooked Chicken", Tile = "food_cooked_chicken", Hunger = 6, Saturation = 7.2f, Price = -1, Stack = 8, Description = "Restores 3 hunger." });
            AddFood(new FoodDef { Key = "raw_mutton", Name = "Raw Mutton", Tile = "food_raw_mutton", Hunger = 2, Saturation = 1.2f, Price = -1, Stack = 8, Description = "From a sheep. Restores 1 hunger raw; cook it in a furnace." });
            AddFood(new FoodDef { Key = "cooked_mutton", Name = "Cooked Mutton", Tile = "food_cooked_mutton", Hunger = 6, Saturation = 9.6f, Price = -1, Stack = 8, Description = "Restores 3 hunger." });
            AddFood(new FoodDef { Key = "raw_rabbit", Name = "Raw Rabbit", Tile = "food_raw_rabbit", Hunger = 3, Saturation = 1.8f, Price = -1, Stack = 8, Description = "From a rabbit. Restores 1.5 hunger raw; cook it in a furnace." });
            AddFood(new FoodDef { Key = "cooked_rabbit", Name = "Cooked Rabbit", Tile = "food_cooked_rabbit", Hunger = 5, Saturation = 6f, Price = -1, Stack = 8, Description = "Restores 2.5 hunger." });
            AddFood(new FoodDef { Key = "raw_beef", Name = "Raw Beef", Tile = "food_raw_beef", Hunger = 3, Saturation = 1.8f, Price = -1, Stack = 8, Description = "From a cow. Restores 1.5 hunger raw; cook it in a furnace into a steak." });
            // fishing: what bites (the furnace cooks cod and salmon; cats are tamed with raw fish)
            AddFood(new FoodDef { Key = "raw_cod", Name = "Raw Cod", Tile = "food_raw_cod", Hunger = 2, Saturation = 0.4f, Price = -1, Stack = 8, Description = "A fish. Cook it in a furnace, or tame a cat with it." });
            AddFood(new FoodDef { Key = "raw_salmon", Name = "Raw Salmon", Tile = "food_raw_salmon", Hunger = 2, Saturation = 0.4f, Price = -1, Stack = 8, Description = "A fish. Cook it in a furnace, or tame a cat with it." });
            AddFood(new FoodDef { Key = "cooked_cod", Name = "Cooked Cod", Tile = "food_cooked_cod", Hunger = 5, Saturation = 6f, Price = -1, Stack = 8, Description = "Cooked fish. Restores 2.5 hunger." });
            AddFood(new FoodDef { Key = "cooked_salmon", Name = "Cooked Salmon", Tile = "food_cooked_salmon", Hunger = 6, Saturation = 9.6f, Price = -1, Stack = 8, Description = "Cooked fish. Restores 3 hunger." });
            AddFood(new FoodDef { Key = "pufferfish", Name = "Pufferfish", Tile = "food_pufferfish", Hunger = 1, Saturation = 0.2f, Price = -1, Stack = 8, PoisonSeconds = 60f, Description = "Better not eat it: a minute of poison." });
            AddFood(new FoodDef { Key = "spider_eye", Name = "Spider Eye", Tile = "food_spider_eye", Hunger = 2, Saturation = 3.2f, Price = -1, Stack = 16, PoisonSeconds = 5f, Description = "From spiders and witches. Poisons you if eaten; in a brewing stand it makes an awkward potion into a potion of Poison." });
            AddFood(new FoodDef { Key = "tropical_fish", Name = "Tropical Fish", Tile = "food_tropical_fish", Hunger = 1, Saturation = 0.2f, Price = -1, Stack = 8, Description = "A pretty fish. Tames a cat too." });
            AddFood(new FoodDef { Key = "rotten_flesh", Name = "Rotten Flesh", Tile = "food_rotten_flesh", Hunger = 4, Saturation = 0.8f, Price = -1, Stack = 8, Description = "Rotten flesh, from zombies. Restores 2 hunger, but it barely fills you." });
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
                ("wooden_hoe", "Wooden Hoe", ToolKind.Hoe, 1, 2f, -1, "item_wooden_hoe"),
                // golden tools (#139: the piglin brute's axe; Minecraft's: wood's harvest level, the fastest of all, 32 uses)
                ("golden_pickaxe", "Golden Pickaxe", ToolKind.Pickaxe, 1, 12f, -1, "item_golden_pickaxe"),
                ("golden_shovel", "Golden Shovel", ToolKind.Shovel, 1, 12f, -1, "item_golden_shovel"),
                ("golden_axe", "Golden Axe", ToolKind.Axe, 1, 12f, -1, "item_golden_axe"),
                ("golden_hoe", "Golden Hoe", ToolKind.Hoe, 1, 12f, -1, "item_golden_hoe"),
                ("shears", "Shears", ToolKind.Shears, 2, 1f, -1, "item_shears"),
                ("stone_hoe", "Stone Hoe", ToolKind.Hoe, 2, 4f, -1, "item_stone_hoe"),
                ("iron_hoe", "Iron Hoe", ToolKind.Hoe, 3, 6f, -1, "item_iron_hoe"),
                ("diamond_hoe", "Diamond Hoe", ToolKind.Hoe, 4, 8f, -1, "item_diamond_hoe"),
                ("netherite_pickaxe", "Netherite Pickaxe", ToolKind.Pickaxe, 5, 9f, -1, "item_netherite_pickaxe"),
                ("netherite_shovel", "Netherite Shovel", ToolKind.Shovel, 5, 9f, -1, "item_netherite_shovel"),
                ("netherite_axe", "Netherite Axe", ToolKind.Axe, 5, 9f, -1, "item_netherite_axe"),
                ("netherite_hoe", "Netherite Hoe", ToolKind.Hoe, 5, 9f, -1, "item_netherite_hoe"),
            };
            foreach (var td in toolDefs)
            {
                var item = MakeItem(td.key, td.name, td.price, 1);
                float toolDamage = Balance.DamageOf(td.key, td.name, td.kind, td.tier);
                item.isDefensiveWeapon = true;
                item.isConductiveMetal = td.tier == 3; // iron (diamond isn't metal)
                item.weight = td.tier <= 2 ? 1.04f : 1.06f;
                item.holdButtonUse = true;
                item.toolTips = new[] { td.kind == ToolKind.Shears ? "Carve : [Right-click]" : td.kind == ToolKind.Pickaxe ? "Mine / swing : [LMB]" : td.kind == ToolKind.Shovel ? "Dig / swing : [LMB]" : td.kind == ToolKind.Hoe ? "Till : [Right-click]" : "Chop wood / swing : [LMB]" };
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
                string what = td.kind == ToolKind.Shears ? "nothing faster, but carve a pumpkin ([Right-click] on one) into a carved pumpkin and four seeds" : td.kind == ToolKind.Pickaxe ? "stone, ores and metal blocks" : td.kind == ToolKind.Shovel ? "dirt, grass, sand, gravel and snow" : td.kind == ToolKind.Hoe ? "nothing faster, but [Right-click] on dirt or grass makes farmland, for seeds" : "wood, planks and wooden blocks";
                string needs = td.kind == ToolKind.Pickaxe ? (td.tier == 1 ? " Can harvest stone, cobblestone and coal ore." : td.tier == 2 ? " Can harvest iron ore too." : td.tier == 3 ? " Can harvest gold, diamond and emerald ore." : " Can harvest everything, including obsidian.") : "";
                Finish(item, td.price, $"{td.name}. Breaks {what} much faster.{needs}\n\nHold [Left-click] on a block to break it. Swing at monsters to hit them.\n\nBetter tools are crafted at a Crafting Table.");
            }

            // swords: crafted only (never sold). Minecraft's damage over 5 (the shovel hits for 1), a swing every 0.7 s:
            // a wooden one about matches the shovel, a diamond one is a good deal deadlier
            foreach (var (key, name, tier, force, metal) in new[]
            {
                ("wooden_sword", "Wooden Sword", 1, 0.8f, false), ("golden_sword", "Golden Sword", 1, 0.8f, true),
                ("stone_sword", "Stone Sword", 2, 1.0f, false), ("iron_sword", "Iron Sword", 3, 1.2f, true), ("diamond_sword", "Diamond Sword", 4, 1.4f, false), ("netherite_sword", "Netherite Sword", 5, 1.6f, false),
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

            // the crossbow (#223): a tool's number holds its wear, enchantments and the arrow it's loaded with (CrossbowData).
            // Always there (pillagers hold one, saves may have one); [Bows] PlayerBows off, no recipe, no drops, and it won't load
            {
                var item = MakeItem(Crossbow.Key, "Crossbow", -1, 1);
                item.isDefensiveWeapon = true;
                item.weight = 1.05f;
                item.holdButtonUse = true;
                item.toolTips = new[] { "Load : hold [Right-click]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.itemIcon = Atlas.IconFor("item_crossbow_standby") ?? item.itemIcon;
                var prefab = MakePrefab(item, out var model);
                var t = prefab.AddComponent<CrossbowItem>();
                t.Kind = ToolKind.None; t.Tier = 1; t.Speed = 1f; t.ItemKey = Crossbow.Key; t.AttackForce = 0f;
                Setup(t, item, model);
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_crossbow_standby") ? "item_crossbow_standby" : "item_bow", 0.55f);
                ByKey[Crossbow.Key] = item;
                Finish(item, -1, "A crossbow. Hold [Right-click] to load an arrow from your inventory (a second and a quarter), then [Right-click] again to shoot it: it stays loaded until you do. Its arrows fly a little faster than a bow's and hit as hard. Enchant it with Quick Charge, Multishot or Piercing. Crafted from three sticks, an iron ingot, two string and a tripwire hook; pillagers carry them.");
            }

            // crafting resources (stackable, not sold: you get them by mining, smelting and crafting)
            AddResource("stick", "Stick", "item_stick");
            AddResource("iron_nugget", "Iron Nugget", "item_iron_nugget"); // (#223: chains, for hanging signs)
            // (#225) Minecraft's 16 dyes
            // (#225) the store's dye pack: two belt bags of the sixteen dyes (DyePack opens it where it lands)
            AddSold(DyePack.Key, "Dye Pack", "item_red_dye", 1, 1, 150, false, "Two belt bags: the sixteen dyes, sixteen of each (eight colours a bag). It opens once it's on the ground.");
            ByKey[DyePack.Key].spawnPrefab.AddComponent<DyePack>();
            // (#225) the banner pattern items
            foreach (var pk in LoomUI.PatternItems.Keys)
            {
                string pname = string.Join(" ", pk.Replace("_banner_pattern", "").Split('_').Select(w => char.ToUpper(w[0]) + w.Substring(1))) + " Banner Pattern";
                AddResource(pk, pname, "item_" + pk, 1);
            }
            foreach (var c in Dyes.Colors) AddResource(Dyes.Key(c), string.Join(" ", c.Split('_').Select(w => char.ToUpper(w[0]) + w.Substring(1))) + " Dye", "item_" + Dyes.Key(c));
            // (#223) boats and chest boats of every wood ([Right-click] on water)
            foreach (var bw in Blocks.Woods)
                foreach (bool chest in new[] { false, true })
                {
                    string bk = BoatAI.ItemKey(bw, chest);
                    AddResource(bk, bw.Name + (chest ? " Boat with Chest" : " Boat"), "item_" + bk, 1);
                    ByKey[bk].itemIcon = Atlas.IconFor("item_" + bk) ?? ByKey[bk].itemIcon;
                }
            AddResource("coal", "Coal", "item_coal");
            // farming (#60): wheat and paper; seeds plant the wheat crop (a block item of their own)
            AddResource("wheat", "Wheat", "item_wheat");
            AddResource("leather", "Leather", "item_leather"); // (#95: from cows; a book is paper and leather)
            AddResource("saddle", "Saddle", "item_saddle"); // (#95: on a tamed horse, to ride it where you like)
            AddResource(Fishing.RodKey, "Fishing Rod", "item_fishing_rod", 1); // (fishing: the use button casts and reels)
            AddResource("paper", "Paper", "item_paper");
            AddResource("book", "Book", "item_book");
            AddResource("sugar", "Sugar", "item_sugar");
            // the Nether (#66)
            AddResource("quartz", "Nether Quartz", "item_quartz");
            AddResource("nether_brick", "Nether Brick", "item_nether_brick");
            AddResource("raw_copper", "Raw Copper", "item_raw_copper"); // (#160: copper ore)
            AddResource("minecart", "Minecart", "item_minecart", 1); // (#160: [Right-click] on a rail)
            AddResource("chest_minecart", "Minecart with Chest", "item_chest_minecart", 1);
            AddResource("hopper_minecart", "Minecart with Hopper", "item_hopper_minecart", 1);
            AddResource("tnt_minecart", "Minecart with TNT", "item_tnt_minecart", 1);
            AddResource("furnace_minecart", "Minecart with Furnace", "item_furnace_minecart", 1);
            AddResource("copper_ingot", "Copper Ingot", "item_copper_ingot");
            AddResource("amethyst_shard", "Amethyst Shard", "item_amethyst_shard"); // (#229: amethyst clusters, in geodes deep underground)
            AddResource("spyglass", "Spyglass", "item_spyglass", 1); // (#229: hold the use button to look through it, Spyglass)
            AddResource(Smithing.TemplateKey, "Netherite Upgrade", "item_netherite_upgrade_smithing_template"); // (#159: bastion loot; at a smithing table) // (#154: smelted netherrack; nether bricks, their fence, red nether bricks)
            AddResource("netherite_scrap", "Netherite Scrap", "item_netherite_scrap");
            // brewing (#88)
            AddResource("glass_bottle", "Glass Bottle", "item_glass_bottle");
            AddResource("blaze_powder", "Blaze Powder", "item_blaze_powder");
            AddResource("magma_cream", "Magma Cream", "item_magma_cream");
            AddResource("gold_nugget", "Gold Nugget", "item_gold_nugget");
            // the skeleton's and the spider's drops (#59)
            AddResource("bone", "Bone", "item_bone");
            AddResource("bone_meal", "Bone Meal", "item_bone_meal");
            AddResource("string", "String", "item_string");
            // bows (the owner's): flint from gravel, feathers from chickens; the bow from the store
            AddResource("flint", "Flint", "item_flint");
            AddResource("resin_brick", "Resin Brick", "item_resin_brick"); // (#230: a smelted resin clump; four make resin bricks; a trim material)
            AddResource("brick", "Brick", "item_brick"); // (#160: the decorated pot's; dirt smelts into it, standing in for clay)
            AddResource("feather", "Feather", "item_feather");
            AddResource("ghast_tear", "Ghast Tear", "item_ghast_tear");
            AddResource("rabbit_hide", "Rabbit Hide", "item_rabbit_hide");
            AddResource("ink_sac", "Ink Sac", "item_ink_sac");
            // (#145) a lead leashes an animal; horse armor goes on a tamed horse ([E] holding it)
            AddResource("lead", "Lead", "item_lead");
            AddResource("honeycomb", "Honeycomb", "item_honeycomb"); // (shears on the Circuit Bees' hive; waxes copper)
            AddResource("snowball", "Snowball", "item_snowball", 16); // (#140: thrown with the use button; four make a snow block)
            AddResource("phantom_membrane", "Phantom Membrane", "item_phantom_membrane"); // (#141: mends an elytra; Slow Falling)
            AddSold(NameTags.Key, "Name Tag", "item_name_tag", 64, 1, 60, false, "A name tag. Name it at an anvil, then [E] on a mob: it keeps that name, and a named mob stays with the ship."); // (#228)
            AddResource("leather_horse_armor", "Leather Horse Armor", "item_leather_horse_armor", 1);
            AddResource("iron_horse_armor", "Iron Horse Armor", "item_iron_horse_armor", 1);
            AddResource("golden_horse_armor", "Golden Horse Armor", "item_golden_horse_armor", 1);
            AddResource("diamond_horse_armor", "Diamond Horse Armor", "item_diamond_horse_armor", 1); // (#150: a squid's; with white wool, black wool) // (#149: four make leather)
            AddResource("rabbit_foot", "Rabbit's Foot", "item_rabbit_foot"); // (#149: a potion of Leaping)
            // tipped arrows (#136: strays and bogged drop them; a bow shoots them: Slowness, Poison on whoever they hit)
            AddResource("arrow_slowness", "Arrow of Slowness", "item_arrow_slowness");
            AddResource("arrow_poison", "Arrow of Poison", "item_arrow_poison"); // (#139: a ghast's; brewed into Regeneration)
            // (#226: sold on every machine, so the store's lists match; [Bows] PlayerBows off, the host's, takes it out: Bow.ApplyHost)
            AddSold(Bow.Key, "Bow", "item_bow", 1, 1, 200, false, "A bow. Hold [Right-click] to draw it (a second for a full draw), let go to shoot an arrow from your inventory. A hit does what a shovel hit does. Arrows: craft them from flint (dig gravel), a stick and a feather (chickens).");
            // (#188: held as the tools are, the size of a sword: Minecraft draws a bow in hand with the same display as a
            // sword's; it was a resource's size and turned on its side, small and seen edge-on)
            ByKey[Bow.Key].positionOffset = new Vector3(0f, 0.1f, 0f);
            ByKey[Bow.Key].rotationOffset = new Vector3(0f, 0f, -10f);
            ByKey[Bow.Key].spawnPrefab.GetComponentInChildren<MeshFilter>().transform.localScale = Vector3.one * 0.55f;
            // (the owner: far too small in hand. A stack item is drawn at 0.6 of its size in hand: a bow at a third of a
            // sword's; Minecraft's are the same size there)
            var bowStack = ByKey[Bow.Key].spawnPrefab.GetComponent<StackItem>();
            if (bowStack != null) bowStack.HeldScale = 1f;
            AddSold(Bow.ArrowKey, "Arrow", "item_arrow", 64, Plugin.ArrowsInStore.Value ? 8 : 1, Plugin.ArrowsInStore.Value ? Balance.PriceOf(Bow.ArrowKey, "Arrow (x8)", 850) : 0, true,
                "Arrows x8, for a bow. Or craft them: flint, a stick and a feather make four."); 
            foreach (var pd in Potions.Defs.Values) AddPotion(pd);
            // (#223) pillager outposts' loot: the bottle o' enchanting, Minecraft's four regular goat horns, the sentry trim
            // template (armor trims themselves: #224)
            AddResource(ExperienceBottle.Key, "Bottle o' Enchanting", "item_experience_bottle");
            foreach (var gi in GoatHorn.Instruments)
            {
                string gk = GoatHorn.KeyPrefix + gi;
                AddResource(gk, "Goat Horn (" + char.ToUpper(gi[0]) + gi.Substring(1) + ")", "item_goat_horn", 1);
                ByKey[gk].itemIcon = Atlas.IconFor("item_goat_horn") ?? ByKey[gk].itemIcon;
            }
            AddResource("sentry_armor_trim_smithing_template", "Sentry Armor Trim Smithing Template", "item_sentry_armor_trim_smithing_template");
            // (#223) the pillager captain's ominous bottle, a level each (its banner is a block: Banners.cs)
            for (int ol = 1; ol <= 5; ol++)
            {
                string ok = OminousBottle.KeyFor(ol);
                AddResource(ok, "Ominous Bottle (Bad Omen " + Enchants.Roman(ol) + ")", "item_ominous_bottle");
                ByKey[ok].itemIcon = Atlas.IconFor("item_ominous_bottle") ?? ByKey[ok].itemIcon;
                ByKey[ok].toolTips = new[] { "Drink : hold [Right-click]", "" };
            }
            {
                var wart = BlockItem(Blocks.NetherWart, "nether_wart", "Nether Wart", 4);
                wart.itemIcon = Atlas.IconFor("item_nether_wart") ?? wart.itemIcon;
                wart.spawnPrefab.GetComponent<StackItem>().ItemKey = "nether_wart";
                ByKey["nether_wart"] = wart;
                Finish(wart, -1, "Nether wart, from the Nether fortress. Plant it on soul sand to grow more; in a brewing stand it makes a water bottle into an awkward potion, the start of every potion.");
            }
            {
                var seeds = BlockItem(Blocks.WheatCrop, "wheat_seeds", "Wheat Seeds", 8);
                seeds.itemIcon = Atlas.IconFor("item_wheat_seeds") ?? seeds.itemIcon;
                seeds.spawnPrefab.GetComponent<StackItem>().ItemKey = "wheat_seeds"; // (its own key: chests, crafting and the inventory find items by it)
                ByKey["wheat_seeds"] = seeds;
                Finish(seeds, 5, "Wheat seeds x8. Plant them on farmland ([Right-click]; a hoe makes farmland from dirt or grass): they grow in about a moon day, and need water within 4 blocks of the farmland (dry farmland turns back to dirt). Ripe wheat drops wheat and more seeds; three wheat make bread.");
            }
            {
                // pumpkin seeds (#130): plant them on farmland like wheat seeds; the stem sets a pumpkin beside it
                var ps = BlockItem(Blocks.PumpkinStem, "pumpkin_seeds", "Pumpkin Seeds", 8);
                ps.itemIcon = Atlas.IconFor("item_pumpkin_seeds") ?? ps.itemIcon;
                ps.spawnPrefab.GetComponent<StackItem>().ItemKey = "pumpkin_seeds";
                ByKey["pumpkin_seeds"] = ps;
                Finish(ps, 8, "Pumpkin seeds x8. Plant them on wet farmland ([Right-click]): the stem grows, then sets a pumpkin on the ground beside it (dirt, grass or farmland, with room). Carve a pumpkin with shears for a carved pumpkin to wear.");
            }
            AddResource("iron_ingot", "Iron Ingot", "item_iron_ingot");
            AddResource("gold_ingot", "Gold Ingot", "item_gold_ingot");
            AddResource("diamond", "Diamond", "item_diamond");
            AddResource("gunpowder", "Gunpowder", "item_gunpowder"); // (creepers drop it, #59; TNT is made of it)
            AddResource(EnchantUI.Lapis, "Lapis Lazuli", "item_lapis_lazuli");
            AddSlimeball();

            // armor: worn in the [I] inventory's armor slots (or right-click it in hand)
            foreach (var ad in Armor.Defs.Values)
            {
                if (Blocks.Get(ad.Key) != null) continue; // (worn but a block: the carved pumpkin's item is its block item)
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
                if (ad.Key == Elytra.Key)
                {
                    item.weight = 1.02f; // (leather: light)
                    Finish(item, 750, "Elytra. Worn in the chestplate slot: jump, then press [Jump] again in the air to open it and glide. Look down to gain speed, up to climb; firework rockets ([Right-click] while gliding) boost you. Walls at speed and dives into the ground hurt. About seven minutes of gliding before it's worn out; mend it with string at a crafting table (each string a quarter).");
                }
                else Items.RegisterItem(item);
            }
            // firework rockets (#61): a boost while gliding; loud
            {
                Resources.Add(Elytra.RocketKey);
                var item = MakeItem(Elytra.RocketKey, "Firework Rocket", -1, 64);
                item.toolTips = new[] { "", "" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.weight = 1.01f;
                var prefab = MakePrefab(item, out var model);
                var st = prefab.AddComponent<StackItem>();
                Setup(st, item, model);
                st.ItemKey = Elytra.RocketKey;
                st.DefaultCount = 32;
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_firework_rocket") ? "item_firework_rocket" : "item_stick", 0.36f);
                ByKey[Elytra.RocketKey] = item;
                Finish(item, 100, "Firework rockets x32. [Right-click] while gliding with an elytra: a boost along where you look, for a second and a half. Loud: monsters hear it. Also crafted from paper and gunpowder (3 a time).");
            }

            // flint and steel
            {
                var item = MakeItem("flint_and_steel", "Flint and Steel", 15, 1);
                item.weight = 1.02f;
                item.toolTips = new[] { Plugin.PlaceWithLeftClick.Value ? "Light fire / TNT : [LMB]" : "Ignite : [Right-click]" };
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
            // buckets (#19)
            foreach (var (key, name, full) in new[] { ("bucket", "Bucket", false), ("water_bucket", "Water Bucket", true), ("lava_bucket", "Lava Bucket", true) })
            {
                var item = MakeItem(key, name, full ? 30 : 15, full ? 1 : 16);
                item.weight = full ? 1.08f : 1.02f;
                item.toolTips = new[] { full ? "Pour : [Right-click]" : "Fill : [Right-click]" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                var prefab = MakePrefab(item, out var model);
                var b = prefab.AddComponent<BucketItem>();
                Setup(b, item, model);
                b.ItemKey = key;
                b.DefaultCount = 1;
                BuildSpriteModel(model, "item_" + key, 0.4f);
                ByKey[key] = item;
                if (key == "lava_bucket") Finish(item, -1, "A bucket of lava. Right-click to pour a lava source out: it flows (slowly, three blocks) and burns. Lava pockets lie deep underground.");
                else Finish(item, full ? 30 : 15, full ? "A bucket of water. Right-click to pour it out: a water source, flowing like Minecraft's. Pour it under you as you fall and you land in water."
                                                  : "An empty bucket (they stack to 16). Right-click a water or lava source (or the moon's own water) to fill one.");
            }
            // the Totem of Undying (#53): in your hotbar, it saves you from dying once
            {
                var item = MakeItem(Totem.Key, "Totem of Undying", 200, 1);
                item.weight = 1f + 15f / 105f; // (15 lb)
                item.toolTips = new[] { "Saves you from dying once" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = new Vector3(0f, 0f, -10f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.disallowUtilitySlot = true;
                var prefab = MakePrefab(item, out var model);
                Setup(prefab.AddComponent<TotemItem>(), item, model);
                BuildSpriteModel(model, "item_totem_of_undying", 0.45f);
                ByKey[Totem.Key] = item;
                Finish(item, 150, Plugin.TotemToShip.Value ? "Keep it in your hotbar: when you would die, it's used up instead. You're back to full health, safe in the ship. 15 lb." : "Keep it in your hotbar: when you would die, it's used up instead. Full health, and two seconds when nothing can hurt you: get out of there. 15 lb.");
            }
            // ender pearls: throwable teleport, found inside and/or sold in the store (configurable)
            if (Plugin.PearlsEnabled.Value) AddPearl();
            AddDiscs();
            AddWitherSkull();
            AddBlazeRod();
            AddNetheriteIngot();
            AddSpawnEgg();
            AddBooks(); // (#160)
            AddPatternedBanner(); // (#225)
            // ore scrap
            foreach (var b in Blocks.All.Where(x => x.DropsScrap))
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

        /// <summary>A resource the store may sell: `count` a purchase, at `price` (through [Store Prices] unless fixed; 0 = not sold).</summary>
        static void AddSold(string key, string name, string tile, int maxStack, int count, int price, bool fixedPrice, string info)
        {
            Resources.Add(key);
            var item = MakeItem(key, name, -1, maxStack);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.rotationOffset = new Vector3(0f, 0f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = key;
            st.DefaultCount = count;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : "item_stick", 0.36f);
            ByKey[key] = item;
            if (price > 0) Finish(item, price, info, fixedPrice); else Items.RegisterItem(item);
        }

        static void AddResource(string key, string name, string tile, int maxStack = 64)
        {
            Resources.Add(key);
            var item = MakeItem(key, name, -1, maxStack);
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

        /// <summary>The book and quill and the written book (#160): one each, their text a number (Books).</summary>
        /// <summary>(#225) A banner with patterns: one item, its design its saved number (BannerDesigns).</summary>
        static void AddPatternedBanner()
        {
            var item = MakeItem(BannerDesigns.ItemKey, "Banner", -1, 1);
            item.toolTips = new[] { "Place : [Right-click]", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.01f;
            var prefab = MakePrefab(item, out var model);
            var bi = prefab.AddComponent<BannerItem>();
            Setup(bi, item, model);
            string tile = Atlas.Tiles.ContainsKey("item_white_banner") ? "item_white_banner" : "item_" + Banners.OminousKey;
            BuildSpriteModel(model, tile, 0.36f);
            item.itemIcon = Atlas.IconFor(tile) ?? item.itemIcon;
            ByKey[BannerDesigns.ItemKey] = item;
            Finish(item, -1, "A banner with patterns, made on a loom. [Right-click] to put it up.");
        }

        static void AddBooks()
        {
            foreach (var (key, name, tip) in new[] { (Books.Writable, "Book and Quill", "Write : [Right-click]"), (Books.Written, "Written Book", "Read : [Right-click]") })
            {
                var item = MakeItem(key, name, -1, 1);
                item.toolTips = new[] { tip, "" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.weight = 1.01f;
                var prefab = MakePrefab(item, out var model);
                var bk = prefab.AddComponent<BookItem>();
                Setup(bk, item, model);
                string tile = Atlas.Tiles.ContainsKey("item_" + key) ? "item_" + key : "item_book";
                BuildSpriteModel(model, tile, 0.36f);
                item.itemIcon = Atlas.IconFor(tile) ?? item.itemIcon;
                ByKey[key] = item;
                Items.RegisterItem(item);
                Finish(item, -1, key == Books.Writable ? "A book and quill: [Right-click] to write in it (50 pages); sign it to make a written book. On a lectern anyone can read it." : "A written book: [Right-click] to read it, or put it on a lectern.");
            }
        }

        /// <summary>The spawn egg (#85): one item; which monster it hatches is its saved number. From the creative menu.</summary>
        static void AddSpawnEgg()
        {
            var item = MakeItem(SpawnEggs.Key, "Spawn Egg", -1, 1);
            item.toolTips = new[] { "Hatch : [Right-click]", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.0f;
            var prefab = MakePrefab(item, out var model);
            var egg = prefab.AddComponent<SpawnEggItem>();
            Setup(egg, item, model);
            string tile = Atlas.Tiles.ContainsKey("egg_creeper") ? "egg_creeper" : "item_slime_ball";
            BuildSpriteModel(model, tile, 0.38f);
            item.itemIcon = Atlas.IconFor(tile) ?? item.itemIcon;
            ByKey[SpawnEggs.Key] = item;
            Items.RegisterItem(item);
            Finish(item, -1, "A spawn egg. Right-click the ground to hatch its monster there (in creative it isn't used up).");
        }

        /// <summary>Music discs (#31): never sold or crafted; found inside facilities as scrap, played in a jukebox.</summary>
        /// <summary>The wither skeleton skull (#59): dropped (rarely) by wither skeletons, a trophy that sells.</summary>
        static void AddWitherSkull()
        {
            var item = MakeItem(WitherSkull.Key, "Wither Skeleton Skull", -1, 1);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.12f, 0f);
            item.restingRotation = new Vector3(0f, 0f, 0f);
            item.verticalOffset = 0.16f;
            item.weight = 1.06f;
            item.isScrap = true; item.minValue = 90; item.maxValue = 140;
            if (Atlas.Tiles.ContainsKey("item_wither_skull")) item.itemIcon = Atlas.IconFor("item_wither_skull") ?? item.itemIcon;
            var prefab = MakePrefab(item, out var model);
            var sk = prefab.AddComponent<WitherSkull>();
            Setup(sk, item, model);
            WitherSkull.BuildModel(model);
            var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
            if (scan != null) scan.nodeType = 2;
            ByKey[WitherSkull.Key] = item;
            Items.RegisterItem(item);
            Finish(item, -1, "A wither skeleton skull. Wither skeletons drop one very rarely. The Company pays well for it.");
        }

        /// <summary>The netherite ingot (#66): four netherite scrap and four gold, crafted; it sells for $500 (scrap, fixed).</summary>
        static void AddNetheriteIngot()
        {
            var item = MakeItem("netherite_ingot", "Netherite Ingot", -1, 1);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.08f;
            item.isScrap = true; item.minValue = 500; item.maxValue = 500;
            var prefab = MakePrefab(item, out var model);
            var ni = prefab.AddComponent<BlazeRod>(); // (a plain scrap item)
            Setup(ni, item, model);
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_netherite_ingot") ? "item_netherite_ingot" : "item_iron_ingot", 0.36f);
            var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
            if (scan != null) scan.nodeType = 2;
            ByKey["netherite_ingot"] = item;
            Items.RegisterItem(item);
            Finish(item, -1, "A netherite ingot: four netherite scrap (smelted ancient debris) and four gold ingots. The Company pays $500 for it; it also makes diamond tools into netherite ones.");
        }

        /// <summary>A potion (#88): one to a stack, drunk by holding [Right-click]; the bottle comes back.</summary>
        static void AddPotion(Potions.Def d)
        {
            var item = MakeItem(d.Key, d.Name, -1, 1);
            item.toolTips = new[] { "Drink : hold [Right-click]", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.02f;
            var prefab = MakePrefab(item, out var model);
            var st = prefab.AddComponent<StackItem>();
            Setup(st, item, model);
            st.ItemKey = d.Key;
            st.DefaultCount = 1;
            string tile = "item_" + d.Key;
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : "item_glass_bottle", 0.36f);
            ByKey[d.Key] = item;
            string what = d.Effect == Potions.Effect.None ? (d.Key == "potion_water" ? "A bottle of water: brew nether wart into it for an awkward potion." : "An awkward potion: no effect yet. Brew sugar, blaze powder, magma cream, a golden carrot, a spider eye or a ghast tear into it.")
                : $"{d.Name}: {(d.Effect == Potions.Effect.Swiftness ? "faster on your feet" : d.Effect == Potions.Effect.Strength ? "Minecraft weapons hit harder" : d.Effect == Potions.Effect.FireResistance ? "fire, lava and fireballs don't burn you" : d.Effect == Potions.Effect.Poison ? "poisons you (never the last of your health)" : d.Effect == Potions.Effect.Regeneration ? "heals you over time" : d.Effect == Potions.Effect.JumpBoost ? "you jump higher" : d.Effect == Potions.Effect.SlowFalling ? "you drift down slowly, no falls hurt" : "see in the dark")} for {(d.Seconds < 60f ? d.Seconds.ToString("0") + " seconds" : (d.Seconds / 60f).ToString("0.#") + " minutes")}.";
            Finish(item, -1, what + " Hold [Right-click] to drink.");
        }

        static void AddBlazeRod()
        {
            var item = MakeItem(BlazeRod.Key, "Blaze Rod", -1, 1);
            item.toolTips = new[] { "", "" };
            item.positionOffset = new Vector3(0f, 0.1f, 0f);
            item.restingRotation = new Vector3(90f, 0f, 0f);
            item.verticalOffset = 0.03f;
            item.weight = 1.02f;
            item.isScrap = true; item.minValue = 30; item.maxValue = 60;
            var prefab = MakePrefab(item, out var model);
            var br = prefab.AddComponent<BlazeRod>();
            Setup(br, item, model);
            BuildSpriteModel(model, Atlas.Tiles.ContainsKey("item_blaze_rod") ? "item_blaze_rod" : "item_stick", 0.4f);
            var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
            if (scan != null) scan.nodeType = 2;
            ByKey[BlazeRod.Key] = item;
            Items.RegisterItem(item);
            Finish(item, -1, "A blaze rod, from the Nether fortress's blazes. The Company pays well for it.");
        }

        /// <summary>Server: one of our scrap items by key, with a value from its range (synced to every client).</summary>
        public static GrabbableObject ServerSpawnScrapItem(string key, Vector3 pos)
        {
            if (!BlockNet.IsServer || !ByKey.TryGetValue(key, out var item)) return null;
            var parent = RoundManager.Instance != null && RoundManager.Instance.spawnedScrapContainer != null ? RoundManager.Instance.spawnedScrapContainer : StartOfRound.Instance.propsContainer;
            var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.identity, parent);
            var g = go.GetComponent<GrabbableObject>();
            int value = Random.Range(item.minValue, item.maxValue + 1);
            g.SetScrapValue(value);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn();
            BlockNet.ServerScrapValue(no.NetworkObjectId, value);
            return g;
        }

        static void AddDiscs()
        {
            bool inside = Plugin.DiscsSpawnInside.Value;
            foreach (var disc in Jukebox.Discs)
            {
                var item = MakeItem(disc.Key, "Music Disc (" + disc.Track.Replace("_", " ") + ")", -1, 1);
                item.toolTips = new[] { "Play : [E] on a jukebox", "" };
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                item.weight = 1.02f;
                item.isScrap = true; item.minValue = 25; item.maxValue = 45;
                var prefab = MakePrefab(item, out var model);
                var di = prefab.AddComponent<DiscItem>();
                di.ItemKey = disc.Key;
                Setup(di, item, model);
                string tile = "item_" + disc.Key;
                BuildSpriteModel(model, Atlas.Tiles.ContainsKey(tile) ? tile : "item_coal", 0.38f);
                var scan = prefab.GetComponentInChildren<ScanNodeProperties>();
                if (scan != null) { scan.nodeType = 2; scan.subText = Jukebox.Title(disc); }
                ByKey[disc.Key] = item;
                if (inside) Items.RegisterScrap(item, Plugin.DiscSpawnRarity.Value, Levels.LevelTypes.All);
                else Items.RegisterItem(item);
            }
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

        /// <summary>A food that's also planted (carrots, potatoes): its stack holds the crop's block too.</summary>
        static void Plants(string key, BlockDef crop)
        {
            if (ByKey.TryGetValue(key, out var it) && it.spawnPrefab.GetComponent<StackItem>() is StackItem st) st.BlockType = crop.Id;
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
            var flat = Atlas.FlatItemTile(b);
            if (flat != null && Atlas.Tiles.ContainsKey(flat))
            {
                // (Minecraft's item is flat for these: held and on the ground the sprite, as its other flat items, #195)
                BuildSpriteModel(model, flat, 0.36f);
                item.positionOffset = new Vector3(0f, 0.1f, 0f);
                item.rotationOffset = Vector3.zero;
                item.restingRotation = new Vector3(90f, 0f, 0f);
                item.verticalOffset = 0.03f;
                return item;
            }
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
            Atlas.NoDecals(mr);
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

        static void Finish(Item item, int price, string info, bool fixedPrice = false)
        {
            // the store price comes from the balance config ([Store Prices]; 0 = not sold)
            string key = item.name.StartsWith("LMC_") ? item.name.Substring(4) : item.name;
            if (!fixedPrice) price = Balance.PriceOf(key, item.itemName, price);
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
                case BlockShape.Pane:
                    // in hand / on the ground: a flat pane
                    mf.sharedMesh = MeshBuilder.For(b, 0, 12);
                    mr.sharedMaterial = Atlas.Cutout;
                    model.transform.localScale = Vector3.one * s;
                    break;
                case BlockShape.Door:
                case BlockShape.Ladder:
                case BlockShape.Trapdoor:
                case BlockShape.Repeater:
                case BlockShape.Comparator:
                case BlockShape.Hook:
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    mr.sharedMaterial = Atlas.Cutout;
                    model.transform.localScale = Vector3.one * s;
                    break;
                case BlockShape.Gate:
                case BlockShape.Hopper:
                case BlockShape.Rod:
                case BlockShape.Basin:
                case BlockShape.Pot:
                case BlockShape.Shelf:
                case BlockShape.Lectern:
                case BlockShape.Anvil: // (#228)
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    mr.sharedMaterial = Atlas.Opaque;
                    model.transform.localScale = Vector3.one * s;
                    break;
                case BlockShape.Wall:
                case BlockShape.Fence:
                    // (in hand: a straight piece with its post)
                    mf.sharedMesh = MeshBuilder.For(b, 0, 3 | 16);
                    mr.sharedMaterial = Atlas.Opaque;
                    model.transform.localScale = Vector3.one * s;
                    break;
                case BlockShape.Stairs:
                case BlockShape.Slab:
                case BlockShape.Detector:
                case BlockShape.Table:
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    mr.sharedMaterial = Atlas.Opaque;
                    model.transform.localScale = Vector3.one * s;
                    break;
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
                    // (the redstone lamp off, as Minecraft's item: it had been held lit, #195)
                    mf.sharedMesh = MeshBuilder.For(b, 0, 0);
                    if (MeshBuilder.HasGlow(mf.sharedMesh)) mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive };
                    else mr.sharedMaterial = b.Render == RenderKind.Emissive ? Atlas.Emissive : Atlas.ForRender(b.Render);
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
            // (in the ship: the ship's, like a stack. #109: tools and armor out of a chest blown up in the ship weren't,
            // and unless they lay in the ship's inner room the round's end deleted them)
            var sor = StartOfRound.Instance;
            bool inShip = BlockWorld.InShip(pos);
            var go = Object.Instantiate(item.spawnPrefab, pos, Quaternion.identity, inShip ? sor.elevatorTransform : sor.propsContainer);
            var g = go.GetComponent<GrabbableObject>();
            if (inShip && g != null) { g.isInShipRoom = true; g.isInElevator = true; g.scrapPersistedThroughRounds = true; }
            go.GetComponent<NetworkObject>().Spawn();
            return g;
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
