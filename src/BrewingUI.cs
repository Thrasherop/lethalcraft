using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The brewing stand's screen (#88), Minecraft's layout: blaze powder at the top left with the fuel bar under it,
    /// the ingredient at the top, the three bottles below, the bubbles and the arrow showing how far the brew is. [E]
    /// on a brewing stand opens it. Same mouse rules as the furnace; shift-click from the hotbar puts an item where it
    /// goes. The contents live on the server.
    /// </summary>
    public class BrewingUI : SlotScreen
    {
        public static BrewingUI Instance;
        const int AreaStand = 11;
        BlockKey stand;
        View[] views = new View[Brewing.Slots];
        Image fuelFill, arrowFill, bubbles;
        static Sprite fuelSprite, arrowSprite3, bubbleSprite;

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        public bool IsOpen => open;
        protected override Vector2 PanelSize => new Vector2(176, 110);
        protected override string Title => "Brewing Stand";
        protected override int HotbarTop => 84;
        protected override bool Usable(string key) => key != null && (Potions.IsPotion(key) || Potions.IsIngredient(key) || ItemData.Unnamed(key) == "blaze_powder");

        static Sprite McSprite(string path)
        {
            var t = McAssets.Available ? McAssets.LoadTexture(path) : null;
            if (t == null) return null;
            t.filterMode = FilterMode.Point;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        }

        Image Bar(string name, Sprite sp, Vector2 pos, Vector2 size, Color fallback, Image.FillMethod method, int origin)
        {
            var bg = Rect(name + "_bg", panel, pos, size).gameObject.AddComponent<Image>();
            bg.sprite = sp; bg.color = sp != null ? new Color(0.3f, 0.3f, 0.3f, 0.5f) : new Color(0, 0, 0, 0.25f); bg.raycastTarget = false;
            var img = Rect(name, panel, pos, size).gameObject.AddComponent<Image>();
            img.sprite = sp; img.color = sp != null ? Color.white : fallback;
            img.type = Image.Type.Filled; img.fillMethod = method; img.fillOrigin = origin;
            img.raycastTarget = false;
            return img;
        }

        protected override void LayoutContent()
        {
            if (fuelSprite == null) fuelSprite = McSprite("gui/sprites/container/brewing_stand/fuel_length");
            if (arrowSprite3 == null) arrowSprite3 = McSprite("gui/sprites/container/brewing_stand/brew_progress");
            if (bubbleSprite == null) bubbleSprite = McSprite("gui/sprites/container/brewing_stand/bubbles");
            // (Minecraft's positions, one pixel in)
            views[0] = MakeSlot(panel, new Vector2(55, -50), AreaStand, 0);
            views[1] = MakeSlot(panel, new Vector2(78, -57), AreaStand, 1);
            views[2] = MakeSlot(panel, new Vector2(101, -50), AreaStand, 2);
            views[Brewing.Ingredient] = MakeSlot(panel, new Vector2(78, -16), AreaStand, Brewing.Ingredient);
            views[Brewing.Fuel] = MakeSlot(panel, new Vector2(16, -16), AreaStand, Brewing.Fuel);
            fuelFill = Bar("fuel", fuelSprite, new Vector2(59, -43), new Vector2(18, 4), new Color(1f, 0.6f, 0.1f), Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            arrowFill = Bar("brew", arrowSprite3, new Vector2(96, -15), new Vector2(9, 28), Color.white, Image.FillMethod.Vertical, (int)Image.OriginVertical.Top);
            bubbles = Bar("bubbles", bubbleSprite, new Vector2(62, -13), new Vector2(12, 29), new Color(1f, 1f, 1f, 0.8f), Image.FillMethod.Vertical, (int)Image.OriginVertical.Bottom);
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            ui.stand = k;
            ui.Show();
        }

        /// <summary>The stand's contents changed (a server update): redraw.</summary>
        public static void OnStateChanged(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !ui.open || !ui.stand.Equals(k)) return;
            ui.Refresh();
        }

        protected override void OnUpdate()
        {
            var p = Local;
            var w = BlockWorld.Instance;
            if (w == null || w.DefAt(stand) != Blocks.BrewingStand) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(stand)) > 8f) { Close(); return; }
            Brewing.Stands.TryGetValue(stand, out var s);
            if (fuelFill != null) fuelFill.fillAmount = s != null ? Mathf.Clamp01(s.FuelLeft / (float)Brewing.BrewsPerPowder) : 0f;
            float t = s != null && s.Brewing ? Mathf.Clamp01(s.Progress / Brewing.BrewSeconds) : 0f;
            if (arrowFill != null) arrowFill.fillAmount = t;
            // the bubbles rise over and over while it brews (Minecraft's: a cycle of a few frames)
            if (bubbles != null) bubbles.fillAmount = s != null && s.Brewing ? (Time.time * 0.6f) % 1f : 0f;
        }

        static (string key, int count) Cell(Brewing.Stand s, int i)
        {
            if (s == null) return (null, 0);
            if (i < Brewing.Bottles) return s.Bottle[i] != null ? (s.Bottle[i], 1) : (null, 0);
            if (i == Brewing.Ingredient) return s.IngCount > 0 ? (s.IngKey, s.IngCount) : (null, 0);
            if (i == Brewing.Fuel) return s.Powder > 0 ? ("blaze_powder", s.Powder) : (null, 0);
            return (null, 0);
        }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area != AreaStand || index < 0 || index >= Brewing.Slots) return;
            Brewing.Stands.TryGetValue(stand, out var s);
            var (key, count) = Cell(s, index);
            if (cursorKey == null)
            {
                if (key == null) return;
                int n = right ? (count + 1) / 2 : count;
                if (!shift) n = Mathf.Min(n, MaxStackOf(key));
                BlockNet.RequestBrewTake(stand, index, n, toInventory: shift && !right);
            }
            else
            {
                if (!Brewing.CanGo(index, cursorKey)) return;
                // (a bottle holder takes one)
                int n = right || index < Brewing.Bottles ? 1 : cursorCount;
                bool swap = !right && key != null && key != cursorKey;
                if (index < Brewing.Bottles && key != null && cursorCount != 1) return;
                string give = cursorKey;
                TakeCursor(n);
                BlockNet.RequestBrewPut(stand, index, give, n, swap || (index < Brewing.Bottles && key != null));
            }
        }

        /// <summary>Shift-click from the hotbar: wherever it goes (the server returns what didn't fit).</summary>
        protected override int QuickMoveIn(string key, int n)
        {
            BlockNet.RequestBrewPut(stand, -1, key, n, false);
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
            Brewing.Stands.TryGetValue(stand, out var s);
            for (int i = 0; i < Brewing.Slots; i++)
            {
                var (key, count) = Cell(s, i);
                Show(views[i], key != null ? IconOf(key) : null, count, 1f, key);
            }
        }

        protected override string HoverContent(int area, int index)
        {
            if (area != AreaStand) return null;
            Brewing.Stands.TryGetValue(stand, out var s);
            var (key, _) = Cell(s, index);
            if (key != null) return Crafting.NameOf(key);
            return index < Brewing.Bottles ? "Water bottles and potions" : index == Brewing.Ingredient ? "Ingredient: nether wart, then sugar, blaze powder, magma cream, golden carrot; redstone or glowstone" : "Fuel: blaze powder";
        }

        protected override string DevContent()
        {
            Brewing.Stands.TryGetValue(stand, out var s);
            string C(int i) { var (k, n) = Cell(s, i); return k != null ? $"{k}x{n}" : "."; }
            return $"stand={stand.Pos} b0={C(0)} b1={C(1)} b2={C(2)} ing={C(Brewing.Ingredient)} fuel={C(Brewing.Fuel)} fuelLeft={(s != null ? s.FuelLeft : 0)} progress={(s != null ? s.Progress : 0):F1}";
        }

        protected override int DevArea(string name) => name == "stand" ? AreaStand : -1;
    }
}
