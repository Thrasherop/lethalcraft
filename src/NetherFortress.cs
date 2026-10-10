using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using DunGen.Graph;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>
    /// The Nether fortress (#66, phase 2): a second interior, deep below the moon, built by a second instance of the
    /// game's own interior generator (DunGen) from tiles made in code out of Minecraft's blocks: nether-brick corridors,
    /// bridges over lava, corners, crossings, a blaze room, and the portal room you arrive in. The same seed gives the
    /// same fortress on every machine (DunGen's own random stream). Generated synchronously, after the game's interior,
    /// with its own navmesh; none of the game's own interior parts (AI nodes, scrap spots, entrances) are in it, so the
    /// moon's monsters and scrap stay out. See the dungen-second-generator notes for DunGen's pitfalls.
    /// </summary>
    public static class NetherFortress
    {
        public const float Depth = -400f;
        public const int Layer = 8; // "Room": walkable, in the game's navmesh layers and the players' collision
        public static GameObject Root;
        public static Vector3 StartPoint, StartForward = Vector3.forward;
        /// <summary>The portal in the portal room: stand in it to go back.</summary>
        public static Transform Exit;
        /// <summary>Where its monsters walk between (the middle of each tile's floor; not the game's AI nodes).</summary>
        public static GameObject[] Nodes;
        /// <summary>The blaze rooms' spawners: where new blazes come from.</summary>
        public static readonly List<Vector3> Spawners = new List<Vector3>();
        public static bool Contains(Vector3 p) => Root != null && p.y < Depth + 60f && p.y > Depth - 40f;
        public static int TileCount, Seed;
        public static string LastError;
        static readonly List<NavMeshSurface> surfaces = new List<NavMeshSurface>();

        static float S => BlockWorld.S;

        // ------------------------------------------------------------------ tiles (templates, built once)
        class Spec
        {
            public string Name;
            public VoxelMesh V;
            public Vector3 Origin;          // where layout cell (0,0,0) sits, in blocks (below 0 for a pit)
            public Vector3 Min, Max;        // the tile's box, in blocks (doorways on its faces)
            public readonly List<(Vector3 pos, float yaw)> Doors = new List<(Vector3, float)>();
            public readonly List<Vector3> Lights = new List<Vector3>();
            public Action<Transform> Extra;
        }

        static GameObject portalRoom, blazeRoom;
        static List<GameObject> path;
        static readonly List<GameObject> templates = new List<GameObject>();

        static string Brick(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h % 100 < 12 ? "cracked_nether_bricks" : "nether_bricks";
        }

        /// <summary>A closed room's shell: floor, roof, four walls (nether bricks, some cracked).</summary>
        static VoxelMesh Shell(int sx, int sy, int sz)
        {
            var v = new VoxelMesh(sx, sy, sz);
            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                        if (y == 0 || y == sy - 1 || x == 0 || x == sx - 1 || z == 0 || z == sz - 1) v.Set(x, y, z, Brick(x, y, z));
            return v;
        }

        // the doorways: 3 wide, 4 tall, on the floor (layout y 1); `at` is the opening's middle along the wall
        static void DoorBack(Spec s, int at) { s.V.Fill(at - 1, 1, 0, at + 1, 4, 0, null); s.Doors.Add((new Vector3(at + 0.5f, 1, 0), 180f)); }
        static void DoorFront(Spec s, int at) { int z = s.V.SZ - 1; s.V.Fill(at - 1, 1, z, at + 1, 4, z, null); s.Doors.Add((new Vector3(at + 0.5f, 1, s.V.SZ), 0f)); }
        static void DoorLeft(Spec s, int at) { s.V.Fill(0, 1, at - 1, 0, 4, at + 1, null); s.Doors.Add((new Vector3(0, 1, at + 0.5f), 270f)); }
        static void DoorRight(Spec s, int at) { int x = s.V.SX - 1; s.V.Fill(x, 1, at - 1, x, 4, at + 1, null); s.Doors.Add((new Vector3(s.V.SX, 1, at + 0.5f), 90f)); }

        static Spec Room(string name, int sx, int sy, int sz)
        {
            var s = new Spec { Name = name, V = Shell(sx, sy, sz), Min = Vector3.zero, Max = new Vector3(sx, sy, sz) };
            // a glowstone lamp in the roof's middle, its light just under it
            s.V.Set(sx / 2, sy - 1, sz / 2, "glowstone", true);
            s.Lights.Add(new Vector3(sx / 2 + 0.5f, sy - 1.6f, sz / 2 + 0.5f));
            return s;
        }

        static List<Spec> Specs()
        {
            var list = new List<Spec>();
            // a straight corridor (5 wide with its walls, 4 tall inside)
            { var s = Room("Corridor", 5, 6, 5); DoorBack(s, 2); DoorFront(s, 2); list.Add(s); }
            // a corner, a T, a crossing
            { var s = Room("Corner", 5, 6, 5); DoorBack(s, 2); DoorRight(s, 2); list.Add(s); }
            { var s = Room("Junction", 5, 6, 5); DoorBack(s, 2); DoorLeft(s, 2); DoorRight(s, 2); list.Add(s); }
            { var s = Room("Crossing", 7, 6, 7); DoorBack(s, 3); DoorFront(s, 3); DoorLeft(s, 3); DoorRight(s, 3); list.Add(s); }
            // a bridge over a lava trench: the deck with low walls, open above them, lava 5 blocks down, pillars under it
            {
                int sx = 5, sy = 12, sz = 9, deck = 6;
                var v = new VoxelMesh(sx, sy, sz);
                v.Fill(0, deck, 0, sx - 1, deck, sz - 1, "nether_bricks");                 // the deck
                v.Fill(0, deck + 1, 0, 0, deck + 1, sz - 1, "nether_bricks");              // its low walls
                v.Fill(sx - 1, deck + 1, 0, sx - 1, deck + 1, sz - 1, "nether_bricks");
                v.Fill(0, deck + 1, 0, sx - 1, sy - 1, 0, null);
                v.Fill(0, sy - 1, 0, sx - 1, sy - 1, sz - 1, "netherrack");                // a rough roof
                v.Fill(0, 0, 0, sx - 1, 0, sz - 1, "netherrack");                          // the trench: its bed,
                v.Fill(0, 1, 0, sx - 1, 1, sz - 1, "lava", true);                          // lava,
                v.Fill(0, 2, 0, 0, deck - 1, sz - 1, "netherrack");                        // its walls
                v.Fill(sx - 1, 2, 0, sx - 1, deck - 1, sz - 1, "netherrack");
                foreach (int z in new[] { 2, 6 }) v.Fill(2, 2, z, 2, deck - 1, z, "nether_bricks"); // pillars
                var s = new Spec { Name = "Bridge", V = v, Origin = new Vector3(0, -deck, 0), Min = new Vector3(0, -deck, 0), Max = new Vector3(sx, sy - deck, sz) };
                s.Doors.Add((new Vector3(2.5f, 1, 0), 180f)); s.Doors.Add((new Vector3(2.5f, 1, sz), 0f));
                s.Lights.Add(new Vector3(2.5f, -3f, 4.5f)); // (the lava's glow)
                list.Add(s);
            }
            // the blaze room: big, lava in its corners, a spawner on a step in the middle
            {
                var s = Room("BlazeRoom", 11, 8, 11);
                DoorBack(s, 5); DoorFront(s, 5); DoorLeft(s, 5); DoorRight(s, 5);
                foreach (var (x, z) in new[] { (1, 1), (9, 1), (1, 9), (9, 9) }) s.V.Set(x, 0, z, "lava", true);
                s.V.Fill(4, 1, 4, 6, 1, 6, "nether_bricks");
                s.V.Set(5, 2, 5, "spawner");
                s.Lights.Add(new Vector3(1.5f, 1.5f, 1.5f)); s.Lights.Add(new Vector3(9.5f, 1.5f, 9.5f));
                list.Add(s);
            }
            // the portal room: where a portal brings you, its own portal on the back wall
            {
                var s = Room("PortalRoom", 7, 7, 7);
                DoorFront(s, 3);
                s.V.Fill(2, 1, 1, 5, 5, 1, "obsidian");
                s.V.Fill(3, 2, 1, 4, 4, 1, null);
                s.Extra = t =>
                {
                    // the lit portal in the frame (the portal block's own mesh and material)
                    for (int x = 3; x <= 4; x++)
                        for (int y = 2; y <= 4; y++)
                        {
                            var go = new GameObject("portal");
                            go.transform.SetParent(t, false);
                            go.transform.localPosition = new Vector3(x + 0.5f, y + 0.5f, 1.5f) * S;
                            go.transform.localScale = Vector3.one * S;
                            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.For(Blocks.NetherPortal, 0, 0);
                            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = new[] { Atlas.Portal, Atlas.Portal };
                            Atlas.NoDecals(r);
                        }
                    var start = new GameObject("Start"); start.transform.SetParent(t, false);
                    start.transform.localPosition = new Vector3(3.5f, 1.05f, 3.5f) * S;
                    var exit = new GameObject("Exit"); exit.transform.SetParent(t, false);
                    exit.transform.localPosition = new Vector3(4f, 3.5f, 1.5f) * S;
                };
                s.Lights.Add(new Vector3(3.5f, 3f, 2.5f));
                list.Add(s);
            }
            return list;
        }

        static GameObject Make(Spec s)
        {
            var go = new GameObject("LMC_Nether_" + s.Name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            go.layer = Layer;
            s.V.Make("Blocks", go.transform, s.Origin * S, Layer);
            var tile = go.AddComponent<Tile>();
            tile.OverrideAutomaticTileBounds = true;
            tile.TileBoundsOverride = new Bounds((s.Min + s.Max) * 0.5f * S, (s.Max - s.Min) * S);
            int i = 0;
            foreach (var (pos, yaw) in s.Doors)
            {
                var d = new GameObject("Doorway" + i++);
                d.transform.SetParent(go.transform, false);
                d.transform.localPosition = pos * S;
                d.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                var dw = d.AddComponent<Doorway>();
                // a wall across the opening, there unless a tile joins here (DunGen takes it away then)
                var panel = new VoxelMesh(3, 4, 1);
                for (int x = 0; x < 3; x++) for (int y = 0; y < 4; y++) panel.Set(x, y, 0, Brick(x + i * 7, y, 3));
                var wall = panel.Make("Blocker", d.transform, new Vector3(-1.5f, 0f, -1f) * S, Layer);
                dw.BlockerSceneObjects.Add(wall);
            }
            foreach (var lp in s.Lights)
            {
                var lgo = new GameObject("Light");
                lgo.transform.SetParent(go.transform, false);
                lgo.transform.localPosition = lp * S;
                var hd = lgo.AddHDLight(HDLightTypeAndShape.Point);
                hd.EnableShadows(false);
                hd.affectsVolumetric = false;
                hd.lightUnit = LightUnit.Lumen;
                hd.intensity = 2500f;
                hd.range = 14f;
                lgo.GetComponent<Light>().color = new Color(1f, 0.55f, 0.28f);
            }
            s.Extra?.Invoke(go.transform);
            return go;
        }

        static void BuildTemplates()
        {
            if (templates.Count > 0 && templates.All(t => t != null)) return;
            templates.Clear();
            path = new List<GameObject>();
            foreach (var s in Specs())
            {
                var go = Make(s);
                templates.Add(go);
                if (s.Name == "PortalRoom") portalRoom = go;
                else if (s.Name == "BlazeRoom") blazeRoom = go;
                else path.Add(go);
            }
        }

        // ------------------------------------------------------------------ generating
        /// <summary>Generate the fortress for a seed (every machine the same). Replaces any fortress there is.</summary>
        public static bool Generate(int seed)
        {
            Clear();
            LastError = null;
            BuildTemplates();
            var startSet = ScriptableObject.CreateInstance<TileSet>(); startSet.AddTile(portalRoom, 1f, 1f);
            var goalSet = ScriptableObject.CreateInstance<TileSet>(); goalSet.AddTile(blazeRoom, 1f, 1f);
            var mainSet = ScriptableObject.CreateInstance<TileSet>();
            foreach (var t in path)
            {
                float w = t.name.EndsWith("Corridor") ? 3f : t.name.EndsWith("Bridge") ? 1.5f : t.name.EndsWith("Crossing") ? 0.7f : 1.2f;
                mainSet.AddTile(t, w, w);
            }
            mainSet.AddTile(blazeRoom, 0.2f, 0.4f);
            var arch = ScriptableObject.CreateInstance<DungeonArchetype>();
            arch.TileSets.Add(mainSet);
            arch.BranchCount = new IntRange(1, 3);
            arch.BranchingDepth = new IntRange(1, 3);
            var flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.Length = new IntRange(6, 10);
            new DungeonFlowBuilder(flow).AddNode(startSet, "Start").AddLine(arch).AddNode(goalSet, "Goal").Complete();

            Root = new GameObject("LMC_NetherFortress");
            Root.transform.position = new Vector3(0f, Depth, 0f);
            var gen = new DungeonGenerator(Root)
            {
                DungeonFlow = flow, Seed = seed, ShouldRandomizeSeed = false, GenerateAsynchronously = false,
                TriggerPlacement = TriggerPlacementMode.None, MaxAttemptCount = 20,
            };
            gen.AllowTilePooling = false;
            gen.CollisionSettings.AvoidCollisionsWithOtherDungeons = false;
            int retries = 0;
            gen.Retrying += () => { if (++retries > 25) throw new InvalidOperationException("the Nether fortress didn't fit after 25 tries"); };
            foreach (var t in templates) t.SetActive(true);
            try { gen.Generate(); }
            catch (Exception e) { LastError = e.Message; Plugin.Log.LogError("Nether fortress generation failed: " + e); }
            finally { foreach (var t in templates) if (t != null) t.SetActive(false); }
            if (gen.Status != GenerationStatus.Complete || gen.CurrentDungeon == null)
            {
                LastError = LastError ?? "status " + gen.Status;
                UnityEngine.Object.Destroy(Root); Root = null;
                return false;
            }
            Seed = seed;
            var tiles = gen.CurrentDungeon.AllTiles;
            TileCount = tiles.Count;
            var startTile = tiles.FirstOrDefault(t => t.name.Contains("PortalRoom"));
            var startMark = startTile != null ? startTile.transform.Find("Start") : null;
            if (startMark != null) { StartPoint = startMark.position; StartForward = startTile.transform.forward; }
            Exit = startTile != null ? startTile.transform.Find("Exit") : null;
            // (DunGen takes the walls out of the doorways it joined with Destroy, at the end of the frame: the navmesh is baked
            // now, so take them out at once)
            foreach (var c in gen.CurrentDungeon.Connections)
                foreach (var d in new[] { c.A, c.B })
                    if (d != null) foreach (var b in d.GetComponentsInChildren<Transform>(true).Where(x => x != null && x.name == "Blocker").Select(x => x.gameObject).ToList()) UnityEngine.Object.DestroyImmediate(b);
            BakeNavMesh();
            // the monsters' nodes: each tile's floor middle; the spawners
            var nodes = new List<GameObject>();
            Spawners.Clear();
            foreach (var t in tiles)
            {
                var tl = t.GetComponent<Tile>();
                var c = tl != null ? tl.TileBoundsOverride.center : Vector3.zero;
                var n = new GameObject("NetherNode");
                n.transform.SetParent(Root.transform, false);
                n.transform.position = t.transform.TransformPoint(new Vector3(c.x, 1.05f * S, c.z));
                nodes.Add(n);
                if (t.name.Contains("BlazeRoom")) Spawners.Add(t.transform.TransformPoint(new Vector3(3f, 1.1f, 3f) * S));
            }
            Nodes = nodes.ToArray();
            NetherLife.ServerPopulate();
            Plugin.Log.LogInfo($"Nether fortress: {TileCount} tiles (seed {seed}, {retries} retries), start at {StartPoint}");
            return true;
        }

        /// <summary>Its own navmesh, as the game bakes its interior's (RoundManager.BakeDunGenNavMesh), from its colliders.</summary>
        static void BakeNavMesh()
        {
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                var surf = Root.AddComponent<NavMeshSurface>();
                surf.agentTypeID = NavMesh.GetSettingsByIndex(i).agentTypeID;
                surf.collectObjects = CollectObjects.Children;
                surf.layerMask = 1 << Layer;
                surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surf.BuildNavMesh();
                surfaces.Add(surf);
            }
        }

        /// <summary>Is this point in the portal room's portal (2 x 3 blocks, the pane and a little either side)?</summary>
        public static bool InExit(Vector3 world)
        {
            if (Root == null || Exit == null) return false;
            var l = Exit.InverseTransformPoint(world);
            return Mathf.Abs(l.x) <= 1.0f * S && Mathf.Abs(l.y) <= 1.5f * S && Mathf.Abs(l.z) <= 0.6f * S;
        }

        public static void Clear()
        {
            foreach (var s in surfaces) if (s != null) s.RemoveData();
            surfaces.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; TileCount = 0; Exit = null; Nodes = null; Spawners.Clear();
        }

        /// <summary>(dev) every tile: its kind, where it is, which way it faces, how many of its walls (blockers) stand.</summary>
        public static string DevTiles()
        {
            if (Root == null) return "none";
            var sb = new System.Text.StringBuilder();
            foreach (Transform t in Root.transform)
            {
                if (t.GetComponent<Tile>() == null) continue;
                int walls = t.GetComponentsInChildren<Transform>().Count(x => x.name == "Blocker");
                sb.Append($"{t.name.Replace("LMC_Nether_", "").Replace("(Clone)", "")}@{t.position.x:F0},{t.position.z:F0} rot{t.eulerAngles.y:F0} walls={walls} ; ");
            }
            return sb.ToString();
        }

        public static string Describe() =>
            Root == null ? $"none{(LastError != null ? " (last error: " + LastError + ")" : "")}" : $"tiles={TileCount} seed={Seed} start={StartPoint} navSurfaces={surfaces.Count}";
    }
}
