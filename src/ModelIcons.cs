using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>
    /// (#178) Icons drawn from an item's own 3D model, for the game's items whose icon isn't theirs: the Mapper carries
    /// the Pro-flashlight's (the game's own data). A copy of its meshes (no scripts) stands far below the map in front of
    /// its own camera and light; drawn once on black and once on white, the difference gives the see-through parts.
    /// Drawn on first use, a few frames later; until then the game's icon shows.
    /// </summary>
    public class ModelIcons : MonoBehaviour
    {
        static ModelIcons runner;
        static readonly Dictionary<Item, Sprite> made = new Dictionary<Item, Sprite>();
        static readonly Queue<Item> todo = new Queue<Item>();
        static readonly HashSet<Item> asked = new HashSet<Item>();
        /// <summary>An icon is ready (the menus redraw).</summary>
        public static event System.Action Ready;
        static readonly Vector3 Spot = new Vector3(0f, -2800f, 0f);
        const int Size = 128;
        public static float LightCandela = 40f; // (dev: tunable)
        /// <summary>(dev) The model's turn (null: as it lies on the floor) and where the camera looks from.</summary>
        public static Vector3? DevEuler; public static Vector3 ViewFrom = new Vector3(-0.55f, 0.6f, -0.6f);

        /// <summary>(dev) Draw it again (after changing the angle).</summary>
        public static void DevRedo(Item it) { if (it == null) return; made.Remove(it); asked.Remove(it); IconOf(it); }

        /// <summary>Its own icon, if it needs one: the game gave it a store item's icon while it isn't sold itself, or the
        /// icon it has is one other items share (scrap's gear).</summary>
        public static bool Needs(Item it)
        {
            if (it == null || it.itemIcon == null || it.spawnPrefab == null) return false;
            var store = FindObjectOfType<Terminal>()?.buyableItemsList;
            if (store == null || store.Contains(it)) return false;
            if (store.Any(s => s != null && s != it && s.itemIcon == it.itemIcon)) return true;
            // (or one icon many share: every scrap's gear, which made the creative menu's last tab a wall of gears)
            var all = StartOfRound.Instance?.allItemsList?.itemsList;
            return all != null && all.Any(o => o != null && o != it && o.itemIcon == it.itemIcon);
        }

        /// <summary>The icon to show: its model's when it needs one (null while that's being drawn), else the game's.</summary>
        public static Sprite IconOf(Item it)
        {
            if (it == null) return null;
            if (made.TryGetValue(it, out var s)) return s;
            if (!Needs(it)) return it.itemIcon;
            if (asked.Add(it)) { todo.Enqueue(it); Run(); }
            return it.itemIcon;
        }

        static void Run()
        {
            if (runner == null) { runner = new GameObject("LMC_ModelIcons").AddComponent<ModelIcons>(); DontDestroyOnLoad(runner.gameObject); }
            if (!runner.busy) runner.StartCoroutine(runner.Work());
        }

        bool busy;
        IEnumerator Work()
        {
            busy = true;
            while (todo.Count > 0)
            {
                var it = todo.Dequeue();
                Sprite s = null;
                yield return Draw(it, r => s = r);
                if (s != null) { made[it] = s; try { Ready?.Invoke(); } catch { } }
            }
            busy = false;
        }

        IEnumerator Draw(Item it, System.Action<Sprite> done)
        {
            int layer = ArmorPreview.PreviewLayer();
            var stage = new GameObject("LMC_IconStage");
            stage.transform.position = Spot;
            // the meshes, where the item has them, as it lies on the floor
            var model = new GameObject("model");
            model.transform.SetParent(stage.transform, false);
            model.transform.localRotation = Quaternion.Euler(DevEuler ?? it.restingRotation);
            var root = it.spawnPrefab.transform;
            // (skinned ones too: the tongue, the masks; not the scan node, a red box the game never draws)
            var parts = it.spawnPrefab.GetComponentsInChildren<MeshFilter>(true).Select(mf => (mf.transform, mf.sharedMesh, (Renderer)mf.GetComponent<MeshRenderer>()))
                .Concat(it.spawnPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(sk => (sk.transform, sk.sharedMesh, (Renderer)sk)));
            foreach (var (tr, mesh, mr) in parts)
            {
                if (mr == null || !mr.enabled || mesh == null || tr.GetComponent<ScanNodeProperties>() != null || tr.name.StartsWith("ScanNode")) continue;
                var m = root.worldToLocalMatrix * tr.localToWorldMatrix;
                var go = new GameObject("part") { layer = layer };
                go.transform.SetParent(model.transform, false);
                go.transform.localPosition = m.GetColumn(3); go.transform.localRotation = m.rotation; go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Destroy(stage); done(null); yield break; }
            var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            // the camera, from the front and a little above, the model filling the frame
            const float fov = 22f;
            var dir = ViewFrom.normalized;
            float dist = b.extents.magnitude / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 1.02f;
            var camGo = new GameObject("cam") { layer = layer };
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.position = b.center + dir * dist;
            camGo.transform.LookAt(b.center);
            var cam = camGo.AddComponent<Camera>();
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; rt.Create();
            cam.targetTexture = rt; cam.cullingMask = 1 << layer; cam.fieldOfView = fov;
            cam.nearClipPlane = Mathf.Max(0.01f, dist - b.extents.magnitude * 2f); cam.farClipPlane = dist + b.extents.magnitude * 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            hd.volumeLayerMask = 0;
            hd.customRenderingSettings = true;
            var fs = hd.renderingPathCustomFrameSettings; var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
            foreach (var f in new[] { FrameSettingsField.AtmosphericScattering, FrameSettingsField.Volumetrics, FrameSettingsField.SSAO, FrameSettingsField.SSR,
                                      FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField, FrameSettingsField.Bloom, FrameSettingsField.ExposureControl,
                                      FrameSettingsField.ContactShadows, FrameSettingsField.Decals, FrameSettingsField.Postprocess })
            { mask.mask[(uint)f] = true; fs.SetEnabled(f, false); }
            hd.renderingPathCustomFrameSettings = fs; hd.renderingPathCustomFrameSettingsOverrideMask = mask;
            var lightGo = new GameObject("light") { layer = layer };
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.position = camGo.transform.position + Vector3.up * dist * 0.6f + camGo.transform.right * dist * 0.3f;
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Point; light.range = dist * 4f;
            var hl = lightGo.AddComponent<HDAdditionalLightData>(); hl.SetIntensity(LightCandela * dist * dist, LightUnit.Candela); hl.affectsVolumetric = false;
            // on black, then on white
            var shots = new Color[2][];
            for (int pass = 0; pass < 2; pass++)
            {
                var bg = pass == 0 ? Color.black : Color.white;
                cam.backgroundColor = bg; hd.backgroundColorHDR = bg;
                for (int i = 0; i < 4; i++) yield return new WaitForEndOfFrame();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); t.Apply();
                RenderTexture.active = prev;
                shots[pass] = t.GetPixels(); Destroy(t);
            }
            var px = new Color[Size * Size];
            for (int i = 0; i < px.Length; i++)
            {
                Color k = shots[0][i], w = shots[1][i];
                float a = Mathf.Clamp01(1f - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / 3f);
                px[i] = a < 0.02f ? Color.clear : new Color(Mathf.Clamp01(k.r / a), Mathf.Clamp01(k.g / a), Mathf.Clamp01(k.b / a), a);
            }
            // trimmed to what's drawn, and scaled to fill the icon (a little margin), as the game's icons fill theirs
            int x0 = Size, y0 = Size, x1 = -1, y1 = -1;
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                if (px[y * Size + x].a > 0.05f) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            var fit = new Color[Size * Size];
            if (x1 >= x0)
            {
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                float k = (Size * 0.92f) / Mathf.Max(w, h);
                float ox = (Size - w * k) / 2f, oy = (Size - h * k) / 2f;
                for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
                {
                    float sx = x0 + (x - ox) / k, sy = y0 + (y - oy) / k;
                    if (sx < x0 || sy < y0 || sx > x1 + 1 || sy > y1 + 1) { fit[y * Size + x] = Color.clear; continue; }
                    fit[y * Size + x] = px[Mathf.Clamp((int)sy, 0, Size - 1) * Size + Mathf.Clamp((int)sx, 0, Size - 1)];
                }
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.SetPixels(fit); tex.Apply();
            cam.targetTexture = null; rt.Release(); Destroy(rt); Destroy(stage);
            done(Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f)));
        }
    }
}
