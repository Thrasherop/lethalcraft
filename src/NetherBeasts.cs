using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's zombified piglin (#146): in the Nether's crimson forests and fortresses ([Nether] NetherZombifiedPiglins),
    /// and what a pig becomes when lightning strikes it. Neutral: it leaves you alone until someone hits one, then every
    /// zombified piglin near it goes for whoever did, for a while. Drops rotten flesh, gold nuggets, now and then a gold
    /// ingot or its golden sword.
    /// </summary>
    public class ZombifiedPiglinAI : MinecraftMobAI
    {
        public static EnemyType Type;
        protected override string Sound => "zpiglin";
        protected override float ChaseSpeed => 4.8f;
        protected override float RoamSpeed => 1.6f;
        protected override int Damage => 30;
        protected override bool HatesLight => false;
        public const float AngerSeconds = 30f, AlertRange = 32f;
        /// <summary>Who it's angry at, until when (every machine: hits come to every machine).</summary>
        readonly Dictionary<ulong, float> angry = new Dictionary<ulong, float>();
        protected override bool Wants(PlayerControllerB p) => p != null && angry.TryGetValue(p.actualClientId, out var until) && until > Time.time;
        public bool AngryAtAnyone { get { foreach (var v in angry.Values) if (v > Time.time) return true; return false; } }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            // (one is hit: all of them near it turn on whoever did it, Minecraft's way)
            if (playerWhoHit != null)
                foreach (var e in RoundManager.Instance.SpawnedEnemies)
                    if (e is ZombifiedPiglinAI z && !z.isEnemyDead && Vector3.Distance(z.transform.position, transform.position) < AlertRange)
                    {
                        z.angry[playerWhoHit.actualClientId] = Time.time + AngerSeconds;
                        if (z != this && z.IsOwner && z.currentBehaviourStateIndex == Roam) { z.targetPlayer = playerWhoHit; z.agent.speed = ChaseSpeed; z.SwitchToBehaviourState(Chase); }
                    }
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        /// <summary>Host: Minecraft's drops: 0-1 rotten flesh, 0-1 gold nugget, a gold ingot 2.5%, its golden sword 8.5%.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("rotten_flesh", out var rf)) ModItems.ServerSpawnStack(rf, 1, at);
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("gold_nugget", out var gn)) ModItems.ServerSpawnStack(gn, 1, at);
            if (KilledByPlayer && Random.value < 0.025f && ModItems.ByKey.TryGetValue("gold_ingot", out var gi)) ModItems.ServerSpawnStack(gi, 1, at);
            ServerDropEquipment("golden_sword", 32, at); // (#244: Minecraft's rule: a player's kill, Looting, worn)
        }
        protected override int Equipment => 1;

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherZombifiedPiglins.Value) return;
            Type = Register<ZombifiedPiglinAI>("ZombifiedPiglin", "Zombified Piglin", "Neutral, until you hit one of them", 0, 4, 2.5f, 4, 1.6f, BuildModel);
        }

        static void BuildModel(Transform parent)
        {
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_ZombifiedPiglin", "entity/piglin/zombified_piglin", new Color(0.75f, 0.55f, 0.5f));
            PiglinAI.BuildPiglin(parent, sharedMat, "item_golden_sword", false);
        }
    }

    /// <summary>
    /// Minecraft's hoglin (#147): the crimson forest's beast ([Nether] NetherHoglins). Hostile: it charges and tosses you
    /// up into the air. Drops 2-4 raw porkchops and 0-1 leather.
    /// </summary>
    public class HoglinAI : MinecraftMobAI
    {
        public static EnemyType Type;
        protected override string Sound => "hoglin";
        protected override float ChaseSpeed => 4.8f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 30;
        protected override bool HatesLight => false;
        protected override void OnHitPlayer(PlayerControllerB p)
        {
            // (its tusks throw you up, Minecraft's)
            if (p != GameNetworkManager.Instance?.localPlayerController) return;
            var away = p.transform.position - transform.position; away.y = 0f;
            Knockback.Toss(p, away.normalized * 6f, 11f);
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue("raw_porkchop", out var pork)) ModItems.ServerSpawnStack(pork, Random.Range(2, 5), at); // (#244: 2-4, Minecraft's)
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("leather", out var l)) ModItems.ServerSpawnStack(l, 1, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherHoglins.Value) return;
            Type = Register<HoglinAI>("Hoglin", "Hoglin", "Charges; tosses you up", 0, 8, 2.0f, 3, 1.4f, BuildModel, length: 2.4f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's hoglin (128x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.9f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Hoglin", "entity/hoglin/hoglin", new Color(0.6f, 0.4f, 0.35f));
            // (Minecraft's: y down from 24, the head toward -z; here feet at 0, the head toward +z)
            EntityModels.Part(parent, "Body", new Vector3(0, 17, 0), new Vector3(16, 14, 26), 1, 1, k, sharedMat);
            EntityModels.Part(parent, "Mane", new Vector3(0, 26, 4.5f), new Vector3(0, 10, 19), 90, 33, k, sharedMat);
            var head = new GameObject("Head").transform; head.SetParent(parent, false);
            head.localPosition = new Vector3(0f, 22f, 12f) * k;
            head.localRotation = Quaternion.Euler(50f, 0f, 0f);
            EntityModels.Part(head, "Skull", new Vector3(0, 0, 9.5f), new Vector3(14, 6, 19), 61, 1, k, sharedMat);
            foreach (var (n, x, u, v, rot) in new[] { ("EarR", -6f, 1, 1, 40f), ("EarL", 6f, 1, 6, -40f) })
            {
                var ear = new GameObject(n).transform; ear.SetParent(head, false);
                ear.localPosition = new Vector3(x, 2f, 3f) * k; ear.localRotation = Quaternion.Euler(0f, 0f, rot);
                EntityModels.Part(ear, "Ear", new Vector3(Mathf.Sign(x) * 3f, 0.5f, 0f), new Vector3(6, 1, 4), u, v, k, sharedMat);
            }
            foreach (var (n, x, u) in new[] { ("HornR", -7f, 10), ("HornL", 7f, 1) })
                EntityModels.Part(head, n, new Vector3(x, -2f + 5.5f, 12f), new Vector3(2, 11, 2), u, 13, k, sharedMat);
            foreach (var (n, x, y, z, w, h, u, v) in new[] { ("LegR", -4f, 14f, 8.5f, 6, 14, 66, 42), ("LegL", 4f, 14f, 8.5f, 6, 14, 41, 42),
                                                             ("LegBR", -5f, 11f, -10f, 5, 11, 21, 45), ("LegBL", 5f, 11f, -10f, 5, 11, 0, 45) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, y, z) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -h / 2f, 0), new Vector3(w, h, w), u, v, k, sharedMat);
            }
        }
    }

    /// <summary>Minecraft's endermite (#143): now and then where an ender pearl lands. Small, quick, hostile; endermen go for it.</summary>
    public class EndermiteAI : MinecraftMobAI
    {
        public static EnemyType Type;
        public static int Spawned; // (dev/tests)
        public static float DevChance = -1f; // (dev/tests: the chance a pearl brings one; -1 = Minecraft's 5%)
        protected override string Sound => "endermite";
        protected override float ChaseSpeed => 4.8f;
        protected override float RoamSpeed => 2.0f;
        protected override int Damage => 10;
        protected override bool HatesLight => false;
        protected override int Xp => 3;
        protected override void ServerDrops(Vector3 at) { }
        float bornAt = -1f;
        public static int Despawned; // (dev/tests)

        /// <summary>(#244) Minecraft's: two minutes and it's gone (unless it's named).</summary>
        public override void Update()
        {
            base.Update();
            if (bornAt < 0f) bornAt = Time.time;
            if (IsServer && !isEnemyDead && Time.time - bornAt > 120f && ShipMobs.NameOf(this) == null) { Despawned++; KillEnemyOnOwnerClient(overrideDestroy: true); }
        }

        public override void Start()
        {
            // (it appears wherever a pearl lands: outside, or in the facility far below)
            SetEnemyOutside(transform.position.y > -100f);
            base.Start();
        }

        /// <summary>Host: a pearl landed here (Minecraft: 5% of the time, an endermite).</summary>
        public static void ServerMaybeSpawn(Vector3 at)
        {
            if (Type == null || !BlockNet.IsServer || Random.value >= (DevChance >= 0f ? DevChance : 0.05f)) return;
            MobSpawns.DevSpawnUntil = Time.time + 1f;
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, Type);
            Spawned++;
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.Endermites.Value) return;
            Type = Register<EndermiteAI>("Endermite", "Endermite", "From ender pearls", 0, 2, 0.4f, 4, 2.0f, BuildModel, length: 0.6f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's endermite (64x32): four segments
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Endermite", "entity/endermite", new Color(0.2f, 0.12f, 0.25f));
            float z = 3.5f;
            foreach (var (w, h, d, v) in new[] { (4, 3, 2, 0), (6, 4, 5, 4), (3, 3, 1, 9), (1, 2, 1, 16) })
            {
                z -= d / 2f;
                EntityModels.Part(parent, "Seg" + v, new Vector3(0, h / 2f, z), new Vector3(w, h, d), 0, v, k, sharedMat);
                z -= d / 2f;
            }
        }
    }

    /// <summary>A shove for the local player (a hoglin's toss): up at once, and a push along for a moment. (Added before
    /// the player's own update, as Swiftness's push is: after it, the game has already used up and cleared the frame's.)</summary>
    [HarmonyLib.HarmonyPatch]
    public class Knockback : MonoBehaviour
    {
        Vector3 push; float left;
        bool tossed; float tossedAt; // (#234: until it lands again)
        public static int Tosses; // (dev/tests)
        public static void Toss(PlayerControllerB p, Vector3 horizontal, float up)
        {
            if (p == null) return;
            // (up the way a jump starts: the game pins a grounded player's fall speed every frame unless they're jumping)
            p.fallValue = up; p.fallValueUncapped = up; p.isFallingFromJump = true;
            var k = p.GetComponent<Knockback>() ?? p.gameObject.AddComponent<Knockback>();
            k.push = horizontal; k.left = 0.45f; k.tossed = true; k.tossedAt = Time.time;
            Tosses++;
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] toss {k.push} grounded={p.thisController.isGrounded}");
        }

        [HarmonyLib.HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyLib.HarmonyPrefix]
        static void Apply(PlayerControllerB __instance)
        {
            var k = __instance.GetComponent<Knockback>();
            if (k == null) return;
            // (#234) landed again: off the "jump" the toss started. The game clears isFallingFromJump only at the end of
            // its own jump coroutine, which a toss doesn't run; left set, no fall afterwards did damage until a real jump
            if (k.tossed && __instance.thisController.isGrounded && Time.time - k.tossedAt > 0.2f) { k.tossed = false; __instance.isFallingFromJump = false; }
            if (k.left <= 0f) return;
            k.left -= Time.deltaTime;
            __instance.externalForces += k.push * Mathf.Clamp01(k.left / 0.45f);
        }
    }
}
