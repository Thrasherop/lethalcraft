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
        /// <summary>Its height in blocks where that matters: a gap under placed blocks lower than this stops it (#112).</summary>
        protected virtual float TallBlocks => 0f;
        public static int LowGapStops; // (dev/tests)
        public static bool DevLowGapsOff; // (dev: the check off, to compare)
        protected virtual float ChaseSpeed => 4.7f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected virtual float RoamSpeed => 2.0f;
        protected virtual int Damage => 25;
        /// <summary>Arms held out in front (Minecraft's zombie) instead of swinging at its sides.</summary>
        protected virtual bool ArmsForward => false;
        /// <summary>Host: what it drops when killed.</summary>
        protected abstract void ServerDrops(Vector3 at);
        /// <summary>Experience for whoever kills it (#139: Minecraft's, 5 for a monster, 1-3 for an animal).</summary>
        protected virtual int Xp => Passive ? UnityEngine.Random.Range(1, 4) : 5;
        /// <summary>Host, as it spawns naturally: another kind to become instead (a husk on a desert moon, a baby, a jockey:
        /// Variants), or null.</summary>
        protected virtual EnemyType ReplaceWith() => null;
        /// <summary>Every client, for its own player: just hit them (the husk's Hunger).</summary>
        protected virtual void OnHitPlayer(PlayerControllerB p) { }
        /// <summary>The head a charged creeper's blast knocks off it (#142), which, worn, halves how far it notices you.</summary>
        protected virtual string HeadKey => null;
        bool dropped;
        /// <summary>Whether it goes for this player at all (the piglin leaves those in gold armor alone).</summary>
        protected virtual bool Wants(PlayerControllerB p) => true;
        /// <summary>Owner, each AI step: true while it's doing something of its own (the piglin with gold); the roam and chase wait.</summary>
        protected virtual bool BusyAI() => false;
        /// <summary>A farm animal (#95): never goes for anyone; hit, it runs off for a few seconds. Lit places don't stop it spawning.</summary>
        protected virtual bool Passive => false;
        public bool IsPassive => Passive;
        public string SoundKey => Sound;
        /// <summary>A lit place stops it spawning (Minecraft's monsters); not animals or pets.</summary>
        protected virtual bool HatesLight => !Passive;
        protected virtual float PanicSpeed => 5f;
        /// <summary>Stuck against blocks, it gnaws through them (EnemiesBreakBlocks); farm animals and pets don't.</summary>
        public virtual bool GnawsBlocks => !Passive;
        /// <summary>What a passive animal follows when someone holds it (Minecraft's pig: a carrot; cow: wheat).</summary>
        protected virtual string Tempt => null;
        float panicUntil;
        PlayerControllerB panicFrom;

        Transform model;
        protected Transform armR, armL;
        readonly Transform[] legs = new Transform[4]; // (a four-legged animal's back legs: LegBL, LegBR)
        float walkPhase, swing, hitCooldown, idleIn = 6f, hurtFlash, stepNoiseIn;
        Vector3 lastPos;
        Material mat;

        public override void Start()
        {
            // (#101, #122: spawned from a runtime prefab it inherited HideAndDontSave, which hides it from the game's
            // FindObjectsOfType: the round's end never despawned it, and it came along to the next moon or the Company)
            SpawnFix.Clear(gameObject);
            base.Start();
            model = transform.Find("Model");
            if (model != null) { armR = model.Find("ArmR"); armL = model.Find("ArmL"); legs[0] = model.Find("LegL"); legs[1] = model.Find("LegR"); legs[2] = model.Find("LegBL"); legs[3] = model.Find("LegBR"); }
            var r = model != null ? model.GetComponentInChildren<MeshRenderer>() : null;
            if (r != null) { mat = new Material(r.sharedMaterial); foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) if (mr.sharedMaterial == r.sharedMaterial) mr.sharedMaterial = mat; }
            // in the Nether fortress: its own nodes to wander between (the game's are up in the facility)
            if (NetherFortress.Contains(transform.position) && NetherFortress.Nodes != null) allAINodes = NetherFortress.Nodes;
            if (!IsServer) return;
            if (MobSpawns.DevNoNatural && Time.time > MobSpawns.DevSpawnUntil) { KillEnemyOnOwnerClient(overrideDestroy: true); return; }
            if (HatesLight && MobSpawns.LightStops(transform.position, out string light))
            {
                Plugin.Log.LogInfo($"[mobs] a {enemyType.enemyName} spawned {light}: removed");
                MobSpawns.Prevented++;
                KillEnemyOnOwnerClient(overrideDestroy: true);
                return;
            }
            // (a natural spawn only: a dev-spawned mob stays what it was asked to be; none in the Nether)
            bool natural = Time.time > MobSpawns.DevSpawnUntil || Time.time <= MobSpawns.DevNaturalUntil;
            var other = natural && !NetherFortress.Contains(transform.position) ? ReplaceWith() : null;
            if (other != null && other != enemyType) { Variants.Replace(this, other); return; }
            agent.speed = RoamSpeed;
            StartSearch(transform.position);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;
            if (BusyAI()) return;
            if (Passive) { PassiveAI(); return; }
            switch (currentBehaviourStateIndex)
            {
                case Roam:
                    {
                        // (Minecraft's mobs notice a player in any direction, given a clear line of sight)
                        var seen = CheckLineOfSightForClosestPlayer(360f, (int)SightRange, 6);
                        // (wearing its head, it notices you from only half as far: Minecraft's)
                        if (seen != null && HeadKey != null && MobHeads.Wears(seen, HeadKey) && Vector3.Distance(seen.transform.position, transform.position) > SightRange * 0.5f) seen = null;
                        if (seen != null && PlayerIsTargetable(seen) && Wants(seen))
                        {
                            if (currentSearch.inProgress) StopSearch(currentSearch);
                            targetPlayer = seen; agent.speed = ChaseSpeed;
                            SwitchToBehaviourState(Chase);
                        }
                        else if (!currentSearch.inProgress) StartSearch(transform.position);
                        break;
                    }
                case Chase:
                    if (targetPlayer == null || !PlayerIsTargetable(targetPlayer) || !Wants(targetPlayer) || Vector3.Distance(transform.position, targetPlayer.transform.position) > SightRange * 1.5f)
                    {
                        targetPlayer = null; movingTowardsTargetPlayer = false; agent.speed = RoamSpeed;
                        SwitchToBehaviourState(Roam);
                        StartSearch(transform.position);
                        break;
                    }
                    SetMovingTowardsTargetPlayer(targetPlayer);
                    if (TallBlocks > 0f && !DevLowGapsOff) KeepOutOfLowGaps();
                    break;
            }
        }

        /// <summary>Owner: a farm animal's day: wander; run from whoever hit it; walk after someone holding its food.</summary>
        void PassiveAI()
        {
            if (Time.time < panicUntil)
            {
                if (!agent.hasPath || agent.remainingDistance < 1f)
                {
                    // (away from who hit it, somewhere it can walk to)
                    var away = transform.position - (panicFrom != null ? panicFrom.transform.position : transform.position - transform.forward);
                    away.y = 0f;
                    var to = transform.position + Quaternion.Euler(0f, UnityEngine.Random.Range(-50f, 50f), 0f) * away.normalized * 7f;
                    if (NavMesh.SamplePosition(to, out var h, 4f, agent.areaMask)) SetDestinationToPosition(h.position);
                }
                return;
            }
            if (agent.speed > RoamSpeed) agent.speed = RoamSpeed;
            var lure = Tempt != null ? Lured() : null;
            Following = lure;
            if (lure != null)
            {
                if (currentSearch.inProgress) StopSearch(currentSearch);
                // (follows to a couple of metres off, as Minecraft's do)
                if (Vector3.Distance(transform.position, lure.transform.position) > 2.2f) SetDestinationToPosition(lure.transform.position, checkForPath: true);
                else SetDestinationToPosition(transform.position);
                return;
            }
            if (!currentSearch.inProgress) StartSearch(transform.position);
        }

        /// <summary>(dev/tests) Who a passive animal is following for their food.</summary>
        public PlayerControllerB Following;
        public bool Panicking => Time.time < panicUntil;

        /// <summary>The nearest player within 8 m holding its food, if any.</summary>
        PlayerControllerB Lured()
        {
            PlayerControllerB best = null; float bd = 8f;
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead || !PlayerIsTargetable(p)) continue;
                if (!(p.currentlyHeldObjectServer is StackItem held) || held.ItemKey != Tempt) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// Host, chasing: a tall mob (an enderman, a wither skeleton) doesn't fit where placed blocks hang lower than it
        /// is tall (#112), Minecraft's two-high hiding hole. The navmesh doesn't know heights, so it walks the path to
        /// its target and stops at the last point it fits, short of the gap, until the way is clear.
        /// </summary>
        void KeepOutOfLowGaps()
        {
            if (agent == null || !agent.isOnNavMesh || targetPlayer == null) return;
            var path = new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(transform.position, targetPlayer.transform.position, agent.areaMask, path) || path.corners.Length < 2) return;
            float h = TallBlocks * BlockWorld.S, step = 0.35f, walked = 0f;
            var lastGood = transform.position;
            // (already under one: let it walk out; otherwise every step counts, or it creeps in a bit each time)
            bool under = LowCeiling(transform.position, h);
            for (int i = 1; i < path.corners.Length; i++)
            {
                var a = path.corners[i - 1]; var b = path.corners[i];
                float len = Vector3.Distance(a, b);
                for (float d = 0f; d < len; d += step)
                {
                    var q = Vector3.Lerp(a, b, d / Mathf.Max(len, 1e-4f));
                    walked += step;
                    // (under one already: let it walk out)
                    if ((!under || walked > 0.8f) && LowCeiling(q, h))
                    {
                        movingTowardsTargetPlayer = false;
                        SetDestinationToPosition(lastGood);
                        LowGapStops++;
                        return;
                    }
                    lastGood = q;
                }
            }
        }

        static bool LowCeiling(Vector3 floor, float h)
        {
            // (only placed blocks: the facility's own doorways are what they are)
            return Physics.Raycast(floor + Vector3.up * 0.25f, Vector3.up, out var hit, h - 0.25f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<BlockRef>() != null;
        }

        /// <summary>Every client, for its own player: walked into, it hits (a second between swings).</summary>
        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (hitCooldown > 0f || isEnemyDead) return;
            var p = MeetsStandardPlayerCollisionConditions(other);
            if (p == null) return;
            // only when it's after someone (#110: a calm enderman or piglin hurt whoever bumped into it). The state is
            // the same on every machine; who it's angry with is the host's, so the host checks that too.
            if (currentBehaviourStateIndex != Chase) return;
            if (IsServer && !Wants(p)) return;
            if (Damage <= 0) return; // (a small slime, a witch: no hit up close)
            hitCooldown = 1f;
            swing = 1f;
            p.DamagePlayer(Damage, true, true, CauseOfDeath.Bludgeoning);
            OnHitPlayer(p);
            Sounds.Play(Sound + ".hit", transform.position + Vector3.up, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 16f);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            hitCooldown -= Time.deltaTime;
            idleIn -= Time.deltaTime;
            if (idleIn <= 0f)
            {
                idleIn = UnityEngine.Random.Range(5f, 11f);
                Sounds.Play(Sound + ".idle", transform.position + Vector3.up * 1.6f, 0.7f, UnityEngine.Random.Range(0.9f, 1.1f), 20f);
                // (#153: an animal's call is a noise the game's monsters hear, like a baboon hawk's: eyeless dogs come for it)
                if (Passive && IsServer && isOutside) RoundManager.Instance.PlayAudibleNoise(transform.position + Vector3.up, 22f, 0.8f, 0, false, 0);
            }
            // walking legs and arms, Minecraft's way; the right arm swings down when it hits
            float speed = Vector3.Distance(transform.position, lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            // (#153: an animal walking about makes footsteps the dogs can hear, as a player's do)
            if (Passive && IsServer && isOutside && speed > 0.5f && (stepNoiseIn -= Time.deltaTime) <= 0f)
            {
                stepNoiseIn = 1.5f;
                RoundManager.Instance.PlayAudibleNoise(transform.position, 10f, 0.5f, 0, false, 0);
            }
            lastPos = transform.position;
            walkPhase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 3f;
            float leg = Mathf.Clamp01(speed / 2f) * 35f * Mathf.Sin(walkPhase);
            if (legs[0] != null) legs[0].localRotation = Quaternion.Euler(leg, 0f, 0f);
            if (legs[1] != null) legs[1].localRotation = Quaternion.Euler(-leg, 0f, 0f);
            if (legs[2] != null) legs[2].localRotation = Quaternion.Euler(-leg, 0f, 0f); // (back legs: diagonal pairs together)
            if (legs[3] != null) legs[3].localRotation = Quaternion.Euler(leg, 0f, 0f);
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
                DeathPoof.Play(model); // (#113: red, over it goes, a puff of smoke)
                if (!Passive && playerWhoHit != null && playerWhoHit == GameNetworkManager.Instance?.localPlayerController) Advancements.Grant("monster_hunter"); // (a monster: not a pig)
                // (once: when another machine owns it, more hits can land here before its death comes back, #126)
                if (IsServer && !dropped)
                {
                    dropped = true; ServerDrops(transform.position + Vector3.up * 0.6f);
                    if (HeadKey != null && MobHeads.KilledByChargedBlast(transform.position)) MobHeads.ServerDropHead(HeadKey, transform.position + Vector3.up * 0.6f);
                    if (playerWhoHit != null && Xp > 0) BlockNet.ServerXp(playerWhoHit.actualClientId, Xp);
                }
                if (IsOwner) KillEnemyOnOwnerClient(overrideDestroy: true);
            }
            else if (IsOwner && Passive)
            {
                // (hurt, it squeals: a louder noise, the dogs close in)
                if (isOutside) RoundManager.Instance.PlayAudibleNoise(transform.position + Vector3.up, 14f, 0.9f, 0, false, 0);
                // (it runs: faster, away from who hit it, a few seconds)
                panicUntil = Time.time + 5f; panicFrom = playerWhoHit; agent.speed = PanicSpeed;
                if (currentSearch.inProgress) StopSearch(currentSearch);
                if (agent.isOnNavMesh) agent.ResetPath(); // (off it: falling, or held by the dev hold)
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
        public static EnemyType Register<T>(string id, string name, string scanText, int rarity, int hp, float height, int maxCount, float roamSpeed, Action<Transform> buildModel,
                                            bool daytime = false, float length = 0.8f, bool outside = false) where T : MinecraftMobAI
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
            box.isTrigger = true; box.size = new Vector3(0.8f, height, length); box.center = new Vector3(0f, height / 2f, 0f);
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
            // (a farm animal: out on the moon by day, as the game's own daytime creatures are)
            // (outside: the moon's night-time monsters, like the creeper's outside kind)
            type.isOutsideEnemy = daytime || outside;
            type.isDaytimeEnemy = daytime;
            type.MaxCount = maxCount;
            type.PowerLevel = daytime ? 0.5f : 1f;
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
                daytime ? LethalLib.Modules.Enemies.SpawnType.Daytime : outside ? LethalLib.Modules.Enemies.SpawnType.Outside : LethalLib.Modules.Enemies.SpawnType.Default,
                (TerminalNode)null, (TerminalKeyword)null);
            Plugin.Log.LogInfo($"{name} registered ({(daytime ? "outside by day" : outside ? "outside" : "inside")}, rarity {rarity})");
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
