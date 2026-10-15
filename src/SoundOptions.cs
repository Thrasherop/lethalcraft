using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// #219: Minecraft's "Music &amp; Sound Options": a volume for each of Minecraft's sound categories (Jukebox/Note Blocks for
    /// the music discs, Blocks, Hostile Creatures, Friendly Creatures, Players, Weather, Ambient/Environment, and a Master
    /// over them), each player's own (BepInEx config, [Sound]), on every sound this mod plays. The screen is Minecraft's
    /// (SoundOptionsScreen: the title, Master Volume across the top, the rest two by two, Done), with its slider sprites
    /// and its font, opened from a button on the pause menu.
    /// </summary>
    [HarmonyPatch]
    public static class SoundOptions
    {
        public enum Cat { Master, Records, Weather, Blocks, Hostile, Friendly, Players, Ambient }
        static readonly (Cat cat, string key, string label)[] cats =
        {
            (Cat.Master, "MasterVolume", "Master Volume"), (Cat.Records, "JukeboxVolume", "Jukebox/Note Blocks"),
            (Cat.Weather, "WeatherVolume", "Weather"), (Cat.Blocks, "BlocksVolume", "Blocks"),
            (Cat.Hostile, "HostileVolume", "Hostile Creatures"), (Cat.Friendly, "FriendlyVolume", "Friendly Creatures"),
            (Cat.Players, "PlayersVolume", "Players"), (Cat.Ambient, "AmbientVolume", "Ambient/Environment"),
        };
        static readonly Dictionary<Cat, ConfigEntry<int>> entries = new Dictionary<Cat, ConfigEntry<int>>();

        static ConfigEntry<int> Entry(Cat c)
        {
            if (entries.TryGetValue(c, out var e)) return e;
            foreach (var (cat, key, label) in cats)
                if (cat == c)
                    entries[c] = e = Plugin.Instance.Config.Bind("Sound", key, 100, new ConfigDescription($"Your own volume for Minecraft's \"{label}\" sounds from this mod, 0-100 (Minecraft's Music & Sound Options; also from the pause menu).", new AcceptableValueRange<int>(0, 100)));
            return e;
        }

        // ((object): the plugin's GameObject is destroyed by the game, so Unity's == says null; the config is still there)
        public static float Get(Cat c) => Mathf.Clamp01(((object)Plugin.Instance != null ? Entry(c).Value : 100) / 100f);

        public static void Set(Cat c, float v)
        {
            Entry(c).Value = Mathf.RoundToInt(Mathf.Clamp01(v) * 100f);
            if (c == Cat.Master || c == Cat.Records) Jukebox.ApplyVolume();
        }

        // ------------------------------------------------------------------ which category a sound is
        static readonly HashSet<string> hostile = new HashSet<string> { "zombie", "husk", "drowned", "skeleton", "stray", "bogged", "wskel", "creeper", "fuse", "spider", "enderman", "endermite", "ghast", "blaze", "magma", "slime", "witch", "phantom", "piglin", "brute", "zpiglin", "hoglin" };
        static readonly HashSet<string> friendly = new HashSet<string> { "cow", "pig", "sheep", "chicken", "rabbit", "horse", "llama", "parrot", "wolf", "cat", "squid", "golem", "snowgolem", "trader", "lead", "minecart" };
        static readonly HashSet<string> players = new HashSet<string> { "hurt", "eat", "burp", "drink", "levelup", "orb", "attack", "crit", "swing", "hit", "bow", "bowhit", "armor", "elytra", "totem", "pearl", "snowball", "fish", "tool", "bucket", "bottle", "pop", "page" };
        static readonly HashSet<string> blocks = new HashSet<string> { "dig", "step", "place", "break", "click", "piston", "chest", "door", "portal", "ignite", "extinguish", "fire", "pumpkin", "till", "bonemeal", "smithing", "enchant", "brew", "bounce", "explode", "lava", "water", "note" };

        public static Cat CategoryOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return Cat.Master;
            int dot = id.IndexOf('.');
            string head = dot < 0 ? id : id.Substring(0, dot);
            if (head == "record" || head == "disc" || head == "jukebox") return Cat.Records;
            if (head == "thunder" || head == "rain") return Cat.Weather;
            if (head == "ambient" || head == "cave") return Cat.Ambient;
            if (hostile.Contains(head)) return Cat.Hostile;
            if (friendly.Contains(head)) return Cat.Friendly;
            if (players.Contains(head)) return Cat.Players;
            if (blocks.Contains(head)) return Cat.Blocks;
            return Cat.Master;
        }

        /// <summary>The volume factor for a sound: Master times its category (Minecraft's SoundEngine.getVolume).</summary>
        public static float Factor(string id)
        {
            var c = CategoryOf(id);
            return Get(Cat.Master) * (c == Cat.Master ? 1f : Get(c));
        }

        public static string DevState()
        {
            string Px(Transform t)
            {
                if (t == null) return "-";
                var r = (RectTransform)t; var c = r.TransformPoint(r.rect.center);
                var cv = r.GetComponentInParent<Canvas>()?.rootCanvas;
                var cam = cv != null && cv.renderMode != RenderMode.ScreenSpaceOverlay ? cv.worldCamera : null;
                var sp = RectTransformUtility.WorldToScreenPoint(cam, c);
                return $"{sp.x:F0},{sp.y:F0}";
            }
            var sb = new System.Text.StringBuilder($"open={Screen.IsOpen} button={(button != null && button.activeInHierarchy ? Px(button.transform) : "-")} ");
            foreach (var (cat, label, s) in Screen.Sliders) sb.Append($"{cat}={Mathf.RoundToInt(Get(cat) * 100)}%@{(s != null && s.gameObject.activeInHierarchy ? Px(s.transform) : "-")}:w{(s != null ? ((RectTransform)s.transform).rect.width : 0):F0} ");
            sb.Append("done=" + (Screen.DoneButton != null && Screen.DoneButton.activeInHierarchy ? Px(Screen.DoneButton.transform) : "-"));
            sb.Append($" | entries={string.Join(",", entries.Select(kv => kv.Key + ":" + kv.Value.Value))}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the pause menu's button
        static GameObject button;

        [HarmonyPatch(typeof(QuickMenuManager), nameof(QuickMenuManager.OpenQuickMenu)), HarmonyPostfix]
        static void AddButton(QuickMenuManager __instance)
        {
            try
            {
                if (button != null || __instance.mainButtonsPanel == null) return;
                var all = __instance.mainButtonsPanel.GetComponentsInChildren<Button>(true);
                if (all.Length < 2) return;
                // (a copy of the game's own button, under the lowest, at the same spacing)
                Button low = all[0], next = all[1];
                foreach (var b in all) if (((RectTransform)b.transform).anchoredPosition.y < ((RectTransform)low.transform).anchoredPosition.y) low = b;
                float step = Mathf.Abs(((RectTransform)all[0].transform).anchoredPosition.y - ((RectTransform)all[1].transform).anchoredPosition.y);
                if (step < 1f) step = 40f;
                button = Object.Instantiate(low.gameObject, low.transform.parent);
                button.name = "LMC_SoundOptions";
                var rt = (RectTransform)button.transform;
                rt.anchoredPosition = ((RectTransform)low.transform).anchoredPosition - new Vector2(0f, step);
                var b2 = button.GetComponent<Button>();
                b2.onClick = new Button.ButtonClickedEvent(); // (not the copied button's own)
                b2.onClick.AddListener(Screen.Open);
                var t = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (t != null) t.text = "> Minecraft sounds";
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Sound options button: " + e.Message); }
        }

        // ------------------------------------------------------------------ the screen
        public static class Screen
        {
            static GameObject root; static RectTransform panel;
            public static bool IsOpen => root != null && root.activeSelf;
            static readonly List<(Cat cat, string label, McSlider slider)> sliders = new List<(Cat, string, McSlider)>();
            public static List<(Cat cat, string label, McSlider slider)> Sliders => sliders;
            public static GameObject DoneButton;

            static float Scale => Mathf.Max(1f, Mathf.Round(UnityEngine.Screen.height / 360f));

            static GameObject hiddenPanel;

            public static void Open()
            {
                Build();
                if (root == null) return;
                root.SetActive(true);
                // (Minecraft's options screen takes the pause screen's place: the pause menu's own buttons away meanwhile)
                var qm = GameNetworkManager.Instance?.localPlayerController?.quickMenuManager;
                if (qm != null && qm.mainButtonsPanel != null && qm.mainButtonsPanel.activeSelf) { hiddenPanel = qm.mainButtonsPanel; hiddenPanel.SetActive(false); }
                foreach (var (cat, label, s) in sliders) s.SetValue(Get(cat), false);
                Sounds.Play2D("click", 0.25f, 1f);
            }

            public static void Close()
            {
                if (root != null) root.SetActive(false);
                if (hiddenPanel != null) { hiddenPanel.SetActive(true); hiddenPanel = null; }
            }

            /// <summary>Every frame (with the HUD): gone when the pause menu is (Escape).</summary>
            public static void Tick()
            {
                if (!IsOpen) return;
                var p = GameNetworkManager.Instance?.localPlayerController;
                if (p == null || p.quickMenuManager == null || !p.quickMenuManager.isMenuOpen) Close();
            }

            static void Build()
            {
                if (root != null || !McFont.Ready) return;
                float s = Scale;
                root = new GameObject("LMC_SoundOptions", typeof(RectTransform));
                Object.DontDestroyOnLoad(root);
                var c = root.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 30;
                root.AddComponent<GraphicRaycaster>();
                // (Minecraft's in-game menu background: the world darkened)
                var bg = new GameObject("bg", typeof(RectTransform)); bg.transform.SetParent(root.transform, false);
                var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
                var bimg = bg.AddComponent<Image>(); bimg.color = new Color(0.06f, 0.06f, 0.06f, 0.8f);
                panel = new GameObject("panel", typeof(RectTransform)).GetComponent<RectTransform>(); panel.SetParent(root.transform, false);
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f); panel.pivot = new Vector2(0.5f, 1f);
                panel.sizeDelta = new Vector2(310f * s, 300f * s); panel.anchoredPosition = Vector2.zero;
                // the title, centred in the 33-pixel header
                const string title = "Music & Sound Options";
                McFont.Draw(panel, title, Color.white, s, (310f - McFont.Width(title)) / 2f, 12f);
                // Master across the top, the rest two by two (rows 25 apart), as Minecraft's
                float y = 37f;
                sliders.Clear();
                sliders.Add((Cat.Master, "Master Volume", McSlider.Make(panel, 0f, y, 310f, s, "Master Volume", v => Set(Cat.Master, v))));
                y += 25f;
                for (int i = 1; i < cats.Length; i++)
                {
                    var (cat, key, label) = cats[i];
                    float x = (i - 1) % 2 == 0 ? 0f : 160f;
                    sliders.Add((cat, label, McSlider.Make(panel, x, y, 150f, s, label, v => Set(cat, v))));
                    if ((i - 1) % 2 == 1) y += 25f;
                }
                if ((cats.Length - 1) % 2 == 1) y += 25f;
                // Done
                DoneButton = McButton.Make(panel, 55f, y + 12f, 200f, s, "Done", Close).gameObject;
                root.SetActive(false);
            }
        }

        [HarmonyPatch(typeof(HUDManager), "Update"), HarmonyPostfix]
        static void HudTick() { try { Screen.Tick(); } catch { } }
    }

    /// <summary>Minecraft's GUI sprites made at their size on screen (#219): nine-sliced the way Minecraft does it (corners
    /// kept, edges and middle tiled; the border from the sprite's .mcmeta), each GUI pixel s screen pixels. Its grayscale
    /// sprites load as alpha only (Unity's Alpha8): read as grey.</summary>
    public static class McWidget
    {
        static readonly Dictionary<string, Sprite> made = new Dictionary<string, Sprite>();

        public static Sprite Sprite(string path, int border, int w, int h, int s)
        {
            string key = $"{path}:{w}x{h}@{s}";
            if (made.TryGetValue(key, out var sp)) return sp;
            var src = McAssets.Available ? McAssets.LoadTexture(path) : null;
            if (src == null) return null;
            bool alphaOnly = src.format == TextureFormat.Alpha8;
            var px = src.GetPixels32();
            int sw = src.width, sh = src.height;
            int Map(int g, int size, int srcSize)
            {
                if (g < border) return g;
                if (g >= size - border) return srcSize - (size - g);
                int inner = srcSize - 2 * border;
                return inner <= 0 ? border : border + (g - border) % inner;
            }
            var tex = new Texture2D(w * s, h * s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var outPx = new Color32[w * s * h * s];
            for (int gy = 0; gy < h; gy++)
                for (int gx = 0; gx < w; gx++)
                {
                    int sx = Map(gx, w, sw), syTop = Map(gy, h, sh);
                    var c = px[(sh - 1 - syTop) * sw + sx];
                    if (alphaOnly) c = new Color32(c.a, c.a, c.a, 255);
                    for (int dy = 0; dy < s; dy++)
                        for (int dx = 0; dx < s; dx++)
                            outPx[((h - 1 - gy) * s + dy) * w * s + gx * s + dx] = c;
                }
            tex.SetPixels32(outPx); tex.Apply();
            made[key] = sp = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f);
            return sp;
        }
    }

    /// <summary>Minecraft's slider (AbstractSliderButton): its track and handle sprites, the label centred on it, the value
    /// set by clicking or dragging along it.</summary>
    public class McSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        RectTransform rt, handle, textRoot; Image track, knob; float value; string label; System.Action<float> onChange; float s; bool hover;
        Sprite trackN, trackH, knobN, knobH;

        public static McSlider Make(RectTransform parent, float x, float y, float w, float s, string label, System.Action<float> onChange)
        {
            var go = new GameObject("slider " + label, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x * s, -y * s); rt.sizeDelta = new Vector2(w * s, 20f * s);
            var sl = go.AddComponent<McSlider>();
            sl.rt = rt; sl.s = s; sl.label = label; sl.onChange = onChange;
            int iw = Mathf.RoundToInt(w), si = Mathf.RoundToInt(s);
            sl.trackN = McWidget.Sprite("gui/sprites/widget/slider", 1, iw, 20, si); sl.trackH = McWidget.Sprite("gui/sprites/widget/slider_highlighted", 1, iw, 20, si);
            sl.knobN = McWidget.Sprite("gui/sprites/widget/slider_handle", 1, 8, 20, si); sl.knobH = McWidget.Sprite("gui/sprites/widget/slider_handle_highlighted", 1, 8, 20, si);
            sl.track = go.AddComponent<Image>(); sl.track.sprite = sl.trackN;
            if (sl.trackN == null) sl.track.color = new Color(0.1f, 0.1f, 0.1f);
            var h = new GameObject("handle", typeof(RectTransform)); h.transform.SetParent(rt, false);
            sl.handle = (RectTransform)h.transform; sl.handle.anchorMin = sl.handle.anchorMax = new Vector2(0f, 1f); sl.handle.pivot = new Vector2(0f, 1f);
            sl.handle.sizeDelta = new Vector2(8f * s, 20f * s);
            sl.knob = h.AddComponent<Image>(); sl.knob.sprite = sl.knobN; sl.knob.raycastTarget = false;
            if (sl.knobN == null) sl.knob.color = new Color(0.75f, 0.75f, 0.75f);
            var tr = new GameObject("text", typeof(RectTransform)); tr.transform.SetParent(rt, false);
            sl.textRoot = (RectTransform)tr.transform; sl.textRoot.anchorMin = sl.textRoot.anchorMax = new Vector2(0f, 1f); sl.textRoot.pivot = new Vector2(0f, 1f);
            return sl;
        }

        public void SetValue(float v, bool notify)
        {
            value = Mathf.Clamp01(v);
            float w = rt.sizeDelta.x / s;
            handle.anchoredPosition = new Vector2(value * (w - 8f) * s, 0f);
            foreach (Transform c in textRoot) Destroy(c.gameObject);
            int pct = Mathf.RoundToInt(value * 100f);
            string text = label + ": " + (pct == 0 ? "OFF" : pct + "%");
            McFont.Draw(textRoot, text, Color.white, s, (w - McFont.Width(text)) / 2f, 6f);
            if (notify) onChange?.Invoke(value);
        }

        void FromPointer(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, null, out var local)) return;
            float w = rt.sizeDelta.x;
            SetValue((local.x - 4f * s) / (w - 8f * s), true);
        }

        public void OnPointerDown(PointerEventData e) { FromPointer(e); Sounds.Play2D("click", 0.25f, 1f); }
        public void OnDrag(PointerEventData e) => FromPointer(e);
        public void OnPointerEnter(PointerEventData e) { hover = true; Paint(); }
        public void OnPointerExit(PointerEventData e) { hover = false; Paint(); }
        void Paint()
        {
            if (trackH != null) track.sprite = hover ? trackH : trackN;
            if (knobH != null) knob.sprite = hover ? knobH : knobN;
        }
    }

    /// <summary>Minecraft's button (its sprites, the label centred).</summary>
    public class McButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        Image img; System.Action onClick; Sprite sp, hi;

        public static McButton Make(RectTransform parent, float x, float y, float w, float s, string label, System.Action onClick)
        {
            int iw = Mathf.RoundToInt(w), si = Mathf.RoundToInt(s);
            var sp = McWidget.Sprite("gui/sprites/widget/button", 3, iw, 20, si); var hi = McWidget.Sprite("gui/sprites/widget/button_highlighted", 3, iw, 20, si);
            var go = new GameObject("button " + label, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x * s, -y * s); rt.sizeDelta = new Vector2(w * s, 20f * s);
            var b = go.AddComponent<McButton>(); b.onClick = onClick; b.sp = sp; b.hi = hi;
            b.img = go.AddComponent<Image>(); b.img.sprite = sp;
            if (sp == null) b.img.color = new Color(0.4f, 0.4f, 0.4f);
            McFont.Draw(rt, label, Color.white, s, (w - McFont.Width(label)) / 2f, 6f);
            return b;
        }

        public void OnPointerClick(PointerEventData e) { Sounds.Play2D("click", 0.25f, 1f); onClick?.Invoke(); }
        public void OnPointerEnter(PointerEventData e) { if (hi != null) img.sprite = hi; }
        public void OnPointerExit(PointerEventData e) { if (sp != null) img.sprite = sp; }
    }
}
