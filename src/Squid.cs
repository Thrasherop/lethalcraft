using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's squid (#150): in the moons' own water (lakes and rivers: the game's water volumes), a few in each when
    /// the ship lands ([Mobs] Squid). It swims about inside the water in slow pulses. Killed: 1-3 ink sacs (with white
    /// wool, black wool). It never leaves the water and never hurts anyone.
    /// </summary>
    [HarmonyPatch]
    public class SquidAI : MinecraftMobAI
    {
        public static EnemyType Type;
        public static int Spawned; // (dev/tests)
        protected override string Sound => "squid";
        protected override bool Passive => true;
        protected override bool Swims => true;
        Bounds water; Vector3 goal; float pulse, nextGoal;
        readonly Transform[] tentacles = new Transform[8];

        public override void Start()
        {
            base.Start();
            // (the water it's in: the game's water volume around it)
            foreach (var q in Object.FindObjectsOfType<QuicksandTrigger>())
            {
                var c = q.GetComponent<Collider>();
                if (q.isWater && c != null && c.bounds.Contains(transform.position)) { water = c.bounds; break; }
            }
            if (water.size == Vector3.zero) water = new Bounds(transform.position, Vector3.one * 4f);
            var m = transform.Find("Model");
            for (int i = 0; i < 8 && m != null; i++) tentacles[i] = m.Find("Tentacle" + i);
        }

        protected override void SwimAI()
        {
            if (Time.time < nextGoal && Vector3.Distance(transform.position, goal) > 0.5f) return;
            nextGoal = Time.time + Random.Range(3f, 8f);
            // (somewhere else in its water, under the surface)
            var min = water.min + Vector3.one * 0.6f; var max = water.max - new Vector3(0.6f, 0.9f, 0.6f);
            goal = new Vector3(Random.Range(min.x, Mathf.Max(min.x, max.x)), Random.Range(min.y, Mathf.Max(min.y, max.y)), Random.Range(min.z, Mathf.Max(min.z, max.z)));
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // a squid's pulse: its tentacles open and close, it shoots forward on each close
            pulse += Time.deltaTime * (Panicking ? 2.2f : 1.1f);
            float s = Mathf.Sin(pulse * Mathf.PI * 2f);
            for (int i = 0; i < 8; i++) if (tentacles[i] != null) tentacles[i].localRotation = Quaternion.AngleAxis(-15f - s * 22f, Quaternion.Euler(0f, i * 45f, 0f) * Vector3.right);
            if (!IsOwner) return;
            var to = goal - transform.position;
            if (to.sqrMagnitude > 0.01f)
            {
                float speed = (Panicking ? 2.2f : 0.7f) * Mathf.Max(0f, -s);
                var step = Vector3.ClampMagnitude(to, speed * Time.deltaTime);
                var next = transform.position + step;
                if (water.Contains(next)) transform.position = next;
                var flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 60f * Time.deltaTime);
            }
            SyncPositionToClients();
        }

        /// <summary>Host: Minecraft's drops: 1-3 ink sacs.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue("ink_sac", out var ink)) ModItems.ServerSpawnStack(ink, Random.Range(1, 4), at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.Squid.Value) return;
            Type = Register<SquidAI>("Squid", "Squid", "Lives in the water. Drops ink sacs", 0, 2, 1.2f, 12, 0.7f, BuildModel, daytime: true, length: 1.0f);
        }

        // ------------------------------------------------------------------ in the moons' water, as the ship lands
        static float landed = -100f;
        [HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc"), HarmonyPostfix]
        static void Landed(RoundManager __instance)
        {
            if (Type == null || !BlockNet.IsServer || TerrainCarver.AtCompany || !Landing.Once(ref landed)) return;
            __instance.StartCoroutine(SpawnLater());
        }

        static IEnumerator SpawnLater()
        {
            yield return new WaitForSeconds(8f);
            int n = ServerPopulate();
            if (n > 0) Plugin.Log.LogInfo($"[squid] {n} in the moon's water");
        }

        /// <summary>Host: a few squid in each of the moon's water volumes big enough to swim in.</summary>
        public static int ServerPopulate(int perVolume = -1)
        {
            int n = 0;
            foreach (var q in Object.FindObjectsOfType<QuicksandTrigger>())
            {
                var c = q.GetComponent<Collider>();
                if (!q.isWater || c == null) continue;
                var b = c.bounds;
                if (b.size.x < 4f || b.size.z < 4f || b.size.y < 1.8f) continue; // (a puddle: no)
                int count = perVolume >= 0 ? perVolume : Mathf.Clamp(Mathf.RoundToInt(b.size.x * b.size.z / 300f), 1, 4);
                for (int i = 0; i < count; i++)
                {
                    var at = new Vector3(Random.Range(b.min.x + 1f, b.max.x - 1f), Random.Range(b.min.y + 0.6f, b.max.y - 1f), Random.Range(b.min.z + 1f, b.max.z - 1f));
                    if (!c.bounds.Contains(at)) continue;
                    MobSpawns.DevSpawnUntil = Time.time + 1f;
                    RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, Type);
                    n++; Spawned++;
                }
            }
            return n;
        }

        // ------------------------------------------------------------------ the model: Minecraft's squid (64x32), upright
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.7f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Squid", "entity/squid/squid", new Color(0.2f, 0.3f, 0.5f));
            EntityModels.Part(parent, "Body", new Vector3(0, 17, 0), new Vector3(12, 16, 12), 0, 0, k, sharedMat);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var t = new GameObject("Tentacle" + i).transform; t.SetParent(parent, false);
                t.localPosition = new Vector3(Mathf.Cos(a) * 5f, 9f, Mathf.Sin(a) * 5f) * k;
                EntityModels.Part(t, "Arm", new Vector3(0, -9, 0), new Vector3(2, 18, 2), 48, 0, k, sharedMat);
            }
        }
    }
}
