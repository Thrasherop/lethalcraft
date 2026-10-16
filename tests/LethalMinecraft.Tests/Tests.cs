using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LethalMinecraft;
using UnityEngine;
using Xunit;
using H = LethalMinecraft.GroundRules.Hit;
using PV = LethalMinecraft.MeshClip.PV;

[assembly: CollectionBehavior(DisableTestParallelization = true)] // the block/recipe tables are shared static data

namespace LethalMinecraft.Tests
{
    // ------------------------------------------------------------------ mesh clipping (holes in level geometry)
    public class MeshClipTests
    {
        static (PV, PV, PV) Tri(Vector3 a, Vector3 b, Vector3 c) => (PV.Original(0, a), PV.Original(1, b), PV.Original(2, c));
        static float TriArea(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        static float Area(List<List<PV>> pieces) => pieces.Sum(MeshClip.Area);

        [Fact]
        public void TriangleAwayFromBoxIsUntouched()
        {
            var (a, b, c) = Tri(new Vector3(10, 0, 10), new Vector3(11, 0, 10), new Vector3(10, 0, 11));
            Assert.Null(MeshClip.ClipOutsideBoxes(a, b, c, new[] { Vector3.zero }, new[] { Vector3.one }));
        }

        [Fact]
        public void TriangleInsideBoxIsRemovedCompletely()
        {
            var (a, b, c) = Tri(new Vector3(0.2f, 0.5f, 0.2f), new Vector3(0.8f, 0.5f, 0.2f), new Vector3(0.2f, 0.5f, 0.8f));
            var pieces = MeshClip.ClipOutsideBoxes(a, b, c, new[] { Vector3.zero }, new[] { Vector3.one });
            Assert.NotNull(pieces);
            Assert.True(Area(pieces) < 1e-5f);
        }

        [Fact]
        public void FloorQuadGetsExactlyOneCellRemoved()
        {
            // a 4x4 floor at y=0.5 made of two triangles; cut a 1x1 cell out of the middle
            var p = new[] { new Vector3(0, 0.5f, 0), new Vector3(4, 0.5f, 0), new Vector3(4, 0.5f, 4), new Vector3(0, 0.5f, 4) };
            var mins = new[] { new Vector3(1, 0, 1) }; var maxs = new[] { new Vector3(2, 1, 2) };
            float left = 0;
            foreach (var (i, j, k) in new[] { (0, 1, 2), (0, 2, 3) })
            {
                var pieces = MeshClip.ClipOutsideBoxes(PV.Original(i, p[i]), PV.Original(j, p[j]), PV.Original(k, p[k]), mins, maxs);
                left += pieces == null ? TriArea(p[i], p[j], p[k]) : Area(pieces);
            }
            Assert.Equal(16f - 1f, left, 3);
        }

        [Fact]
        public void SeveralBoxesRemoveTheirUnion()
        {
            var p = new[] { new Vector3(0, 0.5f, 0), new Vector3(4, 0.5f, 0), new Vector3(4, 0.5f, 4), new Vector3(0, 0.5f, 4) };
            // two adjacent cells and one overlapping both halves: union = 2 + 0 extra
            var mins = new[] { new Vector3(1, 0, 1), new Vector3(2, 0, 1), new Vector3(1.5f, 0, 1) };
            var maxs = new[] { new Vector3(2, 1, 2), new Vector3(3, 1, 2), new Vector3(2.5f, 1, 2) };
            float left = 0;
            foreach (var (i, j, k) in new[] { (0, 1, 2), (0, 2, 3) })
            {
                var pieces = MeshClip.ClipOutsideBoxes(PV.Original(i, p[i]), PV.Original(j, p[j]), PV.Original(k, p[k]), mins, maxs);
                left += pieces == null ? TriArea(p[i], p[j], p[k]) : Area(pieces);
            }
            Assert.Equal(16f - 2f, left, 3);
        }

        [Fact]
        public void WallCrossingABoxKeepsOnlyTheOutsidePart()
        {
            // vertical wall in the x=0.5 plane spanning y,z 0..2; a unit cell at the origin cuts a 1x1 window out of it
            var a = new Vector3(0.5f, 0, 0); var b = new Vector3(0.5f, 2, 0); var c = new Vector3(0.5f, 0, 2); var d = new Vector3(0.5f, 2, 2);
            var mins = new[] { Vector3.zero }; var maxs = new[] { Vector3.one };
            float left = Area(MeshClip.ClipOutsideBoxes(PV.Original(0, a), PV.Original(1, b), PV.Original(2, d), mins, maxs) ?? new List<List<PV>>())
                       + Area(MeshClip.ClipOutsideBoxes(PV.Original(0, a), PV.Original(2, d), PV.Original(3, c), mins, maxs) ?? new List<List<PV>>());
            Assert.Equal(4f - 1f, left, 3);
        }

        [Fact]
        public void ClippedVerticesInterpolateTheirSources()
        {
            var (a, b, c) = Tri(new Vector3(0, 0.5f, 0), new Vector3(2, 0.5f, 0), new Vector3(0, 0.5f, 2));
            var pieces = MeshClip.ClipOutsideBoxes(a, b, c, new[] { new Vector3(-1, 0, -1) }, new[] { new Vector3(1, 1, 3) });
            foreach (var poly in pieces)
                foreach (var v in poly)
                {
                    var w = new Dictionary<int, float>();
                    MeshClip.Accumulate(v, 1f, w);
                    Assert.Equal(1f, w.Values.Sum(), 4);
                    // the weighted source positions reproduce the vertex
                    var src = new[] { a.W, b.W, c.W };
                    var pos = w.Aggregate(Vector3.zero, (acc, kv) => acc + src[kv.Key] * kv.Value);
                    Assert.True((pos - v.W).magnitude < 1e-4f);
                }
        }
    }

    // ------------------------------------------------------------------ ground solidity / materials
    public class GroundRulesTests
    {
        static H[] Hits(H down, H up, H n = H.None, H s = H.None, H w = H.None, H e = H.None) => new[] { down, up, n, s, w, e };

        [Fact] public void OpenSkyIsAir() => Assert.False(GroundRules.IsSolid(Hits(H.Front, H.None)));
        [Fact] public void UnderTerrainIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.None, H.Back)));
        [Fact] public void UnderTerrainAboveTheFacilityIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.Back, H.Back)));
        [Fact] public void InsideARoomIsAir() => Assert.False(GroundRules.IsSolid(Hits(H.Front, H.Front, H.Front, H.Front, H.Front, H.Front)));
        [Fact] public void RoomWithAnOpenDoorwayIsAir() => Assert.False(GroundRules.IsSolid(Hits(H.Front, H.Front, H.Front, H.None, H.Front, H.Front)));
        [Fact] public void RoomWithAHoleInTheCeilingIsStillAir() => Assert.False(GroundRules.IsSolid(Hits(H.Front, H.Back, H.Front, H.Front, H.Front, H.Front)));
        [Fact] public void InsideAWallBetweenRoomsIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.None, H.Back, H.Back, H.Back, H.None, H.None)));
        [Fact] public void BetweenRoomsWithOuterFacesIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.None, H.Back, H.Front, H.Front, H.None, H.None)));
        [Fact] public void BelowTheFacilityFloorIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.None, H.Front)));
        [Fact] public void UnderAnOverhangIsAir() => Assert.False(GroundRules.IsSolid(Hits(H.Front, H.Front)));
        [Fact] public void CatwalkRoomWithAPitBelowIsAir() => Assert.False(GroundRules.IsSolid(Hits(H.None, H.Front, H.Front, H.Front, H.Front, H.Front)));
        [Fact] public void UnderARoofOverTheVoidIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.None, H.Front, H.None, H.None, H.None, H.None)));
        [Fact] public void UnderTerrainNextToBuriedRocksIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.Front, H.Back, H.Front, H.None, H.Front, H.Front)));
        [Fact] public void InsideARockIsSolid() => Assert.True(GroundRules.IsSolid(Hits(H.Back, H.Back, H.Back, H.Back, H.Back, H.Back)));

        [Theory]
        [InlineData(true, 5f, false, false, false, GroundRules.Layer.Top)]
        [InlineData(false, 0.2f, false, false, false, GroundRules.Layer.Top)]
        [InlineData(false, 1.5f, false, false, false, GroundRules.Layer.Soil)]
        [InlineData(false, 1.5f, false, true, false, GroundRules.Layer.Soil)]   // snow on top, dirt underneath
        [InlineData(false, 0.3f, false, true, false, GroundRules.Layer.Top)]
        [InlineData(false, 1.0f, true, false, false, GroundRules.Layer.Stone)]
        [InlineData(false, 1.0f, false, false, true, GroundRules.Layer.Planks)]
        [InlineData(false, 3.0f, false, false, false, GroundRules.Layer.Stone)]
        [InlineData(false, 40f, false, true, false, GroundRules.Layer.Stone)]
        public void Layers(bool partial, float depth, bool stony, bool snow, bool planks, GroundRules.Layer expect)
            => Assert.Equal(expect, GroundRules.LayerFor(partial, depth, stony, snow, planks));

        // (#220) the ground at a fixed depth below the surface, the default chance, this seed
        static void SetGround(float depth, int seed = 7, float chance = 0.15f, float moon = 1f)
        {
            GroundVeins.DepthAt = (x, y, z) => depth;
            GroundVeins.Seed = seed; GroundVeins.VeinChance = chance; GroundVeins.MoonMultiplier = moon;
            GroundVeins.Reset();
        }

        static bool IsOre(GroundRules.Ore o) => GroundVeins.Ores.Any(k => k.Ore == o);

        /// <summary>The ore cells (not lava or stone patches) of each 4x4x4 chunk in a cube of n chunks a side.</summary>
        static List<List<GroundRules.Ore>> Chunks(int n)
        {
            var all = new List<List<GroundRules.Ore>>();
            for (int rx = 0; rx < n; rx++) for (int ry = -n; ry < 0; ry++) for (int rz = 0; rz < n; rz++)
            {
                var cells = new List<GroundRules.Ore>();
                for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) for (int z = 0; z < 4; z++)
                {
                    var o = GroundRules.OreFor(rx * 4 + x, ry * 4 + y, rz * 4 + z);
                    if (IsOre(o)) cells.Add(o);
                }
                all.Add(cells);
            }
            return all;
        }

        [Fact]
        public void OresAreDeterministic()
        {
            SetGround(40f);
            var first = Enumerable.Range(0, 2000).Select(i => GroundRules.OreFor(i, -i * 3, i * 7)).ToList();
            GroundVeins.Reset();
            Assert.Equal(first, Enumerable.Range(0, 2000).Select(i => GroundRules.OreFor(i, -i * 3, i * 7)).ToList());
        }

        [Fact]
        public void TheSeedChangesTheVeins()
        {
            SetGround(40f, seed: 1);
            var a = Chunks(8).Select(c => c.Count).ToList();
            SetGround(40f, seed: 2);
            var b = Chunks(8).Select(c => c.Count).ToList();
            Assert.True(a.Zip(b, (x, y) => x != y).Count(d => d) > a.Count / 10);
        }

        [Fact]
        public void VeinSizesStayInRangeWithTheirAverage()
        {
            foreach (var k in GroundVeins.All)
            {
                double sum = 0; int n = 2000;
                for (int i = 0; i < n; i++)
                {
                    int s = GroundVeins.Size(k, (i + 0.5f) / n);
                    Assert.InRange(s, k.Min, k.Max);
                    sum += s;
                }
                Assert.InRange(sum / n, k.Avg - 0.4, k.Avg + 0.4);
            }
        }

        [Fact]
        public void AChunkHoldsOneOreVeinAtMost()
        {
            SetGround(40f);
            var chunks = Chunks(16);
            foreach (var c in chunks)
            {
                if (c.Count == 0) continue;
                Assert.Single(c.Distinct());
                Assert.InRange(c.Count, 1, GroundVeins.Ores.Find(k => k.Ore == c[0]).Max);
            }
            // about the configured share of chunks has one; the moon's multiplier scales it
            float share = chunks.Count(c => c.Count > 0) / (float)chunks.Count;
            Assert.InRange(share, 0.12f, 0.18f);
            SetGround(40f, moon: 2f);
            float doubled = Chunks(16).Count(c => c.Count > 0) / (float)chunks.Count;
            Assert.InRange(doubled, 0.26f, 0.34f);
        }

        [Fact]
        public void IronComesInVeinsOfTwoToNine()
        {
            SetGround(10f);
            var iron = new HashSet<(int, int, int)>();
            for (int x = 0; x < 64; x++) for (int y = -64; y < 0; y++) for (int z = 0; z < 64; z++)
                if (GroundRules.OreFor(x, y, z) == GroundRules.Ore.Iron) iron.Add((x, y, z));
            Assert.NotEmpty(iron);
            var sizes = new List<int>();
            var seen = new HashSet<(int, int, int)>();
            foreach (var c in iron)
            {
                if (!seen.Add(c)) continue;
                int n = 0; var st = new Stack<(int, int, int)>(); st.Push(c);
                while (st.Count > 0)
                {
                    var (x, y, z) = st.Pop(); n++;
                    foreach (var nb in new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) })
                        if (iron.Contains(nb) && seen.Add(nb)) st.Push(nb);
                }
                sizes.Add(n);
            }
            // (two veins in neighbouring chunks can touch: a few bigger clumps, but most are one vein)
            Assert.True(sizes.Count(s => s >= 2 && s <= 9) >= sizes.Count * 0.85, string.Join(",", sizes));
            Assert.InRange(sizes.Average(), 3.5, 6.0);
        }

        [Theory]
        [InlineData(GroundRules.Ore.Redstone, 15f)]
        [InlineData(GroundRules.Ore.Gold, 25f)]
        [InlineData(GroundRules.Ore.Lapis, 30f)]
        [InlineData(GroundRules.Ore.Diamond, 30f)]
        [InlineData(GroundRules.Ore.Emerald, 30f)]
        public void DeepOresOnlyPastTheirMinDepth(GroundRules.Ore ore, float minDepth)
        {
            SetGround(minDepth - 1f, chance: 1f);
            Assert.DoesNotContain(Chunks(12).SelectMany(c => c), o => o == ore);
            SetGround(minDepth, chance: 1f);
            Assert.Contains(Chunks(12).SelectMany(c => c), o => o == ore);
        }

        [Fact]
        public void DiamondsRarerThanIronDeepDown()
        {
            SetGround(999f);
            var cells = Chunks(16).SelectMany(c => c).ToList();
            int d = cells.Count(o => o == GroundRules.Ore.Diamond), iron = cells.Count(o => o == GroundRules.Ore.Iron);
            Assert.True(d > 0 && d * 3 < iron, $"diamond {d} iron {iron}");
        }

        [Fact]
        public void LavaIsALayerOfItsOwnAndCanTouchDiamonds()
        {
            SetGround(19f);
            int lava19 = 0;
            for (int x = 0; x < 64; x++) for (int y = -64; y < 0; y++) for (int z = 0; z < 64; z++)
                if (GroundRules.OreFor(x, y, z) == GroundRules.Ore.Lava) lava19++;
            Assert.Equal(0, lava19);
            SetGround(40f, chance: 1f);
            bool touching = false;
            for (int x = 0; x < 64 && !touching; x++) for (int y = -64; y < 0; y++) for (int z = 0; z < 64; z++)
                if (GroundRules.OreFor(x, y, z) == GroundRules.Ore.Diamond &&
                    new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) }.Any(n => GroundRules.OreFor(n.Item1, n.Item2, n.Item3) == GroundRules.Ore.Lava))
                { touching = true; break; }
            Assert.True(touching);
        }
    }

    // ------------------------------------------------------------------ blocks, tools, recipes
    public class DataTests
    {
        static DataTests() { Blocks.Init(); Crafting.Init(); }

        static readonly string Src = FindSrc();
        static string FindSrc()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "LethalMinecraft.csproj"))) dir = Path.GetDirectoryName(dir);
            return Path.Combine(dir, "src");
        }

        /// <summary>Every item key the mod registers: blocks, plus tools/resources/food/scrap parsed from Items.cs.</summary>
        static HashSet<string> KnownItems()
        {
            var keys = new HashSet<string>(Blocks.All.Select(b => b.Key));
            var items = File.ReadAllText(Path.Combine(Src, "Items.cs"));
            foreach (Match m in Regex.Matches(items, "\\(\"([a-z_]+)\", \"[^\"]+\", ToolKind\\.")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(items, "AddResource\\(\"([a-z_]+)\"")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(items, "Key = \"([a-z_]+)\"")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(items, "MakeItem\\(\"([a-z_]+)\"")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(items, "\\(\"([a-z_]+_sword)\", \"")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(items, "\\(\"([a-z_]+)\", \"[^\"]+\", (?:true|false)\\)")) keys.Add(m.Groups[1].Value); // (the buckets)
            if (items.Contains("MakeItem(Elytra.RocketKey")) keys.Add(Elytra.RocketKey); // (the firework rocket, #61)
            foreach (var k in Armor.Defs.Keys) keys.Add(k);
            foreach (var k in Potions.Defs.Keys) keys.Add(k); // (#88)
            if (items.Contains("BlazeRod.Key")) keys.Add(BlazeRod.Key);
            if (items.Contains("AddSold(Bow.ArrowKey")) keys.Add("arrow"); // (bows)
            if (items.Contains("AddSold(Bow.Key")) keys.Add("bow");
            if (items.Contains("AddResource(Fishing.RodKey")) keys.Add("fishing_rod");
            if (items.Contains("AddResource(Smithing.TemplateKey")) keys.Add(Smithing.TemplateKey); // (#159)
            if (items.Contains("AddBooks();")) { keys.Add(Books.Writable); keys.Add(Books.Written); } // (#160)
            foreach (var b in Blocks.All.Where(b => b.ScrapValueMin > 0)) keys.Add("scrap_" + b.Key);
            return keys;
        }

        [Fact]
        public void BlockIdsAndKeysAreUnique()
        {
            Assert.Equal(Blocks.All.Count, Blocks.All.Select(b => b.Id).Distinct().Count());
            Assert.Equal(Blocks.All.Count, Blocks.All.Select(b => b.Key).Distinct().Count());
            Assert.All(Blocks.All, b => Assert.Same(b, Blocks.Get(b.Key)));
        }

        [Fact]
        public void BedrockIsUnbreakable()
        {
            Assert.True(Blocks.Bedrock.Unbreakable);
            Assert.True(float.IsPositiveInfinity(Blocks.BreakTime(Blocks.Bedrock, ToolKind.Pickaxe, 3, 8f)));
        }

        [Fact]
        public void RightToolIsFasterAndHarvestTiersHold()
        {
            Assert.True(Blocks.BreakTime(Blocks.Stone, ToolKind.Pickaxe, 1, 4f) < Blocks.BreakTime(Blocks.Stone, ToolKind.None, 0, 1f));
            Assert.True(Blocks.BreakTime(Blocks.Dirt, ToolKind.Shovel, 1, 4f) < Blocks.BreakTime(Blocks.Dirt, ToolKind.Pickaxe, 1, 4f));
            Assert.True(Blocks.BreakTime(Blocks.Planks, ToolKind.Axe, 1, 4f) < Blocks.BreakTime(Blocks.Planks, ToolKind.None, 0, 1f));
            Assert.False(Blocks.CanHarvest(Blocks.Stone, ToolKind.None, 0));
            // Minecraft's levels: 1 wood, 2 stone, 3 iron, 4 diamond
            Assert.True(Blocks.CanHarvest(Blocks.Stone, ToolKind.Pickaxe, 1));
            Assert.True(Blocks.CanHarvest(Blocks.CoalOre, ToolKind.Pickaxe, 1));
            Assert.False(Blocks.CanHarvest(Blocks.IronOre, ToolKind.Pickaxe, 1));
            Assert.True(Blocks.CanHarvest(Blocks.IronOre, ToolKind.Pickaxe, 2));
            Assert.False(Blocks.CanHarvest(Blocks.DiamondOre, ToolKind.Pickaxe, 2));
            Assert.True(Blocks.CanHarvest(Blocks.DiamondOre, ToolKind.Pickaxe, 3));
            Assert.False(Blocks.CanHarvest(Blocks.Obsidian, ToolKind.Pickaxe, 3));
            Assert.True(Blocks.CanHarvest(Blocks.Obsidian, ToolKind.Pickaxe, 4));
            Assert.False(Blocks.CanHarvest(Blocks.DiamondOre, ToolKind.Shovel, 4));
            Assert.True(Blocks.CanHarvest(Blocks.Dirt, ToolKind.None, 0));
        }

        [Fact]
        public void EveryRecipeUsesAndMakesRealItems()
        {
            var known = KnownItems();
            Assert.NotEmpty(Crafting.Recipes);
            foreach (var r in Crafting.Recipes)
            {
                Assert.True(known.Contains(r.Result), $"recipe result '{r.Result}' is not an item");
                Assert.True(r.Count > 0);
                Assert.NotEmpty(r.Needs);
                foreach (var (key, n) in r.Needs)
                {
                    Assert.True(known.Contains(key), $"recipe for {r.Result} needs unknown item '{key}'");
                    Assert.True(n > 0);
                }
            }
        }

        [Fact]
        public void ToolProgressionIsReachable()
        {
            // wooden pickaxe from planks (from a log), stone pickaxe from cobblestone, then smelt mined iron
            Assert.Contains(Crafting.Recipes, r => r.Result == "oak_planks" && r.Needs.Any(n => n.key == "oak_log"));
            Assert.Contains(Crafting.Recipes, r => r.Result == "wooden_pickaxe" && r.Needs.Any(n => n.key == "oak_planks"));
            Assert.Contains(Crafting.Recipes, r => r.Result == "stone_pickaxe" && r.Needs.Any(n => n.key == "cobblestone"));
            Assert.Contains(Crafting.Recipes, r => r.Result == "iron_pickaxe" && r.Needs.Any(n => n.key == "iron_ingot"));
            Assert.Contains(Crafting.SmeltResult, kv => kv.Value == "iron_ingot");
            Assert.True(Crafting.FuelSeconds.ContainsKey("coal"));
            Assert.Equal("coal", Blocks.CoalOre.DropKey);
        }

        [Fact]
        public void SmeltingTablesUseRealItems()
        {
            var known = KnownItems();
            foreach (var kv in Crafting.SmeltResult)
            {
                Assert.True(known.Contains(kv.Key), $"smelt input '{kv.Key}' unknown");
                Assert.True(known.Contains(kv.Value), $"smelt output '{kv.Value}' unknown");
            }
            foreach (var k in Crafting.FuelSeconds.Keys) Assert.True(known.Contains(k), $"fuel '{k}' unknown");
        }

        [Fact]
        public void BrewingTablesUseRealItems()
        {
            var known = KnownItems();
            foreach (var kv in Potions.Brews)
            {
                Assert.True(known.Contains(kv.Key.potion), $"brew input '{kv.Key.potion}' unknown");
                Assert.True(known.Contains(kv.Key.ingredient), $"brew ingredient '{kv.Key.ingredient}' unknown");
                Assert.True(known.Contains(kv.Value), $"brew output '{kv.Value}' unknown");
            }
            // water to awkward to each potion, and a glass bottle and a brewing stand to make
            Assert.Equal("potion_awkward", Potions.BrewOf("potion_water", "nether_wart"));
            Assert.Equal("potion_swiftness_long", Potions.BrewOf(Potions.BrewOf("potion_awkward", "sugar"), "redstone_dust"));
            Assert.Contains(Crafting.Recipes, r => r.Result == "brewing_stand" && r.Needs.Any(n => n.key == "blaze_rod"));
            Assert.Contains(Crafting.Recipes, r => r.Result == "glass_bottle");
        }

        [Fact]
        public void PocketRecipesAreTwoByTwoSized()
        {
            foreach (var r in Crafting.Recipes.Where(r => r.Pocket))
                Assert.True(r.Needs.Sum(n => n.n) <= 4, $"{r.Result} is a pocket recipe but needs more than 4 items");
        }

        [Fact]
        public void FacesAreConsistent()
        {
            for (byte f = 0; f < 6; f++)
            {
                Assert.Equal(-Faces.Dir[f], Faces.Dir[Faces.Opposite(f)]);
                Assert.Equal(f, Faces.FromVector(Faces.Dir[f]));
            }
        }

        [Fact]
        public void BlockKeysHashAndCompare()
        {
            var a = new BlockKey(0, 0, new Vector3Int(1, 2, 3));
            var b = new BlockKey(0, 0, new Vector3Int(1, 2, 3));
            var c = new BlockKey(0, 500, new Vector3Int(1, 2, 3));
            var d = new BlockKey(1, 0, new Vector3Int(1, 2, 3));
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, c);
            Assert.NotEqual(a, d);
            Assert.Equal(new Vector3Int(1, 3, 3), a.Offset((int)Face.Up).Pos);
        }
    }

    // ------------------------------------------------------------------ crafting grid matching
    public class RecipeMatchTests
    {
        static RecipeMatchTests() { Blocks.Init(); Crafting.Init(); }

        static string[] G(int size, params string[] cells) { var g = new string[size * size]; for (int i = 0; i < cells.Length; i++) g[i] = cells[i] == "." ? null : cells[i]; return g; }
        static string Make(int size, params string[] cells) => RecipeBook.Match(Crafting.Recipes, G(size, cells), size)?.Result;

        const string C = "cobblestone", St = "stick", P = "oak_planks", I = "iron_ingot";

        [Fact] public void StonePickaxe() => Assert.Equal("stone_pickaxe", Make(3, C, C, C, ".", St, ".", ".", St, "."));
        // #217: Minecraft's stone_tool_materials / stone_crafting_materials: cobbled deepslate and blackstone too, mixed
        [Fact] public void StonePickaxeFromDeepslateAndBlackstoneMixed() => Assert.Equal("stone_pickaxe", Make(3, "cobbled_deepslate", C, "blackstone", ".", St, ".", ".", St, "."));
        [Fact] public void StoneSwordFromDeepslate() => Assert.Equal("stone_sword", Make(3, "cobbled_deepslate", ".", ".", "cobbled_deepslate", ".", ".", St, ".", "."));
        [Fact] public void FurnaceFromMixedStone() => Assert.Equal("furnace", Make(3, C, "cobbled_deepslate", C, "blackstone", ".", "cobbled_deepslate", C, C, "blackstone"));
        [Fact] public void BrewingStandFromBlackstone() => Assert.Equal("brewing_stand", Make(3, ".", "blaze_rod", ".", "blackstone", "blackstone", "blackstone", ".", ".", "."));
        [Fact] public void LeverIsCobblestoneOnly() => Assert.Null(Make(3, St, ".", ".", "cobbled_deepslate", ".", ".", ".", ".", "."));
        [Fact] public void PickaxeTakesNoOtherStone() => Assert.Null(Make(3, "stone", "stone", "stone", ".", St, ".", ".", St, "."));
        [Fact] public void PickaxeNeedsTheExactShape() => Assert.Null(Make(3, C, C, ".", ".", St, ".", ".", ".", St));
        // (two on top and the handle under the second: Minecraft's hoe, either way round, #60)
        [Fact] public void HoeAndItsMirror()
        {
            Assert.Equal("stone_hoe", Make(3, C, C, ".", ".", St, ".", ".", St, "."));
            Assert.Equal("stone_hoe", Make(3, ".", C, C, ".", St, ".", ".", St, "."));
        }
        [Fact] public void IronPickaxe() => Assert.Equal("iron_pickaxe", Make(3, I, I, I, ".", St, ".", ".", St, "."));
        [Fact] public void ShovelInAnyColumn() => Assert.Equal("stone_shovel", Make(3, ".", ".", C, ".", ".", St, ".", ".", St));
        [Fact] public void AxeAndItsMirror()
        {
            Assert.Equal("stone_axe", Make(3, C, C, ".", C, St, ".", ".", St, "."));
            Assert.Equal("stone_axe", Make(3, ".", C, C, ".", St, C, ".", St, "."));
        }
        // (#119: the diamond you mine is scrap_diamond_ore; the jukebox and the enchanting table only took the crafted one)
        [Fact] public void JukeboxFromAMinedDiamond()
        {
            Assert.Equal("jukebox", Make(3, P, P, P, P, "scrap_diamond_ore", P, P, P, P));
            Assert.Equal("jukebox", Make(3, P, P, P, P, "diamond", P, P, P, P));
        }
        // (#116: Minecraft's book, three paper and string here, and the enchanting table from it or a bookshelf)
        [Fact] public void BookIn2x2() => Assert.Equal("book", Make(2, "paper", "paper", "paper", "string"));
        [Fact] public void BookshelfFromBooks() => Assert.Equal("bookshelf", Make(3, P, P, P, "book", "book", "book", P, P, P));
        [Fact] public void EnchantingTable()
        {
            const string O = "obsidian";
            Assert.Equal("enchanting_table", Make(3, ".", "book", ".", "scrap_diamond_ore", O, "scrap_diamond_ore", O, O, O));
            Assert.Equal("enchanting_table", Make(3, ".", "bookshelf", ".", "diamond", O, "diamond", O, O, O));
        }
        // pumpkins (#130)
        // (recipe-only checks that were in-game tests: t_ore_blocks, t_farming, t_elytra, t_bows, t_block_families,
        // t_nether_mining, t_bone_meal, t_swords; the game tests keep the clicks, these keep the recipes)
        static string MakeN(int size, params string[] cells) { var r = RecipeBook.Match(Crafting.Recipes, G(size, cells), size); return r == null ? null : $"{r.Result}x{r.Count}"; }
        [Fact] public void OreBlocksBackIntoNine()
        {
            foreach (var (block, item) in new[] { ("iron_block", "iron_ingot"), ("diamond_block", "diamond"), ("coal_block", "coal") })
                Assert.Equal($"{item}x9", MakeN(3, ".", ".", ".", ".", block, ".", ".", ".", "."));
            Assert.Equal("coal_blockx1", MakeN(3, "coal", "coal", "coal", "coal", "coal", "coal", "coal", "coal", "coal"));
        }
        [Fact] public void DiamondTools()
        {
            const string D = "diamond";
            Assert.Equal("pickaxex1", MakeN(3, D, D, D, ".", St, ".", ".", St, "."));
            Assert.Equal("diamond_swordx1", MakeN(3, ".", D, ".", ".", D, ".", ".", St, "."));
        }
        [Fact] public void FarmRecipes()
        {
            Assert.Equal("breadx1", MakeN(3, "wheat", "wheat", "wheat", ".", ".", ".", ".", ".", "."));
            Assert.Equal("paperx3", MakeN(3, "sugar_cane", "sugar_cane", "sugar_cane", ".", ".", ".", ".", ".", "."));
            Assert.Equal("sugarx1", MakeN(3, "sugar_cane", ".", ".", ".", ".", ".", ".", ".", "."));
            Assert.Equal("bone_mealx3", MakeN(3, ".", ".", ".", ".", "bone", ".", ".", ".", "."));
        }
        [Fact] public void RocketsAndArrows()
        {
            Assert.Equal("firework_rocketx3", MakeN(3, "paper", "gunpowder", ".", ".", ".", ".", ".", ".", "."));
            Assert.Equal("arrowx4", MakeN(3, ".", "flint", ".", ".", St, ".", ".", "feather", "."));
        }
        [Fact] public void GraniteWalls() => Assert.Equal("granite_wallx6", MakeN(3, "granite", "granite", "granite", "granite", "granite", "granite", ".", ".", "."));
        [Fact] public void NetheriteIngotButNoUpgradeInTheGrid()
        {
            const string S = "netherite_scrap", Au = "gold_ingot";
            Assert.Equal("netherite_ingotx1", MakeN(3, S, S, Au, S, Au, Au, S, Au, "."));
            Assert.Null(MakeN(3, "pickaxe", "netherite_ingot", ".", ".", ".", ".", ".", ".", "."));  // (#159: the smithing table's)
        }
        // (#160: copper's waxed kinds, honeycomb, raw copper)
        [Fact] public void WaxingAndRawCopper()
        {
            Assert.Equal("waxed_exposed_cut_copper_stairsx1", MakeN(2, "exposed_cut_copper_stairs", "honeycomb", ".", "."));
            Assert.Equal("waxed_copper_blockx1", MakeN(2, "honeycomb", "copper_block", ".", "."));
            Assert.Equal("waxed_cut_copperx4", MakeN(2, "waxed_copper_block", "waxed_copper_block", "waxed_copper_block", "waxed_copper_block"));
            Assert.Equal("honeycomb_blockx1", MakeN(2, "honeycomb", "honeycomb", "honeycomb", "honeycomb"));
            Assert.Equal("raw_copper_blockx1", MakeN(3, "raw_copper", "raw_copper", "raw_copper", "raw_copper", "raw_copper", "raw_copper", "raw_copper", "raw_copper", "raw_copper"));
            Assert.Equal("raw_copperx9", MakeN(3, ".", ".", ".", ".", "raw_copper_block", ".", ".", ".", "."));
        }
        [Fact] public void CookedFood()
        {
            Assert.Equal("steak", Crafting.SmeltResult["raw_beef"]);
            Assert.Equal("porkchop", Crafting.SmeltResult["raw_porkchop"]);
            Assert.Equal("cooked_cod", Crafting.SmeltResult["raw_cod"]);
        }
        [Fact] public void ShearsDiagonal() => Assert.Equal("shears", Make(2, ".", I, I, "."));
        [Fact] public void PumpkinSeedsFromAPumpkin() => Assert.Equal("pumpkin_seeds", Make(2, "pumpkin", ".", ".", "."));
        [Fact] public void JackOLanternFromCarvedPumpkinAndTorch() => Assert.Equal("jack_o_lantern", Make(3, ".", "carved_pumpkin", ".", ".", "torch", ".", ".", ".", "."));
        [Fact] public void SticksFromTwoStackedPlanks() => Assert.Equal("stick", Make(2, P, ".", P, "."));
        // (two side by side: Minecraft's wooden pressure plate, #160; not sticks)
        [Fact] public void SideBySidePlanksAreAPressurePlate() => Assert.Equal("oak_pressure_plate", Make(2, P, P, ".", "."));
        [Fact] public void PlanksFromALogAnywhere()
        {
            Assert.Equal("oak_planks", Make(3, ".", ".", ".", ".", ".", ".", ".", ".", "oak_log"));
            Assert.Equal("oak_planks", Make(2, "oak_log", ".", ".", "."));
        }
        [Fact] public void TwoLogsMakeNothing() => Assert.Null(Make(2, "oak_log", "oak_log", ".", "."));
        [Fact] public void CraftingTableIn2x2() => Assert.Equal("crafting_table", Make(2, P, P, P, P));
        [Fact] public void FurnaceRing() => Assert.Equal("furnace", Make(3, C, C, C, C, ".", C, C, C, C));
        [Fact] public void FilledFurnaceRingIsNotAFurnace() => Assert.Null(Make(3, C, C, C, C, C, C, C, C, C));
        [Fact] public void Torch() => Assert.Equal("torch", Make(3, ".", "coal", ".", ".", St, ".", ".", ".", "."));
        [Fact] public void Piston() => Assert.Equal("piston", Make(3, P, P, P, C, I, C, C, "redstone_dust", C));
        [Fact] public void ShapelessInAnyOrder()
        {
            Assert.Equal("flint_and_steel", Make(2, I, ".", ".", "flint"));
            Assert.Equal("flint_and_steel", Make(2, "flint", I, ".", "."));
        }
        [Fact] public void EmptyGridMakesNothing() => Assert.Null(Make(3, ".", ".", ".", ".", ".", ".", ".", ".", "."));
        [Fact] public void ExtraItemsBreakTheRecipe() => Assert.Null(Make(3, C, C, C, ".", St, ".", ".", St, "dirt"));

        [Fact]
        public void PocketGridOnlyTakesSmallRecipes()
        {
            Assert.True(Crafting.Recipes.First(r => r.Result == "stick").Pocket);
            Assert.True(Crafting.Recipes.First(r => r.Result == "crafting_table").Pocket);
            Assert.False(Crafting.Recipes.First(r => r.Result == "stone_pickaxe").Pocket);
            Assert.False(Crafting.Recipes.First(r => r.Result == "furnace").Pocket);
        }

        [Fact]
        public void NoTwoRecipesClaimTheSameGrid()
        {
            // every recipe laid out at the top-left of a 3x3 grid matches itself and only itself
            foreach (var r in Crafting.Recipes)
            {
                var g = new string[9];
                if (r.Shapeless) { int i = 0; foreach (var (k, n) in r.Needs) for (int j = 0; j < n; j++) g[i++] = k; }
                else for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Pattern[y].Length; x++) { char ch = r.Pattern[y][x]; if (ch != ' ') g[y * 3 + x] = r.Map[ch]; }
                var matches = Crafting.Recipes.Where(o => RecipeBook.Matches(o, g, 3)).Select(o => o.Result).ToList();
                // (one result: a stone tool's grid matches each material's recipe, all of them that tool, #217)
                Assert.True(matches.Distinct().Count() == 1 && matches[0] == r.Result, $"{r.Result}: matches [{string.Join(",", matches)}]");
            }
        }
    }
}

namespace LethalMinecraft.Tests
{
    public class TerminalTextTests
    {
        static readonly string[] Ours = { "stonepickaxe", "stoneshovel", "stoneaxe", "stonebricks", "redstonetorch", "redstonedust", "oaklog", "oakplanks" };

        [Theory]
        [InlineData("buy stone pickaxe", "buy stonepickaxe")]
        [InlineData("buy stone pickaxe 3", "buy stonepickaxe 3")]
        [InlineData("buy Stone Pickaxe", "buy stonepickaxe")]
        [InlineData("buy stone pick", "buy stonepickaxe")]
        [InlineData("buy stone bricks 2", "buy stonebricks 2")]
        [InlineData("info redstone torch", "info redstonetorch")]
        [InlineData("buy oak log", "buy oaklog")]
        [InlineData("stone axe", "stoneaxe")]
        [InlineData("buy redstone torches 3", "buy redstonetorch 3")]
        [InlineData("buy stone pickaxes", "buy stonepickaxe")]
        [InlineData("buy oak logs", "buy oaklog")]
        public void MultiWordNamesAreJoined(string typed, string expected) => Assert.Equal(expected, TerminalText.JoinKeywords(typed, Ours));

        [Theory]
        [InlineData("buy stone")]
        [InlineData("buy stone 5")]
        [InlineData("buy shovel")]
        [InlineData("buy redstone")]       // ambiguous: torch or dust, left to the terminal
        [InlineData("buy oak")]
        [InlineData("moons")]
        public void OtherSentencesStayAsTyped(string typed) => Assert.Equal(typed, TerminalText.JoinKeywords(typed, Ours));

        static readonly string[] Words = { "buy", "walkie-talkie", "flashlight", "shovel", "pro-flashlight", "boombox", "bookshelf", "torch", "redstonetorch", "redstonedust", "cobblestonestairs", "cobblestone" };

        [Theory]
        [InlineData("bookshelves", "bookshelf")]   // #39: the game took boombox (first word sharing "boo")
        [InlineData("bookshelfs", "bookshelf")]
        [InlineData("bookshel", "bookshelf")]
        [InlineData("boomboxes", "boombox")]
        [InlineData("boo", "boombox")]             // a tie: the first, as the game does
        [InlineData("flashlights", "flashlight")]
        [InlineData("redstonetorches", "redstonetorch")]
        [InlineData("shovels", "shovel")]
        [InlineData("cobblestones", "cobblestone")]       // (not the start of "cobblestonestairs")
        [InlineData("cobblestonestairs", "cobblestonestairs")]
        [InlineData("cobblestonest", "cobblestonestairs")]
        public void ClosestWordWins(string typed, string expected) => Assert.Equal(expected, Words[TerminalText.BestPrefixMatch(typed, Words)]);

        [Theory]
        [InlineData("xyz")]
        [InlineData("bo")]      // too short (the game wants 3 letters)
        [InlineData("")]
        public void NoCloseWord(string typed) => Assert.Equal(-1, TerminalText.BestPrefixMatch(typed, Words));

        static readonly string[] NotSold = { "Stone Pickaxe", "Stone Shovel", "Iron Pickaxe", "Diamond Pickaxe", "Diamond", "Iron Ingot", "Wooden Pickaxe" };

        [Theory]
        [InlineData("buy stone pickaxe", "Stone", "Stone Pickaxe")]   // the terminal cut it down to Stone
        [InlineData("buy stone pickaxe 2", "Stone", "Stone Pickaxe")]
        [InlineData("buy stone pick", null, "Stone Pickaxe")]
        [InlineData("buy iron pickaxe", null, "Iron Pickaxe")]
        [InlineData("buy iron ingot", null, "Iron Ingot")]
        [InlineData("buy diamond", "Block of Diamond", "Diamond")]  // the exact name of a craft-only item
        [InlineData("buy wooden pickaxe", null, "Wooden Pickaxe")]
        public void CraftOnlyItemsAreCaught(string typed, string resolved, string expected) => Assert.Equal(expected, TerminalText.NotSoldMatch(typed, NotSold, resolved));

        [Theory]
        [InlineData("buy stone", "Stone")]
        [InlineData("buy stone 5", "Stone")]
        [InlineData("buy iron", "Block of Iron")]       // one word, not a full name: left to the terminal
        [InlineData("buy pickaxe", null)]
        [InlineData("buy stone", null)]
        [InlineData("buy shovel", "Shovel")]
        [InlineData("info stone pickaxe", null)]
        [InlineData("buy stone bricks", "Stone Bricks")]
        public void SoldOrUnclearOrdersAreLeftAlone(string typed, string resolved) => Assert.Null(TerminalText.NotSoldMatch(typed, NotSold, resolved));
    }
}

namespace LethalMinecraft.Tests
{
    public class MoldDataTests
    {
        const int R = 5;
        static byte[] Heights() { var h = new byte[R * R]; for (int i = 0; i < h.Length; i++) h[i] = (byte)(i * 7); return h; }

        [Fact]
        public void OldDataCountsAsExposedEverywhere()
        {
            var h = Heights();
            for (int f = 0; f < 6; f++) Assert.True(MoldData.IsExposed(h, R, f));
        }

        [Fact]
        public void NewShapesStartHiddenAndKeepTheirHeights()
        {
            var m = MoldData.FromHeights(Heights(), R);
            for (int f = 0; f < 6; f++) Assert.False(MoldData.IsExposed(m, R, f));
            Assert.Equal(Heights(), m.Take(R * R).ToArray());
        }

        [Fact]
        public void ExposingAFaceOnlyAddsThatFace()
        {
            var m = MoldData.WithExposed(MoldData.FromHeights(Heights(), R), R, 3);
            m = MoldData.WithExposed(m, R, 0);
            for (int f = 0; f < 6; f++) Assert.Equal(f == 3 || f == 0, MoldData.IsExposed(m, R, f));
            Assert.Equal(Heights(), m.Take(R * R).ToArray());
        }

        [Fact]
        public void ExposingTwiceReturnsTheSameData()
        {
            var m = MoldData.WithExposed(MoldData.FromHeights(Heights(), R), R, 2);
            Assert.Same(m, MoldData.WithExposed(m, R, 2));
        }
    }
}

namespace LethalMinecraft.Tests
{
    public class PistonStructureTests
    {
        static readonly Vector3Int E = Vector3Int.right;
        // a tiny world: blocks by position; "slime" cells are sticky, "rock" cells immovable, "torch" crushable
        static PistonStructure.Result Push(Dictionary<Vector3Int, string> world, Vector3Int start, Vector3Int dir, bool pull = false, params Vector3Int[] piston)
            => PistonStructure.Resolve(start, dir, piston.Length > 0 ? piston : new[] { start - dir },
                p => !world.TryGetValue(p, out var b) ? PistonStructure.Cell.Empty : b == "rock" ? PistonStructure.Cell.Immovable : b == "torch" ? PistonStructure.Cell.Crushable : PistonStructure.Cell.Movable,
                p => world.TryGetValue(p, out var b) && (b == "slime" || b == "honey"), pull,
                (a, b) => !(world.TryGetValue(a, out var x) && world.TryGetValue(b, out var y) && (x == "slime" && y == "honey" || x == "honey" && y == "slime")));

        static Vector3Int V(int x, int y = 0, int z = 0) => new Vector3Int(x, y, z);

        [Fact]
        public void HoneyIsStickyButNotToSlime()
        {
            // honey takes the stone on it along; the slime block beside it (not in the push line) stays
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "honey", [V(1, 1)] = "stone", [V(1, 0, 1)] = "slime" };
            var r = Push(w, V(1), E);
            Assert.True(r.Ok);
            Assert.Contains(V(1, 1), r.Move);
            Assert.DoesNotContain(V(1, 0, 1), r.Move);
            // slime beside slime does come along
            w[V(1)] = "slime";
            Assert.Contains(V(1, 0, 1), Push(w, V(1), E).Move);
        }

        [Fact]
        public void PushesALineFrontMostFirst()
        {
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "stone", [V(2)] = "stone", [V(3)] = "stone" };
            var r = Push(w, V(1), E);
            Assert.True(r.Ok);
            Assert.Equal(new[] { V(3), V(2), V(1) }, r.Move);
        }

        [Fact]
        public void BlockedByAnUnmovableBlock()
        {
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "stone", [V(2)] = "rock" };
            Assert.False(Push(w, V(1), E).Ok);
        }

        [Fact]
        public void BreaksATorchInTheWay()
        {
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "stone", [V(2)] = "torch" };
            var r = Push(w, V(1), E);
            Assert.True(r.Ok);
            Assert.Equal(new[] { V(2) }, r.Crush);
        }

        [Fact]
        public void AtMostTwelveBlocks()
        {
            var w = new Dictionary<Vector3Int, string>();
            for (int i = 1; i <= 12; i++) w[V(i)] = "stone";
            Assert.True(Push(w, V(1), E).Ok);
            w[V(13)] = "stone";
            Assert.False(Push(w, V(1), E).Ok);
        }

        [Fact]
        public void SlimeDragsBlocksTouchingItOnAnySide()
        {
            // slime pushed east with a block on top, one to the north and one behind it (west, not the piston)
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "slime", [V(1, 1)] = "stone", [V(1, 0, 1)] = "planks" };
            var r = Push(w, V(1), E, false, V(0, 0, 5)); // piston somewhere else (e.g. pushing from below in a real setup)
            w[V(0)] = "dirt";
            r = Push(w, V(1), E, false, V(-5));
            Assert.True(r.Ok);
            Assert.Equal(4, r.Move.Count);
            Assert.Contains(V(1, 1), r.Move); Assert.Contains(V(1, 0, 1), r.Move); Assert.Contains(V(0), r.Move);
        }

        [Fact]
        public void SlimeIgnoresUnmovableNeighboursAndThePiston()
        {
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "slime", [V(1, 1)] = "rock", [V(1, -1)] = "torch" };
            var r = Push(w, V(1), E); // piston at (0,0,0), touching the slime
            Assert.True(r.Ok);
            Assert.Equal(new[] { V(1) }, r.Move);
            Assert.Empty(r.Crush); // a torch beside the slime isn't in the way
        }

        [Fact]
        public void BlocksInTheWayOfDraggedBlocksMoveToo()
        {
            var w = new Dictionary<Vector3Int, string> { [V(1)] = "slime", [V(1, 1)] = "stone", [V(2, 1)] = "stone", [V(3, 1)] = "rock" };
            Assert.False(Push(w, V(1), E).Ok); // the dragged block's row is blocked by rock
        }

        [Fact]
        public void PullingTakesTheSlimeStructureAlong()
        {
            // sticky piston at x=0 facing east, head at x=1 (gone when retracting), slime at x=2 with a block on top
            var w = new Dictionary<Vector3Int, string> { [V(2)] = "slime", [V(2, 1)] = "stone" };
            var r = Push(w, V(2), Vector3Int.left, true, V(0));
            Assert.True(r.Ok);
            Assert.Equal(2, r.Move.Count);
        }

        [Fact]
        public void PullingNothingMovableIsFine()
        {
            var w = new Dictionary<Vector3Int, string> { [V(2)] = "rock" };
            var r = Push(w, V(2), Vector3Int.left, true, V(0));
            Assert.True(r.Ok);
            Assert.Empty(r.Move);
        }
    }
}

namespace LethalMinecraft.Tests
{
    public class TerminalAliasTests
    {
        static readonly string[] Ours = { "blockofredstone", "blockofiron", "redstonelamp", "redstonetorch", "flintandsteel", "stickypiston" };
        static readonly Dictionary<string, string> Al = new[] { "Block of Redstone", "Block of Iron" }
            .SelectMany(n => TerminalText.Aliases(n).Select(a => (a, TerminalText.Squash(n)))).ToDictionary(x => x.a, x => x.Item2);

        [Theory]
        [InlineData("buy redstone block", "buy blockofredstone")]
        [InlineData("buy redstone block 2", "buy blockofredstone 2")]
        [InlineData("buy block of redstone", "buy blockofredstone")]
        [InlineData("buy iron block", "buy blockofiron")]
        [InlineData("buy redstone lamp", "buy redstonelamp")]
        [InlineData("buy flint and steel", "buy flintandsteel")]
        [InlineData("buy sticky piston", "buy stickypiston")]
        public void AliasesReachTheRightItem(string typed, string expected) => Assert.Equal(expected, TerminalText.JoinKeywords(typed, Ours, Al));
    }

    /// <summary>#161: Minecraft 26.1's entity texture names, from the 1.21 ones the mod asks for.</summary>
    public class TextureRenameTests
    {
        static bool Has(string path, string renamed) => McAssets.Renamed(path).Contains(renamed);
        [Fact] public void OwnFolder() { Assert.True(Has("entity/blaze", "entity/blaze/blaze")); Assert.True(Has("entity/witch", "entity/witch/witch")); }
        [Fact] public void EyesInTheMobsFolder() { Assert.True(Has("entity/spider_eyes", "entity/spider/spider_eyes")); Assert.True(Has("entity/phantom_eyes", "entity/phantom/phantom_eyes")); }
        [Fact] public void VariantAfterTheName() { Assert.True(Has("entity/pig/temperate_pig", "entity/pig/pig_temperate")); Assert.True(Has("entity/chicken/temperate_chicken", "entity/chicken/chicken_temperate")); }
        [Fact] public void MobNameInFront() { Assert.True(Has("entity/cat/tabby", "entity/cat/cat_tabby")); Assert.True(Has("entity/llama/creamy", "entity/llama/llama_creamy")); Assert.True(Has("entity/rabbit/white_splotched", "entity/rabbit/rabbit_white_splotched")); }
        [Fact] public void NotForBlocksOrItems() { Assert.Empty(McAssets.Renamed("block/stone")); Assert.Empty(McAssets.Renamed("item/bow")); }
    }

    /// <summary>#159: the smithing table's netherite upgrade.</summary>
    public class SmithingTests
    {
        [Fact] public void TemplateDiamondPickaxeIngot() => Assert.Equal("netherite_pickaxe", Smithing.Result(Smithing.TemplateKey, "pickaxe", "netherite_ingot"));
        [Fact] public void ArmorToo() => Assert.Equal("netherite_chestplate", Smithing.Result(Smithing.TemplateKey, "diamond_chestplate", "netherite_ingot"));
        [Fact] public void NoTemplateNothing() => Assert.Null(Smithing.Result(null, "pickaxe", "netherite_ingot"));
        [Fact] public void IronToolsNotUpgraded() => Assert.Null(Smithing.Result(Smithing.TemplateKey, "iron_pickaxe", "netherite_ingot"));
    }
}
