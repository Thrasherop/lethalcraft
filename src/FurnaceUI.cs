using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The furnace screen (#83), Minecraft's: what's smelting above, the fuel below, the result on the right, the flame
    /// (how long the fuel burns) and the arrow (how far along the item is) between them. [E] on a furnace opens it. Same
    /// mouse rules as the chest; shift-click from the hotbar puts an item where it goes (ore and sand to smelt, coal and
    /// wood to burn). The furnace's contents live on the server, like a chest's.
    /// </summary>
    public class FurnaceUI : SlotScreen
    {
        public static FurnaceUI Instance;
        const int AreaFurnace = 10;
        public const int In = 0, Fuel = 1, Out = 2;
        BlockKey furnace;
        View inView, fuelView, outView;
        Image flame, arrowFill;
        static Sprite flameSprite, arrowSprite2;

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        public bool IsOpen => open;
        protected override Vector2 PanelSize => new Vector2(176, 110);
        protected override string Title => "Furnace";
        protected override int HotbarTop => 84;
        protected override bool Usable(string key) => key != null && (Crafting.SmeltResult.ContainsKey(key) || Crafting.FuelSeconds.ContainsKey(key));

        static Sprite McSprite(string path)
        {
            var t = McAssets.Available ? McAssets.LoadTexture(path) : null;
            if (t == null) return null;
            t.filterMode = FilterMode.Point;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        }

        protected override void LayoutContent()
        {
            if (flameSprite == null) flameSprite = McSprite("gui/sprites/container/furnace/lit_progress");
            if (arrowSprite2 == null) arrowSprite2 = McSprite("gui/sprites/container/furnace/burn_progress");
            inView = MakeSlot(panel, new Vector2(55, -16), AreaFurnace, In);
            fuelView = MakeSlot(panel, new Vector2(55, -52), AreaFurnace, Fuel);
            outView = MakeSlot(panel, new Vector2(111, -30), AreaFurnace, Out, big: true);
            // the flame: unlit (dark) under the lit one, which burns down as the fuel does
            var dark = Rect("flame_bg", panel, new Vector2(57, -36), new Vector2(14, 14)).gameObject.AddComponent<Image>();
            dark.sprite = flameSprite; dark.color = flameSprite != null ? new Color(0.3f, 0.3f, 0.3f, 0.6f) : new Color(0, 0, 0, 0.25f); dark.raycastTarget = false;
            flame = Rect("flame", panel, new Vector2(57, -36), new Vector2(14, 14)).gameObject.AddComponent<Image>();
            flame.sprite = flameSprite; flame.color = flameSprite != null ? Color.white : new Color(1f, 0.55f, 0.1f);
            flame.type = Image.Type.Filled; flame.fillMethod = Image.FillMethod.Vertical; flame.fillOrigin = (int)Image.OriginVertical.Bottom;
            flame.raycastTarget = false;
            // the arrow: grey, filled white from the left as the item cooks
            MakeArrow(new Vector2(79, -34));
            arrowFill = Rect("arrow_fill", panel, new Vector2(79, -34), new Vector2(arrowSprite2 != null ? 24 : 22, arrowSprite2 != null ? 16 : 15)).gameObject.AddComponent<Image>();
            arrowFill.sprite = arrowSprite2 != null ? arrowSprite2 : arrowSprite; arrowFill.color = arrowSprite2 != null ? Color.white : new Color(1f, 1f, 1f, 0.9f);
            arrowFill.type = Image.Type.Filled; arrowFill.fillMethod = Image.FillMethod.Horizontal; arrowFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            arrowFill.raycastTarget = false;
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            ui.furnace = k;
            ui.Show();
        }

        /// <summary>The furnace's contents changed (a server update): redraw.</summary>
        public static void OnStateChanged(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !ui.open || !ui.furnace.Equals(k)) return;
            ui.Refresh();
        }

        protected override void OnUpdate()
        {
            var p = Local;
            var w = BlockWorld.Instance;
            if (w == null || w.DefAt(furnace) != Blocks.Furnace) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(furnace)) > 8f) { Close(); return; }
            Crafting.Furnaces.TryGetValue(furnace, out var f);
            if (flame != null) flame.fillAmount = f != null && f.Lit && f.BurnMax > 0f ? Mathf.Clamp01(f.Burn / f.BurnMax) : 0f;
            if (arrowFill != null) arrowFill.fillAmount = f != null ? Mathf.Clamp01(f.Progress / Crafting.SmeltSeconds) : 0f;
        }

        static (string key, int count) Cell(Crafting.Furnace f, int i)
        {
            if (f == null) return (null, 0);
            switch (i)
            {
                case In: return f.InCount > 0 ? (f.In, f.InCount) : (null, 0);
                case Fuel: return f.Fuel > 0 ? (f.FuelKey, f.Fuel) : (null, 0);
                case Out: return f.OutCount > 0 ? (f.Out, f.OutCount) : (null, 0);
            }
            return (null, 0);
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area != AreaFurnace || index < In || index > Out) return;
            Crafting.Furnaces.TryGetValue(furnace, out var f);
            var (key, count) = Cell(f, index);
            if (cursorKey == null)
            {
                if (key == null) return;
                // a whole stack (shift: straight into the inventory) or half of it; one-at-a-time items one by one
                int n = right ? (count + 1) / 2 : count;
                if (!shift) n = Mathf.Min(n, MaxStackOf(key));
                BlockNet.RequestFurnaceSlotTake(furnace, index, n, toInventory: shift && !right);
            }
            else
            {
                if (index == Out) return; // (nothing goes into the result slot)
                if (index == In && !Crafting.SmeltResult.ContainsKey(cursorKey)) return;
                if (index == Fuel && !Crafting.FuelSeconds.ContainsKey(cursorKey)) return;
                int n = right ? 1 : cursorCount;
                bool swap = !right && key != null && key != cursorKey;
                string give = cursorKey;
                TakeCursor(n);
                BlockNet.RequestFurnacePut(furnace, index, give, n, swap);
            }
        }

        /// <summary>Shift-click from the hotbar: smelting or fuel, wherever it goes (the server returns what didn't fit).</summary>
        protected override int QuickMoveIn(string key, int n)
        {
            BlockNet.RequestFurnacePut(furnace, -1, key, n, false);
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
            Crafting.Furnaces.TryGetValue(furnace, out var f);
            foreach (var (v, i) in new[] { (inView, In), (fuelView, Fuel), (outView, Out) })
            {
                var (key, count) = Cell(f, i);
                Show(v, key != null ? IconOf(key) : null, count, 1f, key);
            }
        }

        protected override string HoverContent(int area, int index)
        {
            if (area != AreaFurnace) return null;
            Crafting.Furnaces.TryGetValue(furnace, out var f);
            var (key, _) = Cell(f, index);
            return key != null ? Crafting.NameOf(key) : index == In ? "Smelt: ore, sand, cobblestone, logs" : index == Fuel ? "Fuel: coal, wood" : null;
        }

        protected override string DevContent()
        {
            Crafting.Furnaces.TryGetValue(furnace, out var f);
            string C(int i) { var (k, n) = Cell(f, i); return k != null ? $"{k}x{n}" : "."; }
            return $"furnace={furnace.Pos} in={C(In)} fuel={C(Fuel)} out={C(Out)} lit={(f != null && f.Lit)} burn={(f != null ? f.Burn : 0):F1}/{(f != null ? f.BurnMax : 0):F1} progress={(f != null ? f.Progress : 0):F1}";
        }

        protected override int DevArea(string name) => name == "furnace" ? AreaFurnace : -1;
    }
}
