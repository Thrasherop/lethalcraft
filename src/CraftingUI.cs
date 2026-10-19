using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft crafting screen: a 3x3 grid at a Crafting Table ([E]) or the inventory ([I] anywhere: your four armor
    /// slots, a window with your character, the 2x2 pocket grid), an output slot and your hotbar. Arrange items in a recipe's pattern and take the result; shift-click the output to craft as
    /// many as you can. Grid items are out of the hotbar while the screen is open and come back when it closes.
    /// With the big inventory on (Storage), Minecraft's 3x9 storage grid sits above the hotbar (SlotScreen: #135, at the
    /// table too).
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
                if (CreativeUI.Searching) return; // (typing in the creative menu's search box)
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
            // (#244) the effects on you, beside the screen: icon, name and level, time left (Minecraft's EffectsInInventory)
            effRows.Clear();
            for (int i = 0; i < 10; i++)
            {
                var row = Rect("eff" + i, panel, new Vector2(178, -(i * 33)), new Vector2(120, 32));
                var rbg = row.gameObject.AddComponent<UnityEngine.UI.Image>(); rbg.raycastTarget = false;
                var bgs = EffectRowSprite();
                if (bgs != null) { rbg.sprite = bgs; rbg.type = UnityEngine.UI.Image.Type.Sliced; } else rbg.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                var ir = Rect("icon", row, new Vector2(7, -7), new Vector2(18, 18));
                var icon = ir.gameObject.AddComponent<UnityEngine.UI.Image>(); icon.raycastTarget = false; icon.preserveAspect = true;
                var name = Text(row, new Vector2(30, -14), Color.white, true);
                var time = Text(row, new Vector2(30, -25), new Color(0.5f, 0.5f, 0.5f), false);
                effRows.Add((row, icon, name, time));
            }
        }

        readonly List<(RectTransform row, UnityEngine.UI.Image icon, PixelText name, PixelText time)> effRows = new List<(RectTransform, UnityEngine.UI.Image, PixelText, PixelText)>();
        static readonly string[] Roman = { "", "", " II", " III", " IV", " V" };

        static Sprite effectRowSprite; static bool effectRowTried;
        /// <summary>Minecraft's effect_background (a nine-slice, border 4).</summary>
        static Sprite EffectRowSprite()
        {
            if (effectRowTried) return effectRowSprite;
            effectRowTried = true;
            var t = McAssets.Available ? McAssets.LoadTexture("gui/sprites/container/inventory/effect_background") : null;
            if (t != null) { t.filterMode = FilterMode.Point; effectRowSprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width / 32f, 0, SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4) * (t.width / 32f)); }
            return effectRowSprite;
        }

        void UpdateEffectRows()
        {
            if (full || effRows.Count == 0) return;
            var list = Potions.Active().Where(a => a.effect != Potions.Effect.InstantHealth && a.effect != Potions.Effect.InstantDamage).OrderByDescending(a => a.left).ToList();
            // (Minecraft's: 33 apart, closer to fit more than five into 165)
            float stepY = list.Count > 5 ? 132f / (list.Count - 1) : 33f;
            for (int i = 0; i < effRows.Count; i++)
            {
                bool on = i < list.Count;
                if (effRows[i].row.gameObject.activeSelf != on) effRows[i].row.gameObject.SetActive(on);
                if (!on) continue;
                effRows[i].row.anchoredPosition = new Vector2(178, -(i * stepY));
                effRows[i].row.SetAsLastSibling();
                var (e, lv, left) = list[i];
                effRows[i].icon.sprite = HudAssets.EffectIcons.TryGetValue(e, out var sp) ? sp : null;
                effRows[i].icon.enabled = effRows[i].icon.sprite != null;
                string n = EffectName(e) + (lv > 1 && lv < Roman.Length ? Roman[lv] : "");
                effRows[i].name.Set(n, Color.white);
                int s = Mathf.CeilToInt(left);
                effRows[i].time.Set($"{s / 60}:{s % 60:00}", new Color(0.5f, 0.5f, 0.5f));
            }
        }

        static string EffectName(Potions.Effect e) => e switch
        {
            Potions.Effect.Swiftness => "Speed", Potions.Effect.FireResistance => "Fire Resistance", Potions.Effect.NightVision => "Night Vision",
            Potions.Effect.JumpBoost => "Jump Boost", Potions.Effect.SlowFalling => "Slow Falling", Potions.Effect.BadOmen => "Bad Omen",
            Potions.Effect.WaterBreathing => "Water Breathing", _ => e.ToString(),
        };

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

        string[] GridKeys() => Enumerable.Range(0, size * size).Select(i => gridCount[i] > 0 ? gridKey[i] : null).ToArray(); // (RecipeBook goes by their types, #228)

        IEnumerable<Recipe> Available => Crafting.Recipes.Where(x => (full || x.Pocket) && Crafting.Allowed(x));

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
            // with the big inventory: into the storage, like Minecraft (the server hands back what doesn't fit)
            if (!full && withStorage && Storage.Enabled)
            {
                BlockNet.RequestStoragePut(-1, key, n, false);
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

        /// <summary>
        /// Mending an elytra (owner's answer on #61): a worn elytra and string in the grid, nothing else. Each string mends a
        /// quarter of it, like Minecraft's phantom membrane (no phantoms here: a spider's silk sews it up instead).
        /// </summary>
        Recipe RepairMatch()
        {
            // (string, or Minecraft's phantom membrane, #141: one or the other)
            string elytra = null, mender = null; int strings = 0;
            for (int i = 0; i < size * size; i++)
            {
                if (gridCount[i] <= 0 || gridKey[i] == null) continue;
                var type = ItemData.TypeOf(gridKey[i]); // (#228: a named string sews too)
                if (type == Elytra.Key) { if (elytra != null) return null; elytra = gridKey[i]; }
                else if ((type == "string" || type == "phantom_membrane") && (mender == null || mender == type)) { mender = type; strings++; }
                else return null;
            }
            if (elytra == null || strings == 0 || Elytra.MaxUses <= 0) return null;
            int data = ItemData.Of(elytra), used = Enchants.UsesOf(data);
            if (used <= 0) return null; // (as good as new already)
            int mended = Mathf.Max(0, used - strings * Mathf.CeilToInt(Elytra.MaxUses / 4f));
            string result = ItemData.WithName(mended > 0 || Enchants.EnchOf(data) != 0 ? ItemData.With(Elytra.Key, Enchants.Data(mended, Enchants.EnchOf(data))) : Elytra.Key, ItemData.NameId(elytra)); // (#228: the mended elytra keeps its name)
            return new Recipe { Category = "Repair", Result = result, Count = 1, Shapeless = true, Needs = new[] { (Elytra.Key, 1), (mender, strings) } };
        }

        /// <summary>
        /// (#244) Minecraft's RepairItemRecipe: two of the same thing that wears, nothing else in the grid: one, with what both
        /// had left and a twentieth of its durability more (most its full), its enchantments gone.
        /// </summary>
        Recipe ToolRepairMatch()
        {
            string a = null, b = null;
            for (int i = 0; i < size * size; i++)
            {
                if (gridCount[i] <= 0 || gridKey[i] == null) continue;
                if (gridCount[i] != 1) return null;
                if (a == null) a = gridKey[i]; else if (b == null) b = gridKey[i]; else return null;
            }
            if (a == null || b == null || ItemData.TypeOf(a) != ItemData.TypeOf(b) || ItemData.Is(a, Elytra.Key)) return null;
            var (ua, max) = Durability.Of(a);
            var (ub, _) = Durability.Of(b);
            if (max <= 0) return null;
            int left = Mathf.Min(max, (max - ua) + (max - ub) + max * 5 / 100);
            string type = ItemData.TypeOf(a), result;
            if (Armor.Get(type) != null) result = ArmorWear.WithDamage(type, max - left);
            else result = max - left > 0 ? ItemData.With(type, Enchants.Data(max - left, 0)) : type;
            return new Recipe { Category = "Repair", Result = result, Count = 1, Shapeless = true, Needs = new[] { (type, 2) } };
        }
        public static int GridRepairs; // (dev/tests)

        /// <summary>
        /// (#243) Minecraft's banner copy (BannerDuplicateRecipe): a banner with patterns and a plain banner of the same colour,
        /// nothing else: the plain one becomes a copy (its name too), the patterned one stays in the grid.
        /// </summary>
        Recipe BannerCopyMatch() => BannerCopyMatch(out _);
        Recipe BannerCopyMatch(out int keep)
        {
            keep = -1;
            string patterned = null, plain = null;
            for (int i = 0; i < size * size; i++)
            {
                if (gridCount[i] <= 0 || gridKey[i] == null) continue;
                if (BannerDesigns.IdOf(gridKey[i]) > 0) { if (patterned != null || gridCount[i] != 1) return null; patterned = gridKey[i]; keep = i; }
                else if (BannerColor(gridKey[i]) >= 0) { if (plain != null || gridCount[i] != 1) return null; plain = gridKey[i]; }
                else return null;
            }
            if (patterned == null || plain == null) return null;
            if (!BannerDesigns.Designs.TryGetValue(BannerDesigns.IdOf(patterned), out var spec)) return null;
            var (baseColor, layers) = BannerDesigns.Parse(spec);
            if (layers.Count == 0 || layers.Count > BannerDesigns.MaxLayers || baseColor != BannerColor(plain)) return null;
            return new Recipe { Category = "Banner", Result = patterned, Count = 1, Shapeless = true, Needs = new[] { (ItemData.TypeOf(plain), 1) } };
        }

        /// <summary>A plain banner's colour (its dye's number), or -1.</summary>
        static int BannerColor(string key)
        {
            var t = ItemData.TypeOf(key);
            return t != null && t.EndsWith("_banner") ? System.Array.IndexOf(Dyes.Colors, t.Substring(0, t.Length - 7)) : -1;
        }

        public static int BannerCopies; // (dev/tests)

        void Craft(bool all)
        {
            var copy = BannerCopyMatch(out int keep);
            var toolRepair = ToolRepairMatch(); // (#244)
            var repair = RepairMatch() ?? toolRepair ?? copy;
            var r = repair ?? RecipeBook.Match(Available, GridKeys(), size);
            if (r == null) return;
            int times = 1;
            if (all && repair == null)
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
            // (the banner copied from stays: Minecraft's remaining item)
            if (copy != null && keep >= 0) { gridKey[keep] = copy.Result; gridCount[keep] = 1; BannerCopies++; }
            Inventory.Give(r.Result, r.Count * times);
            Sounds.Play2D("pop", 0.5f, 1.2f);
            if (toolRepair != null && repair == toolRepair) GridRepairs++;
            McHud.Toast(copy != null ? "Copied the banner" : toolRepair != null && repair == toolRepair ? $"Repaired the {Crafting.NameOf(r.Result)} ({Durability.Of(r.Result).max - Durability.Of(r.Result).used}/{Durability.Of(r.Result).max})" : repair != null ? $"Mended the elytra ({Elytra.MaxUses - Enchants.UsesOf(ItemData.Of(r.Result))}/{Elytra.MaxUses})" : $"Crafted {Crafting.NameOf(r.Result)} x{r.Count * times}");
        }

        protected override void RefreshContent()
        {
            UpdateEffectRows(); // (#244)
            for (int i = 0; i < gridViews.Count; i++) Show(gridViews[i], IconOf(gridKey[i]), gridCount[i]);
            for (int i = 0; i < armorViews.Count; i++)
            {
                var worn = Armor.Local[i];
                if (worn != null) Show(armorViews[i], IconOf(worn), 1, 1f, worn); // (#244: its wear bar and glint)
                else Show(armorViews[i], EmptyArmorIcon(i), 1, 0.9f);
            }
            current = RepairMatch() ?? ToolRepairMatch() ?? BannerCopyMatch() ?? RecipeBook.Match(Available, GridKeys(), size);
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
            return $"size={size} grid=[{g}] out={(current != null ? current.Result + "x" + current.Count : "-")} armor=[{string.Join(",", Armor.Local.Select(k => k ?? "-"))}] pts={Armor.PointsOf(Armor.Local)} " +
                $"storageShown={withStorage} storage: {Storage.DevDescribe()}";
        }

        protected override int DevArea(string name) => name == "grid" ? AreaGrid : name == "out" ? AreaOutput : name == "armor" ? AreaArmor : -1;
    }
}
