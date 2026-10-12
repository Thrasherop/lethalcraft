using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The Nether and the moon's void.
    /// - A moon's out-of-bounds trigger (the game's OutOfBoundsTrigger, under the map to catch whoever falls off it) can
    ///   reach down to the Nether's depth: Kast's does. The game kills a player who touches one while "inside" (and the
    ///   Nether counts as inside), so arriving in the Nether on Kast killed you at once. Inside the Nether those triggers
    ///   are ignored, for players and for bodies (which the game would otherwise throw back up to where they fell from).
    /// - Falling out of the bottom of the Nether (digging through its floor) is the real void: like falling off a moon,
    ///   it puts you back at the ship instead of killing you.
    /// </summary>
    [HarmonyPatch]
    public static class NetherVoid
    {
        public static int Ignored, Rescued; // (dev/tests)

        [HarmonyPatch(typeof(OutOfBoundsTrigger), "OnTriggerEnter"), HarmonyPrefix]
        static bool TriggerEnter(Collider other)
        {
            if (other == null || NetherFortress.Root == null || !NetherFortress.Contains(other.transform.position)) return true;
            if (!other.CompareTag("Player") && !other.tag.StartsWith("PlayerRagdoll")) return true;
            Ignored++;
            return false;
        }

        /// <summary>Every frame for this machine's player: out of the bottom of the Nether, back to the ship.</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (p == null || p.isPlayerDead || !p.isPlayerControlled || NetherFortress.Root == null) return;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase) return;
            float y = p.transform.position.y;
            if (y >= NetherFortress.Depth - 40f || y < -1000f) return;
            Rescued++;
            Plugin.Log.LogInfo($"[nether] {p.playerUsername} fell out of the Nether at {p.transform.position}: back to the ship");
            p.ResetFallGravity();
            p.TeleportPlayer(sor.outsideShipSpawnPosition.position);
            p.isInsideFactory = false;
            BlockNet.RequestInside(false);
            foreach (var it in p.ItemSlots) if (it != null) it.isInFactory = false;
        }

        /// <summary>(dev) the moon's out-of-bounds triggers: where they are, how big, whether one reaches the Nether.</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder($"ignored={Ignored} rescued={Rescued} ");
            // where the items are: in the Nether, by the ship, elsewhere
            int inNether = 0, byShip = 0, other = 0; var where = new System.Text.StringBuilder();
            var ship = StartOfRound.Instance != null ? StartOfRound.Instance.elevatorTransform.position : Vector3.zero;
            foreach (var g in Object.FindObjectsOfType<GrabbableObject>())
            {
                if (g.isHeld || g.isInShipRoom) continue;
                var at = g.transform.position;
                if (NetherFortress.Contains(at)) inNether++; else if (Vector3.Distance(at, ship) < 25f) byShip++; else { other++; if (other <= 12) where.Append($"{g.itemProperties?.itemName}@{at.x:F0},{at.y:F0},{at.z:F0} "); }
            }
            sb.Append($"items: nether={inNether} byShip={byShip} elsewhere={other} ({where}) ");
            foreach (var t in Object.FindObjectsOfType<OutOfBoundsTrigger>())
            {
                var c = t.GetComponent<Collider>();
                if (c == null) { sb.Append($"[{t.name} no collider] "); continue; }
                var b = c.bounds;
                bool nether = b.max.y > NetherFortress.Depth - 40f && b.min.y < NetherFortress.Depth + 60f;
                sb.Append($"[{t.name} min={b.min} max={b.max} nether={nether} enabled={c.enabled && t.gameObject.activeInHierarchy}] ");
            }
            return sb.ToString();
        }
    }
}
