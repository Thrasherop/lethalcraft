using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft crafting screen: a 3x3 grid at a Crafting Table ([E]) or the 2x2 pocket grid ([I] anywhere), an output
    /// slot and your hotbar. Arrange items in a recipe's pattern and take the result. Mouse works like Minecraft:
    /// left-click picks up / places a whole stack, right-click takes half / places one, shift-click moves a stack into
    /// the grid, shift-click the output crafts as many as you can, clicking outside the window throws the held stack.
    /// Items really leave the hotbar; results and leftovers come back into it (topping up stacks, then free slots), and
    /// whatever doesn't fit drops at your feet, just like Minecraft.
    /// </summary>
    public class CraftingUI : MonoBehaviour
    {
        public static CraftingUI Instance;
        public static bool IsOpen => Instance != null && Instance.open;
        public static float LastClosed = -10f;

        Canvas canvas;
        RectTransform root, panel, cursorRt, tipRt;
        bool open, full;
        float openedAt;
        System.Action<InputAction.CallbackContext> onToggle;
        static Sprite panelSprite, slotSprite, slotHoverSprite, arrowSprite;
        const int Slot = 18;

        // ---- state: what's in the grid cells and on the cursor (taken out of the hotbar)
        int size = 3;
        string[] gridKey = new string[9];
        int[] gridCount = new int[9];
        string cursorKey; int cursorCount;

        class View { public Image Bg, Icon; public PixelText Count; }
        readonly List<View> gridViews = new List<View>();
        readonly List<View> hotbarViews = new List<View>();
        View outputView, cursorView;
        PixelText title, tip;
        Recipe current;

        void Awake()
        {
            Instance = this;
            onToggle = _ => { if (open) Close(); else Open(false); };
            if (ModKeys.PocketCraft != null) ModKeys.PocketCraft.performed += onToggle;
            Build();
            canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (ModKeys.PocketCraft != null) ModKeys.PocketCraft.performed -= onToggle;
            if (open) Close();
            if (Instance == this) Instance = null;
        }

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ art (Minecraft GUI style, generated)
        static Sprite Nine(int w, int h, int border, System.Func<int, int, Color32> fn)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = fn(x, h - 1 - y);
            t.SetPixels32(px); t.Apply();
            // 100 px per unit = the canvas reference, so a 1 px border is 1 GUI pixel
            return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        static void MakeArt()
        {
            if (panelSprite != null) return;
            // light gray panel: black outline, white top-left bevel, dark bottom-right bevel
            panelSprite = Nine(16, 16, 4, (x, y) =>
            {
                bool outer = x == 0 || y == 0 || x == 15 || y == 15;
                if (outer) return (x == 0 && y == 0) || (x == 15 && y == 15) || (x == 0 && y == 15) || (x == 15 && y == 0) ? new Color32(0, 0, 0, 0) : new Color32(0, 0, 0, 255);
                if (x <= 2 || y <= 2) return (x + y <= 3) ? new Color32(198, 198, 198, 255) : new Color32(255, 255, 255, 255);
                if (x >= 13 || y >= 13) return new Color32(85, 85, 85, 255);
                return new Color32(198, 198, 198, 255);
            });
            System.Func<Color32, System.Func<int, int, Color32>> inset = fill => (x, y) =>
            {
                if (x == 0 || y == 0) return x == 17 || y == 17 ? new Color32(139, 139, 139, 255) : new Color32(55, 55, 55, 255);
                if (x == 17 || y == 17) return new Color32(255, 255, 255, 255);
                return fill;
            };
            slotSprite = Nine(18, 18, 1, inset(new Color32(139, 139, 139, 255)));
            slotHoverSprite = Nine(18, 18, 1, inset(new Color32(197, 197, 197, 255)));
            // the gray crafting arrow
            arrowSprite = Nine(22, 15, 0, (x, y) =>
            {
                bool shaft = y >= 5 && y <= 9 && x <= 13;
                bool head = x >= 14 && Mathf.Abs(y - 7) <= 21 - x;
                return shaft || head ? new Color32(139, 139, 139, 255) : new Color32(0, 0, 0, 0);
            });
        }

        RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        PixelText Text(Transform parent, Vector2 pos, Color c, bool shadow = false)
        {
            var rt = Rect("text", parent, pos, Vector2.zero);
            rt.pivot = Vector2.zero; rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            var t = rt.gameObject.AddComponent<PixelText>();
            t.Shadow = shadow;
            t.Set("", c);
            return t;
        }

        void Build()
        {
            MakeArt();
            HudAssets.Load();
            var go = new GameObject("LMC_CraftingUI", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            go.AddComponent<GraphicRaycaster>();
            var dim = new GameObject("dim", typeof(RectTransform)).AddComponent<Image>();
            dim.transform.SetParent(go.transform, false);
            var drt = dim.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
            dim.color = new Color(0, 0, 0, 0.55f);
            dim.gameObject.AddComponent<Handler>().Init(this, Area.Outside, 0);
            root = (RectTransform)new GameObject("root", typeof(RectTransform)).transform;
            root.SetParent(go.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            if (EventSystem.current == null)
            {
                var es = new GameObject("LMC_EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                es.transform.SetParent(transform, false);
            }
        }

        View MakeSlot(Transform parent, Vector2 pos, Area area, int index, bool big = false)
        {
            var srt = Rect("slot", parent, pos, new Vector2(Slot, Slot) + (big ? new Vector2(8, 8) : Vector2.zero));
            var v = new View { Bg = srt.gameObject.AddComponent<Image>() };
            v.Bg.sprite = slotSprite; v.Bg.type = Image.Type.Sliced;
            var irt = Rect("icon", srt, big ? new Vector2(5, -5) : new Vector2(1, -1), new Vector2(16, 16));
            v.Icon = irt.gameObject.AddComponent<Image>();
            v.Icon.preserveAspect = true; v.Icon.raycastTarget = false;
            v.Count = Text(srt, big ? new Vector2(21, -21) : new Vector2(17, -17), Color.white, true);
            v.Count.Alignment = PixelText.Align.Right;
            srt.gameObject.AddComponent<Handler>().Init(this, area, index);
            return v;
        }

        void Layout()
        {
            foreach (Transform c in root) Destroy(c.gameObject);
            gridViews.Clear(); hotbarViews.Clear();
            const int W = 176, H = 112;
            root.sizeDelta = new Vector2(W, H);
            panel = Rect("panel", root, Vector2.zero, new Vector2(W, H));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = panelSprite; bg.type = Image.Type.Sliced;
            panel.gameObject.AddComponent<Handler>().Init(this, Area.Panel, 0);
            title = Text(panel, new Vector2(8, -13), new Color(0.25f, 0.25f, 0.25f));
            title.Set(full ? "Crafting" : "Crafting (2x2)", new Color(0.25f, 0.25f, 0.25f));
            int gx = 30 + (3 - size) * 9, gy = 17 + (3 - size) * 9;
            for (int i = 0; i < size * size; i++)
                gridViews.Add(MakeSlot(panel, new Vector2(gx + (i % size) * Slot, -(gy + (i / size) * Slot)), Area.Grid, i));
            var arrow = Rect("arrow", panel, new Vector2(90, -35), new Vector2(22, 15)).gameObject.AddComponent<Image>();
            arrow.sprite = arrowSprite; arrow.raycastTarget = false;
            outputView = MakeSlot(panel, new Vector2(120, -31), Area.Output, 0, true);
            var hb = Text(panel, new Vector2(8, -82), new Color(0.25f, 0.25f, 0.25f));
            hb.Set("Hotbar", new Color(0.25f, 0.25f, 0.25f));
            for (int i = 0; i < 9; i++) hotbarViews.Add(MakeSlot(panel, new Vector2(8 + i * Slot, -87), Area.Hotbar, i));
            // held stack follows the mouse; tooltip
            cursorRt = Rect("cursor", root, Vector2.zero, new Vector2(16, 16));
            cursorView = new View { Icon = cursorRt.gameObject.AddComponent<Image>() };
            cursorView.Icon.raycastTarget = false; cursorView.Icon.preserveAspect = true;
            cursorView.Count = Text(cursorRt, new Vector2(17, -17), Color.white, true);
            cursorView.Count.Alignment = PixelText.Align.Right;
            tipRt = Rect("tip", root, Vector2.zero, new Vector2(10, 10));
            var tipBg = tipRt.gameObject.AddComponent<Image>(); tipBg.color = new Color(0.07f, 0f, 0.12f, 0.92f); tipBg.raycastTarget = false;
            tip = Text(tipRt, new Vector2(3, -10), Color.white, true);
            tipRt.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ inventory bookkeeping
        /// <summary>Item key, total count and icon per hotbar slot (only Minecraft items can be used for crafting).</summary>
        (string key, int count, Sprite icon) SlotInfo(PlayerControllerB p, int i)
        {
            if (p == null || i >= p.ItemSlots.Length || p.ItemSlots[i] == null) return (null, 0, null);
            var g = p.ItemSlots[i];
            return (Crafting.KeyOf(g), Crafting.CountOf(g), g.itemProperties != null ? g.itemProperties.itemIcon : null);
        }

        int CountIn(PlayerControllerB p, int slot) => p != null && slot < p.ItemSlots.Length ? Inventory.CountIn(p, slot) : 0;

        string[] GridKeys() => Enumerable.Range(0, size * size).Select(i => gridCount[i] > 0 ? gridKey[i] : null).ToArray();

        // ------------------------------------------------------------------ clicks
        enum Area { Grid, Output, Hotbar, Panel, Outside }

        void Click(Area area, int index, bool right)
        {
            var p = Local;
            if (p == null) return;
            bool shift = ShiftHeld;
            switch (area)
            {
                case Area.Hotbar:
                    {
                        var (key, _, _) = SlotInfo(p, index);
                        int have = CountIn(p, index);
                        if (cursorKey == null)
                        {
                            if (key == null || have <= 0) break;
                            int n = right ? (have + 1) / 2 : have;
                            if (Inventory.Take(p, index, n) == null) break;
                            if (shift && !right) { QuickMove(key, n); break; } // straight into the grid
                            cursorKey = key; cursorCount = n;
                        }
                        else if (key == cursorKey)
                        {
                            int put = Inventory.GiveToSlot(p, index, cursorKey, right ? 1 : cursorCount);
                            cursorCount -= put;
                            if (cursorCount <= 0) { cursorKey = null; cursorCount = 0; }
                        }
                        else if (key == null)
                        {
                            // into the inventory (the game picks it up into the first free slot)
                            int n = right ? 1 : cursorCount;
                            Inventory.Give(cursorKey, n);
                            cursorCount -= n;
                            if (cursorCount <= 0) { cursorKey = null; cursorCount = 0; }
                        }
                        break;
                    }
                case Area.Grid:
                    {
                        if (index >= size * size) break;
                        if (cursorKey == null)
                        {
                            if (gridCount[index] <= 0) break;
                            if (shift && !right) { Inventory.Give(gridKey[index], gridCount[index]); gridKey[index] = null; gridCount[index] = 0; break; } // straight back to the inventory
                            int n = right ? (gridCount[index] + 1) / 2 : gridCount[index];
                            cursorKey = gridKey[index]; cursorCount = n;
                            gridCount[index] -= n;
                            if (gridCount[index] <= 0) { gridKey[index] = null; gridCount[index] = 0; }
                        }
                        else if (gridCount[index] <= 0 || gridKey[index] == cursorKey)
                        {
                            int n = Mathf.Min(right ? 1 : cursorCount, 64 - gridCount[index]);
                            if (n <= 0) break;
                            gridKey[index] = cursorKey; gridCount[index] += n;
                            cursorCount -= n;
                            if (cursorCount <= 0) { cursorKey = null; cursorCount = 0; }
                        }
                        else
                        {
                            // swap the held stack with the cell
                            (gridKey[index], cursorKey) = (cursorKey, gridKey[index]);
                            (gridCount[index], cursorCount) = (cursorCount, gridCount[index]);
                        }
                        break;
                    }
                case Area.Output:
                    Craft(p, shift);
                    break;
                case Area.Outside:
                    // like Minecraft: clicking outside the window throws the held stack (right-click: one item)
                    if (cursorKey != null)
                    {
                        int n = right ? 1 : cursorCount;
                        Inventory.Drop(cursorKey, n);
                        cursorCount -= n;
                        if (cursorCount <= 0) { cursorKey = null; cursorCount = 0; }
                    }
                    break;
            }
            Sounds.Play2D("click", 0.25f, right ? 1.3f : 1.1f);
            Refresh();
        }

        /// <summary>Shift-click a hotbar stack: move it into matching grid cells, then the first empty one.</summary>
        void QuickMove(string key, int n)
        {
            for (int pass = 0; pass < 2 && n > 0; pass++)
                for (int i = 0; i < size * size && n > 0; i++)
                {
                    if (pass == 0 ? gridKey[i] != key || gridCount[i] <= 0 : gridCount[i] > 0) continue;
                    int t = Mathf.Min(n, 64 - gridCount[i]);
                    gridKey[i] = key; gridCount[i] += t; n -= t;
                    if (pass == 1) break;
                }
        }

        IEnumerable<Recipe> Available => Crafting.Recipes.Where(x => full || x.Pocket);

        void Craft(PlayerControllerB p, bool all)
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

        // ------------------------------------------------------------------ drawing
        static Sprite IconOf(string key) => key != null && ModItems.ByKey.TryGetValue(key, out var it) ? it.itemIcon : null;

        static void Show(View v, Sprite icon, int count, float alpha = 1f)
        {
            v.Icon.sprite = icon;
            v.Icon.enabled = icon != null && count > 0;
            v.Icon.color = new Color(1, 1, 1, alpha);
            v.Count.Set(count > 1 ? count.ToString() : "", Color.white);
        }

        void Refresh()
        {
            var p = Local;
            if (p == null || gridViews.Count == 0) return;
            for (int i = 0; i < gridViews.Count; i++) Show(gridViews[i], IconOf(gridKey[i]), gridCount[i]);
            for (int i = 0; i < 9; i++)
            {
                var (key, _, icon) = SlotInfo(p, i);
                // non-Minecraft items are shown dimmed: they can't be used for crafting
                if (key != null) Show(hotbarViews[i], icon, CountIn(p, i));
                else Show(hotbarViews[i], icon, icon != null ? 1 : 0, 0.35f);
            }
            current = RecipeBook.Match(Available, GridKeys(), size);
            Show(outputView, current != null ? IconOf(current.Result) : null, current != null ? current.Count : 0);
            Show(cursorView, IconOf(cursorKey), cursorCount);
            if (hoverIndex >= 0) Hover(hoverArea, hoverIndex, true); // the tooltip follows what's in the slot now
        }

        string hoverName;
        Area hoverArea; int hoverIndex = -1;
        void Hover(Area area, int index, bool enter)
        {
            var p = Local;
            hoverName = null;
            hoverIndex = enter ? index : -1; hoverArea = area;
            if (!enter || p == null) return;
            switch (area)
            {
                case Area.Grid: if (index < size * size && gridCount[index] > 0) hoverName = Crafting.NameOf(gridKey[index]); break;
                case Area.Output: if (current != null) hoverName = Crafting.NameOf(current.Result) + (current.Count > 1 ? " x" + current.Count : ""); break;
                case Area.Hotbar:
                    {
                        var (key, _, _) = SlotInfo(p, index);
                        if (key != null) hoverName = Crafting.NameOf(key);
                        else if (index < p.ItemSlots.Length && p.ItemSlots[index] != null) hoverName = p.ItemSlots[index].itemProperties.itemName + " (not a crafting item)";
                        break;
                    }
            }
        }

        class Handler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            CraftingUI owner; Area area; int index;
            public void Init(CraftingUI o, Area a, int i) { owner = o; area = a; index = i; }
            bool IsSlot => area == Area.Grid || area == Area.Hotbar || area == Area.Output;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Middle) return;
                owner.Click(area, index, e.button == PointerEventData.InputButton.Right);
            }
            public void OnPointerEnter(PointerEventData e)
            {
                if (IsSlot) GetComponent<Image>().sprite = slotHoverSprite;
                owner.Hover(area, index, true);
            }
            public void OnPointerExit(PointerEventData e)
            {
                if (IsSlot) GetComponent<Image>().sprite = slotSprite;
                owner.Hover(area, index, false);
            }
        }

        // ------------------------------------------------------------------ open / close
        public static void Open(bool withTable)
        {
            var ui = Instance;
            var p = Local;
            if (ui == null || p == null || p.isPlayerDead || !p.isPlayerControlled) return;
            if (p.inTerminalMenu || p.isTypingChat || (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen)) return;
            if (ui.open) { if (withTable && !ui.full) { ui.full = true; ui.Reset(3); ui.Layout(); ui.Refresh(); } return; }
            ui.full = withTable;
            ui.Reset(withTable ? 3 : 2);
            ui.open = true;
            ui.openedAt = Time.time;
            ui.canvas.gameObject.SetActive(true);
            ui.Layout();
            ui.Refresh();
            p.inSpecialMenu = true;
            p.disableMoveInput = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Sounds.Play2D("click", 0.3f, 1.2f);
        }

        void Reset(int n)
        {
            size = n;
            gridKey = new string[9]; gridCount = new int[9];
            cursorKey = null; cursorCount = 0;
            hoverName = null;
        }

        /// <summary>Closing returns everything to the hotbar automatically: nothing ever left it.</summary>
        public void Close()
        {
            if (!open) return;
            open = false;
            // like Minecraft: the grid and the held stack go back to the inventory; what doesn't fit drops at your feet
            var back = new Dictionary<string, int>();
            for (int i = 0; i < 9; i++) if (gridCount[i] > 0 && gridKey[i] != null) back[gridKey[i]] = (back.TryGetValue(gridKey[i], out int b0) ? b0 : 0) + gridCount[i];
            if (cursorKey != null && cursorCount > 0) back[cursorKey] = (back.TryGetValue(cursorKey, out int b1) ? b1 : 0) + cursorCount;
            foreach (var kv in back) Inventory.Give(kv.Key, kv.Value);
            Reset(size);
            LastClosed = Time.time;
            canvas.gameObject.SetActive(false);
            var p = Local;
            if (p != null) p.disableMoveInput = false;
            if (p != null && p.inSpecialMenu)
            {
                p.inSpecialMenu = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------ dev hooks (DevServer drives the grid like a mouse would)
        public string DevClick(string area, int index, bool right, bool shift)
        {
            if (!open) return "closed";
            devShift = shift;
            var a = area == "grid" ? Area.Grid : area == "out" ? Area.Output : area == "hot" ? Area.Hotbar : Area.Outside;
            Click(a, index, right);
            devShift = false;
            return DevState();
        }

        public string DevState()
        {
            var p = Local;
            var g = string.Join(",", Enumerable.Range(0, size * size).Select(i => gridCount[i] > 0 ? $"{gridKey[i]}x{gridCount[i]}" : "."));
            var h = string.Join(",", Enumerable.Range(0, 9).Select(i => { var s = SlotInfo(p, i); return s.key != null ? $"{s.key}:{CountIn(p, i)}" : "-"; }));
            return $"open={open} size={size} grid=[{g}] cursor={(cursorKey ?? "-")}x{cursorCount} out={(current != null ? current.Result + "x" + current.Count : "-")} hotbar=[{h}]";
        }

        bool devShift;
        bool ShiftHeld => devShift || (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed));

        float refreshTimer;
        InputAction interact;
        void Update()
        {
            if (!open) return;
            var p = Local;
            // Esc (game closes special menus), death, or E closes
            if (p == null || p.isPlayerDead || !p.inSpecialMenu) { Close(); return; }
            var kb = Keyboard.current;
            // the game's Interact key (E unless rebound) closes the screen, like Minecraft's inventory key
            if (interact == null) try { interact = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Interact"); } catch { }
            bool interactPressed = interact != null ? interact.WasPressedThisFrame() : (kb != null && kb.eKey.wasPressedThisFrame);
            if (interactPressed && Time.time - openedAt > 0.2f) { Close(); return; }
            if (!Cursor.visible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            int scale = Mathf.Clamp(Mathf.FloorToInt(Screen.height / 300f), 1, 8);
            root.localScale = Vector3.one * scale;
            // inventory changes arrive from the server asynchronously
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = 0.2f; Refresh(); }
            // held stack and tooltip follow the mouse
            var mouse = Mouse.current;
            if (mouse != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, mouse.position.ReadValue(), null, out var local))
            {
                var topLeft = local + new Vector2(root.sizeDelta.x * 0.5f, -root.sizeDelta.y * 0.5f);
                cursorRt.anchoredPosition = topLeft + new Vector2(-8, 8);
                cursorRt.SetAsLastSibling();
                if (!string.IsNullOrEmpty(hoverName) && cursorKey == null)
                {
                    tipRt.gameObject.SetActive(true);
                    tip.Set(hoverName, Color.white);
                    tipRt.sizeDelta = new Vector2(tip.Width + 6, 13);
                    tipRt.anchoredPosition = topLeft + new Vector2(10, -2);
                    tipRt.SetAsLastSibling();
                }
                else tipRt.gameObject.SetActive(false);
            }
        }
    }
}
