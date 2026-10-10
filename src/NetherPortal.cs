using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Nether portals (#66, phase 1), Minecraft's: an obsidian frame, its inside 2-21 wide and 3-21 tall (the corners
    /// don't matter), standing along either axis, lit with flint and steel, fills with portal blocks. A portal block can't
    /// be mined; take any of its frame away (dig it, blow it up, push it) and the whole portal goes out. Portal blocks
    /// glow purple, animate and hum. Built onto the ship, a portal goes and is saved with it like any ship block.
    /// The state's bit 0 is the axis the portal stands along: 0 east-west (thin north-south), 1 north-south.
    /// </summary>
    public static class NetherPortal
    {
        public const int MinWidth = 2, MinHeight = 3, MaxSize = 21;
        static BlockWorld W => BlockWorld.Instance;

        static bool IsFrame(BlockWorld w, BlockKey k) => w.DefAt(k) == Blocks.Obsidian;

        /// <summary>A cell the portal can fill: nothing there (or a fire), and not inside the moon's ground.</summary>
        static bool IsOpen(BlockWorld w, BlockKey k)
        {
            var d = w.DefAt(k);
            if (d != null) return d == Blocks.Fire;
            return !ServerLogic.Obstructed(k, 0.6f);
        }

        /// <summary>
        /// Minecraft's portal shape (PortalShape) through the cell `k`, standing along `axis` (0: x, 1: z): down to the frame's
        /// bottom, across to its left side, then the inside row by row up to a row of obsidian. Null if there's no frame.
        /// </summary>
        public static List<BlockKey> Shape(BlockKey k, int axis)
        {
            var w = W;
            if (w == null || !IsOpen(w, k)) return null;
            int along = axis == 0 ? (int)Face.East : (int)Face.South, back = (along ^ 1);
            int down = (int)Face.Down, up = (int)Face.Up;
            var c = k;
            for (int n = 0; n < MaxSize && IsOpen(w, c.Offset(down)); n++) c = c.Offset(down);
            if (!IsFrame(w, c.Offset(down))) return null;
            for (int n = 0; n < MaxSize && IsOpen(w, c.Offset(back)) && IsFrame(w, c.Offset(back).Offset(down)); n++) c = c.Offset(back);
            if (!IsFrame(w, c.Offset(back))) return null;
            var corner = c;
            // the width: open cells along the bottom, each on obsidian, up to the right side's obsidian
            int width = 0;
            var x = corner;
            while (width <= MaxSize && IsOpen(w, x) && IsFrame(w, x.Offset(down))) { x = x.Offset(along); width++; }
            if (width < MinWidth || width > MaxSize || !IsFrame(w, x)) return null;
            // the height: rows open all across with obsidian at both ends, up to a row that's obsidian all across
            var cells = new List<BlockKey>();
            for (int h = 0; h <= MaxSize; h++)
            {
                var row = corner;
                for (int i = 0; i < h; i++) row = row.Offset(up);
                bool allFrame = true, allOpen = true;
                var cell = row;
                for (int i = 0; i < width; i++, cell = cell.Offset(along))
                {
                    if (!IsFrame(w, cell)) allFrame = false;
                    if (!IsOpen(w, cell)) allOpen = false;
                }
                if (allFrame && h >= MinHeight) return cells;
                if (!allOpen || !IsFrame(w, row.Offset(back)) || !IsFrame(w, cell)) return null;
                cell = row;
                for (int i = 0; i < width; i++, cell = cell.Offset(along)) cells.Add(cell);
                if (h + 1 > MaxSize) return null;
            }
            return null;
        }

        /// <summary>Server: flint and steel struck at an empty cell: if it's inside an obsidian frame, the frame lights up.</summary>
        public static bool ServerTryLight(BlockKey k)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                var cells = Shape(k, axis);
                if (cells == null || !cells.Contains(k)) continue;
                var ops = cells.Select(c => Op.Set(c, new BlockData(Blocks.NetherPortal.Id, (byte)Face.Up, (byte)axis))).ToList();
                BlockNet.ServerBroadcastOps(ops);
                BlockNet.ServerSound(W.WorldCenter(k), "portal.trigger", 1f, 0.6f);
                Plugin.Log.LogInfo($"[portal] lit: {cells.Count} blocks along {(axis == 0 ? "x" : "z")} at {k}");
                Lit++;
                return true;
            }
            return false;
        }

        public static int Lit, Broken; // (dev/tests)

        /// <summary>
        /// Server, a few times a second: a portal block whose neighbours in its own plane (up, down, either side) aren't all
        /// portal or obsidian goes out, and with it the rest (Minecraft's: break the frame and the portal's gone).
        /// </summary>
        public static void ServerTick()
        {
            var w = W;
            if (w == null) return;
            var portals = new HashSet<BlockKey>(w.Blocks.Where(kv => kv.Value.Data.Def == Blocks.NetherPortal).Select(kv => kv.Key));
            if (portals.Count == 0) return;
            var gone = new HashSet<BlockKey>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var k in portals)
                {
                    if (gone.Contains(k)) continue;
                    int axis = w.Get(k).Data.State & 1;
                    int along = axis == 0 ? (int)Face.East : (int)Face.South;
                    foreach (int f in new[] { (int)Face.Up, (int)Face.Down, along, (along ^ 1) })
                    {
                        var n = k.Offset(f);
                        bool ok = (portals.Contains(n) && !gone.Contains(n) && (w.Get(n).Data.State & 1) == axis) || IsFrame(w, n);
                        if (!ok) { gone.Add(k); changed = true; break; }
                    }
                }
            }
            if (gone.Count == 0) return;
            BlockNet.ServerBroadcastOps(gone.Select(k => Op.Remove(k, true)).ToList());
            Broken++;
            Plugin.Log.LogInfo($"[portal] {gone.Count} portal blocks went out (their frame was broken)");
        }

        public static string Describe()
        {
            var w = W;
            if (w == null) return "-";
            var p = w.Blocks.Where(kv => kv.Value.Data.Def == Blocks.NetherPortal).ToList();
            return $"portalBlocks={p.Count} alongX={p.Count(kv => (kv.Value.Data.State & 1) == 0)} lit={Lit} broken={Broken}";
        }
    }
}
