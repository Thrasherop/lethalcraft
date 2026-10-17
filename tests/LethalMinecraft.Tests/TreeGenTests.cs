using System.Linq;
using LethalMinecraft;
using UnityEngine;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>(#223) Minecraft's trees from a sapling: TreeGen.</summary>
    public class TreeGenTests
    {
        // open ground: solid below y 0, air above
        static TreeGen.Cell Flat(Vector3Int p) => p.y < 0 ? TreeGen.Cell.Solid : TreeGen.Cell.Air;

        static int TrunkHeight(System.Collections.Generic.List<TreeGen.Placed> t, Vector3Int at) =>
            t.Count(b => b.Pos.x == at.x && b.Pos.z == at.z && b.Part == TreeGen.Part.Log);

        [Fact]
        public void BirchIsFiveToSevenLogsUnderBlobLeaves()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var t = TreeGen.Grow("birch", Vector3Int.zero, new System.Random(seed), Flat);
                Assert.NotNull(t);
                int h = TrunkHeight(t, Vector3Int.zero);
                Assert.InRange(h, 5, 7);
                Assert.Contains(t, b => b.Part == TreeGen.Part.Leaves && b.Pos == new Vector3Int(0, h, 0)); // (the top of the blob, over the trunk)
                Assert.DoesNotContain(t, b => b.Pos.y < 0);
                // (BlobFoliagePlacer: the top row's corners are always left out)
                Assert.DoesNotContain(t, b => b.Part == TreeGen.Part.Leaves && b.Pos.y == h && System.Math.Abs(b.Pos.x) == 1 && System.Math.Abs(b.Pos.z) == 1);
            }
        }

        [Fact]
        public void OakIsMostlyFourToSixAndSometimesFancy()
        {
            int fancy = 0;
            for (int seed = 0; seed < 400; seed++)
            {
                var t = TreeGen.Grow("oak", Vector3Int.zero, new System.Random(seed), Flat);
                Assert.NotNull(t);
                bool branches = t.Any(b => b.Part == TreeGen.Part.LogX || b.Part == TreeGen.Part.LogZ);
                int h = TrunkHeight(t, Vector3Int.zero);
                if (h > 6 || branches) fancy++;
                else Assert.InRange(h, 4, 6);
            }
            Assert.InRange(fancy, 10, 80); // (about one in ten)
        }

        [Fact]
        public void DarkOakHasATwoByTwoTrunk()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var t = TreeGen.Grow("dark_oak", Vector3Int.zero, new System.Random(seed), Flat);
                Assert.NotNull(t);
                foreach (var p in new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1) })
                    Assert.Contains(t, b => b.Pos == p && b.Part == TreeGen.Part.Log);
                Assert.True(t.Count(b => b.Part == TreeGen.Part.Leaves) > 30);
            }
        }

        [Fact]
        public void MangroveStandsOnRootsThatReachTheGround()
        {
            int grew = 0;
            for (int seed = 0; seed < 100; seed++)
            {
                var t = TreeGen.Grow("mangrove", Vector3Int.zero, new System.Random(seed), Flat);
                // (Minecraft's root placer gives up on a root longer than 15: that try fails, and the propagule tries again later)
                if (t == null) continue;
                grew++;
                Assert.Contains(t, b => b.Part == TreeGen.Part.Roots && b.Pos.y == 0); // (down to the ground)
                Assert.DoesNotContain(t, b => b.Pos == Vector3Int.zero && b.Part == TreeGen.Part.Log); // (the trunk starts above its roots)
                Assert.DoesNotContain(t, b => b.Pos.y < 0);
            }
            Assert.True(grew > 60, $"grew {grew} of 100");
        }

        [Fact]
        public void MangroveRootsInMudAreMuddy()
        {
            var t = TreeGen.Grow("mangrove", Vector3Int.zero, new System.Random(3), p => p.y < -2 ? TreeGen.Cell.Solid : p.y < 0 ? TreeGen.Cell.Mud : TreeGen.Cell.Air);
            Assert.NotNull(t);
            Assert.Contains(t, b => b.Part == TreeGen.Part.MuddyRoots && b.Pos.y < 0);
        }

        [Fact]
        public void NoRoomNoTree()
        {
            // a block two over the sapling: no tree can grow (Minecraft's: the sapling waits)
            TreeGen.Cell Roofed(Vector3Int p) => p.y < 0 || (p.y == 2 && p.x == 0 && p.z == 0) ? TreeGen.Cell.Solid : TreeGen.Cell.Air;
            for (int seed = 0; seed < 50; seed++) Assert.Null(TreeGen.Grow("birch", Vector3Int.zero, new System.Random(seed), Roofed));
        }

        [Fact]
        public void LeavesNeverGoIntoSolidCells()
        {
            // a wall beside the trunk
            TreeGen.Cell Wall(Vector3Int p) => p.y < 0 || p.x == 2 ? TreeGen.Cell.Solid : TreeGen.Cell.Air;
            for (int seed = 0; seed < 100; seed++)
            {
                var t = TreeGen.Grow("oak", Vector3Int.zero, new System.Random(seed), Wall);
                if (t != null) Assert.DoesNotContain(t, b => b.Pos.x == 2);
            }
        }
    }
}
