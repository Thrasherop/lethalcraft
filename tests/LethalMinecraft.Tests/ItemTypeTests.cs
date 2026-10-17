using LethalMinecraft;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>#228: what an item is goes by its type, never its whole key: a worn, enchanted, valued or named item is
    /// still itself to recipes, machines and checks.</summary>
    public class ItemTypeTests
    {
        static ItemTypeTests() { Blocks.Init(); Crafting.Init(); }

        static string[] G(int size, params string[] cells) { var g = new string[size * size]; for (int i = 0; i < cells.Length; i++) g[i] = cells[i] == "." ? null : cells[i]; return g; }
        static string Make(int size, params string[] cells) => RecipeBook.Match(Crafting.Recipes, G(size, cells), size)?.Result;

        // ------------------------------------------------------------------ the rule
        [Fact] public void TypeOfLeavesTheNumberAndNameOff()
        {
            Assert.Equal("pickaxe", ItemData.TypeOf("pickaxe#1234~3"));
            Assert.Equal("cobblestone", ItemData.TypeOf("cobblestone~7"));
            Assert.Equal("scrap_diamond_ore", ItemData.TypeOf("scrap_diamond_ore#87"));
            Assert.Equal("stick", ItemData.TypeOf("stick"));
            Assert.Null(ItemData.TypeOf(null));
        }

        [Fact] public void IsGoesByType()
        {
            Assert.True(ItemData.Is("totem_of_undying~2", "totem_of_undying"));
            Assert.True(ItemData.Is("pickaxe#1234~3", "pickaxe"));
            Assert.True(ItemData.Is("blaze_powder", "blaze_powder"));
            Assert.False(ItemData.Is("blaze_rod~1", "blaze_powder"));
            Assert.False(ItemData.Is(null, "stick"));
            Assert.False(ItemData.Is("stick", null));
        }

        [Fact] public void TypeMapFindsAnyKeyOfItsType()
        {
            var m = new ItemData.TypeMap<int> { ["coal"] = 32 };
            Assert.True(m.ContainsKey("coal~4"));
            Assert.True(m.TryGetValue("coal#9~4", out int v) && v == 32);
            Assert.Equal(32, m["coal~1"]);
            Assert.False(m.ContainsKey(null));
            Assert.False(m.TryGetValue(null, out _));
        }

        // ------------------------------------------------------------------ recipes
        [Fact] public void ShapedRecipeTakesNamedIngredients()
        {
            const string C = "cobblestone", St = "stick";
            Assert.Equal("stone_pickaxe", Make(3, C + "~7", C, C + "~2", ".", St + "~1", ".", ".", St, "."));
        }

        [Fact] public void ShapelessRecipeTakesNamedIngredients()
        {
            Assert.Equal("oak_planks", Make(2, "oak_log~3"));
            Assert.Equal("wool_black", Make(2, "wool_white~1", "ink_sac~5"));
        }

        [Fact] public void RecipeTakesAValuedAndNamedMinedDiamond()
        {
            const string P = "oak_planks";
            Assert.Equal("jukebox", Make(3, P, P, P, P, "scrap_diamond_ore#87~2", P, P, P, P));
            Assert.True(RecipeBook.Matches(Crafting.Recipes.Find(r => r.Result == "jukebox" && System.Array.Exists(r.Needs, n => n.key == "scrap_diamond_ore")), G(3, P, P, P, P, "scrap_diamond_ore#87", P, P, P, P), 3));
        }

        [Fact] public void RecipeStillWantsTheRightItem() => Assert.Null(Make(3, "stone~1", "stone", "stone", ".", "stick", ".", ".", "stick", "."));

        // ------------------------------------------------------------------ the furnace
        [Fact] public void FurnaceSmeltsAndBurnsNamedItems()
        {
            Assert.True(Crafting.SmeltResult.ContainsKey("raw_beef~4"));
            Assert.True(Crafting.SmeltResult.TryGetValue("scrap_iron_ore#55~2", out var r) && r == "iron_ingot");
            Assert.True(Crafting.FuelSeconds.ContainsKey("coal~3"));
            Assert.Equal(32f, Crafting.FuelSeconds["coal~3"]);
            Assert.False(Crafting.FuelSeconds.ContainsKey("cobblestone~3"));
        }

        [Fact] public void FurnaceKeepsAnOresValueOffAndAToolsWearOn()
        {
            Assert.Equal("scrap_gold_ore", Crafting.Plain("scrap_gold_ore#40~1"));
            Assert.Equal("coal", Crafting.Plain("coal~3"));
            Assert.Equal("wooden_pickaxe#12", Crafting.Plain("wooden_pickaxe#12~1"));
        }

        // ------------------------------------------------------------------ brewing
        [Fact] public void BrewingTakesNamedPotionsAndIngredients()
        {
            Assert.True(Potions.IsPotion("potion_awkward~2"));
            Assert.True(Potions.IsIngredient("sugar~5"));
            Assert.False(Potions.IsIngredient("stick~5"));
            Assert.Equal("potion_awkward", Potions.BrewOf("potion_water~1", "nether_wart~2"));
            Assert.Equal("potion_swiftness", Potions.BrewOf("potion_awkward", "sugar~5"));
        }

        // ------------------------------------------------------------------ dyes, the loom, the smithing table, the composter
        [Fact] public void DyesGoByType()
        {
            Assert.Equal(14, Dyes.IndexOf("red_dye~4"));
            Assert.Equal(11, Dyes.IndexOf("blue_dye#3"));
            Assert.Equal(-1, Dyes.IndexOf("red_wool~4"));
        }

        [Fact] public void LoomPatternsAndCompostGoByType()
        {
            Assert.True(LoomUI.PatternItems.ContainsKey("creeper_banner_pattern~2"));
            Assert.True(Composting.Chance.TryGetValue("golden_carrot~1", out float c) && c == 1f);
        }

        [Fact] public void SmithingTakesNamedTemplateAndIngotAndKeepsTheItemsDataAndName()
        {
            Assert.Equal("netherite_pickaxe#77~3", Smithing.Result(Smithing.TemplateKey + "~1", "pickaxe#77~3", Smithing.Addition + "~2"));
            Assert.Equal("netherite_axe", Smithing.Result(Smithing.TemplateKey, "diamond_axe", Smithing.Addition));
            Assert.Null(Smithing.Result(Smithing.TemplateKey, "diamond_axe", "gold_ingot~2"));
        }
    }
}
