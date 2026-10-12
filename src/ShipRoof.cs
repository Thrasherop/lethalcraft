using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Digging the ship's roof and its antenna (the light post on top), #124: off unless the host turns it on
    /// (Blocks.AllowDiggingShipRoof). Only while the ship is landed, and only the hull above the room's ceiling: its
    /// walls and floor stay. What's dug is ship plating, which drops nothing. The hull is patched up as the ship takes off.
    /// </summary>
    [HarmonyPatch]
    public static class ShipRoof
    {
        /// <summary>The host's rule (clients get it with the other rules).</summary>
        public static bool HostAllows;
        public static bool On => BlockNet.IsServer ? Plugin.DigShipRoof.Value : HostAllows;

        static bool Landed
        {
            get { var s = StartOfRound.Instance; return s != null && !s.inShipPhase && s.shipHasLanded && !s.shipIsLeaving; }
        }

        /// <summary>The hull (its outside is the roof) or a piece of the light post on top of it.</summary>
        public static bool IsPart(GameObject go)
        {
            var sor = StartOfRound.Instance;
            if (go == null || sor == null || sor.elevatorTransform == null || !go.transform.IsChildOf(sor.elevatorTransform)) return false;
            if (go.name == "ShipInside" && go.GetComponent<MeshCollider>() != null) return true;
            for (var t = go.transform; t != null && t != sor.elevatorTransform; t = t.parent)
                if (t.name == "ShipLightsPost") return true;
            return false;
        }

        public static bool Diggable(GameObject go) => On && Landed && IsPart(go);

        /// <summary>The light post's pieces with nothing to collide (its lamps and fittings), while digging there is on.</summary>
        public static IEnumerable<MeshRenderer> Fittings()
        {
            if (!On || !Landed || Post == null) yield break;
            foreach (var r in Post.GetComponentsInChildren<MeshRenderer>())
                if (r.GetComponent<Collider>() == null) yield return r;
        }

        public static IEnumerable<MeshCollider> PostColliders() => Post != null ? Post.GetComponentsInChildren<MeshCollider>(true) : Enumerable.Empty<MeshCollider>();

        static Transform post;
        static Transform Post
        {
            get
            {
                var sor = StartOfRound.Instance;
                if (post == null && sor != null && sor.elevatorTransform != null)
                    post = sor.elevatorTransform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ShipLightsPost");
                return post;
            }
        }

        /// <summary>The ship's room (its ceiling's underside is the top, about 4.5 m over the floor).</summary>
        static Bounds Room()
        {
            var sor = StartOfRound.Instance;
            var b = sor.shipInnerRoomBounds != null ? sor.shipInnerRoomBounds.bounds : default;
            if (b.size.y > 1f) return b;
            var e = sor.elevatorTransform.position; // (fallback: the room's place on a landed ship)
            return new Bounds(e + new Vector3(1.4f, 2.3f, -6.7f), new Vector3(17.5f, 5.3f, 7f));
        }

        /// <summary>
        /// A cell of a ship part that may go: the light post anywhere; the hull only over the room: above its ceiling
        /// and, where the cell reaches down past the ceiling, over the room's floor (the walls and door frame stay whole).
        /// </summary>
        public static bool CellOk(GameObject go, Vector3 center)
        {
            if (go == null || go.name != "ShipInside") return true;
            var room = Room(); float h = Plugin.S * 0.5f;
            if (center.y - h >= room.max.y - 0.05f) return true;
            if (center.y <= room.max.y) return false;
            return center.x - h >= room.min.x && center.x + h <= room.max.x && center.z - h >= room.min.z && center.z + h <= room.max.z;
        }

        // ------------------------------------------------------------------ patched up as the ship takes off
        [HarmonyPatch(typeof(StartOfRound), "ShipLeave"), HarmonyPostfix]
        static void OnShipLeave()
        {
            try
            {
                if (BlockNet.IsServer && BlockWorld.Instance != null)
                {
                    // (the plating blocks the digging left behind: in the moon's frame, they'd hang in the air)
                    var ops = BlockWorld.Instance.Blocks.Values.Where(b => b.Key.Frame == 0 && b.Data.Def == Blocks.ShipPlating)
                        .Select(b => Op.Remove(b.Key, false)).ToList();
                    if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
                }
                int n = TerrainCarver.RestoreShip();
                if (n > 0) Plugin.Log.LogInfo($"[ship] the roof patched up ({n} parts)");
            }
            catch (System.Exception e) { Plugin.Log.LogError("Ship roof repair: " + e); }
        }
    }
}
