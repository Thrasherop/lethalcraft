using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#228) Name tags, Minecraft's: named at an anvil, used on a mob with [E] (any mob, a creeper too) it gives the mob
    /// that name and is used up. The name shows over the mob while you look at it (Minecraft's), and a named mob stays
    /// with the ship (#227, ShipMobs). Found fishing (a treasure), in the facility now and then, in outpost and Nether
    /// chests, and sold in the store (the owner's).
    /// </summary>
    [HarmonyPatch]
    public static class NameTags
    {
        public const string Key = "name_tag";
        public static int Named; // (dev/tests)

        static bool HoldingNamed(PlayerControllerB p, out int nameId)
        {
            nameId = 0;
            var held = p != null ? p.currentlyHeldObjectServer as StackItem : null;
            if (held == null || held.ItemKey != Key) return false;
            nameId = ItemNames.IdOf(held);
            return true;
        }

        /// <summary>The mob in front of you (any kind), or null.</summary>
        public static EnemyAI Aimed(PlayerControllerB p, float range = 4f)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, range, 1 << 19, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var d = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (d != null && d.mainScript != null && !d.mainScript.isEnemyDead) return d.mainScript;
            }
            return null;
        }

        // (before the pets', the horse's and the animals' own [E]: a name tag in hand names whatever it's used on)
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix, HarmonyPriority(Priority.High)]
        static bool OnInteract(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !Builder.CanAct(p) || !HoldingNamed(p, out int id)) return true;
            var e = Aimed(p);
            if (e == null) return true;
            if (id <= 0) { McHud.Toast("Name the tag at an anvil first."); return false; }
            BlockNet.RequestNameMob(e.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix, HarmonyPriority(Priority.Low)]
        static void Tip(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || p.cursorTip == null || !Builder.CanAct(p) || !HoldingNamed(p, out int id)) return;
            if (Aimed(p) != null) p.cursorTip.text = id > 0 ? "Name it \"" + ItemNames.Text(id) + "\" : [E]" : "Name the tag at an anvil first";
        }

        /// <summary>Host: a player used their name tag on a mob.</summary>
        public static void ServerName(ulong sender, ulong mobId)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || !Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mobId, out var no)) return;
            var e = no.GetComponent<EnemyAI>();
            if (e == null || e.isEnemyDead || Vector3.Distance(p.transform.position, e.transform.position) > 6f) return;
            if (!HoldingNamed(p, out int id) || id <= 0) return;
            var name = ItemNames.Text(id);
            if (name == null || ShipMobs.NameOf(e) == name) return;
            ShipMobs.ServerSetName(e, name);
            if (!GameModes.IsCreative(p)) ((StackItem)p.currentlyHeldObjectServer).ServerUseUp(p);
            Named++;
            Plugin.Log.LogInfo($"[names] {p.playerUsername} named a {e.enemyType.enemyName} \"{name}\"");
        }

        // ------------------------------------------------------------------ where they're found
        /// <summary>Host, as the facility's scrap goes out: a name tag one facility in three (the owner's: found now and then).</summary>
        public static void ServerSpawnLoot()
        {
            try
            {
                var spots = Object.FindObjectsOfType<RandomScrapSpawn>();
                if (spots.Length == 0 || !ModItems.ByKey.TryGetValue(Key, out var item)) return;
                var rng = new System.Random(StartOfRound.Instance.randomMapSeed + 228);
                if (rng.Next(3) != 0) return;
                ModItems.ServerSpawnStack(item, 1, spots[rng.Next(spots.Length)].transform.position + Vector3.up * 0.3f);
            }
            catch (System.Exception e) { Plugin.Log.LogError("name tag loot: " + e); }
        }
    }

    /// <summary>(#228) The name over the mob you're looking at, in Minecraft's font, facing you (every machine, its own).</summary>
    public class NameLabels : MonoBehaviour
    {
        GameObject label;
        Texture2D tex;
        Material mat;
        string shownText;
        const float Px = 0.025f;
        EnemyAI topOf; readonly List<Renderer> renderers = new List<Renderer>(); // (a font pixel, in metres: Minecraft's 0.025 a pixel)

        void LateUpdate()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            EnemyAI e = null; string name = null;
            if (p != null && !p.isPlayerDead && ShipMobs.Names.Count > 0 && McFont.Ready)
            {
                e = NameTags.Aimed(p, 16f);
                name = ShipMobs.NameOf(e);
                // (not through a wall: something solid nearer than the mob hides it)
                if (name != null)
                {
                    var cam0 = p.gameplayCamera.transform;
                    float to = Vector3.Distance(cam0.position, e.transform.position);
                    if (Physics.Raycast(cam0.position, cam0.forward, out var wall, to, ServerLogic.WorldGeometryMask | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)
                        && wall.collider.GetComponentInParent<EnemyAI>() == null && wall.distance < to - 1f) name = null;
                }
            }
            if (name == null) { if (label != null) label.SetActive(false); return; }
            if (label == null) Build();
            if (name != shownText) Draw(name);
            // (over its head: the top of what it's drawn with)
            if (e != topOf) { topOf = e; e.GetComponentsInChildren(renderers); }
            float top = e.transform.position.y + 1f;
            foreach (var r in renderers) if (r != null && r.enabled && !(r is ParticleSystemRenderer)) top = Mathf.Max(top, r.bounds.max.y);
            label.transform.position = new Vector3(e.transform.position.x, top + 0.3f, e.transform.position.z);
            var cam = p.gameplayCamera.transform;
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.position, Vector3.up);
            label.layer = p.gameObject.layer == 0 ? 0 : label.layer;
            label.SetActive(true);
        }

        void Build()
        {
            label = new GameObject("LMC_NameLabel");
            DontDestroyOnLoad(label);
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) }; // (it faces away from the camera: read from behind, the right way round)
            m.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 };
            m.RecalculateNormals(); m.RecalculateBounds();
            label.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = label.AddComponent<MeshRenderer>(); Atlas.NoDecals(mr);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mat = Atlas.MakeLit("LMC_NameLabel", cutout: true, doubleSided: true, emissive: true);
            mr.sharedMaterial = mat;
        }

        void Draw(string text)
        {
            shownText = text;
            int w = Mathf.Max(4, Mathf.RoundToInt(McFont.Width(text)) + 4), h = 12;
            if (tex == null || tex.width != w) { if (tex != null) Destroy(tex); tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; }
            var px = new Color32[w * h];
            // (a dark band behind, as Minecraft's: here solid, the text white on it)
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(24, 24, 24, 255);
            McFont.Blit(px, w, h, text, 2, 2, new Color32(255, 255, 255, 255));
            tex.SetPixels32(px); tex.Apply();
            mat.SetTexture("_BaseColorMap", tex); mat.mainTexture = tex;
            if (mat.HasProperty("_EmissiveColorMap")) mat.SetTexture("_EmissiveColorMap", tex);
            label.transform.localScale = new Vector3(w * Px, h * Px, 1f);
        }
    }
}
