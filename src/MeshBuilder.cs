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
            public bool Mirror;      // flip the texture left to right (a door hinged on its other side)
            public FaceTex(string tile) { Tile = tile; UvOffset = Vector2.zero; Skip = false; Up = null; Mirror = false; }
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
                    if (ft.Mirror) upx = 16f - upx;
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

        /// <summary>
        /// An element of a Minecraft block model, as its JSON gives it: from and to in Minecraft's pixels (its z: north is
        /// -z; ours is +z, so it's turned over here), and for each face its tile and uv rectangle (u1, v1, u2, v2 from the
        /// texture's top left), the texture laid on as Minecraft lays it (no uv rotation): seen from outside, u runs to the
        /// right and v down on a side, u east and v south on the top, u east and v north underneath (#195).
        /// </summary>
        /// <summary>(#223) An element's own rotation (Minecraft's "rotation": about an axis through its origin, in its
        /// coordinates), for the next McElement calls; null: none.</summary>
        public Quaternion? McRotation; public Vector3 McRotOrigin;

        public void McElement(Vector3 from, Vector3 to, params (Face face, string tile, Vector4 uv)[] faces)
        {
            var lo = Vector3.Min(from, to); var hi = Vector3.Max(from, to);
            foreach (var (face, tile, uv) in faces)
            {
                if (tile == null || !Atlas.Tiles.ContainsKey(tile)) continue;
                // the face's normal and texture axes (u to the right, v down the texture) in Minecraft's coordinates
                Vector3 n, ud, vd;
                switch (face)
                {
                    case Face.Down: n = Vector3.down; ud = Vector3.right; vd = Vector3.back; break;
                    case Face.Up: n = Vector3.up; ud = Vector3.right; vd = Vector3.forward; break;
                    case Face.North: n = Vector3.back; ud = Vector3.left; vd = Vector3.down; break;
                    case Face.South: n = Vector3.forward; ud = Vector3.right; vd = Vector3.down; break;
                    case Face.West: n = Vector3.left; ud = Vector3.forward; vd = Vector3.down; break;
                    default: n = Vector3.right; ud = Vector3.back; vd = Vector3.down; break;
                }
                // the plane of the face, and its two other axes
                var plane = new Vector3(n.x > 0 ? hi.x : lo.x, n.y > 0 ? hi.y : lo.y, n.z > 0 ? hi.z : lo.z);
                var ua = new Vector3(Mathf.Abs(ud.x), Mathf.Abs(ud.y), Mathf.Abs(ud.z));
                var va = new Vector3(Mathf.Abs(vd.x), Mathf.Abs(vd.y), Mathf.Abs(vd.z));
                bool uNeg = ud.x + ud.y + ud.z < 0, vNeg = vd.x + vd.y + vd.z < 0;
                Rect t = Atlas.UV(tile);
                int baseIdx = verts.Count;
                if (McRotation.HasValue) n = McRotation.Value * n;
                var nMod = new Vector3(n.x, n.y, -n.z);
                for (int k = 0; k < 4; k++)
                {
                    // a, b: 0 at the low end of the face's u and v axes, 1 at the high
                    float a = (k == 1 || k == 2) ? 1 : 0, b = k >= 2 ? 1 : 0;
                    var p = Vector3.Scale(plane, Vector3.one - ua - va) + Vector3.Scale(Vector3.Lerp(lo, hi, a), ua) + Vector3.Scale(Vector3.Lerp(lo, hi, b), va);
                    // how far along the texture's u and v: an axis that points down its own axis runs from the high end
                    float fu = uNeg ? 1f - a : a, fv = vNeg ? 1f - b : b;
                    float u = Mathf.Lerp(uv.x, uv.z, fu), v = Mathf.Lerp(uv.y, uv.w, fv);
                    if (McRotation.HasValue) p = McRotOrigin + McRotation.Value * (p - McRotOrigin);
                    verts.Add((new Vector3(p.x, p.y, 16f - p.z) - new Vector3(8, 8, 8)) / 16f);
                    norms.Add(nMod);
                    uvs.Add(new Vector2(t.x + u / 16f * t.width, t.y + (1f - v / 16f) * t.height));
                }
                Vector3 c0 = verts[baseIdx], c1 = verts[baseIdx + 1], c2 = verts[baseIdx + 2];
                var tl = T;
                if (Vector3.Dot(Vector3.Cross(c1 - c0, c2 - c0), nMod) >= 0) { tl.Add(baseIdx); tl.Add(baseIdx + 1); tl.Add(baseIdx + 2); tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 3); }
                else { tl.Add(baseIdx); tl.Add(baseIdx + 2); tl.Add(baseIdx + 1); tl.Add(baseIdx); tl.Add(baseIdx + 3); tl.Add(baseIdx + 2); }
            }
        }

        /// <summary>
        /// (#223) A Minecraft block model, read from its JSON in the player's Minecraft (models/block/&lt;name&gt;.json, its
        /// parents followed for their elements): every element, its rotation and faces, all in the one tile given (the
        /// signs' and the chain's models use one texture). False: no such model.
        /// </summary>
        public bool McModel(string name, string tile)
        {
            var json = McAssets.ReadJar("assets/minecraft/models/block/" + name + ".json");
            if (json == null) return false;
            var model = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(json));
            for (int depth = 0; model["elements"] == null && model["parent"] != null && depth < 5; depth++)
            {
                string parent = ((string)model["parent"]).Replace("minecraft:", "").Replace("block/", "");
                var pj = McAssets.ReadJar("assets/minecraft/models/block/" + parent + ".json");
                if (pj == null) return false;
                model = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(pj));
            }
            if (!(model["elements"] is Newtonsoft.Json.Linq.JArray els)) return false;
            Vector3 V(Newtonsoft.Json.Linq.JToken a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);
            foreach (var e in els)
            {
                var from = V(e["from"]); var to = V(e["to"]);
                McRotation = null;
                if (e["rotation"] is Newtonsoft.Json.Linq.JObject r)
                {
                    McRotOrigin = V(r["origin"]);
                    if (r["axis"] != null)
                    {
                        string ax = (string)r["axis"]; float ang = (float)r["angle"];
                        McRotation = Quaternion.AngleAxis(ang, ax == "x" ? Vector3.right : ax == "y" ? Vector3.up : Vector3.forward);
                    }
                    else McRotation = Quaternion.Euler((float?)r["x"] ?? 0f, (float?)r["y"] ?? 0f, (float?)r["z"] ?? 0f);
                }
                var faces = new List<(Face, string, Vector4)>();
                foreach (var f in (Newtonsoft.Json.Linq.JObject)e["faces"])
                {
                    Face face = f.Key == "down" ? Face.Down : f.Key == "up" ? Face.Up : f.Key == "north" ? Face.North : f.Key == "south" ? Face.South : f.Key == "west" ? Face.West : Face.East;
                    Vector4 uv;
                    if (f.Value["uv"] is Newtonsoft.Json.Linq.JArray u) uv = new Vector4((float)u[0], (float)u[1], (float)u[2], (float)u[3]);
                    else switch (face) // (Minecraft's default: the face's own rectangle of the element)
                        {
                            case Face.Down: uv = new Vector4(from.x, 16 - to.z, to.x, 16 - from.z); break;
                            case Face.Up: uv = new Vector4(from.x, from.z, to.x, to.z); break;
                            case Face.North: uv = new Vector4(16 - to.x, 16 - to.y, 16 - from.x, 16 - from.y); break;
                            case Face.South: uv = new Vector4(from.x, 16 - to.y, to.x, 16 - from.y); break;
                            case Face.West: uv = new Vector4(from.z, 16 - to.y, to.z, 16 - from.y); break;
                            default: uv = new Vector4(16 - to.z, 16 - to.y, 16 - from.z, 16 - from.y); break;
                        }
                    faces.Add((face, tile, uv));
                }
                McElement(from, to, faces.ToArray());
            }
            McRotation = null;
            return true;
        }

        static (Face, string, Vector4) F(Face f, string tile, float u1, float v1, float u2, float v2) => (f, tile, new Vector4(u1, v1, u2, v2));

        /// <summary>
        /// A box of a Minecraft entity model (ModelPart.Cube: its corners, faces and texture rectangles from texOffs (u, v)
        /// on a tw x th texture), `grow` pixels bigger all round, put into Minecraft's block space by `m` (model pixels to
        /// blocks) and from there into ours (z turned over; mesh space is the block's middle). For icons drawn from a
        /// mob's own texture (#197); the uvs are on that texture, not the atlas.
        /// </summary>
        public void McCube(Matrix4x4 m, int u, int v, Vector3 from, Vector3 size, float grow, int tw, int th)
        {
            float w = size.x, h = size.y, d = size.z;
            float x0 = from.x - grow, y0 = from.y - grow, z0 = from.z - grow, x1 = from.x + w + grow, y1 = from.y + h + grow, z1 = from.z + d + grow;
            Vector3 v7 = new Vector3(x0, y0, z0), v0 = new Vector3(x1, y0, z0), v1 = new Vector3(x1, y1, z0), v2 = new Vector3(x0, y1, z0),
                    v3 = new Vector3(x0, y0, z1), v4 = new Vector3(x1, y0, z1), v5 = new Vector3(x1, y1, z1), v6 = new Vector3(x0, y1, z1);
            float f6 = u, f7 = u + d, f8 = u + d + w, f9 = u + d + w + w, f10 = u + d + w + d, f11 = u + d + w + d + w;
            float f12 = v, f13 = v + d, f14 = v + d + h;
            Vector3 Out(Vector3 p) { var b = m.MultiplyPoint3x4(p); return new Vector3(b.x - 0.5f, b.y - 0.5f, 0.5f - b.z); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, float u1, float vv1, float u2, float vv2, Vector3 nModel)
            {
                int i = verts.Count;
                var n = m.MultiplyVector(nModel).normalized; n = new Vector3(n.x, n.y, -n.z);
                foreach (var p in new[] { a, b, c, e }) { verts.Add(Out(p)); norms.Add(n); }
                uvs.Add(new Vector2(u2 / tw, 1f - vv1 / th)); uvs.Add(new Vector2(u1 / tw, 1f - vv1 / th));
                uvs.Add(new Vector2(u1 / tw, 1f - vv2 / th)); uvs.Add(new Vector2(u2 / tw, 1f - vv2 / th));
                var tl = T;
                Vector3 q0 = verts[i], q1 = verts[i + 1], q2 = verts[i + 2];
                if (Vector3.Dot(Vector3.Cross(q1 - q0, q2 - q0), n) >= 0) { tl.Add(i); tl.Add(i + 1); tl.Add(i + 2); tl.Add(i); tl.Add(i + 2); tl.Add(i + 3); }
                else { tl.Add(i); tl.Add(i + 2); tl.Add(i + 1); tl.Add(i); tl.Add(i + 3); tl.Add(i + 2); }
            }
            Quad(v4, v3, v7, v0, f7, f12, f8, f13, Vector3.down);    // (model y down: its "down" face is at the low y)
            Quad(v1, v2, v6, v5, f8, f13, f9, f12, Vector3.up);
            Quad(v7, v3, v6, v2, f6, f13, f7, f14, Vector3.left);
            Quad(v0, v7, v2, v1, f7, f13, f8, f14, Vector3.back);
            Quad(v4, v0, v1, v5, f8, f13, f10, f14, Vector3.right);
            Quad(v3, v4, v5, v6, f10, f13, f11, f14, Vector3.forward);
        }

        /// <summary>A Minecraft model's box with its default uvs (each face's texture where the box is on the block, as
        /// Minecraft takes it when the model gives none; a bit sticking out of the block wraps round): `top` on its top
        /// and bottom (`bottom` if given), `side` round it (#195).</summary>
        public void McBox(Vector3 a, Vector3 b, string top, string side, string bottom = null)
        {
            Vector4 W(float u1, float v1, float u2, float v2)
            {
                // (wrap a rectangle that's off the texture back onto it: -2..0 is 14..16)
                float du = u1 < 0 || u2 < 0 ? 16 : u1 > 16 || u2 > 16 ? -16 : 0, dv = v1 < 0 || v2 < 0 ? 16 : v1 > 16 || v2 > 16 ? -16 : 0;
                return new Vector4(u1 + du, v1 + dv, u2 + du, v2 + dv);
            }
            McElement(a, b,
                (Face.Down, bottom ?? top, W(a.x, 16 - b.z, b.x, 16 - a.z)), (Face.Up, top, W(a.x, a.z, b.x, b.z)),
                (Face.North, side, W(16 - b.x, 16 - b.y, 16 - a.x, 16 - a.y)), (Face.South, side, W(a.x, 16 - b.y, b.x, 16 - a.y)),
                (Face.West, side, W(a.z, 16 - b.y, b.z, 16 - a.y)), (Face.East, side, W(16 - b.z, 16 - b.y, 16 - a.z, 16 - a.y)));
        }

        /// <summary>
        /// Minecraft's bed (26.1's block model, template_bed_head / _foot, #195; ours had been a 9-high box with no legs):
        /// the mattress 3..9 high and two legs at the outer end, the head's at its north, the foot's at its south. The
        /// canonical bed is Minecraft's facing north (the head towards +z here); BlockWorld turns it.
        /// </summary>
        void McBed(bool head)
        {
            string P(string s) => "bed_" + (head ? "head_" : "foot_") + s;
            string up = P("up"), east = P("east"), west = P("west"), down = "bed_down";
            if (head)
            {
                string north = "bed_head_north";
                McElement(new Vector3(0, 3, 0), new Vector3(16, 9, 16), F(Face.Down, down, 0, 0, 16, 16), F(Face.Up, up, 0, 0, 16, 16),
                    F(Face.North, north, 0, 7, 16, 13), F(Face.West, west, 0, 7, 16, 13), F(Face.East, east, 0, 7, 16, 13));
                McElement(new Vector3(0, 0, 0), new Vector3(3, 3, 3), F(Face.Down, west, 6, 13, 9, 16), F(Face.North, north, 13, 13, 16, 16),
                    F(Face.South, west, 3, 13, 6, 16), F(Face.West, west, 0, 13, 3, 16), F(Face.East, north, 10, 13, 13, 16));
                McElement(new Vector3(13, 0, 0), new Vector3(16, 3, 3), F(Face.Down, east, 7, 13, 10, 16), F(Face.North, north, 0, 13, 3, 16),
                    F(Face.South, east, 10, 13, 13, 16), F(Face.West, north, 3, 13, 6, 16), F(Face.East, east, 13, 13, 16, 16));
            }
            else
            {
                string south = P("south");
                McElement(new Vector3(0, 3, 0), new Vector3(16, 9, 16), F(Face.Down, down, 0, 0, 16, 16), F(Face.Up, up, 0, 0, 16, 16),
                    F(Face.South, south, 0, 7, 16, 13), F(Face.West, west, 0, 7, 16, 13), F(Face.East, east, 0, 7, 16, 13));
                McElement(new Vector3(0, 0, 13), new Vector3(3, 3, 16), F(Face.Down, west, 7, 13, 10, 16), F(Face.North, west, 10, 13, 13, 16),
                    F(Face.South, south, 0, 13, 3, 16), F(Face.West, west, 13, 13, 16, 16), F(Face.East, south, 3, 13, 6, 16));
                McElement(new Vector3(13, 0, 13), new Vector3(16, 3, 16), F(Face.Down, east, 6, 13, 9, 16), F(Face.North, east, 3, 13, 6, 16),
                    F(Face.South, south, 13, 13, 16, 16), F(Face.West, south, 10, 13, 13, 16), F(Face.East, east, 0, 13, 3, 16));
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

        /// <summary>A two-sided quad showing columns u0..u1 (pixels) of a tile, all 16 rows: a to d along the bottom-left, b to c the right.</summary>
        public void PlaneU(Vector3 a, Vector3 b, Vector3 c, Vector3 d, string tile, float u0, float u1)
        {
            Rect t = Atlas.UV(tile);
            var n = Vector3.Cross(b - a, d - a).normalized;
            var corners = new[] { a, b, c, d };
            var uv = new[] { new Vector2(u0 / 16f, 0), new Vector2(u1 / 16f, 0), new Vector2(u1 / 16f, 1), new Vector2(u0 / 16f, 1) };
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

        /// <summary>A shaped block's faces: its top tile on top, bottom below, side tile around (down, up, n, s, w, e).</summary>
        public static FaceTex[] SlabFaces(BlockDef d) => new[] { new FaceTex(d.TileBottom ?? d.TileSide), new FaceTex(d.TileTop ?? d.TileSide), new FaceTex(d.TileSide), new FaceTex(d.TileSide), new FaceTex(d.TileSide), new FaceTex(d.TileSide) };

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
        static readonly Dictionary<long, Mesh> cache = new Dictionary<long, Mesh>();

        /// <summary>
        /// Observer faces for a facing (variant = facing + 1; 0 = the item's, facing south). The block is built with its face
        /// at +Y and turned to its facing, so each side's texture is oriented for where it ends up: upright on the sides
        /// the world sees edge-on, and on the world top/bottom with Minecraft's arrow (observer_top) pointing to the face.
        /// </summary>
        /// <summary>A block with a face of its own that can point any way: the dispenser, dropper and barrel (#195; their
        /// face had never been drawn: the face slot held their top texture).</summary>
        public static bool HasFront(BlockDef def) => def == Blocks.Dispenser || def == Blocks.Dropper || def == Blocks.Barrel;

        /// <summary>
        /// Faces for a block with a face, built with it at +Y and turned to its facing (variant = facing + 1; 0 = north,
        /// the item's). As Minecraft's models: a dispenser or dropper has its face (the vertical one when it faces up or
        /// down), furnace top above and below it and furnace sides at the sides, or furnace top all round when it faces
        /// up or down; a barrel its lid, its bottom behind and its sides round.
        /// </summary>
        static FaceTex[] FrontFaces(BlockDef def, int variant)
        {
            int f = variant >= 1 && variant <= 6 ? variant - 1 : (int)Face.North;
            var dir = (Vector3)Faces.Dir[f];
            bool vertical = Mathf.Abs(dir.y) > 0.5f;
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            var inv = Quaternion.Inverse(rot);
            var ahead = vertical ? Vector3.forward : dir;
            bool barrel = def == Blocks.Barrel;
            string front = def.TileFront, back, top = barrel ? "barrel_side" : def.TileTop, side = def.TileSide;
            if (barrel) back = "barrel_bottom";
            else
            {
                if (vertical && Atlas.Tiles.ContainsKey(front + "_vertical")) front += "_vertical";
                back = vertical ? def.TileTop : def.TileSide;
            }
            var faces = new FaceTex[6];
            for (int i = 0; i < 6; i++)
            {
                var nWorld = rot * FaceN[i];
                bool worldFlat = Mathf.Abs(nWorld.y) > 0.5f; // (ends up as a top or a bottom)
                string tile = i == (int)Face.Up ? front : i == (int)Face.Down ? back : (vertical || worldFlat) ? top : side;
                var up = inv * (worldFlat ? ahead : Vector3.up);
                up = new Vector3(Mathf.Round(up.x), Mathf.Round(up.y), Mathf.Round(up.z));
                faces[i] = new FaceTex(tile) { Up = up };
            }
            return faces;
        }

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

        /// <summary>
        /// A little redstone torch standing on a slab (comparators): 2x2 px at (x0, z0), from y0 up to y1 with the flame on top;
        /// the torch texture's middle column, wherever the box stands (UVs come from the position in the block).
        /// </summary>
        void Torchlet(float x0, float z0, float y0, float y1, bool lit)
        {
            string tile = lit ? "redstone_torch" : "redstone_torch_off";
            var c = new Vector3(x0 + 1, 8, z0 + 1) - new Vector3(8, 8, 8);
            FaceTex[] Faces(float vShift)
            {
                var f = new FaceTex[6];
                for (int i = 0; i < 6; i++)
                {
                    f[i] = new FaceTex(tile);
                    var n = FaceN[i]; var u = DefaultUp(i); var r = Vector3.Cross(u, -n);
                    // (shift the box's position back to the texture's middle column: 7-9)
                    f[i].UvOffset = new Vector2(-Vector3.Dot(c, r), i < 2 ? Vector3.Dot(c, u) + (i == 1 ? -1f : 0f) : vShift);
                }
                f[0].Skip = true;
                return f;
            }
            float stickTop = y1 - 2;
            // the stick: the texture's lower rows; the head: the two rows under the flame's top
            mb_Box(new Vector3(x0, y0, z0), new Vector3(x0 + 2, stickTop, z0 + 2), Faces(y0 - 0f), skipTop: true);
            EmitMode = lit;
            mb_Box(new Vector3(x0, stickTop, z0), new Vector3(x0 + 2, y1, z0 + 2), Faces(stickTop - 8f), skipTop: false);
            EmitMode = false;
        }

        void mb_Box(Vector3 min, Vector3 max, FaceTex[] faces, bool skipTop)
        {
            if (skipTop) faces[1].Skip = true;
            Box(min, max, faces);
        }

        static FaceTex[] Six(string d, string u, string n, string s, string w, string e)
            => new[] { new FaceTex(d), new FaceTex(u), new FaceTex(n), new FaceTex(s), new FaceTex(w), new FaceTex(e) };

        /// <summary>
        /// Mesh for a block in canonical orientation. Axis-oriented blocks (pistons, logs) are built facing +Y and
        /// rotated by BlockWorld; front-oriented (jack o'lantern) face +Z. variant: extra connection bits (dust).
        /// </summary>
        public static Mesh For(BlockDef def, byte state, int variant = 0)
        {
            // (block ids are 16 bits since 1.43: packed into 8, a block's state ran into the next block's id, and a lever
            // that's on or an extended piston was drawn as the copper block 256 ids on, #184, #185)
            long key = def.Id | ((long)state << 16) | ((long)(uint)variant << 24);
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
                    else if (def == Blocks.Slime)
                    {
                        // (#198: Minecraft's slime block, a see-through shell round a core 3..13, both of its texture)
                        mb.McElement(new Vector3(3, 3, 3), new Vector3(13, 13, 13), F(Face.Down, "slime", 3, 3, 13, 13), F(Face.Up, "slime", 3, 3, 13, 13),
                            F(Face.North, "slime", 3, 3, 13, 13), F(Face.South, "slime", 3, 3, 13, 13), F(Face.West, "slime", 3, 3, 13, 13), F(Face.East, "slime", 3, 3, 13, 13));
                        mb.McBox(full0, full1, "slime", "slime");
                    }
                    else if (def == Blocks.Honey)
                    {
                        // (#198: Minecraft's honey block, a see-through shell of its bottom texture round a core 1..15)
                        mb.McElement(new Vector3(1, 1, 1), new Vector3(15, 15, 15), F(Face.Down, def.TileBottom, 1, 1, 15, 15), F(Face.Up, def.TileTop, 1, 1, 15, 15),
                            F(Face.North, def.TileSide, 1, 1, 15, 15), F(Face.South, def.TileSide, 1, 1, 15, 15), F(Face.West, def.TileSide, 1, 1, 15, 15), F(Face.East, def.TileSide, 1, 1, 15, 15));
                        mb.McBox(full0, full1, def.TileBottom, def.TileBottom);
                    }
                    else if (def.Directional && def.FacingIncludesVertical && HasFront(def))
                        mb.Box(full0, full1, FrontFaces(def, variant));
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
                        if (Copper.IsBulb(def) && (state & 1) != 0) side = top = bottom = def.TileSide + "_lit"; // (#160)
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
                        if (variant == 0) mb.Box(new Vector3(5, 0, 6), new Vector3(11, depth, 10), def.TileSide); // (stone, planks...)
                        else mb.Box(new Vector3(5, 6, 0), new Vector3(11, 10, depth), def.TileSide);
                    }
                    break;

                case BlockShape.Plate:
                    {
                        bool pressed = (state & 15) != 0; // (its signal strength: weighted plates give less than 15)
                        mb.Box(new Vector3(1, 0, 1), new Vector3(15, pressed ? 0.5f : 1f, 15), def.TileSide);
                    }
                    break;

                case BlockShape.Lava:
                    {
                        // a pool fills 14/16 of its cell; flowing lava is lower the further it ran (3 levels), falling lava full (#74)
                        int level = state & 7; bool falling = (state & 8) != 0;
                        float h = falling ? 16f : level == 0 ? 14f : Mathf.Max(3f, 14f - level * 3.5f);
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, h, 16), "lava");
                    }
                    break;

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
                    }
                    break;

                case BlockShape.Door:
                    {
                        bool open = (state & 1) != 0, upper = (state & 2) != 0, right = (state & 4) != 0;
                        string tile = upper ? def.TileFront ?? "oak_door_top" : def.TileSide; // (an oak door, an iron one)
                        // (the knob goes on the side away from the hinge, like Minecraft's mirrored doors: #77)
                        var t = new FaceTex(tile) { Mirror = !right };
                        var side = new FaceTex(def.TileBottom ?? "oak_planks");
                        if (!open) mb.Box(new Vector3(0, 0, 13), new Vector3(16, 16, 16), new[] { side, side, t, t, side, side });
                        else if (!right) mb.Box(new Vector3(0, 0, 0), new Vector3(3, 16, 16), new[] { side, side, side, side, t, t });
                        else mb.Box(new Vector3(13, 0, 0), new Vector3(16, 16, 16), new[] { side, side, side, side, t, t });
                    }
                    break;

                case BlockShape.Ladder:
                    {
                        var t = new FaceTex(def.TileSide); // (a ladder, or vines)
                        var skip = new FaceTex(def.TileSide) { Skip = true };
                        mb.Box(new Vector3(0, 0, 0.2f), new Vector3(16, 16, 1f), new[] { skip, skip, t, t, skip, skip });
                    }
                    break;

                case BlockShape.Repeater:
                    {
                        // a 2px slab with the repeater's top, and two little torches: the output one near the front (+z), the
                        // delay one further back the longer the delay
                        bool on = (state & 1) != 0;
                        int delay = 1 + ((state >> 1) & 3);
                        var top = new FaceTex(on ? "repeater_on" : "repeater");
                        var side = new FaceTex("stone");
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 2, 16), new[] { side, top, side, side, side, side });
                        string tile = on ? "redstone_torch" : "redstone_torch_off";
                        // (Minecraft's: the delay torch 4 px behind the output one at 1 tick, 2 px further back each tick more, #162)
                        foreach (float z0 in new[] { 11f, 9f - 2f * delay })
                        {
                            var stick = Six(tile, tile, tile, tile, tile, tile);
                            stick[1].Skip = true;
                            for (int i = 2; i < 6; i++) stick[i].UvOffset = new Vector2(0, 3f);
                            stick[4].UvOffset.x = 7f - z0; stick[5].UvOffset.x = 7f - z0;
                            mb.Box(new Vector3(7, 2, z0), new Vector3(9, 6, z0 + 2), stick);
                            var head = Six(tile, tile, tile, tile, tile, tile);
                            // (the top is the torch head's 2x2 pixels, rows 6-7 of the texture, as Minecraft's torch model,
                            // wherever the torch stands: its rows run against z, 14 - z0 + offset; it had sampled the clear
                            // rows above the head at the front, and the stick's at the delay torch moved back, #162)
                            head[1].UvOffset = new Vector2(0, z0 - 8f);
                            for (int i = 2; i < 6; i++) head[i].UvOffset = new Vector2(0, 2f);
                            head[4].UvOffset.x = 7f - z0; head[5].UvOffset.x = 7f - z0;
                            mb.EmitMode = on;
                            mb.Box(new Vector3(7, 6, z0), new Vector3(9, 8, z0 + 2), head);
                            mb.EmitMode = false;
                        }
                    }
                    break;

                case BlockShape.Comparator:
                    {
                        // like a repeater: a 2px slab with Minecraft's comparator top, two torches at the back (lit while it
                        // gives a signal) and one at the front, lower, lit in subtract mode
                        bool on = (state & 15) != 0, subtract = (state & 16) != 0;
                        var top = new FaceTex(on ? "comparator_on" : "comparator");
                        var side = new FaceTex("stone");
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 2, 16), new[] { side, top, side, side, side, side });
                        mb.Torchlet(4, 2, 2, 7, on);
                        mb.Torchlet(10, 2, 2, 7, on);
                        mb.Torchlet(7, 11, 2, 5, subtract);
                    }
                    break;

                case BlockShape.Water:
                    {
                        // a source fills 14/16 of its cell; flowing water is lower the further it ran (falling water is full)
                        int level = state & 7; bool falling = (state & 8) != 0;
                        // (variant: faces against more water or a solid block, not drawn; water above: full height)
                        bool under = (variant & (1 << (int)Face.Up)) != 0;
                        float h = falling || under ? 16f : level == 0 ? 14f : Mathf.Max(2f, 14f - level * 1.75f);
                        var faces = new FaceTex[6];
                        for (int i = 0; i < 6; i++) faces[i] = new FaceTex("water") { Skip = (variant & (1 << i)) != 0 };
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, h, 16), faces);
                    }
                    break;

                case BlockShape.Bed:
                    {
                        if (Atlas.Tiles.ContainsKey("bed_head_up")) mb.McBed((state & 1) != 0);
                        else mb.Box(new Vector3(0, 0, 0), new Vector3(16, 9, 16), def.TileTop); // (no Minecraft: a plain box)
                    }
                    break;

                case BlockShape.Portal:
                    {
                        int frames = Atlas.PortalFrames;
                        string t = frames > 0 ? "portal_f" + (variant % frames) : "obsidian";
                        mb.EmitMode = true;
                        if ((state & 1) == 0) mb.Box(new Vector3(0, 0, 6), new Vector3(16, 16, 10), Six(t, t, t, t, t, t));
                        else mb.Box(new Vector3(6, 0, 0), new Vector3(10, 16, 16), Six(t, t, t, t, t, t));
                        mb.EmitMode = false;
                    }
                    break;

                case BlockShape.Farmland:
                    {
                        string top = (state & 1) != 0 ? "farmland_moist" : "farmland";
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 15, 16), Six(def.TileBottom, top, def.TileSide, def.TileSide, def.TileSide, def.TileSide));
                    }
                    break;

                case BlockShape.Crop:
                    {
                        // Minecraft's crop: four planes in a #, 4 px in from each side, a pixel down into the farmland
                        string t = Farming.CropTile(def, state);
                        foreach (float x in new[] { 4f, 12f })
                            mb.Plane(new Vector3(x, -1, 0), new Vector3(x, -1, 16), new Vector3(x, 15, 16), new Vector3(x, 15, 0), t);
                        foreach (float z in new[] { 4f, 12f })
                            mb.Plane(new Vector3(0, -1, z), new Vector3(16, -1, z), new Vector3(16, 15, z), new Vector3(0, 15, z), t);
                    }
                    break;

                case BlockShape.BrewingStand:
                    {
                        // Minecraft's: three little feet, the rod, and three holders reaching out from it (the texture's
                        // left half has a bottle in its holder, the right half an empty one)
                        mb.Box(new Vector3(9, 0, 5), new Vector3(15, 2, 11), "brewing_stand_base");
                        mb.Box(new Vector3(2, 0, 1), new Vector3(8, 2, 7), "brewing_stand_base");
                        mb.Box(new Vector3(2, 0, 9), new Vector3(8, 2, 15), "brewing_stand_base");
                        mb.Box(new Vector3(7, 0, 7), new Vector3(9, 14, 9), "brewing_stand");
                        for (int i = 0; i < 3; i++)
                        {
                            float ang = (i * 120f + 0f) * Mathf.Deg2Rad; // (towards each foot: east, then round)
                            var o = new Vector3(8 + Mathf.Cos(ang) * 8f, 0, 8 + Mathf.Sin(ang) * 8f);
                            var c0 = new Vector3(8, 0, 8);
                            bool full = (state & (1 << i)) != 0;
                            // u: 8 at the rod, out to 0 (bottle) or 16 (empty)
                            if (full) mb.PlaneU(o, c0, c0 + Vector3.up * 16, o + Vector3.up * 16, "brewing_stand", 0f, 8f);
                            else mb.PlaneU(c0, o, o + Vector3.up * 16, c0 + Vector3.up * 16, "brewing_stand", 8f, 16f);
                        }
                    }
                    break;

                case BlockShape.Cane:
                    mb.Plane(new Vector3(0, 0, 0), new Vector3(16, 0, 16), new Vector3(16, 16, 16), new Vector3(0, 16, 0), def.TileSide);
                    mb.Plane(new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(0, 16, 16), new Vector3(16, 16, 0), def.TileSide);
                    break;

                case BlockShape.Slab:
                    {
                        // (its block's top and bottom on top and bottom: a sandstone slab's top is sandstone's top)
                        var t = SlabFaces(def);
                        if ((state & 2) != 0) mb.Box(new Vector3(0, 0, 0), new Vector3(16, 16, 16), t);
                        else if ((state & 1) != 0) mb.Box(new Vector3(0, 8, 0), new Vector3(16, 16, 16), t);
                        else mb.Box(new Vector3(0, 0, 0), new Vector3(16, 8, 16), t);
                    }
                    break;

                case BlockShape.Hook:
                    {
                        // (on the wall behind, -z before the facing turns it: a plank plate and the hook sticking out)
                        bool hooked = (state & 1) != 0;
                        mb.Box(new Vector3(6, 1, 0), new Vector3(10, 15, 2), "oak_planks");
                        mb.Box(new Vector3(7.5f, hooked ? 7 : 9, 2), new Vector3(8.5f, hooked ? 8 : 10, hooked ? 7 : 6), "iron_block");
                    }
                    break;

                case BlockShape.Tripwire:
                    {
                        int c = variant & 15;
                        var t = new FaceTex("tripwire");
                        var faces = new[] { t, t, t, t, t, t };
                        if ((c & 1) != 0) mb.Box(new Vector3(7.75f, 1, 8), new Vector3(8.25f, 1.5f, 16), faces);
                        if ((c & 2) != 0) mb.Box(new Vector3(7.75f, 1, 0), new Vector3(8.25f, 1.5f, 8), faces);
                        if ((c & 4) != 0) mb.Box(new Vector3(0, 1, 7.75f), new Vector3(8, 1.5f, 8.25f), faces);
                        if ((c & 8) != 0) mb.Box(new Vector3(8, 1, 7.75f), new Vector3(16, 1.5f, 8.25f), faces);
                    }
                    break;

                case BlockShape.Lectern:
                    {
                        // Minecraft's: a base, a post, a top slanted toward you (+z before the facing turns it)
                        var b0 = new FaceTex(def.TileBottom); var sd = new FaceTex(def.TileSide); var tp = new FaceTex(def.TileTop); var fr = new FaceTex(def.TileFront);
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 2, 16), new[] { b0, b0, b0, b0, b0, b0 });
                        mb.Box(new Vector3(4, 2, 4), new Vector3(12, 15, 12), new[] { sd, sd, fr, sd, sd, sd });
                        mb.Box(new Vector3(0, 12, 3), new Vector3(16, 16, 16), new[] { sd, tp, sd, sd, sd, sd }, Quaternion.Euler(-22.5f, 0f, 0f), new Vector3(8, 14, 8));
                    }
                    break;

                case BlockShape.Shelf:
                    {
                        // its sides and top, and a face of six slots (+z before the facing turns it), each a book or empty
                        var sd = new FaceTex(def.TileSide); var tp = new FaceTex(def.TileTop); var skip = new FaceTex(def.TileSide) { Skip = true };
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 16, 16), new[] { tp, tp, skip, sd, sd, sd });
                        for (int i = 0; i < 6; i++)
                        {
                            int col = i % 3, row = i / 3;
                            float x0 = col * 16f / 3f, x1 = (col + 1) * 16f / 3f, y0 = row == 0 ? 8f : 0f, y1 = row == 0 ? 16f : 8f;
                            var t = new FaceTex((state & (1 << i)) != 0 ? "chiseled_bookshelf_occupied" : "chiseled_bookshelf_empty");
                            var no = new FaceTex(t.Tile) { Skip = true };
                            // (slot 0 is top left as you face it: +z faces you, so x runs right to left)
                            mb.Box(new Vector3(16f - x1, y0, 15.99f), new Vector3(16f - x0, y1, 16f), new[] { no, no, t, no, no, no });
                        }
                    }
                    break;

                case BlockShape.Rail:
                    {
                        // a flat plate 1 px up (or tilted up a slope), its texture turned along the track (Rails)
                        int shape = state & 15; bool on = (state & 16) != 0;
                        string tile = def.Key == "rail" ? (shape >= Rails.SE ? "rail_corner" : "rail") : def.Key + (on ? "_on" : "");
                        bool slope = shape >= Rails.AscE && shape <= Rails.AscS;
                        // (corners: Minecraft's corner texture turns from south to east; the others along z)
                        float rotY = shape == Rails.EW || shape == Rails.AscE || shape == Rails.AscW ? 90f : 0f;
                        if (shape == Rails.SE) rotY = 0f; else if (shape == Rails.SW) rotY = 90f; else if (shape == Rails.NW) rotY = 180f; else if (shape == Rails.NE) rotY = 270f;
                        float tilt = shape == Rails.AscN ? -45f : shape == Rails.AscS ? 45f : shape == Rails.AscE ? -45f : shape == Rails.AscW ? 45f : 0f; // (east and west: tilted along z, then turned to x)
                        var t = new FaceTex(tile); var skip = new FaceTex(tile) { Skip = true };
                        var rot = Quaternion.Euler(0f, rotY, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                        if (!slope) mb.Box(new Vector3(0, 1, 0), new Vector3(16, 1.01f, 16), new[] { t, t, skip, skip, skip, skip }, rot, new Vector3(8, 1, 8));
                        else mb.Box(new Vector3(0, 8, -3.3f), new Vector3(16, 8.01f, 19.3f), new[] { t, t, skip, skip, skip, skip }, rot, new Vector3(8, 8, 8));
                    }
                    break;

                case BlockShape.Banner:
                    break; // (#223: BannerBlock draws it)
                case BlockShape.WoodShelf:
                    // (#223) Minecraft's shelf: its body, and its unpowered rim (powered, the plain one)
                    if (!mb.McModel("template_shelf_body", def.TileSide)) { mb.Box(new Vector3(0, 0, 0), new Vector3(16, 16, 3), SlabFaces(def)); break; }
                    mb.McModel((state & WoodShelves.Powered) != 0 ? "template_shelf_unconnected" : "template_shelf_unpowered", def.TileSide);
                    break;
                case BlockShape.Sign:
                case BlockShape.HangingSign:
                case BlockShape.Chain:
                    {
                        // (#223) Minecraft's own models: state's bit 6 (64) the wall kind; a hanging sign's 16 straight chains
                        string model = def.Shape == BlockShape.Chain ? "template_chain"
                            : def.Shape == BlockShape.Sign ? ((variant & 1) != 0 ? "template_wall_sign" : "template_sign_rot_0")
                            : (variant & 1) != 0 ? "template_wall_hanging_sign" : (state & Signs.Attached) != 0 ? "template_attached_hanging_sign_rot_0" : "template_hanging_sign_rot_0";
                        if (!mb.McModel(model, def.TileSide)) mb.Box(new Vector3(2, 0, 7), new Vector3(14, 12, 9), SlabFaces(def));
                    }
                    break;
                case BlockShape.Plant:
                    {
                        // (#225) a flower or grass: Minecraft's cross model (tall grass: its top half's own picture)
                        string t = (state & 1) != 0 ? def.TileTop : def.TileSide;
                        mb.Plane(new Vector3(0, 0, 0), new Vector3(16, 0, 16), new Vector3(16, 16, 16), new Vector3(0, 16, 0), t);
                        mb.Plane(new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(0, 16, 16), new Vector3(16, 16, 0), t);
                    }
                    break;
                case BlockShape.Cluster:
                    {
                        // (#229) an amethyst bud or cluster: Minecraft's cross model, up out of the face it's on (the facing turns it)
                        mb.Plane(new Vector3(0, 0, 0), new Vector3(16, 0, 16), new Vector3(16, 16, 16), new Vector3(0, 16, 0), def.TileSide);
                        mb.Plane(new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(0, 16, 16), new Vector3(16, 16, 0), def.TileSide);
                    }
                    break;
                case BlockShape.Sapling:
                    {
                        // (#223) two crossed planes, Minecraft's cross model; a propagule hanging under leaves its own picture
                        string t = (state & Saplings.Hanging) != 0 ? "mangrove_propagule_hanging" : def.TileSide;
                        mb.Plane(new Vector3(0, 0, 0), new Vector3(16, 0, 16), new Vector3(16, 16, 16), new Vector3(0, 16, 0), t);
                        mb.Plane(new Vector3(16, 0, 0), new Vector3(0, 0, 16), new Vector3(0, 16, 16), new Vector3(16, 16, 0), t);
                    }
                    break;
                case BlockShape.Pot:
                    {
                        // (Minecraft's DecoratedPotRenderer: a 14 px body 16 high, a 6 px lip on it and an 8 px neck over that,
                        // to 20 px: the neck's tile holds its sides from row 1, shifted down 16 px to where they are)
                        var side = new FaceTex("decorated_pot_side"); var top = new FaceTex("decorated_pot_top");
                        var neck = new FaceTex("decorated_pot_neck") { UvOffset = new Vector2(0f, 16f) };
                        var neckTop = new FaceTex("decorated_pot_neck_top");
                        mb.Box(new Vector3(1, 0, 1), new Vector3(15, 16, 15), new[] { top, top, side, side, side, side });
                        mb.Box(new Vector3(5, 16, 5), new Vector3(11, 17, 11), new[] { neckTop, neckTop, neck, neck, neck, neck });
                        mb.Box(new Vector3(4, 17, 4), new Vector3(12, 20, 12), new[] { neckTop, neckTop, neck, neck, neck, neck });
                    }
                    break;

                case BlockShape.Basin:
                    {
                        // an open box: 2 px walls, a floor, and what's in it up to its level (Composting)
                        var side = new FaceTex(def.TileSide); var top = new FaceTex(def.TileTop); var bot = new FaceTex(def.TileBottom);
                        bool cauldron = def == Blocks.Cauldron;
                        var inner = new FaceTex(cauldron ? "cauldron_inner" : def.TileSide);
                        var wall = new[] { bot, top, side, side, side, side };
                        mb.Box(new Vector3(0, 0, 0), new Vector3(16, 16, 2), wall);
                        mb.Box(new Vector3(0, 0, 14), new Vector3(16, 16, 16), wall);
                        mb.Box(new Vector3(0, 0, 2), new Vector3(2, 16, 14), wall);
                        mb.Box(new Vector3(14, 0, 2), new Vector3(16, 16, 14), wall);
                        float floor = cauldron ? 4f : 2f;
                        mb.Box(new Vector3(2, 0, 2), new Vector3(14, floor, 14), new[] { bot, inner, inner, inner, inner, inner });
                        float fill = 0f; string tile = null;
                        if (cauldron) { if (state == 4) { fill = 15f; tile = "lava"; } else if ((state & 3) > 0) { fill = 6f + 3f * (state & 3); tile = "water"; } }
                        else if (state > 0) { fill = state >= 8 ? 15f : 2f + 1.7f * state; tile = state >= 8 ? "composter_ready" : "composter_compost"; }
                        if (tile != null && fill > floor) mb.Box(new Vector3(2, floor, 2), new Vector3(14, fill, 14), new[] { inner, new FaceTex(tile), inner, inner, inner, inner });
                    }
                    break;

                case BlockShape.Rod:
                    {
                        // Minecraft's: a 2 px rod and a 4 px tip, standing up (+y before the facing turns it). Its texture
                        // keeps them at the left edge (the rod's u 0-2 under the tip, the tip's 0-4 at the top), not where
                        // they stand in the block: taken from the middle, it was blank (#183, invisible in the world too)
                        var rod = Six(def.TileSide, def.TileSide, def.TileSide, def.TileSide, def.TileSide, def.TileSide);
                        for (int i = 0; i < 6; i++) rod[i].UvOffset = new Vector2(-7f, 0f);
                        mb.Box(new Vector3(7, 0, 7), new Vector3(9, 12, 9), rod);
                        var tip = Six(def.TileSide, def.TileSide, def.TileSide, def.TileSide, def.TileSide, def.TileSide);
                        for (int i = 0; i < 6; i++) tip[i].UvOffset = new Vector2(-6f, 0f);
                        tip[0].UvOffset = tip[1].UvOffset = new Vector2(-6f, -6f);
                        mb.Box(new Vector3(6, 12, 6), new Vector3(10, 16, 10), tip);
                    }
                    break;

                case BlockShape.Hopper:
                    {
                        // Minecraft's: a bowl on top, a narrower middle, the spout (down, or out of a side: +z before the
                        // facing turns it; variant 1)
                        var o = new FaceTex(def.TileSide); var top = new FaceTex(def.TileTop); var inside = new FaceTex(def.TileBottom);
                        // (the top: the bowl's inside; the rim texture's middle is see-through, which an opaque block can't show)
                        mb.Box(new Vector3(0, 10, 0), new Vector3(16, 16, 16), new[] { o, inside, o, o, o, o });
                        mb.Box(new Vector3(4, 4, 4), new Vector3(12, 10, 12), def.TileSide);
                        if (variant == 1) mb.Box(new Vector3(6, 4, 12), new Vector3(10, 8, 16), def.TileSide);
                        else mb.Box(new Vector3(6, 0, 6), new Vector3(10, 4, 10), def.TileSide);
                    }
                    break;

                case BlockShape.Gate:
                    {
                        // Minecraft's gate: a post each end and two rails between (across x, before the facing turns it);
                        // open, each half swings back against its post
                        var t = SlabFaces(def);
                        bool open = (state & 1) != 0;
                        mb.Box(new Vector3(0, 5, 7), new Vector3(2, 16, 9), t);
                        mb.Box(new Vector3(14, 5, 7), new Vector3(16, 16, 9), t);
                        if (!open)
                        {
                            foreach (var (y0, y1) in new[] { (6f, 9f), (12f, 15f) }) mb.Box(new Vector3(2, y0, 7), new Vector3(14, y1, 9), t);
                            mb.Box(new Vector3(6, 9, 7), new Vector3(8, 12, 9), t); mb.Box(new Vector3(8, 9, 7), new Vector3(10, 12, 9), t);
                        }
                        else
                            foreach (var x in new[] { 0f, 14f })
                            {
                                foreach (var (y0, y1) in new[] { (6f, 9f), (12f, 15f) }) mb.Box(new Vector3(x, y0, 9), new Vector3(x + 2, y1, 15), t);
                                mb.Box(new Vector3(x, 9, 13), new Vector3(x + 2, 12, 15), t);
                            }
                    }
                    break;

                case BlockShape.Detector:
                    mb.Box(new Vector3(0, 0, 0), new Vector3(16, 6, 16), SlabFaces(def)); // (#160: Minecraft's 6 px)
                    break;

                case BlockShape.Table:
                    // (#196: Minecraft's enchanting table, 12 px high: its sides the bottom 12 rows of their texture)
                    mb.Box(new Vector3(0, 0, 0), new Vector3(16, 12, 16), SlabFaces(def));
                    break;

                case BlockShape.Wall:
                    {
                        // Minecraft's wall: an 8 px post (unless it's a plain straight run), arms 6 px thick and 14 high
                        var t = SlabFaces(def);
                        int c = variant & 15;
                        if ((variant & 16) != 0) mb.Box(new Vector3(4, 0, 4), new Vector3(12, 16, 12), t);
                        if ((c & 1) != 0) mb.Box(new Vector3(5, 0, 8), new Vector3(11, 14, 16), t);
                        if ((c & 2) != 0) mb.Box(new Vector3(5, 0, 0), new Vector3(11, 14, 8), t);
                        if ((c & 4) != 0) mb.Box(new Vector3(0, 0, 5), new Vector3(8, 14, 11), t);
                        if ((c & 8) != 0) mb.Box(new Vector3(8, 0, 5), new Vector3(16, 14, 11), t);
                    }
                    break;

                case BlockShape.Fence:
                    {
                        // Minecraft's fence: a 4 px post and two 2x3 px rails to each neighbour
                        var t = SlabFaces(def);
                        int c = variant & 15;
                        mb.Box(new Vector3(6, 0, 6), new Vector3(10, 16, 10), t);
                        foreach (var (y0, y1) in new[] { (6f, 9f), (12f, 15f) })
                        {
                            if ((c & 1) != 0) mb.Box(new Vector3(7, y0, 10), new Vector3(9, y1, 16), t);
                            if ((c & 2) != 0) mb.Box(new Vector3(7, y0, 0), new Vector3(9, y1, 6), t);
                            if ((c & 4) != 0) mb.Box(new Vector3(0, y0, 7), new Vector3(6, y1, 9), t);
                            if ((c & 8) != 0) mb.Box(new Vector3(10, y0, 7), new Vector3(16, y1, 9), t);
                        }
                    }
                    break;

                case BlockShape.Trapdoor:
                    {
                        // closed: a plate at the bottom (or top) of the cell; open: stood up against the hinge side, toward
                        // where it was placed from (+z before the facing turns it)
                        var t = new FaceTex(def.TileTop);
                        if ((state & 1) != 0) mb.Box(new Vector3(0, 0, 13), new Vector3(16, 16, 16), new[] { t, t, t, t, t, t });
                        else if ((state & 2) != 0) mb.Box(new Vector3(0, 13, 0), new Vector3(16, 16, 16), new[] { t, t, t, t, t, t });
                        else mb.Box(new Vector3(0, 0, 0), new Vector3(16, 3, 16), new[] { t, t, t, t, t, t });
                    }
                    break;

                case BlockShape.Stairs:
                    {
                        var t = SlabFaces(def);
                        if ((state & Blocks.StairsUpsideDown) != 0)
                        {
                            // (#223, Minecraft's half=top: the full half on top, the step under its high side)
                            mb.Box(new Vector3(0, 8, 0), new Vector3(16, 16, 16), t);
                            mb.Box(new Vector3(0, 0, 0), new Vector3(16, 8, 8), t);
                        }
                        else
                        {
                            mb.Box(new Vector3(0, 0, 0), new Vector3(16, 8, 16), t);
                            mb.Box(new Vector3(0, 8, 0), new Vector3(16, 16, 8), t);
                        }
                    }
                    break;

                case BlockShape.Dust:
                    {
                        bool on = state > 0;
                        string line = on ? "dust_line_on" : "dust_line_off";
                        string dot = on ? "dust_dot_on" : "dust_dot_off";
                        if (!Atlas.Tiles.ContainsKey(line)) { line = on ? "dust_on" : "dust_off"; dot = line; }
                        // variant bits: 1=N(+Z) 2=S(-Z) 4=W(-X) 8=E(+X); 16/32/64/128: the line climbs that side's block (#103)
                        int c = variant & 15;
                        int up = (variant >> 4) & 15;
                        if (up != 0)
                        {
                            // a thin wall just inside this cell on that side, facing back in, the line running up it
                            void Wall(Vector3 a, Vector3 b, int face)
                            {
                                var f = new FaceTex[6];
                                for (int i = 0; i < 6; i++) f[i].Skip = true;
                                f[face] = new FaceTex(line) { Up = Vector3.up };
                                mb.Box(a, b, f);
                            }
                            if ((up & 1) != 0) Wall(new Vector3(0, 0, 15.7f), new Vector3(16, 16, 15.75f), (int)Face.South);
                            if ((up & 2) != 0) Wall(new Vector3(0, 0, 0.25f), new Vector3(16, 16, 0.3f), (int)Face.North);
                            if ((up & 4) != 0) Wall(new Vector3(0.25f, 0, 0), new Vector3(0.3f, 16, 16), (int)Face.East);
                            if ((up & 8) != 0) Wall(new Vector3(15.7f, 0, 0), new Vector3(15.75f, 16, 16), (int)Face.West);
                        }
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
            var m = ExtrudedPixels(Atlas.TilePixels(tile), Atlas.UV(tile), "LMC_sprite_" + tile);
            spriteCache[tile] = m;
            return m;
        }

        /// <summary>The same, from 16x16 pixels (bottom-up rows) drawn from `r` of whatever texture they're in (a
        /// patterned banner's own, #225). Not cached.</summary>
        public static Mesh ExtrudedPixels(Color32[] px, Rect r, string name)
        {
            var mb = new MeshBuilder();
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
            return mb.ToMesh(name);
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
