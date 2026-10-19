using System.Collections.Generic;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's pillager (#223): an outside monster on the moons at night ([Mobs] PillagerRarity), and it comes in
    /// patrols. Each one that spawns is a patrol's captain, the ominous banner on its head, and brings two to four more
    /// with it (Minecraft's PatrolSpawner: a group of ceil(difficulty) + 1). The captain walks the patrol toward a far
    /// point, ten blocks at a time on a curve (PatrollingMonster.LongDistancePatrolGoal), the others following its steps.
    /// It fights Minecraft's way (RangedCrossbowAttackGoal): it closes to within 8 blocks with a clear view, stops, loads
    /// its crossbow (1.25 s), waits a second or two and shoots (an arrow at the skeleton's speed, Minecraft's spread);
    /// spotting someone or hurt, it calls the patrol members within 8 blocks onto them (HoldGroundAttackGoal,
    /// HurtByTargetGoal.setAlertOthers). It never hits up close. Killed: its crossbow now and then (Minecraft's 8.5%, worn;
    /// none with [Bows] PlayerBows off), the captain its banner and an ominous bottle (Minecraft's
    /// entities/pillager loot), and 6-8 experience. Model: Minecraft's IllagerModel from entity/illager/pillager.png.
    /// </summary>
    public class PillagerAI : MinecraftMobAI
    {
        protected override int Equipment => 1; // (#244: its crossbow, Minecraft's 1-3 more XP)
        public static EnemyType Type;
        public const string Name = "Pillager";
        public const int MaxHp = 5; // (Minecraft's 24 health: the witch's 26 is 5 here)
        protected override string Sound => "pillager";
        protected override float ChaseSpeed => 5.0f; // (Minecraft's 0.35 movement: faster than a zombie's 0.23, a skeleton's 0.25)
        protected override float RoamSpeed => 2.0f;
        protected override float SightRange => 32f * BlockWorld.S; // (Minecraft's 32-block follow range)
        protected override int Damage => 0; // (no melee: Minecraft's pillager only shoots)
        protected override string HeadKey => null;
        /// <summary>Its crossbow and the equipment's experience (Mob.getBaseExperienceReward: 1-3 a held item).</summary>
        protected override int Xp => 5 + Random.Range(1, 4);
        public static float AttackRadius => 8f * BlockWorld.S;

        // the patrol (host)
        public bool Captain;
        public PillagerAI Leader;
        bool patrolling;
        Vector3? patrolTarget, stepTarget;
        float patrolCooldown;
        public static int Patrols, Shots; // (dev/tests)

        // Minecraft's RangedCrossbowAttackGoal (host), in ticks
        enum Bow : byte { Uncharged, Charging, Charged, Ready }
        Bow bow;
        int seeTicks, attackDelay, chargeTicks;
        float tickAcc;

        // what every machine shows: held, loading (since when), loaded
        byte pose;
        float poseSince;
        Transform crossbow;
        MeshFilter crossbowMesh;
        int shownFrame = -2;
        GameObject banner;

        public override void Start()
        {
            base.Start();
            crossbow = transform.Find("Model/ArmR/Crossbow");
            crossbowMesh = crossbow != null ? crossbow.GetComponentInChildren<MeshFilter>() : null;
            if (agent != null) agent.stoppingDistance = 0.5f;
            if (!IsServer || isEnemyDead || RemovedAtStart || MobSpawns.IsRestored(this)) return; // (removed by base.Start: by a light, or no natural spawns; #227: one brought back from the save is no new captain)
            // a patrol member spawned by its captain: set before Start (ServerSpawnPatrol)
            if (Leader != null) { patrolling = true; return; }
            // (a natural spawn, out on the moon: it's a captain, and brings its patrol; dev spawns, eggs and outposts don't)
            bool natural = Time.time > MobSpawns.DevSpawnUntil || Time.time <= MobSpawns.DevNaturalUntil;
            if (natural && !plain && isOutside && transform.position.y > -100f && !Outposts.Spawning) ServerBecomeCaptain(Random.Range(2, 5));
        }

        bool plain;
        /// <summary>Host: the pillager just spawned there (an outpost's, an egg's) is no captain (its Start is a frame off).</summary>
        public static void NoPatrol(NetworkObjectReference r)
        {
            if (r.TryGet(out NetworkObject no) && no.GetComponent<PillagerAI>() is PillagerAI p) p.plain = true;
        }

        /// <summary>Host: the ominous banner on its head and `followers` more pillagers with it.</summary>
        public void ServerBecomeCaptain(int followers)
        {
            Captain = true; patrolling = true;
            ServerFindPatrolTarget();
            BlockNet.ServerMobState(NetworkObjectId, 1, 1);
            ShowBanner(true);
            Patrols++;
            for (int i = 0; i < followers; i++)
            {
                // (Minecraft's: each a few blocks on from the last)
                var at = transform.position + new Vector3(Random.Range(-4, 5), 0f, Random.Range(-4, 5)) * BlockWorld.S * (i + 1) * 0.5f;
                if (!NavMesh.SamplePosition(at, out var h, 3f * BlockWorld.S, NavMesh.AllAreas)) continue;
                var r = RoundManager.Instance.SpawnEnemyGameObject(h.position, Random.Range(0f, 360f), -1, Type);
                if (r.TryGet(out NetworkObject no) && no.GetComponent<PillagerAI>() is PillagerAI f) f.Leader = this;
            }
            Plugin.Log.LogInfo($"[mobs] a pillager patrol of {followers + 1} at {transform.position}");
        }

        /// <summary>Host: Minecraft's findPatrolTarget, 500 blocks off in any direction: here the far side of the moon's own
        /// outside nodes (a point it can walk toward).</summary>
        void ServerFindPatrolTarget()
        {
            Vector3? best = null; float bd = 0f;
            var nodes = RoundManager.Instance != null ? RoundManager.Instance.outsideAINodes : null;
            if (nodes != null && nodes.Length > 0)
                for (int i = 0; i < 6; i++)
                {
                    var n = nodes[Random.Range(0, nodes.Length)];
                    if (n == null) continue;
                    float d = Vector3.Distance(n.transform.position, transform.position);
                    if (d > bd) { bd = d; best = n.transform.position; }
                }
            patrolTarget = best ?? transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 60f;
        }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 1) { Captain = b != 0; ShowBanner(Captain); }
            else if (a == 2) { pose = b; poseSince = Time.time; }
        }

        void ShowBanner(bool on)
        {
            if (on && banner == null)
            {
                var head = transform.Find("Model/Head");
                if (head != null) banner = Banners.BuildOnHead(head, EntityModels.Px * BlockWorld.S * 0.9f);
            }
            if (banner != null) banner.SetActive(on);
        }

        void SetPose(byte p)
        {
            if (pose == p) return;
            pose = p; poseSince = Time.time;
            BlockNet.ServerMobState(NetworkObjectId, 2, p);
        }

        bool Sees(PlayerControllerB p) => p != null && !Physics.Linecast(eye.position, p.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);

        public override void DoAIInterval()
        {
            var before = targetPlayer;
            base.DoAIInterval();
            if (!IsServer || isEnemyDead) return;
            // (spotted someone, or hit: the patrol members near it go for them too)
            if (targetPlayer != null && targetPlayer != before) AlertOthers(targetPlayer);
            if (currentBehaviourStateIndex != Chase || targetPlayer == null) { ResetBow(); return; }
            // move as RangedCrossbowAttackGoal does: in, until within 8 blocks and seen a quarter second, then stand
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            bool needsToMove = (d > AttackRadius || seeTicks < 5) && attackDelay == 0;
            movingTowardsTargetPlayer = false;
            if (needsToMove)
            {
                agent.speed = bow == Bow.Uncharged ? ChaseSpeed : ChaseSpeed * 0.5f;
                SetDestinationToPosition(targetPlayer.transform.position);
            }
            else if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
        }

        protected override void Wander() { if (patrolling) PatrolStep(); else Stroll(); }

        /// <summary>Host: a step of the patrol's walk (LongDistancePatrolGoal.tick, when its path is done).</summary>
        void PatrolStep()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if (agent.pathPending || (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.3f)) return;
            if (Time.time < patrolCooldown) return;
            agent.speed = Captain ? RoamSpeed * 0.595f / 0.7f : RoamSpeed; // (Minecraft's: the leader a little slower, so they keep up)
            if (!Captain)
            {
                // a member walks to its captain's step; its captain gone, it just wanders (Minecraft's stops patrolling)
                if (Leader == null || Leader.isEnemyDead || Vector3.Distance(Leader.transform.position, transform.position) > 32f * BlockWorld.S) { patrolling = false; return; }
                if (Leader.stepTarget.HasValue) SetDestinationToPosition(Leader.stepTarget.Value + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)) * BlockWorld.S);
                return;
            }
            if (!patrolTarget.HasValue || Vector3.Distance(patrolTarget.Value, transform.position) < 10f * BlockWorld.S) ServerFindPatrolTarget();
            // (Minecraft's: toward the target turned 90 degrees about it, 0.4 of the way: a curve; 10 blocks at a time)
            var t = patrolTarget.Value; var self = transform.position;
            var off = self - t;
            var curved = t + Quaternion.Euler(0f, -90f, 0f) * off * 0.4f;
            var move = self + (curved - self).normalized * 10f * BlockWorld.S;
            if (NavMesh.SamplePosition(move, out var h, 4f * BlockWorld.S, agent.areaMask) && SetDestinationToPosition(h.position, checkForPath: true)) stepTarget = h.position;
            else { patrolCooldown = Time.time + 10f; stepTarget = null; Stroll(); } // (Minecraft's: a random step, and a 10 s wait)
        }

        void AlertOthers(PlayerControllerB who)
        {
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is PillagerAI o && o != this && !o.isEnemyDead && o.currentBehaviourStateIndex == Roam && Vector3.Distance(o.transform.position, transform.position) < 8f * BlockWorld.S)
                {
                    o.targetPlayer = who; o.agent.speed = o.ChaseSpeed;
                    o.SwitchToBehaviourState(Chase);
                }
        }

        void ResetBow()
        {
            if (bow != Bow.Uncharged && bow != Bow.Charged) bow = Bow.Uncharged;
            seeTicks = 0; attackDelay = 0;
            if (pose == 1) SetPose(0);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            if (IsServer) CrossbowTicks();
            // facing whoever it's after while it stands to shoot
            if (currentBehaviourStateIndex == Chase && targetPlayer != null && (agent == null || !agent.hasPath))
            {
                var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            }
            Animate();
        }

        /// <summary>Host: RangedCrossbowAttackGoal.tick, 20 a second.</summary>
        void CrossbowTicks()
        {
            tickAcc += Time.deltaTime;
            while (tickAcc >= 0.05f)
            {
                tickAcc -= 0.05f;
                if (currentBehaviourStateIndex != Chase || targetPlayer == null) continue;
                bool sees = Sees(targetPlayer);
                if (sees != seeTicks > 0) seeTicks = 0;
                seeTicks += sees ? 1 : -1;
                float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
                bool needsToMove = (d > AttackRadius || seeTicks < 5) && attackDelay == 0;
                switch (bow)
                {
                    case Bow.Uncharged:
                        if (!needsToMove) { bow = Bow.Charging; chargeTicks = 0; SetPose(1); }
                        break;
                    case Bow.Charging:
                        if (++chargeTicks >= CrossbowData.ChargeTicks(0))
                        {
                            bow = Bow.Charged; attackDelay = 20 + Random.Range(0, 20); SetPose(2);
                            BlockNet.ServerSound(transform.position + Vector3.up, "crossbow.load_end", 1f / (Random.value * 0.5f + 1f) + 0.2f, 0.8f);
                        }
                        break;
                    case Bow.Charged:
                        if (--attackDelay <= 0) { attackDelay = 0; bow = Bow.Ready; }
                        break;
                    case Bow.Ready:
                        if (sees) { Shoot(targetPlayer); bow = Bow.Uncharged; SetPose(0); }
                        break;
                }
            }
        }

        /// <summary>Host: Minecraft's performCrossbowAttack at a target: aimed a third of the way up them, raised a fifth
        /// of the distance for the drop, at the skeleton's speed, with Minecraft's spread for a mob (uncertainty 6 on
        /// Normal: 14 - 4 x 2).</summary>
        void Shoot(PlayerControllerB t)
        {
            var from = eye.position + transform.forward * 0.5f - Vector3.up * 0.15f;
            var to = t.transform.position + Vector3.up * 0.6f;
            var v = Arrow.Aim(from, to);
            var dir = (v.normalized + new Vector3(Gauss(), Gauss(), Gauss()) * 0.0172275f * 6f).normalized;
            BlockNet.ServerArrow(from, dir * v.magnitude, Arrow.NoShooter, 0, Arrow.Flags(0, true, true));
            BlockNet.ServerSound(from, "crossbow.shoot", 1f, 1f);
            Shots++;
        }

        static float Gauss()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            // (HurtByTargetGoal.setAlertOthers: the patrol turns on whoever hit it)
            if (IsServer && !isEnemyDead && playerWhoHit != null) AlertOthers(playerWhoHit);
        }

        // ------------------------------------------------------------------ the look (every machine)
        /// <summary>IllagerModel's crossbow poses (AnimationUtils.animateCrossbowHold / animateCrossbowCharge), in
        /// Minecraft's radians, turned into ours; the crossbow's frame: at rest, pulled back as it loads, loaded.</summary>
        void Animate()
        {
            if (armR == null || armL == null) return;
            if (pose == 1)
            {
                float a = Mathf.Clamp01((Time.time - poseSince) / (CrossbowData.ChargeTicks(0) / 20f));
                armR.localRotation = McRot(-0.97079635f, -0.8f, 0f);
                armL.localRotation = McRot(Mathf.Lerp(-0.97079635f, -1.5707964f, a), Mathf.Lerp(0.4f, 0.85f, a), 0f);
                ShowCrossbow(a < 0.58f ? 0 : a < 1f ? 1 : 2);
            }
            else
            {
                armR.localRotation = McRot(-1.5707964f + 0.1f, -0.3f, 0f);
                armL.localRotation = McRot(-1.5f, 0.6f, 0f);
                ShowCrossbow(pose == 2 ? 3 : -1);
            }
        }

        /// <summary>A ModelPart's rotation (Minecraft: Z, then Y, then X about its own axes, y down and the front -z) in
        /// our frame (y up, the front +z): x the same, y and z the other way.</summary>
        public static Quaternion McRot(float x, float y, float z) =>
            Quaternion.AngleAxis(-z * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right);

        void ShowCrossbow(int f)
        {
            if (crossbowMesh == null || f == shownFrame) return;
            shownFrame = f;
            string tile = f == 3 ? "item_crossbow_arrow" : f >= 0 ? "item_crossbow_pulling_" + f : "item_crossbow_standby";
            if (Atlas.Tiles.ContainsKey(tile)) crossbowMesh.sharedMesh = MeshBuilder.ExtrudedSprite(tile);
        }

        /// <summary>Host: Minecraft's drops: its crossbow 8.5% of the time, worn (Mob.dropCustomDeathLoot); the captain's
        /// banner every time and its ominous bottle (entities/pillager).</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Crossbow.On && Random.value < 0.085f && ModItems.ByKey.TryGetValue(Crossbow.Key, out var cbItem) && cbItem.spawnPrefab.GetComponent<CrossbowItem>() is CrossbowItem proto)
            {
                int wear = CrossbowData.DroppedWear(proto.MaxUses, Random.value, Random.value);
                ModItems.ServerSpawnPlainKeyed(ItemData.With(Crossbow.Key, CrossbowData.Data(wear, CrossbowData.Load.None, 0)), at);
            }
            if (Captain)
            {
                if (ModItems.ByKey.TryGetValue(Banners.OminousKey, out var b)) ModItems.ServerSpawnStack(b, 1, at);
                OminousBottle.ServerDrop(at);
            }
        }

        static Material mat;

        public static void Register()
        {
            if (Plugin.PillagerRarity.Value <= 0) return;
            Type = Register<PillagerAI>("Pillager", Name, "Shoots crossbow bolts; comes in patrols", Plugin.PillagerRarity.Value, MaxHp, 2.5f, 8, 2.0f, BuildModel, outside: true);
        }

        // ------------------------------------------------------------------ the model: Minecraft's IllagerModel (64x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.9f; // (as tall as a player, like the witch)
            if (mat == null) mat = EntityModels.Material("LMC_Pillager", "entity/illager/pillager", new Color(0.35f, 0.38f, 0.4f));
            // (IllagerModel's boxes, Minecraft's y down from 24 above the feet turned into y up from the feet, its front +z)
            var head = new GameObject("Head").transform; head.SetParent(parent, false);
            head.localPosition = new Vector3(0, 24, 0) * k;
            EntityModels.Part(head, "Skull", new Vector3(0, 5, 0), new Vector3(8, 10, 8), 0, 0, k, mat);
            EntityModels.Part(head, "Nose", new Vector3(0, 1, 5), new Vector3(2, 4, 2), 24, 0, k, mat);
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 6), 16, 20, k, mat);
            var robe = EntityModels.Part(parent, "Robe", new Vector3(0, 14, 0), new Vector3(8, 20, 6), 0, 38, k, mat);
            robe.transform.localScale = new Vector3(9f / 8f, 21f / 20f, 7f / 6f); // (Minecraft inflates it half a pixel)
            foreach (var (n, x, cx) in new[] { ("ArmR", -5f, -1f), ("ArmL", 5f, 1f) })
            {
                var sh = new GameObject(n).transform; sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 22f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(cx, -4, 0), new Vector3(4, 12, 4), 40, 46, k, mat);
                if (n == "ArmR") HeldCrossbow(sh, k);
            }
            foreach (var (n, x) in new[] { ("LegL", 2f), ("LegR", -2f) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 12f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -6, 0), new Vector3(4, 12, 4), 0, 22, k, mat);
            }
        }

        /// <summary>The crossbow in its right hand, as Minecraft's ItemInHandLayer puts it (a turn of -90 about x and 180
        /// about y, 1, 2, -10 px on) with the crossbow's own third-person display (-90, 0, -60; 2, 0.1, -3 px; 0.9 the
        /// size): worked through in Minecraft's frame and turned into ours, 120 degrees about z, 3 px in, 13 down, 2.1 on.</summary>
        static void HeldCrossbow(Transform arm, float k)
        {
            var go = new GameObject("Crossbow") { layer = 19 };
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(-3f, -13f, 2.1f) * k;
            go.transform.localRotation = new Quaternion(0f, 0f, 0.86603f, 0.5f);
            go.transform.localScale = Vector3.one * (14.4f * k);
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_crossbow_standby") ? "item_crossbow_standby" : "item_bow");
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
        }
    }
}
