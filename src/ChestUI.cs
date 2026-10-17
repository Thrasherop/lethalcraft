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
        BlockKey? second; // (#132: a large chest's other half: its slots after the first's)
        readonly List<View> cellViews = new List<View>();

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176, second != null ? 170 : 116);
        protected override string Title => second != null ? (BlockWorld.Instance?.DefAt(chest) == Blocks.TrappedChest ? "Large Trapped Chest" : "Large Chest") : chest.Frame == Chests.MobFrame ? MobTitle(chest.Pos.y == 27 ? (chest.Pos.z == 1 ? "Boat with Chest" : "Minecart with Chest") : chest.Pos.y == 5 ? "Minecart with Hopper" : "Llama") : BlockWorld.Instance?.DefAt(chest)?.Name ?? "Chest";

        /// <summary>A mob's storage: its own name on the screen, as Minecraft's (a name tag's, else its kind's: "Oak Boat with
        /// Chest", "Spruce Boat with Chest").</summary>
        string MobTitle(string fallback)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null || !nm.SpawnManager.SpawnedObjects.TryGetValue((ulong)chest.Pos.x, out var no) || no == null) return fallback;
            var e = no.GetComponent<EnemyAI>();
            return e == null ? fallback : ShipMobs.NameOf(e) ?? e.enemyType?.enemyName ?? fallback;
        }
        int Slots => second != null ? 2 * Chests.Size : Chests.SlotsAt(chest);
        protected override int HotbarTop => second != null ? 145 : 91;

        /// <summary>A screen slot: which chest, which of its slots.</summary>
        (BlockKey k, int s) Cell(int index) => second != null && index >= Chests.Size ? (second.Value, index - Chests.Size) : (chest, index);
        protected override bool Usable(string key) => Chests.Accepts(key);

        protected override void LayoutContent()
        {
            cellViews.Clear();
            int n = Slots;
            // (a chest: three rows of nine; a dropper or dispenser: three by three in the middle; a hopper: a row of five)
            for (int i = 0; i < n; i++)
            {
                var at = n == 9 ? new Vector2(62 + (i % 3) * Slot, -(18 + (i / 3) * Slot)) : n == 5 ? new Vector2(44 + i * Slot, -36) : n == 15 ? new Vector2(44 + (i % 5) * Slot, -(18 + (i / 5) * Slot)) : new Vector2(8 + (i % 9) * Slot, -(18 + (i / 9) * Slot));
                cellViews.Add(MakeSlot(panel, at, AreaChest, i));
            }
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            // (two chests side by side: one large chest, the left one's slots first)
            ui.chest = k; ui.second = null;
            if (k.Frame != Chests.MobFrame && Chests.PartnerOf(k, out bool partnerFirst) is BlockKey pk) { if (partnerFirst) { ui.chest = pk; ui.second = k; } else ui.second = pk; }
            ui.Show(); // (laid out each time: a hopper's five slots, a chest's 27, a large chest's 54)
            foreach (var ck in ui.Halves()) if (BlockWorld.Instance?.DefAt(ck) == Blocks.TrappedChest) BlockNet.RequestChestView(ck, true);
            Sounds.Play2D("chest.open", 0.5f, Random.Range(0.9f, 1.0f));
        }

        protected override void OnClosing()
        {
            Sounds.Play2D("chest.close", 0.45f, Random.Range(0.9f, 1.0f));
            foreach (var ck in Halves()) if (BlockWorld.Instance?.DefAt(ck) == Blocks.TrappedChest) BlockNet.RequestChestView(ck, false);
        }

        IEnumerable<BlockKey> Halves()
        {
            yield return chest;
            if (second != null) yield return second.Value;
        }

        /// <summary>The chest was broken or changed by someone else: redraw (or close if it's gone).</summary>
        public static void OnContentsChanged(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !ui.open || (!ui.chest.Equals(k) && !(ui.second != null && ui.second.Value.Equals(k)))) return;
            if (k.Frame != Chests.MobFrame && (BlockWorld.Instance == null || !Blocks.IsChest(BlockWorld.Instance.DefAt(k)))) { ui.Close(); return; }
            ui.Refresh();
        }

        protected override void OnUpdate()
        {
            // the chest was broken (or we walked away)
            var p = Local;
            var w = BlockWorld.Instance;
            if (chest.Frame == Chests.MobFrame) return; // (a llama's: it closes when you walk off, below the llama's own checks)
            if (w == null || !Blocks.IsChest(w.DefAt(chest)) || (second != null && !Blocks.IsChest(w.DefAt(second.Value)))) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(chest)) > 8f) Close();
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area != AreaChest || index < 0 || index >= Slots) return;
            var (ck, slot) = Cell(index);
            var c = Chests.Of(ck);
            string key = c != null && c.Count[slot] > 0 ? c.Key[slot] : null;
            int count = c != null ? c.Count[slot] : 0;
            if (cursorKey == null)
            {
                if (key == null) return;
                // take a whole stack (shift: straight into the inventory) or half of it
                int n = right ? (count + 1) / 2 : count;
                if (shift && !right) { n = Mathf.Min(n, Inventory.RoomFor(key)); if (n <= 0) return; } // (only what fits, #209)
                BlockNet.RequestChestTake(ck, slot, n, toInventory: shift && !right);
            }
            else
            {
                // put the held stack (or one item) here; a different item swaps with the held one
                int n = right ? 1 : cursorCount;
                bool swap = !right && key != null && key != cursorKey;
                string give = cursorKey;
                TakeCursor(n);
                BlockNet.RequestChestPut(ck, slot, give, n, swap);
            }
        }

        /// <summary>Shift-click from the hotbar: into the chest, matching stacks first (the server returns what didn't fit).</summary>
        protected override int QuickMoveIn(string key, int n)
        {
            // (a large chest: into the top half while it has room for it, else the bottom)
            var target = chest;
            if (second != null)
            {
                var c = Chests.Of(chest);
                bool room = c == null || Enumerable.Range(0, Chests.Size).Any(i => c.Count[i] <= 0 || (c.Key[i] == key && c.Count[i] < Inventory.MaxStackOf(key)));
                if (!room) target = second.Value;
            }
            BlockNet.RequestChestPut(target, -1, key, n, false);
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
            for (int i = 0; i < cellViews.Count; i++)
            {
                var (ck, s) = Cell(i);
                var c = Chests.Of(ck);
                Show(cellViews[i], c != null && c.Count[s] > 0 ? IconOf(c.Key[s]) : null, c != null ? c.Count[s] : 0, 1f, c?.Key[s]);
            }
        }

        protected override string HoverContent(int area, int index)
        {
            if (area != AreaChest || index >= Slots) return null;
            var (ck, s) = Cell(index);
            var c = Chests.Of(ck);
            return c != null && c.Count[s] > 0 ? Crafting.NameOf(c.Key[s]) : null;
        }

        protected override string DevContent()
        {
            var cells = string.Join(",", Enumerable.Range(0, Slots).Select(i => { var (ck, s) = Cell(i); var c = Chests.Of(ck); return c != null && c.Count[s] > 0 ? $"{c.Key[s]}x{c.Count[s]}" : "."; }));
            return $"chest={chest.Pos}{(second != null ? " second=" + second.Value.Pos : "")} slots={Slots} cells=[{cells}]";
        }

        protected override int DevArea(string name) => name == "chest" ? AreaChest : -1;
    }
}
