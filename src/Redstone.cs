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
        static bool dirty;
        static float acc;
        public static long Tick;
        static readonly List<(long due, Action act)> scheduled = new List<(long, Action)>();
        static readonly HashSet<BlockKey> poweredLastTick = new HashSet<BlockKey>();
        static readonly Dictionary<BlockKey, long> pistonBusyUntil = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, List<long>> torchToggles = new Dictionary<BlockKey, List<long>>();
        static readonly Dictionary<BlockKey, long> torchBurnedUntil = new Dictionary<BlockKey, long>();
        static readonly HashSet<BlockKey> torchPending = new HashSet<BlockKey>();
        static readonly Dictionary<BlockKey, long> plateLastSeen = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, long> lampOffAt = new Dictionary<BlockKey, long>();
        // blocks a piston is moving: they arrive 2 ticks later, and until then (like Minecraft's moving blocks) they
        // can't be pushed, don't give power and, if they're pistons, don't fire
        static readonly Dictionary<BlockKey, long> movingUntil = new Dictionary<BlockKey, long>();
        // a piston whose pushed blocks are still moving: losing power now leaves them where they were pushed
        static readonly Dictionary<BlockKey, long> pushedUntil = new Dictionary<BlockKey, long>();
        static readonly Dictionary<BlockKey, int> observerGen = new Dictionary<BlockKey, int>();

        static bool IsMoving(BlockKey k) => movingUntil.TryGetValue(k, out long t) && t > Tick;

        public static void MarkDirty() => dirty = true;

        public static void Schedule(int ticks, Action a) => scheduled.Add((Tick + Math.Max(1, ticks), a));

        public static void Reset()
        {
            scheduled.Clear();
            poweredLastTick.Clear();
            pistonBusyUntil.Clear();
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
            if (Tick % 5 == 0) StackMerging.Tick();
            if (Tick % 10 == 0) EnemyBlockBreaking.Tick(0.5f);
            if (Tick % 10 == 0) Fire.ServerTick(hurtMonsters: Tick % 20 == 0);
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
            if (d == Blocks.Lever || d == Blocks.Button || d == Blocks.PressurePlate) return (b.Data.State & 1) != 0;
            if (d == Blocks.RedstoneTorch) return (b.Data.State & 1) == 0;
            if (d == Blocks.Observer) return (b.Data.State & 1) != 0;
            return false;
        }

        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        // ------------------------------------------------------------------ main recompute
        static void Recompute()
        {
            var world = W;
            if (world == null) return;
            var all = world.Blocks;
            var comps = all.Where(kv => kv.Value.Data.Def.IsRedstoneComponent).ToList();
            if (comps.Count == 0) { poweredLastTick.Clear(); return; }

            // 1. strongly powered solid blocks
            var strong = new HashSet<BlockKey>();
            foreach (var kv in comps)
            {
                var b = kv.Value;
                if (!IsActiveSource(b)) continue;
                var def = b.Data.Def;
                BlockKey target;
                if (def == Blocks.Lever || def == Blocks.Button) target = kv.Key.Offset(Faces.Opposite(b.Data.Facing));
                else if (def == Blocks.RedstoneTorch) target = kv.Key.Offset((int)Face.Up);
                else if (def == Blocks.PressurePlate) target = kv.Key.Offset((int)Face.Down);
                else if (def == Blocks.Observer) target = kv.Key.Offset(Faces.Opposite(b.Data.Facing));
                else continue;
                if (world.IsSolidAt(target)) strong.Add(target);
            }

            // 2. dust power (BFS from seeds, 15 -> 0)
            var dustPower = new Dictionary<BlockKey, int>();
            var queue = new Queue<BlockKey>();
            foreach (var kv in comps)
            {
                if (kv.Value.Data.Def != Blocks.RedstoneDust) continue;
                dustPower[kv.Key] = 0;
                bool seeded = false;
                for (int f = 0; f < 6 && !seeded; f++)
                {
                    var nk = kv.Key.Offset(f);
                    var n = world.Get(nk);
                    if (n == null) continue;
                    var nd = n.Data.Def;
                    if (nd == Blocks.RedstoneDust) continue;
                    if (IsActiveSource(n) && PowersInto(nk, n, kv.Key))
                    {
                        // torches don't power dust on the block they're attached to... close enough: allow
                        seeded = true;
                    }
                    else if (nd.Solid && strong.Contains(nk)) seeded = true;
                }
                if (seeded) { dustPower[kv.Key] = 15; queue.Enqueue(kv.Key); }
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

            // 3. weakly powered solid blocks (from dust on top / next to them)
            var weak = new HashSet<BlockKey>();
            foreach (var kv in dustPower)
            {
                if (kv.Value <= 0) continue;
                var below = kv.Key.Offset((int)Face.Down);
                if (world.IsSolidAt(below)) weak.Add(below);
                foreach (int f in Horizontal)
                {
                    var nk = kv.Key.Offset(f);
                    if (world.IsSolidAt(nk) && !world.Get(nk).Data.Def.IsRedstoneComponent) weak.Add(nk);
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
                    if (nd == Blocks.RedstoneDust && dustPower.TryGetValue(nk, out int dp) && dp > 0 && f != (int)Face.Down) return true;
                    if (nd.Solid && (strong.Contains(nk) || weak.Contains(nk))) return true;
                }
                return false;
            }

            var ops = new List<Op>();
            var nowPowered = new HashSet<BlockKey>();

            foreach (var kv in comps)
            {
                var k = kv.Key;
                var b = kv.Value;
                var def = b.Data.Def;
                if (def == Blocks.RedstoneDust)
                {
                    int p = dustPower.TryGetValue(k, out int v) ? v : 0;
                    if (b.Data.State != p) { var d = b.Data; d.State = (byte)p; ops.Add(Op.State(k, d)); }
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
                    if (IsMoving(k)) { dirty = true; continue; } // being carried: it acts once it lands
                    bool on = PoweredAt(k, b.Data.Facing);
                    bool ext = (b.Data.State & 1) != 0;
                    if (on && !ext)
                    {
                        if (pistonBusyUntil.TryGetValue(k, out long until) && until > Tick) { dirty = true; continue; }
                        if (TryExtend(k, b, ops)) pistonBusyUntil[k] = Tick + 3;
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
                else if (def == Blocks.NoteBlock)
                {
                    bool on = PoweredAt(k, -1);
                    if (on) nowPowered.Add(k);
                    if (on && !poweredLastTick.Contains(k)) ServerLogic.PlayNote(k, b.Data.State);
                }
                else if (def == Blocks.RedstoneTorch)
                {
                    var support = ServerLogic.SupportOf(k, b.Data);
                    bool attachedPowered = strong.Contains(support) || weak.Contains(support);
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
                                torchBurnedUntil[key] = Tick + 100;
                                d.State = 1;
                                BlockNet.ServerSound(W.WorldCenter(key), "ignite", 1.6f, 0.5f);
                                Schedule(101, MarkDirty);
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
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
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
        /// <summary>How a cell behaves when a piston structure moves through it.</summary>
        static PistonStructure.Cell CellFor(BlockKey k)
        {
            var world = W;
            if (OutOfWorld(k)) return PistonStructure.Cell.Immovable;
            var b = world.Get(k);
            if (b == null) return ServerLogic.Obstructed(k) || Ground.IsUndugGround(k) ? PistonStructure.Cell.Immovable : PistonStructure.Cell.Empty;
            var def = b.Data.Def;
            if (!def.Solid && def.Shape != BlockShape.PistonHead) return PistonStructure.Cell.Crushable;
            if (!def.Pushable || b.AnimT < 1f || IsMoving(k) || def.Shape == BlockShape.PistonHead) return PistonStructure.Cell.Immovable;
            if ((def == Blocks.Piston || def == Blocks.StickyPiston) && (b.Data.State & 1) != 0) return PistonStructure.Cell.Immovable;
            return PistonStructure.Cell.Movable;
        }

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
                ops.Add(Op.Remove(K(c), true));
                ServerLogic.SpawnDrop(cb.Data.Def, world.WorldCenter(K(c)));
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
                p => W.DefAt(new BlockKey(start.Frame, start.YOff, p)) == Blocks.Slime, pull);
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
            Schedule(2, MarkDirty); // it may need to act once it lands
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
            b.Data.Def != Blocks.Observer || target.Equals(src.Offset(Faces.Opposite(b.Data.Facing)));

        // ------------------------------------------------------------------ pressure plates
        static void PollPressurePlates()
        {
            var world = W;
            if (world == null) return;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                if (kv.Value.Data.Def != Blocks.PressurePlate) continue;
                var c = world.WorldCenter(kv.Key) + world.FrameDirToWorld(kv.Key.Frame, Vector3.down) * (BlockWorld.S * 0.3f);
                var hits = Physics.OverlapBox(c, new Vector3(0.45f, 0.25f, 0.45f) * BlockWorld.S, world.FrameRotation(kv.Key.Frame), (1 << 3) | (1 << 19), QueryTriggerInteraction.Collide);
                bool occupied = false;
                foreach (var h in hits)
                {
                    var pc = h.GetComponentInParent<PlayerControllerB>();
                    if (pc != null && pc.isPlayerControlled && !pc.isPlayerDead) { occupied = true; break; }
                    var e = h.GetComponentInParent<EnemyAICollisionDetect>();
                    if (e != null && e.mainScript != null && !e.mainScript.isEnemyDead) { occupied = true; break; }
                }
                bool pressed = (kv.Value.Data.State & 1) != 0;
                if (occupied) plateLastSeen[kv.Key] = Tick;
                if (occupied && !pressed)
                {
                    var d = kv.Value.Data; d.State = 1; ops.Add(Op.State(kv.Key, d));
                    BlockNet.ServerSound(world.WorldCenter(kv.Key), "click", 0.6f, 0.5f);
                }
                else if (!occupied && pressed && (!plateLastSeen.TryGetValue(kv.Key, out long seen) || Tick - seen > 20))
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
                        if (slot is StackItem s && s != g && s.ItemKey == g.ItemKey && s.Count < s.MaxStack)
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
                                if (o == g || o.Despawning || g.Despawning || o.ItemKey != g.ItemKey || o.Count <= 0) continue;
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
                if (def.DropsScrap || (def == Blocks.Stone && bi.Data.State == 1) || (bi.Data.State & Blocks.NaturalGround) != 0 || def.Unbreakable) continue; // natural ground / ore veins
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
