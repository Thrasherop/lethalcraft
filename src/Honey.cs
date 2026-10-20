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
                    var ud = br != null ? BlockWorld.Instance?.DefAt(br.Key) : null;
                    now = ud == Blocks.Honey || ud == Blocks.Slime; // (#244: slime slows walking too, Minecraft's SlimeBlock.stepOn)
                    slime = ud == Blocks.Slime;
                }
                if (now && !on)
                {
                    normalSpeed = p.movementSpeed; normalJump = p.jumpForce;
                    p.movementSpeed = normalSpeed * SpeedFactor; p.jumpForce = normalJump * (slime ? 1f : JumpFactor);
                }
                else if (!now && on)
                {
                    if (normalSpeed > 0f) p.movementSpeed = normalSpeed;
                    if (normalJump > 0f) p.jumpForce = normalJump;
                }
                on = now;
            }
            Ice(p);
            HoneyWall(p);
        }

        static bool slime;
        static Vector3 slide;
        public static int IceFrames, WallSlides; // (dev/tests)

        static BlockDef Under(PlayerControllerB p)
        {
            if (p.thisController == null || !p.thisController.isGrounded) return null;
            if (!Physics.Raycast(p.transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)) return null;
            var br = hit.collider.GetComponent<BlockRef>();
            return br != null ? BlockWorld.Instance?.DefAt(br.Key) : null;
        }

        /// <summary>(#244) Minecraft's ice: its friction 0.98 (blue ice 0.989) against the ground's 0.6: you get going slowly and
        /// slide on after you stop.</summary>
        static void Ice(PlayerControllerB p)
        {
            var d = Under(p);
            bool ice = d != null && (d == Blocks.Ice || d == Blocks.PackedIce || d == Blocks.BlueIce);
            var walk = Bow.WalkOf(p);
            if (!ice) { slide = walk; return; }
            float k = Mathf.Pow((d == Blocks.BlueIce ? 0.989f : 0.98f) * 0.91f, Time.deltaTime * 20f);
            slide = slide * k + walk * (1f - k);
            p.externalForces += slide - walk;
            IceFrames++;
        }

        /// <summary>(#244) Minecraft's HoneyBlock: falling against the side of honey, you slide down it slowly (a block a
        /// second), and land without hurt.</summary>
        static void HoneyWall(PlayerControllerB p)
        {
            if (p.thisController == null || p.thisController.isGrounded || p.fallValue > -1f * BlockWorld.S) return;
            var c = p.transform.position + Vector3.up * 0.9f;
            foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                if (!Physics.Raycast(c, dir, out var hit, 0.55f * BlockWorld.S, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)) continue;
                var br = hit.collider.GetComponent<BlockRef>();
                if (br == null || BlockWorld.Instance?.DefAt(br.Key) != Blocks.Honey) continue;
                p.fallValue = Mathf.Max(p.fallValue, -1f * BlockWorld.S);
                p.fallValueUncapped = Mathf.Max(p.fallValueUncapped, -1f * BlockWorld.S);
                p.takingFallDamage = false;
                WallSlides++;
                return;
            }
        }
    }
}
