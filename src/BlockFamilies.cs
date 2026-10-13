using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's block families (#154 stone, granite, diorite, andesite, stone bricks, cobblestone, bricks; #156 quartz;
    /// #157 smooth stone, sandstone, red sandstone; and the Nether's bricks and blackstone, fences for wood): each block
    /// with the polished, smooth, cut, chiseled, cracked and mossy kinds Minecraft has, and their stairs, slabs and walls.
    /// Ids from 118 up, in the order below (saves keep ids: add new ones only at the end).
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextFamilyId = 118;

        /// <summary>Minecraft hardness, tool and tier of the family blocks (ApplyHardness reads it after its own table).</summary>
        static readonly Dictionary<string, (float h, ToolKind t, int tier)> familyHardness = new Dictionary<string, (float, ToolKind, int)>();

        /// <summary>Tile name -> Minecraft texture (block/...), for the atlas.</summary>
        public static readonly Dictionary<string, string> FamilyTiles = new Dictionary<string, string>();

        /// <summary>Each shaped block made from a family block: (the block, the shaped one, its shape), for the recipes.</summary>
        public static readonly List<(string block, string shaped, BlockShape shape)> FamilyShapes = new List<(string, string, BlockShape)>();

        static BlockDef Fam(string key, string name, string side, float hardness, string desc, string top = null, string bottom = null,
            ToolKind tool = ToolKind.Pickaxe, int tier = 1, string sound = "stone", System.Action<BlockDef> cfg = null)
        {
            foreach (var t in new[] { side, top, bottom }) if (t != null) FamilyTiles[t] = t;
            var d = Add(nextFamilyId++, key, name, side, x =>
            {
                x.TileTop = top ?? side; x.TileBottom = bottom ?? top ?? side; x.Sound = sound; x.Description = desc;
                cfg?.Invoke(x);
            });
            familyHardness[key] = (hardness, tool, tier);
            return d;
        }

        /// <summary>The block's stairs, slab and wall (each where asked), keyed &lt;stem&gt;_stairs etc.</summary>
        static void Shapes(BlockDef b, string stem, string nameStem, bool stairs = true, bool slab = true, bool wall = false, bool fence = false)
        {
            var (h, tool, tier) = familyHardness.TryGetValue(b.Key, out var e) ? e : (2f, ToolKind.Pickaxe, 1);
            void One(string suffix, string nameSuffix, BlockShape shape, string desc)
            {
                string key = stem + "_" + suffix;
                if (Get(key) != null) return; // (cobblestone's and stone bricks' stairs and slabs were here first)
                Add(nextFamilyId++, key, nameStem + " " + nameSuffix, b.TileSide, x =>
                {
                    x.TileTop = b.TileTop; x.TileBottom = b.TileBottom; x.Shape = shape; x.Sound = b.Sound; x.Description = desc;
                    if (shape == BlockShape.Stairs) { x.Directional = true; x.FacingIncludesVertical = false; }
                });
                familyHardness[key] = (h, tool, tier);
                FamilyShapes.Add((b.Key, key, shape));
            }
            if (stairs) One("stairs", "Stairs", BlockShape.Stairs, "Walk straight up them, no jumping.");
            if (slab) One("slab", "Slab", BlockShape.Slab, "Half a block. Two in one space make a full block.");
            if (wall) One("wall", "Wall", BlockShape.Wall, "A wall a little over a block high: it joins the walls and blocks beside it.");
            if (fence) One("fence", "Fence", BlockShape.Fence, "Joins the fences and blocks beside it.");
        }

        static void AddFamilies()
        {
            // ---- stone (#154, #157)
            Shapes(Stone, "stone", "Stone", slab: false);
            var smoothStone = Fam("smooth_stone", "Smooth Stone", "smooth_stone", 2f, "Stone smelted smooth.");
            Shapes(smoothStone, "smooth_stone", "Smooth Stone", stairs: false);
            Shapes(Cobblestone, "cobblestone", "Cobblestone", wall: true);
            var mossyCobble = Fam("mossy_cobblestone", "Mossy Cobblestone", "mossy_cobblestone", 2f, "Cobblestone grown over with moss.", cfg: x => { x.ShopPrice = 12; x.ShopStack = 32; });
            Shapes(mossyCobble, "mossy_cobblestone", "Mossy Cobblestone", wall: true);
            Shapes(StoneBricks, "stone_brick", "Stone Brick", wall: true);
            var mossyBricks = Fam("mossy_stone_bricks", "Mossy Stone Bricks", "mossy_stone_bricks", 1.5f, "Stone bricks grown over with moss.", cfg: x => { x.ShopPrice = 14; x.ShopStack = 32; });
            Shapes(mossyBricks, "mossy_stone_brick", "Mossy Stone Brick", wall: true);
            Fam("cracked_stone_bricks", "Cracked Stone Bricks", "cracked_stone_bricks", 1.5f, "Stone bricks fired in a furnace till they crack.");
            Fam("chiseled_stone_bricks", "Chiseled Stone Bricks", "chiseled_stone_bricks", 1.5f, "Stone bricks with a carved ring.");

            // ---- granite, diorite, andesite: patches in the moons' stone (#154)
            var granite = Fam("granite", "Granite", "granite", 1.5f, "Pink speckled stone, in patches underground.");
            Shapes(granite, "granite", "Granite", wall: true);
            var polishedGranite = Fam("polished_granite", "Polished Granite", "polished_granite", 1.5f, "Granite polished smooth.");
            Shapes(polishedGranite, "polished_granite", "Polished Granite");
            Shapes(Diorite, "diorite", "Diorite", wall: true);
            var polishedDiorite = Fam("polished_diorite", "Polished Diorite", "polished_diorite", 1.5f, "Diorite polished smooth.");
            Shapes(polishedDiorite, "polished_diorite", "Polished Diorite");
            var andesite = Fam("andesite", "Andesite", "andesite", 1.5f, "Grey speckled stone, in patches underground.");
            Shapes(andesite, "andesite", "Andesite", wall: true);
            var polishedAndesite = Fam("polished_andesite", "Polished Andesite", "polished_andesite", 1.5f, "Andesite polished smooth.");
            Shapes(polishedAndesite, "polished_andesite", "Polished Andesite");

            // ---- bricks
            Shapes(Bricks, "brick", "Brick", wall: true);

            // ---- sandstone and red sandstone (#157)
            foreach (var (pre, Pre, sand) in new[] { ("", "", "sand"), ("red_", "Red ", "red_sand") })
            {
                if (sand == "red_sand")
                    Fam("red_sand", "Red Sand", "red_sand", 0.5f, "Sand the color of rust. Falls when nothing holds it up.", tool: ToolKind.Shovel, tier: 0, sound: "sand",
                        cfg: x => { x.Gravity = true; x.ShopPrice = 6; x.ShopStack = 32; });
                var ss = Fam(pre + "sandstone", Pre + "Sandstone", pre + "sandstone", 0.8f, $"Four {Pre.ToLower()}sand pressed into stone.", top: pre + "sandstone_top", bottom: pre + "sandstone_bottom");
                Shapes(ss, pre + "sandstone", Pre + "Sandstone", wall: true);
                var cut = Fam("cut_" + pre + "sandstone", "Cut " + Pre + "Sandstone", "cut_" + pre + "sandstone", 0.8f, "Sandstone cut into neat slabs.", top: pre + "sandstone_top");
                Shapes(cut, "cut_" + pre + "sandstone", "Cut " + Pre + "Sandstone", stairs: false);
                Fam("chiseled_" + pre + "sandstone", "Chiseled " + Pre + "Sandstone", "chiseled_" + pre + "sandstone", 0.8f, "Sandstone carved with a pattern.", top: pre + "sandstone_top");
                var smooth = Fam("smooth_" + pre + "sandstone", "Smooth " + Pre + "Sandstone", pre + "sandstone_top", 2f, "Sandstone smelted smooth.");
                Shapes(smooth, "smooth_" + pre + "sandstone", "Smooth " + Pre + "Sandstone");
            }

            // ---- quartz (#156)
            var quartz = Fam("quartz_block", "Block of Quartz", "quartz_block_side", 0.8f, "Four nether quartz.", top: "quartz_block_top", bottom: "quartz_block_bottom");
            Shapes(quartz, "quartz", "Quartz");
            Fam("chiseled_quartz_block", "Chiseled Quartz Block", "chiseled_quartz_block", 0.8f, "Quartz carved with a pattern.", top: "chiseled_quartz_block_top");
            Fam("quartz_pillar", "Quartz Pillar", "quartz_pillar", 0.8f, "A quartz column. Place it sideways by looking sideways.", top: "quartz_pillar_top", cfg: x => x.Directional = true);
            Fam("quartz_bricks", "Quartz Bricks", "quartz_bricks", 0.8f, "Quartz blocks cut into bricks.");
            var smoothQuartz = Fam("smooth_quartz", "Smooth Quartz Block", "quartz_block_bottom", 2f, "A block of quartz smelted smooth.");
            Shapes(smoothQuartz, "smooth_quartz", "Smooth Quartz");

            // ---- the Nether's bricks and blackstone
            Shapes(NetherBricks, "nether_brick", "Nether Brick", wall: true, fence: true);
            var redNether = Fam("red_nether_bricks", "Red Nether Bricks", "red_nether_bricks", 2f, "Nether bricks set with nether wart.");
            Shapes(redNether, "red_nether_brick", "Red Nether Brick", wall: true);
            Fam("cracked_nether_bricks", "Cracked Nether Bricks", "cracked_nether_bricks", 2f, "Nether bricks fired till they crack.");
            Fam("chiseled_nether_bricks", "Chiseled Nether Bricks", "chiseled_nether_bricks", 2f, "Nether bricks with a carved face.");
            Shapes(Blackstone, "blackstone", "Blackstone", wall: true);
            var polishedBlackstone = Fam("polished_blackstone", "Polished Blackstone", "polished_blackstone", 2f, "Blackstone polished smooth.");
            Shapes(polishedBlackstone, "polished_blackstone", "Polished Blackstone", wall: true);
            Shapes(PolishedBlackstoneBricks, "polished_blackstone_brick", "Polished Blackstone Brick", wall: true);
            Fam("chiseled_polished_blackstone", "Chiseled Polished Blackstone", "chiseled_polished_blackstone", 1.5f, "Polished blackstone carved with a piglin's face.");
            Fam("cracked_polished_blackstone_bricks", "Cracked Polished Blackstone Bricks", "cracked_polished_blackstone_bricks", 1.5f, "Blackstone bricks fired till they crack.");

            // ---- wood: fences, and the dark planks' stairs and slab
            Shapes(Planks, "oak", "Oak", stairs: false, slab: false, fence: true);
            Shapes(DarkPlanks, "dark_oak", "Dark Oak", fence: true);
            foreach (var (_, shaped, _) in FamilyShapes)
            {
                var d = Get(shaped);
                if (d.Sound == "wood") { d.Flammable = true; familyHardness[shaped] = (2f, ToolKind.Axe, 0); }
            }
        }
    }
}
