using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The chest screen: the chest's 27 slots above your hotbar ([E] on a chest). Same mouse rules as every slot screen
    /// (see SlotScreen); shift-click moves a stack between the chest and the hotbar. The chest's contents live on the
    /// server: each click asks the server, which moves the items and hands back what comes out (onto the mouse, or into
    /// the inventory for a shift-click).
    /// </summary>
    public class ChestUI : SlotScreen
    {
        public static ChestUI Instance;
        const int AreaChest = 10;
        BlockKey chest;
        readonly List<View> cellViews = new List<View>();

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176, 116);
        protected override string Title => "Chest";
        protected override int HotbarTop => 91;
        protected override bool Usable(string key) => Chests.Accepts(key);

        protected override void LayoutContent()
        {
            cellViews.Clear();
            for (int i = 0; i < Chests.Size; i++)
                cellViews.Add(MakeSlot(panel, new Vector2(8 + (i % 9) * Slot, -(18 + (i / 9) * Slot)), AreaChest, i));
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            ui.chest = k;
            ui.Show();
            Sounds.Play2D("chest.open", 0.5f, Random.Range(0.9f, 1.0f));
        }

        protected override void OnClosing() => Sounds.Play2D("chest.close", 0.45f, Random.Range(0.9f, 1.0f));

        /// <summary>The chest was broken or changed by someone else: redraw (or close if it's gone).</summary>
        public static void OnContentsChanged(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !ui.open || !ui.chest.Equals(k)) return;
            if (BlockWorld.Instance == null || BlockWorld.Instance.DefAt(k) != Blocks.Chest) { ui.Close(); return; }
            ui.Refresh();
        }

        protected override void OnUpdate()
        {
            // the chest was broken (or we walked away)
            var p = Local;
            var w = BlockWorld.Instance;
            if (w == null || w.DefAt(chest) != Blocks.Chest) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(chest)) > 8f) Close();
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area != AreaChest || index < 0 || index >= Chests.Size) return;
            var c = Chests.Of(chest);
            string key = c != null && c.Count[index] > 0 ? c.Key[index] : null;
            int count = c != null ? c.Count[index] : 0;
            if (cursorKey == null)
            {
                if (key == null) return;
                // take a whole stack (shift: straight into the inventory) or half of it
                int n = right ? (count + 1) / 2 : count;
                BlockNet.RequestChestTake(chest, index, n, toInventory: shift && !right);
            }
            else
            {
                // put the held stack (or one item) here; a different item swaps with the held one
                int n = right ? 1 : cursorCount;
                bool swap = !right && key != null && key != cursorKey;
                string give = cursorKey;
                TakeCursor(n);
                BlockNet.RequestChestPut(chest, index, give, n, swap);
            }
        }

        /// <summary>Shift-click from the hotbar: into the chest, matching stacks first (the server returns what didn't fit).</summary>
        protected override int QuickMoveIn(string key, int n)
        {
            BlockNet.RequestChestPut(chest, -1, key, n, false);
            return 0;
        }

        /// <summary>Items the server handed back after a click: onto the mouse, or into the inventory.</summary>
        public static void Received(string key, int n, bool toInventory)
        {
            var ui = Instance;
            if (key == null || n <= 0) return;
            if (toInventory || ui == null || !ui.open) Inventory.Give(key, n);
            else ui.ToCursor(key, n);
            ui?.Refresh();
        }

        protected override void RefreshContent()
        {
            var c = Chests.Of(chest);
            for (int i = 0; i < cellViews.Count; i++)
                Show(cellViews[i], c != null && c.Count[i] > 0 ? IconOf(c.Key[i]) : null, c != null ? c.Count[i] : 0);
        }

        protected override string HoverContent(int area, int index)
        {
            var c = Chests.Of(chest);
            return area == AreaChest && c != null && index < Chests.Size && c.Count[index] > 0 ? Crafting.NameOf(c.Key[index]) : null;
        }

        protected override string DevContent()
        {
            var c = Chests.Of(chest);
            var cells = string.Join(",", Enumerable.Range(0, Chests.Size).Select(i => c != null && c.Count[i] > 0 ? $"{c.Key[i]}x{c.Count[i]}" : "."));
            return $"chest={chest.Pos} cells=[{cells}]";
        }

        protected override int DevArea(string name) => name == "chest" ? AreaChest : -1;
    }
}
