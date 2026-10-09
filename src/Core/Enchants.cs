using System;
using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    public enum EnchKind : byte { None, Efficiency, Unbreaking, Sharpness, Protection, FeatherFalling }

    /// <summary>What an item can be enchanted as.</summary>
    public enum EnchTarget : byte { None, Tool, Sword, Armor, Boots }

    /// <summary>
    /// Enchantments (#46), Minecraft's, a few of them: Efficiency (mining speed) and Unbreaking (fewer uses worn) on tools,
    /// Sharpness on swords (and axes), Protection on armor, Feather Falling on boots. An item's saved number (ItemData)
    /// holds its uses in the low bits and up to three enchantments above them, 6 bits each (kind, level).
    /// </summary>
    public static class Enchants
    {
        public const int UsesBits = 13, UsesMask = (1 << UsesBits) - 1, MaxPerItem = 3;

        static readonly string[] names = { "", "Efficiency", "Unbreaking", "Sharpness", "Protection", "Feather Falling" };
        public static string Name(EnchKind k) => names[(int)k < names.Length ? (int)k : 0];

        public static int MaxLevel(EnchKind k)
        {
            switch (k)
            {
                case EnchKind.Efficiency: return 5;
                case EnchKind.Unbreaking: return 3;
                case EnchKind.Sharpness: return 5;
                case EnchKind.Protection: return 4;
                case EnchKind.FeatherFalling: return 4;
                default: return 0;
            }
        }

        public static IEnumerable<EnchKind> For(EnchTarget t)
        {
            switch (t)
            {
                case EnchTarget.Tool: return new[] { EnchKind.Efficiency, EnchKind.Unbreaking };
                case EnchTarget.Sword: return new[] { EnchKind.Sharpness, EnchKind.Unbreaking };
                case EnchTarget.Armor: return new[] { EnchKind.Protection };
                case EnchTarget.Boots: return new[] { EnchKind.Protection, EnchKind.FeatherFalling };
                default: return new EnchKind[0];
            }
        }

        // ------------------------------------------------------------------ the saved number
        public static int UsesOf(int data) => data & UsesMask;
        public static int EnchOf(int data) => (int)((uint)data >> UsesBits);
        public static int Data(int uses, int ench) => (Math.Max(0, Math.Min(uses, UsesMask))) | (ench << UsesBits);

        public static int Pack(IEnumerable<(EnchKind kind, int level)> list)
        {
            int e = 0, i = 0;
            foreach (var (k, l) in list)
            {
                if (k == EnchKind.None || l <= 0 || i >= MaxPerItem) continue;
                e |= (((int)k & 7) | (Math.Min(l, 7) << 3)) << (6 * i);
                i++;
            }
            return e;
        }

        public static List<(EnchKind kind, int level)> Unpack(int ench)
        {
            var list = new List<(EnchKind, int)>();
            for (int i = 0; i < MaxPerItem; i++)
            {
                int b = (ench >> (6 * i)) & 63;
                if ((b & 7) == 0) continue;
                list.Add(((EnchKind)(b & 7), b >> 3));
            }
            return list;
        }

        public static int Level(int ench, EnchKind k)
        {
            foreach (var (kind, level) in Unpack(ench)) if (kind == k) return level;
            return 0;
        }

        public static string Roman(int n) => n >= 1 && n <= 10 ? new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" }[n - 1] : n.ToString();

        /// <summary>"Efficiency III, Unbreaking I".</summary>
        public static string Describe(int ench) => string.Join(", ", Unpack(ench).Select(e => Name(e.kind) + " " + Roman(e.level)));

        // ------------------------------------------------------------------ what a table offers
        public struct Offer
        {
            public int Required; // levels you need to have
            public int Cost;     // levels (and lapis) it takes
            public int Ench;     // what you get
        }

        /// <summary>
        /// The three offers, Minecraft's way: the bookshelves around the table (up to 15) raise how high they go (up to
        /// level 30); the player's enchanting seed picks them, so they stay the same until something is enchanted.
        /// </summary>
        public static Offer[] Offers(int seed, int shelves, EnchTarget t)
        {
            var kinds = For(t).ToList();
            var offers = new Offer[3];
            if (kinds.Count == 0) return offers;
            int power = Math.Max(0, Math.Min(15, shelves));
            var rnd = new Random(seed * 31 + (int)t);
            int b = rnd.Next(1, 9) + (power >> 1) + rnd.Next(0, power + 1);
            int[] req = { Math.Max(b / 3, 1), b * 2 / 3 + 1, Math.Max(b, power * 2) };
            for (int i = 0; i < 3; i++)
            {
                var r = new Random(seed * 131 + i * 17 + (int)t);
                var k1 = kinds[r.Next(kinds.Count)];
                int l1 = Clamp((int)Math.Ceiling(req[i] * MaxLevel(k1) / 30.0), 1, MaxLevel(k1));
                var list = new List<(EnchKind, int)> { (k1, l1) };
                // higher offers sometimes come with a second enchantment
                if (kinds.Count > 1 && r.Next(0, 50) < req[i] + 5)
                {
                    var k2 = kinds.First(k => k != k1);
                    list.Add((k2, Clamp(l1 - 1, 1, MaxLevel(k2))));
                }
                offers[i] = new Offer { Required = req[i], Cost = i + 1, Ench = Pack(list) };
            }
            return offers;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ what they do
        /// <summary>Mining speed: I x1.3, II x1.7, III x2.2, IV x2.8, V x3.5.</summary>
        public static float EfficiencyFactor(int level) => level <= 0 ? 1f : 1f + 0.25f * level + 0.05f * level * level;
        /// <summary>Sharpness: +10% damage a level.</summary>
        public static float SharpnessFactor(int level) => 1f + 0.1f * Math.Max(0, level);
        /// <summary>Feather Falling: 15% less fall damage a level.</summary>
        public static float FeatherFallingFactor(int level) => Math.Max(0.2f, 1f - 0.15f * Math.Max(0, level));
        /// <summary>Unbreaking: the chance a use wears the tool, 1 / (level + 1).</summary>
        public static float WearChance(int level) => 1f / (1 + Math.Max(0, level));
    }
}
