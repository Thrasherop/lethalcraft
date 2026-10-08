using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Creative mode's item menu ([I] in creative, instead of pocket crafting): every Minecraft item, in tabs, above your
    /// hotbar. Left-click an item for a full stack on the mouse, right-click for one more (or one), shift-click to put a
    /// full stack straight into the hotbar. Clicking the item grid while holding something puts it away (deletes it),
    /// like Minecraft. Hotbar slots work like every slot screen (see SlotScreen). What you take from here is a real item
    /// (the server spawns it for you), so it stays if you go back to survival.
    /// </summary>
    public class CreativeUI : SlotScreen
    {
        public static CreativeUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;

        const int AreaItems = 10, AreaTabs = 11;
        const int Cols = 9, Rows = 5, TabTop = 4, GridTop = 26;
        static readonly string[] TabNames = { "Building", "Redstone", "Tools & Combat", "Food & Items" };
        static List<string>[] tabs;
        int tab;
        readonly List<View> itemViews = new List<View>(), tabViews = new List<View>();

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176, GridTop + Rows * Slot + 19 + Slot + 7);
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
                    if (prefab == null || kv.Value.isScrap || Blocks.Get(kv.Key) != null) continue;
                    if (prefab.GetComponent<ToolItem>() != null || prefab.GetComponent<ArmorItem>() != null || prefab.GetComponent<FlintAndSteelItem>() != null || kv.Key == "ender_pearl") tabs[2].Add(kv.Key);
                    else tabs[3].Add(kv.Key);
                }
                return tabs;
            }
        }

        public static void Open()
        {
            var ui = Instance;
            if (ui == null || !GameModes.LocalCreative || !CanOpen(Local)) return;
            ui.Show();
        }

        protected override void LayoutContent()
        {
            itemViews.Clear(); tabViews.Clear();
            // tab buttons along the top right, each showing its first item
            for (int t = 0; t < TabNames.Length; t++)
            {
                var v = MakeSlot(panel, new Vector2(176 - 8 - (TabNames.Length - t) * (Slot + 2) + 2, -TabTop), AreaTabs, t);
                tabViews.Add(v);
            }
            for (int i = 0; i < Cols * Rows; i++)
                itemViews.Add(MakeSlot(panel, new Vector2(8 + (i % Cols) * Slot, -(GridTop + (i / Cols) * Slot)), AreaItems, i));
        }

        protected override void RefreshContent()
        {
            var list = Tabs[tab];
            for (int i = 0; i < itemViews.Count; i++)
            {
                string key = i < list.Count ? list[i] : null;
                Show(itemViews[i], IconOf(key), key != null ? 1 : 0);
            }
            for (int t = 0; t < tabViews.Count; t++)
            {
                Show(tabViews[t], IconOf(Tabs[t].FirstOrDefault()), 1);
                tabViews[t].Bg.color = t == tab ? Color.white : new Color(0.72f, 0.72f, 0.72f);
            }
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaTabs)
            {
                if (index >= 0 && index < TabNames.Length && index != tab) { tab = index; Relayout(); }
                return;
            }
            if (area != AreaItems) return;
            var list = Tabs[tab];
            string key = index >= 0 && index < list.Count ? list[index] : null;
            if (cursorKey != null)
            {
                // the same item: one more (right-click) or a full stack; anything else is put away (deleted), like Minecraft
                if (key == cursorKey) cursorCount = right ? Mathf.Min(cursorCount + 1, MaxStackOf(key)) : MaxStackOf(key);
                else { cursorKey = null; cursorCount = 0; }
                return;
            }
            if (key == null) return;
            if (shift && !right) { Inventory.Give(key, MaxStackOf(key)); return; }
            cursorKey = key;
            cursorCount = right ? 1 : MaxStackOf(key);
        }

        /// <summary>Shift-click on a hotbar stack: nowhere to move it here, so it stays.</summary>
        protected override int QuickMoveIn(string key, int n) => n;

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaTabs) return index >= 0 && index < TabNames.Length ? TabNames[index] : null;
            var list = Tabs[tab];
            return area == AreaItems && index >= 0 && index < list.Count ? Crafting.NameOf(list[index]) : null;
        }

        protected override void OnUpdate()
        {
            if (!GameModes.LocalCreative) Close();
        }

        protected override string DevContent() => $"tab={tab}:{TabNames[tab]} items=[{string.Join(",", Tabs[tab])}]";

        protected override int DevArea(string name) => name == "items" ? AreaItems : name == "tabs" ? AreaTabs : -1;
    }
}
