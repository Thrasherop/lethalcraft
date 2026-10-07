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
    /// A Minecraft-style inventory screen: a panel with the screen's own slots (crafting grid, chest...) above your hotbar,
    /// a stack held on the mouse and tooltips. Mouse works like Minecraft: left-click picks up / places a whole stack,
    /// right-click takes half / places one, shift-click moves a stack across, clicking outside the window throws the held
    /// stack. Items really leave the hotbar (they're on the cursor or in the screen's slots) and come back into it when
    /// the screen closes; what doesn't fit drops at your feet. Subclasses supply their slots and what clicking them does.
    /// Only one screen is open at a time; while it is, the character doesn't move or act (see CraftingInputLock).
    /// </summary>
    public abstract class SlotScreen : MonoBehaviour
    {
        public static SlotScreen Current;
        public static bool AnyOpen => Current != null && Current.open;
        public static float LastClosed = -10f;

        protected const int Slot = 18;
        protected const int AreaHotbar = 0, AreaOutside = 1, AreaPanel = 2; // subclasses use 10+

        protected Canvas canvas;
        protected RectTransform root, panel;
        RectTransform cursorRt, tipRt;
        protected bool open;
        float openedAt;
        protected static Sprite panelSprite, slotSprite, slotHoverSprite, arrowSprite;

        // the stack held on the mouse (out of the hotbar while the screen is open)
        protected string cursorKey; protected int cursorCount;

        protected class View { public Image Bg, Icon; public PixelText Count; }
        readonly List<View> hotbarViews = new List<View>();
        View cursorView;
        PixelText tip;
        string hoverName;
        int hoverArea, hoverIndex = -1;

        protected static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ what a screen provides
        protected abstract Vector2 PanelSize { get; }
        protected abstract string Title { get; }
        /// <summary>Top of the hotbar row inside the panel (its "Hotbar" label sits just above).</summary>
        protected abstract int HotbarTop { get; }
        protected abstract void LayoutContent();
        protected abstract void ClickContent(int area, int index, bool right, bool shift);
        protected abstract void RefreshContent();
        protected abstract string HoverContent(int area, int index);
        /// <summary>Shift-click on a hotbar stack (already taken out of the hotbar): move it in; return what didn't fit.</summary>
        protected abstract int QuickMoveIn(string key, int n);
        /// <summary>The screen is closing: hand back whatever the screen itself holds (the cursor is handled here).</summary>
        protected virtual void OnClosing() { }
        protected virtual void OnUpdate() { }
        protected virtual string DevContent() => "";
        protected virtual int DevArea(string name) => -1;
        /// <summary>Hotbar items this screen can't use are shown dimmed.</summary>
        protected virtual bool Usable(string key) => key != null;

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

        protected RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
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

        protected PixelText Text(Transform parent, Vector2 pos, Color c, bool shadow = false)
        {
            var rt = Rect("text", parent, pos, Vector2.zero);
            rt.pivot = Vector2.zero; rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            var t = rt.gameObject.AddComponent<PixelText>();
            t.Shadow = shadow;
            t.Set("", c);
            return t;
        }

        protected static readonly Color LabelColor = new Color(0.25f, 0.25f, 0.25f);

        protected virtual void Awake()
        {
            MakeArt();
            HudAssets.Load();
            var go = new GameObject("LMC_" + GetType().Name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            go.AddComponent<GraphicRaycaster>();
            var dim = new GameObject("dim", typeof(RectTransform)).AddComponent<Image>();
            dim.transform.SetParent(go.transform, false);
            var drt = dim.rectTransform; drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one; drt.offsetMin = drt.offsetMax = Vector2.zero;
            dim.color = new Color(0, 0, 0, 0.55f);
            dim.gameObject.AddComponent<Handler>().Init(this, AreaOutside, 0);
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
            canvas.gameObject.SetActive(false);
        }

        protected virtual void OnDestroy()
        {
            if (open) Close();
            if (Current == this) Current = null;
        }

        protected View MakeSlot(Transform parent, Vector2 pos, int area, int index, bool big = false)
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

        protected Image MakeArrow(Vector2 pos)
        {
            var arrow = Rect("arrow", panel, pos, new Vector2(22, 15)).gameObject.AddComponent<Image>();
            arrow.sprite = arrowSprite; arrow.raycastTarget = false;
            return arrow;
        }

        void Layout()
        {
            foreach (Transform c in root) Destroy(c.gameObject);
            hotbarViews.Clear();
            var size = PanelSize;
            root.sizeDelta = size;
            panel = Rect("panel", root, Vector2.zero, size);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = panelSprite; bg.type = Image.Type.Sliced;
            panel.gameObject.AddComponent<Handler>().Init(this, AreaPanel, 0);
            Text(panel, new Vector2(8, -13), LabelColor).Set(Title, LabelColor);
            LayoutContent();
            Text(panel, new Vector2(8, -(HotbarTop - 5)), LabelColor).Set("Hotbar", LabelColor);
            for (int i = 0; i < 9; i++) hotbarViews.Add(MakeSlot(panel, new Vector2(8 + i * Slot, -HotbarTop), AreaHotbar, i));
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

        /// <summary>Rebuild the panel (e.g. the crafting grid changed size while open).</summary>
        protected void Relayout() { if (open) { Layout(); Refresh(); } }

        // ------------------------------------------------------------------ hotbar / cursor helpers
        /// <summary>Item key, total count and icon per hotbar slot (only Minecraft items have a key).</summary>
        protected (string key, int count, Sprite icon) SlotInfo(PlayerControllerB p, int i)
        {
            if (p == null || i >= p.ItemSlots.Length || p.ItemSlots[i] == null) return (null, 0, null);
            var g = p.ItemSlots[i];
            return (Crafting.KeyOf(g), Crafting.CountOf(g), g.itemProperties != null ? g.itemProperties.itemIcon : null);
        }

        protected static int CountIn(PlayerControllerB p, int slot) => p != null && slot < p.ItemSlots.Length ? Inventory.CountIn(p, slot) : 0;

        protected void TakeCursor(int n) { cursorCount -= n; if (cursorCount <= 0) { cursorKey = null; cursorCount = 0; } }

        /// <summary>Puts a stack on the cursor (merging with what's there), or into the inventory if the cursor holds something else.</summary>
        protected void ToCursor(string key, int n)
        {
            if (n <= 0 || key == null) return;
            if (cursorKey == null || cursorCount <= 0) { cursorKey = key; cursorCount = n; }
            else if (cursorKey == key) cursorCount += n;
            else Inventory.Give(key, n);
        }

        protected static int MaxStackOf(string key) => Inventory.MaxStackOf(key);

        protected static Sprite IconOf(string key) => key != null && ModItems.ByKey.TryGetValue(key, out var it) ? it.itemIcon : null;

        protected static void Show(View v, Sprite icon, int count, float alpha = 1f)
        {
            v.Icon.sprite = icon;
            v.Icon.enabled = icon != null && count > 0;
            v.Icon.color = new Color(1, 1, 1, alpha);
            v.Count.Set(count > 1 ? count.ToString() : "", Color.white);
        }

        // ------------------------------------------------------------------ clicks
        void Click(int area, int index, bool right)
        {
            var p = Local;
            if (p == null) return;
            bool shift = ShiftHeld;
            if (area == AreaHotbar) ClickHotbar(p, index, right, shift);
            else if (area == AreaOutside)
            {
                // like Minecraft: clicking outside the window throws the held stack (right-click: one item)
                if (cursorKey != null)
                {
                    int n = right ? 1 : cursorCount;
                    Inventory.Drop(cursorKey, n);
                    TakeCursor(n);
                }
            }
            else if (area >= 10) ClickContent(area, index, right, shift);
            Sounds.Play2D("click", 0.25f, right ? 1.3f : 1.1f);
            Refresh();
        }

        void ClickHotbar(PlayerControllerB p, int index, bool right, bool shift)
        {
            var (key, _, _) = SlotInfo(p, index);
            int have = CountIn(p, index);
            if (cursorKey == null)
            {
                if (key == null || have <= 0 || !Usable(key)) return;
                int n = right ? (have + 1) / 2 : have;
                if (Inventory.Take(p, index, n) == null) return;
                if (shift && !right)
                {
                    // straight into the screen's slots; what doesn't fit goes back
                    int left = QuickMoveIn(key, n);
                    if (left > 0) Inventory.Give(key, left);
                    return;
                }
                cursorKey = key; cursorCount = n;
            }
            else if (key == cursorKey)
            {
                TakeCursor(Inventory.GiveToSlot(p, index, cursorKey, right ? 1 : cursorCount));
            }
            else if (key == null)
            {
                // into this slot, like Minecraft: select it first, since the game picks items up into the selected slot
                // when it's free (the server spawns the stack next to us and we pick it up)
                int n = right ? 1 : cursorCount;
                if (index < p.ItemSlots.Length && p.ItemSlots[index] == null)
                {
                    HotbarInput.SelectSlot(p, index);
                    BlockNet.RequestSpawnForMe(cursorKey, n);
                }
                else Inventory.Give(cursorKey, n);
                TakeCursor(n);
            }
        }

        protected void Refresh()
        {
            var p = Local;
            if (p == null || hotbarViews.Count == 0) return;
            RefreshContent();
            for (int i = 0; i < 9; i++)
            {
                var (key, _, icon) = SlotInfo(p, i);
                // items this screen can't take are shown dimmed
                if (key != null && Usable(key)) Show(hotbarViews[i], icon, CountIn(p, i));
                else Show(hotbarViews[i], icon, icon != null ? Mathf.Max(1, CountIn(p, i)) : 0, 0.35f);
            }
            Show(cursorView, IconOf(cursorKey), cursorCount);
            if (hoverIndex >= 0) Hover(hoverArea, hoverIndex, true); // the tooltip follows what's in the slot now
        }

        void Hover(int area, int index, bool enter)
        {
            var p = Local;
            hoverName = null;
            hoverIndex = enter ? index : -1; hoverArea = area;
            if (!enter || p == null) return;
            if (area == AreaHotbar)
            {
                var (key, _, _) = SlotInfo(p, index);
                if (key != null) hoverName = Crafting.NameOf(key) + (Usable(key) ? "" : " (can't go here)");
                else if (index < p.ItemSlots.Length && p.ItemSlots[index] != null) hoverName = p.ItemSlots[index].itemProperties.itemName + " (not a Minecraft item)";
            }
            else if (area >= 10) hoverName = HoverContent(area, index);
        }

        class Handler : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            SlotScreen owner; int area; int index;
            public int Area => area; public int Index => index;
            public void Init(SlotScreen o, int a, int i) { owner = o; area = a; index = i; }
            bool IsSlot => area == AreaHotbar || area >= 10;
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
        protected static bool CanOpen(PlayerControllerB p) =>
            p != null && !p.isPlayerDead && p.isPlayerControlled && !p.inTerminalMenu && !p.isTypingChat &&
            !(p.quickMenuManager != null && p.quickMenuManager.isMenuOpen);

        protected void Show()
        {
            var p = Local;
            if (Current != null && Current != this && Current.open) Current.Close();
            Current = this;
            cursorKey = null; cursorCount = 0; hoverName = null; hoverIndex = -1;
            open = true;
            openedAt = Time.time;
            canvas.gameObject.SetActive(true);
            Layout();
            Refresh();
            p.inSpecialMenu = true;
            p.disableMoveInput = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Sounds.Play2D("click", 0.3f, 1.2f);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            // like Minecraft: the held stack goes back to the inventory (what doesn't fit drops at your feet)
            OnClosing();
            if (cursorKey != null && cursorCount > 0) Inventory.Give(cursorKey, cursorCount);
            cursorKey = null; cursorCount = 0;
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

        // ------------------------------------------------------------------ dev hooks (DevServer drives screens like a mouse would)
        public string DevClick(string area, int index, bool right, bool shift)
        {
            if (!open) return "closed";
            devShift = shift;
            int a = area == "hot" ? AreaHotbar : area == "outside" ? AreaOutside : DevArea(area);
            if (a >= 0) Click(a, index, right);
            devShift = false;
            return DevState();
        }

        /// <summary>Dev: where a slot is on screen (pixels, bottom-left origin), to click it with the real mouse.</summary>
        public string DevSlotPos(string area, int index)
        {
            if (!open) return "closed";
            int a = area == "hot" ? AreaHotbar : area == "outside" ? AreaOutside : DevArea(area);
            foreach (var h in root.GetComponentsInChildren<Handler>())
            {
                if (h.Area != a || h.Index != index) continue;
                var rt = (RectTransform)h.transform;
                var p = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                return $"{p.x:F0} {p.y:F0}";
            }
            return "none";
        }

        public string DevState()
        {
            var p = Local;
            var h = string.Join(",", Enumerable.Range(0, 9).Select(i => { var s = SlotInfo(p, i); return s.key != null ? $"{s.key}:{CountIn(p, i)}" : "-"; }));
            return $"open={open} {DevContent()} cursor={(cursorKey ?? "-")}x{cursorCount} hotbar=[{h}]";
        }

        bool devShift;
        bool ShiftHeld => devShift || (Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed));

        float refreshTimer;
        InputAction interact;
        protected virtual void Update()
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
            OnUpdate();
            if (!open) return;
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
