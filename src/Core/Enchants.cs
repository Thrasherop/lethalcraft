using System;
using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    // (the first eight keep their numbers: items saved before #244 have them in their packed bits)
    public enum EnchKind : byte
    {
        None, Efficiency, Unbreaking, Sharpness, Protection, FeatherFalling, QuickCharge, Multishot, Piercing,
        // (#244) the rest the owner ticked: armor, swords, tools, bow, fishing rod, mending
        FireProtection, BlastProtection, ProjectileProtection, Thorns, Respiration, AquaAffinity, DepthStrider,
        Smite, BaneOfArthropods, Knockback, FireAspect, Looting, SweepingEdge, SilkTouch, Fortune,
        Power, Punch, Flame, Infinity, LuckOfTheSea, Lure, Mending,
    }

    /// <summary>What an item can be enchanted as. (The first six keep their numbers; Tool is a pickaxe or shovel, Armor a
    /// chestplate or leggings, as before #244.)</summary>
    public enum EnchTarget : byte { None, Tool, Sword, Armor, Boots, Crossbow, Helmet, Leggings, Axe, Hoe, Shears, Bow, FishingRod, Elytra, FlintAndSteel, Book }

    /// <summary>
    /// Enchantments (#46, #244), Minecraft 26.3's: their levels, weights and costs (data/minecraft/enchantment), which items
    /// take them at the table and which only at the anvil (tags/item/enchantable), which exclude each other. An item's
    /// saved number (ItemData) holds its uses in the low bits and its enchantments above them: up to three of the first
    /// eight kinds packed as before (6 bits each: a 3-bit code, a 3-bit level; codes 1-5 the first five kinds, 6 Quick
    /// Charge, 7 Multishot at level 1 or Piercing at 2-5), anything more a list kept by number (EnchSets).
    /// </summary>
    public static class Enchants
    {
        public const int UsesBits = 13, UsesMask = (1 << UsesBits) - 1, MaxPerItem = 3;
        /// <summary>(#244, the owner's: Fortune off unless the config turns it on: not offered, does nothing)</summary>
        public static bool FortuneOn;

        public struct Info
        {
            public string Name; public int Max, Weight, Anvil, MinBase, MinPer, MaxBase, MaxPer; public bool Treasure;
        }

        static Info I(string name, int max, int weight, int anvil, int minBase, int minPer, int maxBase, int maxPer, bool treasure = false) =>
            new Info { Name = name, Max = max, Weight = weight, Anvil = anvil, MinBase = minBase, MinPer = minPer, MaxBase = maxBase, MaxPer = maxPer, Treasure = treasure };

        static readonly Dictionary<EnchKind, Info> info = new Dictionary<EnchKind, Info>
        {
            [EnchKind.Efficiency] = I("Efficiency", 5, 10, 1, 1, 10, 51, 10),
            [EnchKind.Unbreaking] = I("Unbreaking", 3, 5, 2, 5, 8, 55, 8),
            [EnchKind.Sharpness] = I("Sharpness", 5, 10, 1, 1, 11, 21, 11),
            [EnchKind.Protection] = I("Protection", 4, 10, 1, 1, 11, 12, 11),
            [EnchKind.FeatherFalling] = I("Feather Falling", 4, 5, 2, 5, 6, 11, 6),
            [EnchKind.QuickCharge] = I("Quick Charge", 3, 5, 2, 12, 20, 50, 0),
            [EnchKind.Multishot] = I("Multishot", 1, 2, 4, 20, 0, 50, 0),
            [EnchKind.Piercing] = I("Piercing", 4, 10, 1, 1, 10, 50, 0),
            [EnchKind.FireProtection] = I("Fire Protection", 4, 5, 2, 10, 8, 18, 8),
            [EnchKind.BlastProtection] = I("Blast Protection", 4, 2, 4, 5, 8, 13, 8),
            [EnchKind.ProjectileProtection] = I("Projectile Protection", 4, 5, 2, 3, 6, 9, 6),
            [EnchKind.Thorns] = I("Thorns", 3, 1, 8, 10, 20, 60, 20),
            [EnchKind.Respiration] = I("Respiration", 3, 2, 4, 10, 10, 40, 10),
            [EnchKind.AquaAffinity] = I("Aqua Affinity", 1, 2, 4, 1, 0, 41, 0),
            [EnchKind.DepthStrider] = I("Depth Strider", 3, 2, 4, 10, 10, 25, 10),
            [EnchKind.Smite] = I("Smite", 5, 5, 2, 5, 8, 25, 8),
            [EnchKind.BaneOfArthropods] = I("Bane of Arthropods", 5, 5, 2, 5, 8, 25, 8),
            [EnchKind.Knockback] = I("Knockback", 2, 5, 2, 5, 20, 55, 20),
            [EnchKind.FireAspect] = I("Fire Aspect", 2, 2, 4, 10, 20, 60, 20),
            [EnchKind.Looting] = I("Looting", 3, 2, 4, 15, 9, 65, 9),
            [EnchKind.SweepingEdge] = I("Sweeping Edge", 3, 2, 4, 5, 9, 20, 9),
            [EnchKind.SilkTouch] = I("Silk Touch", 1, 1, 8, 15, 0, 65, 0),
            [EnchKind.Fortune] = I("Fortune", 3, 2, 4, 15, 9, 65, 9),
            [EnchKind.Power] = I("Power", 5, 10, 1, 1, 10, 16, 10),
            [EnchKind.Punch] = I("Punch", 2, 2, 4, 12, 20, 37, 20),
            [EnchKind.Flame] = I("Flame", 1, 2, 4, 20, 0, 50, 0),
            [EnchKind.Infinity] = I("Infinity", 1, 1, 8, 20, 0, 50, 0),
            [EnchKind.LuckOfTheSea] = I("Luck of the Sea", 3, 2, 4, 15, 9, 65, 9),
            [EnchKind.Lure] = I("Lure", 3, 2, 4, 15, 9, 65, 9),
            [EnchKind.Mending] = I("Mending", 1, 2, 4, 25, 25, 75, 25, treasure: true),
        };

        public static IEnumerable<EnchKind> AllKinds => info.Keys;
        public static Info InfoOf(EnchKind k) => info.TryGetValue(k, out var i) ? i : default;
        public static string Name(EnchKind k) => info.TryGetValue(k, out var i) ? i.Name : "";
        public static int MaxLevel(EnchKind k) => info.TryGetValue(k, out var i) ? i.Max : 0;
        /// <summary>Minecraft's anvil cost per level (an enchanted book's is half of it).</summary>
        public static int AnvilCost(EnchKind k) => info.TryGetValue(k, out var i) ? i.Anvil : 1;
        public static int MinCost(EnchKind k, int level) { var i = InfoOf(k); return i.MinBase + i.MinPer * (level - 1); }
        public static int MaxCost(EnchKind k, int level) { var i = InfoOf(k); return i.MaxBase + i.MaxPer * (level - 1); }

        static readonly EnchTarget[] AllArmor = { EnchTarget.Helmet, EnchTarget.Armor, EnchTarget.Leggings, EnchTarget.Boots };
        static readonly EnchTarget[] Mining = { EnchTarget.Tool, EnchTarget.Axe, EnchTarget.Hoe };
        static readonly EnchTarget[] Durable = { EnchTarget.Tool, EnchTarget.Axe, EnchTarget.Hoe, EnchTarget.Sword, EnchTarget.Shears, EnchTarget.Helmet, EnchTarget.Armor,
            EnchTarget.Leggings, EnchTarget.Boots, EnchTarget.Bow, EnchTarget.Crossbow, EnchTarget.FishingRod, EnchTarget.Elytra, EnchTarget.FlintAndSteel };

        /// <summary>Minecraft's supported_items (the anvil's): what an enchantment can go on.</summary>
        public static bool Supports(EnchKind k, EnchTarget t)
        {
            if (t == EnchTarget.Book) return info.ContainsKey(k);
            switch (k)
            {
                case EnchKind.Protection: case EnchKind.FireProtection: case EnchKind.BlastProtection: case EnchKind.ProjectileProtection: case EnchKind.Thorns:
                    return AllArmor.Contains(t);
                case EnchKind.Respiration: case EnchKind.AquaAffinity: return t == EnchTarget.Helmet;
                case EnchKind.FeatherFalling: case EnchKind.DepthStrider: return t == EnchTarget.Boots;
                case EnchKind.Sharpness: case EnchKind.Smite: case EnchKind.BaneOfArthropods: return t == EnchTarget.Sword || t == EnchTarget.Axe;
                case EnchKind.Knockback: case EnchKind.FireAspect: case EnchKind.Looting: case EnchKind.SweepingEdge: return t == EnchTarget.Sword;
                case EnchKind.Efficiency: return Mining.Contains(t) || t == EnchTarget.Shears;
                case EnchKind.SilkTouch: case EnchKind.Fortune: return Mining.Contains(t);
                case EnchKind.Unbreaking: case EnchKind.Mending: return Durable.Contains(t);
                case EnchKind.Power: case EnchKind.Punch: case EnchKind.Flame: case EnchKind.Infinity: return t == EnchTarget.Bow;
                case EnchKind.LuckOfTheSea: case EnchKind.Lure: return t == EnchTarget.FishingRod;
                case EnchKind.QuickCharge: case EnchKind.Multishot: case EnchKind.Piercing: return t == EnchTarget.Crossbow;
                default: return false;
            }
        }

        /// <summary>Minecraft's primary_items (the table's): Sharpness, Smite and Bane only on swords there (an axe takes them
        /// at the anvil), Thorns only on a chestplate; no treasure (Mending).</summary>
        public static bool AtTable(EnchKind k, EnchTarget t)
        {
            var i = InfoOf(k);
            if (i.Treasure || !Supports(k, t) || (k == EnchKind.Fortune && !FortuneOn)) return false;
            if (t == EnchTarget.Book) return true;
            if ((k == EnchKind.Sharpness || k == EnchKind.Smite || k == EnchKind.BaneOfArthropods) && t == EnchTarget.Axe) return false;
            if (k == EnchKind.Thorns && t != EnchTarget.Armor) return false;
            return true;
        }

        /// <summary>The kinds an item can take (at the anvil).</summary>
        public static IEnumerable<EnchKind> For(EnchTarget t) => info.Keys.Where(k => Supports(k, t));

        /// <summary>Minecraft's exclusive sets: damage (sharpness, smite, bane), armor (the four protections), mining (fortune,
        /// silk touch), bow (infinity, mending), crossbow (multishot, piercing).</summary>
        public static bool Exclusive(EnchKind a, EnchKind b)
        {
            if (a == b) return false;
            foreach (var set in exclusive) if (set.Contains(a) && set.Contains(b)) return true;
            return false;
        }
        static readonly EnchKind[][] exclusive =
        {
            new[] { EnchKind.Sharpness, EnchKind.Smite, EnchKind.BaneOfArthropods },
            new[] { EnchKind.Protection, EnchKind.FireProtection, EnchKind.BlastProtection, EnchKind.ProjectileProtection },
            new[] { EnchKind.Fortune, EnchKind.SilkTouch },
            new[] { EnchKind.Infinity, EnchKind.Mending },
            new[] { EnchKind.Multishot, EnchKind.Piercing },
        };

        // (an enchantment's 6 bits in the old packing: its code and level, see the summary)
        static bool Packable(EnchKind k, int level) => k >= EnchKind.Efficiency && k <= EnchKind.Piercing && level >= 1 && level <= 7;

        static int Code(EnchKind k, int level)
        {
            level = Math.Min(level, 7);
            switch (k)
            {
                case EnchKind.QuickCharge: return 6 | (Math.Min(level, 3) << 3);
                case EnchKind.Multishot: return 7 | (1 << 3);
                case EnchKind.Piercing: return 7 | ((1 + Math.Min(level, 4)) << 3);
                default: return ((int)k & 7) | (level << 3);
            }
        }

        static (EnchKind, int) FromCode(int b)
        {
            int c = b & 7, l = b >> 3;
            if (c == 6) return (EnchKind.QuickCharge, l);
            if (c == 7) return l <= 1 ? (EnchKind.Multishot, 1) : (EnchKind.Piercing, l - 1);
            return ((EnchKind)c, l);
        }

        // ------------------------------------------------------------------ the saved number
        public static int UsesOf(int data) => data & UsesMask;
        public static int EnchOf(int data) => (int)((uint)data >> UsesBits);
        /// <summary>The saved number; a list still waiting for its number from the host (Pack's -1) gives none.</summary>
        public static int Data(int uses, int ench) => (Math.Max(0, Math.Min(uses, UsesMask))) | (Math.Max(0, ench) << UsesBits);

        static bool IsSet(int ench) => (ench & 63) == EnchSets.Marker;

        /// <summary>Enchantments (and an anvil work count) as the bits above the uses: packed, or a list's number. -1: another
        /// machine waits for the host's number for this list (it's been asked for).</summary>
        public static int Pack(IEnumerable<(EnchKind kind, int level)> list, int work = 0, int color = -1)
        {
            var l = list.Where(e => e.kind != EnchKind.None && e.level > 0).ToList();
            work = Math.Max(0, work);
            bool fits = color < 0 && l.Count <= MaxPerItem && l.All(e => Packable(e.kind, e.level)) && work <= 7 && (work == 0 || l.Count < MaxPerItem);
            if (fits)
            {
                int e = 0;
                for (int i = 0; i < l.Count; i++) e |= Code(l[i].kind, l[i].level) << (6 * i);
                if (work > 0) e |= work << 15;
                return e;
            }
            int id = EnchSets.IdOf(l, work, color);
            return id > 0 ? EnchSets.Marker | (id << 6) : -1;
        }

        public static List<(EnchKind kind, int level)> Unpack(int ench)
        {
            if (ench > 0 && IsSet(ench))
                return EnchSets.TryGet((ench >> 6) & EnchSets.MaxId, out var l, out _) ? new List<(EnchKind, int)>(l) : new List<(EnchKind, int)>();
            var list = new List<(EnchKind, int)>();
            for (int i = 0; i < MaxPerItem; i++)
            {
                int b = (ench >> (6 * i)) & 63;
                if ((b & 7) == 0) continue;
                list.Add(FromCode(b));
            }
            return list;
        }

        // ------------------------------------------------------------------ (#228) the anvil's prior work
        /// <summary>
        /// Minecraft's repair cost (each anvil use doubles it, plus one: 0, 1, 3, 7...) kept as how many times the item has
        /// been worked: packed, in the third enchantment's six bits while that one's empty; with the list otherwise.
        /// </summary>
        public static int WorkOf(int ench)
        {
            if (ench > 0 && IsSet(ench)) return EnchSets.TryGet((ench >> 6) & EnchSets.MaxId, out _, out int w) ? w : 0;
            int b = (ench >> 12) & 63;
            return (b & 7) == 0 ? (b >> 3) & 7 : 0;
        }

        /// <summary>The same enchantments with this work count (-1: waiting for the host's number).</summary>
        public static int WithWork(int ench, int work) => Pack(Unpack(ench), work, ColorOf(ench));

        /// <summary>(#244) A dyed leather piece's colour (0xRRGGBB), or -1: never dyed.</summary>
        public static int ColorOf(int ench) => ench > 0 && IsSet(ench) && EnchSets.TryGet((ench >> 6) & EnchSets.MaxId, out _, out _, out int c) ? c : -1;
        /// <summary>(#244) The same enchantments and work with this colour (-1: washed off; -1 back: waiting for the host).</summary>
        public static int WithColor(int ench, int color) => Pack(Unpack(ench), WorkOf(ench), color);

        /// <summary>The enchantment bits without the work count (-1: waiting).</summary>
        public static int Only(int ench) => WithWork(ench, 0);
        /// <summary>Any enchantment on it (the work count isn't one).</summary>
        public static bool Any(int ench) => ench > 0 && Unpack(ench).Count > 0;
        /// <summary>Minecraft's repair cost for a work count: 2^n - 1.</summary>
        public static int RepairCost(int work) => (1 << Math.Max(0, Math.Min(work, 30))) - 1;

        public static int Level(int ench, EnchKind k)
        {
            foreach (var (kind, level) in Unpack(ench)) if (kind == k) return level;
            return 0;
        }

        public static string Roman(int n) => n >= 1 && n <= 10 ? new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" }[n - 1] : n.ToString();

        /// <summary>"Efficiency III, Unbreaking I" (Minecraft leaves the level off a one-level enchantment).</summary>
        public static string Describe(int ench) => string.Join(", ", Unpack(ench).Select(e => Name(e.kind) + (MaxLevel(e.kind) == 1 ? "" : " " + Roman(e.level))));

        // ------------------------------------------------------------------ what a table offers
        public struct Offer
        {
            public int Required; // levels you need to have
            public int Cost;     // levels (and lapis) it takes
            public int Ench;     // what you get (-1: waiting for the host's number for its list)
            public List<(EnchKind kind, int level)> List; // (#244) the same, unpacked
            public (EnchKind kind, int level) Clue;        // (#244) the one the screen hints at
        }

        /// <summary>
        /// The three offers, Minecraft's way (EnchantmentMenu, EnchantmentHelper): the bookshelves (up to 15) set each slot's
        /// level cost; the item's enchantability raises it; each slot picks enchantments by weight among those whose cost
        /// range holds it, and more now and then, halving as it goes; a book keeps all but one. The player's seed picks them,
        /// so they stay the same until something is enchanted. Enchantability 0: nothing.
        /// </summary>
        public static Offer[] Offers(int seed, int shelves, EnchTarget t) => Offers(seed, shelves, t, Enchantability(t, null));

        public static Offer[] Offers(int seed, int shelves, EnchTarget t, int enchantability)
        {
            var offers = new Offer[3];
            if (enchantability <= 0 || t == EnchTarget.None) return offers;
            int power = Math.Max(0, Math.Min(15, shelves));
            var rnd = new Random(seed);
            int b = rnd.Next(8) + 1 + (power >> 1) + rnd.Next(power + 1);
            int[] req = { Math.Max(b / 3, 1), b * 2 / 3 + 1, Math.Max(b, power * 2) };
            for (int i = 0; i < 3; i++)
            {
                if (req[i] < i + 1) { offers[i] = new Offer(); continue; } // (Minecraft's: below the slot's own number, none)
                var r = new Random(seed + i);
                var list = Select(r, t, enchantability, req[i]);
                if (t == EnchTarget.Book && list.Count > 1) list.RemoveAt(r.Next(list.Count));
                if (list.Count == 0) { offers[i] = new Offer(); continue; }
                offers[i] = new Offer { Required = req[i], Cost = i + 1, List = list, Ench = Pack(list), Clue = list[r.Next(list.Count)] };
            }
            return offers;
        }

        /// <summary>Minecraft's selectEnchantment.</summary>
        public static List<(EnchKind kind, int level)> Select(Random r, EnchTarget t, int enchantability, int cost)
        {
            var result = new List<(EnchKind, int)>();
            cost += 1 + r.Next(enchantability / 4 + 1) + r.Next(enchantability / 4 + 1);
            float span = ((float)r.NextDouble() + (float)r.NextDouble() - 1f) * 0.15f;
            cost = Math.Max(1, (int)Math.Round(cost + cost * span));
            var avail = Available(cost, t);
            if (avail.Count == 0) return result;
            result.Add(Weighted(r, avail));
            while (r.Next(50) <= cost)
            {
                var last = result[result.Count - 1].Item1;
                avail.RemoveAll(e => e.kind == last || Exclusive(e.kind, last) || result.Any(x => x.Item1 == e.kind));
                if (avail.Count == 0) break;
                result.Add(Weighted(r, avail));
                cost /= 2;
            }
            return result;
        }

        /// <summary>Minecraft's getAvailableEnchantmentResults: each table enchantment at the highest level whose cost range
        /// holds the cost.</summary>
        public static List<(EnchKind kind, int level)> Available(int cost, EnchTarget t)
        {
            var list = new List<(EnchKind, int)>();
            foreach (var k in info.Keys)
            {
                if (!AtTable(k, t)) continue;
                for (int l = MaxLevel(k); l >= 1; l--)
                    if (cost >= MinCost(k, l) && cost <= MaxCost(k, l)) { list.Add((k, l)); break; }
            }
            return list;
        }

        static (EnchKind, int) Weighted(Random r, List<(EnchKind kind, int level)> list)
        {
            int total = list.Sum(e => InfoOf(e.kind).Weight);
            int pick = r.Next(Math.Max(1, total));
            foreach (var e in list) { pick -= InfoOf(e.kind).Weight; if (pick < 0) return e; }
            return list[list.Count - 1];
        }

        /// <summary>Minecraft's enchantability (enchantable component): by material for tools and armor (pass the item's key);
        /// 1 for a bow, crossbow, fishing rod or book; 0 (not at the table): shears, elytra, flint and steel.</summary>
        public static int Enchantability(EnchTarget t, string key)
        {
            switch (t)
            {
                case EnchTarget.Bow: case EnchTarget.Crossbow: case EnchTarget.FishingRod: case EnchTarget.Book: return 1;
                case EnchTarget.Shears: case EnchTarget.Elytra: case EnchTarget.FlintAndSteel: case EnchTarget.None: return 0;
            }
            string k = key ?? "";
            bool armor = t == EnchTarget.Helmet || t == EnchTarget.Armor || t == EnchTarget.Leggings || t == EnchTarget.Boots;
            if (k.StartsWith("netherite")) return 15;
            if (k.StartsWith("golden") || k.StartsWith("gold")) return armor ? 25 : 22;
            if (k.StartsWith("diamond") || k == "pickaxe") return 10;
            if (k.StartsWith("iron")) return armor ? 9 : 14;
            if (k.StartsWith("chainmail")) return 12;
            if (k.StartsWith("leather")) return 15;
            if (k.StartsWith("copper")) return armor ? 8 : 13;
            if (k.StartsWith("stone")) return 5;
            if (k.StartsWith("wooden")) return 15;
            if (k.StartsWith("turtle")) return 9;
            return armor ? 9 : 14;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ what they do (Minecraft 26.3's formulas)
        /// <summary>(#244) Efficiency: Minecraft adds level² + 1 to the tool's mining speed (data/enchantment/efficiency:
        /// mining_efficiency). The factor that gives over the tool's own speed.</summary>
        public static float EfficiencyFactor(int level, float toolSpeed) => level <= 0 || toolSpeed <= 0f ? 1f : (toolSpeed + level * level + 1f) / toolSpeed;
        /// <summary>(as before #244, for callers without a tool speed: a diamond pickaxe's 8)</summary>
        public static float EfficiencyFactor(int level) => EfficiencyFactor(level, 8f);
        /// <summary>(#244) Sharpness: Minecraft's +1 and +0.5 a level after the first (0.5 + 0.5 x level), in its damage.</summary>
        public static float SharpnessBonus(int level) => level <= 0 ? 0f : 0.5f + 0.5f * level;
        /// <summary>(as before: a factor on a sword's hit; now Minecraft's bonus over an iron sword's 6)</summary>
        public static float SharpnessFactor(int level) => 1f + SharpnessBonus(level) / 6f;
        /// <summary>(#244) Smite and Bane of Arthropods: +2.5 a level against the undead, against arthropods.</summary>
        public static float SmiteBonus(int level) => 2.5f * Math.Max(0, level);
        /// <summary>(#244) Feather Falling: Minecraft's 12% less fall damage a level (3 protection points of 4% each), within
        /// the 80% all protection together can take.</summary>
        public static float FeatherFallingFactor(int level) => Math.Max(0.2f, 1f - 0.12f * Math.Max(0, level));
        /// <summary>(#244) Minecraft's protection points (EPF) against a kind of damage: Protection 1 a level; Fire, Blast,
        /// Projectile Protection 2 a level against theirs; Feather Falling 3 against falls; all of it 4% each, 80% at most.</summary>
        public static float ProtectionFactor(int points) => 1f - Math.Min(20, Math.Max(0, points)) * 0.04f;
        /// <summary>Quick Charge: a crossbow loads 0.25 s faster a level (Minecraft's 1.25 s, crossbow_charge_time).</summary>
        public static float CrossbowChargeSeconds(int quickCharge) => Math.Max(0f, 1.25f - 0.25f * Math.Max(0, quickCharge));
        /// <summary>Unbreaking: the chance a use wears the tool, 1 / (level + 1) (armor: 60% + 40% / (level + 1)).</summary>
        public static float WearChance(int level) => 1f / (1 + Math.Max(0, level));
        public static float ArmorWearChance(int level) => level <= 0 ? 1f : 0.6f + 0.4f / (1 + level);
    }
}
