using System;
using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Amethyst geodes (#229), pure (no engine calls; the offline unit tests cover it): Minecraft's GeodeFeature with the
    /// amethyst_geode configuration, reimplemented. 3-4 distribution points 4-6 blocks off an origin, each with an offset of
    /// 1-2; a cell's sum of 1/sqrt(distance^2 + offset) over the points, plus a little noise, sorts it into the hollow (air),
    /// the inner layer (amethyst, budding amethyst one time in 12: 0.083), the middle layer (calcite) and the outer layer
    /// (smooth basalt), at Minecraft's thresholds (GeodeLayerSettings 1.7 / 2.2 / 3.2 / 4.2); 95% of geodes get a crack of
    /// air through the shell (GeodeCrackSettings); 35% of budding blocks start with a bud or cluster (one of the four, even
    /// odds) on the first open side, in Minecraft's direction order. Cells are scanned in Minecraft's order (x, then y,
    /// then z), so the draws come in the same order as its.
    /// The ground is cut into 32-block regions; each may hold one geode, centered so the whole of it stays in the region
    /// (no overlaps: a cell asks one region). How often a region holds one is set so half a moon of branch mining at a
    /// depth where they occur finds <see cref="PerHalfMoon"/> of them (the unit tests check it), the way ore veins are
    /// calibrated (GroundVeins): Minecraft's 1 in 24 chunks between y -58 and 30 would almost never be met in the mod's
    /// short half-moon of digging. Whether a geode can really be there (deep enough, nothing of the level's own geometry
    /// near it) is the host's check (Geodes): this only says where they would be.
    /// </summary>
    public static class GeodeGen
    {
        public enum Layer : byte { None, Air, Amethyst, Budding, Calcite, Basalt }

        public const int Region = 32;
        /// <summary>How far from its center a geode may reach (Minecraft scans 16 off its origin; its shells reach 9-12 from
        /// the points: beyond this the outer layer is cut flat, which only happens with the noise at its highest).</summary>
        public const int Reach = 12;
        const int G = Reach * 2 + 1;

        /// <summary>Geodes half a moon of branch mining finds, where they occur (about one in four half moons).</summary>
        public const float PerHalfMoon = 0.25f;
        /// <summary>Chance a region holds a geode (from <see cref="PerHalfMoon"/>, the unit tests' simulation).</summary>
        public static float Chance = 0.49f;
        /// <summary>Blocks under the surface straight above the geode's middle (the host checks it): about where lapis and lava
        /// pockets start, as Minecraft's sit below y 30, well under its ground.</summary>
        public const float MinBelow = 24f;

        // Minecraft's amethyst_geode settings
        const double Filling = 1.7, InnerLayer = 2.2, MiddleLayer = 3.2, OuterLayer = 4.2;
        const double BaseCrackSize = 2.0, CrackChance = 0.95; const int CrackPointOffset = 2;
        const double BuddingChance = 0.083, PlacementChance = 0.35, NoiseMultiplier = 0.05;
        const int WallMin = 4, WallMax = 6;
        /// <summary>Minecraft's Direction order (down, up, north -z, south +z, west -x, east +x) as the mod's faces.</summary>
        public static readonly byte[] DirectionOrder = { 0, 1, 3, 2, 4, 5 };
        static readonly int[,] FaceDir = { { 0, -1, 0 }, { 0, 1, 0 }, { 0, 0, 1 }, { 0, 0, -1 }, { -1, 0, 0 }, { 1, 0, 0 } }; // (Faces.Dir)

        public struct Bud
        {
            public int X, Y, Z;
            /// <summary>The face it grows out of its budding block toward (a mod Face: Down 0, Up 1, North 2 +z...).</summary>
            public byte Face;
            /// <summary>0 small bud, 1 medium, 2 large, 3 cluster.</summary>
            public byte Stage;
        }

        public sealed class Geode
        {
            public int RX, RY, RZ;
            /// <summary>Its middle (the origin + 5, between the points).</summary>
            public int CX, CY, CZ;
            public int Points;
            public bool Cracked;
            internal int MinX, MinY, MinZ;
            internal byte[] Grid = new byte[G * G * G];
            /// <summary>Its air: the hollow and the crack.</summary>
            public readonly List<(int x, int y, int z)> Hollow = new List<(int, int, int)>();
            public readonly List<Bud> Buds = new List<Bud>();

            public Layer At(int x, int y, int z)
            {
                int i = x - MinX, j = y - MinY, k = z - MinZ;
                if (i < 0 || j < 0 || k < 0 || i >= G || j >= G || k >= G) return Layer.None;
                return (Layer)Grid[(k * G + j) * G + i];
            }

            internal void Set(int x, int y, int z, Layer l) => Grid[((z - MinZ) * G + (y - MinY)) * G + (x - MinX)] = (byte)l;

            /// <summary>(tests) How many of its cells are each layer.</summary>
            public int Count(Layer l) { int n = 0; foreach (var b in Grid) if (b == (byte)l) n++; return n; }
        }

        // ------------------------------------------------------------------ hashing
        static uint Hash(int x, int y, int z, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 0x27d4eb2d) ^ 0x2545F491u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return h;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>A small deterministic generator (splitmix64) for one geode's draws.</summary>
        sealed class Rng
        {
            ulong s;
            public Rng(ulong seed) { s = seed; }
            ulong Next64()
            {
                s += 0x9E3779B97F4A7C15UL;
                ulong z = s;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            public int Next(int n) => (int)(Next64() % (ulong)n);
            public float NextFloat() => (Next64() >> 40) / 16777216f;
            public double NextDouble() => (Next64() >> 11) * (1.0 / 9007199254740992.0);
            /// <summary>Minecraft's UniformInt.of(min, max).</summary>
            public int Uniform(int min, int max) => min + Next(max - min + 1);
        }

        /// <summary>Smooth noise in [-1, 1] at Minecraft's geode noise scale (its first octave -4: a feature every 16
        /// blocks), the same for every geode of a moon.</summary>
        public static float Noise(int x, int y, int z, int salt)
        {
            float fx = x / 16f, fy = y / 16f, fz = z / 16f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty); tz = tz * tz * (3f - 2f * tz);
            float L(int i, int j, int k) => (Hash(x0 + i, y0 + j, z0 + k, salt * 31 + 7) & 0xFFFFFF) / 8388608f - 1f;
            float a = Lerp(Lerp(L(0, 0, 0), L(1, 0, 0), tx), Lerp(L(0, 1, 0), L(1, 1, 0), tx), ty);
            float b = Lerp(Lerp(L(0, 0, 1), L(1, 0, 1), tx), Lerp(L(0, 1, 1), L(1, 1, 1), tx), ty);
            return Lerp(a, b, tz);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ------------------------------------------------------------------ where they are
        static readonly Dictionary<(int, int, int, int), Geode> cache = new Dictionary<(int, int, int, int), Geode>();

        /// <summary>The geode of a region (null: none), generated once and kept.</summary>
        public static Geode InRegion(int rx, int ry, int rz, int salt)
        {
            var key = (rx, ry, rz, salt);
            if (cache.TryGetValue(key, out var g)) return g;
            if (cache.Count > 400) cache.Clear(); // (a geode is about 16 KB)
            return cache[key] = Generate(rx, ry, rz, salt, Chance);
        }

        /// <summary>The geode a cell is part of (its shell, its hollow or its crack), or null.</summary>
        public static Geode Containing(int x, int y, int z, int salt)
        {
            var g = InRegion(FloorDiv(x, Region), FloorDiv(y, Region), FloorDiv(z, Region), salt);
            return g != null && g.At(x, y, z) != Layer.None ? g : null;
        }

        public static Layer LayerAt(int x, int y, int z, int salt) => Containing(x, y, z, salt)?.At(x, y, z) ?? Layer.None;

        public static void Reset() => cache.Clear();

        // ------------------------------------------------------------------ GeodeFeature.place
        /// <summary>A region's geode, if it has one (chance), made as Minecraft's GeodeFeature.place makes it.</summary>
        public static Geode Generate(int rx, int ry, int rz, int salt, float chance)
        {
            if ((Hash(rx, ry, rz, salt * 977 + 101) & 0xFFFFFF) / 16777216f >= chance) return null;
            var rng = new Rng(((ulong)Hash(rx, ry, rz, salt * 977 + 202) << 32) | Hash(rx, ry, rz, salt * 977 + 303));
            // the origin: its points 4-6 off it, the middle at +5; placed so the middle's 12 blocks either way stay in the region
            int ox = rx * Region + 7 + rng.Next(8), oy = ry * Region + 7 + rng.Next(8), oz = rz * Region + 7 + rng.Next(8);
            var g = new Geode { RX = rx, RY = ry, RZ = rz, CX = ox + 5, CY = oy + 5, CZ = oz + 5 };
            g.MinX = g.CX - Reach; g.MinY = g.CY - Reach; g.MinZ = g.CZ - Reach;

            int numPoints = rng.Uniform(3, 4);
            g.Points = numPoints;
            double crackSizeAdjustment = numPoints / (double)WallMax;
            double innerAir = 1.0 / Math.Sqrt(Filling);
            double innermostBlockLayer = 1.0 / Math.Sqrt(InnerLayer + crackSizeAdjustment);
            double innerCrust = 1.0 / Math.Sqrt(MiddleLayer + crackSizeAdjustment);
            double outerCrust = 1.0 / Math.Sqrt(OuterLayer + crackSizeAdjustment);
            double crackSize = 1.0 / Math.Sqrt(BaseCrackSize + rng.NextDouble() / 2.0 + (numPoints > 3 ? crackSizeAdjustment : 0.0));
            bool crack = rng.NextFloat() < CrackChance;
            g.Cracked = crack;
            var pts = new (int x, int y, int z, int off)[numPoints];
            for (int i = 0; i < numPoints; i++)
                pts[i] = (ox + rng.Uniform(WallMin, WallMax), oy + rng.Uniform(WallMin, WallMax), oz + rng.Uniform(WallMin, WallMax), rng.Uniform(1, 2));
            var cracks = new List<(int x, int y, int z)>();
            if (crack)
            {
                int which = rng.Next(4), co = numPoints * 2 + 1;
                foreach (int dy in new[] { 7, 5, 1 })
                    cracks.Add(which == 0 ? (ox + co, oy + dy, oz) : which == 1 ? (ox, oy + dy, oz + co) : which == 2 ? (ox + co, oy + dy, oz + co) : (ox, oy + dy, oz));
            }

            var potential = new List<(int x, int y, int z)>();
            for (int z = g.MinZ; z < g.MinZ + G; z++)
                for (int y = g.MinY; y < g.MinY + G; y++)
                    for (int x = g.MinX; x < g.MinX + G; x++)
                    {
                        double noise = Noise(x, y, z, salt) * NoiseMultiplier;
                        double shell = 0.0;
                        foreach (var p in pts) shell += 1.0 / Math.Sqrt(Sq(x - p.x) + Sq(y - p.y) + Sq(z - p.z) + p.off) + noise;
                        if (shell < outerCrust) continue;
                        if (shell >= innerAir) { g.Set(x, y, z, Layer.Air); continue; }
                        double crackSum = 0.0;
                        foreach (var c in cracks) crackSum += 1.0 / Math.Sqrt(Sq(x - c.x) + Sq(y - c.y) + Sq(z - c.z) + CrackPointOffset) + noise;
                        if (crack && crackSum >= crackSize) g.Set(x, y, z, Layer.Air);
                        else if (shell >= innermostBlockLayer)
                        {
                            bool budding = rng.NextFloat() < BuddingChance;
                            g.Set(x, y, z, budding ? Layer.Budding : Layer.Amethyst);
                            if (budding && rng.NextFloat() < PlacementChance) potential.Add((x, y, z));
                        }
                        else if (shell >= innerCrust) g.Set(x, y, z, Layer.Calcite);
                        else g.Set(x, y, z, Layer.Basalt);
                    }
            // the buds: on each chosen budding block, one of the four, on the first side still open (in Direction order)
            var taken = new HashSet<(int, int, int)>();
            foreach (var (x, y, z) in potential)
            {
                byte stage = (byte)rng.Next(4);
                foreach (byte f in DirectionOrder)
                {
                    int nx = x + FaceDir[f, 0], ny = y + FaceDir[f, 1], nz = z + FaceDir[f, 2];
                    if (g.At(nx, ny, nz) != Layer.Air || taken.Contains((nx, ny, nz))) continue;
                    taken.Add((nx, ny, nz));
                    g.Buds.Add(new Bud { X = nx, Y = ny, Z = nz, Face = f, Stage = stage });
                    break;
                }
            }
            for (int z = g.MinZ; z < g.MinZ + G; z++)
                for (int y = g.MinY; y < g.MinY + G; y++)
                    for (int x = g.MinX; x < g.MinX + G; x++)
                        if (g.At(x, y, z) == Layer.Air) g.Hollow.Add((x, y, z));
            return g;
        }

        static double Sq(int d) => d * d;

        // ------------------------------------------------------------------ budding amethyst (BuddingAmethystBlock.randomTick)
        /// <summary>Minecraft's random tick rate: 3 blocks of a 16x16x16 section a game tick, so each block about every
        /// 4096 / 3 ticks (68 s).</summary>
        public const float RandomTickSeconds = 4096f / 3f / 20f;
        public const int GrowthChance = 5;

        /// <summary>What a random tick grows on the side it picked: -1 nothing; else the stage there after it (0 a new small
        /// bud on open air, or the bud already there one stage on if it grows out of this block).</summary>
        public static int Grow(bool open, int stageThere, bool facesThisWay)
        {
            if (open) return 0;
            if (stageThere >= 0 && stageThere < 3 && facesThisWay) return stageThere + 1;
            return -1;
        }

        // ------------------------------------------------------------------ how often they're found
        /// <summary>
        /// Geodes found on average in <paramref name="seconds"/> of branch mining (a 1x2 tunnel; a geode counts as found
        /// once any of its cells shows in the tunnel's walls, floor or ceiling) at a depth where they occur, with a region
        /// chance of <paramref name="chance"/>. <paramref name="stoneSeconds"/> per block mined.
        /// </summary>
        public static float FoundIn(float seconds, float stoneSeconds, float chance, int runs = 300, int seed = 0)
        {
            var regions = new Dictionary<(int, int, int), Geode>();
            Geode At(int x, int y, int z)
            {
                var k = (FloorDiv(x, Region), FloorDiv(y, Region), FloorDiv(z, Region));
                if (!regions.TryGetValue(k, out var g)) regions[k] = g = Generate(k.Item1, k.Item2, k.Item3, 12345 + seed, chance);
                return g != null && g.At(x, y, z) != Layer.None ? g : null;
            }
            long found = 0;
            var seen = new HashSet<Geode>();
            for (int run = 0; run < runs; run++)
            {
                seen.Clear();
                int x = run * 977 + 13 + seed * 100003, y = -40 - (run % 13) * 7, z = run * 389 - 7000 + seed * 7919;
                for (float t = 0f; t < seconds; t += 2f * stoneSeconds, x++)
                    foreach (var (dy, dz) in new[] { (0, 0), (1, 0), (-1, 0), (2, 0), (0, 1), (0, -1), (1, 1), (1, -1) })
                    {
                        var g = At(x, y + dy, z + dz);
                        if (g != null && seen.Add(g)) found++;
                    }
            }
            return found / (float)runs;
        }
    }
}
