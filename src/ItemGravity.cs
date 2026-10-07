using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Items in Lethal Company don't use physics: they fall once when dropped and then stay put. When a block or a
    /// piece of ground under a resting item disappears, re-run the game's own fall so the item drops onto whatever is
    /// below now. Runs on every client (blocks and cuts are applied everywhere), a frame later so destroyed colliders are gone.
    /// </summary>
    public static class ItemGravity
    {
        const int FloorMask = 268437760; // the mask GrabbableObject.FallToGround uses (Room, Colliders, Railing)
        static readonly List<(Bounds b, int frame)> pending = new List<(Bounds, int)>();

        /// <summary>Something solid in this world-space box went away.</summary>
        public static void Removed(Bounds b)
        {
            if (pending.Count > 256) pending.RemoveRange(0, 128);
            pending.Add((b, Time.frameCount));
        }

        public static void Tick()
        {
            if (pending.Count == 0) return;
            var ready = new List<Bounds>();
            for (int i = pending.Count - 1; i >= 0; i--)
                if (Time.frameCount > pending[i].frame + 1) { ready.Add(pending[i].b); pending.RemoveAt(i); }
            if (ready.Count == 0) return;
            foreach (var item in Object.FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || item.isHeld || item.isHeldByEnemy || item.parentObject != null || item.isPocketed || item.itemProperties == null) continue;
                var p = item.transform.position;
                bool near = false;
                foreach (var b in ready)
                {
                    // the item rested on (or just above) the removed space
                    if (p.x >= b.min.x - 0.3f && p.x <= b.max.x + 0.3f && p.z >= b.min.z - 0.3f && p.z <= b.max.z + 0.3f && p.y >= b.min.y - 0.2f && p.y <= b.max.y + 1.5f)
                    { near = true; break; }
                }
                if (!near) continue;
                Settle(item);
            }
        }

        static void Settle(GrabbableObject item)
        {
            var start = item.transform.position + Vector3.up * 0.15f;
            if (!Physics.Raycast(start, Vector3.down, out var hit, 80f, FloorMask, QueryTriggerInteraction.Ignore)) return;
            var floor = hit.point + item.itemProperties.verticalOffset * Vector3.up;
            if (item.transform.position.y - floor.y < 0.08f) return; // still supported
            item.startFallingPosition = item.transform.parent != null ? item.transform.parent.InverseTransformPoint(item.transform.position) : item.transform.position;
            item.hasHitGround = false;
            item.reachedFloorTarget = false;
            item.FallToGround();
        }
    }
}
