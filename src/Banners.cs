using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The ominous banner (#223): the pillager captain's, Minecraft's Raid.getOminousBannerInstance: a white banner with
    /// a cyan rhombus, a light gray bottom stripe, a gray center stripe, a light gray border, a black middle stripe, a
    /// light gray top half, a light gray circle and a black border. Its texture is put together here as Minecraft's
    /// BannerRenderer draws it: entity/banner/banner_base for the pole, bar and cloth, then each pattern's mask
    /// (entity/banner/*.png) tinted with its dye's colour over the cloth, in order. Its model is BannerModel and
    /// BannerFlagModel (a 42 px pole, a 20 px bar, a 20 x 40 cloth, two thirds the size as a block), the cloth swaying.
    /// </summary>
    public static class Banners
    {
        public const string OminousKey = "ominous_banner";
        // (pattern, DyeColor.textureDiffuseColor)
        static readonly (string pattern, uint color)[] Ominous =
        {
            ("base", 0xF9FFFE), ("rhombus", 0x169C9C), ("stripe_bottom", 0x9D9D97), ("stripe_center", 0x474F52), ("border", 0x9D9D97),
            ("stripe_middle", 0x1D1D21), ("half_horizontal", 0x9D9D97), ("circle", 0x9D9D97), ("border", 0x1D1D21),
        };
        static Texture2D tex;
        static Material mat;

        /// <summary>The ominous banner's 64x64 texture (null without Minecraft's assets).</summary>
        public static Texture2D Texture
        {
            get
            {
                if (tex != null) return tex;
                var b = McAssets.LoadTexture("entity/banner/banner_base");
                if (b == null) return null;
                var px = b.GetPixels(); // (bottom-up rows)
                int w = b.width, h = b.height;
                foreach (var (pattern, color) in Ominous)
                {
                    var m = McAssets.LoadTexture("entity/banner/" + pattern);
                    if (m == null || m.width != w || m.height != h) continue;
                    var dye = new Color(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f);
                    var mp = m.GetPixels();
                    // (the cloth's part of the texture only: BannerFlagModel's 20 x 40 x 1 box at 0, 0, 42 x 41 px from the top)
                    for (int y = h - 41; y < h; y++)
                        for (int x = 0; x < 42; x++)
                        {
                            var c = mp[y * w + x];
                            if (c.a <= 0f) continue;
                            px[y * w + x] = Color.Lerp(px[y * w + x], new Color(c.r * dye.r, c.g * dye.g, c.b * dye.b, 1f), c.a);
                        }
                }
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "LMC_OminousBanner" };
                tex.SetPixels(px); tex.Apply();
                return tex;
            }
        }

        public static Material Material => mat != null ? mat : (mat = EntityModels.Material("LMC_OminousBanner", Texture, new Color(0.85f, 0.85f, 0.85f)));

        /// <summary>
        /// The banner's model under `parent`, its pole's foot at the parent's origin (standing) or hanging from its bar
        /// (on a wall: its back toward -z). px: metres per banner pixel. The cloth sways (BannerFlagModel.setupAnim).
        /// </summary>
        public static GameObject Build(Transform parent, bool standing, float px, string name = "LMC_Banner")
        {
            var root = new GameObject(name) { layer = parent.gameObject.layer };
            root.transform.SetParent(parent, false);
            var m = Material;
            // (BannerModel and BannerFlagModel turned from Minecraft's y down, front -z into y up, front +z)
            if (standing) EntityModels.Part(root.transform, "Pole", new Vector3(0, 21, 0), new Vector3(2, 42, 2), 44, 0, px, m, root.layer);
            EntityModels.Part(root.transform, "Bar", standing ? new Vector3(0, 43, 0) : new Vector3(0, 19.5f, -10.5f), new Vector3(20, 2, 2), 0, 42, px, m, root.layer);
            var pivot = new GameObject("Cloth") { layer = root.layer }.transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = (standing ? new Vector3(0, 44, 0) : new Vector3(0, 20.5f, -10.5f)) * px;
            EntityModels.Part(pivot, "Flag", new Vector3(0, -20, 1.5f), new Vector3(20, 40, 1), 0, 0, px, m, root.layer);
            pivot.gameObject.AddComponent<BannerSway>();
            return root;
        }

        /// <summary>
        /// The captain's banner on its head, as Minecraft's CustomHeadLayer draws a banner item there (translateToHead, the
        /// banner's head display, its special model's own transformation): worked through, the pole's foot 6.5 px above
        /// the neck and 4.375 px behind it, at 0.625 of a banner pixel to a model pixel. head: the head's pivot (the
        /// neck); k: metres per model pixel.
        /// </summary>
        public static GameObject BuildOnHead(Transform head, float k)
        {
            var at = new GameObject("Banner") { layer = head.gameObject.layer }.transform;
            at.SetParent(head, false);
            at.localPosition = new Vector3(0f, 6.5f, -4.375f) * k;
            Build(at, true, k * 0.625f);
            return at.gameObject;
        }

        /// <summary>The ominous banner's item tile (16 x 16, bottom-up rows): its cloth's front, squeezed into the middle
        /// half, hanging from its bar.</summary>
        public static Color32[] Tile(int size)
        {
            var t = Texture;
            if (t == null) return null;
            var outp = new Color32[size * size];
            int w = size / 2, x0 = (size - w) / 2;
            for (int y = 0; y < size - 1; y++)
                for (int x = 0; x < w; x++)
                {
                    // (the cloth's front: u 1-21, v 1-41 from the top)
                    int u = 1 + x * 20 / w, v = 1 + (size - 2 - y) * 40 / (size - 1);
                    outp[y * size + x0 + x] = t.GetPixel(u, t.height - 1 - v);
                }
            // the bar across the top
            for (int x = x0 - 1; x < x0 + w + 1; x++) outp[(size - 1) * size + x] = t.GetPixel(4 + x % 8, t.height - 1 - 43);
            return outp;
        }
    }

    /// <summary>A banner's cloth swaying: BannerFlagModel.setupAnim, x turned by (-0.0125 + 0.01 cos(2 pi phase)) pi, the
    /// phase going round every 100 ticks from where it hangs.</summary>
    public class BannerSway : MonoBehaviour
    {
        float offset;
        void Start() { var p = transform.position; offset = Mathf.Repeat(p.x * 7f + p.y * 9f + p.z * 13f, 100f); }
        void Update()
        {
            float phase = Mathf.Repeat(offset + Time.time * 20f, 100f) / 100f;
            transform.localRotation = PillagerAI.McRot((-0.0125f + 0.01f * Mathf.Cos(2f * Mathf.PI * phase)) * Mathf.PI, 0f, 0f);
        }
    }

    /// <summary>A placed banner's model (#223): standing on its cell's floor, or hanging from its wall (the block's own
    /// turn faces it); BannerRenderer's two thirds of a block to its 16 pixels. Every client draws its own.</summary>
    public class BannerBlock : MonoBehaviour
    {
        public static void Attach(GameObject block, bool standing)
        {
            if (block == null) return;
            var have = block.GetComponent<BannerBlock>();
            if (have != null && have.standing == standing) return;
            if (have != null) { Destroy(have.model); Destroy(have); }
            var bb = block.AddComponent<BannerBlock>();
            bb.standing = standing;
            var at = new GameObject("LMC_BannerAt") { layer = block.layer }.transform;
            at.SetParent(block.transform, false);
            at.localPosition = new Vector3(0f, -0.5f, 0f); // (the cell's floor, in the block's unit space)
            Banners.Build(at, standing, (2f / 3f) / 16f);
            bb.model = at.gameObject;
        }

        bool standing;
        GameObject model;
        void OnDestroy() { if (model != null) Destroy(model); }
    }

    public static partial class Blocks
    {
        /// <summary>(#223) this batch's blocks, ids from 500 (so nothing earlier moves; the woods have their own range).</summary>
        static ushort nextPillagerId = 500;
        /// <summary>(#223) A stairs block's state: upside down (Minecraft's half=top), its step under a full top half.
        /// Outposts build them; placing them by hand is as before.</summary>
        public const byte StairsUpsideDown = 4;
        public static BlockDef OminousBanner;

        static void AddPillagerBlocks()
        {
            OminousBanner = Add(nextPillagerId++, Banners.OminousKey, "Ominous Banner", "oak_planks", d =>
            {
                d.Shape = BlockShape.Banner; d.Directional = true; d.FacingIncludesVertical = true; d.Sound = "wood"; d.Flammable = true; d.Pushable = false;
                d.Description = "The pillager captain's banner. Put it on the ground (it turns to face you) or on a wall; or wear it on your head as a captain does.";
            });
            familyHardness[Banners.OminousKey] = (1f, ToolKind.Axe, 0); // (Minecraft's: 1, an axe)
        }
    }
}
