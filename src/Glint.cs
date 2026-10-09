using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's enchantment glint (#46): an enchanted tool, sword or piece of armor shimmers purple, its icon in the
    /// hotbar and the inventory screens (a pulsing purple copy of the icon over it) and its model in hand or on the ground
    /// (a purple glow over its own colours).
    /// </summary>
    public static class Glint
    {
        public static bool Of(GrabbableObject g) => g is ToolItem t ? t.Ench != 0 : g is ArmorItem a && a.Ench != 0;
        public static bool OfKey(string key) => key != null && Enchants.EnchOf(ItemData.Of(key)) != 0;

        /// <summary>The purple over an icon (a light silhouette of it), pulsing slowly.</summary>
        public static Color Tint => new Color(0.8f, 0.45f, 1f, 0.45f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f));

        static readonly System.Collections.Generic.Dictionary<Sprite, Sprite> silhouettes = new System.Collections.Generic.Dictionary<Sprite, Sprite>();

        /// <summary>A white copy of an icon (its shape only): tinted purple over the icon it lightens it, like Minecraft's
        /// glint, where the icon itself tinted purple would darken it.</summary>
        public static Sprite Silhouette(Sprite s)
        {
            if (s == null) return null;
            if (silhouettes.TryGetValue(s, out var w)) return w;
            w = s;
            try
            {
                var src = s.texture;
                var r = s.textureRect;
                var px = src.GetPixels32();
                int tw = src.width;
                var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var outp = new Color32[(int)r.width * (int)r.height];
                for (int y = 0; y < (int)r.height; y++)
                    for (int x = 0; x < (int)r.width; x++)
                    {
                        var c = px[((int)r.y + y) * tw + (int)r.x + x];
                        outp[y * (int)r.width + x] = new Color32(255, 255, 255, c.a);
                    }
                tex.SetPixels32(outp); tex.Apply();
                w = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), s.pixelsPerUnit);
            }
            catch { } // (not readable: the icon itself, tinted)
            silhouettes[s] = w;
            return w;
        }

        static Material glow;
        public static float DevEV = 2.5f; // (dev: tunable, "glint ev <n>")
        public static void DevSetEV(float ev)
        {
            DevEV = ev;
            if (glow != null) { HDMaterial.SetEmissiveIntensity(glow, ev, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100); HDMaterial.ValidateMaterial(glow); }
        }
        static Material GlowMaterial
        {
            get
            {
                if (glow != null) return glow;
                glow = Atlas.MakeLit("LMC_Glint", cutout: true, doubleSided: false, emissive: false);
                // a purple glow added over the item's own (lit) colours, not multiplied with them: they still show
                glow.SetFloat("_AlbedoAffectEmissive", 0f);
                HDMaterial.SetUseEmissiveIntensity(glow, true);
                HDMaterial.SetEmissiveColor(glow, new Color(0.55f, 0.2f, 1f));
                HDMaterial.SetEmissiveIntensity(glow, DevEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                HDMaterial.ValidateMaterial(glow);
                return glow;
            }
        }

        /// <summary>The item's model glows when it's enchanted (and stops when it isn't).</summary>
        public static void ApplyModel(GrabbableObject g)
        {
            if (g == null) return;
            var model = g.transform.Find("model");
            var mr = model != null ? model.GetComponent<MeshRenderer>() : null;
            if (mr == null) return;
            bool on = Of(g);
            if (on && mr.sharedMaterial != GlowMaterial) mr.sharedMaterial = GlowMaterial;
            else if (!on && mr.sharedMaterial == glow) mr.sharedMaterial = Atlas.Cutout;
        }
    }
}
