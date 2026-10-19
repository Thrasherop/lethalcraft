using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    public static partial class Blocks
    {
        public static BlockDef Anvil, ChippedAnvil, DamagedAnvil;
        public static bool IsAnvil(BlockDef d) => d != null && (d == Anvil || d == ChippedAnvil || d == DamagedAnvil);

        /// <summary>(#228) Minecraft's anvil and its two worn states: they fall like sand, a pickaxe takes them.</summary>
        static void AddAnvils()
        {
            BlockDef A(ushort id, string key, string name, string top) => Add(id, key, name, "anvil", d =>
            {
                d.Shape = BlockShape.Anvil; d.TileTop = top; d.Directional = true; d.FacingIncludesVertical = false;
                d.Gravity = true; d.Sound = "anvil"; d.Render = RenderKind.Cutout;
                d.HandTime = 25f; d.PickTime = 2.5f; // (Minecraft's hardness 5; a pickaxe to get it back)
                d.Description = "Mends and joins tools and armor, and names things (a name tag names a mob). [E] to use it. Each use may wear it: chipped, damaged, then gone.";
            });
            Anvil = A(800, "anvil", "Anvil", "anvil_top");
            ChippedAnvil = A(801, "chipped_anvil", "Chipped Anvil", "chipped_anvil_top");
            DamagedAnvil = A(802, "damaged_anvil", "Damaged Anvil", "damaged_anvil_top");
        }
    }

    /// <summary>
    /// (#228) The anvil, Minecraft's (AnvilMenu): an item on the left, its material or another of it on the right, a name
    /// in the box; the cost in levels (AnvilLogic). Taking the result spends the levels, may wear the anvil (12% a use:
    /// chipped, damaged, gone, Minecraft's), and gives the item: named by the host, who keeps the name's text.
    /// </summary>
    public static class Anvils
    {
        public static int Used; // (dev/tests)

        /// <summary>The game's items, for AnvilLogic.</summary>
        public class GameItems : IAnvilItems
        {
            static ToolItem Tool(string key) => ModItems.ByKey.TryGetValue(key, out var it) && it.spawnPrefab != null ? it.spawnPrefab.GetComponent<ToolItem>() : null;
            static bool IsElytra(string key) => ItemData.Is(key, Elytra.Key);

            public int MaxUses(string key)
            {
                if (IsElytra(key)) return Elytra.MaxUses;
                if (Armor.Get(key) != null) return ArmorWear.Max(key); // (#244)
                var t = Tool(key);
                return t != null ? t.MaxUses : 0;
            }

            public int Damage(string key)
            {
                int d = ItemData.Of(key);
                if (!IsElytra(key) && Armor.Get(key) != null) return ArmorWear.Damage(key); // (#244: its wear above its trim)
                return Tool(key) is CrossbowItem ? CrossbowData.WearOf(d) : Enchants.UsesOf(d);
            }

            public string Make(string key, int damage, int ench)
            {
                int d = ItemData.Of(key);
                if (!IsElytra(key) && Armor.Get(key) != null && ArmorWear.Max(key) > 0) // (#244: armor's wear, its trim kept)
                    return ArmorWear.WithDamage(ItemData.With(key, Enchants.Data(Enchants.UsesOf(d), ench)), damage);
                int low = Tool(key) is CrossbowItem ? CrossbowData.Data(damage, CrossbowData.LoadOf(d), 0) & Enchants.UsesMask
                    : MaxUses(key) > 0 ? damage : Enchants.UsesOf(d); // (armor's low bits: its trim, kept)
                return ItemData.With(key, Enchants.Data(low, ench));
            }

            public bool HasData(string key) => Tool(key) != null || Armor.Get(key) != null && Armor.Get(key).Material != "pumpkin"
                || EnchantTargets.Of(key) == EnchTarget.Book || EnchantTargets.Of(key) == EnchTarget.Bow || EnchantTargets.Of(key) == EnchTarget.FishingRod || EnchantTargets.Of(key) == EnchTarget.FlintAndSteel; // (#244: their enchantments on their keys)

            public bool RepairsWith(string key, string material)
            {
                string m = ItemData.TypeOf(material);
                if (IsElytra(key)) return m == "phantom_membrane";
                // (#244) armor: Minecraft's repair items for each material
                var ad = Armor.Get(key);
                if (ad != null) return ArmorWear.Max(key) > 0 && m == (ad.Material == "golden" ? "gold_ingot" : ad.Material == "iron" || ad.Material == "chainmail" ? "iron_ingot" : ad.Material == "diamond" ? "diamond" : ad.Material == "netherite" ? "netherite_ingot" : ad.Material == "leather" ? "leather" : ad.Material == "turtle" ? "turtle_scute" : "-");
                var t = Tool(key);
                if (t == null || t is CrossbowItem || t.Kind == ToolKind.Shears || t.Kind == ToolKind.None) return false;
                string k = ItemData.TypeOf(key);
                if (k.StartsWith("golden_")) return m == "gold_ingot";
                if (k.StartsWith("netherite_")) return m == "netherite_ingot";
                switch (t.Tier)
                {
                    case 1: return m.EndsWith("_planks") || m == "dark_planks";
                    case 2: return Crafting.StoneMaterials.Contains(m);
                    case 3: return m == "iron_ingot";
                    case 4: return m == "diamond" || m == "scrap_diamond_ore";
                    default: return m == "netherite_ingot";
                }
            }

            public EnchTarget Target(string key) => ItemData.TypeOf(key) == "book" ? EnchTarget.None : EnchantTargets.Of(key); // (#244: Minecraft's kinds; an enchanted book takes any)
        }
        public static readonly GameItems Items = new GameItems();

        /// <summary>The item's own name (what the name box starts with when it has no name of its own).</summary>
        public static string DefaultName(string key)
        {
            var k = ItemData.WithName(key, 0);
            return k == null ? "" : ModItems.ByKey.TryGetValue(k, out var it) ? it.itemName : Blocks.Get(ItemData.TypeOf(k))?.Name ?? k;
        }

        // ------------------------------------------------------------------ falling (Minecraft's FallingBlockEntity)
        /// <summary>
        /// Host: an anvil landed after falling `fell` blocks: Minecraft's FallingBlockEntity.causeFallDamage, an anvil's
        /// 2 a block after the first, at most 40 (Minecraft's half hearts; a player's 100 here is Minecraft's 20, so five
        /// times that), to everyone and every mob in the cell it lands in; and it may wear (5% and 5% a block).
        /// </summary>
        public static void ServerLanded(BlockKey at, int fell)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(at);
            if (b == null || !Blocks.IsAnvil(b.Data.Def)) return;
            var c = w.WorldCenter(at);
            BlockNet.ServerSound(c, "anvil.land", 1f, Random.Range(0.9f, 1.1f));
            int i = fell - 1;
            int mcDamage = Mathf.Min(i * 2, 40);
            if (i <= 0 || mcDamage <= 0) return;
            Landed++;
            BlockNet.ServerAnvilHit(c, mcDamage * 5);
            // the mobs under it (the host's word)
            var hit = new System.Collections.Generic.HashSet<EnemyAI>();
            foreach (var col in Physics.OverlapBox(c, Vector3.one * (BlockWorld.S * 0.5f), Quaternion.identity, 1 << 19, QueryTriggerInteraction.Collide))
            {
                var e = col.GetComponent<EnemyAICollisionDetect>()?.mainScript;
                if (e == null || e.isEnemyDead || !hit.Add(e)) continue;
                PetDeaths.Context = "anvil"; // (#232)
                try { e.HitEnemyOnLocalClient(Mathf.Max(1, Mathf.CeilToInt(mcDamage / 5f)), Vector3.down, null, true); } finally { PetDeaths.Context = null; }
            }
            if (Random.value < 0.05f + i * 0.05f)
            {
                var next = b.Data.Def == Blocks.Anvil ? Blocks.ChippedAnvil : b.Data.Def == Blocks.ChippedAnvil ? Blocks.DamagedAnvil : null;
                if (next == null) { BlockNet.ServerBroadcastOps(new System.Collections.Generic.List<Op> { Op.Remove(at, false) }); BlockNet.ServerSound(c, "anvil.break", 1f, 1f); }
                else BlockNet.ServerBroadcastOps(new System.Collections.Generic.List<Op> { Op.Set(at, new BlockData(next.Id, b.Data.Facing, b.Data.State)) });
            }
        }
        public static int Landed; // (dev/tests)

        /// <summary>Every machine: an anvil landed there: if it's on me, it hurts (the host says how much).</summary>
        public static void ClientHit(Vector3 c, int damage)
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null || p.isPlayerDead || damage <= 0) return;
            float half = BlockWorld.S * 0.5f + 0.3f;
            var f = p.transform.position;
            if (Mathf.Abs(f.x - c.x) > half || Mathf.Abs(f.z - c.z) > half) return;
            if (f.y > c.y + BlockWorld.S * 0.5f || f.y + 2.2f < c.y - BlockWorld.S * 0.5f) return;
            p.DamagePlayer(damage, true, true, CauseOfDeath.Crushing, 0, false, Vector3.down * 3f);
        }

        // ------------------------------------------------------------------ the host
        /// <summary>Host: someone took an anvil's result (their machine spent the levels and the items): theirs, named if
        /// it was renamed; the anvil may wear.</summary>
        public static void ServerUse(ulong sender, BlockKey anvil, string key, int count, string rename)
        {
            var p = ServerLogic.PlayerFor(sender);
            var w = BlockWorld.Instance;
            if (p == null || w == null || key == null || count <= 0) return;
            if (!string.IsNullOrEmpty(rename)) key = ItemData.WithName(key, ItemNames.ServerRegister(rename));
            Inventory.ServerSpawnFor(sender, key, count);
            Used++;
            var b = w.Get(anvil);
            if (b == null || !Blocks.IsAnvil(b.Data.Def)) return;
            var at = w.WorldCenter(anvil);
            // (Minecraft's: 12% a use, not in creative)
            if (!GameModes.IsCreative(p) && Random.value < 0.12f)
            {
                var next = b.Data.Def == Blocks.Anvil ? Blocks.ChippedAnvil : b.Data.Def == Blocks.ChippedAnvil ? Blocks.DamagedAnvil : null;
                if (next == null) { BlockNet.ServerBroadcastOps(new System.Collections.Generic.List<Op> { Op.Remove(anvil, false) }); BlockNet.ServerSound(at, "anvil.break", 1f, 1f); return; }
                BlockNet.ServerBroadcastOps(new System.Collections.Generic.List<Op> { Op.Set(anvil, new BlockData(next.Id, b.Data.Facing, b.Data.State)) });
            }
            BlockNet.ServerSound(at, "anvil.use", 1f, Random.Range(0.9f, 1.1f));
        }
    }

    /// <summary>(#228) The anvil's screen: the name box at the top, two slots, the result, the cost (Minecraft's layout).</summary>
    public class AnvilUI : SlotScreen
    {
        public static AnvilUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;
        const int AreaSlots = 10, AreaOut = 11;
        readonly string[] key = new string[2];
        readonly int[] count = new int[2];
        readonly View[] views = new View[2];
        View outView;
        PixelText nameText, costText;
        Image fieldBg;
        string typed; // (null: the box untouched since the item went in)
        AnvilResult current;
        BlockKey anvil;
        bool textHooked;

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy()
        {
            base.OnDestroy(); if (Instance == this) Instance = null;
            if (textHooked && UnityEngine.InputSystem.Keyboard.current != null) UnityEngine.InputSystem.Keyboard.current.onTextInput -= OnText;
        }

        protected override Vector2 PanelSize => new Vector2(176, 104);
        protected override string Title => "Repair & Name";
        protected override int HotbarTop => 78;
        protected override bool CapturesKeys => key[0] != null; // (the name box takes the keys while there's something to name)
        protected override bool Usable(string k) => k != null;

        protected override void LayoutContent()
        {
            var edge = Rect("nameedge", panel, new Vector2(59, -20), new Vector2(105, 14)).gameObject.AddComponent<Image>();
            edge.color = new Color(0.63f, 0.63f, 0.63f); edge.raycastTarget = false;
            fieldBg = Rect("namebox", edge.transform, new Vector2(1, -1), new Vector2(103, 12)).gameObject.AddComponent<Image>();
            fieldBg.color = Color.black; fieldBg.raycastTarget = false;
            nameText = Text(fieldBg.transform, new Vector2(2, -10), Color.white, true);
            views[0] = MakeSlot(panel, new Vector2(26, -40), AreaSlots, 0);
            views[1] = MakeSlot(panel, new Vector2(75, -40), AreaSlots, 1);
            // (the plus between the two, Minecraft's anvil.png: 13 by 13, three thick, (139,139,139))
            foreach (var (at, size) in new[] { (new Vector2(49, -48), new Vector2(13, 3)), (new Vector2(54, -43), new Vector2(3, 13)) })
            {
                var bar = Rect("plus", panel, at, size).gameObject.AddComponent<Image>();
                bar.color = new Color32(139, 139, 139, 255); bar.raycastTarget = false;
            }
            MakeArrow(new Vector2(101, -40));
            outView = MakeSlot(panel, new Vector2(133, -40), AreaOut, 0);
            costText = Text(panel, new Vector2(60, -66), new Color(0.5f, 1f, 0.4f), true);
        }

        void OnText(char c)
        {
            if (!open || key[0] == null) return;
            var s = typed ?? CurrentName();
            if (c == '\b') { if (s.Length > 0) typed = s.Substring(0, s.Length - 1); Refresh(); return; }
            if (char.IsControl(c) || s.Length >= ItemNames.MaxLength) return;
            typed = s + c;
            Refresh();
        }

        string CurrentName() => key[0] == null ? "" : ItemNames.Of(key[0]) ?? Anvils.DefaultName(key[0]);

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area == AreaOut) { Take(); return; }
            if (area != AreaSlots || index < 0 || index > 1) return;
            if (cursorKey == null)
            {
                if (count[index] <= 0) return;
                if (shift) { Inventory.Give(key[index], count[index]); key[index] = null; count[index] = 0; if (index == 0) typed = null; return; }
                int n = right ? (count[index] + 1) / 2 : count[index];
                ToCursor(key[index], n);
                count[index] -= n;
                if (count[index] <= 0) { key[index] = null; count[index] = 0; if (index == 0) typed = null; }
                return;
            }
            if (count[index] <= 0 || key[index] == cursorKey)
            {
                int n = Mathf.Min(right ? 1 : cursorCount, MaxStackOf(cursorKey) - count[index]);
                if (n <= 0) return;
                if (index == 0 && key[0] != cursorKey) typed = null;
                key[index] = cursorKey; count[index] += n;
                TakeCursor(n);
            }
            else
            {
                (key[index], cursorKey) = (cursorKey, key[index]);
                (count[index], cursorCount) = (cursorCount, count[index]);
                if (index == 0) typed = null;
            }
        }

        void Take()
        {
            var r = current;
            if (r == null || r.Key == null || r.TooExpensive) return;
            bool creative = GameModes.LocalCreative;
            if (!creative && Survival.LevelFor(Survival.XpTotal) < r.Cost) { Sounds.Play2D("click", 0.3f, 0.6f); return; }
            if (!creative) Survival.SpendLevels(r.Cost);
            key[0] = null; count[0] = 0;
            count[1] -= r.RightUsed; if (count[1] <= 0) { key[1] = null; count[1] = 0; }
            BlockNet.RequestAnvil(anvil, r.Key, r.Count, r.Rename ?? "");
            typed = null;
            Refresh();
        }

        protected override int QuickMoveIn(string k, int n)
        {
            int i = key[0] == null || key[0] == k ? 0 : key[1] == null || key[1] == k ? 1 : -1;
            if (i < 0) return n;
            int t = Mathf.Min(n, MaxStackOf(k) - count[i]);
            if (t <= 0) return n;
            if (i == 0 && key[0] != k) typed = null;
            key[i] = k; count[i] += t;
            return n - t;
        }

        protected override void RefreshContent()
        {
            for (int i = 0; i < 2; i++) Show(views[i], count[i] > 0 ? IconOf(key[i]) : null, count[i], 1f, count[i] > 0 ? key[i] : null);
            current = key[0] != null ? AnvilLogic.Compute(Anvils.Items, key[0], count[0], key[1], count[1], ItemNames.Of(key[0]), Anvils.DefaultName(key[0]), typed, GameModes.LocalCreative) : null;
            bool ok = current != null && current.Key != null && !current.TooExpensive;
            Show(outView, ok ? IconOf(current.Key) : null, ok ? current.Count : 0, 1f, ok ? current.Key : null);
            string shown = key[0] == null ? "" : typed ?? CurrentName();
            string cut = shown; while (cut.Length > 0 && cut.Length * 6 > 98) cut = cut.Substring(1);
            nameText.Set(key[0] == null ? "" : cut + ((int)(Time.unscaledTime * 3f) % 2 == 0 ? "_" : ""), Color.white);
            if (current == null) costText.Set("", Color.clear);
            else if (current.TooExpensive) costText.Set("Too Expensive!", new Color(1f, 0.33f, 0.33f));
            else
            {
                bool afford = GameModes.LocalCreative || Survival.LevelFor(Survival.XpTotal) >= current.Cost;
                costText.Set("Enchantment Cost: " + current.Cost, afford ? new Color(0.5f, 1f, 0.4f) : new Color(1f, 0.33f, 0.33f));
            }
        }

        protected override string HoverContent(int area, int index)
        {
            if (area == AreaOut) return current?.Key != null ? (current.Rename ?? Crafting.NameOf(current.Key)) : null;
            if (area != AreaSlots || index < 0 || index > 1) return null;
            if (count[index] > 0) return Crafting.NameOf(key[index]);
            return index == 0 ? "What to mend, join or name" : "Its material, or another of it";
        }

        protected override void OnUpdate()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!textHooked && kb != null) { kb.onTextInput += OnText; textHooked = true; }
            if (nameText != null && key[0] != null) RefreshContent(); // (the cursor blinks)
            var b = BlockWorld.Instance?.Get(anvil);
            if (b == null || !Blocks.IsAnvil(b.Data.Def)) Close(); // (it broke, or fell)
        }

        protected override void OnClosing()
        {
            for (int i = 0; i < 2; i++) if (count[i] > 0 && key[i] != null) Inventory.Give(key[i], count[i]);
            for (int i = 0; i < 2; i++) { key[i] = null; count[i] = 0; }
            typed = null;
        }

        public static void Open(BlockKey at)
        {
            var ui = Instance;
            if (ui == null || !CanOpen(Local) || ui.open) return;
            ui.anvil = at;
            ui.Show();
        }

        protected override string DevContent() =>
            $"slots=[{string.Join(",", Enumerable.Range(0, 2).Select(i => count[i] > 0 ? key[i] + "x" + count[i] : "."))}] name='{typed ?? "-"}' out={current?.Key ?? "-"} cost={current?.Cost ?? 0}{(current != null && current.TooExpensive ? " too-expensive" : "")} used={Anvils.Used}";

        protected override int DevArea(string name) => name == "slot" ? AreaSlots : name == "out" ? AreaOut : -1;

        /// <summary>(dev) Type into the name box.</summary>
        public string DevType(string s) { typed = s; Refresh(); return DevState(); }
    }
}
