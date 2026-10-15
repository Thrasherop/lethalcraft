using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>Minecraft's mob models: boxes cut from the mob's own texture (the creeper, the bee).</summary>
    public static class EntityModels
    {
        public const float Px = 1f / 16f; // a texture pixel, in blocks

        /// <summary>A lit material showing a Minecraft entity texture (a plain colour if the texture isn't found).</summary>
        /// <summary>A sword or an axe in a biped's right hand, as Minecraft draws it (#166): ItemInHandLayer's turn (-90
        /// about x, 180 about y, then 1, 2, -10 px), then the handheld model's third-person display (0, 4, 0.5 px; turned
        /// 0, -90, 55; 0.85 the size), from the arm's pivot (here a pixel higher than Minecraft's, the arm's box the same).
        /// With the arm down the blade points ahead, the grip in the hand. The sprite mesh is the item model's space / 16.
        /// (Worked through in Minecraft's space, y down and z back, and turned into ours: 180 about x.)</summary>
        public static GameObject HeldWeapon(Transform arm, float k, string tile, string name)
        {
            var go = new GameObject(name) { layer = 19 };
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -10.5f, 6f) * k;
            go.transform.localRotation = new Quaternion(0.67438f, 0.21263f, 0.67438f, 0.21263f);
            go.transform.localScale = Vector3.one * (0.85f * 16f * k);
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey(tile) ? tile : "item_pickaxe");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            Atlas.NoDecals(mr);
            return go;
        }

        public static Material Material(string name, string texture, Color fallback, bool doubleSided = false)
        {
            var tex = McAssets.LoadTexture(texture);
            if (tex == null) { tex = new Texture2D(1, 1); tex.SetPixel(0, 0, fallback); tex.Apply(); }
            tex.filterMode = FilterMode.Point;
            var m = Atlas.MakeLit(name, cutout: true, doubleSided: doubleSided, emissive: false);
            m.SetTexture("_BaseColorMap", tex);
            m.mainTexture = tex;
            m.SetFloat("_Smoothness", 0.15f);
            m.SetFloat("_AlbedoAffectEmissive", 1f);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetUseEmissiveIntensity(m, true);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveColor(m, Color.white);
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, ArmorModels.AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
            return m;
        }

        /// <summary>
        /// A box of a Minecraft model (sizes in texture pixels, its texture patch at u, v, unwrapped the way Minecraft does;
        /// its front faces +z). k: metres per texture pixel.
        /// </summary>
        public static GameObject Part(Transform parent, string name, Vector3 center, Vector3 size, int u, int v, float k, Material mat, int layer = 19, float texScale = 1f)
        {
            var tex = mat.mainTexture;
            // (texScale: texture pixels per model pixel, 2 for a texture drawn at twice the old resolution)
            float tw = (tex != null && tex.width > 1 ? tex.width : 64) / texScale, th = (tex != null && tex.height > 1 ? tex.height : 32) / texScale;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center * k;
            go.layer = layer;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float hx = size.x * k / 2f, hy = size.y * k / 2f, hz = size.z * k / 2f;
            int w = (int)size.x, h = (int)size.y, d = (int)size.z;
            void Face(Vector3 n, float hn, Vector3 xd, float ex, Vector3 yd, float ey, float px, float py, float pw, float ph)
            {
                if (pw <= 0f || ph <= 0f) return; // (a flat part: only its two big faces)
                var c = n * hn;
                int i = verts.Count;
                verts.Add(c - xd * ex - yd * ey); uvs.Add(new Vector2(px / tw, 1f - py / th));
                verts.Add(c + xd * ex - yd * ey); uvs.Add(new Vector2((px + pw) / tw, 1f - py / th));
                verts.Add(c - xd * ex + yd * ey); uvs.Add(new Vector2(px / tw, 1f - (py + ph) / th));
                verts.Add(c + xd * ex + yd * ey); uvs.Add(new Vector2((px + pw) / tw, 1f - (py + ph) / th));
                for (int q = 0; q < 4; q++) norms.Add(n);
                if (Vector3.Dot(Vector3.Cross(verts[i + 1] - verts[i], verts[i + 2] - verts[i]), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                else tris.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            }
            Vector3 up = Vector3.up, front = Vector3.forward, right = Vector3.right;
            Face(front, hz, -right, hx, -up, hy, u + d, v + d, w, h);               // front (the face)
            Face(-front, hz, right, hx, -up, hy, u + 2 * d + w, v + d, w, h);       // back
            Face(right, hx, -front, hz, -up, hy, u, v + d, d, h);                   // its right side
            Face(-right, hx, front, hz, -up, hy, u + d + w, v + d, d, h);           // its left side
            Face(up, hy, -right, hx, front, hz, u + d, v, w, d);                    // top
            Face(-up, hy, -right, hx, -front, hz, u + d + w, v, w, d);              // bottom
            var mesh = new Mesh { name = "LMC_" + name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            Atlas.NoDecals(mr);
            return go;
        }
    }
}
