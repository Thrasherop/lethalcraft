using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Lava (#32): pockets of it deep underground (Ground opens them up when they come to light). In it, you burn fast:
    /// get out within a couple of seconds or die. A block placed into lava fills it in.
    /// </summary>
    public static class LavaBurn
    {
        public static int DamagePerHalfSecond = 15;
        static float next;

        /// <summary>Local player, every frame.</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (p == null || p.isPlayerDead || !p.isPlayerControlled || Time.time < next) return;
            var world = BlockWorld.Instance;
            if (world == null || StartOfRound.Instance == null || StartOfRound.Instance.inShipPhase) return;
            bool inLava = false;
            foreach (var h in new[] { 0.2f, 1.1f })
            {
                var k = Ground.KeyOf(Ground.CellOf(p.transform.position + Vector3.up * h));
                if (world.DefAt(k) == Blocks.Lava) { inLava = true; break; }
            }
            if (!inLava) return;
            next = Time.time + 0.5f;
            if (GameModes.IsCreative(p)) return;
            Sounds.Play("fire", p.transform.position, 0.8f, Random.Range(0.9f, 1.1f));
            p.DamagePlayer(DamagePerHalfSecond, true, true, CauseOfDeath.Burning);
        }
    }
}
