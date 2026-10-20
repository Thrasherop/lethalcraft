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

        // (#244) Minecraft's: a zombie with its head under water 30 s becomes a drowned (a husk a zombie; babies stay
        // babies); it sinks, as Minecraft's zombies do, and walks the bottom
        protected override bool FloatsInWater => false;
        protected virtual EnemyType DrownsInto => this is BabyZombieAI ? DrownedAI.BabyType : DrownedAI.Type;
        float underFor;
        public static int Converted; // (dev/tests)
        public static float DevDrownSeconds = 30f;
        public override void Update()
        {
            base.Update();
            if (!IsServer || isEnemyDead || DrownsInto == null) return;
            var head = transform.position + Vector3.up * (agent != null ? agent.height * 0.85f : 1.6f);
            bool under = MoonWater.Depth(head) > 0.05f || (WaterSwim.FluidCellAt(head, out var hb) && hb.Data.Def == Blocks.Water);
            underFor = under ? underFor + Time.deltaTime : 0f;
            if (underFor < DevDrownSeconds) return;
            Converted++;
            BlockNet.ServerSound(transform.position + Vector3.up, "drowned.convert", 1f, 1f);
            Variants.Replace(this, DrownsInto);
        }
        protected override string Sound => "zombie";
        protected override float ChaseSpeed => 4.6f; // (#100: as fast as a walking player, 4.55 m/s; a sprinting one gets away)
        protected override float RoamSpeed => 1.6f;
        protected override int Damage => 20;
        protected override bool ArmsForward => true;
        protected override string HeadKey => "zombie_head";

        /// <summary>Host: Minecraft's drops: 0-2 rotten flesh; one time in 40, an iron ingot, a carrot or a potato.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int flesh = Random.Range(0, 3);
            if (flesh > 0 && ModItems.ByKey.TryGetValue("rotten_flesh", out var rf)) ModItems.ServerSpawnStack(rf, flesh, at);
            if (KilledByPlayer && Random.value < 0.025f && ModItems.ByKey.TryGetValue(new[] { "iron_ingot", "carrot", "potato" }[Random.Range(0, 3)], out var rare)) ModItems.ServerSpawnStack(rare, 1, at);
        }

        static readonly System.Collections.Generic.Dictionary<string, Material> mats = new System.Collections.Generic.Dictionary<string, Material>();

        /// <summary>(#136, #137: on a desert moon most zombies are husks, on a swampy one some are drowned; one in twenty is a baby)</summary>
        protected override EnemyType ReplaceWith()
        {
            if (Variants.Desert && Random.value < 0.8f) return HuskAI.Type;
            if (Variants.Swampy && Random.value < 0.4f) return DrownedAI.Type;
            return Random.value < 0.05f ? BabyZombieAI.BabyType : null;
        }

        public static void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0) return;
            Type = Register<ZombieAI>("Zombie", Name, "Slow; drops rotten flesh", Plugin.ZombieRarity.Value, 4, 2.5f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/zombie", new Color(0.25f, 0.45f, 0.3f)));
            // its variants: only what it turns into (Variants)
            HuskAI.Register(); DrownedAI.Register(); BabyZombieAI.Register(); ChickenJockeyAI.Register();
        }

        // ------------------------------------------------------------------ the model: Minecraft's zombie, 64x64 (and its variants')
        public static void BuildZombie(Transform parent, string texture, Color fallback, float scale = 1f, string overlay = null, bool baby = false)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.9f * scale; // (#100: as tall as a player, about 2.5 m; it was 0.8, 2.2 m)
            if (!mats.TryGetValue(texture, out var mat)) mats[texture] = mat = EntityModels.Material("LMC_" + texture.Replace('/', '_'), texture, fallback);
            // head (0,0), body (16,16), arms (40,16), legs (0,16): Minecraft's zombie draws its left limbs from the right ones
            // (its texture leaves the player model's left-limb patches empty)
            Biped(parent, k, mat, 4, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16));
            if (overlay != null && McAssets.LoadTexture(overlay) != null)
            {
                if (!mats.TryGetValue(overlay, out var om)) mats[overlay] = om = EntityModels.Material("LMC_" + overlay.Replace('/', '_'), overlay, new Color(0, 0, 0, 0));
                Variants.Overlay(parent, k, om, 4, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16));
            }
            if (baby) Variants.Babyish(parent, k);
        }
    }
}
