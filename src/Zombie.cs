using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's zombie (#59): inside the facility ([Mobs] ZombieRarity), slow, arms out in front, groaning. It goes for
    /// the nearest player it sees and hits whoever it walks into. Killed, it drops rotten flesh (food, barely), and now
    /// and then Minecraft's rare drop: an iron ingot, a carrot or a potato. Its model comes from your Minecraft textures
    /// (entity/zombie/zombie.png).
    /// </summary>
    public class ZombieAI : MinecraftMobAI
    {
        public const string Name = "Zombie";
        public static EnemyType Type;
        protected override string Sound => "zombie";
        protected override float ChaseSpeed => 2.8f; // (a walking player gets away; a sprinting one easily)
        protected override float RoamSpeed => 1.6f;
        protected override int Damage => 20;
        protected override bool ArmsForward => true;

        /// <summary>Host: Minecraft's drops: 0-2 rotten flesh; one time in 40, an iron ingot, a carrot or a potato.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int flesh = Random.Range(0, 3);
            if (flesh > 0 && ModItems.ByKey.TryGetValue("rotten_flesh", out var rf)) ModItems.ServerSpawnStack(rf, flesh, at);
            if (Random.value < 0.025f && ModItems.ByKey.TryGetValue(new[] { "iron_ingot", "carrot", "potato" }[Random.Range(0, 3)], out var rare)) ModItems.ServerSpawnStack(rare, 1, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0) return;
            Type = Register<ZombieAI>("Zombie", Name, "Slow; drops rotten flesh", Plugin.ZombieRarity.Value, 4, 2.2f, 3, 1.6f, BuildModel);
        }

        // ------------------------------------------------------------------ the model: Minecraft's zombie, 64x64
        static void BuildModel(Transform parent)
        {
            // (Minecraft's player-sized biped at the mod's 0.8: about 2.2 m)
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Zombie", "entity/zombie/zombie", new Color(0.25f, 0.45f, 0.3f));
            // head (0,0), body (16,16), arms (40,16), legs (0,16): Minecraft's zombie draws its left limbs from the right ones
            // (its texture leaves the player model's left-limb patches empty)
            Biped(parent, k, sharedMat, 4, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16));
        }
    }
}
