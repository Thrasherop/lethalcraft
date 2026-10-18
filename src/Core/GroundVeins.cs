using System;
using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Ore veins in natural stone (#220, worked out with the owner; pure, no engine calls: the offline unit tests cover it).
    /// The ground is cut into 4x4x4-block chunks. Each chunk holds at most one ore vein: whether it has one is the vein
    /// chance (times the moon's ore multiplier, more on harder moons), and which ore it is is picked by weight ("chance at
    /// depth") among the ores whose min depth the chunk is past, its depth being blocks below the surface (anything under
    /// world y -100, the facility's rock with it, counts as deep). Lava pockets and the granite, diorite and andesite
    /// patches are layers of their own, each with its own chance per chunk, outside the one-vein rule (a diamond vein can
    /// sit against lava); they fill only what the ore vein left. A vein's whole shape is decided at once from the chunk's
    /// coordinates and the map's seed, so every player sees the same blocks, a vein is found whole, and a moon's ore
    /// changes from one landing to the next.
    /// </summary>
    public static class GroundVeins
    {
        public const int Region = 4;

        public class Kind
        {
            public GroundRules.Ore Ore;
            /// <summary>Ores: only in chunks at least this many blocks below the surface. Layers: the same.</summary>
            public float MinDepth;
            /// <summary>Ores: how likely the chunk's vein is this one, once past its min depth ("chance at depth", a weight
            /// against the other ores there). Layers: the chance a chunk holds one.</summary>
            public float Chance;
            public int Min, Max;
            public float Avg;
        }

        /// <summary>The ores (at most one vein of them per chunk).</summary>
        public static readonly List<Kind> Ores = new List<Kind>
        {
            new Kind { Ore = GroundRules.Ore.Coal, MinDepth = 0f, Chance = 40f, Min = 4, Max = 12, Avg = 7f },
            new Kind { Ore = GroundRules.Ore.Iron, MinDepth = 0f, Chance = 30f, Min = 2, Max = 9, Avg = 4.5f },
            new Kind { Ore = GroundRules.Ore.Copper, MinDepth = 0f, Chance = 25f, Min = 2, Max = 10, Avg = 5f },
            // gravel (bows: flint comes from it)
            new Kind { Ore = GroundRules.Ore.Gravel, MinDepth = 8f, Chance = 6f, Min = 1, Max = 3, Avg = 2f },
            // (each block gives 4-5 dust)
            new Kind { Ore = GroundRules.Ore.Redstone, MinDepth = 15f, Chance = 15f, Min = 3, Max = 8, Avg = 5f },
            new Kind { Ore = GroundRules.Ore.Gold, MinDepth = 25f, Chance = 12f, Min = 2, Max = 6, Avg = 3.5f },
            // lapis (#46): rare on purpose (enchanting could get out of hand)
            new Kind { Ore = GroundRules.Ore.Lapis, MinDepth = 30f, Chance = 6f, Min = 1, Max = 4, Avg = 2f },
            new Kind { Ore = GroundRules.Ore.Diamond, MinDepth = 30f, Chance = 5f, Min = 1, Max = 5, Avg = 2.5f },
            new Kind { Ore = GroundRules.Ore.Emerald, MinDepth = 30f, Chance = 3f, Min = 1, Max = 2, Avg = 1.2f },
        };

        /// <summary>The layers of their own: lava pockets (#32), and the stone patches (#154).</summary>
        public static readonly List<Kind> Layers = new List<Kind>
        {
            new Kind { Ore = GroundRules.Ore.Lava, MinDepth = 20f, Chance = 0.06f, Min = 4, Max = 12, Avg = 7f },
            new Kind { Ore = GroundRules.Ore.Granite, MinDepth = 0f, Chance = 0.15f, Min = 6, Max = 24, Avg = 12f },
            new Kind { Ore = GroundRules.Ore.Diorite, MinDepth = 0f, Chance = 0.15f, Min = 6, Max = 24, Avg = 12f },
            new Kind { Ore = GroundRules.Ore.Andesite, MinDepth = 0f, Chance = 0.15f, Min = 6, Max = 24, Avg = 12f },
        };

        /// <summary>The chance a chunk holds an ore vein (the owner's global knob), before the moon's multiplier.</summary>
        public static float VeinChance = 0.15f;

        /// <summary>More ore on harder moons (set per moon by the host when it lands; 1 = as configured).</summary>
        public static float MoonMultiplier = 1f;

        /// <summary>The map's seed: a moon's ore changes from one landing to the next.</summary>
        public static int Seed;

        /// <summary>Blocks below the surface at a cell (the ground sets it: a ray up to the moon's own surface; deep under
        /// world y -100). Pure tests set their own.</summary>
        public static Func<int, int, int, float> DepthAt = (x, y, z) => 40f;

        /// <summary>Every kind, ores then layers (the config and the dev tools go through them all).</summary>
        public static IEnumerable<Kind> All { get { foreach (var k in Ores) yield return k; foreach (var k in Layers) yield return k; } }

        // ------------------------------------------------------------------ hashing
        static uint Hash(int x, int y, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 0x27d4eb2d) ^ (uint)(Seed * 0x165667b1) ^ 0x9E3779B9u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return h;
        }

        static float Unit(uint h) => (h & 0xFFFFFF) / 16777216f;

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>A vein size between min and max with (about) the given average: min + (max - min) * u^a.</summary>
        public static int Size(Kind k, float u)
        {
            if (k.Max <= k.Min) return k.Min;
            float frac = Math.Max(0.01f, Math.Min(0.99f, (k.Avg - k.Min) / (k.Max - k.Min)));
            float a = 1f / frac - 1f;
            return k.Min + (int)Math.Round(Math.Pow(u, a) * (k.Max - k.Min));
        }

        /// <summary>The ore a chunk this deep picks with roll u (in [0, 1)), by weight among those past their min depth;
        /// None if none is.</summary>
        public static GroundRules.Ore PickOre(float depth, float u)
        {
            float total = 0f;
            foreach (var k in Ores) if (depth >= k.MinDepth && k.Chance > 0f) total += k.Chance;
            if (total <= 0f) return GroundRules.Ore.None;
            float r = u * total;
            foreach (var k in Ores)
            {
                if (depth < k.MinDepth || k.Chance <= 0f) continue;
                r -= k.Chance;
                if (r < 0f) return k.Ore;
            }
            return GroundRules.Ore.None;
        }

        // ------------------------------------------------------------------ veins
        static readonly Dictionary<(int, int, int), Dictionary<(int, int, int), GroundRules.Ore>> cache = new Dictionary<(int, int, int), Dictionary<(int, int, int), GroundRules.Ore>>();

        /// <summary>A random walk of the vein's size from a random cell of the chunk (it stays in the chunk), into cells
        /// nothing has taken yet.</summary>
        static void Walk(Dictionary<(int, int, int), GroundRules.Ore> cells, int rx, int ry, int rz, Kind k, int salt)
        {
            int n = Size(k, Unit(Hash(rx, ry, rz, 201 + salt)));
            uint h = Hash(rx, ry, rz, 301 + salt);
            int cx = (int)(h % Region), cy = (int)(h / Region % Region), cz = (int)(h / (Region * Region) % Region);
            int added = 0;
            for (int step = 0; added < n && step < n * 8; step++)
            {
                var c = (rx * Region + cx, ry * Region + cy, rz * Region + cz);
                if (!cells.ContainsKey(c)) { cells[c] = k.Ore; added++; }
                switch (Hash(rx, ry, rz, 1000 * (salt + 1) + step) % 6)
                {
                    case 0: cx = Math.Min(Region - 1, cx + 1); break;
                    case 1: cx = Math.Max(0, cx - 1); break;
                    case 2: cy = Math.Min(Region - 1, cy + 1); break;
                    case 3: cy = Math.Max(0, cy - 1); break;
                    case 4: cz = Math.Min(Region - 1, cz + 1); break;
                    default: cz = Math.Max(0, cz - 1); break;
                }
            }
        }

        /// <summary>The ore and layer cells of a chunk.</summary>
        static Dictionary<(int, int, int), GroundRules.Ore> RegionCells(int rx, int ry, int rz)
        {
            var key = (rx, ry, rz);
            if (cache.TryGetValue(key, out var have)) return have;
            var cells = new Dictionary<(int, int, int), GroundRules.Ore>();
            // (its depth: at its middle; one depth for the whole chunk, so its vein is one ore on every machine)
            float depth = DepthAt(rx * Region + Region / 2, ry * Region + Region / 2, rz * Region + Region / 2);
            // the ore vein first (at most one)
            if (Unit(Hash(rx, ry, rz, 101)) < Math.Min(1f, VeinChance * MoonMultiplier))
            {
                var ore = PickOre(depth, Unit(Hash(rx, ry, rz, 102)));
                var k = Ores.Find(o => o.Ore == ore);
                if (k != null) Walk(cells, rx, ry, rz, k, 0);
            }
            // then the layers, into what's left
            for (int i = 0; i < Layers.Count; i++)
            {
                var k = Layers[i];
                if (depth < k.MinDepth || k.Chance <= 0f) continue;
                if (Unit(Hash(rx, ry, rz, 111 + i)) >= Math.Min(1f, k.Chance)) continue; // (not the moon's multiplier: that's for ore)
                Walk(cells, rx, ry, rz, k, 1 + i);
            }
            if (cache.Count > 50000) cache.Clear();
            cache[key] = cells;
            return cells;
        }

        /// <summary>Forget cached veins (after the rates, the seed or the moon change).</summary>
        public static void Reset() => cache.Clear();

        /// <summary>The ore (or lava, or stone patch) in a stone cell; None: plain stone.</summary>
        public static GroundRules.Ore OreAt(int x, int y, int z)
        {
            var cells = RegionCells(FloorDiv(x, Region), FloorDiv(y, Region), FloorDiv(z, Region));
            return cells.TryGetValue((x, y, z), out var o) ? o : GroundRules.Ore.None;
        }
    }
}
