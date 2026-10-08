using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Chest contents: 27 slots of Minecraft items (blocks, tools, materials, food, pearls) per chest block. The server
    /// holds the truth and every change is mirrored to all clients; items moved in or out pass through the player's own
    /// inventory, so nothing is ever created or lost. Chests in the ship keep their contents with the save file; a broken
    /// chest drops everything, like Minecraft.
    /// </summary>
    public static class Chests
    {
        public const int Size = 27;

        public class Contents
        {
            public string[] Key = new string[Size];
            public int[] Count = new int[Size];
            public bool Empty => Count.All(c => c <= 0);
        }

        /// <summary>Server truth; mirrored on clients (read-only there).</summary>
        public static readonly Dictionary<BlockKey, Contents> All = new Dictionary<BlockKey, Contents>();

        public static Contents Of(BlockKey k) => All.TryGetValue(k, out var c) ? c : null;

        public static void ResetFrame(int frame)
        {
            foreach (var k in All.Keys.Where(k => k.Frame == frame).ToList()) All.Remove(k);
        }

        /// <summary>The kinds of items a chest takes: anything this mod makes (they have a key).</summary>
        public static bool Accepts(string key) => key != null && ModItems.ByKey.ContainsKey(key);

        // ------------------------------------------------------------------ server
        static Contents Ensure(BlockKey k)
        {
            if (!All.TryGetValue(k, out var c)) All[k] = c = new Contents();
            return c;
        }

        static bool IsChest(BlockKey k) => BlockWorld.Instance != null && BlockWorld.Instance.DefAt(k) == Blocks.Chest;

        /// <summary>Server: take up to n items from a slot for a player. Returns what was taken (sent back to them).</summary>
        public static (string key, int n) ServerTake(BlockKey k, int slot, int n)
        {
            if (!IsChest(k)) return (null, 0);
            var c = Ensure(k);
            var got = TakeFrom(c, slot, n);
            if (got.n > 0) BlockNet.ServerChest(k, c);
            return got;
        }

        /// <summary>
        /// Server: a player puts n items into a slot (slot -1: wherever they fit, matching stacks first). If the slot holds
        /// something else and <paramref name="swap"/>, the two stacks trade places. Returns what goes back to the player.
        /// </summary>
        public static (string key, int n) ServerPut(BlockKey k, int slot, string key, int n, bool swap)
        {
            if (n <= 0 || key == null) return (null, 0);
            if (!IsChest(k)) return (key, n);
            var c = Ensure(k);
            var back = PutInto(c, slot, key, n, swap);
            BlockNet.ServerChest(k, c);
            return back;
        }

        // ------------------------------------------------------------------ the slot rules (chests and the player's storage)
        /// <summary>Takes up to n items out of a slot; returns what came out.</summary>
        public static (string key, int n) TakeFrom(Contents c, int slot, int n)
        {
            if (slot < 0 || slot >= Size || n <= 0 || c.Count[slot] <= 0) return (null, 0);
            int t = Mathf.Min(n, c.Count[slot]);
            string key = c.Key[slot];
            c.Count[slot] -= t;
            if (c.Count[slot] <= 0) { c.Count[slot] = 0; c.Key[slot] = null; }
            return (key, t);
        }

        /// <summary>Puts n items into a slot (-1: wherever they fit, matching stacks first; a different stack there swaps if
        /// asked to); returns what's left over.</summary>
        public static (string key, int n) PutInto(Contents c, int slot, string key, int n, bool swap)
        {
            if (n <= 0 || key == null) return (null, 0);
            if (!Accepts(key) || slot >= Size) return (key, n);
            int max = Inventory.MaxStackOf(key);
            if (slot < 0)
            {
                for (int pass = 0; pass < 2 && n > 0; pass++)
                    for (int i = 0; i < Size && n > 0; i++)
                    {
                        if (pass == 0 ? c.Key[i] != key || c.Count[i] <= 0 : c.Count[i] > 0) continue;
                        int t = Mathf.Min(n, max - c.Count[i]);
                        if (t <= 0) continue;
                        c.Key[i] = key; c.Count[i] += t; n -= t;
                    }
                return (n > 0 ? key : null, n);
            }
            (string key, int n) back = (null, 0);
            if (c.Count[slot] <= 0 || c.Key[slot] == key)
            {
                int t = Mathf.Min(n, max - c.Count[slot]);
                c.Key[slot] = key; c.Count[slot] += t;
                if (n - t > 0) back = (key, n - t);
            }
            else if (swap && n <= max)
            {
                back = (c.Key[slot], c.Count[slot]);
                c.Key[slot] = key; c.Count[slot] = n;
            }
            else back = (key, n);
            return back;
        }

        /// <summary>Server: spawns a contents' items at a point (a broken chest, a player who died with their storage).</summary>
        public static void SpawnContents(Contents c, Vector3 pos)
        {
            for (int i = 0; i < Size; i++)
            {
                if (c.Count[i] <= 0 || c.Key[i] == null || !ModItems.ByKey.TryGetValue(c.Key[i], out var item)) continue;
                var at = pos + new Vector3(Random.Range(-0.3f, 0.3f), 0.2f, Random.Range(-0.3f, 0.3f));
                if (item.spawnPrefab.GetComponent<StackItem>() != null) ModItems.ServerSpawnStack(item, c.Count[i], at);
                else for (int j = 0; j < c.Count[i]; j++) ModItems.ServerSpawnPlain(item, at);
            }
        }

        /// <summary>Server: the chest block moved to another key (attached to the ship): its contents go along.</summary>
        public static void ServerMove(BlockKey from, BlockKey to)
        {
            if (!All.TryGetValue(from, out var c)) return;
            All.Remove(from); All[to] = c;
            BlockNet.ServerChest(from, null);
            BlockNet.ServerChest(to, c);
        }

        /// <summary>Server: the chest was broken: everything inside drops where it stood.</summary>
        public static void ServerDropContents(BlockKey k, Vector3 pos)
        {
            if (!All.TryGetValue(k, out var c)) return;
            SpawnContents(c, pos);
            All.Remove(k);
            BlockNet.ServerChest(k, null);
        }

        // ------------------------------------------------------------------ client mirror
        public static void ApplyState(BlockKey k, Contents c)
        {
            if (c == null) All.Remove(k); else All[k] = c;
            ChestUI.OnContentsChanged(k);
        }

        public static string Describe(BlockKey k)
        {
            var c = Of(k);
            int stacks = c == null ? 0 : c.Count.Count(n => n > 0);
            return stacks == 0 ? "Chest (empty) - open : [E]" : $"Chest ({stacks} stacks) - open : [E]";
        }

        // ------------------------------------------------------------------ saving (chests in the ship)
        public static void Write(BinaryWriter w, IEnumerable<KeyValuePair<BlockKey, Contents>> chests)
        {
            var list = chests.Where(kv => kv.Value != null && !kv.Value.Empty).ToList();
            w.Write(list.Count);
            foreach (var kv in list)
            {
                w.Write(kv.Key.YOff); w.Write(kv.Key.Pos.x); w.Write(kv.Key.Pos.y); w.Write(kv.Key.Pos.z);
                for (int i = 0; i < Size; i++) { w.Write(kv.Value.Key[i] ?? ""); w.Write(kv.Value.Count[i]); }
            }
        }

        public static List<KeyValuePair<BlockKey, Contents>> Read(BinaryReader r, byte frame)
        {
            var list = new List<KeyValuePair<BlockKey, Contents>>();
            int n = r.ReadInt32();
            for (int j = 0; j < n; j++)
            {
                short yoff = r.ReadInt16();
                var pos = new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                var c = new Contents();
                for (int i = 0; i < Size; i++)
                {
                    string key = r.ReadString(); int count = r.ReadInt32();
                    if (count > 0 && key.Length > 0 && Accepts(key)) { c.Key[i] = key; c.Count[i] = count; }
                }
                list.Add(new KeyValuePair<BlockKey, Contents>(new BlockKey(frame, yoff, pos), c));
            }
            return list;
        }
    }
}
