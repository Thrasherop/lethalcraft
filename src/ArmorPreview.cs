using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// The little window in the [I] inventory showing your character (like Minecraft's): a copy of your own player model
    /// (suit and all) standing far below the map in front of its own camera, its body and head turning to follow the
    /// mouse. Built when the inventory opens, gone when it closes.
    /// </summary>
    public class ArmorPreview : MonoBehaviour
    {
        static ArmorPreview instance;
        const int W = 216, H = 288; // the window is 54 x 72 GUI pixels
        static readonly Vector3 Spot = new Vector3(0f, -2600f, 0f);

        RectTransform box;
        RawImage image;
        RenderTexture rt;
        Camera cam;
        GameObject stage, model;
        Transform head, body;
        readonly System.Collections.Generic.List<(Transform mine, Transform src)> bones = new System.Collections.Generic.List<(Transform, Transform)>();
        public static bool FrameOverrides = true, NoVolumes = true; // (dev)
        Quaternion headRest;
        public static float LightIntensity = 60f, CamDistance = 4.7f, CamHeight = 1.25f, Fov = 30f; // (dev: tunable)

        public static void Attach(RectTransform box)
        {
            if (instance == null) instance = new GameObject("LMC_ArmorPreview").AddComponent<ArmorPreview>();
            instance.Show(box);
        }

        void Show(RectTransform b)
        {
            box = b;
            if (rt == null) { rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "LMC_PreviewRT", antiAliasing = 1 }; rt.Create(); }
            var irt = new GameObject("view", typeof(RectTransform)).GetComponent<RectTransform>();
            irt.SetParent(b, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = new Vector2(1, 1); irt.offsetMax = new Vector2(-1, -1);
            image = irt.gameObject.AddComponent<RawImage>();
            image.texture = rt; image.raycastTarget = false;
            BuildStage();
        }

        static int PreviewLayer()
        {
            for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l)) && l != BlockWorld.SolidLayer) return l;
            return 31;
        }

        void BuildStage()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            var src = p != null ? p.transform.Find("ScavengerModel") : null;
            if (src == null) return;
            if (stage == null)
            {
                stage = new GameObject("LMC_PreviewStage");
                stage.transform.position = Spot;
                int layer = PreviewLayer();
                var camGo = new GameObject("cam");
                camGo.transform.SetParent(stage.transform, false);
                cam = camGo.AddComponent<Camera>();
                cam.targetTexture = rt;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f;
                var hd = camGo.AddComponent<HDAdditionalCameraData>();
                hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                hd.backgroundColorHDR = cam.backgroundColor;
                if (NoVolumes) hd.volumeLayerMask = 0; // none of the level's fog, exposure or post effects
                hd.customRenderingSettings = FrameOverrides;
                var fs = hd.renderingPathCustomFrameSettings; var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
                foreach (var f in new[] { FrameSettingsField.AtmosphericScattering, FrameSettingsField.Volumetrics, FrameSettingsField.SSAO, FrameSettingsField.SSR,
                                          FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField, FrameSettingsField.Bloom, FrameSettingsField.ExposureControl,
                                          FrameSettingsField.ContactShadows, FrameSettingsField.Decals, FrameSettingsField.Postprocess })
                {
                    mask.mask[(uint)f] = true; fs.SetEnabled(f, false);
                }
                hd.renderingPathCustomFrameSettings = fs; hd.renderingPathCustomFrameSettingsOverrideMask = mask;
                var lightGo = new GameObject("light");
                lightGo.transform.SetParent(stage.transform, false);
                lightGo.transform.localPosition = new Vector3(1.2f, 2.6f, 2.4f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point; light.range = 12f;
                var hl = lightGo.AddComponent<HDAdditionalLightData>();
                hl.SetIntensity(LightIntensity, LightUnit.Candela);
                hl.affectsVolumetric = false;
                stage.layer = layer; camGo.layer = layer; lightGo.layer = layer;
            }
            if (model != null) Destroy(model);
            // a copy of the model, made inside an inactive holder so none of its parts (the camera, the network object,
            // the rig) ever wake up; then stripped to the meshes and the animator
            var holder = new GameObject("holder"); holder.SetActive(false);
            holder.transform.SetParent(stage.transform, false);
            model = Instantiate(src.gameObject, holder.transform);
            model.name = "model";
            foreach (var n in new[] { "metarig/CameraContainer", "metarig/ScavengerModelArmsOnly", "metarig/Rig 1", "LOD2", "LOD3" })
            {
                var t = model.transform.Find(n);
                if (t != null) DestroyImmediate(t.gameObject);
            }
            // everything but the meshes goes; scripts first, then audio filters, then what they depended on
            for (int pass = 0; pass < 3; pass++)
                foreach (var c in model.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is SkinnedMeshRenderer) continue;
                    if ((c is MeshRenderer || c is MeshFilter) && (c.gameObject.name == "LevelSticker" || c.gameObject.name == "BetaBadge" || c.gameObject.name == ArmorModels.PartName)) continue;
                    bool filter = c is AudioReverbFilter || c is AudioLowPassFilter || c is AudioHighPassFilter || c is AudioChorusFilter || c is AudioEchoFilter || c is AudioDistortionFilter;
                    if (pass == 0 && !(c is MonoBehaviour)) continue;
                    if (pass == 1 && !filter) continue;
                    DestroyImmediate(c);
                }
            int lyr = stage.layer;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = lyr;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.enabled = true; r.gameObject.SetActive(true); }
            // the copy wears your pose: its bones follow your own (animated) model's, matched by their path in the model
            // (not by name: the first-person arms have bones of the same names)
            bones.Clear();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t == model.transform) continue;
                var st = src.Find(PathIn(model.transform, t));
                if (st != null) bones.Add((t, st));
            }
            model.transform.SetParent(stage.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = src.lossyScale;
            Destroy(holder);
            model.SetActive(true);
            body = model.transform;
            head = model.transform.Find("metarig/spine/spine.001/spine.002/spine.003/spine.004");
            if (head != null) headRest = head.localRotation;
            cam.enabled = true;
            PlaceCamera();
        }

        /// <summary>Your model changed (armor put on or taken off): a new copy, if the window is showing.</summary>
        public static void Rebuild()
        {
            if (instance != null && instance.model != null && instance.box != null && instance.box.gameObject.activeInHierarchy) instance.BuildStage();
        }

        static string PathIn(Transform root, Transform t)
        {
            var path = t.name;
            for (var x = t.parent; x != null && x != root; x = x.parent) path = x.name + "/" + path;
            return path;
        }

        void PlaceCamera()
        {
            if (cam == null || body == null) return;
            cam.fieldOfView = Fov;
            var fwd = Vector3.forward; // the model faces +z of the stage
            cam.transform.position = Spot + fwd * CamDistance + Vector3.up * CamHeight;
            cam.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        }

        void LateUpdate()
        {
            bool showing = box != null && box.gameObject.activeInHierarchy;
            if (!showing)
            {
                if (model != null) { Destroy(model); model = null; }
                if (cam != null) cam.enabled = false;
                return;
            }
            PlaceCamera();
            foreach (var (mine, src) in bones)
                if (mine != null && src != null) { mine.localRotation = src.localRotation; mine.localPosition = src.localPosition; }
            if (head != null) headRest = head.localRotation;
            // like Minecraft: the body turns a little toward the mouse, the head a lot more (and looks up/down)
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || body == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(box, mouse.position.ReadValue(), null, out var local);
            var r = box.rect;
            float dx = local.x - r.center.x, dy = local.y - (r.yMax - r.height * 0.22f); // (eye level)
            // (the camera looks at the model's front, so turning toward the viewer's right is a negative yaw)
            float bodyYaw = -Mathf.Atan(dx / 40f) * 20f, headYaw = -Mathf.Atan(dx / 40f) * 40f, headPitch = Mathf.Atan(dy / 40f) * 20f;
            body.localRotation = Quaternion.Euler(0f, bodyYaw, 0f);
            if (head != null)
            {
                // from its rest pose every frame (the animator may not touch the head, so nothing would undo last frame's turn)
                head.localRotation = headRest;
                head.rotation = Quaternion.AngleAxis(headYaw - bodyYaw, body.up) * Quaternion.AngleAxis(-headPitch, body.right) * head.rotation;
            }
        }

        /// <summary>(dev) new light/camera settings: the stage is rebuilt the next time the inventory opens.</summary>
        public static string DevTune(string[] a)
        {
            if (a.Length > 1) LightIntensity = float.Parse(a[1]);
            if (a.Length > 2) CamDistance = float.Parse(a[2]);
            if (a.Length > 3) CamHeight = float.Parse(a[3]);
            if (a.Length > 4) Fov = float.Parse(a[4]);
            if (a.Length > 5) FrameOverrides = a[5] == "1";
            if (a.Length > 6) NoVolumes = a[6] == "1";
            if (a.Length > 1 && instance != null && instance.stage != null) { Destroy(instance.stage); instance.stage = null; instance.model = null; }
            string diag = "";
            if (instance != null && instance.model != null)
            {
                var smr = instance.model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                diag = $" model active={instance.model.activeInHierarchy} bones={instance.bones.Count} smr={(smr != null ? smr.name + " en=" + smr.enabled + " vis=" + smr.isVisible + " bounds=" + smr.bounds.center + "/" + smr.bounds.size + " root=" + (smr.rootBone != null ? smr.rootBone.position.ToString() : "-") : "none")} cam={instance.cam.transform.position} on={instance.cam.enabled}";
            }
            return $"light={LightIntensity} dist={CamDistance} height={CamHeight} fov={Fov} frame={FrameOverrides} novol={NoVolumes}" + diag;
        }

        void OnDestroy()
        {
            if (stage != null) Destroy(stage);
            if (rt != null) rt.Release();
        }
    }
}
