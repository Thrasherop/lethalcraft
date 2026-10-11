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
        /// <summary>The portals in the fortress: [0] the portal room's (the main entrance), then the exit rooms' in a fixed order.</summary>
        public static readonly List<Transform> Exits = new List<Transform>();
        /// <summary>Where you arrive by each of those (in front of its portal), and which way you face.</summary>
        public static readonly List<(Vector3 at, Vector3 facing)> Arrivals = new List<(Vector3, Vector3)>();
        /// <summary>Where its monsters walk between (the middle of each tile's floor; not the game's AI nodes).</summary>
        public static GameObject[] Nodes;
        /// <summary>The blaze rooms' spawners: where new blazes come from.</summary>
        public static readonly List<Vector3> Spawners = new List<Vector3>();
        public static bool Contains(Vector3 p) => Root != null && p.y < Depth + 60f && p.y > Depth - 40f;
        public static int TileCount, Seed;
        public enum NetherKind { Fortress, Bastion, Valley }
        /// <summary>
        /// What this day's Nether is (#68), chosen from the seed, the same on every machine: the fortress; a bastion remnant
        /// (the same plan in blackstone, a treasure room of gold for the blaze room, piglins, gold in its loot; [Nether]
        /// BastionChance); or a soul sand valley (soul sand and soul soil, basalt, a fossil for the blaze room, cold blue
        /// light, skeletons and endermen; [Nether] ValleyChance).
        /// </summary>
        public static NetherKind Kind;
        public static bool Bastion => Kind == NetherKind.Bastion;
        public static bool Valley => Kind == NetherKind.Valley;
        /// <summary>(dev) -1: the seed decides; else the kind.</summary>
        public static int DevForceKind = -1;
        static NetherKind building; // (the palette the templates being built use)
        public static string LastError;
        static readonly List<NavMeshSurface> surfaces = new List<NavMeshSurface>();

        static float S => BlockWorld.S;

        // ------------------------------------------------------------------ tiles (templates, built once)
        /// <summary>A placed tile: its transform, its layout and box (blocks, in the tile's own frame).</summary>
        public class PlacedTile { public Transform Tr; public VoxelMesh Layout; public Vector3 Origin, Min, Max; public List<Bounds> Walls; }
        public static readonly List<PlacedTile> Placed = new List<PlacedTile>();
        /// <summary>The Nether's region (NetherGround): the tiles' box, 10 m out to the sides, 15 m under, 20 m over.</summary>
        public static Bounds Region;
        static readonly Dictionary<string, Spec> specsByName = new Dictionary<string, Spec>();

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

        /// <summary>One palette's templates: the tiles of a fortress, or of a bastion.</summary>
        class TemplateSet { public GameObject PortalRoom, GoalRoom, ExitRoom; public readonly List<GameObject> Path = new List<GameObject>(), All = new List<GameObject>(); }
        static readonly Dictionary<NetherKind, TemplateSet> sets = new Dictionary<NetherKind, TemplateSet>();
        static TemplateSet cur;

        static string Brick(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            uint r = h % 100;
            switch (building)
            {
                // a bastion's walls: polished blackstone bricks, some cracked, some plain blackstone, now and then gilded
                case NetherKind.Bastion: return r < 6 ? "gilded_blackstone" : r < 20 ? "cracked_polished_blackstone_bricks" : r < 30 ? "blackstone" : "polished_blackstone_bricks";
                // a soul sand valley's: soul soil and soul sand, some netherrack, now and then basalt
                case NetherKind.Valley: return r < 45 ? "soul_soil" : r < 80 ? "soul_sand" : r < 93 ? "netherrack" : "basalt";
                default: return r < 12 ? "cracked_nether_bricks" : "nether_bricks";
            }
        }

        /// <summary>A block of the plan in this palette (the fortress's own names map to the bastion's).</summary>
        static string Pal(string key)
        {
            if (building == NetherKind.Fortress) return key;
            bool b = building == NetherKind.Bastion;
            switch (key)
            {
                case "nether_bricks": return b ? "polished_blackstone_bricks" : "basalt";
                case "netherrack": return b ? "blackstone" : "soul_soil";
                default: return key;
            }
        }

        /// <summary>The plain wall block of the current Nether's palette (an unused doorway's panel, NetherGround).</summary>
        public static string WallBlock => Bastion ? "polished_blackstone_bricks" : Valley ? "soul_soil" : "nether_bricks";

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
                v.Fill(0, deck, 0, sx - 1, deck, sz - 1, Pal("nether_bricks"));                 // the deck
                v.Fill(0, deck + 1, 0, 0, deck + 1, sz - 1, Pal("nether_bricks"));              // its low walls
                v.Fill(sx - 1, deck + 1, 0, sx - 1, deck + 1, sz - 1, Pal("nether_bricks"));
                v.Fill(0, deck + 2, 0, 0, sy - 2, sz - 1, Pal("netherrack"));                  // rough walls over the low ones
                v.Fill(sx - 1, deck + 2, 0, sx - 1, sy - 2, sz - 1, Pal("netherrack"));
                v.Fill(0, sy - 1, 0, sx - 1, sy - 1, sz - 1, Pal("netherrack"));                // a rough roof
                v.Fill(0, 0, 0, sx - 1, 0, sz - 1, Pal("netherrack"));                          // the trench: its bed,
                v.Fill(0, 1, 0, sx - 1, 1, sz - 1, "lava", true);                               // lava,
                v.Fill(0, 2, 0, 0, deck - 1, sz - 1, Pal("netherrack"));                        // its walls
                v.Fill(sx - 1, 2, 0, sx - 1, deck - 1, sz - 1, Pal("netherrack"));
                foreach (int z in new[] { 2, 6 }) v.Fill(2, 2, z, 2, deck - 1, z, Pal("nether_bricks")); // pillars
                var s = new Spec { Name = "Bridge", V = v, Origin = new Vector3(0, -deck, 0), Min = new Vector3(0, -deck, 0), Max = new Vector3(sx, sy - deck, sz) };
                s.Doors.Add((new Vector3(2.5f, 1, 0), 180f)); s.Doors.Add((new Vector3(2.5f, 1, sz), 0f));
                s.Lights.Add(new Vector3(2.5f, -3f, 4.5f)); // (the lava's glow)
                list.Add(s);
            }
            // the blaze room: big, lava in its corners, a spawner on a step in the middle; a bastion's treasure room
            // instead: the same room with a heap of gold blocks in the middle
            {
                var s = Room(building == NetherKind.Bastion ? "TreasureRoom" : building == NetherKind.Valley ? "FossilRoom" : "BlazeRoom", 11, 8, 11);
                DoorBack(s, 5); DoorFront(s, 5); DoorLeft(s, 5); DoorRight(s, 5);
                foreach (var (x, z) in new[] { (1, 1), (9, 1), (1, 9), (9, 9) }) s.V.Set(x, 0, z, "lava", true);
                if (building == NetherKind.Bastion) { s.V.Fill(4, 1, 4, 6, 1, 6, "gold_block"); s.V.Set(5, 2, 5, "gold_block"); }
                else if (building == NetherKind.Valley)
                {
                    // a fossil, Minecraft's soul sand valley's: a spine of bone blocks and ribs arching over it
                    s.V.Fill(5, 1, 2, 5, 1, 8, "bone_block");
                    foreach (int z in new[] { 3, 5, 7 })
                    {
                        s.V.Fill(3, 1, z, 3, 3, z, "bone_block"); s.V.Fill(7, 1, z, 7, 3, z, "bone_block"); s.V.Fill(4, 3, z, 6, 3, z, "bone_block");
                    }
                }
                else
                {
                    s.V.Fill(4, 1, 4, 6, 1, 6, "nether_bricks");
                    s.V.Set(5, 2, 5, "spawner");
                }
                s.Lights.Add(new Vector3(1.5f, 1.5f, 1.5f)); s.Lights.Add(new Vector3(9.5f, 1.5f, 9.5f));
                list.Add(s);
            }
            // the portal room (where a portal brings you) and the exit rooms (fire exits, at the branches' ends): a portal in
            // an obsidian frame on the back wall; an exit room's is lit only while it has a portal on the moon to go to
            // a stair hall: up three blocks over six, steps every two (Minecraft's fortress stairs), with an invisible ramp
            // resting on the steps' edges so you (and the monsters) walk up it
            {
                int sx = 5, sy = 9, sz = 9;
                var v = new VoxelMesh(sx, sy, sz);
                for (int x = 0; x < sx; x++)
                    for (int y = 0; y < sy; y++)
                        for (int z = 0; z < sz; z++)
                            if (y == 0 || y == sy - 1 || x == 0 || x == sx - 1 || z == 0 || z == sz - 1) v.Set(x, y, z, Brick(x, y, z));
                v.Fill(1, 1, 3, 3, 1, 4, Pal("nether_bricks"));   // the steps: tops at 2, 3, then 4 (the upper floor)
                v.Fill(1, 1, 5, 3, 2, 6, Pal("nether_bricks"));
                v.Fill(1, 1, 7, 3, 3, 8, Pal("nether_bricks"));
                v.Set(2, sy - 1, 4, "glowstone", true);
                var s2 = new Spec { Name = "Stairs", V = v, Min = Vector3.zero, Max = new Vector3(sx, sy, sz) };
                v.Fill(1, 1, 0, 3, 4, 0, null); s2.Doors.Add((new Vector3(2.5f, 1, 0), 180f));                     // in at the bottom
                v.Fill(1, 4, sz - 1, 3, 7, sz - 1, null); s2.Doors.Add((new Vector3(2.5f, 4, sz), 0f));            // out at the top
                s2.Lights.Add(new Vector3(2.5f, sy - 1.6f, 4.5f));
                s2.Extra = t =>
                {
                    // the ramp: from the floor at z 1 up to the top step's edge at z 7 (through the steps' corners), then flat
                    var r = new GameObject("Ramp"); r.transform.SetParent(t, false); r.layer = Layer;
                    var a0 = new Vector3(2.5f, 1f, 1f) * S; var a1 = new Vector3(2.5f, 4f, 7f) * S;
                    r.transform.localPosition = (a0 + a1) * 0.5f;
                    r.transform.localRotation = Quaternion.LookRotation(a1 - a0, Vector3.up);
                    var c = r.AddComponent<BoxCollider>();
                    c.size = new Vector3(3f * S, 0.05f, (a1 - a0).magnitude);
                    c.center = new Vector3(0f, -0.025f, 0f);
                };
                list.Add(s2);
            }
            list.Add(PortalRoomSpec("PortalRoom"));
            list.Add(PortalRoomSpec("ExitRoom"));
            return list;
        }

        static Spec PortalRoomSpec(string name)
        {
            var s = Room(name, 7, 7, 7);
            DoorFront(s, 3);
            s.V.Fill(2, 1, 1, 5, 5, 1, "obsidian");
            s.V.Fill(3, 2, 1, 4, 4, 1, null);
            s.Extra = t =>
            {
                // the portal in the frame (the portal block's own mesh and material)
                var panes = new GameObject("LMC_Panes"); panes.transform.SetParent(t, false);
                for (int x = 3; x <= 4; x++)
                    for (int y = 2; y <= 4; y++)
                    {
                        var go = new GameObject("LMC_PortalPane");
                        go.transform.SetParent(panes.transform, false);
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
            return s;
        }

        static GameObject Make(Spec s)
        {
            var go = new GameObject((building == NetherKind.Bastion ? "LMC_Bastion_" : building == NetherKind.Valley ? "LMC_Valley_" : "LMC_Nether_") + s.Name);
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
                // (a soul sand valley's light is soul fire's: cold blue)
                lgo.GetComponent<Light>().color = building == NetherKind.Valley ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.55f, 0.28f);
            }
            s.Extra?.Invoke(go.transform);
            return go;
        }

        static void BuildTemplates(NetherKind kind)
        {
            if (sets.TryGetValue(kind, out var have) && have.All.Count > 0 && have.All.All(t => t != null)) { cur = have; return; }
            var set = new TemplateSet();
            building = kind;
            try
            {
                foreach (var s in Specs())
                {
                    var go = Make(s);
                    set.All.Add(go);
                    specsByName[go.name] = s;
                    if (s.Name == "PortalRoom") set.PortalRoom = go;
                    else if (s.Name == "ExitRoom") set.ExitRoom = go;
                    else if (s.Name == "BlazeRoom" || s.Name == "TreasureRoom" || s.Name == "FossilRoom") set.GoalRoom = go;
                    else set.Path.Add(go);
                }
            }
            finally { building = NetherKind.Fortress; }
            sets[kind] = set;
            cur = set;
        }

        /// <summary>What this seed makes (every machine decides the same).</summary>
        public static NetherKind KindOfSeed(int seed)
        {
            if (DevForceKind >= 0) return (NetherKind)DevForceKind;
            double r = new System.Random(seed ^ 0x6873).NextDouble();
            float b = Plugin.NetherBastionChance.Value, v = Plugin.NetherValleyChance.Value;
            return r < b ? NetherKind.Bastion : r < b + v ? NetherKind.Valley : NetherKind.Fortress;
        }

        // ------------------------------------------------------------------ generating
        /// <summary>
        /// Generate the fortress for a seed (every machine the same). Replaces any fortress there is. Now and then a seed
        /// grows no branches, so no exit rooms (fire exits): then the next seeds are tried, the same way on every machine.
        /// </summary>
        public static bool Generate(int seed)
        {
            bool ok = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                ok = GenerateLayout(seed + attempt * 7919);
                if (!ok || Exits.Count >= 2) break;
                Plugin.Log.LogInfo($"[nether] seed {seed + attempt * 7919}: no exit rooms ({TileCount} tiles), trying the next");
            }
            if (ok) NetherLife.ServerPopulate();
            return ok;
        }

        static bool GenerateLayout(int seed)
        {
            Clear();
            LastError = null;
            Kind = KindOfSeed(seed);
            BuildTemplates(Kind);
            var startSet = ScriptableObject.CreateInstance<TileSet>(); startSet.AddTile(cur.PortalRoom, 1f, 1f);
            var goalSet = ScriptableObject.CreateInstance<TileSet>(); goalSet.AddTile(cur.GoalRoom, 1f, 1f);
            var mainSet = ScriptableObject.CreateInstance<TileSet>();
            foreach (var t in cur.Path)
            {
                // (junctions and crossings often enough that the branches, and their exit rooms, have somewhere to start)
                float w = t.name.EndsWith("Corridor") ? 2.5f : t.name.EndsWith("Bridge") ? 1.5f : t.name.EndsWith("Crossing") ? 1.2f : t.name.EndsWith("Junction") ? 2f : t.name.EndsWith("Stairs") ? 0.8f : 1.2f;
                mainSet.AddTile(t, w, w);
            }
            mainSet.AddTile(cur.GoalRoom, 0.2f, 0.4f);
            var arch = ScriptableObject.CreateInstance<DungeonArchetype>();
            arch.TileSets.Add(mainSet);
            arch.BranchCount = new IntRange(2, 3);
            arch.BranchingDepth = new IntRange(1, 3);
            // every branch ends in an exit room (a fire exit for another portal on the moon)
            var exitSet = ScriptableObject.CreateInstance<TileSet>(); exitSet.AddTile(cur.ExitRoom, 1f, 1f);
            arch.BranchCapType = BranchCapType.InsteadOf;
            arch.BranchCapTileSets.Add(exitSet);
            var flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.Length = new IntRange(6, 10);
            new DungeonFlowBuilder(flow).AddNode(startSet, "Start").AddLine(arch).AddNode(goalSet, "Goal").Complete();

            Root = new GameObject("LMC_Nether" + Kind);
            // (on the block grid: every fortress block then fills whole cells, and digging cuts it cell by cell)
            float yo = Ground.GridYOffMetres;
            Root.transform.position = new Vector3(0f, Mathf.Round((Depth - yo) / S) * S + yo, 0f);
            var gen = new DungeonGenerator(Root)
            {
                DungeonFlow = flow, Seed = seed, ShouldRandomizeSeed = false, GenerateAsynchronously = false,
                TriggerPlacement = TriggerPlacementMode.None, MaxAttemptCount = 20,
            };
            gen.AllowTilePooling = false;
            gen.CollisionSettings.AvoidCollisionsWithOtherDungeons = false;
            int retries = 0;
            gen.Retrying += () => { if (++retries > 25) throw new InvalidOperationException("the Nether fortress didn't fit after 25 tries"); };
            foreach (var t in cur.All) t.SetActive(true);
            try { gen.Generate(); }
            catch (Exception e) { LastError = e.Message; Plugin.Log.LogError("Nether fortress generation failed: " + e); }
            finally { foreach (var t in cur.All) if (t != null) t.SetActive(false); }
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
            Exits.Clear(); Arrivals.Clear();
            foreach (var t in new[] { startTile }.Concat(tiles.Where(t => t.name.Contains("ExitRoom"))))
            {
                if (t == null) continue;
                var ex = t.transform.Find("Exit"); var st = t.transform.Find("Start");
                if (ex == null || st == null) continue;
                Exits.Add(ex); Arrivals.Add((st.position, t.transform.forward));
                if (Exits.Count > 1) SetExitLit(Exits.Count - 1, false);
            }
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
            // its ground: every tile's layout and box, and the region around them
            Placed.Clear();
            bool first = true;
            foreach (var t in tiles)
            {
                var key = t.name.Replace("(Clone)", "").Trim();
                if (!specsByName.TryGetValue(key, out var sp)) continue;
                var walls = t.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Blocker").Select(r => r.bounds).ToList();
                Placed.Add(new PlacedTile { Tr = t.transform, Layout = sp.V, Origin = sp.Origin, Min = sp.Min, Max = sp.Max, Walls = walls });
                var tl = t.GetComponent<Tile>();
                var wb = new Bounds(t.transform.TransformPoint(tl.TileBoundsOverride.center), Vector3.zero);
                foreach (var corner in new[] { tl.TileBoundsOverride.min, tl.TileBoundsOverride.max, new Vector3(tl.TileBoundsOverride.min.x, 0, tl.TileBoundsOverride.max.z), new Vector3(tl.TileBoundsOverride.max.x, 0, tl.TileBoundsOverride.min.z) })
                    wb.Encapsulate(t.transform.TransformPoint(corner));
                if (first) { Region = wb; first = false; } else Region.Encapsulate(wb);
            }
            Region.min = new Vector3(Region.min.x - 10f, Region.min.y - 15f, Region.min.z - 10f);
            Region.max = new Vector3(Region.max.x + 10f, Region.max.y + 20f, Region.max.z + 10f);
            NetherGround.Reset();
            Ground.ForgetClassified();
            TerrainCarver.ForgetUnder(Root.transform);
            Plugin.Log.LogInfo($"Nether {Kind}: {TileCount} tiles (seed {seed}, {retries} retries), start at {StartPoint}");
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
        public static bool InExit(Vector3 world) => ExitAt(world) >= 0;

        /// <summary>Which of the fortress's lit portals this point is in (-1: none).</summary>
        public static int ExitAt(Vector3 world)
        {
            if (Root == null) return -1;
            for (int i = 0; i < Exits.Count; i++)
            {
                var e = Exits[i];
                if (e == null || !ExitLit(i)) continue;
                var l = e.InverseTransformPoint(world);
                if (Mathf.Abs(l.x) <= 1.0f * S && Mathf.Abs(l.y) <= 1.5f * S && Mathf.Abs(l.z) <= 0.6f * S) return i;
            }
            return -1;
        }

        public static bool ExitLit(int i)
        {
            if (i == 0) return true;
            var panes = i < Exits.Count && Exits[i] != null ? Exits[i].parent.Find("LMC_Panes") : null;
            return panes != null && panes.gameObject.activeSelf;
        }

        public static void SetExitLit(int i, bool lit)
        {
            var panes = i < Exits.Count && Exits[i] != null ? Exits[i].parent.Find("LMC_Panes") : null;
            if (panes != null && panes.gameObject.activeSelf != lit) panes.gameObject.SetActive(lit);
        }

        public static void Clear()
        {
            // its monsters go with it (theirs are its nodes: they'd search among destroyed ones)
            if (Root != null && BlockNet.IsServer && RoundManager.Instance != null)
                foreach (var e in RoundManager.Instance.SpawnedEnemies.ToList())
                    if (e != null && !e.isEnemyDead && Contains(e.transform.position))
                        try { e.KillEnemyOnOwnerClient(overrideDestroy: true); } catch (Exception ex) { Plugin.Log.LogWarning("[nether] " + ex.Message); }
            foreach (var s in surfaces) if (s != null) s.RemoveData();
            surfaces.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (Root != null) TerrainCarver.ForgetUnder(Root.transform);
            Root = null; TileCount = 0; Exit = null; Nodes = null; Spawners.Clear(); Placed.Clear(); Exits.Clear(); Arrivals.Clear();
            Ground.ForgetClassified();
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
                sb.Append($"{t.name.Replace("LMC_Nether_", "").Replace("LMC_Bastion_", "").Replace("LMC_Valley_", "").Replace("(Clone)", "")}@{t.position.x:F0},{t.position.z:F0} rot{t.eulerAngles.y:F0} walls={walls} ; ");
            }
            return sb.ToString();
        }

        public static string Describe() =>
            Root == null ? $"none{(LastError != null ? " (last error: " + LastError + ")" : "")}" : $"tiles={TileCount} seed={Seed} kind={Kind} bastion={Bastion} start={StartPoint} navSurfaces={surfaces.Count} exits={Exits.Count} lit={Enumerable.Range(0, Exits.Count).Count(ExitLit)}";
    }
}
