using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The big inventory (optional, the host's "BigInventory" setting, off by default): Minecraft's 3x9 storage grid in
    /// the [I] inventory, on top of the hotbar. Minecraft items only. What's stored weighs what it would in the hotbar,
    /// drops where you die, stays with you from round to round and is kept with the host's save. The server holds
    /// everyone's storage (by who they are, so it survives leaving and rejoining); each player gets a copy of their own.
    /// Items moved in or out pass through the player's hotbar or mouse, like a chest's, so nothing is made or lost.
    /// </summary>
    [HarmonyPatch]
    public static class Storage
    {
        public const int Size = Chests.Size; // 27

        // ------------------------------------------------------------------ client: my own storage
        public static Chests.Contents Local = new Chests.Contents();
        /// <summary>The host has the big inventory on (sent with the contents).</summary>
        public static bool Enabled;
        /// <summary>The grid shows when it's on, or while something is still stored (taken out after the host turned it off).</summary>
        public static bool ShowGrid => Enabled || !Local.Empty;

        static float applied; // the weight added to the local player's carry weight for what's stored

        /// <summary>A stack weighs the same stored as in the hotbar (a stack, whatever its count: as the game counts it).</summary>
        public static float WeightOf(Chests.Contents c)
        {
            float w = 0f;
            for (int i = 0; i < Size; i++)
                if (c.Count[i] > 0 && c.Key[i] != null && ModItems.ByKey.TryGetValue(c.Key[i], out var item)) w += item.weight - 1f;
            return w;
        }

        static PlayerControllerB LocalPlayer => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        /// <summary>The local player's carry weight follows what's stored.</summary>
        static void ApplyWeight()
        {
            var p = LocalPlayer;
            if (p == null) return;
            float want = WeightOf(Local);
            if (Mathf.Approximately(want, applied)) return;
            p.carryWeight = Mathf.Clamp(p.carryWeight + (want - applied), 1f, 10f);
            applied = want;
            StartOfRound.Instance?.SendChangedWeightEvent();
        }

        /// <summary>The game set the carry weight back to nothing (death, dropping everything, a new round): add ours again.</summary>
        static void WeightWasReset() { applied = 0f; ApplyWeight(); }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems)), HarmonyPostfix]
        static void AfterDropAll(PlayerControllerB __instance) { if (__instance == LocalPlayer) WeightWasReset(); }

        // a new round: only the players who were dead get their carry weight set back to nothing (the living keep theirs)
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ReviveDeadPlayers)), HarmonyPrefix]
        static void BeforeRevive(out bool __state) { var p = LocalPlayer; __state = p != null && p.isPlayerDead; }

        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ReviveDeadPlayers)), HarmonyPostfix]
        static void AfterRevive(bool __state) { if (__state) WeightWasReset(); }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPostfix]
        static void DropOnDeath(PlayerControllerB __instance)
        {
            if (__instance == null || !__instance.IsOwner || !__instance.isPlayerDead) return;
            applied = 0f; // (dying set the carry weight back to nothing)
            if (Local.Empty) return;
            BlockNet.RequestStorageDrop(__instance.positionOfDeath);
        }

        /// <summary>Client: the server's copy of my storage (and whether the host has the big inventory on).</summary>
        public static void Receive(bool enabled, Chests.Contents c)
        {
            Enabled = enabled;
            Local = c ?? new Chests.Contents();
            ApplyWeight();
            CraftingUI.OnStorageChanged();
        }

        public static void ResetLocal() { Local = new Chests.Contents(); Enabled = false; applied = 0f; }

        // ------------------------------------------------------------------ server: everyone's storage, by who they are
        static readonly Dictionary<string, Chests.Contents> all = new Dictionary<string, Chests.Contents>();
        static bool loaded;

        public static bool? DevForce; // (dev: on or off for this session without touching the config file)
        public static bool ServerEnabled => DevForce ?? Plugin.BigInventory.Value;

        /// <summary>Server: the setting changed: everyone gets told.</summary>
        public static void ServerResendAll() { foreach (var c in sent.ToList()) ServerSend(c); }

        static string IdOf(ulong client)
        {
            // (dead players too: dying is when the storage drops)
            var sor = StartOfRound.Instance;
            var p = sor == null ? null : sor.allPlayerScripts.FirstOrDefault(x => x != null && (x.isPlayerControlled || x.isPlayerDead) && x.actualClientId == client);
            if (p == null) return null;
            string id = Armor.IdentityOf(p);
            return string.IsNullOrEmpty(id) || id == "Player" ? null : id; // (not known yet while joining)
        }

        static Chests.Contents Of(string id)
        {
            ServerLoad();
            if (!all.TryGetValue(id, out var c)) all[id] = c = new Chests.Contents();
            return c;
        }

        public static void ServerSend(ulong client)
        {
            string id = IdOf(client);
            BlockNet.ServerStorage(client, ServerEnabled, id != null ? Of(id) : new Chests.Contents());
        }

        public static (string key, int n) ServerTake(ulong client, int slot, int n)
        {
            string id = IdOf(client);
            if (id == null) return (null, 0);
            var got = Chests.TakeFrom(Of(id), slot, n);
            if (got.n > 0) ServerSend(client);
            return got;
        }

        public static (string key, int n) ServerPut(ulong client, int slot, string key, int n, bool swap)
        {
            string id = IdOf(client);
            if (id == null || !ServerEnabled) return (key, n); // (off: nothing goes in; what's there can still come out)
            var back = Chests.PutInto(Of(id), slot, key, n, swap);
            ServerSend(client);
            return back;
        }

        /// <summary>Server: a player died: what they stored drops where they fell.</summary>
        public static void ServerDrop(ulong client, Vector3 at)
        {
            string id = IdOf(client);
            if (id == null) return;
            var c = Of(id);
            if (c.Empty) return;
            Chests.SpawnContents(c, at + Vector3.up * 0.4f);
            all[id] = new Chests.Contents();
            ServerSend(client);
        }

        /// <summary>Server, now and then: everyone has the current copy of their storage (players who just joined).</summary>
        static readonly HashSet<ulong> sent = new HashSet<ulong>();
        public static void ServerSendNew()
        {
            var sor = StartOfRound.Instance;
            if (!BlockNet.IsServer || sor == null) return;
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || IdOf(p.actualClientId) == null || !sent.Add(p.actualClientId)) continue;
                ServerSend(p.actualClientId);
            }
        }

        public static void ServerForget(ulong client) => sent.Remove(client);

        /// <summary>Fired: everyone's storage goes with the ship's contents.</summary>
        public static void ServerClearAll(string file)
        {
            all.Clear();
            if (ES3.KeyExists(SaveKey, file)) ES3.DeleteKey(SaveKey, file);
            foreach (var c in sent.ToList()) ServerSend(c);
        }

        public static void Reset() { all.Clear(); loaded = false; sent.Clear(); ResetLocal(); }

        // ------------------------------------------------------------------ saving (host, with the ship)
        const string SaveKey = "LMC_Storage_v1";

        public static void Save(string file)
        {
            ServerLoad();
            var lines = all.Where(kv => !kv.Value.Empty).Select(kv => kv.Key + "=" +
                string.Join("|", Enumerable.Range(0, Size).Select(i => kv.Value.Count[i] > 0 ? kv.Value.Key[i] + ":" + kv.Value.Count[i] : "")));
            ES3.Save(SaveKey, string.Join("\n", lines), file);
        }

        static void ServerLoad()
        {
            if (loaded || GameNetworkManager.Instance == null) return;
            loaded = true;
            string file = GameNetworkManager.Instance.currentSaveFileName;
            if (!ES3.KeyExists(SaveKey, file)) return;
            foreach (var line in ES3.Load<string>(SaveKey, file).Split('\n'))
            {
                int eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                var cells = line.Substring(eq + 1).Split('|');
                if (cells.Length != Size) continue;
                var c = new Chests.Contents();
                for (int i = 0; i < Size; i++)
                {
                    int colon = cells[i].LastIndexOf(':');
                    if (colon <= 0 || !int.TryParse(cells[i].Substring(colon + 1), out int n) || n <= 0) continue;
                    string key = cells[i].Substring(0, colon);
                    if (Chests.Accepts(key)) { c.Key[i] = key; c.Count[i] = Mathf.Min(n, Inventory.MaxStackOf(key)); }
                }
                all[line.Substring(0, eq)] = c;
            }
        }

        public static string DevDescribe() => $"enabled={Enabled} weight={WeightOf(Local):F2} applied={applied:F2} cells=[" +
            string.Join(",", Enumerable.Range(0, Size).Select(i => Local.Count[i] > 0 ? $"{Local.Key[i]}x{Local.Count[i]}" : ".")) + "]";
    }
}
