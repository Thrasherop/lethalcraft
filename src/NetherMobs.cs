using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's ghast (#62): in soul sand valleys and basalt deltas. It drifts high up (as high as the room lets it),
    /// moaning; seeing a player it shrieks and spits a fireball that blows up where it lands (blocks too: Minecraft's
    /// power 1), every few seconds. Fragile: a couple of hits. Drops gunpowder. Model from entity/ghast/ghast.png
    /// (1.21.6's is twice the old resolution), drawn smaller than Minecraft's so it fits the Nether's rooms.
    /// </summary>
    public class GhastAI : MinecraftMobAI
    {
        public const string Name = "Ghast";
        public static EnemyType Type;
        protected override string Sound => "ghast";
        protected override float SightRange => 28f;
        protected override float ChaseSpeed => 2.6f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 0;
        public const float ShootRange = 28f, Size = 1.9f; // (its body's side, m)
        float nextShot = 4f, chargeAt = -1f, sway;
        readonly List<Transform> tentacles = new List<Transform>();
        Transform body;
        public static int Shots; // (dev/tests)

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) { body = m.Find("Body"); foreach (Transform t in m) if (t.name.StartsWith("Tentacle")) tentacles.Add(t); }
            if (agent != null) { agent.stoppingDistance = 9f; agent.baseOffset = 1f; }
        }

        float unseenFor; Vector3? lookout; float lookoutUntil;
        public static int Repositions; // (dev/tests)
        public static bool DevNoReposition; // (dev: "ghastmove 0", as before #191, to compare)

        static bool ClearLine(Vector3 a, Vector3 b) => !Physics.Linecast(a, b, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer) return;
            if (targetPlayer == null || currentBehaviourStateIndex != Chase) { lookout = null; if (flying && !landing) StartLanding(); return; }
            var head = targetPlayer.gameplayCamera.transform.position;
            bool sees = ClearLine(eye.position, head);
            // (#191: hidden from it, it floats round to where it can see you again, as Minecraft's ghast drifts about
            // until it has a shot; it had hovered out of sight, still chasing, never shooting again)
            unseenFor = sees ? 0f : unseenFor + AIIntervalTime;
            if (sees) { if (lookout != null) { lookout = null; agent.stoppingDistance = 9f; } }
            else if (unseenFor > 1.5f && flying && !DevNoReposition)
            {
                // (in the air already: somewhere else in the air that sees you)
                if (Time.time > flyPickAt) { flyPickAt = Time.time + 3f; var a = FindAirSpot(head); if (a != null) { StartFlying(a.Value); Repositions++; } }
            }
            else if (unseenFor > 1.5f && agent != null && agent.enabled && !DevNoReposition)
            {
                if (lookout == null || Time.time > lookoutUntil) { lookout = FindLookout(head); lookoutUntil = Time.time + 6f; if (lookout != null) Repositions++; }
                if (lookout != null)
                {
                    movingTowardsTargetPlayer = false;
                    agent.stoppingDistance = 0.5f;
                    SetDestinationToPosition(lookout.Value);
                }
                // (nowhere on its navmesh sees you, or it went there and still doesn't: it takes off, as Minecraft's ghast
                // flies where it likes)
                if ((lookout == null || unseenFor > 8f) && !DevNoFlight && Time.time > flyPickAt)
                {
                    flyPickAt = Time.time + 3f;
                    var a = FindAirSpot(head);
                    if (a != null) { lookout = null; StartFlying(a.Value); Repositions++; }
                }
            }
            if (chargeAt > 0f) return;
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            if (Time.time >= nextShot && d <= ShootRange && sees && !SafeShot(head)) { HeldFire++; if (flying || !DevNoFlight) flyPickAt = 0f; unseenFor = 2f; } // (it'd blow itself up: elsewhere)
            else if (Time.time >= nextShot && d <= ShootRange && sees)
            {
                chargeAt = Time.time + 1f; // (Minecraft's: a shriek, a second, then the fireball)
                BlockNet.ServerSound(transform.position + Vector3.up * Size, "ghast.charge", 1f, 1f);
            }
        }

        /// <summary>A spot on its navmesh 6 to 14 m from the head it's after, from where (floating at its height) it sees it.</summary>
        Vector3? FindLookout(Vector3 head)
        {
            float lift = eye.position.y - (transform.position.y - agent.baseOffset);
            var path = new NavMeshPath();
            for (int i = 0; i < 24; i++)
            {
                // (around you, or around where it is now: somewhere it can get to, a path all the way there)
                var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                var at = i % 2 == 0 ? head + dir * Random.Range(5f, 15f) : transform.position + dir * Random.Range(3f, 10f);
                if (!NavMesh.SamplePosition(at, out var h, 4f, agent.areaMask)) continue;
                if (Vector3.Distance(h.position, head) < 4f || !ClearLine(h.position + Vector3.up * lift, head)) continue;
                if (!agent.CalculatePath(h.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                return h.position;
            }
            return null;
        }

        // ------------------------------------------------------------------ flight (#191)
        // Minecraft's ghast flies where it likes (RandomFloatAroundGoal, GhastMoveControl). Ours walks a navmesh, floating
        // over it; hidden from its target with nowhere on the navmesh to see from (a hole, a chimney, a ledge, the
        // Nether's narrow entrance), it leaves the navmesh and flies to a spot in the air that sees you, and from one such
        // spot to the next. Done chasing, it flies down to the navmesh and walks it again.
        bool flying, landing; Vector3 flyTo, landAt; float flyPickAt, landingSince;
        public static int Flights, Landings; // (dev/tests)
        public static bool DevNoFlight; // (dev: "ghastmove fly 0", as before)
        public static string DevWhy = ""; // (dev: the last air-spot search)
        public bool Flying => flying;
        static int Mask => StartOfRound.Instance.collidersAndRoomMaskAndDefault;
        // (what it fills: its body and tentacles, from its feet to 3.5 m up)
        static readonly Vector3 Half = new Vector3(Size * 0.5f, 1.75f, Size * 0.5f);
        static Vector3 Mid(Vector3 at) => at + Vector3.up * 1.8f;
        static bool Solid(Collider h) => h.GetComponentInParent<EnemyAI>() == null && h.GetComponentInParent<PlayerControllerB>() == null && h.GetComponentInParent<GrabbableObject>() == null;

        /// <summary>(dev) what's in its way at a spot: the colliders in its box there.</summary>
        public static string DevFit(Vector3 at) => (OpenGround(at) ? "" : "ground ") + string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(Physics.OverlapBox(Mid(at), Half, Quaternion.identity, Mask, QueryTriggerInteraction.Ignore), Solid), h => $"{h.name} L{h.gameObject.layer} {h.GetType().Name} b={h.bounds.center}/{h.bounds.size}"));

        /// <summary>(dev) for spots straight over a head: does it fit, and what's on the line from its eye down to the head.</summary>
        public string DevOver(Vector3 head)
        {
            var eyeUp = eye.position - transform.position;
            var sb = new System.Text.StringBuilder($"eyeUp={eyeUp} ");
            for (float k = 1f; k <= 8f; k += 1f)
            {
                var at = head + Vector3.up * k;
                string line = Physics.Linecast(at + eyeUp, head, out var hit, Mask, QueryTriggerInteraction.Ignore) ? $"{hit.collider.name} L{hit.collider.gameObject.layer} at {hit.point}" : "clear";
                sb.Append($"[+{k}: fits={Fits(at)} line={line}] ");
            }
            return sb.ToString();
        }

        public static int HeldFire; // (dev/tests)

        /// <summary>Where its fireball would go off (the first thing on its way, or you) is out of its own blast's reach
        /// (3 m round, the game's): hovering over a hole, a shot that caught the edge blew it up.</summary>
        bool SafeShot(Vector3 head)
        {
            var to = head + Vector3.down * 0.5f;
            var from = eye.position + (to - eye.position).normalized * (Size * 0.75f);
            var dir = to - from; float len = dir.magnitude;
            var burst = to;
            foreach (var h in Physics.SphereCastAll(from, 0.3f, dir / len, len, Mask, QueryTriggerInteraction.Ignore))
                if (Solid(h.collider) && h.distance > 0f && Vector3.Distance(from, h.point) < Vector3.Distance(from, burst)) burst = h.point;
            // (the nearest point of its body to the burst)
            var body = new Bounds(Mid(transform.position), Half * 2f);
            return Vector3.Distance(body.ClosestPoint(burst), burst) > 3.4f;
        }

        static bool Fits(Vector3 at)
        {
            foreach (var h in Physics.OverlapBox(Mid(at), Half, Quaternion.identity, Mask, QueryTriggerInteraction.Ignore)) if (Solid(h)) return false;
            return OpenGround(at);
        }

        /// <summary>The ground cells its box reaches into are open (dug, or air): undug rock has no collider until a
        /// neighbour is dug, so the colliders alone would let it fly into the rock.</summary>
        static bool OpenGround(Vector3 at)
        {
            var m = Mid(at);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                var c = Ground.CellOf(m + Vector3.Scale(Half * 0.9f, new Vector3(x, y, z)));
                if (!Ground.IsDug(c) && Ground.IsSolidCell(c)) return false;
            }
            return true;
        }

        static string lastBlock = ""; // (dev: what stopped the last flight path checked)
        static bool FreeFlight(Vector3 from, Vector3 to)
        {
            var d = to - from; float len = d.magnitude;
            if (len < 0.01f) return true;
            lastBlock = "";
            // (what it touches already, at distance 0, doesn't stop it moving: brushing a wall as it took off, it couldn't go anywhere)
            foreach (var h in Physics.BoxCastAll(Mid(from), Half * 0.95f, d / len, Quaternion.identity, len, Mask, QueryTriggerInteraction.Ignore)) if (h.distance > 0f && Solid(h.collider)) { lastBlock = $"{h.collider.name}@{h.distance:F1}"; return false; }
            for (float t = Mathf.Min(1f, len); t <= len + 0.01f; t += 1f) if (!OpenGround(from + d / len * Mathf.Min(t, len))) { lastBlock = $"rock@{t:F1}"; return false; }
            return true;
        }

        /// <summary>A spot in the air it fits in, can fly to (straight, or up then across), and sees the head from.</summary>
        Vector3? FindAirSpot(Vector3 head)
        {
            var eyeUp = eye.position - transform.position;
            bool nether = NetherFortress.Root != null && NetherFortress.Contains(transform.position);
            var here = transform.position;
            int outside = 0, near = 0, nofit = 0, noline = 0, noway = 0; string wayWhy = "";
            for (int i = 0; i < 48; i++)
            {
                var dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                // (over you, round you, or round where it is now)
                var at = i % 3 == 0 ? head + dir * Random.Range(0f, 3f) + Vector3.up * Random.Range(1f, 8f)
                    : i % 3 == 1 ? head + dir * Random.Range(5f, 14f) + Vector3.up * Random.Range(-2f, 6f)
                    : here + dir * Random.Range(3f, 10f) + Vector3.up * Random.Range(-1f, 5f);
                if (nether && !NetherGround.InRegion(at)) { outside++; continue; }
                // (its blast where it lands hurts it too, 3 m round: it keeps well back, as Minecraft's does)
                if (Vector3.Distance(at + eyeUp, head) < 7f) { near++; continue; }
                if (!Fits(at)) { nofit++; continue; }
                if (!ClearLine(at + eyeUp, head)) { noline++; continue; }
                var up = new Vector3(here.x, at.y, here.z);
                if (!FreeFlight(here, at) && !(FreeFlight(here, up) && FreeFlight(up, at))) { noway++; wayWhy = lastBlock; continue; }
                DevWhy = $"found after {i + 1}";
                return at;
            }
            DevWhy = $"none: outside={outside} near={near} nofit={nofit} noline={noline} noway={noway} ({wayWhy}) from {here} fits={Fits(here)}";
            return null;
        }

        void StartFlying(Vector3 to)
        {
            if (!flying)
            {
                flying = true; Flights++;
                if (agent != null) agent.enabled = false;
                moveTowardsDestination = false;
            }
            landing = false; flyTo = to;
        }

        /// <summary>Done chasing: down to the nearest navmesh below or about.</summary>
        void StartLanding()
        {
            if (agent == null || !NavMesh.SamplePosition(transform.position, out var h, 20f, agent.areaMask)) return; // (nowhere: it stays up)
            landing = true; landingSince = Time.time; landAt = h.position; flyTo = h.position + Vector3.up;
        }

        void Land()
        {
            flying = landing = false; Landings++;
            agent.baseOffset = 1f;
            agent.enabled = true;
            agent.Warp(landAt);
            moveTowardsDestination = true;
        }

        /// <summary>Owner, flying: towards its spot (stopping short of whatever's in the way), facing its target.</summary>
        void FlyStep()
        {
            var to = flyTo - transform.position;
            float step = (landing ? RoamSpeed * 1.5f : ChaseSpeed) * Time.deltaTime;
            if (to.magnitude > 0.05f)
            {
                var mv = Vector3.ClampMagnitude(to, step);
                // (up or down first if it can't go straight: the way it came round)
                var vert = Vector3.up * Mathf.Sign(to.y) * Mathf.Min(step, Mathf.Abs(to.y));
                if (FreeFlight(transform.position, transform.position + mv)) transform.position += mv;
                else if (Mathf.Abs(to.y) > 0.05f && FreeFlight(transform.position, transform.position + vert)) transform.position += vert;
                else if (!landing) flyPickAt = 0f; // (stuck: pick another spot)
            }
            else if (landing) { Land(); return; }
            // (landing and stuck a long while: down onto the navmesh anyway)
            if (landing && Time.time - landingSince > 10f) { Land(); return; }
            var look = targetPlayer != null && !landing ? targetPlayer.transform.position - transform.position : to;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 180f * Time.deltaTime);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            if (IsOwner && flying) FlyStep();
            // tentacles sway; it floats as high as the room lets it (the agent walks the floor beneath)
            sway += Time.deltaTime * 2f;
            for (int i = 0; i < tentacles.Count; i++) tentacles[i].localRotation = Quaternion.Euler(12f * Mathf.Sin(sway + i * 0.7f), 0f, 8f * Mathf.Cos(sway * 0.8f + i));
            if (IsOwner && agent != null && agent.enabled)
            {
                float clear = Physics.Raycast(transform.position - Vector3.up * agent.baseOffset + Vector3.up * 0.2f, Vector3.up, out var h, 12f, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore) ? h.distance : 12f;
                float want = Mathf.Clamp(clear - (Size + 1.6f) - 0.3f, 0.2f, 4.5f);
                agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, want, Time.deltaTime * 1.5f);
                // (it faces whoever it's after)
                bool facing = currentBehaviourStateIndex == Chase && targetPlayer != null;
                agent.updateRotation = !facing;
                if (facing)
                {
                    var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 180f * Time.deltaTime);
                }
            }
            if (IsServer && chargeAt > 0f && Time.time >= chargeAt)
            {
                chargeAt = -1f; nextShot = Time.time + Random.Range(3f, 5f);
                if (targetPlayer != null && !targetPlayer.isPlayerDead)
                {
                    // (out along the line it saw you by, from its eye: from the front of its face it clipped the edge of the
                    // hole it was shooting down, and blew itself up, #191)
                    var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.5f;
                    var from = eye.position + (to - eye.position).normalized * (Size * 0.75f);
                    if (SafeShot(targetPlayer.gameplayCamera.transform.position)) { BlockNet.ServerFireball(from, (to - from).normalized, big: true); Shots++; }
                    else HeldFire++;
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other) { } // (it doesn't touch: it shoots)

        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("gunpowder", out var g)) ModItems.ServerSpawnStack(g, n, at);
            // (Minecraft's: 0-1 ghast tear, for potions of Regeneration)
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("ghast_tear", out var tear)) ModItems.ServerSpawnStack(tear, 1, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherGhasts.Value) return;
            // (only the Nether's spawning makes them)
            Type = Register<GhastAI>("Ghast", Name, "Spits exploding fireballs", 0, 3, Size + 1.6f, 3, 1.4f, BuildModel, length: Size);
        }

        // ------------------------------------------------------------------ the model: Minecraft's ghast (a 64x32 layout at 2x)
        static void BuildModel(Transform parent)
        {
            float k = Size / 16f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Ghast", "entity/ghast/ghast", new Color(0.95f, 0.95f, 0.95f));
            float tex = sharedMat.mainTexture != null && sharedMat.mainTexture.width >= 128 ? 2f : 1f;
            // body 16x16x16 at (0,0), tentacles 2xNx2 at (0,0), nine in a staggered 3x3 under it
            EntityModels.Part(parent, "Body", new Vector3(0, 14 + 8, 0) * (16f / 16f), new Vector3(16, 16, 16), 0, 0, k, sharedMat, texScale: tex);
            var rng = new System.Random(1847);
            for (int i = 0; i < 9; i++)
            {
                float x = ((i % 3 - (i / 3 % 2) * 0.5f + 0.25f) / 2f * 2f - 1f) * 5f, z = ((i / 3) / 2f * 2f - 1f) * 5f;
                int len = rng.Next(7) + 8;
                var t = new GameObject("Tentacle" + i).transform;
                t.SetParent(parent, false);
                t.localPosition = new Vector3(x, 14.2f, z) * k;
                EntityModels.Part(t, "T", new Vector3(0, -len / 2f, 0), new Vector3(2, len, 2), 0, 0, k, sharedMat, texScale: tex);
            }
        }
    }

    /// <summary>
    /// Minecraft's magma cube (#62): in basalt deltas. It hops after you, hits on contact (harder the bigger it is), and
    /// a big one splits into two to four medium ones when killed, each of those into small ones. Small ones drop magma
    /// cream now and then. Model from entity/slime/magmacube.png: eight slices that pull apart as it jumps, glowing core.
    /// </summary>
    public class MagmaCubeAI : MinecraftMobAI
    {
        public static EnemyType Big, Medium, Small;
        protected virtual int CubeSize => 4;
        /// <summary>What it splits into, and what the smallest drops (a slime shares all this: #133).</summary>
        protected virtual EnemyType Smaller => CubeSize == 4 ? Medium : Small;
        protected virtual string SmallDrop => "magma_cream";
        /// <summary>(Minecraft's magma cube: 0-1 magma cream from a big or medium one, a quarter of the time; a small one drops
        /// nothing. A slime: only the small ones, 0-2 slimeballs.)</summary>
        protected virtual int SmallDropCount => 0;
        protected virtual int BigDropCount => Random.value < 0.25f ? 1 : 0;
        protected override int Xp => CubeSize;
        protected override string Sound => "magma";
        protected override float ChaseSpeed => 4.0f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => CubeSize == 4 ? 30 : CubeSize == 2 ? 20 : 10;
        float hopPhase;
        readonly Transform[] slices = new Transform[8];

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) for (int i = 0; i < 8; i++) slices[i] = m.Find("Slice" + i);
            hopPhase = Random.value;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // hops: it moves in bounds (a burst of speed, then a stop), and stretches as it leaves the ground
            bool chasing = currentBehaviourStateIndex == Chase;
            hopPhase += Time.deltaTime / (chasing ? 0.75f : 1.4f);
            float f = hopPhase % 1f;
            bool air = f < 0.45f;
            if (IsOwner && agent != null) agent.speed = air ? (chasing ? ChaseSpeed : RoamSpeed) * 2f : 0.05f;
            float up = air ? Mathf.Sin(f / 0.45f * Mathf.PI) : 0f;
            float k = EntityModels.Px * BlockWorld.S * CubeSize * 0.5f;
            var model = transform.Find("Model");
            if (model != null) model.localPosition = Vector3.up * up * 0.5f * CubeSize * 0.35f;
            for (int i = 0; i < 8; i++)
                if (slices[i] != null) slices[i].localPosition = new Vector3(0f, (7.5f - i) * k * (1f + up * 0.6f), 0f);
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (CubeSize > 1)
            {
                int cream = BigDropCount;
                if (cream > 0 && ModItems.ByKey.TryGetValue(SmallDrop, out var bc)) ModItems.ServerSpawnStack(bc, cream, at);
                // it splits: two to four of the next size down
                var next = Smaller;
                int n = Random.Range(2, 5);
                MobSpawns.DevSpawnUntil = Time.time + 1f; // (its halves aren't a natural spawn: the tests' "spawns off" lets them be)
                for (int i = 0; i < n && next != null; i++)
                {
                    var p = at + new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f));
                    if (NavMesh.SamplePosition(p, out var h, 2f, NavMesh.AllAreas)) p = h.position;
                    RoundManager.Instance.SpawnEnemyGameObject(p, Random.Range(0f, 360f), -1, next);
                }
                Splits += n;
                return;
            }
            int drop = SmallDropCount;
            if (drop > 0 && ModItems.ByKey.TryGetValue(SmallDrop, out var mc)) ModItems.ServerSpawnStack(mc, drop, at);
        }

        public static int Splits; // (dev/tests)
        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherMagmaCubes.Value) return;
            Big = Register<MagmaCubeAI>("MagmaCube", "Magma Cube", "Splits when killed", 0, 4, 4 * 0.7f, 4, 1.4f, b => BuildModel(b, 4), length: 4 * 0.7f);
            Medium = Register<MagmaCubeMediumAI>("MagmaCubeMedium", "Magma Cube", "Splits when killed", 0, 2, 2 * 0.7f, 8, 1.4f, b => BuildModel(b, 2), length: 2 * 0.7f);
            Small = Register<MagmaCubeSmallAI>("MagmaCubeSmall", "Magma Cube", "Small", 0, 1, 0.7f, 16, 1.4f, b => BuildModel(b, 1), length: 0.7f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's magma cube (64x64)
        static void BuildModel(Transform parent, int size)
        {
            float k = EntityModels.Px * BlockWorld.S * size * 0.5f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_MagmaCube", "entity/slime/magmacube", new Color(0.35f, 0.08f, 0.02f));
            for (int i = 0; i < 8; i++)
            {
                int u = i < 4 ? 0 : 32, v = (i < 4 ? i : i - 4) * 9;
                EntityModels.Part(parent, "Slice" + i, new Vector3(0, 7.5f - i, 0), new Vector3(8, 1, 8), u, v, k, sharedMat);
            }
            EntityModels.Part(parent, "Core", new Vector3(0, 4, 0), new Vector3(4, 4, 4), 24, 40, k, sharedMat);
        }
    }

    public class MagmaCubeMediumAI : MagmaCubeAI { protected override int CubeSize => 2; }
    public class MagmaCubeSmallAI : MagmaCubeAI { protected override int CubeSize => 1; }
}
