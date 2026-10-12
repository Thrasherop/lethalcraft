using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A Minecraft mob's death (#113), on every machine: it goes red, tips over onto its side, and a moment later is gone
    /// in a puff of smoke. The monster itself is removed at once (the host despawns it); this is a copy of its model, for
    /// the look only.
    /// </summary>
    public class DeathPoof : MonoBehaviour
    {
        public const float FallTime = 0.45f, PoofAt = 1f, PuffLife = 0.8f;
        public static int Played; // (dev/tests)

        float t;
        bool poofed;
        Quaternion start;
        Vector3 axis;
        Material smoke;
        readonly List<Material> owned = new List<Material>();
        readonly List<(Transform tr, Vector3 vel, float size)> puffs = new List<(Transform, Vector3, float)>();

        /// <summary>The mob's model (the live one: it's hidden, a red copy falls in its place).</summary>
        public static void Play(Transform model)
        {
            if (model == null || !model.gameObject.activeInHierarchy) return;
            var copy = Object.Instantiate(model.gameObject, model.position, model.rotation);
            copy.name = "LMC_DeathPoof";
            copy.transform.localScale = model.lossyScale;
            foreach (var c in copy.GetComponentsInChildren<Collider>()) Destroy(c);
            Material first = null; var copyMats = new List<Material>();
            foreach (var r in copy.GetComponentsInChildren<Renderer>())
            {
                var ms = r.materials; // (copies: the live mob's stay as they are)
                foreach (var m in ms)
                {
                    if (first == null) first = m;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1f, 0.3f, 0.3f));
                }
                r.materials = ms;
                copyMats.AddRange(ms);
            }
            model.gameObject.SetActive(false);
            var p = copy.AddComponent<DeathPoof>();
            p.start = copy.transform.rotation;
            p.axis = model.forward; // (it tips over sideways, Minecraft's way)
            p.owned.AddRange(copyMats);
            if (first != null)
            {
                p.smoke = new Material(first);
                if (p.smoke.HasProperty("_BaseColorMap")) p.smoke.SetTexture("_BaseColorMap", null);
                if (p.smoke.HasProperty("_BaseColor")) p.smoke.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.78f));
                if (p.smoke.HasProperty("_EmissiveColor")) p.smoke.SetColor("_EmissiveColor", Color.black);
            }
            Played++;
        }

        void Update()
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / FallTime);
            transform.rotation = Quaternion.AngleAxis(90f * f * f, axis) * start;
            if (!poofed && t >= PoofAt) Poof();
            for (int i = 0; i < puffs.Count; i++)
            {
                var (tr, vel, size) = puffs[i];
                if (tr == null) continue;
                tr.position += vel * Time.deltaTime;
                float life = Mathf.Clamp01((t - PoofAt) / PuffLife);
                tr.localScale = Vector3.one * size * (1f - life);
            }
            if (t >= PoofAt + PuffLife) Destroy(gameObject);
        }

        void Poof()
        {
            poofed = true;
            // the body's gone: a puff of smoke where it lay
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var r in GetComponentsInChildren<Renderer>()) { bounds.Encapsulate(r.bounds); r.enabled = false; }
            var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            for (int i = 0; i < 14; i++)
            {
                var go = new GameObject("puff");
                go.transform.SetParent(transform, true);
                go.transform.position = bounds.center + Vector3.Scale(bounds.extents, new Vector3(Random.Range(-1f, 1f), Random.Range(-0.6f, 0.6f), Random.Range(-1f, 1f)));
                go.transform.rotation = Random.rotation;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                if (smoke != null) mr.sharedMaterial = smoke;
                float size = Random.Range(0.12f, 0.22f) * Plugin.S;
                go.transform.localScale = Vector3.one * size;
                puffs.Add((go.transform, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.4f, 0.9f), Random.Range(-0.3f, 0.3f)), size));
            }
            Sounds.Play("pop", bounds.center, 0.5f, Random.Range(0.8f, 1.0f), 12f);
        }

        void OnDestroy() { if (smoke != null) Destroy(smoke); foreach (var m in owned) if (m != null) Destroy(m); }
    }
}
