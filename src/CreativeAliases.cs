using System.Collections.Generic;

namespace LethalMinecraft
{
    /// <summary>Minecraft's item id for ours where the key differs (#177: for the creative tabs' order).</summary>
    public static class CreativeAliases
    {
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            ["grass"] = "grass_block", ["leaves"] = "oak_leaves", ["slime"] = "slime_block", ["dark_planks"] = "dark_oak_planks",
            ["button"] = "stone_button", ["pressure_plate"] = "stone_pressure_plate", ["redstone_dust"] = "redstone", ["magma"] = "magma_block",
            ["cut_sandstone_slab"] = "sandstone_slab", ["water"] = "water_bucket", ["lava"] = "lava_bucket",
            ["steak"] = "cooked_beef", ["raw_beef"] = "beef", ["raw_porkchop"] = "porkchop", ["porkchop"] = "cooked_porkchop", ["raw_chicken"] = "chicken", ["raw_mutton"] = "mutton",
            ["raw_rabbit"] = "rabbit", ["raw_cod"] = "cod", ["raw_salmon"] = "salmon",
            ["pickaxe"] = "diamond_pickaxe", ["firework_rocket"] = "@fireworksalldurations", ["written_book"] = "writable_book",
        };
    }
}
