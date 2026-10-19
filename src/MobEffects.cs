using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using Effect = LethalMinecraft.Potions.Effect;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Effects on monsters, Minecraft's: a tipped arrow's or a splash potion's. Host only (the monsters' AI runs
    /// there). Poison (never the last of its health) and wither take Minecraft's health away (5 of it to one of ours),
    /// regeneration gives it back; slowness and swiftness change how fast it goes; healing and harming are at once, and
    /// the other way round on the undead (who don't take poison or regeneration). A monster with effects gives off
    /// Minecraft's swirls in their colour, on every machine (MobSwirls).
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public class MobEffects : MonoBehaviour
    {
        public static int Applied; // (dev/tests)
        readonly Dictionary<Effect, (int level, float until)> active = new Dictionary<Effect, (int, float)>();
        EnemyAI ai;
        int maxHp;
        float poisonAcc, witherAcc, regenAcc, poisonT, witherT, regenT, baseSpeed = -1f, lastSet = -1f;
        uint shownColor;

        public static bool Undead(EnemyAI e) => e is ZombieAI || e is SkeletonAI || e is WitherSkeletonAI || e is ZombifiedPiglinAI || e is PhantomAI;
        /// <summary>(#244) Minecraft's arthropods (Bane of Arthropods): spiders, endermites.</summary>
        public static bool Arthropod(EnemyAI e) => e is SpiderAI || e is EndermiteAI;
        public static int Burned, Knocked; // (dev/tests)
        /// <summary>(#244) Minecraft's fire-immune: blazes, ghasts, magma cubes, wither skeletons, zombified piglins (and things, not living).</summary>
        public static bool FireImmune(EnemyAI e) => e is BlazeAI || e is GhastAI || (e is MagmaCubeAI && !(e is SlimeAI)) || e is WitherSkeletonAI || e is ZombifiedPiglinAI || e is BoatAI || e is MinecartAI;

        /// <summary>(#244) Host: a player's hit landed: the weapon's Fire Aspect (4 s a level on fire), Knockback (pushed off),
        /// Bane of Arthropods (an arthropod slowed, Slowness IV for 1-1.5 s and half a second more a level).</summary>
        [HarmonyLib.HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.HitEnemy)), HarmonyLib.HarmonyPostfix]
        static void AfterHit(EnemyAI __instance, int force, PlayerControllerB playerWhoHit)
        {
            if (!BlockNet.IsServer || __instance == null || __instance.isEnemyDead || playerWhoHit == null || force <= 0) return;
            if (!(playerWhoHit.currentlyHeldObjectServer is ToolItem t) || t.Kind != ToolKind.Sword) return;
            int fire = Enchants.Level(t.Ench, EnchKind.FireAspect), knock = Enchants.Level(t.Ench, EnchKind.Knockback), bane = Enchants.Level(t.Ench, EnchKind.BaneOfArthropods);
            if (fire <= 0 && knock <= 0 && bane <= 0) return;
            if (fire > 0) Ignite(__instance, 4f * fire);
            if (knock > 0) Knock(__instance, __instance.transform.position - playerWhoHit.transform.position, knock);
            if (bane > 0 && Arthropod(__instance)) Apply(__instance, new Potions.Def { Effect = Effect.Slowness, Level = 4, Seconds = 1f + Random.Range(0f, 0.5f * bane) }, 1f, playerWhoHit);
        }

        /// <summary>(#244) Host: a monster on fire that long (not the fire-immune).</summary>
        public static void Ignite(EnemyAI e, float seconds)
        {
            if (!BlockNet.IsServer || e == null || e.isEnemyDead || FireImmune(e)) return;
            var m = Of(e);
            m.fireUntil = Mathf.Max(m.fireUntil, Time.time + seconds); Burned++;
        }

        /// <summary>(#244) Host: a monster pushed off along the ground that way, Knockback's or Punch's level.</summary>
        public static void Knock(EnemyAI e, Vector3 dir, int level)
        {
            if (!BlockNet.IsServer || e == null || e.isEnemyDead || level <= 0) return;
            var m = Of(e);
            dir.y = 0f;
            m.knockDir = dir.sqrMagnitude > 0.001f ? dir.normalized : e.transform.forward;
            m.knockLeft = 0.4f; m.knockSpeed = 6.25f * level * BlockWorld.S; Knocked++; // (about 2.5 blocks a level, as Minecraft's push)
        }

        /// <summary>(#244) Host: a mob pulled toward something (a fishing rod's reel) so many blocks along `dir`.</summary>
        public static void Pull(EnemyAI e, Vector3 dir, float blocks)
        {
            if (!BlockNet.IsServer || e == null || e.isEnemyDead || blocks <= 0f) return;
            var m = Of(e);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            m.knockDir = dir.normalized;
            m.knockLeft = 0.4f; m.knockSpeed = Mathf.Min(blocks, 8f) * BlockWorld.S / 0.4f;
        }

        float fireUntil, fireAcc, fireT, knockLeft, knockSpeed;
        Vector3 knockDir;
        public bool Burning => Time.time < fireUntil;

        public static MobEffects Of(EnemyAI e)
        {
            if (e == null) return null;
            var m = e.GetComponent<MobEffects>();
            if (m == null) { m = e.gameObject.AddComponent<MobEffects>(); m.ai = e; m.maxHp = Mathf.Max(1, e.enemyHP); }
            return m;
        }

        /// <summary>Host: a potion's effect on a monster, that share of it (a splash further off).</summary>
        public static void Apply(EnemyAI e, Potions.Def d, float share, PlayerControllerB from)
        {
            if (!BlockNet.IsServer || e == null || e.isEnemyDead || d == null || d.Effect == Effect.None || share <= 0f) return;
            var m = Of(e);
            bool undead = Undead(e);
            if (d.Effect == Effect.InstantHealth || d.Effect == Effect.InstantDamage)
            {
                // (Minecraft's 4 health healed or 6 hurt a level, doubled at II; ours a fifth of that)
                bool hurts = (d.Effect == Effect.InstantDamage) != undead;
                float amount = (hurts ? 6f : 4f) * (1 << (d.Level - 1)) * share / 5f;
                if (hurts) e.HitEnemy(Mathf.Max(1, Mathf.RoundToInt(amount)), from, true, -1);
                else e.enemyHP = Mathf.Min(m.maxHp, e.enemyHP + Mathf.Max(1, Mathf.RoundToInt(amount)));
                Applied++;
                return;
            }
            if (undead && (d.Effect == Effect.Poison || d.Effect == Effect.Regeneration)) return; // (Minecraft's: the undead take neither)
            float until = Time.time + d.Seconds * share;
            if (m.active.TryGetValue(d.Effect, out var cur) && cur.until > Time.time && (cur.level > d.Level || (cur.level == d.Level && cur.until > until))) return;
            m.active[d.Effect] = (d.Level, until);
            Applied++;
        }

        public int Level(Effect e) => active.TryGetValue(e, out var a) && a.until > Time.time ? a.level : 0;
        public string Describe() => string.Join(" ", active.Where(a => a.Value.until > Time.time).Select(a => $"{a.Key}{a.Value.level}:{a.Value.until - Time.time:F0}s")) + (Burning ? $" Fire:{fireUntil - Time.time:F0}s" : "");

        void Update()
        {
            if (ai == null || ai.isEnemyDead || !BlockNet.IsServer) { if (ai == null || ai.isEnemyDead) Destroy(this); return; }
            float dt = Time.deltaTime;
            // poison: Minecraft's 1 health every 1.25 s (faster a level), never the last; wither: every 2 s, it can kill
            int pl = Level(Effect.Poison);
            if (pl > 0 && (poisonT -= dt) <= 0f) { poisonT = 1.25f / pl; if (ai.enemyHP > 1) Hurt(ref poisonAcc, 0.2f, keepOne: true); }
            int wl = Level(Effect.Wither);
            if (wl > 0 && (witherT -= dt) <= 0f) { witherT = 2f / wl; Hurt(ref witherAcc, 0.2f, keepOne: false); }
            // fire (Fire Aspect): Minecraft's 1 health a second while it burns
            if (Burning && (fireT -= dt) <= 0f) { fireT = 1f; Hurt(ref fireAcc, 0.2f, keepOne: false); }
            // knockback: pushed along the ground (its own walking stops for it)
            if (knockLeft > 0f && ai.agent != null && ai.agent.enabled && ai.agent.isOnNavMesh) { knockLeft -= dt; ai.agent.Move(knockDir * knockSpeed * dt); }
            int rl = Level(Effect.Regeneration);
            if (rl > 0 && (regenT -= dt) <= 0f)
            {
                regenT = 2.5f / rl; regenAcc += 0.2f;
                if (regenAcc >= 1f) { regenAcc -= 1f; ai.enemyHP = Mathf.Min(maxHp, ai.enemyHP + 1); }
            }
            // its colour for the swirls (every machine), when it changes
            uint c = Potions.SwirlColor(active.Where(a => a.Value.until > Time.time).Select(a => (a.Key, a.Value.level))) | (Burning ? 0x02000000u : 0u); // (a high bit: on fire)
            if (c != shownColor) { shownColor = c; BlockNet.ServerMobSwirl(ai.NetworkObjectId, c); }
            if (active.Count > 0 && active.All(a => a.Value.until <= Time.time) && baseSpeed < 0f) active.Clear();
        }

        void Hurt(ref float acc, float amount, bool keepOne)
        {
            acc += amount;
            if (acc < 1f) return;
            acc -= 1f;
            if (keepOne && ai.enemyHP <= 1) return;
            ai.HitEnemy(1, null, false, -1);
        }

        void LateUpdate()
        {
            // slowness and swiftness: Minecraft's 15% slower, 20% quicker a level, over whatever speed its AI set
            if (ai == null || ai.agent == null || !BlockNet.IsServer) return;
            int sl = Level(Effect.Slowness), sw = Level(Effect.Swiftness);
            if (sl == 0 && sw == 0)
            {
                if (baseSpeed >= 0f && Mathf.Abs(ai.agent.speed - lastSet) < 0.001f) ai.agent.speed = baseSpeed;
                baseSpeed = lastSet = -1f;
                return;
            }
            if (baseSpeed < 0f || Mathf.Abs(ai.agent.speed - lastSet) > 0.001f) baseSpeed = ai.agent.speed; // (its AI set a new speed)
            float f = Mathf.Max(0f, 1f - 0.15f * sl) * (1f + 0.2f * sw);
            lastSet = baseSpeed * f;
            ai.agent.speed = lastSet;
        }
    }

    /// <summary>(#244) Minecraft's swirling effect particles, on every machine: around players with effects (their colour
    /// from the host: Potions' word), and monsters with them (MobEffects).</summary>
    public class Swirls : MonoBehaviour
    {
        public static readonly Dictionary<ulong, uint> Players = new Dictionary<ulong, uint>(), Mobs = new Dictionary<ulong, uint>();
        public static int Puffs; // (dev/tests)
        static Swirls instance;
        float next;

        public static void Ensure() { if (instance == null) instance = new GameObject("LMC_Swirls").AddComponent<Swirls>(); }

        void Update()
        {
            // (#244) the fires over burning monsters: Minecraft's flames, 20 frames a second
            if (fires.Count > 0)
            {
                int fr = Atlas.FireFrames > 0 ? (int)(Time.time * 20f) % Atlas.FireFrames : 0;
                var mesh = fr != fireFrame ? MeshBuilder.For(Blocks.Fire, 0, fr) : null;
                fireFrame = fr;
                foreach (var kv in fires.ToList())
                {
                    if (kv.Value == null) { fires.Remove(kv.Key); continue; }
                    if (kv.Key == null || kv.Key.isEnemyDead) { Destroy(kv.Value.gameObject); fires.Remove(kv.Key); continue; }
                    // (on it, wherever it goes: the flames on its feet, about its body: Minecraft's box, 0.7 wide, two blocks high)
                    kv.Value.transform.position = kv.Key.transform.position + Vector3.up * (0.5f * FireTall * BlockWorld.S);
                    if (mesh != null) kv.Value.sharedMesh = mesh;
                }
            }
            if ((next -= Time.deltaTime) > 0f) return;
            next = 0.15f;
            var sor = StartOfRound.Instance;
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (sor == null) return;
            foreach (var p in sor.allPlayerScripts)
                if (p != null && p != me && p.isPlayerControlled && !p.isPlayerDead && Players.TryGetValue(p.actualClientId, out var c) && c != 0)
                    Puff(p.transform.position + Vector3.up * Random.Range(0.2f, 1.8f), c);
            if (RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e != null && !e.isEnemyDead && Mobs.TryGetValue(e.NetworkObjectId, out var c) && c != 0)
                {
                    if ((c & 0x01000000u) != 0) Puff(e.transform.position + Vector3.up * Random.Range(0.2f, 1.5f), c);
                }
            // (#244) on fire: Minecraft's fire drawn over its body (the fire block's own flames, animated)
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (e == null) continue;
                bool burning = !e.isEnemyDead && Mobs.TryGetValue(e.NetworkObjectId, out var fc) && (fc & 0x02000000u) != 0;
                fires.TryGetValue(e, out var have);
                if (burning && have == null)
                {
                    var go = new GameObject("LMC_MobFire");
                    go.transform.position = e.transform.position;
                    go.transform.localScale = new Vector3(0.7f, FireTall, 0.7f) * BlockWorld.S; // (the fire block's mesh is a block's: 1 wide)
                    var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = MeshBuilder.For(Blocks.Fire, 0, fireFrame); fires[e] = mf;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new[] { Atlas.Cutout, Atlas.CutoutEmissive };
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    FireShown++;
                }
                else if (!burning && have != null) { Destroy(have.gameObject); fires.Remove(e); }
            }
        }

        public static int FireShown; // (dev/tests)
        const float FireTall = 1.42f; // (the fire's 22 px drawn about two blocks high)
        static readonly Dictionary<EnemyAI, MeshFilter> fires = new Dictionary<EnemyAI, MeshFilter>();
        static int fireFrame;

        static void Puff(Vector3 at, uint rgb)
        {
            Puffs++;
            var col = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
            at += new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
            Totem.SwirlSpark(at, col, new Vector3(0f, 0.6f, 0f), 0.06f, 1.0f);
        }
    }
}
