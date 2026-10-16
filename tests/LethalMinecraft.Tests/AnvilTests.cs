using System.Linq;
using LethalMinecraft;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>#228: Minecraft's anvil rules (AnvilMenu.createResult) on a pickaxe with 1561 uses (iron: 250).</summary>
    public class AnvilTests
    {
        class Items : IAnvilItems
        {
            public int MaxUses(string key) => ItemData.Base(key) == "pickaxe" ? 1561 : ItemData.Base(key) == "iron_pickaxe" ? 250 : 0;
            public int Damage(string key) => Enchants.UsesOf(ItemData.Of(key));
            public string Make(string key, int damage, int ench) => ItemData.With(key, Enchants.Data(damage, ench));
            public bool HasData(string key) => MaxUses(key) > 0 || ItemData.Base(key) == "diamond_chestplate";
            public bool RepairsWith(string key, string material) => (ItemData.Base(key) == "pickaxe" && material == "diamond") || (ItemData.Base(key) == "iron_pickaxe" && material == "iron_ingot");
            public EnchTarget Target(string key) => MaxUses(key) > 0 ? EnchTarget.Tool : ItemData.Base(key) == "diamond_chestplate" ? EnchTarget.Armor : EnchTarget.None;
        }
        static readonly Items I = new Items();
        static string Pick(int used, params (EnchKind, int)[] e) => ItemData.With("pickaxe", Enchants.Data(used, Enchants.Pack(e)));

        [Fact] public void RepairAQuarterPerDiamond()
        {
            var r = AnvilLogic.Compute(I, Pick(1000), 1, "diamond", 5, null, "Diamond Pickaxe", null, false);
            Assert.Equal(3, r.RightUsed); // 390 + 390 + 220
            Assert.Equal(0, I.Damage(r.Key));
            Assert.Equal(3, r.Cost);
            Assert.Equal(1, Enchants.WorkOf(Enchants.EnchOf(ItemData.Of(r.Key))));
        }

        [Fact] public void NothingToMendNothingOut() => Assert.Null(AnvilLogic.Compute(I, Pick(0), 1, "diamond", 1, null, "Diamond Pickaxe", null, false));

        [Fact] public void CombineSameLevelGoesUp()
        {
            var r = AnvilLogic.Compute(I, Pick(0, (EnchKind.Efficiency, 3)), 1, Pick(0, (EnchKind.Efficiency, 3)), 1, null, "Diamond Pickaxe", null, false);
            Assert.Equal(4, Enchants.Level(Enchants.EnchOf(ItemData.Of(r.Key)), EnchKind.Efficiency));
            Assert.Equal(4, r.Cost); // anvil_cost 1 x level 4
        }

        [Fact] public void CombineUsesPlusTwelvePercent()
        {
            var r = AnvilLogic.Compute(I, Pick(1200), 1, Pick(1200), 1, null, "Diamond Pickaxe", null, false);
            // 361 + 361 + 187 = 909 left: 652 used
            Assert.Equal(652, I.Damage(r.Key));
            Assert.Equal(2, r.Cost);
        }

        [Fact] public void UnbreakingCostsTwoALevel()
        {
            var r = AnvilLogic.Compute(I, Pick(0), 1, Pick(0, (EnchKind.Unbreaking, 3)), 1, null, "Diamond Pickaxe", null, false);
            Assert.Equal(6, r.Cost);
        }

        [Fact] public void RenameOneLevel()
        {
            var r = AnvilLogic.Compute(I, "name_tag", 3, null, 0, null, "Name Tag", "Steve", false);
            Assert.Equal("Steve", r.Rename); Assert.Equal(1, r.Cost); Assert.Equal(3, r.Count); Assert.Equal("name_tag", r.Key);
        }

        [Fact] public void SameNameNothing() => Assert.Null(AnvilLogic.Compute(I, "name_tag", 1, null, 0, null, "Name Tag", "Name Tag", false));

        [Fact] public void ClearingANameTakesItOff()
        {
            var r = AnvilLogic.Compute(I, "cobblestone~4", 10, null, 0, "Rocks", "Cobblestone", "", false);
            Assert.True(r.ClearName); Assert.Equal("cobblestone", r.Key);
        }

        [Fact] public void PriorWorkDoubles()
        {
            var worked = ItemData.With("pickaxe", Enchants.Data(1000, Enchants.WithWork(0, 3))); // repair cost 7
            var r = AnvilLogic.Compute(I, worked, 1, "diamond", 1, null, "Diamond Pickaxe", null, false);
            Assert.Equal(8, r.Cost);
            Assert.Equal(4, Enchants.WorkOf(Enchants.EnchOf(ItemData.Of(r.Key))));
        }

        [Fact] public void TooExpensive()
        {
            var worked = ItemData.With("pickaxe", Enchants.Data(1000, Enchants.WithWork(0, 6))); // 63
            var r = AnvilLogic.Compute(I, worked, 1, "diamond", 1, null, "Diamond Pickaxe", null, false);
            Assert.True(r.TooExpensive);
            Assert.Null(r.Key);
        }

        [Fact] public void RenameAloneNeverTooExpensive()
        {
            var worked = ItemData.With("pickaxe", Enchants.Data(0, Enchants.WithWork(0, 6)));
            var r = AnvilLogic.Compute(I, worked, 1, null, 0, null, "Diamond Pickaxe", "Digger", false);
            Assert.Equal(39, r.Cost); Assert.False(r.TooExpensive);
            Assert.Equal(6, Enchants.WorkOf(Enchants.EnchOf(ItemData.Of(r.Key)))); // (a rename alone doesn't add work)
        }

        [Fact] public void WrongItemNothing() => Assert.Null(AnvilLogic.Compute(I, Pick(500), 1, "iron_ingot", 1, null, "Diamond Pickaxe", null, false));

        [Fact] public void ArmorEnchantsJoin()
        {
            string Chest(int lvl) => ItemData.With("diamond_chestplate", Enchants.Data(0, Enchants.Pack(new[] { (EnchKind.Protection, lvl) })));
            var r = AnvilLogic.Compute(I, Chest(2), 1, Chest(2), 1, null, "Diamond Chestplate", null, false);
            Assert.Equal(3, Enchants.Level(Enchants.EnchOf(ItemData.Of(r.Key)), EnchKind.Protection));
        }

        [Fact] public void WorkIsNoEnchantment()
        {
            Assert.False(Enchants.Any(Enchants.WithWork(0, 5)));
            Assert.Empty(Enchants.Unpack(Enchants.WithWork(0, 5)));
            Assert.Equal(5, Enchants.WorkOf(Enchants.WithWork(Enchants.Pack(new[] { (EnchKind.Efficiency, 2), (EnchKind.Unbreaking, 1) }), 5)));
        }

        [Fact] public void NamesRideOnKeys()
        {
            var k = ItemData.WithName(ItemData.With("pickaxe", 1234), 7);
            Assert.Equal("pickaxe#1234~7", k);
            Assert.Equal("pickaxe", ItemData.Base(k)); Assert.Equal(1234, ItemData.Of(k)); Assert.Equal(7, ItemData.NameId(k));
            Assert.Equal("pickaxe#99~7", ItemData.With(k, 99));
            Assert.Equal("cobblestone", ItemData.Base("cobblestone~3"));
            Assert.Equal(0, ItemData.Of("cobblestone~3"));
            Assert.Equal("pickaxe#1234", ItemData.Unnamed(k));
        }
    }
}
