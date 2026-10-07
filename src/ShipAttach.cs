using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Structures attached to the ship travel with it. Blocks placed on the ship (inside, on the hull, on the roof,
    /// or on other ship blocks) already live in the ship's grid. At takeoff, any moon-grid structure that touches the
    /// ship or a ship block is converted into ship blocks, so a house built against the ship comes along.
    /// </summary>
    [HarmonyPatch]
    public static class ShipAttach
    {
        public static bool IsShipCollider(Collider c)
        {
            var sor = StartOfRound.Instance;
            if (c == null || sor == null || sor.elevatorTransform == null) return false;
            if (c.GetComponentInParent<BlockRef>() != null) return false;
            if (c.GetComponentInParent<GrabbableObject>() != null) return false;
            return c.transform.IsChildOf(sor.elevatorTransform);
        }

        [HarmonyPatch(typeof(StartOfRound), "ShipLeave"), HarmonyPrefix]
        static void OnShipLeave()
        {
            if (!BlockNet.IsServer) return;
            try { AbsorbAttachedStructures(); }
            catch (System.Exception e) { Plugin.Log.LogError("Ship attach failed: " + e); }
        }

        public static int AbsorbAttachedStructures()
        {
            var world = BlockWorld.Instance;
            if (world == null) return 0;
            var shipRoot = world.FrameRoot(1, true);
            if (shipRoot == null) return 0;
            float S = Plugin.S;

            var moon = world.Blocks.Values.Where(b => b.Key.Frame == 0 && b.Go != null && !IsNaturalVein(b)).ToList();
            if (moon.Count == 0) return 0;
            var shipCenters = world.Blocks.Values.Where(b => b.Key.Frame == 1 && b.Go != null).Select(b => b.Go.transform.position).ToList();

            // seeds: moon blocks touching ship geometry or a ship block
            var attached = new HashSet<BlockInstance>();
            var queue = new Queue<BlockInstance>();
            foreach (var b in moon)
            {
                var c = b.Go.transform.position;
                bool touch = shipCenters.Any(sc => (sc - c).sqrMagnitude < (S * 1.08f) * (S * 1.08f));
                if (!touch)
                {
                    var hits = Physics.OverlapBox(c, Vector3.one * (S * 0.56f), b.Go.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                    touch = hits.Any(IsShipCollider);
                }
                if (touch && attached.Add(b)) queue.Enqueue(b);
            }
            if (attached.Count == 0) return 0;

            // flood through touching moon blocks (works across sub-grids too)
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var cp = cur.Go.transform.position;
                foreach (var o in moon)
                {
                    if (attached.Contains(o)) continue;
                    if ((o.Go.transform.position - cp).sqrMagnitude < (S * 1.08f) * (S * 1.08f)) { attached.Add(o); queue.Enqueue(o); }
                }
            }

            // convert into ship-grid keys
            var shipYOffs = world.YOffsInUse(1).ToList();
            var ops = new List<Op>();
            var heads = new List<Op>();
            var taken = new HashSet<BlockKey>(world.Blocks.Keys.Where(k => k.Frame == 1));
            foreach (var b in attached)
            {
                var local = shipRoot.InverseTransformPoint(b.Go.transform.position) / S; // block center in ship cells
                float yBottom = local.y - 0.5f;
                int cy = Mathf.FloorToInt(yBottom);
                int yoff = Mathf.RoundToInt((yBottom - cy) * 1000f);
                foreach (var e in shipYOffs.Concat(new short[] { }))
                {
                    int d = e - yoff; if (d > 500) d -= 1000; if (d < -500) d += 1000;
                    if (Mathf.Abs(d) <= 80) { yoff += d; break; }
                }
                if (yoff < 0) { yoff += 1000; cy--; }
                if (yoff >= 1000) { yoff -= 1000; cy++; }
                var key = new BlockKey(1, (short)yoff, new Vector3Int(Mathf.FloorToInt(local.x), cy, Mathf.FloorToInt(local.z)));
                if (!taken.Add(key)) continue;
                var d0 = b.Data;
                var worldDir = Faces.Dir[d0.Facing];
                var localDir = shipRoot.InverseTransformDirection(new Vector3(worldDir.x, worldDir.y, worldDir.z));
                d0.Facing = Faces.FromVector(localDir);
                if (d0.Def == Blocks.TNT) d0.State = 0;
                ops.Add(Op.Remove(b.Key, false));
                var set = Op.Set(key, d0);
                if (d0.Def.Shape == BlockShape.PistonHead) heads.Add(set); else ops.Add(set);
                if (!shipYOffs.Contains((short)yoff)) shipYOffs.Add((short)yoff);
            }
            ops.AddRange(heads);
            BlockNet.ServerBroadcastOps(ops);
            int n = ops.Count(o => o.Type == OpType.Set);
            Plugin.Log.LogInfo($"Ship takeoff: {n} attached blocks now travel with the ship");
            return n;
        }

        static bool IsNaturalVein(BlockInstance b) => b.Data.Def.ScrapValueMin > 0 || (b.Data.Def == Blocks.Stone && b.Data.State == 1) || (b.Data.State & Blocks.NaturalGround) != 0;
    }
}
