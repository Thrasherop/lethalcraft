using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Saplings and growing trees (#223, the owner's), Minecraft's SaplingBlock and TreeGrower: oak, birch and dark oak
    /// saplings and the mangrove propagule, planted on dirt, grass, farmland or mud (a block, or the moon's own ground).
    /// A sapling grows in two stages (Minecraft's random ticks, here about four minutes a stage), or with bone meal (45%
    /// a use, the meal used either way); grown, it becomes its tree (TreeGen), where there's room; dark oak only from
    /// four saplings in a square. Saplings come from chopping the moons' trees, from leaves, and the store.
    /// Leaves a tree grew are natural (state 1): cut off from every log by more than six leaves (Minecraft's distance 7),
    /// they decay, a few seconds apart; placed leaves never do. Leaves broken, or decaying, drop what Minecraft's do:
    /// only shears give the block; otherwise sometimes a sapling (oak, birch 1 in 20, dark oak 1 in 40, never mangrove),
    /// sticks (1 in 50) and an apple (oak and dark oak, 1 in 200).
    /// </summary>
    public static class Saplings
    {
        public const float StageSeconds = 240f;
        public const int Natural = 1; // (a leaves block's state: a tree grew it)
        public const int Hanging = 2; // (a propagule's state: hanging under mangrove leaves)
        public static int TreesGrown, Decayed; // (dev/tests)
        static readonly List<BlockKey> growing = new List<BlockKey>();
        static readonly HashSet<BlockKey> queued = new HashSet<BlockKey>(); // (what's in decayQueue)
        static readonly List<(BlockKey key, float at)> decayQueue = new List<(BlockKey, float)>();
        static BlockWorld W => BlockWorld.Instance;

        public static bool IsSapling(BlockDef d) => d != null && d.Shape == BlockShape.Sapling;
        public static bool IsLeaves(BlockDef d) => d != null && Blocks.Woods.Any(w => w.Leaves == d);

        static bool DirtLike(BlockDef d) => d == Blocks.Dirt || d == Blocks.Grass || d == Blocks.Farmland || d == Blocks.Mud || d == Blocks.MuddyMangroveRoots;

        /// <summary>Can a sapling stand at k: on dirt, grass, farmland or mud (a block, or the moon's own dirt or grass).</summary>
        public static bool CanStand(BlockKey k)
        {
            var w = W;
            if (w == null) return false;
            var below = k.Offset((int)Face.Down);
            var bd = w.DefAt(below);
            if (DirtLike(bd)) return true;
            return bd == null && Ground.MaterialUnder(k) is BlockDef m && (m == Blocks.Dirt || m == Blocks.Grass);
        }

        // ------------------------------------------------------------------ the host: growing
        public static void ServerTick(float dt)
        {
            var w = W;
            if (w == null) return;
            // (Minecraft's random ticks: each second a chance, a stage taking StageSeconds on average, saplings side by
            // side not all at once; looked over without copying the world, grown after)
            growing.Clear();
            foreach (var kv in w.Blocks)
            {
                var d = kv.Value.Data;
                if (IsSapling(d.Def) && (d.State & Hanging) == 0 && Random.value < dt / StageSeconds) growing.Add(kv.Key);
            }
            foreach (var k in growing) Advance(k);
            // leaves due to decay
            for (int i = decayQueue.Count - 1; i >= 0; i--)
            {
                if (decayQueue[i].at > Time.time) continue;
                var k = decayQueue[i].key; decayQueue.RemoveAt(i); queued.Remove(k);
                var b = w.Get(k);
                if (b == null || !IsLeaves(b.Data.Def) || (b.Data.State & Natural) == 0 || NearLog(k)) continue;
                var ops = new List<Op> { Op.Remove(k, true) };
                var below = k.Offset((int)Face.Down);
                var pb = w.Get(below);
                if (pb != null && IsSapling(pb.Data.Def) && (pb.Data.State & Hanging) != 0) ServerLogic.PopOff(below, pb.Data, ops, true); // (a propagule hanging under it falls)
                BlockNet.ServerBroadcastOps(ops);
                ServerLeafDrop(b.Data.Def, w.WorldCenter(k), null);
                Decayed++;
                ServerBlockRemoved(k, b.Data.Def); // (its neighbours may go too)
            }
        }

        /// <summary>Host: a stage on (SaplingBlock.advanceTree): the first, then the tree.</summary>
        static void Advance(BlockKey k)
        {
            var w = W;
            var b = w.Get(k);
            if (b == null || !IsSapling(b.Data.Def)) return;
            if ((b.Data.State & 1) == 0)
            {
                var nd = b.Data; nd.State = (byte)(nd.State | 1);
                BlockNet.ServerBroadcastOps(new List<Op> { Op.State(k, nd) });
                return;
            }
            TryGrow(k, b.Data.Def);
        }

        /// <summary>Host: bone meal on a sapling (a player's or a dispenser's): Minecraft's 45%. True: it was one.</summary>
        public static bool ServerBoneMeal(BlockKey k)
        {
            var b = W?.Get(k);
            if (b == null || !IsSapling(b.Data.Def) || (b.Data.State & Hanging) != 0) return false;
            BlockNet.ServerSound(W.WorldCenter(k), "bonemeal", 1f, 0.8f);
            if (Random.value < 0.45f) Advance(k);
            return true;
        }

        /// <summary>What a cell holds, for the tree: air, something it replaces, a log, mud, or something solid (a block, the
        /// moon's ground, the level's own geometry).</summary>
        static TreeGen.Cell CellAt(BlockKey frameKey, Vector3Int p)
        {
            var w = W;
            var k = new BlockKey(frameKey.Frame, frameKey.YOff, p);
            var d = w.DefAt(k);
            if (d == null)
            {
                if (frameKey.Frame == 0 && Ground.IsUndugGround(k)) return TreeGen.Cell.Solid;
                return ServerLogic.Obstructed(k, 0.8f) ? TreeGen.Cell.Solid : TreeGen.Cell.Air;
            }
            if (IsLeaves(d) || IsSapling(d) || d == Blocks.Vine || d.Shape == BlockShape.Crop || d.Shape == BlockShape.Cane || d == Blocks.Water) return TreeGen.Cell.Replaceable;
            if (Blocks.IsPillar(d)) return TreeGen.Cell.Log;
            if (d == Blocks.Mud || d == Blocks.MuddyMangroveRoots || d == Blocks.MangroveRoots) return TreeGen.Cell.Mud;
            return TreeGen.Cell.Solid;
        }

        /// <summary>Host: grow the tree a sapling makes (TreeGrower.growTree), where there's room; it stays a sapling if not.</summary>
        public static bool TryGrow(BlockKey k, BlockDef sapling)
        {
            var w = W;
            var wood = Blocks.Woods.FirstOrDefault(x => x.Sapling == sapling);
            if (w == null || wood == null) return false;
            var origin = k;
            var saplings = new List<BlockKey> { k };
            if (wood == Blocks.DarkOak)
            {
                // (Minecraft's: four in a square, this one in it; the square's north-west corner is the tree's)
                bool found = false;
                for (int i = 0; i >= -1 && !found; i--)
                    for (int j = 0; j >= -1 && !found; j--)
                    {
                        var sq = new[] { new Vector3Int(i, 0, j), new Vector3Int(i + 1, 0, j), new Vector3Int(i, 0, j + 1), new Vector3Int(i + 1, 0, j + 1) }
                            .Select(o => new BlockKey(k.Frame, k.YOff, k.Pos + o)).ToList();
                        if (sq.All(q => w.DefAt(q) == sapling)) { origin = sq[0]; saplings = sq; found = true; }
                    }
                if (!found) return false;
            }
            // (the saplings out of the way while it looks for room: Minecraft sets them to air first)
            var skip = new HashSet<Vector3Int>(saplings.Select(s => s.Pos));
            var placed = TreeGen.Grow(wood.Key, origin.Pos, new System.Random(Random.Range(int.MinValue, int.MaxValue)),
                p => skip.Contains(p) ? TreeGen.Cell.Air : CellAt(origin, p));
            if (placed == null) return false;
            var ops = new List<Op>();
            foreach (var s in saplings) ops.Add(Op.Remove(s, false));
            foreach (var pl in placed)
            {
                var pk = new BlockKey(origin.Frame, origin.YOff, pl.Pos);
                BlockData data;
                switch (pl.Part)
                {
                    case TreeGen.Part.Log: data = new BlockData(wood.Log.Id, (byte)Face.Up, 0); break;
                    case TreeGen.Part.LogX: data = new BlockData(wood.Log.Id, (byte)Face.East, 0); break;
                    case TreeGen.Part.LogZ: data = new BlockData(wood.Log.Id, (byte)Face.North, 0); break;
                    case TreeGen.Part.Leaves: data = new BlockData(wood.Leaves.Id, (byte)Face.Up, (byte)Natural); break;
                    case TreeGen.Part.Roots: data = new BlockData(Blocks.MangroveRoots.Id, (byte)Face.Up, 0); break;
                    case TreeGen.Part.MuddyRoots: data = new BlockData(Blocks.MuddyMangroveRoots.Id, (byte)Face.Up, 0); break;
                    case TreeGen.Part.Vine: data = new BlockData(Blocks.Vine.Id, pl.Facing, 0); break;
                    default: data = new BlockData(Blocks.Mangrove.Sapling.Id, (byte)Face.Up, (byte)Hanging); break;
                }
                ops.Add(Op.Set(pk, data));
            }
            // (Minecraft's placeBelowTrunkBlock: grass under the trunk turns to dirt)
            foreach (var s in saplings)
            {
                var below = s.Offset((int)Face.Down);
                if (w.DefAt(below) == Blocks.Grass) ops.Add(Op.Set(below, new BlockData(Blocks.Dirt.Id, (byte)Face.Up, 0)));
            }
            BlockNet.ServerBroadcastOps(ops);
            TreesGrown++;
            Plugin.Log.LogInfo($"[trees] a {wood.Key} tree grew at {origin.Pos}: {placed.Count} blocks");
            return true;
        }

        // ------------------------------------------------------------------ leaves
        /// <summary>Host: a log or leaves went: the natural leaves near it that no log holds any more decay, a few seconds
        /// apart (Minecraft's random ticks).</summary>
        public static void ServerBlockRemoved(BlockKey k, BlockDef def)
        {
            if (!(Blocks.IsPillar(def) || IsLeaves(def))) return;
            var w = W;
            for (int x = -7; x <= 7; x++)
                for (int y = -7; y <= 7; y++)
                    for (int z = -7; z <= 7; z++)
                    {
                        var n = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(x, y, z));
                        var b = w.Get(n);
                        if (b == null || !IsLeaves(b.Data.Def) || (b.Data.State & Natural) == 0) continue;
                        if (queued.Contains(n)) continue;
                        if (!NearLog(n)) { decayQueue.Add((n, Time.time + Random.Range(0.5f, 8f))); queued.Add(n); }
                    }
        }

        /// <summary>A log within Minecraft's leaf distance (6: five leaves between at most).</summary>
        static bool NearLog(BlockKey start)
        {
            var w = W;
            var seen = new HashSet<BlockKey> { start };
            var frontier = new List<BlockKey> { start };
            for (int depth = 1; depth <= 6 && frontier.Count > 0; depth++) // (Minecraft's distance: a leaf 7 from a log decays)
            {
                var next = new List<BlockKey>();
                foreach (var f in frontier)
                    for (int face = 0; face < 6; face++)
                    {
                        var n = f.Offset(face);
                        if (!seen.Add(n)) continue;
                        var d = w.DefAt(n);
                        if (d == null) continue;
                        if (Blocks.IsPillar(d)) return true;
                        if (IsLeaves(d) && depth < 6) next.Add(n);
                    }
                frontier = next;
            }
            return false;
        }

        /// <summary>Host: leaves broken (or decayed): Minecraft's drops. True: handled (the leaves' own drop isn't made).</summary>
        public static bool ServerLeafDrop(BlockDef leaves, Vector3 at, PlayerControllerB breaker)
        {
            if (!IsLeaves(leaves)) return false;
            var tool = breaker != null ? breaker.currentlyHeldObjectServer as ToolItem : null;
            if (tool != null && tool.Kind == ToolKind.Shears) { ServerLogic.SpawnDrop(leaves, at); return true; }
            var wood = Blocks.Woods.First(x => x.Leaves == leaves);
            float sap = wood == Blocks.DarkOak ? 0.025f : wood == Blocks.Mangrove ? 0f : 0.05f;
            if (Random.value < sap && ModItems.ByKey.TryGetValue(wood.Sapling.Key, out var s)) ModItems.ServerSpawnStack(s, 1, at);
            if (Random.value < 0.02f && ModItems.ByKey.TryGetValue("stick", out var st)) ModItems.ServerSpawnStack(st, Random.Range(1, 3), at);
            if ((wood == Blocks.Oak || wood == Blocks.DarkOak) && Random.value < 0.005f && ModItems.ByKey.TryGetValue("apple", out var ap)) ModItems.ServerSpawnStack(ap, 1, at);
            return true;
        }
    }
}
