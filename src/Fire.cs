using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Fire, like Minecraft's. Flint and steel lights it on whatever surface you strike (the ground or a block), or
    /// lights TNT. On stone or dirt it burns out after a few seconds; next to flammable blocks (wood, planks, leaves,
    /// wool, bookshelves, crafting tables, note blocks) it keeps going, spreads to them and burns them away (no drops),
    /// and lights TNT it touches. Standing in it hurts, monsters standing in it get hit too, and a punch puts it out.
    /// The server runs the fire; each client hurts its own player.
    /// </summary>
    public static class Fire
    {
        static BlockWorld W => BlockWorld.Instance;
        static readonly Dictionary<BlockKey, int> age = new Dictionary<BlockKey, int>();
        static readonly System.Random rng = new System.Random();

        public static void Reset() => age.Clear();

        static bool IsFire(BlockInstance b) => b != null && b.Data.Def == Blocks.Fire;

        /// <summary>
        /// Something to burn on: a solid block under it, or the ground anywhere from the middle of its cell to just
        /// under it (a fire on the moons' grid often sits partly in uneven ground; a ray from inside the ground, or a box
        /// wholly inside it, wouldn't see that ground).
        /// </summary>
        static bool HasGround(BlockKey k)
        {
            var world = W;
            var below = world.Get(k.Offset((int)Face.Down));
            if (below != null && below.Data.Def.Solid) return true;
            var c = world.WorldCenter(k);
            var down = world.FrameDirToWorld(k.Frame, Vector3.down);
            var right = world.FrameDirToWorld(k.Frame, Vector3.right);
            var fwd = world.FrameDirToWorld(k.Frame, Vector3.forward);
            float S = BlockWorld.S;
            // over the same 3x3 footprint placement rests a block on (its highest ground point): on a slope the ground
            // right under the middle can be well below the cell
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                {
                    var o = c - down * (S * 0.1f) + (right * sx + fwd * sz) * (S * 0.4f);
                    foreach (var h in Physics.RaycastAll(o, down, S * 0.85f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
                        if (h.collider.GetComponentInParent<BlockRef>() == null) return true;
                }
            return false;
        }

        static bool NextToFuel(BlockKey k)
        {
            for (int f = 0; f < 6; f++)
            {
                var n = W.Get(k.Offset(f));
                if (n != null && (n.Data.Def.Flammable || n.Data.Def == Blocks.TNT)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ server
        /// <summary>Server: flint and steel struck at a cell: light TNT there, or start a fire in the (empty) cell.</summary>
        public static void ServerLight(ulong sender, BlockKey k)
        {
            var world = W;
            if (world == null) return;
            var b = world.Get(k);
            if (b != null) { if (b.Data.Def == Blocks.TNT) ServerLogic.Ignite(k, 80); return; }
            var p = ServerLogic.PlayerFor(sender);
            string why = null;
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, world.WorldCenter(k)) > 9f * BlockWorld.S + 3f) why = "out of reach";
            else if (k.Frame == 0 && !world.WorldFrameAvailable) why = "no world frame";
            // only the middle of the cell must be clear: a fire sits on (and a little into) uneven ground
            else if (Redstone.OutOfWorld(k)) why = "out of the world";
            else if (ServerLogic.Obstructed(k, 0.6f)) why = "obstructed";
            else if (!HasGround(k) && !NextToFuel(k)) why = "nothing to burn on"; // needs something to burn on
            if (why != null)
            {
                if (Plugin.DevMode.Value)
                {
                    // (how far down the ground really is, and what it is)
                    var c0 = world.WorldCenter(k); var dn = world.FrameDirToWorld(k.Frame, Vector3.down);
                    string ground = "none within 4 blocks";
                    foreach (var h in Physics.RaycastAll(c0, dn, BlockWorld.S * 4f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                        if (h.collider.GetComponentInParent<BlockRef>() == null) { ground = $"{h.collider.name} L{h.collider.gameObject.layer} {h.distance / BlockWorld.S:F2} blocks below the middle"; break; }
                    Plugin.Log.LogInfo($"[dev] no fire at {k}: {why} (ground: {ground})");
                }
                return;
            }
            Place(k, null);
        }

        static void Place(BlockKey k, List<Op> ops)
        {
            age[k] = 0;
            var op = Op.Set(k, new BlockData(Blocks.Fire.Id, (byte)Face.Up, 0));
            if (ops != null) ops.Add(op); else BlockNet.ServerBroadcastOp(op);
        }

        /// <summary>Server, twice a second: burn, spread, burn out.</summary>
        public static void ServerTick(bool hurtMonsters)
        {
            var world = W;
            if (world == null || !BlockNet.IsServer) return;
            var fires = world.Blocks.Where(kv => IsFire(kv.Value)).Select(kv => kv.Key).ToList();
            foreach (var k in age.Keys.ToList()) if (!IsFire(world.Get(k))) age.Remove(k);
            if (fires.Count == 0) return;
            var ops = new List<Op>();
            var lit = new HashSet<BlockKey>();
            foreach (var k in fires)
            {
                bool fuel = false;
                for (int f = 0; f < 6; f++)
                {
                    var nk = k.Offset(f);
                    var n = world.Get(nk);
                    if (n == null) continue;
                    if (n.Data.Def == Blocks.TNT) { ServerLogic.Ignite(nk, 80); continue; }
                    if (!n.Data.Def.Flammable) continue;
                    fuel = true;
                    // a flammable neighbour burns away in a few seconds, sometimes leaving fire where it was
                    if (rng.NextDouble() < 0.06)
                    {
                        ServerLogic.BreakBlock(nk, false);
                        if (rng.NextDouble() < 0.5) lit.Add(nk);
                    }
                }
                // spreads to empty cells next to something flammable (within a block, a little more upward like Minecraft)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 2; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            var c = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(dx, dy, dz));
                            if (world.Get(c) != null || lit.Contains(c) || !NextToFuel(c)) continue;
                            if (rng.NextDouble() < (dy > 0 ? 0.05 : 0.025)) lit.Add(c);
                        }
                age.TryGetValue(k, out int a);
                a += rng.Next(0, 3);
                age[k] = a;
                bool burnedOut = !fuel ? a >= 8 + rng.Next(0, 9) : a >= 30 && rng.NextDouble() < 0.2; // bare ground: ~6 s
                bool unsupported = !fuel && !HasGround(k);
                if (burnedOut || unsupported)
                {
                    if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] fire at {k} out: {(burnedOut ? $"burned out (age {a})" : "nothing under it")}");
                    ops.Add(Op.Remove(k, false)); age.Remove(k);
                }
            }
            foreach (var c in lit)
                if (world.Get(c) == null && !ServerLogic.Obstructed(c, 0.6f) && !Redstone.OutOfWorld(c)) Place(c, ops);
            BlockNet.ServerBroadcastOps(ops);
            if (hurtMonsters) HurtMonsters(world);
        }

        static void HurtMonsters(BlockWorld world)
        {
            var fires = world.Blocks.Values.Where(IsFire).Select(b => world.WorldCenter(b.Key)).ToList();
            if (fires.Count == 0) return;
            float h = BlockWorld.S * 0.5f;
            foreach (var e in Object.FindObjectsOfType<EnemyAI>())
            {
                if (e == null || e.isEnemyDead || !e.IsSpawned) continue;
                var p = e.transform.position;
                foreach (var c in fires)
                {
                    if (Mathf.Abs(p.x - c.x) > h || Mathf.Abs(p.z - c.z) > h || p.y < c.y - h - 0.4f || p.y > c.y + h) continue;
                    try { e.HitEnemyOnLocalClient(1, Vector3.zero, null, true, -1); } catch (System.Exception ex) { Plugin.Log.LogWarning("Fire hit " + e.name + ": " + ex.Message); }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ every client
        static float nextHurt, nextCrackle;

        /// <summary>Every client: its own player standing in fire gets hurt (the game's damage, so god/creative rules apply).</summary>
        public static void ClientTick()
        {
            var world = W;
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (world == null || p == null) return;
            if (Time.time >= nextCrackle)
            {
                nextCrackle = Time.time + 1.6f;
                var near = world.Blocks.Values.Where(b => IsFire(b) && b.Go != null && Vector3.Distance(b.Go.transform.position, p.transform.position) < 16f).ToList();
                if (near.Count > 0)
                {
                    var f = near[Random.Range(0, near.Count)];
                    Sounds.Play("fire", f.Go.transform.position, Random.Range(0.7f, 1.0f), 0.55f);
                }
            }
            if (Time.time < nextHurt || p.isPlayerDead || !p.isPlayerControlled) return;
            nextHurt = Time.time + 0.5f;
            var pb = p.thisController.bounds;
            foreach (var b in world.Blocks.Values)
            {
                if (!IsFire(b) || b.Go == null) continue;
                var cell = new Bounds(b.Go.transform.position, Vector3.one * BlockWorld.S * 0.85f);
                if (!cell.Intersects(pb)) continue;
                // Minecraft: half a heart every half second in fire (a tenth of your health a second)
                p.DamagePlayer(5, hasDamageSFX: true, callRPC: true, causeOfDeath: CauseOfDeath.Burning);
                break;
            }
        }
    }
}
