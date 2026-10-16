using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The smithing table (#159), Minecraft's: a netherite upgrade template, a diamond tool or armor piece and a netherite
    /// ingot make its netherite kind, enchantments and wear kept. (The crafting grid used to do the upgrade without a
    /// template.) Templates come from bastions, and seven diamonds and netherrack copy one. The same table puts armor trims
    /// on (#224, ArmorTrims): a trim template, an armor piece and a trim material.
    /// </summary>
    public static class Smithing
    {
        public const string TemplateKey = "netherite_upgrade_smithing_template";
        public const string Addition = "netherite_ingot";

        /// <summary>Diamond kind -> netherite kind (the diamond pickaxe's key is plain "pickaxe").</summary>
        public static readonly Dictionary<string, string> Upgrades = new Dictionary<string, string>
        {
            ["pickaxe"] = "netherite_pickaxe", ["diamond_shovel"] = "netherite_shovel", ["diamond_axe"] = "netherite_axe", ["diamond_hoe"] = "netherite_hoe",
            ["diamond_sword"] = "netherite_sword", ["diamond_helmet"] = "netherite_helmet", ["diamond_chestplate"] = "netherite_chestplate",
            ["diamond_leggings"] = "netherite_leggings", ["diamond_boots"] = "netherite_boots",
        };

        /// <summary>What the three slots make (its item data carried over), or null.</summary>
        public static string Result(string template, string item, string addition)
        {
            template = ItemData.Unnamed(template); addition = ItemData.Unnamed(addition); // (#228: named, they're still themselves)
            var trimmed = ArmorTrims.Result(template, item, addition);
            if (trimmed != null) return trimmed;
            if (template != TemplateKey || addition != Addition || item == null) return null;
            if (!Upgrades.TryGetValue(ItemData.Base(item) ?? item, out var up)) return null;
            int data = ItemData.Of(item);
            return ItemData.WithName(data != 0 ? ItemData.With(up, data) : up, ItemData.NameId(item)); // (#228: its name kept)
        }
    }

    /// <summary>The smithing table's screen: template, item and addition in a row, the result after the arrow. Like the
    /// crafting grid, what's put in is out of the hotbar while it's open and goes back when it closes.</summary>
    public class SmithingUI : SlotScreen
    {
        public static SmithingUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;
        const int AreaSlots = 10, AreaOut = 11;
        readonly string[] key = new string[3];
        readonly int[] count = new int[3];
        readonly View[] views = new View[3];
        View outView;
        string current;
        public static int Upgraded; // (dev/tests)

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176, 90);
        protected override string Title => "Upgrade Gear";
        protected override int HotbarTop => 64;
        protected override bool Usable(string k) => SlotFor(k) >= 0;

        protected override void LayoutContent()
        {
            for (int i = 0; i < 3; i++) views[i] = MakeSlot(panel, new Vector2(8 + i * Slot, -30), AreaSlots, i);
            MakeArrow(new Vector2(68, -30));
            outView = MakeSlot(panel, new Vector2(98, -26), AreaOut, 0, true);
        }

        /// <summary>Which slot a stack belongs in: the template first, the gear second, the ingot third.</summary>
        static int SlotFor(string k) => k == null ? -1 : ItemData.Unnamed(k) == Smithing.TemplateKey || ArmorTrims.IsTemplate(k) ? 0 : ItemData.Unnamed(k) == Smithing.Addition || ArmorTrims.IsMaterial(k) ? 2
            : Smithing.Upgrades.ContainsKey(ItemData.Base(k) ?? k) || ArmorTrims.Trimmable(k) ? 1 : -1; // (#224: and trims)

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaOut) { Upgrade(); return; }
            if (area != AreaSlots || index < 0 || index > 2) return;
            if (cursorKey == null)
            {
                if (count[index] <= 0) return;
                if (shift) { Inventory.Give(key[index], count[index]); key[index] = null; count[index] = 0; return; }
                int n = right ? (count[index] + 1) / 2 : count[index];
                ToCursor(key[index], n);
                count[index] -= n;
                if (count[index] <= 0) { key[index] = null; count[index] = 0; }
                return;
            }
            if (SlotFor(cursorKey) != index) return; // (each slot takes its own kind, as Minecraft's)
            if (count[index] <= 0 || key[index] == cursorKey)
            {
                // (the gear slot holds one piece)
                int n = Mathf.Min(right ? 1 : cursorCount, (index == 1 ? 1 : 64) - count[index]);
                if (n <= 0) return;
                key[index] = cursorKey; count[index] += n;
                TakeCursor(n);
            }
            else if (cursorCount == 1 || index != 1)
            {
                (key[index], cursorKey) = (cursorKey, key[index]);
                (count[index], cursorCount) = (cursorCount, count[index]);
            }
        }

        void Upgrade()
        {
            var r = Smithing.Result(count[0] > 0 ? key[0] : null, count[1] > 0 ? key[1] : null, count[2] > 0 ? key[2] : null);
            if (r == null) return;
            bool trim = ArmorTrims.IsTemplate(key[0]); // (#224)
            for (int i = 0; i < 3; i++) { count[i]--; if (count[i] <= 0) { count[i] = 0; key[i] = null; } }
            Inventory.Give(r, 1);
            Upgraded++;
            Sounds.Play2D("smithing", 0.7f, 1f);
            McHud.Toast((trim ? "Trimmed: " : "Upgraded: ") + Crafting.NameOf(r));
        }

        protected override int QuickMoveIn(string k, int n)
        {
            int i = SlotFor(k);
            if (i < 0) return n;
            if (count[i] > 0 && key[i] != k) return n;
            int t = Mathf.Min(n, (i == 1 ? 1 : 64) - count[i]);
            if (t <= 0) return n;
            key[i] = k; count[i] += t;
            return n - t;
        }

        protected override void RefreshContent()
        {
            for (int i = 0; i < 3; i++) Show(views[i], count[i] > 0 ? IconOf(key[i]) : null, count[i], 1f, count[i] > 0 ? key[i] : null);
            current = Smithing.Result(count[0] > 0 ? key[0] : null, count[1] > 0 ? key[1] : null, count[2] > 0 ? key[2] : null);
            Show(outView, current != null ? IconOf(current) : null, current != null ? 1 : 0, 1f, current);
        }

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaOut) return current != null ? Crafting.NameOf(current) : null;
            if (area != AreaSlots || index < 0 || index > 2) return null;
            if (count[index] > 0) return Crafting.NameOf(key[index]);
            return index == 0 ? "A netherite upgrade or armor trim template" : index == 1 ? "A diamond tool, weapon or armor piece; or armor to trim" : "A netherite ingot; or a trim material";
        }

        protected override void OnClosing()
        {
            for (int i = 0; i < 3; i++) if (count[i] > 0 && key[i] != null) Inventory.Give(key[i], count[i]);
            for (int i = 0; i < 3; i++) { key[i] = null; count[i] = 0; }
        }

        public static void Open()
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local) || ui.open) return;
            ui.Show();
        }

        protected override string DevContent() =>
            $"slots=[{string.Join(",", Enumerable.Range(0, 3).Select(i => count[i] > 0 ? key[i] + "x" + count[i] : "."))}] out={current ?? "-"} upgraded={Upgraded}";

        protected override int DevArea(string name) => name == "slot" ? AreaSlots : name == "out" ? AreaOut : -1;
    }
}
