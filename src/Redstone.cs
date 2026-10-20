using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Server-side redstone + piston simulation, ticked at 20 Hz (one Minecraft game tick).
    /// Simplified but faithful: levers/buttons/plates/torches/redstone blocks are sources; dust carries power
    /// with 15-step falloff; solid blocks can be strongly/weakly powered; redstone torches invert with a delay
    /// (so clocks work) and burn out when toggled too fast.
    /// </summary>
    public static class Redstone
    {
        /// <summary>([Redstone] QuasiConnectivity) Java's quasi-connectivity for pistons (#160).</summary>
        public static bool QuasiConnectivity = true;
        static bool dirty;
        static float acc;
        public static long Tick;
        static readonly List<(long due, Action act)> scheduled = new List<(long, Action)>();
        static readonly HashSet<BlockKey> poweredLastTick = new HashSet<BlockKey>();
        static readonly Dictionary<BlockKey, long> pistonBusyUntil = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, List<long>> torchToggles = new Dictionary<BlockKey, List<long>>();
        static readonly Dictionary<BlockKey, long> torchBurnedUntil = new Dictionary<BlockKey, long>();
        static readonly HashSet<BlockKey> torchPending = new HashSet<BlockKey>();
        static readonly HashSet<BlockKey> repeaterPending = new HashSet<BlockKey>();
        static readonly HashSet<BlockKey> comparatorPending = new HashSet<BlockKey>();
        public const byte ComparatorSubtract = 16;
        static readonly Dictionary<BlockKey, long> plateLastSeen = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, long> lampOffAt = new Dictionary<BlockKey, long>();
        // blocks a piston is moving: they arrive 2 ticks later, and until then (like Minecraft's moving blocks) they
        // can't be pushed, don't give power and, if they're pistons, don't fire
        static readonly Dictionary<BlockKey, long> movingUntil = new Dictionary<BlockKey, long>();
        // a piston whose pushed blocks are still moving: losing power now leaves them where they were pushed
        static readonly Dictionary<BlockKey, long> pushedUntil = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, int> observerGen = new Dictionary<BlockKey, int>();

        static bool IsMoving(BlockKey k) => movingUntil.TryGetValue(k, out long t) && t > Tick;

        // ------------------------------------------------------------------ block updates (#185)
        // Minecraft's pistons look at their power only when a block update reaches them (PistonBaseBlock.neighborChanged,
        // onPlace): one powered without an update stays put until something next to it changes (a "BUD"). With
        // quasi-connectivity that's most of how piston doors work: a piston powered through the space above it fires only
        // once something updates it. A change updates the cells next to it (Level.updateNeighborsAt); redstone reaches
        // further, as Minecraft's does: dust and torches update their neighbours' neighbours, a lever or a button the
        // block it's on and that block's neighbours, a repeater, comparator or observer the cell it outputs into and that
        // cell's neighbours, a pressure plate the block under it and its neighbours.
        static readonly HashSet<BlockKey> updated = new HashSet<BlockKey>();
        public static int Updates; // (dev)

        static void Around(BlockKey k) { updated.Add(k); for (int f = 0; f < 6; f++) updated.Add(k.Offset(f)); }

        /// <summary>Host: a block changed here (placed, broken, its state, moved in or out): the cells it updates.</summary>
        public static void BlockUpdates(BlockKey k, BlockData? before, BlockData? after)
        {
            if (!BlockNet.IsServer) return;
            Updates++;
            Around(k);
            if (before.HasValue) Reaches(k, before.Value);
            if (after.HasValue) Reaches(k, after.Value);
        }

        /// <summary>The further cells a redstone component updates when it changes.</summary>
        static void Reaches(BlockKey k, BlockData d)
        {
            var def = d.Def;
            if (def == null) return;
            if (def == Blocks.RedstoneDust || def == Blocks.RedstoneTorch) { for (int f = 0; f < 6; f++) Around(k.Offset(f)); }
            else if (def == Blocks.Lever || def.Shape == BlockShape.Button || def.Shape == BlockShape.Hook) Around(k.Offset(Faces.Opposite(d.Facing)));
            else if (def.Shape == BlockShape.Plate) Around(k.Offset((int)Face.Down));
            else if (def == Blocks.Repeater || def == Blocks.Comparator) Around(k.Offset(d.Facing));
            else if (def == Blocks.Observer) Around(k.Offset(Faces.Opposite(d.Facing)));
            else if (def == Blocks.LightningRod) Around(k.Offset(Faces.Opposite(d.Facing)));
        }

        public static void MarkDirty() => dirty = true;

        public static void Schedule(int ticks, Action a) => scheduled.Add((Tick + Math.Max(1, ticks), a));

        public static void Reset()
        {
            scheduled.Clear();
            updated.Clear();
            poweredLastTick.Clear();
            pistonBusyUntil.Clear();
            repeaterPending.Clear();
            comparatorPending.Clear();
            torchToggles.Clear();
            torchBurnedUntil.Clear();
            torchPending.Clear();
            plateLastSeen.Clear();
            lampOffAt.Clear();
            movingUntil.Clear();
            pushedUntil.Clear();
            observerGen.Clear();
            observerBusy.Clear();
        }

        public static void ServerTick()
        {
            acc += Time.deltaTime;
            // catch up at most 4 ticks a frame (a long frame skips the rest); with the game sped up (the dev speed for
            // tests) a frame covers that much more game time, so that many more
            float speed = Mathf.Max(1f, Time.timeScale);
            int guard = 0, most = Mathf.CeilToInt(4 * speed);
            while (acc >= 0.05f && guard++ < most)
            {
                acc -= 0.05f;
                Tick++;
                try { GameTick(); }
                catch (Exception e) { Plugin.Log.LogError("Redstone tick: " + e); }
            }
            if (acc > 0.2f * speed) acc = 0;
        }

        static void GameTick()
        {
            // scheduled actions
            if (scheduled.Count > 0)
            {
                var due = scheduled.Where(s => s.due <= Tick).ToList();
                scheduled.RemoveAll(s => s.due <= Tick);
                foreach (var d in due)
                {
                    try { d.act(); } catch (Exception e) { Plugin.Log.LogError(e); }
                }
            }
            if (Tick % 4 == 0) PollPressurePlates();
            if (Tick % 20 == 0) PollDaylight();
            if (Tick % 4 == 2) PollTripwires();
            if (Tick % 8 == 5 && W != null) ItemTransport.ServerHopperTick(W); // (Minecraft's: an item every 8 ticks)
            if (Tick % 5 == 0) StackMerging.Tick();
            if (Tick % 10 == 0) EnemyBlockBreaking.Tick(0.5f);
            if (Tick % 10 == 0) Fire.ServerTick(hurtMonsters: Tick % 20 == 0);
            if (Tick % 20 == 0) { Farming.ServerTick(1f); Saplings.ServerTick(1f); ShipMobs.ServerTick(); Amethyst.ServerTick(1f); } // (#229: budding amethyst)
            if (Tick % 4 == 0) NetherPortal.ServerTick();
            Gravity.Tick();
            if (dirty && Tick % 2 == 0)
            {
                dirty = false;
                Recompute();
            }
        }

        // ------------------------------------------------------------------ helpers
        static BlockWorld W => BlockWorld.Instance;

        static bool IsActiveSource(BlockInstance b)
        {
            if (IsMoving(b.Key)) return false;
            var d = b.Data.Def;
            if (d == Blocks.RedstoneBlock) return true;
            if (d == Blocks.Lever || d.Shape == BlockShape.Button) return (b.Data.State & 1) != 0;
            if (d.Shape == BlockShape.Hook) return (b.Data.State & 2) != 0;
            if (d == Blocks.LightningRod || d == Blocks.Lectern) return (b.Data.State & 1) != 0;
            if (d == Blocks.DetectorRail) return (b.Data.State & Rails.Powered) != 0;
            if (d == Blocks.TrappedChest) return Chests.Viewers(b.Key) > 0;
            if (d.Shape == BlockShape.Plate || d == Blocks.Target || d == Blocks.DaylightDetector) return (b.Data.State & 15) != 0;
            if (d == Blocks.RedstoneTorch) return (b.Data.State & 1) == 0;
            if (d == Blocks.Observer) return (b.Data.State & 1) != 0;
            if (d == Blocks.Repeater) return (b.Data.State & 1) != 0;
            if (d == Blocks.Comparator) return (b.Data.State & 15) != 0;
            return false;
        }

        /// <summary>The strength an active source gives: 15, but a comparator its own output.</summary>
        static int StrengthOf(BlockInstance b) => b.Data.Def == Blocks.TrappedChest ? Math.Min(15, Chests.Viewers(b.Key)) : b.Data.Def == Blocks.Comparator || b.Data.Def.Shape == BlockShape.Plate || b.Data.Def == Blocks.Target || b.Data.Def == Blocks.DaylightDetector ? b.Data.State & 15 : 15;

        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        // ------------------------------------------------------------------ (#244) dust that points
        /// <summary>A dust's state: its power (0-15), and this bit when [E] made a lone one a dot.</summary>
        public const byte DustDot = 16;
        /// <summary>(#244) A repeater's state bit: locked by a repeater or comparator at its side.</summary>
        public const byte RepeaterLocked = 8;

        /// <summary>Minecraft's isSignalSource: what dust runs to (on or off).</summary>
        public static bool IsSignalSource(BlockDef d) =>
            d == Blocks.RedstoneBlock || d == Blocks.Lever || d.Shape == BlockShape.Button || d.Shape == BlockShape.Hook || d == Blocks.LightningRod
            || d == Blocks.Lectern || d == Blocks.DetectorRail || d == Blocks.TrappedChest || d.Shape == BlockShape.Plate || d == Blocks.Target
            || d == Blocks.DaylightDetector || d == Blocks.RedstoneTorch || d == Blocks.Observer || d == Blocks.Repeater || d == Blocks.Comparator;

        /// <summary>Minecraft's RedStoneWireBlock.getSignal: a dust powers the block under it and the sides it points to (a
        /// cross all four, a line its two ends, a dot none).</summary>
        static bool DustPoints(BlockKey dust, int face)
        {
            if (face == (int)Face.Down) return true;
            if (face == (int)Face.Up) return false;
            int c = W.DustConnections(dust);
            int s = c & 15;
            if (s == 0) return (c & 256) == 0; // (a cross: every way; a dot: none)
            int bit = face == (int)Face.North ? 1 : face == (int)Face.South ? 2 : face == (int)Face.West ? 4 : 8;
            return (s & bit) != 0;
        }

        static int FaceToward(BlockKey a, BlockKey b) { for (int f = 0; f < 6; f++) if (a.Offset(f).Equals(b)) return f; return (int)Face.Down; }

        /// <summary>Host: [E] on a lone dust: a cross becomes a dot, a dot a cross (Minecraft's).</summary>
        public static void ServerToggleDot(BlockKey k)
        {
            var b = W?.Get(k);
            if (b == null || b.Data.Def != Blocks.RedstoneDust || ((W.DustConnections(k) & 15) != 0 && (b.Data.State & DustDot) == 0)) return;
            var d = b.Data; d.State = (byte)(d.State ^ DustDot);
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            MarkDirty();
        }

        // ------------------------------------------------------------------ main recompute
        static void Recompute()
        {
            var world = W;
            if (world == null) return;
            var all = world.Blocks;
            var comps = all.Where(kv => kv.Value.Data.Def.IsRedstoneComponent).ToList();
            if (comps.Count == 0) { poweredLastTick.Clear(); updated.Clear(); return; }

            // 1. strongly powered solid blocks (and how strongly: a comparator gives its own strength)
            var strong = new Dictionary<BlockKey, int>();
            foreach (var kv in comps)
            {
                var b = kv.Value;
                if (!IsActiveSource(b)) continue;
                var def = b.Data.Def;
                BlockKey target;
                if (def == Blocks.Lever || def.Shape == BlockShape.Button || def.Shape == BlockShape.Hook) target = kv.Key.Offset(Faces.Opposite(b.Data.Facing));
                else if (def == Blocks.TrappedChest) target = kv.Key.Offset((int)Face.Down); // (Minecraft's: the block under it, strongly)
                else if (def == Blocks.RedstoneTorch) target = kv.Key.Offset((int)Face.Up);
                else if (def.Shape == BlockShape.Plate) target = kv.Key.Offset((int)Face.Down);
                else if (def == Blocks.Observer) target = kv.Key.Offset(Faces.Opposite(b.Data.Facing));
                else if (def == Blocks.Repeater || def == Blocks.Comparator) target = kv.Key.Offset(b.Data.Facing);
                else continue;
                if (world.IsSolidAt(target)) strong[target] = Math.Max(strong.TryGetValue(target, out int had) ? had : 0, StrengthOf(b));
            }

            // 2. dust power (BFS from seeds, 15 -> 0)
            var dustPower = new Dictionary<BlockKey, int>();
            var queue = new Queue<BlockKey>();
            foreach (var kv in comps)
            {
                if (kv.Value.Data.Def != Blocks.RedstoneDust) continue;
                dustPower[kv.Key] = 0;
                int seed = 0;
                for (int f = 0; f < 6 && seed < 15; f++)
                {
                    var nk = kv.Key.Offset(f);
                    var n = world.Get(nk);
                    if (n == null) continue;
                    var nd = n.Data.Def;
                    if (nd == Blocks.RedstoneDust) continue;
                    if (IsActiveSource(n) && PowersInto(nk, n, kv.Key))
                    {
                        // torches don't power dust on the block they're attached to... close enough: allow
                        seed = Math.Max(seed, StrengthOf(n));
                    }
                    else if (nd.Solid && strong.TryGetValue(nk, out int sp)) seed = Math.Max(seed, sp);
                }
                if (seed > 0) { dustPower[kv.Key] = seed; queue.Enqueue(kv.Key); }
            }
            while (queue.Count > 0)
            {
                var k = queue.Dequeue();
                int p = dustPower[k];
                if (p <= 1) continue;
                foreach (var nk in DustNeighbors(k))
                {
                    if (dustPower.TryGetValue(nk, out int np) && np < p - 1)
                    {
                        dustPower[nk] = p - 1;
                        queue.Enqueue(nk);
                    }
                }
            }

            // 3. weakly powered solid blocks (from dust on top / next to them), as strongly as the dust
            var weak = new Dictionary<BlockKey, int>();
            void Weak(BlockKey wk, int p) { if (!weak.TryGetValue(wk, out int had) || had < p) weak[wk] = p; }
            foreach (var kv in dustPower)
            {
                if (kv.Value <= 0) continue;
                var below = kv.Key.Offset((int)Face.Down);
                if (world.IsSolidAt(below)) Weak(below, kv.Value);
                foreach (int f in Horizontal)
                {
                    var nk = kv.Key.Offset(f);
                    if (world.IsSolidAt(nk) && !world.Get(nk).Data.Def.IsRedstoneComponent && DustPoints(kv.Key, f)) Weak(nk, kv.Value); // (#244: where it points)
                }
            }

            bool PoweredAt(BlockKey k, int ignoreFace)
            {
                for (int f = 0; f < 6; f++)
                {
                    if (f == ignoreFace) continue;
                    var nk = k.Offset(f);
                    var n = world.Get(nk);
                    if (n == null) continue;
                    var nd = n.Data.Def;
                    if (IsActiveSource(n) && PowersInto(nk, n, k))
                    {
                        // a torch doesn't power the block it hangs on
                        if (nd == Blocks.RedstoneTorch && ServerLogic.SupportOf(nk, n.Data).Equals(k)) continue;
                        return true;
                    }
                    if (nd == Blocks.RedstoneDust && dustPower.TryGetValue(nk, out int dp) && dp > 0 && f != (int)Face.Down && DustPoints(nk, f ^ 1)) return true; // (#244: dust beside it pointing at it, or on it)
                    if (nd.Solid && (strong.ContainsKey(nk) || weak.ContainsKey(nk))) return true;
                }
                return false;
            }

            // what a block gives a repeater or comparator behind it: dust its power, a source pointing in its strength, a
            // powered block how strongly it's powered
            int RearSignal(BlockKey back, BlockKey into)
            {
                var bb = world.Get(back);
                if (bb == null) return 0;
                var bd = bb.Data.Def;
                if (bd == Blocks.RedstoneDust) return dustPower.TryGetValue(back, out int dp) && DustPoints(back, FaceToward(back, into)) ? dp : 0;
                if (IsActiveSource(bb) && PowersInto(back, bb, into)) return StrengthOf(bb);
                if (bd.Solid)
                {
                    int p = strong.TryGetValue(back, out int sp) ? sp : 0;
                    if (weak.TryGetValue(back, out int wp)) p = Math.Max(p, wp);
                    return p;
                }
                return 0;
            }

            var ops = new List<Op>();
            var nowPowered = new HashSet<BlockKey>();
            // (dust changing now updates around it this same tick, as Minecraft's does: a piston it reaches acts now)
            foreach (var kv in comps)
                if (kv.Value.Data.Def == Blocks.RedstoneDust && (kv.Value.Data.State & 15) != (dustPower.TryGetValue(kv.Key, out int np) ? np : 0))
                { Around(kv.Key); Reaches(kv.Key, kv.Value.Data); }
            var keep = new List<BlockKey>(); // (updates a piston can't act on yet: it's moving, or busy)

            foreach (var kv in comps)
            {
                var k = kv.Key;
                var b = kv.Value;
                var def = b.Data.Def;
                if (def == Blocks.RedstoneDust)
                {
                    int p = dustPower.TryGetValue(k, out int v) ? v : 0;
                    if ((b.Data.State & 15) != p) { var d = b.Data; d.State = (byte)((d.State & DustDot) | p); ops.Add(Op.State(k, d)); } // (#244: the dot kept)
                }
                else if (WoodShelves.Is(def))
                {
                    // (#223) a shelf's power: [E] swaps with the hotbar while it's on (ShelfBlock.neighborChanged)
                    bool on = PoweredAt(k, -1), was = (b.Data.State & WoodShelves.Powered) != 0;
                    if (on != was)
                    {
                        var d = b.Data; d.State = (byte)(on ? d.State | WoodShelves.Powered : d.State & ~WoodShelves.Powered); ops.Add(Op.State(k, d));
                        BlockNet.ServerSound(BlockWorld.Instance.WorldCenter(k), on ? "shelf.activate" : "shelf.deactivate", 1f, 1f);
                    }
                }
                else if (def == Blocks.RedstoneLamp)
                {
                    bool on = PoweredAt(k, -1);
                    bool lit = (b.Data.State & 1) != 0;
                    // like Minecraft: a lamp lights at once but goes out 4 ticks after losing power (so a 2-tick pulse shows)
                    if (on || !lit) lampOffAt.Remove(k);
                    else
                    {
                        if (!lampOffAt.TryGetValue(k, out long offAt)) { lampOffAt[k] = Tick + 4; dirty = true; continue; }
                        if (offAt > Tick) { dirty = true; continue; }
                        lampOffAt.Remove(k);
                    }
                    if (lit != on) { var d = b.Data; d.State = (byte)(on ? 1 : 0); ops.Add(Op.State(k, d)); }
                }
                else if (def == Blocks.Piston || def == Blocks.StickyPiston)
                {
                    if (IsMoving(k)) { if (updated.Contains(k)) keep.Add(k); dirty = true; continue; } // being carried: it acts once it lands
                    if (!updated.Contains(k)) continue; // (#185: no block update, no look at its power: Minecraft's)
                    // (#160: quasi-connectivity, Java's: also powered by whatever would power the space above it, the
                    // piston itself aside, so Java builds that rely on it work)
                    bool on = PoweredAt(k, b.Data.Facing) || (QuasiConnectivity && PoweredAt(k.Offset((int)Face.Up), (int)Face.Down));
                    bool ext = (b.Data.State & 1) != 0;
                    if (on && !ext)
                    {
                        if (pistonBusyUntil.TryGetValue(k, out long until) && until > Tick) { keep.Add(k); dirty = true; continue; }
                        settling = false;
                        if (TryExtend(k, b, ops)) pistonBusyUntil[k] = Tick + 3;
                        else if (settling) { keep.Add(k); dirty = true; } // (what it pushes is still landing: next pass)
                    }
                    else if (!on && ext)
                    {
                        // like Minecraft, a piston can always retract. A sticky piston that loses power while the blocks it
                        // pushed are still moving (a pulse of 2 ticks or less) leaves them there instead of pulling them
                        // back: that's what makes flying machines go. Having pushed nothing, it pulls as usual.
                        bool drop = pushedUntil.TryGetValue(k, out long pu) && pu >= Tick;
                        Retract(k, b, ops, pull: !drop);
                        pushedUntil.Remove(k);
                        pistonBusyUntil[k] = Tick + 3;
                    }
                }
                else if (def == Blocks.TNT)
                {
                    if (PoweredAt(k, -1)) ServerLogic.Ignite(k, 80);
                }
                else if (def.Shape == BlockShape.Door || def.Shape == BlockShape.Trapdoor || def.Shape == BlockShape.Gate)
                {
                    // (#160: Minecraft's: it opens when it gets power and shuts when the power goes, on the change only, so
                    // [E] still works in between; a door's two halves count together)
                    bool on = PoweredAt(k, -1);
                    if (def.Shape == BlockShape.Door) on |= PoweredAt(k.Offset((b.Data.State & 2) != 0 ? (int)Face.Down : (int)Face.Up), -1);
                    if (on) nowPowered.Add(k);
                    if (on != poweredLastTick.Contains(k) && ((b.Data.State & 1) != 0) != on)
                    {
                        var d = b.Data; d.State = (byte)(on ? d.State | 1 : d.State & ~1);
                        ops.Add(Op.State(k, d));
                        if (def.Shape == BlockShape.Trapdoor || (b.Data.State & 2) == 0)
                            BlockNet.ServerSound(world.WorldCenter(k), ServerLogic.DoorSound(def, on), 0.8f, def.Shape == BlockShape.Door ? 1f : 1.15f);
                    }
                }
                else if (def == Blocks.PoweredRail || def == Blocks.ActivatorRail)
                {
                    // powered itself, or by a powered one of its kind up to 8 rails along the line (Minecraft's)
                    bool on = PoweredAt(k, -1);
                    // (along the line each way, over slopes up and down, as Minecraft's findPoweredRailSignal; it had
                    // only looked level and a block up, so power stopped at the foot of a slope, #176)
                    if (!on)
                        foreach (var ex0 in Rails.Exits(b.Data.State & 15))
                        {
                            var cur = k; var dir = ex0.dir; int up = ex0.up;
                            for (int i = 0; i < 8 && !on; i++)
                            {
                                var nk = Rails.NextAlong(world, cur, dir, up, def, out int ns);
                                if (nk == null) break;
                                on = PoweredAt(nk.Value, -1);
                                cur = nk.Value;
                                var onward = Rails.Exits(ns).Where(e => e.dir == dir).ToList();
                                if (onward.Count == 0) break;
                                up = onward[0].up;
                            }
                            if (on) break;
                        }
                    bool was = (b.Data.State & Rails.Powered) != 0;
                    if (on != was) { var d = b.Data; d.State = (byte)(on ? d.State | Rails.Powered : d.State & ~Rails.Powered); ops.Add(Op.State(k, d)); }
                }
                else if (Copper.IsBulb(def))
                {
                    // (Minecraft's: each time it gets power it turns on, or off)
                    bool on = PoweredAt(k, -1);
                    if (on) nowPowered.Add(k);
                    if (on && !poweredLastTick.Contains(k)) { var d = b.Data; d.State = (byte)(d.State ^ 1); ops.Add(Op.State(k, d)); BlockNet.ServerSound(world.WorldCenter(k), "click", 0.5f, 1.5f); }
                }
                else if (def == Blocks.Hopper)
                {
                    bool locked = PoweredAt(k, -1);
                    if (((b.Data.State & 1) != 0) != locked) { var d = b.Data; d.State = (byte)(locked ? 1 : 0); ops.Add(Op.State(k, d)); }
                }
                else if (def == Blocks.Dropper || def == Blocks.Dispenser || def == Blocks.Crafter)
                {
                    bool on = PoweredAt(k, -1) || (QuasiConnectivity && PoweredAt(k.Offset((int)Face.Up), (int)Face.Down));
                    if (on) nowPowered.Add(k);
                    if (on && !poweredLastTick.Contains(k)) { var fk = k; Schedule(4, () => ItemTransport.ServerFire(fk)); }
                }
                else if (def == Blocks.NoteBlock)
                {
                    bool on = PoweredAt(k, -1);
                    if (on) nowPowered.Add(k);
                    if (on && !poweredLastTick.Contains(k)) ServerLogic.PlayNote(k, b.Data.State);
                }
                else if (def == Blocks.Repeater)
                {
                    // input: what's behind it (dust with power, a source pointing in, a powered block); the output follows
                    // after the delay (a pulse shorter than the delay comes out as long as the delay, like Minecraft)
                    var back = k.Offset(Faces.Opposite(b.Data.Facing));
                    // (#244) Minecraft's isLocked: a powered repeater or comparator pointing into its side holds it as it is
                    bool locked = false;
                    foreach (int f in Horizontal)
                    {
                        if (f == b.Data.Facing || f == Faces.Opposite(b.Data.Facing)) continue;
                        var sk = k.Offset(f); var sb = world.Get(sk);
                        if (sb != null && (sb.Data.Def == Blocks.Repeater || sb.Data.Def == Blocks.Comparator) && IsActiveSource(sb) && PowersInto(sk, sb, k)) locked = true;
                    }
                    if (locked != ((b.Data.State & RepeaterLocked) != 0)) { var ld = b.Data; ld.State = (byte)(ld.State ^ RepeaterLocked); ops.Add(Op.State(k, ld)); }
                    if (locked) continue;
                    bool input = RearSignal(back, k) > 0;
                    bool on = (b.Data.State & 1) != 0;
                    if (input != on && !repeaterPending.Contains(k))
                    {
                        repeaterPending.Add(k);
                        var key = k; bool to = input;
                        Schedule(2 * (1 + ((b.Data.State >> 1) & 3)), () =>
                        {
                            repeaterPending.Remove(key);
                            var rb = W?.Get(key);
                            if (rb == null || rb.Data.Def != Blocks.Repeater || (rb.Data.State & RepeaterLocked) != 0) return; // (#244: locked meanwhile: as it is)
                            var d = rb.Data; d.State = (byte)((d.State & ~1) | (to ? 1 : 0));
                            BlockNet.ServerBroadcastOp(Op.State(key, d));
                            MarkDirty();
                        });
                    }
                }
                else if (def == Blocks.Comparator)
                {
                    // behind it: a signal, or how full the chest / furnace / jukebox there is (or one behind the solid block
                    // there); at its sides: dust, repeaters and comparators pointing in, redstone blocks
                    var back = k.Offset(Faces.Opposite(b.Data.Facing));
                    int rear = RearSignal(back, k);
                    int fill = ContainerSignal(back);
                    if (fill < 0 && rear < 15 && world.IsSolidAt(back)) fill = ContainerSignal(back.Offset(Faces.Opposite(b.Data.Facing))); // (#244: Minecraft's getInputSignal: under 15, through the block)
                    if (fill >= 0) rear = Math.Max(rear, fill);
                    int sideIn = 0;
                    foreach (int f in Horizontal)
                    {
                        if (f == b.Data.Facing || f == Faces.Opposite(b.Data.Facing)) continue;
                        var sk = k.Offset(f);
                        var sb = world.Get(sk);
                        if (sb == null) continue;
                        var sd = sb.Data.Def;
                        if (sd == Blocks.RedstoneDust) sideIn = Math.Max(sideIn, dustPower.TryGetValue(sk, out int dp) ? dp : 0);
                        else if (sd == Blocks.RedstoneBlock && !IsMoving(sk)) sideIn = 15;
                        else if ((sd == Blocks.Repeater || sd == Blocks.Comparator) && IsActiveSource(sb) && PowersInto(sk, sb, k)) sideIn = Math.Max(sideIn, StrengthOf(sb));
                    }
                    bool subtract = (b.Data.State & ComparatorSubtract) != 0;
                    int output = subtract ? Math.Max(0, rear - sideIn) : rear >= sideIn ? rear : 0;
                    int cur = b.Data.State & 15;
                    if (output != cur && !comparatorPending.Contains(k))
                    {
                        // one redstone tick later (2 game ticks), like Minecraft
                        comparatorPending.Add(k);
                        var key = k; int to = output;
                        Schedule(2, () =>
                        {
                            comparatorPending.Remove(key);
                            var cb = W?.Get(key);
                            if (cb == null || cb.Data.Def != Blocks.Comparator) return;
                            var d = cb.Data; d.State = (byte)((d.State & ~15) | to);
                            BlockNet.ServerBroadcastOp(Op.State(key, d));
                            MarkDirty();
                        });
                    }
                }
                else if (def == Blocks.RedstoneTorch)
                {
                    var support = ServerLogic.SupportOf(k, b.Data);
                    bool attachedPowered = strong.ContainsKey(support) || weak.ContainsKey(support);
                    bool lit = (b.Data.State & 1) == 0;
                    bool burned = torchBurnedUntil.TryGetValue(k, out long bu) && bu > Tick;
                    bool wantLit = !attachedPowered && !burned;
                    if (wantLit != lit && !torchPending.Contains(k))
                    {
                        torchPending.Add(k);
                        var key = k;
                        Schedule(2, () =>
                        {
                            torchPending.Remove(key);
                            var tb = W?.Get(key);
                            if (tb == null || tb.Data.Def != Blocks.RedstoneTorch) return;
                            // track toggles for burnout
                            if (!torchToggles.TryGetValue(key, out var list)) torchToggles[key] = list = new List<long>();
                            list.Add(Tick);
                            list.RemoveAll(t => t < Tick - 60);
                            var d = tb.Data;
                            if (list.Count > 8)
                            {
                                torchBurnedUntil[key] = Tick + 160; // (#244: Minecraft's RESTART_DELAY 160)
                                d.State = 1;
                                BlockNet.ServerSound(W.WorldCenter(key), "ignite", 1.6f, 0.5f);
                                Schedule(161, MarkDirty);
                            }
                            else d.State = (byte)((d.State & 1) ^ 1);
                            BlockNet.ServerBroadcastOp(Op.State(key, d));
                            MarkDirty();
                        });
                    }
                }
            }
            poweredLastTick.Clear();
            foreach (var k in nowPowered) poweredLastTick.Add(k);
            // (this pass's updates are used up; what it changes now updates for the next)
            updated.Clear(); foreach (var k in keep) updated.Add(k);
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
        }

        /// <summary>
        /// How full a container is, as a comparator reads it (-1: not one). Like Minecraft: 0 empty, else 1 + 14 x the
        /// fill (each slot's count over its stack size, averaged over the slots), rounded down. A jukebox gives its disc's
        /// number (1-15).
        /// </summary>
        public static int ContainerSignal(BlockKey k)
        {
            var world = W;
            var def = world?.DefAt(k);
            if (def == null) return -1;
            if (Copper.IsBulb(def)) return (W.Get(k).Data.State & 1) != 0 ? 15 : 0; // (#160: a lit bulb reads 15)
            if (def.Shape == BlockShape.Basin) return Composting.ComparatorLevel(W.Get(k));
            if (def == Blocks.Cake) return (7 - (W.Get(k).Data.State & 7)) * 2; // (#244: Minecraft's, 14 for a whole cake)
            if (def == Blocks.Lectern) return Lecterns.ComparatorLevel(k, W.Get(k));
            if (def == Blocks.ChiseledBookshelf) return Blocks.ShelfLastSlot.TryGetValue(k, out int ls) ? ls : 0;
            if (Blocks.IsChest(def))
            {
                // (a large chest: both halves, as Minecraft's comparators read it, #132)
                var halves = new List<BlockKey> { k };
                if (Chests.PartnerOf(k, out _) is BlockKey pk) halves.Add(pk);
                float sum = 0; bool any = false; int slots = 0;
                foreach (var hk in halves)
                {
                    int n = Chests.SlotsAt(hk); slots += n;
                    var c = Chests.Of(hk);
                    if (c == null) continue;
                    for (int i = 0; i < n; i++)
                    {
                        if (c.Count[i] <= 0 || c.Key[i] == null) continue;
                        any = true;
                        sum += c.Count[i] / (float)Mathf.Max(1, Inventory.MaxStackOf(c.Key[i]));
                    }
                }
                return any ? 1 + Mathf.FloorToInt(sum / slots * 14f) : 0;
            }
            if (def == Blocks.Furnace)
            {
                if (!Crafting.Furnaces.TryGetValue(k, out var f)) return 0;
                float sum = 0; bool any = false;
                void Slot(string key, int n) { if (n > 0) { any = true; sum += n / (float)Mathf.Max(1, key != null ? Inventory.MaxStackOf(key) : 64); } }
                Slot(f.In, f.InCount); Slot(f.FuelKey, f.Fuel); Slot(f.Out, f.OutCount);
                return any ? 1 + Mathf.FloorToInt(sum / 3f * 14f) : 0;
            }
            if (def == Blocks.BrewingStand)
            {
                // (#244, Minecraft's: how full its five slots are)
                if (!Brewing.Stands.TryGetValue(k, out var bs)) return 0;
                float sum = 0; bool any = false;
                foreach (var b in bs.Bottle) if (b != null) { any = true; sum += 1f; }
                if (bs.IngCount > 0) { any = true; sum += bs.IngCount / (float)Mathf.Max(1, Inventory.MaxStackOf(bs.IngKey)); }
                if (bs.Powder > 0) { any = true; sum += bs.Powder / 64f; }
                return any ? 1 + Mathf.FloorToInt(sum / 5f * 14f) : 0;
            }
            if (def == Blocks.Jukebox)
            {
                var b = world.Get(k);
                return b == null ? 0 : Mathf.Min(15, b.Data.State);
            }
            return -1;
        }

        static IEnumerable<BlockKey> DustNeighbors(BlockKey k)
        {
            var world = W;
            bool solidAbove = world.IsSolidAt(k.Offset((int)Face.Up));
            foreach (int f in Horizontal)
            {
                var n = k.Offset(f);
                if (world.DefAt(n) == Blocks.RedstoneDust) yield return n;
                var down = n.Offset((int)Face.Down);
                if (!world.IsSolidAt(n) && world.DefAt(down) == Blocks.RedstoneDust) yield return down;
                var up = n.Offset((int)Face.Up);
                if (!solidAbove && world.DefAt(up) == Blocks.RedstoneDust) yield return up;
            }
        }

        // ------------------------------------------------------------------ pistons
        static bool settling; // (a push met a block still arriving: the piston tries again, #185)

        /// <summary>How a cell behaves when a piston structure moves through it.</summary>
        static PistonStructure.Cell CellFor(BlockKey k)
        {
            var world = W;
            if (OutOfWorld(k)) return PistonStructure.Cell.Immovable;
            var b = world.Get(k);
            if (b == null) return ServerLogic.Obstructed(k) || Ground.IsUndugGround(k) ? PistonStructure.Cell.Immovable : PistonStructure.Cell.Empty;
            var def = b.Data.Def;
            if (KeepsThings(def, k)) return PistonStructure.Cell.Immovable; // (before the crushing below: a hopper or a lectern isn't a full block)
            if (!def.Solid && def.Shape != BlockShape.PistonHead) return PistonStructure.Cell.Crushable;
            if (Amethyst.PistonPops(def)) return PistonStructure.Cell.Crushable; // (#229: budding amethyst breaks, Minecraft's POPPED)
            if (b.AnimT < 1f || IsMoving(k)) { settling = true; return PistonStructure.Cell.Immovable; }
            if (Popped(def)) return PistonStructure.Cell.Crushable;
            if (!def.Pushable || def.Shape == BlockShape.PistonHead) return PistonStructure.Cell.Immovable;
            if ((def == Blocks.Piston || def == Blocks.StickyPiston) && (b.Data.State & 1) != 0) return PistonStructure.Cell.Immovable;
            return PistonStructure.Cell.Movable;
        }

        /// <summary>(#244) Minecraft's pistons don't move a block with a block entity (PistonBaseBlock.isPushable:
        /// !hasBlockEntity): a furnace, lectern, jukebox and the like stay put, as do their contents (they had been moved and
        /// what was in them lost or left behind). Anything holding a container of ours, and the blocks Minecraft gives one.</summary>
        static bool KeepsThings(BlockDef d, BlockKey k)
        {
            if (Chests.Of(k) != null) return true;
            string key = d.Key;
            return key == "furnace" || key == "blast_furnace" || key == "smoker" || key == "lectern" || key == "jukebox" || key == "enchanting_table"
                || key.StartsWith("chiseled_bookshelf") || key.StartsWith("daylight_detector") || key == "hopper" || key == "dispenser" || key == "dropper"
                || key == "barrel" || key == "crafter" || key == "comparator" || key.EndsWith("_shelf") || key.EndsWith("_sign") || key.EndsWith("campfire")
                || key == "beehive" || key == "bee_nest" || key == "spawner" || key.EndsWith("chest");
        }

        /// <summary>(#244, #133) What a piston breaks instead of moving: beds, bells, decorated pots and mob heads (Minecraft's
        /// PushReaction.DESTROY).</summary>
        static bool Popped(BlockDef d) => d.Shape == BlockShape.Bed || d.Key == "bell" || d.Key == "decorated_pot" || d.Key.EndsWith("_head") || d.Key.EndsWith("_skull");

        /// <summary>
        /// The edge of the world for piston-moved blocks, like Minecraft's build limit and world border: a flying machine
        /// stops there instead of flying on forever (about 250 m up, 1 km out; around the ship, 60 blocks).
        /// </summary>
        public static bool OutOfWorld(BlockKey k) => k.Frame == 0
            ? k.Pos.y > 180 || k.Pos.y < -400 || Mathf.Abs(k.Pos.x) > 700 || Mathf.Abs(k.Pos.z) > 700
            : Mathf.Abs(k.Pos.x) > 60 || Mathf.Abs(k.Pos.y) > 60 || Mathf.Abs(k.Pos.z) > 60;

        /// <summary>Moves a resolved structure one cell (crushing what's in the way); natural blocks leaving the ground open it.</summary>
        static void MoveStructure(PistonStructure.Result r, byte frame, short yoff, Vector3Int dir, List<Op> ops)
        {
            var world = W;
            BlockKey K(Vector3Int p) => new BlockKey(frame, yoff, p);
            foreach (var c in r.Crush)
            {
                var cb = world.Get(K(c));
                if (cb == null) continue;
                // (#229: a break, so a natural cell opens the ground behind it and what hangs on it pops off)
                ServerLogic.BreakBlock(K(c), true, null, ops);
            }
            var dest = new HashSet<Vector3Int>(r.Move.Select(p => p + dir));
            foreach (var p in r.Move)
            {
                var from = K(p);
                bool natural = (world.Get(from).Data.State & Blocks.NaturalGround) != 0;
                ops.Add(Op.Move(from, K(p + dir), 2));
                // pushing a natural block out of the ground opens the ground where it was (unless another block moves in)
                if (natural && !dest.Contains(p)) Schedule(1, () => Ground.OnRemoved(from));
            }
        }

        static PistonStructure.Result Resolve(BlockKey start, Vector3Int dir, BlockKey piston, bool pull, BlockKey? emptyHead = null)
        {
            var body = new HashSet<Vector3Int> { piston.Pos };
            return PistonStructure.Resolve(start.Pos, dir, body,
                p => emptyHead.HasValue && p == emptyHead.Value.Pos ? PistonStructure.Cell.Empty : CellFor(new BlockKey(start.Frame, start.YOff, p)),
                p => { var d = W.DefAt(new BlockKey(start.Frame, start.YOff, p)); return d == Blocks.Slime || d == Blocks.Honey; }, pull,
                (a, b) =>
                {
                    var da = W.DefAt(new BlockKey(start.Frame, start.YOff, a)); var db = W.DefAt(new BlockKey(start.Frame, start.YOff, b));
                    return !(da == Blocks.Slime && db == Blocks.Honey || da == Blocks.Honey && db == Blocks.Slime);
                });
        }

        static bool TryExtend(BlockKey p, BlockInstance piston, List<Op> ops)
        {
            var world = W;
            byte dir = piston.Data.Facing;
            var r = Resolve(p.Offset(dir), Faces.Dir[dir], p, pull: false);
            if (!r.Ok) return false;
            MoveStructure(r, p.Frame, p.YOff, Faces.Dir[dir], ops);
            if (r.Move.Count > 0) pushedUntil[p] = Tick + 2;
            var d = piston.Data; d.State = 1;
            ops.Add(Op.State(p, d));
            bool sticky = piston.Data.Def == Blocks.StickyPiston;
            ops.Add(Op.SetSlide(p.Offset(dir), new BlockData(Blocks.PistonHead.Id, dir, (byte)(sticky ? 1 : 0)), p));
            BlockNet.ServerSound(world.WorldCenter(p), "piston.out", UnityEngine.Random.Range(0.6f, 0.85f), 0.5f);
            ServerLogic.Noise(world.WorldCenter(p), 10f, 0.4f);
            Gravity.MarkDirty();
            return true;
        }

        static void Retract(BlockKey p, BlockInstance piston, List<Op> ops, bool pull = true)
        {
            var world = W;
            byte dir = piston.Data.Facing;
            var headKey = p.Offset(dir);
            var head = world.Get(headKey);
            if (head != null && head.Data.Def.Shape == BlockShape.PistonHead) ops.Add(Op.Remove(headKey, false));
            var d = piston.Data; d.State = 0;
            ops.Add(Op.State(p, d));
            if (pull && piston.Data.Def == Blocks.StickyPiston)
            {
                // the block stuck to the head comes back (with everything slime holds on to)
                var r = Resolve(headKey.Offset(dir), -Faces.Dir[dir], p, pull: true, emptyHead: headKey);
                if (r.Ok) MoveStructure(r, p.Frame, p.YOff, -Faces.Dir[dir], ops);
            }
            BlockNet.ServerSound(world.WorldCenter(p), "piston.in", UnityEngine.Random.Range(0.6f, 0.8f), 0.5f);
            Gravity.MarkDirty();
        }

        // ------------------------------------------------------------------ observers
        static readonly HashSet<BlockKey> observerBusy = new HashSet<BlockKey>();
        public static int ObserverPulses; // (dev/tests)

        /// <summary>Server: something changed at k (placed, broken, moved, toggled): observers looking at it pulse.</summary>
        public static void OnChanged(BlockKey k)
        {
            var world = W;
            if (world == null || !BlockNet.IsServer) return;
            for (int f = 0; f < 6; f++)
            {
                var o = k.Offset(Faces.Opposite((byte)f));
                var ob = world.Get(o);
                if (ob == null || ob.Data.Def != Blocks.Observer || ob.Data.Facing != f || IsMoving(o)) continue;
                StartSignal(o);
            }
        }

        static int Gen(BlockKey k) => observerGen.TryGetValue(k, out int g) ? g : 0;

        /// <summary>Like Minecraft: 2 ticks after seeing a change, an observer sends a 2-tick pulse out of its back.</summary>
        static void StartSignal(BlockKey o)
        {
            if (observerBusy.Contains(o)) return;
            var ob = W?.Get(o);
            if (ob == null || ob.Data.Def != Blocks.Observer || (ob.Data.State & 1) != 0) return;
            observerBusy.Add(o);
            int gen = Gen(o);
            Schedule(2, () => { if (Gen(o) == gen) SetObserver(o, true); });
            Schedule(4, () => { if (Gen(o) == gen) { SetObserver(o, false); observerBusy.Remove(o); } });
        }

        /// <summary>
        /// Server: a piston moved a block from one cell to the next. Like Minecraft, it lands 2 ticks later; an observer
        /// that lands fires (that's how flying machines keep going), unless it was moved mid-pulse: then it just goes off.
        /// Whatever was scheduled for either cell is forgotten (it was for the block that left, or the one now arriving).
        /// </summary>
        public static void OnMoved(BlockKey from, BlockKey to)
        {
            var world = W;
            if (world == null || !BlockNet.IsServer) return;
            foreach (var c in new[] { from, to })
            {
                observerGen[c] = Gen(c) + 1;
                observerBusy.Remove(c);
                pushedUntil.Remove(c);
                pistonBusyUntil.Remove(c);
            }
            movingUntil.Remove(from);
            movingUntil[to] = Tick + 2;
            var b = world.Get(to);
            int gen = Gen(to);
            if (b != null && b.Data.Def == Blocks.Observer)
            {
                bool wasOn = (b.Data.State & 1) != 0;
                Schedule(2, () =>
                {
                    if (Gen(to) != gen) return;
                    if (wasOn) SetObserver(to, false);
                    else StartSignal(to);
                });
            }
            // it may need to act once it lands; and landing, it updates what's around it (Minecraft's moving block puts
            // the block down with neighbour updates: a piston that couldn't push it while it was moving tries again, #185)
            Schedule(2, () => { var lb = W?.Get(to); BlockUpdates(to, null, lb != null ? lb.Data : (BlockData?)null); MarkDirty(); });
        }

        static void SetObserver(BlockKey k, bool on)
        {
            var b = W?.Get(k);
            if (b == null || b.Data.Def != Blocks.Observer) return;
            var d = b.Data; d.State = (byte)(on ? 1 : 0);
            if (on) ObserverPulses++;
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            MarkDirty();
        }

        /// <summary>Does this active source power the cell next to it? (Observers only out of their back.)</summary>
        static bool PowersInto(BlockKey src, BlockInstance b, BlockKey target) =>
            b.Data.Def == Blocks.Observer ? target.Equals(src.Offset(Faces.Opposite(b.Data.Facing)))
            : b.Data.Def == Blocks.Repeater || b.Data.Def == Blocks.Comparator ? target.Equals(src.Offset(b.Data.Facing)) // (only out of its front)
            : true;

        // ------------------------------------------------------------------ the target and the daylight detector (#160)
        public static int TargetHits; // (dev/tests)

        /// <summary>Host: a projectile hit a block. A target gives a pulse (an arrow's a second, a snowball's less), 15 at
        /// the middle of the face down to 1 at its edge, like Minecraft's.</summary>
        public static void ServerProjectileHit(RaycastHit hit, bool arrow)
        {
            if (!BlockNet.IsServer || hit.collider == null) return;
            var br = hit.collider.GetComponentInParent<BlockRef>();
            var world = W;
            var b = br != null ? world?.Get(br.Key) : null;
            // (#160: an arrow shatters a decorated pot, Minecraft's onProjectileHit)
            if (b != null && arrow && b.Data.Def == Blocks.DecoratedPot) { Pots.ServerShatter(br.Key); return; }
            if (b != null && Amethyst.ServerProjectileHit(br.Key, b.Data.Def)) return; // (#229: amethyst chimes, any projectile)
            if (b == null || b.Data.Def != Blocks.Target) return;
            var local = world.ToFrameLocal(br.Key.Frame, hit.point) - world.LocalCenter(br.Key);
            var n = world.ToFrameLocalDir(br.Key.Frame, hit.normal);
            // (how far off the face's middle, 0..1, in the face's own two directions)
            float off = Mathf.Abs(n.x) > 0.5f ? Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.z)) : Mathf.Abs(n.y) > 0.5f ? Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.z)) : Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y));
            int strength = Mathf.Clamp(Mathf.CeilToInt(15f * (1f - off / (BlockWorld.S * 0.5f))), 1, 15);
            var key = br.Key;
            var d = b.Data; d.State = (byte)strength;
            BlockNet.ServerBroadcastOp(Op.State(key, d));
            TargetHits++;
            MarkDirty();
            Schedule(arrow ? 20 : 8, () =>
            {
                var t = W?.Get(key);
                if (t == null || t.Data.Def != Blocks.Target) return;
                var dd = t.Data; dd.State = 0;
                BlockNet.ServerBroadcastOp(Op.State(key, dd));
                MarkDirty();
            });
        }

        /// <summary>The daylight a detector sees: 0-15, Minecraft's curve over the game's day (6 AM to midnight), none
        /// indoors (the facility, under a roof of blocks).</summary>
        public static int DaylightAt(BlockKey k)
        {
            var world = W; var tod = TimeOfDay.Instance;
            if (world == null || tod == null) return 0;
            var at = world.WorldCenter(k);
            if (at.y < -100f || Physics.Raycast(at + Vector3.up * (BlockWorld.S * 0.45f), Vector3.up, 200f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) return 0;
            float hours = tod.normalizedTimeOfDay * tod.numberOfHours; // (hours since 6 AM)
            float sun = Mathf.Sin(Mathf.Clamp01(hours / 13f) * Mathf.PI); // (up at 6 AM, highest at half past twelve, down by 7 PM)
            return Mathf.Clamp(Mathf.RoundToInt(sun * 15f), 0, 15);
        }

        static void PollDaylight()
        {
            var world = W;
            if (world == null || Blocks.DaylightDetector == null) return;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                if (kv.Value.Data.Def != Blocks.DaylightDetector) continue;
                int s = DaylightAt(kv.Key);
                bool inverted = (kv.Value.Data.State & 16) != 0;
                if (inverted) s = 15 - s;
                if ((kv.Value.Data.State & 15) != s) { var d = kv.Value.Data; d.State = (byte)((d.State & 16) | s); ops.Add(Op.State(kv.Key, d)); }
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); MarkDirty(); }
        }

        // ------------------------------------------------------------------ tripwires (#160)
        public static int Tripped; // (dev/tests)

        /// <summary>Each hook: hooked up when a line of tripwire (up to 40) runs out of its front to a hook facing back;
        /// tripped while anything (someone, a monster, an item) is on the line.</summary>
        static void PollTripwires()
        {
            var world = W;
            if (world == null || Blocks.TripwireHook == null) return;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                if (kv.Value.Data.Def != Blocks.TripwireHook) continue;
                byte f = kv.Value.Data.Facing;
                bool hooked = false, tripped = false;
                var k = kv.Key;
                var cells = new List<BlockKey>();
                for (int i = 0; i < 41; i++)
                {
                    k = k.Offset(f);
                    var b = world.Get(k);
                    if (b == null) break;
                    if (b.Data.Def == Blocks.TripwireHook) { hooked = b.Data.Facing == Faces.Opposite(f) && cells.Count > 0; break; }
                    if (b.Data.Def != Blocks.Tripwire) break;
                    cells.Add(k);
                }
                if (hooked)
                    foreach (var c in cells)
                    {
                        var at = world.WorldCenter(c) + world.FrameDirToWorld(c.Frame, Vector3.down) * (BlockWorld.S * 0.25f);
                        foreach (var h in Physics.OverlapBox(at, new Vector3(0.45f, 0.3f, 0.45f) * BlockWorld.S, world.FrameRotation(c.Frame), (1 << 3) | (1 << 19) | (1 << 6), QueryTriggerInteraction.Collide))
                        {
                            var pc = h.GetComponentInParent<PlayerControllerB>();
                            var e = h.GetComponentInParent<EnemyAICollisionDetect>();
                            var g = h.GetComponentInParent<GrabbableObject>();
                            if ((pc != null && pc.isPlayerControlled && !pc.isPlayerDead) || (e != null && e.mainScript != null && !e.mainScript.isEnemyDead) || (g != null && !g.isHeld && !g.isPocketed)) { tripped = true; break; }
                        }
                        if (tripped) break;
                    }
                int s = (hooked ? 1 : 0) | (tripped ? 2 : 0);
                if (kv.Value.Data.State != s)
                {
                    if (tripped && (kv.Value.Data.State & 2) == 0) { Tripped++; BlockNet.ServerSound(world.WorldCenter(kv.Key), "click", 0.6f, 0.7f); }
                    var d = kv.Value.Data; d.State = (byte)s; ops.Add(Op.State(kv.Key, d));
                }
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); MarkDirty(); }
        }

        // ------------------------------------------------------------------ pressure plates
        static void PollPressurePlates()
        {
            var world = W;
            if (world == null) return;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                var def = kv.Value.Data.Def;
                if (def.Shape != BlockShape.Plate) continue;
                // (#160, Minecraft's: stone plates feel players and mobs; wooden and weighted ones items too; a weighted
                // plate's signal grows with how many things are on it)
                bool items = def != Blocks.PressurePlate && def.Key != "polished_blackstone_pressure_plate";
                var c = world.WorldCenter(kv.Key) + world.FrameDirToWorld(kv.Key.Frame, Vector3.down) * (BlockWorld.S * 0.3f);
                var hits = Physics.OverlapBox(c, new Vector3(0.45f, 0.25f, 0.45f) * BlockWorld.S, world.FrameRotation(kv.Key.Frame), (1 << 3) | (1 << 19) | (items ? 1 << 6 : 0), QueryTriggerInteraction.Collide);
                var on = new HashSet<UnityEngine.Object>();
                foreach (var h in hits)
                {
                    var pc = h.GetComponentInParent<PlayerControllerB>();
                    if (pc != null && pc.isPlayerControlled && !pc.isPlayerDead) { on.Add(pc); continue; }
                    var e = h.GetComponentInParent<EnemyAICollisionDetect>();
                    if (e != null && e.mainScript != null && !e.mainScript.isEnemyDead) { on.Add(e.mainScript); continue; }
                    var g = items ? h.GetComponentInParent<GrabbableObject>() : null;
                    if (g != null && !g.isHeld && !g.isPocketed) on.Add(g);
                }
                int n = on.Count;
                int strength = n == 0 ? 0 : def.Key == "light_weighted_pressure_plate" ? Math.Min(n, 15) : def.Key == "heavy_weighted_pressure_plate" ? Math.Min((n + 9) / 10, 15) : 15;
                int was = kv.Value.Data.State & 15;
                if (strength > 0) plateLastSeen[kv.Key] = Tick;
                int hold = def.Key.Contains("weighted") ? 10 : 20; // (ticks it stays down after: Minecraft's)
                if (strength > 0 && strength != was)
                {
                    var d = kv.Value.Data; d.State = (byte)strength; ops.Add(Op.State(kv.Key, d));
                    if (was == 0) BlockNet.ServerSound(world.WorldCenter(kv.Key), "click", 0.6f, 0.5f);
                }
                else if (strength == 0 && was != 0 && (!plateLastSeen.TryGetValue(kv.Key, out long seen) || Tick - seen > hold))
                {
                    var d = kv.Value.Data; d.State = 0; ops.Add(Op.State(kv.Key, d));
                    BlockNet.ServerSound(world.WorldCenter(kv.Key), "click", 0.5f, 0.5f);
                }
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); MarkDirty(); }
        }
    }

    /// <summary>Dropped stacks merge with each other and get vacuumed into matching stacks in nearby players' hotbars.</summary>
    public static class StackMerging
    {
        // (reused every tick: no new lists)
        static readonly List<StackItem> ground = new List<StackItem>();
        static readonly Dictionary<(int, int, int), List<StackItem>> cells = new Dictionary<(int, int, int), List<StackItem>>();
        static readonly Stack<List<StackItem>> spareLists = new Stack<List<StackItem>>();
        static (int, int, int) CellOf(Vector3 p) => (Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)); // 1 m: the merge range

        public static void Tick()
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return;
            // the stacks lying on the ground (#18: kept as a list as they spawn and go, instead of a search of the whole
            // scene four times a second, which got slower the more there was around)
            ground.Clear();
            foreach (var s in StackItem.Live)
                if (s != null && s.IsSpawned && !s.isHeld && !s.isHeldByEnemy && !s.isPocketed && s.Count > 0 && s.playerHeldBy == null && !s.Despawning) ground.Add(s);
            if (ground.Count == 0) return;
            // ...and where they are, by 1 m cell: a stack only looks for others to merge with in the cells around it
            foreach (var l in cells.Values) { l.Clear(); spareLists.Push(l); }
            cells.Clear();
            foreach (var s in ground)
            {
                var c = CellOf(s.transform.position);
                if (!cells.TryGetValue(c, out var l)) cells[c] = l = spareLists.Count > 0 ? spareLists.Pop() : new List<StackItem>();
                l.Add(s);
            }
            foreach (var g in ground)
            {
                if (g.Despawning || Time.time - g.SpawnTime < 0.6f) continue;
                // into players (not one just thrown: it would jump straight back into the thrower's stack)
                if (Time.time < g.NoMergeUntil) continue;
                foreach (var p in sor.allPlayerScripts)
                {
                    if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                    if (Vector3.Distance(p.transform.position + Vector3.up * 0.5f, g.transform.position) > 1.6f) continue;
                    foreach (var slot in p.ItemSlots)
                    {
                        if (slot is StackItem s && s != g && s.ItemKey == g.ItemKey && ItemNames.IdOf(s) == ItemNames.IdOf(g) && StackData.Of(s) == StackData.Of(g) && s.Count < s.MaxStack) // (#228; #244: its number)
                        {
                            int move = Mathf.Min(g.Count, s.MaxStack - s.Count);
                            // found as scrap (ender pearls): the stack in hand takes its value along
                            if (g.scrapValue > 0 && move == g.Count) { s.SetScrapValue(s.scrapValue + g.scrapValue); BlockNet.ServerScrapValue(s.NetworkObjectId, s.scrapValue); }
                            s.ServerSetCount(s.Count + move);
                            g.ServerSetCount(g.Count - move, despawnIfEmpty: true);
                            BlockNet.ServerSound(p.transform.position, "pop", UnityEngine.Random.Range(1.4f, 2.0f), 0.35f);
                            break;
                        }
                    }
                    if (g.Count <= 0) break;
                }
                if (g.Count <= 0 || g.Despawning) continue;
                // scrap on the ground (pearls found in the facility) stays as the game spawned it: it tracks every piece
                // and its value (merging despawned pieces it still referenced, and lost their value)
                if (g.itemProperties.isScrap && g.scrapValue > 0) continue;
                // into other ground stacks (within 1 m: in this cell or a neighbouring one)
                var gc = CellOf(g.transform.position);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!cells.TryGetValue((gc.Item1 + dx, gc.Item2 + dy, gc.Item3 + dz), out var near)) continue;
                            foreach (var o in near)
                            {
                                if (o == g || o.Despawning || g.Despawning || o.ItemKey != g.ItemKey || o.Count <= 0 || ItemNames.IdOf(o) != ItemNames.IdOf(g) || StackData.Of(o) != StackData.Of(g)) continue; // (#228: named differently; #244: numbered)
                                // (a stack that just spawned is on its way to someone: crafted, bought or given items land at
                                // their feet to be picked up, and vanished into a matching stack lying there)
                                if (Time.time - o.SpawnTime < 0.6f) continue;
                                if (o.itemProperties.isScrap && o.scrapValue > 0) continue;
                                if (Vector3.Distance(o.transform.position, g.transform.position) > 1.0f) continue;
                                if (o.Count + g.Count > g.MaxStack) continue;
                                if (o.NetworkObjectId < g.NetworkObjectId) continue; // merge into the older one
                                g.ServerSetCount(g.Count + o.Count);
                                o.ServerSetCount(0, despawnIfEmpty: true);
                            }
                        }
            }
        }
    }

    /// <summary>Monsters that are stuck against blocks gnaw through them.</summary>
    public static class EnemyBlockBreaking
    {
        static readonly Dictionary<BlockKey, float> damage = new Dictionary<BlockKey, float>();

        public static void Tick(float dt)
        {
            if (!Plugin.EnemiesBreakBlocks.Value) return;
            var rm = RoundManager.Instance;
            var world = BlockWorld.Instance;
            if (rm == null || world == null || world.Blocks.Count == 0) return;
            var touched = new HashSet<BlockKey>();
            var sor = StartOfRound.Instance;
            foreach (var e in rm.SpawnedEnemies)
            {
                if (e == null || e.isEnemyDead || e.agent == null || !e.agent.enabled) continue;
                if (e is MinecraftMobAI mm && !mm.GnawsBlocks) continue; // (a pig, a horse, a pet: no)
                var eye = e.transform.position + Vector3.up * 1.0f;
                BlockRef best = null;
                // 1) a block standing between the monster and a nearby living player
                foreach (var pl in sor.allPlayerScripts)
                {
                    if (pl == null || !pl.isPlayerControlled || pl.isPlayerDead) continue;
                    var target = pl.transform.position + Vector3.up * 1.0f;
                    var dir = target - eye;
                    if (dir.magnitude > 7f) continue;
                    if (Physics.Raycast(eye, dir.normalized, out var hit, dir.magnitude, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore) && hit.distance < 1.6f + BlockWorld.S * 0.5f)
                    {
                        var br = hit.collider.GetComponent<BlockRef>();
                        if (br != null) { best = br; break; }
                    }
                }
                // 2) stuck against blocks while trying to go somewhere
                if (best == null && e.agent.velocity.magnitude < 0.6f &&
                    (e.movingTowardsTargetPlayer || e.targetPlayer != null || e.agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathComplete))
                {
                    float bestD = float.MaxValue;
                    foreach (var h in Physics.OverlapSphere(eye, 0.9f + BlockWorld.S * 0.5f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore))
                    {
                        var br = h.GetComponent<BlockRef>();
                        if (br == null) continue;
                        float d = Vector3.Distance(h.transform.position, eye);
                        if (d < bestD) { bestD = d; best = br; }
                    }
                }
                if (best == null) continue;
                var bi = world.Get(best.Key);
                if (bi == null) continue;
                var def = bi.Data.Def;
                if (Blocks.NaturalVein(bi.Data) || (def == Blocks.Stone && bi.Data.State == 1) || (bi.Data.State & Blocks.NaturalGround) != 0 || def.Unbreakable) continue; // natural ground / ore veins
                float hardness = def == Blocks.Obsidian ? 90f : Mathf.Clamp(def.HandTime * 1.2f, 2f, 20f);
                damage.TryGetValue(best.Key, out float cur);
                cur += dt;
                damage[best.Key] = cur;
                touched.Add(best.Key);
                int stage = Mathf.Clamp((int)(cur / hardness * 10f), 0, 9);
                BlockNet.ServerCrack(best.Key, (sbyte)stage);
                if (cur >= hardness)
                {
                    damage.Remove(best.Key);
                    BlockNet.ServerCrack(best.Key, -1);
                    ServerLogic.BreakBlock(best.Key, false);
                }
            }
            // decay damage on blocks nobody is chewing
            foreach (var k in damage.Keys.ToList())
            {
                if (touched.Contains(k)) continue;
                damage[k] -= dt * 0.5f;
                if (damage[k] <= 0) { damage.Remove(k); BlockNet.ServerCrack(k, -1); }
            }
        }
    }
}
