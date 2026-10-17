using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's item movers (#160): the hopper (five slots: pulls from the container above it or items lying on top,
    /// pushes one at a time where its spout points, two and a half items a second; locked while powered), the dropper
    /// and the dispenser (nine slots: a redstone pulse drops one item out of the front, or into a container there; the
    /// dispenser uses what it can: shoots arrows and snowballs, pours and fills buckets, lights fires, primes TNT,
    /// bone-meals crops, hatches spawn eggs). Furnaces work with hoppers as in Minecraft: in from the top, fuel from the
    /// side, the result out of the bottom. Their contents are a chest's (Chests), with fewer slots.
    /// </summary>
    public static class ItemTransport
    {
        public static int Moved, Fired; // (dev/tests)

        /// <summary>Host, every 8 redstone ticks (Minecraft's hopper pace): each unlocked hopper moves one item each way.</summary>
        public static void ServerHopperTick(BlockWorld world)
        {
            if (Blocks.Hopper == null) return;
            var hoppers = world.Blocks.Where(kv => kv.Value.Data.Def == Blocks.Hopper && (kv.Value.Data.State & 1) == 0).Select(kv => kv.Key).ToList();
            foreach (var k in hoppers)
            {
                var c = Chests.Ensure(k);
                bool changed = false;
                // out of the spout
                var face = world.Get(k)?.Data.Facing ?? (byte)Face.Down;
                var to = k.Offset(face);
                for (int i = 0; i < c.Slots && !changed; i++)
                {
                    if (c.Count[i] <= 0) continue;
                    if (Insert(world, to, c.Key[i], face)) { c.Count[i]--; if (c.Count[i] <= 0) { c.Count[i] = 0; c.Key[i] = null; } changed = true; Moved++; }
                }
                // in from above: a container's first stack, a furnace's result, or items lying on it
                var above = k.Offset((int)Face.Up);
                var got = Extract(world, above);
                if (got != null)
                {
                    var back = Chests.PutInto(c, -1, got, 1, false);
                    if (back.n > 0) Insert(world, above, got, (byte)Face.Down, putBack: true); // (no room: back where it came from)
                    else { changed = true; Moved++; }
                }
                else if (Vacuum(world, k, c)) changed = true;
                if (changed) BlockNet.ServerChest(k, c);
            }
        }

        /// <summary>One item into whatever's at k, entering from <paramref name="from"/>'s direction: a container, or a
        /// furnace (from above: to smelt; from the side: fuel).</summary>
        static bool Insert(BlockWorld world, BlockKey k, string key, byte dir, bool putBack = false)
        {
            var def = world.DefAt(k);
            if (def == null) return false;
            if (Blocks.IsChest(def))
            {
                var c = Chests.Ensure(k);
                var back = Chests.PutInto(c, -1, key, 1, false);
                if (back.n > 0) return false;
                BlockNet.ServerChest(k, c);
                return true;
            }
            if (def == Blocks.Composter) return !putBack && Composting.ServerAdd(k, key);
            if (def == Blocks.Furnace)
            {
                int slot = dir == (byte)Face.Down ? 0 : 1;
                if (putBack) slot = -1;
                var back = Crafting.ServerPut(k, slot, key, 1, false);
                return back.n == 0;
            }
            return false;
        }

        /// <summary>One item out of whatever's at k (for a hopper under it): a container's first stack, a furnace's result.</summary>
        static string Extract(BlockWorld world, BlockKey k)
        {
            var def = world.DefAt(k);
            if (def == null) return null;
            if (Blocks.IsChest(def))
            {
                var c = Chests.Of(k);
                if (c == null) return null;
                for (int i = 0; i < c.Slots; i++)
                    if (c.Count[i] > 0)
                    {
                        var (key, n) = Chests.TakeFrom(c, i, 1);
                        if (n > 0) { BlockNet.ServerChest(k, c); return key; }
                    }
                return null;
            }
            if (def == Blocks.Furnace) return Crafting.ServerTakeResult(k);
            if (def == Blocks.Composter) return Composting.ServerTakeBoneMeal(k);
            // (a rail: a chest or hopper minecart standing on it, as Minecraft's hoppers empty them)
            if (Rails.IsRail(def))
                foreach (var cart in RoundManager.Instance.SpawnedEnemies.OfType<MinecartAI>())
                {
                    if (cart == null || cart.isEnemyDead || (cart.enemyType != MinecartAI.ChestType && cart.enemyType != MinecartAI.HopperType)) continue;
                    if (Vector3.Distance(cart.transform.position, world.WorldCenter(k)) > BlockWorld.S * 0.75f) continue;
                    var cc = Chests.Of(cart.StorageKey);
                    if (cc == null) continue;
                    for (int i = 0; i < cc.Slots; i++)
                        if (cc.Count[i] > 0) { var (key, n) = Chests.TakeFrom(cc, i, 1); if (n > 0) { BlockNet.ServerChest(cart.StorageKey, cc); return key; } }
                }
            return null;
        }

        /// <summary>Host, a hopper minecart (#160): one item from the container above it, else items lying near it.</summary>
        public static bool ServerPullInto(BlockWorld w, BlockKey above, Vector3 near, Chests.Contents c)
        {
            var got = Extract(w, above);
            if (got != null) { if (Chests.PutInto(c, -1, got, 1, false).n > 0) Insert(w, above, got, (byte)Face.Down, putBack: true); else return true; }
            bool any = false;
            foreach (var h in Physics.OverlapSphere(near, BlockWorld.S * 0.8f, 1 << 6, QueryTriggerInteraction.Collide))
            {
                var st = h.GetComponentInParent<StackItem>();
                if (st == null || st.isHeld || st.isPocketed || st.Count <= 0 || st.ItemKey == null) continue;
                var back = Chests.PutInto(c, -1, Crafting.KeyOf(st), st.Count, false); // (#228: its name too)
                if (back.n == st.Count) continue;
                st.ServerSetCount(back.n, despawnIfEmpty: true);
                any = true; Moved++;
            }
            return any;
        }

        /// <summary>Items lying in the space above a hopper go in (whole stacks, as many as fit).</summary>
        static bool Vacuum(BlockWorld world, BlockKey k, Chests.Contents c)
        {
            var above = world.WorldCenter(k.Offset((int)Face.Up));
            bool any = false;
            foreach (var h in Physics.OverlapBox(above, Vector3.one * (BlockWorld.S * 0.5f), world.FrameRotation(k.Frame), 1 << 6, QueryTriggerInteraction.Collide))
            {
                var st = h.GetComponentInParent<StackItem>();
                if (st == null || st.isHeld || st.isPocketed || st.Count <= 0 || st.ItemKey == null) continue;
                var back = Chests.PutInto(c, -1, Crafting.KeyOf(st), st.Count, false); // (#228: its name too)
                if (back.n == st.Count) continue;
                st.ServerSetCount(back.n, despawnIfEmpty: true);
                any = true; Moved++;
            }
            return any;
        }

        // ------------------------------------------------------------------ the dropper and the dispenser
        /// <summary>Host: a redstone pulse reached a dropper or dispenser (Minecraft: it acts 4 ticks later).</summary>
        public static void ServerFire(BlockKey k)
        {
            var world = BlockWorld.Instance;
            var b = world?.Get(k);
            if (b == null || (b.Data.Def != Blocks.Dropper && b.Data.Def != Blocks.Dispenser && b.Data.Def != Blocks.Crafter)) return;
            if (b.Data.Def == Blocks.Crafter) { ServerCraft(world, k, b); return; }
            var c = Chests.Ensure(k);
            var full = Enumerable.Range(0, c.Slots).Where(i => c.Count[i] > 0).ToList();
            var at = world.WorldCenter(k);
            if (full.Count == 0) { BlockNet.ServerSound(at, "click", 0.5f, 1.2f); return; } // (Minecraft's empty click)
            int slot = full[Random.Range(0, full.Count)];
            string key = c.Key[slot];
            byte f = b.Data.Facing;
            var front = k.Offset(f);
            var dir = world.FrameDirToWorld(k.Frame, Faces.Dir[f]);
            var outAt = at + dir * (BlockWorld.S * 0.7f);
            bool used;
            if (b.Data.Def == Blocks.Dropper && Blocks.IsChest(world.DefAt(front))) used = Insert(world, front, key, Faces.Opposite(f));
            else if (b.Data.Def == Blocks.Dispenser) used = Dispense(world, k, front, key, outAt, dir, c, slot);
            else used = false;
            if (!used)
            {
                // drop it out of the front
                if (!ModItems.ByKey.TryGetValue(key, out var it)) return;
                if (it.spawnPrefab.GetComponent<StackItem>() != null) ItemNames.ServerApply(ModItems.ServerSpawnStack(it, 1, outAt), key); // (#228)
                else ModItems.ServerSpawnPlainKeyed(key, outAt);
                used = true;
            }
            if (used && c.Key[slot] == key) { c.Count[slot]--; if (c.Count[slot] <= 0) { c.Count[slot] = 0; c.Key[slot] = null; } }
            Fired++;
            BlockNet.ServerChest(k, c);
            BlockNet.ServerSound(at, "click", 0.5f, 1f);
        }

        /// <summary>The crafter's pulse: its grid's recipe, made once (each slot gives one), out of its face.</summary>
        static void ServerCraft(BlockWorld world, BlockKey k, BlockInstance b)
        {
            var c = Chests.Ensure(k);
            var grid = Enumerable.Range(0, 9).Select(i => c.Count[i] > 0 ? c.Key[i] : null).ToArray();
            var r = RecipeBook.Match(Crafting.Recipes.Where(Crafting.Allowed), grid, 3);
            var at = world.WorldCenter(k);
            if (r == null) { BlockNet.ServerSound(at, "click", 0.5f, 1.2f); return; }
            for (int i = 0; i < 9; i++) if (c.Count[i] > 0) { c.Count[i]--; if (c.Count[i] == 0) c.Key[i] = null; }
            byte f = b.Data.Facing;
            var front = k.Offset(f);
            int left = r.Count;
            if (Blocks.IsChest(world.DefAt(front)))
            {
                var fc = Chests.Ensure(front);
                left = Chests.PutInto(fc, -1, r.Result, r.Count, false).n;
                BlockNet.ServerChest(front, fc);
            }
            if (left > 0 && ModItems.ByKey.TryGetValue(r.Result, out var it))
                ModItems.ServerSpawnStack(it, left, at + world.FrameDirToWorld(k.Frame, Faces.Dir[f]) * (BlockWorld.S * 0.7f));
            Fired++;
            BlockNet.ServerChest(k, c);
            BlockNet.ServerSound(at, "pop", 0.6f, 1f);
        }

        /// <summary>A dispenser's use of an item (false: it just drops it). A bucket swaps itself in the slot.</summary>
        static bool Dispense(BlockWorld world, BlockKey self, BlockKey front, string key, Vector3 outAt, Vector3 dir, Chests.Contents c, int slot)
        {
            string baseKey = ItemData.TypeOf(key) ?? ""; // (what it is, #228)
            if (baseKey == Bow.ArrowKey || baseKey.StartsWith("arrow_"))
            {
                byte effect = baseKey == "arrow_slowness" ? (byte)1 : baseKey == "arrow_poison" ? (byte)2 : (byte)0;
                BlockNet.ServerArrow(outAt, (dir + Vector3.up * 0.1f).normalized * Arrow.Speed, Arrow.NoShooter, effect);
                return true;
            }
            if (baseKey == "snowball") { BlockNet.ServerSnowball(outAt, (dir + Vector3.up * 0.1f).normalized * 18f, Arrow.NoShooter); return true; }
            if (baseKey == "tnt" && world.DefAt(front) == null)
            {
                BlockNet.ServerBroadcastOp(Op.Set(front, new BlockData(Blocks.TNT.Id)));
                ServerLogic.Ignite(front, 80);
                return true;
            }
            if (baseKey == "flint_and_steel" && world.DefAt(front) == null)
            {
                BlockNet.ServerBroadcastOp(Op.Set(front, new BlockData(Blocks.Fire.Id)));
                c.Count[slot]++; // (the tool stays: the caller takes one, this puts it back)
                return true;
            }
            if (baseKey == "water_bucket" || baseKey == "lava_bucket")
            {
                if (world.DefAt(front) != null) return false;
                var fluid = baseKey == "water_bucket" ? Blocks.Water : Blocks.Lava;
                BlockNet.ServerBroadcastOp(Op.Set(front, new BlockData(fluid.Id)));
                c.Key[slot] = "bucket"; // (it pours and keeps the empty bucket)
                return true;
            }
            if (baseKey == "bucket")
            {
                var fd = world.DefAt(front);
                var fb = world.Get(front);
                if ((fd == Blocks.Water || fd == Blocks.Lava) && fb != null && (fb.Data.State & 15) == 0)
                {
                    BlockNet.ServerBroadcastOp(Op.Remove(front, false));
                    c.Key[slot] = fd == Blocks.Water ? "water_bucket" : "lava_bucket";
                    return true;
                }
                return false;
            }
            if (baseKey == "bone_meal" && Farming.ServerBoneMeal(front)) return true;
            if (baseKey.StartsWith("spawn_egg") && SpawnEggs.ServerHatch(key, outAt)) return true;
            return false;
        }

    }
}
