using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Dyed leather, Minecraft's: leather armor and leather horse armor take a colour in the crafting grid (the piece
    /// and one to eight dyes: DyedItemColor.applyDyes, mixing with the colour it has), and a water cauldron washes it off.
    /// The colour is kept with the item's enchantment list (EnchSets: a list's number holds it), so it goes wherever the
    /// item goes. Undyed leather is Minecraft's brown, A06540. The textures: the grey base multiplied by the colour, its
    /// overlay (the stitching and buckles) drawn over it as it is.
    /// </summary>
    public static class LeatherDye
    {
        public const int Default = 0xA06540;
        public const string HorseKey = "leather_horse_armor";
        public static int Washed; // (dev/tests)

        public static bool Dyeable(string key)
        {
            var t = ItemData.TypeOf(key);
            return t != null && (t == HorseKey || Armor.Get(t)?.Material == "leather");
        }

        /// <summary>Its colour (0xRRGGBB), or -1: never dyed.</summary>
        public static int ColorOf(string key) => key == null ? -1 : Enchants.ColorOf(Enchants.EnchOf(ItemData.Of(key)));
        public static int ShownColor(string key) { int c = ColorOf(key); return c < 0 ? Default : c; }

        /// <summary>The key with this colour (-1: washed), or null while the host's number for it is on its way.</summary>
        public static string With(string key, int color)
        {
            int d = ItemData.Of(key);
            int e = Enchants.WithColor(Enchants.EnchOf(d), color);
            return e < 0 ? null : ItemData.With(key, Enchants.Data(Enchants.UsesOf(d), e));
        }

        /// <summary>Minecraft's DyedItemColor.applyDyes: the colours averaged (the old one counted as one), brightened back
        /// to the average of their brightest channels.</summary>
        public static int Mix(int current, IEnumerable<int> dyeIndexes)
        {
            int r = 0, g = 0, b = 0, intensity = 0, n = 0;
            void Add(int c)
            {
                int cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
                intensity += Mathf.Max(cr, Mathf.Max(cg, cb));
                r += cr; g += cg; b += cb; n++;
            }
            if (current >= 0) Add(current);
            foreach (int i in dyeIndexes) Add((int)BannerDesigns.DiffuseColors[Mathf.Clamp(i, 0, 15)]);
            if (n == 0) return current;
            r /= n; g /= n; b /= n;
            float avg = (float)intensity / n, max = Mathf.Max(r, Mathf.Max(g, b));
            if (max > 0) { r = (int)(r * avg / max); g = (int)(g * avg / max); b = (int)(b * avg / max); }
            return (r << 16) | (g << 8) | b;
        }

        /// <summary>The base's pixels times the colour, the overlay's over them wherever it isn't clear.</summary>
        public static Color32[] Tint(Color32[] basePx, Color32[] overlay, int rgb)
        {
            byte cr = (byte)((rgb >> 16) & 255), cg = (byte)((rgb >> 8) & 255), cb = (byte)(rgb & 255);
            var px = new Color32[basePx.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = basePx[i];
                px[i] = overlay != null && i < overlay.Length && overlay[i].a > 20 ? overlay[i] : new Color32((byte)(c.r * cr / 255), (byte)(c.g * cg / 255), (byte)(c.b * cb / 255), c.a);
            }
            return px;
        }

        /// <summary>A texture of Minecraft's (base and its overlay, e.g. item/leather_helmet) in a colour; null without its assets.</summary>
        public static Texture2D Texture(string basePath, string overlayPath, int rgb)
        {
            var b = McAssets.LoadTexture(basePath);
            if (b == null) return null;
            var o = overlayPath != null ? McAssets.LoadTexture(overlayPath) : null;
            Color32[] bp, op = null;
            try { bp = b.GetPixels32(); if (o != null && o.width == b.width && o.height == b.height) op = o.GetPixels32(); }
            catch { if (o != null) Object.Destroy(o); return b; }
            if (o != null) Object.Destroy(o);
            b.SetPixels32(Tint(bp, op, rgb)); b.filterMode = FilterMode.Point; b.Apply();
            return b;
        }

        /// <summary>An item's 16x16 picture in its colour (its item texture and overlay).</summary>
        public static Color32[] ItemPixels(string type, int rgb)
        {
            var t = Texture("item/" + type, "item/" + type + "_overlay", rgb);
            if (t == null) return null;
            var px = t.width == 16 && t.height == 16 ? t.GetPixels32() : null;
            Object.Destroy(t);
            return px;
        }

        static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
        /// <summary>A dyed piece's icon (a trimmed one's too: ArmorTrims), or null: the item's own icon does.</summary>
        public static Sprite IconFor(string key)
        {
            if (key == null) return null;
            if (ItemData.TypeOf(key) != HorseKey) return ArmorTrims.IconFor(key);
            int c = ColorOf(key);
            if (c < 0) return null;
            string id = HorseKey + "/" + c;
            if (icons.TryGetValue(id, out var s)) return s;
            var px = ItemPixels(HorseKey, c);
            return icons[id] = px == null ? null : Sprite.Create(Atlas.SpriteIconPx(px), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------ in hand and on the floor
        static readonly Dictionary<string, (Mesh mesh, Material mat, Material glow)> models = new Dictionary<string, (Mesh, Material, Material)>();

        /// <summary>A dyed piece's model in its colour (glinting if enchanted); false: not a dyed piece (Glint does it).</summary>
        public static bool ApplyModel(GrabbableObject g, bool glint)
        {
            string key = g is ArmorItem a ? Crafting.KeyOf(a) : g is LeatherHorseArmorItem h ? Crafting.KeyOf(h) : null;
            if (key == null || !Dyeable(key)) return false;
            var mf = g.transform.Find("model")?.GetComponent<MeshFilter>();
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mr == null) return false;
            int c = ColorOf(key);
            string type = ItemData.TypeOf(key);
            if (c < 0)
            {
                // (undyed: the atlas's picture, already Minecraft's brown)
                mf.sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_" + type) ? "item_" + type : "item_iron_ingot");
                mr.sharedMaterial = glint ? Glint.GlowMaterial : Atlas.Cutout;
                return true;
            }
            string id = type + "/" + c;
            if (!models.TryGetValue(id, out var m) || m.mesh == null)
            {
                var px = ItemPixels(type, c);
                if (px == null) return false;
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px); tex.Apply();
                Material Copy(Material from)
                {
                    var mat = new Material(from) { name = "LMC_dyed_" + id, mainTexture = tex };
                    if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", tex);
                    if (mat.HasProperty("_EmissiveColorMap") && mat.GetTexture("_EmissiveColorMap") != null) mat.SetTexture("_EmissiveColorMap", tex);
                    return mat;
                }
                models[id] = m = (MeshBuilder.ExtrudedPixels(px, new Rect(0, 0, 1, 1), "LMC_dyed_" + id), Copy(Atlas.Cutout), Copy(Glint.GlowMaterial));
            }
            mf.sharedMesh = m.mesh;
            mr.sharedMaterial = glint ? m.glow : m.mat;
            return true;
        }

        // ------------------------------------------------------------------ the host: a cauldron washes it
        /// <summary>Host: [E] on a water cauldron with a dyed piece in hand: the colour washes off, a third of the water
        /// goes (Minecraft's CauldronInteraction.DYED_ITEM). False: nothing to wash.</summary>
        public static bool ServerWash(PlayerControllerB p, BlockKey k, int level)
        {
            var g = p != null ? p.currentlyHeldObjectServer : null;
            if (level < 1 || level > 3 || g == null) return false;
            if (g is ArmorItem a && ColorOf(Crafting.KeyOf(a)) >= 0)
            {
                int e = Enchants.WithColor(a.Ench, -1);
                if (e < 0) return false;
                a.Ench = e;
            }
            else if (g is LeatherHorseArmorItem h && ColorOf(Crafting.KeyOf(h)) >= 0)
            {
                int e = Enchants.WithColor(h.Ench, -1);
                if (e < 0) return false;
                h.Ench = e;
            }
            else return false;
            BlockNet.ServerItemData(g);
            ApplyModel(g, Glint.Of(g));
            Composting.SetCauldron(k, level - 1);
            Washed++;
            return true;
        }
    }

    /// <summary>(#244) Leather horse armor: one item, not a stack, so it can carry its colour (LeatherDye).</summary>
    public class LeatherHorseArmorItem : ToolItem
    {
        public override int MaxUses => 0;
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        public override void SetControlTipsForItem()
        {
            HUDManager.Instance.ChangeControlTipMultiple(new[] { "Put on a tamed horse : [E] on it", Plugin.PlaceWithLeftClick.Value ? "" : "Break : hold [LMB]", itemProperties.itemName }, holdingItem: true, itemProperties);
        }
    }
}
