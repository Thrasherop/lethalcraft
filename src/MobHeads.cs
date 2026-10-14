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

        /// <summary>
        /// Minecraft's head model for a head (#197), its boxes in model pixels (y down, the face towards -z): the skull's
        /// 8 px box; the zombie's hat layer over it (createHumanoidHeadLayer); the piglin's 10 px head with its snout, tusks
        /// and ears (PiglinModel.addHead: the ears hang from 4.5 px out, tipped 30 degrees).
        /// </summary>
        public static List<(int u, int v, Vector3 from, Vector3 size, float grow, Matrix4x4 pose)> ModelBoxes(string key)
        {
            var id = Matrix4x4.identity;
            var l = new List<(int, int, Vector3, Vector3, float, Matrix4x4)>();
            if (key == "piglin_head")
            {
                l.Add((0, 0, new Vector3(-5, -8, -4), new Vector3(10, 8, 8), 0f, id));
                l.Add((31, 1, new Vector3(-2, -4, -5), new Vector3(4, 4, 1), 0f, id));
                l.Add((2, 4, new Vector3(2, -2, -5), new Vector3(1, 2, 1), 0f, id));
                l.Add((2, 0, new Vector3(-3, -2, -5), new Vector3(1, 2, 1), 0f, id));
                l.Add((51, 6, new Vector3(0, 0, -2), new Vector3(1, 5, 4), 0f, Matrix4x4.TRS(new Vector3(4.5f, -6f, 0f), Quaternion.Euler(0f, 0f, -30f), Vector3.one)));
                l.Add((39, 6, new Vector3(-1, 0, -2), new Vector3(1, 5, 4), 0f, Matrix4x4.TRS(new Vector3(-4.5f, -6f, 0f), Quaternion.Euler(0f, 0f, 30f), Vector3.one)));
                return l;
            }
            l.Add((0, 0, new Vector3(-4, -8, -4), new Vector3(8, 8, 8), 0f, id));
            if (key == "zombie_head") l.Add((32, 0, new Vector3(-4, -8, -4), new Vector3(8, 8, 8), 0.25f, id));
            return l;
        }

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
            if (key == "piglin_head")
            {
                // (#197: its snout, tusks and ears, as on the piglin itself: Piglin.BuildPiglin, the head's middle 28 px up)
                EntityModels.Part(root.transform, "Snout", new Vector3(0, -2, 4.5f), new Vector3(4, 4, 1), 31, 1, k, m);
                EntityModels.Part(root.transform, "TuskR", new Vector3(-2.5f, -3, 4.5f), new Vector3(1, 2, 1), 2, 0, k, m);
                EntityModels.Part(root.transform, "TuskL", new Vector3(2.5f, -3, 4.5f), new Vector3(1, 2, 1), 2, 4, k, m);
                foreach (var (n, x, u, ang) in new[] { ("EarL", 4.5f, 51, 30f), ("EarR", -4.5f, 39, -30f) })
                {
                    var pivot = new GameObject(n).transform;
                    pivot.SetParent(root.transform, false);
                    pivot.localPosition = new Vector3(x, 2f, 0f) * k;
                    pivot.localRotation = Quaternion.Euler(0f, 0f, ang);
                    EntityModels.Part(pivot, "Ear", new Vector3(Mathf.Sign(x) * 0.5f, -2.5f, 0f), new Vector3(1, 5, 4), u, 6, k, m);
                }
            }
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
