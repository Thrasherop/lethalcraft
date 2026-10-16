using System;
using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's trees grown from a sapling (#223), as its TreeFeature places them (pure: the unit tests cover it): the
    /// tree's height (TrunkPlacer.getTreeHeight), the room it needs (its FeatureSize, TreeFeature.getMaxFreeTreeHeight),
    /// its trunk, its foliage at each of the trunk's attachments, and for mangroves its roots and decorations.
    /// - oak (TreeGrower: 9 in 10 the oak, 1 in 10 the fancy oak): StraightTrunkPlacer(4, 2, 0), BlobFoliagePlacer(2, 0, 3);
    ///   FancyTrunkPlacer(3, 11, 0), FancyFoliagePlacer(2, 4, 4)
    /// - birch: StraightTrunkPlacer(5, 2, 0), BlobFoliagePlacer(2, 0, 3)
    /// - dark oak, from four saplings in a square: DarkOakTrunkPlacer(6, 2, 1), DarkOakFoliagePlacer(0, 0)
    /// - mangrove (15 the mangrove, 85 the tall one): UpwardsBranchingTrunkPlacer, RandomSpreadFoliagePlacer(3, 0, 2, 70),
    ///   MangroveRootPlacer (roots down to the ground, muddy in mud), hanging vines (0.125) and propagules (0.14).
    /// The world answers what's at a cell; nothing is placed where it isn't free (air, or what trees replace: leaves,
    /// saplings, vines, plants, water).
    /// </summary>
    public static class TreeGen
    {
        public enum Cell : byte { Air, Replaceable, Log, Mud, Solid }
        public enum Part : byte { Log, LogX, LogZ, Leaves, Roots, MuddyRoots, Vine, Propagule }
        public struct Placed { public Vector3Int Pos; public Part Part; public byte Facing; }

        class Ctx
        {
            public Func<Vector3Int, Cell> At;
            public System.Random R;
            public readonly Dictionary<Vector3Int, Placed> Out = new Dictionary<Vector3Int, Placed>();
            public readonly List<Vector3Int> Leaves = new List<Vector3Int>();
            public Cell Get(Vector3Int p) => Out.TryGetValue(p, out var o) ? (o.Part == Part.Leaves || o.Part == Part.Vine || o.Part == Part.Propagule ? Cell.Replaceable : Cell.Log) : At(p);
            public bool ValidTreePos(Vector3Int p) { var c = Get(p); return c == Cell.Air || c == Cell.Replaceable; }
            public bool IsFree(Vector3Int p) { var c = Get(p); return c == Cell.Air || c == Cell.Replaceable || c == Cell.Log; }
            public int Next(int n) => n <= 0 ? 0 : R.Next(n);
            public float F() => (float)R.NextDouble();
            public void Log(Vector3Int p, Part axis = Part.Log) { if (ValidTreePos(p)) Out[p] = new Placed { Pos = p, Part = axis }; }
            public void Leaf(Vector3Int p) { if (ValidTreePos(p) && !Out.ContainsKey(p)) { Out[p] = new Placed { Pos = p, Part = Part.Leaves }; Leaves.Add(p); } }
        }

        struct Attachment { public Vector3Int Pos; public int RadiusOffset; public bool DoubleTrunk; public Attachment(Vector3Int p, int r, bool d) { Pos = p; RadiusOffset = r; DoubleTrunk = d; } }

        /// <summary>The blocks of the tree a sapling of this wood grows into at `origin` (its sapling's cell; for dark oak the
        /// square's north-west sapling), or null when there's no room (Minecraft's: it doesn't grow, and tries again).</summary>
        public static List<Placed> Grow(string wood, Vector3Int origin, System.Random rnd, Func<Vector3Int, Cell> at)
        {
            var c = new Ctx { At = at, R = rnd };
            bool ok;
            switch (wood)
            {
                case "oak": ok = rnd.Next(10) == 0 ? Fancy(c, origin) : StraightBlob(c, origin, 4, 2, 0); break;
                case "birch": ok = StraightBlob(c, origin, 5, 2, 0); break;
                case "dark_oak": ok = DarkOak(c, origin); break;
                case "mangrove": ok = rnd.Next(100) < 85 ? Mangrove(c, origin, true) : Mangrove(c, origin, false); break;
                default: ok = false; break;
            }
            return ok ? new List<Placed>(c.Out.Values) : null;
        }

        static int Height(Ctx c, int b, int a1, int a2) => b + c.Next(a1 + 1) + c.Next(a2 + 1);

        /// <summary>TreeFeature.getMaxFreeTreeHeight with a feature size: the radius to keep free at each height.</summary>
        static int MaxFree(Ctx c, int treeHeight, Vector3Int pos, Func<int, int> sizeAt, bool doubleTrunk = false)
        {
            for (int y = 0; y <= treeHeight + 1; y++)
            {
                int r = sizeAt(y);
                for (int x = -r; x <= r + (doubleTrunk ? 1 : 0); x++)
                    for (int z = -r; z <= r + (doubleTrunk ? 1 : 0); z++)
                        if (!c.IsFree(pos + new Vector3Int(x, y, z))) return y - 2;
            }
            return treeHeight;
        }

        // ------------------------------------------------------------------ oak and birch: straight trunk, blob foliage
        static bool StraightBlob(Ctx c, Vector3Int o, int b, int a1, int a2)
        {
            int h = Height(c, b, a1, a2);
            // (TwoLayersFeatureSize(1, 0, 1): the trunk's own column at the bottom, a 3x3 above)
            if (MaxFree(c, h, o, y => y < 1 ? 0 : 1) < h) return false;
            for (int y = 0; y < h; y++) c.Log(o + new Vector3Int(0, y, 0));
            Blob(c, new Attachment(o + new Vector3Int(0, h, 0), 0, false), 2, 3, 0);
            return true;
        }

        static void Blob(Ctx c, Attachment a, int leafRadius, int foliageHeight, int offset)
        {
            int fh = foliageHeight + 0;
            for (int yo = offset; yo >= offset - fh; yo--)
            {
                int r = Math.Max(leafRadius + a.RadiusOffset - 1 - yo / 2, 0);
                Row(c, a.Pos, r, yo, a.DoubleTrunk, (dx, y, dz, cr) => dx == cr && dz == cr && (c.Next(2) == 0 || y == 0));
            }
        }

        /// <summary>FoliagePlacer.placeLeavesRow: a square of radius r at height y (one more each way for a 2x2 trunk), less
        /// what the placer skips (it's given the distances from the trunk).</summary>
        static void Row(Ctx c, Vector3Int origin, int r, int y, bool doubleTrunk, Func<int, int, int, int, bool> skip, Func<int, int, bool> skipSigned = null)
        {
            int off = doubleTrunk ? 1 : 0;
            for (int dx = -r; dx <= r + off; dx++)
                for (int dz = -r; dz <= r + off; dz++)
                {
                    if (skipSigned != null && skipSigned(dx, dz)) continue;
                    int mx = doubleTrunk ? Math.Min(Math.Abs(dx), Math.Abs(dx - 1)) : Math.Abs(dx);
                    int mz = doubleTrunk ? Math.Min(Math.Abs(dz), Math.Abs(dz - 1)) : Math.Abs(dz);
                    if (!skip(mx, y, mz, r)) c.Leaf(origin + new Vector3Int(dx, y, dz));
                }
        }

        // ------------------------------------------------------------------ the fancy oak
        static bool Fancy(Ctx c, Vector3Int o)
        {
            int treeHeight = Height(c, 3, 11, 0);
            // (TwoLayersFeatureSize(0, 0, 0, min 4): only its own column; a shorter tree if that's all the room, from 4)
            int clipped = MaxFree(c, treeHeight, o, y => 0);
            if (clipped < treeHeight && clipped < 4) return false;
            treeHeight = clipped;
            int height = treeHeight + 2;
            int trunkHeight = (int)Math.Floor(height * 0.618);
            int clustersPerY = Math.Min(1, (int)Math.Floor(1.382 + Math.Pow(1.0 * height / 13.0, 2.0)));
            int trunkTop = o.y + trunkHeight;
            int relY = height - 5;
            var foliage = new List<(Vector3Int pos, int branchBase)> { (o + new Vector3Int(0, relY, 0), trunkTop) };
            for (; relY >= 0; relY--)
            {
                float shape = FancyShape(height, relY);
                if (shape < 0f) continue;
                for (int i = 0; i < clustersPerY; i++)
                {
                    double radius = 1.0 * shape * (c.F() + 0.328);
                    double angle = c.F() * 2.0 * Math.PI;
                    double x = radius * Math.Sin(angle) + 0.5, z = radius * Math.Cos(angle) + 0.5;
                    var start = o + new Vector3Int((int)Math.Floor(x), relY - 1, (int)Math.Floor(z));
                    var end = start + new Vector3Int(0, 5, 0);
                    if (!Limb(c, start, end, false)) continue;
                    int dx = o.x - start.x, dz = o.z - start.z;
                    double branchHeight = start.y - Math.Sqrt(dx * dx + dz * dz) * 0.381;
                    int branchTop = branchHeight > trunkTop ? trunkTop : (int)branchHeight;
                    var baseAt = new Vector3Int(o.x, branchTop, o.z);
                    if (Limb(c, baseAt, start, false)) foliage.Add((start, baseAt.y));
                }
            }
            Limb(c, o, o + new Vector3Int(0, trunkHeight, 0), true);
            foreach (var (pos, bb) in foliage)
            {
                var baseAt = new Vector3Int(o.x, bb, o.z);
                if (baseAt != pos && bb - o.y >= height * 0.2) Limb(c, baseAt, pos, true);
            }
            // (FancyFoliagePlacer(2, 4, 4): five layers from 4 above each cluster down to it, 2, 3, 3, 3, 2 round)
            foreach (var (pos, bb) in foliage)
            {
                if (bb - o.y < height * 0.2) continue;
                for (int yo = 4; yo >= 0; yo--)
                {
                    int r = 2 + (yo != 4 && yo != 0 ? 1 : 0);
                    Row(c, pos, r, yo, false, (ax, y, az, cr) => (ax + 0.5f) * (ax + 0.5f) + (az + 0.5f) * (az + 0.5f) > cr * cr);
                }
            }
            return true;
        }

        static float FancyShape(int height, int y)
        {
            if (y < height * 0.3f) return -1f;
            float radius = height / 2f, adjacent = radius - y;
            float distance = (float)Math.Sqrt(radius * radius - adjacent * adjacent);
            if (adjacent == 0f) distance = radius;
            else if (Math.Abs(adjacent) >= radius) return 0f;
            return distance * 0.5f;
        }

        /// <summary>FancyTrunkPlacer.makeLimb: a line of logs (turned along it), or only checks the way is free.</summary>
        static bool Limb(Ctx c, Vector3Int a, Vector3Int b, bool place)
        {
            if (!place && a == b) return true;
            var d = b - a;
            int steps = Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z)));
            float sx = steps == 0 ? 0 : (float)d.x / steps, sy = steps == 0 ? 0 : (float)d.y / steps, sz = steps == 0 ? 0 : (float)d.z / steps;
            for (int i = 0; i <= steps; i++)
            {
                var p = a + new Vector3Int((int)Math.Floor(0.5f + i * sx), (int)Math.Floor(0.5f + i * sy), (int)Math.Floor(0.5f + i * sz));
                if (place)
                {
                    int xd = Math.Abs(p.x - a.x), zd = Math.Abs(p.z - a.z), md = Math.Max(xd, zd);
                    c.Log(p, md > 0 ? (xd == md ? Part.LogX : Part.LogZ) : Part.Log);
                }
                else if (!c.IsFree(p)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ dark oak: a 2x2 trunk leaning at the top, branches
        static bool DarkOak(Ctx c, Vector3Int o)
        {
            int h = Height(c, 6, 2, 1);
            // (ThreeLayersFeatureSize(1, 1, 0, 1, 2): nothing round the trunk at its foot, 1 up its length, 2 at the top)
            if (MaxFree(c, h, o, y => y < 1 ? 0 : y >= h - 1 ? 2 : 1, true) < h) return false;
            var dirs = new[] { new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0) };
            var lean = dirs[c.Next(4)];
            int leanHeight = h - c.Next(4), leanSteps = 2 - c.Next(3);
            int tx = o.x, tz = o.z, ey = o.y + h - 1;
            for (int y = 0; y < h; y++)
            {
                if (y >= leanHeight && leanSteps > 0) { tx += lean.x; tz += lean.z; leanSteps--; }
                var p = new Vector3Int(tx, o.y + y, tz);
                var cell = c.Get(p);
                if (cell == Cell.Air || cell == Cell.Replaceable)
                    foreach (var q in new[] { p, p + new Vector3Int(1, 0, 0), p + new Vector3Int(0, 0, 1), p + new Vector3Int(1, 0, 1) }) c.Log(q);
            }
            var atts = new List<Attachment> { new Attachment(new Vector3Int(tx, ey, tz), 0, true) };
            for (int ox = -1; ox <= 2; ox++)
                for (int oz = -1; oz <= 2; oz++)
                    if ((ox < 0 || ox > 1 || oz < 0 || oz > 1) && c.Next(3) <= 0)
                    {
                        int len = c.Next(3) + 2;
                        for (int by = 0; by < len; by++) c.Log(new Vector3Int(o.x + ox, ey - by - 1, o.z + oz));
                        atts.Add(new Attachment(new Vector3Int(o.x + ox, ey, o.z + oz), 0, false));
                    }
            foreach (var a in atts)
            {
                if (a.DoubleTrunk)
                {
                    // (DarkOakFoliagePlacer.shouldSkipLocationSigned: the middle row's corners, taken by their signed offsets)
                    Row(c, a.Pos, 2, -1, true, DarkSkip(true)); Row(c, a.Pos, 3, 0, true, DarkSkip(true), (dx, dz) => (dx == -3 || dx >= 3) && (dz == -3 || dz >= 3)); Row(c, a.Pos, 2, 1, true, DarkSkip(true));
                    if (c.Next(2) == 0) Row(c, a.Pos, 0, 2, true, DarkSkip(true));
                }
                else { Row(c, a.Pos, 2, -1, false, DarkSkip(false)); Row(c, a.Pos, 1, 0, false, DarkSkip(false)); }
            }
            return true;
        }

        /// <summary>DarkOakFoliagePlacer.shouldSkipLocation (given the distances from the trunk).</summary>
        static Func<int, int, int, int, bool> DarkSkip(bool doubleTrunk) => (dx, y, dz, r) =>
        {
            if (y == -1 && !doubleTrunk) return dx == r && dz == r;
            if (y == 1) return dx + dz > r * 2 - 2;
            return false;
        };

        // ------------------------------------------------------------------ mangrove: on roots, branching upward, a loose crown
        static bool Mangrove(Ctx c, Vector3Int o, bool tall)
        {
            int h = tall ? Height(c, 4, 1, 9) : Height(c, 2, 1, 4);
            int rootsUp = tall ? 3 + c.Next(5) : 1 + c.Next(3);
            var trunk = o + new Vector3Int(0, rootsUp, 0);
            if (MaxFree(c, h, trunk, y => y < (tall ? 3 : 2) ? 0 : 2) < h) return false;
            if (!MangroveRoots(c, o, trunk)) return false;
            var atts = new List<Attachment>();
            int stepsMax = tall ? 6 : 4;
            for (int i = 0; i < h; i++)
            {
                var p = trunk + new Vector3Int(0, i, 0);
                bool placed = c.ValidTreePos(p) || c.Get(p) == Cell.Mud;
                if (placed) c.Log(p);
                if (placed && i < h - 1 && c.F() < 0.5f)
                {
                    var dirs = new[] { new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0) };
                    var dir = dirs[c.Next(4)];
                    int len = c.Next(2), pos = Math.Max(0, len - c.Next(2) - 1), steps = 1 + c.Next(stepsMax);
                    int along = trunk.y + i + pos, lx = p.x, lz = p.z;
                    for (int bi = pos; bi < h && steps > 0; steps--, bi++)
                    {
                        if (bi < 1) continue;
                        int ph = trunk.y + i + bi;
                        lx += dir.x; lz += dir.z; along = ph;
                        var bp = new Vector3Int(lx, ph, lz);
                        if (c.ValidTreePos(bp)) { c.Log(bp); along = ph + 1; }
                        atts.Add(new Attachment(bp, 0, false));
                    }
                    if (along - (trunk.y + i) > 1)
                    {
                        var fp = new Vector3Int(lx, along, lz);
                        atts.Add(new Attachment(fp, 0, false)); atts.Add(new Attachment(fp - new Vector3Int(0, 2, 0), 0, false));
                    }
                }
                if (i == h - 1) atts.Add(new Attachment(trunk + new Vector3Int(0, h, 0), 0, false));
            }
            // RandomSpreadFoliagePlacer(3, 0, 2, 70): 70 tries each, within 3 across and 2 up or down
            foreach (var a in atts)
                for (int t = 0; t < 70; t++)
                    c.Leaf(a.Pos + new Vector3Int(c.Next(3) - c.Next(3), c.Next(2) - c.Next(2), c.Next(3) - c.Next(3)));
            // decorations: hanging vines (LeaveVineDecorator 0.125), hanging propagules (AttachedToLeavesDecorator 0.14)
            // (a vine faces away from the leaf it hangs on: the mod's faces, North +z, South -z, West -x, East +x)
            var side = new[] { (new Vector3Int(-1, 0, 0), (byte)4), (new Vector3Int(1, 0, 0), (byte)5), (new Vector3Int(0, 0, -1), (byte)3), (new Vector3Int(0, 0, 1), (byte)2) };
            foreach (var lp in c.Leaves.ToArray())
                foreach (var (d, facing) in side)
                    if (c.F() < 0.125f && c.Get(lp + d) == Cell.Air)
                    {
                        var vp = lp + d;
                        for (int n = 0; n <= 4 && c.Get(vp) == Cell.Air; n++, vp += Vector3Int.down) c.Out[vp] = new Placed { Pos = vp, Part = Part.Vine, Facing = facing };
                    }
            var taken = new HashSet<Vector3Int>();
            var order = c.Leaves.ToArray();
            for (int i = order.Length - 1; i > 0; i--) { int j = c.Next(i + 1); var tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            foreach (var lp in order)
            {
                var pp = lp + Vector3Int.down;
                if (taken.Contains(pp) || !(c.F() < 0.14f)) continue;
                if (c.Get(pp) != Cell.Air || c.Get(pp + Vector3Int.down) != Cell.Air) continue;
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) taken.Add(pp + new Vector3Int(x, 0, z));
                c.Out[pp] = new Placed { Pos = pp, Part = Part.Propagule };
            }
            return true;
        }

        /// <summary>MangroveRootPlacer.placeRoots: the column under the trunk, then from each side of the trunk's foot roots
        /// out and down until the ground stops them (no more than 15 long, 8 out); muddy roots in mud.</summary>
        static bool MangroveRoots(Ctx c, Vector3Int origin, Vector3Int trunk)
        {
            bool CanRoot(Vector3Int p) { var cell = c.Get(p); return cell == Cell.Air || cell == Cell.Replaceable || cell == Cell.Mud; }
            for (var p = origin; p.y < trunk.y; p += Vector3Int.up) if (!CanRoot(p)) return false;
            var roots = new List<Vector3Int> { trunk + Vector3Int.down };
            var dirs = new[] { new Vector3Int(0, 0, -1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0) };
            foreach (var dir in dirs)
            {
                var start = trunk + dir;
                var found = new List<Vector3Int>();
                if (!Simulate(start, dir, found, 0)) return false;
                roots.AddRange(found);
                roots.Add(start);
            }
            foreach (var r in roots)
            {
                var cell = c.Get(r);
                if (cell == Cell.Mud) c.Out[r] = new Placed { Pos = r, Part = Part.MuddyRoots };
                else if (CanRoot(r)) c.Out[r] = new Placed { Pos = r, Part = Part.Roots };
            }
            return true;

            bool Simulate(Vector3Int pos, Vector3Int dir, List<Vector3Int> found, int layer)
            {
                if (layer == 15 || found.Count > 15) return false;
                var below = pos + Vector3Int.down; var next = pos + dir;
                int width = Math.Abs(pos.x - trunk.x) + Math.Abs(pos.y - trunk.y) + Math.Abs(pos.z - trunk.z);
                List<Vector3Int> options;
                if (width > 8 - 3 && width <= 8) options = c.F() < 0.2f ? new List<Vector3Int> { below, next + Vector3Int.down } : new List<Vector3Int> { below };
                else if (width > 8) options = new List<Vector3Int> { below };
                else if (c.F() < 0.2f) options = new List<Vector3Int> { below };
                else options = c.Next(2) == 0 ? new List<Vector3Int> { next } : new List<Vector3Int> { below };
                foreach (var p in options)
                    if (CanRoot(p))
                    {
                        found.Add(p);
                        if (!Simulate(p, dir, found, layer + 1)) return false;
                    }
                return true;
            }
        }
    }
}
