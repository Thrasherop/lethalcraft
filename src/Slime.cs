using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's slime (#133): an outside monster on the moons ([Mobs] SlimeRarity), at night like the game's own.
    /// It hops after you and hits on contact (a small one doesn't hurt); killed, a big one splits into two to four
    /// medium ones and those into small ones, and the small ones drop slimeballs. The magma cube's hopping and splitting,
    /// with Minecraft's slime model: a see-through green outer cube round a darker core, eyes and a mouth.
    /// </summary>
    public class SlimeAI : MagmaCubeAI
    {
        public static new EnemyType Big, Medium, Small;
        protected override string Sound => "slime";
        protected override float ChaseSpeed => 3.6f;
        // (less than Minecraft's 4 and 2 of 20: the game has no moment of safety after a hit, so a big one's split, two to
        // four medium ones all at once, took a full health bar in a couple of seconds)
        protected override int Damage => CubeSize == 4 ? 15 : CubeSize == 2 ? 5 : 0;
        protected override EnemyType Smaller => CubeSize == 4 ? Medium : Small;
        protected override string SmallDrop => "slime_ball";
        protected override int SmallDropCount => Random.Range(0, 3); // (Minecraft's: 0-2)

        static Material outer, inner;

        public static new void Register()
        {
            if (Plugin.SlimeRarity.Value <= 0) return;
            Big = Register<SlimeAI>("Slime", "Slime", "Splits when killed", Plugin.SlimeRarity.Value, 4, 4 * 0.7f, 3, 1.4f, b => BuildModel(b, 4), length: 4 * 0.7f, outside: true);
            // (the smaller ones only come from splitting)
            Medium = Register<SlimeMediumAI>("SlimeMedium", "Slime", "Splits when killed", 0, 2, 2 * 0.7f, 8, 1.4f, b => BuildModel(b, 2), length: 2 * 0.7f, outside: true);
            Small = Register<SlimeSmallAI>("SlimeSmall", "Slime", "Small", 0, 1, 0.7f, 16, 1.4f, b => BuildModel(b, 1), length: 0.7f, outside: true);
        }

        // ------------------------------------------------------------------ the model: Minecraft's slime (64x32)
        static void BuildModel(Transform parent, int size)
        {
            float k = EntityModels.Px * BlockWorld.S * size * 0.5f;
            if (inner == null) inner = EntityModels.Material("LMC_Slime", "entity/slime/slime", new Color(0.35f, 0.7f, 0.3f));
            if (outer == null)
            {
                // (the outer cube: alpha blended, as Minecraft draws it)
                outer = EntityModels.Material("LMC_SlimeOuter", "entity/slime/slime", new Color(0.4f, 0.8f, 0.35f, 0.6f), doubleSided: false);
                outer.SetFloat("_AlphaCutoffEnable", 0f);
                HDMaterial.SetSurfaceType(outer, true);
                outer.SetFloat("_SurfaceType", 1f);
                outer.SetFloat("_BlendMode", 0f);
                HDMaterial.ValidateMaterial(outer);
                outer.renderQueue = 3000;
            }
            // (Minecraft's: y down from 24 px; here feet at 0)
            EntityModels.Part(parent, "Core", new Vector3(0, 4, 0), new Vector3(6, 6, 6), 0, 16, k, inner);
            EntityModels.Part(parent, "EyeR", new Vector3(-2.25f, 5, 2.5f), new Vector3(2, 2, 2), 32, 0, k, inner);
            EntityModels.Part(parent, "EyeL", new Vector3(2.25f, 5, 2.5f), new Vector3(2, 2, 2), 32, 4, k, inner);
            EntityModels.Part(parent, "Mouth", new Vector3(0.5f, 2.5f, 3f), new Vector3(1, 1, 1), 32, 8, k, inner);
            EntityModels.Part(parent, "Outer", new Vector3(0, 4, 0), new Vector3(8, 8, 8), 0, 0, k, outer);
        }
    }

    public class SlimeMediumAI : SlimeAI { protected override int CubeSize => 2; }
    public class SlimeSmallAI : SlimeAI { protected override int CubeSize => 1; }
}
