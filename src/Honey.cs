using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Honey blocks underfoot (#23), Minecraft's: walking on one is slow and jumping off it is low. The local player's
    /// speed and jump are turned down while standing on honey and put back after.
    /// </summary>
    public static class HoneyFeet
    {
        public static float SpeedFactor = 0.4f, JumpFactor = 0.6f;
        static float normalSpeed = -1f, normalJump = -1f;
        static bool on;
        static float next;

        public static bool On => on;

        /// <summary>Local player, every frame (from Builder).</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (p == null) return;
            if (Time.time >= next)
            {
                next = Time.time + 0.1f;
                bool now = false;
                if (p.thisController != null && p.thisController.isGrounded
                    && Physics.Raycast(p.transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore))
                {
                    var br = hit.collider.GetComponent<BlockRef>();
                    now = br != null && BlockWorld.Instance?.DefAt(br.Key) == Blocks.Honey;
                }
                if (now && !on)
                {
                    normalSpeed = p.movementSpeed; normalJump = p.jumpForce;
                    p.movementSpeed = normalSpeed * SpeedFactor; p.jumpForce = normalJump * JumpFactor;
                }
                else if (!now && on)
                {
                    if (normalSpeed > 0f) p.movementSpeed = normalSpeed;
                    if (normalJump > 0f) p.jumpForce = normalJump;
                }
                on = now;
            }
        }
    }
}
