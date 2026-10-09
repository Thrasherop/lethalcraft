using System.Linq;
using LethalMinecraft;
using Xunit;

namespace LethalMinecraft.Tests
{
    public class EnchantTests
    {
        [Fact]
        public void PackAndUnpack()
        {
            int e = Enchants.Pack(new[] { (EnchKind.Efficiency, 5), (EnchKind.Unbreaking, 3) });
            var list = Enchants.Unpack(e);
            Assert.Equal(new[] { (EnchKind.Efficiency, 5), (EnchKind.Unbreaking, 3) }, list.ToArray());
            Assert.Equal(5, Enchants.Level(e, EnchKind.Efficiency));
            Assert.Equal(0, Enchants.Level(e, EnchKind.Sharpness));
            Assert.Equal("Efficiency V, Unbreaking III", Enchants.Describe(e));
        }

        [Fact]
        public void UsesAndEnchantmentsShareTheSavedNumber()
        {
            int e = Enchants.Pack(new[] { (EnchKind.Protection, 4), (EnchKind.FeatherFalling, 4), (EnchKind.Unbreaking, 3) });
            int d = Enchants.Data(1561, e);
            Assert.True(d > 0); // (fits a positive int: a key's "#n" and the game's item save)
            Assert.Equal(1561, Enchants.UsesOf(d));
            Assert.Equal(e, Enchants.EnchOf(d));
            Assert.Equal(100, Enchants.Data(100, 0)); // (a worn, plain tool's number is its uses: keys from 1.4.10 still read)
        }

        [Fact]
        public void OffersAreTheSameForTheSameSeedAndGrowWithShelves()
        {
            var a = Enchants.Offers(1234, 15, EnchTarget.Tool);
            var b = Enchants.Offers(1234, 15, EnchTarget.Tool);
            Assert.Equal(a.Select(o => (o.Required, o.Cost, o.Ench)), b.Select(o => (o.Required, o.Cost, o.Ench)));
            Assert.Equal(new[] { 1, 2, 3 }, a.Select(o => o.Cost));
            for (int seed = 0; seed < 200; seed++)
            {
                var none = Enchants.Offers(seed, 0, EnchTarget.Sword);
                var full = Enchants.Offers(seed, 15, EnchTarget.Sword);
                Assert.True(none.Max(o => o.Required) <= 8, $"seed {seed}: no shelves, at most level 8");
                Assert.Equal(30, full[2].Required); // (15 shelves: the top offer needs 30)
                foreach (var o in full.Concat(none))
                    foreach (var (k, l) in Enchants.Unpack(o.Ench))
                    {
                        Assert.Contains(k, Enchants.For(EnchTarget.Sword));
                        Assert.InRange(l, 1, Enchants.MaxLevel(k));
                    }
            }
        }

        [Fact]
        public void OnlyFittingEnchantments()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                foreach (var o in Enchants.Offers(seed, 10, EnchTarget.Armor))
                    Assert.All(Enchants.Unpack(o.Ench), e => Assert.Equal(EnchKind.Protection, e.kind));
                Assert.All(Enchants.Offers(seed, 0, EnchTarget.None), o => Assert.Equal(0, o.Ench));
            }
        }

        [Fact]
        public void Effects()
        {
            Assert.Equal(1f, Enchants.EfficiencyFactor(0));
            Assert.InRange(Enchants.EfficiencyFactor(5), 3.4f, 3.6f);
            Assert.InRange(Enchants.FeatherFallingFactor(4), 0.39f, 0.41f);
            Assert.Equal(0.25f, Enchants.WearChance(3));
        }
    }
}
