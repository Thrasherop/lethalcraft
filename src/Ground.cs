using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Server-side "natural ground" voxels. Level geometry (moon terrain, rocks, facility walls/floors/ceilings) stays
    /// a normal mesh until you dig; then the dug cube is cut out of every mesh it touches and only the cells bordering
    /// the hole become real blocks (dirt, stone, ores...). Cells that straddle a surface become "molded" blocks whose
    /// open face follows that surface (floor, wall or ceiling), so hole edges have no gaps.
    /// Solid vs. air is decided from the ORIGINAL geometry: underground, inside rock, between facility rooms = solid;
    /// open sky or an enclosed room = air. So you can tunnel anywhere, from the surface all the way down to the facility.
    /// All natural cells live on one world-aligned grid (frame 0, no vertical offset).
    /// </summary>
    public static class Ground
    {
        public const int MoldRes = 5;                // samples per side for molded faces
        static readonly HashSet<Vector3Int> dug = new HashSet<Vector3Int>();
        public static bool IsDug(Vector3Int c) => dug.Contains(c);
        public static IEnumerable<Vector3Int> DugCells => dug;
        static readonly Dictionary<Vector3Int, Info> infoCache = new Dictionary<Vector3Int, Info>();
        static float floorY = float.NaN;

        public static void Reset() { dug.Clear(); infoCache.Clear(); floorY = float.NaN; gridYOff = -1; Facility.Reset(); TerrainCarver.ForgetRenderOnlyCache(); }
        public static int DugCount => dug.Count;

        static float S => BlockWorld.S;
        static float FilmReach => 0.06f * S; // > the thickest surface sliver a cell can hold and still count as air (3% of a block)
        // The natural grid is shifted vertically so the facility's main floor lies exactly on a cell boundary:
        // a two-block tunnel dug from a facility floor then has the full two blocks of headroom (the player is 1.8 blocks tall).
        // Derived from level geometry only, so every client computes the same value.
        static short gridYOff = -1;
        public static short GridYOff
        {
            get
            {
                if (gridYOff >= 0) return gridYOff;
                gridYOff = 0;
                try
                {
                    // the most common floor height (mod one block) under the facility's AI nodes
                    var votes = new Dictionary<int, int>();
                    foreach (var n in GameObject.FindGameObjectsWithTag("AINode"))
                    {
                        if (n == null || !Facility.Contains(n)) continue;
                        if (!Physics.Raycast(n.transform.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore)) continue;
                        int b = Mathf.RoundToInt(Mathf.Repeat(hit.point.y, S) / S * 100f) % 100; // 1/100 block buckets
                        votes[b] = votes.TryGetValue(b, out int v) ? v + 1 : 1;
                    }
                    if (votes.Count > 0)
                    {
                        // smooth over neighbouring buckets so tiny floor differences vote together
                        int best = votes.Keys.OrderByDescending(b => votes.Where(kv => Mathf.Abs(kv.Key - b) <= 2 || Mathf.Abs(kv.Key - b) >= 98).Sum(kv => kv.Value)).ThenBy(b => b).First();
                        gridYOff = (short)Mathf.Clamp(best * 10, 0, 999);
                    }
                    else
                    {
                        var inside = Object.FindObjectsOfType<EntranceTeleport>().Where(e => !e.isEntranceToBuilding && e.entrancePoint != null).OrderBy(e => e.entranceId).FirstOrDefault();
                        if (inside != null && Physics.Raycast(inside.entrancePoint.position + Vector3.up * 1f, Vector3.down, out var hit, 4f, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore))
                            gridYOff = (short)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(hit.point.y, S) / S * 1000f), 0, 999);
                    }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Ground grid offset: " + e.Message); }
                Plugin.Log.LogInfo($"Natural ground grid offset: {gridYOff}/1000 block");
                return gridYOff;
            }
        }
        static float YO => GridYOff / 1000f * S;
        public static BlockKey KeyOf(Vector3Int c) => new BlockKey(0, GridYOff, c);
        static BlockKey Key(Vector3Int c) => KeyOf(c);
        public static Vector3Int CellOf(Vector3 world) => new Vector3Int(Mathf.FloorToInt(world.x / S), Mathf.FloorToInt((world.y - YO) / S), Mathf.FloorToInt(world.z / S));
        public static Vector3 Center(Vector3Int c) => new Vector3((c.x + 0.5f) * S, (c.y + 0.5f) * S + YO, (c.z + 0.5f) * S);
        public static bool IsNaturalGrid(BlockKey k) => k.Frame == 0 && k.YOff == GridYOff;

        enum Kind { Air, Solid, Partial }

        class Info
        {
            public Kind Kind;
            public byte Facing = (byte)Face.Up; // open side of a partial cell
            public byte[] Mold;
            public GameObject Surface;          // nearest level object we're "inside" (material)
            public float Depth;                 // blocks to the nearest open space (for layers and ores)
        }

        static readonly Vector3[] Axes = { Vector3.down, Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }; // = Faces.Dir order

        // ------------------------------------------------------------------ solidity
        /// <summary>
        /// Is a point inside the ground? Six axis rays against the original geometry: open sky above, or an enclosed room
        /// (front faces all around) = air. Everything else (we see the back of a surface: under terrain, inside rock,
        /// in the gap between facility rooms) = solid.
        /// </summary>
        static bool IsSolid(Vector3 p, out GameObject nearest, out float nearestDist)
        {
            nearest = null; nearestDist = float.MaxValue;
            var hits = new GroundRules.Hit[6];
            for (int f = 0; f < 6; f++)
            {
                float max = f == 1 ? 600f : 60f;
                if (!TerrainCarver.RayOriginal(p, Axes[f], max, out float d, out bool front, out var obj))
                {
                    if (f == 1) return false; // open sky
                    continue;
                }
                hits[f] = front ? GroundRules.Hit.Front : GroundRules.Hit.Back;
                if (!front && d < nearestDist && !TerrainCarver.IsUnderlay(obj)) { nearestDist = d; nearest = obj; }
            }
            // nothing below by the mesh rays: a floor made of plain colliders counts as ground under our feet (open air on
            // the Company platform read as underground and TNT filled it with blocks)
            if (hits[0] == GroundRules.Hit.None && TerrainCarver.PlainFloorBelow(p, 4f * S)) hits[0] = GroundRules.Hit.Front;
            bool solid = GroundRules.IsSolid(hits);
            // ground truth from the game: monsters walk on the navmesh, so the space right above it is open air whatever
            // the rays make of the meshes (hollow rock shells read as "inside a rock", walls made of plain colliders let
            // the rays through to the backs of the next rooms)
            if (solid && WalkableAir(p)) solid = false;
            if (!solid) nearest = null;
            return solid;
        }

        /// <summary>Is p in the open space above walkable navmesh (within a player's height, straight above it)?</summary>
        static bool WalkableAir(Vector3 p)
        {
            // most points asked about are deep underground, nowhere near walkable space: one cheap query rules them out
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out _, 2.2f, UnityEngine.AI.NavMesh.AllAreas)) return false;
            // probe straight down at a few depths with a small radius (on ramps the nearest navmesh point is up-slope)
            for (float dy = 0.3f; dy <= 1.95f; dy += 0.4f)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(p + Vector3.down * dy, out var h, 0.45f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                float up = p.y - h.position.y;
                var flat = new Vector2(p.x - h.position.x, p.z - h.position.z);
                if (up >= 0.15f && up <= 2.0f && flat.magnitude <= 0.35f * S) return true;
            }
            return false;
        }

        /// <summary>Solid/air/partial for a cell, with mold heights along its open side. Pure function of the original geometry (cached).</summary>
        static Info Classify(Vector3Int c)
        {
            if (infoCache.TryGetValue(c, out var cached)) return cached;
            if (infoCache.Count > 20000) infoCache.Clear();
            var info = new Info { Kind = Kind.Air };
            var center = Center(c);
            float o = 0.35f * S;
            int n = 0;
            Vector3 airSum = Vector3.zero, solSum = Vector3.zero;
            int airN = 0;
            for (int k = 0; k < 9; k++)
            {
                var off = k == 0 ? Vector3.zero : new Vector3((k & 1) != 0 ? o : -o, (k & 2) != 0 ? o : -o, (k & 4) != 0 ? o : -o);
                if (k == 8) off = new Vector3(-o, -o, -o);
                if (IsSolid(center + off, out var obj, out float d))
                {
                    n++; solSum += off;
                    if (obj != null && (info.Surface == null || d / S < info.Depth)) { info.Surface = obj; info.Depth = d / S; }
                }
                else { airSum += off; airN++; }
            }
            if (n > 0 && info.Surface == null) info.Depth = 30f;

            // which way is open? nearest surface crossing seen from the center, else the air side of the samples
            int facing = -1; float bestD = float.MaxValue; GameObject crossObj = null;
            bool centerSolid = n >= 5;
            for (int f = 0; f < 6; f++)
            {
                if (!TerrainCarver.RayOriginal(center, Axes[f], 0.75f * S, out float d, out bool front, out var xo)) continue;
                // from solid we see the back of the surface in the open direction; from air the front of a surface toward the solid
                int open = centerSolid ? (front ? -1 : f) : (front ? (f ^ 1) : -1);
                if (open >= 0 && d < bestD) { bestD = d; facing = open; crossObj = xo; }
            }
            // no sample inside the ground: only a thin sliver of surface may cross the cell (e.g. a floor just above the cell bottom)
            if (n == 0 && (facing < 0 || bestD >= 0.5f * S)) { infoCache[c] = info; return info; }
            if (info.Surface == null) info.Surface = crossObj;
            // all samples inside the ground still isn't proof of a full cube: on a steep slope the surface can dip below
            // the cell's top edge between the samples, and a cube there poked out of the hillside. Measure the shape
            // (from below, when no surface crosses near the center) and only call it Solid if it fills the cell.
            if (n == 9 && (facing < 0 || bestD > 0.5f * S)) facing = facing >= 0 ? facing : (int)Face.Up;
            if (facing < 0)
            {
                var dir = (airN > 0 ? airSum / airN : Vector3.zero) - solSum / n;
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.up;
                facing = Faces.FromVector(dir);
            }
            info.Facing = (byte)facing;

            // mold: heights measured from the closed side toward the open side, in the same frame the renderer uses
            var rot = Quaternion.FromToRotation(Vector3.up, Axes[facing]);
            var up = Axes[facing];
            var heights = new float[MoldRes * MoldRes];
            bool full = true;
            for (int j = 0; j < MoldRes; j++)
                for (int i = 0; i < MoldRes; i++)
                {
                    float fx = Mathf.Lerp(0.02f, 0.98f, i / (float)(MoldRes - 1)) - 0.5f;
                    float fz = Mathf.Lerp(0.02f, 0.98f, j / (float)(MoldRes - 1)) - 0.5f;
                    var p = center + rot * new Vector3(fx * S, -0.5f * S + 0.01f, fz * S);
                    float h;
                    if (TerrainCarver.RayOriginal(p, up, 3f * S, out float d, out bool front, out _))
                        h = front ? 0f : d + 0.01f;
                    else h = centerSolid ? S : 0f;
                    h = Mathf.Clamp(h, 0f, S);
                    if (h < S - 0.01f) full = false;
                    heights[j * MoldRes + i] = h;
                }
            if (full) { info.Kind = Kind.Solid; infoCache[c] = info; return info; }
            if (heights.Average() < S * 0.03f) { infoCache[c] = info; return info; }
            info.Kind = Kind.Partial;
            info.Mold = heights.Select(h => (byte)Mathf.RoundToInt(Mathf.Clamp01(h / S) * 255f)).ToArray();
            infoCache[c] = info;
            return info;
        }

        // ------------------------------------------------------------------ bedrock / protection
        /// <summary>The bottom of the world: a few blocks under the lowest level geometry (the facility, or the moon).</summary>
        static float FloorY()
        {
            if (!float.IsNaN(floorY)) return floorY;
            float min = float.MaxValue;
            if (Facility.Bounds(out var db)) min = db.min.y;
            else
            {
                var sor = StartOfRound.Instance;
                if (sor != null && sor.currentLevel != null)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(sor.currentLevel.sceneName);
                    if (scene.IsValid())
                        foreach (var mc in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshCollider>()))
                            if (((1 << mc.gameObject.layer) & TerrainCarver.LevelMask) != 0 && mc.bounds.size.y < 400f) min = Mathf.Min(min, mc.bounds.min.y);
                }
            }
            floorY = min == float.MaxValue ? -1000f : min - 6f * S;
            return floorY;
        }

        /// <summary>Cells that can't be dug: the world floor, and anything holding up the ship, doors, entrances, monster vents or uncuttable level geometry.</summary>
        public static bool IsBedrock(Vector3Int c) => BedrockReason(c) != null;

        public static string BedrockReason(Vector3Int c)
        {
            if (TerrainCarver.AtCompany && !Plugin.DigAtCompany.Value) return "company";
            if ((c.y + 1) * S + YO < FloorY()) return "world floor";
            var center = Center(c);
            foreach (var t in Facility.Entrances())
                if (t != null && (t.position - center).sqrMagnitude < (2.2f * S) * (2.2f * S)) return "entrance";
            var sor = StartOfRound.Instance;
            // doors (their panels sit on the interactable layer): a hole through a door frame would bypass the lock
            foreach (var h in Physics.OverlapBox(center, Vector3.one * (S * 0.45f), Quaternion.identity, 1 << 9, QueryTriggerInteraction.Ignore))
                if (h.GetComponentInParent<DoorLock>() != null || h.GetComponentInParent<TerminalAccessibleObject>() != null) return "door " + h.name;
            foreach (var h in Physics.OverlapBox(center, Vector3.one * (S * 0.45f), Quaternion.identity, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<GrabbableObject>() != null) continue;
                if (h.GetComponentInParent<PlayerControllerB>() != null || h.GetComponentInParent<EnemyAI>() != null) continue;
                var go = TerrainCarver.GroundObject(h);
                if (TerrainCarver.CanCarve(go, out _)) continue;
                if (ShipAttach.IsShipCollider(h)) return "ship";
                if (sor != null && sor.elevatorTransform != null && h.transform.IsChildOf(sor.elevatorTransform)) return "ship";
                if (h.GetComponentInParent<DoorLock>() != null || h.GetComponentInParent<EntranceTeleport>() != null || h.GetComponentInParent<TerminalAccessibleObject>() != null) return "door " + h.name;
                if (h.GetComponentInParent<InteractTrigger>() != null) return "interactable " + h.name;
                if (h.GetComponentInParent<EnemyVent>() != null) return "vent " + h.name;
                // uncuttable level shell (or an invisible wall): don't open holes we can't really make
                if (h is MeshCollider && ((1 << h.gameObject.layer) & TerrainCarver.LevelMask) != 0) return "uncuttable " + h.name + " (" + TerrainCarver.WhyNot + ")";
                if (h.GetComponent<Renderer>() == null && h.GetComponentInParent<Renderer>() == null && h.bounds.size.magnitude > 3f * S) return "invisible wall " + h.name;
            }
            return null;
        }

        // ------------------------------------------------------------------ materials
        static BlockDef TopOf(GameObject surface)
        {
            if (surface == null) return Blocks.Stone;
            bool facility = Facility.Contains(surface);
            var b = TerrainCarver.SurfaceBlock(surface);
            if (facility) return b == Blocks.Planks || b == Blocks.StoneBricks ? b : Blocks.Stone; // (never cobblestone: that's what mined stone drops)
            return b ?? Blocks.Dirt;
        }

        /// <summary>What a natural cell is made of: surface material, a soil layer, then stone with ores (richer deeper down).</summary>
        static BlockDef Material(Vector3Int c, Info info)
        {
            var top = TopOf(info.Surface);
            switch (GroundRules.LayerFor(info.Kind == Kind.Partial, info.Depth, TerrainCarver.IsStony(top), top == Blocks.Snow, top == Blocks.Planks))
            {
                case GroundRules.Layer.Top: return top;
                case GroundRules.Layer.Soil: return Blocks.Dirt;
                case GroundRules.Layer.Snow: return Blocks.Snow;
                case GroundRules.Layer.Planks: return Blocks.Planks;
            }
            // no ore at the Company: with digging allowed there, the ship could strip-mine its quota without risking a moon
            if (TerrainCarver.AtCompany) return Blocks.Stone;
            switch (GroundRules.OreFor(c.x, c.y, c.z, info.Depth))
            {
                case GroundRules.Ore.Coal: return Blocks.CoalOre;
                case GroundRules.Ore.Iron: return Blocks.IronOre;
                case GroundRules.Ore.Gold: return Blocks.GoldOre;
                case GroundRules.Ore.Diamond: return Blocks.DiamondOre;
                case GroundRules.Ore.Emerald: return Blocks.EmeraldOre;
            }
            return Blocks.Stone;
        }

        // ------------------------------------------------------------------ opening cells
        /// <summary>Makes sure a natural cell exists as a block if it is ground. Adds ops/molds to the batch.</summary>
        static void Ensure(Vector3Int c, List<Op> ops, Dictionary<BlockKey, byte[]> molds)
        {
            var key = Key(c);
            var world = BlockWorld.Instance;
            if (dug.Contains(c) || world.Has(key) || ops.Any(o => o.Key.Equals(key))) return;
            // a player-built block filling (most of) the same space, on any sub-grid? leave it. A block that only
            // half-overlaps (built on the uneven surface) doesn't stop the ground from closing the gap under it.
            var cellB = new Bounds(Center(c), Vector3.one * S);
            foreach (var h in Physics.OverlapBox(cellB.center, cellB.extents * 0.95f, Quaternion.identity, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponent<BlockRef>() == null) continue;
                var hb = h.bounds;
                var mn = Vector3.Max(hb.min, cellB.min); var mx = Vector3.Min(hb.max, cellB.max);
                var d = mx - mn;
                if (d.x > 0 && d.y > 0 && d.z > 0 && d.x * d.y * d.z > 0.6f * S * S * S) return;
            }
            var info = Classify(c);
            if (info.Kind == Kind.Air) return;
            var def = IsBedrock(c) ? Blocks.Bedrock : Material(c, info);
            // bedrock is molded like any natural block: as a full cube it stuck out above floors and slopes (the
            // protected geometry behind it stays solid either way)
            ops.Add(Op.Set(key, new BlockData(def.Id, info.Kind == Kind.Partial ? info.Facing : (byte)Face.Up, Blocks.NaturalGround)));
            if (info.Kind == Kind.Partial) molds[key] = MoldData.FromHeights(info.Mold, MoldRes);
        }

        /// <summary>Opens a set of cells at once: one neighbour batch and one multi-box cut per level object.</summary>
        static void OpenMany(List<Vector3Int> cells)
        {
            if (cells.Count == 0) return;
            var world = BlockWorld.Instance;
            foreach (var c in cells) dug.Add(c);
            var ops = new List<Op>();
            var molds = new Dictionary<BlockKey, byte[]>();
            foreach (var c in cells)
                for (int f = 0; f < 6; f++) Ensure(c + Faces.Dir[f], ops, molds);
            // every natural block next to a dug cell shows the face toward it (new blocks and ones already there)
            foreach (var c in cells)
                for (int f = 0; f < 6; f++)
                {
                    var nk = Key(c + Faces.Dir[f]);
                    if (!molds.TryGetValue(nk, out var m) && !BlockWorld.Molds.TryGetValue(nk, out m)) continue;
                    var e = MoldData.WithExposed(m, MoldRes, Faces.Opposite((byte)f));
                    if (!ReferenceEquals(e, m) || molds.ContainsKey(nk)) molds[nk] = e;
                }
            if (molds.Count > 0) BlockNet.ServerMolds(molds);
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            const float eps = 0.004f;
            var byObj = new Dictionary<string, (List<Vector3> mins, List<Vector3> maxs)>();
            foreach (var c in cells)
            {
                var mn = new Vector3(c.x * S - eps, c.y * S + YO - eps, c.z * S - eps);
                var mx = new Vector3((c.x + 1) * S + eps, (c.y + 1) * S + YO + eps, (c.z + 1) * S + eps);
                // a surface lying just past the cell's face (a few cm into a neighbour that counts as air) would stay
                // as a paper-thin film over the hole: reach a little into such neighbours
                for (int f = 0; f < 6; f++)
                {
                    var n = c + Faces.Dir[f];
                    if (dug.Contains(n) || world.Has(Key(n)) || Classify(n).Kind != Kind.Air) continue;
                    var d = (Vector3)Faces.Dir[f] * FilmReach;
                    mn = Vector3.Min(mn, mn + d); mx = Vector3.Max(mx, mx + d);
                }
                foreach (var go in TerrainCarver.ObjectsIn(mn, mx))
                {
                    var path = TerrainCarver.PathOf(go);
                    if (!byObj.TryGetValue(path, out var e)) byObj[path] = e = (new List<Vector3>(), new List<Vector3>());
                    e.mins.Add(mn); e.maxs.Add(mx);
                }
            }
            foreach (var kv in byObj)
                BlockNet.ServerCut(new TerrainCarver.Cut { Path = kv.Key, Mins = kv.Value.mins.ToArray(), Maxs = kv.Value.maxs.ToArray() });
            PopUnsupported(cells);
        }

        /// <summary>Torches, levers, dust... that hung on level geometry which was just dug away pop off as drops.</summary>
        static void PopUnsupported(List<Vector3Int> cells)
        {
            var world = BlockWorld.Instance;
            var seen = new HashSet<BlockKey>();
            var ops = new List<Op>();
            foreach (var c in cells)
            {
                foreach (var h in Physics.OverlapBox(Center(c), Vector3.one * (S * 1.1f), Quaternion.identity, (1 << BlockWorld.SolidLayer) | (1 << BlockWorld.NonSolidLayer), QueryTriggerInteraction.Collide))
                {
                    var br = h.GetComponent<BlockRef>();
                    if (br == null || !seen.Add(br.Key)) continue;
                    var bi = world.Get(br.Key);
                    if (bi == null || bi.Data.Def.Solid) continue;
                    var sup = ServerLogic.SupportOf(br.Key, bi.Data);
                    if (world.Has(sup)) continue;
                    var from = world.WorldCenter(br.Key);
                    var dir = (world.WorldCenter(sup) - from).normalized;
                    bool held = false;
                    foreach (var hit in Physics.RaycastAll(from, dir, S * 0.75f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
                        if (hit.collider.GetComponentInParent<BlockRef>() == null && hit.collider.GetComponentInParent<GrabbableObject>() == null && hit.collider.GetComponentInParent<PlayerControllerB>() == null) { held = true; break; }
                    if (held) continue;
                    ops.Add(Op.Remove(br.Key, true));
                    ServerLogic.SpawnDrop(bi.Data.Def, from);
                }
            }
            if (ops.Count > 0) { BlockNet.ServerBroadcastOps(ops); Redstone.MarkDirty(); }
        }

        /// <summary>
        /// An explosion blew up some natural blocks (already removed) and, if carveRaw, also blasts a crater into the
        /// untouched ground around it. Everything is opened in one batch.
        /// </summary>
        public static void Explode(Vector3 center, float radius, List<BlockKey> removedNatural, bool carveRaw)
        {
            var world = BlockWorld.Instance;
            if (world == null) return;
            var cells = new List<Vector3Int>();
            foreach (var k in removedNatural)
                if (IsNaturalGrid(k) && dug.Contains(k.Pos) == false) cells.Add(k.Pos);
            int raw = 0;
            if (carveRaw && radius > 0 && Plugin.AllowDigging.Value && world.WorldFrameAvailable &&
                Physics.OverlapSphere(center, radius, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore).Any(h => TerrainCarver.CanCarve(h, out _)))
            {
                int r = Mathf.CeilToInt(radius / S);
                var cc = CellOf(center);
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dz = -r; dz <= r; dz++)
                        {
                            var c = cc + new Vector3Int(dx, dy, dz);
                            var wc = Center(c);
                            float d = Vector3.Distance(wc, center);
                            if (d > radius) continue;
                            // ragged crater edge
                            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663) ^ (uint)(c.z * 83492791);
                            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                            if (d > radius * 0.7f && (h % 100) < 45) continue;
                            if (dug.Contains(c) || world.Has(Key(c)) || cells.Contains(c)) continue;
                            Info info;
                            if (IsSolid(wc, out var surf, out float sd)) info = new Info { Kind = Kind.Solid, Surface = surf, Depth = surf != null ? sd / S : 30f };
                            else
                            {
                                // the cell holding the ground's surface is partial: it must go too, or the crater keeps its skin
                                info = Classify(c);
                                if (info.Kind == Kind.Air) continue;
                            }
                            if (IsBedrock(c)) continue;
                            cells.Add(c);
                            raw++;
                            if ((h % 100) < 22) ServerLogic.SpawnDrop(Material(c, info), wc); // like Minecraft, some blocks survive as drops
                        }
            }
            OpenMany(cells);
            if (raw > 0) Plugin.Log.LogInfo($"Explosion carved {raw} ground cells");
        }

        /// <summary>The cell a dig at this surface hit removes: the first one behind the surface that is ground, else the thin cell at the surface.</summary>
        public static Vector3Int PickCell(Vector3 point, Vector3 normal, out bool solid)
        {
            normal = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            foreach (var depth in new[] { 0.05f, 0.25f, 0.5f })
            {
                var c = CellOf(point - normal * (depth * S));
                if (BlockWorld.Instance != null && BlockWorld.Instance.Has(Key(c))) { solid = true; return c; }
                if (Classify(c).Kind != Kind.Air) { solid = true; return c; }
            }
            solid = false;
            return CellOf(point - normal * (0.05f * S)); // thin wall / sliver of geometry: still diggable
        }

        /// <summary>Client estimate of what a dig would break (for break time and particles).</summary>
        public static BlockDef EstimateAt(Vector3Int c, GameObject hitObject)
        {
            var info = Classify(c);
            if (info.Kind == Kind.Air) return TopOf(hitObject);
            return Material(c, info);
        }

        /// <summary>First dig into raw ground (no block exists there yet).</summary>
        public static void Dig(ulong sender, Vector3 point, Vector3 normal)
        {
            var world = BlockWorld.Instance;
            if (world == null || !world.WorldFrameAvailable || !Plugin.AllowDigging.Value) return;
            var player = ServerLogic.PlayerFor(sender);
            if (player != null && Vector3.Distance(player.gameplayCamera.transform.position, point) > 9f * S + 3f) return;
            normal = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            if (!Physics.Raycast(point + normal * 0.3f, -normal, out var hit, 0.8f, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore)) return;
            var hitObj = TerrainCarver.GroundObject(hit.collider);
            if (!TerrainCarver.CanCarve(hitObj, out string why)) { if (!string.IsNullOrEmpty(why)) BlockNet.ServerToast(sender, why); return; }
            var c = PickCell(hit.point, normal, out bool solid);
            DigCell(sender, c, hitObj);
        }

        /// <summary>Removes one natural cell (raw ground or its block).</summary>
        public static string DigCell(ulong sender, Vector3Int c, GameObject hitObj = null)
        {
            var world = BlockWorld.Instance;
            var player = ServerLogic.PlayerFor(sender);
            if (world.Has(Key(c))) { var bd = world.Get(Key(c)).Data.Def; ServerLogic.HandleBreak(sender, Key(c), false); return "broke " + bd.Key; } // molded block under the surface
            if (dug.Contains(c)) return "already dug";
            if (IsBedrock(c)) { BlockNet.ServerToast(sender, "Bedrock: can't dig here."); return "bedrock"; }
            var info = Classify(c);
            if (info.Kind == Kind.Air && hitObj == null) return "air";
            var def = info.Kind == Kind.Air ? TopOf(hitObj) : Material(c, info);
            var tool = player != null ? player.currentlyHeldObjectServer as ToolItem : null;
            bool harvest = tool != null ? Blocks.CanHarvest(def, tool.Kind, tool.Tier) : Blocks.CanHarvest(def, ToolKind.None, 0);
            if (GameModes.IsCreative(sender)) harvest = false; // creative: no drops, no XP
            OpenMany(new List<Vector3Int> { c });
            var center = Center(c);
            BlockNet.ServerSound(center, "dig." + Sounds.Family(def), 0.9f, 1f);
            if (harvest) ServerLogic.SpawnDrop(def, center);
            if (harvest && (def.ScrapValueMin > 0 || def == Blocks.CoalOre)) BlockNet.ServerXp(sender, Random.Range(2, 6) + (def == Blocks.DiamondOre || def == Blocks.EmeraldOre ? 5 : 0));
            ServerLogic.Noise(center, 10f, 0.5f);
            return "dug " + def.Key;
        }

        /// <summary>A natural block was removed (mined, exploded...): open the ground there.</summary>
        public static void OnRemoved(BlockKey key)
        {
            if (!IsNaturalGrid(key) || dug.Contains(key.Pos)) return;
            OpenMany(new List<Vector3Int> { key.Pos });
        }

        /// <summary>Server: is this block cell (any grid) solid ground that hasn't been dug or turned into a block yet?</summary>
        public static bool IsUndugGround(BlockKey k)
        {
            var world = BlockWorld.Instance;
            if (k.Frame != 0 || world == null || !world.WorldFrameAvailable) return false;
            var c = CellOf(world.WorldCenter(k));
            if (dug.Contains(c) || world.Has(Key(c))) return false;
            return Classify(c).Kind != Kind.Air;
        }

        public static string RaysAt(Vector3 p)
        {
            var sb = new System.Text.StringBuilder();
            for (int f = 0; f < 6; f++)
            {
                bool hit = TerrainCarver.RayOriginal(p, Axes[f], f == 1 ? 600f : 60f, out float d, out bool front, out var obj);
                sb.Append($"{(Face)f}:{(hit ? (front ? "F" : "B") + d.ToString("F1") + " " + obj.name : "-")}  ");
            }
            sb.Append("solid=" + IsSolid(p, out _, out _));
            return sb.ToString();
        }

        /// <summary>
        /// Dev/tests: places where a dug hole lets you see into the void. Every side of a dug cell must be open (dug, or
        /// real air), a natural block that draws its face toward the hole, a block the player built, or the level's own
        /// geometry. Returns one line per gap.
        /// </summary>
        public static List<string> Gaps(int max = 20)
        {
            var res = new List<string>();
            var world = BlockWorld.Instance;
            if (world == null) return res;
            foreach (var c in dug)
            {
                for (int f = 0; f < 6 && res.Count < max; f++)
                {
                    var n = c + Faces.Dir[f];
                    if (dug.Contains(n)) continue;
                    var k = Key(n);
                    var bi = world.Get(k);
                    if (bi != null)
                    {
                        if (BlockWorld.Molds.TryGetValue(k, out var m) && !MoldData.IsExposed(m, MoldRes, Faces.Opposite((byte)f)))
                            res.Add($"{c}->{(Face)f}: {bi.Data.Def.Key} doesn't draw its face toward the hole");
                        continue;
                    }
                    if (Classify(n).Kind == Kind.Air) continue;
                    // no block: the level's own surface must close that side
                    if (TerrainCarver.RayOriginal(Center(c), Axes[f], 1.05f * S, out _, out _, out _)) continue;
                    if (IsBedrock(n)) continue; // protected: the geometry behind it stays (and draws) as it was
                    res.Add($"{c}->{(Face)f}: nothing closes this side ({Classify(n).Kind})");
                }
                if (res.Count >= max) break;
            }
            return res;
        }

        /// <summary>Dev: per-sample solidity and navmesh evidence for a cell.</summary>
        public static string SampleDebug(Vector3Int c)
        {
            var sb = new System.Text.StringBuilder();
            var center = Center(c); float o = 0.35f * S;
            for (int k = 0; k < 9; k++)
            {
                var off = k == 0 ? Vector3.zero : new Vector3((k & 1) != 0 ? o : -o, (k & 2) != 0 ? o : -o, (k & 4) != 0 ? o : -o);
                if (k == 8) off = new Vector3(-o, -o, -o);
                var p = center + off;
                bool solid = IsSolid(p, out _, out _);
                string nav = UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? $"nav dy={p.y - h.position.y:F2} flat={new Vector2(p.x - h.position.x, p.z - h.position.z).magnitude:F2}" : "no nav";
                sb.Append($"[{k} {(solid ? "S" : "a")} walk={WalkableAir(p)} {nav}] ");
            }
            return sb.ToString();
        }

        /// <summary>Dev/tests: is this cell classified fully solid (ground filling it)?</summary>
        public static bool IsSolidCell(Vector3Int c) => Classify(c).Kind == Kind.Solid;

        /// <summary>(dev) A cell's classification, fresh (not from the cache): kind, open side, shape checksum, material.</summary>
        public static string DevFingerprint(Vector3Int c)
        {
            infoCache.TryGetValue(c, out var old);
            infoCache.Remove(c);
            var i = Classify(c);
            if (old != null) infoCache[c] = old; else infoCache.Remove(c);
            int mold = 0;
            if (i.Mold != null) foreach (var b in i.Mold) mold = mold * 31 + b;
            return $"{c.x},{c.y},{c.z} {i.Kind} f{i.Facing} m{mold} d{i.Depth:F2} {(i.Surface != null ? i.Surface.name : "-")}";
        }

        /// <summary>(dev) A cell classified again now, ignoring what's cached (is a cached answer stale?).</summary>
        public static string DevClassifyFresh(Vector3Int c)
        {
            infoCache.TryGetValue(c, out var old);
            infoCache.Remove(c);
            var fresh = Classify(c);
            if (old != null) infoCache[c] = old; else infoCache.Remove(c);
            return $"cached={(old != null ? old.Kind.ToString() : "-")} fresh={fresh.Kind}";
        }

        public static string Describe(Vector3Int c)
        {
            var i = Classify(c);
            return $"{i.Kind} gridYOff={GridYOff} facing={(Face)i.Facing} surf={(i.Surface != null ? i.Surface.name : "-")} depth={i.Depth:F1} bedrock={BedrockReason(c) ?? "no"} floorY={FloorY():F1} dug={(dug.Contains(c) ? "yes" : "no")}";
        }
    }
}
