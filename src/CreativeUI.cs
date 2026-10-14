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
        public static CreativeUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;

        const int AreaItems = 10, AreaTabs = 11;
        const int Cols = 9, Rows = 5, TabTop = -20, GridTop = 26; // (tabs sit above the panel, like Minecraft's)
        static readonly string[] TabNames = { "Building", "Redstone", "Tools & Combat", "Food & Items", "Lethal Company" };
        const int VanillaTab = 4;
        const string Lc = "lc:";
        /// <summary>The game's equipment that isn't sold in the store (scrap is left out: it's the Company's).</summary>
        static readonly string[] VanillaExtras = { "Shotgun", "Ammo", "Kitchen knife", "Key", "Homemade flashbang" };
        static List<string>[] tabs;
        int tab;
        int scrollRow; // (the first row of the tab shown: the wheel scrolls, like Minecraft's creative menu)
        Image knob;
        int MaxScroll => Mathf.Max(0, Mathf.CeilToInt(Tabs[tab].Count / (float)Cols) - Rows);
        string ItemAt(int slot) { var list = Tabs[tab]; int i = scrollRow * Cols + slot; return slot >= 0 && i < list.Count ? list[i] : null; }
        readonly List<View> itemViews = new List<View>(), tabViews = new List<View>();

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176 + ScrollW + 4, GridTop + Rows * Slot + 19 + Slot + 7);
        const int ScrollW = 12, ScrollX = 172; // (the scroll bar, right of the grid)
        protected override string Title => TabNames[tab];
        protected override int HotbarTop => GridTop + Rows * Slot + 19;

        /// <summary>Every item, sorted into Minecraft-like tabs (ore scrap is the Company's, not a creative item).</summary>
        static List<string>[] Tabs
        {
            get
            {
                if (tabs != null) return tabs;
                tabs = Enumerable.Range(0, TabNames.Length).Select(_ => new List<string>()).ToArray();
                foreach (var b in Blocks.All)
                {
                    if (!ModItems.ByKey.ContainsKey(b.Key) || b == Blocks.Bedrock) continue;
                    bool redstone = b.IsRedstoneComponent || b == Blocks.Slime || b == Blocks.RedstoneBlock;
                    tabs[redstone ? 1 : 0].Add(b.Key);
                }
                foreach (var kv in ModItems.ByKey)
                {
                    var prefab = kv.Value.spawnPrefab;
                    // (ore scrap is the Company's; ender pearls count even when the config makes them scrap)
                    if (prefab == null || kv.Key.StartsWith("scrap_") || Blocks.Get(kv.Key) != null) continue;
                    if (prefab.GetComponent<ToolItem>() != null || prefab.GetComponent<ArmorItem>() != null || prefab.GetComponent<FlintAndSteelItem>() != null || kv.Key == "ender_pearl") tabs[2].Add(kv.Key);
                    else tabs[3].Add(kv.Key);
                }
                // the game's own: what the store sells, then the rest of the equipment
                var store = Object.FindObjectOfType<Terminal>()?.buyableItemsList ?? new Item[0];
                // (the store also sells our blocks: those are in the other tabs)
                // then every other item the game knows (#86): the rest of the equipment, then scrap, other mods' too
                var rest = (StartOfRound.Instance != null ? StartOfRound.Instance.allItemsList.itemsList : new List<Item>())
                    .Where(it => it != null).OrderBy(it => it.isScrap).ThenBy(it => it.itemName);
                foreach (var it in store.Concat(VanillaExtras.Select(VanillaItem)).Concat(rest))
                    if (it != null && it.spawnPrefab != null && !string.IsNullOrEmpty(it.itemName) && !ModItems.ByKey.Values.Contains(it) && !tabs[VanillaTab].Contains(Lc + it.itemName)) tabs[VanillaTab].Add(Lc + it.itemName);
                // spawn eggs (#85): one per monster the game knows
                foreach (var e in SpawnEggs.All) tabs[VanillaTab].Add(SpawnEggs.CreativePrefix + e.enemyName);
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

        static Sprite Icon(string key) => key != null && key.StartsWith(Lc) ? VanillaItem(key.Substring(Lc.Length))?.itemIcon
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
            // tab buttons along the top, above the panel, each showing its first item
            for (int t = 0; t < TabNames.Length; t++)
            {
                var v = MakeSlot(panel, new Vector2(4 + t * (Slot + 3), -TabTop), AreaTabs, t);
                tabViews.Add(v);
            }
            for (int i = 0; i < Cols * Rows; i++)
                itemViews.Add(MakeSlot(panel, new Vector2(8 + (i % Cols) * Slot, -(GridTop + (i / Cols) * Slot)), AreaItems, i));
            // the scroll bar: a track the height of the grid, and a knob showing where you are
            var track = Rect("scrolltrack", panel, new Vector2(ScrollX, -GridTop), new Vector2(ScrollW, Rows * Slot)).gameObject.AddComponent<Image>();
            track.color = new Color(0.33f, 0.33f, 0.33f); track.raycastTarget = false;
            knob = Rect("scrollknob", track.transform, Vector2.zero, new Vector2(ScrollW, 15)).gameObject.AddComponent<Image>();
            knob.raycastTarget = false;
        }

        protected override void RefreshContent()
        {
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
                Show(tabViews[t], Icon(Tabs[t].FirstOrDefault()), 1);
                tabViews[t].Bg.color = t == tab ? Color.white : new Color(0.72f, 0.72f, 0.72f);
            }
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaTabs)
            {
                if (index >= 0 && index < TabNames.Length && index != tab) { tab = index; scrollRow = 0; Relayout(); }
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
            if (key.StartsWith(SpawnEggs.CreativePrefix)) return "Spawn Egg (" + SpawnEggs.NameOf(SpawnEggs.ByName(key.Substring(SpawnEggs.CreativePrefix.Length))) + ")";
            return key.StartsWith(Lc) ? key.Substring(Lc.Length) : Crafting.NameOf(key);
        }

        protected override void OnUpdate()
        {
            if (!GameModes.LocalCreative) { Close(); return; }
            // the mouse wheel scrolls the items a row at a time
            var m = UnityEngine.InputSystem.Mouse.current;
            float wheel = m != null ? m.scroll.ReadValue().y : 0f;
            if (wheel != 0f)
            {
                int s = Mathf.Clamp(scrollRow + (wheel > 0f ? -1 : 1), 0, MaxScroll);
                if (s != scrollRow) { scrollRow = s; Refresh(); }
            }
        }

        protected override string DevContent() => $"tab={tab}:{TabNames[tab]} scroll={scrollRow}/{MaxScroll} shown=[{string.Join(",", Enumerable.Range(0, Cols * Rows).Select(ItemAt).Where(k => k != null))}] items=[{string.Join(",", Tabs[tab])}]";

        protected override int DevArea(string name) => name == "items" ? AreaItems : name == "tabs" ? AreaTabs : -1;
    }
}
