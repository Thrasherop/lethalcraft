using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Banners of every colour and their patterns (#225, the owner's). A banner is its base colour and up to six pattern
    /// layers (Minecraft's loom limit), written "base;pattern:colour,pattern:colour" (colours as DyeColor's index). A plain
    /// banner is its colour's block (white_banner...); one with patterns is a patterned banner item carrying a design's
    /// number (the host's registry of designs, sent to everyone and saved with the game, as written books are), and a
    /// placed one remembers its design. Textures are put together as BannerRenderer does (Banners.TextureFor).
    /// </summary>
    public static class BannerDesigns
    {
        public const string ItemKey = "patterned_banner";
        public const int MaxLayers = 6;
        public static readonly Dictionary<int, string> Designs = new Dictionary<int, string>();
        public static readonly Dictionary<BlockKey, int> Placed = new Dictionary<BlockKey, int>();
        static int nextId = 1;
        const string SaveDesigns = "LMC_BannerDesigns_v1", SavePlaced = "LMC_BannerPlaced_v1";

        /// <summary>DyeColor.textureDiffuseColor, by index.</summary>
        public static readonly uint[] DiffuseColors = { 0xF9FFFE, 0xF9801D, 0xC74EBD, 0x3AB3DA, 0xFED83D, 0x80C71F, 0xF38BAA, 0x474F52, 0x9D9D97, 0x169C9C, 0x8932B8, 0x3C44AA, 0x835432, 0x5E7C16, 0xB02E26, 0x1D1D21 };
        public const string OminousSpec = "0;rhombus:9,stripe_bottom:8,stripe_center:7,border:8,stripe_middle:15,half_horizontal:8,circle:8,border:15";

        public static void Reset() { Designs.Clear(); Placed.Clear(); icons.Clear(); models.Clear(); nextId = 1; }
        /// <summary>A frame's placed designs forgotten here (the moon's when it goes: everyone forgets it on their own).</summary>
        public static void ForgetFrame(int frame) { foreach (var k in Placed.Keys.Where(k => k.Frame == frame).ToList()) Placed.Remove(k); }
        /// <summary>Host: a frame's placed designs forgotten by everyone (the ship's builds wiped).</summary>
        public static void ServerForgetFrame(int frame) { foreach (var k in Placed.Keys.Where(k => k.Frame == frame).ToList()) { Placed.Remove(k); BlockNet.ServerBannerPlaced(k, 0); } }

        public static string BlockKeyFor(int color) => Dyes.Colors[Mathf.Clamp(color, 0, 15)] + "_banner";
        public static int ColorOfBlock(BlockDef d) => d == null || !d.Key.EndsWith("_banner") ? -1 : Array.IndexOf(Dyes.Colors, d.Key.Substring(0, d.Key.Length - 7));

        public static (int baseColor, List<(string pattern, int color)> layers) Parse(string spec)
        {
            var layers = new List<(string, int)>();
            if (string.IsNullOrEmpty(spec)) return (0, layers);
            var parts = spec.Split(';');
            int.TryParse(parts[0], out int b);
            if (parts.Length > 1 && parts[1].Length > 0)
                foreach (var l in parts[1].Split(','))
                {
                    var pc = l.Split(':');
                    if (pc.Length == 2 && int.TryParse(pc[1], out int c)) layers.Add((pc[0], c));
                }
            return (b, layers);
        }

        public static string Spec(int baseColor, IEnumerable<(string pattern, int color)> layers) => baseColor + ";" + string.Join(",", layers.Select(l => l.pattern + ":" + l.color));

        /// <summary>A placed banner's design: its own, the ominous banner's, or its block's plain colour.</summary>
        public static string SpecAt(BlockInstance b)
        {
            if (b == null) return null;
            if (Placed.TryGetValue(b.Key, out int id) && Designs.TryGetValue(id, out var s)) return s;
            if (b.Data.Def == Blocks.OminousBanner) return OminousSpec;
            int c = ColorOfBlock(b.Data.Def);
            return c >= 0 ? c + ";" : OminousSpec;
        }

        /// <summary>A design's number in an item's key ("patterned_banner#12"), or 0.</summary>
        public static int IdOf(string key) => key != null && ItemData.Base(key) == ItemKey ? ItemData.Of(key) : 0;

        // ------------------------------------------------------------------ the host's registry
        public static int ServerRegister(string spec)
        {
            foreach (var kv in Designs) if (kv.Value == spec) return kv.Key;
            int id = nextId++;
            Designs[id] = spec;
            BlockNet.ServerBannerDesign(id, spec);
            return id;
        }

        public static void Receive(int id, string spec)
        {
            Designs[id] = spec;
            if (id >= nextId) nextId = id + 1;
            foreach (var kv in Placed.Where(kv => kv.Value == id).ToList()) ReceivePlaced(kv.Key, id);
            foreach (var bi in UnityEngine.Object.FindObjectsOfType<BannerItem>()) if (bi.DesignId == id) ApplyModel(bi); // (one that came before its design)
        }

        public static void ReceivePlaced(BlockKey k, int id)
        {
            if (id <= 0) Placed.Remove(k); else Placed[k] = id;
            var b = BlockWorld.Instance?.Get(k);
            if (b?.Go != null) BannerBlock.Attach(b);
        }

        public static void ServerSyncTo(ulong client)
        {
            foreach (var kv in Designs) BlockNet.ServerBannerDesign(kv.Key, kv.Value, client);
            foreach (var kv in Placed) BlockNet.ServerBannerPlaced(kv.Key, kv.Value, client);
        }

        /// <summary>Host: a patterned banner put down there (its item already out of the hand; given back if it can't go).</summary>
        public static void ServerPlace(ulong sender, string itemKey, BlockKey k, byte facing)
        {
            var w = BlockWorld.Instance;
            var p = ServerLogic.PlayerFor(sender);
            int id = IdOf(itemKey);
            if (w == null || p == null || id <= 0 || !Designs.TryGetValue(id, out var spec)) { if (p != null) Inventory.ServerSpawnFor(sender, itemKey, 1); return; }
            var def = Blocks.Get(BlockKeyFor(Parse(spec).baseColor));
            if (def == null || w.Has(k) || Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) { Inventory.ServerSpawnFor(sender, itemKey, 1); return; }
            byte state = 0;
            if (facing >= 16) { state = (byte)((facing - 16) & 15); facing = (byte)Face.Up; }
            else if (facing < 2 || facing > 5) { Inventory.ServerSpawnFor(sender, itemKey, 1); return; }
            Placed[k] = id;
            BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(def.Id, facing, state)));
            BlockNet.ServerBannerPlaced(k, id);
            BlockNet.ServerSound(w.WorldCenter(k), "dig.wood", 0.8f, 1f);
            if (k.Frame == 1 && StartOfRound.Instance.inShipPhase) ShipPersistence.Save();
        }

        /// <summary>Host: a banner broken: a patterned one drops its own design (true: handled).</summary>
        public static bool ServerBroken(BlockKey k, Vector3 at, bool drop)
        {
            if (!Placed.TryGetValue(k, out int id)) return false;
            Placed.Remove(k);
            BlockNet.ServerBannerPlaced(k, 0);
            if (drop) ModItems.ServerSpawnPlainKeyed(ItemData.With(ItemKey, id), at);
            return true;
        }

        // ------------------------------------------------------------------ saved with the game
        public static void Save(string file)
        {
            try
            {
                var ms = new MemoryStream(); var wr = new BinaryWriter(ms);
                wr.Write(Designs.Count);
                foreach (var kv in Designs) { wr.Write(kv.Key); wr.Write(kv.Value); }
                ES3.Save(SaveDesigns, Convert.ToBase64String(ms.ToArray()), file);
                ms = new MemoryStream(); wr = new BinaryWriter(ms);
                var ship = Placed.Where(kv => kv.Key.Frame == 1).ToList();
                wr.Write(ship.Count);
                foreach (var kv in ship) { wr.Write(kv.Key.YOff); wr.Write(kv.Key.Pos.x); wr.Write(kv.Key.Pos.y); wr.Write(kv.Key.Pos.z); wr.Write(kv.Value); }
                ES3.Save(SavePlaced, Convert.ToBase64String(ms.ToArray()), file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("banners save: " + e.Message); }
        }

        public static void Load(string file)
        {
            try
            {
                if (ES3.KeyExists(SaveDesigns, file))
                {
                    var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SaveDesigns, file))));
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) { int id = r.ReadInt32(); string s = r.ReadString(); Designs[id] = s; nextId = Math.Max(nextId, id + 1); BlockNet.ServerBannerDesign(id, s); }
                }
                foreach (var k in Placed.Keys.Where(k => k.Frame == 1).ToList()) Placed.Remove(k);
                if (ES3.KeyExists(SavePlaced, file))
                {
                    var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SavePlaced, file))));
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        short yoff = r.ReadInt16(); int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32(), id = r.ReadInt32();
                        var k = new BlockKey(1, yoff, new Vector3Int(x, y, z));
                        Placed[k] = id; BlockNet.ServerBannerPlaced(k, id);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("banners load: " + e.Message); }
        }

        // ------------------------------------------------------------------ the local player
        /// <summary>The use button with a patterned banner: put it down where a block would go.</summary>
        public static void TryPlaceHeld(PlayerControllerB p, BannerItem item)
        {
            if (!Designs.TryGetValue(item.DesignId, out var spec)) return;
            var def = Blocks.Get(BlockKeyFor(Parse(spec).baseColor));
            if (def == null || Builder.Instance == null || !Builder.Instance.ComputePlacement(p, def, out var key, out var facing)) return;
            string itemKey = ItemData.With(ItemKey, item.DesignId);
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return;
            BlockNet.RequestBannerPlace(itemKey, key, facing);
        }

        static readonly Dictionary<int, (Mesh mesh, Material mat)> models = new Dictionary<int, (Mesh, Material)>();
        /// <summary>A patterned banner held or on the floor: its own design, extruded as the plain ones are.</summary>
        public static void ApplyModel(BannerItem b)
        {
            var mf = b != null ? b.transform.Find("model")?.GetComponent<MeshFilter>() : null;
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mr == null || !Designs.TryGetValue(b.DesignId, out var spec)) return;
            if (!models.TryGetValue(b.DesignId, out var m) || m.mesh == null || m.mat == null)
            {
                var icon = IconFor(ItemData.With(ItemKey, b.DesignId));
                var px = icon != null ? Banners.Tile(16, spec) : null;
                if (px == null) return;
                var mat = new Material(Atlas.Cutout) { name = "LMC_banner_" + b.DesignId, mainTexture = icon.texture };
                if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", icon.texture);
                models[b.DesignId] = m = (MeshBuilder.ExtrudedPixels(px, new Rect(0, 0, 1, 1), "LMC_banner_" + b.DesignId), mat);
            }
            mf.sharedMesh = m.mesh;
            mr.sharedMaterial = m.mat;
        }

        static readonly Dictionary<int, Sprite> icons = new Dictionary<int, Sprite>();
        /// <summary>A patterned banner's icon (its design's cloth), or null: not one.</summary>
        public static Sprite IconFor(string key)
        {
            int id = IdOf(key);
            if (id <= 0 || !Designs.TryGetValue(id, out var spec)) return null;
            if (icons.TryGetValue(id, out var s) && s != null) return s;
            var px = Banners.Tile(16, spec);
            if (px == null) return null;
            var t = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixels32(px); t.Apply();
            return icons[id] = Sprite.Create(t, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        }
    }

    /// <summary>A banner with patterns, as an item (#225): its design's number, saved with it.</summary>
    public class BannerItem : GrabbableObject
    {
        public int DesignId;
        public override int GetItemDataToSave() => DesignId;
        public override void LoadItemSaveData(int saveData) { DesignId = saveData; BannerDesigns.ApplyModel(this); }
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        void Awake() => SpawnFix.Clear(gameObject);
        public Sprite Icon => BannerDesigns.IconFor(ItemData.With(BannerDesigns.ItemKey, DesignId)) ?? itemProperties.itemIcon;
    }
}
