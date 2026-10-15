using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Walking straight up stairs (#50), like Minecraft: a stair's half step is 0.7 m, more than the game lets a player
    /// step up, so while the local player is up against stairs their step height is raised to clear it (and set back after).
    /// </summary>
    public static class StairsStep
    {
        static float normal = -1f;
        static float nextCheck;
        static bool near;
        public static bool Near => near;
        public static float DevWant = -1f;
        public static bool HeadroomCheck = true; // (dev: stairstep room 0 brings the old way back)
        static float nextRoom, room = 99f;
        static readonly RaycastHit[] roomHits = new RaycastHit[8];

        /// <summary>How far the capsule could rise before its top hits something solid: over you and a little ahead the way
        /// you're going (the lintel's in front of you as you reach the doorway).</summary>
        static float Room(GameNetcodeStuff.PlayerControllerB p, CharacterController cc, float upTo)
        {
            var v = p.thisController.velocity; v.y = 0f;
            var ahead = v.sqrMagnitude > 0.04f ? v.normalized : p.transform.forward;
            float best = upTo;
            int mask = (1 << BlockWorld.SolidLayer) | StartOfRound.Instance.collidersAndRoomMaskAndDefault;
            foreach (var off in new[] { Vector3.zero, ahead * (cc.radius + 0.2f) })
            {
                var top = p.transform.position + cc.center + Vector3.up * (cc.height * 0.5f - cc.radius) + off;
                int n = Physics.SphereCastNonAlloc(top, cc.radius * 0.9f, Vector3.up, roomHits, upTo, mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = roomHits[i].collider;
                    if (c == null || c.transform.IsChildOf(p.transform) || roomHits[i].distance <= 0f) continue;
                    best = Mathf.Min(best, roomHits[i].distance);
                }
            }
            return best;
        }
        static readonly Collider[] hits = new Collider[16];

        /// <summary>Local player, every frame (from Builder).</summary>
        public static void Tick(GameNetcodeStuff.PlayerControllerB p)
        {
            var cc = p != null ? p.thisController : null;
            if (cc == null) return;
            if (normal < 0f) normal = cc.stepOffset;
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.1f;
                near = false;
                // stairs at your feet or just ahead
                var feet = p.transform.position + Vector3.up * 0.35f;
                int n = Physics.OverlapSphereNonAlloc(feet + p.transform.forward * 0.4f, 0.75f, hits, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n && !near; i++)
                {
                    var br = hits[i].GetComponentInParent<BlockRef>();
                    var bi = br != null ? BlockWorld.Instance?.Get(br.Key) : null;
                    if (bi != null && (bi.Data.Def.Shape == BlockShape.Stairs || bi.Data.Def.Shape == BlockShape.Slab && (bi.Data.State & 3) == 0)) near = true;
                }
            }
            float want = near ? Mathf.Max(normal, DevWant > 0f ? DevWant : BlockWorld.S * 0.5f + 0.08f) : normal;
            // (#215) the controller steps by lifting the whole capsule by the step height and then moving on, also when it
            // just brushes an edge: under a lintel (a two-high doorway leaves 0.3 m over your head) that lift hit it, and
            // only crouching got you in (from the side with stairs: the step raised for them). Never more than the room
            // above you, here and just ahead.
            if (HeadroomCheck && Time.time >= nextRoom)
            {
                nextRoom = Time.time + 0.1f;
                room = Room(p, cc, want + 0.1f);
            }
            if (HeadroomCheck && room < want + 0.05f) want = Mathf.Max(0.05f, room - 0.05f);
            if (!Mathf.Approximately(cc.stepOffset, want) && (cc.isGrounded || !near || want < cc.stepOffset)) cc.stepOffset = want;
        }
    }
}
