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
        public static BlockDef IronDoor, IronTrapdoor;
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
