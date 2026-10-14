using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Rendering;

namespace LethalMinecraft
{
    /// <summary>
    /// Worn armor on the players' models (#20), with Minecraft's look: boxes around the head, body, arms and legs wearing
    /// Minecraft's armor textures (entity/equipment/humanoid since 1.21.2, models/armor before), sized from the model's
    /// own bone colliders and moving with its bones. Every client draws everyone's from the armor state it already has.
    /// Your own only casts a shadow (you'd be looking out through your helmet); it shows in the [I] inventory's preview.
    /// </summary>
    public static class ArmorModels
    {
        public static bool Enabled = true;
        public static bool DevShowLocal;
        public static float AmbientEV = 2f; // (dev: tunable) // (dev: your own model and armor drawn, to photograph them)
        public const string PartName = "LMC_ArmorPart";

        /// <summary>A box on one bone: which part of Minecraft's armor texture it wears (u, v, w, h, d in texture pixels),
        /// the slice of that part from the top (0) to the bottom (1), the texture layer (2: leggings), how far it stands
        /// out, and whether it's a limb (its bone points away from the body: Minecraft's top is at the bone's start).</summary>
        struct Part
        {
            public string Bone; public int U, V, W, H, D; public float From, To; public int Layer; public float Grow, Wide, Deep; public bool Limb, Mirror;
        }

        static Part P(string bone, (int u, int v, int w, int h, int d) uv, float from, float to, int layer, float grow, bool limb = false, bool mirror = false) =>
            new Part { Bone = bone, U = uv.u, V = uv.v, W = uv.w, H = uv.h, D = uv.d, From = from, To = to, Layer = layer, Grow = grow, Limb = limb, Mirror = mirror,
                       // (the colliders are hit boxes: the suit's arms are thinner than theirs, its legs thicker, and deeper)
                       Wide = bone.StartsWith("arm") ? 0.85f : bone.StartsWith("thigh") || bone.StartsWith("shin") ? 1.15f : 1f,
                       Deep = bone.StartsWith("thigh") || bone.StartsWith("shin") ? 1.4f : 1f };

        static readonly (int, int, int, int, int) Head = (0, 0, 8, 8, 8), Body = (16, 16, 8, 12, 4), Arm = (40, 16, 4, 12, 4), Leg = (0, 16, 4, 12, 4);

        // helmet, chestplate, leggings, boots (boots outside the leggings, the chestplate over the leggings' waist)
        static readonly Part[][] Pieces =
        {
            new[] { P("spine.004", Head, 0f, 1f, 1, 0.07f) },
            new[]
            {
                P("spine.003", Body, 0f, 0.55f, 1, 0.06f), P("spine.002", Body, 0.55f, 1f, 1, 0.06f),
                P("arm.R_upper", Arm, 0f, 0.5f, 1, 0.05f, limb: true), P("arm.R_lower", Arm, 0.5f, 1f, 1, 0.05f, limb: true),
                P("arm.L_upper", Arm, 0f, 0.5f, 1, 0.05f, limb: true, mirror: true), P("arm.L_lower", Arm, 0.5f, 1f, 1, 0.05f, limb: true, mirror: true),
            },
            new[]
            {
                P("spine.002", Body, 0.55f, 1f, 2, 0.04f),
                P("thigh.R", Leg, 0f, 0.5f, 2, 0.02f, limb: true), P("shin.R", Leg, 0.5f, 1f, 2, 0.02f, limb: true),
                P("thigh.L", Leg, 0f, 0.5f, 2, 0.02f, limb: true, mirror: true), P("shin.L", Leg, 0.5f, 1f, 2, 0.02f, limb: true, mirror: true),
            },
            new[]
            {
                P("shin.R", Leg, 0.5f, 1f, 1, 0.04f, limb: true), P("shin.L", Leg, 0.5f, 1f, 1, 0.04f, limb: true, mirror: true),
            },
        };

        class Shown
        {
            public readonly string[] Keys = new string[Armor.Slots];
            public readonly List<GameObject>[] Parts = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
            public bool Local;
        }

        static readonly Dictionary<PlayerControllerB, Shown> shown = new Dictionary<PlayerControllerB, Shown>();
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static float next;

        /// <summary>Local client, every frame: what everyone wears, onto their models (checked a few times a second).</summary>
        public static void Tick()
        {
            if (Time.time < next) return;
            next = Time.time + 0.25f;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.allPlayerScripts == null) return;
            var local = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            bool localChanged = false;
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null) continue;
                bool on = Enabled && Plugin.ShowArmorOnPlayers.Value && McAssets.Available && (p.isPlayerControlled || p == local) && !p.isPlayerDead;
                var keys = on ? Armor.Of(p.actualClientId) : null;
                if (!shown.TryGetValue(p, out var s)) { if (keys == null || System.Array.TrueForAll(keys, k => k == null)) continue; shown[p] = s = new Shown(); }
                bool isLocal = p == local;
                for (int slot = 0; slot < Armor.Slots; slot++)
                {
                    string want = keys != null && slot < keys.Length ? keys[slot] : null;
                    // (a part gone missing: the model was rebuilt, e.g. a suit change)
                    bool lost = s.Parts[slot].Exists(g => g == null);
                    if (want == s.Keys[slot] && !lost && s.Local == isLocal) continue;
                    foreach (var g in s.Parts[slot]) if (g != null) Object.Destroy(g);
                    s.Parts[slot].Clear();
                    s.Keys[slot] = want;
                    if (want != null) Build(p, slot, want, isLocal, s.Parts[slot]);
                    if (isLocal) localChanged = true;
                }
                s.Local = isLocal;
            }
            if (localChanged) ArmorPreview.Rebuild();
        }

        static Transform FindBone(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                if (c.name == "ScavengerModelArmsOnly" || c.name == "CameraContainer") continue; // (the first-person arms have the same bone names)
                var f = FindBone(c, name);
                if (f != null) return f;
            }
            return null;
        }

        static void Build(PlayerControllerB p, int slot, string key, bool isLocal, List<GameObject> into)
        {
            var def = Armor.Get(key);
            var rig = p.transform.Find("ScavengerModel/metarig");
            if (def == null || rig == null) return;
            if (def.Key == Elytra.Key)
            {
                var chest = FindBone(rig, "spine.003");
                if (chest != null) ElytraWings.Build(p, chest, isLocal, into);
                return;
            }
            int layer = p.thisPlayerModel != null ? p.thisPlayerModel.gameObject.layer : p.gameObject.layer;
            foreach (var part in Pieces[slot])
            {
                var bone = FindBone(rig, part.Bone);
                var col = bone != null ? bone.GetComponent<BoxCollider>() : null;
                var mat = MaterialFor(def.Material, part.Layer);
                if (col == null || mat == null) continue;
                var go = new GameObject(PartName);
                go.layer = layer;
                go.transform.SetParent(bone, false);
                go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(p, bone, col, part, mat.mainTexture);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = isLocal && !DevShowLocal ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
                // lit like the suit (the same light layers), and no decals painted on it
                if (p.thisPlayerModel != null) r.renderingLayerMask = p.thisPlayerModel.renderingLayerMask;
                Atlas.NoDecals(r);
                into.Add(go);
            }
        }

        /// <summary>Minecraft's armor texture for a material ("golden", "iron", "diamond") and layer (2: leggings).</summary>
        static Material MaterialFor(string material, int layer)
        {
            string id = material + layer;
            if (mats.TryGetValue(id, out var m)) return m;
            string name = material == "golden" ? "gold" : material;
            var tex = McAssets.LoadTexture($"entity/equipment/{(layer == 2 ? "humanoid_leggings" : "humanoid")}/{name}")
                      ?? McAssets.LoadTexture($"models/armor/{name}_layer_{layer}");
            if (tex != null)
            {
                m = Atlas.MakeLit("LMC_Armor_" + id, cutout: true, doubleSided: true, emissive: false);
                m.SetTexture("_BaseColorMap", tex);
                m.mainTexture = tex;
                m.SetFloat("_Smoothness", material == "diamond" ? 0.35f : 0.25f);
                // (Minecraft's iron is nearly white: under the game's lights it glares)
                // (a faint glow of its own colour, like the blocks': in shade it would be pitch black, the suit isn't)
                m.SetFloat("_AlbedoAffectEmissive", 1f);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetUseEmissiveIntensity(m, true);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveColor(m, Color.white);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                m.SetColor("_BaseColor", material == "iron" ? new Color(0.62f, 0.62f, 0.64f) : material == "golden" ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.8f, 0.8f, 0.8f));
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
                m.SetFloat("_Metallic", material == "diamond" ? 0f : 0.2f);
            }
            mats[id] = m;
            return m;
        }

        /// <summary>
        /// Which of a bone's axes faces the model's front, in the model's rest (bind) pose: the live pose can have it bent
        /// any way (crouching, climbing). The bone points along its Y axis, so it's one of the other four.
        /// </summary>
        static Vector3 FrontOf(PlayerControllerB p, Transform bone)
        {
            Vector3 f;
            var smr = p.thisPlayerModel;
            int i = smr != null && smr.bones != null ? System.Array.IndexOf(smr.bones, bone) : -1;
            Matrix4x4[] bind = null;
            try { bind = i >= 0 && smr.sharedMesh != null ? smr.sharedMesh.bindposes : null; } catch { }
            if (bind != null && i < bind.Length)
            {
                var rest = bind[i].inverse; // the bone's rest frame, in the mesh's space
                var fwd = smr.transform.InverseTransformDirection(p.transform.forward);
                f = new Vector3(Vector3.Dot(rest.MultiplyVector(Vector3.right).normalized, fwd), 0f, Vector3.Dot(rest.MultiplyVector(Vector3.forward).normalized, fwd));
            }
            else f = bone.InverseTransformDirection(p.transform.forward); // (no bind pose: as it stands now)
            return Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(f.z));
        }

        /// <summary>
        /// A box around the bone's collider, in the bone's space, with each face showing its patch of Minecraft's armor
        /// texture.
        /// </summary>
        static Mesh BoxMesh(PlayerControllerB p, Transform bone, BoxCollider col, Part part, Texture tex)
        {
            Vector3 up = part.Limb ? Vector3.down : Vector3.up;
            Vector3 front = FrontOf(p, bone);
            Vector3 right = Vector3.Cross(up, front);
            var pt = p.transform;
            if (Plugin.DevMode.Value && DevAxes.Count < 64) DevAxes.Add($"{part.Bone}: up={pt.InverseTransformDirection(bone.TransformDirection(up)):F1} front={pt.InverseTransformDirection(bone.TransformDirection(front)):F1} right={pt.InverseTransformDirection(bone.TransformDirection(right)):F1}");
            Vector3 half = col.size * 0.5f;
            float Ext(Vector3 axis, float k) => Mathf.Abs(Vector3.Dot(axis, half)) * k + part.Grow;
            float hu = Ext(up, 1f), hf = Ext(front, part.Deep), hr = Ext(right, part.Wide);
            Vector3 c = col.center;
            float tw = tex != null ? tex.width : 64, th = tex != null ? tex.height : 32;

            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            // a face: centre, its normal, the directions of the texture's x (left to right) and y (top to bottom), half sizes,
            // and the texture rect (pixels from the top left)
            void Face(Vector3 n, float hn, Vector3 xd, float hx, Vector3 yd, float hy, float px, float py, float pw, float ph)
            {
                if (part.Mirror) { px += pw; pw = -pw; }
                var center = c + n * hn;
                int i = verts.Count;
                verts.Add(center - xd * hx - yd * hy); uvs.Add(new Vector2(px / tw, 1f - py / th));
                verts.Add(center + xd * hx - yd * hy); uvs.Add(new Vector2((px + pw) / tw, 1f - py / th));
                verts.Add(center - xd * hx + yd * hy); uvs.Add(new Vector2(px / tw, 1f - (py + ph) / th));
                verts.Add(center + xd * hx + yd * hy); uvs.Add(new Vector2((px + pw) / tw, 1f - (py + ph) / th));
                for (int k = 0; k < 4; k++) norms.Add(n);
                // (wound to face out: the far side of a double-sided face is shaded as if lit from inside, black)
                if (Vector3.Dot(Vector3.Cross(verts[i + 1] - verts[i], verts[i + 2] - verts[i]), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                else tris.AddRange(new[] { i, i + 2, i + 1, i + 2, i + 3, i + 1 });
            }
            int u = part.U, v = part.V, w = part.W, h = part.H, d = part.D;
            float top = v + d + part.From * h, sh = (part.To - part.From) * h;
            // (left and right as the model's own: on the texture its right side comes first)
            Vector3 R = part.Mirror ? -right : right;
            Face(front, hf, -R, hr, -up, hu, u + d, top, w, sh);              // front
            Face(-front, hf, R, hr, -up, hu, u + 2 * d + w, top, w, sh);      // back
            Face(R, hr, -front, hf, -up, hu, u, top, d, sh);                  // right side
            Face(-R, hr, front, hf, -up, hu, u + d + w, top, d, sh);          // left side
            if (part.From <= 0f) Face(up, hu, -R, hr, front, hf, u + d, v, w, d);              // top
            if (part.To >= 1f) Face(-up, hu, -R, hr, -front, hf, u + d + w, v, w, d);         // bottom

            var mesh = new Mesh { name = "LMC_ArmorBox" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Dev: draw your own model and armor (or not), rebuilt.</summary>
        public static void DevShow(bool on)
        {
            DevShowLocal = on;
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p != null && p.thisPlayerModel != null) p.thisPlayerModel.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var s in shown.Values) for (int i = 0; i < Armor.Slots; i++) { foreach (var g in s.Parts[i]) if (g != null) Object.Destroy(g); s.Parts[i].Clear(); s.Keys[i] = null; }
            next = 0f;
        }

        public static readonly List<string> DevAxes = new List<string>();

        /// <summary>Dev: the local model's bone colliders the pieces are sized from.</summary>
        public static string DevBones()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            var rig = p != null ? p.transform.Find("ScavengerModel/metarig") : null;
            if (rig == null) return "no rig";
            var sb = new System.Text.StringBuilder();
            foreach (var name in new[] { "spine.004", "spine.003", "spine.002", "arm.R_upper", "arm.R_lower", "thigh.R", "shin.R" })
            {
                var b = FindBone(rig, name); var c = b != null ? b.GetComponent<BoxCollider>() : null;
                if (c == null) { sb.Append(name).Append(" -; "); continue; }
                var f = b.InverseTransformDirection(p.transform.forward);
                var pt = p.transform;
                sb.Append($"{name} c={c.center:F2} s={c.size:F2} scale={b.lossyScale.x:F2} fwd={f:F2} X={pt.InverseTransformDirection(b.right):F1} Y={pt.InverseTransformDirection(b.up):F1} Z={pt.InverseTransformDirection(b.forward):F1}; ");
            }
            return sb.ToString();
        }

        public static string DevLayers()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null || p.thisPlayerModel == null) return "-";
            string mine = "";
            foreach (var r in p.GetComponentsInChildren<MeshRenderer>(true)) if (r.name == PartName) { mine = r.renderingLayerMask.ToString("X"); break; }
            return $"suit={p.thisPlayerModel.renderingLayerMask:X} armor={mine}";
        }

        /// <summary>Dev: what's drawn on whom.</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in shown)
            {
                if (kv.Key == null) continue;
                sb.Append(kv.Key.playerUsername).Append(": ");
                for (int s = 0; s < Armor.Slots; s++) sb.Append(kv.Value.Keys[s] ?? "-").Append('(').Append(kv.Value.Parts[s].Count).Append(") ");
                sb.Append("; ");
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }
    }
}
