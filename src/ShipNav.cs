using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Monsters follow you onto blocks built onto the ship (#128). Placed blocks only cut holes in the game's navmesh
    /// (monsters go round them). On a landed ship a navmesh of its own is baked over the blocks attached to it, the ship
    /// and the ground around them: the tops a monster can step up to (by stairs or slabs, not a full block), joined to
    /// the game's navmesh by links all round its edge. Every machine bakes it (a monster's AI runs on whoever owns it),
    /// again a moment after those blocks change; it goes when the ship leaves.
    /// </summary>
    public static class ShipNav
    {
        public const int Area = 29;             // (an area the game doesn't use: tells this navmesh's polygons from the game's)
        const float Margin = 3f;                // ground taken in round the blocks (where the links are)
        const float LinkStep = 1.5f;
        static readonly List<NavMeshDataInstance> instances = new List<NavMeshDataInstance>();
        static readonly List<NavMeshLinkInstance> links = new List<NavMeshLinkInstance>();
        static int lastHash, pendingHash;
        static float nextLook, changedAt = -1f;
        static bool building;
        public static string Last = "none";
        public static Bounds Baked;
        public static bool Off; // (dev: the old way, to compare)

        static int Mask => (1 << BlockWorld.SolidLayer) | TerrainCarver.LevelMask | (1 << 11) | (1 << 28);

        /// <summary>Every frame (BlockWorld.Update): rebakes a moment after the ship's blocks change; gone off the moon.</summary>
        public static void Tick(BlockWorld w)
        {
            if (Time.time < nextLook || building) return;
            nextLook = Time.time + 0.5f;
            var sor = StartOfRound.Instance;
            bool landed = sor != null && !sor.inShipPhase && sor.shipHasLanded && !sor.shipIsLeaving && !Off;
            if (!landed) { if (instances.Count > 0 || links.Count > 0) { Clear(); Last = "cleared (not landed)"; } lastHash = 0; changedAt = -1f; return; }
            int h = Hash(w, out _);
            if (h == lastHash) { changedAt = -1f; return; }
            if (h != pendingHash) { pendingHash = h; changedAt = Time.time; return; }
            if (Time.time - changedAt < 1f) return; // (let a run of placing settle)
            lastHash = h; changedAt = -1f;
            w.StartCoroutine(Bake(w));
        }

        /// <summary>The ship's blocks a monster could stand on or bump into, as a number that changes when they do.</summary>
        static int Hash(BlockWorld w, out Bounds b)
        {
            int h = 17; bool any = false; b = default;
            foreach (var bi in w.Blocks.Values)
            {
                if (!MoonNav.IsLadder(bi, 1) && (bi.Key.Frame != 1 || bi.Go == null || !bi.Data.Def.Collides || bi.Col == null || !bi.Col.enabled)) continue; // (#214: a ladder up the hull too)
                unchecked { h = h * 31 + bi.Key.GetHashCode(); h = h * 31 + bi.Data.Def.Id; h = h * 31 + bi.Data.State; }
                if (!any) { b = bi.Col.bounds; any = true; } else b.Encapsulate(bi.Col.bounds);
            }
            return any ? h : 0;
        }

        static IEnumerator Bake(BlockWorld w)
        {
            building = true;
            try
            {
                if (Hash(w, out var blocks) == 0) { Clear(); Last = "no blocks on the ship"; yield break; }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var area = new Bounds(blocks.center, blocks.size + new Vector3(Margin * 2f, 0f, Margin * 2f));
                area.SetMinMax(area.min - Vector3.up * 2f, area.max + Vector3.up * 3f);
                var sources = new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(area, Mask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
                // (not people, monsters or items; nor a mesh the builder can't read: it skips those with an error each time)
                sources.RemoveAll(s => !(s.component is Collider c) || c.isTrigger || c.GetComponentInParent<PlayerControllerB>() != null ||
                    c.GetComponentInParent<EnemyAI>() != null || c.GetComponentInParent<GrabbableObject>() != null ||
                    (s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m && !m.isReadable));
                for (int i = 0; i < sources.Count; i++) { var s = sources[i]; s.area = Area; sources[i] = s; }
                var made = new List<NavMeshData>();
                for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                {
                    var st = NavMesh.GetSettingsByIndex(i);
                    // (up a whole block, as Minecraft's mobs jump one, #214; a taller wall stays a wall)
                    // (a half block is a step, as stairs and slabs are; a whole block is climbed by a step link, #214)
                    st.agentClimb = Mathf.Max(st.agentClimb, Mathf.Min(0.75f * Plugin.S, st.agentHeight * 0.95f));
                    st.minRegionArea = 0.2f; // (a stair's narrow step top was dropped as too small a region: Unity's default is 2 m2)
                    foreach (var msg in st.ValidationReport(area)) Plugin.Log.LogWarning("[shipnav] " + msg);
                    var data = new NavMeshData(st.agentTypeID);
                    var op = NavMeshBuilder.UpdateNavMeshDataAsync(data, st, sources, area);
                    while (!op.isDone) yield return null;
                    made.Add(data);
                }
                // the new one in, then the old one out (a monster on the old one is put back on a navmesh)
                var old = instances.ToList(); var oldLinks = links.ToList();
                instances.Clear(); links.Clear();
                foreach (var d in made) instances.Add(NavMesh.AddNavMeshData(d));
                Obstacles(w, true);
                int nLinks = Link(area, blocks);
                // (up a whole block onto the ship's blocks too: the obstacles' ring keeps the step from being one surface, #214)
                nLinks += MoonNav.StepLinks(bi => bi.Key.Frame == 1 && area.Intersects(bi.Col.bounds), links);
                nLinks += MoonNav.LadderLinks(bi => bi.Key.Frame == 1 && area.Intersects(bi.Col.bounds), links); // (#214: up ladders, to a house on the roof)
                foreach (var l in oldLinks) l.Remove();
                foreach (var inst in old) inst.Remove();
                Reseat(area);
                Baked = area;
                Last = $"baked {sources.Count} colliders over {area.size.x:F0}x{area.size.z:F0} m, {nLinks} links, {made.Count} agent types, {sw.ElapsedMilliseconds} ms";
                Plugin.Log.LogInfo("[shipnav] " + Last);
            }
            finally { building = false; }
        }

        /// <summary>Links between this navmesh and the game's wherever the two lie together (every LinkStep): not on the blocks, which the game's doesn't cover.</summary>
        static int Link(Bounds area, Bounds blocks)
        {
            int n = 0;
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int agent = NavMesh.GetSettingsByIndex(i).agentTypeID;
                var ours = new NavMeshQueryFilter { agentTypeID = agent, areaMask = 1 << Area };
                var theirs = new NavMeshQueryFilter { agentTypeID = agent, areaMask = ~((1 << Area) | (1 << MoonNav.Area)) };
                for (float x = area.min.x + 0.5f; x <= area.max.x - 0.5f; x += LinkStep)
                    for (float z = area.min.z + 0.5f; z <= area.max.z - 0.5f; z += LinkStep)
                    {
                        // (each of our polygons over this spot, top down: the ground, the ship's floor, its roof)
                        for (float y = area.max.y; y > area.min.y; )
                        {
                            if (!NavMesh.SamplePosition(new Vector3(x, y, z), out var a, 0.6f, ours) && !RayDown(new Vector3(x, y, z), area.min.y, ours, out a)) break;
                            if (NavMesh.SamplePosition(a.position, out var g, 0.35f, theirs) && Mathf.Abs(g.position.y - a.position.y) < 0.3f)
                            {
                                links.Add(MoonNav.AddLinkDbg("ship-link", new NavMeshLinkData
                                {
                                    startPosition = a.position, endPosition = g.position, width = 0f, costModifier = -1f,
                                    bidirectional = true, area = 0, agentTypeID = agent,
                                }));
                                n++;
                            }
                            y = a.position.y - 1.2f;
                        }
                    }
            }
            return n;
        }

        /// <summary>Our polygon straight down from a point (stepping down a little at a time), if any.</summary>
        static bool RayDown(Vector3 from, float minY, NavMeshQueryFilter f, out NavMeshHit hit)
        {
            for (var p = from; p.y > minY; p += Vector3.down * 1f)
                if (NavMesh.SamplePosition(p, out hit, 0.6f, f)) return true;
            hit = default;
            return false;
        }

        /// <summary>Monsters in the baked area that lost their footing with the swap: back onto the nearest navmesh.</summary>
        static void Reseat(Bounds area)
        {
            var rm = RoundManager.Instance;
            if (rm == null) return;
            foreach (var e in rm.SpawnedEnemies)
            {
                if (e == null || e.agent == null || !e.agent.enabled || e.agent.isOnNavMesh || !area.Contains(e.transform.position)) continue;
                if (NavMesh.SamplePosition(e.transform.position, out var h, 3f, NavMesh.AllAreas)) e.agent.Warp(h.position);
            }
        }

        /// <summary>
        /// The ship's blocks cut the game's navmesh (monsters go round them) with an obstacle each; whole, it would cut
        /// ours too, at every edge of their tops and steps. While ours is there they only reach up a little from the
        /// bottom: they still cut the game's navmesh on the ground under them, not ours over them.
        /// </summary>
        static void Obstacles(BlockWorld w, bool low)
        {
            if (w == null) return;
            foreach (var bi in w.Blocks.Values)
            {
                if (bi.Key.Frame != 1 || bi.Obstacle == null || bi.Data.Def.Shape != BlockShape.Cube) continue;
                bi.Obstacle.center = low ? new Vector3(0f, -0.425f, 0f) : Vector3.zero;
                bi.Obstacle.size = low ? new Vector3(1.02f, 0.65f, 1.02f) : Vector3.one * 1.02f;
            }
        }

        public static void Clear()
        {
            Obstacles(BlockWorld.Instance, false);
            foreach (var l in links) l.Remove();
            foreach (var inst in instances) inst.Remove();
            links.Clear(); instances.Clear();
            Baked = default;
        }

        /// <summary>(dev) The nearest point of ours and of the game's navmesh to a point, per agent type.</summary>
        public static string Probe(Vector3 q)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var st = NavMesh.GetSettingsByIndex(i);
                sb.Append($"agent {st.agentTypeID} r={st.agentRadius:F2} h={st.agentHeight:F2} climb={st.agentClimb:F2} slope={st.agentSlope:F0}: ");
                foreach (var (name, mask) in new[] { ("ours", 1 << Area), ("game", ~(1 << Area)) })
                {
                    var f = new NavMeshQueryFilter { agentTypeID = st.agentTypeID, areaMask = mask };
                    sb.Append(NavMesh.SamplePosition(q, out var h, 3f, f) ? $"{name} {h.position.x:F2},{h.position.y:F2},{h.position.z:F2} area={h.mask} " : $"{name} none ");
                }
                sb.Append("| ");
            }
            return sb.ToString();
        }

        public static string Describe() => $"off={Off} instances={instances.Count} links={links.Count} last: {Last}";
    }
}
