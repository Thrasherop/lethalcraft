using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    public enum BlockShape : byte
    {
        Cube,       // full solid cube
        Torch,      // small stick, non-solid
        Dust,       // flat on floor, non-solid
        Lever,      // small base + handle, non-solid, interactable
        Button,     // tiny box on a face, non-solid, interactable
        Plate,      // thin plate on floor, non-solid, triggered by walking
        PistonHead, // internal block: the moving arm of an extended piston
        Ladder,     // thin panel on a wall
        Fire,       // flickering flame planes, non-solid (lit by flint and steel)
        Lava,       // a pool of lava filling its cell: non-solid, burns whoever is in it
        Pane,       // a thin pane joining up with the blocks and panes beside it (glass panes)
        Door,       // two blocks tall, a thin panel at the front; [E] swings it open (state: 1 open, 2 upper half, 4 hinge right)
        Stairs,     // a full slab and a half step on top at the back (the low side faces the way it was placed from)
    }

    public enum RenderKind : byte { Opaque, Cutout, Emissive }

    public enum ToolKind : byte { None, Pickaxe, Shovel, Axe, Sword }

    public enum Face : byte { Down = 0, Up = 1, North = 2, South = 3, West = 4, East = 5 }

    public class BlockDef
    {
        public byte Id;
        public string Key;          // stable key used in saves + configs
        public string Name;         // display name
        public string TileTop, TileBottom, TileSide, TileFront; // atlas tiles (front used by directional blocks)
        public BlockShape Shape = BlockShape.Cube;
        public RenderKind Render = RenderKind.Opaque;
        public float HandTime = 1f;     // seconds to break bare-handed (derived from Hardness)
        public float PickTime = 1f;     // seconds with the best tool (derived)
        public float Hardness = 1f;     // Minecraft hardness
        public ToolKind Tool;           // tool that speeds it up
        public int HarvestTier;         // 0 = drops by hand, 1 = stone tool+, 2 = iron+, 3 = diamond
        public bool Unbreakable;
        public bool Gravity;            // falls like sand
        public bool Directional;        // uses Facing (pistons, levers, torches...)
        public bool FacingIncludesVertical = true;
        public float LightIntensity;    // lumens; 0 = no light
        public float LightRange;
        public Color LightColor = new Color(1f, 0.78f, 0.45f);
        public bool Pushable = true;
        public bool Flammable;          // fire spreads to it and burns it away (wood, leaves, wool...)
        public bool ExplosionProof;
        public string Sound = "stone";  // sound family: stone, wood, grass, gravel, sand, glass, wool, metal
        public int ShopPrice = -1;      // -1 => not sold in the store
        public int ShopStack = 32;      // amount you get per purchase
        public string Description = "";
        public string IconName;         // icons.<IconName>.png
        public bool Solid => Shape == BlockShape.Cube || Shape == BlockShape.PistonHead;
        /// <summary>Players and things bump into it (a full block, or a thin one like a pane); Solid is "a full block".</summary>
        public bool Collides => Solid || Shape == BlockShape.Pane;
        /// <summary>Players and things bump into it (a full block, or a thin one like a door); Solid is "a full block".</summary>
        public bool Collides => Solid || Shape == BlockShape.Door;
        /// <summary>Players and things bump into it (a full block, or stairs); Solid is "a full block".</summary>
        public bool Collides => Solid || Shape == BlockShape.Stairs;
        public bool IsRedstoneComponent;
        public BlockDef DropAs;         // what it drops when broken, if not itself (stone -> cobblestone, like Minecraft)
        public int ScrapValueMin, ScrapValueMax; // what the scrap item mined from it is worth (raw iron may be worth 0)
        /// <summary>An ore: mining it drops a scrap item (ScrapName), not the block.</summary>
        public bool DropsScrap => ScrapName != null;
        public string ScrapName;
        public string DropKey;          // what breaking it drops (null = itself), e.g. grass -> dirt
        public int DropMin = 1, DropMax = 1; // how many of DropKey (redstone ore: 4-5 dust)
    }

    public static class Blocks
    {
        public static readonly List<BlockDef> All = new List<BlockDef>();
        static readonly Dictionary<string, BlockDef> byKey = new Dictionary<string, BlockDef>();
        static BlockDef[] byId = new BlockDef[256];

        public static BlockDef Get(byte id) => byId[id];
        public static BlockDef Get(string key) => byKey.TryGetValue(key, out var d) ? d : null;

        // ids are stable: never reorder, only append (saves store ids)
        public static BlockDef Grass, Dirt, Stone, Cobblestone, Planks, Log, Glass, Sand, Gravel, Bricks, TNT,
            Piston, StickyPiston, PistonHead, RedstoneBlock, RedstoneLamp, Glowstone, Obsidian, DiamondOre, GoldOre,
            IronOre, CoalOre, EmeraldOre, DiamondBlock, GoldBlock, IronBlock, Leaves, WoolWhite, WoolRed, WoolBlue,
            WoolYellow, NoteBlock, JackOLantern, Ice, Slime, Bookshelf, StoneBricks, DarkPlanks, Torch, RedstoneTorch,
            Lever, Button, PressurePlate, RedstoneDust, Bedrock, Snow, Furnace, CraftingTable, Chest, Observer, Fire, CoalBlock,
            RedstoneOre, Diorite, Lava;
            GlassPane;
            OakDoor;
            Ladder;
            OakStairs, CobblestoneStairs, StoneBrickStairs;

        /// <summary>BlockData.State flag for natural ground generated by digging (not an obstacle, not ship-attachable).</summary>
        public const byte NaturalGround = 0x80;

        static BlockDef Add(byte id, string key, string name, string tile, System.Action<BlockDef> cfg = null)
        {
            var d = new BlockDef { Id = id, Key = key, Name = name, TileTop = tile, TileBottom = tile, TileSide = tile, TileFront = tile, IconName = key };
            cfg?.Invoke(d);
            All.Add(d);
            byKey[key] = d;
            byId[id] = d;
            return d;
        }

        /// <summary>Minecraft hardness, correct tool and required tool tier for every block.</summary>
        static void ApplyHardness()
        {
            var P = ToolKind.Pickaxe; var Sh = ToolKind.Shovel; var Ax = ToolKind.Axe; var N = ToolKind.None;
            var table = new Dictionary<string, (float h, ToolKind t, int tier)>
            {
                ["grass"] = (0.6f, Sh, 0), ["dirt"] = (0.5f, Sh, 0), ["sand"] = (0.5f, Sh, 0), ["gravel"] = (0.6f, Sh, 0), ["snow_block"] = (0.2f, Sh, 0),
                ["stone"] = (1.5f, P, 1), ["diorite"] = (1.5f, P, 1), ["cobblestone"] = (2f, P, 1), ["bricks"] = (2f, P, 1), ["stone_bricks"] = (1.5f, P, 1),
                ["obsidian"] = (50f, P, 4), ["coal_ore"] = (3f, P, 1), ["redstone_ore"] = (3f, P, 3), ["iron_ore"] = (3f, P, 2), ["gold_ore"] = (3f, P, 3),
                ["diamond_ore"] = (3f, P, 3), ["emerald_ore"] = (3f, P, 3), ["iron_block"] = (5f, P, 2), ["gold_block"] = (3f, P, 3),
                ["diamond_block"] = (5f, P, 3), ["coal_block"] = (5f, P, 1), ["redstone_block"] = (5f, P, 1), ["furnace"] = (3.5f, P, 1), ["ice"] = (0.5f, P, 0),
                ["piston"] = (0.5f, P, 0), ["sticky_piston"] = (0.5f, P, 0), ["piston_head"] = (0.5f, P, 0), ["button"] = (0.5f, P, 0),
                ["pressure_plate"] = (0.5f, P, 0),
                ["oak_planks"] = (2f, Ax, 0), ["oak_door"] = (3f, Ax, 0), ["dark_planks"] = (2f, Ax, 0), ["oak_log"] = (2f, Ax, 0), ["bookshelf"] = (1.5f, Ax, 0),
                ["oak_planks"] = (2f, Ax, 0), ["ladder"] = (0.4f, Ax, 0), ["dark_planks"] = (2f, Ax, 0), ["oak_log"] = (2f, Ax, 0), ["bookshelf"] = (1.5f, Ax, 0),
                ["oak_planks"] = (2f, Ax, 0), ["oak_stairs"] = (2f, Ax, 0), ["cobblestone_stairs"] = (2f, P, 1), ["stone_brick_stairs"] = (1.5f, P, 1), ["dark_planks"] = (2f, Ax, 0), ["oak_log"] = (2f, Ax, 0), ["bookshelf"] = (1.5f, Ax, 0),
                ["crafting_table"] = (2.5f, Ax, 0), ["chest"] = (2.5f, Ax, 0), ["observer"] = (3f, P, 1), ["note_block"] = (0.8f, Ax, 0), ["jack_o_lantern"] = (1f, Ax, 0),
                ["glass"] = (0.3f, N, 0), ["glass_pane"] = (0.3f, N, 0), ["redstone_lamp"] = (0.3f, N, 0), ["glowstone"] = (0.3f, N, 0), ["leaves"] = (0.2f, N, 0),
                ["wool_white"] = (0.8f, N, 0), ["wool_red"] = (0.8f, N, 0), ["wool_blue"] = (0.8f, N, 0), ["wool_yellow"] = (0.8f, N, 0),
                ["tnt"] = (0f, N, 0), ["fire"] = (0f, N, 0), ["slime"] = (0f, N, 0), ["torch"] = (0f, N, 0), ["redstone_torch"] = (0f, N, 0),
                ["lever"] = (0.5f, N, 0), ["lava"] = (-1f, N, 99), ["redstone_dust"] = (0f, N, 0), ["bedrock"] = (-1f, N, 99),
            };
            foreach (var d in All)
            {
                if (!table.TryGetValue(d.Key, out var e)) e = (1f, N, 0);
                d.Hardness = e.h; d.Tool = e.t; d.HarvestTier = e.tier;
                if (e.h < 0) { d.Unbreakable = true; continue; }
                d.HandTime = BreakTime(d, ToolKind.None, 0, 1f);
                d.PickTime = BreakTime(d, d.Tool, 4, 8f);
            }
        }

        /// <summary>Minecraft break time: hardness x 1.5 (or x5 when the block can't be harvested), divided by tool speed.</summary>
        public static float BreakTime(BlockDef d, ToolKind tool, int tier, float speed)
        {
            if (d.Unbreakable) return float.PositiveInfinity;
            bool right = tool != ToolKind.None && tool == d.Tool;
            bool harvest = CanHarvest(d, tool, tier);
            float t = d.Hardness * (harvest ? 1.5f : 5f) / (right ? speed : 1f);
            return Mathf.Max(0.05f, t);
        }

        public static bool CanHarvest(BlockDef d, ToolKind tool, int tier) => d.HarvestTier == 0 || (tool == d.Tool && tier >= d.HarvestTier);

        static void Stoneish(BlockDef d, float hand, float pick) { d.HandTime = hand; d.PickTime = pick; d.Sound = "stone"; }

        public static void Init()
        {
            if (All.Count > 0) return;
            Grass = Add(1, "grass", "Grass Block", "grass_side", d => { d.TileTop = "grass_top"; d.TileBottom = "dirt"; d.HandTime = 0.9f; d.PickTime = 0.6f; d.Sound = "grass"; d.ShopPrice = 6; d.ShopStack = 32; d.Description = "A cozy chunk of the overworld. Smells like home."; });
            Dirt = Add(2, "dirt", "Dirt", "dirt", d => { d.HandTime = 0.75f; d.PickTime = 0.5f; d.Sound = "gravel"; d.ShopPrice = 4; d.ShopStack = 32; d.Description = "Cheap, quick to place, quick to break. Perfect for emergency walls."; });
            Stone = Add(3, "stone", "Stone", "stone", d => { Stoneish(d, 7.5f, 0.6f); d.ShopPrice = 10; d.ShopStack = 32; d.Description = "Solid stone. Monsters hate it."; });
            Cobblestone = Add(4, "cobblestone", "Cobblestone", "cobblestone", d => { Stoneish(d, 9f, 0.75f); d.ShopPrice = 8; d.ShopStack = 32; d.Description = "Sturdy and classic. Takes forever to punch through without a pickaxe."; });
            Stone.DropAs = Cobblestone; // mined stone gives cobblestone; natural ground is stone
            Grass.DropAs = Dirt;
            Planks = Add(5, "oak_planks", "Oak Planks", "oak_planks", d => { d.HandTime = 3f; d.PickTime = 1.4f; d.Sound = "wood"; d.ShopPrice = 6; d.ShopStack = 32; d.Description = "Warm wooden planks for cozy bases."; });
            Log = Add(6, "oak_log", "Oak Log", "log_side", d => { d.TileTop = "log_top"; d.TileBottom = "log_top"; d.HandTime = 3f; d.PickTime = 1.4f; d.Sound = "wood"; d.Directional = true; d.ShopPrice = 6; d.ShopStack = 16; d.Description = "A sturdy log. Place it sideways by looking sideways."; });
            Glass = Add(7, "glass", "Glass", "glass", d => { d.HandTime = 0.45f; d.PickTime = 0.3f; d.Sound = "glass"; d.Render = RenderKind.Cutout; d.ShopPrice = 10; d.ShopStack = 16; d.Description = "See the monsters coming. Shatters easily."; });
            Sand = Add(8, "sand", "Sand", "sand", d => { d.HandTime = 0.75f; d.PickTime = 0.5f; d.Sound = "sand"; d.Gravity = true; d.ShopPrice = 5; d.ShopStack = 32; d.Description = "Falls when nothing holds it up. Great for sealing holes from above."; });
            Gravel = Add(9, "gravel", "Gravel", "gravel", d => { d.HandTime = 0.9f; d.PickTime = 0.6f; d.Sound = "gravel"; d.Gravity = true; d.ShopPrice = 5; d.ShopStack = 32; d.Description = "Crunchy and gravity-affected."; });
            Bricks = Add(10, "bricks", "Bricks", "bricks", d => { Stoneish(d, 10f, 1f); d.ShopPrice = 14; d.ShopStack = 32; d.Description = "Fancy fired-clay bricks."; });
            TNT = Add(11, "tnt", "TNT", "tnt_side", d => { d.TileTop = "tnt_top"; d.TileBottom = "tnt_bottom"; d.HandTime = 0.05f; d.PickTime = 0.05f; d.Sound = "grass"; d.ShopPrice = 40; d.ShopStack = 4; d.IsRedstoneComponent = true; d.Description = "Ignite with Flint and Steel or redstone. Run."; });
            Piston = Add(12, "piston", "Piston", "piston_side", d => { d.TileTop = "piston_top"; d.TileFront = "piston_top"; d.TileBottom = "piston_bottom"; d.Directional = true; d.HandTime = 1.5f; d.PickTime = 0.6f; d.IsRedstoneComponent = true; d.ShopPrice = 25; d.ShopStack = 4; d.Description = "Pushes up to 12 blocks when powered by redstone."; });
            StickyPiston = Add(13, "sticky_piston", "Sticky Piston", "piston_side", d => { d.TileTop = "sticky_top"; d.TileFront = "sticky_top"; d.TileBottom = "piston_bottom"; d.Directional = true; d.HandTime = 1.5f; d.PickTime = 0.6f; d.IsRedstoneComponent = true; d.ShopPrice = 35; d.ShopStack = 4; d.Description = "Like a piston, but pulls the block back. Secret doors!"; });
            PistonHead = Add(14, "piston_head", "Piston Head", "piston_side", d => { d.TileTop = "piston_top"; d.TileFront = "piston_top"; d.TileBottom = "piston_top"; d.Shape = BlockShape.PistonHead; d.Directional = true; d.HandTime = 1.5f; d.PickTime = 0.6f; d.Pushable = false; });
            RedstoneBlock = Add(15, "redstone_block", "Block of Redstone", "redstone_block", d => { Stoneish(d, 5f, 0.6f); d.Sound = "metal"; d.IsRedstoneComponent = true; d.ShopPrice = 20; d.ShopStack = 4; d.Description = "Always-on redstone power source."; });
            RedstoneLamp = Add(16, "redstone_lamp", "Redstone Lamp", "lamp_off", d => { d.HandTime = 0.5f; d.PickTime = 0.3f; d.Sound = "glass"; d.IsRedstoneComponent = true; d.LightIntensity = 0f; d.LightRange = 14f; d.LightColor = new Color(1f, 0.82f, 0.55f); d.ShopPrice = 18; d.ShopStack = 4; d.Description = "Lights up brightly when powered."; });
            Glowstone = Add(17, "glowstone", "Glowstone", "glowstone", d => { d.HandTime = 0.5f; d.PickTime = 0.3f; d.Sound = "glass"; d.Render = RenderKind.Emissive; d.LightIntensity = 4000f; d.LightRange = 16f; d.LightColor = new Color(1f, 0.85f, 0.55f); d.ShopPrice = 25; d.ShopStack = 4; d.Description = "A bright, glowing block. Lights up a whole room."; });
            Obsidian = Add(18, "obsidian", "Obsidian", "obsidian", d => { d.HandTime = 120f; d.PickTime = 9f; d.Sound = "stone"; d.ExplosionProof = true; d.Pushable = false; d.ShopPrice = 45; d.ShopStack = 8; d.Description = "Nearly indestructible and blast-proof. Pistons can't move it."; });
            DiamondOre = Add(19, "diamond_ore", "Diamond Ore", "diamond_ore", d => { Stoneish(d, 20f, 1.4f); d.ScrapName = "Diamond"; d.ScrapValueMin = 70; d.ScrapValueMax = 120; });
            GoldOre = Add(20, "gold_ore", "Gold Ore", "gold_ore", d => { Stoneish(d, 18f, 1.2f); d.ScrapName = "Raw Gold"; d.ScrapValueMin = 35; d.ScrapValueMax = 60; });
            IronOre = Add(21, "iron_ore", "Iron Ore", "iron_ore", d => { Stoneish(d, 16f, 1.1f); d.ScrapName = "Raw Iron"; d.ScrapValueMin = 10; d.ScrapValueMax = 10; });
            CoalOre = Add(22, "coal_ore", "Coal Ore", "coal_ore", d => { Stoneish(d, 14f, 1f); d.DropKey = "coal"; });
            EmeraldOre = Add(23, "emerald_ore", "Emerald Ore", "emerald_ore", d => { Stoneish(d, 20f, 1.4f); d.ScrapName = "Emerald"; d.ScrapValueMin = 90; d.ScrapValueMax = 150; });
            DiamondBlock = Add(24, "diamond_block", "Block of Diamond", "diamond_block", d => { Stoneish(d, 25f, 2f); d.Sound = "metal"; d.ShopPrice = 750; d.ShopStack = 1; d.Description = "Nine diamonds, pressed together. Craft it back into diamonds for diamond tools."; });
            GoldBlock = Add(25, "gold_block", "Block of Gold", "gold_block", d => { Stoneish(d, 15f, 1.5f); d.Sound = "metal"; d.ShopPrice = 150; d.ShopStack = 1; d.Description = "Shiny. The Company would be proud."; });
            IronBlock = Add(26, "iron_block", "Block of Iron", "iron_block", d => { Stoneish(d, 15f, 1.5f); d.Sound = "metal"; d.ShopPrice = 400; d.ShopStack = 1; d.Description = "Nine iron ingots. Craft it back into ingots for iron tools, or build with it."; });
            Leaves = Add(27, "leaves", "Leaves", "leaves", d => { d.HandTime = 0.3f; d.PickTime = 0.3f; d.Sound = "grass"; d.Render = RenderKind.Cutout; d.ShopPrice = 4; d.ShopStack = 32; d.Description = "Leafy camouflage."; });
            WoolWhite = Add(28, "wool_white", "White Wool", "wool_white", d => { d.HandTime = 1.2f; d.PickTime = 1f; d.Sound = "wool"; d.ShopPrice = 6; d.ShopStack = 16; d.Description = "Soft and quiet. Muffles footsteps (not really)."; });
            WoolRed = Add(29, "wool_red", "Red Wool", "wool_red", d => { d.HandTime = 1.2f; d.PickTime = 1f; d.Sound = "wool"; d.ShopPrice = 6; d.ShopStack = 16; d.Description = "Red wool. Great for marking danger."; });
            WoolBlue = Add(30, "wool_blue", "Blue Wool", "wool_blue", d => { d.HandTime = 1.2f; d.PickTime = 1f; d.Sound = "wool"; d.ShopPrice = 6; d.ShopStack = 16; d.Description = "Blue wool."; });
            WoolYellow = Add(31, "wool_yellow", "Yellow Wool", "wool_yellow", d => { d.HandTime = 1.2f; d.PickTime = 1f; d.Sound = "wool"; d.ShopPrice = 6; d.ShopStack = 16; d.Description = "Yellow wool. Very visible on the radar camera (not really)."; });
            NoteBlock = Add(32, "note_block", "Note Block", "note_block", d => { d.HandTime = 1.2f; d.PickTime = 0.8f; d.Sound = "wood"; d.IsRedstoneComponent = true; d.ShopPrice = 12; d.ShopStack = 4; d.Description = "Plays a note when powered or punched. [E] to tune."; });
            JackOLantern = Add(33, "jack_o_lantern", "Jack o'Lantern", "pumpkin_side", d => { d.TileTop = "pumpkin_top"; d.TileBottom = "pumpkin_top"; d.TileFront = "jack_face"; d.Directional = true; d.FacingIncludesVertical = false; d.HandTime = 1.2f; d.PickTime = 0.8f; d.Sound = "wood"; d.LightIntensity = 3200f; d.LightRange = 15f; d.LightColor = new Color(1f, 0.7f, 0.3f); d.ShopPrice = 15; d.ShopStack = 4; d.Description = "A spooky light source. Faces you when placed."; });
            Ice = Add(34, "ice", "Ice", "ice", d => { d.HandTime = 0.6f; d.PickTime = 0.4f; d.Sound = "glass"; d.ShopPrice = 8; d.ShopStack = 16; d.Description = "Slippery-looking frozen water."; });
            Slime = Add(35, "slime", "Slime Block", "slime", d => { d.HandTime = 0.1f; d.PickTime = 0.1f; d.Sound = "slime"; d.ShopPrice = 20; d.ShopStack = 8; d.Description = "Bouncy! Land on it to launch back up. No fall damage."; });
            Bookshelf = Add(36, "bookshelf", "Bookshelf", "bookshelf", d => { d.TileTop = "oak_planks"; d.TileBottom = "oak_planks"; d.HandTime = 2f; d.PickTime = 1f; d.Sound = "wood"; d.ShopPrice = 12; d.ShopStack = 8; d.Description = "Full of riveting quarterly reports."; });
            StoneBricks = Add(37, "stone_bricks", "Stone Bricks", "stone_bricks", d => { Stoneish(d, 9f, 0.75f); d.ShopPrice = 10; d.ShopStack = 32; d.Description = "Neat masonry for proper fortifications."; });
            DarkPlanks = Add(38, "dark_planks", "Dark Oak Planks", "oak_planks_dark", d => { d.HandTime = 3f; d.PickTime = 1.4f; d.Sound = "wood"; d.ShopPrice = 6; d.ShopStack = 32; d.Description = "Moody dark wood planks."; });
            Torch = Add(39, "torch", "Torch", "item_torch", d => { d.Shape = BlockShape.Torch; d.Directional = true; d.HandTime = 0.05f; d.PickTime = 0.05f; d.Sound = "wood"; d.LightIntensity = 2400f; d.LightRange = 13f; d.LightColor = new Color(1f, 0.72f, 0.38f); d.Render = RenderKind.Cutout; d.ShopPrice = 10; d.ShopStack = 16; d.Description = "Light up the darkness. Place on floors or walls."; });
            RedstoneTorch = Add(40, "redstone_torch", "Redstone Torch", "item_torch", d => { d.Shape = BlockShape.Torch; d.Directional = true; d.HandTime = 0.05f; d.PickTime = 0.05f; d.Sound = "wood"; d.LightIntensity = 500f; d.LightRange = 6f; d.LightColor = new Color(1f, 0.15f, 0.08f); d.IsRedstoneComponent = true; d.Render = RenderKind.Cutout; d.ShopPrice = 10; d.ShopStack = 8; d.Description = "Powers things around it. Turns OFF when the block it's attached to is powered."; });
            Lever = Add(41, "lever", "Lever", "cobblestone", d => { d.Shape = BlockShape.Lever; d.Directional = true; d.HandTime = 0.3f; d.PickTime = 0.2f; d.Sound = "wood"; d.IsRedstoneComponent = true; d.ShopPrice = 8; d.ShopStack = 4; d.Description = "Flip with [E] to send redstone power."; });
            Button = Add(42, "button", "Stone Button", "stone", d => { d.Shape = BlockShape.Button; d.Directional = true; d.HandTime = 0.3f; d.PickTime = 0.2f; d.Sound = "stone"; d.IsRedstoneComponent = true; d.ShopPrice = 6; d.ShopStack = 4; d.Description = "Press with [E] for a one-second pulse of power."; });
            PressurePlate = Add(43, "pressure_plate", "Pressure Plate", "stone", d => { d.Shape = BlockShape.Plate; d.HandTime = 0.3f; d.PickTime = 0.2f; d.Sound = "stone"; d.IsRedstoneComponent = true; d.ShopPrice = 10; d.ShopStack = 4; d.Description = "Powers when a player or monster stands on it. Trap time."; });
            RedstoneDust = Add(44, "redstone_dust", "Redstone Dust", "dust_off", d => { d.Shape = BlockShape.Dust; d.HandTime = 0.05f; d.PickTime = 0.05f; d.Sound = "stone"; d.IsRedstoneComponent = true; d.Render = RenderKind.Cutout; d.ShopPrice = 10; d.ShopStack = 32; d.Description = "Carries redstone power up to 15 blocks."; });

            Bedrock = Add(45, "bedrock", "Bedrock", "bedrock", d => { d.Unbreakable = true; d.ExplosionProof = true; d.Pushable = false; d.Sound = "stone"; d.HandTime = 999f; d.PickTime = 999f; });
            Snow = Add(46, "snow_block", "Snow Block", "snow", d => { d.HandTime = 0.6f; d.PickTime = 0.4f; d.Sound = "snow"; d.ShopPrice = 4; d.ShopStack = 32; d.Description = "Packed snow. Brr."; });
            Furnace = Add(47, "furnace", "Furnace", "furnace_side", d => { d.TileTop = "furnace_top"; d.TileBottom = "furnace_top"; d.TileFront = "furnace_front"; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "stone"; d.LightIntensity = 0f; d.LightRange = 9f; d.LightColor = new Color(1f, 0.6f, 0.3f); d.ShopPrice = 25; d.ShopStack = 1; d.Description = "Smelts raw iron and gold into ingots. [E] with ore/fuel in hand to load it, [E] empty-handed to take the output."; });
            CraftingTable = Add(48, "crafting_table", "Crafting Table", "crafting_table_side", d => { d.TileTop = "crafting_table_top"; d.TileBottom = "oak_planks"; d.TileFront = "crafting_table_front"; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.ShopPrice = 15; d.ShopStack = 1; d.Description = "[E] to open the crafting menu: tools, ingots, torches and more."; });
            Chest = Add(49, "chest", "Chest", "chest_side", d => { d.TileTop = "chest_top"; d.TileBottom = "chest_top"; d.TileFront = "chest_front"; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.Pushable = false; d.ShopPrice = 15; d.ShopStack = 1; d.Description = "Stores 27 stacks of blocks, tools and materials. [E] to open. Chests in the ship keep their contents between days."; });
            Observer = Add(50, "observer", "Observer", "observer_side", d => { d.TileTop = "observer_front"; d.TileFront = "observer_front"; d.TileBottom = "observer_back"; d.Directional = true; d.IsRedstoneComponent = true; d.ShopPrice = 30; d.ShopStack = 4; d.Description = "Watches the block its face looks at. When that block changes, it sends a short redstone pulse out of its back."; });
            Fire = Add(51, "fire", "Fire", "fire_0", d => { d.Shape = BlockShape.Fire; d.Render = RenderKind.Cutout; d.HandTime = 0f; d.PickTime = 0f; d.Sound = "wool"; d.LightIntensity = 1600f; d.LightRange = 11f; d.LightColor = new Color(1f, 0.62f, 0.28f); d.Pushable = false; d.Description = "Burns. Lit with flint and steel."; });
            // redstone ore (#47): deep down; an iron pickaxe gets 4-5 redstone dust out of it
            RedstoneOre = Add(53, "redstone_ore", "Redstone Ore", "redstone_ore", d => { Stoneish(d, 15f, 1.1f); d.DropKey = "redstone_dust"; d.DropMin = 4; d.DropMax = 5; d.Description = "Found deep underground. Mine it with an iron pickaxe for redstone dust."; });
            // diorite (#50)
            Diorite = Add(54, "diorite", "Diorite", "diorite", d => { Stoneish(d, 7.5f, 0.6f); d.ShopPrice = 10; d.ShopStack = 32; d.Description = "Speckled white stone. Builds like stone, looks like a choice."; });
            // lava pockets deep down (#32): digging straight down is a risk
            Lava = Add(61, "lava", "Lava", "lava", d => { d.Shape = BlockShape.Lava; d.Render = RenderKind.Emissive; d.Sound = "fire"; d.LightIntensity = 2400f; d.LightRange = 10f; d.LightColor = new Color(1f, 0.45f, 0.1f); d.Description = "Don't."; });
            // glass panes (#49): thin, joining up with the blocks and panes next to them
            GlassPane = Add(55, "glass_pane", "Glass Pane", "glass", d => { d.Shape = BlockShape.Pane; d.HandTime = 0.45f; d.PickTime = 0.3f; d.Sound = "glass"; d.Render = RenderKind.Cutout; d.Directional = true; d.FacingIncludesVertical = false; d.ShopPrice = 10; d.ShopStack = 16; d.Description = "A thin sheet of glass: windows that join up with the walls and panes around them."; });
            // doors (#54)
            OakDoor = Add(56, "oak_door", "Oak Door", "oak_door_bottom", d => { d.Shape = BlockShape.Door; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.Render = RenderKind.Cutout; d.ShopPrice = 20; d.ShopStack = 1; d.Description = "Two blocks tall. [E] to open and close it. Monsters can't walk through a closed one."; });
            // ladders (#30): on a wall, climbed like the game's own
            Ladder = Add(57, "ladder", "Ladder", "ladder", d => { d.Shape = BlockShape.Ladder; d.Sound = "wood"; d.Render = RenderKind.Cutout; d.ShopPrice = 10; d.ShopStack = 8; d.Description = "Put it on a wall, [E] to climb it (with a hand free). Stack them for a taller climb."; });
            // stairs (#50): walk straight up them
            OakStairs = Add(58, "oak_stairs", "Oak Stairs", "oak_planks", d => { d.Shape = BlockShape.Stairs; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "wood"; d.ShopPrice = 10; d.ShopStack = 8; d.Description = "Walk straight up them, no jumping."; });
            CobblestoneStairs = Add(59, "cobblestone_stairs", "Cobblestone Stairs", "cobblestone", d => { d.Shape = BlockShape.Stairs; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "stone"; d.ShopPrice = 10; d.ShopStack = 8; d.Description = "Walk straight up them, no jumping."; });
            StoneBrickStairs = Add(60, "stone_brick_stairs", "Stone Brick Stairs", "stone_bricks", d => { d.Shape = BlockShape.Stairs; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "stone"; d.ShopPrice = 12; d.ShopStack = 8; d.Description = "Walk straight up them, no jumping."; });
            CoalBlock = Add(52, "coal_block", "Block of Coal", "coal_block", d => { Stoneish(d, 25f, 1.9f); d.ShopPrice = 150; d.ShopStack = 1; d.Description = "Nine coal. Craft it back into coal, or burn the whole block in a furnace."; });
            foreach (var f in new[] { CoalBlock, Planks, DarkPlanks, Log, Leaves, WoolWhite, WoolRed, WoolBlue, WoolYellow, Bookshelf, CraftingTable, NoteBlock })
                f.Flammable = true;
            Grass.DropKey = "dirt";
            Stone.DropKey = "cobblestone";

            ApplyHardness();

            Torch.IconName = "torch";
            RedstoneTorch.IconName = "torch";
            Lever.IconName = "lever";
            Button.IconName = "button";
            PressurePlate.IconName = "pressure_plate";
            RedstoneDust.IconName = "redstone_dust";
            RedstoneLamp.IconName = "redstone_lamp";
            Grass.IconName = "grass";
            DarkPlanks.IconName = "dark_planks";
        }
    }

    public static class Faces
    {
        public static readonly Vector3Int[] Dir =
        {
            new Vector3Int(0, -1, 0), new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            new Vector3Int(-1, 0, 0), new Vector3Int(1, 0, 0),
        };

        public static byte Opposite(byte f) => (byte)(f ^ 1);

        public static byte FromVector(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ay >= ax && ay >= az) return (byte)(v.y < 0 ? Face.Down : Face.Up);
            if (az >= ax) return (byte)(v.z > 0 ? Face.North : Face.South);
            return (byte)(v.x < 0 ? Face.West : Face.East);
        }

        public static byte FromVectorHorizontal(Vector3 v)
        {
            v.y = 0;
            if (v.sqrMagnitude < 1e-6f) return (byte)Face.North;
            return FromVector(v);
        }

        public static Quaternion Rotation(byte f)
        {
            // rotation that maps local +Z ("front") to the facing direction
            switch ((Face)f)
            {
                case Face.Down: return Quaternion.Euler(90, 0, 0);
                case Face.Up: return Quaternion.Euler(-90, 0, 0);
                case Face.North: return Quaternion.identity;
                case Face.South: return Quaternion.Euler(0, 180, 0);
                case Face.West: return Quaternion.Euler(0, -90, 0);
                default: return Quaternion.Euler(0, 90, 0);
            }
        }
    }
}
