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

        /// <summary>(#244) A creeper looked at (its own kind of mob: flint and steel lights it).</summary>
        static CreeperAI AimedCreeper(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var d = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (d != null && d.mainScript is CreeperAI c && !c.isEnemyDead) return c;
            }
            return null;
        }

        public const string EggPrefix = "spawn_egg:";
        static string HeldKey(PlayerControllerB p)
        {
            var h = p.currentlyHeldObjectServer;
            // (a stack just used up (the last carpet put on a llama) is gone, though its object may linger a moment)
            if (h == null || !p.isHoldingObject || (h is StackItem st0 && st0.Count <= 0)) return null;
            return h is SpawnEggItem egg ? EggPrefix + egg.EnemyHash : (h as StackItem)?.ItemKey ?? (h as ToolItem)?.ItemKey;
        }

        // (after the horse's and the pets' own [E]: they keep theirs)
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static bool OnInteract(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !Builder.CanAct(p)) return true;
            var m = Aimed(p);
            if (m == null || m.UseTip(HeldKey(p)) == null)
            {
                var c = AimedCreeper(p);
                if (c == null || c.UseTip(HeldKey(p)) == null) return true;
                BlockNet.RequestAnimal(c.NetworkObjectId);
                return false;
            }
            BlockNet.RequestAnimal(m.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Tip(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || p.cursorTip == null || !Builder.CanAct(p)) return;
            var m = Aimed(p);
            var tip = m != null ? m.UseTip(HeldKey(p)) : AimedCreeper(p)?.UseTip(HeldKey(p));
            if (tip != null) p.cursorTip.text = tip + " : [E]";
        }

        /// <summary>Host: a request came in for this animal.</summary>
        public static void ServerUse(ulong sender, ulong netId)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || !Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var no)) return;
            var cr = no.GetComponent<CreeperAI>();
            if (cr != null) { if (!cr.isEnemyDead && Vector3.Distance(p.transform.position, cr.transform.position) <= 5f) cr.ServerUse(p, p.currentlyHeldObjectServer); return; }
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
            var born = RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, baby);
            // (#244: a puppy is its parents' owner's)
            if (a is PetAI pa && pa.Tamed && born.TryGet(out Unity.Netcode.NetworkObject pno) && pno.GetComponent<PetAI>() is PetAI pup) pup.ServerTame(pa.Owner, quiet: true);
            // (#244: a foal's speed, jump and health from its parents', Minecraft's)
            if (a is HorseAI ha && b is HorseAI hb && born.TryGet(out Unity.Netcode.NetworkObject hno) && hno.GetComponent<HorseAI>() is HorseAI foal) foal.ServerInherit(ha, hb);
            // (#244: a young llama's strength and health from its parents')
            if (a is LlamaAI la && b is LlamaAI lb && born.TryGet(out Unity.Netcode.NetworkObject lno) && lno.GetComponent<LlamaAI>() is LlamaAI cria) cria.ServerInherit(la, lb);
            // (#244: a lamb's colour from its parents', Minecraft's)
            if (a is SheepAI sa && b is SheepAI sb && born.TryGet(out Unity.Netcode.NetworkObject bno) && bno.GetComponent<SheepAI>() is SheepAI lamb)
                lamb.LoadState("0," + SheepAI.OffspringColor(sa.Color, sb.Color));
            BlockNet.ServerSound(at + Vector3.up, "orb", 0.6f, 1.2f);
            if (who != null) BlockNet.ServerXp(who.actualClientId, Random.Range(1, 8), at);
            Born++;
        }
    }
}
