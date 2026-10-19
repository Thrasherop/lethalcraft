using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// #214: monsters onto blocks built on the moon, as ShipNav does it for the ship's (#128). A placed block only cut a
    /// hole in the game's navmesh, so monsters went round a set of steps and stood under you. Every group of placed
    /// blocks (natural ground left out) gets a navmesh of its own over its tops and the ground round it, a monster able to
    /// go up a whole block as Minecraft's mobs jump one, joined to the game's navmesh by links all round. Every machine
    /// bakes it (a monster's AI runs on whoever owns it), again a moment after that group changes; all gone off the moon.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class MoonNav
    {
        /// <summary>The game gives each monster an area mask (EnemyAI.Start) of the areas its navmesh has: ours (and the
        /// ship's, ShipNav) weren't in it, so the game's own monsters never set foot on a navmesh baked over blocks; only
        /// the mod's, whose mask is every area, did.</summary>
        [HarmonyLib.HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.Start)), HarmonyLib.HarmonyPostfix]
        static void OntoBlocks(EnemyAI __instance)
        {
            if (__instance.agent == null) return;
            int ours = (1 << Area) | (1 << ShipNav.Area);
            __instance.agentMask |= ours;
            __instance.agent.areaMask |= ours;
        }

        public const int Area = 28;             // (the game doesn't use it; ShipNav's is 29)
        const float Margin = 3f, LinkStep = 1.5f;
        public static bool Off; // (dev: moonnav off, the old way, to compare)
        public static bool DevLog = true;
        public static string Last = "none";

        class Group
        {
            public Bounds Blocks; public int Hash;
            public List<NavMeshDataInstance> Data = new List<NavMeshDataInstance>();
            public List<NavMeshLinkInstance> Links = new List<NavMeshLinkInstance>();
        }
        static readonly List<Group> baked = new List<Group>();
        static readonly Dictionary<int, float> seen = new Dictionary<int, float>(); // a group's hash -> when first seen like that
        static float nextLook;
        static bool building;

        static int Mask => (1 << BlockWorld.SolidLayer) | TerrainCarver.LevelMask | (1 << 11) | (1 << 28);

        /// <summary>A whole block and a little: Minecraft's mobs jump one block (a fence's one and a half stops them).</summary>
        public static float Climb => BlockWorld.S + 0.25f; // (the navmesh rounds heights a little either way)

        static bool Ours(BlockInstance bi) =>
            bi.Key.Frame == 0 && bi.Go != null && bi.Data.Def.Collides && bi.Col != null && bi.Col.enabled && (bi.Data.State & Blocks.NaturalGround) == 0;

        /// <summary>Every frame (BlockWorld.Update): bakes a group a moment after it's built or changed; all gone off the moon.</summary>
        public static void Tick(BlockWorld w)
        {
            if (Time.time < nextLook || building) return;
            nextLook = Time.time + 0.5f;
            var sor = StartOfRound.Instance;
            bool landed = sor != null && !sor.inShipPhase && sor.shipHasLanded && !sor.shipIsLeaving && !Off;
            if (!landed) { if (baked.Count > 0) { Clear(); Last = "cleared (not landed)"; } seen.Clear(); return; }
            var groups = Groups(w);
            // groups baked as they are stay; one that's new or changed is baked once it has been the same for a second
            foreach (var g in groups)
            {
                if (baked.Any(b => b.Hash == g.Hash)) continue;
                if (!seen.TryGetValue(g.Hash, out float t)) { seen[g.Hash] = Time.time; continue; }
                if (Time.time - t < 1f) continue;
                w.StartCoroutine(Bake(g, groups));
                return; // (one at a time)
            }
            // a baked group that's gone (or changed: its replacement baked above) goes
            for (int i = baked.Count - 1; i >= 0; i--)
                if (!groups.Any(g => g.Hash == baked[i].Hash) && !groups.Any(g => g.Blocks.Intersects(baked[i].Blocks) && !baked.Any(b => b.Hash == g.Hash)))
                { Remove(baked[i]); baked.RemoveAt(i); }
            if (seen.Count > 256) seen.Clear();
        }

        /// <summary>Placed blocks in groups: touching (edges and corners too) is one group.</summary>
        static List<Group> Groups(BlockWorld w)
        {
            var cells = new Dictionary<Vector3Int, BlockInstance>();
            foreach (var bi in w.Blocks.Values) if (Ours(bi)) cells[bi.Key.Pos] = bi;
            var groups = new List<Group>();
            var done = new HashSet<Vector3Int>();
            var stack = new Stack<Vector3Int>();
            foreach (var start in cells.Keys)
            {
                if (done.Contains(start)) continue;
                var g = new Group(); int h = 17; bool any = false;
                stack.Push(start); done.Add(start);
                while (stack.Count > 0)
                {
                    var c = stack.Pop(); var bi = cells[c];
                    unchecked { h = h * 31 + bi.Key.GetHashCode(); h = h * 31 + bi.Data.Def.Id; h = h * 31 + bi.Data.State; }
                    if (!any) { g.Blocks = bi.Col.bounds; any = true; } else g.Blocks.Encapsulate(bi.Col.bounds);
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    {
                        var n = c + new Vector3Int(dx, dy, dz);
                        if (cells.ContainsKey(n) && done.Add(n)) stack.Push(n);
                    }
                }
                g.Hash = h;
                groups.Add(g);
            }
            return groups;
        }

        static IEnumerator Bake(Group g, List<Group> all)
        {
            building = true;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var area = new Bounds(g.Blocks.center, g.Blocks.size + new Vector3(Margin * 2f, 0f, Margin * 2f));
                area.SetMinMax(area.min - Vector3.up * 2f, area.max + Vector3.up * 3f);
                var sources = new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(area, Mask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
                // (not people, monsters or items; nor a mesh the builder can't read)
                sources.RemoveAll(s => !(s.component is Collider c) || c.isTrigger || c.GetComponentInParent<PlayerControllerB>() != null ||
                    c.GetComponentInParent<EnemyAI>() != null || c.GetComponentInParent<GrabbableObject>() != null ||
                    (s.shape == NavMeshBuildSourceShape.Mesh && s.sourceObject is Mesh m && !m.isReadable));
                for (int i = 0; i < sources.Count; i++) { var s = sources[i]; s.area = Area; sources[i] = s; }
                var made = new List<NavMeshData>();
                for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                {
                    var st = NavMesh.GetSettingsByIndex(i);
                    // (a half block is a step, as stairs and slabs are; a whole block is climbed by a step link: the game's agent is
                    // a metre tall, and a taller one here would make low places, under the ship's roof, unwalkable)
                    st.agentClimb = Mathf.Max(st.agentClimb, Mathf.Min(0.75f * Plugin.S, st.agentHeight * 0.95f));
                    st.minRegionArea = 0.2f; // (a stair's narrow step top was dropped as too small a region: Unity's default is 2 m2)
                    foreach (var msg in st.ValidationReport(area)) Plugin.Log.LogWarning("[moonnav] " + msg);
                    if (DevLog) Plugin.Log.LogInfo($"[moonnav] agent {st.agentTypeID}: height {st.agentHeight:F2} climb {st.agentClimb:F2} radius {st.agentRadius:F2} slope {st.agentSlope:F0} voxel {(st.overrideVoxelSize ? st.voxelSize : st.agentRadius / 3f):F3}");
                    var data = new NavMeshData(st.agentTypeID);
                    var op = NavMeshBuilder.UpdateNavMeshDataAsync(data, st, sources, area);
                    while (!op.isDone) yield return null;
                    made.Add(data);
                }
                // the new one in, then whatever it replaces out (a monster on the old one is put back on a navmesh)
                foreach (var d in made) g.Data.Add(NavMesh.AddNavMeshData(d));
                for (int i = baked.Count - 1; i >= 0; i--)
                    if (baked[i].Blocks.Intersects(g.Blocks) || !all.Any(x => x.Hash == baked[i].Hash)) { Remove(baked[i], restore: !baked[i].Blocks.Intersects(g.Blocks)); baked.RemoveAt(i); }
                LowObstacles(g.Blocks, true);
                // (the obstacles' cuts in the navmesh follow a moment later: links looked for before then found the block
                // tops still cut away, and a group had 5 step links one time and 18 the next)
                float until = Time.time + 0.6f;
                while (Time.time < until) yield return null;
                int nLinks = Link(g, area);
                int nSteps = StepLinks(g);
                baked.Add(g);
                Reseat(area);
                Last = $"baked {sources.Count} colliders over {area.size.x:F0}x{area.size.z:F0} m, {nLinks} links, {nSteps} step links, {made.Count} agent types, {sw.ElapsedMilliseconds} ms; {baked.Count} groups";
                Plugin.Log.LogInfo("[moonnav] " + Last);
            }
            finally { building = false; }
        }

        /// <summary>Links between this navmesh and the game's wherever the two lie together (every LinkStep).</summary>
        static int Link(Group g, Bounds area)
        {
            int n = 0;
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                int agent = NavMesh.GetSettingsByIndex(i).agentTypeID;
                var ours = new NavMeshQueryFilter { agentTypeID = agent, areaMask = 1 << Area };
                var theirs = new NavMeshQueryFilter { agentTypeID = agent, areaMask = ~((1 << Area) | (1 << ShipNav.Area)) };
                for (float x = area.min.x + 0.5f; x <= area.max.x - 0.5f; x += LinkStep)
                    for (float z = area.min.z + 0.5f; z <= area.max.z - 0.5f; z += LinkStep)
                        for (float y = area.max.y; y > area.min.y; )
                        {
                            if (!NavMesh.SamplePosition(new Vector3(x, y, z), out var a, 0.6f, ours) && !RayDown(new Vector3(x, y, z), area.min.y, ours, out a)) break;
                            if (NavMesh.SamplePosition(a.position, out var gh, 0.35f, theirs) && Mathf.Abs(gh.position.y - a.position.y) < 0.3f)
                            {
                                g.Links.Add(NavMesh.AddLink(new NavMeshLinkData
                                {
                                    startPosition = a.position, endPosition = gh.position, width = 0f, costModifier = -1f,
                                    bidirectional = true, area = 0, agentTypeID = agent,
                                }));
                                n++;
                            }
                            y = a.position.y - 1.2f;
                        }
            }
            return n;
        }

        /// <summary>
        /// A monster goes up a whole block as Minecraft's mobs jump one: a link from the ground (or a lower block) beside a
        /// block onto its top, wherever there's room to stand up there. (The game's navmesh is cut round each block by the
        /// block's obstacle, our navmesh with it, a ring of the agent's radius: so the step itself is never one surface.)
        /// </summary>
        static int StepLinks(Group g) => StepLinks(bi => Ours(bi) && g.Blocks.Intersects(bi.Col.bounds), g.Links);

        /// <summary>Step links for the blocks a test picks (the ship's too, ShipNav), into a list (removed with it).</summary>
        public static int StepLinks(System.Func<BlockInstance, bool> pick, List<NavMeshLinkInstance> into)
        {
            var w = BlockWorld.Instance;
            if (w == null) return 0;
            float S = BlockWorld.S;
            int n = 0;
            var dirs = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            var any = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            int noRoom = 0, noTop = 0, noLow = 0, badRise = 0, noEdge = 0, blocks = 0;
            foreach (var bi in w.Blocks.Values)
            {
                if (bi.Go == null || bi.Col == null || !bi.Col.enabled || !bi.Data.Def.Collides || !pick(bi)) continue;
                blocks++;
                var b = bi.Col.bounds;
                var top = new Vector3(b.center.x, b.max.y, b.center.z);
                // room to stand on it: nothing solid in the two cells above
                int solid = (1 << BlockWorld.SolidLayer) | TerrainCarver.LevelMask;
                if (Physics.CheckBox(top + Vector3.up * S, new Vector3(S * 0.4f, S * 0.95f, S * 0.4f), Quaternion.identity, solid, QueryTriggerInteraction.Ignore)) { noRoom++; continue; }
                // (from the blocks themselves, not the navmesh: the obstacles' cuts in it follow a moment later, and links
                // looked for in it before then were missing from a group one time in two)
                foreach (var d in dirs)
                {
                    // the ground (or a lower block) beside the block, a step out from its edge: straight down from just
                    // under the top's height, as far as a monster can climb
                    var from = top + d * (S * 0.5f + 0.6f) + Vector3.up * 0.05f;
                    if (Physics.CheckSphere(from, 0.05f, solid, QueryTriggerInteraction.Ignore)) { noLow++; continue; } // (a block beside it as high)
                    if (!Physics.Raycast(from, Vector3.down, out var hit, Climb + 0.1f, solid, QueryTriggerInteraction.Ignore)) { noLow++; continue; }
                    float rise = top.y - hit.point.y;
                    if (rise < 0.4f || rise > Climb) { badRise++; continue; }
                    // room to stand down there, and to come up (two cells clear over the spot)
                    if (Physics.CheckBox(hit.point + Vector3.up * (S + 0.05f), new Vector3(0.3f, S * 0.95f, 0.3f), Quaternion.identity, solid, QueryTriggerInteraction.Ignore)) { noEdge++; continue; }
                    var edge = top + d * (S * 0.5f - 0.5f); // (inside the navmesh on top: it stops the agent's radius, 0.35 m, short of the edge)
                    for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
                    {
                        into.Add(NavMesh.AddLink(new NavMeshLinkData
                        {
                            startPosition = hit.point, endPosition = edge, width = 0f, costModifier = -1f,
                            bidirectional = true, area = 0, agentTypeID = NavMesh.GetSettingsByIndex(i).agentTypeID,
                        }));
                        n++;
                    }
                }
            }
            StepWhy = $"blocks {blocks}: no room above {noRoom}; sides: nothing to step from {noLow}, wrong rise {badRise}, no room below {noEdge}; links {n}";
            return n;
        }
        public static string StepWhy = "-";

        static bool RayDown(Vector3 from, float minY, NavMeshQueryFilter f, out NavMeshHit hit)
        {
            for (var p = from; p.y > minY; p += Vector3.down * 1f)
                if (NavMesh.SamplePosition(p, out hit, 0.6f, f)) return true;
            hit = default;
            return false;
        }

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

        /// <summary>The group's blocks cut the game's navmesh with an obstacle each; whole, it would cut ours too, at every
        /// edge of their tops. While ours is there they only reach up a little from the bottom.</summary>
        static void LowObstacles(Bounds blocks, bool low)
        {
            var w = BlockWorld.Instance;
            if (w == null) return;
            var near = blocks; near.Expand(0.2f);
            foreach (var bi in w.Blocks.Values)
            {
                if (bi.Key.Frame != 0 || bi.Obstacle == null || bi.Data.Def.Shape != BlockShape.Cube || bi.Col == null || !near.Intersects(bi.Col.bounds)) continue;
                // (one standing on another placed block has no game navmesh under it to cut; ours sees it as the solid it
                // is. Its obstacle would only cut ours: the top of the block under it, a stair's step away)
                var below = w.Get(bi.Key.Offset((byte)Face.Down));
                bool onBlock = below != null && Ours(below);
                bi.Obstacle.enabled = !(low && onBlock);
                bi.Obstacle.center = low ? new Vector3(0f, -0.425f, 0f) : Vector3.zero;
                bi.Obstacle.size = low ? new Vector3(1.02f, 0.65f, 1.02f) : Vector3.one * 1.02f;
            }
        }

        /// <summary>restore: the blocks' obstacles back to whole (not when a new bake over them replaces it).</summary>
        static void Remove(Group g, bool restore = true)
        {
            if (restore) LowObstacles(g.Blocks, false);
            foreach (var l in g.Links) l.Remove();
            foreach (var d in g.Data) d.Remove();
            g.Links.Clear(); g.Data.Clear();
        }

        public static void Clear()
        {
            foreach (var g in baked) Remove(g);
            baked.Clear();
        }

        public static string Describe() => $"off={Off} groups={baked.Count} links={baked.Sum(g => g.Links.Count)} last: {Last} | steps: {StepWhy}";
    }
}
