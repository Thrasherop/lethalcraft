using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The loom (#225, the owner's), Minecraft's LoomScreen: a banner, a dye and (if you have one) a banner pattern item go
    /// in; the patterns it can weave show in a grid (with no pattern item, Minecraft's 32 that need none; with one, its
    /// own); pick one and the banner comes out with that pattern in the dye's colour over what it had (six layers at
    /// most). The banner and one dye are used; the pattern item stays. The new design goes to the host, which gives the
    /// patterned banner back (BannerDesigns). What's left in the slots goes back to the inventory when it closes.
    /// The pattern items: crafted from paper and their item (flower: an oxeye daisy; creeper: a creeper head; skull: a
    /// wither skeleton skull; field masoned: bricks; bordure indented: vines), the rest (Mojang, globe, snout, flow,
    /// guster) found in the Nether.
    /// </summary>
    public class LoomUI : SlotScreen
    {
        public static LoomUI Instance;
        const int AreaBanner = 10, AreaDye = 11, AreaPattern = 12, AreaPick = 13, AreaOut = 14, AreaScroll = 15;
        public static readonly string[] Plain =
        {
            "square_bottom_left", "square_bottom_right", "square_top_left", "square_top_right", "stripe_bottom", "stripe_top", "stripe_left", "stripe_right",
            "stripe_center", "stripe_middle", "stripe_downright", "stripe_downleft", "small_stripes", "cross", "straight_cross", "triangle_bottom", "triangle_top",
            "triangles_bottom", "triangles_top", "diagonal_left", "diagonal_up_right", "diagonal_up_left", "diagonal_right", "circle", "rhombus", "half_vertical",
            "half_horizontal", "half_vertical_right", "half_horizontal_bottom", "border", "gradient", "gradient_up",
        };
        /// <summary>The banner pattern items and the pattern each weaves.</summary>
        public static readonly Dictionary<string, string> PatternItems = new Dictionary<string, string>
        {
            ["flower_banner_pattern"] = "flower", ["creeper_banner_pattern"] = "creeper", ["skull_banner_pattern"] = "skull", ["mojang_banner_pattern"] = "mojang",
            ["globe_banner_pattern"] = "globe", ["piglin_banner_pattern"] = "piglin", ["flow_banner_pattern"] = "flow", ["guster_banner_pattern"] = "guster",
            ["field_masoned_banner_pattern"] = "bricks", ["bordure_indented_banner_pattern"] = "curly_border",
        };
        public static int Woven; // (dev/tests)

        BlockKey loom;
        string bannerKey; int dyeCount; string dyeKey; string patternItem;
        int picked = -1, scroll;
        View bannerView, dyeView, patternView, outView;
        readonly Image[] pick = new Image[16];
        readonly RawImage[] pickArt = new RawImage[16];
        RawImage preview;

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }
        protected override Vector2 PanelSize => new Vector2(176, 166);
        protected override string Title => "Loom";
        protected override int HotbarTop => 141;
        protected override bool Usable(string key) => IsBanner(key) || Dyes.IndexOf(key) >= 0 || (key != null && PatternItems.ContainsKey(key));

        static bool IsBanner(string key) => key != null && (BannerDesigns.IdOf(key) > 0 || (key.EndsWith("_banner") && key != Banners.OminousKey && System.Array.IndexOf(Dyes.Colors, key.Substring(0, key.Length - 7)) >= 0));

        /// <summary>A banner key's design so far.</summary>
        static string SpecOf(string key)
        {
            int id = BannerDesigns.IdOf(key);
            if (id > 0) return BannerDesigns.Designs.TryGetValue(id, out var s) ? s : null;
            return key != null && key.EndsWith("_banner") ? System.Array.IndexOf(Dyes.Colors, key.Substring(0, key.Length - 7)) + ";" : null;
        }

        List<string> Choices => patternItem != null && PatternItems.TryGetValue(patternItem, out var p) ? new List<string> { p } : Plain.ToList();

        string Result()
        {
            var spec = SpecOf(bannerKey);
            var choices = Choices;
            if (spec == null || dyeKey == null || dyeCount <= 0 || picked < 0 || picked >= choices.Count) return null;
            var (b, layers) = BannerDesigns.Parse(spec);
            if (layers.Count >= BannerDesigns.MaxLayers) return null;
            layers.Add((choices[picked], Dyes.IndexOf(dyeKey)));
            return BannerDesigns.Spec(b, layers);
        }

        protected override void LayoutContent()
        {
            bannerView = MakeSlot(panel, new Vector2(12, -25), AreaBanner, 0);
            dyeView = MakeSlot(panel, new Vector2(32, -25), AreaDye, 0);
            patternView = MakeSlot(panel, new Vector2(22, -44), AreaPattern, 0);
            outView = MakeSlot(panel, new Vector2(142, -56), AreaOut, 0, true);
            // the patterns: four by four, fourteen pixels each (Minecraft's), the rest a scroll away
            for (int i = 0; i < 16; i++)
            {
                var rt = Rect("pick", panel, new Vector2(60 + (i % 4) * 14, -(13 + (i / 4) * 14)), new Vector2(14, 14));
                pick[i] = rt.gameObject.AddComponent<Image>(); pick[i].sprite = slotSprite; pick[i].type = Image.Type.Sliced;
                rt.gameObject.AddComponent<Handler>().Init(this, AreaPick, i);
                var art = Rect("art", rt, new Vector2(3, -1), new Vector2(8, 12));
                pickArt[i] = art.gameObject.AddComponent<RawImage>(); pickArt[i].raycastTarget = false;
            }
            var sc = Rect("scroll", panel, new Vector2(119, -13), new Vector2(12, 56));
            var sci = sc.gameObject.AddComponent<Image>(); sci.sprite = slotSprite; sci.type = Image.Type.Sliced;
            sc.gameObject.AddComponent<Handler>().Init(this, AreaScroll, 0);
            var pv = Rect("preview", panel, new Vector2(142, -10), new Vector2(20, 40));
            preview = pv.gameObject.AddComponent<RawImage>(); preview.raycastTarget = false;
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            ui.loom = k; ui.bannerKey = null; ui.dyeKey = null; ui.dyeCount = 0; ui.patternItem = null; ui.picked = -1; ui.scroll = 0;
            ui.Show();
        }

        protected override void OnClosing()
        {
            if (bannerKey != null) Inventory.Give(bannerKey, 1);
            if (dyeKey != null && dyeCount > 0) Inventory.Give(dyeKey, dyeCount);
            if (patternItem != null) Inventory.Give(patternItem, 1);
            bannerKey = dyeKey = patternItem = null; dyeCount = 0;
        }

        protected override void OnUpdate()
        {
            var p = Local; var w = BlockWorld.Instance;
            if (w == null || w.DefAt(loom) != Blocks.Loom) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(loom)) > 8f) Close();
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            switch (area)
            {
                case AreaBanner: Single(ref bannerKey, IsBanner, shift); picked = -1; break;
                case AreaPattern: Single(ref patternItem, k => k != null && PatternItems.ContainsKey(k), shift); picked = -1; scroll = 0; break;
                case AreaDye:
                    if (cursorKey == null) { if (dyeCount > 0) { int n = right ? (dyeCount + 1) / 2 : dyeCount; if (shift) Inventory.Give(dyeKey, n); else ToCursor(dyeKey, n); dyeCount -= n; if (dyeCount <= 0) dyeKey = null; } }
                    else if (Dyes.IndexOf(cursorKey) >= 0 && (dyeKey == null || dyeKey == cursorKey))
                    { int n = Mathf.Min(right ? 1 : cursorCount, 64 - dyeCount); dyeKey = cursorKey; dyeCount += n; TakeCursor(n); }
                    break;
                case AreaPick:
                    { int i = scroll * 4 + index; if (i < Choices.Count && SpecOf(bannerKey) != null) { picked = i; Sounds.Play2D("loom.select", 0.6f, 1f); } }
                    break;
                case AreaScroll:
                    { int rows = (Choices.Count + 3) / 4; scroll = rows <= 4 ? 0 : (scroll + (right ? -1 : 1) + (rows - 3)) % (rows - 3); }
                    break;
                case AreaOut:
                    {
                        var spec = Result();
                        if (spec == null || cursorKey != null) break;
                        // the banner and a dye are used; the host registers the design and gives the banner back
                        bannerKey = null; dyeCount--; if (dyeCount <= 0) dyeKey = null; picked = -1;
                        BlockNet.RequestLoom(spec);
                        Sounds.Play2D("loom.take", 0.8f, 1f);
                        Woven++;
                    }
                    break;
            }
            Refresh();
        }

        void Single(ref string slot, System.Func<string, bool> ok, bool shift)
        {
            if (cursorKey == null) { if (slot != null) { if (shift) Inventory.Give(slot, 1); else ToCursor(slot, 1); slot = null; } }
            else if (ok(cursorKey) && cursorCount >= 1)
            {
                var old = slot; slot = cursorKey; TakeCursor(1);
                if (old != null) ToCursor(old, 1);
            }
        }

        protected override int QuickMoveIn(string key, int n)
        {
            if (bannerKey == null && IsBanner(key)) { bannerKey = key; return n - 1; }
            if (Dyes.IndexOf(key) >= 0 && (dyeKey == null || dyeKey == key)) { int t = Mathf.Min(n, 64 - dyeCount); dyeKey = key; dyeCount += t; return n - t; }
            if (patternItem == null && PatternItems.ContainsKey(key)) { patternItem = key; return n - 1; }
            return n;
        }

        static readonly Dictionary<string, Texture2D> art = new Dictionary<string, Texture2D>();
        /// <summary>A pattern's button picture (Minecraft's loom: the pattern over a plain banner), its cloth cropped.</summary>
        static Texture2D Art(string pattern)
        {
            if (art.TryGetValue(pattern, out var t) && t != null) return t;
            return art[pattern] = Banners.TextureFor("8;" + pattern + ":15");
        }

        static readonly Rect Cloth = new Rect(1f / 64f, 23f / 64f, 20f / 64f, 40f / 64f); // (the flag's front: u 1-21, v 1-41 from the top)

        protected override void RefreshContent()
        {
            Show(bannerView, bannerKey != null ? IconOf(bannerKey) : null, bannerKey != null ? 1 : 0, 1f, bannerKey);
            Show(dyeView, dyeKey != null ? IconOf(dyeKey) : null, dyeCount);
            Show(patternView, patternItem != null ? IconOf(patternItem) : null, patternItem != null ? 1 : 0);
            var spec = Result();
            Show(outView, spec != null ? IconOf(BannerDesigns.BlockKeyFor(BannerDesigns.Parse(spec).baseColor)) : null, spec != null ? 1 : 0);
            var choices = Choices;
            bool can = SpecOf(bannerKey) != null && dyeCount > 0;
            for (int i = 0; i < 16; i++)
            {
                int c = scroll * 4 + i;
                bool there = c < choices.Count;
                pick[i].gameObject.SetActive(there);
                if (!there) continue;
                pickArt[i].texture = Art(choices[c]); pickArt[i].uvRect = Cloth;
                pickArt[i].color = can ? Color.white : new Color(1, 1, 1, 0.4f);
                pick[i].color = c == picked ? new Color(0.6f, 0.9f, 0.6f) : Color.white;
            }
            var show = spec ?? SpecOf(bannerKey);
            preview.texture = show != null ? Banners.TextureFor(show) : null; preview.uvRect = Cloth;
            preview.enabled = show != null;
        }

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaBanner && bannerKey != null) return Crafting.NameOf(bannerKey);
            if (area == AreaDye && dyeKey != null) return Crafting.NameOf(dyeKey);
            if (area == AreaPattern && patternItem != null) return Crafting.NameOf(patternItem);
            if (area == AreaPick) { int c = scroll * 4 + index; var ch = Choices; if (c < ch.Count) return ch[c].Replace('_', ' '); }
            if (area == AreaOut && Result() != null) return "Banner";
            if (area == AreaScroll) return "More patterns";
            return null;
        }

        protected override string DevContent() => $"loom={loom.Pos} banner={bannerKey ?? "-"} dye={dyeKey ?? "-"}x{dyeCount} pattern={patternItem ?? "-"} picked={picked} result={Result() ?? "-"}";
        protected override int DevArea(string name) => name == "banner" ? AreaBanner : name == "dye" ? AreaDye : name == "pattern" ? AreaPattern : name == "pick" ? AreaPick : name == "out" ? AreaOut : -1;

        /// <summary>Host: someone wove a design: register it and give them the banner.</summary>
        public static void ServerWove(ulong sender, string spec)
        {
            var (b, layers) = BannerDesigns.Parse(spec);
            if (b < 0 || b > 15 || layers.Count == 0 || layers.Count > BannerDesigns.MaxLayers) return;
            if (layers.Any(l => l.color < 0 || l.color > 15 || !(Plain.Contains(l.pattern) || PatternItems.ContainsValue(l.pattern)))) return;
            int id = BannerDesigns.ServerRegister(spec);
            Inventory.ServerSpawnFor(sender, ItemData.With(BannerDesigns.ItemKey, id), 1, true);
        }
    }

    public static partial class Blocks
    {
        public static BlockDef Loom;
        static void AddLoom()
        {
            Loom = AddW("loom", "Loom", "loom_side", 2.5f, ToolKind.Axe, x =>
            {
                x.TileTop = "loom_top"; x.TileBottom = "loom_bottom"; x.TileFront = "loom_front"; x.Directional = true; x.FacingIncludesVertical = false;
                x.Description = "Weaves patterns onto banners: a banner, a dye, and a banner pattern if you have one. [E] to use it.";
            });
        }
    }
}
