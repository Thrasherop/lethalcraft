using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's book and quill and written book (#160: for the lectern). A book's text lives with the host, saved with
    /// the ship, under a number the book item carries (its saved data); every machine gets a copy when it changes.
    /// [Right-click] with one in hand opens it: a book and quill is written in (up to 50 pages) and can be signed with a
    /// title, which makes it a written book; a written book is read.
    /// </summary>
    public static class Books
    {
        public const string Writable = "writable_book", Written = "written_book";
        public const int MaxPages = 50, PageChars = 256;
        public class Book { public string Title = "", Author = ""; public List<string> Pages = new List<string> { "" }; }
        /// <summary>The books' text (the host's own; a mirror on the others).</summary>
        public static readonly Dictionary<int, Book> All = new Dictionary<int, Book>();
        static bool loaded;
        public static int Saved, Signed; // (dev/tests)

        public static Book Of(int id) { ServerLoad(); return id > 0 && All.TryGetValue(id, out var b) ? b : null; }

        /// <summary>Host: a player saved a book (a new one gets its number); signed, the book and quill becomes a written
        /// book (the old one already out of their hand). Returns the number.</summary>
        public static void ServerSave(ulong sender, int id, List<string> pages, bool sign, string title)
        {
            ServerLoad();
            var p = ServerLogic.PlayerFor(sender);
            if (p == null) return;
            if (id <= 0 || !All.ContainsKey(id)) id = All.Count == 0 ? 1 : All.Keys.Max() + 1;
            var b = All.TryGetValue(id, out var have) ? have : (All[id] = new Book());
            b.Pages = pages.Take(MaxPages).Select(t => t.Length > PageChars ? t.Substring(0, PageChars) : t).ToList();
            if (b.Pages.Count == 0) b.Pages.Add("");
            if (sign) { b.Title = (title ?? "").Trim(); if (b.Title.Length > 32) b.Title = b.Title.Substring(0, 32); b.Author = p.playerUsername; Signed++; }
            Saved++;
            BlockNet.ServerBook(id, b);
            // the book itself: its number on it (a new book and quill), or the written book in its place
            if (sign) Inventory.ServerSpawnFor(sender, ItemData.With(Written, id), 1);
            else if (p.currentlyHeldObjectServer is BookItem held && held.BookId != id) { held.BookId = id; BlockNet.ServerItemData(held); }
        }

        /// <summary>Every machine: a book's text came from the host.</summary>
        public static void Receive(int id, Book b) { All[id] = b; BookUI.Changed(id); }

        public static void ServerSyncTo(ulong client) { ServerLoad(); foreach (var kv in All) BlockNet.ServerBook(kv.Key, kv.Value, client); }

        // ------------------------------------------------------------------ saving with the ship
        const string SaveKey = "LMC_Books_v1";
        public static void Save(string file) { ServerLoad(); ES3.Save(SaveKey, JsonConvert.SerializeObject(All), file); }
        public static void Reset() { All.Clear(); loaded = false; }

        static void ServerLoad()
        {
            if (loaded || !BlockNet.IsServer || GameNetworkManager.Instance == null) return;
            loaded = true;
            try
            {
                string file = GameNetworkManager.Instance.currentSaveFileName;
                if (!ES3.KeyExists(SaveKey, file)) return;
                var d = JsonConvert.DeserializeObject<Dictionary<int, Book>>(ES3.Load<string>(SaveKey, file));
                if (d != null) foreach (var kv in d) All[kv.Key] = kv.Value;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("books: " + e.Message); }
        }
    }

    /// <summary>A book and quill, or a written book: the number of its text (Books).</summary>
    public class BookItem : GrabbableObject
    {
        public int BookId;
        public bool IsWritten => itemProperties != null && itemProperties.name == "LMC_" + Books.Written;
        public override int GetItemDataToSave() => BookId;
        public override void LoadItemSaveData(int saveData) => BookId = saveData;
        void Awake() => SpawnFix.Clear(gameObject);

        // (opened by the use button, as Minecraft's right-click: Builder, like the bow and the rod; left-click mines)
    }

    /// <summary>
    /// The book screen: Minecraft's open book, a page of text, arrows to turn the pages. A book and quill's page is a text
    /// box (Done saves it; Sign asks for a title and makes it a written book). A written book, or a book on a lectern, is
    /// read; on a lectern, turning a page is the lectern's (a pulse of power), and Take Book takes it off.
    /// </summary>
    public class BookUI : MonoBehaviour
    {
        public static BookUI Instance;
        public static bool IsOpen => Instance != null && Instance.canvas != null && Instance.canvas.gameObject.activeSelf;
        Canvas canvas;
        RectTransform panel;
        TMPro.TMP_InputField field, titleField;
        TMPro.TextMeshProUGUI pageText, pageNum, titleLabel;
        Button prev, next, done, sign, take;
        BookItem held; BlockKey? lectern;
        int bookId, page;
        bool editable, signing;
        List<string> pages = new List<string> { "" };

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void OpenHeld(BookItem b)
        {
            var ui = Instance;
            if (ui == null || IsOpen || SlotScreen.AnyOpen) return;
            ui.held = b; ui.lectern = null; ui.bookId = b.BookId; ui.editable = !b.IsWritten; ui.page = 0;
            ui.Show();
        }

        public static void OpenLectern(BlockKey k, int bookId, int page)
        {
            var ui = Instance;
            if (ui == null || IsOpen || SlotScreen.AnyOpen) return;
            ui.held = null; ui.lectern = k; ui.bookId = bookId; ui.editable = false; ui.page = page;
            ui.Show();
        }

        public static void Changed(int id) { if (IsOpen && Instance.bookId == id && !Instance.editable) Instance.Load(); }
        public static int DevPage => Instance != null ? Instance.page : -1;
        public static string DevState() => Instance == null ? "-" : $"open={IsOpen} id={Instance.bookId} page={Instance.page}/{Instance.pages.Count} editable={Instance.editable} lectern={(Instance.lectern?.Pos.ToString() ?? "-")} text='{(Instance.pages.Count > Instance.page ? Instance.pages[Instance.page] : "")}'";

        void Load()
        {
            var b = Books.Of(bookId);
            pages = b != null ? new List<string>(b.Pages) : new List<string> { "" };
            if (pages.Count == 0) pages.Add("");
            page = Mathf.Clamp(page, 0, pages.Count - 1);
            Refresh();
        }

        void Build()
        {
            var go = new GameObject("LMC_BookUI");
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 120;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            panel = new GameObject("Book").AddComponent<RectTransform>(); panel.SetParent(go.transform, false);
            panel.sizeDelta = new Vector2(560, 700);
            var bg = panel.gameObject.AddComponent<Image>();
            var tex = McAssets.Available ? McAssets.LoadTexture("gui/book") : null;
            if (tex != null) { bg.sprite = Sprite.Create(tex, new Rect(0, tex.height - 192f * tex.width / 256f, 146f * tex.width / 256f, 180f * tex.width / 256f), new Vector2(0.5f, 0.5f)); }
            else bg.color = new Color(0.93f, 0.88f, 0.75f);
            var font = HUDManager.Instance != null ? HUDManager.Instance.chatText.font : null;
            TMPro.TextMeshProUGUI Label(string name, Vector2 pos, Vector2 size, float fs)
            {
                var t = new GameObject(name).AddComponent<TMPro.TextMeshProUGUI>(); t.rectTransform.SetParent(panel, false);
                t.rectTransform.anchoredPosition = pos; t.rectTransform.sizeDelta = size; if (font != null) t.font = font;
                t.fontSize = fs; t.color = Color.black; t.enableWordWrapping = true; t.raycastTarget = false;
                return t;
            }
            // (Minecraft's BookEditScreen, on its 146x180 page here: the text at (35, 30), 114 x 126 texels; the page
            // number right-aligned at x 148, y 14; the arrows at (43, 155) and (116, 155). It had been centred: #180)
            Vector2 At(float u, float v) => new Vector2((u - 73f) * (560f / 146f), (90f - v) * (700f / 180f));
            Vector2 Size(float w, float h) => new Vector2(w * 560f / 146f, h * 700f / 180f);
            pageNum = Label("PageNum", At(140f, 18.5f) - new Vector2(100f, 0f), new Vector2(200, 36), 22); // (right edge 140: at Minecraft's 148 its last figure fell off our page, 146 wide) pageNum.alignment = TMPro.TextAlignmentOptions.Right;
            pageText = Label("Text", At(92f, 93f), Size(114f, 126f), 32);
            titleLabel = Label("Title", new Vector2(0, 200), new Vector2(440, 60), 28); titleLabel.alignment = TMPro.TextAlignmentOptions.Center;
            // (the text boxes: copies of the game's own chat box, so they type like it)
            TMPro.TMP_InputField Field(string name, Vector2 pos, Vector2 size, bool multi)
            {
                var src = HUDManager.Instance.chatTextField;
                var f = Instantiate(src.gameObject, panel).GetComponent<TMPro.TMP_InputField>();
                f.name = name; f.onSubmit.RemoveAllListeners(); f.onEndEdit.RemoveAllListeners(); f.onValueChanged.RemoveAllListeners();
                var rt = (RectTransform)f.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
                f.lineType = multi ? TMPro.TMP_InputField.LineType.MultiLineNewline : TMPro.TMP_InputField.LineType.SingleLine;
                f.characterLimit = multi ? Books.PageChars : 32;
                if (f.placeholder != null) f.placeholder.gameObject.SetActive(false); // (the chat box's own hint, on an empty page)
                if (f.textComponent != null) { f.textComponent.color = Color.black; f.textComponent.fontSize = 24; f.textComponent.enableWordWrapping = true; }
                foreach (var g in f.GetComponentsInChildren<Graphic>(true)) if (g is Image) g.color = new Color(0, 0, 0, 0.04f);
                return f;
            }
            field = Field("Page", At(92f, 93f), Size(114f, 126f), true);
            if (field.textComponent != null) field.textComponent.fontSize = 32; // (about Minecraft's 19 letters a line)
            titleField = Field("TitleBox", new Vector2(0, 140), new Vector2(400, 50), false);
            Button Btn(string name, string text, Vector2 pos, System.Action onClick)
            {
                var b = new GameObject(name).AddComponent<Image>(); b.rectTransform.SetParent(panel, false);
                b.rectTransform.anchoredPosition = pos; b.rectTransform.sizeDelta = new Vector2(text.Length > 2 ? 170 : 60, 44); b.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
                var t = Label(name + "Text", Vector2.zero, b.rectTransform.sizeDelta, 22); t.rectTransform.SetParent(b.rectTransform, false); t.text = text; t.color = Color.white; t.alignment = TMPro.TextAlignmentOptions.Center;
                var btn = b.gameObject.AddComponent<Button>(); btn.onClick.AddListener(() => onClick());
                return btn;
            }
            prev = Btn("Prev", "<", At(54.5f, 161.5f), () => Turn(-1));
            next = Btn("Next", ">", At(127.5f, 161.5f), () => Turn(1));
            // (Minecraft's page arrows on the page, not grey buttons)
            foreach (var (btn, name) in new[] { (prev, "page_backward"), (next, "page_forward") })
            {
                var arrow = McAssets.Available ? McAssets.LoadTexture("gui/sprites/widget/" + name) : null;
                var img = btn.GetComponent<Image>();
                if (arrow == null || img == null) continue;
                img.sprite = Sprite.Create(arrow, new Rect(0, 0, arrow.width, arrow.height), new Vector2(0.5f, 0.5f)); img.color = Color.white;
                img.rectTransform.sizeDelta = Size(23f, 13f);
                foreach (var t in btn.GetComponentsInChildren<TMPro.TextMeshProUGUI>()) t.gameObject.SetActive(false);
                var hl = McAssets.LoadTexture("gui/sprites/widget/" + name + "_highlighted");
                if (hl != null)
                {
                    btn.transition = Selectable.Transition.SpriteSwap;
                    var ss = btn.spriteState; ss.highlightedSprite = Sprite.Create(hl, new Rect(0, 0, hl.width, hl.height), new Vector2(0.5f, 0.5f)); btn.spriteState = ss;
                }
            }
            done = Btn("Done", "Done", new Vector2(-100, -380), () => Close(save: true));
            sign = Btn("Sign", "Sign", new Vector2(100, -380), () => { if (!signing) { signing = true; Refresh(); } else SignNow(); });
            take = Btn("Take", "Take Book", new Vector2(100, -380), () => { if (lectern != null) BlockNet.RequestLectern(lectern.Value, 2, 0); Close(save: false); });
            canvas.gameObject.SetActive(false);
        }

        void Show()
        {
            if (canvas == null) Build();
            signing = false;
            Load();
            canvas.gameObject.SetActive(true);
            var p = GameNetworkManager.Instance.localPlayerController;
            p.inSpecialMenu = true; p.disableMoveInput = true; p.isTypingChat = editable; // (keys type, they don't act)
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Refresh();
            if (editable) { field.text = pages[page]; field.ActivateInputField(); }
        }

        void Refresh()
        {
            if (canvas == null) return;
            pageNum.text = $"Page {page + 1} of {pages.Count}";
            field.gameObject.SetActive(editable && !signing);
            pageText.gameObject.SetActive(!editable && !signing);
            pageText.text = pages[page];
            titleField.gameObject.SetActive(signing);
            titleLabel.gameObject.SetActive(signing);
            titleLabel.text = signing ? "Enter Book Title:" : "";
            prev.gameObject.SetActive(!signing && page > 0);
            next.gameObject.SetActive(!signing && (page < pages.Count - 1 || (editable && pages.Count < Books.MaxPages)));
            done.gameObject.SetActive(lectern == null);
            sign.gameObject.SetActive(editable);
            sign.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = signing ? "Sign and Close" : "Sign";
            take.gameObject.SetActive(lectern != null);
            if (signing) titleField.ActivateInputField();
        }

        void Turn(int by)
        {
            if (editable) pages[page] = field.text;
            int to = page + by;
            if (to < 0) return;
            if (to >= pages.Count) { if (!editable || pages.Count >= Books.MaxPages) return; pages.Add(""); }
            page = to;
            if (editable) { field.text = pages[page]; field.ActivateInputField(); }
            if (lectern != null) BlockNet.RequestLectern(lectern.Value, 1, page); // (the lectern's page: a pulse of power)
            Sounds.Play2D("page", 0.6f, 1f);
            Refresh();
        }

        void SignNow()
        {
            pages[page] = field.text;
            string title = titleField.text.Trim();
            if (title.Length == 0) return;
            var p = GameNetworkManager.Instance.localPlayerController;
            // (the book and quill goes; a written book comes back from the host)
            if (held != null && p.currentlyHeldObjectServer == held && Inventory.Take(p, p.currentItemSlot, 1) != null)
                BlockNet.RequestBookSave(bookId, pages, true, title);
            Close(save: false);
        }

        public void Close(bool save)
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            if (save && editable && held != null) { pages[page] = field.text; BlockNet.RequestBookSave(bookId, pages, false, null); }
            canvas.gameObject.SetActive(false);
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p != null) { p.inSpecialMenu = false; p.disableMoveInput = false; p.isTypingChat = false; }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            SlotScreen.LastClosed = Time.time; // (the click that closed it isn't a click out in the world)
        }

        void Update()
        {
            if (!IsOpen) return;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) Close(save: true);
            // (walked away from the lectern, or the book's gone from the hand)
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null) return;
            if (held != null && p.currentlyHeldObjectServer != held) Close(save: false);
            if (lectern != null && BlockWorld.Instance != null && Vector3.Distance(p.transform.position, BlockWorld.Instance.WorldCenter(lectern.Value)) > 6f) Close(save: false);
        }

        public static void DevType(string text) { if (IsOpen && Instance.editable) Instance.field.text = text; }
        public static void DevTurn(int by) { if (IsOpen) Instance.Turn(by); }
        public static void DevSign(string title) { if (IsOpen && Instance.editable) { Instance.signing = true; Instance.titleField.text = title; Instance.SignNow(); } }
    }

    /// <summary>The lectern (#160): a book on it (put on with [E]), read by anyone ([E]); turning its page gives a pulse
    /// of power, and a comparator reads how far through the book it's open. State: bits 1-7 the page, 1 the pulse.</summary>
    public static class Lecterns
    {
        public static int Pulses; // (dev/tests)

        /// <summary>Host: a lectern's book (the key of the book item on it; null: none).</summary>
        public static string BookOn(BlockKey k) { var c = Chests.Of(k); return c != null && c.Count[0] > 0 ? c.Key[0] : null; }

        /// <summary>Host: op 0 put the book on (already out of the hand), 1 turn to a page, 2 take the book.</summary>
        public static void ServerRequest(ulong sender, BlockKey k, byte op, int arg, string bookKey)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || b.Data.Def != Blocks.Lectern) { if (op == 0 && bookKey != null) Inventory.ServerSpawnFor(sender, bookKey, 1); return; }
            var c = Chests.Ensure(k);
            if (op == 0)
            {
                if (c.Count[0] > 0 || bookKey == null) { if (bookKey != null) Inventory.ServerSpawnFor(sender, bookKey, 1); return; }
                c.Key[0] = bookKey; c.Count[0] = 1;
                BlockNet.ServerChest(k, c);
                SetPage(k, b, 0, pulse: false);
                BlockNet.ServerSound(w.WorldCenter(k), "page", 0.8f, 1f);
            }
            else if (op == 1 && c.Count[0] > 0) SetPage(k, b, arg, pulse: true);
            else if (op == 2 && c.Count[0] > 0)
            {
                Inventory.ServerSpawnFor(sender, c.Key[0], 1);
                c.Key[0] = null; c.Count[0] = 0;
                BlockNet.ServerChest(k, c);
                SetPage(k, b, 0, pulse: false);
            }
        }

        static void SetPage(BlockKey k, BlockInstance b, int page, bool pulse)
        {
            var d = b.Data; d.State = (byte)((Mathf.Clamp(page, 0, 127) << 1) | (pulse ? 1 : 0));
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            Redstone.MarkDirty();
            if (!pulse) return;
            Pulses++;
            Redstone.Schedule(2, () =>
            {
                var bb = BlockWorld.Instance?.Get(k);
                if (bb == null || bb.Data.Def != Blocks.Lectern) return;
                var dd = bb.Data; dd.State = (byte)(dd.State & ~1);
                BlockNet.ServerBroadcastOp(Op.State(k, dd));
                Redstone.MarkDirty();
            });
        }

        /// <summary>What a comparator reads: how far through its book it's open (1-15), 0 with none (Minecraft's).</summary>
        public static int ComparatorLevel(BlockKey k, BlockInstance b)
        {
            var key = BookOn(k);
            if (key == null) return 0;
            var book = Books.Of(ItemData.Of(key));
            int n = book != null ? book.Pages.Count : 1, page = (b.Data.State >> 1) & 127;
            return n <= 1 ? 15 : Mathf.FloorToInt(14f * page / (n - 1)) + 1;
        }
    }
}
