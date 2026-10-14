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
        public const string Name = "Blaze";
        public static EnemyType Type;
        protected override string Sound => "blaze";
        protected override float ChaseSpeed => 2.6f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 15;
        public const float ShootRange = 18f;

        float nextBurst = 3f, rodAngle, bob;
        int burstLeft; float nextShot;
        readonly List<Transform> rods = new List<Transform>();

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
            // the rods turn, the head bobs (Minecraft's blaze)
            rodAngle += Time.deltaTime * 90f;
            bob += Time.deltaTime * 2f;
            for (int i = 0; i < rods.Count; i++)
            {
                int ring = i / 4; float r = new[] { 9f, 7f, 5f }[ring] * K;
                float a = (rodAngle * (ring == 1 ? -1f : 1f) + i % 4 * 90f + ring * 45f) * Mathf.Deg2Rad;
                rods[i].localPosition = new Vector3(Mathf.Cos(a) * r, (new[] { 15f, 10f, 5f }[ring] + Mathf.Sin(bob + i) * 1.5f) * K, Mathf.Sin(a) * r);
            }
            if (IsServer && burstLeft > 0 && Time.time >= nextShot && targetPlayer != null)
            {
                burstLeft--; nextShot = Time.time + 0.3f;
                var from = eye.position + transform.forward * 0.5f;
                var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.4f + Random.insideUnitSphere * 0.6f;
                BlockNet.ServerFireball(from, (to - from).normalized);
            }
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.5f) ModItems.ServerSpawnScrapItem(BlazeRod.Key, at);
        }

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

    /// <summary>A blaze's fireball (#66): every machine flies its own copy; each checks its own player; the host sets fires.</summary>
    public class Fireball : MonoBehaviour
    {
        public const float Speed = 16f, Life = 5f;
        Vector3 dir; float age;

        public static void Launch(Vector3 from, Vector3 dir)
        {
            var go = new GameObject("LMC_Fireball");
            go.transform.position = from;
            var f = go.AddComponent<Fireball>(); f.dir = dir;
            // Minecraft's small fireball: the fire charge's look, glowing
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_fire_charge") ? "item_fire_charge" : "fire_0");
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Emissive; Atlas.NoDecals(mr);
            go.transform.localScale = Vector3.one * 0.45f;
            var lgo = new GameObject("light"); lgo.transform.SetParent(go.transform, false);
            var hd = lgo.AddHDLight(UnityEngine.Rendering.HighDefinition.HDLightTypeAndShape.Point);
            hd.EnableShadows(false); hd.affectsVolumetric = false;
            hd.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lumen; hd.intensity = 600f; hd.range = 6f;
            lgo.GetComponent<Light>().color = new Color(1f, 0.55f, 0.2f);
            Sounds.Play("blaze.shoot", from, 0.8f, Random.Range(0.9f, 1.1f), 24f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
            float step = Speed * Time.deltaTime;
            var p = transform.position;
            // the local player: a hit
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp != null && !lp.isPlayerDead)
            {
                var a = lp.transform.position + Vector3.up * 0.3f; var b = lp.transform.position + Vector3.up * 2.0f;
                var closest = ClosestOnSegment(a, b, p);
                if (Vector3.Distance(closest, p) < 0.6f)
                {
                    lp.DamagePlayer(25, true, true, CauseOfDeath.Burning, 0, false, dir * 3f);
                    Sounds.Play("hurt", p, 0.8f, 1f, 16f);
                    Destroy(gameObject); return;
                }
            }
            // the world: it stops, and the host sets fire there
            if (Physics.SphereCast(p, 0.15f, dir, out var hit, step, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EnemyAI>() == null)
                {
                    if (BlockNet.IsServer) Fire.ServerIgniteAt(hit.point + hit.normal * 0.3f);
                    Sounds.Play("extinguish", hit.point, 0.4f, 1.6f, 16f);
                    Destroy(gameObject); return;
                }
            }
            transform.position = p + dir * step;
        }

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
