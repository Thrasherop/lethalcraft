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
        protected override float ChaseSpeed => 6.0f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 30;
        protected override float SightRange => 32f;
        protected override float TallBlocks => 2.9f; // (Minecraft's: a two-high gap keeps it out)
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

        // ------------------------------------------------------------------ carrying blocks (#94)
        /// <summary>
        /// What an enderman picks up, Minecraft's list as far as this world has it: grass, dirt, sand, gravel, TNT, a
        /// jack o'lantern, and the Nether's netherrack, nylium, soul sand and soul soil. Only blocks players placed (the
        /// facility's own walls and the moon's ground stay where they are).
        /// </summary>
        public static readonly HashSet<string> Holdable = new HashSet<string>
        {
            "grass", "dirt", "sand", "gravel", "tnt", "jack_o_lantern", "netherrack", "crimson_nylium", "warped_nylium", "soul_sand", "soul_soil",
        };
        /// <summary>The block in its arms (0: none): the host's, told to every machine for the look.</summary>
        public byte Carrying;
        float nextBlockThought = 5f;
        Transform carried;
        public static int PickedUp, PutDown; // (dev/tests)

        /// <summary>Host, now and then while it wanders: pick a block up from close by, or put the one it has down.</summary>
        void ServerBlockThought()
        {
            if (DevBlockHaste && nextBlockThought > Time.time + 1.5f) nextBlockThought = Time.time + 0.5f; // (haste: now)
            if (Time.time < nextBlockThought) return;
            nextBlockThought = Time.time + Random.Range(6f, 14f) * (DevBlockHaste ? 0.1f : 1f);
            var w = BlockWorld.Instance;
            if (w == null) return;
            if (Carrying == 0)
            {
                if (!DevBlockHaste && Random.value > 0.5f) return;
                BlockKey best = default; float bd = 3.5f; bool found = false;
                foreach (var kv in w.Blocks)
                {
                    var def = kv.Value.Data.Def;
                    if (def == null || !Holdable.Contains(def.Key) || kv.Key.Frame != 0) continue;
                    if ((kv.Value.Data.State & Blocks.NaturalGround) != 0) continue; // (the moon's own ground, molded: not that)
                    float d = Vector3.Distance(w.WorldCenter(kv.Key), transform.position + Vector3.up);
                    if (d < bd) { bd = d; best = kv.Key; found = true; }
                }
                if (!found) return;
                var taken = w.Get(best).Data.Def;
                BlockNet.ServerBroadcastOp(Op.Remove(best, false));
                Redstone.MarkDirty(); Gravity.MarkDirty();
                Carrying = taken.Id; PickedUp++;
                BlockNet.ServerEndermanCarry(NetworkObjectId, Carrying);
            }
            else
            {
                if (!DevBlockHaste && Random.value > 0.35f) return;
                // down beside it, on something solid, where nothing (and nobody) is
                var fwd = transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                foreach (var off in new[] { fwd * 1.6f, -fwd * 1.6f, transform.right * 1.6f, -transform.right * 1.6f })
                    foreach (int dy in new[] { 0, 1, -1 })
                    {
                        var c = Ground.CellOf(transform.position + off + Vector3.up * 0.3f) + new Vector3Int(0, dy, 0);
                        var k = Ground.KeyOf(c);
                        var below = c + Vector3Int.down;
                        if (w.Has(k) || !Ground.IsAirCell(c)) continue;
                        if (!w.Has(Ground.KeyOf(below)) && !Ground.IsSolidCell(below)) continue;
                        if (ServerLogic.Obstructed(k)) continue;
                        w.FrameRoot(0, true);
                        BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Carrying, (byte)Face.Up)));
                        Redstone.MarkDirty(); Gravity.MarkDirty();
                        Carrying = 0; PutDown++;
                        BlockNet.ServerEndermanCarry(NetworkObjectId, 0);
                        return;
                    }
            }
        }

        /// <summary>(dev/tests) think about blocks every second or so, always acting.</summary>
        public static bool DevBlockHaste;

        /// <summary>Every machine: the block it carries, held out in front in both hands.</summary>
        public void ShowCarried(byte id)
        {
            Carrying = id;
            if (carried != null) { Destroy(carried.gameObject); carried = null; }
            var def = id != 0 ? Blocks.Get(id) : null;
            if (def == null) return;
            var m = transform.Find("Model");
            if (m == null) return;
            var go = new GameObject("Carried");
            go.transform.SetParent(m, false);
            go.transform.localPosition = new Vector3(0f, 30f, 7f) * K;
            go.transform.localScale = Vector3.one * (10f * K);
            go.layer = 19;
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.For(def, 0, 0);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive }; Atlas.NoDecals(mr);
            carried = go.transform;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (!isEnemyDead && IsServer && currentBehaviourStateIndex != Chase) ServerBlockThought();
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
            // carrying: both arms out in front, holding it
            if (Carrying != 0 && armR != null && armL != null) { armR.localRotation = Quaternion.Euler(-35f, 0f, 0f); armL.localRotation = Quaternion.Euler(-35f, 0f, 0f); }
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
            // (what it was carrying falls with it)
            var held = Carrying != 0 ? Blocks.Get(Carrying) : null;
            if (held != null && ModItems.ByKey.TryGetValue(held.Key, out var hi)) ModItems.ServerSpawnStack(hi, 1, at);
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
            return $"endermen={all.Count} provoked={Provoked} teleports={Teleports} picked={PickedUp} put={PutDown} " + string.Join(" ; ", all.Select(e => $"{e.transform.position.x:F1},{e.transform.position.y:F1},{e.transform.position.z:F1} state={e.currentBehaviourStateIndex} target={(e.targetPlayer != null ? e.targetPlayer.playerUsername : "-")} angry={e.angry.Count} carrying={(e.Carrying != 0 ? Blocks.Get(e.Carrying)?.Key : "-")}"));
        }
    }
}
