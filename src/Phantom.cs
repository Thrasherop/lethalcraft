using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Phantoms (#141; the idea is @dvuiodvojd's, from #132). Only outdoors, and only after 6:00 PM; far more of them on
    /// an eclipsed or stormy moon ([Mobs] Phantoms, PhantomChance). One circles silently high up over a player outside,
    /// screeches a couple of seconds before it dives, and strikes. It hates bright light: a Pro-flashlight or a laser
    /// pointer aimed straight at it while it dives blinds it a while: it gives up the dive and climbs back into the sky.
    /// Drops phantom membrane (it mends an elytra; a potion of Slow Falling).
    /// </summary>
    [HarmonyPatch]
    public class PhantomAI : MinecraftMobAI
    {
        public static EnemyType Type;
        public static int Spawned, Dives, Blinded, Screeches, Bumps, Skipped; // (dev/tests)
        protected override string Sound => "phantom";
        protected override bool Swims => true; // (it flies: no navmesh, its own movement)
        protected override int Damage => 20;
        protected override bool HatesLight => false;
        public const float CircleHeight = 22f, CircleRadius = 11f, CircleSpeed = 7f, DiveSpeed = 15f, ScreechAhead = 2.2f;

        enum Phase { Circle, Screech, Dive, Climb, Blind }
        Phase phase = Phase.Circle;
        float phaseUntil, angle;
        PlayerControllerB prey;
        Vector3 diveAt;
        Transform wingL, wingR;
        public string State => phase.ToString();
        protected override int Xp => 5;

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            if (m != null) { wingL = m.Find("WingL"); wingR = m.Find("WingR"); }
            angle = Random.value * 360f;
            phaseUntil = Time.time + (DevCircle >= 0f ? DevCircle : Random.Range(10f, 20f));
        }

        protected override void SwimAI()
        {
            if (!IsServer) return;
            if (prey == null || prey.isPlayerDead || prey.isInsideFactory || !prey.isPlayerControlled) prey = Outdoors().OrderBy(p => Vector3.Distance(p.transform.position, transform.position)).FirstOrDefault();
            switch (phase)
            {
                case Phase.Circle:
                    if (prey != null && Time.time > phaseUntil) { phase = Phase.Screech; phaseUntil = Time.time + ScreechAhead; Screeches++; BlockNet.ServerSound(transform.position, "phantom.swoop", 1f, Random.Range(0.9f, 1.1f)); }
                    break;
                case Phase.Screech:
                    if (Time.time > phaseUntil && prey != null)
                    {
                        var at = prey.transform.position + Vector3.up * 1.2f;
                        // (#222: no way through to you, under a roof: it doesn't swoop, it circles on, as Minecraft's can't
                        // swoop through blocks)
                        if (Blocked(transform.position, at - transform.position, 1.2f)) { phase = Phase.Circle; phaseUntil = Time.time + Random.Range(4f, 8f); Skipped++; break; }
                        phase = Phase.Dive; phaseUntil = Time.time + 3f; diveAt = at; Dives++; SwitchToBehaviourState(Chase);
                    }
                    break;
                case Phase.Dive:
                    if (Lit()) { phase = Phase.Blind; phaseUntil = Time.time + 10f; Blinded++; SwitchToBehaviourState(Roam); BlockNet.ServerSound(transform.position, "phantom.hurt", 1f, 1.3f); break; }
                    if (Time.time > phaseUntil || Vector3.Distance(transform.position, diveAt) < 1.2f) { phase = Phase.Climb; phaseUntil = Time.time + 3f; SwitchToBehaviourState(Roam); }
                    break;
                case Phase.Climb:
                case Phase.Blind:
                    if (Time.time > phaseUntil) { phase = Phase.Circle; phaseUntil = Time.time + (DevCircle >= 0f ? DevCircle : Random.Range(8f, 16f)); }
                    break;
            }
        }

        static int Mask => StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer);

        /// <summary>Something solid (the level, a block) in its way over that move, short of the last `spare` metres (a
        /// player, items and monsters don't count).</summary>
        static bool Blocked(Vector3 from, Vector3 move, float spare)
        {
            float len = move.magnitude - spare;
            if (len <= 0.001f) return false;
            foreach (var h in Physics.SphereCastAll(from, 0.45f, move.normalized, len, Mask, QueryTriggerInteraction.Ignore))
            {
                if (h.distance <= 0f) continue; // (already touching it: moving off it isn't stopped)
                if (h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<PlayerControllerB>() != null || h.collider.GetComponentInParent<GrabbableObject>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>Host: someone outside aims a Pro-flashlight or a laser pointer, on, straight at it.</summary>
        bool Lit()
        {
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead || p.isInsideFactory) continue;
                if (!(p.currentlyHeldObjectServer is FlashlightItem f) || !f.isBeingUsed || f.itemProperties == null) continue;
                string n = f.itemProperties.itemName.ToLower();
                bool laser = n.Contains("laser"), pro = n.Contains("pro");
                if (!laser && !pro) continue;
                var cam = p.gameplayCamera.transform; var to = transform.position - cam.position;
                if (to.magnitude > (laser ? 60f : 35f)) continue;
                if (Vector3.Angle(cam.forward, to) > (laser ? 6f : 14f)) continue;
                if (Physics.Linecast(cam.position, transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                return true;
            }
            return false;
        }

        /// <summary>(dev/tests) What Lit() sees of this player's light: on, kind, angle and distance to it, blocked.</summary>
        public string LightDebug(PlayerControllerB p)
        {
            var f = p.currentlyHeldObjectServer as FlashlightItem;
            var cam = p.gameplayCamera.transform; var to = transform.position - cam.position;
            bool blocked = Physics.Linecast(cam.position, transform.position, out var hit, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
            return $"light={(f == null ? "-" : f.itemProperties.itemName + (f.isBeingUsed ? " on" : " off"))} angle={Vector3.Angle(cam.forward, to):F0} dist={to.magnitude:F0} blocked={(blocked ? hit.collider.name : "-")}";
        }

        static System.Collections.Generic.IEnumerable<PlayerControllerB> Outdoors() =>
            StartOfRound.Instance.allPlayerScripts.Where(p => p != null && p.isPlayerControlled && !p.isPlayerDead && !p.isInsideFactory && !BlockWorld.InShip(p.transform.position));

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // its wings beat (slowly while it glides round)
            float flap = Mathf.Sin(Time.time * (phase == Phase.Dive ? 4f : 7f)) * (phase == Phase.Dive ? 8f : 25f);
            if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, 0f, flap);
            if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 0f, -flap);
            if (!IsOwner) return;
            Vector3 target; float speed;
            var center = prey != null ? prey.transform.position : StartOfRound.Instance.elevatorTransform.position;
            switch (phase)
            {
                case Phase.Dive: target = diveAt; speed = DiveSpeed; break;
                case Phase.Climb: case Phase.Blind:
                    target = transform.position + Vector3.up * 10f + transform.forward * 4f; speed = phase == Phase.Blind ? 9f : 7f;
                    if (transform.position.y > center.y + CircleHeight) target.y = transform.position.y;
                    break;
                default:
                    angle += CircleSpeed / CircleRadius * Mathf.Rad2Deg * Time.deltaTime;
                    target = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * CircleRadius + Vector3.up * CircleHeight; speed = CircleSpeed;
                    break;
            }
            var to = target - transform.position;
            if (to.sqrMagnitude > 0.0001f)
            {
                var step = Vector3.ClampMagnitude(to, speed * Time.deltaTime);
                // (#222: blocks and the level are solid to it, as in Minecraft: it had flown through them, and slid along
                // inside them. A swoop that runs into something ends (Minecraft's horizontalCollision); otherwise it goes up
                // and over)
                if (Blocked(transform.position, step, 0f))
                {
                    Bumps++;
                    if (phase == Phase.Dive && IsServer) { phase = Phase.Climb; phaseUntil = Time.time + 3f; SwitchToBehaviourState(Roam); }
                    var up = Vector3.up * speed * Time.deltaTime;
                    step = Blocked(transform.position, up, 0f) ? Vector3.zero : up;
                }
                transform.position += step;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 4f * Time.deltaTime);
            }
            SyncPositionToClients();
        }

        /// <summary>Host: Minecraft's drops: 0-1 phantom membrane.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("phantom_membrane", out var m)) ModItems.ServerSpawnStack(m, 1, at);
        }

        // ------------------------------------------------------------------ spawning: after 6 PM, outside
        static float nextRoll;
        public static float DevChance = -1f;
        /// <summary>(dev `phantoms circle`) seconds it circles before each dive; -1: Minecraft's 8-20.</summary>
        public static float DevCircle = -1f;

        /// <summary>Host, each frame: now and then, after 6 PM with someone outside, a phantom high over them.</summary>
        public static void ServerTick()
        {
            if (Type == null || !BlockNet.IsServer || Time.time < nextRoll) return;
            nextRoll = Time.time + 20f;
            var sor = StartOfRound.Instance; var tod = TimeOfDay.Instance;
            if (sor == null || tod == null || sor.inShipPhase || !sor.shipHasLanded || TerrainCarver.AtCompany || MobSpawns.DevNoNatural) return;
            // (the game's clock: 6:00 AM plus the day's share of its hours)
            float minutes = tod.normalizedTimeOfDay * 60f * tod.numberOfHours + 360f;
            if (minutes < 18f * 60f) return;
            var outside = Outdoors().ToList();
            if (outside.Count == 0) return;
            bool dark = tod.currentLevelWeather == LevelWeatherType.Eclipsed || tod.currentLevelWeather == LevelWeatherType.Stormy;
            int alive = RoundManager.Instance.SpawnedEnemies.Count(e => e is PhantomAI && !e.isEnemyDead);
            if (alive >= (dark ? 6 : 3)) return;
            float chance = DevChance >= 0f ? DevChance : Plugin.PhantomChance.Value * (dark ? 4f : 1f);
            if (Random.value >= chance) return;
            var p = outside[Random.Range(0, outside.Count)];
            ServerSpawnOver(p);
            Plugin.Log.LogInfo($"[phantom] one over {p.playerUsername} at {Clock()}{(dark ? " (eclipsed/stormy)" : "")}");
        }

        /// <summary>The game's clock, as it shows it (6:00 AM plus the day's share of its hours).</summary>
        public static string Clock()
        {
            var tod = TimeOfDay.Instance;
            if (tod == null) return "-";
            int m = (int)(tod.normalizedTimeOfDay * 60f * tod.numberOfHours) + 360;
            return $"{m / 60:00}:{m % 60:00}";
        }

        public static void ServerSpawnOver(PlayerControllerB p)
        {
            var at = p.transform.position + Vector3.up * CircleHeight + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * CircleRadius;
            MobSpawns.DevSpawnUntil = Time.time + 1f;
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, Type);
            Spawned++;
        }

        static Material sharedMat, eyesMat;

        public static void Register()
        {
            if (!Plugin.Phantoms.Value) return;
            Type = Register<PhantomAI>("Phantom", "Phantom", "Circles over you at night, then dives. Hates bright light", 0, 4, 0.8f, 6, 7f, BuildModel, outside: true, length: 1.5f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's phantom (64x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Phantom", "entity/phantom", new Color(0.3f, 0.35f, 0.5f));
            EntityModels.Part(parent, "Body", new Vector3(0, 0, 0), new Vector3(5, 3, 9), 0, 8, k, sharedMat);
            EntityModels.Part(parent, "Head", new Vector3(0, 0.5f, 6.5f), new Vector3(7, 3, 5), 0, 0, k, sharedMat);
            if (eyesMat == null && McAssets.LoadTexture("entity/phantom_eyes") != null)
            {
                eyesMat = EntityModels.Material("LMC_PhantomEyes", "entity/phantom_eyes", new Color(0, 0, 0, 0));
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(eyesMat, ArmorModels.AmbientEV + 7f, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
            }
            if (eyesMat != null) EntityModels.Part(parent, "Eyes", new Vector3(0, 0.5f, 6.52f), new Vector3(7.04f, 3.04f, 5.04f), 0, 0, k, eyesMat);
            var tail = EntityModels.Part(parent, "Tail", new Vector3(0, 0, -7.5f), new Vector3(3, 2, 6), 3, 20, k, sharedMat);
            EntityModels.Part(tail.transform, "TailTip", new Vector3(0, 0, -6), new Vector3(1, 1, 6), 4, 29, k, sharedMat);
            foreach (var (n, s) in new[] { ("WingL", 1f), ("WingR", -1f) })
            {
                var w = new GameObject(n).transform; w.SetParent(parent, false);
                w.localPosition = new Vector3(2.5f * s, 1f, 0f) * k;
                EntityModels.Part(w, "Base", new Vector3(3f * s, 0, 0), new Vector3(6, 2, 9), 23, 12, k, sharedMat);
                EntityModels.Part(w, "Tip", new Vector3(12.5f * s, 0, 0), new Vector3(13, 1, 9), 16, 24, k, sharedMat);
            }
        }
    }
}
