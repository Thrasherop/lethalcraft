using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) A number on a stack (firework rockets and stars: Fireworks). A tool or armor piece saves its number itself;
    /// a stack saves only its count, so its number is kept here by its network id, as a custom name is (ItemNames): the
    /// host sets it when the stack comes out of a key, tells everyone, and saves the ship's stacks' numbers by their kind and
    /// place. The stack's key carries it (Crafting.KeyOf: "firework_rocket#12"), so stacks with different numbers don't
    /// merge, and the number goes wherever the key goes (chests, storage, the hotbar, messages).
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class StackData
    {
        static readonly Dictionary<ulong, int> objects = new Dictionary<ulong, int>();
        const string SaveKey = "LMC_StackData_v1";
        public static int Applied; // (dev/tests)

        public static int Of(GrabbableObject g) => g != null && g.NetworkObject != null && objects.TryGetValue(g.NetworkObjectId, out var n) ? n : 0;

        /// <summary>Host: a stack spawned for a key: its number, if the key has one.</summary>
        public static void ServerApply(GrabbableObject g, string key)
        {
            int n = ItemData.Of(key);
            if (!(g is StackItem) || g.NetworkObject == null || n <= 0) return;
            objects[g.NetworkObjectId] = n;
            NameKeeper.On(g.gameObject, g.NetworkObjectId, false);
            BlockNet.ServerStackData(g.NetworkObjectId, n);
            Applied++;
            Fireworks.ApplyModel(g);
        }

        public static void Forget(ulong obj) => objects.Remove(obj);

        public static void ServerSyncTo(ulong client)
        {
            foreach (var kv in objects) BlockNet.ServerStackData(kv.Key, kv.Value, client);
        }

        /// <summary>Every machine: a stack's number from the host.</summary>
        public static void Receive(ulong obj, int n)
        {
            if (n <= 0) { objects.Remove(obj); return; }
            objects[obj] = n;
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(obj, out var no))
            {
                NameKeeper.On(no.gameObject, obj, false);
                Fireworks.ApplyModel(no.GetComponent<GrabbableObject>());
            }
        }

        // ------------------------------------------------------------------ saved with the game
        public static void Save(string file)
        {
            try
            {
                var ms = new MemoryStream(); var w = new BinaryWriter(ms);
                var numbered = UnityEngine.Object.FindObjectsOfType<StackItem>().Where(g => Of(g) > 0 && BlockWorld.InShip(g.transform.position)).ToList();
                w.Write(numbered.Count);
                foreach (var g in numbered) { var p = g.transform.position; w.Write(g.ItemKey ?? ""); w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(Of(g)); }
                ES3.Save(SaveKey, Convert.ToBase64String(ms.ToArray()), file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("stack numbers save: " + e.Message); }
        }

        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "LoadShipGrabbableItems"), HarmonyLib.HarmonyPostfix]
        static void ShipItemsLoaded() { try { Load(GameNetworkManager.Instance.currentSaveFileName); } catch (Exception e) { Plugin.Log.LogWarning("stack numbers: " + e.Message); } }

        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "Start"), HarmonyLib.HarmonyPrefix]
        static void NewGame() => objects.Clear();

        /// <summary>Host, as the game puts the ship's saved items out (where they were saved): each numbered stack's number,
        /// found by its kind and place.</summary>
        public static void Load(string file)
        {
            try
            {
                if (!ES3.KeyExists(SaveKey, file)) return;
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SaveKey, file))));
                int m = r.ReadInt32();
                var items = UnityEngine.Object.FindObjectsOfType<StackItem>().Where(g => g.NetworkObject != null && g.NetworkObject.IsSpawned).ToList();
                int found = 0;
                for (int i = 0; i < m; i++)
                {
                    string type = r.ReadString(); var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); int n = r.ReadInt32();
                    var g = items.Where(x => !objects.ContainsKey(x.NetworkObjectId) && x.ItemKey == type).OrderBy(x => (x.transform.position - p).sqrMagnitude).FirstOrDefault();
                    if (g == null || (g.transform.position - p).sqrMagnitude > 0.5f * 0.5f) continue;
                    objects[g.NetworkObjectId] = n; NameKeeper.On(g.gameObject, g.NetworkObjectId, false); BlockNet.ServerStackData(g.NetworkObjectId, n); found++;
                }
                if (m > 0) Plugin.Log.LogInfo($"[stacks] {found} of {m} numbered stacks in the ship found again");
            }
            catch (Exception e) { Plugin.Log.LogWarning("stack numbers load: " + e.Message); }
        }
    }
}
