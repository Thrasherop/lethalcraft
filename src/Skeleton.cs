using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's skeleton (#59): inside the facility ([Mobs] SkeletonRarity), with a bow. It keeps its distance and
    /// shoots arrows at a player it can see (they fall a little with distance; they hurt like a monster's hit, a bit
    /// less), and closes in when it can't see you. Killed, it drops bones: bone meal for crops. Its model comes from
    /// your Minecraft textures (entity/skeleton/skeleton.png).
    /// </summary>
    public class SkeletonAI : MinecraftMobAI
    {
        public const string Name = "Skeleton";
        public static EnemyType Type;
        protected override string Sound => "skeleton";
        protected override float ChaseSpeed => 4.6f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 10; // (up close it only pushes you with its bow)
        public const float ShootRange = 16f, Keep = 7f;

        float nextShot = 2f, aim;

        public override void Start()
        {
            base.Start();
            if (agent != null) agent.stoppingDistance = Keep;
        }

        bool Sees(PlayerControllerB p) => p != null && !Physics.Linecast(eye.position, p.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer || targetPlayer == null || currentBehaviourStateIndex != Chase) return;
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            bool sees = d <= ShootRange && Sees(targetPlayer);
            // (keeps its distance while it has a clear shot; walks in when it hasn't)
            if (agent != null) agent.stoppingDistance = sees ? Keep : 1f;
            if (!sees || Time.time < nextShot) return;
            nextShot = Time.time + Random.Range(1.8f, 3f);
            var from = eye.position + transform.forward * 0.4f;
            var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.5f + Random.insideUnitSphere * 0.4f;
            BlockNet.ServerArrow(from, Arrow.Aim(from, to));
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // bow up: both arms out in front while it has someone to shoot at
            bool aiming = currentBehaviourStateIndex == Chase;
            // (standing at its distance the agent doesn't turn it: it turns to face whoever it's shooting at)
            if (aiming && targetPlayer != null)
            {
                var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            }
            aim = Mathf.MoveTowards(aim, aiming ? 1f : 0f, Time.deltaTime * 3f);
            if (aim > 0f && armR != null && armL != null)
            {
                armR.localRotation = Quaternion.Slerp(armR.localRotation, Quaternion.Euler(-90f, 10f, 0f), aim);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, Quaternion.Euler(-90f, -25f, 0f), aim);
            }
        }

        /// <summary>Host: Minecraft's drops: 0-2 bones.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("bone", out var bone)) ModItems.ServerSpawnStack(bone, n, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.SkeletonRarity.Value <= 0) return;
            Type = Register<SkeletonAI>("Skeleton", Name, "Shoots arrows; drops bones", Plugin.SkeletonRarity.Value, 4, 2.5f, 3, 1.8f, BuildModel);
        }

        // ------------------------------------------------------------------ the model: Minecraft's skeleton, 64x32
        static void BuildModel(Transform parent)
        {
            // (a player-sized biped, like the zombie: about 2.2 m)
            float k = EntityModels.Px * BlockWorld.S * 0.9f; // (#100: as tall as a player, about 2.5 m; it was 0.8, 2.2 m)
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Skeleton", "entity/skeleton/skeleton", new Color(0.8f, 0.8f, 0.78f));
            Biped(parent, k, sharedMat, 2, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16), arm => Bow(arm, k));
        }

        /// <summary>A bow in its right hand (the item's own extruded sprite), upright.</summary>
        static void Bow(Transform arm, float k)
        {
            var go = new GameObject("Bow");
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 1f) * k;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 45f);
            go.transform.localScale = Vector3.one * (0.9f * k / 0.0805f);
            go.layer = 19;
            // (held by the wood in the middle of its arc, pixel (4.5, 9.5) from the bottom left; centred, the hand was on the string, #99)
            var sp = new GameObject("Sprite"); sp.transform.SetParent(go.transform, false); sp.layer = 19;
            sp.transform.localPosition = new Vector3(3.5f, -1.5f, 0f) / 16f;
            sp.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_bow") ? "item_bow" : "item_pickaxe");
            var mr = sp.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            Atlas.NoDecals(mr);
        }
    }

    /// <summary>A skeleton's arrow (#59): every machine flies its own copy and checks its own player, like the blaze's fireball.</summary>
    public class Arrow : MonoBehaviour
    {
        public const float Speed = 26f, Gravity = 6f, Life = 4f, StuckFor = 3f;
        public const int Hurt = 15;
        Vector3 vel; float age, stuck = -1f;

        /// <summary>The starting velocity to hit `to` from `from` (a little up, for the drop).</summary>
        public static Vector3 Aim(Vector3 from, Vector3 to)
        {
            float t = Vector3.Distance(from, to) / Speed;
            var v = (to - from) / Mathf.Max(t, 0.01f);
            v.y += 0.5f * Gravity * t;
            return v;
        }

        public static void Launch(Vector3 from, Vector3 vel)
        {
            var go = new GameObject("LMC_Arrow");
            go.transform.position = from;
            var a = go.AddComponent<Arrow>(); a.vel = vel;
            // Minecraft's arrow: the item's sprite, flying point first
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, -90f, -45f); // (the sprite's tip is its top right: turned along +z)
            model.transform.localScale = Vector3.one * 0.6f;
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_arrow") ? "item_arrow" : "item_pickaxe");
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            go.transform.rotation = Quaternion.LookRotation(vel);
            Sounds.Play("bow", from, 0.8f, Random.Range(0.9f, 1.15f), 24f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (stuck >= 0f) { stuck += Time.deltaTime; if (stuck > StuckFor) Destroy(gameObject); return; }
            if (age > Life) { Destroy(gameObject); return; }
            vel += Vector3.down * Gravity * Time.deltaTime;
            var p = transform.position;
            var step = vel * Time.deltaTime;
            // the local player: a hit
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp != null && !lp.isPlayerDead)
            {
                var a = lp.transform.position + Vector3.up * 0.3f; var b = lp.transform.position + Vector3.up * 2.0f;
                if (SegmentDistance(p, p + step, a, b) < 0.5f)
                {
                    lp.DamagePlayer(Hurt, true, true, CauseOfDeath.Stabbing, 0, false, vel.normalized * 2f);
                    Sounds.Play("bowhit", p, 0.8f, 1f, 16f);
                    Hits++;
                    Destroy(gameObject); return;
                }
            }
            // the world: it sticks there a moment
            if (Physics.Raycast(p, step.normalized, out var hit, step.magnitude, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<EnemyAI>() == null)
            {
                transform.position = hit.point - step.normalized * 0.15f;
                stuck = 0f;
                Sounds.Play("bowhit", hit.point, 0.5f, 1.2f, 14f);
                return;
            }
            transform.position = p + step;
            if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        }

        public static int Hits; // (dev/tests: arrows that hit the local player)

        /// <summary>The closest the segments p0-p1 and a-b come to each other (sampled: the arrow moves less than a metre a frame).</summary>
        static float SegmentDistance(Vector3 p0, Vector3 p1, Vector3 a, Vector3 b)
        {
            float best = float.MaxValue;
            for (int i = 0; i <= 4; i++)
            {
                var q = Vector3.Lerp(p0, p1, i / 4f);
                var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(q - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector3.Distance(q, a + ab * t));
            }
            return best;
        }
    }
}
