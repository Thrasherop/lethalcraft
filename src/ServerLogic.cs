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
        public static void HandlePlace(ulong sender, ulong stackId, BlockKey key, ushort type, byte facing)
        {
            var world = W;
            var def = Blocks.Get(type);
            if (world == null || def == null || def.Shape == BlockShape.PistonHead) return;
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(stackId, out var no)) return;
            var stack = no.GetComponent<StackItem>();
            if (stack == null || stack.Count <= 0 || stack.BlockType != type) return;
            if (key.Frame == 0 && !world.WorldFrameAvailable) { BlockNet.ServerToast(sender, "You can't build out here."); return; }
            var clumps = world.Get(key);
            if (clumps != null && Resin.IsClump(clumps.Data.Def))
            {
                // (#230, Minecraft's MultifaceBlock: a clump on another of its faces; anything else takes the cell)
                if (Resin.IsClump(def))
                {
                    if (Resin.ServerAddFace(key, clumps, Faces.Opposite(facing)))
                    {
                        if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                        BlockNet.ServerSound(world.WorldCenter(key), "dig." + Sounds.Family(def), 0.8f, 1f);
                    }
                    return;
                }
                BlockNet.ServerBroadcastOp(Op.Remove(key, false));
            }
            if (world.Has(key) && world.DefAt(key) != Blocks.Lava && world.DefAt(key) != Blocks.Water)
            {
                // a slab onto the open half of the same kind of slab: one full block of two
                var ex = world.Get(key);
                // (#244) snow on snow: a layer more
                if (def == Blocks.SnowLayer && ex != null && ex.Data.Def == def && (ex.Data.State & 7) < 7)
                {
                    var sd = ex.Data; sd.State = (byte)((sd.State & 7) + 1);
                    BlockNet.ServerBroadcastOp(Op.State(key, sd));
                    if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                    BlockNet.ServerSound(world.WorldCenter(key), "dig.snow", 0.8f, 1f);
                    return;
                }
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

            // (farming: seeds only on farmland, sugar cane only by water)
            if (def.Shape == BlockShape.Crop && world.DefAt(key.Offset((int)Face.Down)) != Farming.SoilFor(def)) return;
            if (def.Shape == BlockShape.Crop && def != Blocks.NetherWart && (world.Get(key.Offset((int)Face.Down)).Data.State & Farming.Moist) == 0)
                BlockNet.ServerToast(sender, "This farmland is dry: crops need water within 4 blocks to grow.");
            if (def == Blocks.SugarCane && !Farming.CaneCanStand(key)) { BlockNet.ServerToast(sender, "Sugar cane grows on dirt, grass or sand beside water."); return; }
            byte state = 0;
            // (the half comes in the facing: a slab's Down = the top half; a trapdoor's +8 = the top half)
            if (def.Shape == BlockShape.Slab) { state = (byte)(facing == (byte)Face.Down ? 1 : 0); facing = (byte)Face.Up; }
            if (def.Shape == BlockShape.Trapdoor) { state = (byte)((facing & 8) != 0 ? 2 : 0); facing = (byte)(facing & 7); }
            if (def.Shape == BlockShape.Banner) { if (facing >= 16) { state = (byte)(facing - 16 & 15); facing = (byte)Face.Up; } else if (facing < 2 || facing > 5) return; } // (#223: a standing banner's turn)
            if (def.Shape == BlockShape.Sign || def.Shape == BlockShape.HangingSign)
            {
                // (#223) standing or hung: its turn (and a hanging sign's straight chains) came in the facing
                if (facing >= 16) { state = (byte)((facing - 16) & 31); facing = (byte)(def.Shape == BlockShape.Sign ? Face.Up : Face.Down); }
                else if (facing < 2 || facing > 5) return;
            }
            if (def == Blocks.TallGrass)
            {
                // (#225) two blocks: room for its top half
                var top = key.Offset((int)Face.Up);
                if (world.Has(top) || Obstructed(top, 0.6f)) return;
                BlockNet.ServerBroadcastOps(new List<Op> { Op.Set(key, new BlockData(type, (byte)Face.Up, 0)), Op.Set(top, new BlockData(type, (byte)Face.Up, 1)) });
                if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                BlockNet.ServerSound(center, "dig.grass", 0.8f, 1f);
                return;
            }
            if (def.Shape == BlockShape.Bed)
            {
                var head = key.Offset(facing);
                if (world.Has(head) || Obstructed(head, 0.6f)) { BlockNet.ServerToast(sender, "A bed needs two blocks of room."); return; }
                BlockNet.ServerBroadcastOps(new List<Op> { Op.Set(key, new BlockData(type, facing, 0)), Op.Set(head, new BlockData(type, facing, 1)) });
                if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                BlockNet.ServerSound(center, "dig.wool", 0.8f, 1f);
                return;
            }
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
            // (#140: a pumpkin that completes a golem isn't placed: the golem comes instead)
            if (Golems.ServerTryBuild(key, def))
            {
                if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1);
                return;
            }
            if (Resin.IsClump(def))
            {
                // (#230) its first face: toward the block it was put on
                int on = Faces.Opposite(facing);
                if (!Resin.CanAttach(key, on)) return;
                state = (byte)(1 << on); facing = (byte)Face.Up;
            }
            if (def.DropsScrap) state |= Blocks.PlacedOre; // (#244: Silk Touch's ore, put down: not a vein)
            var data = new BlockData(type, facing, state);
            BlockNet.ServerBroadcastOp(Op.Set(key, data));
            if (def.Shape == BlockShape.Rail) Rails.ServerReshape(key); // (#160)
            if (!GameModes.IsCreative(sender)) stack.ServerSetCount(stack.Count - 1); // creative: blocks never run out
            BlockNet.ServerSound(center, "dig." + Sounds.Family(def), 0.8f, 1f);
            Noise(center, 9f, 0.45f);
            Redstone.MarkDirty();
            Gravity.MarkDirty();
        }

        // ------------------------------------------------------------------ buckets (#19)
        /// <summary>Server: a player's bucket (already out of their hand) fills from a water source or pours one out; what
        /// they get back: the other bucket, or theirs again if nothing happened.</summary>
        public static void HandleBucket(ulong sender, BlockKey key, bool fill, bool fromMoon, bool lava = false)
        {
            var world = W;
            // (what goes back to the player if nothing happens: the bucket as it was)
            string back = fill ? "bucket" : lava ? "lava_bucket" : "water_bucket";
            if (world != null)
            {
                var b = fromMoon ? null : world.Get(key);
                var center = fromMoon ? Vector3.zero : world.WorldCenter(key);
                if (b != null && b.Data.Def == Blocks.Cauldron) // (#160: a cauldron fills from a bucket and gives a full one back)
                {
                    int s = b.Data.State;
                    if (fill && (s == 3 || s == 4)) { back = s == 4 ? "lava_bucket" : "water_bucket"; Composting.SetCauldron(key, 0); }
                    else if (!fill && s == 0) { back = "bucket"; Composting.SetCauldron(key, lava ? 4 : 3); }
                    GiveBack(sender, back, fill, lava);
                    return;
                }
                if (fill && fromMoon) back = "water_bucket";
                else if (fill && b != null && (b.Data.Def == Blocks.Water || b.Data.Def == Blocks.Lava) && b.Data.State == 0)
                {
                    bool isLava = b.Data.Def == Blocks.Lava;
                    BlockNet.ServerBroadcastOp(Op.Remove(key, false));
                    BlockNet.ServerSound(center, isLava ? "bucket.fill_lava" : "bucket.fill", 0.6f, 1f);
                    back = isLava ? "lava_bucket" : "water_bucket";
                }
                else if (!fill && (b == null || b.Data.Def == Blocks.Water || b.Data.Def == Blocks.Lava || b.Data.Def == Blocks.Fire) && !Obstructed(key, 0.6f)
                         && (key.Frame != 0 || world.WorldFrameAvailable))
                {
                    // water on lava: obsidian (a source) or cobblestone; lava on water: the same, from the other side
                    var other = lava ? Blocks.Water : Blocks.Lava;
                    if (b != null && b.Data.Def == other) BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData((b.Data.State == 0 ? Blocks.Obsidian : Blocks.Cobblestone).Id, (byte)Face.Up, 0)));
                    else BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData((lava ? Blocks.Lava : Blocks.Water).Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(center, lava ? "bucket.empty_lava" : "bucket.empty", 0.6f, 1f);
                    back = "bucket";
                }
            }
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] bucket fill={fill} moon={fromMoon} at {key.Pos} ({world?.Get(key)?.Data.Def.Key}:{world?.Get(key)?.Data.State}) -> {back}");
            GiveBack(sender, back, fill, lava);
        }

        /// <summary>The bucket a bucket use leaves: in creative the one in hand was never taken (#200), so an emptied one
        /// stays full and a filled one is given as well, like Minecraft's (its empty bucket stays).</summary>
        static void GiveBack(ulong sender, string back, bool fill, bool lava)
        {
            string held = fill ? "bucket" : lava ? "lava_bucket" : "water_bucket";
            if (GameModes.IsCreative(sender) && (back == held || back == "bucket")) return;
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
            if (def.Unbreakable && !(GameModes.IsCreative(sender) && Ground.CreativeMayBreak(key, bi.Data))) return; // (creative: placed bedrock, and the ground around entrances)
            McHud.ServerBlockBroken();
            var player = PlayerFor(sender);
            var tool = player != null ? player.currentlyHeldObjectServer as ToolItem : null;
            bool harvest = tool != null ? Blocks.CanHarvest(def, tool.Kind, tool.Tier) : Blocks.CanHarvest(def, ToolKind.None, 0);
            if (def == Blocks.Fire) { BlockNet.ServerSound(world.WorldCenter(key), "extinguish", Random.Range(1.6f, 2.2f), 0.5f); BreakBlock(key, false, player); return; }
            if (GameModes.IsCreative(sender)) { BreakBlock(key, false, player); return; } // creative: no drops, no XP (a chest still spills)
            if (def == Blocks.DecoratedPot && tool != null) { Pots.ServerShatter(key); return; } // (#160: a tool shatters it; by hand it drops whole)
            MiningTool = tool;
            try { BreakBlock(key, harvest, player); } finally { MiningTool = null; }
            if (tool != null && def.Hardness > 0f && tool.Kind != ToolKind.None) tool.ServerUse(tool.Kind == ToolKind.Sword ? 2 : 1); // (#244: a sword 2 a block, Minecraft's) // (#48: instant blocks, like torches, don't wear it; nor a crossbow, #223)
            var od = def.OreBase ?? def; // (a deepslate ore counts as its ore, #155)
            int oreXp = ToolLevel(tool, EnchKind.SilkTouch) > 0 ? 0 : OreXp(od); // (#244: none with Silk Touch, Minecraft's)
            if (player != null && oreXp > 0 && harvest) BlockNet.ServerXp(sender, oreXp, world.WorldCenter(key));
            else if (player != null && !harvest && def.HarvestTier > 0 && !Amethyst.DropsNothing(def)) BlockNet.ServerToast(sender, def.HarvestTier == 1 ? "Needs a pickaxe to drop anything." : def.HarvestTier == 2 ? "Needs a stone pickaxe or better." : def.HarvestTier == 3 ? "Needs an iron pickaxe or better." : "Needs a diamond pickaxe.");
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
            if (def.Shape == BlockShape.Bed)
            {
                var other = key.Offset((bi.Data.State & 1) != 0 ? Faces.Opposite(bi.Data.Facing) : bi.Data.Facing);
                var ob = world.Get(other);
                if (ob != null && ob.Data.Def.Shape == BlockShape.Bed) ops.Add(Op.Remove(other, true));
            }
            if (def == Blocks.TallGrass && (bi.Data.State & 1) != 0)
            {
                // (#225) its top half broken: its bottom goes too (and drops what it drops)
                var lower = key.Offset((int)Face.Down);
                var lb = world.Get(lower);
                if (lb != null && lb.Data.Def == Blocks.TallGrass) { ops.Add(Op.Remove(lower, true)); if (drop) Dyes.ServerGrassDrop(lb.Data, world.WorldCenter(lower), breaker); }
            }
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
                // (a door's other half is already gone with it, and one door drops: not two, #82)
                if (def.Shape == BlockShape.Door && nd.Shape == BlockShape.Door) continue;
                if (Resin.ServerFaceGone(nk, n.Data, f ^ 1, ops, drop || (breaker != null && !GameModes.IsCreative(breaker)))) continue; // (#230: that face's clump only)
                BlockKey support = SupportOf(nk, n.Data);
                // (#229: what hangs on it drops even when it doesn't: a cluster on budding amethyst broken by hand)
                // (as broken with no tool, Minecraft's loot for a block whose support went: a cluster's two shards, but a
                // vine nothing: it needs shears)
                if (support.Equals(key)) PopOff(nk, n.Data, ops, (drop || (breaker != null && !GameModes.IsCreative(breaker))) && Blocks.CanHarvest(nd, ToolKind.None, 0));
            }
            if (def == Blocks.SugarCane) Farming.ServerCaneAbove(key, ops, drop);
            if (def.Shape == BlockShape.Rail) { var rk = key; Redstone.Schedule(1, () => Rails.ServerReshape(rk)); } // (#160: after it's gone)

            if (into == null) BlockNet.ServerBroadcastOps(ops);
            if ((bi.Data.State & Blocks.NaturalGround) != 0) OnNaturalRemoved(key);
            if (drop && dropDef != null && !Amethyst.ServerClusterDrop(bi.Data, center, breaker) && !Farming.ServerCropDrop(bi.Data, center) && !Saplings.ServerLeafDrop(dropDef, center, breaker) && !Dyes.ServerGrassDrop(bi.Data, center, breaker) && !BannerDesigns.ServerBroken(key, center, drop)) SpawnDrop(dropDef, center);
            else if (!drop) BannerDesigns.ServerBroken(key, center, false); // (#225: its design forgotten either way) // (#223: leaves drop as Minecraft's)
            Saplings.ServerBlockRemoved(key, def); // (#223: leaves no log holds decay)
            if (Signs.IsSign(def)) Signs.ServerRemoved(key); // (#223: its text goes with it)
            if (drop && def.Shape == BlockShape.Slab && (bi.Data.State & 2) != 0) SpawnDrop(def, center); // (two slabs)
            if (drop && Resin.IsClump(def)) for (int i = 1; i < Resin.FaceCount(bi.Data.State); i++) SpawnDrop(def, center); // (#230: one a face)
            if (drop && def == Blocks.SnowLayer) for (int i = 0; i < (bi.Data.State & 7); i++) SpawnDrop(def, center); // (#244: a snowball a layer)
            if (def == Blocks.Furnace) Crafting.ServerDropContents(key, center);
            if (def == Blocks.BrewingStand) Brewing.ServerDropContents(key, center);
            if (Blocks.IsChest(def) || def == Blocks.Lectern || WoodShelves.Is(def)) Chests.ServerDropContents(key, center); // (#223: a shelf's three too)
            if (def == Blocks.ChiseledBookshelf) Blocks.ServerShelfBooksOut(key, bi.Data.State, center); // (its very books; a book for a slot from before they were kept)
            if (def == Blocks.Jukebox) Jukebox.ServerBroken(key, bi.Data.State, center);
            Noise(center, 10f, 0.5f);
            Redstone.MarkDirty();
            Gravity.MarkDirty();
        }

        public static BlockKey SupportOf(BlockKey k, BlockData d)
        {
            var def = d.Def;
            if (Saplings.IsSapling(def) && (d.State & Saplings.Hanging) != 0) return k.Offset((int)Face.Up); // (#223: a propagule under its leaves)
            switch (def.Shape)
            {
                case BlockShape.Torch:
                case BlockShape.Lever:
                case BlockShape.Button:
                case BlockShape.Hook:
                case BlockShape.Ladder:
                    return k.Offset(Faces.Opposite(d.Facing)); // (their wall: the clump's case below had swallowed these, so a
                                                               // torch, lever, button, hook, ladder or vine hung on the block under it)
                case BlockShape.Clump: // (#230: the block one of its faces is on: the first, ServerLogic's pop-off asks each)
                    for (int dir = 0; dir < 6; dir++) if (Resin.HasFace(d.State, dir)) return k.Offset(dir);
                    return k.Offset((int)Face.Down);
                case BlockShape.Cluster: // (#229: an amethyst bud on the block it grows out of)
                    return k.Offset(Faces.Opposite(d.Facing));
                case BlockShape.Banner: // (#223: a standing banner on the block under it, a wall banner on its wall)
                    return d.Facing == (byte)Face.Up ? k.Offset((int)Face.Down) : k.Offset(Faces.Opposite(d.Facing));
                case BlockShape.Sign: // (#223: a standing sign on the block under it, a wall sign on its wall)
                    return d.Facing == (byte)Face.Up ? k.Offset((int)Face.Down) : k.Offset(Faces.Opposite(d.Facing));
                case BlockShape.HangingSign: // (#223: under the block over it, or on its wall)
                    return d.Facing == (byte)Face.Down ? k.Offset((int)Face.Up) : k.Offset(Faces.Opposite(d.Facing));
                default:
                    return k.Offset((int)Face.Down);
            }
        }

        /// <summary>Host: a block that hung on one that's gone comes off (into `ops`) and drops what it drops, as broken
        /// without a tool (Minecraft's: grass by its neighbour gives seeds now and then, never itself): a crop its crop, a
        /// patterned banner its design (#225), a sign's text forgotten (#223), tall grass's top half with it.</summary>
        public static void PopOff(BlockKey k, BlockData d, List<Op> ops, bool drop)
        {
            var world = BlockWorld.Instance;
            var at = world.WorldCenter(k);
            ops.Add(Op.Remove(k, true));
            if (d.Def == Blocks.TallGrass && (d.State & 1) == 0)
            {
                var up = k.Offset((int)Face.Up);
                var ub = world.Get(up);
                if (ub != null && ub.Data.Def == Blocks.TallGrass && (ub.Data.State & 1) != 0) ops.Add(Op.Remove(up, true));
            }
            if (drop && !Farming.ServerCropDrop(d, at) && !Dyes.ServerGrassDrop(d, at, null) && !BannerDesigns.ServerBroken(k, at, true)) SpawnDrop(d.Def, at);
            else if (!drop) BannerDesigns.ServerBroken(k, at, false);
            if (Signs.IsSign(d.Def)) Signs.ServerRemoved(k);
        }

        /// <summary>(#244) The XP an ore gives when mined, Minecraft's (Blocks.java DropExperienceBlock): coal 0-2, lapis and
        /// quartz 2-5, redstone 1-5, diamond and emerald 3-7, Nether gold 0-1; iron, gold, copper and ancient debris none
        /// (theirs comes from smelting).</summary>
        /// <summary>(#244) A door's or trapdoor's sound, Minecraft's per material: iron's, copper's toggle, wood's.</summary>
        public static string DoorSound(BlockDef def, bool open)
        {
            bool trap = def.Shape == BlockShape.Trapdoor;
            if (def.Key.Contains("iron")) return (trap ? "iron_trapdoor." : "iron_door.") + (open ? "open" : "close");
            if (def.Key.Contains("copper")) return trap ? "copper_trapdoor.toggle" : "copper_door.toggle";
            return open ? "door.open" : "door.close";
        }

        public static int OreXp(BlockDef od)
        {
            if (od == null) return 0;
            string k = od.Key;
            if (k.Contains("diamond_ore") || k.Contains("emerald_ore")) return Random.Range(3, 8);
            if (k.Contains("lapis_ore") || k.Contains("quartz_ore")) return Random.Range(2, 6);
            if (k.Contains("redstone_ore")) return Random.Range(1, 6);
            if (k.Contains("coal_ore")) return Random.Range(0, 3);
            if (k.Contains("nether_gold_ore")) return Random.Range(0, 2);
            return 0;
        }

        /// <summary>(#244) Host, while a block's drops come out: the tool that broke it (its Silk Touch, its Fortune).</summary>
        public static ToolItem MiningTool;
        public static int SilkTouched, Fortuned; // (dev/tests)

        public static int ToolLevel(ToolItem t, EnchKind k) => t != null ? Enchants.Level(t.Ench, k) : 0;

        /// <summary>(#244) Minecraft's Fortune on an ore: ore_drops, times 1 + max(0, a random 0..level+1 less one);
        /// redstone's: up to its level more.</summary>
        static int Fortune(BlockDef od, int count)
        {
            int f = Enchants.FortuneOn ? ToolLevel(MiningTool, EnchKind.Fortune) : 0;
            if (f <= 0 || od == null || !(od.Key.EndsWith("_ore") || od.OreBase != null)) return count;
            Fortuned++;
            if (od.Key.Contains("redstone")) return count + Random.Range(0, f + 1);
            return count * (1 + Mathf.Max(0, Random.Range(0, f + 2) - 1));
        }

        public static void SpawnDrop(BlockDef def, Vector3 pos)
        {
            if (def == null || Amethyst.DropsNothing(def)) return; // (#229: budding amethyst, the buds)
            // (#244) Silk Touch: the block itself, where it is one (an ore's block, glass, grass...)
            if (ToolLevel(MiningTool, EnchKind.SilkTouch) > 0 && ModItems.ItemForBlock(def) is Item silk) { ModItems.ServerSpawnStack(silk, 1, pos); SilkTouched++; return; }
            if (def.DropsNothing) return; // (#244: glass and ice, Minecraft's)
            // (#244) gilded blackstone: Minecraft's 10% chance of 2-5 nuggets (more with Fortune), else the block itself
            if (def == Blocks.GildedBlackstone)
            {
                int gf = Enchants.FortuneOn ? Mathf.Clamp(ToolLevel(MiningTool, EnchKind.Fortune), 0, 3) : 0;
                float gchance = new[] { 0.1f, 0.14285715f, 0.25f, 1f }[gf];
                if (Random.value >= gchance) { var self = ModItems.ItemForBlock(def); if (self != null) ModItems.ServerSpawnStack(self, 1, pos); return; }
            }
            var dropper = def;
            // (gravel: flint now and then instead)
            if (dropper.ChanceDropKey != null && Random.value < dropper.ChanceDrop && ModItems.ByKey.TryGetValue(dropper.ChanceDropKey, out var chance)) { ModItems.ServerSpawnStack(chance, 1, pos); return; }
            def = def.DropAs ?? def;
            var ore = dropper.OreBase ?? dropper;
            if (def.DropsScrap)
            {
                for (int n = Fortune(ore, 1); n > 0; n--) ModItems.ServerSpawnScrap(def, pos); // (#244: Fortune, when it's on)
                return;
            }
            if (def.DropKey != null)
            {
                if (Blocks.Get(def.DropKey) is BlockDef dd) def = dd;
                else if (ModItems.ByKey.TryGetValue(def.DropKey, out var di)) { ModItems.ServerSpawnStack(di, Fortune(ore, dropper.DropMax > 1 ? Random.Range(dropper.DropMin, dropper.DropMax + 1) : 1), pos); return; }
            }
            var item = ModItems.ItemForBlock(def);
            if (item == null) return;
            // (some drop several: redstone ore gives 4-5 dust)
            ModItems.ServerSpawnStack(item, dropper.DropMax > 1 ? Random.Range(dropper.DropMin, dropper.DropMax + 1) : 1, pos);
        }

        public static void HandleGroundDig(ulong sender, Vector3 point, Vector3 normal) => Ground.Dig(sender, point, normal);
        public static void ResetGround() => Ground.Reset();
        static void OnNaturalRemoved(BlockKey key) => Ground.OnRemoved(key);

        /// <summary>Host: [E] on a chiseled bookshelf's slot (#160): a book in hand goes in, else the book there comes out.
        /// Each slot keeps the very book put in (Minecraft's ChiseledBookShelfBlockEntity: its stack, name and all; a book
        /// and quill, a written book): in the shelf's contents, its state's bits only what shows.</summary>
        public static void HandleShelf(ulong sender, BlockKey key, byte slot, string bookKey = null)
        {
            var world = W;
            var bi = world?.Get(key);
            var p = PlayerFor(sender);
            if (bi != null && WoodShelves.Is(bi.Data.Def)) { WoodShelves.ServerUse(sender, key, slot); return; } // (#223)
            if (bi == null || p == null || bi.Data.Def != Blocks.ChiseledBookshelf || slot > 5) { if (!string.IsNullOrEmpty(bookKey) && p != null) Inventory.ServerSpawnFor(sender, bookKey, 1); return; }
            int bit = 1 << slot;
            var d = bi.Data;
            if (!string.IsNullOrEmpty(bookKey))
            {
                // (the book came out of their hand on their machine: in, or back to them)
                if ((d.State & bit) != 0 || !Blocks.ShelfBook(bookKey)) { Inventory.ServerSpawnFor(sender, bookKey, 1); return; }
                var c = Chests.Ensure(key); c.Key[slot] = bookKey; c.Count[slot] = 1;
                d.State = (byte)(d.State | bit);
            }
            else if ((d.State & bit) != 0)
            {
                string back = "book"; // (a shelf filled before its books were kept: a book)
                if (Chests.All.TryGetValue(key, out var c) && c.Count[slot] > 0 && c.Key[slot] != null) { back = c.Key[slot]; c.Key[slot] = null; c.Count[slot] = 0; }
                Inventory.ServerSpawnFor(sender, back, 1);
                d.State = (byte)(d.State & ~bit);
            }
            else return;
            BlockNet.ServerBroadcastOp(Op.State(key, d));
            BlockNet.ServerSound(world.WorldCenter(key), "dig.wood", 0.6f, 1.3f);
            Blocks.ShelfLastSlot[key] = slot + 1;
            Redstone.MarkDirty();
        }

        // ------------------------------------------------------------------ using
        public static void HandleUse(ulong sender, BlockKey key)
        {
            var world = W;
            var bi = world?.Get(key);
            if (bi == null) return;
            var def = bi.Data.Def;
            var pos = world.WorldCenter(key);
            if (Blocks.RedstoneOnly(def)) return; // (#160: iron doors and trapdoors: only redstone opens them)
            if (def.Shape == BlockShape.Fence) { AnimalCare.ServerFence(sender, key); return; } // (#145: a lead tied to it)
            if (def.Shape == BlockShape.Basin) { Composting.ServerUse(sender, key); return; } // (#160)
            if (def == Blocks.DecoratedPot) { Pots.ServerUse(sender, key); return; } // (#160)
            if (def == Blocks.DaylightDetector)
            {
                var d = bi.Data; d.State = (byte)((d.State ^ 16) & 16); // (inverted or not; the next poll sets its signal)
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", 0.5f, 0.6f);
                return;
            }
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
                BlockNet.ServerSound(pos, DoorSound(def, open), 0.8f, Random.Range(0.9f, 1.1f));
                return;
            }
            if (def == Blocks.Jukebox) { Jukebox.ServerEject(key); return; }
            if (def.Shape == BlockShape.Bed)
            {
                if (NetherGround.InRegion(pos))
                {
                    var other = key.Offset((bi.Data.State & 1) != 0 ? Faces.Opposite(bi.Data.Facing) : bi.Data.Facing);
                    BlockNet.ServerBroadcastOps(new List<Op> { Op.Remove(key, false), Op.Remove(other, false) });
                    float r = 4.5f * BlockWorld.S; // (Minecraft: power 5 against TNT's 4)
                    ExplodeBlocks(pos, r, key.Frame, carveGround: true);
                    BlockNet.ServerExplosion(pos, r);
                    Noise(pos, 45f, 1f);
                    for (int i = 0; i < 6; i++) Fire.ServerIgniteAt(pos + new Vector3(Random.Range(-3f, 3f), 0.5f, Random.Range(-3f, 3f)) * BlockWorld.S);
                }
                else BlockNet.ServerToast(sender, "You can't sleep now. Beds don't set where you come back here.");
                return;
            }
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
            if (def.Shape == BlockShape.Comparator)
            {
                // [E]: compare <-> subtract (the front torch lights in subtract mode)
                var d = bi.Data;
                d.State = (byte)(d.State ^ Redstone.ComparatorSubtract);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", 0.4f, (d.State & Redstone.ComparatorSubtract) != 0 ? 0.55f : 0.5f);
                Redstone.MarkDirty();
                return;
            }
            if (def.Shape == BlockShape.Gate) // (#160: [E] swings it, like the trapdoor)
            {
                var gd = bi.Data; gd.State = (byte)(gd.State ^ 1);
                BlockNet.ServerBroadcastOp(Op.State(key, gd));
                BlockNet.ServerSound(pos, (gd.State & 1) != 0 ? "door.open" : "door.close", 0.7f, Random.Range(1.2f, 1.35f));
                return;
            }
            if (def.Shape == BlockShape.Trapdoor)
            {
                var d = bi.Data; d.State = (byte)(d.State ^ 1);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, DoorSound(def, (d.State & 1) != 0), 0.7f, Random.Range(1.1f, 1.25f));
                return;
            }
            if (def == Blocks.Lever)
            {
                var d = bi.Data; d.State = (byte)(d.State ^ 1);
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", (d.State & 1) != 0 ? 0.6f : 0.5f, 0.6f);
                Redstone.MarkDirty();
            }
            else if (def.Shape == BlockShape.Button)
            {
                if ((bi.Data.State & 1) != 0) return;
                var d = bi.Data; d.State = 1;
                BlockNet.ServerBroadcastOp(Op.State(key, d));
                BlockNet.ServerSound(pos, "click", 0.6f, 0.6f);
                // (Minecraft: a stone button stays in a second, a wooden one a second and a half)
                Redstone.Schedule(def.Sound == "wood" ? 30 : 20, () =>
                {
                    var b = W?.Get(key);
                    if (b == null || b.Data.Def.Shape != BlockShape.Button) return;
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
                if (below != null && below.Sound == "wood" && below.Solid) inst = "note.bass"; // (Minecraft's: any wood under it, #223's woods too)
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
            var gone = new List<(BlockKey, BlockDef)>();
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
                if (Blocks.IsChest(def) || def == Blocks.Lectern || WoodShelves.Is(def)) Chests.ServerDropContents(kv.Key, c); // (#223: a shelf's three too)
                if (def == Blocks.ChiseledBookshelf) Blocks.ServerShelfBooksOut(kv.Key, kv.Value.Data.State, c);
                if (def == Blocks.Furnace) Crafting.ServerDropContents(kv.Key, c);
                if (def == Blocks.BrewingStand) Brewing.ServerDropContents(kv.Key, c);
                bool dropIt = Random.value < Balance.ExplosionDropChance && def.Shape != BlockShape.PistonHead && !(def.DropsScrap && ModItems.ItemForBlock(def) == null);
                if (Amethyst.ServerClusterExploded(def, c, radius / BlockWorld.S)) dropIt = false; // (#229: Minecraft's explosion decay, its own)
                if (!BannerDesigns.ServerBroken(kv.Key, c, dropIt) && dropIt) SpawnDrop(def, c); // (#225: a patterned banner drops its design, or forgets it)
                if (Signs.IsSign(def)) Signs.ServerRemoved(kv.Key); // (#223)
                gone.Add((kv.Key, def));
            }
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            foreach (var (gk, gd) in gone) Saplings.ServerBlockRemoved(gk, gd); // (#223: leaves no log holds any more decay)
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
                if (Blocks.IsAnvil(world.Get(k)?.Data.Def)) { var at = cur; int fell = dist; Redstone.Schedule(ticks, () => Anvils.ServerLanded(at, fell)); } // (#228: it hurts what it lands on)
            }
            if (ops.Count > 0)
            {
                BlockNet.ServerBroadcastOps(ops);
                // land sound once things arrive
                var landings = ops.Where(o => o.Type == OpType.Move).ToList();
                if (landings.Count > 0)
                {
                    var first = landings[0];
                    Redstone.Schedule(first.Fx, () => { if (BlockWorld.Instance != null && !Blocks.IsAnvil(BlockWorld.Instance.DefAt(first.Key2))) BlockNet.ServerSound(BlockWorld.Instance.WorldCenter(first.Key2), "step.sand", 0.9f, 0.8f); });
                }
                dirty = true; // blocks above may now fall too
            }
        }
    }
}
