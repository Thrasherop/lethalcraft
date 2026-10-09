using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Engine-free: which blocks a piston moves, Minecraft's way. Starting from the first block in front (or, for a sticky
    /// piston pulling, the block stuck to its head), every block in the way of a moving block has to move too, and slime
    /// blocks drag along whatever movable blocks touch them on any side. Non-solid blocks in the way (torches, dust) get
    /// broken; anything unmovable in the way (bedrock, an extended piston, raw ground) blocks the whole move. At most
    /// <see cref="Limit"/> blocks move.
    /// </summary>
    public static class PistonStructure
    {
        public const int Limit = 12;

        public enum Cell { Empty, Movable, Crushable, Immovable }

        public class Result
        {
            public bool Ok;
            /// <summary>Blocks to move, ordered so each moves into a cell that's already free (front-most first).</summary>
            public List<Vector3Int> Move = new List<Vector3Int>();
            public List<Vector3Int> Crush = new List<Vector3Int>();
        }

        static readonly Vector3Int[] Dirs = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

        /// <param name="start">The first block the piston moves (in front when pushing, stuck to the head when pulling).</param>
        /// <param name="dir">Direction the blocks move.</param>
        /// <param name="piston">Cells that belong to the piston itself (body, head): never moved, never in the way.</param>
        /// <param name="pull">Pulling: a start block that isn't movable just stays (no failure).</param>
        /// <param name="sticksTo">Whether a sticky cell takes its neighbour along (null: always; slime and honey don't stick to each other).</param>
        public static Result Resolve(Vector3Int start, Vector3Int dir, ICollection<Vector3Int> piston, System.Func<Vector3Int, Cell> cellAt,
                                     System.Func<Vector3Int, bool> sticky, bool pull = false, System.Func<Vector3Int, Vector3Int, bool> sticksTo = null)
        {
            var res = new Result { Ok = true };
            var startCell = cellAt(start);
            if (startCell != Cell.Movable)
            {
                if (pull) return res; // nothing stuck to the head: the head just retracts
                if (startCell == Cell.Immovable) res.Ok = false;
                else if (startCell == Cell.Crushable) res.Crush.Add(start);
                return res;
            }
            var moving = new HashSet<Vector3Int>();
            var crush = new HashSet<Vector3Int>();
            // (cell, attached): attached cells only join if movable; cells in the way must be cleared
            var queue = new Queue<(Vector3Int p, bool attached)>();
            queue.Enqueue((start, false));
            while (queue.Count > 0)
            {
                var (p, attached) = queue.Dequeue();
                if (moving.Contains(p)) continue;
                if (piston.Contains(p))
                {
                    if (attached) continue;                // slime touching the piston doesn't move it
                    res.Ok = false; return res;            // pushing into / pulling through the piston itself
                }
                var c = cellAt(p);
                if (c == Cell.Empty) continue;
                if (c == Cell.Crushable) { if (!attached) crush.Add(p); continue; }
                if (c == Cell.Immovable) { if (attached) continue; res.Ok = false; return res; }
                moving.Add(p);
                if (moving.Count > Limit) { res.Ok = false; return res; }
                queue.Enqueue((p + dir, false));            // whatever is in the way moves (or breaks) too
                if (sticky(p))
                    foreach (var d in Dirs)
                        if (d != dir && (sticksTo == null || sticksTo(p, p + d))) queue.Enqueue((p + d, true));
            }
            // a moving block's destination freed by another moving block isn't crushed
            crush.ExceptWith(moving);
            res.Crush.AddRange(crush);
            res.Move.AddRange(moving.OrderByDescending(p => p.x * dir.x + p.y * dir.y + p.z * dir.z));
            return res;
        }
    }
}
