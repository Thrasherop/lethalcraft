using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's spider (#59): inside the facility ([Mobs] SpiderRarity). Low, wide and fast, eight legs scuttling,
    /// red eyes glowing in the dark; it runs at the nearest player it sees and bites. Killed, it drops string: four
    /// make a block of white wool. Its model comes from your Minecraft textures (entity/spider/spider.png, its eyes from
    /// entity/spider_eyes.png).
    /// </summary>
    public class SpiderAI : MinecraftMobAI
    {
        public const string Name = "Spider";
        public static EnemyType Type;
        protected override string Sound => "spider";
        protected override float ChaseSpeed => 5.4f; // (faster than a walking player, 4.55 m/s)
        protected override float RoamSpeed => 2.2f;
        protected override int Damage => 15;

        // climbing (#182): Minecraft's spider climbs any wall it walks into (Spider.tick: climbing = horizontalCollision,
        // and a climbing mob rises 0.2 a tick, 4 blocks a second); when its path can't get there (the target up on
        // placed blocks, or walled off by them), it heads straight for the target (WallClimberNavigation) and goes up
        // what's in the way. Off the navmesh meanwhile: the agent keeps its place below and takes over again on the ground.
        public bool Climbing { get; private set; }
        public static int Climbs; // (dev/tests)
        float climbVy, climbSince, stuckFor;
        Vector3 stuckAt;

        readonly Transform[] legs = new Transform[8];
        readonly Quaternion[] legRest = new Quaternion[8];
        float phase;
        Vector3 last;

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            for (int i = 0; i < 8 && m != null; i++)
            {
                legs[i] = m.Find("Leg" + i);
                if (legs[i] != null) legRest[i] = legs[i].localRotation;
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            if (IsOwner) ClimbTick();
            // Minecraft's scuttle: the legs swing forward and back in two alternating sets, lifting as they go
            float speed = Vector3.Distance(transform.position, last) / Mathf.Max(Time.deltaTime, 1e-4f);
            last = transform.position;
            phase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 4f;
            float amt = Mathf.Clamp01(speed / 2f);
            for (int i = 0; i < 8; i++)
            {
                if (legs[i] == null) continue;
                float s = Mathf.Sin(phase + (i % 2 == 0 ? 0f : Mathf.PI) + (i / 2) * 0.5f);
                legs[i].localRotation = Quaternion.Euler(0f, s * 20f * amt, Mathf.Max(0f, s) * 12f * amt * (i < 4 ? 1f : -1f)) * legRest[i];
            }
        }

        // (what it stands on: what a player walks on, and blocks)
        static bool SolidBlock(Vector3 at) => BlockWorld.Instance != null && BlockWorld.Instance.IsSolidAt(Ground.KeyOf(Ground.CellOf(at)));

        /// <summary>The top of the block under these feet (the one they're in, or just below), or MinValue.</summary>
        static float BlockTopUnder(Vector3 feet)
        {
            var c = Ground.CellOf(feet - Vector3.up * 0.05f * BlockWorld.S);
            if (BlockWorld.Instance == null || !BlockWorld.Instance.IsSolidAt(Ground.KeyOf(c))) return float.MinValue;
            return (c.y + 1) * BlockWorld.S + Ground.GridYOffMetres;
        }

        static int Mask => (StartOfRound.Instance.allPlayersCollideWithMask & ~(1 << 3) & ~(1 << 19)) | (1 << BlockWorld.SolidLayer);
        static int Blocks => 1 << BlockWorld.SolidLayer; // (what it climbs: blocks; the game's own walls and pipes weren't made for it)

        /// <summary>Owner, every frame: start climbing when the walk can't get to the target, climb, and get back on the
        /// navmesh when it can walk again.</summary>
        void ClimbTick()
        {
            if (agent == null || !agent.enabled) return;
            var t = currentBehaviourStateIndex == Chase && targetPlayer != null && !targetPlayer.isPlayerDead ? targetPlayer : null;
            var pos = transform.position;
            if (!Climbing)
            {
                if (t == null) { stuckFor = 0f; return; }
                var to = t.transform.position - pos; float up = to.y; to.y = 0f;
                if (to.magnitude > 12f) { stuckFor = 0f; return; }
                // can't walk there: the target up above, or the walk not getting closer (walled off)
                if ((pos - stuckAt).sqrMagnitude > 0.04f) { stuckAt = pos; stuckFor = 0f; } else stuckFor += Time.deltaTime;
                // (the walk takes it to the foot of the blocks; the climb starts there, so it never sets off straight
                // across the facility's gaps and drops)
                bool above = up > 0.9f * BlockWorld.S && to.magnitude < 1.8f * BlockWorld.S
                    && Physics.Raycast(t.transform.position + Vector3.up * 0.2f, Vector3.down, 1.5f * BlockWorld.S, Blocks, QueryTriggerInteraction.Ignore); // (up on blocks)
                bool walled = stuckFor > 0.8f && to.magnitude > 1.2f
                    && Physics.SphereCast(pos + Vector3.up * 0.35f * BlockWorld.S, 0.25f * BlockWorld.S, to.normalized, out _, 0.8f * BlockWorld.S, Blocks, QueryTriggerInteraction.Ignore);
                if (!above && !walled) return;
                Climbing = true; Climbs++; climbVy = 0f; climbSince = Time.time;
                agent.updatePosition = false; agent.updateRotation = false;
            }
            float dt = Time.deltaTime, S = BlockWorld.S;
            Vector3 dir = Vector3.zero; float upTo = 0f;
            if (t != null)
            {
                var to = t.transform.position - pos; upTo = to.y; to.y = 0f;
                if (to.magnitude > 0.6f) dir = to.normalized;
            }
            else if (NavMesh.SamplePosition(pos, out var near, 8f * S, agent.areaMask))
            {
                // (nobody to go for now: back to where it can walk, off the edge if it must)
                var to = near.position - pos; to.y = 0f;
                if (to.magnitude > 0.3f) dir = to.normalized;
            }
            // a block in front (read off the block grid: a physics cast starting against or in a block misses it): up it;
            // none: ahead, along the top, or falling
            bool wall = dir != Vector3.zero && SolidBlock(pos + dir * 0.55f * S + Vector3.up * 0.1f * S);
            if (wall && Time.time - climbSince < 8f)
            {
                pos.y += 4f * S * dt; climbVy = 0f;
            }
            else
            {
                var next = pos + dir * ChaseSpeed * 0.7f * dt;
                if (!SolidBlock(next + Vector3.up * 0.1f * S)) pos = next; // (never into a block)
                float top = BlockTopUnder(pos);
                if (top > float.MinValue && pos.y - top < 0.25f * S && pos.y - top > -0.3f * S) { pos.y = top; climbVy = 0f; }
                else if (Physics.Raycast(pos + Vector3.up * 0.5f * S, Vector3.down, out var g, 0.5f * S + Mathf.Max(0.05f, -climbVy * dt), Mask, QueryTriggerInteraction.Ignore)
                         && (top == float.MinValue || g.point.y >= top - 0.01f))
                { pos.y = g.point.y; climbVy = 0f; }
                else { climbVy -= 20f * dt; pos.y += climbVy * dt; }
            }
            transform.position = pos;
            if (dir != Vector3.zero) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
            // back on its feet on the navmesh, with nothing above to go up to: walking again
            bool onGround = climbVy == 0f && !wall;
            NavMeshHit nh = default;
            bool back = onGround && (t == null || upTo < 0.5f * S || Time.time - climbSince > 12f)
                && NavMesh.SamplePosition(pos, out nh, 0.6f * S, agent.areaMask) && Mathf.Abs(nh.position.y - pos.y) < 0.4f * S;
            // (and if it's been at it a long while, stuck somewhere: put back on the nearest walkable ground)
            if (!back && Time.time - climbSince > 20f && NavMesh.SamplePosition(pos, out nh, 10f * S, agent.areaMask)) back = true;
            if (back)
            {
                agent.Warp(nh.position);
                agent.updatePosition = true; agent.updateRotation = true;
                Climbing = false; stuckFor = 0f; stuckAt = nh.position;
            }
        }

        /// <summary>Host: Minecraft's drops: 0-2 string.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("string", out var s)) ModItems.ServerSpawnStack(s, n, at);
            // (Minecraft's: a spider eye a third of the time)
            if (KilledByPlayer && Random.value < 0.33f && ModItems.ByKey.TryGetValue("spider_eye", out var eye)) ModItems.ServerSpawnStack(eye, 1, at);
        }

        static Material sharedMat, eyesMat;

        public static void Register()
        {
            if (Plugin.SpiderRarity.Value <= 0) return;
            Type = Register<SpiderAI>("Spider", Name, "Fast; drops string", Plugin.SpiderRarity.Value, 3, 1.1f, 3, 2.2f, BuildModel); // (#244: health 3, Minecraft's 16 over 5)
            SpiderJockeyAI.Register();
        }

        // ------------------------------------------------------------------ the model: Minecraft's spider, 64x32
        /// <summary>(#138: one in a hundred carries a skeleton)</summary>
        protected override EnemyType ReplaceWith() => Random.value < 0.01f ? SpiderJockeyAI.Type : null;

        public static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Spider", "entity/spider/spider", new Color(0.25f, 0.2f, 0.18f));
            // (Minecraft's numbers, feet at 0, facing +z): the head in front, a small neck, the big body behind
            EntityModels.Part(parent, "Head", new Vector3(0, 9, 7), new Vector3(8, 8, 8), 32, 4, k, sharedMat);
            EntityModels.Part(parent, "Neck", new Vector3(0, 9, 0), new Vector3(6, 6, 6), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Body", new Vector3(0, 9, -9), new Vector3(10, 8, 12), 0, 12, k, sharedMat);
            // the eyes glow: Minecraft's eye layer, on the head's face
            if (eyesMat == null) eyesMat = EyesMaterial();
            if (eyesMat != null) EntityModels.Part(parent, "Eyes", new Vector3(0, 9, 7.02f), new Vector3(8.04f, 8.04f, 8.04f), 32, 4, k, eyesMat);
            // eight legs from the neck's sides, spread from front to back and angled down to the ground
            float[] spread = { 45f, 22.5f, -22.5f, -45f };
            for (int i = 0; i < 8; i++)
            {
                bool right = i < 4; int j = i % 4;
                var hip = new GameObject("Leg" + i).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(right ? -4f : 4f, 9f, 1f - j) * k;
                // (pointing out sideways, turned towards the front or back, tilted down so the tip reaches the floor)
                float yaw = right ? spread[j] : -spread[j];
                float tilt = j == 0 || j == 3 ? 40f : 33f;
                hip.localRotation = Quaternion.Euler(0f, yaw, right ? tilt : -tilt);
                EntityModels.Part(hip, "Leg", new Vector3(right ? -7f : 7f, 0f, 0f), new Vector3(16, 2, 2), 18, 0, k, sharedMat);
            }
        }

        static Material EyesMaterial()
        {
            if (McAssets.LoadTexture("entity/spider_eyes") == null) return null;
            var m = EntityModels.Material("LMC_SpiderEyes", "entity/spider_eyes", new Color(0, 0, 0, 0));
            // (glowing: the texture's own red, much brighter than the ambient light the other parts get; see-through elsewhere)
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, ArmorModels.AmbientEV + 7f, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            return m;
        }
    }
}
