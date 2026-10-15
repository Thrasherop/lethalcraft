using System.Collections.Generic;
using HarmonyLib;

namespace LethalMinecraft
{
    /// <summary>
    /// Spawn weights by moon: some mobs are far more common on the moons that suit them (#144: slimes on swampy March;
    /// the biome variants use it too). Each landing, the host sets this mod's mobs' weights on the moon's lists: the
    /// registered weight times the moon's multiplier (or the registered weight where no rule matches).
    /// </summary>
    [HarmonyPatch]
    public static class MoonSpawns
    {
        /// <summary>(enemy type, a word in the moon's name, multiplier)</summary>
        static readonly List<(EnemyType type, string moon, float mult)> rules = new List<(EnemyType, string, float)>();
        /// <summary>The weight each (moon, list, type) had before any rule touched it.</summary>
        static readonly Dictionary<(SelectableLevel, int, EnemyType), int> baseRarity = new Dictionary<(SelectableLevel, int, EnemyType), int>();

        public static void Boost(EnemyType type, string moon, float mult)
        {
            if (type != null && mult >= 0f) rules.Add((type, moon, mult));
        }

        /// <summary>The multiplier for a type on a moon (1 where no rule says otherwise).</summary>
        public static float MultiplierFor(EnemyType type, SelectableLevel level)
        {
            float m = 1f;
            if (level == null) return m;
            foreach (var r in rules)
                if (r.type == type && level.PlanetName != null && level.PlanetName.IndexOf(r.moon, System.StringComparison.OrdinalIgnoreCase) >= 0) m *= r.mult;
            return m;
        }

        [HarmonyPatch(typeof(RoundManager), nameof(RoundManager.LoadNewLevel)), HarmonyPrefix]
        static void Apply(SelectableLevel newLevel) => ApplyTo(newLevel);

        public static void ApplyTo(SelectableLevel level)
        {
            if (level == null || rules.Count == 0) return;
            var lists = new[] { level.Enemies, level.OutsideEnemies, level.DaytimeEnemies };
            for (int li = 0; li < lists.Length; li++)
            {
                if (lists[li] == null) continue;
                foreach (var e in lists[li])
                {
                    if (e?.enemyType == null || !rules.Exists(r => r.type == e.enemyType)) continue;
                    var key = (level, li, e.enemyType);
                    if (!baseRarity.TryGetValue(key, out int b)) baseRarity[key] = b = e.rarity;
                    e.rarity = UnityEngine.Mathf.RoundToInt(b * MultiplierFor(e.enemyType, level));
                }
            }
        }
    }
}
