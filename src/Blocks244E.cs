namespace LethalMinecraft
{
    /// <summary>
    /// (#244 part E) Minecraft's blocks for part E: the cake (seven slices, eaten where it stands). Ids from 1100, after
    /// part D's (saves keep ids: new ones only at the end).
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextEId = 1100;
        public static BlockDef Cake;

        static BlockDef AddE(string key, string name, string tile, float hardness, ToolKind tool, int tier, System.Action<BlockDef> cfg)
        {
            FamilyTiles[tile] = tile;
            var d = Add(nextEId++, key, name, tile, x => cfg?.Invoke(x));
            foreach (var t in new[] { d.TileTop, d.TileBottom, d.TileFront }) if (t != null && t != tile) FamilyTiles[t] = t;
            familyHardness[key] = (hardness, tool, tier);
            return d;
        }

        static void AddThoroughE()
        {
            // (#86, #87) the cake: Minecraft's 7 slices (state: slices eaten, 0-6), 2 hunger each; nothing drops (Silk Touch neither)
            FamilyTiles["cake_inner"] = "cake_inner";
            Cake = AddE("cake", "Cake", "cake_side", 0.5f, ToolKind.None, 0, d => { d.Shape = BlockShape.Cake; d.TileTop = "cake_top"; d.TileBottom = "cake_bottom"; d.Sound = "wool"; d.NeverDrops = true; d.Description = "Milk, sugar, an egg and wheat. Place it and [E] on it: a slice (1 hunger), seven slices in all. Broken, it's gone."; });
        }
    }
}
