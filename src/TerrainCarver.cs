using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LethalMinecraft
{
    using PV = MeshClip.PV;

    /// <summary>
    /// Makes level geometry (moon terrain, rocks, facility floors/walls/ceilings) editable and cuts exact
    /// block-sized cubes out of it. Works with readable meshes, meshes locked on the GPU (read back once), and
    /// static-batched objects (drawn by a proxy renderer from an editable copy).
    /// Also answers "what surface is there?" questions against the ORIGINAL geometry, so classification never
    /// changes as holes are dug.
    /// </summary>
    public static class TerrainCarver
    {
        public struct Cut
        {
            public string Path;
            public Vector3[] Mins, Maxs; // world-space boxes (one per removed cell)
        }

        class Carved
        {
            public string Path;
            public GameObject Go;
            public MeshCollider OrigCollider;   // may be null (render-only geometry)
            public Mesh RenderMesh;
            public Matrix4x4 L2W, NormalMat;
            public List<Vector3> Pos = new List<Vector3>(), Nrm = new List<Vector3>();
            public List<Vector4> Tan = new List<Vector4>();
            public List<Color> Col = new List<Color>();
            public List<Vector4>[] Uv = new List<Vector4>[8];
            public int[] UvDim = new int[8];            // 2, 3 or 4 components, as in the source mesh
            public List<Vector3> World = new List<Vector3>();
            public List<int>[] Sub;
            public Dictionary<Vector2Int, Chunk> Chunks = new Dictionary<Vector2Int, Chunk>();
            // original triangles (for stable queries), spatially indexed by every chunk their XZ bounds touch
            public List<(int a, int b, int c, bool up, Vector3 n)> OrigTris = new List<(int, int, int, bool, Vector3)>();
            public Dictionary<Vector2Int, List<int>> OrigIndex = new Dictionary<Vector2Int, List<int>>();
            public List<Vector3> TriMin = new List<Vector3>(), TriMax = new List<Vector3>(); // each original triangle's bounds
            public int[] Seen; public int Stamp;        // triangles a query already tested (stamped, no per-query set)
            public Bounds WorldBounds;
            public bool Ready;
            public float ChunkSize = MinChunkSize;
            public int SourceSubmeshes;                 // what the original mesh had (for the integrity check)
            public float OrigArea = -1f;                // surface area before any cut
            public int CutBoxes;                        // cell boxes cut out of it so far
        }

        class Chunk
        {
            public GameObject Go;
            public MeshCollider Col;
            public Mesh Mesh;
            public HashSet<(int, int, int)> Tris = new HashSet<(int, int, int)>();
            public bool Dirty = true;
        }

        const float MinChunkSize = 16f;
        static readonly Dictionary<string, Carved> carved = new Dictionary<string, Carved>();
        public static readonly List<Cut> Cuts = new List<Cut>();
        static readonly Dictionary<int, Mesh> gpuCopies = new Dictionary<int, Mesh>();
        public const int LevelMask = (1 << 0) | (1 << 8) | (1 << 25);
        // also cut decoration (MiscLevelGeometry, Foliage) where it overlaps a dug cell; it never counts as solid ground
        public const int CarveMask = LevelMask | (1 << 24) | (1 << 10);
        const int InvisibleBoxMask = (1 << 8) | (1 << 11) | (1 << 28);

        public static void Reset()
        {
            carved.Clear();
            terrains = null;
            clonedData.Clear();
            terrainsConverted = false; levelBoxesConverted = false;
            terrainMats.Clear();
            Cuts.Clear();
            meshData.Clear();
            renderOnly = null;
        }

        /// <summary>The material the surface of this object turns into when dug.</summary>
        public static BlockDef SurfaceBlock(Collider c) => SurfaceBlock(GroundObject(c));

        public static BlockDef SurfaceBlock(GameObject go)
        {
            switch (go?.tag)
            {
                case "Grass": return Blocks.Grass;
                case "Gravel": return Blocks.Dirt;
                case "Puddle": return Blocks.Dirt;
                case "Snow": return Blocks.Snow;
                case "Rock": return Blocks.Stone;
                case "Concrete": return Blocks.Stone;
                case "Tiles": return Blocks.StoneBricks;
                case "Carpet": return Blocks.Stone;
                case "Wood": return Blocks.Planks;
            }
            return null;
        }

        public static bool IsStony(BlockDef top) => top == Blocks.Stone || top == Blocks.Cobblestone || top == Blocks.StoneBricks;

        public static GameObject GroundObject(Collider c) => c != null && c.name == "LMC_GroundChunk" && c.transform.parent != null ? c.transform.parent.gameObject : c?.gameObject;

        static bool InCurrentLevel(GameObject go)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase || sor.currentLevel == null) return false;
            // moon scene, or the facility (generated into the main scene under the dungeon root)
            if (go.scene.name == sor.currentLevel.sceneName) return true;
            var dg = RoundManager.Instance != null && RoundManager.Instance.dungeonGenerator != null ? RoundManager.Instance.dungeonGenerator.Root : null;
            return dg != null && go.transform.IsChildOf(dg.transform);
        }

        static bool Excluded(GameObject go)
        {
            if (go.name.StartsWith("LMC_")) return true;
            if (go.GetComponentInParent<BlockRef>() != null) return true;
            if (go.GetComponentInParent<GrabbableObject>() != null) return true;
            if (go.GetComponentInParent<PlayerControllerB>() != null) return true;
            if (go.GetComponentInParent<EnemyAI>() != null) return true;
            if (go.GetComponentInParent<Rigidbody>() != null) return true;
            if (go.GetComponentInParent<DoorLock>() != null) return true;
            if (go.GetComponentInParent<EntranceTeleport>() != null) return true;
            if (go.GetComponentInParent<InteractTrigger>() != null) return true;
            if (go.GetComponentInParent<EnemyVent>() != null) return true; // monster vents: the cover animates open, and cutting leaves it floating
            if (IsWaterSurface(go)) return true; // a lake's surface stays: a hole dug at the shore shows water up to the lake's level
            if (go.GetComponentInParent<TerminalAccessibleObject>() != null) return true; // big security doors, turrets, mines
            var sor = StartOfRound.Instance;
            if (sor != null && sor.elevatorTransform != null && go.transform.IsChildOf(sor.elevatorTransform)) return true;
            return false;
        }

        /// <summary>A lake's or river's surface: a render-only plane (no collider) named or parented as water.</summary>
        static bool IsWaterSurface(GameObject go)
        {
            if (go.GetComponent<Collider>() != null) return false;
            for (var t = go.transform; t != null; t = t.parent)
                if (t.name.IndexOf("water", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool CanCarve(Collider c, out string why) => CanCarve(GroundObject(c), out why);

        public static string WhyNot = "";

        public static bool AtCompany
        {
            get
            {
                var lvl = StartOfRound.Instance != null ? StartOfRound.Instance.currentLevel : null;
                return lvl != null && (lvl.sceneName == "CompanyBuilding" || !lvl.spawnEnemiesAndScrap && lvl.levelID == 3);
            }
        }

        /// <summary>Safety underlays below the real terrain: solid like rock, but never a "surface" material.</summary>
        public static bool IsUnderlay(GameObject go) => go != null && go.name.StartsWith("OutOfBounds");

        public static bool CanCarve(GameObject go, out string why)
        {
            why = ""; WhyNot = "ok";
            if (go == null) { why = "Too hard to dig here."; WhyNot = "null"; return false; }
            if (carved.TryGetValue(PathOf(go), out var cv) && cv.Ready) return true;
            if (IsInvisibleFacilityCollider(go)) return true; // boxes are cut as box-shaped mesh colliders
            if (((1 << go.layer) & CarveMask) == 0 || Excluded(go)) { why = "Too hard to dig here."; WhyNot = "layer/excluded"; return false; }
            if (ShipAttach.IsShipCollider(go.GetComponent<Collider>())) { why = "You can't dig up the ship."; WhyNot = "ship"; return false; }
            if (!InCurrentLevel(go)) { why = "Too hard to dig here."; WhyNot = "not in level scene " + go.scene.name; return false; }
            if (AtCompany && !Plugin.DigAtCompany.Value) { why = "You can't dig at the Company."; WhyNot = "company"; return false; }
            if (go.GetComponent<Terrain>() != null) { ConvertTerrains(); why = "Too hard to dig here."; WhyNot = "unconverted terrain"; return false; } // dig its mesh copy instead
            if (SourceMesh(go, out _) == null) { why = "Too hard to dig here."; WhyNot = "no source mesh"; return false; }
            return true;
        }

        /// <summary>The geometry we edit for this object: collision mesh if readable, else the render mesh (read back from the GPU if needed).</summary>
        static Mesh SourceMesh(GameObject go, out bool proxy)
        {
            proxy = false;
            if (IsInvisibleFacilityBox(go) || IsLevelBox(go)) BoxToMeshCollider(go);
            var mc = go.GetComponent<MeshCollider>();
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            // a convex collider on static level geometry (mineshaft doorway rocks) is fine: its cut pieces become
            // ordinary (concave) static colliders. Only moving bodies need convex ones.
            if (mc != null && mc.convex && go.GetComponentInParent<Rigidbody>() != null) return null;
            if (mc != null && mc.sharedMesh != null && mr == null) return mc.sharedMesh.isReadable ? mc.sharedMesh : GpuCopy(mc.sharedMesh); // collision-only geometry
            bool Drawable(Mesh m) => m != null && m.uv.Length > 0 && m.subMeshCount <= mr.sharedMaterials.Length;
            if (mc != null && mc.sharedMesh != null && mc.sharedMesh.isReadable)
            {
                if (mf == null) return null;
                if (mf.sharedMesh == mc.sharedMesh) return mc.sharedMesh;
                // static batched / LOD render mesh: draw the collision mesh instead, if it carries render data
                if (Drawable(mc.sharedMesh)) { proxy = true; return mc.sharedMesh; }
                // else (e.g. mineshaft doorway rocks: a bare collision hull): cut the visible mesh, whose shape then
                // becomes the collision too
            }
            if (mf == null || mr == null || mf.sharedMesh == null) return null;
            // solid through a box/capsule/sphere collider: cutting only the visual would leave an invisible wall
            if (mc == null && go.GetComponents<Collider>().Any(c => c.enabled && !c.isTrigger)) return null;
            if (mc != null && mc.sharedMesh != null && (mr.isPartOfStaticBatch || mf.sharedMesh != mc.sharedMesh))
            {
                // locked collision mesh + batched render: read the collision mesh back from the GPU and draw it with a proxy
                var g = mc.sharedMesh.isReadable ? mc.sharedMesh : GpuCopy(mc.sharedMesh);
                if (Drawable(g)) { proxy = true; return g; }
            }
            if (mr.isPartOfStaticBatch) return null; // locked inside a shared batch
            if (mf.sharedMesh.isReadable) return mf.sharedMesh;
            return GpuCopy(mf.sharedMesh);
        }

        /// <summary>Invisible collision boxes that are part of the facility shell (e.g. mineshaft dead-end blockers).</summary>
        /// <summary>
        /// Invisible collision that's part of the facility itself: wall boxes, gap fillers, stair ramps (no visible
        /// renderer of their own), but not the collision proxy of a visible prop such as a shelf or a server rack.
        /// </summary>
        static bool IsInvisibleFacilityCollider(GameObject go)
        {
            if (go == null || ((1 << go.layer) & InvisibleBoxMask) == 0) return false;
            var r = go.GetComponent<Renderer>();
            if (r != null && r.enabled) return false;
            var col = go.GetComponent<Collider>();
            if (col == null || !col.enabled || col.isTrigger) return false;
            if (!(col is BoxCollider) && !(col is MeshCollider mc && !mc.convex && mc.sharedMesh != null)) return false;
            if (!Facility.Contains(go) || Excluded(go)) return false;
            // a prop's collision proxy: a small visible object under the same parent occupying the same space
            var parent = go.transform.parent;
            if (parent != null)
            {
                var cb = col.bounds;
                foreach (var pr in parent.GetComponentsInChildren<Renderer>())
                    if (pr != null && pr.enabled && pr.gameObject != go && pr.bounds.size.magnitude < 6f && pr.bounds.Intersects(cb)) return false;
            }
            return true;
        }

        static bool IsInvisibleFacilityBox(GameObject go) => IsInvisibleFacilityCollider(go) && go.GetComponent<BoxCollider>() != null && go.GetComponent<MeshCollider>() == null;

        /// <summary>
        /// A big visible box that's part of the level (#73: modded moons such as Kast build their ground from scaled
        /// cubes, solid through a box collider, so the carver found nothing it could cut). Not a prop: big, on a level
        /// layer, drawn, and its only collider. It's cut like an invisible facility box (a box-shaped mesh collider), and
        /// drawn by the carver's proxy with the object's own material.
        /// </summary>
        static bool IsLevelBox(GameObject go)
        {
            if (go == null || ((1 << go.layer) & LevelMask) == 0 || go.GetComponent<MeshCollider>() != null) return false;
            var bc = go.GetComponent<BoxCollider>();
            if (bc == null || !bc.enabled || bc.isTrigger) return false;
            if (go.GetComponents<Collider>().Length != 1 || Excluded(go)) return false;
            var size = bc.bounds.size;
            if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < 6f) return false;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null && go.GetComponent<Renderer>() == null) return HullVisual(go, bc.bounds) != null; // (a model's invisible hull)
            return mr != null && mr.enabled && !IsPropVisual(mr);
        }

        /// <summary>
        /// The model an invisible box hull stands for (#57: E Gypt builds its blocks as a drawn model plus a separate box
        /// collider, "UCX_..."): a drawn mesh under the same parent, about the box's size and in the same place.
        /// </summary>
        static MeshRenderer HullVisual(GameObject go, Bounds cb)
        {
            var parent = go.transform.parent;
            if (parent == null) return null;
            MeshRenderer best = null;
            foreach (var r in parent.GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null || !r.enabled || r.gameObject == go || r.name == "LMC_GroundProxy") continue;
                var rb = r.bounds;
                if (!rb.Intersects(cb)) continue;
                bool same = true;
                for (int i = 0; i < 3; i++) if (Mathf.Abs(rb.size[i] - cb.size[i]) > Mathf.Max(1f, cb.size[i] * 0.25f)) same = false;
                if (!same) continue;
                if (best == null || r.name.Contains("LOD0")) best = r;
            }
            return best;
        }

        /// <summary>
        /// A model's box hull being dug (#57): from now on the hull is what's drawn (a box with the model's material,
        /// cut like any level box), and the model, with its lower-detail copies, is hidden.
        /// </summary>
        static void DressHull(GameObject go)
        {
            var mc = go.GetComponent<MeshCollider>();
            if (mc == null || mc.sharedMesh == null || go.GetComponent<Renderer>() != null || go.GetComponent<BoxCollider>() == null || Facility.Contains(go)) return;
            var visual = HullVisual(go, mc.bounds);
            if (visual == null) return;
            var mf = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>(); // (some hulls keep an unused filter)
            mf.sharedMesh = mc.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { visual.sharedMaterial };
            mr.shadowCastingMode = visual.shadowCastingMode;
            mr.receiveShadows = visual.receiveShadows;
            var g = visual.GetComponentInParent<LODGroup>();
            if (g != null)
            {
                foreach (var l in g.GetLODs()) foreach (var r in l.renderers) if (r != null) r.enabled = false;
                g.enabled = false;
            }
            visual.enabled = false;
            Plugin.Log.LogInfo($"Box hull '{go.name}' now drawn in place of its model '{visual.name}'");
        }

        /// <summary>
        /// Once per moon (#73): the level's big visible boxes become box-shaped mesh colliders up front, so the ground
        /// rays see them (they only count mesh colliders as level shell) before anything asks what's solid there.
        /// </summary>
        static bool levelBoxesConverted;
        public static int ConvertLevelBoxes()
        {
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase || !Plugin.AllowDigging.Value || (AtCompany && !Plugin.DigAtCompany.Value)) return 0;
            if (levelBoxesConverted) return 0;
            levelBoxesConverted = true;
            int n = 0;
            foreach (var bc in Object.FindObjectsOfType<BoxCollider>())
            {
                var go = bc.gameObject;
                if (!InCurrentLevel(go) || Facility.Contains(go) || ShipAttach.IsShipCollider(bc) || !IsLevelBox(go)) continue;
                if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] level box '{go.name}' <{(go.transform.parent != null ? go.transform.parent.name : "-")}> L{go.layer} size {bc.bounds.size} at {bc.bounds.center}{(go.GetComponent<MeshRenderer>() == null ? " (hull)" : "")}");
                try { BoxToMeshCollider(go); n++; } catch (System.Exception e) { Plugin.Log.LogWarning($"Level box '{go.name}': {e.Message}"); }
            }
            Plugin.Log.LogInfo($"Level boxes made diggable: {n}");
            return n;
        }

        /// <summary>Swaps a box collider for an identical box-shaped mesh collider, which the carver can cut.</summary>
        static void BoxToMeshCollider(GameObject go)
        {
            var bc = go.GetComponent<BoxCollider>();
            Vector3 c = bc.center, h = bc.size * 0.5f;
            var verts = new List<Vector3>(); var nrms = new List<Vector3>(); var tris = new List<int>(); var uvs = new List<Vector2>();
            void Face(Vector3 n, Vector3 u, Vector3 v)
            {
                // quad on the side n of the box, wound so its front faces outward
                int b = verts.Count;
                var o = c + Vector3.Scale(n, h);
                var du = Vector3.Scale(u, h); var dv = Vector3.Scale(v, h);
                verts.Add(o - du - dv); verts.Add(o + du - dv); verts.Add(o + du + dv); verts.Add(o - du + dv);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                for (int i = 0; i < 4; i++) nrms.Add(n);
                if (Vector3.Dot(Vector3.Cross(verts[b + 1] - verts[b], verts[b + 2] - verts[b]), n) > 0) { tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3); }
                else { tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2); }
            }
            Face(Vector3.right, Vector3.up, Vector3.forward); Face(Vector3.left, Vector3.up, Vector3.forward);
            Face(Vector3.up, Vector3.right, Vector3.forward); Face(Vector3.down, Vector3.right, Vector3.forward);
            Face(Vector3.forward, Vector3.right, Vector3.up); Face(Vector3.back, Vector3.right, Vector3.up);
            var m = new Mesh { name = go.name + "_box" };
            m.SetVertices(verts); m.SetNormals(nrms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateBounds(); m.RecalculateTangents();
            // (added while inactive: a new MeshCollider takes the object's MeshFilter mesh at once, and a static-batched
            // render mesh can't be read: Unity logged an error cooking it before ours replaced it)
            bool was = go.activeSelf;
            if (go.GetComponent<MeshFilter>() != null) go.SetActive(false);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = m;
            mc.sharedMaterial = bc.sharedMaterial;
            bc.enabled = false;
            if (go.activeSelf != was) go.SetActive(was);
        }

        /// <summary>Reads a non-readable mesh back from the GPU into a readable copy (cached).</summary>
        public static Mesh GpuCopy(Mesh m)
        {
            if (m == null) return null;
            if (gpuCopies.TryGetValue(m.GetInstanceID(), out var cached)) return cached;
            Mesh copy = null;
            try
            {
                int vc = m.vertexCount;
                var attrs = m.GetVertexAttributes();
                var streams = new byte[m.vertexBufferCount][];
                var strides = new int[m.vertexBufferCount];
                for (int s = 0; s < m.vertexBufferCount; s++)
                {
                    using (var vb = m.GetVertexBuffer(s))
                    {
                        strides[s] = vb.stride;
                        streams[s] = new byte[vb.count * vb.stride];
                        vb.GetData(streams[s]);
                    }
                }
                Vector4 Read(VertexAttributeDescriptor d, int v)
                {
                    var buf = streams[d.stream]; int o = v * strides[d.stream] + m.GetVertexAttributeOffset(d.attribute);
                    var r = Vector4.zero;
                    for (int k = 0; k < d.dimension && k < 4; k++)
                    {
                        float f;
                        switch (d.format)
                        {
                            case VertexAttributeFormat.Float32: f = System.BitConverter.ToSingle(buf, o + k * 4); break;
                            case VertexAttributeFormat.Float16: f = Mathf.HalfToFloat(System.BitConverter.ToUInt16(buf, o + k * 2)); break;
                            case VertexAttributeFormat.UNorm8: f = buf[o + k] / 255f; break;
                            case VertexAttributeFormat.SNorm8: f = Mathf.Max(-1f, (sbyte)buf[o + k] / 127f); break;
                            case VertexAttributeFormat.UNorm16: f = System.BitConverter.ToUInt16(buf, o + k * 2) / 65535f; break;
                            case VertexAttributeFormat.SNorm16: f = Mathf.Max(-1f, System.BitConverter.ToInt16(buf, o + k * 2) / 32767f); break;
                            default: f = 0; break;
                        }
                        r[k] = f;
                    }
                    return r;
                }
                copy = new Mesh { name = m.name + "_gpu", indexFormat = IndexFormat.UInt32 };
                foreach (var d in attrs)
                {
                    switch (d.attribute)
                    {
                        case VertexAttribute.Position: copy.SetVertices(Enumerable.Range(0, vc).Select(v => (Vector3)Read(d, v)).ToList()); break;
                        case VertexAttribute.Normal: copy.SetNormals(Enumerable.Range(0, vc).Select(v => (Vector3)Read(d, v)).ToList()); break;
                        case VertexAttribute.Tangent: copy.SetTangents(Enumerable.Range(0, vc).Select(v => Read(d, v)).ToList()); break;
                        case VertexAttribute.Color: copy.SetColors(Enumerable.Range(0, vc).Select(v => (Color)Read(d, v)).ToList()); break;
                        default:
                            int ch = d.attribute - VertexAttribute.TexCoord0;
                            if (ch >= 0 && ch < 8) copy.SetUVs(ch, Enumerable.Range(0, vc).Select(v => Read(d, v)).ToList());
                            break;
                    }
                }
                int[] all;
                using (var ib = m.GetIndexBuffer())
                {
                    if (ib.stride == 2) { var s16 = new ushort[ib.count]; ib.GetData(s16); all = s16.Select(x => (int)x).ToArray(); }
                    else { all = new int[ib.count]; ib.GetData(all); }
                }
                copy.subMeshCount = m.subMeshCount;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var sd = m.GetSubMesh(s);
                    if (sd.topology != MeshTopology.Triangles) { copy.SetTriangles(new int[0], s); continue; }
                    var tri = new int[sd.indexCount];
                    for (int i = 0; i < sd.indexCount; i++) tri[i] = all[sd.indexStart + i] + sd.baseVertex;
                    copy.SetTriangles(tri, s, false);
                }
                copy.RecalculateBounds();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"GPU readback of '{m.name}' failed: {e.Message}");
                copy = null;
            }
            gpuCopies[m.GetInstanceID()] = copy;
            return copy;
        }

        public static string PathOf(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name + "#" + t.GetSiblingIndex());
            parts.Reverse();
            return go.scene.name + "/" + string.Join("/", parts);
        }

        static GameObject FindByPath(string path)
        {
            int slash = path.IndexOf('/');
            var scene = SceneManager.GetSceneByName(path.Substring(0, slash));
            if (!scene.IsValid() || !scene.isLoaded) return null;
            Transform cur = null;
            foreach (var part in path.Substring(slash + 1).Split('/'))
            {
                int h = part.LastIndexOf('#');
                string name = part.Substring(0, h);
                int idx = int.Parse(part.Substring(h + 1));
                if (cur == null)
                {
                    var roots = scene.GetRootGameObjects().Select(g => g.transform).ToList();
                    cur = roots.FirstOrDefault(t => t.name == name && t.GetSiblingIndex() == idx) ?? roots.FirstOrDefault(t => t.name == name);
                }
                else
                {
                    Transform next = idx < cur.childCount ? cur.GetChild(idx) : null;
                    if (next == null || next.name != name) next = cur.Find(name);
                    cur = next;
                }
                if (cur == null) return null;
            }
            return cur.gameObject;
        }

        public static GameObject ObjectFor(string path) => FindByPath(path);

        static Vector2Int ChunkOf(Carved cv, Vector3 w) => new Vector2Int(Mathf.FloorToInt(w.x / cv.ChunkSize), Mathf.FloorToInt(w.z / cv.ChunkSize));

        // ------------------------------------------------------------------ preparation
        /// <summary>Warm up the big ground meshes after landing (spread over frames) so the first dig doesn't hitch.</summary>
        public static IEnumerator PrewarmLevel()
        {
            yield return new WaitForSeconds(3f);
            var sor = StartOfRound.Instance;
            if (sor == null || sor.currentLevel == null) yield break;
            var scene = SceneManager.GetSceneByName(sor.currentLevel.sceneName);
            if (!scene.IsValid()) yield break;
            var candidates = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshCollider>())
                .Where(mc => mc.sharedMesh != null && mc.sharedMesh.vertexCount > 20000 && CanCarve(mc.gameObject, out _)).Select(mc => mc.gameObject).ToList();
            foreach (var go in candidates)
            {
                var e = PrepareRoutine(go, true);
                while (e.MoveNext()) yield return e.Current;
            }
        }

        static Carved Prepare(GameObject go)
        {
            if (go == null) return null;
            if (carved.TryGetValue(PathOf(go), out var cv) && cv.Ready) return cv;
            var e = PrepareRoutine(go, false);
            while (e.MoveNext()) { }
            return carved.TryGetValue(PathOf(go), out cv) && cv.Ready ? cv : null;
        }

        static IEnumerator PrepareRoutine(GameObject go, bool spread)
        {
            var path = PathOf(go);
            if (carved.TryGetValue(path, out var existing))
            {
                if (existing.Ready || spread) yield break;
                carved.Remove(path);
            }
            var src = SourceMesh(go, out bool proxy);
            if (src == null) yield break;
            if (!proxy) DressHull(go);
            var mc = go.GetComponent<MeshCollider>();
            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var cv = new Carved { Path = path, Go = go, OrigCollider = mc, L2W = go.transform.localToWorldMatrix };
            cv.NormalMat = cv.L2W.inverse.transpose;
            carved[path] = cv;
            src.GetVertices(cv.Pos);
            src.GetNormals(cv.Nrm);
            src.GetTangents(cv.Tan);
            src.GetColors(cv.Col);
            for (int ch = 0; ch < 8; ch++)
            {
                var l = new List<Vector4>();
                src.GetUVs(ch, l);
                cv.Uv[ch] = l.Count == cv.Pos.Count ? l : null;
                cv.UvDim[ch] = Mathf.Clamp(src.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + ch), 2, 4);
            }
            foreach (var p in cv.Pos) cv.World.Add(cv.L2W.MultiplyPoint3x4(p));
            if (cv.World.Count > 0)
            {
                // ~600 collider chunks at most: big sparse meshes (km-wide underlays) get big chunks
                float x0 = cv.World.Min(v => v.x), x1 = cv.World.Max(v => v.x), z0 = cv.World.Min(v => v.z), z1 = cv.World.Max(v => v.z);
                cv.ChunkSize = Mathf.Max(MinChunkSize, Mathf.Sqrt((x1 - x0) * (z1 - z0) / 600f));
            }
            bool mirrored = cv.L2W.determinant < 0;
            cv.Sub = new List<int>[src.subMeshCount];
            cv.SourceSubmeshes = src.subMeshCount;
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] preparing '{go.name}' mesh '{src.name}' sub={src.subMeshCount} verts={src.vertexCount}");
            var bmin = Vector3.positiveInfinity; var bmax = Vector3.negativeInfinity;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (src.GetTopology(s) != MeshTopology.Triangles) { cv.Sub[s] = new List<int>(); continue; }
                cv.Sub[s] = new List<int>(src.GetTriangles(s));
                var t = cv.Sub[s];
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    if (mc != null) AddToChunk(cv, a, b, c);
                    // facing: prefer the authored vertex normals (robust for closed rooms); fall back to winding
                    Vector3 n;
                    if (cv.Nrm.Count == cv.Pos.Count) n = cv.NormalMat.MultiplyVector(cv.Nrm[a] + cv.Nrm[b] + cv.Nrm[c]).normalized;
                    else { n = Vector3.Cross(cv.World[b] - cv.World[a], cv.World[c] - cv.World[a]).normalized; if (mirrored) n = -n; }
                    int id = cv.OrigTris.Count;
                    cv.OrigTris.Add((a, b, c, n.y > 0, n));
                    Vector3 wa = cv.World[a], wb = cv.World[b], wc = cv.World[c];
                    var tmn = Vector3.Min(wa, Vector3.Min(wb, wc)); var tmx = Vector3.Max(wa, Vector3.Max(wb, wc));
                    cv.TriMin.Add(tmn); cv.TriMax.Add(tmx);
                    bmin = Vector3.Min(bmin, tmn); bmax = Vector3.Max(bmax, tmx);
                    var c0 = ChunkOf(cv, tmn); var c1 = ChunkOf(cv, tmx);
                    for (int x = c0.x; x <= c1.x; x++)
                        for (int z = c0.y; z <= c1.y; z++)
                        {
                            var key = new Vector2Int(x, z);
                            if (!cv.OrigIndex.TryGetValue(key, out var l)) cv.OrigIndex[key] = l = new List<int>();
                            l.Add(id);
                        }
                }
            }
            cv.WorldBounds = new Bounds((bmin + bmax) * 0.5f, bmax - bmin);
            // collision-only geometry (invisible walls/ramps/underlays) has nothing to draw: no render copy (an
            // unreferenced mesh can be unloaded by the engine under us)
            if (proxy || (mf != null && mr != null))
            {
                cv.RenderMesh = new Mesh { name = src.name + "_carved", indexFormat = IndexFormat.UInt32 };
                WriteRenderMesh(cv);
            }
            if (spread) yield return null;

            if (mc != null)
            {
                int built = 0;
                foreach (var kv in cv.Chunks.ToList())
                {
                    RebuildChunk(cv, kv.Value);
                    if (spread && ++built % 24 == 0) yield return null;
                    if (go == null) yield break;
                }
                mc.enabled = false;
            }
            if (proxy)
            {
                var p = new GameObject("LMC_GroundProxy");
                p.transform.SetParent(go.transform, false);
                p.layer = go.layer;
                p.AddComponent<MeshFilter>().sharedMesh = cv.RenderMesh;
                var pr = p.AddComponent<MeshRenderer>();
                pr.sharedMaterials = mr.sharedMaterials.Take(Mathf.Max(1, cv.RenderMesh.subMeshCount)).ToArray();
                pr.shadowCastingMode = mr.shadowCastingMode;
                pr.receiveShadows = mr.receiveShadows;
                pr.lightmapIndex = mr.lightmapIndex;
                pr.lightmapScaleOffset = mr.lightmapScaleOffset;
                pr.renderingLayerMask = mr.renderingLayerMask;
                pr.lightProbeUsage = mr.lightProbeUsage;
                mr.enabled = false;
            }
            else if (mf != null && mr != null) mf.sharedMesh = cv.RenderMesh;
            if (mr != null) SettleLods(go);
            cv.Ready = true;
            if (sw.ElapsedMilliseconds > 20 || cv.Pos.Count > 5000)
                Plugin.Log.LogInfo($"Prepared diggable '{go.name}' ({cv.Pos.Count} verts{(mc != null ? $", {cv.Chunks.Count} collider chunks" : ", render-only")}{(proxy ? ", proxy renderer" : "")}{(src.name.EndsWith("_gpu") ? ", GPU readback" : "")}) in {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// A carved model with lower-detail copies (a LODGroup, #57: E Gypt's giant walls): from a distance the game would
        /// draw an uncut copy, a wall standing where the hole is. The copies are hidden and the carved model is drawn at
        /// every distance.
        /// </summary>
        static void SettleLods(GameObject go)
        {
            var g = go.GetComponentInParent<LODGroup>();
            if (g == null || !g.enabled) return;
            var lods = g.GetLODs();
            if (lods.Length < 2 || !lods[0].renderers.Any(r => r != null && r.gameObject == go)) return;
            var keep = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
            foreach (var l in lods.Skip(1))
                foreach (var r in l.renderers)
                    if (r != null && !keep.Contains(r)) r.enabled = false;
            g.enabled = false;
        }

        /// <summary>
        /// Writes the whole render copy from the carved data: vertices and their attributes, every submesh (one per
        /// material) and its triangles. The one place render geometry is produced, both when an object is prepared and
        /// after each cut.
        /// </summary>
        static void WriteRenderMesh(Carved cv)
        {
            var m = cv.RenderMesh;
            if (m == null) return;
            m.Clear();
            m.indexFormat = IndexFormat.UInt32; // (empty mesh: safe to switch)
            m.SetVertices(cv.Pos);
            if (cv.Nrm.Count == cv.Pos.Count) m.SetNormals(cv.Nrm);
            if (cv.Tan.Count == cv.Pos.Count) m.SetTangents(cv.Tan);
            if (cv.Col.Count == cv.Pos.Count) m.SetColors(cv.Col);
            for (int ch = 0; ch < 8; ch++)
            {
                var uv = cv.Uv[ch];
                if (uv == null || uv.Count != cv.Pos.Count) continue;
                switch (cv.UvDim[ch])
                {
                    case 2: m.SetUVs(ch, uv.ConvertAll(v => (Vector2)v)); break;
                    case 3: m.SetUVs(ch, uv.ConvertAll(v => (Vector3)v)); break;
                    default: m.SetUVs(ch, uv); break;
                }
            }
            m.subMeshCount = cv.Sub.Length;
            for (int s = 0; s < cv.Sub.Length; s++) m.SetTriangles(cv.Sub[s], s, false);
            m.RecalculateBounds();
        }

        static void AddToChunk(Carved cv, int a, int b, int c)
        {
            var key = ChunkOf(cv, (cv.World[a] + cv.World[b] + cv.World[c]) / 3f);
            if (!cv.Chunks.TryGetValue(key, out var ch)) cv.Chunks[key] = ch = new Chunk();
            ch.Tris.Add((a, b, c));
            ch.Dirty = true;
        }

        static void RemoveFromChunk(Carved cv, int a, int b, int c)
        {
            var key = ChunkOf(cv, (cv.World[a] + cv.World[b] + cv.World[c]) / 3f);
            if (cv.Chunks.TryGetValue(key, out var ch) && ch.Tris.Remove((a, b, c))) ch.Dirty = true;
        }

        static void RebuildChunk(Carved cv, Chunk ch)
        {
            if (ch.Col == null)
            {
                ch.Go = new GameObject("LMC_GroundChunk");
                ch.Go.transform.SetParent(cv.Go.transform, false);
                ch.Go.layer = cv.Go.layer;
                ch.Go.tag = cv.Go.tag;
                ch.Col = ch.Go.AddComponent<MeshCollider>();
                ch.Col.sharedMaterial = cv.OrigCollider != null ? cv.OrigCollider.sharedMaterial : null;
                ch.Mesh = new Mesh { name = "LMC_GroundChunk", indexFormat = IndexFormat.UInt32 };
            }
            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var tris = new List<int>(ch.Tris.Count * 3);
            foreach (var (a, b, c) in ch.Tris)
            {
                // slivers left by cutting can be degenerate; PhysX refuses to cook meshes made only of those
                // (PhysX welds vertices closer than ~1 mm and drops what collapses; skip anything near that size)
                var e1 = cv.World[b] - cv.World[a]; var e2 = cv.World[c] - cv.World[a];
                if (Vector3.Cross(e1, e2).sqrMagnitude < 1e-8f || e1.sqrMagnitude < 1e-6f || e2.sqrMagnitude < 1e-6f || (cv.World[c] - cv.World[b]).sqrMagnitude < 1e-6f) continue;
                foreach (var vi in new[] { a, b, c })
                {
                    if (!remap.TryGetValue(vi, out int ni)) { ni = verts.Count; remap[vi] = ni; verts.Add(cv.Pos[vi]); }
                    tris.Add(ni);
                }
            }
            ch.Mesh.Clear();
            ch.Mesh.SetVertices(verts);
            ch.Mesh.SetTriangles(tris, 0);
            ch.Col.sharedMesh = null;
            if (tris.Count > 0) ch.Col.sharedMesh = ch.Mesh;
            ch.Dirty = false;
        }

        // ------------------------------------------------------------------ queries against ORIGINAL geometry
        /// <summary>Physics.RaycastAll without a new array per ray (the ground classification casts tens of thousands):
        /// a shared buffer, or a fresh RaycastAll in the rare case it's full (so no hit is ever missed).</summary>
        static readonly RaycastHit[] rayBuf = new RaycastHit[128];
        static int RaycastAllInto(Vector3 o, Vector3 dir, float maxDist, int mask, out RaycastHit[] hits)
        {
            int n = Physics.RaycastNonAlloc(o, dir, rayBuf, maxDist, mask, QueryTriggerInteraction.Ignore);
            if (n < rayBuf.Length) { hits = rayBuf; return n; }
            hits = Physics.RaycastAll(o, dir, maxDist, mask, QueryTriggerInteraction.Ignore);
            return hits.Length;
        }

        static bool IsCarvedPart(Collider c) => c.name == "LMC_GroundChunk" || (c is MeshCollider && !c.enabled);

        /// <summary>
        /// First level surface hit along an axis-aligned ray, against original (uncut) geometry, hitting both sides.
        /// frontFacing = we see the surface's front (we're in open space on that side).
        /// </summary>
        public static bool RayOriginal(Vector3 o, Vector3 dir, float maxDist, out float dist, out bool frontFacing, out GameObject obj)
        {
            dist = maxDist; frontFacing = false; obj = null;
            bool found = false;
            // 1) untouched level colliders via physics (backfaces too)
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                int n = RaycastAllInto(o, dir, maxDist, LevelMask, out var hitList);
                for (int i = 0; i < n; i++)
                {
                    var h = hitList[i];
                    if (h.distance >= dist || IsCarvedPart(h.collider) || h.collider.GetComponent<BlockRef>() != null) continue;
                    if (Excluded(h.collider.gameObject)) continue;
                    if (!(h.collider is MeshCollider)) continue; // boxes/props aren't level shell
                    dist = h.distance; frontFacing = FrontFacing(h, dir); obj = h.collider.gameObject; found = true;
                }
            }
            finally { Physics.queriesHitBackfaces = prev; }
            // 2) Unity terrains: from the heightmap (holes we dug don't change it)
            foreach (var ter in LevelTerrains())
            {
                if (RayTerrain(ter, o, dir, dist, out float tt, out bool tf)) { dist = tt; frontFacing = tf; obj = ter.gameObject; found = true; }
            }
            // 3) carved objects: their colliders are cut, so test the original triangles
            foreach (var cv in carved.Values)
            {
                if (!cv.Ready || cv.Go == null || cv.OrigCollider == null || ((1 << cv.Go.layer) & LevelMask) == 0) continue; // render-only trim isn't part of the solid shell
                var b = cv.WorldBounds; b.Expand(0.1f);
                if (!b.IntersectRay(new Ray(o, dir), out float enter) || enter > dist) continue;
                if (RayCarved(cv, o, dir, dist, out float t, out bool front)) { dist = t; frontFacing = front; obj = cv.Go; found = true; }
            }
            return found;
        }

        /// <summary>
        /// A floor made of plain colliders (boxes) right below: some walkable level geometry has no mesh collider at all
        /// (the Company's platform is box colliders), so the mesh rays see nothing under it and the open air above read as
        /// "nothing below, a surface overhead" = underground. Blocks, items, players and monsters don't count.
        /// </summary>
        public static bool PlainFloorBelow(Vector3 p, float maxDist)
        {
            int n = RaycastAllInto(p, Vector3.down, maxDist, LevelMask | InvisibleBoxMask, out var hitList);
            for (int i = 0; i < n; i++)
            {
                var h = hitList[i];
                var c = h.collider;
                if (c is MeshCollider || !c.enabled) continue;
                if (c.GetComponentInParent<BlockRef>() != null || c.GetComponentInParent<GrabbableObject>() != null) continue;
                if (c.GetComponentInParent<GameNetcodeStuff.PlayerControllerB>() != null || c.GetComponentInParent<EnemyAI>() != null) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (h.normal.y < 0.5f) continue; // something to stand on
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Unity terrains
        static List<Terrain> terrains;
        static readonly HashSet<TerrainData> clonedData = new HashSet<TerrainData>();

        static List<Terrain> LevelTerrains()
        {
            bool stale = terrains == null;
            if (!stale) for (int i = 0; i < terrains.Count; i++) if (terrains[i] == null) { stale = true; break; }
            if (stale)
                terrains = Terrain.activeTerrains.Where(x => x != null && x.terrainData != null && ((1 << x.gameObject.layer) & LevelMask) != 0 &&
                    x.GetComponent<TerrainCollider>() != null && x.GetComponent<TerrainCollider>().enabled && InCurrentLevel(x.gameObject)).ToList();
            return terrains;
        }

        /// <summary>Experiment: a mesh copy of a terrain region drawn with the terrain's own material.</summary>
        public static GameObject BuildTerrainPatch(Terrain t, Vector3 center, float half, float step, float lift)
        {
            var td = t.terrainData; var tp = t.transform.position; var size = td.size;
            int n = Mathf.CeilToInt(half * 2 / step) + 1;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var nrms = new List<Vector3>(); var tris = new List<int>();
            float x0 = center.x - half, z0 = center.z - half;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    float u = (x - tp.x) / size.x, v = (z - tp.z) / size.z;
                    float h = td.GetInterpolatedHeight(u, v);
                    verts.Add(new Vector3(x, tp.y + h + lift, z));
                    uvs.Add(new Vector2(u, v));
                    nrms.Add(td.GetInterpolatedNormal(u, v));
                }
            for (int j = 0; j + 1 < n; j++)
                for (int i = 0; i + 1 < n; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            var m = new Mesh { indexFormat = IndexFormat.UInt32, name = "LMC_TerrainPatch" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetNormals(nrms); m.SetTriangles(tris, 0); m.RecalculateTangents(); m.RecalculateBounds();
            var go = new GameObject("LMC_TerrainPatch");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TerrainMeshMaterial(t);
            go.layer = t.gameObject.layer;
            return go;
        }

        static readonly Dictionary<Terrain, Material> terrainMats = new Dictionary<Terrain, Material>();
        static bool terrainsConverted;
        const int TerrainChunkQuads = 64;

        /// <summary>
        /// Unity terrains (heightmaps) can't be cut, and this game's pipeline can't draw terrain holes. So each level
        /// terrain is replaced by an identical mesh (same heights, same layered material) split into chunks, which the
        /// carver handles like any other ground. Trees and grass are still drawn by the terrain. Every client does this
        /// the same way when the level is ready, so chunk paths match for networked cuts.
        /// </summary>
        /// <summary>
        /// Some facility shell pieces (mineshaft doorway rocks) use convex colliders, which rays from inside don't hit,
        /// so the ground grid saw them as air and the carver couldn't cut them. As static geometry they work the same
        /// as plain (concave) mesh colliders. Every client does this when the level is ready.
        /// </summary>
        public static void MakeFacilityShellConcave()
        {
            var gen = RoundManager.Instance?.dungeonGenerator;
            if (gen == null || gen.Root == null) return;
            int n = 0;
            foreach (var mc in gen.Root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!mc.convex || mc.isTrigger || mc.sharedMesh == null || !mc.sharedMesh.isReadable) continue;
                if (((1 << mc.gameObject.layer) & LevelMask) == 0 || mc.GetComponentInParent<Rigidbody>() != null || Excluded(mc.gameObject)) continue;
                mc.convex = false; n++;
            }
            if (n > 0) Plugin.Log.LogInfo($"Facility: {n} convex shell colliders made diggable");
        }

        public static void ConvertTerrains()
        {
            if (terrainsConverted) return;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase || !Plugin.AllowDigging.Value || (AtCompany && !Plugin.DigAtCompany.Value)) return;
            terrainsConverted = true;
            foreach (var t in LevelTerrains().ToList())
            {
                try { ConvertTerrain(t); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Terrain '{t.name}' could not be made diggable: {e}"); }
            }
            terrains = null;
        }

        static void ConvertTerrain(Terrain t)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var td = t.terrainData;
            int res = td.heightmapResolution;
            var size = td.size;
            var heights = td.GetHeights(0, 0, res, res); // [z, x]
            var holes = td.GetHoles(0, 0, res - 1, res - 1); // [z, x], true = solid
            float dx = size.x / (res - 1), dz = size.z / (res - 1);
            var tc = t.GetComponent<TerrainCollider>();
            var mat = TerrainMeshMaterial(t);
            // terrains are always drawn world-aligned at their position, whatever their parents' rotation/scale
            var root = new GameObject("TerrainMesh");
            root.transform.SetPositionAndRotation(t.GetPosition(), Quaternion.identity);
            root.transform.localScale = Vector3.one;
            root.transform.SetParent(t.transform, true);
            Plugin.Log.LogInfo($"Terrain '{t.name}': pos={t.GetPosition()} lossyScale={t.transform.lossyScale} rot={t.transform.rotation.eulerAngles} rootLossy={root.transform.lossyScale}");
            root.layer = t.gameObject.layer;
            root.tag = t.gameObject.tag;
            int chunks = 0;
            for (int cz = 0; cz * TerrainChunkQuads < res - 1; cz++)
                for (int cx = 0; cx * TerrainChunkQuads < res - 1; cx++)
                {
                    int x0 = cx * TerrainChunkQuads, z0 = cz * TerrainChunkQuads;
                    int x1 = Mathf.Min(res - 1, x0 + TerrainChunkQuads), z1 = Mathf.Min(res - 1, z0 + TerrainChunkQuads);
                    int w = x1 - x0 + 1;
                    var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var nrms = new List<Vector3>(); var tris = new List<int>();
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float u = x / (float)(res - 1), v = z / (float)(res - 1);
                            verts.Add(new Vector3(x * dx, heights[z, x] * size.y, z * dz));
                            uvs.Add(new Vector2(u, v));
                            nrms.Add(td.GetInterpolatedNormal(u, v));
                        }
                    for (int z = z0; z < z1; z++)
                        for (int x = x0; x < x1; x++)
                        {
                            if (!holes[z, x]) continue; // authored terrain holes stay holes
                            int a = (z - z0) * w + (x - x0), b = a + 1, c = a + w, d = c + 1;
                            tris.Add(a); tris.Add(c); tris.Add(d);
                            tris.Add(a); tris.Add(d); tris.Add(b);
                        }
                    if (tris.Count == 0) continue;
                    var m = new Mesh { name = $"TerrainChunk_{cx}_{cz}" };
                    m.SetVertices(verts); m.SetUVs(0, uvs); m.SetUVs(1, uvs); m.SetNormals(nrms); m.SetTriangles(tris, 0);
                    m.RecalculateTangents(); m.RecalculateBounds();
                    var go = new GameObject($"TerrainChunk_{cx}_{cz}");
                    go.transform.SetParent(root.transform, false);
                    go.layer = t.gameObject.layer;
                    go.tag = t.gameObject.tag;
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = t.shadowCastingMode;
                    mr.lightmapIndex = t.lightmapIndex;
                    mr.lightmapScaleOffset = t.lightmapScaleOffset;
                    mr.renderingLayerMask = t.renderingLayerMask;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = m;
                    if (tc != null) mc.sharedMaterial = tc.sharedMaterial;
                    chunks++;
                }
            t.drawHeightmap = false;
            if (tc != null) tc.enabled = false;
            Plugin.Log.LogInfo($"Terrain '{t.name}' ({res}x{res}) converted to {chunks} diggable mesh chunks in {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>The terrain's TerrainLit material set up to draw an ordinary mesh (uv0 = normalized terrain coords).</summary>
        public static Material TerrainMeshMaterial(Terrain t)
        {
            if (terrainMats.TryGetValue(t, out var cached) && cached != null) return cached;
            var td = t.terrainData;
            var mat = new Material(t.materialTemplate) { name = "LMC_TerrainMesh" };
            mat.DisableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            var layers = td.terrainLayers;
            var ctrl = td.alphamapTextures;
            if (ctrl.Length > 0) mat.SetTexture("_Control0", ctrl[0]);
            if (ctrl.Length > 1) { mat.SetTexture("_Control1", ctrl[1]); mat.EnableKeyword("_TERRAIN_8_LAYERS"); }
            bool anyNormal = false, anyMask = false;
            for (int i = 0; i < layers.Length && i < 8; i++)
            {
                var l = layers[i];
                if (l == null) continue;
                mat.SetTexture("_Splat" + i, l.diffuseTexture);
                var tile = new Vector2(td.size.x / Mathf.Max(0.01f, l.tileSize.x), td.size.z / Mathf.Max(0.01f, l.tileSize.y));
                var off = new Vector2(l.tileOffset.x / Mathf.Max(0.01f, l.tileSize.x), l.tileOffset.y / Mathf.Max(0.01f, l.tileSize.y));
                mat.SetVector("_Splat" + i + "_ST", new Vector4(tile.x, tile.y, off.x, off.y));
                if (l.normalMapTexture != null) { mat.SetTexture("_Normal" + i, l.normalMapTexture); anyNormal = true; }
                mat.SetFloat("_NormalScale" + i, l.normalScale);
                if (l.maskMapTexture != null) { mat.SetTexture("_Mask" + i, l.maskMapTexture); anyMask = true; }
                mat.SetFloat("_LayerHasMask" + i, l.maskMapTexture != null ? 1f : 0f);
                mat.SetVector("_DiffuseRemapScale" + i, l.diffuseRemapMax - l.diffuseRemapMin);
                mat.SetVector("_MaskMapRemapOffset" + i, l.maskMapRemapMin);
                mat.SetVector("_MaskMapRemapScale" + i, l.maskMapRemapMax - l.maskMapRemapMin);
                mat.SetFloat("_Metallic" + i, l.metallic);
                mat.SetFloat("_Smoothness" + i, l.smoothness);
            }
            if (anyNormal) mat.EnableKeyword("_NORMALMAP");
            if (anyMask) mat.EnableKeyword("_MASKMAP");
            terrainMats[t] = mat;
            return mat;
        }

        static float SurfaceArea(Carved cv)
        {
            double a = 0;
            foreach (var t in cv.Sub)
                for (int i = 0; i + 2 < t.Count; i += 3)
                    a += Vector3.Cross(cv.World[t[i + 1]] - cv.World[t[i]], cv.World[t[i + 2]] - cv.World[t[i]]).magnitude * 0.5;
            return (float)a;
        }

        /// <summary>
        /// Integrity check of every carved object (dev/tests): the drawn mesh still has all its material parts and exactly
        /// the carved triangles, and no more surface vanished than the cut cells could hold. Catches "the whole building
        /// disappeared" kinds of bugs generically. Returns one line per problem, empty when all is well.
        /// </summary>
        public static List<string> IntegrityProblems()
        {
            var problems = new List<string>();
            float S = Plugin.S;
            foreach (var cv in carved.Values)
            {
                if (!cv.Ready || cv.Go == null) continue;
                string name = cv.Go.name;
                if (cv.OrigArea < 0) cv.OrigArea = cv.OrigTris.Sum(t => Vector3.Cross(cv.World[t.b] - cv.World[t.a], cv.World[t.c] - cv.World[t.a]).magnitude * 0.5f);
                var m = cv.RenderMesh;
                if (m != null)
                {
                    if (m.subMeshCount != cv.SourceSubmeshes) problems.Add($"{name}: draws {m.subMeshCount} material parts, source had {cv.SourceSubmeshes}");
                    for (int s = 0; s < cv.Sub.Length && s < m.subMeshCount; s++)
                        if (m.GetIndexCount(s) != cv.Sub[s].Count) problems.Add($"{name}: part {s} draws {m.GetIndexCount(s)} indices, carved data has {cv.Sub[s].Count}");
                    var mf = cv.Go.GetComponent<MeshFilter>();
                    var proxy = cv.Go.transform.Find("LMC_GroundProxy");
                    var drawn = proxy != null ? proxy.GetComponent<MeshFilter>()?.sharedMesh : mf?.sharedMesh;
                    if (drawn != m) problems.Add($"{name}: renderer isn't drawing the carved mesh ({drawn?.name})");
                }
                // each cut cell can take at most what lies inside an S-sized box; allow 8 S^2 per cell (very generous)
                float lost = cv.OrigArea - SurfaceArea(cv);
                float allowed = cv.CutBoxes * 8f * S * S + 0.01f * cv.OrigArea + 1f;
                if (lost > allowed) problems.Add($"{name}: lost {lost:F0} m2 of surface to {cv.CutBoxes} cut cells (max expected {allowed:F0})");
            }
            return problems;
        }

        /// <summary>
        /// Dev/tests: visible level geometry that was left standing inside a box (a dug cell, shrunk a little): any enabled
        /// renderer on a level layer whose drawn triangles still cross it. Props are kept whole on purpose and skipped.
        /// </summary>
        public static List<string> GhostGeometry(Vector3 mn, Vector3 mx)
        {
            var found = new List<string>();
            var box = new Bounds((mn + mx) * 0.5f, mx - mn);
            foreach (var r in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (!r.enabled || ((1 << r.gameObject.layer) & CarveMask) == 0 || !r.bounds.Intersects(box)) continue;
                // foliage: leaf cards are big, mostly see-through quads (and leaves overhanging a hole are fine)
                if (r.gameObject.layer == 10) continue;
                // our own objects don't count (blocks are solid on purpose), except the cut ground we draw for batched meshes
                bool proxy = r.name == "LMC_GroundProxy";
                if (!proxy && (Excluded(r.gameObject) || IsPropVisual(r) || IsTree(r.transform))) continue;
                if (!InCurrentLevel(r.gameObject)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                if (!m.isReadable) m = GpuCopy(m);
                if (m == null) continue;
                // a static-batched renderer draws its own range of the scene's combined mesh, whose vertices are already in
                // world space (applying the object's transform again maps far-away triangles into the cell)
                bool batched = r.isPartOfStaticBatch;
                var l2w = batched ? Matrix4x4.identity : r.transform.localToWorldMatrix;
                int s0 = batched ? r.subMeshStartIndex : 0;
                int s1 = batched ? Mathf.Min(m.subMeshCount, s0 + Mathf.Max(1, r.sharedMaterials.Length)) : m.subMeshCount;
                var v = m.vertices;
                bool ghost = false;
                for (int s = s0; s < s1 && !ghost; s++)
                {
                    if (m.GetTopology(s) != MeshTopology.Triangles) continue;
                    var t = m.GetTriangles(s);
                    for (int i = 0; i + 2 < t.Length && !ghost; i += 3)
                    {
                        Vector3 a = l2w.MultiplyPoint3x4(v[t[i]]), b = l2w.MultiplyPoint3x4(v[t[i + 1]]), c = l2w.MultiplyPoint3x4(v[t[i + 2]]);
                        if (!TriBox(a, b, c, box)) continue;
                        // drawn here: is anything solid there too? (trees and props keep their own collision on purpose)
                        var q = box.ClosestPoint((a + b + c) / 3f);
                        if (!SolidNear(q, 0.25f)) ghost = true;
                    }
                }
                if (ghost) found.Add(r.name);
            }
            return found;
        }

        /// <summary>Dev: how GhostGeometry sees one renderer in one box.</summary>
        public static string GhostDebug(MeshRenderer r, Vector3 mn, Vector3 mx)
        {
            var box = new Bounds((mn + mx) * 0.5f, mx - mn);
            var mf = r.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null) return "no mesh";
            bool readable = m.isReadable;
            if (!readable) m = GpuCopy(m);
            if (m == null) return "no copy";
            bool batched = r.isPartOfStaticBatch;
            var l2w = batched ? Matrix4x4.identity : r.transform.localToWorldMatrix;
            int s0 = batched ? r.subMeshStartIndex : 0;
            int s1 = batched ? Mathf.Min(m.subMeshCount, s0 + Mathf.Max(1, r.sharedMaterials.Length)) : m.subMeshCount;
            var v = m.vertices;
            int inBox = 0, solid = 0, all = 0;
            for (int s = s0; s < s1; s++)
            {
                var t = m.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    all++;
                    Vector3 a = l2w.MultiplyPoint3x4(v[t[i]]), b = l2w.MultiplyPoint3x4(v[t[i + 1]]), c = l2w.MultiplyPoint3x4(v[t[i + 2]]);
                    if (!TriBox(a, b, c, box)) continue;
                    inBox++;
                    if (SolidNear(box.ClosestPoint((a + b + c) / 3f), 0.25f)) solid++;
                }
            }
            return $"{r.name} en={r.enabled} batched={batched} readable={readable} sub={m.subMeshCount} range={s0}..{s1} tris={all} inBox={inBox} solidNear={solid} bounds={r.bounds.Intersects(box)}";
        }

        static bool SolidNear(Vector3 p, float r)
        {
            foreach (var h in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<GameNetcodeStuff.PlayerControllerB>() != null) continue;
                if (h.GetComponentInParent<GrabbableObject>() != null || h.GetComponentInParent<EnemyAI>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>Triangle / axis-aligned box overlap (separating axis test).</summary>
        static bool TriBox(Vector3 a, Vector3 b, Vector3 c, Bounds box)
        {
            var cen = box.center; var e = box.extents;
            a -= cen; b -= cen; c -= cen;
            if (Mathf.Max(a.x, b.x, c.x) < -e.x || Mathf.Min(a.x, b.x, c.x) > e.x) return false;
            if (Mathf.Max(a.y, b.y, c.y) < -e.y || Mathf.Min(a.y, b.y, c.y) > e.y) return false;
            if (Mathf.Max(a.z, b.z, c.z) < -e.z || Mathf.Min(a.z, b.z, c.z) > e.z) return false;
            var n = Vector3.Cross(b - a, c - a);
            float rN = e.x * Mathf.Abs(n.x) + e.y * Mathf.Abs(n.y) + e.z * Mathf.Abs(n.z);
            if (Mathf.Abs(Vector3.Dot(n, a)) > rN) return false;
            var edges = new[] { b - a, c - b, a - c };
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            foreach (var ed in edges)
                foreach (var ax in axes)
                {
                    var L = Vector3.Cross(ed, ax);
                    if (L.sqrMagnitude < 1e-12f) continue;
                    float pa = Vector3.Dot(a, L), pb = Vector3.Dot(b, L), pc = Vector3.Dot(c, L);
                    float r = e.x * Mathf.Abs(L.x) + e.y * Mathf.Abs(L.y) + e.z * Mathf.Abs(L.z);
                    if (Mathf.Min(pa, pb, pc) > r || Mathf.Max(pa, pb, pc) < -r) return false;
                }
            return true;
        }

        public static string CarvedDebug(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var cv in carved.Values.Where(c => c.Go != null && c.Go.name == name))
            {
                float up = 0, down = 0;
                foreach (var t in cv.OrigTris)
                {
                    float area = Vector3.Cross(cv.World[t.b] - cv.World[t.a], cv.World[t.c] - cv.World[t.a]).magnitude * 0.5f;
                    if (t.n.y > 0.3f) up += area; else if (t.n.y < -0.3f) down += area;
                }
                var wind = cv.OrigTris.Take(2000).Sum(t => Vector3.Cross(cv.World[t.b] - cv.World[t.a], cv.World[t.c] - cv.World[t.a]).normalized.y);
                sb.Append($"[{cv.Path.Substring(System.Math.Max(0, cv.Path.Length - 50))} normals={cv.Nrm.Count == cv.Pos.Count} det={cv.L2W.determinant:F2} upArea={up:F0} downArea={down:F0} windingUpSum={wind:F0} tris={cv.OrigTris.Count}] ");
            }
            return sb.Length == 0 ? "not carved" : sb.ToString();
        }

        public static string TerrainDebug()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in LevelTerrains())
            {
                var td = t.terrainData;
                int res = td.holesResolution, holes = 0;
                var all = td.GetHoles(0, 0, res, res);
                foreach (var b in all) if (!b) holes++;
                sb.Append($"[{t.name} td={td.name} cloned={clonedData.Contains(td)} colTd={t.GetComponent<TerrainCollider>().terrainData?.name} holesRes={res} hmRes={td.heightmapResolution} size={td.size} pos={t.transform.position} holes={holes} drawHoles?={t.materialTemplate?.shader?.name}] ");
            }
            return sb.Length == 0 ? "no terrains" : sb.ToString();
        }

        static float TerrainHeight(Terrain t, float x, float z)
        {
            var tp = t.transform.position; var size = t.terrainData.size;
            if (x < tp.x || z < tp.z || x > tp.x + size.x || z > tp.z + size.z) return float.NaN;
            return t.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
        }

        /// <summary>Axis ray vs a heightmap. front = we came from above the surface (the open side).</summary>
        static bool RayTerrain(Terrain t, Vector3 o, Vector3 dir, float max, out float dist, out bool front)
        {
            dist = max; front = false;
            if (Mathf.Abs(dir.y) > 0.99f)
            {
                float h = TerrainHeight(t, o.x, o.z);
                if (float.IsNaN(h)) return false;
                float d = dir.y > 0 ? h - o.y : o.y - h;
                if (d <= 1e-4f || d >= max) return false;
                dist = d; front = dir.y < 0;
                return true;
            }
            const float step = 0.35f;
            float prevS = float.NaN, prevT = 0f;
            for (float s = 0f; s <= max; s += step)
            {
                var p = o + dir * s;
                float h = TerrainHeight(t, p.x, p.z);
                if (float.IsNaN(h)) { prevS = float.NaN; continue; }
                float side = p.y - h; // > 0: above ground
                if (!float.IsNaN(prevS) && (side > 0) != (prevS > 0))
                {
                    float k = prevS / (prevS - side);
                    float d = Mathf.Lerp(prevT, s, k);
                    if (d > 1e-4f && d < max) { dist = d; front = prevS > 0; return true; }
                }
                prevS = side; prevT = s;
            }
            return false;
        }

        /// <summary>Digs terrain holes for the heightmap texels whose center is inside a box and whose surface lies in it.</summary>
        static void ApplyTerrain(Terrain t, Cut cut)
        {
            var td = t.terrainData;
            if (!clonedData.Contains(td))
            {
                // never modify the shared asset: holes would survive into the next visit
                var orig = td;
                td = Object.Instantiate(orig);
                td.name = orig.name + "_dug";
                t.terrainData = td;
                t.GetComponent<TerrainCollider>().terrainData = td;
                clonedData.Add(td);
                terrains = null;
            }
            int res = td.holesResolution;
            var tp = t.transform.position; var size = td.size;
            float dx = size.x / res, dz = size.z / res;
            for (int k = 0; k < cut.Mins.Length; k++)
            {
                Vector3 mn = cut.Mins[k], mx = cut.Maxs[k];
                int i0 = Mathf.Clamp(Mathf.FloorToInt((mn.x - tp.x) / dx), 0, res - 1), i1 = Mathf.Clamp(Mathf.CeilToInt((mx.x - tp.x) / dx), 0, res - 1);
                int j0 = Mathf.Clamp(Mathf.FloorToInt((mn.z - tp.z) / dz), 0, res - 1), j1 = Mathf.Clamp(Mathf.CeilToInt((mx.z - tp.z) / dz), 0, res - 1);
                int w = i1 - i0 + 1, hgt = j1 - j0 + 1;
                if (w <= 0 || hgt <= 0) continue;
                var holes = td.GetHoles(i0, j0, w, hgt); // [z, x], true = solid
                bool any = false;
                for (int j = 0; j < hgt; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float cx = tp.x + (i0 + i + 0.5f) * dx, cz = tp.z + (j0 + j + 0.5f) * dz;
                        if (cx < mn.x || cx > mx.x || cz < mn.z || cz > mx.z) continue;
                        float y = TerrainHeight(t, cx, cz);
                        if (float.IsNaN(y) || y < mn.y - 0.05f || y > mx.y + 0.05f) continue;
                        if (holes[j, i]) { holes[j, i] = false; any = true; }
                    }
                if (any) td.SetHoles(i0, j0, holes);
            }
        }

        static readonly Dictionary<int, (int[] tris, Vector3[] nrm, Vector3[] pos)> meshData = new Dictionary<int, (int[], Vector3[], Vector3[])>();

        /// <summary>Which side of the hit triangle the ray came from, using the authored vertex normals (winding as a fallback).</summary>
        static bool FrontFacing(RaycastHit h, Vector3 dir)
        {
            if (h.collider is MeshCollider mc && mc.sharedMesh != null && h.triangleIndex >= 0)
            {
                var m = mc.sharedMesh;
                int id = m.GetInstanceID();
                if (!meshData.TryGetValue(id, out var md))
                {
                    var src = m.isReadable ? m : GpuCopy(m);
                    if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] facing data for '{m.name}' readable={m.isReadable} sub={src?.subMeshCount} on {mc.name}");
                    md = src != null ? (src.triangles, src.normals, src.vertices) : (null, null, null);
                    meshData[id] = md;
                }
                int i = h.triangleIndex * 3;
                if (md.tris != null && i + 2 < md.tris.Length)
                {
                    int a = md.tris[i], b = md.tris[i + 1], c = md.tris[i + 2];
                    var l2w = mc.transform.localToWorldMatrix;
                    Vector3 n;
                    if (md.nrm != null && md.nrm.Length == md.pos.Length && md.nrm.Length > 0)
                        n = l2w.inverse.transpose.MultiplyVector(md.nrm[a] + md.nrm[b] + md.nrm[c]);
                    else
                    {
                        n = Vector3.Cross(l2w.MultiplyPoint3x4(md.pos[b]) - l2w.MultiplyPoint3x4(md.pos[a]), l2w.MultiplyPoint3x4(md.pos[c]) - l2w.MultiplyPoint3x4(md.pos[a]));
                        if (l2w.determinant < 0) n = -n;
                    }
                    if (n.sqrMagnitude > 1e-12f) return Vector3.Dot(n, dir) < 0;
                }
            }
            return Vector3.Dot(h.normal, dir) < 0;
        }

        static bool RayCarved(Carved cv, Vector3 o, Vector3 dir, float maxDist, out float best, out bool front)
        {
            best = maxDist; front = false;
            bool found = false;
            // which triangles this query already tested (a triangle sits in every column it spans): stamped, no new set
            int n = cv.OrigTris.Count;
            if (cv.Seen == null || cv.Seen.Length != n) { cv.Seen = new int[n]; cv.Stamp = 0; }
            if (++cv.Stamp == int.MaxValue) { System.Array.Clear(cv.Seen, 0, n); cv.Stamp = 1; }
            int stamp = cv.Stamp;
            // an axis-aligned ray (all the ground queries): walk its columns outward and stop once the next column starts
            // beyond the nearest hit; skip triangles whose bounds the ray can't touch before testing them exactly
            int axis = AxisOf(dir);
            if (axis >= 0 && cv.TriMin.Count == n)
            {
                float sgn = dir[axis];
                var end = o + dir * maxDist;
                var cs = ChunkOf(cv, o); var ce = ChunkOf(cv, end);
                int steps = axis == 0 ? Mathf.Abs(ce.x - cs.x) : axis == 2 ? Mathf.Abs(ce.y - cs.y) : 0;
                const float eps = 0.02f; // (wider than the hit test's own tolerance on long triangles: never rejects a hit it would accept)
                for (int k = 0; k <= steps; k++)
                {
                    int dk = sgn > 0 ? k : -k;
                    var col = axis == 0 ? new Vector2Int(cs.x + dk, cs.y) : axis == 2 ? new Vector2Int(cs.x, cs.y + dk) : cs;
                    if (k > 0)
                    {
                        // where this column begins along the ray: past the nearest hit, nothing further can be nearer
                        int ci = axis == 0 ? col.x : col.y;
                        float edge = (sgn > 0 ? ci : ci + 1) * cv.ChunkSize;
                        if (Mathf.Abs(edge - o[axis]) - eps > best) break;
                    }
                    if (!cv.OrigIndex.TryGetValue(col, out var list)) continue;
                    for (int li = 0; li < list.Count; li++)
                    {
                        int id = list[li];
                        if (cv.Seen[id] == stamp) continue;
                        cv.Seen[id] = stamp;
                        Vector3 mn = cv.TriMin[id], mx = cv.TriMax[id];
                        // the ray's fixed coordinates must be within the triangle's bounds, and it must reach them
                        if (axis != 0 && (o.x < mn.x - eps || o.x > mx.x + eps)) continue;
                        if (axis != 1 && (o.y < mn.y - eps || o.y > mx.y + eps)) continue;
                        if (axis != 2 && (o.z < mn.z - eps || o.z > mx.z + eps)) continue;
                        if (sgn > 0 ? (mx[axis] < o[axis] - eps || mn[axis] > o[axis] + best + eps)
                                    : (mn[axis] > o[axis] + eps || mx[axis] < o[axis] - best - eps)) continue;
                        var tr = cv.OrigTris[id];
                        if (!RayTri(o, dir, cv.World[tr.a], cv.World[tr.b], cv.World[tr.c], out float t) || t >= best) continue;
                        best = t; front = Vector3.Dot(tr.n, dir) < 0; found = true;
                    }
                }
                return found;
            }
            // any other ray: every column in its XZ extent
            var e2 = o + dir * maxDist;
            var c0 = ChunkOf(cv, Vector3.Min(o, e2)); var c1 = ChunkOf(cv, Vector3.Max(o, e2));
            for (int x = c0.x; x <= c1.x; x++)
                for (int z = c0.y; z <= c1.y; z++)
                {
                    if (!cv.OrigIndex.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    for (int li = 0; li < list.Count; li++)
                    {
                        int id = list[li];
                        if (cv.Seen[id] == stamp) continue;
                        cv.Seen[id] = stamp;
                        var tr = cv.OrigTris[id];
                        if (!RayTri(o, dir, cv.World[tr.a], cv.World[tr.b], cv.World[tr.c], out float t) || t >= best) continue;
                        best = t; front = Vector3.Dot(tr.n, dir) < 0; found = true;
                    }
                }
            return found;
        }

        /// <summary>(dev) The fast carved-mesh ray against a test of every original triangle, for random axis-aligned rays
        /// around a point: how many disagree (hit or not, distance, facing). Should always be 0.</summary>
        public static string DevCheckRays(Vector3 center, float radius, int n)
        {
            int rays = 0, hits = 0, bad = 0; string first = null;
            var rng = new System.Random(12345);
            float[] lengths = { 0.75f * 1.4f, 3f * 1.4f, 60f, 600f };
            foreach (var cv in carved.Values)
            {
                if (!cv.Ready || cv.Go == null || cv.OrigCollider == null) continue;
                for (int i = 0; i < n; i++)
                {
                    var o = center + new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)) * radius;
                    var dir = Ground_Axes[rng.Next(6)];
                    float max = lengths[rng.Next(lengths.Length)];
                    bool fast = RayCarved(cv, o, dir, max, out float t1, out bool f1);
                    // every original triangle, nothing skipped
                    bool slow = false; float t2 = max; bool f2 = false;
                    foreach (var tr in cv.OrigTris)
                    {
                        if (!RayTri(o, dir, cv.World[tr.a], cv.World[tr.b], cv.World[tr.c], out float t) || t >= t2) continue;
                        t2 = t; f2 = Vector3.Dot(tr.n, dir) < 0; slow = true;
                    }
                    rays++; if (slow) hits++;
                    if (fast != slow || (slow && (Mathf.Abs(t1 - t2) > 1e-4f || f1 != f2)))
                    {
                        bad++;
                        if (first == null) first = $"{cv.Go.name} o={o} dir={dir} max={max}: fast {fast} {t1:F4} {f1} / all {slow} {t2:F4} {f2}";
                    }
                }
            }
            return $"rays={rays} hitting={hits} disagree={bad}" + (first != null ? " first: " + first : "");
        }

        static readonly Vector3[] Ground_Axes = { Vector3.down, Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

        /// <summary>0, 1 or 2 for a ray along x, y or z (either way), else -1.</summary>
        static int AxisOf(Vector3 d)
        {
            const float e = 1e-6f;
            bool x = Mathf.Abs(d.x) > e, y = Mathf.Abs(d.y) > e, z = Mathf.Abs(d.z) > e;
            if (x && !y && !z) return 0;
            if (y && !x && !z) return 1;
            if (z && !x && !y) return 2;
            return -1;
        }

        static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1e-5f || u > 1 + 1e-5f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-5f || u + v > 1 + 1e-5f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-4f;
        }

        // ------------------------------------------------------------------ cutting

        static List<MeshRenderer> renderOnly;

        /// <summary>Every level object (with collision or render-only trim) that overlaps a box and can be carved.</summary>
        public static List<GameObject> ObjectsIn(Vector3 mn, Vector3 mx)
        {
            var res = new HashSet<GameObject>();
            var c = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f;
            foreach (var h in Physics.OverlapBox(c, half, Quaternion.identity, CarveMask | InvisibleBoxMask, QueryTriggerInteraction.Ignore))
            {
                var go = GroundObject(h);
                if (go != null && CanCarve(go, out _)) res.Add(go);
            }
            foreach (var cv in carved.Values)
                if (cv.Ready && cv.Go != null && cv.WorldBounds.Intersects(new Bounds(c, mx - mn))) res.Add(cv.Go);
            // render-only meshes on level layers: trim and baseboards, and the visible shell of rooms whose collision is a
            // separate invisible mesh (e.g. Experimentation's start room: cutting only the collision left a wall you could
            // walk into). Inside the facility, whatever their size and even while the game has them culled (it switches
            // off the renderers of rooms you can't see, so "enabled" says nothing); outside, small enabled trim only.
            if (renderOnly == null)
            {
                renderOnly = Object.FindObjectsOfType<MeshRenderer>().Where(r => ((1 << r.gameObject.layer) & CarveMask) != 0 &&
                    r.GetComponent<Collider>() == null && InCurrentLevel(r.gameObject) && !Excluded(r.gameObject) &&
                    (r.enabled || Facility.Contains(r.gameObject))).ToList();
            }
            var box = new Bounds(c, mx - mn);
            foreach (var r in renderOnly)
            {
                if (r == null || !r.bounds.Intersects(box)) continue;
                bool facility = Facility.Contains(r.gameObject);
                if (!facility && (!r.enabled || r.bounds.size.magnitude >= 60f)) continue;
                if (CanCarve(r.gameObject, out _) && !IsPropVisual(r)) res.Add(r.gameObject);
            }
            return res.ToList();
        }

        public static void ForgetRenderOnlyCache() => renderOnly = null;

        /// <summary>
        /// The visual of a prop (server rack, shelf, machine) whose collision lives on a separate collider: cutting only
        /// the visual would leave invisible collision behind, so props stay whole.
        /// </summary>
        /// <summary>
        /// Part of a moon tree (an object tagged Tree, or its LOD models under it): trees are chopped whole, not carved, so a
        /// hole dug beside one shows its buried trunk like any prop's.
        /// </summary>
        static bool IsTree(Transform t)
        {
            for (; t != null; t = t.parent) if (t.CompareTag("Tree") || t.name.StartsWith("tree", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsPropVisual(Renderer r)
        {
            var b = r.bounds;
            if (b.size.magnitude > 10f) return false; // a room's shell, not furniture
            foreach (var h in Physics.OverlapBox(b.center, b.extents * 0.9f + Vector3.one * 0.01f, Quaternion.identity, (1 << 6) | (1 << 11) | (1 << 0), QueryTriggerInteraction.Ignore))
            {
                if (h is MeshCollider) continue; // level shell
                if (h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<GrabbableObject>() != null) continue;
                var hb = h.bounds;
                // a collider of roughly the prop's size, sharing its hierarchy or footprint
                if (h.transform.IsChildOf(r.transform.root) && (hb.size.magnitude < b.size.magnitude * 2.5f)) return true;
            }
            return false;
        }

        /// <summary>Apply a cut locally (every client runs this for every cut).</summary>
        public static void Apply(Cut cut, bool record = true)
        {
            var go = FindByPath(cut.Path);
            if (go == null) { Plugin.Log.LogWarning("Dig: object not found: " + cut.Path); return; }
            var ter = go.GetComponent<Terrain>();
            if (ter != null)
            {
                if (cut.Mins == null || cut.Mins.Length == 0) return;
                if (record) Cuts.Add(cut);
                ApplyTerrain(ter, cut);
                for (int k = 0; k < cut.Mins.Length; k++)
                {
                    ItemGravity.Removed(new Bounds((cut.Mins[k] + cut.Maxs[k]) * 0.5f, cut.Maxs[k] - cut.Mins[k]));
                    AddHoleObstacle(go, cut.Mins[k], cut.Maxs[k]);
                }
                return;
            }
            var cv = Prepare(go);
            if (cv == null || cut.Mins == null || cut.Mins.Length == 0) return;
            if (record) Cuts.Add(cut);
            cv.CutBoxes += cut.Mins.Length;
            var cache = new Dictionary<(int, int, int), int>();
            int nb = cut.Mins.Length;
            Vector3 umn = cut.Mins[0], umx = cut.Maxs[0];
            for (int k = 1; k < nb; k++) { umn = Vector3.Min(umn, cut.Mins[k]); umx = Vector3.Max(umx, cut.Maxs[k]); }
            bool hasCol = cv.OrigCollider != null;
            for (int s = 0; s < cv.Sub.Length; s++)
            {
                var src = cv.Sub[s];
                var dst = new List<int>(src.Count + 64);
                bool changed = false;
                for (int i = 0; i < src.Count; i += 3)
                {
                    int ia = src[i], ib = src[i + 1], ic = src[i + 2];
                    Vector3 a = cv.World[ia], b = cv.World[ib], c = cv.World[ic];
                    Vector3 tmn = Vector3.Min(a, Vector3.Min(b, c)), tmx = Vector3.Max(a, Vector3.Max(b, c));
                    bool any = tmx.x > umn.x && tmn.x < umx.x && tmx.z > umn.z && tmn.z < umx.z && tmx.y > umn.y && tmn.y < umx.y;
                    List<List<PV>> pieces = any ? MeshClip.ClipOutsideBoxes(V(ia, cv), V(ib, cv), V(ic, cv), cut.Mins, cut.Maxs) : null;
                    if (pieces == null) any = false;
                    if (!any) { dst.Add(ia); dst.Add(ib); dst.Add(ic); continue; }
                    changed = true;
                    if (hasCol) RemoveFromChunk(cv, ia, ib, ic);
                    foreach (var piece in pieces)
                    {
                        if (piece.Count < 3) continue;
                        var ids = piece.Select(pv => VertexFor(cv, pv, cache)).ToList();
                        for (int k = 1; k + 1 < ids.Count; k++)
                        {
                            if (ids[0] == ids[k] || ids[k] == ids[k + 1] || ids[0] == ids[k + 1]) continue;
                            dst.Add(ids[0]); dst.Add(ids[k]); dst.Add(ids[k + 1]);
                            if (hasCol) AddToChunk(cv, ids[0], ids[k], ids[k + 1]);
                        }
                    }
                }
                if (changed) cv.Sub[s] = dst;
            }
            WriteRenderMesh(cv);
            if (hasCol) foreach (var chk in cv.Chunks.Values) if (chk.Dirty) RebuildChunk(cv, chk);

            if (!hasCol) return;
            for (int k = 0; k < nb; k++) ItemGravity.Removed(new Bounds((cut.Mins[k] + cut.Maxs[k]) * 0.5f, cut.Maxs[k] - cut.Mins[k]));
            // monsters' navmesh doesn't know about the hole: carve it so they path around instead of walking on air
            for (int k = 0; k < nb; k++) AddHoleObstacle(go, cut.Mins[k], cut.Maxs[k]);
        }

        static void AddHoleObstacle(GameObject go, Vector3 mn, Vector3 mx)
        {
            var holeRoot = BlockWorld.Instance != null ? BlockWorld.Instance.FrameRoot(0, true) : null;
            var hole = new GameObject("LMC_HoleNav");
            if (holeRoot != null) hole.transform.SetParent(holeRoot, true);
            else SceneManager.MoveGameObjectToScene(hole, go.scene);
            hole.transform.position = (mn + mx) * 0.5f;
            hole.transform.rotation = Quaternion.identity;
            var ob = hole.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            ob.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            ob.carving = true;
            ob.size = mx - mn + new Vector3(0, 0.6f, 0);
        }

        static PV V(int i, Carved cv) => PV.Original(i, cv.World[i]);

        static int VertexFor(Carved cv, PV pv, Dictionary<(int, int, int), int> cache)
        {
            if (pv.Src0 >= 0 && pv.Src0 == pv.Src1) return pv.Src0;
            var weights = new Dictionary<int, float>();
            MeshClip.Accumulate(pv, 1f, weights);
            var keyParts = weights.OrderBy(k => k.Key).Take(3).Select(k => (k.Key, Mathf.RoundToInt(k.Value * 10000))).ToArray();
            var key = (keyParts.Length > 0 ? keyParts[0].Key * 7919 + keyParts[0].Item2 : 0,
                       keyParts.Length > 1 ? keyParts[1].Key * 7919 + keyParts[1].Item2 : 0,
                       keyParts.Length > 2 ? keyParts[2].Key * 7919 + keyParts[2].Item2 : 0);
            if (cache.TryGetValue(key, out int existing)) return existing;
            int ni = cv.Pos.Count;
            Vector3 pos = Vector3.zero, nrm = Vector3.zero; Vector4 tan = Vector4.zero; Color col = Color.clear;
            var uvs = new Vector4[8];
            foreach (var kv in weights)
            {
                int i = kv.Key; float wgt = kv.Value;
                pos += cv.Pos[i] * wgt;
                if (cv.Nrm.Count > i) nrm += cv.Nrm[i] * wgt;
                if (cv.Tan.Count > i) tan += cv.Tan[i] * wgt;
                if (cv.Col.Count > i) col += cv.Col[i] * wgt;
                for (int ch = 0; ch < 8; ch++) if (cv.Uv[ch] != null) uvs[ch] += cv.Uv[ch][i] * wgt;
            }
            cv.Pos.Add(pos);
            cv.World.Add(cv.L2W.MultiplyPoint3x4(pos));
            if (cv.Nrm.Count > 0) cv.Nrm.Add(nrm.normalized);
            if (cv.Tan.Count > 0) { var t3 = new Vector3(tan.x, tan.y, tan.z).normalized; cv.Tan.Add(new Vector4(t3.x, t3.y, t3.z, tan.w >= 0 ? 1 : -1)); }
            if (cv.Col.Count > 0) cv.Col.Add(col);
            for (int ch = 0; ch < 8; ch++) if (cv.Uv[ch] != null) cv.Uv[ch].Add(uvs[ch]);
            cache[key] = ni;
            return ni;
        }

    }
}
