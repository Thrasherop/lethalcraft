using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The moon's own water (its swimming volumes: March's lake, rivers) and ours: how deep a point is under it. (#247:
    /// the game's navmesh carries on along the bed of March's lake, as ordinary walkable ground, a couple of metres under
    /// the surface near its shores: our mobs strolled about down there.)
    /// </summary>
    public static class MoonWater
    {
        static readonly List<Collider> volumes = new List<Collider>();
        static float nextLook;

        /// <summary>Metres of water over the point (0: none).</summary>
        public static float Depth(Vector3 p)
        {
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 5f;
                volumes.Clear();
                foreach (var q in Object.FindObjectsOfType<QuicksandTrigger>())
                {
                    var c = q.GetComponent<Collider>();
                    if (q.isWater && c != null) volumes.Add(c);
                }
            }
            float depth = 0f;
            foreach (var c in volumes)
            {
                if (c == null || !c.enabled || !c.bounds.Contains(p)) continue;
                if ((c.ClosestPoint(p) - p).sqrMagnitude > 1e-4f) continue; // (in its box, not in it)
                depth = Mathf.Max(depth, c.bounds.max.y - p.y);
            }
            // ours: a water block there (a pool, a farm's channel)
            if (depth <= 0f && WaterSwim.WaterCellAt(p + Vector3.up * 0.2f * BlockWorld.S, out _)) depth = 0.5f * BlockWorld.S;
            return depth;
        }

        /// <summary>Minecraft's land mobs stroll to dry ground only (WaterAvoidingRandomStrollGoal: LandRandomPos).</summary>
        public static bool Wet(Vector3 p) => Depth(p) > 0.3f;
    }
}
