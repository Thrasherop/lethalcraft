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
            if (!Mathf.Approximately(cc.stepOffset, want) && (cc.isGrounded || !near)) cc.stepOffset = want;
        }
    }
}
