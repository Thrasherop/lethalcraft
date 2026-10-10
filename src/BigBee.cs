using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The Circuit Bees as one big Minecraft bee (#84, [Mobs] MinecraftBee): their swarm of particles is hidden and a bee
    /// about 0.8 block long flies where the swarm would be, buzzing its wings, angry (Minecraft's angry bee) while it
    /// chases. Their sounds, hive and zaps are the game's own. Every client builds its own (it follows the same point).
    /// </summary>
    [HarmonyPatch]
    public class BigBee : MonoBehaviour
    {
        public RedLocustBees Bees;
        Transform body, wingL, wingR;
        Renderer[] parts;
        static Material calm, angry;
        bool isAngry;
        Vector3 vel;
        float flap;

        [HarmonyPatch(typeof(RedLocustBees), "Start"), HarmonyPostfix]
        static void Replace(RedLocustBees __instance)
        {
            if (!Plugin.MinecraftBee.Value || __instance == null || __instance.beeParticles == null) return;
            try
            {
                __instance.beeParticles.enabled = false;
                var go = new GameObject("LMC_BigBee");
                var bee = go.AddComponent<BigBee>();
                bee.Bees = __instance;
                bee.Build();
                go.transform.position = __instance.beeParticlesTarget != null ? __instance.beeParticlesTarget.position : __instance.transform.position;
            }
            catch (System.Exception e) { Plugin.Log.LogError("Big bee: " + e); }
        }

        void Build()
        {
            if (calm == null)
            {
                calm = EntityModels.Material("LMC_Bee", "entity/bee/bee", new Color(0.95f, 0.75f, 0.2f), doubleSided: true);
                angry = EntityModels.Material("LMC_BeeAngry", "entity/bee/bee_angry", new Color(0.95f, 0.55f, 0.15f), doubleSided: true);
            }
            // (Minecraft's bee, 64x64: a 7x7x10 body at 0,0; antennae 1x2x3 at 2,0 and 2,3; flat wings 9x0x6 at 0,18;
            // flat legs 7x2x0 at 26,1 / 26,3 / 26,5. Scaled so the body is 0.8 of a block long; its face is +z)
            float k = 0.8f * BlockWorld.S / 10f;
            body = new GameObject("body").transform;
            body.SetParent(transform, false);
            EntityModels.Part(body, "BeeBody", Vector3.zero, new Vector3(7, 7, 10), 0, 0, k, calm);
            EntityModels.Part(body, "AntennaL", new Vector3(2f, 3.5f, 6.5f), new Vector3(1, 2, 3), 2, 0, k, calm);
            EntityModels.Part(body, "AntennaR", new Vector3(-2f, 3.5f, 6.5f), new Vector3(1, 2, 3), 2, 3, k, calm);
            wingL = Hinge("WingL", new Vector3(1.5f, 3.5f, 0f) * k);
            EntityModels.Part(wingL, "Wing", new Vector3(4.5f, 0f, 0f), new Vector3(9, 0, 6), 0, 18, k, calm);
            wingR = Hinge("WingR", new Vector3(-1.5f, 3.5f, 0f) * k);
            EntityModels.Part(wingR, "Wing", new Vector3(-4.5f, 0f, 0f), new Vector3(9, 0, 6), 0, 18, k, calm);
            EntityModels.Part(body, "LegsFront", new Vector3(0f, -4.5f, 2f), new Vector3(7, 2, 0), 26, 1, k, calm);
            EntityModels.Part(body, "LegsMiddle", new Vector3(0f, -4.5f, 0f), new Vector3(7, 2, 0), 26, 3, k, calm);
            EntityModels.Part(body, "LegsBack", new Vector3(0f, -4.5f, -2f), new Vector3(7, 2, 0), 26, 5, k, calm);
            parts = GetComponentsInChildren<Renderer>();
        }

        Transform Hinge(string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(body, false);
            t.localPosition = at;
            return t;
        }

        void Update()
        {
            if (Bees == null || Bees.isEnemyDead) { Destroy(gameObject); return; }
            var target = Bees.beeParticlesTarget != null ? Bees.beeParticlesTarget.position : Bees.transform.position;
            // (flies to where the swarm would be: quick when it chases, lazy round the hive)
            bool chasing = Bees.currentBehaviourStateIndex >= 1; // (1: guarding the hive, 2: after whoever took it)
            transform.position = Vector3.SmoothDamp(transform.position, target, ref vel, chasing ? 0.25f : 0.8f, chasing ? 14f : 4f);
            // facing where it goes, or the player it's after
            var look = Bees.targetPlayer != null && chasing ? Bees.targetPlayer.transform.position + Vector3.up * 1.2f - transform.position : vel;
            look.y *= 0.3f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), Time.deltaTime * 6f);
            // a bob, and wings buzzing
            flap += Time.deltaTime;
            body.localPosition = Vector3.up * Mathf.Sin(flap * 3f) * 0.06f;
            float a = Mathf.Sin(flap * 55f) * 35f;
            wingL.localRotation = Quaternion.Euler(0f, 0f, a);
            wingR.localRotation = Quaternion.Euler(0f, 0f, -a);
            if (chasing != isAngry)
            {
                isAngry = chasing;
                foreach (var r in parts) r.sharedMaterial = isAngry ? angry : calm;
            }
        }

        public static string Describe()
        {
            var all = FindObjectsOfType<BigBee>();
            return $"bees={all.Length} " + string.Join(" ; ", System.Array.ConvertAll(all, b => $"{b.transform.position} angry={b.isAngry} particles={(b.Bees != null && b.Bees.beeParticles != null ? b.Bees.beeParticles.enabled.ToString() : "-")}"));
        }
    }
}
