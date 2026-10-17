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
    /// The book screen, Minecraft's BookEditScreen / BookViewScreen / BookSignScreen / LecternScreen as drawn (#223, the
    /// owner: it was off): the whole open book (gui/book, 192 x 192) at the top of the screen, 2 down; "Page 1 of 1" right
    /// against x 148, 16 down; the page in Minecraft's font from (36, 30) (a book and quill's from 35, 30), 114 wide, 9 a
    /// line; the page arrows at (43, 157) and (116, 157); the buttons 2 under the book: Sign and Done (98 wide each), Done
    /// alone (200) for a written book, Done and Take Book on a lectern. Sign: "Enter Book Title:", the title box (15
    /// letters), "by" you, Minecraft's warning, Sign and Close and Cancel. The typing goes into hidden text boxes (copies of
    /// the game's chat box) so it types like the chat; Minecraft's font draws it, with its blinking cursor.
    /// </summary>
    public class BookUI : MonoBehaviour
    {
        public static BookUI Instance;
        public static bool IsOpen => Instance != null && Instance.canvas != null && Instance.canvas.gameObject.activeSelf;
        Canvas canvas;
        RectTransform root, view, textLayer;
        TMPro.TMP_InputField field, titleField;
        BookItem held; BlockKey? lectern;
        int bookId, page;
        bool editable, signing;
        List<string> pages = new List<string> { "" };
        int shownW, shownH;
        float blinkFrom;
        string drawn;

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
        public static string DevState() => Instance == null ? "-" : $"open={IsOpen} id={Instance.bookId} page={Instance.page}/{Instance.pages.Count} editable={Instance.editable} signing={Instance.signing} lectern={(Instance.lectern?.Pos.ToString() ?? "-")} text='{(Instance.pages.Count > Instance.page ? Instance.pages[Instance.page] : "")}'";

        void Load()
        {
            var b = Books.Of(bookId);
            pages = b != null ? new List<string>(b.Pages) : new List<string> { "" };
            if (pages.Count == 0) pages.Add("");
            page = Mathf.Clamp(page, 0, pages.Count - 1);
            Layout();
        }

        void Build()
        {
            root = McGui.NewCanvas("LMC_BookUI", 120, out canvas);
            McGui.Dim(root);
            // (the text boxes: copies of the game's chat box, so they type like it; not seen, Minecraft's font draws them)
            TMPro.TMP_InputField Field(string name, bool multi, int limit)
            {
                var src = HUDManager.Instance.chatTextField;
                var f = Instantiate(src.gameObject, root).GetComponent<TMPro.TMP_InputField>();
                f.name = name; f.onSubmit.RemoveAllListeners(); f.onEndEdit.RemoveAllListeners(); f.onValueChanged.RemoveAllListeners();
                var rt = (RectTransform)f.transform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(-4000, 0); rt.sizeDelta = new Vector2(600, 600);
                f.lineType = multi ? TMPro.TMP_InputField.LineType.MultiLineNewline : TMPro.TMP_InputField.LineType.SingleLine;
                f.characterLimit = limit;
                f.onFocusSelectAll = false; // (back to the page: the cursor where it was, nothing selected)
                if (f.placeholder != null) f.placeholder.gameObject.SetActive(false);
                if (f.textComponent != null) f.textComponent.color = new Color(0, 0, 0, 0);
                f.customCaretColor = true; f.caretColor = new Color(0, 0, 0, 0); f.selectionColor = new Color(0, 0, 0, 0);
                foreach (var g in f.GetComponentsInChildren<Graphic>(true)) if (g is Image) g.color = new Color(0, 0, 0, 0);
                return f;
            }
            field = Field("Page", true, Books.PageChars);
            titleField = Field("TitleBox", false, 15); // (Minecraft's title: 15 letters)
            titleField.onSubmit.AddListener(_ => SignNow()); // (Enter signs, as Minecraft's)
            canvas.gameObject.SetActive(false);
        }

        int Left => (McGui.Width - 192) / 2;
        const int Top = 2;

        /// <summary>The book, its arrows and buttons for what's showing (again when the page, the mode or the screen size
        /// changes); the text is drawn on top each frame it changes.</summary>
        void Layout()
        {
            if (canvas == null) return;
            if (view != null) Destroy(view.gameObject);
            int W = McGui.Width, H = McGui.Height, left = Left;
            shownW = Screen.width; shownH = Screen.height;
            view = McGui.Box(root, "View", 0, 0, W, H);
            var tex = McAssets.Available ? McAssets.LoadTexture("gui/book") : null;
            McGui.Blit(view, tex, left, Top, 192, 192, 0, 0, 192, 192, 256, 256);
            if (signing)
            {
                var sc = McGui.Button(view, "Sign and Close", W / 2 - 100, 196, 98, 20, SignNow);
                McGui.Button(view, "Cancel", W / 2 + 2, 196, 98, 20, () => { signing = false; Layout(); field.ActivateInputField(); });
                signButton = sc;
            }
            else
            {
                if (page > 0) McGui.SpriteButton(view, "widget/page_backward", left + 43, Top + 157, 23, 13, () => Turn(-1));
                if (page < pages.Count - 1 || (editable && pages.Count < Books.MaxPages)) McGui.SpriteButton(view, "widget/page_forward", left + 116, Top + 157, 23, 13, () => Turn(1));
                if (lectern != null)
                {
                    McGui.Button(view, "Done", W / 2 - 100, 196, 98, 20, () => Close(save: false));
                    McGui.Button(view, "Take Book", W / 2 + 2, 196, 98, 20, () => { if (lectern != null) BlockNet.RequestLectern(lectern.Value, 2, 0); Close(save: false); });
                }
                else if (editable)
                {
                    McGui.Button(view, "Sign", W / 2 - 100, 196, 98, 20, () => { pages[page] = field.text; signing = true; Layout(); titleField.ActivateInputField(); titleField.MoveTextEnd(false); }); // (the title typed before, Cancel kept)
                    McGui.Button(view, "Done", W / 2 + 2, 196, 98, 20, () => Close(save: true));
                }
                else McGui.Button(view, "Done", (W - 200) / 2, 196, 200, 20, () => Close(save: false));
            }
            textLayer = McGui.Box(view, "Text", 0, 0, W, H);
            drawn = null;
        }
        Button signButton;

        void Show()
        {
            if (canvas == null) Build();
            signing = false; titleField.text = "";
            canvas.gameObject.SetActive(true);
            Load();
            var p = GameNetworkManager.Instance.localPlayerController;
            p.inSpecialMenu = true; p.disableMoveInput = true; p.isTypingChat = editable; // (keys type, they don't act)
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (editable) { field.text = pages[page]; field.ActivateInputField(); field.MoveTextEnd(false); }
            blinkFrom = Time.unscaledTime;
        }

        void Turn(int by)
        {
            if (editable) pages[page] = field.text;
            int to = page + by;
            if (to < 0) return;
            if (to >= pages.Count) { if (!editable || pages.Count >= Books.MaxPages) return; pages.Add(""); }
            page = to;
            if (editable) { field.text = pages[page]; field.ActivateInputField(); field.MoveTextEnd(false); }
            if (lectern != null) BlockNet.RequestLectern(lectern.Value, 1, page); // (the lectern's page: a pulse of power)
            Sounds.Play2D("page", 0.6f, 1f);
            Layout();
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
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) { Close(save: true); return; }
            // (walked away from the lectern, or the book's gone from the hand)
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null) return;
            if (held != null && p.currentlyHeldObjectServer != held) { Close(save: false); return; }
            if (lectern != null && BlockWorld.Instance != null && Vector3.Distance(p.transform.position, BlockWorld.Instance.WorldCenter(lectern.Value)) > 6f) { Close(save: false); return; }
            // (a click on the screen took the typing away: back to the box)
            if (signing && !titleField.isFocused) titleField.ActivateInputField();
            else if (!signing && editable && !field.isFocused) field.ActivateInputField();
            if (Screen.width != shownW || Screen.height != shownH) Layout();
            if (editable && !signing) KeepToPage();
            Draw();
        }

        /// <summary>No more than a page holds (Minecraft's: 14 lines of 114 pixels; what won't fit isn't typed).</summary>
        void KeepToPage()
        {
            string s = field.text;
            while (s.Length > 0 && McGui.Wrap(s, 114).Count > 14) s = s.Substring(0, s.Length - 1);
            if (s != field.text) field.text = s;
        }

        static readonly Color Black = Color.black;

        /// <summary>The page number, the page's text and the cursor (or the signing page), redrawn when they change.</summary>
        void Draw()
        {
            int left = Left;
            bool on = McGui.CursorOn(blinkFrom);
            string body = signing ? titleField.text : editable ? field.text : pages[page];
            int caret = signing ? titleField.stringPosition : editable ? field.stringPosition : -1;
            string state = body + "|" + caret + "|" + on + "|" + page + "|" + pages.Count + "|" + signing;
            if (state == drawn) return;
            if (drawn != null && !drawn.StartsWith(body + "|")) { blinkFrom = Time.unscaledTime; on = true; state = body + "|" + caret + "|" + on + "|" + page + "|" + pages.Count + "|" + signing; } // (typing shows the cursor)
            drawn = state;
            McGui.Clear(textLayer);
            if (signing)
            {
                const string head = "Enter Book Title:";
                McGui.Text(textLayer, head, left + 36 + (114 - McFont.Width(head)) / 2f, 34, Black, false);
                int W = McGui.Width;
                // (Minecraft's title box: no frame, the title centred in its 114 at y 50, black, no shadow)
                float fx = (W - 114) / 2 - 3, fy = 50, tx0 = fx + Mathf.Floor((114 - McFont.Width(body)) / 2f);
                McGui.Text(textLayer, body, tx0, fy, Black, false);
                if (on)
                {
                    float cx = tx0 + McFont.Width(body.Substring(0, Mathf.Clamp(caret, 0, body.Length)));
                    if (caret >= body.Length) McGui.Text(textLayer, "_", cx, fy, Black, false);
                    else McGui.Bar(textLayer, cx - 1, fy, 9, Black);
                }
                string by = "by " + (GameNetworkManager.Instance?.localPlayerController?.playerUsername ?? "");
                McGui.Text(textLayer, by, left + 36 + (114 - McFont.Width(by)) / 2f, 60, new Color(0.333f, 0.333f, 0.333f), false);
                const string warn = "Note! When you sign the book, it will no longer be editable.";
                int y = 82;
                foreach (var (st, len) in McGui.Wrap(warn, 114)) { McGui.Text(textLayer, warn.Substring(st, len), left + 36, y, Black, false); y += 9; }
                if (signButton != null)
                {
                    bool can = body.Trim().Length > 0;
                    if (signButton.interactable != can) { signButton.interactable = can; McGui.SetLabel(signButton, "Sign and Close"); }
                }
                return;
            }
            string num = $"Page {page + 1} of {Mathf.Max(pages.Count, 1)}";
            McGui.Text(textLayer, num, left + 148 - McFont.Width(num), Top + 16, Black, false);
            float tx = editable ? (McGui.Width - 114) / 2 - 4 : left + 36, ty = Top + 30;
            var wrapped = McGui.Wrap(body, 114);
            bool drewCursor = false;
            for (int i = 0; i < wrapped.Count && i < 14; i++)
            {
                var (st, len) = wrapped[i];
                float y = ty + i * 9;
                McGui.Text(textLayer, body.Substring(st, len), tx, y, Black, false);
                // (the insert cursor in the line it's in; past the end of the text, "_" after it)
                if (on && caret >= 0 && caret < body.Length && !drewCursor && caret >= st && caret <= st + len)
                {
                    McGui.Bar(textLayer, tx + McFont.Width(body.Substring(st, caret - st)), y, 9, Black);
                    drewCursor = true;
                }
            }
            if (on && caret >= 0 && caret >= body.Length)
            {
                int last = Mathf.Min(wrapped.Count, 14) - 1;
                var (st, len) = wrapped[Mathf.Max(last, 0)];
                bool newLine = body.EndsWith("\n");
                float cx = newLine ? tx : tx + McFont.Width(body.Substring(st, len));
                float cy = ty + Mathf.Max(last, 0) * 9;
                McGui.Text(textLayer, "_", cx, cy, Black, false);
            }
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
