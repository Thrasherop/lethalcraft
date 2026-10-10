using System;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// A Minecraft melee mob inside the facility (#59): the wither skeleton, the zombie. It roams, goes for the nearest
    /// player it sees and hits whoever it walks into; hit, it flashes red and turns on whoever hit it. Lit up places keep
    /// it from spawning, like the creeper. Each kind gives its numbers, sounds ("&lt;Sound&gt;.idle/.hurt/.death/.hit"),
    /// drops and model; the AI, the walk and the swing are shared.
    /// </summary>
    public abstract class MinecraftMobAI : EnemyAI
    {
        public const int Roam = 0, Chase = 1;
        protected abstract string Sound { get; }
        protected virtual float SightRange => 20f;
        protected virtual float ChaseSpeed => 3.4f;
        protected virtual float RoamSpeed => 2.0f;
        protected virtual int Damage => 25;
        /// <summary>Arms held out in front (Minecraft's zombie) instead of swinging at its sides.</summary>
        protected virtual bool ArmsForward => false;
        /// <summary>Host: what it drops when killed.</summary>
        protected abstract void ServerDrops(Vector3 at);

        Transform model, armR, armL;
        readonly Transform[] legs = new Transform[2];
        float walkPhase, swing, hitCooldown, idleIn = 6f, hurtFlash;
        Vector3 lastPos;
        Material mat;

        public override void Start()
        {
            base.Start();
            model = transform.Find("Model");
            if (model != null) { armR = model.Find("ArmR"); armL = model.Find("ArmL"); legs[0] = model.Find("LegL"); legs[1] = model.Find("LegR"); }
            var r = model != null ? model.GetComponentInChildren<MeshRenderer>() : null;
            if (r != null) { mat = new Material(r.sharedMaterial); foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) if (mr.sharedMaterial == r.sharedMaterial) mr.sharedMaterial = mat; }
            // in the Nether fortress: its own nodes to wander between (the game's are up in the facility)
            if (NetherFortress.Contains(transform.position) && NetherFortress.Nodes != null) allAINodes = NetherFortress.Nodes;
            if (!IsServer) return;
            if (MobSpawns.DevNoNatural && Time.time > MobSpawns.DevSpawnUntil) { KillEnemyOnOwnerClient(overrideDestroy: true); return; }
            if (MobSpawns.Lit(transform.position, out string light))
            {
                Plugin.Log.LogInfo($"[mobs] a {enemyType.enemyName} spawned {light}: removed");
                MobSpawns.Prevented++;
                KillEnemyOnOwnerClient(overrideDestroy: true);
                return;
            }
            agent.speed = RoamSpeed;
            StartSearch(transform.position);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;
            switch (currentBehaviourStateIndex)
            {
                case Roam:
                    {
                        // (Minecraft's mobs notice a player in any direction, given a clear line of sight)
                        var seen = CheckLineOfSightForClosestPlayer(360f, (int)SightRange, 6);
                        if (seen != null && PlayerIsTargetable(seen))
                        {
                            if (currentSearch.inProgress) StopSearch(currentSearch);
                            targetPlayer = seen; agent.speed = ChaseSpeed;
                            SwitchToBehaviourState(Chase);
                        }
                        else if (!currentSearch.inProgress) StartSearch(transform.position);
                        break;
                    }
                case Chase:
                    if (targetPlayer == null || !PlayerIsTargetable(targetPlayer) || Vector3.Distance(transform.position, targetPlayer.transform.position) > SightRange * 1.5f)
                    {
                        targetPlayer = null; movingTowardsTargetPlayer = false; agent.speed = RoamSpeed;
                        SwitchToBehaviourState(Roam);
                        StartSearch(transform.position);
                        break;
                    }
                    SetMovingTowardsTargetPlayer(targetPlayer);
                    break;
            }
        }

        /// <summary>Every client, for its own player: walked into, it hits (a second between swings).</summary>
        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (hitCooldown > 0f || isEnemyDead) return;
            var p = MeetsStandardPlayerCollisionConditions(other);
            if (p == null) return;
            hitCooldown = 1f;
            swing = 1f;
            p.DamagePlayer(Damage, true, true, CauseOfDeath.Bludgeoning);
            Sounds.Play(Sound + ".hit", transform.position + Vector3.up, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 16f);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            hitCooldown -= Time.deltaTime;
            idleIn -= Time.deltaTime;
            if (idleIn <= 0f) { idleIn = UnityEngine.Random.Range(5f, 11f); Sounds.Play(Sound + ".idle", transform.position + Vector3.up * 1.6f, 0.7f, UnityEngine.Random.Range(0.9f, 1.1f), 20f); }
            // walking legs and arms, Minecraft's way; the right arm swings down when it hits
            float speed = Vector3.Distance(transform.position, lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            lastPos = transform.position;
            walkPhase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 3f;
            float leg = Mathf.Clamp01(speed / 2f) * 35f * Mathf.Sin(walkPhase);
            if (legs[0] != null) legs[0].localRotation = Quaternion.Euler(leg, 0f, 0f);
            if (legs[1] != null) legs[1].localRotation = Quaternion.Euler(-leg, 0f, 0f);
            swing = Mathf.Max(0f, swing - Time.deltaTime * 3f);
            float s = 70f * Mathf.Sin(swing * Mathf.PI);
            if (ArmsForward)
            {
                // (Minecraft's zombie: both arms straight out, bobbing a little; a hit swings them down and back up)
                float bob = 4f * Mathf.Sin(Time.time * 2.5f);
                if (armR != null) armR.localRotation = Quaternion.Euler(-90f + bob + s * 0.6f, 0f, 0f);
                if (armL != null) armL.localRotation = Quaternion.Euler(-90f - bob + s * 0.6f, 0f, 0f);
            }
            else
            {
                if (armR != null) armR.localRotation = Quaternion.Euler(-leg * 0.6f - s, 0f, 0f);
                if (armL != null) armL.localRotation = Quaternion.Euler(leg * 0.6f, 0f, 0f);
            }
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.deltaTime * 3f);
            if (mat != null) mat.SetColor("_BaseColor", hurtFlash > 0f ? Color.Lerp(Color.white, Color.red, hurtFlash) : Color.white);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;
            hurtFlash = 1f;
            Sounds.Play(Sound + ".hurt", transform.position + Vector3.up * 1.6f, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 20f);
            enemyHP -= force;
            if (enemyHP <= 0)
            {
                Sounds.Play(Sound + ".death", transform.position + Vector3.up * 1.6f, 0.9f, 1f, 20f);
                if (IsServer) ServerDrops(transform.position + Vector3.up * 0.6f);
                if (IsOwner) KillEnemyOnOwnerClient(overrideDestroy: true);
            }
            else if (IsOwner && playerWhoHit != null && currentBehaviourStateIndex == Roam)
            {
                targetPlayer = playerWhoHit; agent.speed = ChaseSpeed; SwitchToBehaviourState(Chase);
            }
        }

        // ------------------------------------------------------------------ registering one (startup)
        /// <summary>
        /// A mob's network prefab and enemy type, registered with LethalLib to spawn inside at that rarity: the agent, a
        /// trigger box to hit and be hit, its eye, voice, scan node and its model (built by `model`, feet at 0, facing +z).
        /// </summary>
        public static EnemyType Register<T>(string id, string name, string scanText, int rarity, int hp, float height, int maxCount, float roamSpeed, Action<Transform> buildModel) where T : MinecraftMobAI
        {
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("LMC_" + id);
            prefab.layer = 19;
            prefab.GetComponent<NetworkObject>().AutoObjectParentSync = false;
            var agent = prefab.AddComponent<NavMeshAgent>();
            agent.speed = roamSpeed; agent.angularSpeed = 400f; agent.acceleration = 12f;
            agent.radius = 0.4f; agent.height = height; agent.stoppingDistance = 0.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(prefab.transform, false);
            modelGo.layer = 19;
            modelGo.AddComponent<Animator>();
            buildModel(modelGo.transform);

            var col = new GameObject("Collision");
            col.transform.SetParent(prefab.transform, false);
            col.layer = 19; col.tag = "Enemy";
            var box = col.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(0.8f, height, 0.8f); box.center = new Vector3(0f, height / 2f, 0f);
            var rb = col.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var detect = col.AddComponent<EnemyAICollisionDetect>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(prefab.transform, false);
            eye.localPosition = new Vector3(0f, height - 0.2f, 0.2f);

            AudioSource Audio(string n)
            {
                var go = new GameObject(n); go.transform.SetParent(prefab.transform, false); go.transform.localPosition = Vector3.up;
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.maxDistance = 25f;
                return a;
            }

            var scan = new GameObject("ScanNode");
            scan.transform.SetParent(prefab.transform, false);
            scan.transform.localPosition = Vector3.up * (height * 0.6f);
            scan.layer = 22;
            var sc = scan.AddComponent<BoxCollider>(); sc.isTrigger = true; sc.size = Vector3.one * 0.6f;
            var sn = scan.AddComponent<ScanNodeProperties>();
            sn.headerText = name; sn.subText = scanText; sn.maxRange = 30; sn.minRange = 1;
            sn.requiresLineOfSight = true; sn.nodeType = 1; sn.creatureScanID = -1;

            var type = ScriptableObject.CreateInstance<EnemyType>();
            type.name = "LMC_" + id + "Type";
            type.enemyName = name;
            type.enemyPrefab = prefab;
            type.isOutsideEnemy = false;
            type.isDaytimeEnemy = false;
            type.MaxCount = maxCount;
            type.PowerLevel = 1f;
            type.probabilityCurve = AnimationCurve.Constant(0f, 1f, 1f);
            type.numberSpawnedFalloff = AnimationCurve.Constant(0f, 1f, 1f);
            type.useNumberSpawnedFalloff = false;
            type.canDie = true;
            type.destroyOnDeath = true;
            type.canBeStunned = true;
            type.stunTimeMultiplier = 1f;
            type.doorSpeedMultiplier = 1f;
            type.canSeeThroughFog = false;
            type.disableAnimatorWhenFar = false;
            type.pushPlayerForce = 0.3f;
            type.pushPlayerDistance = 1.2f;
            type.miscAnimations = new MiscAnimation[0];
            type.audioClips = new AudioClip[0];

            var ai = prefab.AddComponent<T>();
            ai.enemyType = type;
            ai.agent = agent;
            ai.eye = eye;
            ai.creatureSFX = Audio("SFX");
            ai.creatureVoice = Audio("Voice");
            ai.creatureAnimator = modelGo.GetComponent<Animator>();
            ai.enemyHP = hp;
            ai.AIIntervalTime = 0.2f;
            ai.updatePositionThreshold = 0.2f;
            ai.syncMovementSpeed = 0.2f;
            ai.exitVentAnimationTime = 0f;
            ai.ventAnimationFinished = true;
            ai.enemyBehaviourStates = new[]
            {
                new EnemyBehaviourState { name = "Roam", parameterString = "roam" },
                new EnemyBehaviourState { name = "Chase", parameterString = "chase" },
            };
            detect.mainScript = ai;
            detect.canCollideWithEnemies = false;

            LethalLib.Modules.Enemies.RegisterEnemy(type, rarity, LethalLib.Modules.Levels.LevelTypes.All,
                LethalLib.Modules.Enemies.SpawnType.Default, (TerminalNode)null, (TerminalKeyword)null);
            Plugin.Log.LogInfo($"{name} registered (inside, rarity {rarity})");
            return type;
        }

        /// <summary>A humanoid's limbs on their joints (Minecraft's biped): arms at the shoulders (ArmR, ArmL), legs at the
        /// hips (LegL, LegR), each `limb` texture pixels thick; the texture patches as (u, v) per limb.</summary>
        public static void Biped(Transform parent, float k, Material mat, int limb, (int u, int v) head, (int u, int v) body,
                                 (int u, int v) armR, (int u, int v) armL, (int u, int v) legR, (int u, int v) legL, Action<Transform> inRightHand = null)
        {
            float half = 4f + limb / 2f; // (arm centres: beside the 8-wide body)
            EntityModels.Part(parent, "Head", new Vector3(0, 28, 0), new Vector3(8, 8, 8), head.u, head.v, k, mat);
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 4), body.u, body.v, k, mat);
            foreach (var (n, x, uv) in new[] { ("ArmR", -half, armR), ("ArmL", half, armL) })
            {
                var sh = new GameObject(n).transform;
                sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 23f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(0, -5, 0), new Vector3(limb, 12, limb), uv.u, uv.v, k, mat);
                if (n == "ArmR") inRightHand?.Invoke(sh);
            }
            foreach (var (n, x, uv) in new[] { ("LegL", 2f, legL), ("LegR", -2f, legR) }) // (Minecraft: the hips 2 px either side, whatever the legs' width)
            {
                var hip = new GameObject(n).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 12f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -6, 0), new Vector3(limb, 12, limb), uv.u, uv.v, k, mat);
            }
        }
    }
}
