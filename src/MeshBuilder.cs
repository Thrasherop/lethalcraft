using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Builds block meshes in "pixel space": a block spans 0..16 on each axis (like Minecraft models),
    /// the final mesh is centered and scaled to a 1x1x1 unit cube. UVs are derived from vertex position
    /// (Minecraft-style auto UV) with optional per-face pixel offsets.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        readonly List<int> trisEmit = new List<int>();
        /// <summary>While true, faces go to submesh 1 (rendered with an emissive material).</summary>
        public bool EmitMode;
        List<int> T => EmitMode ? trisEmit : tris;

        public static readonly Vector3[] FaceN =
        {
            Vector3.down, Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right
        };

        // texture "up" direction for each face in canonical space
        static Vector3 DefaultUp(int f)
        {
            switch (f)
            {
                case 0: return Vector3.back;    // bottom
                case 1: return Vector3.forward; // top
                default: return Vector3.up;     // sides
            }
        }

        public struct FaceTex
        {
            public string Tile;
            public Vector2 UvOffset; // pixel shift applied to auto UVs
            public bool Skip;
            public Vector3? Up;      // override texture up axis
            public FaceTex(string tile) { Tile = tile; UvOffset = Vector2.zero; Skip = false; Up = null; }
        }

        /// <summary>Adds an axis aligned box (pixel coords 0..16). faces indexed by Face enum (D,U,N(+Z),S(-Z),W(-X),E(+X)).</summary>
        public void Box(Vector3 min, Vector3 max, FaceTex[] faces, Quaternion? rot = null, Vector3? pivot = null)
        {
            var center = (min + max) * 0.5f;
            var size = max - min;
            for (int f = 0; f < 6; f++)
            {
                var ft = faces[f];
                if (ft.Skip || ft.Tile == null) continue;
                Vector3 N = FaceN[f];
                Vector3 U = ft.Up ?? DefaultUp(f);
                Vector3 D = -N;
                Vector3 R = Vector3.Cross(U, D);
                float sizeR = Mathf.Abs(Vector3.Dot(size, R));
                float sizeU = Mathf.Abs(Vector3.Dot(size, U));
                Vector3 fc = center + Vector3.Scale(N, size) * 0.5f;
                Rect tile = Atlas.UV(ft.Tile);
                int baseIdx = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    float s = (k == 1 || k == 2) ? 1 : 0;
                    float t = (k >= 2) ? 1 : 0;
                    Vector3 p = fc + R * (s - 0.5f) * sizeR + U * (t - 0.5f) * sizeU;
                    // auto uv from position relative to block center (8,8,8)
                    var rel = p - new Vector3(8, 8, 8);
                    float upx = Vector3.Dot(rel, R) + 8 + ft.UvOffset.x;
                    float tpx = Vector3.Dot(rel, U) + 8 - ft.UvOffset.y;
                    upx = Mathf.Clamp(upx, 0, 16);
                    tpx = Mathf.Clamp(tpx, 0, 16);
                    Vector3 pv = p;
                    Vector3 nv = N;
                    if (rot.HasValue)
                    {
                        var pvt = pivot ?? new Vector3(8, 8, 8);
                        pv = rot.Value * (p - pvt) + pvt;
                        nv = rot.Value * N;
                    }
                    verts.Add((pv - new Vector3(8, 8, 8)) / 16f);
                    norms.Add(nv);
                    uvs.Add(new Vector2(tile.x + upx / 16f * tile.width, tile.y + tpx / 16f * tile.height));
                }
                // ensure winding matches the normal
                Vector3 a = verts[baseIdx], b = verts[baseIdx + 1], c = verts[baseIdx + 2];
                var tl = T;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), norms[baseIdx]) >= 0)
                {
                    tl.Add(baseIdx); tl.Add(baseIdx + 1); tl.Add(baseIdx + 2);
                    tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 3);
                }
                else
                {
                    tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 1);
                    tl.Add(baseIdx); tl.Add(baseIdx + 3); tl.Add(baseIdx + 2);
                }
            }
        }

        /// <summary>A two-sided quad (pixel coords, corners counter-clockwise from bottom-left) showing a whole tile.</summary>
        public void Plane(Vector3 a, Vector3 b, Vector3 c, Vector3 d, string tile)
        {
            Rect t = Atlas.UV(tile);
            var n = Vector3.Cross(b - a, d - a).normalized;
            var corners = new[] { a, b, c, d };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            for (int side = 0; side < 2; side++)
            {
                int baseIdx = verts.Count;
                for (int k = 0; k < 4; k++)
                {
                    verts.Add((corners[k] - new Vector3(8, 8, 8)) / 16f);
                    norms.Add(side == 0 ? n : -n);
                    uvs.Add(new Vector2(t.x + uv[k].x * t.width, t.y + uv[k].y * t.height));
                }
                var tl = T;
                if (side == 0) { tl.Add(baseIdx); tl.Add(baseIdx + 1); tl.Add(baseIdx + 2); tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 3); }
                else { tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 1); tl.Add(baseIdx); tl.Add(baseIdx + 3); tl.Add(baseIdx + 2); }
            }
        }

        public void Box(Vector3 min, Vector3 max, string allTile, Quaternion? rot = null, Vector3? pivot = null)
        {
            var f = new FaceTex[6];
            for (int i = 0; i < 6; i++) f[i] = new FaceTex(allTile);
            Box(min, max, f, rot, pivot);
        }

        /// <summary>Flat quad, used for redstone dust. Pixel space.</summary>
        public void FlatQuad(Vector3 min, Vector3 max, string tile, float y, bool doubleSided = true)
        {
            var f = new FaceTex[6];
            f[1] = new FaceTex(tile);
            if (doubleSided) f[0] = new FaceTex(tile);
            for (int i = 2; i < 6; i++) f[i].Skip = true;
            if (!doubleSided) f[0].Skip = true;
            Box(new Vector3(min.x, y, min.z), new Vector3(max.x, y + 0.001f, max.z), f);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            if (trisEmit.Count > 0)
            {
                m.subMeshCount = 2;
                m.SetTriangles(tris, 0);
                m.SetTriangles(trisEmit, 1);
            }
            else m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            m.UploadMeshData(false);
            return m;
        }

        public bool Empty => verts.Count == 0;
        public static bool HasGlow(Mesh m) => m != null && m.subMeshCount > 1;

        // ================================================================== block meshes
        static readonly Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();

        /// <summary>
        /// Observer faces for a facing (variant = facing + 1; 0 = the item's, facing south). The block is built with its face
        /// at +Y and turned to its facing, so each side's texture is oriented for where it ends up: upright on the sides
        /// the world sees edge-on, and on the world top/bottom with Minecraft's arrow (observer_top) pointing to the face.
        /// </summary>
        static FaceTex[] ObserverFaces(int variant, bool pulsing)
        {
            int f = variant >= 1 && variant <= 6 ? variant - 1 : (int)Face.South;
            var dir = (Vector3)Faces.Dir[f];
            var inv = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, dir));
            // the face's "forward" in the horizontal plane (an up/down facing observer just uses north)
            var ahead = Mathf.Abs(dir.y) > 0.5f ? Vector3.forward : dir;
            var faces = new FaceTex[6];
            for (int i = 0; i < 6; i++)
            {
                var nWorld = Quaternion.FromToRotation(Vector3.up, dir) * FaceN[i];
                bool worldFlat = Mathf.Abs(nWorld.y) > 0.5f; // ends up as a top or bottom
                string tile = i == (int)Face.Up ? "observer_front" : i == (int)Face.Down ? (pulsing ? "observer_back_on" : "observer_back")
                    : worldFlat ? "observer_top" : "observer_side";
                var up = inv * (worldFlat ? ahead : Vector3.up);
                up = new Vector3(Mathf.Round(up.x), Mathf.Round(up.y), Mathf.Round(up.z));
                faces[i] = new FaceTex(tile) { Up = up };
            }
            return faces;
        }

        static FaceTex[] Six(string d, string u, string n, string s, string w, string e)
            => new[] { new FaceTex(d), new FaceTex(u), new FaceTex(n), new FaceTex(s), new FaceTex(w), new FaceTex(e) };

        /// <summary>
        /// Mesh for a block in canonical orientation. Axis-oriented blocks (pistons, logs) are built facing +Y and
        /// rotated by BlockWorld; front-oriented (jack o'lantern) face +Z. variant: extra connection bits (dust).
        /// </summary>
        public static Mesh For(BlockDef def, byte state, int variant = 0)
        {
            int key = def.Id | (state << 8) | (variant << 16);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            var mb = new MeshBuilder();
            var full0 = Vector3.zero;
            var full1 = new Vector3(16, 16, 16);
            switch (def.Shape)
            {
                case BlockShape.Cube:
                    if (def == Blocks.Piston || def == Blocks.StickyPiston)
                    {
                        bool ext = (state & 1) != 0;
                        // canonical: facing +Y. sides: texture up = +Y so the wooden strip is at the front
                        var faces = Six(def.TileBottom, ext ? "piston_inner" : def.TileTop, def.TileSide, def.TileSide, def.TileSide, def.TileSide);
                        if (ext)
                        {
                            for (int i = 2; i < 6; i++) faces[i].UvOffset = new Vector2(0, 4); // shift down: skip the wooden strip
                            mb.Box(new Vector3(0, 0, 0), new Vector3(16, 12, 16), faces);
                        }
                        else mb.Box(full0, full1, faces);
                    }
                    else if (def == Blocks.Observer)
                        mb.Box(full0, full1, ObserverFaces(variant, (state & 1) != 0));
                    else if (def.Directional && def.FacingIncludesVertical)
                    {
                        mb.Box(full0, full1, Six(def.TileBottom, def.TileTop, def.TileSide, def.TileSide, def.TileSide, def.TileSide));
                    }
                    else if (def.Directional)
                    {
                        bool litFurnace = def == Blocks.Furnace && (state & 1) != 0;
                        var faces = Six(def.TileBottom, def.TileTop, litFurnace ? "furnace_front_on" : def.TileFront, def.TileSide, def.TileSide, def.TileSide);
                        bool glowFront = def == Blocks.JackOLantern || litFurnace;
                        if (glowFront) faces[2].Skip = true;
                        mb.Box(full0, full1, faces);
                        if (glowFront)
                        {
                            var front = new FaceTex[6];
                            for (int i = 0; i < 6; i++) front[i].Skip = true;
                            front[2] = new FaceTex(litFurnace ? "furnace_front_on" : def.TileFront);
                            mb.EmitMode = true;
                            mb.Box(full0, full1, front);
                            mb.EmitMode = false;
                        }
                    }
                    else
                    {
                        string side = def.TileSide;
                        string top = def.TileTop, bottom = def.TileBottom;
                        if (def == Blocks.RedstoneLamp) side = top = bottom = (state & 1) != 0 ? "lamp_on" : "lamp_off";
                        mb.Box(full0, full1, Six(bottom, top, side, side, side, side));
                    }
                    break;

                case BlockShape.Fire:
                    {
                        int frames = Atlas.FireFrames;
                        string ft = frames > 0 ? "fire_0_f" + (variant % frames) : "fire_0";
                        string ft1 = frames > 0 ? "fire_1_f" + (variant % frames) : "fire_0";
                        mb.EmitMode = true;
                        // four planes just inside the edges, leaning in at the top (Minecraft's fire_floor), 22 px tall
                        mb.Plane(new Vector3(0, 0, 1), new Vector3(16, 0, 1), new Vector3(16, 22, 4), new Vector3(0, 22, 4), ft);
                        mb.Plane(new Vector3(16, 0, 15), new Vector3(0, 0, 15), new Vector3(0, 22, 12), new Vector3(16, 22, 12), ft);
                        mb.Plane(new Vector3(1, 0, 16), new Vector3(1, 0, 0), new Vector3(4, 22, 0), new Vector3(4, 22, 16), ft);
                        mb.Plane(new Vector3(15, 0, 0), new Vector3(15, 0, 16), new Vector3(12, 22, 16), new Vector3(12, 22, 0), ft);
                        // and two crossed through the middle
                        mb.Plane(new Vector3(0, 0, 0), new Vector3(16, 0, 16), new Vector3(16, 20, 16), new Vector3(0, 20, 0), ft1);
                        mb.Plane(new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(0, 20, 16), new Vector3(16, 20, 0), ft1);
                        mb.EmitMode = false;
                    }
                    break;

                case BlockShape.PistonHead:
                    {
                        bool sticky = (state & 1) != 0;
                        string face = sticky ? "sticky_top" : "piston_top";
                        // plate at the front (+Y in canonical space) 4px thick
                        var plate = Six("piston_top", face, "piston_side", "piston_side", "piston_side", "piston_side");
                        for (int i = 2; i < 6; i++) plate[i].UvOffset = new Vector2(0, 0);
                        mb.Box(new Vector3(0, 12, 0), new Vector3(16, 16, 16), plate);
                        // rod reaching back into the base (base is 12px tall when extended, so rod spans -4..12)
                        var rod = Six(null, null, "piston_side", "piston_side", "piston_side", "piston_side");
                        rod[0].Skip = true; rod[1].Skip = true;
                        for (int i = 2; i < 6; i++) { rod[i].UvOffset = new Vector2(0, 0); rod[i].Up = Vector3.up; }
                        mb.Box(new Vector3(6, -4, 6), new Vector3(10, 12, 10), rod);
                    }
                    break;

                case BlockShape.Torch:
                    {
                        bool wall = variant == 1; // variant 1 = wall-mounted (canonical: wall at -Z, torch leans to +Z)
                        string tile = def == Blocks.RedstoneTorch ? ((state & 1) != 0 ? "redstone_torch_off" : "redstone_torch") : "item_torch";
                        var f = Six(tile, tile, tile, tile, tile, tile);
                        f[1].UvOffset = new Vector2(0, -2); // top face shows the flame texels (rows 6..8)
                        f[0].UvOffset = new Vector2(0, 0);
                        bool lit = def != Blocks.RedstoneTorch || (state & 1) == 0;
                        var stick = Six(tile, tile, tile, tile, tile, tile);
                        stick[1].Skip = true;
                        if (!wall)
                        {
                            mb.Box(new Vector3(7, 0, 7), new Vector3(9, 8, 9), stick);
                            mb.EmitMode = lit;
                            mb.Box(new Vector3(7, 8, 7), new Vector3(9, 10, 9), f);
                            mb.EmitMode = false;
                        }
                        else
                        {
                            // same texels as the floor torch: the wall torch sits 3.5px higher, so shift side UVs down
                            for (int i = 2; i < 6; i++) { stick[i].UvOffset = new Vector2(0, 3.5f); f[i].UvOffset = new Vector2(0, 3.5f); }
                            // west/east faces span z -1..1 (block edge) instead of 7..9: shift to the stick column
                            stick[4].UvOffset.x = -8; f[4].UvOffset.x = -8;
                            stick[5].UvOffset.x = 8; f[5].UvOffset.x = 8;
                            var rot = Quaternion.Euler(22.5f, 0, 0); // lean away from wall (+Z)
                            var pivot = new Vector3(8, 3.5f, 0);
                            mb.Box(new Vector3(7, 3.5f, -1f), new Vector3(9, 11.5f, 1f), stick, rot, pivot);
                            mb.EmitMode = lit;
                            mb.Box(new Vector3(7, 11.5f, -1f), new Vector3(9, 13.5f, 1f), f, rot, pivot);
                            mb.EmitMode = false;
                        }
                    }
                    break;

                case BlockShape.Lever:
                    {
                        // canonical: attached to the floor (-Y). variant bit: 0 = floor, 1 = wall (base on -Z)
                        bool on = (state & 1) != 0;
                        string baseTile = "cobblestone";
                        if (variant == 0)
                        {
                            mb.Box(new Vector3(5, 0, 4), new Vector3(11, 3, 12), baseTile);
                            var rot = Quaternion.Euler(on ? 45f : -45f, 0, 0);
                            mb.Box(new Vector3(7, 1, 7), new Vector3(9, 11, 9), Six("lever_handle", "lever_handle", "lever_handle", "lever_handle", "lever_handle", "lever_handle"), rot, new Vector3(8, 1, 8));
                        }
                        else
                        {
                            mb.Box(new Vector3(5, 4, 0), new Vector3(11, 12, 3), baseTile);
                            // handle hinges at the base and sticks out of the wall (+Z), tilted up (off) or down (on)
                            var rot = Quaternion.Euler(on ? 135f : 45f, 0, 0);
                            mb.Box(new Vector3(7, 8, 1), new Vector3(9, 18, 3), Six("lever_handle", "lever_handle", "lever_handle", "lever_handle", "lever_handle", "lever_handle"), rot, new Vector3(8, 8, 2));
                        }
                    }
                    break;

                case BlockShape.Button:
                    {
                        bool pressed = (state & 1) != 0;
                        float depth = pressed ? 1 : 2;
                        if (variant == 0) mb.Box(new Vector3(5, 0, 6), new Vector3(11, depth, 10), "stone");
                        else mb.Box(new Vector3(5, 6, 0), new Vector3(11, 10, depth), "stone");
                    }
                    break;

                case BlockShape.Plate:
                    {
                        bool pressed = (state & 1) != 0;
                        mb.Box(new Vector3(1, 0, 1), new Vector3(15, pressed ? 0.5f : 1f, 15), "stone");
                    }
                    break;

                case BlockShape.Lava:
                    mb.Box(new Vector3(0, 0, 0), new Vector3(16, 14, 16), "lava");
                case BlockShape.Pane:
                    {
                        // glass on the broad sides, the pane's edge texture on the thin ones
                        string edge = Atlas.Tiles.ContainsKey("glass_pane_top") ? "glass_pane_top" : "glass";
                        int c = variant & 15;
                        var ns = new[] { new FaceTex("glass"), new FaceTex("glass"), new FaceTex(edge), new FaceTex(edge), new FaceTex("glass"), new FaceTex("glass") }; // (down, up, n, s, w, e)
                        var we = new[] { new FaceTex("glass"), new FaceTex("glass"), new FaceTex("glass"), new FaceTex("glass"), new FaceTex(edge), new FaceTex(edge) };
                        var post = new[] { new FaceTex(edge), new FaceTex(edge), new FaceTex(edge), new FaceTex(edge), new FaceTex(edge), new FaceTex(edge) };
                        // tops and bottoms of the arms: the edge texture too
                        ns[0] = ns[1] = new FaceTex(edge); we[0] = we[1] = new FaceTex(edge);
                        mb.Box(new Vector3(7, 0, 7), new Vector3(9, 16, 9), post);
                        if ((c & 1) != 0) mb.Box(new Vector3(7, 0, 9), new Vector3(9, 16, 16), ns);
                        if ((c & 2) != 0) mb.Box(new Vector3(7, 0, 0), new Vector3(9, 16, 7), ns);
                        if ((c & 4) != 0) mb.Box(new Vector3(0, 0, 7), new Vector3(7, 16, 9), we);
                        if ((c & 8) != 0) mb.Box(new Vector3(9, 0, 7), new Vector3(16, 16, 9), we);
                case BlockShape.Door:
                    {
                        bool open = (state & 1) != 0, upper = (state & 2) != 0, right = (state & 4) != 0;
                        string tile = upper ? "oak_door_top" : "oak_door_bottom";
                        var t = new FaceTex(tile);
                        var side = new FaceTex("oak_planks");
                        if (!open) mb.Box(new Vector3(0, 0, 13), new Vector3(16, 16, 16), new[] { side, side, t, t, side, side });
                        else if (!right) mb.Box(new Vector3(0, 0, 0), new Vector3(3, 16, 16), new[] { side, side, side, side, t, t });
                        else mb.Box(new Vector3(13, 0, 0), new Vector3(16, 16, 16), new[] { side, side, side, side, t, t });
                    }
                    break;

                case BlockShape.Dust:
                    {
                        bool on = state > 0;
                        string line = on ? "dust_line_on" : "dust_line_off";
                        string dot = on ? "dust_dot_on" : "dust_dot_off";
                        if (!Atlas.Tiles.ContainsKey(line)) { line = on ? "dust_on" : "dust_off"; dot = line; }
                        // variant bits: 1=N(+Z) 2=S(-Z) 4=W(-X) 8=E(+X)
                        int c = variant & 15;
                        bool ns = (c & 3) != 0, we = (c & 12) != 0;
                        if (c == 0) { mb.FlatQuad(new Vector3(0, 0, 0), new Vector3(16, 0, 16), dot == line ? dot : "dust_cross_" + (on ? "on" : "off"), 0.25f); }
                        else
                        {
                            // straight line if only one axis connected
                            if (ns && !we)
                                mb.FlatQuad(new Vector3(0, 0, 0), new Vector3(16, 0, 16), line, 0.25f);
                            else if (we && !ns)
                            {
                                var f = new FaceTex[6]; f[1] = new FaceTex(line) { Up = Vector3.right }; f[0] = new FaceTex(line) { Up = Vector3.right };
                                for (int i = 2; i < 6; i++) f[i].Skip = true;
                                mb.Box(new Vector3(0, 0.25f, 0), new Vector3(16, 0.251f, 16), f);
                            }
                            else
                            {
                                // junction: dot + arms
                                mb.FlatQuad(new Vector3(5, 0, 5), new Vector3(11, 0, 11), dot, 0.26f);
                                if ((c & 1) != 0) mb.FlatQuad(new Vector3(0, 0, 8), new Vector3(16, 0, 16), line, 0.25f);
                                if ((c & 2) != 0) mb.FlatQuad(new Vector3(0, 0, 0), new Vector3(16, 0, 8), line, 0.25f);
                                if ((c & 4) != 0 || (c & 8) != 0)
                                {
                                    var f = new FaceTex[6]; f[1] = new FaceTex(line) { Up = Vector3.right }; f[0] = new FaceTex(line) { Up = Vector3.right };
                                    for (int i = 2; i < 6; i++) f[i].Skip = true;
                                    if ((c & 4) != 0) mb.Box(new Vector3(0, 0.255f, 0), new Vector3(8, 0.256f, 16), f);
                                    if ((c & 8) != 0) mb.Box(new Vector3(8, 0.255f, 0), new Vector3(16, 0.256f, 16), f);
                                }
                            }
                        }
                    }
                    break;
            }
            m = mb.ToMesh("LMC_" + def.Key + "_" + state + "_" + variant);
            cache[key] = m;
            return m;
        }

        /// <summary>A block whose top follows the terrain: heights (0..255 of a block) on a MoldRes grid.</summary>
        /// <summary>
        /// A natural block shaped to the ground in its cell. <paramref name="withTop"/>: include the surface itself. The drawn
        /// mesh leaves it out: a natural block's own cell is never cut, so the real terrain already draws that surface and
        /// a second, approximate copy of it poked through on slopes. Colliders keep it.
        /// </summary>
        public static Mesh Mold(BlockDef def, byte[] q, bool withTop = true, int exposed = 0x3F)
        {
            // exposed: canonical faces to draw (bit 0 +x, 1 -x, 3 -y bottom, 4 +z, 5 -z); the collider (withTop) gets all
            if (withTop) exposed = 0x3F;
            int n = Ground.MoldRes;
            var mb = new MeshBuilder();
            const float sink = 0.35f; // px below the terrain so the uncut ground never z-fights
            float H(int i, int j) => Mathf.Max(0f, q[j * n + i] / 255f * 16f - sink);
            // the sides reach the real surface (a hair above), so the rim of a hole never shows a slit under the ground's edge
            float HS(int i, int j) => q[j * n + i] <= 0 ? 0f : Mathf.Min(16f, q[j * n + i] / 255f * 16f + 0.12f);
            float X(int i) => i * 16f / (n - 1);
            Rect top = Atlas.UV(def.TileTop), side = Atlas.UV(def.TileSide), bot = Atlas.UV(def.TileBottom);
            // top surface
            for (int j = 0; j < n - 1 && withTop; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    var a = new Vector3(X(i), H(i, j), X(j)); var b = new Vector3(X(i + 1), H(i + 1, j), X(j));
                    var c = new Vector3(X(i + 1), H(i + 1, j + 1), X(j + 1)); var d = new Vector3(X(i), H(i, j + 1), X(j + 1));
                    mb.RawQuad(a, d, c, b, top, (x, y, z) => new Vector2(x, z));
                }
            // four sides, texture anchored to the top edge so grass fringes follow the surface
            for (int k = 0; k < n - 1; k++)
            {
                if ((exposed & 32) != 0) mb.SideStrip(new Vector3(X(k), 0, 0), new Vector3(X(k + 1), 0, 0), HS(k, 0), HS(k + 1, 0), Vector3.back, side, true);
                if ((exposed & 16) != 0) mb.SideStrip(new Vector3(X(k + 1), 0, 16), new Vector3(X(k), 0, 16), HS(k + 1, n - 1), HS(k, n - 1), Vector3.forward, side, true);
                if ((exposed & 2) != 0) mb.SideStrip(new Vector3(0, 0, X(k + 1)), new Vector3(0, 0, X(k)), HS(0, k + 1), HS(0, k), Vector3.left, side, true);
                if ((exposed & 1) != 0) mb.SideStrip(new Vector3(16, 0, X(k)), new Vector3(16, 0, X(k + 1)), HS(n - 1, k), HS(n - 1, k + 1), Vector3.right, side, true);
            }
            // bottom
            if ((exposed & 8) != 0) mb.RawQuad(new Vector3(0, 0, 0), new Vector3(16, 0, 0), new Vector3(16, 0, 16), new Vector3(0, 0, 16), bot, (x, y, z) => new Vector2(x, z), Vector3.down);
            var m = mb.ToMesh((withTop ? "LMC_moldcol_" : "LMC_mold_") + def.Key);
            return m;
        }

        delegate Vector2 UvFn(float x, float y, float z);

        void RawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect tile, UvFn uv, Vector3? normal = null)
        {
            var n = normal ?? Vector3.Cross(b - a, c - a).normalized;
            if (normal == null && n.y < 0) n = -n;
            int bi = verts.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                verts.Add((p - new Vector3(8, 8, 8)) / 16f);
                norms.Add(n);
                var t = uv(p.x, p.y, p.z);
                uvs.Add(new Vector2(tile.x + Mathf.Clamp(t.x, 0, 16) / 16f * tile.width, tile.y + Mathf.Clamp(t.y, 0, 16) / 16f * tile.height));
            }
            Vector3 va = verts[bi], vb = verts[bi + 1], vc = verts[bi + 2];
            var tl = T;
            if (Vector3.Dot(Vector3.Cross(vb - va, vc - va), n) >= 0) { tl.Add(bi); tl.Add(bi + 1); tl.Add(bi + 2); tl.Add(bi); tl.Add(bi + 2); tl.Add(bi + 3); }
            else { tl.Add(bi); tl.Add(bi + 2); tl.Add(bi + 1); tl.Add(bi); tl.Add(bi + 3); tl.Add(bi + 2); }
        }

        /// <summary>Vertical face from y=0 up to h0/h1 between two bottom corners.</summary>
        void SideStrip(Vector3 p0, Vector3 p1, float h0, float h1, Vector3 normal, Rect tile, bool topAnchored)
        {
            if (h0 <= 0.01f && h1 <= 0.01f) return;
            float hmax = Mathf.Max(h0, h1);
            // u along the edge, v measured down from the top edge (so the texture's top row sits on the surface)
            UvFn uv = (x, y, z) =>
            {
                float u = Mathf.Abs(normal.x) > 0.5f ? z : x;
                if (normal.x < -0.5f || normal.z > 0.5f) u = 16 - u;
                float v = topAnchored ? 16f - (hmax - y) : y;
                return new Vector2(u, v);
            };
            RawQuad(p0, p1, p1 + Vector3.up * h1, p0 + Vector3.up * h0, tile, uv, normal);
        }

        // ================================================================== misc meshes
        static Mesh outline, unitCube;

        public static Mesh Outline()
        {
            if (outline != null) return outline;
            var mb = new MeshBuilder();
            const float t = 0.12f; // pixel thickness
            float lo = -0.04f, hi = 16.04f;
            // 12 edges
            for (int a = 0; a < 3; a++)
                for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        Vector3 min = Vector3.zero, max = Vector3.zero;
                        int b = (a + 1) % 3, c = (a + 2) % 3;
                        min[a] = lo; max[a] = hi;
                        float pb = i == 0 ? lo : hi, pc = j == 0 ? lo : hi;
                        min[b] = pb - t; max[b] = pb + t;
                        min[c] = pc - t; max[c] = pc + t;
                        mb.Box(min, max, "obsidian");
                    }
            outline = mb.ToMesh("LMC_Outline");
            return outline;
        }

        public static Mesh UnitCube(string tile)
        {
            var mb = new MeshBuilder();
            mb.Box(Vector3.zero, new Vector3(16, 16, 16), tile);
            return mb.ToMesh("LMC_cube_" + tile);
        }

        static readonly Dictionary<string, Mesh> crackCache = new Dictionary<string, Mesh>();
        public static Mesh Crack(int stage)
        {
            string k = "crack_" + Mathf.Clamp(stage, 0, 9);
            if (crackCache.TryGetValue(k, out var m)) return m;
            var mb = new MeshBuilder();
            mb.Box(new Vector3(-0.05f, -0.05f, -0.05f), new Vector3(16.05f, 16.05f, 16.05f), k);
            m = mb.ToMesh("LMC_" + k);
            crackCache[k] = m;
            return m;
        }

        /// <summary>A tiny cube with a random 3x3 px fragment of the given tile (break particles).</summary>
        public static Mesh Fragment(string tile, System.Random rng)
        {
            var mb = new MeshBuilder();
            var f = new FaceTex[6];
            var off = new Vector2(rng.Next(0, 12) - 6.5f, rng.Next(0, 12) - 6.5f);
            for (int i = 0; i < 6; i++) f[i] = new FaceTex(tile) { UvOffset = off };
            mb.Box(new Vector3(6.5f, 6.5f, 6.5f), new Vector3(9.5f, 9.5f, 9.5f), f);
            return mb.ToMesh("LMC_frag");
        }

        static readonly Dictionary<string, Mesh> spriteCache = new Dictionary<string, Mesh>();

        /// <summary>Minecraft-style held item: the 16x16 sprite extruded 1 pixel deep. Centered, 1 unit = 16 px.</summary>
        public static Mesh ExtrudedSprite(string tile)
        {
            if (spriteCache.TryGetValue(tile, out var cached)) return cached;
            var px = Atlas.TilePixels(tile); // bottom-up rows
            var mb = new MeshBuilder();
            Rect r = Atlas.UV(tile);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < 16 && y < 16 && px[y * 16 + x].a > 20;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    if (!Solid(x, y)) continue;
                    var uv = new Vector2(r.x + (x + 0.5f) / 16f * r.width, r.y + (y + 0.5f) / 16f * r.height);
                    // front/back
                    mb.PixelQuad(new Vector3(x, y, 7.5f), Vector3.right, Vector3.up, Vector3.back, uv);
                    mb.PixelQuad(new Vector3(x + 1, y, 8.5f), Vector3.left, Vector3.up, Vector3.forward, uv);
                    if (!Solid(x - 1, y)) mb.PixelQuad(new Vector3(x, y, 8.5f), Vector3.back, Vector3.up, Vector3.left, uv);
                    if (!Solid(x + 1, y)) mb.PixelQuad(new Vector3(x + 1, y, 7.5f), Vector3.forward, Vector3.up, Vector3.right, uv);
                    if (!Solid(x, y - 1)) mb.PixelQuad(new Vector3(x, y, 8.5f), Vector3.right, Vector3.back, Vector3.down, uv);
                    if (!Solid(x, y + 1)) mb.PixelQuad(new Vector3(x, y + 1, 7.5f), Vector3.right, Vector3.forward, Vector3.up, uv);
                }
            var m = mb.ToMesh("LMC_sprite_" + tile);
            spriteCache[tile] = m;
            return m;
        }

        void PixelQuad(Vector3 origin, Vector3 u, Vector3 v, Vector3 n, Vector2 uv)
        {
            int b = verts.Count;
            Vector3[] p = { origin, origin + u, origin + u + v, origin + v };
            foreach (var q in p)
            {
                verts.Add((q - new Vector3(8, 8, 8)) / 16f);
                norms.Add(n);
                uvs.Add(uv);
            }
            Vector3 a = verts[b], bb = verts[b + 1], c = verts[b + 2];
            if (Vector3.Dot(Vector3.Cross(bb - a, c - a), n) >= 0)
            { tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3); }
            else
            { tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2); }
        }
    }
}
