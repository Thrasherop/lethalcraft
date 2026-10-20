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
                // (#244: a large chest is one container: the other half when this one's full, Minecraft's)
                foreach (var hk in Halves(k))
                {
                    var c = Chests.Ensure(hk);
                    if (Chests.PutInto(c, -1, key, 1, false).n > 0) continue;
                    BlockNet.ServerChest(hk, c);
                    return true;
                }
                return false;
            }
            // (#244, Minecraft's ComposterBlock.InputContainer: only from above)
            if (def == Blocks.Composter) return !putBack && dir == (byte)Face.Down && Composting.ServerAdd(k, key);
            // (#244) a jukebox takes a disc when it has none; a chiseled bookshelf a book in its first empty slot
            if (def == Blocks.Jukebox) return Jukebox.ServerPutDisc(k, key);
            if (def == Blocks.ChiseledBookshelf) return ServerShelfPut(world, k, key);
            // (#244) a chest or hopper minecart on a rail there
            if (Rails.IsRail(def) && CartOn(world, k) is MinecartAI cart)
            {
                var cc = Chests.Ensure(cart.StorageKey);
                if (Chests.PutInto(cc, -1, key, 1, false).n > 0) return false;
                BlockNet.ServerChest(cart.StorageKey, cc);
                return true;
            }
            if (def == Blocks.BrewingStand)
            {
                // (#244, Minecraft's: from above into the ingredient slot; from the side blaze powder to the fuel, bottles into
                // the empty holders)
                string t = ItemData.TypeOf(key);
                if (putBack) return Brewing.ServerPut(k, -1, key, 1, false).n == 0;
                if (dir == (byte)Face.Down) return Potions.IsIngredient(t) && Brewing.ServerPut(k, Brewing.Ingredient, key, 1, false).n == 0;
                if (t == "blaze_powder") return Brewing.ServerPut(k, Brewing.Fuel, key, 1, false).n == 0;
                if (!Brewing.CanGo(0, key)) return false;
                for (int i = 0; i < Brewing.Bottles; i++) if (Brewing.ServerPut(k, i, key, 1, false).n == 0) return true;
                return false;
            }
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
                foreach (var hk in Halves(k)) // (#244: both halves of a large chest)
                {
                    var c = Chests.Of(hk);
                    if (c == null) continue;
                    for (int i = 0; i < c.Slots; i++)
                        if (c.Count[i] > 0)
                        {
                            var (key, n) = Chests.TakeFrom(c, i, 1);
                            if (n > 0) { BlockNet.ServerChest(hk, c); return key; }
                        }
                }
                return null;
            }
            if (def == Blocks.Jukebox) return Jukebox.ServerTakeDisc(k); // (#244)
            if (def == Blocks.ChiseledBookshelf) return ServerShelfTake(world, k); // (#244)
            if (def == Blocks.Furnace) return Crafting.ServerTakeResult(k);
            if (def == Blocks.BrewingStand)
            {
                // (#244, Minecraft's: a hopper under it takes the bottles out, brewed or not)
                for (int i = 0; i < Brewing.Bottles; i++) { var (bk, bn) = Brewing.ServerTakeSlot(k, i, 1); if (bn > 0) return bk; }
                return null;
            }
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

        /// <summary>(#244) A chest and, for a large chest, its other half: in Minecraft's order (the first half first).</summary>
        static IEnumerable<BlockKey> Halves(BlockKey k)
        {
            if (k.Frame != Chests.MobFrame && Chests.PartnerOf(k, out bool partnerFirst) is BlockKey pk)
            {
                if (partnerFirst) { yield return pk; yield return k; } else { yield return k; yield return pk; }
            }
            else yield return k;
        }

        static MinecartAI CartOn(BlockWorld world, BlockKey k)
        {
            if (RoundManager.Instance == null) return null;
            foreach (var cart in RoundManager.Instance.SpawnedEnemies.OfType<MinecartAI>())
                if (cart != null && !cart.isEnemyDead && (cart.enemyType == MinecartAI.ChestType || cart.enemyType == MinecartAI.HopperType) && Vector3.Distance(cart.transform.position, world.WorldCenter(k)) <= BlockWorld.S * 0.75f) return cart;
            return null;
        }

        /// <summary>(#244) A book into a chiseled bookshelf's first empty slot (Minecraft's: it takes one book a slot).</summary>
        static bool ServerShelfPut(BlockWorld world, BlockKey k, string key)
        {
            var b = world.Get(k);
            if (b == null || !Blocks.ShelfBook(key)) return false;
            for (int s = 0; s < 6; s++)
            {
                if ((b.Data.State & (1 << s)) != 0) continue;
                var c = Chests.Ensure(k); c.Key[s] = key; c.Count[s] = 1;
                var d = b.Data; d.State = (byte)(d.State | (1 << s));
                BlockNet.ServerBroadcastOp(Op.State(k, d));
                Blocks.ShelfLastSlot[k] = s + 1;
                Redstone.MarkDirty();
                return true;
            }
            return false;
        }

        static string ServerShelfTake(BlockWorld world, BlockKey k)
        {
            var b = world.Get(k);
            if (b == null) return null;
            for (int s = 0; s < 6; s++)
            {
                if ((b.Data.State & (1 << s)) == 0) continue;
                string back = "book";
                if (Chests.All.TryGetValue(k, out var c) && c.Count[s] > 0 && c.Key[s] != null) { back = c.Key[s]; c.Key[s] = null; c.Count[s] = 0; }
                var d = b.Data; d.State = (byte)(d.State & ~(1 << s));
                BlockNet.ServerBroadcastOp(Op.State(k, d));
                Blocks.ShelfLastSlot[k] = s + 1;
                Redstone.MarkDirty();
                return back;
            }
            return null;
        }

        /// <summary>(#244) What a dropper puts into rather than dropping in front (Minecraft's getContainerAt).</summary>
        static bool IsContainerAt(BlockWorld world, BlockKey k)
        {
            var d = world.DefAt(k);
            return d != null && (Blocks.IsChest(d) || d == Blocks.Composter || d == Blocks.BrewingStand || d == Blocks.Furnace || d == Blocks.Jukebox || d == Blocks.ChiseledBookshelf || (Rails.IsRail(d) && CartOn(world, k) != null));
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
            bool into = b.Data.Def == Blocks.Dropper && IsContainerAt(world, front);
            if (into) used = Insert(world, front, key, f); // (#244: a furnace, a composter, a brewing stand too; full, nothing comes out: Minecraft's)
            else if (b.Data.Def == Blocks.Dispenser)
            {
                int r = DispenseUse(world, k, front, key, outAt, dir, c, slot, f);
                used = r == Used;
                if (r == Kept) { BlockNet.ServerSound(at, "click", 0.5f, 1.2f); Kept244++; return; } // (#244: it failed: the item stays, Minecraft's click)
            }
            else used = false;
            if (into && !used) { BlockNet.ServerSound(at, "click", 0.5f, 1.2f); return; }
            if (!used)
            {
                // drop it out of the front (#244: thrown a little way, as Minecraft's DefaultDispenseItemBehavior: it lands 1-2
                // blocks out, short of a wall)
                if (!ModItems.ByKey.TryGetValue(key, out var it)) return;
                float far = Random.Range(1.2f, 2.2f) * BlockWorld.S;
                if (Physics.Raycast(outAt, dir, out var wh, far, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) far = Mathf.Max(0f, wh.distance - 0.3f * BlockWorld.S);
                outAt += dir * far + Vector3.up * 0.2f * BlockWorld.S;
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
            var rests = new List<string>();
            for (int i = 0; i < 9; i++) if (c.Count[i] > 0)
            {
                string rest = CraftingUI.Remainder(c.Key[i]); // (#244: Minecraft's: a milk bucket leaves its bucket)
                c.Count[i]--; if (c.Count[i] == 0) c.Key[i] = null;
                if (rest == null) continue;
                if (c.Count[i] == 0) { c.Key[i] = rest; c.Count[i] = 1; } else rests.Add(rest);
            }
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
            foreach (var rest in rests) if (ModItems.ByKey.TryGetValue(rest, out var ri)) ModItems.ServerSpawnStack(ri, 1, at + world.FrameDirToWorld(k.Frame, Faces.Dir[f]) * (BlockWorld.S * 0.7f));
            Fired++;
            BlockNet.ServerChest(k, c);
            BlockNet.ServerSound(at, "pop", 0.6f, 1f);
        }

        /// <summary>A dispenser's use of an item (false: it just drops it). A bucket swaps itself in the slot.</summary>
        const int Dropped = 0, Used = 1, Kept = 2;
        public static int Kept244, Equipped244, Placed244; // (dev/tests)

        /// <summary>(#244) Minecraft 26.3's dispenser behaviours (DispenseItemBehavior.bootStrap): Used, Kept (it failed: the
        /// item stays, a click), or Dropped (it's thrown out, as a dropper would).</summary>
        static int DispenseUse(BlockWorld world, BlockKey self, BlockKey front, string key, Vector3 outAt, Vector3 dir, Chests.Contents c, int slot, byte facing)
        {
            string t = ItemData.TypeOf(key) ?? "";
            var fc = world.WorldCenter(front);
            if (t == "flint_and_steel")
            {
                // (TNT in front primes; open air catches fire; anything else: it fails. It wears, as in a hand.)
                var fd = world.DefAt(front);
                if (fd == Blocks.TNT) ServerLogic.Ignite(front, 80); // (the block goes as the lit TNT comes)
                else if (fd == null && !Ground.IsSolidCell(Ground.CellOf(fc))) Fire.ServerPlace(front); // (on soul sand: soul fire)
                else return Kept;
                WearInPlace(c, slot, key);
                return Used;
            }
            if (t == "bone_meal") return Farming.ServerBoneMeal(front) ? Used : Kept;
            if (t == "glass_bottle")
            {
                // (Minecraft's: a water source in front fills it)
                var wb = world.Get(front);
                bool water = (wb != null && wb.Data.Def == Blocks.Water && (wb.Data.State & 15) == 0) || MoonWater.Depth(fc) > 0.3f * BlockWorld.S;
                if (!water) return Dropped;
                Swap(c, slot, "potion_water", outAt);
                return Used;
            }
            if (t == "shears")
            {
                // (a sheep in front is sheared; it wears; nothing to shear: it fails)
                if (!(MobAt<SheepAI>(fc) is SheepAI sh) || !sh.ServerShearFree()) return Kept;
                WearInPlace(c, slot, key);
                return Used;
            }
            if (t == "carved_pumpkin" && world.Get(front) == null && Golems.ServerTryBuild(front, Blocks.CarvedPumpkin)) { Placed244++; return Used; }
            if (Armor.Get(t) is var arm && arm != null)
            {
                // (onto a player standing in front, in its slot if that's empty)
                foreach (var p in StartOfRound.Instance.allPlayerScripts)
                {
                    if (p == null || !p.isPlayerControlled || p.isPlayerDead || !InFront(p.transform.position, fc)) continue;
                    var worn = (string[])Armor.Of(p.actualClientId).Clone();
                    if (worn[arm.Slot] != null) continue;
                    worn[arm.Slot] = key;
                    Armor.ServerSet(p.actualClientId, worn, false, p.transform.position);
                    Equipped244++;
                    return Used;
                }
                return t == "carved_pumpkin" ? Kept : Dropped;
            }
            if (t == "saddle" && MobAt<HorseAI>(fc) is HorseAI hs && hs.Tamed && !hs.Saddled && !hs.IsBaby)
            {
                hs.Saddled = true;
                BlockNet.ServerSound(hs.transform.position + Vector3.up * 1.5f, "horse.saddle", 0.9f, 1f);
                BlockNet.ServerHorse(hs, false);
                Equipped244++;
                return Used;
            }
            int ak = System.Array.IndexOf(HorseAI.ArmorKeys, t);
            if (ak > 0 && MobAt<HorseAI>(fc) is HorseAI ha && ha.Tamed && !ha.IsBaby && ha.HorseArmor == 0)
            {
                ha.Receive(ha.Tamed, ha.Saddled, ha.Rider, false, (byte)ak, ak == 1 ? LeatherDye.ColorOf(key) : -1);
                BlockNet.ServerHorse(ha, false);
                BlockNet.ServerSound(ha.transform.position + Vector3.up * 1.5f, "horse.armor", 0.9f, 1f);
                Equipped244++;
                return Used;
            }
            if (t == "chest" && MobAt<LlamaAI>(fc) is LlamaAI la && la.Tamed && !la.HasChest && !la.IsBaby)
            {
                la.HasChest = true; BlockNet.ServerMobState(la.NetworkObjectId, 241, 1);
                Equipped244++;
                return Used;
            }
            if (BoatAI.TypeFor(t) is EnemyType boat)
            {
                // (on water in front, or on water just under it)
                var wb = world.Get(front); var below = world.Get(front.Offset((int)Face.Down));
                bool water = (wb != null && wb.Data.Def == Blocks.Water) || MoonWater.Depth(fc) > 0.2f * BlockWorld.S || (wb == null && below != null && below.Data.Def == Blocks.Water);
                if (!water) return Dropped;
                MobSpawns.DevSpawnUntil = Time.time + 1f;
                RoundManager.Instance.SpawnEnemyGameObject(fc, Quaternion.LookRotation(dir).eulerAngles.y, -1, boat);
                Placed244++;
                return Used;
            }
            int cart = t == "minecart" ? 0 : t == "chest_minecart" ? 1 : t == "hopper_minecart" ? 2 : t == "tnt_minecart" ? 3 : t == "furnace_minecart" ? 4 : -1;
            if (cart >= 0)
            {
                // (onto a rail in front, or one under it)
                BlockKey rail = Rails.IsRail(world.DefAt(front)) ? front : front.Offset((int)Face.Down);
                if (!Rails.IsRail(world.DefAt(rail))) return Dropped;
                MinecartAI.ServerSpawnOn(Unity.Netcode.NetworkManager.ServerClientId, rail, (byte)cart);
                Placed244++;
                return Used;
            }
            if (t == Fireworks.RocketKey)
            {
                // (Minecraft's: shot the way it faces, 0.5 blocks a tick, and it speeds on)
                int id = ItemData.Of(key);
                BlockNet.ServerFireworkShot(outAt, dir * 0.5f, id, Fireworks.LifeTicks(Fireworks.RocketOfId(id).Flight), Fireworks.NotAttached);
                return Used;
            }
            if (t == ExperienceBottle.Key)
            {
                // (Minecraft's: thrown the way it faces, a little up)
                BlockNet.ServerXpBottle(outAt, (dir + Vector3.up * 0.1f).normalized * ExperienceBottle.Speed * 0.75f);
                return Used;
            }
            if (t == "honeycomb") return Copper.ServerWaxAt(front) ? Used : Kept;
            return Dispense(world, self, front, key, outAt, dir, c, slot) ? Used : Dropped;
        }

        /// <summary>A point stands in the block in front (a player's feet, a mob).</summary>
        static bool InFront(Vector3 at, Vector3 fc)
        {
            var d = at - fc;
            return Mathf.Abs(d.x) < 0.75f * BlockWorld.S && Mathf.Abs(d.z) < 0.75f * BlockWorld.S && d.y > -1.2f * BlockWorld.S && d.y < 0.8f * BlockWorld.S;
        }

        static T MobAt<T>(Vector3 fc) where T : EnemyAI
        {
            if (RoundManager.Instance == null) return null;
            foreach (var e in RoundManager.Instance.SpawnedEnemies) if (e is T m && !m.isEnemyDead && InFront(m.transform.position, fc)) return m;
            return null;
        }

        /// <summary>One of the slot becomes another thing (a bottle filled): in its place if it was the last, else into the
        /// dispenser, or out of its front with no room.</summary>
        static void Swap(Chests.Contents c, int slot, string to, Vector3 outAt)
        {
            if (c.Count[slot] <= 1) { c.Key[slot] = to; return; }
            if (Chests.PutInto(c, -1, to, 1, false).n > 0) ModItems.ServerSpawnPlainKeyedOrStack(to, outAt);
        }

        /// <summary>A tool in the slot used once (the caller takes one: this puts it back, worn; worn out, it's gone).</summary>
        static void WearInPlace(Chests.Contents c, int slot, string key)
        {
            var (used, max) = Durability.Of(key);
            int data = ItemData.Of(key), ench = Enchants.EnchOf(data);
            int ub = Enchants.Level(ench, EnchKind.Unbreaking);
            if (max > 0 && (ub <= 0 || Random.value < Enchants.WearChance(ub))) used++;
            if (max > 0 && used >= max) return; // (broken: gone)
            string nk = max > 0 ? ItemData.With(key, Enchants.Data(used, ench)) : key;
            if (c.Count[slot] <= 1) { c.Key[slot] = nk; return; } // (the caller sees another key: it takes none)
            c.Count[slot]++; // (a stack of them: back as it was)
        }

        static bool Dispense(BlockWorld world, BlockKey self, BlockKey front, string key, Vector3 outAt, Vector3 dir, Chests.Contents c, int slot)
        {
            string baseKey = ItemData.TypeOf(key) ?? ""; // (what it is, #228)
            if (baseKey == Bow.ArrowKey || baseKey.StartsWith("arrow_"))
            {
                byte effect = baseKey == "arrow_slowness" ? (byte)1 : baseKey == "arrow_poison" ? (byte)2 : (byte)0;
                BlockNet.ServerArrow(outAt, (dir + Vector3.up * 0.1f).normalized * Arrow.Speed, Arrow.NoShooter, effect, Arrow.Dispensed); // (#244: it can be picked up)
                return true;
            }
            if (baseKey == "snowball") { BlockNet.ServerSnowball(outAt, (dir + Vector3.up * 0.1f).normalized * 18f, Arrow.NoShooter); return true; }
            if (baseKey == "tnt" && world.DefAt(front) == null)
            {
                BlockNet.ServerBroadcastOp(Op.Set(front, new BlockData(Blocks.TNT.Id)));
                ServerLogic.Ignite(front, 80);
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
                    string full = fd == Blocks.Water ? "water_bucket" : "lava_bucket";
                    if (c.Count[slot] <= 1) { c.Key[slot] = full; return true; }
                    // (#244) from a stack, one bucket fills (the caller takes it); the full one into the dispenser, or out of
                    // its front with no room (Minecraft's consumeWithRemainder: the whole stack had filled from one source)
                    int left = Chests.PutInto(c, -1, full, 1, false).n;
                    if (left > 0 && ModItems.ByKey.TryGetValue(full, out var fit)) ModItems.ServerSpawnStack(fit, 1, outAt);
                    return true;
                }
                return false;
            }
            if (baseKey.StartsWith("spawn_egg") && SpawnEggs.ServerHatch(key, outAt)) return true;
            return false;
        }

    }
}
