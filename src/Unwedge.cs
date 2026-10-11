using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A safety net for #64 (players stuck fast): when you're trying to move and haven't for a second and a half, and your
    /// body is caught inside something (a pipe, the lamp bars in the factory's halls, a block), you're moved to the nearest
    /// spot where you fit. The game's own movement can't get a body out of a collider it's inside; this does what /unstuck
    /// does, by itself. Being pressed against a wall doesn't count: only a real overlap.
    /// </summary>
    public class Unwedge : MonoBehaviour
    {
        public const float After = 1.5f;
        float still;
        Vector3 last;
        public static int Freed; // (dev/tests)

        void Update()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || p.isPlayerDead || !p.isPlayerControlled || p.inSpecialInteractAnimation || p.isClimbingLadder
                || p.inAnimationWithEnemy != null || p.isTypingChat || p.inTerminalMenu || CreativeFlight.Flying) { still = 0f; return; }
            var pos = p.transform.position;
            bool pushing = p.moveInputVector.sqrMagnitude > 0.25f;
            if (!pushing || (pos - last).sqrMagnitude > 0.0025f) { still = 0f; last = pos; return; }
            still += Time.deltaTime;
            if (still < After) return;
            still = 0f;
            if (Commands.BodyFits(pos, p, 0.03f)) return; // (just against a wall)
            var spot = Commands.NearestFit(pos, p);
            if (spot == null) { Plugin.Log.LogInfo($"[unwedge] caught at {pos}, no free spot near"); return; }
            Plugin.Log.LogInfo($"[unwedge] caught at {pos}: moved to {spot.Value}");
            p.TeleportPlayer(spot.Value);
            p.ResetFallGravity();
            last = spot.Value;
            Freed++;
        }
    }
}
