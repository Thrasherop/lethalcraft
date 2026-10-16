using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LethalMinecraft;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>(#223) The pillager outpost's watchtower, read from the installed Minecraft (skipped without it).</summary>
    public class OutpostTests
    {
        static byte[] FromJar(string path)
        {
            var jar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions", "26.3", "26.3.jar");
            if (!File.Exists(jar)) return null;
            using (var z = ZipFile.OpenRead(jar))
            {
                var e = z.GetEntry(path);
                if (e == null) return null;
                using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
            }
        }

        [Fact]
        public void EveryWatchtowerBlockIsOneOfOurs()
        {
            Blocks.Init();
            foreach (var name in new[] { "watchtower", "watchtower_overgrown" })
            {
                var data = FromJar($"data/minecraft/structure/pillager_outpost/{name}.nbt");
                if (data == null) return; // (no Minecraft here)
                var blocks = Nbt.StructureBlocks(Nbt.ReadGzip(data));
                Assert.True(blocks.Count > 4000);
                var missing = blocks.Select(b => b.id).Where(id => id != "air" && id != "structure_void" && id != "jigsaw")
                    .Select(id => id == "dark_oak_planks" ? "dark_planks" : id == "white_wall_banner" ? Banners.OminousKey : id)
                    .Distinct().Where(k => Blocks.Get(k) == null).ToList();
                Assert.True(missing.Count == 0, name + " needs: " + string.Join(", ", missing));
                Assert.Contains(blocks, b => b.id == "chest");
                Assert.Equal(8, blocks.Count(b => b.id == "white_wall_banner"));
            }
        }
    }
}

namespace LethalMinecraft.Tests
{
    /// <summary>(#225) Banner designs: written and read back.</summary>
    public class BannerDesignTests
    {
        [Fact]
        public void DesignsRoundTrip()
        {
            var (b, layers) = BannerDesigns.Parse(BannerDesigns.OminousSpec);
            Assert.Equal(0, b);
            Assert.Equal(8, layers.Count);
            Assert.Equal(("rhombus", 9), layers[0]);
            Assert.Equal(BannerDesigns.OminousSpec, BannerDesigns.Spec(b, layers));
            var plain = BannerDesigns.Parse("14;");
            Assert.Equal(14, plain.baseColor);
            Assert.Empty(plain.layers);
            Assert.Equal("red_banner", BannerDesigns.BlockKeyFor(14));
            Assert.Equal(3, BannerDesigns.IdOf("patterned_banner#3"));
            Assert.Equal(0, BannerDesigns.IdOf("white_banner"));
        }
    }
}
