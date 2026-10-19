using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's blaze (#66, phase 4): it lives in the Nether fortress (its spawner keeps them coming), hovers, burns
    /// whoever touches it, and from a distance shoots a burst of three fireballs that hurt and set fire where they land.
    /// Killed, it drops a blaze rod half the time: worth good money. Its model comes from your Minecraft textures
    /// (entity/blaze.png): a head and twelve rods turning around it in three rings.
    /// </summary>
    public class BlazeAI : MinecraftMobAI
    {
        protected override bool WaterHurts => true; // (#244: Minecraft's isSensitiveToWater)
        protected override bool FloatsInWater => false; // (#247: it sets its own height)
        public const string Name = "Blaze";
        public static EnemyType Type;
        protected override string Sound => "blaze";
        protected override float ChaseSpeed => 4.6f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 15;
        public const float ShootRange = 18f;

        float nextBurst = 3f, rodAngle, bob;
        int burstLeft; float nextShot;
        readonly List<Transform> rods = new List<Transform>();
        // (#111: after someone, it burns: a flickering glow, its rods spin faster, embers rise off it, Minecraft's way)
        HDAdditionalLightData glow;
        float burn, nextEmber;
        readonly List<(Transform tr, float age)> embers = new List<(Transform, float)>();
        static Mesh emberMesh;

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) foreach (Transform t in m) if (t.name.StartsWith("Rod")) rods.Add(t);
            if (agent != null) { agent.baseOffset = 0.5f; agent.stoppingDistance = 5f; }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer || targetPlayer == null || currentBehaviourStateIndex != Chase) return;
            // a burst of three, every few seconds, at a player it can see
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (Time.time >= nextBurst && d <= ShootRange && !Physics.Linecast(eye.position, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
            {
                burstLeft = 3; nextShot = Time.time; nextBurst = Time.time + Random.Range(4f, 6f);
            }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // the rods turn, the head bobs (Minecraft's blaze); faster when it burns
            bool aggro = currentBehaviourStateIndex == Chase;
            burn = Mathf.MoveTowards(burn, aggro ? 1f : 0f, Time.deltaTime * 2f);
            Burn();
            rodAngle += Time.deltaTime * (90f + 180f * burn);
            bob += Time.deltaTime * 2f;
            for (int i = 0; i < rods.Count; i++)
            {
                int ring = i / 4; float r = new[] { 9f, 7f, 5f }[ring] * K;
                float a = (rodAngle * (ring == 1 ? -1f : 1f) + i % 4 * 90f + ring * 45f) * Mathf.Deg2Rad;
                rods[i].localPosition = new Vector3(Mathf.Cos(a) * r, (new[] { 15f, 10f, 5f }[ring] + Mathf.Sin(bob + i) * 1.5f) * K, Mathf.Sin(a) * r);
            }
            // (it faces whoever it's after, as Minecraft's does, even while it moves sideways round a corner: #102, it was
            // turned with its path and shot at you with its back to you)
            if (IsOwner && agent != null)
            {
                bool facing = currentBehaviourStateIndex == Chase && targetPlayer != null;
                agent.updateRotation = !facing;
                if (facing)
                {
                    var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                }
                // (#244) Minecraft's blaze flies up and down to the eye height of what it's after (BlazeAttackGoal/aiStep);
                // otherwise it settles back to its hover
                float ground = transform.position.y - agent.baseOffset;
                float want = facing ? Mathf.Clamp(targetPlayer.gameplayCamera.transform.position.y - ground - 1.2f * BlockWorld.S, 0.5f, 6f * BlockWorld.S) : 0.5f;
                agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, want, Time.deltaTime * 1.5f);
            }
            if (IsServer && burstLeft > 0 && Time.time >= nextShot && targetPlayer != null)
            {
                burstLeft--; nextShot = Time.time + 0.3f;
                var from = eye.position + transform.forward * 0.5f;
                var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.4f + Random.insideUnitSphere * 0.6f;
                BlockNet.ServerFireball(from, (to - from).normalized);
            }
        }

        void Burn()
        {
            if (burn > 0f && glow == null)
            {
                var lgo = new GameObject("BlazeGlow"); lgo.transform.SetParent(transform, false); lgo.transform.localPosition = Vector3.up * 1.6f;
                glow = lgo.AddHDLight(HDLightTypeAndShape.Point);
                glow.EnableShadows(false); glow.affectsVolumetric = false;
                glow.lightUnit = LightUnit.Lumen; glow.range = 7f;
                lgo.GetComponent<Light>().color = new Color(1f, 0.5f, 0.15f);
            }
            if (glow != null)
            {
                glow.intensity = burn * (900f + 500f * Mathf.PerlinNoise(Time.time * 9f, GetInstanceID() * 0.01f));
                glow.gameObject.SetActive(burn > 0.01f);
            }
            // embers: little flames that rise off it and burn out
            if (burn > 0.5f && Time.time >= nextEmber)
            {
                nextEmber = Time.time + 0.07f;
                if (emberMesh == null) emberMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("fire_0") ? "fire_0" : "item_fire_charge");
                var e = new GameObject("ember");
                e.transform.position = transform.position + Vector3.up * Random.Range(0.6f, 2.0f) + Random.insideUnitSphere * 0.5f;
                e.transform.localScale = Vector3.one * Random.Range(0.12f, 0.2f);
                e.AddComponent<MeshFilter>().sharedMesh = emberMesh;
                var mr = e.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Emissive; Atlas.NoDecals(mr);
                embers.Add((e.transform, 0f));
            }
            var cam = Camera.main != null ? Camera.main.transform : null;
            for (int i = embers.Count - 1; i >= 0; i--)
            {
                var (tr, age) = embers[i];
                age += Time.deltaTime;
                if (tr == null || age > 0.6f) { if (tr != null) Destroy(tr.gameObject); embers.RemoveAt(i); continue; }
                tr.position += Vector3.up * 1.2f * Time.deltaTime;
                tr.localScale *= 1f - Time.deltaTime * 1.5f;
                if (cam != null) tr.rotation = Quaternion.LookRotation(tr.position - cam.position);
                embers[i] = (tr, age);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var (tr, _) in embers) if (tr != null) Destroy(tr.gameObject);
            embers.Clear();
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (KilledByPlayer && Random.value < 0.5f) ModItems.ServerSpawnScrapItem(BlazeRod.Key, at); // (#244: a player's kill only)
        }
        protected override int Xp => 10; // (Minecraft's)

        static float K => EntityModels.Px * BlockWorld.S * 0.8f;
        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherBlazes.Value) return;
            // (only the Nether's spawning makes them: a weight of 0 in the moons' own lists)
            Type = Register<BlazeAI>("Blaze", Name, "Shoots fireballs; drops blaze rods", 0, 4, 2.0f, 6, 1.4f, BuildModel);
        }

        static void BuildModel(Transform parent)
        {
            float k = K;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Blaze", "entity/blaze", new Color(1f, 0.75f, 0.2f));
            // the head 8x8x8 at (0,0), up where a body would be; rods 2x8x2 at (0,16)
            EntityModels.Part(parent, "Head", new Vector3(0, 18, 0), new Vector3(8, 8, 8), 0, 0, k, sharedMat);
            for (int i = 0; i < 12; i++) EntityModels.Part(parent, "Rod" + i, Vector3.zero, new Vector3(2, 8, 2), 0, 16, k, sharedMat);
        }
    }

    /// <summary>A blaze's fireball (#66): every machine flies its own copy; each checks its own player; the host sets fires.
    /// A ghast's can be hit back (#187), as Minecraft's: an attack on it sends it the way you look; back at a ghast, it
    /// kills it ("Return to Sender").</summary>
    public class Fireball : MonoBehaviour
    {
        public const float Speed = 16f, BigSpeed = 11f, Life = 5f;
        public const ulong NoOne = ulong.MaxValue;
        Vector3 dir; float age;
        bool big; // (a ghast's: bigger, slower, and it blows up where it lands, #62)
        int id;
        ulong deflector = NoOne; float sparedUntil; // (hit back: by whom (their player id); its hitter safe from it a moment)
        static readonly Dictionary<int, Fireball> live = new Dictionary<int, Fireball>();
        public static int Deflects, Returned; // (dev/tests)
        /// <summary>(dev) The fireballs in flight here: where, how far from p, whose hit.</summary>
        public static string Describe(Vector3 from) => $"deflects={Deflects} returned={Returned} " + string.Join(" ; ", live.Values.Where(f => f != null).Select(f =>
            $"#{f.id} big={f.big} at={f.transform.position.x:F1},{f.transform.position.y:F1},{f.transform.position.z:F1} d={Vector3.Distance(f.transform.position, from):F1} by={(f.deflector == NoOne ? "-" : f.deflector.ToString())}"));

        public static void Launch(Vector3 from, Vector3 dir, bool big = false, int id = 0)
        {
            var go = new GameObject("LMC_Fireball");
            go.transform.position = from;
            var f = go.AddComponent<Fireball>(); f.dir = dir; f.big = big; f.id = id;
            if (id != 0) live[id] = f;
            // Minecraft's small fireball: the fire charge's look, glowing
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_fire_charge") ? "item_fire_charge" : "fire_0");
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Emissive; Atlas.NoDecals(mr);
            go.transform.localScale = Vector3.one * (big ? 1.3f : 0.45f);
            var lgo = new GameObject("light"); lgo.transform.SetParent(go.transform, false);
            var hd = lgo.AddHDLight(UnityEngine.Rendering.HighDefinition.HDLightTypeAndShape.Point);
            hd.EnableShadows(false); hd.affectsVolumetric = false;
            hd.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lumen; hd.intensity = 600f; hd.range = 6f;
            lgo.GetComponent<Light>().color = new Color(1f, 0.55f, 0.2f);
            Sounds.Play(big ? "ghast.shoot" : "blaze.shoot", from, 0.8f, Random.Range(0.9f, 1.1f), 24f);
        }

        void OnDestroy() { if (id != 0 && live.TryGetValue(id, out var f) && f == this) live.Remove(id); }

        /// <summary>The local player attacked (an attack press, any item or none): a ghast's fireball under the crosshair,
        /// in reach, goes the way they look. Minecraft's: attacking the fireball deflects it along the attacker's view.</summary>
        public static bool TryDeflect(PlayerControllerB p)
        {
            if (live.Count == 0 || p == null || p.isPlayerDead) return false;
            var cam = p.gameplayCamera.transform;
            float reach = 3f * BlockWorld.S + 1f;
            foreach (var f in live.Values)
            {
                if (f == null || !f.big) continue;
                var to = f.transform.position - cam.position;
                float along = Vector3.Dot(to, cam.forward);
                if (along < 0f || along > reach) continue;
                if ((to - cam.forward * along).magnitude > 1.0f) continue; // (its size, and a little)
                var d = cam.forward;
                f.Turn(f.transform.position, d, p.playerClientId);
                BlockNet.RequestDeflect(f.id, f.transform.position, d);
                Sounds.Play("hit", f.transform.position, 0.8f, 1f, 16f);
                return true;
            }
            return false;
        }

        void Turn(Vector3 at, Vector3 d, ulong by)
        {
            transform.position = at; dir = d.normalized; deflector = by;
            age = 0f; sparedUntil = Time.time + 0.5f;
            Deflects++;
        }

        /// <summary>Every machine: the host's word that someone hit this fireball back.</summary>
        public static void Deflected(int id, Vector3 at, Vector3 d, ulong by)
        {
            if (!live.TryGetValue(id, out var f) || f == null) return;
            var lp = GameNetworkManager.Instance?.localPlayerController;
            if (lp != null && lp.playerClientId == by && f.deflector == by) return; // (my own hit: already flying)
            f.Turn(at, d, by);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
            float step = (big ? BigSpeed : Speed) * Time.deltaTime;
            var p = transform.position;
            // the local player: a hit
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp != null && !lp.isPlayerDead && !(Time.time < sparedUntil && lp.playerClientId == deflector))
            {
                var a = lp.transform.position + Vector3.up * 0.3f; var b = lp.transform.position + Vector3.up * 2.0f;
                var closest = ClosestOnSegment(a, b, p);
                if (Vector3.Distance(closest, p) < (big ? 0.9f : 0.6f))
                {
                    if (big) { BlockNet.RequestBigFireballHit(p); Destroy(gameObject); return; } // (the host blows it up there: its blast hurts)
                    lp.DamagePlayer(25, true, true, CauseOfDeath.Burning, 0, false, dir * 3f);
                    Sounds.Play("hurt", p, 0.8f, 1f, 16f);
                    Destroy(gameObject); return;
                }
            }
            // hit back, it hits monsters too: a ghast dies of it (Minecraft's 1000 damage), anything else takes the blast
            if (deflector != NoOne && Physics.SphereCast(p, 0.6f, dir, out var mh, step, 1 << 19, QueryTriggerInteraction.Collide))
            {
                var e = mh.collider.GetComponentInParent<EnemyAI>();
                if (e != null && !e.isEnemyDead)
                {
                    if (BlockNet.IsServer)
                    {
                        var by = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.playerClientId == deflector);
                        if (e is GhastAI)
                        {
                            Returned++;
                            if (by != null) Advancements.ServerGrant(by.actualClientId, "return_to_sender");
                            e.HitEnemy(1000, by, true);
                        }
                        else e.HitEnemy(2, by, true);
                        ServerBlast(p);
                    }
                    Destroy(gameObject); return;
                }
            }
            // the world: it stops, and the host sets fire there
            if (Physics.SphereCast(p, 0.15f, dir, out var hit, step, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EnemyAI>() == null)
                {
                    if (big) { if (BlockNet.IsServer) ServerBlast(hit.point + hit.normal * 0.3f); Destroy(gameObject); return; }
                    if (BlockNet.IsServer) Fire.ServerIgniteAt(hit.point + hit.normal * 0.3f);
                    Sounds.Play("extinguish", hit.point, 0.4f, 1.6f, 16f);
                    Destroy(gameObject); return;
                }
            }
            transform.position = p + dir * step;
        }

        /// <summary>Host: a ghast's fireball goes off (Minecraft's power 1: blocks go, it hurts, it sets a fire).</summary>
        public static void ServerBlast(Vector3 at)
        {
            float r = 1.6f * BlockWorld.S;
            try { ServerLogic.ExplodeBlocks(at, r, -1, carveGround: true); } catch (System.Exception e) { Plugin.Log.LogWarning("ghast fireball blocks: " + e.Message); }
            BlockNet.ServerExplosion(at, r);
            Fire.ServerIgniteAt(at);
            Blasts++;
        }
        public static int Blasts; // (dev/tests)

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }
    }

    /// <summary>A blaze rod (#66): what a blaze drops; scrap the Company pays well for.</summary>
    public class BlazeRod : GrabbableObject
    {
        public const string Key = "blaze_rod";
        void Awake() => SpawnFix.Clear(gameObject);
    }
}
