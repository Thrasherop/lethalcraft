using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The book floating over an enchanting table (#196), as Minecraft's EnchantTableRenderer draws it: its BookModel
    /// (two lids, a seam, two blocks of pages and two flipping pages, from its own texture) bobbing over the table, turning
    /// to face the nearest player within 3 blocks and opening for them, its pages flipping now and then; with nobody near
    /// it closes and slowly turns. Every client draws its own (it's only for looking at).
    /// The animation is Minecraft's own (EnchantingTableBlockEntity.bookAnimationTick, 20 a second; BookModel.setupAnim),
    /// worked out in Minecraft's coordinates and turned into ours (z the other way: M' = C M C, C = diag(1, 1, -1)).
    /// </summary>
    public class EnchantingBook : MonoBehaviour
    {
        static Material mat;
        static Mesh[] meshes;
        // the parts: left lid, right lid, seam, left pages, right pages, flip page 1, flip page 2
        readonly Transform[] parts = new Transform[7];
        float time, rot, oRot, tRot, open, oOpen, flip, oFlip, flipT, flipA, tickLeft;
        static readonly System.Random Rng = new System.Random();

        /// <summary>A table's block was drawn: give it its book (once).</summary>
        public static void Attach(GameObject table)
        {
            if (table == null || table.GetComponent<EnchantingBook>() != null) return;
            table.AddComponent<EnchantingBook>();
        }

        void Start()
        {
            if (mat == null)
            {
                mat = EntityModels.Material("LMC_EnchantingBook", McAssets.LoadTexture("entity/enchantment/enchanting_table_book") != null ? "entity/enchantment/enchanting_table_book" : "entity/enchanting_table_book", new Color(0.45f, 0.3f, 0.2f), doubleSided: true);
                meshes = new[]
                {
                    Box(0, 0, -6f, -5f, -0.005f, 6f, 10f, 0.005f), Box(16, 0, 0f, -5f, -0.005f, 6f, 10f, 0.005f), Box(12, 0, -1f, -5f, 0f, 2f, 10f, 0.005f),
                    Box(0, 10, 0f, -4f, -0.99f, 5f, 8f, 1f), Box(12, 10, 0f, -4f, -0.01f, 5f, 8f, 1f), Box(24, 10, 0f, -4f, 0f, 5f, 8f, 0.005f), Box(24, 10, 0f, -4f, 0f, 5f, 8f, 0.005f),
                };
            }
            var root = new GameObject("LMC_EnchantingBook").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < parts.Length; i++)
            {
                var go = new GameObject("part" + i) { layer = gameObject.layer };
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; Atlas.NoDecals(mr);
                parts[i] = go.transform;
            }
            tRot = rot = oRot = (float)(Rng.NextDouble() * Mathf.PI * 2f);
            Pose(0f);
        }

        /// <summary>A box of BookModel (Minecraft's model pixels and its texture's layout, 64 x 32), in our coordinates.</summary>
        static Mesh Box(int u, int v, float x0, float y0, float z0, float w, float h, float d)
        {
            float x1 = x0 + w, y1 = y0 + h, z1 = z0 + d;
            // ModelPart.Cube's corners and faces, with its texture rectangles
            Vector3 v7 = new Vector3(x0, y0, z0), v0 = new Vector3(x1, y0, z0), v1 = new Vector3(x1, y1, z0), v2 = new Vector3(x0, y1, z0),
                    v3 = new Vector3(x0, y0, z1), v4 = new Vector3(x1, y0, z1), v5 = new Vector3(x1, y1, z1), v6 = new Vector3(x0, y1, z1);
            float f6 = u, f7 = u + d, f8 = u + d + w, f9 = u + d + w + w, f10 = u + d + w + d, f11 = u + d + w + d + w;
            float f12 = v, f13 = v + d, f14 = v + d + h;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, float u1, float vv1, float u2, float vv2)
            {
                int i = verts.Count;
                foreach (var p in new[] { a, b, c, e }) verts.Add(new Vector3(p.x, p.y, -p.z) / 16f);
                uvs.Add(new Vector2(u2 / 64f, 1f - vv1 / 32f)); uvs.Add(new Vector2(u1 / 64f, 1f - vv1 / 32f));
                uvs.Add(new Vector2(u1 / 64f, 1f - vv2 / 32f)); uvs.Add(new Vector2(u2 / 64f, 1f - vv2 / 32f));
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Quad(v4, v3, v7, v0, f7, f12, f8, f13);  // down
            Quad(v1, v2, v6, v5, f8, f13, f9, f12);  // up
            Quad(v7, v3, v6, v2, f6, f13, f7, f14);  // west
            Quad(v0, v7, v2, v1, f7, f13, f8, f14);  // north
            Quad(v4, v0, v1, v5, f8, f13, f10, f14); // east
            Quad(v3, v4, v5, v6, f10, f13, f11, f14); // south
            var m = new Mesh { name = "LMC_BookPart" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            if (parts[0] == null) return;
            tickLeft -= Time.deltaTime;
            int guard = 0;
            while (tickLeft <= 0f && guard++ < 4) { tickLeft += 0.05f; Tick(); }
            if (tickLeft <= 0f) tickLeft = 0.05f;
            Pose(1f - tickLeft / 0.05f);
        }

        /// <summary>EnchantingTableBlockEntity.bookAnimationTick.</summary>
        void Tick()
        {
            oOpen = open; oRot = rot;
            // the nearest player within 3 blocks of the table's middle (on its own grid: blocks, Minecraft's x and z)
            Vector3? near = null; float best = 3f;
            var sor = StartOfRound.Instance;
            if (sor != null)
                foreach (var p in sor.allPlayerScripts)
                {
                    if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                    var l = transform.InverseTransformPoint(p.transform.position);
                    float dist = l.magnitude;
                    if (dist <= best) { best = dist; near = l; }
                }
            if (near != null)
            {
                tRot = Mathf.Atan2(-near.Value.z, near.Value.x); // (Minecraft's z is ours turned over)
                open += 0.1f;
                if (open < 0.5f || Rng.Next(40) == 0)
                {
                    float f1 = flipT;
                    do flipT += Rng.Next(4) - Rng.Next(4); while (f1 == flipT);
                }
            }
            else { tRot += 0.02f; open -= 0.1f; }
            rot = Wrap(rot); tRot = Wrap(tRot);
            rot += Wrap(tRot - rot) * 0.4f;
            open = Mathf.Clamp01(open);
            time++;
            oFlip = flip;
            float f = Mathf.Clamp((flipT - flip) * 0.4f, -0.2f, 0.2f);
            flipA += (f - flipA) * 0.9f;
            flip += flipA;
        }

        static float Wrap(float a) { while (a >= Mathf.PI) a -= Mathf.PI * 2f; while (a < -Mathf.PI) a += Mathf.PI * 2f; return a; }

        /// <summary>EnchantTableRenderer.render and BookModel.setupAnim, `pt` of the way to the next tick.</summary>
        void Pose(float pt)
        {
            float t = time + pt;
            float r = oRot + Wrap(rot - oRot) * pt;
            float fl = Mathf.Lerp(oFlip, flip, pt);
            float f3 = Mathf.Clamp01(Frac(fl + 0.25f) * 1.6f - 0.3f), f4 = Mathf.Clamp01(Frac(fl + 0.75f) * 1.6f - 0.3f);
            float opening = Mathf.Lerp(oOpen, open, pt);
            // the table's corner to its middle, 0.75 up, bobbing; turned to face, tipped 80 degrees (block units; the
            // book's own pixels are 1/16)
            var bas = T(0f, 0.75f - 0.5f + 0.1f + Mathf.Sin(t * 0.1f) * 0.01f, 0f) * RY(-r) * RZ(80f * Mathf.Deg2Rad);
            float f = (Mathf.Sin(t * 0.02f) * 0.1f + 1.25f) * opening;
            float sx = Mathf.Sin(f) / 16f;
            Set(0, bas * T(0f, 0f, -1f / 16f) * RY(Mathf.PI + f));
            Set(1, bas * T(0f, 0f, 1f / 16f) * RY(-f));
            Set(2, bas * RY(Mathf.PI / 2f));
            Set(3, bas * T(sx, 0f, 0f) * RY(f));
            Set(4, bas * T(sx, 0f, 0f) * RY(-f));
            Set(5, bas * T(sx, 0f, 0f) * RY(f - f * 2f * f3));
            Set(6, bas * T(sx, 0f, 0f) * RY(f - f * 2f * f4));
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        void Set(int i, Matrix4x4 mc)
        {
            // into ours: z turned over on both sides
            var c = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            var m = c * mc * c;
            parts[i].localPosition = m.GetColumn(3);
            parts[i].localRotation = m.rotation;
        }

        static Matrix4x4 T(float x, float y, float z) => Matrix4x4.Translate(new Vector3(x, y, z));
        static Matrix4x4 RY(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity; m.m00 = c; m.m02 = s; m.m20 = -s; m.m22 = c; return m;
        }
        static Matrix4x4 RZ(float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var m = Matrix4x4.identity; m.m00 = c; m.m01 = -s; m.m10 = s; m.m11 = c; return m;
        }
    }
}
