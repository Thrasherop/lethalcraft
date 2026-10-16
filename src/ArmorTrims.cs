using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Armor trims (#224), Minecraft's: at the smithing table a trim template, a piece of armor and a trim material put the
    /// template's pattern on the armor in the material's colours (the template used up, as Minecraft's). Drawn on the worn
    /// armor (and so in the inventory's preview) and on its icon, as Minecraft draws them: the pattern's greys
    /// (palettes/trim_base) swapped for the material's palette, the darker one when the armor is of the same material
    /// (equipment/*.json's trim_overrides).
    /// A trim lives in the armor's saved number, in the low bits a tool keeps its uses in (armor wears out only as an elytra,
    /// and an elytra takes no trim): the pattern in 5 bits, the material above it in 4; the enchantments stay above both.
    /// Only the sentry template can be found so far (pillager outposts); the other patterns' numbers are kept for theirs.
    /// </summary>
    public static class ArmorTrims
    {
        /// <summary>Patterns by number (0: none). Their order is saved: add new ones at the end.</summary>
        public static readonly string[] Patterns = { null, "sentry", "dune", "coast", "wild", "ward", "eye", "vex", "tide", "snout", "rib", "spire", "wayfinder", "shaper", "silence", "raiser", "host", "flow", "bolt" };
        /// <summary>Materials by number (0: none), Minecraft's trim_material names. Their order is saved too.</summary>
        public static readonly string[] Materials = { null, "iron", "copper", "gold", "lapis", "emerald", "diamond", "netherite", "redstone", "amethyst", "quartz", "resin" };

        /// <summary>The item each material is (Minecraft's #trim_materials), for the ones the mod has (a mined diamond is
        /// scrap until it's sold or put in a block: either kind does).</summary>
        public static readonly ItemData.TypeMap<string> MaterialItems = new ItemData.TypeMap<string> // (#228: by type)
        {
            ["iron_ingot"] = "iron", ["copper_ingot"] = "copper", ["gold_ingot"] = "gold", ["lapis_lazuli"] = "lapis",
            ["diamond"] = "diamond", ["scrap_diamond_ore"] = "diamond", ["scrap_emerald_ore"] = "emerald", ["netherite_ingot"] = "netherite", ["redstone_dust"] = "redstone", ["quartz"] = "quartz", ["resin_brick"] = "resin", // (#230)
        };

        /// <summary>Templates that can be had (each one's pattern).</summary>
        public static readonly ItemData.TypeMap<string> Templates = new ItemData.TypeMap<string>
        {
            ["sentry_armor_trim_smithing_template"] = "sentry",
        };

        static readonly string[] trimmable = { "golden", "iron", "diamond", "netherite" };
        public static bool Trimmable(string key)
        {
            var d = Armor.Get(key);
            return d != null && System.Array.IndexOf(trimmable, d.Material) >= 0;
        }

        public static bool IsTemplate(string key) => Templates.ContainsKey(key);
        public static bool IsMaterial(string key) => MaterialItems.ContainsKey(key); // (a mined diamond carries its value)

        // ------------------------------------------------------------------ the saved number
        const int PatternBits = 5, MaterialMask = 15;

        /// <summary>An armor key's trim: its pattern and material numbers (0, 0: none).</summary>
        public static (int pattern, int material) Of(string key)
        {
            if (!Trimmable(key)) return (0, 0);
            int t = Enchants.UsesOf(ItemData.Of(key));
            int p = t & ((1 << PatternBits) - 1), m = (t >> PatternBits) & MaterialMask;
            return p > 0 && p < Patterns.Length && m > 0 && m < Materials.Length ? (p, m) : (0, 0);
        }

        public static string With(string key, int pattern, int material)
        {
            int ench = Enchants.EnchOf(ItemData.Of(key));
            return ItemData.With(key, Enchants.Data(pattern | (material << PatternBits), ench));
        }

        /// <summary>What a template, an armor piece and a material make at the smithing table, or null (Minecraft's
        /// SmithingTrimRecipe: nothing when the piece already has that very trim).</summary>
        public static string Result(string template, string item, string addition)
        {
            if (template == null || item == null || addition == null) return null;
            if (!Templates.TryGetValue(template, out var pat) || !Trimmable(item) || !MaterialItems.TryGetValue(addition, out var mat)) return null;
            int p = System.Array.IndexOf(Patterns, pat), m = System.Array.IndexOf(Materials, mat);
            if (Of(item) == (p, m)) return null;
            return With(item, p, m);
        }

        static string Title(string s) => string.Join(" ", s.Split('_').Select(w => char.ToUpper(w[0]) + w.Substring(1)));

        /// <summary>"Sentry Armor Trim, Iron Material" (Minecraft's tooltip lines), or null.</summary>
        public static string Describe(string key)
        {
            var (p, m) = Of(key);
            return p > 0 ? $"{Title(Patterns[p])} Armor Trim, {Title(Materials[m])} Material" : null;
        }

        // ------------------------------------------------------------------ drawing
        /// <summary>The palette a material's trim is drawn with on armor of `armorMaterial` (its darker one on its own kind).</summary>
        public static string PaletteFor(int material, string armorMaterial)
        {
            var name = Materials[material];
            var am = armorMaterial == "golden" ? "gold" : armorMaterial;
            return name == am && (am == "iron" || am == "gold" || am == "diamond" || am == "netherite") ? name + "_darker" : name;
        }

        static Color32[] basePalette;
        static readonly Dictionary<string, Color32[]> palettes = new Dictionary<string, Color32[]>();

        static Color32[] Palette(string name)
        {
            if (palettes.TryGetValue(name, out var p)) return p;
            var t = McAssets.LoadTexture("palettes/" + name);
            p = t != null ? t.GetPixels32() : null;
            if (t != null) Object.Destroy(t);
            return palettes[name] = p;
        }

        /// <summary>A trim texture's pixels in a material's colours (Minecraft's PalettedPermutations), or null.</summary>
        public static Color32[] Paletted(string texture, string palette, out int w, out int h)
        {
            w = h = 0;
            if (basePalette == null) basePalette = Palette("trim_base");
            var to = Palette("trim/" + palette);
            var t = McAssets.LoadTexture(texture);
            if (t == null || basePalette == null || to == null) { if (t != null) Object.Destroy(t); return null; }
            w = t.width; h = t.height;
            var px = t.GetPixels32();
            Object.Destroy(t);
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a == 0) continue;
                for (int j = 0; j < basePalette.Length && j < to.Length; j++)
                    if (basePalette[j].r == px[i].r && basePalette[j].g == px[i].g && basePalette[j].b == px[i].b)
                    {
                        var c = to[j]; c.a = px[i].a; px[i] = c;
                        break;
                    }
            }
            return px;
        }

        /// <summary>`over` laid on `under` (same size, or scaled to it), where it isn't clear.</summary>
        static void Lay(Color32[] under, int w, int h, Color32[] over, int ow, int oh)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var o = over[(y * oh / h) * ow + x * ow / w];
                    if (o.a == 0) continue;
                    var u = under[y * w + x];
                    float a = o.a / 255f;
                    under[y * w + x] = new Color32((byte)(u.r + (o.r - u.r) * a), (byte)(u.g + (o.g - u.g) * a), (byte)(u.b + (o.b - u.b) * a), (byte)Mathf.Max(u.a, o.a));
                }
        }

        /// <summary>An armor texture (64x32) with a trim drawn on it, for ArmorModels; null: nothing to draw.</summary>
        public static Texture2D OnArmor(Texture2D armor, string armorMaterial, int layer, int pattern, int material)
        {
            if (armor == null || pattern <= 0 || material <= 0) return null;
            var trim = Paletted($"trims/entity/{(layer == 2 ? "humanoid_leggings" : "humanoid")}/{Patterns[pattern]}", PaletteFor(material, armorMaterial), out int tw, out int th);
            if (trim == null) return null;
            Color32[] px;
            try { px = armor.GetPixels32(); } catch { return null; } // (not readable)
            Lay(px, armor.width, armor.height, trim, tw, th);
            var t = new Texture2D(armor.width, armor.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        /// <summary>A trimmed armor piece's icon (Minecraft's item model: the piece, its trim over it), or null: not trimmed.</summary>
        public static Sprite IconFor(string key)
        {
            var (p, m) = Of(key);
            if (p <= 0) return null;
            var d = Armor.Get(key);
            string id = d.Key + "/" + p + "/" + m;
            if (icons.TryGetValue(id, out var s)) return s; // (null: no trim textures in this Minecraft: not looked for again)
            // (the item's trim overlay shows the material, not the pattern: every pattern looks the same in the hotbar, as Minecraft's)
            var trim = Paletted($"trims/items/{Armor.PieceKeys[d.Slot]}_trim", PaletteFor(m, d.Material), out int tw, out int th);
            var bt = McAssets.LoadTexture("item/" + d.Key);
            if (trim == null || bt == null) { if (bt != null) Object.Destroy(bt); return icons[id] = null; }
            var px = bt.GetPixels32();
            int w = bt.width, h = bt.height;
            Object.Destroy(bt);
            Lay(px, w, h, trim, tw, th);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixels32(px); t.Apply();
            return icons[id] = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        }
    }
}
