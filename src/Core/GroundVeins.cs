using System;
using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Ore veins in natural stone (pure, no engine calls; the offline unit tests cover it). The ground is cut into 4x4x4
    /// regions; each region may hold a vein of each ore, its whole shape decided at once from the region's coordinates
    /// (a random walk of 2-9 cells for iron), so every player sees the same blocks and a vein is found whole, not cell by
    /// cell (#43). How often veins turn up comes from how much of each ore half a moon of mining should give
    /// (<see cref="Calibrate"/>).
    /// </summary>
    public static class GroundVeins
    {
        public const int Region = 4;

        public class Kind
        {
            public GroundRules.Ore Ore;
            /// <summary>Ore blocks a player mining for half a moon (8 in-game hours, stone pickaxe) should get, on average.</summary>
            public float PerHalfMoon;
            public int Min, Max;
            public float Avg;
            /// <summary>Only this many blocks (or more) from open space (the surface, caves, rooms).</summary>
            public float MinDepth;
            /// <summary>Only this many blocks (or more) below the surface straight above (diamonds: dig down for them).</summary>
            public float MinBelow;
            /// <summary>Chance a region holds a vein of this ore (worked out by <see cref="Calibrate"/>).</summary>
            public float Chance;
        }

        /// <summary>Rarest first (where two veins meet, the rarer ore wins).</summary>
        public static readonly List<Kind> Kinds = new List<Kind>
        {
            new Kind { Ore = GroundRules.Ore.Diamond, PerHalfMoon = 4f, Min = 1, Max = 5, Avg = 2.5f, MinDepth = 1.5f, MinBelow = 30f },
            new Kind { Ore = GroundRules.Ore.Emerald, PerHalfMoon = 2f, Min = 1, Max = 2, Avg = 1.2f, MinDepth = 4.5f },
            new Kind { Ore = GroundRules.Ore.Gold, PerHalfMoon = 6f, Min = 2, Max = 6, Avg = 3.5f, MinDepth = 3.5f },
            new Kind { Ore = GroundRules.Ore.Iron, PerHalfMoon = 22f, Min = 2, Max = 9, Avg = 4.5f, MinDepth = 0f },
            new Kind { Ore = GroundRules.Ore.Coal, PerHalfMoon = 30f, Min = 4, Max = 12, Avg = 7f, MinDepth = 0f },
        };

        /// <summary>More ore on some moons (set per moon by the host when it lands; 1 = as configured).</summary>
        public static float MoonMultiplier = 1f;

        // ------------------------------------------------------------------ hashing
        static uint Hash(int x, int y, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 0x27d4eb2d) ^ 0x9E3779B9u;
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

        // ------------------------------------------------------------------ veins
        static readonly Dictionary<(int, int, int), Dictionary<(int, int, int), int>> cache = new Dictionary<(int, int, int), Dictionary<(int, int, int), int>>();

        /// <summary>The ore cells of a region: cell -> index into <see cref="Kinds"/>. Only kinds with index &lt; 0 skipped.</summary>
        static Dictionary<(int, int, int), int> RegionCells(int rx, int ry, int rz, int only = -1)
        {
            var key = (rx, ry, rz);
            if (only < 0 && cache.TryGetValue(key, out var have)) return have;
            var cells = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < Kinds.Count; i++)
            {
                if (only >= 0 && i != only) continue;
                var k = Kinds[i];
                float chance = Math.Min(1f, k.Chance * (only >= 0 ? 1f : MoonMultiplier));
                if (chance <= 0f || Unit(Hash(rx, ry, rz, 101 + i)) >= chance) continue;
                int n = Size(k, Unit(Hash(rx, ry, rz, 201 + i)));
                uint h = Hash(rx, ry, rz, 301 + i);
                int cx = (int)(h % Region), cy = (int)(h / Region % Region), cz = (int)(h / (Region * Region) % Region);
                int added = 0;
                for (int step = 0; added < n && step < n * 8; step++)
                {
                    var c = (rx * Region + cx, ry * Region + cy, rz * Region + cz);
                    if (!cells.ContainsKey(c)) { cells[c] = i; added++; }
                    uint d = Hash(rx, ry, rz, 1000 * (i + 1) + step) % 6;
                    switch (d)
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
            if (only < 0)
            {
                if (cache.Count > 50000) cache.Clear();
                cache[key] = cells;
            }
            return cells;
        }

        /// <summary>Forget cached veins (after the rates change).</summary>
        public static void Reset() => cache.Clear();

        /// <summary>The ore in a stone cell (depth: blocks from the nearest open space; below: blocks under the surface
        /// straight above).</summary>
        public static GroundRules.Ore OreAt(int x, int y, int z, float depth, float below) => OreAt(x, y, z, depth, below, -1);

        static GroundRules.Ore OreAt(int x, int y, int z, float depth, float below, int only)
        {
            var cells = RegionCells(FloorDiv(x, Region), FloorDiv(y, Region), FloorDiv(z, Region), only);
            if (!cells.TryGetValue((x, y, z), out int i)) return GroundRules.Ore.None;
            var k = Kinds[i];
            return depth >= k.MinDepth && below >= k.MinBelow ? k.Ore : GroundRules.Ore.None;
        }

        // ------------------------------------------------------------------ how much a player finds
        /// <summary>
        /// Average ore blocks of one kind a player gets from <paramref name="seconds"/> of branch mining (a 1x2 tunnel,
        /// mining every vein that shows in its walls, floor or ceiling, whole), at a depth where that ore occurs.
        /// <paramref name="stoneSeconds"/> per stone block mined, <paramref name="oreSeconds"/> per ore block.
        /// </summary>
        public static float Yield(int kindIndex, float seconds, float stoneSeconds, float oreSeconds, int runs = 120, int seed = 0)
        {
            var k = Kinds[kindIndex];
            float depth = Math.Max(k.MinDepth, 10f), below = Math.Max(k.MinBelow, 40f);
            long total = 0;
            var taken = new HashSet<(int, int, int)>();
            var stack = new Stack<(int, int, int)>();
            for (int run = 0; run < runs; run++)
            {
                taken.Clear();
                int x = run * 977 + 13 + seed * 100003, y = -40 - (run % 9) * 11 - seed * 37, z = run * 389 - 7000 + seed * 7919;
                float t = 0f;
                int got = 0;
                while (t < seconds)
                {
                    // the tunnel's next two blocks
                    for (int dy = 0; dy < 2; dy++)
                    {
                        var c = (x, y + dy, z);
                        if (taken.Contains(c)) continue;
                        if (OreAt(x, y + dy, z, depth, below, kindIndex) != GroundRules.Ore.None) { got += Mine(c); }
                        else t += stoneSeconds;
                    }
                    // what that opened up: veins showing around it get mined out whole
                    foreach (var (dx, dy, dz) in new[] { (0, -1, 0), (0, 2, 0), (0, 0, 1), (0, 0, -1), (0, 1, 1), (0, 1, -1) })
                    {
                        var c = (x + dx, y + dy, z + dz);
                        if (!taken.Contains(c) && OreAt(c.Item1, c.Item2, c.Item3, depth, below, kindIndex) != GroundRules.Ore.None) got += Mine(c);
                    }
                    x++;
                }
                total += got;

                int Mine((int, int, int) start)
                {
                    int n = 0;
                    stack.Clear(); stack.Push(start); taken.Add(start);
                    while (stack.Count > 0)
                    {
                        var (cx, cy, cz) = stack.Pop();
                        n++;
                        t += oreSeconds + stoneSeconds; // (and about a block of stone to get at it)
                        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                        {
                            var nb = (cx + dx, cy + dy, cz + dz);
                            if (taken.Contains(nb) || OreAt(nb.Item1, nb.Item2, nb.Item3, depth, below, kindIndex) == GroundRules.Ore.None) continue;
                            taken.Add(nb); stack.Push(nb);
                        }
                    }
                    return n;
                }
            }
            return total / (float)runs;
        }

        /// <summary>Sets each kind's vein chance so half a moon of mining yields its PerHalfMoon (on average).</summary>
        public static void Calibrate(float seconds, float stoneSeconds, float oreSeconds)
        {
            for (int i = 0; i < Kinds.Count; i++)
            {
                var k = Kinds[i];
                if (k.PerHalfMoon <= 0f) { k.Chance = 0f; continue; }
                k.Chance = 0.02f;
                for (int pass = 0; pass < 3; pass++)
                {
                    float y = Yield(i, seconds, stoneSeconds, oreSeconds);
                    if (y <= 0.001f) { k.Chance = Math.Min(1f, k.Chance * 4f); continue; }
                    k.Chance = Math.Min(1f, k.Chance * k.PerHalfMoon / y);
                }
            }
            Reset();
        }
    }
}
