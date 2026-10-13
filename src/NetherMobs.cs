using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's ghast (#62): in soul sand valleys and basalt deltas. It drifts high up (as high as the room lets it),
    /// moaning; seeing a player it shrieks and spits a fireball that blows up where it lands (blocks too: Minecraft's
    /// power 1), every few seconds. Fragile: a couple of hits. Drops gunpowder. Model from entity/ghast/ghast.png
    /// (1.21.6's is twice the old resolution), drawn smaller than Minecraft's so it fits the Nether's rooms.
    /// </summary>
    public class GhastAI : MinecraftMobAI
    {
        public const string Name = "Ghast";
        public static EnemyType Type;
        protected override string Sound => "ghast";
        protected override float SightRange => 28f;
        protected override float ChaseSpeed => 2.6f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 0;
        public const float ShootRange = 28f, Size = 1.9f; // (its body's side, m)
        float nextShot = 4f, chargeAt = -1f, sway;
        readonly List<Transform> tentacles = new List<Transform>();
        Transform body;
        public static int Shots; // (dev/tests)

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) { body = m.Find("Body"); foreach (Transform t in m) if (t.name.StartsWith("Tentacle")) tentacles.Add(t); }
            if (agent != null) { agent.stoppingDistance = 9f; agent.baseOffset = 1f; }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer || targetPlayer == null || currentBehaviourStateIndex != Chase || chargeAt > 0f) return;
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (Time.time >= nextShot && d <= ShootRange && !Physics.Linecast(eye.position, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                chargeAt = Time.time + 1f; // (Minecraft's: a shriek, a second, then the fireball)
                BlockNet.ServerSound(transform.position + Vector3.up * Size, "ghast.charge", 1f, 1f);
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // tentacles sway; it floats as high as the room lets it (the agent walks the floor beneath)
            sway += Time.deltaTime * 2f;
            for (int i = 0; i < tentacles.Count; i++) tentacles[i].localRotation = Quaternion.Euler(12f * Mathf.Sin(sway + i * 0.7f), 0f, 8f * Mathf.Cos(sway * 0.8f + i));
            if (IsOwner && agent != null && agent.enabled)
            {
                float clear = Physics.Raycast(transform.position - Vector3.up * agent.baseOffset + Vector3.up * 0.2f, Vector3.up, out var h, 12f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) ? h.distance : 12f;
                float want = Mathf.Clamp(clear - (Size + 1.6f) - 0.3f, 0.2f, 4.5f);
                agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, want, Time.deltaTime * 1.5f);
                // (it faces whoever it's after)
                bool facing = currentBehaviourStateIndex == Chase && targetPlayer != null;
                agent.updateRotation = !facing;
                if (facing)
                {
                    var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 180f * Time.deltaTime);
                }
            }
            if (IsServer && chargeAt > 0f && Time.time >= chargeAt)
            {
                chargeAt = -1f; nextShot = Time.time + Random.Range(3f, 5f);
                if (targetPlayer != null && !targetPlayer.isPlayerDead)
                {
                    var from = transform.position + Vector3.up * (1.6f + Size * 0.5f) + transform.forward * (Size * 0.6f);
                    var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.5f;
                    BlockNet.ServerFireball(from, (to - from).normalized, big: true);
                    Shots++;
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) { } // (it doesn't touch: it shoots)

        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("gunpowder", out var g)) ModItems.ServerSpawnStack(g, n, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherGhasts.Value) return;
            // (only the Nether's spawning makes them)
            Type = Register<GhastAI>("Ghast", Name, "Spits exploding fireballs", 0, 3, Size + 1.6f, 3, 1.4f, BuildModel, length: Size);
        }

        // ------------------------------------------------------------------ the model: Minecraft's ghast (a 64x32 layout at 2x)
        static void BuildModel(Transform parent)
        {
            float k = Size / 16f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Ghast", "entity/ghast/ghast", new Color(0.95f, 0.95f, 0.95f));
            float tex = sharedMat.mainTexture != null && sharedMat.mainTexture.width >= 128 ? 2f : 1f;
            // body 16x16x16 at (0,0), tentacles 2xNx2 at (0,0), nine in a staggered 3x3 under it
            EntityModels.Part(parent, "Body", new Vector3(0, 14 + 8, 0) * (16f / 16f), new Vector3(16, 16, 16), 0, 0, k, sharedMat, texScale: tex);
            var rng = new System.Random(1847);
            for (int i = 0; i < 9; i++)
            {
                float x = ((i % 3 - (i / 3 % 2) * 0.5f + 0.25f) / 2f * 2f - 1f) * 5f, z = ((i / 3) / 2f * 2f - 1f) * 5f;
                int len = rng.Next(7) + 8;
                var t = new GameObject("Tentacle" + i).transform;
                t.SetParent(parent, false);
                t.localPosition = new Vector3(x, 14.2f, z) * k;
                EntityModels.Part(t, "T", new Vector3(0, -len / 2f, 0), new Vector3(2, len, 2), 0, 0, k, sharedMat, texScale: tex);
            }
        }
    }

    /// <summary>
    /// Minecraft's magma cube (#62): in basalt deltas. It hops after you, hits on contact (harder the bigger it is), and
    /// a big one splits into two to four medium ones when killed, each of those into small ones. Small ones drop magma
    /// cream now and then. Model from entity/slime/magmacube.png: eight slices that pull apart as it jumps, glowing core.
    /// </summary>
    public class MagmaCubeAI : MinecraftMobAI
    {
        public static EnemyType Big, Medium, Small;
        protected virtual int CubeSize => 4;
        /// <summary>What it splits into, and what the smallest drops (a slime shares all this: #133).</summary>
        protected virtual EnemyType Smaller => CubeSize == 4 ? Medium : Small;
        protected virtual string SmallDrop => "magma_cream";
        protected virtual int SmallDropCount => Random.value < 0.25f ? 1 : 0;
        protected override string Sound => "magma";
        protected override float ChaseSpeed => 4.0f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => CubeSize == 4 ? 30 : CubeSize == 2 ? 20 : 10;
        float hopPhase;
        readonly Transform[] slices = new Transform[8];

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) for (int i = 0; i < 8; i++) slices[i] = m.Find("Slice" + i);
            hopPhase = Random.value;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // hops: it moves in bounds (a burst of speed, then a stop), and stretches as it leaves the ground
            bool chasing = currentBehaviourStateIndex == Chase;
            hopPhase += Time.deltaTime / (chasing ? 0.75f : 1.4f);
            float f = hopPhase % 1f;
            bool air = f < 0.45f;
            if (IsOwner && agent != null) agent.speed = air ? (chasing ? ChaseSpeed : RoamSpeed) * 2f : 0.05f;
            float up = air ? Mathf.Sin(f / 0.45f * Mathf.PI) : 0f;
            float k = EntityModels.Px * BlockWorld.S * CubeSize * 0.5f;
            var model = transform.Find("Model");
            if (model != null) model.localPosition = Vector3.up * up * 0.5f * CubeSize * 0.35f;
            for (int i = 0; i < 8; i++)
                if (slices[i] != null) slices[i].localPosition = new Vector3(0f, (7.5f - i) * k * (1f + up * 0.6f), 0f);
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (CubeSize > 1)
            {
                // it splits: two to four of the next size down
                var next = Smaller;
                int n = Random.Range(2, 5);
                MobSpawns.DevSpawnUntil = Time.time + 1f; // (its halves aren't a natural spawn: the tests' "spawns off" lets them be)
                for (int i = 0; i < n && next != null; i++)
                {
                    var p = at + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                    if (NavMesh.SamplePosition(p, out var h, 2f, NavMesh.AllAreas)) p = h.position;
                    RoundManager.Instance.SpawnEnemyGameObject(p, Random.Range(0f, 360f), -1, next);
                }
                Splits += n;
                return;
            }
            int drop = SmallDropCount;
            if (drop > 0 && ModItems.ByKey.TryGetValue(SmallDrop, out var mc)) ModItems.ServerSpawnStack(mc, drop, at);
        }

        public static int Splits; // (dev/tests)
        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherMagmaCubes.Value) return;
            Big = Register<MagmaCubeAI>("MagmaCube", "Magma Cube", "Splits when killed", 0, 4, 4 * 0.7f, 4, 1.4f, b => BuildModel(b, 4), length: 4 * 0.7f);
            Medium = Register<MagmaCubeMediumAI>("MagmaCubeMedium", "Magma Cube", "Splits when killed", 0, 2, 2 * 0.7f, 8, 1.4f, b => BuildModel(b, 2), length: 2 * 0.7f);
            Small = Register<MagmaCubeSmallAI>("MagmaCubeSmall", "Magma Cube", "Small", 0, 1, 0.7f, 16, 1.4f, b => BuildModel(b, 1), length: 0.7f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's magma cube (64x64)
        static void BuildModel(Transform parent, int size)
        {
            float k = EntityModels.Px * BlockWorld.S * size * 0.5f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_MagmaCube", "entity/slime/magmacube", new Color(0.35f, 0.08f, 0.02f));
            for (int i = 0; i < 8; i++)
            {
                int u = i < 4 ? 0 : 32, v = (i < 4 ? i : i - 4) * 9;
                EntityModels.Part(parent, "Slice" + i, new Vector3(0, 7.5f - i, 0), new Vector3(8, 1, 8), u, v, k, sharedMat);
            }
            EntityModels.Part(parent, "Core", new Vector3(0, 4, 0), new Vector3(4, 4, 4), 24, 40, k, sharedMat);
        }
    }

    public class MagmaCubeMediumAI : MagmaCubeAI { protected override int CubeSize => 2; }
    public class MagmaCubeSmallAI : MagmaCubeAI { protected override int CubeSize => 1; }
}
