namespace LethalMinecraft
{
    /// <summary>
    /// (#244 part E) Minecraft 26.3's recipes for part E: the cake (its milk buckets give their buckets back: Remainder)
    /// and the pumpkin pie.
    /// </summary>
    public static partial class Crafting
    {
        static void ThoroughERecipes()
        {
            S("Food", "cake", 1, new[] { "MMM", "SES", "WWW" }, ('M', "milk_bucket"), ('S', "sugar"), ('E', "egg"), ('W', "wheat"));
            L("Food", "pumpkin_pie", 1, "pumpkin", "sugar", "egg");
        }
    }
}
