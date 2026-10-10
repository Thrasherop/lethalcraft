using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's wither skeleton (#59): a rare monster inside the facility ([Mobs] WitherSkeletonRarity). It roams, goes
    /// for the nearest player it sees and hits with its stone sword. Lit up places keep it from spawning, like the creeper.
    /// Killed, it drops coal now and then, and very rarely a wither skeleton skull: a trophy that sells
    /// ([Mobs] WitherSkullChance). Its model comes from your Minecraft textures (entity/skeleton/wither_skeleton.png).
    /// </summary>
    public class WitherSkeletonAI : MinecraftMobAI
    {
        public const string Name = "Wither Skeleton";
        public static EnemyType Type;
        protected override string Sound => "wskel";
        protected override float ChaseSpeed => 3.4f;
        protected override float RoamSpeed => 2.0f;
        protected override int Damage => 25;

        /// <summary>Host: what it drops (Minecraft's: coal a third of the time, the skull rarely).</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.33f && ModItems.ByKey.TryGetValue("coal", out var coal)) ModItems.ServerSpawnStack(coal, 1, at);
            if (Random.value < Plugin.WitherSkullChance.Value) ModItems.ServerSpawnScrapItem(WitherSkull.Key, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.WitherSkeletonRarity.Value <= 0) return;
            Type = Register<WitherSkeletonAI>("WitherSkeleton", Name, "Stone sword; drops its skull rarely", Plugin.WitherSkeletonRarity.Value, 4, 2.3f, 2, 2.0f, BuildModel);
        }

        // ------------------------------------------------------------------ the model: Minecraft's wither skeleton, 64x32
        static void BuildModel(Transform parent)
        {
            // (Minecraft's skeleton, 1.2 times as big for the wither skeleton, then the mod's 0.8: about 2.5 m)
            float k = EntityModels.Px * BlockWorld.S * 0.8f * 1.15f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_WitherSkeleton", "entity/skeleton/wither_skeleton", new Color(0.18f, 0.18f, 0.2f));
            // head 8x8x8 at 0,0; body 8x12x4 at 16,16; arms 2x12x2 at 40,16; legs 2x12x2 at 0,16; feet at y 0, facing +z
            Biped(parent, k, sharedMat, 2, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16), arm => Sword(arm, k));
        }

        /// <summary>A stone sword in its right hand (the item's own extruded sprite).</summary>
        static void Sword(Transform arm, float k)
        {
            var go = new GameObject("Sword");
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 3f) * k;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, -45f);
            go.transform.localScale = Vector3.one * 0.9f;
            go.layer = 19;
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_stone_sword") ? "item_stone_sword" : "item_pickaxe");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            Atlas.NoDecals(mr);
        }
    }

    /// <summary>A wither skeleton skull (#59): the head of the wither skeleton, a rare trophy that sells.</summary>
    public class WitherSkull : GrabbableObject
    {
        public const string Key = "wither_skeleton_skull";
        void Awake() => SpawnFix.Clear(gameObject);

        /// <summary>Its model: the wither skeleton's head (a box from its texture).</summary>
        public static void BuildModel(GameObject model)
        {
            var mat = EntityModels.Material("LMC_WitherSkull", "entity/skeleton/wither_skeleton", new Color(0.18f, 0.18f, 0.2f));
            EntityModels.Part(model.transform, "Skull", Vector3.zero, new Vector3(8, 8, 8), 0, 0, 0.32f / 8f, mat, model.layer);
            var mf = model.GetComponent<MeshFilter>(); var mr = model.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false; // (the box above is the look)
        }
    }
}
