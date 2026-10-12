using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's spider (#59): inside the facility ([Mobs] SpiderRarity). Low, wide and fast, eight legs scuttling,
    /// red eyes glowing in the dark; it runs at the nearest player it sees and bites. Killed, it drops string: four
    /// make a block of white wool. Its model comes from your Minecraft textures (entity/spider/spider.png, its eyes from
    /// entity/spider_eyes.png).
    /// </summary>
    public class SpiderAI : MinecraftMobAI
    {
        public const string Name = "Spider";
        public static EnemyType Type;
        protected override string Sound => "spider";
        protected override float ChaseSpeed => 5.4f; // (faster than a walking player, 4.55 m/s)
        protected override float RoamSpeed => 2.2f;
        protected override int Damage => 15;

        readonly Transform[] legs = new Transform[8];
        readonly Quaternion[] legRest = new Quaternion[8];
        float phase;
        Vector3 last;

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            for (int i = 0; i < 8 && m != null; i++)
            {
                legs[i] = m.Find("Leg" + i);
                if (legs[i] != null) legRest[i] = legs[i].localRotation;
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // Minecraft's scuttle: the legs swing forward and back in two alternating sets, lifting as they go
            float speed = Vector3.Distance(transform.position, last) / Mathf.Max(Time.deltaTime, 1e-4f);
            last = transform.position;
            phase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 4f;
            float amt = Mathf.Clamp01(speed / 2f);
            for (int i = 0; i < 8; i++)
            {
                if (legs[i] == null) continue;
                float s = Mathf.Sin(phase + (i % 2 == 0 ? 0f : Mathf.PI) + (i / 2) * 0.5f);
                legs[i].localRotation = Quaternion.Euler(0f, s * 20f * amt, Mathf.Max(0f, s) * 12f * amt * (i < 4 ? 1f : -1f)) * legRest[i];
            }
        }

        /// <summary>Host: Minecraft's drops: 0-2 string.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("string", out var s)) ModItems.ServerSpawnStack(s, n, at);
        }

        static Material sharedMat, eyesMat;

        public static void Register()
        {
            if (Plugin.SpiderRarity.Value <= 0) return;
            Type = Register<SpiderAI>("Spider", Name, "Fast; drops string", Plugin.SpiderRarity.Value, 4, 1.1f, 3, 2.2f, BuildModel);
        }

        // ------------------------------------------------------------------ the model: Minecraft's spider, 64x32
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Spider", "entity/spider/spider", new Color(0.25f, 0.2f, 0.18f));
            // (Minecraft's numbers, feet at 0, facing +z): the head in front, a small neck, the big body behind
            EntityModels.Part(parent, "Head", new Vector3(0, 9, 7), new Vector3(8, 8, 8), 32, 4, k, sharedMat);
            EntityModels.Part(parent, "Neck", new Vector3(0, 9, 0), new Vector3(6, 6, 6), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Body", new Vector3(0, 9, -9), new Vector3(10, 8, 12), 0, 12, k, sharedMat);
            // the eyes glow: Minecraft's eye layer, on the head's face
            if (eyesMat == null) eyesMat = EyesMaterial();
            if (eyesMat != null) EntityModels.Part(parent, "Eyes", new Vector3(0, 9, 7.02f), new Vector3(8.04f, 8.04f, 8.04f), 32, 4, k, eyesMat);
            // eight legs from the neck's sides, spread from front to back and angled down to the ground
            float[] spread = { 45f, 22.5f, -22.5f, -45f };
            for (int i = 0; i < 8; i++)
            {
                bool right = i < 4; int j = i % 4;
                var hip = new GameObject("Leg" + i).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(right ? -4f : 4f, 9f, 1f - j) * k;
                // (pointing out sideways, turned towards the front or back, tilted down so the tip reaches the floor)
                float yaw = right ? spread[j] : -spread[j];
                float tilt = j == 0 || j == 3 ? 40f : 33f;
                hip.localRotation = Quaternion.Euler(0f, yaw, right ? tilt : -tilt);
                EntityModels.Part(hip, "Leg", new Vector3(right ? -7f : 7f, 0f, 0f), new Vector3(16, 2, 2), 18, 0, k, sharedMat);
            }
        }

        static Material EyesMaterial()
        {
            if (McAssets.LoadTexture("entity/spider_eyes") == null) return null;
            var m = EntityModels.Material("LMC_SpiderEyes", "entity/spider_eyes", new Color(0, 0, 0, 0));
            // (glowing: the texture's own red, much brighter than the ambient light the other parts get; see-through elsewhere)
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, ArmorModels.AmbientEV + 7f, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            return m;
        }
    }
}
