using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's chicken: out on the moons by day ([Mobs] ChickenRarity), like the pig and the cow. It wanders, never
    /// hurts anyone, runs off when hit, and follows a player holding wheat seeds. Killed, it drops 0-2 feathers (arrows:
    /// flint, a stick and a feather) and a raw chicken; the furnace cooks it. Model from entity/chicken (64x32).
    /// </summary>
    public class ChickenAI : MinecraftMobAI
    {
        public const string Name = "Chicken";
        public static EnemyType Type;
        protected override string Sound => "chicken";
        protected override bool Passive => true;
        protected override string Tempt => "wheat_seeds";
        protected override float RoamSpeed => 1.2f;
        protected override float PanicSpeed => 4.5f;

        /// <summary>Host: Minecraft's drops: 0-2 feathers, a raw chicken.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int f = Random.Range(0, 3);
            if (f > 0 && ModItems.ByKey.TryGetValue("feather", out var feather)) ModItems.ServerSpawnStack(feather, f, at);
            if (ModItems.ByKey.TryGetValue("raw_chicken", out var meat)) ModItems.ServerSpawnStack(meat, 1, at);
        }

        // (#244) Minecraft's laying: an egg every 5-10 minutes (eggTime: 6000-12000 ticks), with the plop; not a chick
        float eggIn = -1f;
        public static int Laid; // (dev/tests)
        public static float DevLayIn = -1f; public static int DevLayGen; int devLaySeen; // (dev "lay": every chicken's next egg in that many seconds)

        public override void Update()
        {
            base.Update();
            if (!IsServer || isEnemyDead || IsBaby) return;
            if (eggIn < 0f) eggIn = Random.Range(300f, 600f);
            if (devLaySeen != DevLayGen) { devLaySeen = DevLayGen; eggIn = Mathf.Min(eggIn, DevLayIn); }
            eggIn -= Time.deltaTime;
            if (eggIn > 0f) return;
            eggIn = Random.Range(300f, 600f);
            BlockNet.ServerSound(transform.position + Vector3.up * 0.3f, "chicken.plop", Random.Range(0.8f, 1.2f), 1f);
            if (ModItems.ByKey.TryGetValue("egg", out var egg)) ModItems.ServerSpawnStack(egg, 1, transform.position + Vector3.up * 0.2f);
            Laid++;
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.ChickenRarity.Value <= 0) return;
            Type = Register<ChickenAI>("Chicken", Name, "Bawk. Drops feathers", Plugin.ChickenRarity.Value, 1, 0.8f, 5, 1.2f, BuildModel, daytime: true, length: 0.7f);
            RegisterBaby<ChickenAI>(Type, "BabyChicken", "Chick", 1, 0.4f, 1.2f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; });
        }

        // ------------------------------------------------------------------ the model: Minecraft's chicken (64x32)
        public static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Chicken", PigAI.Tex("entity/chicken/temperate_chicken", "entity/chicken"), new Color(0.95f, 0.95f, 0.95f));
            // (Minecraft's: y down from 24 px, the head toward -z; here feet at 0, the head toward +z)
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 8, 0), new Vector3(6, 8, 6), 0, 9, k, sharedMat);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            EntityModels.Part(parent, "Head", new Vector3(0, 12, 4.5f), new Vector3(4, 6, 3), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Beak", new Vector3(0, 12, 7), new Vector3(4, 2, 2), 14, 0, k, sharedMat);
            EntityModels.Part(parent, "Wattle", new Vector3(0, 10, 6), new Vector3(2, 2, 2), 14, 4, k, sharedMat);
            EntityModels.Part(parent, "WingR", new Vector3(-3.5f, 9, 0), new Vector3(1, 4, 6), 24, 13, k, sharedMat);
            EntityModels.Part(parent, "WingL", new Vector3(3.5f, 9, 0), new Vector3(1, 4, 6), 24, 13, k, sharedMat);
            foreach (var (n, x) in new[] { ("LegL", 1.5f), ("LegR", -1.5f) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 5f, 0.5f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -2.5f, 0), new Vector3(3, 5, 3), 26, 0, k, sharedMat);
            }
        }
    }
}
