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
        /// <summary>
        /// What an item comes to rest on: the game's own drop mask (GetItemFloorPosition: Room, Colliders, Railing and
        /// Default). The game's FallToGround leaves Default out, so an item spawned or thrown over a Default-layer floor
        /// (the Company's ramp) fell through it to whatever was below, out of sight but still scannable (#78).
        /// </summary>
        public const int FloorMask = 268437761;
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

        // ------------------------------------------------------------------ riding blocks a piston moves
        static readonly List<GrabbableObject> resting = new List<GrabbableObject>();
        static readonly HashSet<GrabbableObject> movedThisFrame = new HashSet<GrabbableObject>();
        static int restingFrame = -1;

        /// <summary>
        /// A block slid this frame (pushed or pulled by a piston): items resting on its top move with it, like things
        /// riding a moving block in Minecraft. Every client does this as it animates the block, so they agree.
        /// A piston head sliding out doesn't count (what stands on the piston's body stays; the head only pushes).
        /// </summary>
        public static void Carry(BlockInstance bi, Vector3 delta)
        {
            if (bi.Go == null || !bi.Data.Def.Solid || bi.Data.Def.Shape == BlockShape.PistonHead) return;
            if (restingFrame != Time.frameCount)
            {
                restingFrame = Time.frameCount;
                resting.Clear(); movedThisFrame.Clear();
                foreach (var item in Object.FindObjectsOfType<GrabbableObject>())
                    if (item != null && !item.isHeld && !item.isHeldByEnemy && !item.isPocketed && item.parentObject == null && item.itemProperties != null && item.hasHitGround)
                        resting.Add(item);
            }
            float S = Plugin.S;
            var before = bi.Go.transform.position - delta; // where the block was before this frame's step
            float top = before.y + S * 0.5f;
            foreach (var item in resting)
            {
                if (item == null || movedThisFrame.Contains(item)) continue;
                var p = item.transform.position;
                if (Mathf.Abs(p.x - before.x) > S * 0.5f + 0.05f || Mathf.Abs(p.z - before.z) > S * 0.5f + 0.05f) continue;
                if (p.y < top - 0.1f || p.y > top + 0.6f) continue;
                item.transform.position += delta;
                var parent = item.transform.parent;
                var local = parent != null ? parent.InverseTransformVector(delta) : delta;
                item.targetFloorPosition += local;
                item.startFallingPosition += local;
                movedThisFrame.Add(item);
            }
        }

        /// <summary>(#244) An item lying about put somewhere else (a fishing rod's pull): it falls from there.</summary>
        public static void MoveTo(GrabbableObject item, Vector3 to)
        {
            if (item == null || item.isHeld || item.isPocketed) return;
            item.transform.position = to;
            item.startFallingPosition = item.transform.parent != null ? item.transform.parent.InverseTransformPoint(to) : to;
            item.hasHitGround = false;
            item.reachedFloorTarget = false;
            item.FallToGround();
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

    /// <summary>
    /// #78: the mod's items (block drops, thrown stacks, everything spawned on the ground) land on what a dropped item
    /// would land on, Default-layer floors included, not just the layers the game's FallToGround looks at. The game's
    /// own items keep its behaviour.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.FallToGround))]
    static class FallOntoDefault
    {
        static void Postfix(GrabbableObject __instance, bool justSpawned)
        {
            if (justSpawned || __instance == null || __instance.itemProperties == null) return;
            var n = __instance.itemProperties.name;
            if (n == null || !n.StartsWith("LMC_")) return;
            var start = __instance.transform.position;
            if (!Physics.Raycast(start, Vector3.down, out var hit, 80f, ItemGravity.FloorMask, QueryTriggerInteraction.Ignore)) return;
            var floor = hit.point + __instance.itemProperties.verticalOffset * Vector3.up;
            var parent = __instance.transform.parent;
            var current = parent != null ? parent.TransformPoint(__instance.targetFloorPosition) : __instance.targetFloorPosition;
            if (floor.y <= current.y + 0.01f) return; // (the game's floor is the nearest one already)
            __instance.targetFloorPosition = parent != null ? parent.InverseTransformPoint(floor) : floor;
        }
    }
}
