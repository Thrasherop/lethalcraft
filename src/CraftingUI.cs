using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft crafting screen: a 3x3 grid at a Crafting Table ([E]) or the inventory ([I] anywhere: your four armor
    /// slots, a window with your character, the 2x2 pocket grid), an output slot and your hotbar. Arrange items in a recipe's pattern and take the result; shift-click the output to craft as
    /// many as you can. Grid items are out of the hotbar while the screen is open and come back when it closes.
    /// (Panel, cursor, hotbar and mouse rules: see SlotScreen.)
    /// </summary>
    public class CraftingUI : SlotScreen
    {
        public static CraftingUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;
        public new static float LastClosed => SlotScreen.LastClosed;

        const int AreaGrid = 10, AreaOutput = 11, AreaArmor = 12;
        readonly List<View> armorViews = new List<View>();
        public RectTransform PreviewBox { get; private set; }
        System.Action<UnityEngine.InputSystem.InputAction.CallbackContext> onToggle;
        bool full;
        int size = 3;
        string[] gridKey = new string[9];
        int[] gridCount = new int[9];
        readonly List<View> gridViews = new List<View>();
        View outputView;
        Recipe current;

        protected override void Awake()
        {
            base.Awake();
            Instance = this;
            onToggle = _ =>
            {
                if (open) Close();
                else if (CreativeUI.IsOpen) CreativeUI.Instance.Close();
                else if (GameModes.LocalCreative) CreativeUI.Open(); // creative: every item instead of the 2x2 grid
                else Open(false);
            };
            if (ModKeys.PocketCraft != null) ModKeys.PocketCraft.performed += onToggle;
        }

        protected override void OnDestroy()
        {
            if (ModKeys.PocketCraft != null) ModKeys.PocketCraft.performed -= onToggle;
            base.OnDestroy();
            if (Instance == this) Instance = null;
        }

        protected override Vector2 PanelSize => full ? new Vector2(176, 112) : new Vector2(176, 121);
        protected override string Title => full ? "Crafting" : "";
        protected override int HotbarTop => full ? 87 : 96;

        protected override void LayoutContent()
        {
            gridViews.Clear(); armorViews.Clear(); PreviewBox = null;
            if (full)
            {
                for (int i = 0; i < 9; i++)
                    gridViews.Add(MakeSlot(panel, new Vector2(30 + (i % 3) * Slot, -(17 + (i / 3) * Slot)), AreaGrid, i));
                MakeArrow(new Vector2(90, -35));
                outputView = MakeSlot(panel, new Vector2(120, -31), AreaOutput, 0, true);
                return;
            }
            // Minecraft's inventory: armor down the left, your character beside it, the 2x2 grid on the right
            for (int i = 0; i < Armor.Slots; i++) armorViews.Add(MakeSlot(panel, new Vector2(8, -(8 + i * Slot)), AreaArmor, i));
            PreviewBox = Rect("preview", panel, new Vector2(26, -8), new Vector2(54, 72));
            var bg = PreviewBox.gameObject.AddComponent<UnityEngine.UI.Image>();
            bg.color = Color.black; bg.raycastTarget = false;
            ArmorPreview.Attach(PreviewBox);
            Text(panel, new Vector2(86, -13), LabelColor).Set("Crafting", LabelColor);
            for (int i = 0; i < 4; i++)
                gridViews.Add(MakeSlot(panel, new Vector2(86 + (i % 2) * Slot, -(18 + (i / 2) * Slot)), AreaGrid, i));
            MakeArrow(new Vector2(124, -29));
            outputView = MakeSlot(panel, new Vector2(150, -27), AreaOutput, 0);
        }

        static Sprite EmptyArmorIcon(int slot) => Atlas.IconFor("slot_" + Armor.PieceKeys[slot]);

        void ClickArmor(int index, bool shift)
        {
            var worn = Armor.Local[index];
            if (shift)
            {
                if (worn == null) return;
                Armor.SetLocal(index, null);
                Inventory.Give(worn, 1);
                return;
            }
            if (cursorKey == null)
            {
                if (worn == null) return;
                Armor.SetLocal(index, null);
                ToCursor(worn, 1);
                return;
            }
            var d = Armor.Get(cursorKey);
            if (d == null || d.Slot != index) return; // only the right piece goes in each slot
            string put = cursorKey;
            TakeCursor(1);
            Armor.SetLocal(index, put);
            if (worn != null) ToCursor(worn, 1);
            Sounds.Play2D("armor." + d.Material, 0.6f, 1f);
        }

        string[] GridKeys() => Enumerable.Range(0, size * size).Select(i => gridCount[i] > 0 ? gridKey[i] : null).ToArray();

        IEnumerable<Recipe> Available => Crafting.Recipes.Where(x => full || x.Pocket);

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            var p = Local;
            if (area == AreaOutput) { Craft(shift); return; }
            if (area == AreaArmor) { ClickArmor(index, shift); return; }
            if (area != AreaGrid || index >= size * size) return;
            if (cursorKey == null)
            {
                if (gridCount[index] <= 0) return;
                if (shift && !right) { Inventory.Give(gridKey[index], gridCount[index]); gridKey[index] = null; gridCount[index] = 0; return; } // straight back to the inventory
                int n = right ? (gridCount[index] + 1) / 2 : gridCount[index];
                ToCursor(gridKey[index], n);
                gridCount[index] -= n;
                if (gridCount[index] <= 0) { gridKey[index] = null; gridCount[index] = 0; }
            }
            else if (gridCount[index] <= 0 || gridKey[index] == cursorKey)
            {
                int n = Mathf.Min(right ? 1 : cursorCount, 64 - gridCount[index]);
                if (n <= 0) return;
                gridKey[index] = cursorKey; gridCount[index] += n;
                TakeCursor(n);
            }
            else
            {
                // swap the held stack with the cell
                (gridKey[index], cursorKey) = (cursorKey, gridKey[index]);
                (gridCount[index], cursorCount) = (cursorCount, gridCount[index]);
            }
        }

        /// <summary>Shift-click a hotbar stack: into matching grid cells, then the first empty one.</summary>
        protected override int QuickMoveIn(string key, int n)
        {
            // armor goes on (into its empty slot) before anything else
            var ad = Armor.Get(key);
            if (!full && ad != null && n == 1 && Armor.Local[ad.Slot] == null)
            {
                Armor.SetLocal(ad.Slot, key);
                Sounds.Play2D("armor." + ad.Material, 0.6f, 1f);
                return 0;
            }
            for (int pass = 0; pass < 2 && n > 0; pass++)
                for (int i = 0; i < size * size && n > 0; i++)
                {
                    if (pass == 0 ? gridKey[i] != key || gridCount[i] <= 0 : gridCount[i] > 0) continue;
                    int t = Mathf.Min(n, 64 - gridCount[i]);
                    gridKey[i] = key; gridCount[i] += t; n -= t;
                    if (pass == 1) break;
                }
            return n;
        }

        void Craft(bool all)
        {
            var r = RecipeBook.Match(Available, GridKeys(), size);
            if (r == null) return;
            int times = 1;
            if (all)
            {
                times = int.MaxValue;
                for (int i = 0; i < size * size; i++) if (gridCount[i] > 0) times = Mathf.Min(times, gridCount[i]);
                times = Mathf.Clamp(times, 1, 64);
            }
            // the ingredients are already out of the hotbar (in the grid): use them up, hand over the result
            for (int i = 0; i < size * size; i++)
            {
                if (gridCount[i] <= 0) continue;
                gridCount[i] -= times;
                if (gridCount[i] <= 0) { gridCount[i] = 0; gridKey[i] = null; }
            }
            Inventory.Give(r.Result, r.Count * times);
            Sounds.Play2D("pop", 0.5f, 1.2f);
            McHud.Toast($"Crafted {Crafting.NameOf(r.Result)} x{r.Count * times}");
        }

        protected override void RefreshContent()
        {
            for (int i = 0; i < gridViews.Count; i++) Show(gridViews[i], IconOf(gridKey[i]), gridCount[i]);
            for (int i = 0; i < armorViews.Count; i++)
            {
                var worn = Armor.Local[i];
                if (worn != null) Show(armorViews[i], IconOf(worn), 1);
                else Show(armorViews[i], EmptyArmorIcon(i), 1, 0.9f);
            }
            current = RecipeBook.Match(Available, GridKeys(), size);
            Show(outputView, current != null ? IconOf(current.Result) : null, current != null ? current.Count : 0);
        }

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaGrid && index < size * size && gridCount[index] > 0) return Crafting.NameOf(gridKey[index]);
            if (area == AreaArmor && index < Armor.Slots)
            {
                var worn = Armor.Local[index];
                int pts = Armor.PointsOf(Armor.Local);
                return worn != null ? $"{Crafting.NameOf(worn)} (+{Armor.Get(worn).Points} armor; {pts} total)" : Armor.PieceNames[index] + " slot";
            }
            if (area == AreaOutput && current != null) return Crafting.NameOf(current.Result) + (current.Count > 1 ? " x" + current.Count : "");
            return null;
        }

        /// <summary>Closing: what's left in the grid goes back to the inventory (drops if there's no room).</summary>
        protected override void OnClosing()
        {
            var back = new Dictionary<string, int>();
            for (int i = 0; i < 9; i++) if (gridCount[i] > 0 && gridKey[i] != null) back[gridKey[i]] = (back.TryGetValue(gridKey[i], out int b0) ? b0 : 0) + gridCount[i];
            foreach (var kv in back) Inventory.Give(kv.Key, kv.Value);
            gridKey = new string[9]; gridCount = new int[9];
        }

        public static void Open(bool withTable)
        {
            var ui = Instance;
            var p = Local;
            if (ui == null || !CanOpen(p)) return;
            if (ui.open)
            {
                if (withTable && !ui.full) { ui.OnClosing(); ui.full = true; ui.size = 3; ui.Relayout(); }
                return;
            }
            ui.full = withTable;
            ui.size = withTable ? 3 : 2;
            ui.gridKey = new string[9]; ui.gridCount = new int[9];
            ui.Show();
        }

        protected override string DevContent()
        {
            var g = string.Join(",", Enumerable.Range(0, size * size).Select(i => gridCount[i] > 0 ? $"{gridKey[i]}x{gridCount[i]}" : "."));
            return $"size={size} grid=[{g}] out={(current != null ? current.Result + "x" + current.Count : "-")} armor=[{string.Join(",", Armor.Local.Select(k => k ?? "-"))}] pts={Armor.PointsOf(Armor.Local)}";
        }

        protected override int DevArea(string name) => name == "grid" ? AreaGrid : name == "out" ? AreaOutput : name == "armor" ? AreaArmor : -1;
    }
}
