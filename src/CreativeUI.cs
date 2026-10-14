using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Creative mode's item menu ([I] in creative, instead of pocket crafting): every Minecraft item, in tabs, above your
    /// hotbar. Left-click an item for a full stack on the mouse, right-click for one more (or one), shift-click to put a
    /// full stack straight into the hotbar. Clicking the item grid while holding something puts it away (deletes it),
    /// like Minecraft. Hotbar slots work like every slot screen (see SlotScreen). What you take from here is a real item
    /// (the server spawns it for you), so it stays if you go back to survival.
    /// The last tab has the game's own equipment (the store's, plus shotguns, shells, knives...): a click puts one in your
    /// hotbar (they aren't stacks, so they don't go on the mouse).
    /// </summary>
    public class CreativeUI : SlotScreen
    {
        protected override bool WantsStorage => false; // (its own grid of every item; tall already)
        public static CreativeUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;

        const int AreaItems = 10, AreaTabs = 11;
        const int Cols = 9, Rows = 5, TabTop = -20, GridTop = 26; // (tabs above the panel and below it, like Minecraft's)
        // Minecraft's tabs (#177): its top row, then ours (the game's own things), then its bottom row
        static readonly string[] TabNames = { "Building Blocks", "Colored Blocks", "Natural Blocks", "Functional Blocks", "Redstone Blocks", "Lethal Company",
                                              "Tools & Utilities", "Combat", "Food & Drinks", "Ingredients", "Spawn Eggs", "Search Items" };
        const int VanillaTab = 5, EggTab = 10, TopRowTabs = 6, SearchTab = 11;
        /// <summary>Which of our tabs each of Minecraft's (CreativeOrder.Tabs) is.</summary>
        static readonly int[] OurTab = { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10 };
        /// <summary>Each tab's icon, Minecraft's (CreativeModeTabs' icon), when we have it.</summary>
        static readonly string[] TabIcons = { "bricks", "wool_cyan", "grass", "oak_sign", "redstone_dust", null, "pickaxe", "netherite_sword", "golden_apple", "iron_ingot", null, null };
        const string Lc = "lc:";
        /// <summary>The game's equipment that isn't sold in the store (scrap is left out: it's the Company's).</summary>
        static readonly string[] VanillaExtras = { "Shotgun", "Ammo", "Kitchen knife", "Key", "Homemade flashbang" };
        static List<string>[] tabs;
        int tab;
        int scrollRow; // (the first row of the tab shown: the wheel scrolls, like Minecraft's creative menu)
        Image knob;
        int MaxScroll => Mathf.Max(0, Mathf.CeilToInt(Shown.Count / (float)Cols) - Rows);
        string ItemAt(int slot) { var list = Shown; int i = scrollRow * Cols + slot; return slot >= 0 && i < list.Count ? list[i] : null; }

        // ------------------------------------------------------------------ the search tab (Minecraft's Search Items)
        /// <summary>What you've typed in the search tab, and the items it finds (every item, in the tabs' order, whose
        /// name has it in it; all of them when it's empty).</summary>
        string query = "";
        List<string> found;
        int backspaceFrame = -1;
        bool textHooked;
        Image fieldBg;
        PixelText fieldText;
        List<string> Shown => tab != SearchTab ? Tabs[tab] : found ?? (found = Search(query));
        /// <summary>The search tab is open: the keys type into its box (they don't close the menu or pick hotbar slots).</summary>
        public static bool Searching => IsOpen && Instance.tab == SearchTab;

        /// <summary>The name an entry is shown (and searched) by.</summary>
        static string NameOf(string key) => key.StartsWith(SpawnEggs.CreativePrefix) ? "Spawn Egg (" + SpawnEggs.NameOf(SpawnEggs.ByName(key.Substring(SpawnEggs.CreativePrefix.Length))) + ")"
            : key.StartsWith(Lc) ? key.Substring(Lc.Length) : Crafting.NameOf(key);

        static List<string> Search(string q)
        {
            var all = new List<string>();
            var seen = new HashSet<string>();
            for (int t = 0; t < SearchTab; t++)
                foreach (var k in Tabs[t]) if (seen.Add(k)) all.Add(k);
            q = q.Trim().ToLowerInvariant();
            if (q.Length == 0) return all;
            // (the name, or the id: "redstone" finds the dust, the torch, the lamp...)
            return all.Where(k => (NameOf(k) ?? "").ToLowerInvariant().Contains(q) || k.ToLowerInvariant().Replace('_', ' ').Contains(q) || k.ToLowerInvariant().Contains(q)).ToList();
        }

        void SetQuery(string q)
        {
            query = q.Length > 50 ? q.Substring(0, 50) : q;
            found = null; scrollRow = 0;
            Refresh();
        }

        /// <summary>Typed characters (the keyboard's text, so any layout and shift work as in a text box).</summary>
        void OnText(char c)
        {
            if (!Searching) return;
            if (c == '\b') { backspaceFrame = Time.frameCount; if (query.Length > 0) SetQuery(query.Substring(0, query.Length - 1)); return; }
            if (char.IsControl(c)) return;
            SetQuery(query + c);
        }

        static Sprite compass;
        static Sprite Compass()
        {
            if (compass != null) return compass;
            var t = McAssets.Available ? McAssets.LoadTexture("item/compass_16") ?? McAssets.LoadTexture("item/compass") : null;
            if (t == null) return null;
            t.filterMode = FilterMode.Point;
            // (an animated strip: the first frame)
            return compass = Sprite.Create(t, new Rect(0, t.height - t.width, t.width, t.width), new Vector2(0.5f, 0.5f));
        }
        readonly List<View> itemViews = new List<View>(), tabViews = new List<View>();

        protected override void Awake() { base.Awake(); Instance = this; ModelIcons.Ready += IconReady; }
        protected override void OnDestroy()
        {
            base.OnDestroy(); ModelIcons.Ready -= IconReady; if (Instance == this) Instance = null;
            if (textHooked && UnityEngine.InputSystem.Keyboard.current != null) UnityEngine.InputSystem.Keyboard.current.onTextInput -= OnText;
        }
        void IconReady() { if (open) Refresh(); } // (#178: an icon drawn from its model is ready)

        protected override Vector2 PanelSize => new Vector2(176 + ScrollW + 4, GridTop + Rows * Slot + 19 + Slot + 7);
        const int ScrollW = 12, ScrollX = 172; // (the scroll bar, right of the grid)
        protected override string Title => tab == SearchTab ? "" : TabNames[tab]; // (the search tab has its box there instead, like Minecraft's)
        protected override int HotbarTop => GridTop + Rows * Slot + 19;

        // Minecraft's place for each of its item ids: (our tab, position), every tab it's in (a redstone torch is in two)
        static Dictionary<string, List<(int tab, int pos)>> mcPlace;
        static List<(int tab, int pos)> PlacesOf(string mcId)
        {
            if (mcPlace == null)
            {
                mcPlace = new Dictionary<string, List<(int, int)>>();
                for (int t = 0; t < CreativeOrder.Tabs.Length; t++)
                {
                    var ids = CreativeOrder.Tabs[t].Split(' ');
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (!mcPlace.TryGetValue(ids[i], out var l)) mcPlace[ids[i]] = l = new List<(int, int)>();
                        l.Add((OurTab[t], i));
                    }
                }
            }
            return mcId != null && mcPlace.TryGetValue(mcId, out var p) ? p : null;
        }

        /// <summary>Minecraft's id for one of our items, where our key differs.</summary>
        public static string McId(string key)
        {
            if (CreativeAliases.Map.TryGetValue(key, out var id)) return id;
            if (key.StartsWith("wool_")) return key.Substring(5) + "_wool";
            if (key.StartsWith("potion_")) return "@potion";
            if (key.StartsWith("arrow_")) return "@tipped_arrow";
            return key;
        }

        /// <summary>Every item, in Minecraft's tabs and order (CreativeOrder); ours that Minecraft doesn't have go where
        /// their kind goes, after its own (ore scrap is the Company's, not a creative item).</summary>
        static List<string>[] Tabs
        {
            get
            {
                if (tabs != null) return tabs;
                var placed = Enumerable.Range(0, TabNames.Length).Select(_ => new List<(string key, int pos, int seq)>()).ToArray();
                int seq = 0;
                void Put(string key, int fallback)
                {
                    var places = PlacesOf(McId(key));
                    if (places != null) foreach (var (t, p) in places) placed[t].Add((key, p, seq++));
                    else placed[fallback].Add((key, 100000, seq++));
                }
                foreach (var b in Blocks.All)
                {
                    if (!ModItems.ByKey.ContainsKey(b.Key) || b == Blocks.Bedrock) continue;
                    bool redstone = b.IsRedstoneComponent || b == Blocks.Slime || b == Blocks.RedstoneBlock;
                    Put(b.Key, redstone ? 4 : 0);
                }
                foreach (var kv in ModItems.ByKey)
                {
                    var prefab = kv.Value.spawnPrefab;
                    // (ore scrap is the Company's; ender pearls count even when the config makes them scrap)
                    if (prefab == null || kv.Key.StartsWith("scrap_") || Blocks.Get(kv.Key) != null || kv.Key == "spawn_egg") continue; // (the eggs: their own tab)
                    int kind = prefab.GetComponent<ArmorItem>() != null ? 7 : prefab.GetComponent<ToolItem>() != null || prefab.GetComponent<FlintAndSteelItem>() != null ? 6
                        : ModItems.Foods.ContainsKey(kv.Key) ? 8 : 9;
                    Put(kv.Key, kind);
                }
                tabs = placed.Select(l => l.OrderBy(e => e.pos).ThenBy(e => e.seq).Select(e => e.key).ToList()).ToArray();
                // the game's own: what the store sells, then the rest of the equipment
                var store = Object.FindObjectOfType<Terminal>()?.buyableItemsList ?? new Item[0];
                // (the store also sells our blocks: those are in the other tabs)
                // then every other item the game knows (#86): the rest of the equipment, then scrap, other mods' too
                var rest = (StartOfRound.Instance != null ? StartOfRound.Instance.allItemsList.itemsList : new List<Item>())
                    .Where(it => it != null).OrderBy(it => it.isScrap).ThenBy(it => it.itemName);
                foreach (var it in store.Concat(VanillaExtras.Select(VanillaItem)).Concat(rest))
                    if (it != null && it.spawnPrefab != null && !string.IsNullOrEmpty(it.itemName) && !ModItems.ByKey.Values.Contains(it) && !tabs[VanillaTab].Contains(Lc + it.itemName)) tabs[VanillaTab].Add(Lc + it.itemName);
                // spawn eggs (#85): one per monster the game knows, in their own tab: Minecraft's mobs in its order, then the game's
                var eggs = SpawnEggs.All.Select((e, i) =>
                {
                    var p = PlacesOf(SpawnEggs.NameOf(e).ToLowerInvariant().Replace(' ', '_') + "_spawn_egg");
                    return (key: SpawnEggs.CreativePrefix + e.enemyName, pos: p != null ? p[0].pos : 100000 + i);
                }).OrderBy(e => e.pos).Select(e => e.key);
                tabs[EggTab].AddRange(eggs);
                if (store.Length == 0) { var t = tabs; tabs = null; return t; } // (no terminal yet: build the list again later)
                return tabs;
            }
        }

        /// <summary>One of the game's own items by name (not ours).</summary>
        public static Item VanillaItem(string name)
        {
            var all = StartOfRound.Instance != null ? StartOfRound.Instance.allItemsList.itemsList : null;
            return all?.FirstOrDefault(it => it != null && it.itemName == name && !ModItems.ByKey.Values.Contains(it));
        }

        static Sprite Icon(string key) => key != null && key.StartsWith(Lc) ? ModelIcons.IconOf(VanillaItem(key.Substring(Lc.Length)))
            : key != null && key.StartsWith(SpawnEggs.CreativePrefix) ? SpawnEggs.IconFor(SpawnEggs.ByName(key.Substring(SpawnEggs.CreativePrefix.Length))) : IconOf(key);

        public static void Open()
        {
            var ui = Instance;
            if (ui == null || !GameModes.LocalCreative || !CanOpen(Local)) return;
            ui.Show();
        }

        protected override void LayoutContent()
        {
            itemViews.Clear(); tabViews.Clear();
            // tab buttons: Minecraft's top row above the panel, its bottom row below it, each showing the tab's icon
            for (int t = 0; t < TabNames.Length; t++)
            {
                // (the search tab at the top row's far right, as Minecraft's)
                int col = t == SearchTab ? Cols - 1 : t < TopRowTabs ? t : t - TopRowTabs;
                float y = t < TopRowTabs || t == SearchTab ? -TabTop : -(PanelSize.y + 2);
                var v = MakeSlot(panel, new Vector2(4 + col * (Slot + 3), y), AreaTabs, t);
                tabViews.Add(v);
            }
            for (int i = 0; i < Cols * Rows; i++)
                itemViews.Add(MakeSlot(panel, new Vector2(8 + (i % Cols) * Slot, -(GridTop + (i / Cols) * Slot)), AreaItems, i));
            // the scroll bar: a track the height of the grid, and a knob showing where you are
            var track = Rect("scrolltrack", panel, new Vector2(ScrollX, -GridTop), new Vector2(ScrollW, Rows * Slot)).gameObject.AddComponent<Image>();
            track.color = new Color(0.33f, 0.33f, 0.33f); track.raycastTarget = false;
            knob = Rect("scrollknob", track.transform, Vector2.zero, new Vector2(ScrollW, 15)).gameObject.AddComponent<Image>();
            knob.raycastTarget = false;
            // the search tab's box: where the title is, black with a grey edge like Minecraft's text fields
            fieldBg = null; fieldText = null;
            if (tab == SearchTab)
            {
                var edge = Rect("searchedge", panel, new Vector2(80, -4), new Vector2(92, 14)).gameObject.AddComponent<Image>();
                edge.color = new Color(0.63f, 0.63f, 0.63f); edge.raycastTarget = false;
                fieldBg = Rect("searchbox", edge.transform, new Vector2(1, -1), new Vector2(90, 12)).gameObject.AddComponent<Image>();
                fieldBg.color = Color.black; fieldBg.raycastTarget = false;
                fieldText = Text(fieldBg.transform, new Vector2(2, -10), Color.white, true);
            }
        }

        protected override void RefreshContent()
        {
            if (fieldText != null)
            {
                // what's typed (its end, when it's longer than the box), and a blinking cursor
                string shown = query; while (shown.Length > 0 && shown.Length * 6 > 80) shown = shown.Substring(1);
                fieldText.Set(shown + ((int)(Time.unscaledTime * 3f) % 2 == 0 ? "_" : ""), Color.white);
            }
            scrollRow = Mathf.Clamp(scrollRow, 0, MaxScroll);
            for (int i = 0; i < itemViews.Count; i++)
            {
                string key = ItemAt(i);
                Show(itemViews[i], Icon(key), key != null ? 1 : 0);
            }
            if (knob != null)
            {
                // (grey and at the top when everything fits, like Minecraft's)
                int max = MaxScroll;
                knob.color = max > 0 ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.55f, 0.55f, 0.55f);
                float y = max > 0 ? (Rows * Slot - 15) * scrollRow / (float)max : 0f;
                knob.rectTransform.anchoredPosition = new Vector2(0f, -y);
            }
            for (int t = 0; t < tabViews.Count; t++)
            {
                string ik = TabIcons[t] != null && ModItems.ByKey.ContainsKey(TabIcons[t]) ? TabIcons[t] : Tabs[t].FirstOrDefault();
                Show(tabViews[t], t == SearchTab ? Compass() ?? Icon("item_compass") : Icon(ik), 1);
                tabViews[t].Bg.color = t == tab ? Color.white : new Color(0.72f, 0.72f, 0.72f);
            }
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaTabs)
            {
                if (index >= 0 && index < TabNames.Length && index != tab) { tab = index; scrollRow = 0; found = null; Relayout(); }
                return;
            }
            if (area != AreaItems) return;
            string key = ItemAt(index);
            if (cursorKey != null)
            {
                // the same item: one more (right-click) or a full stack; anything else is put away (deleted), like Minecraft
                if (key == cursorKey) cursorCount = right ? Mathf.Min(cursorCount + 1, MaxStackOf(key)) : MaxStackOf(key);
                else { cursorKey = null; cursorCount = 0; }
                return;
            }
            if (key == null) return;
            if (key.StartsWith(Lc) || key.StartsWith(SpawnEggs.CreativePrefix))
            {
                BlockNet.RequestSpawnVanilla(key.StartsWith(Lc) ? key.Substring(Lc.Length) : key);
                Sounds.Play2D("pop", 0.35f, Random.Range(1.4f, 2.0f));
                return;
            }
            if (shift && !right) { Inventory.Give(key, MaxStackOf(key)); return; }
            cursorKey = key;
            cursorCount = right ? 1 : MaxStackOf(key);
        }

        /// <summary>Shift-click on a hotbar stack: nowhere to move it here, so it stays.</summary>
        protected override int QuickMoveIn(string key, int n) => n;

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaTabs) return index >= 0 && index < TabNames.Length ? TabNames[index] : null;
            string key = area == AreaItems ? ItemAt(index) : null;
            if (key == null) return null;
            return NameOf(key);
        }

        protected override void OnUpdate()
        {
            if (!GameModes.LocalCreative) { Close(); return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!textHooked && kb != null) { kb.onTextInput += OnText; textHooked = true; }
            if (Searching && kb != null)
            {
                // (backspace, if the keyboard's text didn't bring it; held, it doesn't repeat)
                if (kb.backspaceKey.wasPressedThisFrame && backspaceFrame != Time.frameCount && query.Length > 0) SetQuery(query.Substring(0, query.Length - 1));
                if (fieldText != null) RefreshContent(); // (the cursor blinks)
            }
            // the mouse wheel scrolls the items a row at a time
            var m = UnityEngine.InputSystem.Mouse.current;
            float wheel = m != null ? m.scroll.ReadValue().y : 0f;
            if (wheel != 0f)
            {
                int s = Mathf.Clamp(scrollRow + (wheel > 0f ? -1 : 1), 0, MaxScroll);
                if (s != scrollRow) { scrollRow = s; Refresh(); }
            }
        }

        protected override string DevContent() => $"tab={tab}:{TabNames[tab]} query='{query}' scroll={scrollRow}/{MaxScroll} shown=[{string.Join(",", Enumerable.Range(0, Cols * Rows).Select(ItemAt).Where(k => k != null))}] items=[{string.Join(",", Shown)}]";

        /// <summary>Keys go to the search box while the search tab is open.</summary>
        protected override bool CapturesKeys => tab == SearchTab;

        protected override int DevArea(string name) => name == "items" ? AreaItems : name == "tabs" ? AreaTabs : -1;
    }
}
