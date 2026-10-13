using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's Redstone tab, the blocks this mod lacked (#160). Ids from 400 up (the block families take 118 up),
    /// in this order: add new ones only at the end.
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextRedstoneId = 400;
        public static BlockDef IronDoor, IronTrapdoor, Target, DaylightDetector, Barrel, TripwireHook, Tripwire, TrappedChest, Hopper, Dropper, Dispenser, Crafter, Composter, Cauldron, Rail, PoweredRail, DetectorRail, ActivatorRail;

        /// <summary>A block that holds stacks (a chest's storage): the chest, the trapped chest, the barrel, and the
        /// hopper, the dropper and the dispenser (#160).</summary>
        public static bool IsChest(BlockDef d) => d != null && (d == Chest || d == Barrel || d == TrappedChest || d == Hopper || d == Dropper || d == Dispenser || d == Crafter);

        /// <summary>How many slots a container block has.</summary>
        public static int SlotsOf(BlockDef d) => d == null ? Chests.Size : d == Hopper ? 5 : d == Dropper || d == Dispenser || d == Crafter ? 9 : Chests.Size;
        static readonly Dictionary<string, (float h, ToolKind t, int tier)> redstoneHardness = new Dictionary<string, (float, ToolKind, int)>();

        static BlockDef AddR(string key, string name, string tile, float hardness, ToolKind tool, int tier, System.Action<BlockDef> cfg)
        {
            var d = Add(nextRedstoneId++, key, name, tile, cfg);
            redstoneHardness[key] = (hardness, tool, tier);
            return d;
        }

        /// <summary>Iron doors and trapdoors open only by redstone ([E] does nothing), as in Minecraft.</summary>
        public static bool RedstoneOnly(BlockDef d) => d != null && (d == IronDoor || d == IronTrapdoor);

        static void AddRedstoneBlocks()
        {
            // buttons and pressure plates of each wood and of polished blackstone; weighted plates (gold, iron)
            foreach (var (pre, Pre, tile, wood) in new[] { ("oak", "Oak", "oak_planks", true), ("dark_oak", "Dark Oak", "oak_planks_dark", true), ("polished_blackstone", "Polished Blackstone", "polished_blackstone", false) })
            {
                AddR(pre + "_button", Pre + " Button", tile, 0.5f, wood ? ToolKind.Axe : ToolKind.Pickaxe, 0, d =>
                {
                    d.Shape = BlockShape.Button; d.Directional = true; d.Sound = wood ? "wood" : "stone"; d.IsRedstoneComponent = true;
                    d.Description = wood ? "[E]: a second and a half of power." : "[E]: a second of power.";
                });
                AddR(pre + "_pressure_plate", Pre + " Pressure Plate", tile, 0.5f, wood ? ToolKind.Axe : ToolKind.Pickaxe, 0, d =>
                {
                    d.Shape = BlockShape.Plate; d.Sound = wood ? "wood" : "stone"; d.IsRedstoneComponent = true;
                    d.Description = wood ? "Powered while anything is on it: someone, a monster, or an item." : "Powered while someone or a monster stands on it.";
                });
            }
            AddR("light_weighted_pressure_plate", "Light Weighted Pressure Plate", "gold_block", 0.5f, ToolKind.Pickaxe, 0, d =>
            {
                d.Shape = BlockShape.Plate; d.Sound = "metal"; d.IsRedstoneComponent = true;
                d.Description = "Its signal grows with how many things are on it: one per thing, up to 15.";
            });
            // (the target's recipe needs Minecraft's hay bale: nine wheat)
            AddR("hay_block", "Hay Bale", "hay_block_side", 0.5f, ToolKind.Hoe, 0, d =>
            {
                d.TileTop = d.TileBottom = "hay_block_top"; d.Directional = true; d.Sound = "grass"; d.Flammable = true;
                d.Description = "Nine wheat, baled. Place it sideways by looking sideways.";
            });
            Target = AddR("target", "Target", "target_side", 0.5f, ToolKind.Hoe, 0, d =>
            {
                d.TileTop = d.TileBottom = "target_top"; d.Sound = "grass"; d.IsRedstoneComponent = true;
                d.Description = "Hit it with an arrow or a snowball: a pulse of power, stronger the nearer the middle.";
            });
            DaylightDetector = AddR("daylight_detector", "Daylight Detector", "daylight_detector_side", 0.2f, ToolKind.Axe, 0, d =>
            {
                d.Shape = BlockShape.Detector; d.TileTop = "daylight_detector_top"; d.TileBottom = "oak_planks"; d.Sound = "wood"; d.IsRedstoneComponent = true;
                d.Description = "Power by daylight: strongest at midday, none at night or indoors. [E] turns it round (power by night).";
            });
            // the barrel: a chest's storage in a block, opening toward you (comparators read it as they do a chest)
            Barrel = AddR("barrel", "Barrel", "barrel_side", 2.5f, ToolKind.Axe, 0, d =>
            {
                d.TileTop = d.TileBottom = "barrel_side"; d.TileFront = "barrel_top"; d.Directional = true; d.Sound = "wood"; d.Pushable = false; d.Flammable = true;
                d.Description = "Holds as much as a chest. [E] to open it. Its lid faces you; blocks on it don't stop it opening.";
            });
            // tripwire: two hooks facing each other on walls, string laid between them; anything crossing the line powers both
            TripwireHook = AddR("tripwire_hook", "Tripwire Hook", "oak_planks", 0f, ToolKind.None, 0, d =>
            {
                d.Shape = BlockShape.Hook; d.Directional = true; d.Sound = "wood"; d.Render = RenderKind.Cutout; d.IsRedstoneComponent = true;
                d.Description = "Put two on walls facing each other, string between them on the ground: whoever (or whatever) crosses it powers both hooks.";
            });
            Tripwire = AddR("tripwire", "Tripwire", "tripwire", 0f, ToolKind.None, 0, d =>
            {
                d.Shape = BlockShape.Tripwire; d.Render = RenderKind.Cutout; d.Sound = "wool"; d.DropKey = "string"; d.Pushable = false;
                d.Description = "String laid on the ground between two tripwire hooks.";
            });
            // the trapped chest: a chest that gives power while someone's looking in (one per player, up to 15)
            TrappedChest = AddR("trapped_chest", "Trapped Chest", "chest_side", 2.5f, ToolKind.Axe, 0, d =>
            {
                d.TileTop = d.TileBottom = "chest_top"; d.TileFront = "chest_front"; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.Pushable = false;
                d.IsRedstoneComponent = true; d.Flammable = true;
                d.Description = "A chest that gives power while it's open: stronger the more players look in.";
            });
            // fence gates: [E] or redstone opens them
            foreach (var (pre, Pre, tile) in new[] { ("oak", "Oak", "oak_planks"), ("dark_oak", "Dark Oak", "oak_planks_dark") })
                AddR(pre + "_fence_gate", Pre + " Fence Gate", tile, 2f, ToolKind.Axe, 0, d =>
                {
                    d.Shape = BlockShape.Gate; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.IsRedstoneComponent = true; d.Flammable = true;
                    d.Description = "A gate in a fence: [E] (or redstone) opens it. Shut, nothing walks through.";
                });
            // item movers (ItemTransport)
            Hopper = AddR("hopper", "Hopper", "hopper_outside", 3f, ToolKind.Pickaxe, 1, d =>
            {
                d.Shape = BlockShape.Hopper; d.TileTop = "hopper_top"; d.TileBottom = "hopper_inside"; d.Directional = true; d.Sound = "metal"; d.Pushable = false;
                d.IsRedstoneComponent = true;
                d.Description = "Takes items from the container above it (or lying on it) and passes them, one at a time, into the one its spout points at. Locked while powered.";
            });
            Dropper = AddR("dropper", "Dropper", "furnace_side", 3.5f, ToolKind.Pickaxe, 1, d =>
            {
                d.TileTop = d.TileBottom = "furnace_top"; d.TileFront = "dropper_front"; d.Directional = true; d.Sound = "stone"; d.Pushable = false; d.IsRedstoneComponent = true;
                d.Description = "Nine slots. A redstone pulse drops one item out of its face, or into a container there.";
            });
            Dispenser = AddR("dispenser", "Dispenser", "furnace_side", 3.5f, ToolKind.Pickaxe, 1, d =>
            {
                d.TileTop = d.TileBottom = "furnace_top"; d.TileFront = "dispenser_front"; d.Directional = true; d.Sound = "stone"; d.Pushable = false; d.IsRedstoneComponent = true;
                d.Description = "Nine slots. A redstone pulse uses one item out of its face: shoots arrows and snowballs, pours or fills buckets, lights fires, primes TNT, bone-meals crops, hatches eggs; anything else it drops.";
            });
            // the crafter (1.21): its nine slots are a crafting grid; a pulse crafts once, out of its face
            Crafter = AddR("crafter", "Crafter", "crafter_east", 1.5f, ToolKind.Pickaxe, 0, d =>
            {
                d.TileTop = "crafter_top"; d.TileBottom = "crafter_bottom"; d.TileFront = "crafter_north"; d.Directional = true; d.Sound = "stone"; d.Pushable = false; d.IsRedstoneComponent = true;
                d.Description = "Nine slots laid out as a crafting grid (fill them with a recipe). A redstone pulse crafts it once: the result comes out of its face, into a container there or onto the ground.";
            });
            Composter = AddR("composter", "Composter", "composter_side", 0.6f, ToolKind.Axe, 0, d =>
            {
                d.Shape = BlockShape.Basin; d.TileTop = "composter_top"; d.TileBottom = "composter_bottom"; d.Sound = "wood"; d.Flammable = true; d.IsRedstoneComponent = true;
                d.Description = "[E] with seeds, crops, leaves or other plant matter: now and then its level rises; full, it gives bone meal. Hoppers feed it and empty it.";
            });
            Cauldron = AddR("cauldron", "Cauldron", "cauldron_side", 2f, ToolKind.Pickaxe, 1, d =>
            {
                d.Shape = BlockShape.Basin; d.TileTop = "cauldron_top"; d.TileBottom = "cauldron_bottom"; d.Sound = "metal"; d.IsRedstoneComponent = true;
                d.Description = "Holds a bucket of water (three bottles' worth) or of lava.";
            });
            // rails (Minecarts.cs)
            Rail = AddR("rail", "Rail", "rail", 0.7f, ToolKind.Pickaxe, 0, d => { d.Shape = BlockShape.Rail; d.Render = RenderKind.Cutout; d.Sound = "metal"; d.Description = "Lay a line for a minecart: rails join up, round corners and up slopes."; });
            PoweredRail = AddR("powered_rail", "Powered Rail", "powered_rail", 0.7f, ToolKind.Pickaxe, 0, d => { d.Shape = BlockShape.Rail; d.Render = RenderKind.Cutout; d.Sound = "metal"; d.IsRedstoneComponent = true; d.Description = "Powered, it speeds a minecart up (the power runs along 8 of them); unpowered, it stops one."; });
            DetectorRail = AddR("detector_rail", "Detector Rail", "detector_rail", 0.7f, ToolKind.Pickaxe, 0, d => { d.Shape = BlockShape.Rail; d.Render = RenderKind.Cutout; d.Sound = "metal"; d.IsRedstoneComponent = true; d.Description = "Gives power while a minecart is on it."; });
            ActivatorRail = AddR("activator_rail", "Activator Rail", "activator_rail", 0.7f, ToolKind.Pickaxe, 0, d => { d.Shape = BlockShape.Rail; d.Render = RenderKind.Cutout; d.Sound = "metal"; d.IsRedstoneComponent = true; d.Description = "Powered, it sets off the carts that do something (to come)."; });
            AddR("heavy_weighted_pressure_plate", "Heavy Weighted Pressure Plate", "iron_block", 0.5f, ToolKind.Pickaxe, 0, d =>
            {
                d.Shape = BlockShape.Plate; d.Sound = "metal"; d.IsRedstoneComponent = true;
                d.Description = "Its signal grows with how many things are on it: one for every ten.";
            });
            IronDoor = AddR("iron_door", "Iron Door", "iron_door_bottom", 5f, ToolKind.Pickaxe, 1, d =>
            {
                d.Shape = BlockShape.Door; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "metal"; d.Render = RenderKind.Cutout;
                d.TileFront = "iron_door_top"; d.IsRedstoneComponent = true;
                d.Description = "Two blocks tall; only redstone opens it (a button or lever beside it), so monsters and the curious stay out.";
            });
            IronTrapdoor = AddR("iron_trapdoor", "Iron Trapdoor", "iron_trapdoor", 5f, ToolKind.Pickaxe, 1, d =>
            {
                d.Shape = BlockShape.Trapdoor; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "metal"; d.IsRedstoneComponent = true;
                d.Description = "A trapdoor only redstone opens.";
            });
        }
    }
}
