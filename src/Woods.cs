using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's woods (#223, the owner's): oak, birch, dark oak and mangrove, each its whole family: the log, its wood
    /// (bark all round), both stripped (an axe's use button on a log or wood), planks, stairs, slab, fence, fence gate,
    /// door, trapdoor, button, pressure plate and leaves. Oak and dark oak had some of theirs already: those stay as they
    /// were (their ids too), the rest come here, ids from 600 so nothing earlier moves. Mangrove brings its roots and
    /// Minecraft's mud family (mud from a water bottle on dirt, packed mud, mud bricks and their stairs, slab and wall,
    /// muddy mangrove roots). Logs come from the moons' trees (Trees: Dine's are dark oak, March's mangrove, the rest oak)
    /// and the store.
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextWoodId = 600;

        public class WoodKind
        {
            public string Key, Name;          // "birch", "Birch"
            public BlockDef Log, Wood, StrippedLog, StrippedWood, Planks, Stairs, Slab, Fence, Gate, Door, Trapdoor, Button, Plate, Leaves, Sapling;
        }

        public static readonly List<WoodKind> Woods = new List<WoodKind>();
        public static WoodKind Oak, Birch, DarkOak, Mangrove;
        public static BlockDef Mud, PackedMud, MudBricks, MangroveRoots, MuddyMangroveRoots;
        static readonly Dictionary<BlockDef, BlockDef> strippedOf = new Dictionary<BlockDef, BlockDef>();
        static readonly HashSet<BlockDef> pillars = new HashSet<BlockDef>();

        /// <summary>What an axe strips it to (a log or wood), or null.</summary>
        public static BlockDef StrippedOf(BlockDef d) => d != null && strippedOf.TryGetValue(d, out var s) ? s : null;
        /// <summary>A log, wood or stripped one: placed along the face clicked (its axis), as Minecraft's pillars.</summary>
        public static bool IsPillar(BlockDef d) => d != null && (d == Log || pillars.Contains(d));
        /// <summary>The wood a block belongs to (its planks, logs, leaves...), or null.</summary>
        public static WoodKind WoodOf(BlockDef d) => d == null ? null : Woods.FirstOrDefault(w => w.Log == d || w.Wood == d || w.StrippedLog == d || w.StrippedWood == d || w.Planks == d || w.Leaves == d);

        static BlockDef AddW(string key, string name, string tile, float hardness, ToolKind tool, System.Action<BlockDef> cfg)
        {
            FamilyTiles[tile] = tile;
            var d = Add(nextWoodId++, key, name, tile, x => { x.Sound = "wood"; x.Flammable = true; cfg?.Invoke(x); });
            foreach (var t in new[] { d.TileTop, d.TileBottom, d.TileFront }) if (t != null && t != tile && !t.StartsWith("oak_planks") && t != "log_side" && t != "log_top") FamilyTiles[t] = t;
            familyHardness[key] = (hardness, tool, 0);
            return d;
        }

        static BlockDef Shaped(BlockDef planks, string key, string name, BlockShape shape, string desc)
        {
            var d = AddW(key, name, planks.TileSide, 2f, ToolKind.Axe, x =>
            {
                x.TileTop = planks.TileTop; x.TileBottom = planks.TileBottom; x.Shape = shape; x.Description = desc;
                if (shape == BlockShape.Stairs) { x.Directional = true; x.FacingIncludesVertical = false; }
            });
            FamilyShapes.Add((planks.Key, key, shape));
            return d;
        }

        static void AddWoods()
        {
            Oak = new WoodKind { Key = "oak", Name = "Oak", Log = Log, Planks = Planks, Stairs = OakStairs, Slab = OakSlab, Fence = Get("oak_fence"), Gate = Get("oak_fence_gate"), Door = OakDoor, Trapdoor = OakTrapdoor, Button = Get("oak_button"), Plate = Get("oak_pressure_plate"), Leaves = Leaves };
            DarkOak = new WoodKind { Key = "dark_oak", Name = "Dark Oak", Planks = DarkPlanks, Stairs = Get("dark_oak_stairs"), Slab = Get("dark_oak_slab"), Fence = Get("dark_oak_fence"), Gate = Get("dark_oak_fence_gate"), Button = Get("dark_oak_button"), Plate = Get("dark_oak_pressure_plate") };
            Birch = new WoodKind { Key = "birch", Name = "Birch" };
            Mangrove = new WoodKind { Key = "mangrove", Name = "Mangrove" };
            Woods.AddRange(new[] { Oak, Birch, DarkOak, Mangrove });
            foreach (var w in Woods)
            {
                string k = w.Key, n = w.Name, logTile = k == "oak" ? "log_side" : k + "_log", logTop = k == "oak" ? "log_top" : k + "_log_top";
                // logs and wood: an axis each (IsPillar); a log's top shows its rings, wood is bark all round
                if (w.Log == null) w.Log = AddW(k + "_log", n + " Log", logTile, 2f, ToolKind.Axe, x =>
                {
                    x.TileTop = x.TileBottom = logTop; x.Directional = true; x.ShopPrice = 40; x.ShopStack = 16;
                    x.Description = $"A {n.ToLower()} log. Four {n.ToLower()} planks; place it sideways by looking sideways.";
                });
                w.Wood = AddW(k + "_wood", n + " Wood", logTile, 2f, ToolKind.Axe, x => { x.Directional = true; x.Description = $"{n} bark all round: four logs make three."; });
                w.StrippedLog = AddW("stripped_" + k + "_log", "Stripped " + n + " Log", "stripped_" + k + "_log", 2f, ToolKind.Axe, x =>
                {
                    x.TileTop = x.TileBottom = "stripped_" + k + "_log_top"; x.Directional = true;
                    x.Description = $"A {n.ToLower()} log with its bark taken off by an axe.";
                });
                w.StrippedWood = AddW("stripped_" + k + "_wood", "Stripped " + n + " Wood", "stripped_" + k + "_log", 2f, ToolKind.Axe, x => { x.Directional = true; x.Description = $"{n} wood with its bark taken off by an axe."; });
                foreach (var p in new[] { w.Log, w.Wood, w.StrippedLog, w.StrippedWood }) pillars.Add(p);
                strippedOf[w.Log] = w.StrippedLog; strippedOf[w.Wood] = w.StrippedWood;
                if (w.Planks == null) w.Planks = AddW(k + "_planks", n + " Planks", k + "_planks", 2f, ToolKind.Axe, x => { x.Description = $"{n} planks: four from a log."; });
                if (w.Stairs == null) w.Stairs = Shaped(w.Planks, k + "_stairs", n + " Stairs", BlockShape.Stairs, "Walk straight up them, no jumping.");
                if (w.Slab == null) w.Slab = Shaped(w.Planks, k + "_slab", n + " Slab", BlockShape.Slab, "Half a block. Two in one space make a full block.");
                if (w.Fence == null) w.Fence = Shaped(w.Planks, k + "_fence", n + " Fence", BlockShape.Fence, "Joins the fences and blocks beside it.");
                string planks = w.Planks.TileSide;
                if (w.Gate == null) w.Gate = AddW(k + "_fence_gate", n + " Fence Gate", planks, 2f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.Gate; x.Directional = true; x.FacingIncludesVertical = false; x.IsRedstoneComponent = true;
                    x.Description = "A gate in a fence: [E] (or redstone) opens it. Shut, nothing walks through.";
                });
                if (w.Door == null) w.Door = AddW(k + "_door", n + " Door", k + "_door_bottom", 3f, ToolKind.Axe, x =>
                {
                    x.TileFront = k + "_door_top"; x.TileBottom = planks; x.Shape = BlockShape.Door; x.Directional = true; x.FacingIncludesVertical = false;
                    x.Render = RenderKind.Cutout; x.IsRedstoneComponent = true;
                    x.Description = "Two blocks tall. [E] to open and close it.";
                });
                if (w.Trapdoor == null) w.Trapdoor = AddW(k + "_trapdoor", n + " Trapdoor", k + "_trapdoor", 3f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.Trapdoor; x.Directional = true; x.FacingIncludesVertical = false; x.Render = RenderKind.Cutout; x.IsRedstoneComponent = true;
                    x.Description = "A hatch: [E] to open and close it. Cover a hole, or a window.";
                });
                if (w.Button == null) w.Button = AddW(k + "_button", n + " Button", planks, 0.5f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.Button; x.Directional = true; x.IsRedstoneComponent = true; x.Description = "[E]: a second and a half of power.";
                });
                if (w.Plate == null) w.Plate = AddW(k + "_pressure_plate", n + " Pressure Plate", planks, 0.5f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.Plate; x.IsRedstoneComponent = true; x.Description = "Powered while anything is on it: someone, a monster, or an item.";
                });
                if (w.Leaves == null) w.Leaves = AddW(k + "_leaves", n + " Leaves", k + "_leaves", 0.2f, ToolKind.Hoe, x =>
                {
                    x.Sound = "grass"; x.Render = RenderKind.Cutout; x.Description = $"{n} leaves.";
                });
            }
            // mangrove roots, and Minecraft's mud family
            MangroveRoots = AddW("mangrove_roots", "Mangrove Roots", "mangrove_roots_side", 0.7f, ToolKind.Axe, x =>
            {
                x.TileTop = x.TileBottom = "mangrove_roots_top"; x.Render = RenderKind.Cutout; x.Sound = "mangrove_roots";
                x.Description = "A mangrove's tangle of roots.";
            });
            Mud = AddW("mud", "Mud", "mud", 0.5f, ToolKind.Shovel, x => { x.Sound = "mud"; x.Flammable = false; x.Description = "Wet dirt: a water bottle on dirt. Packed with wheat, it makes bricks."; });
            PackedMud = AddW("packed_mud", "Packed Mud", "packed_mud", 1f, ToolKind.Pickaxe, x => { x.Sound = "packed_mud"; x.Flammable = false; x.Description = "Mud and wheat, packed: four make mud bricks."; });
            MudBricks = AddW("mud_bricks", "Mud Bricks", "mud_bricks", 1.5f, ToolKind.Pickaxe, x => { x.Sound = "mud_bricks"; x.Flammable = false; x.Description = "Bricks of packed mud."; });
            familyHardness["packed_mud"] = (1f, ToolKind.Pickaxe, 0); familyHardness["mud_bricks"] = (1.5f, ToolKind.Pickaxe, 1);
            foreach (var (suffix, nameSuffix, shape) in new[] { ("stairs", "Stairs", BlockShape.Stairs), ("slab", "Slab", BlockShape.Slab), ("wall", "Wall", BlockShape.Wall) })
            {
                var d = AddW("mud_brick_" + suffix, "Mud Brick " + nameSuffix, "mud_bricks", 1.5f, ToolKind.Pickaxe, x =>
                {
                    x.Shape = shape; x.Sound = "mud_bricks"; x.Flammable = false;
                    x.Description = shape == BlockShape.Wall ? "A wall a little over a block high: it joins the walls and blocks beside it." : shape == BlockShape.Slab ? "Half a block. Two in one space make a full block." : "Walk straight up them, no jumping.";
                    if (shape == BlockShape.Stairs) { x.Directional = true; x.FacingIncludesVertical = false; }
                });
                familyHardness[d.Key] = (1.5f, ToolKind.Pickaxe, 1);
                FamilyShapes.Add(("mud_bricks", d.Key, shape));
            }
            MuddyMangroveRoots = AddW("muddy_mangrove_roots", "Muddy Mangrove Roots", "muddy_mangrove_roots_side", 0.7f, ToolKind.Shovel, x =>
            {
                x.TileTop = x.TileBottom = "muddy_mangrove_roots_top"; x.Sound = "muddy_mangrove_roots"; x.Directional = true;
                x.Description = "Mangrove roots packed with mud.";
            });
            pillars.Remove(MuddyMangroveRoots); // (it turns with its axis, but isn't wood: leaves don't hang on it)
            // saplings (Saplings): planted on dirt-like ground, they grow their trees; sold in the store too (birch has no moon)
            foreach (var w in Woods)
            {
                bool prop = w == Mangrove;
                string key = prop ? "mangrove_propagule" : w.Key + "_sapling";
                w.Sapling = AddW(key, prop ? "Mangrove Propagule" : w.Name + " Sapling", key, 0f, ToolKind.None, x =>
                {
                    x.Shape = BlockShape.Sapling; x.Render = RenderKind.Cutout; x.Sound = "grass"; x.ShopPrice = 12; x.ShopStack = 4;
                    x.Description = prop ? "Plant it on dirt, grass or mud: it grows into a mangrove, on roots." : w == DarkOak
                        ? "Four planted in a square grow into a dark oak (one alone doesn't grow)." : $"Plant it on dirt or grass: it grows into {(w == Oak ? "an oak" : "a birch")} tree. Bone meal hurries it.";
                });
            }
            FamilyTiles["mangrove_propagule_hanging"] = "mangrove_propagule_hanging";
        }
    }
}
