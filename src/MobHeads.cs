using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Mob heads (#142): what a charged creeper's blast knocks off the mobs it kills, Minecraft's way: a skeleton skull, a
    /// zombie head, a creeper head, a piglin head. Worn in the helmet slot (like a carved pumpkin, no armor), and that mob
    /// notices you from only half as far.
    /// </summary>
    [HarmonyPatch]
    public static class MobHeads
    {
        public class Head { public string Key, Name, Texture; public int U, V, W; }
        public static readonly Dictionary<string, Head> All = new Dictionary<string, Head>
        {
            ["skeleton_skull"] = new Head { Key = "skeleton_skull", Name = "Skeleton Skull", Texture = "entity/skeleton/skeleton", U = 0, V = 0, W = 8 },
            ["zombie_head"] = new Head { Key = "zombie_head", Name = "Zombie Head", Texture = "entity/zombie/zombie", U = 0, V = 0, W = 8 },
            ["creeper_head"] = new Head { Key = "creeper_head", Name = "Creeper Head", Texture = "entity/creeper/creeper", U = 0, V = 0, W = 8 },
            ["piglin_head"] = new Head { Key = "piglin_head", Name = "Piglin Head", Texture = "entity/piglin/piglin", U = 0, V = 0, W = 10 },
        };
        public static bool IsHead(string key) => key != null && All.ContainsKey(key);

        /// <summary>Whether this player wears that head (the mob it's from sees them from half as far).</summary>
        public static bool Wears(PlayerControllerB p, string head)
        {
            if (p == null || head == null) return false;
            var worn = Armor.Of(p.actualClientId);
            return worn != null && worn.Length > 0 && ItemData.Base(worn[0]) == head;
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>The head on a player's head bone (ArmorModels): the mob's head, a size up.</summary>
        public static GameObject Build(string key, Transform bone, Vector3 center, float size)
        {
            if (!All.TryGetValue(key, out var h)) return null;
            if (!mats.TryGetValue(key, out var m)) mats[key] = m = EntityModels.Material("LMC_Head_" + key, h.Texture, Color.gray);
            var root = new GameObject("LMC_MobHead");
            root.transform.SetParent(bone, false);
            root.transform.localPosition = center;
            // (a block of 8 px, the size of the pumpkin on the head)
            float k = size / 8f;
            EntityModels.Part(root.transform, "Head", Vector3.zero, new Vector3(h.W, 8, 8), h.U, h.V, k, m);
            return root;
        }

        // ------------------------------------------------------------------ a charged creeper's blast: what it kills drops its head
        static Vector3 blastAt; static float blastUntil = -1f, blastRadius;
        public static void ServerChargedBlast(Vector3 at, float radius) { blastAt = at; blastRadius = radius * 1.3f; blastUntil = Time.time + 1.5f; }
        public static bool KilledByChargedBlast(Vector3 at) => Time.time < blastUntil && Vector3.Distance(at, blastAt) < blastRadius;
        public static void ServerDropHead(string key, Vector3 at)
        {
            if (key != null && ModItems.ByKey.TryGetValue(key, out var it)) ModItems.ServerSpawnPlain(it, at); // (worn like armor: one at a time)
        }

        // ------------------------------------------------------------------ lightning (every machine hears it)
        public static int Charged, PigsZombified; // (dev/tests)

        [HarmonyPatch(typeof(StormyWeather), nameof(StormyWeather.LightningStrike)), HarmonyPrefix]
        static void Struck(Vector3 strikePosition) => Strike(strikePosition);

        /// <summary>Lightning here: a creeper near it is charged (Minecraft's 4 blocks), a pig turns into a zombified piglin.</summary>
        public static void Strike(Vector3 at)
        {
            if (RoundManager.Instance == null) return;
            float r = 4f * BlockWorld.S;
            foreach (var e in RoundManager.Instance.SpawnedEnemies.ToArray())
            {
                if (e == null || e.isEnemyDead || Vector3.Distance(e.transform.position, at) > r) continue;
                if (e is CreeperAI c && !c.Charged) { c.Charge(); Charged++; }
                else if (e is PigAI && ZombifiedPiglinAI.Type != null && e.IsServer) { Variants.Replace(e, ZombifiedPiglinAI.Type); PigsZombified++; }
            }
        }
    }
}
