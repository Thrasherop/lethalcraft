using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Where a portal leads (#118), Minecraft's way: a portal at P comes out in the Nether at P/8 (x and z), in the room
    /// nearest to that with room for a portal; a portal appears there (obsidian and all) unless the room has one already.
    /// Back through it: out of the portal you came in by, else the portal nearest to 8 times its position (a portal inside
    /// the facility leads back into the facility). The fortress's own exit rooms work as before.
    /// </summary>
    public static class NetherLinks
    {
        public const float Scale = 8f;
        public static int Built; // (dev/tests)
        static BlockWorld W => BlockWorld.Instance;

        /// <summary>The cells of a frame standing along `axis` (0: x, 1: z) whose inside's bottom-left cell is `o`.</summary>
        public static void Frame(Vector3Int o, int axis, List<Vector3Int> obsidian, List<Vector3Int> inside)
        {
            var along = axis == 0 ? Vector3Int.right : new Vector3Int(0, 0, 1);
            for (int i = -1; i <= 2; i++)
                for (int y = -1; y <= 3; y++)
                {
                    var c = o + along * i + new Vector3Int(0, y, 0);
                    bool edge = i == -1 || i == 2 || y == -1 || y == 3;
                    (edge ? obsidian : inside).Add(c);
                }
        }

        static bool Free(Vector3Int c) => !W.Has(Ground.KeyOf(c)) && Ground.IsAirCell(c);
        static bool Solid(Vector3Int c) { var k = Ground.KeyOf(c); return W.Has(k) ? W.IsSolidAt(k) : Ground.IsSolidCell(c); }

        /// <summary>A frame fits here: every cell free, on solid floor, with room to stand on one side (which: `front`).</summary>
        static bool Fits(Vector3Int o, int axis, out Vector3Int front)
        {
            front = default;
            var ob = new List<Vector3Int>(); var ins = new List<Vector3Int>();
            Frame(o, axis, ob, ins);
            if (!ob.Concat(ins).All(Free)) return false;
            var along = axis == 0 ? Vector3Int.right : new Vector3Int(0, 0, 1);
            for (int i = -1; i <= 2; i++) if (!Solid(o + along * i + new Vector3Int(0, -2, 0))) return false;
            var side = axis == 0 ? new Vector3Int(0, 0, 1) : Vector3Int.right;
            foreach (var s in new[] { side, -side })
            {
                bool ok = true;
                for (int i = 0; i <= 1 && ok; i++)
                {
                    var f = o + along * i + s + new Vector3Int(0, -1, 0);
                    ok = Free(f) && Free(f + Vector3Int.up) && Solid(f + Vector3Int.down);
                }
                if (ok) { front = s; return true; }
            }
            return false;
        }

        /// <summary>
        /// Where a trip from `from` comes out: an arrival spot and facing, and the frame to build there (none if the room has
        /// a portal already).
        /// </summary>
        public static bool ArrivalFor(Vector3 from, out Vector3 at, out Vector3 facing, out Vector3Int frameOrigin, out int frameAxis, out bool build)
        {
            at = facing = Vector3.zero; frameOrigin = default; frameAxis = 0; build = false;
            var w = W;
            if (w == null || NetherFortress.Nodes == null || NetherFortress.Nodes.Length == 0) return false;
            var target = new Vector3(from.x / Scale, 0f, from.z / Scale);
            float Flat(Vector3 a) => new Vector2(a.x - target.x, a.z - target.z).magnitude;
            var rooms = NetherFortress.Nodes.Where(n => n != null).OrderBy(n => Flat(n.transform.position)).Take(16).ToList();
            float s = BlockWorld.S;
            foreach (var node in rooms)
            {
                var np = node.transform.position;
                // a portal in this room already: come out of it
                var there = w.Blocks.Values.FirstOrDefault(b => b.Data.Def == Blocks.NetherPortal && b.Go != null && Vector3.Distance(b.Go.transform.position, np) < 5f * s);
                if (there != null && FrontOf(there, out at, out facing)) return true;
                // (the room's first air cell over its floor, under the node)
                var c = Ground.CellOf(np);
                if (!Free(c)) continue;
                for (int k = 0; k < 4 && Free(c + Vector3Int.down); k++) c += Vector3Int.down;
                for (int r = 0; r <= 3; r++)
                    for (int dx = -r; dx <= r; dx++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                            for (int axis = 0; axis < 2; axis++)
                            {
                                var o = c + new Vector3Int(dx, 1, dz); // (the inside's bottom row one above the floor's air cell)
                                if (!Fits(o, axis, out var front)) continue;
                                var along = axis == 0 ? Vector3.right : Vector3.forward;
                                // stand in front of its middle, on the floor, facing away from it
                                var stand = o + new Vector3Int(front.x, -1, front.z);
                                var mid = Ground.Center(stand) + along * (0.5f * s);
                                at = new Vector3(mid.x, Ground.Center(stand).y - 0.5f * s + 0.05f, mid.z);
                                facing = new Vector3(front.x, 0, front.z);
                                frameOrigin = o; frameAxis = axis; build = true;
                                return true;
                            }
                        }
            }
            return false;
        }

        /// <summary>A step out of the portal `b` belongs to, on its open side.</summary>
        public static bool FrontOf(BlockInstance b, out Vector3 at, out Vector3 facing)
        {
            at = facing = Vector3.zero;
            var w = W;
            var portals = w.Blocks.Values.Where(x => x.Data.Def == Blocks.NetherPortal && x.Go != null).ToList();
            var bottom = portals.Where(x => Vector3.ProjectOnPlane(x.Go.transform.position - b.Go.transform.position, Vector3.up).magnitude < 0.1f)
                                .OrderBy(x => x.Go.transform.position.y).FirstOrDefault() ?? b;
            bool alongX = (bottom.Data.State & 1) == 0;
            var n = alongX ? bottom.Go.transform.forward : bottom.Go.transform.right;
            float s = BlockWorld.S;
            var feet = bottom.Go.transform.position - bottom.Go.transform.up * (0.5f * s);
            var outA = feet + n * s; var outB = feet - n * s;
            var to = Physics.CheckCapsule(outA + Vector3.up * 0.4f, outA + Vector3.up * 1.8f, 0.3f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) ? outB : outA;
            at = to + Vector3.up * 0.05f; facing = (to - feet).normalized;
            return true;
        }

        /// <summary>Server: the frame and its portal, as a client asked (only into free cells).</summary>
        public static void ServerBuild(Vector3Int o, int axis)
        {
            var w = W;
            if (w == null || NetherFortress.Root == null || !NetherFortress.Contains(Ground.Center(o))) return;
            var ob = new List<Vector3Int>(); var ins = new List<Vector3Int>();
            Frame(o, axis, ob, ins);
            if (ins.Any(c => w.Has(Ground.KeyOf(c)) && w.DefAt(Ground.KeyOf(c)) != Blocks.NetherPortal)) return;
            if (ins.All(c => w.DefAt(Ground.KeyOf(c)) == Blocks.NetherPortal)) return; // (built already: two players at once)
            var ops = new List<Op>();
            foreach (var c in ob) if (!w.Has(Ground.KeyOf(c))) ops.Add(Op.Set(Ground.KeyOf(c), new BlockData(Blocks.Obsidian.Id, (byte)Face.Up, 0)));
            foreach (var c in ins) ops.Add(Op.Set(Ground.KeyOf(c), new BlockData(Blocks.NetherPortal.Id, (byte)Face.Up, (byte)axis)));
            w.FrameRoot(0, true);
            BlockNet.ServerBroadcastOps(ops);
            BlockNet.ServerSound(Ground.Center(o), "portal.trigger", 1f, 0.6f);
            Built++;
            Plugin.Log.LogInfo($"[portal] a portal for an arrival in the Nether at {o} along {(axis == 0 ? "x" : "z")}");
        }

        /// <summary>Back from a portal in the Nether: the one you came in by, else the portal nearest to 8x its position.</summary>
        public static BlockInstance ReturnTarget(BlockInstance from, bool hasCameFrom, Vector3 cameFrom)
        {
            var w = W;
            var outside = w.Blocks.Values.Where(x => x.Data.Def == Blocks.NetherPortal && x.Go != null && !NetherFortress.Contains(x.Go.transform.position)).ToList();
            if (outside.Count == 0) return null;
            if (hasCameFrom) return outside.OrderBy(x => Vector3.Distance(x.Go.transform.position, cameFrom)).First();
            var q = from.Go.transform.position * Scale;
            return outside.OrderBy(x => new Vector2(x.Go.transform.position.x - q.x, x.Go.transform.position.z - q.z).magnitude).First();
        }
    }
}
