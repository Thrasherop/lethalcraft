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
        public override int ArmorRule => 1; // (#244: Minecraft's, some wear armor)
        public static EnemyType Type;
        protected override string Sound => "skeleton";
        protected override float ChaseSpeed => 4.6f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 10; // (up close it only pushes you with its bow)
        public const float ShootRange = 16f, Keep = 7f;

        float nextShot = 2f, aim;
        /// <summary>What its arrows do to whoever they hit (#136: the stray's Slowness, the bogged's Poison).</summary>
        protected virtual Potions.Effect ArrowEffect => Potions.Effect.None;
        protected override string HeadKey => "skeleton_skull";
        /// <summary>(#136: on a snowy moon most skeletons are strays, on a swampy one bogged)</summary>
        protected override EnemyType ReplaceWith()
        {
            if (Variants.Snowy && Random.value < 0.8f) return StrayAI.Type;
            if (Variants.Swampy && Random.value < 0.8f) return BoggedAI.Type;
            return null;
        }

        public override void Start()
        {
            base.Start();
            if (agent != null) agent.stoppingDistance = Keep;
        }

        /// <summary>(#244) A mob it's fighting: shot at as a player would be.</summary>
        protected override bool ShootAtMob(EnemyAI foe, float dist)
        {
            var to = foe.transform.position + Vector3.up * 0.8f * BlockWorld.S;
            bool sees = dist <= ShootRange && !Physics.Linecast(eye.position, to, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
            if (agent != null) { agent.stoppingDistance = sees ? Keep : 1f; agent.speed = ChaseSpeed; SetDestinationToPosition(foe.transform.position, checkForPath: false); }
            var look = foe.transform.position - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(look);
            if (!sees || Time.time < nextShot) return true;
            nextShot = Time.time + Random.Range(1.8f, 3f);
            var from = eye.position + transform.forward * 0.4f;
            Arrow.NextMobShooter = this;
            BlockNet.ServerArrow(from, Arrow.Aim(from, to + Random.insideUnitSphere * 0.3f), Arrow.NoShooter, (byte)ArrowEffect);
            Arrow.NextMobShooter = null;
            return true;
        }

        bool Sees(PlayerControllerB p) => p != null && !Physics.Linecast(eye.position, p.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);

        // (#244) Minecraft's RangedBowAttackGoal: in range with a clear shot it strafes sideways (now and then the other way)
        // and backs off when you come within three quarters of its range; and it keeps away from wolves (AvoidEntityGoal)
        float strafeDir = 1f, strafeSwitch;
        public static int Strafes, Fled; // (dev/tests)

        bool FleeWolves()
        {
            WolfAI near = null; float nd = 6f * BlockWorld.S;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is WolfAI w && !w.isEnemyDead) { float d = Vector3.Distance(w.transform.position, transform.position); if (d < nd) { nd = d; near = w; } }
            if (near == null) return false;
            var away = transform.position - near.transform.position; away.y = 0f;
            var to = transform.position + away.normalized * 6f * BlockWorld.S;
            if (UnityEngine.AI.NavMesh.SamplePosition(to, out var h, 4f, agent.areaMask)) { movingTowardsTargetPlayer = false; agent.speed = ChaseSpeed; agent.stoppingDistance = 0.5f; SetDestinationToPosition(h.position); Fled++; }
            return true;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer) return;
            if (FleeWolves()) return;
            if (targetPlayer == null || currentBehaviourStateIndex != Chase) return;
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            bool sees = d <= ShootRange && Sees(targetPlayer);
            // (keeps its distance while it has a clear shot; walks in when it hasn't)
            if (agent != null) agent.stoppingDistance = sees ? Keep : 1f;
            if (sees && agent != null && agent.isOnNavMesh)
            {
                if ((strafeSwitch -= AIIntervalTime) <= 0f) { strafeSwitch = 1f; if (Random.value < 0.3f) strafeDir = -strafeDir; }
                var toT = targetPlayer.transform.position - transform.position; toT.y = 0f;
                var side = Vector3.Cross(Vector3.up, toT.normalized) * strafeDir;
                var back = d < ShootRange * 0.75f ? -toT.normalized : Vector3.zero;
                var want = transform.position + (side + back).normalized * 2f * BlockWorld.S;
                if (UnityEngine.AI.NavMesh.SamplePosition(want, out var sh, 2f, agent.areaMask)) { movingTowardsTargetPlayer = false; agent.stoppingDistance = 0.2f; SetDestinationToPosition(sh.position); Strafes++; }
                else strafeDir = -strafeDir;
            }
            if (!sees || Time.time < nextShot) return;
            nextShot = Time.time + Random.Range(1.8f, 3f);
            var from = eye.position + transform.forward * 0.4f;
            var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.5f + Random.insideUnitSphere * 0.4f;
            Arrow.NextMobShooter = this; // (#244: its arrow knows whose it is: a mob it hits fights back)
            BlockNet.ServerArrow(from, Arrow.Aim(from, to), Arrow.NoShooter, (byte)ArrowEffect);
            Arrow.NextMobShooter = null;
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
            // now and then an arrow or two (the owner's: a slim chance; Minecraft's is 0-2 every time)
            if (Random.value < 0.08f && ModItems.ByKey.TryGetValue(LethalMinecraft.Bow.ArrowKey, out var arrow)) ModItems.ServerSpawnStack(arrow, Random.Range(1, 3), at);
            ServerDropEquipment(LethalMinecraft.Bow.Key, 384, at); // (#244: its bow, now and then)
        }
        protected override int Equipment => 1; // (#244: its bow)

        static readonly System.Collections.Generic.Dictionary<string, Material> mats = new System.Collections.Generic.Dictionary<string, Material>();

        public static void Register()
        {
            if (Plugin.SkeletonRarity.Value <= 0) return;
            Type = Register<SkeletonAI>("Skeleton", Name, "Shoots arrows; drops bones", Plugin.SkeletonRarity.Value, 4, 2.5f, 3, 1.8f, b => BuildSkeleton(b, "entity/skeleton/skeleton", new Color(0.8f, 0.8f, 0.78f)));
            // its variants: only what it turns into (Variants)
            StrayAI.Register(); BoggedAI.Register();
        }

        // ------------------------------------------------------------------ the model: Minecraft's skeleton, 64x32 (and its variants')
        public static void BuildSkeleton(Transform parent, string texture, Color fallback, string overlay = null)
        {
            // (a player-sized biped, like the zombie: about 2.2 m)
            float k = EntityModels.Px * BlockWorld.S * 0.9f; // (#100: as tall as a player, about 2.5 m; it was 0.8, 2.2 m)
            if (!mats.TryGetValue(texture, out var mat)) mats[texture] = mat = EntityModels.Material("LMC_" + texture.Replace('/', '_'), texture, fallback);
            Biped(parent, k, mat, 2, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16), arm => Bow(arm, k));
            if (overlay != null && McAssets.LoadTexture(overlay) != null)
            {
                if (!mats.TryGetValue(overlay, out var om)) mats[overlay] = om = EntityModels.Material("LMC_" + overlay.Replace('/', '_'), overlay, new Color(0, 0, 0, 0));
                Variants.Overlay(parent, k, om, 2, (0, 0), (16, 16), (40, 16), (40, 16), (0, 16), (0, 16));
            }
        }

        /// <summary>A bow in its right hand (the item's own extruded sprite), upright.</summary>
        static void Bow(Transform arm, float k)
        {
            var go = new GameObject("Bow");
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 1f) * k;
            // (across the arm, its belly forward, the string toward the skeleton: raised to aim, the bow stands upright, as
            // Minecraft's does; along the arm it lay flat and pointed ahead)
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 135f);
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

    /// <summary>
    /// An arrow: a skeleton's (#59) or a player's bow shot. Every machine flies its own copy. A skeleton's checks the
    /// machine's own player, like the blaze's fireball; a player's stops at the first monster or player in its way, and
    /// the shooter's machine lands the hit (one shovel hit's worth), as the game's own shovel does.
    /// </summary>
    /// <summary>(#244) A Flame arrow burns as it flies (Minecraft's: the arrow on fire).</summary>
    public class ArrowFlame : MonoBehaviour
    {
        float t;
        void Update()
        {
            if ((t -= Time.deltaTime) > 0f) return;
            t = 0.04f;
            Totem.SwirlSpark(transform.position + Random.insideUnitSphere * 0.08f, Random.value < 0.5f ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 0.85f, 0.2f), Vector3.up * 0.6f, 0.08f, 0.35f);
        }
    }

    public class Arrow : MonoBehaviour
    {
        public const float Speed = 26f, Gravity = 6f, Life = 4f, StuckFor = 3f;
        public const int Hurt = 15;
        public const ulong NoShooter = ulong.MaxValue;
        Vector3 vel; float age, stuck = -1f;
        ulong shooter = NoShooter;
        Potions.Effect effect; // (#136: Slowness or Poison on whoever it hits)
        // (#223: a crossbow's: Piercing passes through that many more; Multishot's extra two can't be picked up)
        int pierceLeft; bool noPickup, dispensed;
        /// <summary>(#244) A dispenser's arrow (flag 32): picked up where it sticks, as Minecraft's.</summary>
        public const byte Dispensed = 32;
        readonly System.Collections.Generic.HashSet<Object> pierced = new System.Collections.Generic.HashSet<Object>();

        /// <summary>A crossbow arrow's flags for the ArrowShot message: its Piercing level (0-4), whether it can't be picked
        /// up, and that it came from a crossbow.</summary>
        public static byte Flags(int pierce, bool noPickup, bool crossbow) => (byte)(Mathf.Clamp(pierce, 0, 7) | (noPickup ? 8 : 0) | (crossbow ? 16 : 0));
        /// <summary>(#244) A bow's enchantments on its arrow: Power (0-5), Punch (0-2), Flame.</summary>
        public static byte EnchByte(int power, int punch, bool flame) => (byte)(Mathf.Clamp(power, 0, 7) | (Mathf.Clamp(punch, 0, 3) << 3) | (flame ? 32 : 0));
        int power, punch; bool flame;
        /// <summary>(#244) Minecraft's Power: an arrow's damage 2, plus half a level and a half (Power V: 2.5 times); a hit's
        /// force here, its fraction a hit more by chance.</summary>
        public static int ForceFor(int power)
        {
            float f = power > 0 ? 1.25f + 0.25f * power : 1f;
            int n = Mathf.FloorToInt(f);
            return n + (Random.value < f - n ? 1 : 0);
        }
        bool PlayerShot => shooter != NoShooter;
        public static int PlayerHits, Recovered; // (dev/tests: a player's arrows that hit something, ones picked back up)

        /// <summary>The starting velocity to hit `to` from `from` (a little up, for the drop).</summary>
        public static Vector3 Aim(Vector3 from, Vector3 to)
        {
            float t = Vector3.Distance(from, to) / Speed;
            var v = (to - from) / Mathf.Max(t, 0.01f);
            v.y += 0.5f * Gravity * t;
            return v;
        }

        public static void Launch(Vector3 from, Vector3 vel, ulong shooter = NoShooter, byte effect = 0, byte flags = 0, byte ench = 0)
        {
            var go = new GameObject("LMC_Arrow");
            go.transform.position = from;
            var a = go.AddComponent<Arrow>(); a.vel = vel; a.shooter = shooter; a.effect = (Potions.Effect)effect;
            a.mobShooter = NextMobShooter; // (#244: the host's own copy of a mob's arrow knows its shooter)
            a.pierceLeft = flags & 7; a.noPickup = (flags & 8) != 0; a.dispensed = (flags & Dispensed) != 0;
            a.power = ench & 7; a.punch = (ench >> 3) & 3; a.flame = (ench & 32) != 0;
            if (a.flame) a.gameObject.AddComponent<ArrowFlame>();
            // Minecraft's arrow: the item's sprite, flying point first
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false);
            model.transform.localRotation = Quaternion.Euler(0f, -90f, -45f); // (the sprite's tip is its top right: turned along +z)
            model.transform.localScale = Vector3.one * 0.6f;
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_arrow") ? "item_arrow" : "item_pickaxe");
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            go.transform.rotation = Quaternion.LookRotation(vel);
            if ((flags & 16) == 0) Sounds.Play("bow", from, 0.8f, Random.Range(0.9f, 1.15f), 24f); // (a crossbow's: its own shot sound, from the host)
        }

        void Update()
        {
            age += Time.deltaTime;
            if (stuck >= 0f) { stuck += Time.deltaTime; if (stuck > StuckFor) Destroy(gameObject); return; }
            if (age > Life) { Destroy(gameObject); return; }
            vel += Vector3.down * (PlayerShot ? Bow.Gravity : Gravity) * Time.deltaTime;
            var p = transform.position;
            var step = vel * Time.deltaTime;
            if (PlayerShot && HitsSomeone(p, step)) return;
            if (!PlayerShot && mobShooter != null && BlockNet.IsServer && HitsAMob(p, step)) return;
            // the local player: a hit
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (!PlayerShot && lp != null && !lp.isPlayerDead)
            {
                var a = lp.transform.position + Vector3.up * 0.3f; var b = lp.transform.position + Vector3.up * 2.0f;
                if (SegmentDistance(p, p + step, a, b) < 0.5f)
                {
                    lp.DamagePlayer(Hurt, true, true, CauseOfDeath.Stabbing, 0, false, vel.normalized * 2f);
                    Affect(false);
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
                Redstone.ServerProjectileHit(hit, true); // (#160: a target block)
                if (PlayerShot && Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] arrow stuck in {hit.collider.name} (layer {hit.collider.gameObject.layer}) at {hit.point} after {age:F2} s");
                Sounds.Play("bowhit", hit.point, 0.5f, 1.2f, 14f);
                // a player's arrow in a wall: picked back up there, when the host allows it (not a creative player's)
                // (#244: the arrow it was, tipped or not: Minecraft's turns plain only after 30 s stuck, and ours is an item at once)
                string back = effect == Potions.Effect.Slowness ? "arrow_slowness" : effect == Potions.Effect.Poison ? "arrow_poison" : Bow.ArrowKey;
                if ((PlayerShot || dispensed) && !noPickup && BlockNet.IsServer && LethalMinecraft.Bow.Recover && (dispensed || !GameModes.IsCreative(shooter)) && ModItems.ByKey.TryGetValue(back, out var item))
                {
                    ModItems.ServerSpawnStack(item, 1, hit.point + hit.normal * 0.25f);
                    Recovered++;
                    Destroy(gameObject);
                }
                return;
            }
            transform.position = p + step;
            if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        }

        public static int Hits; // (dev/tests: arrows that hit the local player)

        /// <summary>(#244) Host: set while a mob shoots, for the arrow it makes.</summary>
        public static MinecraftMobAI NextMobShooter;
        MinecraftMobAI mobShooter;
        public static int MobHits, CreeperDiscs; // (dev/tests)

        /// <summary>(#244) Host: a mob's arrow into another mob (not its shooter): hurt, and it fights back; a creeper a
        /// skeleton's arrow kills drops a music disc (Minecraft's creeper_drop_music_discs).</summary>
        bool HitsAMob(Vector3 p, Vector3 step)
        {
            float len = step.magnitude;
            if (len < 1e-4f) return false;
            foreach (var h in Physics.SphereCastAll(p, 0.2f, step / len, len, 1 << 19, QueryTriggerInteraction.Collide))
            {
                var col = h.transform.GetComponent<EnemyAICollisionDetect>();
                if (col == null || col.mainScript == null || col.mainScript.isEnemyDead || col.mainScript == mobShooter) continue;
                var at = h.point != Vector3.zero ? h.point : h.transform.position;
                if (Physics.Linecast(p, at, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                var victim = col.mainScript;
                Sounds.Play("bowhit", at, 0.8f, 1f, 16f);
                bool skeleton = mobShooter is SkeletonAI;
                try { victim.HitEnemyOnLocalClient(1, vel.normalized, null, true, -1); } catch { }
                MobHits++;
                if (victim is MinecraftMobAI m && mobShooter != null && !mobShooter.isEnemyDead) m.ServerAngerAt(mobShooter);
                if (skeleton && victim is CreeperAI c && (c.isEnemyDead || c.enemyHP <= 0) && c.ServerDiscOnce())
                {
                    var discs = new[] { "13", "cat", "blocks", "chirp", "far", "mall", "mellohi", "stal", "strad", "ward", "11", "wait" };
                    ModItems.ServerSpawnPlainKeyedOrStack("music_disc_" + discs[Random.Range(0, discs.Length)], c.transform.position + Vector3.up * 0.5f);
                    CreeperDiscs++;
                }
                Destroy(gameObject);
                return true;
            }
            return false;
        }

        /// <summary>The local player was hit: the arrow's effect (Minecraft's: a stray's Slowness 30 s, a bogged's Poison 4 s;
        /// a tipped arrow from a bow an eighth of its potion's time).</summary>
        void Affect(bool fromBow)
        {
            if (effect == Potions.Effect.None) return;
            float secs = effect == Potions.Effect.Slowness ? (fromBow ? 11f : 30f) : effect == Potions.Effect.Poison ? (fromBow ? 5.6f : 4f) : 5f;
            Potions.GiveEffect(effect, 1, secs);
        }

        /// <summary>A player's arrow: the first monster or player (not the shooter) along this step. The shooter's machine
        /// lands the hit; every machine stops its copy there.</summary>
        bool HitsSomeone(Vector3 p, Vector3 step)
        {
            float len = step.magnitude;
            if (len < 1e-4f) return false;
            // (the game's shovel mask: players and monsters' hit boxes)
            foreach (var h in Physics.SphereCastAll(p, 0.2f, step / len, len, 1084754248, QueryTriggerInteraction.Collide))
            {
                if (h.transform.gameObject.layer == 8 || h.transform.gameObject.layer == 11) continue;
                if (!h.transform.TryGetComponent<IHittable>(out var hittable)) continue;
                var pl = h.transform.GetComponent<GameNetcodeStuff.PlayerControllerB>();
                if (pl != null && (pl.actualClientId == shooter || pl.isPlayerDead)) continue;
                var col = h.transform.GetComponent<EnemyAICollisionDetect>();
                if (col != null && (col.mainScript == null || col.mainScript.isEnemyDead)) continue;
                if (pl == null && col == null) continue;
                Object who = pl != null ? (Object)pl : col.mainScript;
                if (pierced.Contains(who)) continue; // (a piercing arrow already went through it)
                // (#244) an enderman dodges: it teleports, the arrow flies on (Minecraft's)
                if (col != null && col.mainScript is EndermanAI dodger) { if (BlockNet.IsServer) dodger.ServerDodge(); pierced.Add(who); continue; }
                // (walls first: nothing through them)
                var at = h.point != Vector3.zero ? h.point : h.transform.position;
                if (Physics.Linecast(p, at, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                Sounds.Play("bowhit", at, 0.8f, 1f, 16f);
                var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
                if (pl != null && pl == lp) Affect(true); // (the one hit: a tipped arrow's effect, on their own machine)
                if (col != null && effect != Potions.Effect.None && BlockNet.IsServer) // (#244: on a monster too, the host's)
                    MobEffects.Apply(col.mainScript, new Potions.Def { Effect = effect, Level = 1, Seconds = effect == Potions.Effect.Slowness ? 11f : 5.6f }, 1f, ServerLogic.PlayerFor(shooter));
                // (#244) a bow's Flame (Minecraft's 5 s on fire) and Punch (pushed off, more a level), the host's
                if (col != null && BlockNet.IsServer)
                {
                    if (flame) MobEffects.Ignite(col.mainScript, 5f);
                    if (punch > 0) MobEffects.Knock(col.mainScript, vel, punch);
                }
                if (lp != null && lp.actualClientId == shooter)
                {
                    try { hittable.Hit(ForceFor(power), vel.normalized, lp, true, 1); } catch (System.Exception e) { Plugin.Log.LogWarning("arrow hit: " + e.Message); }
                    PlayerHits++;
                }
                // (Piercing: on through it, to the next)
                if (pierceLeft > 0) { pierceLeft--; pierced.Add(who); continue; }
                Destroy(gameObject);
                return true;
            }
            return false;
        }

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
