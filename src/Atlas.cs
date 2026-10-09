using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>Builds the block texture atlas + materials (HDRP).</summary>
    public static class Atlas
    {
        public const int Tile = 16;
        public const int Cols = 16;
        public static Texture2D Texture, Emission;
        public static Dictionary<string, int> Tiles = new Dictionary<string, int>();
        public static Material Opaque, Cutout, Emissive, CutoutEmissive, Crack, Outline, Particle, OpaqueAmb, CutoutAmb;
        public static float AmbientEV = 1f;
        public static float EmissiveEV = 5f;
        public static Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        static Texture2D genAtlas, genEmit;

        // our tile name -> (minecraft texture path, tint)
        static readonly Dictionary<string, (string path, Color32? tint)> McMap = new Dictionary<string, (string, Color32?)>
        {
            ["grass_top"] = ("block/grass_block_top", new Color32(124, 189, 91, 255)),
            ["grass_side"] = ("block/grass_block_side", null),
            ["dirt"] = ("block/dirt", null),
            ["stone"] = ("block/stone", null),
            ["cobblestone"] = ("block/cobblestone", null),
            ["oak_planks"] = ("block/oak_planks", null),
            ["log_side"] = ("block/oak_log", null),
            ["log_top"] = ("block/oak_log_top", null),
            ["glass"] = ("block/glass", null),
            ["sand"] = ("block/sand", null),
            ["gravel"] = ("block/gravel", null),
            ["bricks"] = ("block/bricks", null),
            ["tnt_side"] = ("block/tnt_side", null),
            ["tnt_top"] = ("block/tnt_top", null),
            ["tnt_bottom"] = ("block/tnt_bottom", null),
            ["piston_side"] = ("block/piston_side", null),
            ["piston_top"] = ("block/piston_top", null),
            ["sticky_top"] = ("block/piston_top_sticky", null),
            ["piston_bottom"] = ("block/piston_bottom", null),
            ["piston_inner"] = ("block/piston_inner", null),
            ["redstone_block"] = ("block/redstone_block", null),
            ["lamp_off"] = ("block/redstone_lamp", null),
            ["lamp_on"] = ("block/redstone_lamp_on", null),
            ["glowstone"] = ("block/glowstone", null),
            ["obsidian"] = ("block/obsidian", null),
            ["diamond_ore"] = ("block/diamond_ore", null),
            ["gold_ore"] = ("block/gold_ore", null),
            ["iron_ore"] = ("block/iron_ore", null),
            ["coal_ore"] = ("block/coal_ore", null),
            ["emerald_ore"] = ("block/emerald_ore", null),
            ["diamond_block"] = ("block/diamond_block", null),
            ["gold_block"] = ("block/gold_block", null),
            ["iron_block"] = ("block/iron_block", null),
            ["leaves"] = ("block/oak_leaves", new Color32(98, 160, 50, 255)),
            ["wool_white"] = ("block/white_wool", null),
            ["wool_red"] = ("block/red_wool", null),
            ["wool_blue"] = ("block/blue_wool", null),
            ["wool_yellow"] = ("block/yellow_wool", null),
            ["ladder"] = ("block/ladder", null),
            ["note_block"] = ("block/note_block", null),
            ["jack_face"] = ("block/jack_o_lantern", null),
            ["pumpkin_side"] = ("block/pumpkin_side", null),
            ["pumpkin_top"] = ("block/pumpkin_top", null),
            ["ice"] = ("block/ice", null),
            ["slime"] = ("block/slime_block", null),
            ["bookshelf"] = ("block/bookshelf", null),
            ["oak_planks_dark"] = ("block/dark_oak_planks", null),
            ["stone_bricks"] = ("block/stone_bricks", null),
            ["bedrock"] = ("block/bedrock", null),
            ["crafting_table_top"] = ("block/crafting_table_top", null),
            ["crafting_table_front"] = ("block/crafting_table_front", null),
            ["crafting_table_side"] = ("block/crafting_table_side", null),
            ["furnace_front"] = ("block/furnace_front", null),
            ["furnace_front_on"] = ("block/furnace_front_on", null),
            ["furnace_side"] = ("block/furnace_side", null),
            ["furnace_top"] = ("block/furnace_top", null),
            ["fire_0"] = ("block/fire_0", null),
            ["observer_front"] = ("block/observer_front", null),
            ["observer_side"] = ("block/observer_side", null),
            ["observer_top"] = ("block/observer_top", null),
            ["observer_back"] = ("block/observer_back", null),
            ["observer_back_on"] = ("block/observer_back_on", null),
            ["item_wooden_pickaxe"] = ("item/wooden_pickaxe", null),
            ["item_wooden_shovel"] = ("item/wooden_shovel", null),
            ["item_wooden_axe"] = ("item/wooden_axe", null),
            ["item_stone_pickaxe"] = ("item/stone_pickaxe", null),
            ["item_iron_pickaxe"] = ("item/iron_pickaxe", null),
            ["item_stone_shovel"] = ("item/stone_shovel", null),
            ["item_iron_shovel"] = ("item/iron_shovel", null),
            ["item_diamond_shovel"] = ("item/diamond_shovel", null),
            ["item_stone_axe"] = ("item/stone_axe", null),
            ["item_iron_axe"] = ("item/iron_axe", null),
            ["item_diamond_axe"] = ("item/diamond_axe", null),
            ["item_stick"] = ("item/stick", null),
            ["item_coal"] = ("item/coal", null),
            ["item_iron_ingot"] = ("item/iron_ingot", null),
            ["item_gold_ingot"] = ("item/gold_ingot", null),
            ["item_diamond"] = ("item/diamond", null),
            ["item_wooden_sword"] = ("item/wooden_sword", null), ["item_stone_sword"] = ("item/stone_sword", null), ["item_iron_sword"] = ("item/iron_sword", null),
            ["item_golden_sword"] = ("item/golden_sword", null), ["item_diamond_sword"] = ("item/diamond_sword", null),
            ["item_golden_helmet"] = ("item/golden_helmet", null), ["item_golden_chestplate"] = ("item/golden_chestplate", null),
            ["item_golden_leggings"] = ("item/golden_leggings", null), ["item_golden_boots"] = ("item/golden_boots", null),
            ["item_iron_helmet"] = ("item/iron_helmet", null), ["item_iron_chestplate"] = ("item/iron_chestplate", null),
            ["item_iron_leggings"] = ("item/iron_leggings", null), ["item_iron_boots"] = ("item/iron_boots", null),
            ["item_diamond_helmet"] = ("item/diamond_helmet", null), ["item_diamond_chestplate"] = ("item/diamond_chestplate", null),
            ["item_diamond_leggings"] = ("item/diamond_leggings", null), ["item_diamond_boots"] = ("item/diamond_boots", null),
            ["slot_helmet"] = ("gui/sprites/container/slot/helmet", null), ["slot_chestplate"] = ("gui/sprites/container/slot/chestplate", null),
            ["slot_leggings"] = ("gui/sprites/container/slot/leggings", null), ["slot_boots"] = ("gui/sprites/container/slot/boots", null),
            ["coal_block"] = ("block/coal_block", null),
            ["item_ender_pearl"] = ("item/ender_pearl", null),
            ["item_slime_ball"] = ("item/slime_ball", null),
            ["snow"] = ("block/snow", null),
            ["item_torch"] = ("block/torch", null),
            ["redstone_torch"] = ("block/redstone_torch", null),
            ["redstone_torch_off"] = ("block/redstone_torch_off", null),
            ["lever_handle"] = ("block/lever", null),
            ["item_pickaxe"] = ("item/diamond_pickaxe", null),
            ["item_flint_and_steel"] = ("item/flint_and_steel", null),
            ["item_redstone_dust"] = ("item/redstone", null),
            ["food_bread"] = ("item/bread", null),
            ["food_steak"] = ("item/cooked_beef", null),
            ["food_apple"] = ("item/apple", null),
            ["food_golden_apple"] = ("item/golden_apple", null),
            ["food_cookie"] = ("item/cookie", null),
            ["food_porkchop"] = ("item/cooked_porkchop", null),
            ["scrap_diamond"] = ("item/diamond", null),
            ["scrap_emerald"] = ("item/emerald", null),
            ["scrap_raw_gold"] = ("item/raw_gold", null),
            ["scrap_raw_iron"] = ("item/raw_iron", null),
            ["scrap_coal"] = ("item/coal", null),
        };

        public static int T(string name)
        {
            if (name != null && Tiles.TryGetValue(name, out int i)) return i;
            return Tiles.TryGetValue("stone", out int s) ? s : 0;
        }

        public static Rect UV(string name)
        {
            int i = T(name);
            int col = i % Cols, row = i / Cols;
            float s = 1f / Cols;
            // row 0 is the TOP of the image; Unity UV origin is bottom-left
            const float inset = 0.0005f;
            return new Rect(col * s + inset, 1f - (row + 1) * s + inset, s - 2 * inset, s - 2 * inset);
        }

        static Texture2D LoadEmbedded(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream(name))
            {
                if (st == null) return null;
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    t.LoadImage(ms.ToArray(), false);
                    t.filterMode = FilterMode.Point;
                    return t;
                }
            }
        }

        public static Texture2D LoadEmbeddedIcon(string name) => LoadEmbedded("icons." + name + ".png");

        static string ReadEmbeddedText(string name)
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(st)) return r.ReadToEnd();
        }

        public static void Build()
        {
            genAtlas = LoadEmbedded("atlas.png");
            genEmit = LoadEmbedded("atlas_emit.png");
            var genTiles = new Dictionary<string, int>();
            foreach (var p in JObject.Parse(ReadEmbeddedText("tiles.json")).Properties()) genTiles[p.Name] = (int)p.Value;

            Texture = new Texture2D(Tile * Cols, Tile * Cols, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0, name = "LMC_Atlas" };
            Emission = new Texture2D(Tile * Cols, Tile * Cols, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 0, name = "LMC_AtlasEmit" };
            var clear = new Color32[Tile * Cols * Tile * Cols];
            Texture.SetPixels32(clear);
            var black = new Color32[clear.Length];
            for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
            Emission.SetPixels32(black);

            // all names: generated set + mc-only extras
            var names = new List<string>(genTiles.Keys);
            foreach (var k in McMap.Keys) if (!names.Contains(k)) names.Add(k);
            for (int s = 0; s < 10; s++) if (!names.Contains("crack_" + s)) names.Add("crack_" + s);
            if (McAssets.Available)
                foreach (var dn in new[] { "dust_line_on", "dust_line_off", "dust_dot_on", "dust_dot_off", "dust_cross_on", "dust_cross_off" })
                    names.Add(dn);
            if (McAssets.Available)
                for (int f = 0; f < MaxFireFrames; f++) { names.Add("fire_0_f" + f); names.Add("fire_1_f" + f); }

            int next = 0;
            int mcCount = 0;
            foreach (var name in names)
            {
                Color32[] px = null;
                Color32[] em = null;
                if (McAssets.Available)
                {
                    px = LoadMcTile(name);
                    if (px != null) mcCount++;
                }
                if (px == null)
                {
                    string gen = name;
                    if (!genTiles.ContainsKey(gen))
                    {
                        // fallbacks for mc-only names
                        if (name == "redstone_torch" || name == "redstone_torch_off") gen = "item_torch";
                        else if (name == "lever_handle") gen = "item_torch";
                        else if (name == "observer_top") gen = "observer_side";
                        else if (name == "fire_0") gen = "glowstone";
                        else if (name == "observer_back_on") gen = "observer_back";
                        else if (name.StartsWith("food_") || name.StartsWith("scrap_")) gen = null;
                        else gen = null;
                    }
                    if (gen != null && genTiles.TryGetValue(gen, out int gi))
                    {
                        px = ReadTile(genAtlas, gi);
                        em = ReadTile(genEmit, gi);
                    }
                }
                if (px == null) continue;
                if (em == null) em = MakeEmission(name, px);
                Tiles[name] = next;
                WriteTile(Texture, next, px);
                WriteTile(Emission, next, em);
                next++;
                if (next >= Cols * Cols) break;
            }
            Texture.Apply(false, false);
            Emission.Apply(false, false);
            foreach (var t in fireStrips.Values) if (t != null) Object.Destroy(t);
            fireStrips.Clear();
            FireFrames = 0;
            while (FireFrames < MaxFireFrames && Tiles.ContainsKey("fire_0_f" + FireFrames) && Tiles.ContainsKey("fire_1_f" + FireFrames)) FireFrames++;
            Plugin.Log.LogInfo($"Atlas built: {Tiles.Count} tiles ({mcCount} from Minecraft; fire animation {FireFrames} frames)");

            BuildMaterials();
            BuildIcons();
        }

        static Color32[] MakeEmission(string name, Color32[] px)
        {
            var em = new Color32[px.Length];
            for (int i = 0; i < em.Length; i++) em[i] = new Color32(0, 0, 0, 255);
            bool full = name == "lamp_on" || name == "glowstone" || name.StartsWith("fire_") || (name.StartsWith("dust_") && name.EndsWith("_on"));
            bool bright = name == "jack_face" || name == "item_torch" || name == "redstone_torch" || name == "furnace_front_on";
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 10) continue;
                if (full) em[i] = c;
                else if (bright)
                {
                    float lum = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
                    bool warm = c.r > 200 && c.g > 120 && c.b < 140;
                    bool red = name == "redstone_torch" && c.r > 150 && c.g < 80;
                    if ((lum > 0.62f && warm) || red || ((name == "jack_face" || name == "furnace_front_on") && c.r > 200 && c.g > 120)) em[i] = c;
                }
            }
            return em;
        }

        public const int MaxFireFrames = 32;
        /// <summary>Frames of the fire animation in the atlas (0: no animation, fire uses the still tile).</summary>
        public static int FireFrames;
        static readonly Dictionary<string, Texture2D> fireStrips = new Dictionary<string, Texture2D>();

        /// <summary>Frame n (top to bottom) of an animation strip, scaled to a tile; null past its last frame.</summary>
        static Color32[] StripFrame(Texture2D t, int n)
        {
            int w = t.width, frames = t.height / w;
            if (n >= frames) return null;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    outp[y * Tile + x] = (Color32)t.GetPixel(x * w / Tile, t.height - (n + 1) * w + y * w / Tile);
            return outp;
        }

        static Color32[] LoadMcTile(string name)
        {
            if (name.StartsWith("fire_0_f") || name.StartsWith("fire_1_f"))
            {
                string strip = name.Substring(0, 6); // fire_0 / fire_1
                if (!fireStrips.TryGetValue(strip, out var st)) fireStrips[strip] = st = McAssets.LoadTexture("block/" + strip);
                if (st == null) return null;
                int n = int.Parse(name.Substring(8));
                int frames = st.height / st.width;
                // fire_0 plays its strip starting halfway (its .mcmeta), so the two flicker out of step like Minecraft's
                if (strip == "fire_0") n = (n + frames / 2) % Mathf.Max(1, frames);
                return StripFrame(st, n);
            }
            if (name.StartsWith("crack_"))
            {
                var t = McAssets.LoadTexture("block/destroy_stage_" + name.Substring(6));
                return t == null ? null : FirstFrame(t, null);
            }
            if (name.StartsWith("dust_"))
            {
                var dot = McAssets.LoadTexture("block/redstone_dust_dot");
                var line = McAssets.LoadTexture("block/redstone_dust_line0");
                if (dot == null || line == null) return null;
                var d = FirstFrame(dot, null);
                var l = FirstFrame(line, null);
                bool on = name.EndsWith("_on");
                var tint = on ? new Color32(255, 40, 20, 255) : new Color32(105, 0, 0, 255);
                bool cross = name == "dust_on" || name == "dust_off" || name.StartsWith("dust_cross");
                bool onlyLine = name.StartsWith("dust_line");
                bool onlyDot = name.StartsWith("dust_dot");
                var outp = new Color32[Tile * Tile];
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        int i = y * Tile + x;
                        // cross = line0 (vertical) + line0 rotated 90 degrees + dot
                        Color32 a = l[i];
                        Color32 b = l[x * Tile + (Tile - 1 - y)];
                        Color32 c = d[i];
                        Color32 m = default;
                        if (onlyLine) m = a;
                        else if (onlyDot) m = c;
                        else if (cross) m = a.a > 0 ? a : (b.a > 0 ? b : c);
                        if (m.a > 0) outp[i] = Mul(m, tint);
                    }
                return outp;
            }
            if (name == "chest_front" || name == "chest_side" || name == "chest_top") return ChestFace(name);
            if (!McMap.TryGetValue(name, out var map)) return null;
            var tex = McAssets.LoadTexture(map.path);
            if (tex == null) return null;
            return FirstFrame(tex, map.tint);
        }

        /// <summary>
        /// A chest face from Minecraft's chest model texture (entity/chest/normal, 64x64): the lid's side (14x5x14 box at
        /// 0,0) over the base's (14x10x14 box at 0,19), the latch (2x4 at 1,1) on the front; the top is the lid's outside.
        /// Stretched from 14 px to the block's 16.
        /// </summary>
        static Color32[] ChestFace(string name)
        {
            var t = McAssets.LoadTexture("entity/chest/normal");
            if (t == null) return null;
            int sc = Mathf.Max(1, t.width / 64);
            Color32 P(int u, int v) => t.GetPixel(u * sc, t.height - 1 - v * sc);
            int w = 14, h = name == "chest_top" ? 14 : 15;
            var src = new Color32[w * h]; // row 0 = top
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c;
                    if (name == "chest_top") c = P(28 + x, y);                       // the lid's outside (14,0 is its inside)
                    else
                    {
                        // the model is built upside down: in the texture each side strip's top row is its lower edge.
                        // The front is the strip with the latch notches (u 42), the side the one at u 0.
                        int u0 = name == "chest_front" ? 42 : 0;
                        c = y < 5 ? P(u0 + x, 18 - y) : P(u0 + x, 42 - (y - 5));      // lid side, then base side
                        if (name == "chest_front" && x >= 6 && x < 8 && y >= 2 && y < 6) c = P(1 + (x - 6), 1 + (y - 2)); // latch
                    }
                    src[y * w + x] = c;
                }
            Object.Destroy(t);
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    var c = src[(y * h / Tile) * w + x * w / Tile];
                    c.a = 255;
                    outp[(Tile - 1 - y) * Tile + x] = c; // our rows go bottom-up
                }
            return outp;
        }

        static Color32 Mul(Color32 a, Color32 t) => new Color32((byte)(a.r * t.r / 255), (byte)(a.g * t.g / 255), (byte)(a.b * t.b / 255), a.a);

        static Color32[] FirstFrame(Texture2D t, Color32? tint)
        {
            // animated textures are vertical strips; take the top frame. Scale HD packs down to 16x16.
            int w = t.width;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    int sx = x * w / Tile;
                    int sy = t.height - w + (y * w / Tile); // top frame, in texture (bottom-up) coords
                    var c = (Color32)t.GetPixel(sx, sy);
                    if (tint.HasValue) c = Mul(c, tint.Value);
                    outp[y * Tile + x] = c;
                }
            Object.Destroy(t);
            return outp;
        }

        static Color32[] ReadTile(Texture2D atlas, int index)
        {
            int col = index % Cols, row = index / Cols;
            int x0 = col * Tile, y0 = atlas.height - (row + 1) * Tile;
            var outp = new Color32[Tile * Tile];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                    outp[y * Tile + x] = atlas.GetPixel(x0 + x, y0 + y);
            return outp;
        }

        static void WriteTile(Texture2D atlas, int index, Color32[] px)
        {
            int col = index % Cols, row = index / Cols;
            int x0 = col * Tile, y0 = atlas.height - (row + 1) * Tile;
            atlas.SetPixels32(x0, y0, Tile, Tile, px);
        }

        public static Color32[] TilePixels(string name) => ReadTile(Texture, T(name));

        // ------------------------------------------------------------------ materials
        static Shader litShader, unlitShader;

        static void BuildMaterials()
        {
            litShader = Shader.Find("HDRP/Lit");
            unlitShader = Shader.Find("HDRP/Unlit");
            if (litShader == null) Plugin.Log.LogError("HDRP/Lit shader not found!");
            Opaque = MakeLit("LMC_Opaque", false, false, false);
            Cutout = MakeLit("LMC_Cutout", true, true, false);
            Emissive = MakeLit("LMC_Emissive", false, false, true);
            CutoutEmissive = MakeLit("LMC_CutoutEmissive", true, true, true);
            Crack = MakeLit("LMC_Crack", true, false, false);
            Crack.SetFloat("_AlphaCutoff", 0.1f);
            Particle = MakeLit("LMC_Particle", true, true, false);
            OpaqueAmb = MakeLit("LMC_OpaqueAmb", false, false, false);
            CutoutAmb = MakeLit("LMC_CutoutAmb", true, true, false);
            SetAmbient(AmbientEV);

            Outline = new Material(unlitShader ?? litShader) { name = "LMC_Outline" };
            Outline.SetColor("_UnlitColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            Outline.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 1f));
            HDMaterial.ValidateMaterial(Outline);
        }

        /// <summary>No decal lands on this renderer (its decal-layer bits cleared; its light layers stay): the moons' decals
        /// (leaf shadows, stains, blood) are meant for the game's surfaces. Our materials still "support" decals so that
        /// HDRP draws them in its early depth pass (#40).</summary>
        public static void NoDecals(Renderer r) { if (r != null) r.renderingLayerMask &= ~0xFF00u; }

        public static Material MakeLit(string name, bool cutout, bool doubleSided, bool emissive)
        {
            var m = new Material(litShader) { name = name };
            m.SetTexture("_BaseColorMap", Texture);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            if (cutout)
            {
                m.SetFloat("_AlphaCutoffEnable", 1f);
                m.SetFloat("_AlphaCutoff", 0.5f);
                HDMaterial.SetAlphaClipping(m, true);
                HDMaterial.SetAlphaCutoff(m, 0.5f);
            }
            if (doubleSided)
            {
                m.SetFloat("_DoubleSidedEnable", 1f);
                m.SetFloat("_CullMode", 0f);
                m.SetFloat("_CullModeForward", 0f);
            }
            if (emissive)
            {
                // Emission maps are stripped from the game's shader variants, so glow = albedo * intensity instead
                // (only glowing geometry uses these materials).
                m.SetFloat("_AlbedoAffectEmissive", 1f);
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, EmissiveEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                m.SetFloat("_EmissiveExposureWeight", 0.6f);
            }
            // Blocks take decals like the game's own surfaces (#40). Turned off, HDRP draws their depth after the decal pass
            // and the game's build has no shader variant without decals, so a stain on the floor behind a block (the
            // facility's puddle meshes) was painted onto the block's face: seen "through" it. On, blocks are in the early
            // depth pass and hide what's behind them. (No decal paints our renderers themselves: NoDecals.)
            m.SetFloat("_SupportDecals", 1f);
            HDMaterial.ValidateMaterial(m);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Faint albedo-tinted self-illumination so block sides aren't pitch black in shade (outdoors/ship only).</summary>
        public static void SetAmbient(float ev)
        {
            AmbientEV = ev;
            foreach (var m in new[] { OpaqueAmb, CutoutAmb })
            {
                m.SetFloat("_AlbedoAffectEmissive", 1f);
                HDMaterial.SetUseEmissiveIntensity(m, true);
                HDMaterial.SetEmissiveColor(m, Color.white);
                HDMaterial.SetEmissiveIntensity(m, ev, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            }
        }

        public static Material Ambient(Material m) => m == Opaque ? OpaqueAmb : m == Cutout ? CutoutAmb : m;

        public static Material ForRender(RenderKind k, bool emissiveState = false)
        {
            switch (k)
            {
                case RenderKind.Cutout: return emissiveState ? CutoutEmissive : Cutout;
                case RenderKind.Emissive: return Emissive;
                default: return emissiveState ? Emissive : Opaque;
            }
        }

        // ------------------------------------------------------------------ icons
        static void BuildIcons()
        {
            foreach (var b in Blocks.All)
            {
                if (b.Shape == BlockShape.PistonHead) continue;
                Texture2D tex = null;
                if (McAssets.Available)
                {
                    switch (b.Shape)
                    {
                        case BlockShape.Cube:
                            string front = b.Directional && b.TileFront != b.TileSide ? b.TileFront : b.TileSide;
                            if (b == Blocks.Piston || b == Blocks.StickyPiston) front = b.TileSide;
                            if (b == Blocks.RedstoneLamp) tex = IsoIcon("lamp_on", "lamp_on", "lamp_on");
                            else tex = IsoIcon(b.TileTop, front, b.TileSide);
                            break;
                        case BlockShape.Torch: tex = SpriteIcon(b == Blocks.RedstoneTorch ? "redstone_torch" : "item_torch"); break;
                        case BlockShape.Dust: tex = SpriteIcon("item_redstone_dust"); break;
                        case BlockShape.Lever: tex = SpriteIcon("lever_handle"); break;
                    }
                }
                if (tex == null) tex = LoadEmbeddedIcon(b.IconName);
                if (tex == null) tex = IsoIcon(b.TileTop, b.TileSide, b.TileSide);
                Icons[b.Key] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            foreach (var s in new[] { "pickaxe", "flint_and_steel" })
            {
                Texture2D tex = McAssets.Available ? SpriteIcon("item_" + s) : null;
                if (tex == null) tex = LoadEmbeddedIcon(s);
                Icons[s] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        public static Sprite IconFor(string key)
        {
            if (Icons.TryGetValue(key, out var s)) return s;
            var tex = Tiles.ContainsKey(key) ? SpriteIcon(key) : null;
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            Icons[key] = s;
            return s;
        }

        /// <summary>Upscaled flat 16x16 sprite with a 1px dark drop outline, 128x128.</summary>
        public static Texture2D SpriteIcon(string tile)
        {
            if (!Tiles.ContainsKey(tile)) return null;
            var px = TilePixels(tile);
            const int S = 128, scale = 7, off = 8;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var outp = new Color32[S * S];
            for (int y = 0; y < Tile; y++)
                for (int x = 0; x < Tile; x++)
                {
                    var c = px[y * Tile + x];
                    if (c.a < 20) continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            outp[(off + y * scale + dy) * S + off + x * scale + dx] = c;
                }
            tex.SetPixels32(outp);
            tex.Apply();
            return tex;
        }

        /// <summary>Software-rendered isometric cube icon (128x128) from three atlas tiles.</summary>
        public static Texture2D IsoIcon(string top, string left, string right)
        {
            const int S = 128;
            var tp = TilePixels(top); var lp = TilePixels(left); var rp = TilePixels(right);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var outp = new Color32[S * S];
            float cx = S / 2f, w = 55f, h = w * 0.5f, topY = 8f, sideH = 62f;
            // image y grows downward in these formulas; texture rows are bottom-up so flip at write time
            for (int py = 0; py < S; py++)
                for (int px = 0; px < S; px++)
                {
                    float X = px + 0.5f, Y = py + 0.5f;
                    Color32 c = default; bool hit = false;
                    // top face: diamond with corners (cx,topY) (cx+w,topY+h) (cx,topY+2h) (cx-w,topY+h)
                    {
                        // basis: u along (cx,topY)->(cx+w,topY+h), v along (cx,topY)->(cx-w,topY+h)
                        float dx = X - cx, dy = Y - topY;
                        // solve dx = u*w - v*w ; dy = u*h + v*h
                        float u = (dx / w + dy / h) * 0.5f, v = (dy / h - dx / w) * 0.5f;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1)
                        {
                            c = Sample(tp, u, v, 1f); hit = c.a > 10;
                        }
                    }
                    if (!hit && X < cx)
                    {
                        // left face: from (cx-w, topY+h) right-down to (cx, topY+2h), down sideH
                        float u = (X - (cx - w)) / w;
                        float yTop = topY + h + u * h;
                        float v = (Y - yTop) / sideH;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1) { c = Sample(lp, u, v, 0.72f); hit = c.a > 10; }
                    }
                    if (!hit && X >= cx)
                    {
                        float u = (X - cx) / w;
                        float yTop = topY + 2 * h - u * h;
                        float v = (Y - yTop) / sideH;
                        if (u >= 0 && u < 1 && v >= 0 && v < 1) { c = Sample(rp, u, v, 0.86f); hit = c.a > 10; }
                    }
                    if (hit) outp[(S - 1 - py) * S + px] = c;
                }
            tex.SetPixels32(outp);
            tex.Apply();
            return tex;
        }

        static Color32 Sample(Color32[] tile, float u, float v, float shade)
        {
            int x = Mathf.Clamp((int)(u * Tile), 0, Tile - 1);
            int y = Mathf.Clamp((int)(v * Tile), 0, Tile - 1);
            var c = tile[(Tile - 1 - y) * Tile + x]; // tile arrays are bottom-up rows
            return new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), c.a);
        }
    }
}
