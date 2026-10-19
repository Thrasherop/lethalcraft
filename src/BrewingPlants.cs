using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244, B) What brewing needs that this world didn't have: melons (the glistering melon for Healing) and mushrooms
    /// (the brown one for the fermented spider eye), with Minecraft's uses for them: melon slices to eat, seeds to plant,
    /// mushroom and rabbit stew. Ids from 850 (the anvils have 800-802).
    /// </summary>
    public static partial class Blocks
    {
        public static BlockDef Melon, MelonStem, BrownMushroom, RedMushroom;

        static void AddBrewingPlants()
        {
            FamilyTiles["melon_side"] = "melon_side"; FamilyTiles["melon_top"] = "melon_top";
            FamilyTiles["brown_mushroom"] = "brown_mushroom"; FamilyTiles["red_mushroom"] = "red_mushroom";
            Melon = Add(850, "melon", "Melon", "melon_side", d =>
            {
                d.TileTop = "melon_top"; d.TileBottom = "melon_top"; d.Sound = "wood"; d.DropKey = "melon_slice"; d.DropMin = 3; d.DropMax = 7;
                d.Description = "A melon: broken, 3-7 slices to eat. Nine slices make one again. Grows from melon seeds on wet farmland.";
            });
            MelonStem = Add(851, "melon_stem", "Melon Stem", "melon_stem", d =>
            {
                d.Shape = BlockShape.Crop; d.Render = RenderKind.Cutout; d.Sound = "grass"; d.Pushable = false;
                d.Description = "A melon stem. Grown, it sets a melon on the ground beside it.";
            });
            foreach (var (key, name) in new[] { ("brown_mushroom", "Brown Mushroom"), ("red_mushroom", "Red Mushroom") })
            {
                var m = Add(key == "brown_mushroom" ? (ushort)852 : (ushort)853, key, name, key, d =>
                {
                    d.Shape = BlockShape.Plant; d.Render = RenderKind.Cutout; d.Sound = "grass";
                    d.Description = key == "brown_mushroom"
                        ? "A brown mushroom, from the Nether's forests or a wandering trader. With a spider eye and sugar it ferments the eye for brewing; two mushrooms and a bowl make stew."
                        : "A red mushroom, from the Nether's forests or a wandering trader. Two mushrooms and a bowl make stew.";
                });
                if (key == "brown_mushroom") BrownMushroom = m; else RedMushroom = m;
                familyHardness[key] = (0f, ToolKind.None, 0);
            }
            familyHardness["melon"] = (1f, ToolKind.Axe, 0);
        }

        public static bool IsMushroom(BlockDef d) => d != null && (d == BrownMushroom || d == RedMushroom);
    }

    public static class Mushrooms
    {
        /// <summary>Where a mushroom can stand: on top of a full block (Minecraft's: any solid block, in the dark; this world
        /// has no light levels, so anywhere), or the moon's own ground.</summary>
        public static bool CanStand(BlockKey k)
        {
            var w = BlockWorld.Instance;
            if (w == null) return false;
            var below = k.Offset((int)Face.Down);
            var bd = w.DefAt(below);
            if (bd != null) return bd.Solid && bd.Shape == BlockShape.Cube;
            return Ground.MaterialUnder(k) != null;
        }
    }
}
