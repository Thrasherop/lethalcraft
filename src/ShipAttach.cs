using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
                if (d0.Def == Blocks.Chest) Chests.ServerMove(b.Key, key); // the contents move with it
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

    /// <summary>
    /// What stands on blocks attached to the ship travels with it. The game carries a player (and anything they drop)
    /// only while they stand inside the ship's box; standing on a ship block outside it (a porch off the hangar door,
    /// a tower on the roof) now counts as being on the ship too, so the game parents them to it as usual. Items resting
    /// on ship blocks go in the ship the same way (and come out of it when they no longer rest on one).
    /// </summary>
    [HarmonyPatch]
    public static class ShipCarry
    {
        public static bool Enabled = true; // (dev: off reproduces the old behaviour)

        /// <summary>Stand-in for Bounds.Contains in StartOfRound.LateUpdate, whose every check is "is the local player in it".</summary>
        public static bool Contains(ref Bounds b, Vector3 p)
        {
            if (b.Contains(p)) return true;
            var sor = StartOfRound.Instance;
            if (!Enabled || sor == null || sor.shipBounds == null || b != sor.shipBounds.bounds) return false;
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            bool r = lp != null && (DevOldCheck ? OnShipBlock(lp.transform.position, 0.9f) : StandsOnShipBlock(lp.transform.position));
            DevLast = $"{(r ? "T" : "F")}@{lp?.transform.position.y:0.000}";
            if (!r) DevFalseCount++;
            return r;
        }
        public static string DevLast; public static int DevFalseCount;

        // ------------------------------------------------------------------ the ship's outside sound zone (#37)
        // The ship has an outside reverb zone (AudioReverbTrigger, "not in the elevator") around it; while a player stands
        // in it, it keeps setting them off the ship, and StartOfRound.LateUpdate (on the ground only) sets them back on.
        // On a porch of ship blocks that's a flicker; but mid-jump only the zone runs: the player stops riding the ship,
        // the porch rises out from under them, and if the ship leaves then, they're left behind (killed, items lost).
        // A player over the ship's blocks stays on the ship as far as the zone is concerned (vanilla decides it on the
        // ground: standing on a ship block or not).
        public static int DevZoneSaves;

        [HarmonyPatch(typeof(AudioReverbTrigger), nameof(AudioReverbTrigger.ChangeAudioReverbForPlayer)), HarmonyPrefix]
        static void ZoneBefore(out (bool el, bool room) __state)
        {
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            __state = lp != null ? (lp.isInElevator, lp.isInHangarShipRoom) : (false, false);
        }

        [HarmonyPatch(typeof(AudioReverbTrigger), nameof(AudioReverbTrigger.ChangeAudioReverbForPlayer)), HarmonyPostfix]
        static void ZoneAfter((bool el, bool room) __state)
        {
            if (!Enabled || DevOldCheck || !__state.el) return;
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp == null || lp.isInElevator || lp.isPlayerDead) return;
            var feet = lp.transform.position;
            bool over = lp.thisController.isGrounded ? StandsOnShipBlock(feet) : ShipBlockBelow(feet, 4f);
            if (!over) return;
            lp.isInElevator = true;
            lp.isInHangarShipRoom = __state.room;
            if (lp.currentlyHeldObjectServer != null && lp.isHoldingObject) lp.SetItemInElevator(__state.room, true, lp.currentlyHeldObjectServer);
            DevZoneSaves++;
        }

        /// <summary>A ship block somewhere in the column under these feet (an airborne player over a porch).</summary>
        static bool ShipBlockBelow(Vector3 feet, float depth)
        {
            var center = feet + Vector3.down * (depth * 0.5f - 0.3f);
            var half = new Vector3(0.25f, depth * 0.5f, 0.25f);
            int n = Physics.OverlapBoxNonAlloc(center, half, overlap, Quaternion.identity, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var br = overlap[i].GetComponentInParent<BlockRef>();
                if (br != null && br.Key.Frame == 1) return true;
            }
            return false;
        }

        /// <summary>A player's feet on (or a little in, or up to 0.6 m above) a ship block. A box, not a ray (#37): a ray that
        /// starts inside a block doesn't hit it, and while the ship rises a player landing from a jump can have their feet
        /// a little inside the block they land on; the ray missed it, the game took them for off the ship (it decides on
        /// the ground), and they were left behind.</summary>
        public static bool StandsOnShipBlock(Vector3 feet)
        {
            const float above = 0.6f, inside = 0.5f;
            var center = feet + Vector3.up * (inside - above) * 0.5f;
            var half = new Vector3(0.25f, (above + inside) * 0.5f, 0.25f);
            int n = Physics.OverlapBoxNonAlloc(center, half, overlap, Quaternion.identity, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var br = overlap[i].GetComponentInParent<BlockRef>();
                if (br != null && br.Key.Frame == 1) return true;
            }
            return false;
        }
        static readonly Collider[] overlap = new Collider[16];
        public static bool DevOldCheck; // (dev "shipcarry old": the ray, to compare)

        /// <summary>Is there a ship block right under this point (feet, or an item's resting spot)?</summary>
        public static bool OnShipBlock(Vector3 at, float reach)
        {
            foreach (var h in Physics.RaycastAll(at + Vector3.up * 0.3f, Vector3.down, reach, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore))
            {
                var br = h.collider.GetComponentInParent<BlockRef>();
                if (br != null && br.Key.Frame == 1) return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(StartOfRound), "LateUpdate"), HarmonyTranspiler]
        static IEnumerable<CodeInstruction> CountShipBlocks(IEnumerable<CodeInstruction> code)
        {
            var contains = AccessTools.Method(typeof(Bounds), nameof(Bounds.Contains), new[] { typeof(Vector3) });
            var mine = AccessTools.Method(typeof(ShipCarry), nameof(Contains));
            int n = 0;
            foreach (var ci in code)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo m && m == contains)
                {
                    n++;
                    ci.opcode = OpCodes.Call; // same stack: the bounds' address and the point (labels stay on it)
                    ci.operand = mine;
                }
                yield return ci;
            }
            Plugin.Log.LogInfo($"Ship carry: {n} ship-bounds checks in StartOfRound.LateUpdate also count ship blocks");
        }

        static float nextItemCheck;

        /// <summary>Every client, once a second: items resting on ship blocks belong to the ship; ones that don't, don't.</summary>
        public static void TickItems()
        {
            if (!Enabled || Time.time < nextItemCheck) return;
            nextItemCheck = Time.time + 1f;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.elevatorTransform == null || sor.shipBounds == null) return;
            foreach (var item in Object.FindObjectsOfType<GrabbableObject>())
            {
                if (item == null || item.isHeld || item.isHeldByEnemy || item.isPocketed || item.parentObject != null || !item.hasHitGround || !item.reachedFloorTarget) continue;
                var p = item.transform.position;
                bool inBox = sor.shipBounds.bounds.Contains(p);
                bool onShipBlock = !inBox && OnShipBlock(p, 0.9f);
                bool inShip = item.transform.parent == sor.elevatorTransform;
                if (onShipBlock && !inShip) Move(item, sor.elevatorTransform, true, sor.shipInnerRoomBounds.bounds.Contains(p));
                else if (!inBox && !onShipBlock && inShip && item.transform.parent == sor.elevatorTransform && !(item is StackItem st && st.SpawnTime > Time.time - 2f))
                    Move(item, sor.propsContainer, false, false);
            }
        }

        static void Move(GrabbableObject item, Transform parent, bool inElevator, bool inRoom)
        {
            item.transform.SetParent(parent, worldPositionStays: true);
            item.targetFloorPosition = item.transform.localPosition;
            item.startFallingPosition = item.transform.localPosition;
            item.isInElevator = inElevator;
            item.isInShipRoom = inRoom;
        }
    }
}
