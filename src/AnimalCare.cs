using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// [E] on one of the mod's animals with something in hand (#145, #148): the host decides what it does (MinecraftMobAI
    /// .ServerUse): food to breed two of them into a baby, shears on a sheep... Babies grow up after a while. Small
    /// state the clients need (a sheep sheared or not) goes out as a MobState message (MinecraftMobAI.ReceiveState).
    /// </summary>
    [HarmonyPatch]
    public static class AnimalCare
    {
        /// <summary>Seconds a baby takes to grow up (Minecraft: 20 minutes; a round here is shorter).</summary>
        public static float GrowSeconds = 300f;
        public static readonly Dictionary<EnemyType, EnemyType> BabyOf = new Dictionary<EnemyType, EnemyType>(), AdultOf = new Dictionary<EnemyType, EnemyType>();
        public static int Born; // (dev/tests)
        static Material leashMat;
        public static Material LeashMaterial
        {
            get
            {
                if (leashMat == null) { leashMat = new Material(Shader.Find("HDRP/Unlit")); leashMat.SetColor("_UnlitColor", new Color(0.4f, 0.27f, 0.13f)); }
                return leashMat;
            }
        }

        public static MinecraftMobAI Aimed(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide))
            {
                var d = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (d != null && d.mainScript is MinecraftMobAI m && !m.isEnemyDead) return m;
            }
            return null;
        }

        static string HeldKey(PlayerControllerB p) => (p.currentlyHeldObjectServer as StackItem)?.ItemKey ?? (p.currentlyHeldObjectServer as ToolItem)?.ItemKey;

        // (after the horse's and the pets' own [E]: they keep theirs)
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static bool OnInteract(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !Builder.CanAct(p)) return true;
            var m = Aimed(p);
            if (m == null || m.UseTip(HeldKey(p)) == null) return true;
            BlockNet.RequestAnimal(m.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Tip(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || p.cursorTip == null || !Builder.CanAct(p)) return;
            var m = Aimed(p);
            var tip = m != null ? m.UseTip(HeldKey(p)) : null;
            if (tip != null) p.cursorTip.text = tip + " : [E]";
        }

        /// <summary>Host: a request came in for this animal.</summary>
        public static void ServerUse(ulong sender, ulong netId)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || !Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var no)) return;
            var m = no.GetComponent<MinecraftMobAI>();
            if (m == null || m.isEnemyDead || Vector3.Distance(p.transform.position, m.transform.position) > 5f) return;
            m.ServerUse(p, p.currentlyHeldObjectServer);
        }

        /// <summary>Host: a baby for these two (between them), and the experience Minecraft gives for it.</summary>
        public static void ServerBreed(MinecraftMobAI a, MinecraftMobAI b, PlayerControllerB who)
        {
            if (!BabyOf.TryGetValue(a.enemyType, out var baby)) return;
            var at = Vector3.Lerp(a.transform.position, b.transform.position, 0.5f);
            MobSpawns.DevSpawnUntil = Time.time + 1f; // (not a natural spawn to be stopped)
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, baby);
            BlockNet.ServerSound(at + Vector3.up, "orb", 0.6f, 1.2f);
            if (who != null) BlockNet.ServerXp(who.actualClientId, Random.Range(1, 8));
            Born++;
        }
    }
}
