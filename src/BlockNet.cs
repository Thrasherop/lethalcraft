using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// All block traffic goes through one named message. Clients send requests, the server validates,
    /// mutates its world and broadcasts op batches that every peer (including the host) applies identically.
    /// </summary>
    public static class BlockNet
    {
        const string Channel = "LethalMinecraft.Net";

        enum Msg : byte
        {
            // client -> server
            // (ids from 100 also go to the server: 1-19 are all taken)
            PlaceReq = 1, BreakReq = 2, UseReq = 3, IgniteReq = 4, SyncReq = 5, MineProgressReq = 6, SwingHitReq = 7, EatReq = 8, GroundDigReq = 9, FurnaceInsertReq = 10, FurnaceTakeReq = 11, CraftReq = 12, ConsumeReq = 13, InsideReq = 14, AddToStackReq = 15, SpawnForMeReq = 16, PearlThrowReq = 17, ChestTakeReq = 18, ChestPutReq = 19,
            // server -> client
            Batch = 20, StackCount = 21, Explosion = 22, MineProgress = 23, FullSync = 24, Sound = 25, Toast = 26, Xp = 27, ScrapValue = 28, Cut = 29, Molds = 30, FurnaceState = 31, InsideState = 32, AutoGrab = 33, PearlFlight = 34, ChestState = 35, ChestGive = 36, GameModes = 37, ArmorState = 38, TreeFell = 39, StorageState = 40, StorageGive = 41,
            // client -> server, continued
            ArmorReq = 100, TreeChopReq = 101, MergeGroundReq = 102, SpawnVanillaReq = 103, StorageTakeReq = 104, StoragePutReq = 105, StorageDropReq = 106,
        }

        static bool ToServer(byte m) => m < 20 || (m >= 100 && m < 128);

        static bool registered;
        public static bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public static bool IsConnected => NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient);

        public static void Register()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null) return;
            if (registered) Unregister();
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnMessage);
            registered = true;
            Plugin.Log.LogInfo("BlockNet registered (server=" + nm.IsServer + ")");
        }

        public static void Unregister()
        {
            try { NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(Channel); } catch { }
            registered = false;
        }

        // ------------------------------------------------------------------ write helpers
        static void W(ref FastBufferWriter w, BlockKey k)
        {
            w.WriteValueSafe(k.Frame);
            w.WriteValueSafe(k.YOff);
            w.WriteValueSafe(k.Pos.x);
            w.WriteValueSafe(k.Pos.y);
            w.WriteValueSafe(k.Pos.z);
        }

        static BlockKey RK(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte f);
            r.ReadValueSafe(out short y);
            r.ReadValueSafe(out int px);
            r.ReadValueSafe(out int py);
            r.ReadValueSafe(out int pz);
            return new BlockKey(f, y, new Vector3Int(px, py, pz));
        }

        static void W(ref FastBufferWriter w, BlockData d)
        {
            w.WriteValueSafe(d.Type);
            w.WriteValueSafe(d.Facing);
            w.WriteValueSafe(d.State);
        }

        static BlockData RD(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte t);
            r.ReadValueSafe(out byte f);
            r.ReadValueSafe(out byte s);
            return new BlockData(t, f, s);
        }

        static void WriteOps(ref FastBufferWriter w, List<Op> ops)
        {
            w.WriteValueSafe(ops.Count);
            foreach (var op in ops)
            {
                w.WriteValueSafe((byte)op.Type);
                W(ref w, op.Key);
                switch (op.Type)
                {
                    case OpType.Set:
                        W(ref w, op.Data);
                        w.WriteValueSafe(op.Fx);
                        if (op.Fx == 1) W(ref w, op.Key2);
                        break;
                    case OpType.Remove: w.WriteValueSafe(op.Fx); break;
                    case OpType.Move: W(ref w, op.Key2); w.WriteValueSafe(op.Fx); break;
                    case OpType.State: W(ref w, op.Data); break;
                }
            }
        }

        static List<Op> ReadOps(ref FastBufferReader r)
        {
            r.ReadValueSafe(out int n);
            var ops = new List<Op>(n);
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out byte t);
                var op = new Op { Type = (OpType)t, Key = RK(ref r) };
                switch (op.Type)
                {
                    case OpType.Set:
                        op.Data = RD(ref r);
                        r.ReadValueSafe(out op.Fx);
                        if (op.Fx == 1) op.Key2 = RK(ref r);
                        break;
                    case OpType.Remove: r.ReadValueSafe(out op.Fx); break;
                    case OpType.Move: op.Key2 = RK(ref r); r.ReadValueSafe(out op.Fx); break;
                    case OpType.State: op.Data = RD(ref r); break;
                }
                ops.Add(op);
            }
            return ops;
        }

        static FastBufferWriter NewWriter(Msg m, int size = 256)
        {
            var w = new FastBufferWriter(size, Allocator.Temp, 1024 * 1024 * 4);
            w.WriteValueSafe((byte)m);
            return w;
        }

        // ------------------------------------------------------------------ sending
        static void SendToServer(FastBufferWriter w)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) { w.Dispose(); return; }
            using (w)
            {
                if (nm.IsServer)
                {
                    var r = new FastBufferReader(w, Allocator.Temp);
                    using (r) HandleOnServer(nm.LocalClientId, ref r);
                }
                else nm.CustomMessagingManager.SendNamedMessage(Channel, NetworkManager.ServerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        /// <summary>Server: send to all remote clients and process locally.</summary>
        static void Broadcast(FastBufferWriter w, ulong? onlyClient = null, bool includeLocal = true)
        {
            var nm = NetworkManager.Singleton;
            using (w)
            {
                if (onlyClient.HasValue)
                {
                    if (onlyClient.Value == nm.LocalClientId)
                    {
                        var r = new FastBufferReader(w, Allocator.Temp);
                        using (r) HandleOnClient(ref r);
                    }
                    else nm.CustomMessagingManager.SendNamedMessage(Channel, onlyClient.Value, w, NetworkDelivery.ReliableFragmentedSequenced);
                    return;
                }
                var targets = nm.ConnectedClientsIds.Where(id => id != nm.LocalClientId).ToList();
                if (targets.Count > 0)
                    nm.CustomMessagingManager.SendNamedMessage(Channel, targets, w, NetworkDelivery.ReliableFragmentedSequenced);
                if (includeLocal)
                {
                    var r = new FastBufferReader(w, Allocator.Temp);
                    using (r) HandleOnClient(ref r);
                }
            }
        }

        // ------------------------------------------------------------------ public client API
        public static void RequestPlace(StackItem stack, BlockKey key, byte type, byte facing)
        {
            var w = NewWriter(Msg.PlaceReq);
            w.WriteValueSafe(stack.NetworkObjectId);
            W(ref w, key);
            w.WriteValueSafe(type);
            w.WriteValueSafe(facing);
            SendToServer(w);
        }

        public static void RequestBreak(BlockKey key, bool withPickaxe)
        {
            var w = NewWriter(Msg.BreakReq);
            W(ref w, key);
            w.WriteValueSafe(withPickaxe);
            SendToServer(w);
        }

        public static void RequestUse(BlockKey key)
        {
            var w = NewWriter(Msg.UseReq);
            W(ref w, key);
            SendToServer(w);
        }

        public static void RequestIgnite(BlockKey key)
        {
            var w = NewWriter(Msg.IgniteReq);
            W(ref w, key);
            SendToServer(w);
        }

        public static void RequestSync()
        {
            var w = NewWriter(Msg.SyncReq);
            SendToServer(w);
        }

        public static void SendMineProgress(BlockKey key, sbyte stage)
        {
            if (!IsConnected) return;
            var w = NewWriter(Msg.MineProgressReq);
            W(ref w, key);
            w.WriteValueSafe(stage);
            SendToServer(w);
        }

        /// <summary>Local player crossed into / out of the facility through a dug tunnel.</summary>
        public static void RequestInside(bool inside)
        {
            var w = NewWriter(Msg.InsideReq);
            w.WriteValueSafe(inside);
            SendToServer(w);
        }

        public static void RequestGroundDig(Vector3 point, Vector3 normal)
        {
            var w = NewWriter(Msg.GroundDigReq);
            w.WriteValueSafe(point);
            w.WriteValueSafe(normal);
            SendToServer(w);
        }

        static void WriteCut(ref FastBufferWriter w, TerrainCarver.Cut c)
        {
            w.WriteValueSafe(c.Path);
            w.WriteValueSafe(c.Mins.Length);
            for (int i = 0; i < c.Mins.Length; i++) { w.WriteValueSafe(c.Mins[i]); w.WriteValueSafe(c.Maxs[i]); }
        }

        static TerrainCarver.Cut ReadCut(ref FastBufferReader r)
        {
            var c = new TerrainCarver.Cut();
            r.ReadValueSafe(out c.Path);
            r.ReadValueSafe(out int nb);
            c.Mins = new Vector3[nb]; c.Maxs = new Vector3[nb];
            for (int i = 0; i < nb; i++) { r.ReadValueSafe(out c.Mins[i]); r.ReadValueSafe(out c.Maxs[i]); }
            return c;
        }

        public static void ServerMolds(Dictionary<BlockKey, byte[]> molds)
        {
            var w = NewWriter(Msg.Molds, 64 + molds.Count * 64);
            w.WriteValueSafe(molds.Count);
            foreach (var kv in molds)
            {
                W(ref w, kv.Key);
                w.WriteValueSafe(kv.Value.Length);
                foreach (var b in kv.Value) w.WriteValueSafe(b);
            }
            Broadcast(w);
        }

        public static void ServerCut(TerrainCarver.Cut cut)
        {
            var w = NewWriter(Msg.Cut, 512 + cut.Mins.Length * 24);
            WriteCut(ref w, cut);
            Broadcast(w);
        }

        public static void RequestConsume(StackItem stack, int n)
        {
            var w = NewWriter(Msg.ConsumeReq);
            w.WriteValueSafe(stack.NetworkObjectId);
            w.WriteValueSafe(n);
            SendToServer(w);
        }

        public static void RequestCraft(int recipe, int times = 1)
        {
            var w = NewWriter(Msg.CraftReq);
            w.WriteValueSafe(recipe);
            w.WriteValueSafe(times);
            SendToServer(w);
        }

        /// <summary>stackId = 0 when the item was a single (already destroyed by the owner) item.</summary>
        public static void RequestFurnaceInsert(BlockKey furnace, string itemKey, ulong stackId, int n)
        {
            var w = NewWriter(Msg.FurnaceInsertReq);
            W(ref w, furnace);
            w.WriteValueSafe(itemKey);
            w.WriteValueSafe(stackId);
            w.WriteValueSafe(n);
            SendToServer(w);
        }

        public static void RequestFurnaceTake(BlockKey furnace)
        {
            var w = NewWriter(Msg.FurnaceTakeReq);
            W(ref w, furnace);
            SendToServer(w);
        }

        public static void ServerFurnace(BlockKey k, Crafting.Furnace f)
        {
            var w = NewWriter(Msg.FurnaceState);
            W(ref w, k);
            w.WriteValueSafe(f != null);
            if (f != null)
            {
                w.WriteValueSafe(f.In ?? ""); w.WriteValueSafe(f.InCount);
                w.WriteValueSafe(f.FuelKey ?? ""); w.WriteValueSafe(f.Fuel);
                w.WriteValueSafe(f.Out ?? ""); w.WriteValueSafe(f.OutCount);
                w.WriteValueSafe(f.Burn); w.WriteValueSafe(f.Progress);
            }
            Broadcast(w);
        }

        // ------------------------------------------------------------------ chests
        public static void RequestChestTake(BlockKey k, int slot, int n, bool toInventory)
        {
            var w = NewWriter(Msg.ChestTakeReq);
            W(ref w, k); w.WriteValueSafe(slot); w.WriteValueSafe(n); w.WriteValueSafe(toInventory);
            SendToServer(w);
        }

        public static void RequestChestPut(BlockKey k, int slot, string key, int n, bool swap)
        {
            var w = NewWriter(Msg.ChestPutReq);
            W(ref w, k); w.WriteValueSafe(slot); w.WriteValueSafe(key); w.WriteValueSafe(n); w.WriteValueSafe(swap);
            SendToServer(w);
        }

        // ------------------------------------------------------------------ the big inventory (Storage)
        public static void RequestStorageTake(int slot, int n, bool toInventory)
        {
            var w = NewWriter(Msg.StorageTakeReq);
            w.WriteValueSafe(slot); w.WriteValueSafe(n); w.WriteValueSafe(toInventory);
            SendToServer(w);
        }

        public static void RequestStoragePut(int slot, string key, int n, bool swap)
        {
            var w = NewWriter(Msg.StoragePutReq);
            w.WriteValueSafe(slot); w.WriteValueSafe(key); w.WriteValueSafe(n); w.WriteValueSafe(swap);
            SendToServer(w);
        }

        public static void RequestStorageDrop(Vector3 at)
        {
            var w = NewWriter(Msg.StorageDropReq);
            w.WriteValueSafe(at);
            SendToServer(w);
        }

        /// <summary>Server: a player's own storage (and whether the big inventory is on), to that player.</summary>
        public static void ServerStorage(ulong client, bool enabled, Chests.Contents c)
        {
            var w = NewWriter(Msg.StorageState, 64 + Storage.Size * 24);
            w.WriteValueSafe(enabled);
            for (int i = 0; i < Storage.Size; i++) { w.WriteValueSafe(c.Count[i] > 0 ? c.Key[i] ?? "" : ""); w.WriteValueSafe(c.Count[i]); }
            Broadcast(w, client);
        }

        static void ServerStorageGive(ulong client, string key, int n, bool toInventory)
        {
            if (key == null || n <= 0) return;
            var w = NewWriter(Msg.StorageGive);
            w.WriteValueSafe(key); w.WriteValueSafe(n); w.WriteValueSafe(toInventory);
            Broadcast(w, client);
        }

        static void WriteChest(ref FastBufferWriter w, BlockKey k, Chests.Contents c)
        {
            W(ref w, k);
            w.WriteValueSafe(c != null);
            if (c == null) return;
            for (int i = 0; i < Chests.Size; i++) { w.WriteValueSafe(c.Key[i] ?? ""); w.WriteValueSafe(c.Count[i]); }
        }

        static (BlockKey k, Chests.Contents c) ReadChest(ref FastBufferReader r)
        {
            var k = RK(ref r);
            r.ReadValueSafe(out bool has);
            if (!has) return (k, null);
            var c = new Chests.Contents();
            for (int i = 0; i < Chests.Size; i++)
            {
                r.ReadValueSafe(out string key); r.ReadValueSafe(out int n);
                if (n > 0 && key.Length > 0) { c.Key[i] = key; c.Count[i] = n; }
            }
            return (k, c);
        }

        public static void ServerChest(BlockKey k, Chests.Contents c)
        {
            var w = NewWriter(Msg.ChestState, 64 + Chests.Size * 24);
            WriteChest(ref w, k, c);
            Broadcast(w);
        }

        static void ServerChestGive(ulong client, string key, int n, bool toInventory)
        {
            if (key == null || n <= 0) return;
            var w = NewWriter(Msg.ChestGive);
            w.WriteValueSafe(key); w.WriteValueSafe(n); w.WriteValueSafe(toInventory);
            Broadcast(w, client);
        }

        /// <summary>Server: everyone's game mode (the list of creative players) to every client, the host included.</summary>
        public static void ServerGameModes(IEnumerable<ulong> creative)
        {
            var ids = creative.ToList();
            var w = NewWriter(Msg.GameModes, 16 + ids.Count * 8);
            w.WriteValueSafe(ids.Count);
            foreach (var id in ids) w.WriteValueSafe(id);
            Broadcast(w);
        }

        /// <summary>Owner client: what it wears now (dropOld: the server drops what it wore at `at`, e.g. where it died).</summary>
        public static void RequestArmor(string[] keys, bool dropOld, Vector3 at)
        {
            var w = NewWriter(Msg.ArmorReq);
            w.WriteValueSafe(dropOld);
            w.WriteValueSafe(at);
            for (int i = 0; i < Armor.Slots; i++) w.WriteValueSafe(keys[i] ?? "");
            SendToServer(w);
        }

        /// <summary>Server: a player's armor, to everyone (or one client catching up).</summary>
        public static void ServerArmor(ulong player, string[] keys, ulong? onlyClient = null)
        {
            var w = NewWriter(Msg.ArmorState);
            w.WriteValueSafe(player);
            for (int i = 0; i < Armor.Slots; i++) w.WriteValueSafe(keys[i] ?? "");
            Broadcast(w, onlyClient);
        }

        /// <summary>Owner client: top up my hotbar stack from this stack on the ground.</summary>
        public static void RequestMergeGround(StackItem ground, StackItem target)
        {
            var w = NewWriter(Msg.MergeGroundReq);
            w.WriteValueSafe(ground.NetworkObjectId);
            w.WriteValueSafe(target.NetworkObjectId);
            SendToServer(w);
        }

        /// <summary>Owner client: chopped the tree whose trunk it was hitting at this point.</summary>
        public static void RequestTreeChop(Vector3 at)
        {
            var w = NewWriter(Msg.TreeChopReq);
            w.WriteValueSafe(at);
            SendToServer(w);
        }

        /// <summary>Server: a tree comes down, on every client.</summary>
        public static void ServerTreeFell(Vector3 center)
        {
            var w = NewWriter(Msg.TreeFell);
            w.WriteValueSafe(center);
            Broadcast(w);
        }

        public static void RequestEat(StackItem stack)
        {
            var w = NewWriter(Msg.EatReq);
            w.WriteValueSafe(stack.NetworkObjectId);
            SendToServer(w);
        }

        // ------------------------------------------------------------------ public server API
        public static void ServerBroadcastOps(List<Op> ops)
        {
            if (ops.Count == 0) return;
            var w = NewWriter(Msg.Batch, 64 + ops.Count * 40);
            WriteOps(ref w, ops);
            Broadcast(w);
        }

        public static void ServerBroadcastOp(Op op) => ServerBroadcastOps(new List<Op> { op });

        public static void ServerStackCount(StackItem item)
        {
            var w = NewWriter(Msg.StackCount);
            w.WriteValueSafe(item.NetworkObjectId);
            w.WriteValueSafe(item.Count);
            Broadcast(w);
        }

        public static void ServerExplosion(Vector3 pos, float radius)
        {
            var w = NewWriter(Msg.Explosion);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(radius);
            Broadcast(w);
        }

        public static void ServerSound(Vector3 pos, string id, float pitch = 1f, float volume = 1f)
        {
            var w = NewWriter(Msg.Sound);
            w.WriteValueSafe(pos);
            w.WriteValueSafe(id);
            w.WriteValueSafe(pitch);
            w.WriteValueSafe(volume);
            Broadcast(w);
        }

        /// <summary>Owner client: add items to one of my stacks (items coming back from a screen).</summary>
        public static void RequestAddToStack(StackItem st, int n)
        {
            var w = NewWriter(Msg.AddToStackReq);
            w.WriteValueSafe(st.NetworkObjectId);
            w.WriteValueSafe(n);
            SendToServer(w);
        }

        /// <summary>Owner client: spawn items next to me and let me pick them up.</summary>
        public static void RequestSpawnForMe(string key, int n, bool pickUp = true)
        {
            var w = NewWriter(Msg.SpawnForMeReq);
            w.WriteValueSafe(key);
            w.WriteValueSafe(n);
            w.WriteValueSafe(pickUp);
            SendToServer(w);
        }

        /// <summary>Owner client (creative): one of the game's own items (by name), next to me and into my hotbar.</summary>
        public static void RequestSpawnVanilla(string itemName)
        {
            var w = NewWriter(Msg.SpawnVanillaReq);
            w.WriteValueSafe(itemName);
            SendToServer(w);
        }

        public static void RequestPearlThrow(Vector3 start, Vector3 vel)
        {
            var w = NewWriter(Msg.PearlThrowReq);
            w.WriteValueSafe(start);
            w.WriteValueSafe(vel);
            SendToServer(w);
        }

        public static void ServerAutoGrab(ulong client, ulong netId)
        {
            var w = NewWriter(Msg.AutoGrab);
            w.WriteValueSafe(netId);
            Broadcast(w, client);
        }

        public static void ServerToast(ulong client, string text)
        {
            var w = NewWriter(Msg.Toast);
            w.WriteValueSafe(text);
            Broadcast(w, client);
        }

        public static void ServerScrapValue(ulong netId, int value)
        {
            var w = NewWriter(Msg.ScrapValue);
            w.WriteValueSafe(netId);
            w.WriteValueSafe(value);
            Broadcast(w);
        }

        public static readonly Dictionary<ulong, int> PendingScrap = new Dictionary<ulong, int>();

        public static void ServerCrack(BlockKey key, sbyte stage)
        {
            var w = NewWriter(Msg.MineProgress);
            W(ref w, key);
            w.WriteValueSafe(stage);
            w.WriteValueSafe(ulong.MaxValue);
            Broadcast(w);
        }

        public static void ServerXp(ulong client, int amount)
        {
            var w = NewWriter(Msg.Xp);
            w.WriteValueSafe(amount);
            Broadcast(w, client);
        }

        static void ServerSendFullSync(ulong client)
        {
            var ops = BlockWorld.Instance != null ? BlockWorld.Instance.SnapshotOps() : new List<Op>();
            var stacks = Object.FindObjectsOfType<StackItem>().Where(s => s.IsSpawned).ToList();
            var w = NewWriter(Msg.FullSync, 1024 + ops.Count * 32 + stacks.Count * 12 + BlockWorld.Molds.Count * 48);
            // shapes first: blocks read theirs when they're created
            w.WriteValueSafe(BlockWorld.Molds.Count);
            foreach (var kv in BlockWorld.Molds)
            {
                W(ref w, kv.Key);
                w.WriteValueSafe(kv.Value.Length);
                foreach (var b in kv.Value) w.WriteValueSafe(b);
            }
            WriteOps(ref w, ops);
            w.WriteValueSafe(stacks.Count);
            foreach (var s in stacks) { w.WriteValueSafe(s.NetworkObjectId); w.WriteValueSafe(s.Count); }
            w.WriteValueSafe(TerrainCarver.Cuts.Count);
            foreach (var c in TerrainCarver.Cuts) WriteCut(ref w, c);
            w.WriteValueSafe(Chests.All.Count);
            foreach (var kv in Chests.All) WriteChest(ref w, kv.Key, kv.Value);
            var creative = GameModes.All.ToList();
            w.WriteValueSafe(creative.Count);
            foreach (var id in creative) w.WriteValueSafe(id);
            Broadcast(w, client);
        }

        // ------------------------------------------------------------------ receiving
        static void OnMessage(ulong sender, FastBufferReader reader)
        {
            try
            {
                int start = reader.Position; // remote named messages may start after a header
                reader.ReadValueSafe(out byte peek);
                reader.Seek(start);
                if (ToServer(peek))
                {
                    if (NetworkManager.Singleton.IsServer) HandleOnServer(sender, ref reader);
                }
                else HandleOnClient(ref reader);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("BlockNet message error: " + e);
            }
        }

        static void HandleOnServer(ulong sender, ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte m);
            var world = BlockWorld.Instance;
            if (world == null) return;
            switch ((Msg)m)
            {
                case Msg.PlaceReq:
                    {
                        r.ReadValueSafe(out ulong stackId);
                        var key = RK(ref r);
                        r.ReadValueSafe(out byte type);
                        r.ReadValueSafe(out byte facing);
                        ServerLogic.HandlePlace(sender, stackId, key, type, facing);
                    }
                    break;
                case Msg.BreakReq:
                    {
                        var key = RK(ref r);
                        r.ReadValueSafe(out bool pick);
                        ServerLogic.HandleBreak(sender, key, pick);
                    }
                    break;
                case Msg.UseReq:
                    ServerLogic.HandleUse(sender, RK(ref r));
                    break;
                case Msg.IgniteReq:
                    Fire.ServerLight(sender, RK(ref r)); // TNT there: lit; an empty cell: fire
                    break;
                case Msg.SyncReq:
                    ServerSendFullSync(sender);
                    foreach (var kv in Armor.All.ToList()) ServerArmor(kv.Key, kv.Value, sender);
                    break;
                case Msg.SpawnVanillaReq:
                    {
                        r.ReadValueSafe(out string name);
                        if (GameModes.IsCreative(sender)) Inventory.ServerSpawnVanillaFor(sender, name);
                    }
                    break;
                case Msg.MergeGroundReq:
                    {
                        r.ReadValueSafe(out ulong groundId); r.ReadValueSafe(out ulong targetId);
                        Pickup.ServerMerge(sender, groundId, targetId);
                    }
                    break;
                case Msg.TreeChopReq:
                    {
                        r.ReadValueSafe(out Vector3 at);
                        Trees.ServerChop(sender, at);
                    }
                    break;
                case Msg.ArmorReq:
                    {
                        r.ReadValueSafe(out bool dropOld);
                        r.ReadValueSafe(out Vector3 at);
                        var keys = new string[Armor.Slots];
                        for (int i = 0; i < Armor.Slots; i++) { r.ReadValueSafe(out string k); keys[i] = k.Length > 0 ? k : null; }
                        Armor.ServerSet(sender, keys, dropOld, at);
                    }
                    break;
                case Msg.MineProgressReq:
                    {
                        var key = RK(ref r);
                        r.ReadValueSafe(out sbyte stage);
                        var w = NewWriter(Msg.MineProgress);
                        W(ref w, key);
                        w.WriteValueSafe(stage);
                        w.WriteValueSafe(sender);
                        Broadcast(w);
                    }
                    break;
                case Msg.GroundDigReq:
                    {
                        r.ReadValueSafe(out Vector3 pt);
                        r.ReadValueSafe(out Vector3 nrm);
                        ServerLogic.HandleGroundDig(sender, pt, nrm);
                    }
                    break;
                case Msg.InsideReq:
                    {
                        r.ReadValueSafe(out bool inside);
                        var pl = ServerLogic.PlayerFor(sender);
                        if (pl != null)
                        {
                            var w = NewWriter(Msg.InsideState);
                            w.WriteValueSafe((int)pl.playerClientId);
                            w.WriteValueSafe(inside);
                            Broadcast(w);
                        }
                    }
                    break;
                case Msg.AddToStackReq:
                    {
                        r.ReadValueSafe(out ulong id);
                        r.ReadValueSafe(out int n);
                        var pl = ServerLogic.PlayerFor(sender);
                        if (pl != null && n > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out var ano) &&
                            ano.GetComponent<StackItem>() is StackItem ast && ast.playerHeldBy == pl)
                        {
                            int add = Mathf.Min(n, ast.MaxStack - ast.Count);
                            if (add > 0) ast.ServerSetCount(ast.Count + add);
                            if (add < n) Inventory.ServerSpawnFor(sender, Crafting.KeyOf(ast), n - add); // raced past full: hand the rest over
                        }
                    }
                    break;
                case Msg.SpawnForMeReq:
                    {
                        r.ReadValueSafe(out string key);
                        r.ReadValueSafe(out int n);
                        r.ReadValueSafe(out bool pickUp);
                        Inventory.ServerSpawnFor(sender, key, Mathf.Clamp(n, 0, 64 * 9), pickUp);
                    }
                    break;
                case Msg.ChestTakeReq:
                    {
                        var k = RK(ref r);
                        r.ReadValueSafe(out int slot); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool toInv);
                        var (key, got) = Chests.ServerTake(k, slot, n);
                        ServerChestGive(sender, key, got, toInv);
                    }
                    break;
                case Msg.StorageTakeReq:
                    {
                        r.ReadValueSafe(out int slot); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool toInv);
                        var (key, got) = Storage.ServerTake(sender, slot, n);
                        ServerStorageGive(sender, key, got, toInv);
                    }
                    break;
                case Msg.StoragePutReq:
                    {
                        r.ReadValueSafe(out int slot); r.ReadValueSafe(out string key); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool swap);
                        var (back, bn) = Storage.ServerPut(sender, slot, key, Mathf.Clamp(n, 0, 64), swap);
                        ServerStorageGive(sender, back, bn, slot < 0); // (a shift-click's leftovers to the inventory, a click's onto the mouse)
                    }
                    break;
                case Msg.StorageDropReq:
                    {
                        r.ReadValueSafe(out Vector3 at);
                        Storage.ServerDrop(sender, at);
                    }
                    break;
                case Msg.ChestPutReq:
                    {
                        var k = RK(ref r);
                        r.ReadValueSafe(out int slot); r.ReadValueSafe(out string key); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool swap);
                        var (back, bn) = Chests.ServerPut(k, slot, key, n, swap);
                        // a shift-click's leftovers go back to the inventory, a click's onto the mouse
                        ServerChestGive(sender, back, bn, slot < 0);
                    }
                    break;
                case Msg.PearlThrowReq:
                    {
                        r.ReadValueSafe(out Vector3 start);
                        r.ReadValueSafe(out Vector3 vel);
                        if (vel.magnitude > 200f) break;
                        var w = NewWriter(Msg.PearlFlight);
                        w.WriteValueSafe(sender);
                        w.WriteValueSafe(start);
                        w.WriteValueSafe(vel);
                        Broadcast(w);
                    }
                    break;
                case Msg.ConsumeReq:
                    {
                        r.ReadValueSafe(out ulong id);
                        r.ReadValueSafe(out int n);
                        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out var cno) && cno.GetComponent<StackItem>() is StackItem cs && n > 0)
                            cs.ServerSetCount(cs.Count - n);
                    }
                    break;
                case Msg.CraftReq:
                    {
                        r.ReadValueSafe(out int idx);
                        r.ReadValueSafe(out int times);
                        Crafting.ServerCraft(sender, idx, Mathf.Clamp(times, 1, 64));
                    }
                    break;
                case Msg.FurnaceInsertReq:
                    {
                        var fk = RK(ref r);
                        r.ReadValueSafe(out string ik);
                        r.ReadValueSafe(out ulong sid);
                        r.ReadValueSafe(out int n);
                        StackItem stack = null;
                        if (sid != 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(sid, out var sno)) stack = sno.GetComponent<StackItem>();
                        if (stack != null) n = Mathf.Min(n, stack.Count);
                        int taken = Crafting.ServerInsert(fk, ik, n);
                        if (stack != null && taken > 0) stack.ServerSetCount(stack.Count - taken);
                        else if (stack == null && taken < n && ModItems.ByKey.TryGetValue(ik, out var back))
                        {
                            // single item the owner already removed but the furnace refused: give it back
                            var p = ServerLogic.PlayerFor(sender);
                            if (p != null) ModItems.ServerSpawnPlain(back, p.transform.position + Vector3.up * 0.6f);
                        }
                        if (taken > 0) ServerSound(world.WorldCenter(fk), "click", 1.4f, 0.5f);
                        else ServerToast(sender, "That can't go in the furnace right now.");
                    }
                    break;
                case Msg.FurnaceTakeReq:
                    Crafting.ServerTake(sender, RK(ref r));
                    break;
                case Msg.EatReq:
                    {
                        r.ReadValueSafe(out ulong stackId);
                        ServerLogic.HandleConsume(sender, stackId);
                    }
                    break;
            }
        }

        public static readonly Dictionary<ulong, int> PendingCounts = new Dictionary<ulong, int>();

        static void HandleOnClient(ref FastBufferReader r)
        {
            r.ReadValueSafe(out byte m);
            var world = BlockWorld.Instance;
            switch ((Msg)m)
            {
                case Msg.Batch:
                    {
                        var ops = ReadOps(ref r);
                        world?.Apply(ops);
                    }
                    break;
                case Msg.FullSync:
                    {
                        r.ReadValueSafe(out int nm);
                        for (int i = 0; i < nm; i++)
                        {
                            var k = RK(ref r);
                            r.ReadValueSafe(out int len);
                            var bytes = new byte[len];
                            for (int j = 0; j < len; j++) r.ReadValueSafe(out bytes[j]);
                            if (!NetworkManager.Singleton.IsServer) BlockWorld.Molds[k] = bytes;
                        }
                        var ops = ReadOps(ref r);
                        if (world != null && !NetworkManager.Singleton.IsServer) world.Apply(ops);
                        r.ReadValueSafe(out int n);
                        for (int i = 0; i < n; i++)
                        {
                            r.ReadValueSafe(out ulong id);
                            r.ReadValueSafe(out int c);
                            ApplyCount(id, c);
                        }
                        r.ReadValueSafe(out int nc);
                        for (int i = 0; i < nc; i++)
                        {
                            var cut = ReadCut(ref r);
                            if (!NetworkManager.Singleton.IsServer) TerrainCarver.Apply(cut);
                        }
                        r.ReadValueSafe(out int nch);
                        for (int i = 0; i < nch; i++)
                        {
                            var (k, c) = ReadChest(ref r);
                            if (!NetworkManager.Singleton.IsServer && c != null) Chests.All[k] = c;
                        }
                        r.ReadValueSafe(out int ngm);
                        var gm = new List<ulong>();
                        for (int i = 0; i < ngm; i++) { r.ReadValueSafe(out ulong id); gm.Add(id); }
                        if (!NetworkManager.Singleton.IsServer) GameModes.Receive(gm);
                    }
                    break;
                case Msg.StackCount:
                    {
                        r.ReadValueSafe(out ulong id);
                        r.ReadValueSafe(out int c);
                        ApplyCount(id, c);
                        Inventory.OnCountConfirmed(id);
                    }
                    break;
                case Msg.Explosion:
                    {
                        r.ReadValueSafe(out Vector3 pos);
                        r.ReadValueSafe(out float radius);
                        ClientEffects.Explosion(pos, radius);
                    }
                    break;
                case Msg.MineProgress:
                    {
                        var key = RK(ref r);
                        r.ReadValueSafe(out sbyte stage);
                        r.ReadValueSafe(out ulong who);
                        if (who != NetworkManager.Singleton.LocalClientId) ClientEffects.RemoteMineProgress(key, stage, who);
                    }
                    break;
                case Msg.Sound:
                    {
                        r.ReadValueSafe(out Vector3 pos);
                        r.ReadValueSafe(out string id);
                        r.ReadValueSafe(out float pitch);
                        r.ReadValueSafe(out float vol);
                        Sounds.Play(id, pos, vol, pitch);
                    }
                    break;
                case Msg.Toast:
                    {
                        r.ReadValueSafe(out string text);
                        McHud.Toast(text);
                    }
                    break;
                case Msg.ChestState:
                    {
                        var (k, c) = ReadChest(ref r);
                        if (!NetworkManager.Singleton.IsServer) { if (c == null) Chests.All.Remove(k); else Chests.All[k] = c; }
                        Chests.ApplyState(k, NetworkManager.Singleton.IsServer ? Chests.Of(k) : c);
                    }
                    break;
                case Msg.TreeFell:
                    {
                        r.ReadValueSafe(out Vector3 center);
                        Trees.Fell(center);
                    }
                    break;
                case Msg.ArmorState:
                    {
                        r.ReadValueSafe(out ulong player);
                        var keys = new string[Armor.Slots];
                        for (int i = 0; i < Armor.Slots; i++) { r.ReadValueSafe(out string k); keys[i] = k.Length > 0 ? k : null; }
                        Armor.Receive(player, keys);
                    }
                    break;
                case Msg.GameModes:
                    {
                        r.ReadValueSafe(out int n);
                        var ids = new List<ulong>();
                        for (int i = 0; i < n; i++) { r.ReadValueSafe(out ulong id); ids.Add(id); }
                        GameModes.Receive(ids);
                    }
                    break;
                case Msg.StorageState:
                    {
                        r.ReadValueSafe(out bool enabled);
                        var c = new Chests.Contents();
                        for (int i = 0; i < Storage.Size; i++)
                        {
                            r.ReadValueSafe(out string key); r.ReadValueSafe(out int n);
                            if (n > 0 && key.Length > 0) { c.Key[i] = key; c.Count[i] = n; }
                        }
                        Storage.Receive(enabled, c);
                    }
                    break;
                case Msg.StorageGive:
                    {
                        r.ReadValueSafe(out string key); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool toInv);
                        CraftingUI.ReceivedFromStorage(key, n, toInv);
                    }
                    break;
                case Msg.ChestGive:
                    {
                        r.ReadValueSafe(out string key); r.ReadValueSafe(out int n); r.ReadValueSafe(out bool toInv);
                        ChestUI.Received(key, n, toInv);
                    }
                    break;
                case Msg.FurnaceState:
                    {
                        var fk = RK(ref r);
                        r.ReadValueSafe(out bool has);
                        if (!has) { Crafting.Furnaces.Remove(fk); break; }
                        var f = Crafting.Furnaces.TryGetValue(fk, out var ef) ? ef : new Crafting.Furnace();
                        r.ReadValueSafe(out string fin); r.ReadValueSafe(out f.InCount);
                        r.ReadValueSafe(out string ffk); r.ReadValueSafe(out f.Fuel);
                        r.ReadValueSafe(out string fout); r.ReadValueSafe(out f.OutCount);
                        r.ReadValueSafe(out f.Burn); r.ReadValueSafe(out f.Progress);
                        f.In = fin == "" ? null : fin; f.FuelKey = ffk == "" ? null : ffk; f.Out = fout == "" ? null : fout;
                        Crafting.Furnaces[fk] = f;
                    }
                    break;
                case Msg.Molds:
                    {
                        r.ReadValueSafe(out int n);
                        for (int i = 0; i < n; i++)
                        {
                            var k = RK(ref r);
                            r.ReadValueSafe(out int len);
                            var bytes = new byte[len];
                            for (int j = 0; j < len; j++) r.ReadValueSafe(out bytes[j]);
                            BlockWorld.Molds[k] = bytes;
                            BlockWorld.Instance?.RefreshMold(k);
                        }
                    }
                    break;
                case Msg.Cut:
                    {
                        var cut = ReadCut(ref r);
                        TerrainCarver.Apply(cut);
                    }
                    break;
                case Msg.ScrapValue:
                    {
                        r.ReadValueSafe(out ulong id);
                        r.ReadValueSafe(out int value);
                        var nm = NetworkManager.Singleton;
                        if (nm.SpawnManager != null && nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var sno) && sno.GetComponent<GrabbableObject>() is GrabbableObject g)
                        {
                            g.SetScrapValue(value);
                            if (RoundManager.Instance != null) RoundManager.Instance.totalScrapValueInLevel += value;
                        }
                        else PendingScrap[id] = value;
                    }
                    break;
                case Msg.InsideState:
                    {
                        r.ReadValueSafe(out int idx);
                        r.ReadValueSafe(out bool inside);
                        Facility.ApplyRemote(idx, inside);
                    }
                    break;
                case Msg.PearlFlight:
                    {
                        r.ReadValueSafe(out ulong owner);
                        r.ReadValueSafe(out Vector3 start);
                        r.ReadValueSafe(out Vector3 vel);
                        EnderPearls.StartFlight(owner, start, vel);
                    }
                    break;
                case Msg.AutoGrab:
                    {
                        r.ReadValueSafe(out ulong gid);
                        Inventory.QueueGrab(gid);
                    }
                    break;
                case Msg.Xp:
                    {
                        r.ReadValueSafe(out int amount);
                        Survival.AddXp(amount);
                    }
                    break;
            }
        }

        static void ApplyCount(ulong id, int c)
        {
            var nm = NetworkManager.Singleton;
            if (nm.SpawnManager != null && nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var no))
            {
                var s = no.GetComponent<StackItem>();
                if (s != null) { s.SetCountLocal(c); return; }
            }
            PendingCounts[id] = c;
        }
    }
}
