using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// #210: tamed pets (and golems the crew built) in the ship stay with it. Before, they were noted as the ship left,
    /// stood still in the air while it flew off ("left behind like any other mob"), were deleted with the round's
    /// monsters, and a new one (a new colour) appeared at the next landing; none in orbit. Now, on every machine, those
    /// aboard when the ship starts to leave keep their place in the ship while it isn't on the ground (takeoff, orbit,
    /// landing), their AI paused, and the round's end leaves them be. On the next landing they step out onto the moon.
    /// </summary>
    [HarmonyPatch]
    public static class ShipPassengers
    {
        public static bool Enabled = true; // (dev: shippets 0 brings the old despawn-and-respawn back)

        class Rider { public EnemyAI E; public Vector3 Local; public Quaternion Rot; }
        static readonly List<Rider> riders = new List<Rider>();
        public static int Count => riders.Count;
        static float nextBoard;
        public static string Describe() => $"riding={riders.Count} " + string.Join(",", riders.Where(r => r.E != null).Select(r => r.E.enemyType.enemyName + "@" + r.E.transform.position.ToString("F1")));

        // (#227: what the ship keeps, ShipMobs.Keeps: animals, pets, golems, boats, minecarts, named mobs)
        static bool Rides(EnemyAI e) => ShipMobs.Keeps(e) && BlockWorld.InShip(e.transform.position);

        /// <summary>(#227) Every machine: one more rider, brought back aboard from the save, held in its place till landing.</summary>
        public static void Aboard(EnemyAI e)
        {
            if (!Enabled || e == null || riders.Any(r => r.E == e)) return;
            var ship = StartOfRound.Instance.elevatorTransform;
            riders.Add(new Rider { E = e, Local = ship.InverseTransformPoint(e.transform.position), Rot = Quaternion.Inverse(ship.rotation) * e.transform.rotation });
        }

        /// <summary>Every machine: the ship starts to leave: who rides along.</summary>
        [HarmonyPatch(typeof(StartOfRound), "ShipLeave"), HarmonyPrefix]
        static void Board()
        {
            if (!Enabled) return;
            var ship = StartOfRound.Instance.elevatorTransform;
            riders.Clear();
            foreach (var e in Object.FindObjectsOfType<EnemyAI>())
                if (Rides(e)) riders.Add(new Rider { E = e, Local = ship.InverseTransformPoint(e.transform.position), Rot = Quaternion.Inverse(ship.rotation) * e.transform.rotation });
            if (riders.Count > 0) Plugin.Log.LogInfo($"[pets] {riders.Count} ride with the ship: {Describe()}");
        }

        /// <summary>Every machine, every frame after the ship's animation moved it.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Ride(PlayerControllerB __instance)
        {
            if (__instance != GameNetworkManager.Instance?.localPlayerController) return;
            var sor = StartOfRound.Instance;
            if (sor == null) return;
            // (#227: in orbit, what's aboard rides even if this machine never saw the ship leave: brought back from the
            // save, or this player joined in orbit)
            if (riders.Count == 0 && sor.inShipPhase && !sor.shipIsLeaving && Time.time >= nextBoard) { nextBoard = Time.time + 2f; Board(); }
            if (riders.Count == 0) return;
            riders.RemoveAll(r => r.E == null || r.E.isEnemyDead);
            if (sor.shipHasLanded && !sor.inShipPhase && !sor.shipIsLeaving) { Release(); return; }
            var ship = sor.elevatorTransform;
            foreach (var r in riders)
            {
                if (r.E.enabled) { r.E.enabled = false; if (r.E.agent != null) r.E.agent.enabled = false; }
                if (r.E is ParrotAI pa && pa.Perched) { pa.OnShoulder(); continue; } // (on its owner's shoulder, who's in the ship)
                r.E.transform.SetPositionAndRotation(ship.TransformPoint(r.Local), ship.rotation * r.Rot);
                r.E.serverPosition = r.E.transform.position;
            }
        }

        /// <summary>Landed: back to their own business, on this moon's ground.</summary>
        static void Release()
        {
            int n = 0;
            foreach (var r in riders)
            {
                var e = r.E;
                if (e == null) continue;
                e.enabled = true;
                e.serverPosition = e.transform.position;
                // (#227: its AI nodes were the old moon's, gone with it: this moon's; a mob in the Nether sets its own)
                try { e.allAINodes = GameObject.FindGameObjectsWithTag(e.isOutside ? "OutsideAINode" : "AINode"); } catch { }
                bool perched = (e is ParrotAI pa && pa.Perched) || (e is MinecraftMobAI om && om.OffNavMesh); // (#227: a boat, a minecart: no agent)
                if (e.IsOwner && e.agent != null && !perched && NavMesh.SamplePosition(e.transform.position, out var h, 4f, NavMesh.AllAreas))
                {
                    e.agent.enabled = true;
                    e.agent.Warp(h.position);
                }
                if (BlockNet.IsServer && RoundManager.Instance != null && !RoundManager.Instance.SpawnedEnemies.Contains(e)) RoundManager.Instance.SpawnedEnemies.Add(e);
                n++;
            }
            riders.Clear();
            if (n > 0) Plugin.Log.LogInfo($"[pets] {n} landed with the ship");
        }

        // ------------------------------------------------------------------ the round's end deletes every monster but these
        static readonly List<GameObject> hidden = new List<GameObject>();

        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.UnloadSceneObjectsEarly)), HarmonyPrefix]
        static void Keep()
        {
            hidden.Clear();
            Plugin.Log.LogInfo($"[pets] the round's end: {Describe()}");
            // (#232) a tamed pet not riding along is lost with the round's monsters: its owner hears of it
            if (BlockNet.IsServer)
                foreach (var pet in Object.FindObjectsOfType<PetAI>())
                    if (pet != null && pet.Tamed && !pet.isEnemyDead && !riders.Exists(r => r.E == pet)) PetDeaths.ServerLeftBehind(pet);
            if (!Enabled) return;
            // (UnloadSceneObjectsEarly, 1.5 s into the takeoff, finds what to delete with FindObjectsOfType, which skips what's switched off: off for that moment)
            foreach (var r in riders)
                if (r.E != null && r.E.gameObject.activeSelf) { r.E.gameObject.SetActive(false); hidden.Add(r.E.gameObject); }
        }

        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.UnloadSceneObjectsEarly)), HarmonyFinalizer]
        static void Kept()
        {
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            if (hidden.Count > 0) Plugin.Log.LogInfo($"[pets] {hidden.Count} kept through the round's end");
            hidden.Clear();
            if (RoundManager.Instance != null && BlockNet.IsServer)
                foreach (var r in riders)
                    if (r.E != null && !RoundManager.Instance.SpawnedEnemies.Contains(r.E)) RoundManager.Instance.SpawnedEnemies.Add(r.E);
        }

        public static void ResetSession() { riders.Clear(); hidden.Clear(); }
    }
}
