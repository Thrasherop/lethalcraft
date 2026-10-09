using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>The Totem of Undying in hand (one item, doesn't stack).</summary>
    public class TotemItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (!right && QKey.CanThrow(this)) playerHeldBy.DiscardHeldObject();
        }
    }

    /// <summary>
    /// Totem of Undying (#53): a store item. Anywhere in your hotbar when you would die, it's used up instead: you're back
    /// to full health and in the ship, with Minecraft's totem showing over your screen and its green and yellow particles
    /// around you for everyone to see.
    /// </summary>
    [HarmonyPatch]
    public static class Totem
    {
        public const string Key = "totem_of_undying";
        public static bool DevNoTotem; // (dev: "totem 0" makes deaths ignore the totem, to compare)

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool Save(PlayerControllerB __instance, CauseOfDeath causeOfDeath)
        {
            var p = __instance;
            if (DevNoTotem || p == null || !p.IsOwner || p.isPlayerDead || !p.isPlayerControlled || p != GameNetworkManager.Instance?.localPlayerController) return true;
            if (GameModes.IsCreative(p)) return true;
            int slot = -1;
            for (int i = 0; i < p.ItemSlots.Length; i++)
                if (Crafting.KeyOf(p.ItemSlots[i]) == Key) { slot = i; break; }
            if (slot < 0) return true;
            Plugin.Log.LogInfo($"Totem of Undying saved {p.playerUsername} ({causeOfDeath})");
            // used up
            p.DestroyItemInSlotAndSync(slot);
            // whatever had hold of you lets go; full health, not bleeding
            try { p.CancelSpecialTriggerAnimations(); } catch { }
            p.inSpecialInteractAnimation = false;
            p.inAnimationWithEnemy = null;
            p.health = 100;
            if (p.criticallyInjured || p.bleedingHeavily) p.MakeCriticallyInjured(false);
            HUDManager.Instance.UpdateHealthUI(100, false);
            if (Survival.Instance != null) Survival.Instance.LastHealth = 100;
            // back to the ship
            var sor = StartOfRound.Instance;
            Commands.TeleportLocal(sor.GetPlayerSpawnPosition((int)p.playerClientId), false, true, true);
            Overlay.Show();
            BlockNet.RequestTotemPop();
            return false;
        }

        /// <summary>Dev: the effect alone (overlay and burst), to look at.</summary>
        public static string DevFx()
        {
            var p = GameNetworkManager.Instance.localPlayerController;
            Overlay.Show();
            Pop((int)p.playerClientId);
            return "icon=" + (Atlas.IconFor("item_" + Key) != null);
        }

        /// <summary>Every client: the green and yellow burst (and the sound) around a player whose totem went off.</summary>
        public static void Pop(int playerIdx)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || playerIdx < 0 || playerIdx >= sor.allPlayerScripts.Length) return;
            var p = sor.allPlayerScripts[playerIdx];
            if (p == null) return;
            var at = p.transform.position + Vector3.up * 1.1f;
            Sounds.Play("totem", at, 1f, 1f);
            // (your own: around you, not in your face)
            Particles(at, p == GameNetworkManager.Instance?.localPlayerController);
        }

        static Material green, yellow;

        static void Particles(Vector3 at, bool self)
        {
            if (green == null)
            {
                green = Glow("LMC_TotemGreen", new Color(0.35f, 1f, 0.2f));
                yellow = Glow("LMC_TotemYellow", new Color(1f, 0.85f, 0.15f));
            }
            var mesh = Quad();
            for (int i = 0; i < 70; i++)
            {
                var go = new GameObject("LMC_TotemSpark");
                var off = Random.onUnitSphere; if (self) off.y = Mathf.Abs(off.y) * 0.3f - 0.4f;
                go.transform.position = at + off * (self ? Random.Range(0.9f, 1.4f) : Random.Range(0f, 0.4f));
                go.transform.localScale = Vector3.one * (self ? Random.Range(0.03f, 0.07f) : Random.Range(0.06f, 0.14f));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Random.value < 0.6f ? green : yellow;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var s = go.AddComponent<Spark>();
                var dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 1.4f + 0.3f;
                s.Velocity = dir.normalized * Random.Range(2.5f, 6f);
                s.Life = Random.Range(1.2f, 2.4f);
            }
        }

        static Material Glow(string name, Color c)
        {
            var m = new Material(Shader.Find("HDRP/Unlit")) { name = name };
            m.SetColor("_UnlitColor", c);
            m.SetColor("_BaseColor", c);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
            return m;
        }

        static Mesh quad;
        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "LMC_TotemQuad" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0); // (both sides)
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>A spark: drifts out, slows, falls a little, faces the camera, shrinks away.</summary>
        class Spark : MonoBehaviour
        {
            public Vector3 Velocity;
            public float Life;
            float age;
            Vector3 size;
            void Start() => size = transform.localScale;
            void Update()
            {
                age += Time.deltaTime;
                if (age >= Life) { Destroy(gameObject); return; }
                Velocity *= 1f - 2.2f * Time.deltaTime;
                Velocity += Vector3.down * 0.6f * Time.deltaTime;
                transform.position += Velocity * Time.deltaTime;
                var cam = Camera.main != null ? Camera.main.transform : null;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
                transform.localScale = size * (1f - age / Life);
            }
        }

        /// <summary>The totem over your screen: it springs up big in the middle, turns, and falls away (local player).</summary>
        class Overlay : MonoBehaviour
        {
            RectTransform img;
            float t;

            public static void Show()
            {
                var sprite = Atlas.IconFor("item_" + Key);
                if (sprite == null) return;
                var go = new GameObject("LMC_TotemOverlay");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                var ig = new GameObject("totem");
                ig.transform.SetParent(go.transform, false);
                var image = ig.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                var o = go.AddComponent<Overlay>();
                o.img = ig.GetComponent<RectTransform>();
                o.img.sizeDelta = new Vector2(Screen.height * 0.5f, Screen.height * 0.5f);
            }

            void Update()
            {
                t += Time.deltaTime;
                const float total = 1.8f;
                if (t >= total) { Destroy(gameObject); return; }
                // a quick overshoot up to full size, a wobble, then it drops and shrinks away
                float up = Mathf.Clamp01(t / 0.35f), s = up < 1f ? Mathf.SmoothStep(0.2f, 1.15f, up) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.35f) / 0.2f));
                float away = Mathf.Clamp01((t - 1.1f) / 0.7f);
                img.localScale = Vector3.one * s * (1f - away * 0.6f);
                img.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 9f) * 12f * (1f - up * 0.5f));
                img.anchoredPosition = new Vector2(0, -away * away * Screen.height * 0.9f);
            }
        }
    }
}
