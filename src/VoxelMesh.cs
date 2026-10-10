using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A small block layout turned into one mesh (#66: the Nether fortress's tiles): every face of a block that isn't
    /// against another block is drawn with that block's atlas tile, in metres (a block is BlockWorld.S). Glowing blocks
    /// (lava, glowstone, magma) go in a second submesh for the emissive material. Its collider is the same mesh.
    /// </summary>
    public class VoxelMesh
    {
        public readonly int SX, SY, SZ;
        readonly string[] cells; // atlas tile per cell (null: air)
        readonly bool[] glow;

        public VoxelMesh(int sx, int sy, int sz) { SX = sx; SY = sy; SZ = sz; cells = new string[sx * sy * sz]; glow = new bool[sx * sy * sz]; }

        int I(int x, int y, int z) => (y * SZ + z) * SX + x;
        bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < SX && y < SY && z < SZ;

        public string this[int x, int y, int z]
        {
            get => In(x, y, z) ? cells[I(x, y, z)] : null;
            set { if (In(x, y, z)) cells[I(x, y, z)] = value; }
        }

        public void Set(int x, int y, int z, string tile, bool glows = false) { if (!In(x, y, z)) return; cells[I(x, y, z)] = tile; glow[I(x, y, z)] = glows; }

        /// <summary>Fill a box of cells (inclusive bounds).</summary>
        public void Fill(int x0, int y0, int z0, int x1, int y1, int z1, string tile, bool glows = false)
        {
            for (int x = Mathf.Min(x0, x1); x <= Mathf.Max(x0, x1); x++)
                for (int y = Mathf.Min(y0, y1); y <= Mathf.Max(y0, y1); y++)
                    for (int z = Mathf.Min(z0, z1); z <= Mathf.Max(z0, z1); z++)
                        Set(x, y, z, tile, glows);
        }

        public bool Solid(int x, int y, int z) => In(x, y, z) && cells[I(x, y, z)] != null;

        static readonly Vector3Int[] Dirs = { Vector3Int.down, Vector3Int.up, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.left, Vector3Int.right };

        /// <summary>
        /// The mesh, in metres, cell (0,0,0)'s corner at `origin` (local). Faces against other blocks of the layout are
        /// left out; faces at the layout's edge are drawn (the tile next door draws its own).
        /// </summary>
        public Mesh Build(string name, Vector3 origin)
        {
            float s = BlockWorld.S;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var opaque = new List<int>(); var emit = new List<int>();
            for (int y = 0; y < SY; y++)
                for (int z = 0; z < SZ; z++)
                    for (int x = 0; x < SX; x++)
                    {
                        var t = cells[I(x, y, z)];
                        if (t == null) continue;
                        bool g = glow[I(x, y, z)];
                        var uv = Atlas.UV(t);
                        for (int f = 0; f < 6; f++)
                        {
                            var d = Dirs[f];
                            if (Solid(x + d.x, y + d.y, z + d.z)) continue;
                            // the face's corners: u along one in-plane axis, v along the other (up, for the sides)
                            Vector3 n = d;
                            Vector3 u, v;
                            if (f <= 1) { u = Vector3.right; v = Vector3.forward; }
                            else if (f <= 3) { u = Vector3.right; v = Vector3.up; }
                            else { u = Vector3.forward; v = Vector3.up; }
                            var c = origin + (new Vector3(x, y, z) + Vector3.one * 0.5f + n * 0.5f) * s;
                            int b = verts.Count;
                            for (int k = 0; k < 4; k++)
                            {
                                float a = (k == 1 || k == 2) ? 0.5f : -0.5f, e = k >= 2 ? 0.5f : -0.5f;
                                verts.Add(c + (u * a + v * e) * s);
                                norms.Add(n);
                                uvs.Add(new Vector2(uv.x + (a + 0.5f) * uv.width, uv.y + (e + 0.5f) * uv.height));
                            }
                            var list = g ? emit : opaque;
                            if (Vector3.Dot(Vector3.Cross(verts[b + 1] - verts[b], verts[b + 2] - verts[b]), n) >= 0f)
                                list.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                            else list.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                        }
                    }
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(opaque, 0); m.SetTriangles(emit, 1);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A GameObject showing the layout (block materials, the glowing ones lit) with a collider of the same shape.</summary>
        public GameObject Make(string name, Transform parent, Vector3 origin, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var mesh = Build(name, origin);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive };
            Atlas.NoDecals(r);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }
    }
}
