using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Creeper and TNT blasts hurt the way Minecraft's do (#105): damage falls off with distance over twice the
    /// explosion's power (in blocks), times how much of you the blast can see, then armor takes its share. An unarmored
    /// player within about two blocks of a creeper dies; three blocks away it's about eight hearts. The game's own
    /// landmine blast (used for the effect, the push and hurting monsters) killed only inside 0.55 of the radius and did
    /// a flat 50 beyond it: a creeper a block away cost "4-5 hearts" (the owner, 1.25.0).
    /// </summary>
    [HarmonyPatch]
    public static class BlastDamage
    {
        /// <summary>While one of our blasts runs the game's explosion: its own kill/damage of the local player is skipped.</summary>
        public static bool Ours;
        public static int LastDamage = -1; public static float LastDistance = -1f; // (dev/tests)

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool NoGameKill(PlayerControllerB __instance) => !(Ours && __instance.IsOwner);

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool NoGameDamage(PlayerControllerB __instance) => !(Ours && __instance.IsOwner);

        /// <summary>Every machine, for its own player, after the blast: Minecraft's explosion damage.</summary>
        public static void Apply(Vector3 center, float power)
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || p.isPlayerDead || !p.isPlayerControlled || power <= 0f) return;
            float s = BlockWorld.S;
            var feet = p.transform.position;
            float d = Vector3.Distance(center, feet + Vector3.up * (0.5f * s)) / s; // (blocks, to the body's middle)
            LastDistance = d;
            float reach = 2f * power;
            if (d >= reach) { LastDamage = 0; return; }
            // how much of you it sees: three lines, feet, middle, head
            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault & ~(1 << 3); // (not players)
            int seen = 0;
            foreach (float h in new[] { 0.15f, 0.5f * s, 1.6f })
                if (!Physics.Linecast(center, feet + Vector3.up * h, mask, QueryTriggerInteraction.Ignore)) seen++;
            float impact = (1f - d / reach) * (seen / 3f);
            if (impact <= 0f) { LastDamage = 0; return; }
            float mc = (impact * impact + impact) / 2f * 7f * reach + 1f; // Minecraft's, in half-hearts (20 = dead)
            int dmg = Mathf.RoundToInt(mc * 5f);                          // (100 health here)
            LastDamage = dmg;
            p.DamagePlayer(dmg, true, true, CauseOfDeath.Blast, 0, false, (feet - center).normalized * 10f);
        }
    }
}
