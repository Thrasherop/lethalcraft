using System.Collections.Generic;
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
                // (#244: Minecraft's table: what a chestplate takes there, no treasure)
                foreach (var o in Enchants.Offers(seed, 10, EnchTarget.Armor, 9))
                    Assert.All(o.List ?? new List<(EnchKind, int)>(), e => Assert.True(Enchants.AtTable(e.kind, EnchTarget.Armor), e.kind.ToString()));
                Assert.All(Enchants.Offers(seed, 0, EnchTarget.None), o => Assert.Equal(0, o.Ench));
            }
        }

        [Fact]
        public void Effects()
        {
            Assert.Equal(1f, Enchants.EfficiencyFactor(0));
            // (#244: Minecraft's: level² + 1 on the tool's speed; Feather Falling 12% a level)
            Assert.InRange(Enchants.EfficiencyFactor(5, 8f), 4.24f, 4.26f);
            Assert.InRange(Enchants.FeatherFallingFactor(4), 0.51f, 0.53f);
            Assert.Equal(0.25f, Enchants.WearChance(3));
        }
    
        // ------------------------------------------------------------------ the crossbow's (#223)
        [Fact]
        public void CrossbowEnchantmentsPackAndOldOnesReadTheSame()
        {
            foreach (var (k, l) in new[] { (EnchKind.QuickCharge, 3), (EnchKind.Multishot, 1), (EnchKind.Piercing, 1), (EnchKind.Piercing, 4) })
            {
                int e = Enchants.Pack(new[] { (k, l), (EnchKind.Unbreaking, 3) });
                Assert.Equal(l, Enchants.Level(e, k));
                Assert.Equal(3, Enchants.Level(e, EnchKind.Unbreaking));
            }
            Assert.Equal("Quick Charge III, Piercing IV", Enchants.Describe(Enchants.Pack(new[] { (EnchKind.QuickCharge, 3), (EnchKind.Piercing, 4) })));
            // (the first five kinds' bits are as they were: saved tools still read)
            Assert.Equal(1 | (5 << 3), Enchants.Pack(new[] { (EnchKind.Efficiency, 5) }));
        }

        [Fact]
        public void CrossbowOffersNeverPutMultishotWithPiercing()
        {
            for (int seed = 0; seed < 400; seed++)
                foreach (var o in Enchants.Offers(seed, 15, EnchTarget.Crossbow))
                {
                    var kinds = Enchants.Unpack(o.Ench).Select(x => x.kind).ToList();
                    Assert.NotEmpty(kinds);
                    Assert.All(kinds, k => Assert.Contains(k, Enchants.For(EnchTarget.Crossbow)));
                    Assert.False(kinds.Contains(EnchKind.Multishot) && kinds.Contains(EnchKind.Piercing));
                    foreach (var (k, l) in Enchants.Unpack(o.Ench)) Assert.InRange(l, 1, Enchants.MaxLevel(k));
                }
        }

        [Fact]
        public void CrossbowDataKeepsWearLoadAndEnchantmentsApart()
        {
            int e = Enchants.Pack(new[] { (EnchKind.Multishot, 1), (EnchKind.QuickCharge, 2) });
            int d = CrossbowData.Data(464, CrossbowData.Load.Poison, e);
            Assert.Equal(464, CrossbowData.WearOf(d));
            Assert.Equal(CrossbowData.Load.Poison, CrossbowData.LoadOf(d));
            Assert.Equal(e, CrossbowData.EnchOf(d));
            Assert.True(d > 0);
            Assert.Equal(CrossbowData.Load.None, CrossbowData.LoadOf(CrossbowData.Data(2047, CrossbowData.Load.None, 0)));
            Assert.Equal("arrow_slowness", CrossbowData.ArrowKey(CrossbowData.LoadFor("arrow_slowness")));
            // (#244: a rocket, in the wear's top bit: wear and enchantments kept, and an old save's loads read as before)
            int r = CrossbowData.Data(464, CrossbowData.Load.Rocket, e);
            Assert.Equal(CrossbowData.Load.Rocket, CrossbowData.LoadOf(r));
            Assert.Equal(464, CrossbowData.WearOf(r));
            Assert.Equal(e, CrossbowData.EnchOf(r));
            Assert.Equal(CrossbowData.Load.Arrow, CrossbowData.LoadOf(Enchants.Data(100 | (1 << 11), 0)));
        }

        [Theory]
        [InlineData(0, 25)]
        [InlineData(1, 20)]
        [InlineData(2, 15)]
        [InlineData(3, 10)]
        public void CrossbowChargeTicksAreMinecrafts(int quickCharge, int ticks) => Assert.Equal(ticks, CrossbowData.ChargeTicks(quickCharge));

        [Fact]
        public void DroppedCrossbowsAreWornButNotBroken()
        {
            var rnd = new System.Random(3);
            for (int i = 0; i < 2000; i++)
                Assert.InRange(CrossbowData.DroppedWear(465, rnd.NextDouble(), rnd.NextDouble()), 4, 464);
            Assert.Equal(new[] { 0f, -10f, 10f }, CrossbowData.ShotAngles(true));
        }

        // ------------------------------------------------------------------ (#244) more than fits: a list by number
        [Fact]
        public void BigListsGoByNumberAndReadBack()
        {
            EnchSets.Reset(); EnchSets.Allocate = null;
            var list = new[] { (EnchKind.Efficiency, 5), (EnchKind.Unbreaking, 3), (EnchKind.Fortune, 3), (EnchKind.Mending, 1) };
            int e = Enchants.Pack(list);
            Assert.True(e > 0);
            Assert.Equal(EnchSets.Marker, e & 63);
            Assert.Equal(list.ToList(), Enchants.Unpack(e));
            Assert.Equal(3, Enchants.Level(e, EnchKind.Fortune));
            Assert.True(Enchants.Data(100, e) > 0);
            Assert.Equal(e, Enchants.EnchOf(Enchants.Data(100, e)));
            // the same list, the same number
            Assert.Equal(e, Enchants.Pack(list));
            // a work count with three of the old kinds: kept now (it had been lost)
            var three = new[] { (EnchKind.QuickCharge, 3), (EnchKind.Piercing, 4), (EnchKind.Unbreaking, 3) };
            int w = Enchants.WithWork(Enchants.Pack(three), 2);
            Assert.Equal(2, Enchants.WorkOf(w));
            Assert.Equal(three.ToList(), Enchants.Unpack(w));
            // old ones still pack as before
            Assert.Equal(1 | (5 << 3), Enchants.Pack(new[] { (EnchKind.Efficiency, 5) }));
            // waiting on the host: -1, nothing on the item
            EnchSets.Allocate = t => 0;
            Assert.Equal(-1, Enchants.Pack(new[] { (EnchKind.Looting, 3), (EnchKind.Sharpness, 5) }));
            Assert.Equal(0, Enchants.EnchOf(Enchants.Data(5, -1)));
            EnchSets.Allocate = null; EnchSets.Reset();
        }

        // ------------------------------------------------------------------ (#244) dyed leather: a colour with the list
        [Fact]
        public void ColoursGoWithTheList()
        {
            EnchSets.Reset(); EnchSets.Allocate = null;
            Assert.Equal(-1, Enchants.ColorOf(0));
            int red = Enchants.Pack(new (EnchKind, int)[0], 0, 0xB02E26);
            Assert.True(red > 0);
            Assert.Equal(0xB02E26, Enchants.ColorOf(red));
            Assert.False(Enchants.Any(red));                                  // (a colour isn't a glint)
            Assert.Empty(Enchants.Unpack(red));
            // enchanted and worked, it keeps its colour; washed, it's back to nothing
            int ench = Enchants.Pack(new[] { (EnchKind.Protection, 4) }, 0, 0xB02E26);
            Assert.Equal(0xB02E26, Enchants.ColorOf(Enchants.WithWork(ench, 3)));
            Assert.Equal(3, Enchants.WorkOf(Enchants.WithWork(ench, 3)));
            Assert.Equal(4, Enchants.Level(Enchants.WithWork(ench, 3), EnchKind.Protection));
            Assert.Equal(0, Enchants.WithColor(red, -1));
            Assert.Equal(Enchants.Pack(new[] { (EnchKind.Protection, 4) }), Enchants.WithColor(ench, -1));
            // black (0x000000) is a colour too, and the text reads back (the host's save and its messages)
            int black = Enchants.Pack(new (EnchKind, int)[0], 0, 0);
            Assert.Equal(0, Enchants.ColorOf(black));
            var all = EnchSets.All.ToList();
            EnchSets.Reset();
            foreach (var (id, text) in all) EnchSets.Receive(id, text);
            Assert.Equal(0xB02E26, Enchants.ColorOf(red));
            Assert.Equal(0, Enchants.ColorOf(black));
            EnchSets.Reset();
        }

        [Fact]
        public void RawTextsShareTheNumbers()
        {
            EnchSets.Reset(); EnchSets.Allocate = null;
            int e = Enchants.Pack(new[] { (EnchKind.Looting, 3), (EnchKind.Sharpness, 5), (EnchKind.Mending, 1), (EnchKind.Unbreaking, 3) });
            int f = EnchSets.IdOfRaw("F:r:2|0,B02E26,,1,0");
            Assert.True(f > 0);
            Assert.NotEqual((e >> 6) & EnchSets.MaxId, f);
            Assert.Equal("F:r:2|0,B02E26,,1,0", EnchSets.RawOf(f));
            Assert.Equal(f, EnchSets.IdOfRaw("F:r:2|0,B02E26,,1,0"));
            Assert.Empty(Enchants.Unpack(Enchants.EnchOf(f)));                 // (a small number on a stack isn't an enchantment)
            var all = EnchSets.All.ToList();
            EnchSets.Reset();
            foreach (var (id, text) in all) EnchSets.Receive(id, text);
            Assert.Equal("F:r:2|0,B02E26,,1,0", EnchSets.RawOf(f));
            Assert.Equal(3, Enchants.Level(e, EnchKind.Looting));
            EnchSets.Reset();
        }

        [Fact]
        public void MinecraftsTableRules()
        {
            Assert.True(Enchants.Exclusive(EnchKind.Sharpness, EnchKind.Smite));
            Assert.True(Enchants.Exclusive(EnchKind.Fortune, EnchKind.SilkTouch));
            Assert.True(Enchants.Exclusive(EnchKind.Infinity, EnchKind.Mending));
            Assert.False(Enchants.Exclusive(EnchKind.Sharpness, EnchKind.Looting));
            Assert.False(Enchants.AtTable(EnchKind.Mending, EnchTarget.Tool));        // (treasure)
            Assert.True(Enchants.Supports(EnchKind.Mending, EnchTarget.Tool));
            Assert.False(Enchants.AtTable(EnchKind.Sharpness, EnchTarget.Axe));      // (an axe's sharpness: the anvil)
            Assert.True(Enchants.Supports(EnchKind.Sharpness, EnchTarget.Axe));
            Assert.Equal(0, Enchants.Enchantability(EnchTarget.Shears, "shears"));   // (shears: anvil only)
            Assert.Equal(22, Enchants.Enchantability(EnchTarget.Sword, "golden_sword"));
            Assert.Equal(25, Enchants.Enchantability(EnchTarget.Boots, "golden_boots"));
            // offers: a book can get anything at the table; a pickaxe never a sword's
            EnchSets.Reset(); EnchSets.Allocate = null;
            for (int seed = 0; seed < 200; seed++)
                foreach (var o in Enchants.Offers(seed, 15, EnchTarget.Tool, 10))
                    Assert.All(o.List ?? new List<(EnchKind, int)>(), e => Assert.True(Enchants.AtTable(e.kind, EnchTarget.Tool)));
            // with 15 shelves the third slot is level 30 and often brings more than one
            int multi = 0;
            for (int seed = 0; seed < 200; seed++) { var o = Enchants.Offers(seed, 15, EnchTarget.Sword, 14)[2]; Assert.Equal(30, o.Required); if (o.List.Count > 1) multi++; }
            Assert.InRange(multi, 40, 200);
            EnchSets.Reset();
        }
    }
}
