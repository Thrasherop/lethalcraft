using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#227, the owner's) What stays with the ship: every passive animal on it, tamed or not (cows, pigs, sheep, chickens,
    /// cats, parrots, wolves, horses, llamas...), golems, boats and minecarts, and any mob with a name (#228's name tags, a
    /// creeper too). They ride along at takeoff (ShipPassengers) and are saved with the game: brought back aboard after a
    /// reload, in their places, looking the same (their LookId), named, tamed to the same player (by Steam id), with
    /// what a chest boat, chest minecart or llama carried. Unnamed monsters aren't kept, as before.
    /// Names live here too, by network id, sent to everyone (and to late joiners) with each mob's look.
    /// </summary>
    public static class ShipMobs
    {
        const string SaveKey = "LMC_ShipMobs_v1";
        public static readonly Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
        public static int Restored; // (dev/tests)

        public static string NameOf(EnemyAI e) => e != null && e.NetworkObject != null && Names.TryGetValue(e.NetworkObjectId, out var n) ? n : null;

        /// <summary>A mob the ship keeps (if it's aboard).</summary>
        public static bool Keeps(EnemyAI e)
        {
            if (e == null || e.isEnemyDead) return false;
            if (NameOf(e) != null) return true;
            if (e is PetAI || e is GolemAI || e is BoatAI || e is MinecartAI) return true;
            return e is MinecraftMobAI m && m.IsPassive && !(e is WanderingTraderAI);
        }

        public static void Reset() { Names.Clear(); pendingOwners.Clear(); MobSpawns.Restored.Clear(); }

        /// <summary>Every mob there is: the round's list and a search, together (a mob brought back from the save, switched
        /// off to ride before its Start ever ran, wasn't found by the search alone, and the next save lost it).</summary>
        public static List<EnemyAI> AllMobs()
        {
            var set = new HashSet<EnemyAI>();
            if (RoundManager.Instance != null) foreach (var e in RoundManager.Instance.SpawnedEnemies) if (e != null) set.Add(e);
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyAI>(true)) if (e != null) set.Add(e);
            return set.ToList();
        }

        /// <summary>In the ship: within its bounds, riding with it, or a parrot on the shoulder of someone in it (a
        /// shoulder can be above the ship's bounds box).</summary>
        public static bool Aboard(EnemyAI e)
        {
            if (e == null) return false;
            if (BlockWorld.InShip(e.transform.position) || ShipPassengers.IsRiding(e)) return true;
            if (e is ParrotAI pa && pa.Perched) { var o = pa.OwnerPlayer; return o != null && (o.isInHangarShipRoom || BlockWorld.InShip(o.transform.position)); }
            return false;
        }

        // ------------------------------------------------------------------ names and looks, to everyone
        /// <summary>Host: a mob's name (null: none), to everyone.</summary>
        public static void ServerSetName(EnemyAI e, string name)
        {
            if (e == null || e.NetworkObject == null) return;
            if (string.IsNullOrEmpty(name)) Names.Remove(e.NetworkObjectId); else { Names[e.NetworkObjectId] = name; NameKeeper.On(e.gameObject, e.NetworkObjectId, true); }
            BlockNet.ServerMobInfo(e);
            Changed?.Invoke(e);
        }

        /// <summary>Every machine: the host's word on a mob's look and name.</summary>
        public static void Receive(EnemyAI e, ulong look, string name)
        {
            if (e == null) return;
            if (e is MinecraftMobAI m) m.SetLook(look);
            if (string.IsNullOrEmpty(name)) Names.Remove(e.NetworkObjectId); else { Names[e.NetworkObjectId] = name; NameKeeper.On(e.gameObject, e.NetworkObjectId, true); }
            Changed?.Invoke(e);
        }

        /// <summary>A mob's name or look changed here (#228's name over its head listens).</summary>
        public static event Action<EnemyAI> Changed;

        public static void ServerSyncTo(ulong client)
        {
            foreach (var e in AllMobs())
            {
                if (e == null || e.NetworkObject == null || !e.NetworkObject.IsSpawned) continue;
                bool looked = e is MinecraftMobAI m && m.LookId != e.NetworkObjectId;
                if (looked || NameOf(e) != null) BlockNet.ServerMobInfo(e, client);
                if (e is MinecraftMobAI mm && !e.isEnemyDead) mm.ServerSyncTo(client);
            }
        }

        // ------------------------------------------------------------------ saved with the game
        /// <summary>Host, as the game saves (in orbit): the kept mobs aboard, where they are in the ship.</summary>
        public static void Save(string file)
        {
            try
            {
                var ship = StartOfRound.Instance.elevatorTransform;
                var list = AllMobs().Where(e => Keeps(e) && Aboard(e)).ToList();
                var ms = new MemoryStream(); var w = new BinaryWriter(ms);
                w.Write(list.Count);
                foreach (var e in list)
                {
                    var m = e as MinecraftMobAI;
                    var local = ship.InverseTransformPoint(e.transform.position);
                    float yaw = (Quaternion.Inverse(ship.rotation) * e.transform.rotation).eulerAngles.y;
                    w.Write(e.enemyType.enemyName); w.Write(e.enemyType.isOutsideEnemy);
                    w.Write(local.x); w.Write(local.y); w.Write(local.z); w.Write(yaw);
                    w.Write(m != null ? m.LookId : 0UL);
                    w.Write(NameOf(e) ?? "");
                    w.Write(m?.SaveState() ?? "");
                    var sk = m?.Storage;
                    var c = sk.HasValue ? Chests.Of(sk.Value) : null;
                    w.Write(c != null);
                    if (c != null) Chests.Write(w, new[] { new KeyValuePair<BlockKey, Chests.Contents>(sk.Value, c) });
                }
                ES3.Save(SaveKey, Convert.ToBase64String(ms.ToArray()), file);
                if (list.Count > 0) Plugin.Log.LogInfo($"[ship mobs] saved {list.Count}: {string.Join(", ", list.Select(e => e.enemyType.enemyName))}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("ship mobs save: " + e.Message); }
        }

        /// <summary>Host, as the save loads: each kept mob back in its place aboard, riding with the ship till it lands.</summary>
        public static void Load(string file)
        {
            try
            {
                if (!ES3.KeyExists(SaveKey, file)) return;
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(SaveKey, file))));
                var ship = StartOfRound.Instance.elevatorTransform;
                var types = Resources.FindObjectsOfTypeAll<EnemyType>().Where(t => t != null && t.enemyPrefab != null).ToList();
                int n = r.ReadInt32(), made = 0;
                for (int i = 0; i < n; i++)
                {
                    string type = r.ReadString(); bool outside = r.ReadBoolean();
                    var local = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float yaw = r.ReadSingle();
                    ulong look = r.ReadUInt64(); string name = r.ReadString(); string state = r.ReadString();
                    Chests.Contents contents = null;
                    if (r.ReadBoolean()) { var cl = Chests.Read(r, Chests.MobFrame); if (cl.Count > 0) contents = cl[0].Value; }
                    var t = types.FirstOrDefault(x => x.enemyName == type && x.isOutsideEnemy == outside) ?? types.FirstOrDefault(x => x.enemyName == type);
                    if (t == null) { Plugin.Log.LogWarning($"[ship mobs] no '{type}' in this game: not brought back"); continue; }
                    var at = ship.TransformPoint(local);
                    MobSpawns.DevSpawnUntil = Time.time + 1f; // (not a natural spawn)
                    var refr = RoundManager.Instance.SpawnEnemyGameObject(at, ship.eulerAngles.y + yaw, -1, t);
                    if (!refr.TryGet(out NetworkObject no) || !(no.GetComponent<EnemyAI>() is EnemyAI e)) continue;
                    MobSpawns.Restored.Add(e); // (its Start may wait till the landing: no light, no natural-spawn rule touches it then)
                    if (e is MinecraftMobAI m)
                    {
                        if (look != 0) m.SetLook(look);
                        if (state.Length > 0) m.LoadState(state);
                        var sk = m.Storage;
                        if (contents != null && sk.HasValue) { contents.Slots = Chests.SlotsAt(sk.Value); Chests.All[sk.Value] = contents; BlockNet.ServerChest(sk.Value, contents); }
                    }
                    if (name.Length > 0) { Names[e.NetworkObjectId] = name; NameKeeper.On(e.gameObject, e.NetworkObjectId, true); }
                    BlockNet.ServerMobInfo(e);
                    if (e is PetAI pet && !pet.ServerFindOwner()) pendingOwners.Add(pet);
                    ShipPassengers.Aboard(e);
                    made++;
                }
                Restored += made;
                if (made > 0) Plugin.Log.LogInfo($"[ship mobs] {made} back aboard from the save");
            }
            catch (Exception e) { Plugin.Log.LogWarning("ship mobs load: " + e); }
        }

        /// <summary>Host: fired: what the ship kept goes with everything else aboard.</summary>
        public static void ServerFired()
        {
            int n = 0;
            foreach (var e in AllMobs())
                if (Keeps(e) && Aboard(e)) { e.KillEnemyOnOwnerClient(overrideDestroy: true); n++; }
            if (GameNetworkManager.Instance != null && ES3.KeyExists(SaveKey, GameNetworkManager.Instance.currentSaveFileName)) ES3.DeleteKey(SaveKey, GameNetworkManager.Instance.currentSaveFileName);
            if (n > 0) Plugin.Log.LogInfo($"[ship mobs] fired: {n} aboard gone");
        }

        // ------------------------------------------------------------------ owners who weren't here yet
        static readonly List<PetAI> pendingOwners = new List<PetAI>();
        static float nextOwnerLook;

        /// <summary>Host, every frame: a pet back from the save finds its owner when they join.</summary>
        public static void ServerTick()
        {
            if (pendingOwners.Count == 0 || Time.time < nextOwnerLook) return;
            nextOwnerLook = Time.time + 2f;
            pendingOwners.RemoveAll(p => p == null || p.isEnemyDead || p.ServerFindOwner());
        }
    }
}
