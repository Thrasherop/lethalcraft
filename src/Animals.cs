using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's farm animals (#95): pigs and cows out on the moons by day ([Mobs] PigRarity, CowRarity). They wander,
    /// never hurt anyone, run off when hit, and walk after a player holding their food (a carrot for a pig, wheat for a
    /// cow). Killed, a pig gives raw porkchops and a cow raw beef and leather; the furnace cooks the meat.
    /// Their models come from your Minecraft textures (entity/pig/temperate_pig.png, entity/cow/temperate_cow.png).
    /// </summary>
    public class PigAI : MinecraftMobAI
    {
        public const string Name = "Pig";
        public static EnemyType Type;
        protected override string Sound => "pig";
        protected override bool Passive => true;
        protected override string Tempt => "carrot";
        protected override string[] Foods => new[] { "carrot", "potato", "beetroot" }; // (#244: Minecraft's pig food)
        protected override float RoamSpeed => 1.3f;
        protected override float PanicSpeed => 4.5f;

        /// <summary>Host: Minecraft's drops: 1-3 raw porkchops.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue("raw_porkchop", out var it)) ModItems.ServerSpawnStack(it, Random.Range(1, 4), at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.PigRarity.Value <= 0) return;
            Type = Register<PigAI>("Pig", Name, "Oink. Drops porkchops", Plugin.PigRarity.Value, 2, 1.3f, 4, 1.3f, BuildModel, daytime: true, length: 1.4f);
            RegisterBaby<PigAI>(Type, "BabyPig", "Piglet", 1, 0.7f, 1.3f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; });
        }

        // ------------------------------------------------------------------ the model: Minecraft's pig (64x64, the classic layout)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Pig", Tex("entity/pig/temperate_pig", "entity/pig/pig"), new Color(0.95f, 0.6f, 0.6f));
            // (Minecraft's model is in its own space, y down from 24 px and the head toward -z; here feet at 0, head toward +z)
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 10, 0), new Vector3(10, 16, 8), 28, 8, k, sharedMat);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // (it lies along the pig, as Minecraft turns it)
            EntityModels.Part(parent, "Head", new Vector3(0, 12, 10), new Vector3(8, 8, 8), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Snout", new Vector3(0, 10.5f, 14.5f), new Vector3(4, 3, 1), 16, 16, k, sharedMat);
            Legs(parent, k, sharedMat, 6f, 3f, 5f, -7f, new Vector3(4, 6, 4), 0, 16);
        }

        /// <summary>Four legs on their hips (front LegL/LegR, back LegBL/LegBR), `hip` px up, `x` px either side.</summary>
        public static void Legs(Transform parent, float k, Material mat, float hip, float x, float front, float back, Vector3 size, int u, int v)
        {
            foreach (var (n, px, pz) in new[] { ("LegL", x, front), ("LegR", -x, front), ("LegBL", x, back), ("LegBR", -x, back) })
            {
                var h = new GameObject(n).transform;
                h.SetParent(parent, false);
                h.localPosition = new Vector3(px, hip, pz) * k;
                EntityModels.Part(h, "Leg", new Vector3(0, -size.y / 2f, 0), size, u, v, k, mat);
            }
        }

        /// <summary>The first of these textures your Minecraft has (its names changed in 1.21.5).</summary>
        public static string Tex(params string[] names)
        {
            foreach (var n in names) if (McAssets.LoadTexture(n) != null) return n;
            return names[0];
        }
    }

    public class CowAI : MinecraftMobAI
    {
        public const string Name = "Cow";
        public static EnemyType Type;
        protected override string Sound => "cow";
        protected override bool Passive => true;
        protected override string Tempt => "wheat";
        protected override float RoamSpeed => 1.2f;
        protected override float PanicSpeed => 4.2f;
        public static int Milked; // (dev/tests)

        // (#244) an empty bucket on a cow: a bucket of milk, Minecraft's (a calf too)
        public override string UseTip(string heldKey) => heldKey == "bucket" ? "Milk" : base.UseTip(heldKey);

        public override void ServerUse(GameNetcodeStuff.PlayerControllerB p, GrabbableObject held)
        {
            if (held is BucketItem b && !b.Full)
            {
                if (!GameModes.IsCreative(p.actualClientId)) b.ServerUseUp(p);
                Inventory.ServerSpawnFor(p.actualClientId, "milk_bucket", 1);
                BlockNet.ServerSound(transform.position + Vector3.up, "cow.milk", 0.8f, 1f);
                Milked++;
                return;
            }
            base.ServerUse(p, held);
        }

        /// <summary>Host: Minecraft's drops: 1-3 raw beef, 0-2 leather.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue("raw_beef", out var beef)) ModItems.ServerSpawnStack(beef, Random.Range(1, 4), at);
            int leather = Random.Range(0, 3);
            if (leather > 0 && ModItems.ByKey.TryGetValue("leather", out var l)) ModItems.ServerSpawnStack(l, leather, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.CowRarity.Value <= 0) return;
            Type = Register<CowAI>("Cow", Name, "Moo. Drops beef and leather", Plugin.CowRarity.Value, 2, 2.0f, 4, 1.2f, BuildModel, daytime: true, length: 1.6f);
            RegisterBaby<CowAI>(Type, "BabyCow", "Calf", 1, 1.0f, 1.2f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; });
        }

        // ------------------------------------------------------------------ the model: Minecraft's cow (64x64; 1.21.5's muzzle)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            string tex = PigAI.Tex("entity/cow/temperate_cow", "entity/cow/cow");
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Cow", tex, new Color(0.3f, 0.22f, 0.15f));
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 17, -1), new Vector3(12, 18, 10), 18, 4, k, sharedMat);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var udder = EntityModels.Part(parent, "Udder", new Vector3(0, 11.5f, -7), new Vector3(4, 6, 1), 52, 0, k, sharedMat);
            udder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            EntityModels.Part(parent, "Head", new Vector3(0, 20, 11), new Vector3(8, 8, 6), 0, 0, k, sharedMat);
            if (tex.EndsWith("temperate_cow")) EntityModels.Part(parent, "Muzzle", new Vector3(0, 17.5f, 14.5f), new Vector3(6, 3, 1), 1, 33, k, sharedMat);
            EntityModels.Part(parent, "HornR", new Vector3(-4.5f, 23.5f, 11.5f), new Vector3(1, 3, 1), 22, 0, k, sharedMat);
            EntityModels.Part(parent, "HornL", new Vector3(4.5f, 23.5f, 11.5f), new Vector3(1, 3, 1), 22, 0, k, sharedMat);
            PigAI.Legs(parent, k, sharedMat, 12f, 4f, 6f, -7f, new Vector3(4, 12, 4), 0, 16);
        }
    }
}
