using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's resin (#230, the owner's): resin clumps, found as loot (the mod has no creaking hearts: piles inside the
    /// facility, outpost chests), smelted into resin brick; a block of resin (nine clumps, and back), resin bricks (four
    /// resin brick) and their stairs, slab and wall, chiseled resin bricks. A clump goes on a face of a full block (floor,
    /// wall or ceiling) as Minecraft's MultifaceBlock: up to six in one cell, one on each face (the state's bits, one a
    /// direction toward the block it's on); each comes off with the block it's on, and breaking the cell drops one a
    /// face; any other block put there takes the cell (Minecraft's canBeReplaced). Resin brick is a trim material too
    /// (ArmorTrims). Ids from 750.
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextResinId = 750;
        public static BlockDef ResinClump, ResinBlock, ResinBricks;

        static BlockDef AddR(string key, string name, string tile, float hardness, ToolKind tool, int tier, string sound, System.Action<BlockDef> cfg)
        {
            FamilyTiles[tile] = tile;
            var d = Add(nextResinId++, key, name, tile, x => { x.Sound = sound; cfg?.Invoke(x); });
            familyHardness[key] = (hardness, tool, tier);
            return d;
        }

        static void AddResin()
        {
            // (Minecraft's Blocks: the clump and the block break at once, the bricks at 1.5 with a pickaxe)
            ResinClump = AddR("resin_clump", "Resin Clump", "resin_clump", 0f, ToolKind.None, 0, "resin", x =>
            {
                x.Shape = BlockShape.Clump; x.Render = RenderKind.Cutout; x.Pushable = false;
                x.Description = "Sticky orange resin. Put it on a floor, a wall or a ceiling; smelt it into resin brick, or nine make a block of resin.";
            });
            ResinBlock = AddR("resin_block", "Block of Resin", "resin_block", 0f, ToolKind.None, 0, "resin", x => x.Description = "Nine resin clumps. Craft it back into nine.");
            ResinBricks = AddR("resin_bricks", "Resin Bricks", "resin_bricks", 1.5f, ToolKind.Pickaxe, 1, "resin_bricks", x => x.Description = "Four resin brick, smelted from resin clumps.");
            foreach (var (suffix, nameSuffix, shape) in new[] { ("stairs", "Stairs", BlockShape.Stairs), ("slab", "Slab", BlockShape.Slab), ("wall", "Wall", BlockShape.Wall) })
            {
                var d = AddR("resin_brick_" + suffix, "Resin Brick " + nameSuffix, "resin_bricks", 1.5f, ToolKind.Pickaxe, 1, "resin_bricks", x =>
                {
                    x.Shape = shape;
                    x.Description = shape == BlockShape.Wall ? "A wall a little over a block high: it joins the walls and blocks beside it." : shape == BlockShape.Slab ? "Half a block. Two in one space make a full block." : "Walk straight up them, no jumping.";
                    if (shape == BlockShape.Stairs) { x.Directional = true; x.FacingIncludesVertical = false; }
                });
                FamilyShapes.Add(("resin_bricks", d.Key, shape));
            }
            AddR("chiseled_resin_bricks", "Chiseled Resin Bricks", "chiseled_resin_bricks", 1.5f, ToolKind.Pickaxe, 1, "resin_bricks", x => x.Description = "Resin bricks carved with a face (two resin brick slabs).");
        }
    }

    /// <summary>(#230) Resin clumps as loot: one facility in two has a pile of 3-9 on one of its scrap spots (outpost
    /// chests have theirs: Outposts.FillChest).</summary>
    public static class Resin
    {
        public const string Clump = "resin_clump";
        public static int PilesSpawned; // (dev/tests)

        // ------------------------------------------------------------------ Minecraft's MultifaceBlock
        public static bool IsClump(BlockDef d) => d != null && d == Blocks.ResinClump;
        public static bool HasFace(byte state, int dir) => (state & (1 << dir)) != 0;
        public static int FaceCount(byte state) { int n = 0; for (int d = 0; d < 6; d++) if (HasFace(state, d)) n++; return n; }

        /// <summary>Whether a clump in cell k can hold onto the block toward `dir` (a full block; the ground, the ship's
        /// floor and walls, anything of the level's own that fills that cell).</summary>
        public static bool CanAttach(BlockKey k, int dir)
        {
            var w = BlockWorld.Instance;
            var n = k.Offset(dir);
            var b = w.Get(n);
            if (b != null) return b.Data.Def.Solid;
            if (n.Frame == 0 && n.YOff == Ground.GridYOff && Ground.IsSolidCell(n.Pos)) return true;
            return ServerLogic.Obstructed(n, 0.8f);
        }

        /// <summary>Host: a clump put on another face of a cell that has clumps already (false: that face is taken, or
        /// there's nothing to hold onto).</summary>
        public static bool ServerAddFace(BlockKey k, BlockInstance ex, int dir)
        {
            if (ex == null || !IsClump(ex.Data.Def) || dir < 0 || dir > 5 || HasFace(ex.Data.State, dir) || !CanAttach(k, dir)) return false;
            var d = ex.Data; d.State = (byte)(d.State | (1 << dir));
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            return true;
        }

        /// <summary>Host: the block toward `dir` from a clump cell went: that face's clump comes off (and drops); the
        /// last one takes the cell with it. True: it was a clump cell (handled).</summary>
        public static bool ServerFaceGone(BlockKey k, BlockData d, int dir, List<Op> ops, bool drop)
        {
            if (!IsClump(d.Def)) return false;
            if (!HasFace(d.State, dir)) return true;
            var left = (byte)(d.State & ~(1 << dir));
            var w = BlockWorld.Instance;
            if (left == 0) ops.Add(Op.Remove(k, true)); else { d.State = left; ops.Add(Op.State(k, d)); }
            if (drop) ServerLogic.SpawnDrop(d.Def, w.WorldCenter(k));
            return true;
        }

        /// <summary>Host: a clump cell's faces that nothing holds any more come off (ground dug away under them).</summary>
        public static bool ServerCheckFaces(BlockKey k, BlockData d, List<Op> ops)
        {
            if (!IsClump(d.Def)) return false;
            for (int dir = 0; dir < 6; dir++)
                if (HasFace(d.State, dir) && !CanAttach(k, dir)) { ServerFaceGone(k, d, dir, ops, true); d.State = (byte)(d.State & ~(1 << dir)); if (d.State == 0) break; }
            return true;
        }

        /// <summary>Host, after the facility's scrap.</summary>
        public static void ServerSpawnLoot()
        {
            try
            {
                var spots = UnityEngine.Object.FindObjectsOfType<RandomScrapSpawn>();
                if (spots.Length == 0 || !ModItems.ByKey.TryGetValue(Clump, out var item)) return;
                var rng = new System.Random(StartOfRound.Instance.randomMapSeed + 230); // (its own: the farm piles stay where they were)
                if (rng.Next(2) != 0) return;
                var at = spots[rng.Next(spots.Length)].transform.position + UnityEngine.Vector3.up * 0.3f;
                if (ModItems.ServerSpawnStack(item, rng.Next(3, 10), at) != null) PilesSpawned++;
            }
            catch (System.Exception e) { Plugin.Log.LogError("Resin loot: " + e); }
        }
    }
}
