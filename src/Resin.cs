namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's resin (#230, the owner's): resin clumps, found as loot (the mod has no creaking hearts: piles inside the
    /// facility, outpost chests), smelted into resin brick; a block of resin (nine clumps, and back), resin bricks (four
    /// resin brick) and their stairs, slab and wall, chiseled resin bricks. A clump goes on a face of a full block (floor,
    /// wall or ceiling) as Minecraft's MultifaceBlock, one face to a cell here. Resin brick is a trim material too
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
