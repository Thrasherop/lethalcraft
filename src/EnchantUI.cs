using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The enchanting table's screen (#46), Minecraft's: an item slot and a lapis slot, and three offers. Put in a tool,
    /// sword or piece of armor (not already enchanted) and some lapis lazuli; each offer needs a level and costs 1, 2 or 3
    /// lapis and as many levels. The bookshelves around the table (up to 15, within two blocks) make the offers better.
    /// What's in the slots goes back to the inventory when the screen closes. The enchantments ride on the item's key
    /// (ItemData), so the item comes back enchanted like a worn tool comes back worn.
    /// </summary>
    public class EnchantUI : SlotScreen
    {
        public static EnchantUI Instance;
        const int AreaItem = 10, AreaLapis = 11, AreaOffer = 12;
        public const string Lapis = "lapis_lazuli";
        BlockKey table;
        string itemKey; int lapis;
        View itemView, lapisView;
        readonly Image[] offerBg = new Image[3];
        readonly PixelText[] offerName = new PixelText[3], offerReq = new PixelText[3];
        Enchants.Offer[] offers = new Enchants.Offer[3];
        int shelves;
        static int seed = -1;

        protected override void Awake() { base.Awake(); Instance = this; TableGlyphs.Ensure(); }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        protected override Vector2 PanelSize => new Vector2(176, 112);
        protected override string Title => "Enchant";
        protected override int HotbarTop => 87;
        protected override bool Usable(string key) => ItemData.Is(key, Lapis) || CanEnchant(key); // (#228: named lapis too)

        static int Seed
        {
            get
            {
                if (seed < 0) { try { seed = ES3.Load("LMC_EnchSeed", "LCGeneralSaveData", -1); } catch { } }
                if (seed < 0) NewSeed();
                return seed;
            }
        }
        static void NewSeed() { seed = Random.Range(0, int.MaxValue); try { ES3.Save("LMC_EnchSeed", seed, "LCGeneralSaveData"); } catch { } }

        /// <summary>What an item key can be enchanted as here (None: it can't, or it already is). (#244: Minecraft's: whatever
        /// has an enchantability: tools but not shears, armor, a bow, crossbow or fishing rod, a book.)</summary>
        public static EnchTarget TargetOf(string key)
        {
            if (key == null || Enchants.Any(Enchants.EnchOf(ItemData.Of(key)))) return EnchTarget.None;
            if (ItemData.TypeOf(key) == AnvilLogic.Book) return EnchTarget.None;
            var t = EnchantTargets.Of(key);
            return EnchantTargets.Enchantability(key) > 0 ? t : EnchTarget.None;
        }
        static bool CanEnchant(string key) => TargetOf(key) != EnchTarget.None;

        protected override void LayoutContent()
        {
            itemView = MakeSlot(panel, new Vector2(14, -40), AreaItem, 0);
            lapisView = MakeSlot(panel, new Vector2(34, -40), AreaLapis, 0);
            for (int i = 0; i < 3; i++)
            {
                var rt = Rect("offer", panel, new Vector2(60, -(14 + i * 21)), new Vector2(108, 19));
                offerBg[i] = rt.gameObject.AddComponent<Image>();
                offerBg[i].sprite = slotSprite; offerBg[i].type = Image.Type.Sliced;
                rt.gameObject.AddComponent<Handler>().Init(this, AreaOffer, i);
                offerName[i] = Text(rt, new Vector2(3, -13), new Color(0.75f, 0.7f, 0.95f), true);
                offerReq[i] = Text(rt, new Vector2(105, -13), new Color(0.5f, 1f, 0.3f), true);
                offerReq[i].Alignment = PixelText.Align.Right;
            }
        }

        public static void Open(BlockKey k)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local)) return;
            ui.table = k;
            ui.itemKey = null; ui.lapis = 0;
            ui.shelves = CountShelves(k);
            ui.Show();
        }

        /// <summary>Bookshelves within two blocks of the table (its level and the one above), like Minecraft's ring.</summary>
        public static int CountShelves(BlockKey k) => System.Math.Min(15, PoweringShelves(k).Count);

        /// <summary>(#244, Minecraft's EnchantingTableBlock.isValidBookShelf) The ring's bookshelves with air between them and
        /// the table (the cell halfway, toward the table): a block there cuts that shelf off.</summary>
        public static System.Collections.Generic.List<BlockKey> PoweringShelves(BlockKey k)
        {
            var list = new System.Collections.Generic.List<BlockKey>();
            var w = BlockWorld.Instance;
            if (w == null) return list;
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        if (System.Math.Abs(dx) < 2 && System.Math.Abs(dz) < 2) continue; // (the ring around it, not next to it)
                        var c = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(dx, dy, dz));
                        if (w.DefAt(c) != Blocks.Bookshelf) continue;
                        var mid = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(dx / 2, dy, dz / 2));
                        var md = w.DefAt(mid);
                        if (md != null && md.Solid) continue;
                        list.Add(c);
                    }
            return list;
        }

        protected override void OnClosing()
        {
            // what's still in the slots goes back to the inventory
            if (itemKey != null) Inventory.Give(itemKey, 1);
            if (lapis > 0) Inventory.Give(Lapis, lapis);
            itemKey = null; lapis = 0;
        }

        protected override void OnUpdate()
        {
            var p = Local;
            var w = BlockWorld.Instance;
            if (w == null || w.DefAt(table) != Blocks.EnchantingTable) { Close(); return; }
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(table)) > 8f) Close();
        }

        int Level => Survival.Instance != null ? Survival.Instance.XpLevel : 0;
        bool Creative => GameModes.IsCreative(Local);

        bool Affordable(int i) =>
            itemKey != null && CanEnchant(itemKey) && (Creative || (lapis >= offers[i].Cost && Level >= offers[i].Required && Level >= offers[i].Cost));

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaItem)
            {
                if (cursorKey == null) { if (itemKey != null) { if (shift) Inventory.Give(itemKey, 1); else ToCursor(itemKey, 1); itemKey = null; } }
                else if (cursorCount == 1 && (CanEnchant(cursorKey) || Enchants.EnchOf(ItemData.Of(cursorKey)) != 0 && itemKey == null))
                {
                    // (an enchanted one can go in too, it just gets no offers: Minecraft lets you)
                    var old = itemKey; itemKey = cursorKey; TakeCursor(1);
                    if (old != null) ToCursor(old, 1);
                }
            }
            else if (area == AreaLapis)
            {
                if (cursorKey == null)
                {
                    if (lapis <= 0) return;
                    int n = right ? (lapis + 1) / 2 : lapis;
                    if (shift) Inventory.Give(Lapis, n); else ToCursor(Lapis, n);
                    lapis -= n;
                }
                else if (ItemData.Is(cursorKey, Lapis))
                {
                    int n = Mathf.Min(right ? 1 : cursorCount, 64 - lapis);
                    lapis += n; TakeCursor(n);
                }
            }
            else if (area == AreaOffer && index >= 0 && index < 3)
            {
                if (!Affordable(index)) { Sounds.Play2D("click", 0.3f, 0.6f); return; }
                var o = offers[index];
                int data = ItemData.Of(itemKey);
                // (#244: the list's number may still be on its way from the host: then not yet)
                int ench = o.List != null ? Enchants.Pack(o.List, Enchants.WorkOf(Enchants.EnchOf(data))) : -1; // (#228: its anvil work kept)
                if (ench < 0) { McHud.Toast("One moment..."); Sounds.Play2D("click", 0.3f, 0.6f); return; }
                // (a book becomes an enchanted book, Minecraft's)
                string baseKey = ItemData.TypeOf(itemKey) == "book" ? ItemData.WithName(AnvilLogic.Book, ItemData.NameId(itemKey)) : itemKey;
                itemKey = ItemData.With(baseKey, Enchants.Data(Enchants.UsesOf(data), ench));
                if (!Creative) { lapis -= o.Cost; Survival.SpendLevels(o.Cost); }
            Advancements.Grant("enchanter");
                NewSeed();
                Sounds.Play2D("enchant", 0.7f, Random.Range(0.9f, 1.1f));
            }
            Refresh();
        }

        protected override int QuickMoveIn(string key, int n)
        {
            if (ItemData.Is(key, Lapis)) { int t = Mathf.Min(n, 64 - lapis); lapis += t; return n - t; }
            if (itemKey == null && n == 1 && CanEnchant(key)) { itemKey = key; return 0; }
            return n;
        }

        protected override void RefreshContent()
        {
            Show(itemView, itemKey != null ? IconOf(itemKey) : null, itemKey != null ? 1 : 0, 1f, itemKey);
            Show(lapisView, lapis > 0 ? IconOf(Lapis) : null, lapis);
            var t = TargetOf(itemKey);
            offers = t != EnchTarget.None ? Enchants.Offers(Seed, shelves, t, EnchantTargets.Enchantability(itemKey)) : new Enchants.Offer[3];
            for (int i = 0; i < 3; i++)
            {
                bool any = t != EnchTarget.None && offers[i].List != null;
                bool ok = any && Affordable(i);
                // (#244, Minecraft's: the button in the Galactic alphabet; what it is shows on hover)
                offerName[i].Alt = true;
                offerName[i].Set(any ? GalacticWords(Seed + i) : "", ok ? new Color(0.85f, 0.8f, 1f) : new Color(0.45f, 0.42f, 0.5f));
                offerReq[i].Set(any ? offers[i].Required.ToString() : "", ok ? new Color(0.5f, 1f, 0.3f) : new Color(0.25f, 0.45f, 0.2f));
                offerBg[i].color = ok ? new Color(0.85f, 0.75f, 0.95f) : new Color(0.55f, 0.5f, 0.55f);
            }
        }

        /// <summary>What an offer hints at (Minecraft's clue: one of its enchantments, "Efficiency II . . . ?").</summary>
        static string Hint(Enchants.Offer o)
        {
            if (o.List == null || o.List.Count == 0) return "";
            var c = o.Clue;
            return Enchants.Name(c.kind) + (Enchants.MaxLevel(c.kind) == 1 ? "" : " " + Enchants.Roman(c.level)) + " . . . ?";
        }

        /// <summary>A few made-up words for a button (Minecraft writes random ones in the Galactic alphabet: nobody can read
        /// them anyway), the same for a seed.</summary>
        static string GalacticWords(int seed)
        {
            var r = new System.Random(seed);
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            var words = new System.Collections.Generic.List<string>();
            int len = 0, n = 3 + r.Next(3);
            for (int i = 0; i < n && len < 16; i++)
            {
                var w = new System.Text.StringBuilder();
                for (int j = 0, l = 2 + r.Next(5); j < l; j++) w.Append(letters[r.Next(letters.Length)]);
                words.Add(w.ToString()); len += w.Length + 1;
            }
            return string.Join(" ", words);
        }

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaItem && itemKey != null) return Crafting.NameOf(itemKey);
            if (area == AreaLapis && lapis > 0) return Crafting.NameOf(Lapis);
            if (area == AreaOffer && index >= 0 && index < 3 && TargetOf(itemKey) != EnchTarget.None)
            {
                var o = offers[index];
                if (o.List == null) return null;
                return $"{Hint(o)}  {o.Cost} lapis, {o.Cost} level{(o.Cost > 1 ? "s" : "")} (needs level {o.Required})";
            }
            return null;
        }

        protected override string DevContent()
        {
            var t = TargetOf(itemKey);
            var os = t != EnchTarget.None ? string.Join(",", offers.Select(o => o.List == null ? "-" : $"{o.Required}:{o.Cost}:{string.Join("+", o.List.Select(e => e.kind + "" + e.level))}:clue={o.Clue.kind}{o.Clue.level}")) : "-";
            return $"table={table.Pos} item={itemKey ?? "-"} lapis={lapis} shelves={shelves} level={Level} offers=[{os}]";
        }

        protected override int DevArea(string name) => name == "item" ? AreaItem : name == "lapis" ? AreaLapis : name == "offer" ? AreaOffer : -1;
    }
}
