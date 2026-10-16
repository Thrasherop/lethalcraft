using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#228, the owner's: every item can be renamed at the anvil) Custom names. The host keeps each name text once, by
    /// number (sent to everyone, saved with the game, as banner designs are); an item key carries its name's number after
    /// a '~' (ItemData), so the name follows it through chests, storage, the hotbar and messages; an item lying about or
    /// in a hand has its name's number by its network id (sent to everyone, so every machine makes the same key of it).
    /// The ship's items keep their names across a reload by where they lie (the game saves them in place).
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class ItemNames
    {
        public const int MaxLength = 50; // (Minecraft's anvil: 50 characters)
        public static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        static readonly Dictionary<ulong, int> objects = new Dictionary<ulong, int>();
        static int nextId = 1;
        const string SaveNames = "LMC_ItemNames_v2";

        public static void Reset() { Names.Clear(); objects.Clear(); nextId = 1; }

        public static string Text(int id) => id > 0 && Names.TryGetValue(id, out var s) ? s : null;
        /// <summary>The custom name a key carries, or null.</summary>
        public static string Of(string key) => Text(ItemData.NameId(key));
        public static int IdOf(GrabbableObject g) => g != null && g.NetworkObject != null && objects.TryGetValue(g.NetworkObjectId, out var id) ? id : 0;
        public static string Of(GrabbableObject g) => Text(IdOf(g));

        /// <summary>Minecraft's anvil: the text trimmed, at most 50 characters.</summary>
        public static string Clean(string s)
        {
            s = (s ?? "").Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > MaxLength ? s.Substring(0, MaxLength) : s;
        }

        // ------------------------------------------------------------------ the host's
        public static int ServerRegister(string text)
        {
            text = Clean(text);
            if (text.Length == 0) return 0;
            foreach (var kv in Names) if (kv.Value == text) return kv.Key;
            int id = nextId++;
            Names[id] = text;
            BlockNet.ServerItemName(id, text);
            return id;
        }

        /// <summary>Host: an item spawned for a key: its name, if the key has one.</summary>
        public static void ServerApply(GrabbableObject g, string key)
        {
            int id = ItemData.NameId(key);
            if (g == null || g.NetworkObject == null || id <= 0) return;
            objects[g.NetworkObjectId] = id;
            NameKeeper.On(g.gameObject, g.NetworkObjectId, false);
            BlockNet.ServerObjectName(g.NetworkObjectId, id);
        }

        /// <summary>An item gone (used up, put away, despawned): its network id may come back as something else.</summary>
        public static void Forget(ulong obj) => objects.Remove(obj);

        public static void ServerSyncTo(ulong client)
        {
            foreach (var kv in Names) BlockNet.ServerItemName(kv.Key, kv.Value, client);
            foreach (var kv in objects) BlockNet.ServerObjectName(kv.Key, kv.Value, client);
        }

        // ------------------------------------------------------------------ every machine
        public static void ReceiveName(int id, string text) { Names[id] = text; if (id >= nextId) nextId = id + 1; }
        public static void ReceiveObject(ulong obj, int id)
        {
            if (id <= 0) { objects.Remove(obj); return; }
            objects[obj] = id;
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(obj, out var no)) NameKeeper.On(no.gameObject, obj, false);
        }

        // ------------------------------------------------------------------ saved with the game
        public static void Save(string file)
        {
            try
            {
                var ms = new MemoryStream(); var w = new BinaryWriter(ms);
                w.Write(Names.Count);
                foreach (var kv in Names) { w.Write(kv.Key); w.Write(kv.Value); }
                // (the ship's named items, where they lie: the game saves its items in place)
                var named = UnityEngine.Object.FindObjectsOfType<GrabbableObject>().Where(g => IdOf(g) > 0 && BlockWorld.InShip(g.transform.position)).ToList();
                w.Write(named.Count);
                foreach (var g in named) { var p = g.transform.position; w.Write(ItemData.TypeOf(Crafting.KeyOf(g)) ?? ""); w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(IdOf(g)); }
                ES3.Save(SaveNames, Convert.ToBase64String(ms.ToArray()), file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("item names save: " + e.Message); }
        }

        /// <summary>Host, right as the game puts the ship's saved items out (where they were saved, before anything moves
        /// them): the names, and each named item's, found by its kind and place.</summary>
        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "LoadShipGrabbableItems"), HarmonyLib.HarmonyPostfix]
        static void ShipItemsLoaded() { try { Load(GameNetworkManager.Instance.currentSaveFileName); } catch (Exception e) { Plugin.Log.LogWarning("item names: " + e.Message); } }

        /// <summary>A new game's names (before the game's own load, which brings the save's back).</summary>
        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "Start"), HarmonyLib.HarmonyPrefix]
        static void NewGame() => Reset();

        public static void Load(string file)
        {
            try
            {
                if (!ES3.KeyExists(SaveNames, file)) return;
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SaveNames, file))));
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int id = r.ReadInt32(); string s = r.ReadString(); Names[id] = s; nextId = Math.Max(nextId, id + 1); BlockNet.ServerItemName(id, s); }
                int m = r.ReadInt32();
                var items = UnityEngine.Object.FindObjectsOfType<GrabbableObject>().Where(g => g.NetworkObject != null && g.NetworkObject.IsSpawned).ToList();
                int found = 0;
                for (int i = 0; i < m; i++)
                {
                    string type = r.ReadString(); var p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); int id = r.ReadInt32();
                    // (the nearest of its own kind not named yet: held and pocketed things were saved where the hand was, together)
                    var g = items.Where(x => !objects.ContainsKey(x.NetworkObjectId) && ItemData.TypeOf(Crafting.KeyOf(x)) == type)
                                 .OrderBy(x => (x.transform.position - p).sqrMagnitude).FirstOrDefault();
                    if (g == null || (g.transform.position - p).sqrMagnitude > 0.5f * 0.5f) continue;
                    objects[g.NetworkObjectId] = id; NameKeeper.On(g.gameObject, g.NetworkObjectId, false); BlockNet.ServerObjectName(g.NetworkObjectId, id); found++;
                }
                if (m > 0) Plugin.Log.LogInfo($"[names] {found} of {m} named items in the ship found again");
            }
            catch (Exception e) { Plugin.Log.LogWarning("item names load: " + e.Message); }
        }
    }

    /// <summary>(#227, #228) On a named item or mob: when it's gone, its network id's name goes too (the id can come back
    /// as something else).</summary>
    public class NameKeeper : MonoBehaviour
    {
        ulong id; bool mob;
        public static void On(GameObject go, ulong id, bool mob)
        {
            if (go == null) return;
            var k = go.GetComponent<NameKeeper>() ?? go.AddComponent<NameKeeper>();
            k.id = id; k.mob = mob;
        }
        void OnDestroy() { if (mob) ShipMobs.Names.Remove(id); else ItemNames.Forget(id); }
    }
}
