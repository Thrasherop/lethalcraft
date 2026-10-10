using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's wither skeleton (#59): a rare monster inside the facility ([Mobs] WitherSkeletonRarity). It roams, goes
    /// for the nearest player it sees and hits with its stone sword. Lit up places keep it from spawning, like the creeper.
    /// Killed, it drops coal now and then, and very rarely a wither skeleton skull: a trophy that sells
    /// ([Mobs] WitherSkullChance). Its model comes from your Minecraft textures (entity/skeleton/wither_skeleton.png).
    /// </summary>
    public class WitherSkeletonAI : EnemyAI
    {
        public const int Roam = 0, Chase = 1;
        public const string Name = "Wither Skeleton";
        public static EnemyType Type;
        public static float SightRange = 20f, ChaseSpeed = 3.4f, RoamSpeed = 2.0f;
        public static int Damage = 25;

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
            if (!IsServer) return;
            if (MobSpawns.DevNoNatural && Time.time > MobSpawns.DevSpawnUntil) { KillEnemyOnOwnerClient(overrideDestroy: true); return; }
            if (MobSpawns.Lit(transform.position, out string light))
            {
                Plugin.Log.LogInfo($"[mobs] a wither skeleton spawned {light}: removed");
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
                        var seen = CheckLineOfSightForClosestPlayer(180f, (int)SightRange, 6);
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
            Sounds.Play("wskel.hit", transform.position + Vector3.up, 0.8f, Random.Range(0.9f, 1.1f), 16f);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            hitCooldown -= Time.deltaTime;
            idleIn -= Time.deltaTime;
            if (idleIn <= 0f) { idleIn = Random.Range(5f, 11f); Sounds.Play("wskel.idle", transform.position + Vector3.up * 1.6f, 0.7f, Random.Range(0.9f, 1.1f), 20f); }
            // walking legs and arms, Minecraft's way; the sword arm swings down when it hits
            float speed = Vector3.Distance(transform.position, lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            lastPos = transform.position;
            walkPhase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 3f;
            float leg = Mathf.Clamp01(speed / 2f) * 35f * Mathf.Sin(walkPhase);
            if (legs[0] != null) legs[0].localRotation = Quaternion.Euler(leg, 0f, 0f);
            if (legs[1] != null) legs[1].localRotation = Quaternion.Euler(-leg, 0f, 0f);
            swing = Mathf.Max(0f, swing - Time.deltaTime * 3f);
            if (armR != null) armR.localRotation = Quaternion.Euler(-leg * 0.6f - 70f * Mathf.Sin(swing * Mathf.PI), 0f, 0f);
            if (armL != null) armL.localRotation = Quaternion.Euler(leg * 0.6f, 0f, 0f);
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.deltaTime * 3f);
            if (mat != null) mat.SetColor("_BaseColor", hurtFlash > 0f ? Color.Lerp(Color.white, Color.red, hurtFlash) : Color.white);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;
            hurtFlash = 1f;
            Sounds.Play("wskel.hurt", transform.position + Vector3.up * 1.6f, 0.8f, Random.Range(0.9f, 1.1f), 20f);
            enemyHP -= force;
            if (enemyHP <= 0)
            {
                Sounds.Play("wskel.death", transform.position + Vector3.up * 1.6f, 0.9f, 1f, 20f);
                if (IsServer) ServerDrops(transform.position + Vector3.up * 0.6f);
                if (IsOwner) KillEnemyOnOwnerClient(overrideDestroy: true);
            }
            else if (IsOwner && playerWhoHit != null && currentBehaviourStateIndex == Roam)
            {
                targetPlayer = playerWhoHit; agent.speed = ChaseSpeed; SwitchToBehaviourState(Chase);
            }
        }

        /// <summary>Host: what it drops (Minecraft's: coal a third of the time, the skull rarely).</summary>
        static void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.33f && ModItems.ByKey.TryGetValue("coal", out var coal)) ModItems.ServerSpawnStack(coal, 1, at);
            if (Random.value < Plugin.WitherSkullChance.Value) ModItems.ServerSpawnScrapItem(WitherSkull.Key, at);
        }

        // ------------------------------------------------------------------ building it (startup)
        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.WitherSkeletonRarity.Value <= 0) return;
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("LMC_WitherSkeleton");
            prefab.layer = 19;
            prefab.GetComponent<NetworkObject>().AutoObjectParentSync = false;
            var agent = prefab.AddComponent<NavMeshAgent>();
            agent.speed = RoamSpeed; agent.angularSpeed = 400f; agent.acceleration = 12f;
            agent.radius = 0.4f; agent.height = 2.3f; agent.stoppingDistance = 0.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(prefab.transform, false);
            modelGo.layer = 19;
            modelGo.AddComponent<Animator>();
            BuildModel(modelGo.transform);

            var col = new GameObject("Collision");
            col.transform.SetParent(prefab.transform, false);
            col.layer = 19; col.tag = "Enemy";
            var box = col.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(0.8f, 2.3f, 0.8f); box.center = new Vector3(0f, 1.15f, 0f);
            var rb = col.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var detect = col.AddComponent<EnemyAICollisionDetect>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(prefab.transform, false);
            eye.localPosition = new Vector3(0f, 2.1f, 0.2f);

            AudioSource Audio(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(prefab.transform, false); go.transform.localPosition = Vector3.up;
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.maxDistance = 25f;
                return a;
            }

            var scan = new GameObject("ScanNode");
            scan.transform.SetParent(prefab.transform, false);
            scan.transform.localPosition = Vector3.up * 1.4f;
            scan.layer = 22;
            var sc = scan.AddComponent<BoxCollider>(); sc.isTrigger = true; sc.size = Vector3.one * 0.6f;
            var sn = scan.AddComponent<ScanNodeProperties>();
            sn.headerText = Name; sn.subText = "Stone sword; drops its skull rarely"; sn.maxRange = 30; sn.minRange = 1;
            sn.requiresLineOfSight = true; sn.nodeType = 1; sn.creatureScanID = -1;

            var type = ScriptableObject.CreateInstance<EnemyType>();
            type.name = "LMC_WitherSkeletonType";
            type.enemyName = Name;
            type.enemyPrefab = prefab;
            type.isOutsideEnemy = false;
            type.isDaytimeEnemy = false;
            type.MaxCount = 2;
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
            Type = type;

            var ai = prefab.AddComponent<WitherSkeletonAI>();
            ai.enemyType = type;
            ai.agent = agent;
            ai.eye = eye;
            ai.creatureSFX = Audio("SFX");
            ai.creatureVoice = Audio("Voice");
            ai.creatureAnimator = modelGo.GetComponent<Animator>();
            ai.enemyHP = 4;
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

            LethalLib.Modules.Enemies.RegisterEnemy(type, Plugin.WitherSkeletonRarity.Value, LethalLib.Modules.Levels.LevelTypes.All,
                LethalLib.Modules.Enemies.SpawnType.Default, (TerminalNode)null, (TerminalKeyword)null);
            Plugin.Log.LogInfo($"Wither skeleton registered (inside, rarity {Plugin.WitherSkeletonRarity.Value})");
        }

        // ------------------------------------------------------------------ the model: Minecraft's wither skeleton, 64x32
        static void BuildModel(Transform parent)
        {
            // (Minecraft's skeleton, 1.2 times as big for the wither skeleton, then the mod's 0.8: about 2.5 m)
            float k = EntityModels.Px * BlockWorld.S * 0.8f * 1.15f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_WitherSkeleton", "entity/skeleton/wither_skeleton", new Color(0.18f, 0.18f, 0.2f));
            // head 8x8x8 at 0,0; body 8x12x4 at 16,16; arms 2x12x2 at 40,16; legs 2x12x2 at 0,16; feet at y 0, facing +z
            EntityModels.Part(parent, "Head", new Vector3(0, 28, 0), new Vector3(8, 8, 8), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 4), 16, 16, k, sharedMat);
            foreach (var (name, x) in new[] { ("ArmR", -5f), ("ArmL", 5f) })
            {
                var sh = new GameObject(name).transform;
                sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 23f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(0, -5, 0), new Vector3(2, 12, 2), 40, 16, k, sharedMat);
                if (name == "ArmR") Sword(sh, k);
            }
            foreach (var (name, x) in new[] { ("LegL", 2f), ("LegR", -2f) })
            {
                var hip = new GameObject(name).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 12f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -6, 0), new Vector3(2, 12, 2), 0, 16, k, sharedMat);
            }
        }

        /// <summary>A stone sword in its right hand (the item's own extruded sprite).</summary>
        static void Sword(Transform arm, float k)
        {
            var go = new GameObject("Sword");
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 3f) * k;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, -45f);
            go.transform.localScale = Vector3.one * 0.9f;
            go.layer = 19;
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_stone_sword") ? "item_stone_sword" : "item_pickaxe");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            Atlas.NoDecals(mr);
        }
    }

    /// <summary>A wither skeleton skull (#59): the head of the wither skeleton, a rare trophy that sells.</summary>
    public class WitherSkull : GrabbableObject
    {
        public const string Key = "wither_skeleton_skull";
        void Awake() => SpawnFix.Clear(gameObject);

        /// <summary>Its model: the wither skeleton's head (a box from its texture).</summary>
        public static void BuildModel(GameObject model)
        {
            var mat = EntityModels.Material("LMC_WitherSkull", "entity/skeleton/wither_skeleton", new Color(0.18f, 0.18f, 0.2f));
            EntityModels.Part(model.transform, "Skull", Vector3.zero, new Vector3(8, 8, 8), 0, 0, 0.32f / 8f, mat, model.layer);
            var mf = model.GetComponent<MeshFilter>(); var mr = model.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false; // (the box above is the look)
        }
    }
}
