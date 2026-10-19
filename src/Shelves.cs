using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Wooden shelves (#223, the owner's: every wood), Minecraft's ShelfBlock: three slots across its front, each showing
    /// what's on it. [E] on a slot swaps what you hold with what's there (Minecraft's swapSingleItem: the whole stack).
    /// Powered by redstone, [E] swaps the shelf's three (and up to two more powered shelves beside it, facing the same
    /// way, from the left) with the end of your hotbar, Minecraft's swapHotbar (the last 3, 6 or 9 slots). Its contents are
    /// a three-slot container (Chests): hoppers and comparators work with it as with a chest. Model: Minecraft's own
    /// (template_shelf_*.json, read from the player's Minecraft). Six stripped logs make six.
    /// </summary>
    public static class WoodShelves
    {
        public const byte Powered = 1;
        public static int Swaps; // (dev/tests)
        public static bool Is(BlockDef d) => d != null && d.Shape == BlockShape.WoodShelf;

        /// <summary>Host: [E] on slot (0-2, from the left as you face it) of the shelf at k.</summary>
        public static void ServerUse(ulong sender, BlockKey k, byte slot)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            var p = ServerLogic.PlayerFor(sender);
            if (b == null || p == null || !Is(b.Data.Def) || slot > 2) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            if ((b.Data.State & Powered) != 0) { ServerSwapHotbar(sender, p, k); return; }
            var c = Chests.Ensure(k);
            string oldKey = c.Count[slot] > 0 ? c.Key[slot] : null; int oldN = c.Count[slot];
            var st = p.currentlyHeldObjectServer as StackItem;
            string key = st != null && st.Count > 0 ? Crafting.KeyOf(st) : null; int n = key != null ? st.Count : 0;
            if (key == null && oldKey == null) return;
            c.Key[slot] = key; c.Count[slot] = n;
            if (key != null) st.ServerSetCount(0, despawnIfEmpty: true);
            if (oldKey != null) Inventory.ServerSpawnFor(sender, oldKey, oldN, true);
            BlockNet.ServerChest(k, c);
            BlockNet.ServerSound(w.WorldCenter(k), key == null ? "shelf.take" : oldKey == null ? "shelf.place" : "shelf.swap", 1f, 1f);
            Redstone.MarkDirty();
            Swaps++;
        }

        /// <summary>Host: a powered shelf's row (itself and the powered shelves beside it facing its way, up to three) swaps
        /// with the last slots of the hotbar.</summary>
        static void ServerSwapHotbar(ulong sender, PlayerControllerB p, BlockKey k)
        {
            var w = BlockWorld.Instance;
            var row = Row(w, k);
            int hot = p.ItemSlots.Length;
            for (int part = 0; part < row.Count; part++)
            {
                var c = Chests.Ensure(row[part]);
                for (int s = 0; s < 3; s++)
                {
                    int idx = hot - (row.Count - part) * 3 + s;
                    if (idx < 0 || idx >= hot) continue;
                    string oldKey = c.Count[s] > 0 ? c.Key[s] : null; int oldN = c.Count[s];
                    var st = p.ItemSlots[idx] as StackItem;
                    string key = st != null && st.Count > 0 ? Crafting.KeyOf(st) : null; int n = key != null ? st.Count : 0;
                    if (key == null && oldKey == null) continue;
                    if (p.ItemSlots[idx] != null && st == null) continue; // (a tool or such stays put: only stacks move)
                    c.Key[s] = key; c.Count[s] = n;
                    if (key != null) st.ServerSetCount(0, despawnIfEmpty: true);
                    if (oldKey != null) Inventory.ServerSpawnFor(sender, oldKey, oldN, true, idx); // (into that very slot, Minecraft's swapHotbar)
                }
                BlockNet.ServerChest(row[part], c);
            }
            BlockNet.ServerSound(w.WorldCenter(k), "shelf.multi_swap", 1f, 1f);
            Redstone.MarkDirty();
            Swaps++;
        }

        /// <summary>The powered shelves in a line with k, facing its way, from its left (as you face it), three at most.</summary>
        static List<BlockKey> Row(BlockWorld w, BlockKey k)
        {
            var b = w.Get(k);
            var front = Faces.Dir[b.Data.Facing];
            var left = new Vector3Int(front.z, 0, -front.x); // (your left facing the shelf is its right; its own left: this)
            bool Same(BlockKey q) { var x = w.Get(q); return x != null && x.Data.Def != null && Is(x.Data.Def) && x.Data.Facing == b.Data.Facing && (x.Data.State & Powered) != 0; }
            var start = k;
            for (int i = 0; i < 2 && Same(new BlockKey(k.Frame, k.YOff, start.Pos + left)); i++) start = new BlockKey(k.Frame, k.YOff, start.Pos + left);
            var row = new List<BlockKey>();
            for (var q = start; row.Count < 3 && Same(q); q = new BlockKey(k.Frame, k.YOff, q.Pos - left)) row.Add(q);
            return row;
        }

        public static string HoverTip(BlockInstance b) => (b.Data.State & Powered) != 0 ? "Shelf (powered) - swap with your hotbar : [E]" : "Shelf - swap : [E]";
    }

    /// <summary>A shelf's three items, shown on it (every client): Minecraft's ShelfRenderer, each a quarter size, 0.3125 of a
    /// block apart, a quarter block back from its middle.</summary>
    public class ShelfDisplay : MonoBehaviour
    {
        public BlockKey Key;
        readonly GameObject[] shown = new GameObject[3];
        readonly string[] shownKey = new string[3];

        public static void Attach(BlockInstance b)
        {
            if (b?.Go == null) return;
            var d = b.Go.GetComponent<ShelfDisplay>() ?? b.Go.AddComponent<ShelfDisplay>();
            d.Key = b.Key;
            d.Refresh();
        }

        public static void Changed(BlockKey k)
        {
            var b = BlockWorld.Instance?.Get(k);
            if (b?.Go != null && b.Go.GetComponent<ShelfDisplay>() is ShelfDisplay d) d.Refresh();
        }

        public void Refresh()
        {
            var c = Chests.Of(Key);
            for (int s = 0; s < 3; s++)
            {
                string key = c != null && c.Count[s] > 0 ? ItemData.TypeOf(c.Key[s]) : null;
                if (key == shownKey[s]) continue;
                shownKey[s] = key;
                if (shown[s] != null) Destroy(shown[s]);
                if (key == null) continue;
                var go = new GameObject("LMC_ShelfItem" + s) { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                // (its slot, from the left as you face it: our front is +z, so its left is our -x... the shelf turned to face
                // you, your left is its +x side seen from the front; a quarter block in from the middle, toward its back, as
                // Minecraft's ShelfRenderer has it: #237, it was a quarter block out in front, the items sticking out of it)
                go.transform.localPosition = new Vector3((1 - s) * 0.3125f, 0f, -0.25f);
                go.transform.localScale = Vector3.one * 0.25f;
                var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>(); Atlas.NoDecals(mr);
                var block = Blocks.Get(key);
                var flat = block != null ? Atlas.FlatItemTile(block) : null;
                // (as the item looks in hand: a wall or fence its post with a straight run, a pane flat; variant 0 of those
                // is no mesh at all: a wall put on a shelf showed nothing)
                int v = block != null && (block.Shape == BlockShape.Wall || block.Shape == BlockShape.Fence) ? 3 | 16 : block != null && block.Shape == BlockShape.Pane ? 12 : 0;
                if (block != null && flat == null) { mf.sharedMesh = MeshBuilder.For(block, 0, v); mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive }; }
                else
                {
                    string tile = flat ?? (Atlas.Tiles.ContainsKey("item_" + key) ? "item_" + key : Atlas.Tiles.ContainsKey(key) ? key : null);
                    if (tile == null) { Destroy(go); continue; }
                    mf.sharedMesh = MeshBuilder.ExtrudedSprite(tile); mr.sharedMaterial = Atlas.Cutout;
                }
                shown[s] = go;
            }
        }
    }

    public static partial class Blocks
    {
        static void AddShelves()
        {
            foreach (var w in Woods) AddShelfFor(w);
        }

        static void AddShelfFor(WoodKind w)
        {
            {
                AddW(w.Key + "_shelf", w.Name + " Shelf", w.Key + "_shelf", 2f, ToolKind.Axe, x =>
                {
                    x.Shape = BlockShape.WoodShelf; x.Directional = true; x.FacingIncludesVertical = false; x.Render = RenderKind.Cutout; x.Pushable = false; x.IsRedstoneComponent = true;
                    x.Description = "Three items on show. [E] on one swaps it with what you hold; powered, [E] swaps the shelf's three with the end of your hotbar.";
                });
            }
        }
    }
}
