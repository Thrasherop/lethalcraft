using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Flowing water and lava (#19, #74), Minecraft's rules, worked out by the host. A source (level 0) flows down into open
    /// cells under it (falling) and, on a floor, out to the sides, one level weaker per block: water up to level 7 every
    /// 0.25 s, lava up to level 3 and slower (every 1.5 s), like the Overworld's. Flowing liquid dries up once nothing
    /// feeds it. Two water sources with a floor between them make a new one (infinite water); lava doesn't. Water washes
    /// away torches, dust and the like (they drop), lava burns them (gone); water puts out fire and turns lava into
    /// obsidian (a source) or cobblestone; lava running into water makes stone (falling) or cobblestone.
    /// State: bits 0-2 the level (0 = source), bit 3 falling. Unlike Minecraft, a fall counts against the levels the
    /// liquid reaches (falling liquid keeps the level it fell from): the moons are long slopes, not blocks, and water reset
    /// by every fall ran off down a whole hillside.
    /// </summary>
    public sealed class FluidSim
    {
        public const byte Falling = 8;
        public readonly BlockDef Def;
        readonly int maxLevel;
        readonly float step;
        readonly bool infinite;
        public int MaxPerStep = 300;
        readonly HashSet<BlockKey> active = new HashSet<BlockKey>();
        float next;
        public int Steps; // (dev/tests)

        public FluidSim(BlockDef def, int maxLevel, float step, bool infinite) { Def = def; this.maxLevel = maxLevel; this.step = step; this.infinite = infinite; }

        static BlockWorld W => BlockWorld.Instance;
        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        bool Is(BlockKey k) => W != null && W.DefAt(k) == Def;
        static byte StateAt(BlockKey k) { var b = W.Get(k); return b != null ? b.Data.State : (byte)0; }

        /// <summary>Server: something changed at k: liquid around it (or in it) gets looked at.</summary>
        public void Touch(BlockKey k)
        {
            if (W == null) return;
            bool near = Is(k);
            for (int f = 0; f < 6 && !near; f++) near = Is(k.Offset(f));
            if (!near) return;
            active.Add(k);
            for (int f = 0; f < 6; f++) active.Add(k.Offset(f));
        }

        public void Reset() => active.Clear();

        /// <summary>
        /// The liquid may go into a cell: nothing there and no level geometry in it, or something it washes away (or the
        /// other liquid, which it reacts with). A cell is open only with no level geometry in it at all (Lethal Company's
        /// terrain isn't made of cells, and water let into the mostly-open cells over a slope ran off down the whole
        /// hillside), but a floor reaching a little into its bottom doesn't count: the ship's floor sits a few
        /// centimetres into the cells above it, and water on it neither spread nor fell (#75).
        /// </summary>
        bool CanFlowInto(BlockKey k, Dictionary<BlockKey, bool> openCache)
        {
            if (Redstone.OutOfWorld(k)) return false;
            var b = W.Get(k);
            if (b != null)
            {
                var d = b.Data.Def;
                if (d == Def) return true;
                return WaterFlow.WashedAway(d) || d == Blocks.Water || d == Blocks.Lava;
            }
            if (!openCache.TryGetValue(k, out bool open)) openCache[k] = open = !ObstructedAboveFloor(k) && StaysOnShip(k);
            return open;
        }

        /// <summary>
        /// Liquid on the ship's grid stays on the ship: in its box, or over the ship's own blocks (a porch), never off an
        /// edge. (The ship's grid travels with it: water out of its door fell, cell by cell, 60 blocks into space in orbit
        /// and made sources down there, thousands of blocks saved with the ship.)
        /// </summary>
        static bool StaysOnShip(BlockKey k)
        {
            if (k.Frame == 0) return true;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.shipBounds == null) return false;
            var c = W.WorldCenter(k);
            if (sor.shipBounds.bounds.Contains(c) || (sor.shipInnerRoomBounds != null && sor.shipInnerRoomBounds.bounds.Contains(c))) return true;
            return W.IsSolidAt(k.Offset((int)Face.Down));
        }

        /// <summary>Level geometry in the cell, apart from its bottom tenth (a floor surface just poking in).</summary>
        static bool ObstructedAboveFloor(BlockKey k)
        {
            // (a cell the ground fills, on the moon's own grid: a box overlap only finds the terrain's surface, so a cell
            // deep inside it looked open, and water let in under a low surface fell on down through solid ground)
            if (k.Frame == 0 && k.YOff == Ground.GridYOff && Ground.IsSolidCell(k.Pos)) return true;
            var world = W;
            var c = world.WorldCenter(k) + world.FrameDirToWorld(k.Frame, Vector3.up) * (BlockWorld.S * 0.05f);
            var half = new Vector3(0.49f, 0.44f, 0.49f) * BlockWorld.S;
            foreach (var h in Physics.OverlapBox(c, half, world.FrameRotation(k.Frame), ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<BlockRef>() != null) continue;
                if (h.GetComponentInParent<GrabbableObject>() != null) continue;
                if (h.GetComponentInParent<GameNetcodeStuff.PlayerControllerB>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>A floor under a cell: a solid block, or level geometry (natural ground isn't made of blocks, #79).</summary>
        bool Floor(BlockKey below, Dictionary<BlockKey, bool> open)
        {
            if (W.IsSolidAt(below)) return true;
            if (W.Get(below) != null) return false;
            return !CanFlowInto(below, open);
        }

        /// <summary>Server, every frame: a step of this liquid while some of it is moving.</summary>
        public void ServerTick()
        {
            if (active.Count == 0 || Time.time < next || W == null) return;
            next = Time.time + step;
            Steps++;
            var batch = active.Take(MaxPerStep).ToList();
            // (#244: lava steps every half second in the Nether, every third step elsewhere: Minecraft's 10 and 30 ticks)
            if (Def == Blocks.Lava && Steps % 3 != 0) batch = batch.Where(InNether).ToList();
            foreach (var k in batch) active.Remove(k);
            var want = new Dictionary<BlockKey, byte?>(); // what each cell becomes this step (null: no liquid; see Put)
            var open = new Dictionary<BlockKey, bool>();
            foreach (var k in batch) Evaluate(k, want, open);

            var ops = new List<Op>();
            foreach (var kv in want)
            {
                var k = kv.Key;
                var cur = W.Get(k);
                if (kv.Value == null)
                {
                    if (cur != null && cur.Data.Def == Def) { ops.Add(Op.Remove(k, false)); WaterFlow.Touch(k); }
                    continue;
                }
                byte st = kv.Value.Value;
                bool falling = (st & Falling) != 0;
                if (Def == Blocks.Water && cur != null && cur.Data.Def == Blocks.Lava)
                {
                    // water meets lava: a lava pool turns to obsidian (a source block) or cobblestone
                    var into = cur.Data.State == 0 ? Blocks.Obsidian : Blocks.Cobblestone;
                    ops.Add(Op.Set(k, new BlockData(into.Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.7f, 1f);
                    continue;
                }
                if (Def == Blocks.Lava && cur != null && cur.Data.Def == Blocks.Water)
                {
                    // lava runs into water: stone where it falls into it, cobblestone from the side (Minecraft's)
                    var into = falling ? Blocks.Stone : Blocks.Cobblestone;
                    ops.Add(Op.Set(k, new BlockData(into.Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.7f, 1f);
                    WaterFlow.Touch(k);
                    continue;
                }
                if (cur != null && cur.Data.Def != Def)
                {
                    // washed away (it drops) or burnt (lava: gone); fire just goes out under water
                    if (cur.Data.Def == Blocks.Fire) { if (Def == Blocks.Water) BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.5f, 1.2f); }
                    else if (Def == Blocks.Water) ServerLogic.SpawnDrop(cur.Data.Def, W.WorldCenter(k));
                    else BlockNet.ServerSound(W.WorldCenter(k), "extinguish", 0.4f, 1.6f);
                    ops.Add(Op.Remove(k, false));
                }
                if (cur != null && cur.Data.Def == Def && cur.Data.State == st) continue;
                ops.Add(Op.Set(k, new BlockData(Def.Id, (byte)Face.Up, st)));
                WaterFlow.Touch(k);
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); Redstone.MarkDirty(); }
        }

        /// <summary>
        /// A cell's outcome this step, the stronger of what's asked of it: a source over falling liquid over a lower level
        /// over none (two cells flowing into one, or a cell drying while a neighbour flows into it).
        /// </summary>
        static void Put(Dictionary<BlockKey, byte?> want, BlockKey k, byte? v)
        {
            if (want.TryGetValue(k, out var had) && Strength(had) >= Strength(v)) return;
            want[k] = v;
        }
        static int Strength(byte? v) => v == null ? -1 : v.Value == 0 ? 100 : (v.Value & Falling) != 0 ? 50 + 7 - (v.Value & 7) : 7 - (v.Value & 7);

        /// <summary>Liquid at k spreads out to the sides: it can't fall (nothing open under it, or still liquid there).</summary>
        bool Spreads(BlockKey k, System.Func<BlockKey, byte?> cur, Dictionary<BlockKey, bool> open)
        {
            var down = k.Offset((int)Face.Down);
            if (!CanFlowInto(down, open)) return true;
            var d = cur(down);
            return d != null && (d.Value & Falling) == 0;
        }

        /// <summary>
        /// What happens at one cell this step (writes into want: the cell itself, and where its liquid goes), worked out
        /// from the liquid as it was when the step began, so it moves one block a step, as Minecraft's (a tick every 5 for
        /// water, 30 for lava). Reading this step's results as they came, liquid that had just fallen into a cell fell on
        /// in the same step: water off a pillar 8 high was on the ground in half a second (Minecraft: 2 s), and it ran
        /// two blocks a step along the floor (#202).
        /// </summary>
        void Evaluate(BlockKey k, Dictionary<BlockKey, byte?> want, Dictionary<BlockKey, bool> open)
        {
            byte? Cur(BlockKey c) => Is(c) ? StateAt(c) : (byte?)null;
            var self = Cur(k);
            if (self == null) return;
            byte s = self.Value;
            bool source = s == 0;

            if (!source)
            {
                // flowing liquid stays only while something feeds it: the same liquid above (falling) or a stronger neighbour
                byte? keep = null;
                var above = Cur(k.Offset((int)Face.Up));
                if (above != null) keep = (byte)(Falling | (above.Value & 7));
                else
                {
                    int best = 8, sources = 0;
                    foreach (int f in Horizontal)
                    {
                        var nk = k.Offset(f);
                        var n = Cur(nk);
                        if (n == null) continue;
                        // (only liquid that spreads feeds its neighbours: liquid with room to fall under it doesn't; a
                        // waterfall's cells kept each other up, and it took a second or two per block to dry)
                        if (n.Value != 0 && !Spreads(nk, Cur, open)) continue;
                        int lv = n.Value & 7;
                        if (n.Value == 0) sources++;
                        if (lv < best) best = lv;
                    }
                    // two sources beside it and a floor (or a source) under it: a new source, like Minecraft's water
                    var below = k.Offset((int)Face.Down);
                    if (infinite && sources >= 2 && (Floor(below, open) || Cur(below) == 0)) keep = 0;
                    else if (best < MaxLevelAt(k)) keep = (byte)(best + 1);
                }
                if (keep == null) { Put(want, k, null); return; }
                if (keep != s) { Put(want, k, keep); s = keep.Value; source = s == 0; }
            }

            // down first: the liquid falls into an open cell under it
            var down = k.Offset((int)Face.Down);
            if (CanFlowInto(down, open))
            {
                var d = Cur(down);
                byte fall = (byte)(Falling | (s & 7));
                if (d == null || d.Value != 0) { if (d != fall) Put(want, down, fall); }
                if (d == null || (d.Value & Falling) != 0) return; // (falling: it doesn't spread out sideways too)
            }
            // then out to the sides, one level weaker (liquid that fell, one weaker than where it fell from)
            int nextLevel = (s & 7) + 1;
            if (nextLevel > MaxLevelAt(k)) return;
            // (#244) Minecraft's FlowingFluid.getSpread: only toward the nearest drop within its reach (water 4 blocks, lava 2,
            // the Nether's lava 4), every way when there's none
            int reach = SlopeReach(k);
            var dist = new int[6]; int bestDist = 1000;
            foreach (int f in Horizontal)
            {
                var n = k.Offset(f);
                dist[f] = 1000;
                if (!CanFlowInto(n, open) || Cur(n) == 0) continue;
                dist[f] = Hole(n, open) ? 0 : SlopeDistance(n, 1, f ^ 1, reach, open, Cur);
                if (dist[f] < bestDist) bestDist = dist[f];
            }
            foreach (int f in Horizontal)
            {
                var n = k.Offset(f);
                if (!CanFlowInto(n, open)) continue;
                if (dist[f] > bestDist) continue; // (#244: not the way down)
                var cur = Cur(n);
                if (cur == 0) continue;                                         // a source stays a source
                if (cur != null && (cur.Value & Falling) == 0 && (cur.Value & 7) <= nextLevel) continue; // as strong already
                if (cur != null && (cur.Value & Falling) != 0) continue;
                Put(want, n, (byte)nextLevel);
            }
        }

        /// <summary>(#244) An open cell with room for the liquid to fall under it.</summary>
        bool Hole(BlockKey k, Dictionary<BlockKey, bool> open) => CanFlowInto(k.Offset((int)Face.Down), open);

        /// <summary>(#244) Minecraft's FlowingFluid.getSlopeDistance: how many blocks to the nearest drop, not back the way it
        /// came, within reach (1000: none).</summary>
        int SlopeDistance(BlockKey k, int depth, int from, int reach, Dictionary<BlockKey, bool> open, System.Func<BlockKey, byte?> cur)
        {
            int best = 1000;
            foreach (int f in Horizontal)
            {
                if (f == from) continue;
                var n = k.Offset(f);
                if (!CanFlowInto(n, open) || cur(n) == 0) continue;
                if (Hole(n, open)) return depth;
                if (depth < reach) { int d = SlopeDistance(n, depth + 1, f ^ 1, reach, open, cur); if (d < best) best = d; }
            }
            return best;
        }

        /// <summary>(#244) Minecraft's: lava in the Nether runs 7 blocks (a level a block) and looks 4 for a drop; elsewhere 3
        /// and 2. Water: 7 and 4.</summary>
        static bool InNether(BlockKey k) => W != null && NetherGround.InRegion(W.WorldCenter(k));
        int MaxLevelAt(BlockKey k) => Def == Blocks.Lava && InNether(k) ? 7 : maxLevel;
        int SlopeReach(BlockKey k) => Def == Blocks.Lava ? (InNether(k) ? 4 : 2) : 4;

        public string Describe() => $"{Def.Key}: active={active.Count} steps={Steps}";
    }

    /// <summary>The liquids the host runs: water and lava (same engine, Minecraft's numbers).</summary>
    public static class WaterFlow
    {
        public const byte Falling = FluidSim.Falling;
        static FluidSim water, lava;
        static FluidSim Water => water ?? (water = new FluidSim(Blocks.Water, 7, 0.25f, infinite: true));
        static FluidSim Lava => lava ?? (lava = new FluidSim(Blocks.Lava, 3, 0.5f, infinite: false)); // (#244: its steps every half second, the Overworld's only every third)

        public static bool IsWater(BlockKey k) => BlockWorld.Instance != null && BlockWorld.Instance.DefAt(k) == Blocks.Water;

        /// <summary>Server: something changed at k: water and lava around it get looked at.</summary>
        public static void Touch(BlockKey k) { Water.Touch(k); Lava.Touch(k); }
        public static void Reset() { Water.Reset(); Lava.Reset(); }
        public static void ServerTick() { Water.ServerTick(); Lava.ServerTick(); ServerCarryItems(); }
        public static int Steps => Water.Steps;

        /// <summary>Things liquids flow over and break: torches, dust, levers, buttons, plates, repeaters, comparators, fire.</summary>
        public static bool WashedAway(BlockDef d) =>
            d == Blocks.Fire || d.Shape == BlockShape.Torch || d.Shape == BlockShape.Dust || d.Shape == BlockShape.Lever
            || d.Shape == BlockShape.Button || d.Shape == BlockShape.Plate || d.Shape == BlockShape.Repeater || d.Shape == BlockShape.Comparator
            || d.Shape == BlockShape.Crop || d.Shape == BlockShape.Sapling || d.Shape == BlockShape.Plant || d.Shape == BlockShape.Tripwire; // (#244: crops, saplings, flowers, grass, tripwire)

        public static string Describe() => Water.Describe() + " | " + Lava.Describe();

        // ------------------------------------------------------------------ (#244) the current
        /// <summary>Minecraft's FlowingFluid.getFlow, flat: from a water cell toward the lower water (or open space) beside it,
        /// in world space (zero in still water, or none).</summary>
        public static Vector3 FlowAt(Vector3 world)
        {
            var w = BlockWorld.Instance;
            if (w == null || !WaterSwim.WaterCellAt(world, out var b)) return Vector3.zero;
            float Height(BlockInstance x) => x == null ? 0f : (x.Data.State & Falling) != 0 ? 1f : (8 - (x.Data.State & 7)) / 9f;
            float own = Height(b);
            var flow = Vector3.zero;
            foreach (int f in new[] { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East })
            {
                var nk = b.Key.Offset(f);
                var n = w.Get(nk);
                float diff;
                if (n != null && n.Data.Def == Blocks.Water) diff = own - Height(n);
                else if (n == null || !n.Data.Def.Solid)
                {
                    var below = w.Get(nk.Offset((int)Face.Down));
                    if (below == null || below.Data.Def != Blocks.Water) continue;
                    diff = own - (Height(below) - 8f / 9f);
                }
                else continue;
                flow += (Vector3)Faces.Dir[f] * diff;
            }
            if (flow.sqrMagnitude < 1e-6f) return Vector3.zero;
            return w.FrameDirToWorld(b.Key.Frame, flow.normalized);
        }

        static float nextItems;
        public static int ItemsCarried; // (dev/tests)
        /// <summary>Host, often: items lying in moving water drift along it (Minecraft's: about two blocks a second).</summary>
        public static void ServerCarryItems()
        {
            if (Time.time < nextItems) return;
            nextItems = Time.time + 0.25f;
            foreach (var st in StackItem.Live.ToList())
            {
                if (st == null || st.isHeld || st.isPocketed || st.isHeldByEnemy || !st.IsSpawned) continue;
                var at = st.transform.position + Vector3.up * 0.05f;
                var flow = FlowAt(at);
                if (flow == Vector3.zero) continue;
                var to = st.transform.position + flow * 0.5f * BlockWorld.S;
                if (Physics.Raycast(at, flow, 0.6f * BlockWorld.S, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                ItemGravity.MoveTo(st, to);
                BlockNet.ServerItemMoved(st, to);
                ItemsCarried++;
            }
        }
    }
}
