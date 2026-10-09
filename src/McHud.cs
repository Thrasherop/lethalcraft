using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>GUI sprites: from the local Minecraft jar when available, otherwise generated pixel art.</summary>
    public static class HudAssets
    {
        public static Sprite Hotbar, Selection, HeartContainer, HeartContainerBlink, HeartFull, HeartHalf, HeartFullBlink, HeartHalfBlink,
            AbsorbFull, AbsorbHalf, ArmorFull, ArmorHalf, ArmorEmpty, FoodEmpty, FoodFull, FoodHalf, XpBack, XpFill, Crosshair, White;
        public static Sprite HandIcon;
        public static Texture2D FontTex;
        public static int[] GlyphWidth = new int[256];
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Hotbar = Mc("gui/sprites/hud/hotbar") ?? Gen(182, 22, GenHotbar);
            Selection = Mc("gui/sprites/hud/hotbar_selection") ?? Gen(24, 24, GenSelection);
            HeartContainer = Mc("gui/sprites/hud/heart/container") ?? Pattern(Heart, new Color32(30, 0, 0, 255), new Color32(30, 0, 0, 255), true);
            HeartContainerBlink = Mc("gui/sprites/hud/heart/container_blinking") ?? Pattern(Heart, new Color32(255, 255, 255, 255), new Color32(30, 0, 0, 255), true);
            HeartFull = Mc("gui/sprites/hud/heart/full") ?? Pattern(Heart, new Color32(220, 20, 20, 255), new Color32(255, 140, 140, 255), false);
            HeartHalf = Mc("gui/sprites/hud/heart/half") ?? Half(HeartFull);
            HeartFullBlink = Mc("gui/sprites/hud/heart/full_blinking") ?? HeartFull;
            HeartHalfBlink = Mc("gui/sprites/hud/heart/half_blinking") ?? HeartHalf;
            AbsorbFull = Mc("gui/sprites/hud/heart/absorbing_full") ?? Pattern(Heart, new Color32(230, 190, 20, 255), new Color32(255, 240, 140, 255), false);
            AbsorbHalf = Mc("gui/sprites/hud/heart/absorbing_half") ?? Half(AbsorbFull);
            ArmorFull = Mc("gui/sprites/hud/armor_full") ?? Pattern(Heart, new Color32(200, 200, 210, 255), new Color32(255, 255, 255, 255), false);
            ArmorHalf = Mc("gui/sprites/hud/armor_half") ?? Half(ArmorFull);
            ArmorEmpty = Mc("gui/sprites/hud/armor_empty") ?? Pattern(Heart, new Color32(40, 40, 40, 255), new Color32(40, 40, 40, 255), true);
            FoodEmpty = Mc("gui/sprites/hud/food_empty") ?? Pattern(Drumstick, new Color32(30, 20, 10, 255), new Color32(30, 20, 10, 255), true);
            FoodFull = Mc("gui/sprites/hud/food_full") ?? Pattern(Drumstick, new Color32(180, 100, 40, 255), new Color32(240, 220, 200, 255), false);
            FoodHalf = Mc("gui/sprites/hud/food_half") ?? Half(FoodFull, true);
            XpBack = Mc("gui/sprites/hud/experience_bar_background") ?? Gen(182, 5, (x, y) => (x == 0 || x == 181 || y == 0 || y == 4) ? new Color32(0, 0, 0, 255) : new Color32(30, 50, 20, 255));
            XpFill = Mc("gui/sprites/hud/experience_bar_progress") ?? Gen(182, 5, (x, y) => (y == 0 || y == 4) ? new Color32(0, 0, 0, 0) : new Color32(128, 255, 32, 255));
            Crosshair = Mc("gui/sprites/hud/crosshair") ?? Gen(15, 15, (x, y) => (x == 7 || y == 7) ? new Color32(255, 255, 255, 220) : new Color32(0, 0, 0, 0));
            White = Gen(2, 2, (x, y) => new Color32(255, 255, 255, 255));
            HandIcon = FindVanillaHandIcon() ?? White;

            FontTex = McAssets.Available ? McAssets.LoadTexture("font/ascii") : null;
            if (FontTex != null) ComputeGlyphWidths();
        }

        static Sprite FindVanillaHandIcon()
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<InteractTrigger>())
                if (t != null && t.hoverIcon != null && t.hoverIcon.name.ToLower().Contains("hand")) return t.hoverIcon;
            foreach (var t in Resources.FindObjectsOfTypeAll<InteractTrigger>())
                if (t != null && t.hoverIcon != null) return t.hoverIcon;
            return null;
        }

        static Sprite Mc(string path)
        {
            if (!McAssets.Available) return null;
            var t = McAssets.LoadTexture(path);
            if (t == null) return null;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 1f);
        }

        delegate Color32 PixelFn(int x, int y);

        static Sprite Gen(int w, int h, PixelFn fn)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(x, h - 1 - y); // fn uses top-down y
            t.SetPixels32(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
        }

        static Color32 GenHotbar(int x, int y)
        {
            if (x == 0 || x == 181 || y == 0 || y == 21) return new Color32(20, 20, 20, 230);
            int sx = (x - 1) % 20;
            if (sx == 0 || sx == 19 || y == 1 || y == 20) return new Color32(140, 140, 140, 230);
            if (sx == 1 || y == 2) return new Color32(60, 60, 60, 230);
            return new Color32(110, 110, 110, 200);
        }

        static Color32 GenSelection(int x, int y)
        {
            bool edge = x <= 1 || x >= 22 || y <= 1 || y >= 22;
            if (!edge) return new Color32(0, 0, 0, 0);
            return (x == 0 || x == 23 || y == 0 || y == 23) ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
        }

        static readonly string[] Heart =
        {
            ".OO...OO.",
            "OxxO.OxxO",
            "OxhxOxxxO",
            "OxxxxxxxO",
            "OxxxxxxxO",
            ".OxxxxxO.",
            "..OxxxO..",
            "...OxO...",
            "....O....",
        };

        static readonly string[] Drumstick =
        {
            "....OOO..",
            "...OxxxO.",
            "..OxhxxxO",
            "..OxxxxxO",
            ".OxxxxxO.",
            "OwOxxxO..",
            "OwwOOO...",
            ".OwO.....",
            "..O......",
        };

        static Sprite Pattern(string[] rows, Color32 fill, Color32 highlight, bool emptyStyle)
        {
            return Gen(9, 9, (x, y) =>
            {
                char c = rows[y][x];
                if (c == 'O') return new Color32(0, 0, 0, 255);
                if (c == 'x') return emptyStyle ? new Color32(40, 40, 40, 255) : fill;
                if (c == 'h') return emptyStyle ? new Color32(60, 60, 60, 255) : highlight;
                if (c == 'w') return emptyStyle ? new Color32(40, 40, 40, 255) : new Color32(230, 220, 200, 255);
                return new Color32(0, 0, 0, 0);
            });
        }

        static Sprite Half(Sprite full, bool rightHalf = false)
        {
            var src = full.texture;
            return Gen(src.width, src.height, (x, y) =>
            {
                bool keep = rightHalf ? x >= src.width / 2 : x <= src.width / 2;
                var c = (Color32)src.GetPixel(x, src.height - 1 - y);
                if (!keep && c.a > 0 && !(c.r == 0 && c.g == 0 && c.b == 0)) return new Color32(0, 0, 0, 0);
                return c;
            });
        }

        static void ComputeGlyphWidths()
        {
            int cell = FontTex.width / 16;
            for (int ch = 0; ch < 256; ch++)
            {
                int cx = (ch % 16) * cell, cyTop = (ch / 16) * cell;
                int w = 0;
                for (int x = cell - 1; x >= 0; x--)
                {
                    bool any = false;
                    for (int y = 0; y < cell; y++)
                        if (FontTex.GetPixel(cx + x, FontTex.height - 1 - (cyTop + y)).a > 0.1f) { any = true; break; }
                    if (any) { w = x + 1; break; }
                }
                GlyphWidth[ch] = ch == ' ' ? cell / 2 : w;
            }
        }

        static readonly Dictionary<char, Sprite> glyphs = new Dictionary<char, Sprite>();
        public static Sprite Glyph(char c)
        {
            if (FontTex == null) return null;
            if (glyphs.TryGetValue(c, out var s)) return s;
            int ch = c < 256 ? c : '?';
            int cell = FontTex.width / 16;
            int cx = (ch % 16) * cell, cy = FontTex.height - ((ch / 16) + 1) * cell;
            s = Sprite.Create(FontTex, new Rect(cx, cy, cell, cell), new Vector2(0, 0), 1f);
            glyphs[c] = s;
            return s;
        }
    }

    /// <summary>Pixel text drawn with Minecraft's bitmap font (or a legacy font fallback). Units are GUI pixels.</summary>
    public class PixelText : MonoBehaviour
    {
        string current;
        Color color = Color.white;
        readonly List<Image> pool = new List<Image>();
        Text fallback;
        public enum Align { Left, Center, Right }
        public Align Alignment = Align.Left;
        public bool Shadow = true;
        public bool Outline;

        public float Width { get; private set; }

        public void Set(string text, Color c)
        {
            if (text == current && c == color) return;
            current = text;
            color = c;
            Rebuild();
        }

        void Rebuild()
        {
            foreach (var i in pool) i.gameObject.SetActive(false);
            if (HudAssets.FontTex == null)
            {
                if (fallback == null)
                {
                    var go = new GameObject("text", typeof(RectTransform));
                    go.transform.SetParent(transform, false);
                    fallback = go.AddComponent<Text>();
                    fallback.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    fallback.fontSize = 8;
                    fallback.horizontalOverflow = HorizontalWrapMode.Overflow;
                    fallback.verticalOverflow = VerticalWrapMode.Overflow;
                    var sh = go.AddComponent<UnityEngine.UI.Shadow>();
                    sh.effectDistance = new Vector2(1, -1);
                    var rt = (RectTransform)go.transform;
                    rt.sizeDelta = new Vector2(200, 10);
                }
                fallback.text = current;
                fallback.color = color;
                fallback.alignment = Alignment == Align.Left ? TextAnchor.LowerLeft : Alignment == Align.Right ? TextAnchor.LowerRight : TextAnchor.LowerCenter;
                var frt = (RectTransform)fallback.transform;
                frt.pivot = Alignment == Align.Left ? new Vector2(0, 0) : Alignment == Align.Right ? new Vector2(1, 0) : new Vector2(0.5f, 0);
                frt.anchoredPosition = Vector2.zero;
                return;
            }
            // measure
            float w = 0;
            foreach (char c in current) w += HudAssets.GlyphWidth[c < 256 ? c : '?'] + 1;
            Width = Mathf.Max(0, w - 1);
            float x0 = Alignment == Align.Left ? 0 : Alignment == Align.Right ? -Width : -Mathf.Round(Width / 2f);
            int n = 0;
            var shadowCol = Outline ? new Color(0, 0, 0, color.a) : new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, color.a);
            Vector2[] shadowOffsets = Outline ? new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) } : Shadow ? new[] { new Vector2(1, -1) } : new Vector2[0];
            foreach (var off in shadowOffsets) n = Emit(x0, off, shadowCol, n);
            Emit(x0, Vector2.zero, color, n);
        }

        int Emit(float x0, Vector2 off, Color c, int n)
        {
            float x = x0;
            foreach (char ch in current)
            {
                int code = ch < 256 ? ch : '?';
                if (ch != ' ')
                {
                    var img = Get(n++);
                    img.sprite = HudAssets.Glyph(ch);
                    img.color = c;
                    var rt = img.rectTransform;
                    rt.anchoredPosition = new Vector2(x + off.x, off.y - 1);
                    rt.sizeDelta = new Vector2(8, 8);
                }
                x += HudAssets.GlyphWidth[code] + 1;
            }
            return n;
        }

        Image Get(int i)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject("g", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                pool.Add(img);
            }
            pool[i].gameObject.SetActive(true);
            return pool[i];
        }
    }

    /// <summary>The Minecraft HUD: hotbar with item counts, hearts, hunger, XP bar, crosshair and action-bar messages.</summary>
    public class McHud : MonoBehaviour
    {
        public static McHud Instance;
        Canvas canvas;
        CanvasGroup group;
        RectTransform root, crossRoot;
        Image hotbar, selection;
        readonly Image[] icons = new Image[9];
        readonly PixelText[] counts = new PixelText[9];
        readonly Image[] durBack = new Image[9], durFill = new Image[9];
        readonly Image[] hearts = new Image[10], heartBg = new Image[10];
        readonly Image[] absorb = new Image[10], absorbBg = new Image[10];
        readonly Image[] armor = new Image[10];
        readonly Image[] food = new Image[10], foodBg = new Image[10];
        readonly Image[] slotShade = new Image[9];
        Image xpBack, xpFill, crosshair, eatBar;
        PixelText xpText, toastText;
        static string toastMsg;
        static float toastTime = -10f;
        int scale = 3;
        float heartTick;
        bool vitalsShown = true;
        int[] heartJitter = new int[10];
        int[] foodJitter = new int[10];

        public static void Toast(string msg)
        {
            toastMsg = msg;
            toastTime = Time.time;
        }

        void Awake()
        {
            Instance = this;
            HudAssets.Load();
            var go = new GameObject("LMC_HUD", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            canvas.pixelPerfect = true;
            group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            root = NewRect("root", go.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);

            hotbar = Img("hotbar", HudAssets.Hotbar, -91, 0, 182, 22);
            for (int i = 0; i < 9; i++)
            {
                slotShade[i] = Img("shade" + i, HudAssets.White, -90 + i * 20, 1, 20, 20);
                slotShade[i].color = new Color(0, 0, 0, 0.55f);
                icons[i] = Img("icon" + i, null, -88 + i * 20, 3, 16, 16);
                icons[i].preserveAspect = true;
                durBack[i] = Img("durb" + i, HudAssets.White, -86 + i * 20, 4, 13, 2);
                durBack[i].color = Color.black;
                durFill[i] = Img("durf" + i, HudAssets.White, -86 + i * 20, 5, 12, 1);
                var ct = NewRect("count" + i, root);
                ct.anchoredPosition = new Vector2(-88 + i * 20 + 17, 3);
                counts[i] = ct.gameObject.AddComponent<PixelText>();
                counts[i].Alignment = PixelText.Align.Right;
            }
            selection = Img("selection", HudAssets.Selection, -92, -1, 24, HudAssets.Selection.rect.height);
            xpBack = Img("xpback", HudAssets.XpBack, -91, 24, 182, 5);
            xpFill = Img("xpfill", HudAssets.XpFill, -91, 24, 182, 5);
            xpFill.type = Image.Type.Filled;
            xpFill.fillMethod = Image.FillMethod.Horizontal;
            var xt = NewRect("xptext", root);
            xt.anchoredPosition = new Vector2(0, 27);
            xpText = xt.gameObject.AddComponent<PixelText>();
            xpText.Alignment = PixelText.Align.Center;
            xpText.Outline = true;
            xpText.Shadow = false;
            for (int i = 0; i < 10; i++)
            {
                heartBg[i] = Img("hb" + i, HudAssets.HeartContainer, -91 + i * 8, 30, 9, 9);
                hearts[i] = Img("h" + i, HudAssets.HeartFull, -91 + i * 8, 30, 9, 9);
                absorbBg[i] = Img("ab" + i, HudAssets.HeartContainer, -91 + i * 8, 40, 9, 9);
                absorb[i] = Img("a" + i, HudAssets.AbsorbFull, -91 + i * 8, 40, 9, 9);
                armor[i] = Img("ar" + i, HudAssets.ArmorEmpty, -91 + i * 8, 40, 9, 9);
                foodBg[i] = Img("fb" + i, HudAssets.FoodEmpty, 91 - 9 - i * 8, 30, 9, 9);
                food[i] = Img("f" + i, HudAssets.FoodFull, 91 - 9 - i * 8, 30, 9, 9);
            }
            eatBar = Img("eat", HudAssets.White, -20, 50, 40, 2);
            eatBar.color = new Color(1f, 0.85f, 0.4f, 0.9f);
            var tt = NewRect("toast", root);
            tt.anchoredPosition = new Vector2(0, 58);
            toastText = tt.gameObject.AddComponent<PixelText>();
            toastText.Alignment = PixelText.Align.Center;

            crossRoot = NewRect("cross", go.transform);
            crossRoot.anchorMin = crossRoot.anchorMax = new Vector2(0.5f, 0.5f);
            crossRoot.pivot = new Vector2(0.5f, 0.5f);
            crosshair = new GameObject("crosshair", typeof(RectTransform)).AddComponent<Image>();
            crosshair.transform.SetParent(crossRoot, false);
            crosshair.sprite = HudAssets.Crosshair;
            crosshair.raycastTarget = false;
            crosshair.rectTransform.sizeDelta = new Vector2(15, 15);
            crosshair.color = new Color(1, 1, 1, 0.75f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        Image Img(string name, Sprite s, float x, float y, float w, float h)
        {
            var rt = NewRect(name, root);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.raycastTarget = false;
            if (s == null) img.enabled = false;
            return img;
        }

        /// <summary>The HUD stays hidden until someone breaks a block (HideHudUntilBlockBroken); the server says when.</summary>
        public static bool Revealed;
        public static bool Concealed => Plugin.HideHudUntilBlockBroken.Value && !Revealed;
        /// <summary>Server: has anyone broken a block yet this session?</summary>
        public static bool ServerRevealed;

        /// <summary>Server: a block was broken: everyone's HUD shows from now on.</summary>
        public static void ServerBlockBroken()
        {
            if (ServerRevealed) return;
            ServerRevealed = true;
            BlockNet.ServerHudReveal(null);
        }

        bool ShouldShow(PlayerControllerB p)
        {
            if (!Plugin.MinecraftHud.Value || p == null) return false;
            if (Concealed) return false;
            if (p.isPlayerDead || !p.isPlayerControlled) return false;
            if (p.inTerminalMenu) return false;
            if (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen) return false;
            var hud = HUDManager.Instance;
            if (hud == null || hud.hudHidden || (hud.HUDContainer != null && !hud.HUDContainer.activeInHierarchy)) return false;
            return true;
        }

        void LateUpdate()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            bool show = ShouldShow(p);
            group.alpha = show ? 1f : 0f;
            // hide the vanilla inventory boxes (our hotbar replaces them)
            var hud = HUDManager.Instance;
            if (hud != null && Plugin.MinecraftHud.Value && !Concealed)
            {
                if (hud.Inventory?.canvasGroup != null) hud.Inventory.canvasGroup.alpha = 0f;
                if (Plugin.HideVanillaHealth.Value && hud.PlayerInfo?.canvasGroup != null) hud.PlayerInfo.canvasGroup.alpha = 0f;
            }
            if (!show) return;

            int s = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 360f), 1, 8);
            if (s != scale) scale = s;
            root.localScale = Vector3.one * scale;
            crossRoot.localScale = Vector3.one * scale * 0.8f;
            root.anchoredPosition = Vector2.zero;

            UpdateHotbar(p);
            UpdateVitals(p);

            float tAge = Time.time - toastTime;
            if (tAge < 2.5f && !string.IsNullOrEmpty(toastMsg))
            {
                float a = tAge < 2f ? 1f : 1f - (tAge - 2f) / 0.5f;
                toastText.gameObject.SetActive(true);
                toastText.Set(toastMsg, new Color(1, 1, 1, a));
            }
            else toastText.gameObject.SetActive(false);

            var sv = Survival.Instance;
            bool eating = sv != null && sv.Eating && sv.EatProgress > 0.02f;
            eatBar.enabled = eating;
            if (eating) eatBar.rectTransform.sizeDelta = new Vector2(40f * sv.EatProgress, 2);
        }

        void UpdateHotbar(PlayerControllerB p)
        {
            int slots = p.ItemSlots.Length;
            int sel = p.currentItemSlot;
            selection.enabled = sel >= 0 && sel < 9;
            if (selection.enabled) selection.rectTransform.anchoredPosition = new Vector2(-92 + sel * 20, -1);
            for (int i = 0; i < 9; i++)
            {
                slotShade[i].enabled = i >= slots;
                var item = i < slots ? p.ItemSlots[i] : null;
                if (item == null || item.itemProperties == null)
                {
                    icons[i].enabled = false;
                    counts[i].gameObject.SetActive(false);
                    durBack[i].enabled = durFill[i].enabled = false;
                    continue;
                }
                icons[i].enabled = true;
                icons[i].sprite = item.itemProperties.itemIcon;
                if (item is StackItem st && st.Count != 1)
                {
                    counts[i].gameObject.SetActive(true);
                    counts[i].Set(st.Count.ToString(), Color.white);
                }
                else if (item.itemProperties.isScrap && item.scrapValue > 0)
                {
                    counts[i].gameObject.SetActive(true);
                    counts[i].Set("$" + item.scrapValue, new Color(0.55f, 1f, 0.45f));
                }
                else counts[i].gameObject.SetActive(false);
                // a worn tool: Minecraft's bar, green to red (#48)
                if (item is ToolItem tool && tool.Used > 0 && tool.MaxUses > 0)
                {
                    durBack[i].enabled = durFill[i].enabled = true;
                    float left = 1f - tool.Wear;
                    durFill[i].rectTransform.sizeDelta = new Vector2(Mathf.Max(1f, Mathf.Round(12f * left)), 1f);
                    durFill[i].color = Color.HSVToRGB(left / 3f, 1f, 1f);
                    continue;
                }
                // battery as a durability bar
                bool battery = item.itemProperties.requiresBattery && item.insertedBattery != null;
                durBack[i].enabled = durFill[i].enabled = battery;
                if (battery)
                {
                    float c = Mathf.Clamp01(item.insertedBattery.charge);
                    durFill[i].rectTransform.sizeDelta = new Vector2(Mathf.Round(13 * c), 1);
                    durFill[i].color = Color.HSVToRGB(c / 3f, 1f, 1f);
                }
            }
        }

        void UpdateVitals(PlayerControllerB p)
        {
            var sv = Survival.Instance;
            // creative, like Minecraft: no hearts, hunger or XP bar
            bool show = !GameModes.LocalCreative;
            if (vitalsShown != show)
            {
                vitalsShown = show;
                foreach (var set in new[] { hearts, heartBg, absorb, absorbBg, armor, food, foodBg })
                    foreach (var g in set) if (g != null) g.gameObject.SetActive(show);
                foreach (var g in new Component[] { xpBack, xpFill, xpText }) if (g != null) g.gameObject.SetActive(show);
            }
            if (!show) return;
            int hp = Mathf.CeilToInt(Mathf.Clamp(p.health, 0, 100) / 5f); // half hearts 0..20
            heartTick += Time.deltaTime;
            bool tick = false;
            if (heartTick > 0.05f) { heartTick = 0; tick = true; }
            bool blinking = sv != null && Time.time - sv.LastHurtTime < 0.6f && ((int)((Time.time - sv.LastHurtTime) * 6f)) % 2 == 0;
            bool regen = sv != null && Time.time < sv.RegenBoostUntil;
            int regenWave = regen ? (int)(Time.time * 20f) % 15 : -1;
            for (int i = 0; i < 10; i++)
            {
                if (tick)
                {
                    heartJitter[i] = hp <= 4 ? Random.Range(0, 2) : 0;
                    foodJitter[i] = sv != null && Plugin.HungerEnabled.Value && sv.Saturation <= 0 && Random.Range(0, 3 * (sv.Hunger + 1)) == 0 ? Random.Range(-1, 2) : 0;
                }
                int y = 30 + heartJitter[i] + (i == regenWave ? 2 : 0);
                heartBg[i].rectTransform.anchoredPosition = new Vector2(-91 + i * 8, y);
                hearts[i].rectTransform.anchoredPosition = new Vector2(-91 + i * 8, y);
                heartBg[i].sprite = blinking ? HudAssets.HeartContainerBlink : HudAssets.HeartContainer;
                int v = hp - i * 2;
                if (v >= 2) { hearts[i].enabled = true; hearts[i].sprite = blinking ? HudAssets.HeartFullBlink : HudAssets.HeartFull; }
                else if (v == 1) { hearts[i].enabled = true; hearts[i].sprite = blinking ? HudAssets.HeartHalfBlink : HudAssets.HeartHalf; }
                else hearts[i].enabled = false;

                int ab = sv != null ? Mathf.CeilToInt(sv.Absorption / 5f) : 0;
                int av = ab - i * 2;
                absorbBg[i].enabled = av > 0;
                absorb[i].enabled = av > 0;
                if (av > 0) absorb[i].sprite = av >= 2 ? HudAssets.AbsorbFull : HudAssets.AbsorbHalf;

                // armor points, like Minecraft: a row above the hearts (above the golden ones when you have any), only while worn
                int pts = Armor.PointsOf(Armor.Local);
                armor[i].enabled = pts > 0;
                if (pts > 0)
                {
                    int rv = pts - i * 2;
                    armor[i].sprite = rv >= 2 ? HudAssets.ArmorFull : rv == 1 ? HudAssets.ArmorHalf : HudAssets.ArmorEmpty;
                    armor[i].rectTransform.anchoredPosition = new Vector2(-91 + i * 8, ab > 0 ? 50 : 40);
                }

                bool hungerOn = Plugin.HungerEnabled.Value && sv != null;
                foodBg[i].enabled = hungerOn;
                int fv = hungerOn ? sv.Hunger - i * 2 : 0;
                food[i].enabled = hungerOn && fv >= 1;
                if (hungerOn)
                {
                    int fy = 30 + foodJitter[i];
                    foodBg[i].rectTransform.anchoredPosition = new Vector2(91 - 9 - i * 8, fy);
                    food[i].rectTransform.anchoredPosition = new Vector2(91 - 9 - i * 8, fy);
                    if (fv >= 2) food[i].sprite = HudAssets.FoodFull;
                    else if (fv == 1) food[i].sprite = HudAssets.FoodHalf;
                }
            }
            int lvl = sv != null ? sv.XpLevel : 0;
            xpFill.fillAmount = sv != null ? Mathf.Clamp01(sv.XpFraction) : 0;
            xpText.gameObject.SetActive(lvl > 0);
            if (lvl > 0) xpText.Set(lvl.ToString(), new Color(0.5f, 1f, 0.125f));
        }
    }
}
