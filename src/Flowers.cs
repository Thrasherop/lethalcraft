using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Dyes, flowers and grass (#225, the owner's). Minecraft's 16 dyes, crafted from what the mod has (Minecraft's
    /// recipes: an ink sac, lapis, bone meal, the flowers below, and mixing two dyes); green and brown (a smelted cactus,
    /// cocoa beans in Minecraft) come from the store's dye pack. Flowers and grass don't grow on their own (the owner's):
    /// bone meal on dirt or grass, a block or the moon's own ground, grows them round it (Minecraft's
    /// GrassBlock.performBonemeal: 128 tries, each a short walk over that ground; one in eight a flower, the rest short
    /// grass; short grass met grows tall now and then). The flowers: dandelion, poppy, blue orchid, allium, azure bluet,
    /// the four tulips, oxeye daisy, cornflower, lily of the valley. Short grass and tall grass drop wheat seeds now and
    /// then (one in eight); only shears take them. Wool takes any dye; so does a sign's text ([E] with it).
    /// </summary>
    public static class Dyes
    {
        public static readonly string[] Colors = { "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black" };
        public static string Key(string color) => color + "_dye";
        /// <summary>A dye's index (Minecraft's DyeColor order), or -1.</summary>
        public static int IndexOf(string key) { key = ItemData.TypeOf(key); return key != null && key.EndsWith("_dye") ? System.Array.IndexOf(Colors, key.Substring(0, key.Length - 4)) : -1; } // (#228: by type: a named dye too)
        public static readonly string[] Flowers = { "dandelion", "poppy", "blue_orchid", "allium", "azure_bluet", "red_tulip", "orange_tulip", "white_tulip", "pink_tulip", "oxeye_daisy", "cornflower", "lily_of_the_valley" };
        /// <summary>What each flower makes (Minecraft's recipes: one dye each).</summary>
        public static readonly Dictionary<string, string> FlowerDye = new Dictionary<string, string>
        {
            ["dandelion"] = "yellow", ["poppy"] = "red", ["blue_orchid"] = "light_blue", ["allium"] = "magenta", ["azure_bluet"] = "light_gray", ["red_tulip"] = "red",
            ["orange_tulip"] = "orange", ["white_tulip"] = "light_gray", ["pink_tulip"] = "pink", ["oxeye_daisy"] = "light_gray", ["cornflower"] = "blue", ["lily_of_the_valley"] = "white",
        };
        /// <summary>The mod's wool keys by colour (its own names: wool_&lt;colour&gt;).</summary>
        public static string Wool(string color) => "wool_" + color;
        public static int Grown; // (dev/tests)

        static BlockWorld W => BlockWorld.Instance;

        /// <summary>Dirt or grass there to grow on: a block, or the moon's own ground.</summary>
        static bool Growable(BlockKey below)
        {
            var w = W;
            var d = w.DefAt(below);
            if (d != null) return d == Blocks.Grass || d == Blocks.Dirt;
            return below.Frame == 0 && below.YOff == Ground.GridYOff && Ground.MaterialOf(below.Pos) is BlockDef m && (m == Blocks.Dirt || m == Blocks.Grass);
        }

        static bool Free(BlockKey k) => W.DefAt(k) == null && !(k.Frame == 0 && Ground.IsUndugGround(k)) && !ServerLogic.Obstructed(k, 0.6f);

        /// <summary>Host: bone meal on dirt or grass at k (a block or the moon's ground): Minecraft's GrassBlock.performBonemeal.
        /// True if it was such ground.</summary>
        public static bool ServerBoneMeal(BlockKey k)
        {
            var w = W;
            if (w == null || !Growable(k)) return false;
            var above = k.Offset((int)Face.Up);
            var ops = new List<Op>();
            var taken = new HashSet<BlockKey>();
            var r = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            for (int j = 0; j < 128; j++)
            {
                var pos = above; bool ok = true;
                for (int i = 0; i < j / 16; i++)
                {
                    pos = new BlockKey(pos.Frame, pos.YOff, pos.Pos + new Vector3Int(r.Next(3) - 1, (r.Next(3) - 1) * r.Next(3) / 2, r.Next(3) - 1));
                    if (!Growable(pos.Offset((int)Face.Down)) || w.IsSolidAt(pos)) { ok = false; break; }
                }
                if (!ok || taken.Contains(pos)) continue;
                var here = w.DefAt(pos);
                if (here == null && Free(pos) && Growable(pos.Offset((int)Face.Down)))
                {
                    var plant = r.Next(8) == 0 ? Blocks.Get(Flowers[r.Next(Flowers.Length)]) : Blocks.ShortGrass;
                    if (plant == null) continue;
                    ops.Add(Op.Set(pos, new BlockData(plant.Id, (byte)Face.Up, 0))); taken.Add(pos);
                }
                else if (here == Blocks.ShortGrass && r.Next(10) == 0) GrowTall(pos, ops, taken);
            }
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            BlockNet.ServerSound(w.WorldCenter(above), "bonemeal", 1f, 0.8f);
            Grown += ops.Count;
            return true;
        }

        /// <summary>Host: bone meal on short grass: tall grass, where there's room over it.</summary>
        public static bool ServerBoneMealPlant(BlockKey k)
        {
            var w = W;
            if (w?.DefAt(k) != Blocks.ShortGrass) return false;
            var ops = new List<Op>();
            GrowTall(k, ops, new HashSet<BlockKey>());
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            BlockNet.ServerSound(w.WorldCenter(k), "bonemeal", 1f, 0.8f);
            return true;
        }

        static void GrowTall(BlockKey k, List<Op> ops, HashSet<BlockKey> taken)
        {
            var up = k.Offset((int)Face.Up);
            if (!Free(up) || taken.Contains(up)) return;
            ops.Add(Op.Set(k, new BlockData(Blocks.TallGrass.Id, (byte)Face.Up, 0)));
            ops.Add(Op.Set(up, new BlockData(Blocks.TallGrass.Id, (byte)Face.Up, 1)));
            taken.Add(up);
        }

        /// <summary>Host: what grass drops when broken (only shears take it; otherwise wheat seeds one time in eight).
        /// True: handled.</summary>
        public static bool ServerGrassDrop(BlockData d, Vector3 at, GameNetcodeStuff.PlayerControllerB breaker)
        {
            if (d.Def != Blocks.ShortGrass && d.Def != Blocks.TallGrass) return false;
            if (d.Def == Blocks.TallGrass && (d.State & 1) != 0) return true; // (the top half drops nothing of its own)
            var tool = breaker != null ? breaker.currentlyHeldObjectServer as ToolItem : null;
            if (tool != null && tool.Kind == ToolKind.Shears) { ServerLogic.SpawnDrop(d.Def, at); return true; }
            if (Random.value < 0.125f && ModItems.ByKey.TryGetValue("wheat_seeds", out var seeds)) ModItems.ServerSpawnStack(seeds, 1, at);
            return true;
        }
    }

    public static partial class Blocks
    {
        public static BlockDef ShortGrass, TallGrass;

        static void AddPlants()
        {
            foreach (var f in Dyes.Flowers)
            {
                string name = string.Join(" ", f.Split('_').Select(w => w == "of" || w == "the" ? w : char.ToUpper(w[0]) + w.Substring(1)));
                AddW(f, name, f, 0f, ToolKind.None, x =>
                {
                    x.Shape = BlockShape.Plant; x.Render = RenderKind.Cutout; x.Sound = "grass";
                    x.Description = $"A flower: bone meal on dirt or grass grows them. Makes {Dyes.FlowerDye[f].Replace('_', ' ')} dye.";
                });
            }
            ShortGrass = AddW("short_grass", "Short Grass", "short_grass", 0f, ToolKind.Shears, x =>
            {
                x.Shape = BlockShape.Plant; x.Render = RenderKind.Cutout; x.Sound = "grass";
                x.Description = "Grass, grown by bone meal on dirt or grass. Only shears take it; otherwise wheat seeds now and then.";
            });
            TallGrass = AddW("tall_grass", "Tall Grass", "tall_grass_bottom", 0f, ToolKind.Shears, x =>
            {
                x.Shape = BlockShape.Plant; x.TileTop = "tall_grass_top"; x.Render = RenderKind.Cutout; x.Sound = "grass";
                x.Description = "Two blocks of grass: bone meal on short grass. Only shears take it.";
            });
            familyHardness["short_grass"] = (0f, ToolKind.Shears, 0); familyHardness["tall_grass"] = (0f, ToolKind.Shears, 0);
        }
    }
}
