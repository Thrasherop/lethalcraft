using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244 part D) Minecraft's blocks the mod lacked: the lapis, emerald and netherite blocks; polished basalt; packed and
    /// blue ice; snow layers; coarse dirt and the dirt path; the lantern, soul lantern and soul torch; carpets, stained
    /// glass and panes, and beds in Minecraft's sixteen colours. Ids from 900, after every other block (saves keep ids: new
    /// ones only at the end).
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextDId = 900;
        public static BlockDef LapisBlock, EmeraldBlock, NetheriteBlock, PolishedBasalt, PackedIce, BlueIce, SnowLayer, CoarseDirt, DirtPath, Lantern, SoulLantern, SoulTorch;
        /// <summary>A colour's carpet, stained glass, pane and bed (Flowers.Colors' order; red's bed is the old red_bed).</summary>
        public static readonly Dictionary<string, BlockDef> Carpets = new Dictionary<string, BlockDef>(), StainedGlass = new Dictionary<string, BlockDef>(), StainedPanes = new Dictionary<string, BlockDef>(), Beds = new Dictionary<string, BlockDef>();

        /// <summary>A block (its tiles Minecraft's block/ textures of those names, unless `oldTiles`: the atlas has them).</summary>
        static BlockDef AddD(string key, string name, string tile, float hardness, ToolKind tool, int tier, System.Action<BlockDef> cfg, bool oldTiles = false)
        {
            if (!oldTiles) FamilyTiles[tile] = tile;
            var d = Add(nextDId++, key, name, tile, x => cfg?.Invoke(x));
            if (!oldTiles) foreach (var t in new[] { d.TileTop, d.TileBottom, d.TileFront }) if (t != null && t != tile) FamilyTiles[t] = t;
            familyHardness[key] = (hardness, tool, tier);
            return d;
        }

        /// <summary>A bed's colour ("red" for the old red bed).</summary>
        public static string BedColor(BlockDef d) => d == null || d.Shape != BlockShape.Bed ? null : d == RedBed ? "red" : d.Key.Substring(0, d.Key.Length - 4);

        static void AddThoroughD()
        {
            const ToolKind P = ToolKind.Pickaxe, Sh = ToolKind.Shovel, N = ToolKind.None, Ax = ToolKind.Axe;
            // storage blocks (#66)
            LapisBlock = AddD("lapis_block", "Block of Lapis Lazuli", "lapis_block", 3f, P, 2, d => { d.Description = "Nine lapis lazuli."; });
            EmeraldBlock = AddD("emerald_block", "Block of Emerald", "emerald_block", 5f, P, 3, d => { d.Sound = "metal"; d.Description = "Nine emeralds."; });
            NetheriteBlock = AddD("netherite_block", "Block of Netherite", "netherite_block", 50f, P, 4, d => { d.Sound = "metal"; d.ExplosionProof = true; d.Description = "Nine netherite ingots. Lava doesn't burn it."; });
            // (#70, #78) polished basalt: a pillar, as basalt is now
            PolishedBasalt = AddD("polished_basalt", "Polished Basalt", "polished_basalt_side", 1.25f, P, 1, d => { d.TileTop = d.TileBottom = "polished_basalt_top"; d.Directional = true; d.Description = "Basalt, polished: four from four."; });
            // (#72) ice
            PackedIce = AddD("packed_ice", "Packed Ice", "packed_ice", 0.5f, P, 0, d => { d.Sound = "glass"; d.DropsNothing = true; d.Description = "Nine ice: it doesn't melt. Broken, it's gone (Silk Touch keeps it)."; });
            BlueIce = AddD("blue_ice", "Blue Ice", "blue_ice", 2.8f, P, 0, d => { d.Sound = "glass"; d.DropsNothing = true; d.Description = "Nine packed ice."; });
            // (#72) snow layers: one to eight, stacked by placing more on them
            SnowLayer = AddD("snow", "Snow", "snow", 0.1f, Sh, 0, d => { d.Shape = BlockShape.Layer; d.Sound = "snow"; d.DropKey = "snowball"; d.Description = "A layer of snow: place more on it to stack it, eight make a block's worth. Dug, it gives snowballs."; }, oldTiles: true);
            // (#73) coarse dirt and the dirt path
            CoarseDirt = AddD("coarse_dirt", "Coarse Dirt", "coarse_dirt", 0.5f, Sh, 0, d => { d.Sound = "gravel"; d.Description = "Dirt and gravel: grass doesn't grow on it."; });
            FamilyTiles["dirt_path_top"] = "dirt_path_top";
            DirtPath = AddD("dirt_path", "Dirt Path", "dirt_path_side", 0.65f, Sh, 0, d => { d.TileTop = "dirt_path_top"; d.TileBottom = "dirt"; d.Shape = BlockShape.Path; d.Sound = "grass"; d.DropKey = "dirt"; d.Description = "A shovel's use button on grass makes it. Broken, it's dirt."; }, oldTiles: true);
            FamilyTiles["dirt_path_side"] = "dirt_path_side";
            // (#76) the lanterns and the soul torch
            Lantern = AddD("lantern", "Lantern", "lantern", 3.5f, P, 0, d => { d.Shape = BlockShape.Lantern; d.Render = RenderKind.Cutout; d.Sound = "lantern"; d.LightIntensity = 2400f; d.LightRange = 13f; d.LightColor = new Color(1f, 0.75f, 0.42f); d.Description = "A torch in iron: stands on a block or hangs under one."; });
            SoulLantern = AddD("soul_lantern", "Soul Lantern", "soul_lantern", 3.5f, P, 0, d => { d.Shape = BlockShape.Lantern; d.Render = RenderKind.Cutout; d.Sound = "lantern"; d.LightIntensity = 1500f; d.LightRange = 10f; d.LightColor = new Color(0.45f, 0.85f, 1f); d.Description = "A soul torch in iron: a dimmer, blue light."; });
            SoulTorch = AddD("soul_torch", "Soul Torch", "soul_torch", 0f, N, 0, d => { d.Shape = BlockShape.Torch; d.Directional = true; d.HandTime = 0.05f; d.PickTime = 0.05f; d.Sound = "wood"; d.Render = RenderKind.Cutout; d.LightIntensity = 1500f; d.LightRange = 10f; d.LightColor = new Color(0.45f, 0.85f, 1f); d.Description = "A torch lit on soul sand or soil: a dimmer, blue light."; });
            // (#71) Minecraft's sixteen colours: carpets, stained glass and panes, beds
            foreach (var c in Dyes.Colors)
            {
                string n = Title(c), wool = "wool_" + c;
                Carpets[c] = AddD(c + "_carpet", n + " Carpet", wool, 0.1f, N, 0, d => { d.Shape = BlockShape.Carpet; d.Sound = "wool"; d.Flammable = true; d.Description = "A thin layer of wool: two wool make three."; }, oldTiles: true);
                StainedGlass[c] = AddD(c + "_stained_glass", n + " Stained Glass", c + "_stained_glass", 0.3f, N, 0, d => { d.Render = RenderKind.Translucent; d.Sound = "glass"; d.DropsNothing = true; d.Description = "Glass with a colour: eight glass and a dye. Broken, it's gone."; });
                FamilyTiles[c + "_stained_glass_pane_top"] = c + "_stained_glass_pane_top";
                ItemTiles["item_" + c + "_stained_glass_pane"] = "block/" + c + "_stained_glass"; // (its icon: flat stained glass, Minecraft's)
                StainedPanes[c] = AddD(c + "_stained_glass_pane", n + " Stained Glass Pane", c + "_stained_glass", 0.3f, N, 0, d => { d.Shape = BlockShape.Pane; d.Render = RenderKind.Translucent; d.Directional = true; d.FacingIncludesVertical = false; d.Sound = "glass"; d.DropsNothing = true; d.Description = "A thin sheet of stained glass."; });
                if (c == "red") { Beds[c] = RedBed; continue; }
                Beds[c] = AddD(c + "_bed", n + " Bed", "oak_planks", 0.2f, N, 0, d => { d.Shape = BlockShape.Bed; d.Sound = "wool"; d.Flammable = true; d.Pushable = false; d.HandTime = 0.3f; d.PickTime = 0.3f; d.Description = "A bed. Sleeping doesn't set where you come back here. In the Nether, it explodes."; }, oldTiles: true);
            }
            // (#78) basalt and the bone block turn with their axis, as logs do (the stems: with their woods)
            Basalt.Directional = true; BoneBlock.Directional = true; PolishedBasalt.Directional = true;
            foreach (var p in new[] { Basalt, BoneBlock, PolishedBasalt, CrimsonStem, Get("warped_stem") }) if (p != null) { p.Directional = true; pillars.Add(p); }
        }

        static string Title(string c) => string.Join(" ", System.Array.ConvertAll(c.Split('_'), w => char.ToUpper(w[0]) + w.Substring(1)));
    }
}
