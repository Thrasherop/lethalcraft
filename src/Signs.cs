using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Signs and hanging signs (#223, the owner's: for every wood), Minecraft's: a sign stands on the ground (turned to
    /// face you, in 16ths) or hangs on a wall; a hanging sign hangs from the block over it (on chains in a V; straight
    /// ones, any turn, while you crouch) or from a wall on a bracket. Both have text on both faces: four lines (a sign's
    /// 90 font pixels wide, a hanging sign's 60), written as it's placed and changed with [E] (the face toward you), in
    /// Minecraft's font, drawn where its sign renderers draw it. Honeycomb waxes one: no more changes. Their models are
    /// Minecraft's own (models/block/template_*sign*.json, read from the player's Minecraft). The text is the host's,
    /// sent to everyone and saved with the ship's blocks. Hanging signs are made with iron chains (added with them: iron
    /// nuggets and the chain block, Minecraft's).
    /// </summary>
    public static class Signs
    {
        public class Text
        {
            public string[] Front = { "", "", "", "" }, Back = { "", "", "", "" };
            public byte FrontColor, BackColor; // (a dye's index; 0 black, Minecraft's default)
            public bool Waxed;
        }

        public const int Lines = 4;
        public const byte Attached = 16; // (a hanging sign's state: straight chains, any turn)
        public static readonly Dictionary<BlockKey, Text> All = new Dictionary<BlockKey, Text>();
        public static int Edits; // (dev/tests)
        const string SaveKey = "LMC_Signs_v1";

        public static bool IsSign(BlockDef d) => d != null && (d.Shape == BlockShape.Sign || d.Shape == BlockShape.HangingSign);
        public static bool IsHanging(BlockDef d) => d != null && d.Shape == BlockShape.HangingSign;
        public static int MaxWidth(BlockDef d) => IsHanging(d) ? 60 : 90;
        public static Text Of(BlockKey k) => All.TryGetValue(k, out var t) ? t : null;

        /// <summary>A line cut to what fits the sign (Minecraft's: the font's width, not a count of letters).</summary>
        public static string Fit(string s, int maxWidth)
        {
            s = (s ?? "").Replace("\n", " ").Replace("\r", "");
            if (s.Length > 50) s = s.Substring(0, 50);
            while (s.Length > 0 && McFont.Ready && McFont.Width(s) > maxWidth) s = s.Substring(0, s.Length - 1);
            return s;
        }

        /// <summary>Which face of a sign a player at `from` sees (SignBlockEntity.isFacingFrontText: within 90 degrees of
        /// the way it faces).</summary>
        public static bool FacingFront(BlockInstance b, Vector3 from)
        {
            if (b?.Go == null) return true;
            var front = -b.Go.transform.forward; // (its front: the block's -z, Minecraft's model's south)
            var to = from - b.Go.transform.position; to.y = 0f;
            return Vector3.Dot(front, to) >= 0f;
        }

        // ------------------------------------------------------------------ the local player
        /// <summary>[E] on a sign: honeycomb waxes it; otherwise, unless it's waxed, edit the face toward you.</summary>
        public static void UseLocal(PlayerControllerB p, BlockInstance b)
        {
            var held = p.currentlyHeldObjectServer as StackItem;
            if (held != null && held.ItemKey == "honeycomb")
            {
                if (Of(b.Key)?.Waxed != true) BlockNet.RequestSign(b.Key, 1, 0, null);
                return;
            }
            if (Of(b.Key)?.Waxed == true) { Sounds.Play("click", b.Go.transform.position, 0.5f, 0.8f); return; } // (Minecraft's waxed sign: a little knock)
            SignUI.Open(b.Key, FacingFront(b, p.gameplayCamera.transform.position));
        }

        // ------------------------------------------------------------------ the host
        public static void ServerRequest(ulong sender, BlockKey k, byte op, byte side, string[] lines)
        {
            var w = BlockWorld.Instance;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null || !IsSign(w.DefAt(k))) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            if (!All.TryGetValue(k, out var t)) All[k] = t = new Text();
            if (op == 1)
            {
                if (t.Waxed || !(p.currentlyHeldObjectServer is StackItem hc) || hc.ItemKey != "honeycomb") return;
                if (!GameModes.IsCreative(sender)) hc.ServerUseUp(p);
                t.Waxed = true;
                BlockNet.ServerSound(w.WorldCenter(k), "honeycomb.wax", 1f, 1f);
            }
            else
            {
                if (t.Waxed || lines == null) return;
                int max = MaxWidth(w.DefAt(k));
                var dest = side == 0 ? t.Front : t.Back;
                for (int i = 0; i < Lines; i++) dest[i] = Fit(i < lines.Length ? lines[i] : "", max);
                Edits++;
            }
            BlockNet.ServerSignState(k, t);
            if (k.Frame == 1 && StartOfRound.Instance.inShipPhase) ShipPersistence.Save();
        }

        /// <summary>Host: the sign's gone (broken, burned, blown up).</summary>
        public static void ServerRemoved(BlockKey k)
        {
            if (!All.Remove(k)) return;
            BlockNet.ServerSignState(k, null);
        }

        public static void ServerSyncTo(ulong client) { foreach (var kv in All) BlockNet.ServerSignState(kv.Key, kv.Value, client); }

        /// <summary>Every client: a sign's text (null: gone).</summary>
        public static void Receive(BlockKey k, Text t)
        {
            if (t == null) All.Remove(k); else All[k] = t;
            var b = BlockWorld.Instance?.Get(k);
            if (b?.Go != null && b.Go.GetComponent<SignBlock>() is SignBlock sb) sb.Refresh();
        }

        // ------------------------------------------------------------------ saved with the ship
        public static void Save(string file)
        {
            try
            {
                var ms = new MemoryStream(); var wr = new BinaryWriter(ms);
                var ship = All.Where(kv => kv.Key.Frame == 1).ToList();
                wr.Write(ship.Count);
                foreach (var kv in ship)
                {
                    wr.Write(kv.Key.YOff); wr.Write(kv.Key.Pos.x); wr.Write(kv.Key.Pos.y); wr.Write(kv.Key.Pos.z);
                    foreach (var s in kv.Value.Front.Concat(kv.Value.Back)) wr.Write(s ?? "");
                    wr.Write(kv.Value.FrontColor); wr.Write(kv.Value.BackColor); wr.Write(kv.Value.Waxed);
                }
                ES3.Save(SaveKey, Convert.ToBase64String(ms.ToArray()), file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("signs save: " + e.Message); }
        }

        public static void Load(string file)
        {
            try
            {
                foreach (var k in All.Keys.Where(k => k.Frame == 1).ToList()) All.Remove(k);
                if (!ES3.KeyExists(SaveKey, file)) return;
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SaveKey, file))));
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    short yoff = r.ReadInt16(); int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                    var t = new Text();
                    for (int j = 0; j < Lines; j++) t.Front[j] = r.ReadString();
                    for (int j = 0; j < Lines; j++) t.Back[j] = r.ReadString();
                    t.FrontColor = r.ReadByte(); t.BackColor = r.ReadByte(); t.Waxed = r.ReadBoolean();
                    var k = new BlockKey(1, yoff, new Vector3Int(x, y, z));
                    All[k] = t;
                    BlockNet.ServerSignState(k, t);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("signs load: " + e.Message); }
        }

        /// <summary>A dye's text colour (DyeColor.getTextColor), by its index; 0 black.</summary>
        public static Color32 TextColor(byte dye)
        {
            uint[] c = { 0x000000, 0xF9801D, 0xC74EBD, 0x3AB3DA, 0xFED83D, 0x80C71F, 0xF38BAA, 0x474F52, 0x9D9D97, 0x169C9C, 0x8932B8, 0x3C44AA, 0x835432, 0x5E7C16, 0xB02E26, 0xF0F0F0 };
            uint v = c[dye < c.Length ? dye : 0];
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }
    }

    /// <summary>A placed sign's text (every client draws its own): a quad on each face, its picture the lines in
    /// Minecraft's font, where Minecraft's StandingSignRenderer and HangingSignRenderer put them.</summary>
    public class SignBlock : MonoBehaviour
    {
        public BlockKey Key;
        GameObject front, back;
        Texture2D frontTex, backTex;
        static Material baseMat;

        public static void Attach(BlockInstance b)
        {
            if (b?.Go == null) return;
            var sb = b.Go.GetComponent<SignBlock>() ?? b.Go.AddComponent<SignBlock>();
            sb.Key = b.Key;
            sb.Refresh();
        }

        public void Refresh()
        {
            var b = BlockWorld.Instance?.Get(Key);
            if (b == null) return;
            var def = b.Data.Def;
            bool hanging = Signs.IsHanging(def), wall = b.Data.Facing != (byte)(hanging ? Face.Down : Face.Up);
            var t = Signs.Of(Key);
            // (the text's origin and its pixel, in the block's own space: the block's centre, its front toward -z)
            float px; Vector3 at;
            if (hanging) { px = 0.0140625f; at = new Vector3(0f, -0.195f, 0.073f); }
            else if (wall) { px = 1f / 96f; at = new Vector3(0f, 0.0208f, 0.4375f - 0.0467f); }
            else { px = 1f / 96f; at = new Vector3(0f, 0.3333f, 0.0467f); }
            int w = Signs.MaxWidth(def) + 6, lineH = hanging ? 9 : 10, h = lineH * Signs.Lines;
            Side(ref front, ref frontTex, "Front", t?.Front, t?.FrontColor ?? 0, w, h, lineH, px, new Vector3(at.x, at.y, wall && !hanging ? at.z : -at.z), true);
            // (a wall sign's back is against the wall: Minecraft draws it all the same)
            Side(ref back, ref backTex, "Back", t?.Back, t?.BackColor ?? 0, w, h, lineH, px, new Vector3(at.x, at.y, wall && !hanging ? at.z + 2f * 0.0467f : at.z), false);
        }

        void Side(ref GameObject go, ref Texture2D tex, string name, string[] lines, byte color, int w, int h, int lineH, float px, Vector3 pos, bool faceFront)
        {
            bool any = lines != null && lines.Any(l => !string.IsNullOrEmpty(l));
            if (!any) { if (go != null) go.SetActive(false); return; }
            if (go == null)
            {
                go = new GameObject("LMC_SignText" + name) { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var m = new Mesh();
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
                m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                m.RecalculateNormals(); m.RecalculateBounds();
                mf.sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); Atlas.NoDecals(mr);
            }
            go.SetActive(true);
            if (tex == null || tex.width != w || tex.height != h) tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[w * h];
            var col = Signs.TextColor(color);
            for (int i = 0; i < Signs.Lines; i++)
            {
                string s = lines[i] ?? "";
                if (s.Length == 0) continue;
                int lw = Mathf.RoundToInt(McFont.Width(s));
                McFont.Blit(pixels, w, h, s, (w - lw) / 2, i * lineH + 1, col);
            }
            tex.SetPixels32(pixels); tex.Apply();
            var r = go.GetComponent<MeshRenderer>();
            if (r.sharedMaterial == null || r.sharedMaterial.mainTexture != tex)
            {
                if (baseMat == null) baseMat = Atlas.MakeLit("LMC_SignTextBase", cutout: true, doubleSided: false, emissive: false);
                var m = new Material(baseMat);
                m.SetTexture("_BaseColorMap", tex); m.mainTexture = tex;
                r.sharedMaterial = m;
            }
            go.transform.localPosition = pos;
            // (the quad faces -z: the front's text the way you read it from the front, the back's turned round)
            go.transform.localRotation = faceFront ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(w * px, h * px, 1f);
        }

        void OnDestroy()
        {
            if (frontTex != null) Destroy(frontTex);
            if (backTex != null) Destroy(backTex);
        }
    }

    /// <summary>The sign editor (Minecraft's SignEditScreen): four lines over a picture of the sign, typed into; Done (or
    /// Escape) sends them to the host.</summary>
    public class SignUI : MonoBehaviour
    {
        public static SignUI Instance;
        public static bool IsOpen => Instance != null && Instance.canvas != null && Instance.canvas.gameObject.activeSelf;
        Canvas canvas; RectTransform panel;
        readonly TMPro.TMP_InputField[] lines = new TMPro.TMP_InputField[Signs.Lines];
        BlockKey key; bool front;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Open(BlockKey k, bool frontSide)
        {
            var ui = Instance;
            if (ui == null || IsOpen || SlotScreen.AnyOpen || BookUI.IsOpen) return;
            ui.key = k; ui.front = frontSide;
            ui.Show();
        }

        void Build()
        {
            var go = new GameObject("LMC_SignUI");
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 120;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            panel = new GameObject("Sign").AddComponent<RectTransform>(); panel.SetParent(go.transform, false);
            panel.sizeDelta = new Vector2(620, 340);
            var bg = panel.gameObject.AddComponent<Image>(); bg.color = new Color(0, 0, 0, 0);
            var font = HUDManager.Instance != null ? HUDManager.Instance.chatText.font : null;
            var title = new GameObject("Title").AddComponent<TMPro.TextMeshProUGUI>(); title.rectTransform.SetParent(panel, false);
            title.rectTransform.anchoredPosition = new Vector2(0, 230); title.rectTransform.sizeDelta = new Vector2(600, 50);
            if (font != null) title.font = font;
            title.fontSize = 30; title.color = Color.white; title.alignment = TMPro.TextAlignmentOptions.Center; title.text = "Edit Sign Message";
            board = new GameObject("Board").AddComponent<RawImage>(); board.rectTransform.SetParent(panel, false);
            board.rectTransform.sizeDelta = new Vector2(600, 300);
            for (int i = 0; i < Signs.Lines; i++)
            {
                var src = HUDManager.Instance.chatTextField;
                var f = Instantiate(src.gameObject, panel).GetComponent<TMPro.TMP_InputField>();
                f.name = "Line" + i; f.onSubmit.RemoveAllListeners(); f.onEndEdit.RemoveAllListeners(); f.onValueChanged.RemoveAllListeners();
                var rt = (RectTransform)f.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0, 90 - i * 60); rt.sizeDelta = new Vector2(560, 56);
                f.lineType = TMPro.TMP_InputField.LineType.SingleLine; f.characterLimit = 50;
                if (f.textComponent != null) { f.textComponent.color = Color.black; f.textComponent.fontSize = 34; f.textComponent.alignment = TMPro.TextAlignmentOptions.Center; }
                foreach (var g in f.GetComponentsInChildren<Graphic>(true)) if (g is Image) g.color = new Color(0, 0, 0, 0.06f);
                int idx = i;
                f.onSubmit.AddListener(_ => { if (idx < Signs.Lines - 1) lines[idx + 1].ActivateInputField(); else Close(true); });
                lines[i] = f;
            }
            var done = new GameObject("Done").AddComponent<Image>(); done.rectTransform.SetParent(panel, false);
            done.rectTransform.anchoredPosition = new Vector2(0, -230); done.rectTransform.sizeDelta = new Vector2(400, 50); done.color = new Color(0.3f, 0.3f, 0.3f, 0.95f);
            var dt = new GameObject("DoneText").AddComponent<TMPro.TextMeshProUGUI>(); dt.rectTransform.SetParent(done.rectTransform, false); dt.rectTransform.sizeDelta = done.rectTransform.sizeDelta;
            if (font != null) dt.font = font;
            dt.text = "Done"; dt.fontSize = 26; dt.color = Color.white; dt.alignment = TMPro.TextAlignmentOptions.Center;
            done.gameObject.AddComponent<Button>().onClick.AddListener(() => Close(true));
            canvas.gameObject.SetActive(false);
        }

        RawImage board;

        void Show()
        {
            if (canvas == null) Build();
            var b = BlockWorld.Instance?.Get(key);
            var t = Signs.Of(key);
            // (the sign's own wood behind the text: its board's picture)
            var tex = b != null ? McAssets.LoadTexture("block/" + b.Data.Def.Key) : null;
            board.texture = tex; board.color = tex != null ? Color.white : new Color(0.6f, 0.45f, 0.25f);
            if (tex != null) board.uvRect = Signs.IsHanging(b.Data.Def) ? new Rect(1f / 16f, 3f / 16f, 7f / 16f, 5f / 16f) : new Rect(0f, 9f / 16f, 12f / 16f, 6f / 16f);
            var cur = t != null ? (front ? t.Front : t.Back) : null;
            for (int i = 0; i < Signs.Lines; i++) lines[i].text = cur != null ? cur[i] : "";
            canvas.gameObject.SetActive(true);
            var p = GameNetworkManager.Instance.localPlayerController;
            p.inSpecialMenu = true; p.disableMoveInput = true; p.isTypingChat = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            lines[0].ActivateInputField();
        }

        public void Close(bool save)
        {
            if (canvas == null || !canvas.gameObject.activeSelf) return;
            if (save) BlockNet.RequestSign(key, 0, (byte)(front ? 0 : 1), lines.Select(l => l.text).ToArray());
            canvas.gameObject.SetActive(false);
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p != null) { p.inSpecialMenu = false; p.disableMoveInput = false; p.isTypingChat = false; }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            SlotScreen.LastClosed = Time.time;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) Close(true);
            // (each line no wider than the sign: Minecraft stops the typing there)
            var b = BlockWorld.Instance?.Get(key);
            if (b == null) { Close(false); return; }
            int max = Signs.MaxWidth(b.Data.Def);
            foreach (var l in lines) { var fit = Signs.Fit(l.text, max); if (fit != l.text) l.text = fit; }
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p != null && Vector3.Distance(p.transform.position, BlockWorld.Instance.WorldCenter(key)) > 8f) Close(true);
        }

        public static void DevType(params string[] text) { if (!IsOpen) return; for (int i = 0; i < Signs.Lines; i++) Instance.lines[i].text = i < text.Length ? text[i] : ""; Instance.Close(true); }
    }

    public static partial class Blocks
    {
        public static BlockDef IronChain;
        /// <summary>(#223) Item pictures of blocks whose item isn't the block's own texture: tile -> Minecraft's path.</summary>
        public static readonly Dictionary<string, string> ItemTiles = new Dictionary<string, string>();

        static void AddSigns()
        {
            foreach (var w in Woods)
            {
                string k = w.Key, n = w.Name;
                var sign = AddW(k + "_sign", n + " Sign", k + "_sign", 1f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.Sign; x.Directional = true; x.Render = RenderKind.Cutout; x.Pushable = false;
                    x.Description = "Stands on the ground (turned to face you) or hangs on a wall; write on both faces ([E] on it). Honeycomb waxes it.";
                });
                var hanging = AddW(k + "_hanging_sign", n + " Hanging Sign", k + "_hanging_sign", 1f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.HangingSign; x.Directional = true; x.Render = RenderKind.Cutout; x.Pushable = false;
                    x.Description = "Hangs from the block over it on chains (crouch: straight chains, turned any way) or from a wall on a bracket; write on both faces ([E] on it).";
                });
                ItemTiles["item_" + sign.Key] = "item/" + sign.Key;
                ItemTiles["item_" + hanging.Key] = "item/" + hanging.Key;
            }
            IronChain = AddW("iron_chain", "Iron Chain", "iron_chain", 5f, ToolKind.Pickaxe, x =>
            {
                x.Shape = BlockShape.Chain; x.Directional = true; x.Render = RenderKind.Cutout; x.Sound = "metal"; x.Flammable = false;
                x.Description = "A length of iron chain, along the face it's put on. Two make hanging signs.";
            });
            familyHardness["iron_chain"] = (5f, ToolKind.Pickaxe, 1);
            ItemTiles["item_iron_chain"] = "item/iron_chain";
        }
    }
}
