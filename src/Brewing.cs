using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The brewing stand (#88), Minecraft's: three bottles, an ingredient on top and blaze powder for fuel (one powder
    /// lasts 20 brews). With fuel, an ingredient and at least one bottle it changes, it brews for 20 s, then every
    /// bottle it can change becomes the next potion (Potions.Brews) and one ingredient is used up. Taking the
    /// ingredient out (or the bottles) stops it. The contents live on the server, like a furnace's; the block's state
    /// shows which bottle holders are full.
    /// </summary>
    public static class Brewing
    {
        public const int Bottles = 3, Ingredient = 3, Fuel = 4, Slots = 5;
        public static float BrewSeconds = 20f; // (Minecraft's; dev `brewui time`)
        public const int BrewsPerPowder = 20;

        public class Stand
        {
            public string[] Bottle = new string[Bottles];
            public string IngKey; public int IngCount;
            public int Powder;      // blaze powder waiting in the fuel slot
            public int FuelLeft;    // brews left from the powder already used (the bar)
            public float Progress;  // seconds brewed so far (0: not brewing)
            public string BrewWith; // (server) the ingredient it started with: swapping it starts over
            public bool Brewing => Progress > 0f;
        }

        public static readonly Dictionary<BlockKey, Stand> Stands = new Dictionary<BlockKey, Stand>(); // server truth; mirrored on clients
        static float syncTimer;

        public static void Reset() => Stands.Clear();

        public static bool CanGo(int slot, string key)
        {
            if (key == null) return false;
            if (slot < Bottles) return Potions.IsPotion(key);
            if (slot == Ingredient) return Potions.IsIngredient(key);
            if (slot == Fuel) return key == "blaze_powder";
            return false;
        }

        /// <summary>What the stand would make right now, if it brewed (null: nothing would change).</summary>
        static bool AnyBrews(Stand s) => s.IngCount > 0 && s.Bottle.Any(b => Potions.BrewOf(b, s.IngKey) != null);

        public static string Describe(BlockKey k)
        {
            if (!Stands.TryGetValue(k, out var s) || (s.Bottle.All(b => b == null) && s.IngCount == 0 && s.Powder == 0))
                return "Brewing Stand (empty) - [E] to open";
            var parts = new List<string>();
            int nb = s.Bottle.Count(b => b != null);
            if (nb > 0) parts.Add($"{nb} bottle{(nb > 1 ? "s" : "")}");
            if (s.IngCount > 0) parts.Add($"{Crafting.NameOf(s.IngKey)} x{s.IngCount}");
            if (s.Brewing) parts.Add($"brewing {Mathf.RoundToInt(s.Progress / BrewSeconds * 100)}%");
            return "Brewing Stand: " + string.Join(", ", parts);
        }

        /// <summary>
        /// Server, the brewing screen: items into a slot (0-2 bottles, 3 the ingredient, 4 blaze powder; -1 wherever they
        /// go, a shift-click). Returns what comes back: what didn't fit, or what was there before (a swap).
        /// </summary>
        public static (string back, int n) ServerPut(BlockKey k, int slot, string key, int n, bool swap)
        {
            var world = BlockWorld.Instance;
            if (world == null || world.DefAt(k) != Blocks.BrewingStand || key == null || n <= 0) return (key, n);
            if (!Stands.TryGetValue(k, out var s)) Stands[k] = s = new Stand();
            if (slot < 0)
            {
                // a shift-click: powder to the fuel (or the ingredient, when the fuel's full), ingredients on top, bottles in the empty holders
                if (key == "blaze_powder" && s.Powder < 64) slot = Fuel;
                else if (Potions.IsIngredient(key) && (s.IngCount == 0 || s.IngKey == key)) slot = Ingredient;
                else if (Potions.IsPotion(key))
                {
                    int left = n;
                    for (int i = 0; i < Bottles && left > 0; i++) if (s.Bottle[i] == null) { s.Bottle[i] = key; left--; }
                    if (left != n) Changed(k, s);
                    return (key, left);
                }
                else return (key, n);
            }
            if (!CanGo(slot, key)) return (key, n);
            if (slot < Bottles)
            {
                if (s.Bottle[slot] == null) { s.Bottle[slot] = key; Changed(k, s); return (key, n - 1); }
                if (!swap || n != 1) return (key, n);
                var old = s.Bottle[slot];
                s.Bottle[slot] = key; Changed(k, s);
                return (old, 1);
            }
            if (slot == Ingredient)
            {
                if (s.IngCount == 0 || s.IngKey == key)
                {
                    int t = Mathf.Min(n, 64 - s.IngCount);
                    s.IngKey = key; s.IngCount += t;
                    Changed(k, s);
                    return (key, n - t);
                }
                if (!swap) return (key, n);
                var old = (s.IngKey, s.IngCount);
                s.IngKey = key; s.IngCount = Mathf.Min(n, 64); s.Progress = 0f;
                Changed(k, s);
                return old;
            }
            // the fuel slot (blaze powder only)
            int f = Mathf.Min(n, 64 - s.Powder);
            s.Powder += f;
            Changed(k, s);
            return (key, n - f);
        }

        /// <summary>Server, the brewing screen: items out of a slot.</summary>
        public static (string key, int n) ServerTakeSlot(BlockKey k, int slot, int n)
        {
            if (!Stands.TryGetValue(k, out var s) || n <= 0) return (null, 0);
            string key; int got;
            if (slot >= 0 && slot < Bottles)
            {
                key = s.Bottle[slot]; got = key != null ? 1 : 0; s.Bottle[slot] = null;
            }
            else if (slot == Ingredient) { key = s.IngKey; got = Mathf.Min(n, s.IngCount); s.IngCount -= got; if (s.IngCount == 0) s.IngKey = null; }
            else if (slot == Fuel) { key = "blaze_powder"; got = Mathf.Min(n, s.Powder); s.Powder -= got; }
            else return (null, 0);
            if (got > 0) Changed(k, s);
            return (key, got);
        }

        /// <summary>The stand was broken: everything inside drops.</summary>
        public static void ServerDropContents(BlockKey k, Vector3 pos)
        {
            if (!Stands.TryGetValue(k, out var s)) return;
            void Drop(string key, int n)
            {
                if (key == null || n <= 0 || !ModItems.ByKey.TryGetValue(key, out var item)) return;
                if (item.spawnPrefab.GetComponent<StackItem>() != null) ItemNames.ServerApply(ModItems.ServerSpawnStack(item, n, pos), key); // (#228)
                else for (int i = 0; i < n; i++) ModItems.ServerSpawnPlainKeyed(key, pos);
            }
            foreach (var b in s.Bottle) Drop(b, 1);
            Drop(s.IngKey, s.IngCount); Drop("blaze_powder", s.Powder);
            Stands.Remove(k);
            BlockNet.ServerBrewing(k, null);
        }

        public static void ServerTick(float dt)
        {
            var world = BlockWorld.Instance;
            if (world == null || Stands.Count == 0) return;
            syncTimer -= dt;
            bool syncNow = syncTimer <= 0f;
            if (syncNow) syncTimer = 0.5f;
            foreach (var kv in Stands.ToList())
            {
                var k = kv.Key; var s = kv.Value;
                var bi = world.Get(k);
                if (bi == null || bi.Data.Def != Blocks.BrewingStand) { Stands.Remove(k); continue; }
                bool can = AnyBrews(s) && (!s.Brewing || s.BrewWith == s.IngKey);
                if (!can) { if (s.Brewing) { s.Progress = 0f; Sync(k, s); } continue; }
                if (!s.Brewing)
                {
                    // starting a brew takes one from the fuel bar (a new powder fills it)
                    if (s.FuelLeft <= 0)
                    {
                        if (s.Powder <= 0) continue;
                        s.Powder--; s.FuelLeft = BrewsPerPowder;
                    }
                    s.FuelLeft--;
                    s.Progress = 0.0001f; s.BrewWith = s.IngKey;
                    Sync(k, s);
                }
                s.Progress += dt;
                if (s.Progress >= BrewSeconds)
                {
                    for (int i = 0; i < Bottles; i++)
                    {
                        var r = Potions.BrewOf(s.Bottle[i], s.IngKey);
                        if (r != null) s.Bottle[i] = r;
                    }
                    s.IngCount--; if (s.IngCount == 0) s.IngKey = null;
                    s.Progress = 0f;
                    BlockNet.ServerSound(world.WorldCenter(k), "brew", 1f, 0.6f);
                    Changed(k, s);
                }
                else if (syncNow) Sync(k, s);
            }
        }

        /// <summary>The contents changed: tell everyone, and show the bottles on the block.</summary>
        static void Changed(BlockKey k, Stand s)
        {
            var world = BlockWorld.Instance;
            var bi = world != null ? world.Get(k) : null;
            if (bi != null)
            {
                int bits = 0;
                for (int i = 0; i < Bottles; i++) if (s.Bottle[i] != null) bits |= 1 << i;
                if ((bi.Data.State & 7) != bits)
                {
                    var d = bi.Data; d.State = (byte)((d.State & ~7) | bits);
                    BlockNet.ServerBroadcastOp(Op.State(k, d));
                }
            }
            Sync(k, s);
        }

        static void Sync(BlockKey k, Stand s) => BlockNet.ServerBrewing(k, s);
    }
}
