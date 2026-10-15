using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
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
            // (nearest first: RaycastAll's hits come in no order, and a mob behind the one you look at, the trader's llama,
            // could take the [E])
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
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
            float d = m != null ? Vector3.Distance(p.transform.position, m.transform.position) : -1f;
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] [E] on {(m != null ? m.enemyType.enemyName : "?")} from {p.playerUsername} at {d:F1} m{(m == null || m.isEnemyDead || d > 5f ? ": refused" : "")}");
            if (m == null || m.isEnemyDead || d > 5f) return;
            m.ServerUse(p, p.currentlyHeldObjectServer);
        }

        /// <summary>Host: [E] on a fence (#145): the animals this player leads are tied to it; with none led, the ones tied
        /// there come back onto this player's lead (Minecraft's).</summary>
        public static void ServerFence(ulong sender, BlockKey fence)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null) return;
            var mobs = RoundManager.Instance.SpawnedEnemies.OfType<MinecraftMobAI>().Where(m => m != null && !m.isEnemyDead).ToList();
            var led = mobs.Where(m => m.LeashedTo == sender).ToList();
            var w = BlockWorld.Instance;
            if (led.Count > 0)
            {
                foreach (var m in led) { m.LeashedTo = MinecraftMobAI.FenceLeash; m.TiedFence = fence; BlockNet.ServerMobState(m.NetworkObjectId, 201, 0); }
                Tied += led.Count;
            }
            else
                foreach (var m in mobs.Where(m => m.LeashedTo == MinecraftMobAI.FenceLeash && m.TiedFence.Equals(fence)))
                {
                    m.LeashedTo = sender;
                    BlockNet.ServerMobState(m.NetworkObjectId, 200, (byte)p.playerClientId);
                }
            BlockNet.ServerSound(w.WorldCenter(fence), "lead.tie", 0.7f, 1f);
        }

        /// <summary>The tip for [E] on a fence: tie the animals you lead, or take back the ones tied there.</summary>
        public static string FenceTip(BlockKey fence)
        {
            if (RoundManager.Instance == null || NetworkManager.Singleton == null) return null;
            ulong me = NetworkManager.Singleton.LocalClientId;
            var mobs = RoundManager.Instance.SpawnedEnemies.OfType<MinecraftMobAI>().Where(m => m != null && !m.isEnemyDead);
            if (mobs.Any(m => m.LeashedTo == me)) return "Tie the lead : [E]";
            var w = BlockWorld.Instance;
            if (w != null && mobs.Any(m => m.LeashedTo == MinecraftMobAI.FenceLeash && Vector3.Distance(m.transform.position, w.WorldCenter(fence)) < 6f * BlockWorld.S)) return "Take the lead : [E]";
            return null;
        }
        public static int Tied; // (dev/tests)

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
