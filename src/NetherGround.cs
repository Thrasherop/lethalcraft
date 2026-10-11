using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The Nether's ground (#66, phase 5): around the fortress (its box and some way past it) everything that isn't the
    /// fortress itself is solid netherrack, so its walls dig like the facility's and you tunnel on through netherrack.
    /// The fortress's own blocks dig to what they are (nether bricks, glowstone, obsidian...). In the netherrack: veins of
    /// nether quartz ore, lava pockets, and very rarely ancient debris, further from the fortress.
    /// </summary>
    public static class NetherGround
    {
        static float S => BlockWorld.S;

        /// <summary>The Nether's region: the fortress's tiles, 10 m out to the sides, from 15 m under them to 20 m over.</summary>
        public static bool InRegion(Vector3 p) => NetherFortress.Root != null && NetherFortress.Region.Contains(p);

        /// <summary>On the region's outermost shell: the Nether's edge, which doesn't dig (bedrock-like).</summary>
        public static bool AtEdge(Vector3 p)
        {
            var b = NetherFortress.Region;
            float e = S * 1.05f;
            return p.x < b.min.x + e || p.x > b.max.x - e || p.y < b.min.y + e || p.y > b.max.y - e || p.z < b.min.z + e || p.z > b.max.z - e;
        }

        /// <summary>The fortress block at a point inside one of its tiles' boxes ("" for open space in a tile; null outside every tile).</summary>
        public static string LayoutAt(Vector3 p)
        {
            foreach (var t in NetherFortress.Placed)
            {
                if (t.Tr == null) continue;
                var l = t.Tr.InverseTransformPoint(p) / S;
                if (l.x < t.Min.x || l.y < t.Min.y || l.z < t.Min.z || l.x > t.Max.x || l.y > t.Max.y || l.z > t.Max.z) continue;
                var cell = l - t.Origin;
                var v = t.Layout[Mathf.FloorToInt(cell.x), Mathf.FloorToInt(cell.y), Mathf.FloorToInt(cell.z)];
                if (v != null) return v;
                // an unused doorway's wall (its panel still there)
                foreach (var b in t.Walls) if (b.Contains(p)) return NetherFortress.WallBlock;
                return "";
            }
            return null;
        }

        /// <summary>Blocks from a point to the nearest tile box (how deep in the netherrack it is).</summary>
        public static float DepthAt(Vector3 p)
        {
            float best = 99f;
            foreach (var t in NetherFortress.Placed)
            {
                if (t.Tr == null) continue;
                var l = t.Tr.InverseTransformPoint(p) / S;
                var q = new Vector3(Mathf.Clamp(l.x, t.Min.x, t.Max.x), Mathf.Clamp(l.y, t.Min.y, t.Max.y), Mathf.Clamp(l.z, t.Min.z, t.Max.z));
                best = Mathf.Min(best, (l - q).magnitude);
            }
            return best;
        }

        public static BlockDef BlockFor(string tile)
        {
            switch (tile)
            {
                case "nether_bricks": case "cracked_nether_bricks": case "spawner": return Blocks.NetherBricks;
                case "netherrack": return Blocks.Netherrack;
                case "polished_blackstone_bricks": case "cracked_polished_blackstone_bricks": return Blocks.PolishedBlackstoneBricks;
                case "blackstone": return Blocks.Blackstone;
                case "gilded_blackstone": return Blocks.GildedBlackstone;
                case "gold_block": return Blocks.GoldBlock;
                case "soul_sand": return Blocks.SoulSand;
                case "soul_soil": return Blocks.SoulSoil;
                case "basalt": return Blocks.Basalt;
                case "bone_block": return Blocks.BoneBlock;
                case "glowstone": return Blocks.Glowstone;
                case "lava": return Blocks.Lava;
                case "obsidian": return Blocks.Obsidian;
                default: return Blocks.Netherrack;
            }
        }

        /// <summary>What a natural cell of the Nether is made of.</summary>
        public static BlockDef Material(Vector3Int c, Vector3 center)
        {
            var lay = LayoutAt(center);
            if (!string.IsNullOrEmpty(lay)) return BlockFor(lay);
            float depth = DepthAt(center);
            return VeinAt(c, depth) ?? Blocks.Netherrack;
        }

        // ------------------------------------------------------------------ veins (deterministic per 4x4x4 region, like the moon's)
        static readonly Dictionary<Vector3Int, Dictionary<Vector3Int, BlockDef>> cache = new Dictionary<Vector3Int, Dictionary<Vector3Int, BlockDef>>();
        public static void Reset() => cache.Clear();

        static BlockDef VeinAt(Vector3Int c, float depth)
        {
            var region = new Vector3Int(Mathf.FloorToInt(c.x / 4f), Mathf.FloorToInt(c.y / 4f), Mathf.FloorToInt(c.z / 4f));
            BlockDef found = null;
            for (int dx = -1; dx <= 1 && found == null; dx++)
                for (int dy = -1; dy <= 1 && found == null; dy++)
                    for (int dz = -1; dz <= 1 && found == null; dz++)
                    {
                        var r = region + new Vector3Int(dx, dy, dz);
                        if (!cache.TryGetValue(r, out var cells)) cache[r] = cells = Veins(r);
                        if (cells.TryGetValue(c, out var d)) found = d;
                    }
            // ancient debris only well away from the fortress (Minecraft's: deep, alone or in pairs)
            if (found == Blocks.AncientDebris && depth < 4f) return Blocks.Netherrack;
            return found;
        }

        static Dictionary<Vector3Int, BlockDef> Veins(Vector3Int r)
        {
            var cells = new Dictionary<Vector3Int, BlockDef>();
            var rng = new System.Random(unchecked(r.x * 73856093 ^ r.y * 19349663 ^ r.z * 83492791 ^ NetherFortress.Seed));
            void Walk(BlockDef d, int size)
            {
                var p = new Vector3Int(r.x * 4 + rng.Next(4), r.y * 4 + rng.Next(4), r.z * 4 + rng.Next(4));
                for (int i = 0; i < size; i++)
                {
                    cells[p] = d;
                    int f = rng.Next(6);
                    p += f == 0 ? Vector3Int.right : f == 1 ? Vector3Int.left : f == 2 ? Vector3Int.up : f == 3 ? Vector3Int.down : f == 4 ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
                }
            }
            double roll = rng.NextDouble();
            if (roll < 0.30) Walk(Blocks.NetherQuartzOre, 4 + rng.Next(7));      // quartz: common
            else if (roll < 0.38) Walk(Blocks.Lava, 3 + rng.Next(5));            // lava pockets: more than overworld
            else if (roll < 0.41) Walk(Blocks.AncientDebris, 1 + rng.Next(2));
            else if (roll < 0.50) Walk(Blocks.SoulSand, 6 + rng.Next(8));       // soul sand: nether wart grows on it (#88)   // ancient debris: rare (about one in 2,000 cells: seen now and then, rarely worth digging for)
            return cells;
        }
    }
}
