using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>Server-authoritative block rules. Everything here runs on the host only.</summary>
    public static class ServerLogic
    {
        public static int WorldGeometryMask => (1 << 8) | (1 << 11) | (1 << 0) | (1 << 25) | (1 << 26) | (1 << 28);

        public static PlayerControllerB PlayerFor(ulong clientId)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;
            foreach (var p in sor.allPlayerScripts)
                if (p != null && p.isPlayerControlled && p.actualClientId == clientId) return p;
            return null;
        }

        static BlockWorld W => BlockWorld.Instance;

        // ------------------------------------------------------------------ placing
        public static void HandlePlace(ulong sender, ulong stackId, BlockKey key, byte type, byte facing)
        {
            var world = W;
            var def = Blocks.Get(type);
            if (world == null || def == null || def.Shape == BlockShape.PistonHead) return;
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(stackId, out var no)) return;
            var stack = no.GetComponent<StackItem>();
            if (stack == null || stack.Count <= 0 || stack.BlockType != type) return;
            if (key.Frame == 0 && !world.WorldFrameAvailable) { BlockNet.ServerToast(sender, "You can't build out here."); return; }
            if (world.Has(key) && world.DefAt(key) != Blocks.Lava && world.DefAt(key) != Blocks.Water)
            {
                // a slab onto the open half of the same kind of slab: one full block of two
                var ex = world.Get(key);
                if (def.Shape == BlockShape.Slab && ex != null && ex.Data.Def == def && (ex.Data.State & 2) == 0 && ((ex.Data.State & 1) != 0) != (facing == (byte)Face.Down))
                {
                    var dd = ex.Data; dd.State = 2;
                    BlockNet.ServerBroadcastOp(Op.State(key, dd));
                    if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                    BlockNet.ServerSound(world.WorldCenter(key), "dig." + Sounds.Family(def), 0.8f, 1f);
                }
                return;
            }
            var player = PlayerFor(sender);
            var center = world.WorldCenter(key);
            if (player != null && Vector3.Distance(player.gameplayCamera.transform.position, center) > 9f * BlockWorld.S + 3f) return;

            byte state = 0;
            // (the half comes in the facing: a slab's Down = the top half; a trapdoor's +8 = the top half)
            if (def.Shape == BlockShape.Slab) { state = (byte)(facing == (byte)Face.Down ? 1 : 0); facing = (byte)Face.Up; }
            if (def.Shape == BlockShape.Trapdoor) { state = (byte)((facing & 8) != 0 ? 2 : 0); facing = (byte)(facing & 7); }
            if (def.Shape == BlockShape.Door)
            {
                var top = key.Offset((int)Face.Up);
                if (world.Has(top) || Obstructed(top, 0.6f)) { BlockNet.ServerToast(sender, "A door needs two blocks of room."); return; }
                BlockNet.ServerBroadcastOps(new List<Op> { Op.Set(key, new BlockData(type, facing, 0)), Op.Set(top, new BlockData(type, facing, 2)) });
                if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                BlockNet.ServerSound(center, "dig.wood", 0.8f, 1f);
                Gravity.MarkDirty();
                return;
            }
            var data = new BlockData(type, facing, state);
            BlockNet.ServerBroadcastOp(Op.Set(key, data));
            if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1); // creative: blocks never run out
            BlockNet.ServerSound(center, "dig." + Sounds.Family(def), 0.8f, 1f);
            Noise(center, 9f, 0.45f);
            Redstone.MarkDirty();
            Gravity.MarkDirty();
        }

        // ------------------------------------------------------------------ buckets (#19)
        /// <summary>Server: a player's bucket (already out of their hand) fills from a water source or pours one out; what
        /// they get back: the other bucket, or theirs again if nothing happened.</summary>
        public static void HandleBucket(ulong sender, BlockKey key, bool fill, bool fromMoon)
        {
            var world = W;
            string back = fill ? "bucket" : "water_bucket";
            if (world != null)
            {
                var b = fromMoon ? null : world.Get(key);
                var center = fromMoon ? Vector3.zero : world.WorldCenter(key);
                if (fill && fromMoon) back = "water_bucket";
                else if (fill && b != null && b.Data.Def == Blocks.Water && b.Data.State == 0)
                {
                    BlockNet.ServerBroadcastOp(Op.Remove(key, false));
                    BlockNet.ServerSound(center, "dig.slime", 0.4f, 1.4f);
                    back = "water_bucket";
                }
                else if (!fill && (b == null || b.Data.Def == Blocks.Water || b.Data.Def == Blocks.Lava || b.Data.Def == Blocks.Fire) && !Obstructed(key, 0.6f)
                         && (key.Frame != 0 || world.WorldFrameAvailable))
                {
                    if (b != null && b.Data.Def == Blocks.Lava) BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData((b.Data.State == 0 ? Blocks.Obsidian : Blocks.Cobblestone).Id, (byte)Face.Up, 0)));
                    else BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(Blocks.Water.Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(center, "dig.slime", 0.4f, 1.1f);
                    back = "bucket";
                }
            }
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] bucket fill={fill} moon={fromMoon} at {key.Pos} ({world?.Get(key)?.Data.Def.Key}:{world?.Get(key)?.Data.State}) -> {back}");
            Inventory.ServerSpawnFor(sender, back, 1);
        }

        // ------------------------------------------------------------------ breaking
        public static void HandleBreak(ulong sender, BlockKey key, bool withPickaxe)
        {
            var world = W;
            if (world == null) return;
            var bi = world.Get(key);
            if (bi == null) return;
            var def = bi.Data.Def;
            if (def.Unbreakable) return;
            McHud.ServerBlockBroken();
            var player = PlayerFor(sender);
            var tool = player != null ? player.currentlyHeldObjectServer as ToolItem : null;
            bool harvest = tool != null ? Blocks.CanHarvest(def, tool.Kind, tool.Tier) : Blocks.CanHarvest(def, ToolKind.None, 0);
            if (def == Blocks.Fire) { BlockNet.ServerSound(world.WorldCenter(key), "extinguish", Random.Range(1.6f, 2.2f), 0.5f); BreakBlock(key, false, player); return; }
            if (GameModes.IsCreative(sender)) { BreakBlock(key, false, player); return; } // creative: no drops, no XP (a chest still spills)
            BreakBlock(key, harvest, player);
            if (tool != null && def.Hardness > 0f) tool.ServerUse(1); // (#48: instant blocks, like torches, don't wear it)
            bool ore = def.DropsScrap || def == Blocks.CoalOre;
            if (player != null && ore && harvest) BlockNet.ServerXp(sender, Random.Range(2, 6) + (def == Blocks.DiamondOre || def == Blocks.EmeraldOre ? 5 : 0));
            else if (player != null && !harvest && def.HarvestTier > 0) BlockNet.ServerToast(sender, def.HarvestTier == 1 ? "Needs a pickaxe to drop anything." : def.HarvestTier == 2 ? "Needs a stone pickaxe or better." : def.HarvestTier == 3 ? "Needs an iron pickaxe or better." : "Needs a diamond pickaxe.");
        }

        /// <summary>Removes a block (and dependent blocks), spawning drops.</summary>
        public static void BreakBlock(BlockKey key, bool drop, PlayerControllerB breaker = null, List<Op> into = null)
        {
            var world = W;
            var bi = world.Get(key);
            if (bi == null) return;
            var def = bi.Data.Def;
            var ops = into ?? new List<Op>();
            var center = world.WorldCenter(key);
            BlockDef dropDef = def;

            if (def.Shape == BlockShape.PistonHead)
            {
                // breaking the arm breaks the piston behind it
                var baseKey = key.Offset(Faces.Opposite(bi.Data.Facing));
                var b = world.Get(baseKey);
                if (b != null && (b.Data.Def == Blocks.Piston || b.Data.Def == Blocks.StickyPiston))
                {
                    ops.Add(Op.Remove(baseKey, true));
                    dropDef = b.Data.Def;
                }
                else dropDef = null;
            }
            else if ((def == Blocks.Piston || def == Blocks.StickyPiston) && (bi.Data.State & 1) != 0)
            {
                var headKey = key.Offset(bi.Data.Facing);
                var h = world.Get(headKey);
                if (h != null && h.Data.Def.Shape == BlockShape.PistonHead) ops.Add(Op.Remove(headKey, false));
            }
            ops.Add(Op.Remove(key, true));
            if (def.Shape == BlockShape.Door)
            {
                var other = key.Offset((bi.Data.State & 2) != 0 ? (int)Face.Down : (int)Face.Up);
                var ob = world.Get(other);
                if (ob != null && ob.Data.Def.Shape == BlockShape.Door) ops.Add(Op.Remove(other, true));
            }

            // attached things pop off
            for (int f = 0; f < 6; f++)
            {
                var nk = key.Offset(f);
                var n = world.Get(nk);
                if (n == null) continue;
                var nd = n.Data.Def;
                if (nd.StandsAlone) continue;
                BlockKey support = SupportOf(nk, n.Data);
                if (support.Equals(key))
                {
                    ops.Add(Op.Remove(nk, true));
                    if (drop) SpawnDrop(nd, world.WorldCenter(nk));
                }
            }

            if (into == null) BlockNet.ServerBroadcastOps(ops);
            if ((bi.Data.State & Blocks.NaturalGround) != 0) OnNaturalRemoved(key);
            if (drop && dropDef != null) SpawnDrop(dropDef, center);
            if (drop && def.Shape == BlockShape.Slab && (bi.Data.State & 2) != 0) SpawnDrop(def, center); // (two slabs)
            if (def == Blocks.Furnace) Crafting.ServerDropContents(key, center);
            if (def == Blocks.Chest) Chests.ServerDropContents(key, center);
            if (def == Blocks.Jukebox) Jukebox.ServerBroken(key, bi.Data.State, center);
            Noise(center, 10f, 0.5f);
            Redstone.MarkDirty();
            Gravity.MarkDirty();
        }

        public static BlockKey SupportOf(BlockKey k, BlockData d)
        {
            var def = d.Def;
            switch (def.Shape)
            {
                case BlockShape.Torch:
                case BlockShape.Lever:
                case BlockShape.Button:
                case BlockShape.Ladder:
                    return k.Offset(Faces.Opposite(d.Facing));
                default:
                    return k.Offset((int)Face.Down);
            }
        }

        public static void SpawnDrop(BlockDef def, Vector3 pos)
        {
            if (def == null) return;
            var dropper = def;
            def = def.DropAs ?? def;
            if (def.DropsScrap)
            {
                ModItems.ServerSpawnScrap(def, pos);
                return;
            }
            if (def.DropKey != null)
            {
                if (Blocks.Get(def.DropKey) is BlockDef dd) def = dd;
                else if (ModItems.ByKey.TryGetValue(def.DropKey, out var di)) { ModItems.ServerSpawnStack(di, dropper.DropMax > 1 ? Random.Range(dropper.DropMin, dropper.DropMax + 1) : 1, pos); return; }
            }
            var item = ModItems.ItemForBlock(def);
            if (item == null) return;
            // (some drop several: redstone ore gives 4-5 dust)
            ModItems.ServerSpawnStack(item, dropper.DropMax > 1 ? Random.Range(dropper.DropMin, dropper.DropMax + 1) : 1, pos);
        }

        public static void HandleGroundDig(ulong sender, Vector3 point, Vector3 normal) => Ground.Dig(sender, point, normal);
        public static void ResetGround() => Ground.Reset();
        static void OnNaturalRemoved(BlockKey key) => Ground.OnRemoved(key);

        // ------------------------------------------------------------------ using
        public static void HandleUse(ulong sender, BlockKey key)
        {
            var world = W;
            var bi = world?.Get(key);
            if (bi == null) return;
            var def = bi.Data.Def;
            var pos = world.WorldCenter(key);
            if (def.Shape == BlockShape.Door)
            {
                bool open = (bi.Data.State & 1) == 0;
                var ops = new List<Op>();
                foreach (var k in new[] { key, key.Offset((bi.Data.State & 2) != 0 ? (int)Face.Down : (int)Face.Up) })
                {
                    var hb = world.Get(k);
                    if (hb == null || hb.Data.Def.Shape != BlockShape.Door) continue;
                    var d = hb.Data; d.State = (byte)(open ? d.State | 1 : d.State & ~1);
                    ops.Add(Op.State(k, d));
                }
                BlockNet.ServerBroadcastOps(ops);
                BlockNet.ServerSound(pos, open ? "door.open" : "door.close", 0.8f, Random.Range(0.9f, 1.1f));
                return;
            }
            if (def == Blocks.Jukebox) { Jukebox.ServerEject(key); return; }
            if (def.Shape == BlockShape.Repeater)
            {
                // [E]: the next delay, 1 to 4 ticks
                var d = bi.Data;
                int delay = ((d.State >> 1) & 3) + 1 & 3;
                d.State = (byte)((d.State & ~6) | (delay << 1));
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", 0.4f, 0.5f);
                Redstone.MarkDirty();
                return;
            }
            if (def.Shape == BlockShape.Trapdoor)
            {
                var d = bi.Data; d.State = (byte)(d.State ^ 1);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, (d.State & 1) != 0 ? "door.open" : "door.close", 0.7f, Random.Range(1.1f, 1.25f));
                return;
            }
            if (def == Blocks.Lever)
            {
                var d = bi.Data; d.State = (byte)(d.State ^ 1);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", (d.State & 1) != 0 ? 0.6f : 0.5f, 0.6f);
                Redstone.MarkDirty();
            }
            else if (def == Blocks.Button)
            {
                if ((bi.Data.State & 1) != 0) return;
                var d = bi.Data; d.State = 1;
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", 0.6f, 0.6f);
                Redstone.Schedule(20, () =>
                {
                    var b = W?.Get(key);
                    if (b == null || b.Data.Def != Blocks.Button) return;
                    var dd = b.Data; dd.State = 0;
                    BlockNet.ServerBroadcastOp(Op.State(key, dd));
                    BlockNet.ServerSound(W.WorldCenter(key), "click", 0.5f, 0.6f);
                    Redstone.MarkDirty();
                });
                Redstone.MarkDirty();
            }
            else if (def == Blocks.NoteBlock)
            {
                var d = bi.Data; d.State = (byte)((d.State + 1) % 25);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                PlayNote(key, d.State);
            }
        }

        public static void PlayNote(BlockKey key, int note)
        {
            var pos = W.WorldCenter(key);
            string inst = "note.harp";
            var below = W.DefAt(key.Offset((int)Face.Down));
            if (below != null)
            {
                if (below == Blocks.Planks || below == Blocks.DarkPlanks || below == Blocks.Log || below == Blocks.Bookshelf) inst = "note.bass";
                else if (below == Blocks.GoldBlock) inst = "note.bell";
                else if (below == Blocks.Glowstone) inst = "note.pling";
            }
            BlockNet.ServerSound(pos, inst, Mathf.Pow(2f, (note - 12) / 12f), 1f);
            Noise(pos, 14f, 0.6f);
        }

        // ------------------------------------------------------------------ TNT & explosions
        public static void Ignite(BlockKey key, int fuseTicks)
        {
            var bi = W?.Get(key);
            if (bi == null || bi.Data.Def != Blocks.TNT || (bi.Data.State & 1) != 0) return;
            var d = bi.Data; d.State = 1;
            BlockNet.ServerBroadcastOp(Op.State(key, d));
            BlockNet.ServerSound(W.WorldCenter(key), "fuse", 1f, 1f);
            Noise(W.WorldCenter(key), 12f, 0.6f);
            Redstone.Schedule(fuseTicks, () => Detonate(key));
        }

        static void Detonate(BlockKey key)
        {
            var bi = W?.Get(key);
            if (bi == null || bi.Data.Def != Blocks.TNT) return;
            var pos = W.WorldCenter(key);
            BlockNet.ServerBroadcastOp(Op.Remove(key, false));
            float r = 3.6f * BlockWorld.S;
            ExplodeBlocks(pos, r, key.Frame, carveGround: true);
            BlockNet.ServerExplosion(pos, r);
            Noise(pos, 40f, 1f);
        }

        public static bool SuppressGameExplosionHook;
        public static bool DevCarveGameExplosions; // (dev: "boom ... 1" without changing the config)
        static readonly HashSet<BlockKey> naturalRemoved = new HashSet<BlockKey>();

        public static void ExplodeBlocks(Vector3 pos, float radius, int frameHint = -1, bool carveGround = false)
        {
            var world = W;
            if (world == null) return;
            var ops = new List<Op>();
            var toIgnite = new List<BlockKey>();
            foreach (var kv in world.Blocks.ToList())
            {
                var c = world.WorldCenter(kv.Key);
                float d = Vector3.Distance(c, pos);
                if (d > radius) continue;
                var def = kv.Value.Data.Def;
                if (def == Blocks.TNT) { if ((kv.Value.Data.State & 1) == 0) toIgnite.Add(kv.Key); continue; }
                if (def.ExplosionProof || def.Unbreakable) continue;
                // falloff: edge blocks have a chance to survive, sturdy blocks resist more
                float resist = Mathf.Clamp01(def.PickTime / 3f) * 0.5f;
                if (d > radius * 0.6f && Random.value < resist + (d / radius - 0.6f)) continue;
                ops.Add(Op.Remove(kv.Key, false));
                if ((kv.Value.Data.State & Blocks.NaturalGround) != 0) naturalRemoved.Add(kv.Key);
                // what's stored inside comes out
                if (def == Blocks.Chest) Chests.ServerDropContents(kv.Key, c);
                if (def == Blocks.Furnace) Crafting.ServerDropContents(kv.Key, c);
                if (Random.value < Balance.ExplosionDropChance && def.Shape != BlockShape.PistonHead && !def.DropsScrap) SpawnDrop(def, c);
            }
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            // TNT blasts a crater (about 2.6 blocks) into raw ground too; other explosions only open dug-up ground
            // TNT is 2.7 blocks; game explosions (landmines...) use their own blast size, capped
            float craterR = frameHint == -1 ? Mathf.Clamp(radius * 0.75f, 1.2f * BlockWorld.S, 2.7f * BlockWorld.S) : 2.7f * BlockWorld.S;
            Ground.Explode(pos, carveGround ? craterR : 0f, naturalRemoved.ToList(), carveGround);
            naturalRemoved.Clear();
            foreach (var k in toIgnite) Ignite(k, Random.Range(10, 30));
            Gravity.MarkDirty();
            Redstone.MarkDirty();
        }

        // ------------------------------------------------------------------ eating
        public static void HandleConsume(ulong sender, ulong stackId)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(stackId, out var no)) return;
            var stack = no.GetComponent<StackItem>();
            if (stack == null || stack.Count <= 0) return;
            stack.ServerSetCount(stack.Count - 1);
        }

        // ------------------------------------------------------------------ helpers
        public static void Noise(Vector3 pos, float range, float loudness)
        {
            try
            {
                bool inShip = BlockWorld.InShip(pos) && StartOfRound.Instance.hangarDoorsClosed;
                RoundManager.Instance?.PlayAudibleNoise(pos, range, loudness, 0, inShip, 4242);
            }
            catch { }
        }

        /// <summary>Is the cell blocked by level geometry (walls, floors, doors...)?</summary>
        public static bool Obstructed(BlockKey k, float shrink = 0.9f)
        {
            var world = W;
            var c = world.WorldCenter(k);
            var rot = world.FrameRotation(k.Frame);
            var hits = Physics.OverlapBox(c, Vector3.one * (BlockWorld.S * 0.5f * shrink), rot, WorldGeometryMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<BlockRef>() != null) continue;
                if (h.GetComponentInParent<GrabbableObject>() != null) continue;
                if (h.GetComponentInParent<PlayerControllerB>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>Does something hold this cell up (block below or level floor right under it)?</summary>
        public static bool Supported(BlockKey k)
        {
            var world = W;
            var below = k.Offset((int)Face.Down);
            if (world.Get(below) is BlockInstance b && b.Data.Def.Solid) return true;
            var c = world.WorldCenter(k);
            var down = world.FrameDirToWorld(k.Frame, Vector3.down);
            var bottom = c + down * (BlockWorld.S * 0.45f);
            // probe the center and 4 inset corners
            var right = world.FrameDirToWorld(k.Frame, Vector3.right) * BlockWorld.S * 0.35f;
            var fwd = world.FrameDirToWorld(k.Frame, Vector3.forward) * BlockWorld.S * 0.35f;
            Vector3[] probes = { bottom, bottom + right + fwd, bottom - right + fwd, bottom + right - fwd, bottom - right - fwd };
            foreach (var p in probes)
                if (Physics.Raycast(p, down, out var hit, BlockWorld.S * 0.2f, WorldGeometryMask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<BlockRef>() == null)
                    return true;
            return Obstructed(below, 0.8f);
        }
    }

    /// <summary>Falling sand / gravel.</summary>
    public static class Gravity
    {
        static bool dirty;
        public static void MarkDirty() => dirty = true;

        public static void Tick()
        {
            if (!dirty) return;
            dirty = false;
            var world = BlockWorld.Instance;
            if (world == null) return;
            var ops = new List<Op>();
            if (world.Blocks.Values.Any(b => b.Data.Def.Gravity && b.AnimT < 1f)) { dirty = true; return; }
            var falling = world.Blocks.Where(kv => kv.Value.Data.Def.Gravity).Select(kv => kv.Key).OrderBy(k => k.Pos.y).ToList();
            var claimed = new HashSet<BlockKey>();
            foreach (var k in falling)
            {
                if (ServerLogic.Supported(k)) continue;
                // find landing cell
                var cur = k;
                int dist = 0;
                bool landed = false;
                for (int i = 0; i < 64; i++)
                {
                    var next = cur.Offset((int)Face.Down);
                    if (world.Has(next) || claimed.Contains(next) || ServerLogic.Obstructed(next, 0.8f) || Ground.IsUndugGround(next)) { landed = true; break; }
                    cur = next;
                    dist++;
                    if (ServerLogic.Supported(cur)) { landed = true; break; }
                }
                if (dist == 0) continue;
                claimed.Add(cur);
                if (!landed)
                {
                    Plugin.Log.LogWarning($"Falling {world.Get(k)?.Data.Def.Key} at {k.Pos} found nothing to land on within 64 blocks: removed");
                    ops.Add(Op.Remove(k, false)); continue;
                }
                int ticks = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(2f * dist * BlockWorld.S / 9.81f) / 0.05f), 2, 60);
                ops.Add(Op.Move(k, cur, (byte)ticks));
            }
            if (ops.Count > 0)
            {
                BlockNet.ServerBroadcastOps(ops);
                // land sound once things arrive
                var landings = ops.Where(o => o.Type == OpType.Move).ToList();
                if (landings.Count > 0)
                {
                    var first = landings[0];
                    Redstone.Schedule(first.Fx, () => { if (BlockWorld.Instance != null) BlockNet.ServerSound(BlockWorld.Instance.WorldCenter(first.Key2), "step.sand", 0.9f, 0.8f); });
                }
                dirty = true; // blocks above may now fall too
            }
        }
    }
}
