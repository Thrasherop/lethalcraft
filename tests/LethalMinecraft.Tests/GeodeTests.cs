using System;
using System.Collections.Generic;
using System.Linq;
using LethalMinecraft;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>(#229) Amethyst geodes (GeodeGen: Minecraft's GeodeFeature) and the amethyst blocks, items and recipes.</summary>
    public class GeodeTests
    {
        static GeodeTests() { Blocks.Init(); Crafting.Init(); }

        static readonly int[,] Dir = { { 0, -1, 0 }, { 0, 1, 0 }, { 0, 0, 1 }, { 0, 0, -1 }, { -1, 0, 0 }, { 1, 0, 0 } }; // (Faces.Dir)

        /// <summary>Some geodes (every region given one).</summary>
        static IEnumerable<GeodeGen.Geode> Some(int n, int salt = 3)
        {
            for (int i = 0; i < n; i++) yield return GeodeGen.Generate(i * 7 - 40, -2 - i % 3, i * 3 - 20, salt, 1f);
        }

        static int Rank(GeodeGen.Layer l) => l == GeodeGen.Layer.Air ? 0 : l == GeodeGen.Layer.Amethyst || l == GeodeGen.Layer.Budding ? 1 : l == GeodeGen.Layer.Calcite ? 2 : l == GeodeGen.Layer.Basalt ? 3 : 4;

        [Fact]
        public void SameRegionSameGeode()
        {
            for (int i = 0; i < 30; i++)
            {
                var a = GeodeGen.Generate(i, -3, -i, 5, 1f);
                var b = GeodeGen.Generate(i, -3, -i, 5, 1f);
                Assert.Equal((a.CX, a.CY, a.CZ, a.Points, a.Cracked, a.Hollow.Count, a.Buds.Count), (b.CX, b.CY, b.CZ, b.Points, b.Cracked, b.Hollow.Count, b.Buds.Count));
                for (int x = a.CX - GeodeGen.Reach; x <= a.CX + GeodeGen.Reach; x++)
                    for (int y = a.CY - GeodeGen.Reach; y <= a.CY + GeodeGen.Reach; y++)
                        Assert.Equal(a.At(x, y, a.CZ), b.At(x, y, a.CZ));
            }
            // (another moon: other geodes)
            int differ = 0;
            for (int i = 0; i < 30; i++)
            {
                var a = GeodeGen.Generate(i, -3, -i, 5, 1f); var b = GeodeGen.Generate(i, -3, -i, 6, 1f);
                if ((a.CX, a.CY, a.CZ, a.Hollow.Count) != (b.CX, b.CY, b.CZ, b.Hollow.Count)) differ++;
            }
            Assert.True(differ > 20, $"only {differ} of 30 differ between moons");
        }

        [Fact]
        public void AGeodeStaysInItsRegion()
        {
            foreach (var g in Some(60))
            {
                int x0 = g.RX * GeodeGen.Region, y0 = g.RY * GeodeGen.Region, z0 = g.RZ * GeodeGen.Region;
                for (int x = g.CX - GeodeGen.Reach - 1; x <= g.CX + GeodeGen.Reach + 1; x++)
                    for (int y = g.CY - GeodeGen.Reach - 1; y <= g.CY + GeodeGen.Reach + 1; y++)
                        for (int z = g.CZ - GeodeGen.Reach - 1; z <= g.CZ + GeodeGen.Reach + 1; z++)
                            if (g.At(x, y, z) != GeodeGen.Layer.None)
                                Assert.True(x >= x0 && x < x0 + GeodeGen.Region && y >= y0 && y < y0 + GeodeGen.Region && z >= z0 && z < z0 + GeodeGen.Region, $"{x},{y},{z} out of region {g.RX},{g.RY},{g.RZ}");
            }
        }

        [Fact]
        public void LayersGoOutwardAirAmethystCalciteBasalt()
        {
            int rays = 0, basaltLast = 0;
            foreach (var g in Some(60))
            {
                Assert.Equal(GeodeGen.Layer.Air, g.At(g.CX, g.CY, g.CZ));
                for (int f = 0; f < 6; f++)
                {
                    // out from the middle the layers never go back inward (the crack's air aside); the last before stone is basalt
                    int best = 0; var last = GeodeGen.Layer.Air;
                    for (int d = 0; d <= GeodeGen.Reach; d++)
                    {
                        var l = g.At(g.CX + Dir[f, 0] * d, g.CY + Dir[f, 1] * d, g.CZ + Dir[f, 2] * d);
                        if (l == GeodeGen.Layer.None) break;
                        if (l == GeodeGen.Layer.Air && best > 0) continue;
                        Assert.True(Rank(l) >= best, $"{l} after rank {best}");
                        best = Rank(l); last = l;
                    }
                    rays++; if (last == GeodeGen.Layer.Basalt) basaltLast++;
                }
                Assert.True(g.Count(GeodeGen.Layer.Calcite) > 0 && g.Count(GeodeGen.Layer.Basalt) > 0 && g.Count(GeodeGen.Layer.Amethyst) > 0);
            }
            // (the basalt is under a block thick, as Minecraft's: a straight line out can step over it, and the crack opens
            // some ways right out; most end on it)
            Assert.True(basaltLast > rays * 0.6, $"{basaltLast} of {rays} lines out end on basalt");
        }

        [Fact]
        public void MinecraftsSizesAndOdds()
        {
            int n = 0, cracked = 0; long air = 0, amethyst = 0, budding = 0, buds = 0;
            var stages = new int[4];
            foreach (var g in Some(200))
            {
                n++; if (g.Cracked) cracked++;
                air += g.Count(GeodeGen.Layer.Air); amethyst += g.Count(GeodeGen.Layer.Amethyst); budding += g.Count(GeodeGen.Layer.Budding); buds += g.Buds.Count;
                foreach (var b in g.Buds) stages[b.Stage]++;
                Assert.InRange(g.Points, 3, 4);
            }
            // (Minecraft's: about 600 cells of air, budding amethyst one in 12 of the inner layer, 95% cracked)
            Assert.InRange(air / n, 250, 1200);
            Assert.InRange(budding / (double)(budding + amethyst), 0.06, 0.106);
            Assert.InRange(cracked / (double)n, 0.88, 1.0);
            Assert.InRange(buds / (double)n, 2.0, 15.0);
            foreach (int s in stages) Assert.InRange(s / (double)buds, 0.17, 0.33); // (each of the four, even odds)
        }

        [Fact]
        public void BudsGrowIntoTheAirOffBuddingAmethyst()
        {
            foreach (var g in Some(80))
            {
                var cells = new HashSet<(int, int, int)>();
                foreach (var b in g.Buds)
                {
                    Assert.Equal(GeodeGen.Layer.Air, g.At(b.X, b.Y, b.Z));
                    Assert.Equal(GeodeGen.Layer.Budding, g.At(b.X - Dir[b.Face, 0], b.Y - Dir[b.Face, 1], b.Z - Dir[b.Face, 2]));
                    Assert.InRange(b.Stage, 0, 3);
                    Assert.True(cells.Add((b.X, b.Y, b.Z)), "two buds in one cell");
                }
                Assert.Equal(g.Count(GeodeGen.Layer.Air), g.Hollow.Count);
            }
        }

        [Fact]
        public void LookupFindsTheRegionsGeode()
        {
            GeodeGen.Reset();
            int found = 0;
            for (int r = 0; r < 40; r++)
            {
                var g = GeodeGen.InRegion(r, -2, 5, 9);
                if (g == null) continue;
                found++;
                Assert.Same(g, GeodeGen.Containing(g.CX, g.CY, g.CZ, 9));
                Assert.Equal(GeodeGen.Layer.Air, GeodeGen.LayerAt(g.CX, g.CY, g.CZ, 9));
                var (hx, hy, hz) = g.Hollow[0];
                Assert.Same(g, GeodeGen.Containing(hx, hy, hz, 9));
                Assert.Null(GeodeGen.Containing(g.RX * GeodeGen.Region, g.RY * GeodeGen.Region, g.RZ * GeodeGen.Region, 9)); // (a region's corner: stone)
            }
            // (about Chance of the regions)
            Assert.InRange(found, 40 * GeodeGen.Chance * 0.5, 40 * GeodeGen.Chance * 1.5);
        }

        /// <summary>How often: half a moon of branch mining with a stone pickaxe (Balance's 60 s of digging) at their depth
        /// finds about PerHalfMoon geodes.</summary>
        [Fact]
        public void HalfAMoonOfMiningFindsAboutAQuarterOfAGeode()
        {
            float stone = Blocks.BreakTime(Blocks.Stone, ToolKind.Pickaxe, 2, 4f);
            float found = GeodeGen.FoundIn(60f, stone, GeodeGen.Chance, seed: 1);
            Assert.InRange(found, GeodeGen.PerHalfMoon * 0.7f, GeodeGen.PerHalfMoon * 1.3f);
        }

        [Fact]
        public void BuddingAmethystGrowsInFourStages()
        {
            Assert.Equal(0, GeodeGen.Grow(true, -1, false));       // open air: a small bud
            Assert.Equal(1, GeodeGen.Grow(false, 0, true));        // small -> medium
            Assert.Equal(2, GeodeGen.Grow(false, 1, true));        // medium -> large
            Assert.Equal(3, GeodeGen.Grow(false, 2, true));        // large -> cluster
            Assert.Equal(-1, GeodeGen.Grow(false, 3, true));       // a cluster is grown
            Assert.Equal(-1, GeodeGen.Grow(false, 1, false));      // a bud of another block's
            Assert.Equal(-1, GeodeGen.Grow(false, -1, false));     // stone, a block
            Assert.InRange(GeodeGen.RandomTickSeconds, 68f, 68.5f); // (4096 / 3 ticks)
        }

        // ------------------------------------------------------------------ the blocks, items and recipes
        [Fact]
        public void AmethystBlocksAreInTheirIdsAndMinecraftsHardness()
        {
            var ours = new[] { Blocks.AmethystBlock, Blocks.BuddingAmethyst, Blocks.AmethystCluster, Blocks.LargeAmethystBud, Blocks.MediumAmethystBud, Blocks.SmallAmethystBud, Blocks.Calcite, Blocks.SmoothBasalt, Blocks.TintedGlass };
            foreach (var b in ours)
            {
                Assert.NotNull(b);
                Assert.InRange(b.Id, 700, 749);
                Assert.Same(b, Blocks.Get(b.Id));
            }
            Assert.Equal(1.5f, Blocks.AmethystBlock.Hardness); Assert.Equal(1, Blocks.AmethystBlock.HarvestTier); Assert.Equal(ToolKind.Pickaxe, Blocks.AmethystBlock.Tool);
            Assert.Equal(0.75f, Blocks.Calcite.Hardness); Assert.Equal(1.25f, Blocks.SmoothBasalt.Hardness); Assert.Equal(0.3f, Blocks.TintedGlass.Hardness);
            Assert.Equal(0, Blocks.AmethystCluster.HarvestTier); // (drops to any tool, more to a pickaxe)
            foreach (var b in new[] { Blocks.AmethystCluster, Blocks.LargeAmethystBud, Blocks.MediumAmethystBud, Blocks.SmallAmethystBud })
            {
                Assert.Equal(BlockShape.Cluster, b.Shape);
                Assert.True(b.Collides && !b.Solid && !b.StandsAlone);
                Assert.True(b.LightIntensity > 0f);
            }
            Assert.True(Blocks.AmethystCluster.LightIntensity > Blocks.LargeAmethystBud.LightIntensity && Blocks.LargeAmethystBud.LightIntensity > Blocks.MediumAmethystBud.LightIntensity && Blocks.MediumAmethystBud.LightIntensity > Blocks.SmallAmethystBud.LightIntensity);
        }

        [Fact]
        public void WhatTheyDrop()
        {
            Assert.True(Amethyst.DropsNothing(Blocks.BuddingAmethyst));
            Assert.True(Amethyst.DropsNothing(Blocks.SmallAmethystBud) && Amethyst.DropsNothing(Blocks.MediumAmethystBud) && Amethyst.DropsNothing(Blocks.LargeAmethystBud));
            Assert.False(Amethyst.DropsNothing(Blocks.AmethystCluster) || Amethyst.DropsNothing(Blocks.AmethystBlock) || Amethyst.DropsNothing(Blocks.TintedGlass));
            Assert.Equal(Amethyst.ShardKey, Blocks.AmethystCluster.DropKey); // (2 without a pickaxe; 4 with one, ServerClusterDrop)
            Assert.Equal(2, Blocks.AmethystCluster.DropMin); Assert.Equal(2, Blocks.AmethystCluster.DropMax);
            for (int s = 0; s < 4; s++) Assert.Equal(s, Amethyst.StageOf(Amethyst.BudOf(s)));
            Assert.True(Amethyst.Chimes(Blocks.AmethystBlock) && Amethyst.Chimes(Blocks.BuddingAmethyst) && !Amethyst.Chimes(Blocks.AmethystCluster));
            Assert.True(Amethyst.IsAmethyst(Blocks.SmallAmethystBud) && !Amethyst.IsAmethyst(Blocks.Calcite));
        }

        static string MakeN(int size, params string[] cells)
        {
            var g = new string[size * size];
            for (int i = 0; i < cells.Length; i++) g[i] = cells[i] == "." ? null : cells[i];
            var r = RecipeBook.Match(Crafting.Recipes, g, size);
            return r == null ? null : $"{r.Result}x{r.Count}";
        }

        [Fact]
        public void MinecraftsAmethystRecipes()
        {
            const string S = "amethyst_shard";
            Assert.Equal("amethyst_blockx1", MakeN(2, S, S, S, S));
            Assert.Equal("tinted_glassx2", MakeN(3, ".", S, ".", S, "glass", S, ".", S, "."));
            Assert.Equal("spyglassx1", MakeN(3, ".", S, ".", ".", "copper_ingot", ".", ".", "copper_ingot", "."));
            Assert.Null(MakeN(3, ".", S, ".", ".", S, ".", ".", "copper_ingot", "."));
        }

        [Fact]
        public void TheShardIsATrimMaterial() => Assert.Equal("amethyst", ArmorTrims.MaterialItems["amethyst_shard"]);
    }
}
