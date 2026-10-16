using System;
using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>What the anvil needs to know of an item (the game's items, or the tests' own).</summary>
    public interface IAnvilItems
    {
        /// <summary>How many uses it has (0: it doesn't wear).</summary>
        int MaxUses(string key);
        /// <summary>How worn it is (uses so far).</summary>
        int Damage(string key);
        /// <summary>The key with that wear and those enchantments (its other bits, a trim, a crossbow's load, kept).</summary>
        string Make(string key, int damage, int ench);
        /// <summary>Whether it keeps a saved number at all (tools, armor): only those keep enchantments and work.</summary>
        bool HasData(string key);
        /// <summary>Minecraft's repair material: whether `material` mends it.</summary>
        bool RepairsWith(string key, string material);
        /// <summary>What enchantments it can take.</summary>
        EnchTarget Target(string key);
    }

    public class AnvilResult
    {
        public string Key;      // what comes out (its new name not on it yet: Rename)
        public int Count;       // how many (a renamed stack is renamed whole)
        public int Cost;        // levels
        public int RightUsed;   // how many of the right slot it takes
        public string Rename;   // the new name, or null
        public bool ClearName;  // its custom name taken off
        public bool TooExpensive;
    }

    /// <summary>
    /// (#228) Minecraft's anvil (AnvilMenu.createResult), the parts the mod has: mending with its material (a quarter of
    /// its uses back for each, one level each), joining two of the same (their uses together plus 12%, two levels; the
    /// right one's enchantments onto the left: the same level one higher, Minecraft's anvil_cost times the level),
    /// renaming (one level), plus the prior work of both (each use doubles it: 2^n - 1). 40 levels or more is "Too
    /// Expensive!" (a rename alone is held at 39). No enchanted books in the mod, so none of their rules.
    /// </summary>
    public static class AnvilLogic
    {
        public const int TooExpensive = 40;

        public static int AnvilCost(EnchKind k)
        {
            switch (k)
            {
                case EnchKind.Unbreaking: case EnchKind.FeatherFalling: case EnchKind.QuickCharge: return 2;
                case EnchKind.Multishot: return 4;
                default: return 1;
            }
        }

        /// <param name="currentName">its custom name now (null: none)</param>
        /// <param name="defaultName">its own name (what the name box starts with when it has none)</param>
        /// <param name="typed">the name box (null: not touched)</param>
        public static AnvilResult Compute(IAnvilItems items, string left, int leftCount, string right, int rightCount, string currentName, string defaultName, string typed, bool creative)
        {
            if (left == null || leftCount <= 0) return null;
            bool data = items.HasData(left);
            int enchL = data ? Enchants.EnchOf(ItemData.Of(left)) : 0;
            int workL = Enchants.WorkOf(enchL), workR = 0;
            int max = items.MaxUses(left), damage = data ? items.Damage(left) : 0;
            var enchs = Enchants.Unpack(Enchants.Only(enchL)).ToDictionary(e => e.kind, e => e.level);
            int i = 0, t = 0, used = 0;
            int baseCost = Enchants.RepairCost(workL);
            bool hasRight = right != null && rightCount > 0;
            if (hasRight)
            {
                bool dataR = items.HasData(right);
                int enchR = dataR ? Enchants.EnchOf(ItemData.Of(right)) : 0;
                workR = Enchants.WorkOf(enchR);
                baseCost += Enchants.RepairCost(workR);
                if (max > 0 && items.RepairsWith(left, ItemData.TypeOf(right)))
                {
                    int k = Math.Min(damage, max / 4);
                    if (k <= 0) return null;
                    int m = 0;
                    for (; k > 0 && m < rightCount; m++) { damage -= k; i++; k = Math.Min(damage, max / 4); }
                    used = m;
                }
                else
                {
                    if (ItemData.TypeOf(right) != ItemData.TypeOf(left) || !data || (max <= 0 && items.Target(left) == EnchTarget.None)) return null;
                    used = 1;
                    if (max > 0)
                    {
                        int k = max - damage, l = max - items.Damage(right), m = l + max * 12 / 100, n = k + m, o = Math.Max(0, max - n);
                        if (o < damage) { damage = o; i += 2; }
                    }
                    var allowed = Enchants.For(items.Target(left)).ToList();
                    bool any = false, blocked = false;
                    foreach (var (kind, level) in Enchants.Unpack(Enchants.Only(enchR)))
                    {
                        int q = enchs.TryGetValue(kind, out var lq) ? lq : 0;
                        int r = q == level ? level + 1 : Math.Max(level, q);
                        bool ok = creative || allowed.Contains(kind);
                        foreach (var other in enchs.Keys) if (other != kind && Enchants.Exclusive(kind, other)) { ok = false; i++; }
                        if (!ok) { blocked = true; continue; }
                        any = true;
                        r = Math.Min(r, Enchants.MaxLevel(kind));
                        enchs[kind] = r;
                        i += AnvilCost(kind) * r;
                        if (leftCount > 1) i = TooExpensive;
                    }
                    if (blocked && !any) return null;
                }
            }
            var res = new AnvilResult { Count = leftCount, RightUsed = used };
            // the name: Minecraft's box starts with what it's called; changed, it's renamed; emptied, its name comes off
            if (typed != null)
            {
                string want = typed.Trim();
                if (want.Length == 0) { if (currentName != null) { t = 1; i += t; res.ClearName = true; } }
                else if (want != (currentName ?? defaultName)) { t = 1; i += t; res.Rename = want.Length > ItemNames.MaxLength ? want.Substring(0, ItemNames.MaxLength) : want; }
            }
            if (i <= 0) return null;
            int cost = baseCost + i;
            if (t == i && t > 0 && cost >= TooExpensive) cost = TooExpensive - 1;
            res.Cost = cost;
            if (cost >= TooExpensive && !creative) { res.TooExpensive = true; return res; }
            // (the prior work: the more worked of the two, once more, unless it was only renamed)
            int work = Math.Max(workL, hasRight ? workR : 0);
            if (t != i || t == 0) work++;
            if (data)
            {
                int ench = Enchants.WithWork(Enchants.Pack(enchs.Select(kv => (kv.Key, kv.Value))), work);
                res.Key = items.Make(left, damage, ench);
            }
            else res.Key = left;
            if (res.ClearName) res.Key = ItemData.WithName(res.Key, 0);
            return res;
        }
    }
}
