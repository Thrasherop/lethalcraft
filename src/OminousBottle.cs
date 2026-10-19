using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The ominous bottle (#223): the pillager captain's drop (Minecraft's entities/pillager loot: one, its Bad Omen
    /// amplifier 0-4). Drunk like a potion (hold the use button, 1.6 s), it gives Bad Omen at its level for 100 minutes
    /// (Minecraft's 120000 ticks), and the bottle's gone (no glass bottle back: Minecraft's ominous_bottle/dispose sound).
    /// The owner's: there are no villages, so Bad Omen does nothing yet; it's shown with the other effects. Each level is
    /// its own item (ominous_bottle_1 to _5), so they stack apart as Minecraft's do.
    /// </summary>
    public static class OminousBottle
    {
        public const string KeyPrefix = "ominous_bottle_";
        public const float Seconds = 6000f;
        public static string KeyFor(int level) => KeyPrefix + Mathf.Clamp(level, 1, 5);

        /// <summary>Its Bad Omen level (1-5), or 0: not an ominous bottle.</summary>
        public static int LevelOf(string key) => (key = ItemData.TypeOf(key)) != null && key.StartsWith(KeyPrefix) && int.TryParse(key.Substring(KeyPrefix.Length), out int l) && l >= 1 && l <= 5 ? l : 0;

        /// <summary>Host: a captain's: one, at a level from 1 to 5.</summary>
        public static void ServerDrop(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue(KeyFor(Random.Range(1, 6)), out var b)) ModItems.ServerSpawnStack(b, 1, at);
        }

        /// <summary>The local player drank one.</summary>
        public static void Drunk(string key)
        {
            int l = LevelOf(key);
            if (l <= 0) return;
            Potions.GiveEffect(Potions.Effect.BadOmen, l, Seconds);
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p != null) Sounds.Play("ominous.dispose", p.transform.position + Vector3.up, 0.8f, 1f, 12f);
        }
    }
}
