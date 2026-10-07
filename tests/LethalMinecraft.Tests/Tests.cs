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

        [Fact]
        public void OresAreDeterministic()
        {
            for (int i = 0; i < 200; i++)
                Assert.Equal(GroundRules.OreFor(i, -i * 3, i * 7, 10f), GroundRules.OreFor(i, -i * 3, i * 7, 10f));
        }

        static Dictionary<GroundRules.Ore, int> Census(float depth)
        {
            var d = Enum.GetValues(typeof(GroundRules.Ore)).Cast<GroundRules.Ore>().ToDictionary(o => o, o => 0);
            for (int x = 0; x < 40; x++) for (int y = -60; y < 0; y++) for (int z = 0; z < 40; z++) d[GroundRules.OreFor(x, y, z, depth)]++;
            return d;
        }

        [Fact]
        public void OreRatesAreSane()
        {
            var shallow = Census(2f); var deep = Census(30f);
            int n = 40 * 60 * 40;
            Assert.InRange(shallow[GroundRules.Ore.Coal] / (float)n, 0.035f, 0.065f);
            Assert.Equal(0, shallow[GroundRules.Ore.Diamond]);   // no diamonds right next to open space
            Assert.Equal(0, shallow[GroundRules.Ore.Emerald]);
            Assert.True(deep[GroundRules.Ore.Diamond] > 0);
            Assert.True(deep[GroundRules.Ore.Diamond] < deep[GroundRules.Ore.Iron]);
            Assert.True(deep[GroundRules.Ore.Gold] + deep[GroundRules.Ore.Diamond] > shallow[GroundRules.Ore.Gold] + shallow[GroundRules.Ore.Diamond]);
            Assert.InRange(deep[GroundRules.Ore.None] / (float)n, 0.85f, 0.95f);
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
        [Fact] public void PickaxeNeedsTheExactShape() => Assert.Null(Make(3, C, C, ".", ".", St, ".", ".", St, "."));
        [Fact] public void IronPickaxe() => Assert.Equal("iron_pickaxe", Make(3, I, I, I, ".", St, ".", ".", St, "."));
        [Fact] public void ShovelInAnyColumn() => Assert.Equal("stone_shovel", Make(3, ".", ".", C, ".", ".", St, ".", ".", St));
        [Fact] public void AxeAndItsMirror()
        {
            Assert.Equal("stone_axe", Make(3, C, C, ".", C, St, ".", ".", St, "."));
            Assert.Equal("stone_axe", Make(3, ".", C, C, ".", St, C, ".", St, "."));
        }
        [Fact] public void SticksFromTwoStackedPlanks() => Assert.Equal("stick", Make(2, P, ".", P, "."));
        [Fact] public void SideBySidePlanksAreNotSticks() => Assert.Null(Make(2, P, P, ".", "."));
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
            Assert.Equal("flint_and_steel", Make(2, I, ".", ".", "gravel"));
            Assert.Equal("flint_and_steel", Make(2, "gravel", I, ".", "."));
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
                Assert.True(matches.Count == 1 && matches[0] == r.Result, $"{r.Result}: matches [{string.Join(",", matches)}]");
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
        public void MultiWordNamesAreJoined(string typed, string expected) => Assert.Equal(expected, TerminalText.JoinKeywords(typed, Ours));

        [Theory]
        [InlineData("buy stone")]
        [InlineData("buy stone 5")]
        [InlineData("buy shovel")]
        [InlineData("buy redstone")]       // ambiguous: torch or dust, left to the terminal
        [InlineData("buy oak")]
        [InlineData("moons")]
        public void OtherSentencesStayAsTyped(string typed) => Assert.Equal(typed, TerminalText.JoinKeywords(typed, Ours));
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
                p => world.TryGetValue(p, out var b) && b == "slime", pull);

        static Vector3Int V(int x, int y = 0, int z = 0) => new Vector3Int(x, y, z);

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
