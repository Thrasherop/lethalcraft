using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's enderman (#59): a rare, tall wanderer inside the facility ([Mobs] EndermanRarity). It leaves you alone
    /// until you look it in the eyes (or hit it): then it screams, shakes, and comes for you fast, teleporting close
    /// every few seconds and away when it's hurt. Killed, it sometimes drops an ender pearl. Its model comes from your
    /// Minecraft textures (entity/enderman/enderman.png, the eyes glowing from enderman_eyes.png).
    /// </summary>
    public class EndermanAI : MinecraftMobAI
    {
        public const string Name = "Enderman";
        public static EnemyType Type;
        protected override string Sound => "enderman";
        protected override float ChaseSpeed => 5.0f;
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 30;
        protected override float SightRange => 32f;
        public const float StareRange = 24f, Scale = 0.75f;

        /// <summary>Who it's angry with (looked at it, or hit it): host's list, it goes only for them.</summary>
        readonly HashSet<ulong> angry = new HashSet<ulong>();
        float nextBlink = 4f, provokeCooldown, shake;
        Transform head;
        public static int Provoked, Teleports; // (dev/tests)

        static float K => EntityModels.Px * BlockWorld.S * Scale;
        static float HeadHeight => 42f * K;

        protected override bool Wants(PlayerControllerB p) => angry.Contains(p.actualClientId);

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            head = m != null ? m.Find("Head") : null;
        }

        /// <summary>Host: someone looked it in the eyes (or hit it): after them.</summary>
        public void ServerProvoke(ulong client)
        {
            if (isEnemyDead || angry.Contains(client)) return;
            var p = ServerLogic.PlayerFor(client);
            if (p == null) return;
            angry.Add(client);
            Provoked++;
            BlockNet.ServerSound(transform.position + Vector3.up * HeadHeight, "enderman.scream", 1f, 1f);
            if (currentSearch.inProgress) StopSearch(currentSearch);
            targetPlayer = p; agent.speed = ChaseSpeed;
            SwitchToBehaviourState(Chase);
            nextBlink = Time.time + Random.Range(2f, 4f);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer || currentBehaviourStateIndex != Chase || targetPlayer == null) return;
            // after someone: now and then it blinks in close to them (behind or beside, on the navmesh)
            if (Time.time >= nextBlink)
            {
                nextBlink = Time.time + Random.Range(4f, 8f);
                if (Vector3.Distance(transform.position, targetPlayer.transform.position) > 5f)
                    ServerTeleport(targetPlayer.transform.position, 2.5f, 5f);
            }
        }

        /// <summary>Host: warp to a navmesh spot `min`-`max` metres from `around` (the portal sound at both ends).</summary>
        bool ServerTeleport(Vector3 around, float min, float max)
        {
            // (not while its agent is off or off the navmesh: held still, or between floors; Unity errors on paths then)
            if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
            for (int i = 0; i < 12; i++)
            {
                var off = Random.insideUnitCircle.normalized * Random.Range(min, max);
                var want = around + new Vector3(off.x, 0f, off.y);
                if (!NavMesh.SamplePosition(want, out var hit, 2f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - around.y) > 2.5f) continue;
                var path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                BlockNet.ServerSound(transform.position + Vector3.up, "enderman.portal", 1f, 0.8f);
                agent.Warp(hit.position);
                serverPosition = hit.position;
                BlockNet.ServerSound(hit.position + Vector3.up, "enderman.portal", 1f, 0.8f);
                Teleports++;
                return true;
            }
            return false;
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead || !IsServer) return;
            if (playerWhoHit != null) ServerProvoke(playerWhoHit.actualClientId);
            // hurt: it blinks away (and comes back)
            if (Random.value < 0.6f) ServerTeleport(transform.position, 6f, 12f);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            bool angryNow = currentBehaviourStateIndex == Chase;
            // angry: the head shakes and the jaw drops (Minecraft's: its mouth opens)
            shake = angryNow ? shake + Time.deltaTime * 40f : 0f;
            if (head != null) head.localPosition = new Vector3(angryNow ? Mathf.Sin(shake) * 0.4f * K : 0f, (angryNow ? 43f : 42f) * K, 0f);
            CheckStare();
        }

        /// <summary>Every machine, for its own player: looking it in the eyes from close enough provokes it (told to the host).</summary>
        void CheckStare()
        {
            provokeCooldown -= Time.deltaTime;
            if (provokeCooldown > 0f) return;
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || p.isPlayerDead || currentBehaviourStateIndex == Chase && targetPlayer == p) return;
            var cam = p.gameplayCamera.transform;
            var eyes = transform.position + Vector3.up * HeadHeight;
            var to = eyes - cam.position; float d = to.magnitude;
            if (d > StareRange || d < 0.5f) return;
            // (Minecraft's test: the look within a cone narrowing with distance, 1 - 0.025 / blocks: about 6 degrees at 5 blocks)
            if (Vector3.Dot(cam.forward, to / d) < 1f - 0.03f * BlockWorld.S / d) return;
            if (Physics.Linecast(cam.position, eyes, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) return;
            provokeCooldown = 1.5f;
            Sounds.Play2D("enderman.stare", 0.7f, 1f);
            BlockNet.RequestProvoke(NetworkObjectId);
        }

        /// <summary>Host: Minecraft's drop: an ender pearl half the time.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("ender_pearl", out var pearl)) ModItems.ServerSpawnStack(pearl, 1, at);
        }

        static Material sharedMat, eyesMat;

        public static void Register()
        {
            if (Plugin.EndermanRarity.Value <= 0) return;
            Type = Register<EndermanAI>("Enderman", Name, "Don't look it in the eyes", Plugin.EndermanRarity.Value, 8, 46f * K, 2, 1.8f, BuildModel);
        }

        // ------------------------------------------------------------------ the model: Minecraft's enderman, 64x32
        static void BuildModel(Transform parent)
        {
            float k = K;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Enderman", "entity/enderman/enderman", new Color(0.06f, 0.06f, 0.08f));
            // feet at 0: legs 30 tall, the body above them, the head on top; arms as long as the legs, from the shoulders
            EntityModels.Part(parent, "Head", new Vector3(0, 42, 0), new Vector3(8, 8, 8), 0, 0, k, sharedMat);
            if (eyesMat == null) eyesMat = EyesMaterial();
            if (eyesMat != null)
            {
                var h = parent.Find("Head");
                EntityModels.Part(h, "Eyes", new Vector3(0, 0, 0.02f), new Vector3(8.04f, 8.04f, 8.04f), 0, 0, k, eyesMat);
            }
            EntityModels.Part(parent, "Body", new Vector3(0, 32, 0), new Vector3(8, 12, 4), 32, 16, k, sharedMat);
            foreach (var (n, x) in new[] { ("ArmR", -5f), ("ArmL", 5f) })
            {
                var sh = new GameObject(n).transform;
                sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 37f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(0, -14, 0), new Vector3(2, 30, 2), 56, 0, k, sharedMat);
            }
            foreach (var (n, x) in new[] { ("LegL", 2f), ("LegR", -2f) })
            {
                var hip = new GameObject(n).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 29f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -14.5f, 0), new Vector3(2, 30, 2), 56, 0, k, sharedMat);
            }
        }

        static Material EyesMaterial()
        {
            if (McAssets.LoadTexture("entity/enderman/enderman_eyes") == null) return null;
            var m = EntityModels.Material("LMC_EndermanEyes", "entity/enderman/enderman_eyes", new Color(0, 0, 0, 0));
            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, ArmorModels.AmbientEV + 7f, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            return m;
        }

        public static string Describe()
        {
            var all = RoundManager.Instance != null ? RoundManager.Instance.SpawnedEnemies.OfType<EndermanAI>().Where(e => !e.isEnemyDead).ToList() : new List<EndermanAI>();
            return $"endermen={all.Count} provoked={Provoked} teleports={Teleports} " + string.Join(" ; ", all.Select(e => $"{e.transform.position.x:F1},{e.transform.position.y:F1},{e.transform.position.z:F1} state={e.currentBehaviourStateIndex} target={(e.targetPlayer != null ? e.targetPlayer.playerUsername : "-")} angry={e.angry.Count}"));
        }
    }
}
