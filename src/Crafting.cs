using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>Recipes, furnace simulation (server) and inventory helpers.</summary>
    public static class Crafting
    {
        public static readonly List<Recipe> Recipes = new List<Recipe>();

        static void S(string cat, string result, int count, string[] rows, params (char, string)[] map) => Recipes.Add(Recipe.Shaped(cat, result, count, rows, map));
        static void L(string cat, string result, int count, params string[] ingredients) => Recipes.Add(Recipe.ShapelessOf(cat, result, count, ingredients));

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
            S("Basics", "furnace", 1, new[] { "###", "# #", "###" }, ('#', C));
            // dark planks work wherever planks do (Minecraft accepts any planks)
            S("Basics", "stick", 4, new[] { "#", "#" }, ('#', "dark_planks"));
            S("Basics", "crafting_table", 1, new[] { "##", "##" }, ('#', "dark_planks"));
            S("Basics", "chest", 1, new[] { "###", "# #", "###" }, ('#', P));
            S("Redstone", "observer", 1, new[] { "###", "RRQ", "###" }, ('#', C), ('R', R), ('Q', I));
            S("Basics", "chest", 1, new[] { "###", "# #", "###" }, ('#', "dark_planks"));
            // tools (pickaxe, shovel, axe) in wood, stone, iron and diamond
            foreach (var (mat, key) in new[] { ("wooden", P), ("wooden", "dark_planks"), ("stone", C), ("iron", I), ("diamond", D), ("diamond", "diamond") })
            {
                string pick = mat == "diamond" ? "pickaxe" : mat + "_pickaxe";
                S("Tools", pick, 1, new[] { "###", " | ", " | " }, ('#', key), ('|', St));
                S("Tools", mat + "_shovel", 1, new[] { "#", "|", "|" }, ('#', key), ('|', St));
                S("Tools", mat + "_axe", 1, new[] { "##", "#|", " |" }, ('#', key), ('|', St));
            }
            L("Tools", "flint_and_steel", 1, I, "gravel");
            // swords
            foreach (var (key, mat) in new[] { ("wooden_sword", P), ("wooden_sword", "dark_planks"), ("stone_sword", C), ("iron_sword", I), ("golden_sword", G), ("diamond_sword", D), ("diamond_sword", "diamond") })
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
            S("Materials", "glowstone", 1, new[] { " r ", "rgr", " r " }, ('r', R), ('g', G));
            // redstone
            S("Redstone", "redstone_block", 1, new[] { "###", "###", "###" }, ('#', R));
            L("Redstone", R, 9, "redstone_block");
            S("Redstone", "lever", 1, new[] { "|", "#" }, ('|', St), ('#', C));
            L("Redstone", "button", 1, "stone");
            S("Redstone", "pressure_plate", 1, new[] { "##" }, ('#', "stone"));
            S("Redstone", "redstone_torch", 1, new[] { "r", "|" }, ('r', R), ('|', St));
            S("Redstone", "piston", 1, new[] { "PPP", "#i#", "#r#" }, ('P', P), ('#', C), ('i', I), ('r', R));
            S("Redstone", "sticky_piston", 1, new[] { "s", "p" }, ('s', "slime"), ('p', "piston"));
            S("Redstone", "redstone_lamp", 1, new[] { " r ", "rgr", " r " }, ('r', R), ('g', "glowstone"));
            S("Redstone", "note_block", 1, new[] { "PPP", "PrP", "PPP" }, ('P', P), ('r', R));
            // armor
            foreach (var ad in Armor.Defs.Values)
                foreach (var mat in ad.Material == "diamond" ? new[] { "diamond", D } : new[] { ad.Ingredient })
                {
                    var shape = ad.Slot == 0 ? new[] { "###", "# #" } : ad.Slot == 1 ? new[] { "# #", "###", "###" } : ad.Slot == 2 ? new[] { "###", "# #", "# #" } : new[] { "# #", "# #" };
                    S("Armor", ad.Key, 1, shape, ('#', mat));
                }
            // food
            S("Food", "golden_apple", 1, new[] { "ggg", "gag", "ggg" }, ('g', G), ('a', "apple"));
        }

        // ------------------------------------------------------------------ smelting
        public static readonly Dictionary<string, string> SmeltResult = new Dictionary<string, string>
        {
            ["scrap_iron_ore"] = "iron_ingot", ["scrap_gold_ore"] = "gold_ingot", ["iron_ore"] = "iron_ingot", ["gold_ore"] = "gold_ingot",
            ["sand"] = "glass", ["cobblestone"] = "stone", ["oak_log"] = "coal",
        };
        public static readonly Dictionary<string, float> FuelSeconds = new Dictionary<string, float>
        {
            ["coal"] = 32f, ["coal_block"] = 288f, ["oak_planks"] = 6f, ["dark_planks"] = 6f, ["oak_log"] = 6f, ["stick"] = 2f, ["crafting_table"] = 6f, ["bookshelf"] = 6f,
        };
        public const float SmeltSeconds = 4f;

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
                return "Furnace (empty) - hold ore/sand + fuel and [E] to load";
            var parts = new List<string>();
            if (f.InCount > 0) parts.Add($"{NameOf(f.In)} x{f.InCount}{(f.Lit ? $" {Mathf.RoundToInt(f.Progress / SmeltSeconds * 100)}%" : "")}");
            if (f.Fuel > 0 || f.Lit) parts.Add($"fuel {(f.FuelKey != null ? NameOf(f.FuelKey) : "")} x{f.Fuel}");
            if (f.OutCount > 0) parts.Add($"-> {NameOf(f.Out)} x{f.OutCount} ([E] empty hand to take)");
            return "Furnace: " + string.Join(", ", parts);
        }

        public static string NameOf(string key)
        {
            if (key == null) return "";
            if (ModItems.ByKey.TryGetValue(key, out var it)) return it.itemName;
            var b = Blocks.Get(key);
            return b != null ? b.Name : key;
        }

        /// <summary>Server: add items to a furnace. Returns how many were accepted.</summary>
        public static int ServerInsert(BlockKey k, string key, int n)
        {
            var world = BlockWorld.Instance;
            if (world == null || world.DefAt(k) != Blocks.Furnace || n <= 0) return 0;
            if (!Furnaces.TryGetValue(k, out var f)) Furnaces[k] = f = new Furnace();
            bool smeltable = SmeltResult.ContainsKey(key);
            bool fuel = FuelSeconds.ContainsKey(key);
            int taken = 0;
            // smeltables go in as input (logs too, unless something else is already smelting); fuels go in the fuel slot
            if (smeltable && (f.InCount == 0 || f.In == key))
            {
                f.In = key;
                taken = Mathf.Min(n, 64 - f.InCount);
                f.InCount += taken;
            }
            else if (fuel && (f.Fuel == 0 || f.FuelKey == key))
            {
                f.FuelKey = key;
                taken = Mathf.Min(n, 64 - f.Fuel);
                f.Fuel += taken;
            }
            if (taken > 0) Sync(k, f);
            return taken;
        }

        public static void ServerTake(ulong client, BlockKey k)
        {
            if (!Furnaces.TryGetValue(k, out var f) || f.OutCount <= 0) return;
            var p = ServerLogic.PlayerFor(client);
            if (p == null || !ModItems.ByKey.TryGetValue(f.Out, out var item)) return;
            // into the player's hands (like crafting results); stays at their feet if the hotbar is full
            Inventory.ServerSpawnFor(client, f.Out, f.OutCount, pickUp: true);
            BlockNet.ServerXp(client, Mathf.Max(1, f.OutCount / 2));
            BlockNet.ServerToast(client, $"Took {f.OutCount} {NameOf(f.Out)}");
            f.OutCount = 0;
            f.Out = null;
            Sync(k, f);
        }

        /// <summary>Furnace was broken: everything inside drops.</summary>
        public static void ServerDropContents(BlockKey k, Vector3 pos)
        {
            if (!Furnaces.TryGetValue(k, out var f)) return;
            void Drop(string key, int n)
            {
                if (key == null || n <= 0 || !ModItems.ByKey.TryGetValue(key, out var item)) return;
                if (item.spawnPrefab.GetComponent<StackItem>() != null) ModItems.ServerSpawnStack(item, n, pos);
                else for (int i = 0; i < n; i++) ModItems.ServerSpawnPlain(item, pos);
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
                if (canOut && f.Burn <= 0f && f.Fuel > 0)
                {
                    f.Fuel--;
                    f.BurnMax = f.Burn = FuelSeconds.TryGetValue(f.FuelKey ?? "", out var fs) ? fs : 4f;
                    if (f.Fuel == 0) f.FuelKey = null;
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
            if (g is StackItem st && !string.IsNullOrEmpty(st.ItemKey)) return st.ItemKey;
            var n = g.itemProperties.name;
            return n != null && n.StartsWith("LMC_") ? n.Substring(4) : null;
        }

        public static int CountOf(GrabbableObject g) => g is StackItem st ? Mathf.Max(0, st.Count) : 1;

        public static int Have(PlayerControllerB p, string key)
        {
            int n = 0;
            foreach (var g in p.ItemSlots) if (g != null && KeyOf(g) == key) n += CountOf(g);
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
                    if (g == null || KeyOf(g) != key) continue;
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
            var p = ServerLogic.PlayerFor(client);
            if (p == null || !ModItems.ByKey.TryGetValue(r.Result, out var item)) return;
            var pos = p.transform.position + Vector3.up * 0.6f;
            int total = r.Count * times;
            if (item.spawnPrefab.GetComponent<StackItem>() != null)
                for (int left = total; left > 0; left -= 64) ModItems.ServerSpawnStack(item, Mathf.Min(64, left), pos);
            else for (int i = 0; i < total; i++) ModItems.ServerSpawnPlain(item, pos);
            BlockNet.ServerSound(pos, "pop", 1.2f, 0.6f);
        }
    }
}
