using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Flowing water (#19), Minecraft's rules, worked out by the host: a source (level 0) flows down into open cells under
    /// it (falling water) and, on a floor, out to the sides, one level weaker per block, up to level 7. Flowing water dries
    /// up once nothing feeds it. Two sources with a floor between them make a new one (infinite water). Water washes away
    /// torches, dust and the like (they drop), puts out fire, and turns lava to obsidian (a source) or cobblestone.
    /// Only cells near a change are looked at, a few hundred at a time every 0.25 s (Minecraft's water ticks every 5).
    /// State: bits 0-2 the level (0 = source), bit 3 falling. Unlike Minecraft, a fall counts against the 7 levels water
    /// reaches (falling water keeps the level it fell from): the moons are long slopes, not blocks, and water reset by every
    /// fall ran off down a whole hillside.
    /// </summary>
    public static class WaterFlow
    {
        public const byte Falling = 8;
        public static int MaxPerStep = 300;
        static readonly HashSet<BlockKey> active = new HashSet<BlockKey>();
        static float next;
        public static int Steps; // (dev/tests)

        static BlockWorld W => BlockWorld.Instance;
        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        public static bool IsWater(BlockKey k) => W != null && W.DefAt(k) == Blocks.Water;
        static byte StateAt(BlockKey k) { var b = W.Get(k); return b != null ? b.Data.State : (byte)0; }
        static bool IsSource(BlockKey k) => IsWater(k) && StateAt(k) == 0;

        /// <summary>Server: something changed at k: water around it (or in it) gets looked at.</summary>
        public static void Touch(BlockKey k)
        {
            if (W == null) return;
            bool near = IsWater(k);
            for (int f = 0; f < 6 && !near; f++) near = IsWater(k.Offset(f));
            if (!near) return;
            active.Add(k);
            for (int f = 0; f < 6; f++) active.Add(k.Offset(f));
        }

        public static void Reset() => active.Clear();

        /// <summary>Water may go into a cell: nothing there (and no level geometry in it), or something water washes away.</summary>
        static bool CanFlowInto(BlockKey k, Dictionary<BlockKey, bool> openCache)
        {
            if (Redstone.OutOfWorld(k)) return false;
            var b = W.Get(k);
            if (b != null)
            {
                var d = b.Data.Def;
                if (d == Blocks.Water) return true;
                return WashedAway(d) || d == Blocks.Lava;
            }
            // (only a cell with no level geometry in it at all: Lethal Company's terrain isn't made of cells, and water let
            // into the mostly-open cells over a slope ran off down the whole hillside)
            if (!openCache.TryGetValue(k, out bool open)) openCache[k] = open = !ServerLogic.Obstructed(k, 0.98f);
            return open;
        }

        /// <summary>Things water flows over and breaks (they drop): torches, dust, levers, buttons, plates, repeaters, fire.</summary>
        static bool WashedAway(BlockDef d) =>
            d == Blocks.Fire || d.Shape == BlockShape.Torch || d.Shape == BlockShape.Dust || d.Shape == BlockShape.Lever
            || d.Shape == BlockShape.Button || d.Shape == BlockShape.Plate || d.Shape == BlockShape.Repeater || d.Shape == BlockShape.Comparator;

        /// <summary>Server, every frame: a step of water every 0.25 s while there's moving water.</summary>
        public static void ServerTick()
        {
            if (active.Count == 0 || Time.time < next || W == null) return;
            next = Time.time + 0.25f;
            Steps++;
            var batch = active.Take(MaxPerStep).ToList();
            foreach (var k in batch) active.Remove(k);
            var want = new Dictionary<BlockKey, byte?>(); // what each cell becomes this step (null: no water)
            var open = new Dictionary<BlockKey, bool>();
            foreach (var k in batch) Evaluate(k, want, open);

            var ops = new List<Op>();
            foreach (var kv in want)
            {
                var k = kv.Key;
                var cur = W.Get(k);
                if (kv.Value == null)
                {
                    if (cur != null && cur.Data.Def == Blocks.Water) { ops.Add(Op.Remove(k, false)); Touch(k); }
                    continue;
                }
                byte st = kv.Value.Value;
                if (cur != null && cur.Data.Def == Blocks.Lava)
                {
                    // lava meets water: a lava pool turns to obsidian (a source block) or cobblestone
                    var into = cur.Data.State == 0 ? Blocks.Obsidian : Blocks.Cobblestone;
                    ops.Add(Op.Set(k, new BlockData(into.Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.7f, 1f);
                    continue;
                }
                if (cur != null && cur.Data.Def != Blocks.Water)
                {
                    // washed away: it drops (fire just goes out)
                    if (cur.Data.Def == Blocks.Fire) BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.5f, 1.2f);
                    else ServerLogic.SpawnDrop(cur.Data.Def, W.WorldCenter(k));
                    ops.Add(Op.Remove(k, false));
                }
                if (cur != null && cur.Data.Def == Blocks.Water && cur.Data.State == st) continue;
                ops.Add(Op.Set(k, new BlockData(Blocks.Water.Id, (byte)Face.Up, st)));
                Touch(k);
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); Redstone.MarkDirty(); }
        }

        /// <summary>What happens at one cell this step (writes into want: the cell itself, and where its water goes).</summary>
        static void Evaluate(BlockKey k, Dictionary<BlockKey, byte?> want, Dictionary<BlockKey, bool> open)
        {
            byte? Cur(BlockKey c) => want.TryGetValue(c, out var w) ? w : IsWater(c) ? StateAt(c) : (byte?)null;
            var self = Cur(k);
            if (self == null) return;
            byte s = self.Value;
            bool source = s == 0;

            if (!source)
            {
                // flowing water stays only while something feeds it: water above (falling) or a stronger neighbour
                byte? keep = null;
                var above = Cur(k.Offset((int)Face.Up));
                if (above != null) keep = (byte)(Falling | (above.Value & 7));
                else
                {
                    int best = 8, sources = 0;
                    foreach (int f in Horizontal)
                    {
                        var n = Cur(k.Offset(f));
                        if (n == null) continue;
                        int lv = n.Value & 7;
                        if (n.Value == 0) sources++;
                        if (lv < best) best = lv;
                    }
                    // two sources beside it and a floor (or a source) under it: a new source, like Minecraft
                    var below = k.Offset((int)Face.Down);
                    if (sources >= 2 && (W.IsSolidAt(below) || Cur(below) == 0)) keep = 0;
                    else if (best < 7) keep = (byte)(best + 1);
                }
                if (keep == null) { want[k] = null; return; }
                if (keep != s) { want[k] = keep; s = keep.Value; source = s == 0; }
            }

            // down first: water falls into an open cell under it
            var down = k.Offset((int)Face.Down);
            if (CanFlowInto(down, open))
            {
                var d = Cur(down);
                byte fall = (byte)(Falling | (s & 7));
                if (d == null || d.Value != 0) { if (d != fall) want[down] = fall; }
                if (d == null || (d.Value & Falling) != 0) return; // (falling: it doesn't spread out sideways too)
            }
            // then out to the sides, one level weaker (water that fell, one weaker than where it fell from)
            int nextLevel = (s & 7) + 1;
            if (nextLevel > 7) return;
            foreach (int f in Horizontal)
            {
                var n = k.Offset(f);
                if (!CanFlowInto(n, open)) continue;
                var cur = Cur(n);
                if (cur == 0) continue;                                         // a source stays a source
                if (cur != null && (cur.Value & Falling) == 0 && (cur.Value & 7) <= nextLevel) continue; // as strong already
                if (cur != null && (cur.Value & Falling) != 0) continue;
                want[n] = (byte)nextLevel;
            }
        }

        public static string Describe() => $"active={active.Count} steps={Steps}";
    }
}
