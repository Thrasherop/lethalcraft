using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's witch (#134): an outside monster on the moons ([Mobs] WitchRarity), at night like the game's own. It
    /// keeps its distance and throws splash potions, picked Minecraft's way: slowness at someone far off, poison at
    /// someone healthy, weakness now and then up close, harming otherwise. Hurt, it stops and drinks a healing potion. It
    /// never hits up close. Killed, it drops some of what Minecraft's does: glass bottles, glowstone, gunpowder,
    /// redstone, sugar, sticks. Model from entity/witch.png (Minecraft's villager body: robe, crossed arms, the bent hat).
    /// </summary>
    public class WitchAI : MinecraftMobAI
    {
        public static EnemyType Type;
        protected override string Sound => "witch";
        protected override float ChaseSpeed => Time.time < swiftUntil ? 3.4f * 1.2f : 3.4f; // (#244: after a potion of Swiftness, 20% faster)
        protected override float RoamSpeed => 1.6f;
        protected override int Damage => 0; // (Minecraft's witch only throws)
        public const float ThrowRange = 13f, Keep = 7f, DrinkTime = 1.6f;
        public const int MaxHp = 5;
        /// <summary>A splash of harming, full on (owner: very lethal, about eight seconds to kill someone standing still).</summary>
        public const float Harming = 30f, ThrowEvery = 2f; // (Minecraft throws every 3 s: 2 brings it to the eight seconds)
        public static int Thrown, Drinks; // (dev/tests)
        public static string DevWhy = "-"; // (dev: the last AI step's throw decision)

        float nextThrow = 2f, drinkUntil = -1f, drinkTick, swiftUntil = -1f;
        byte drinking; // (#244: what it's drinking: 1 Healing, 2 Swiftness)
        Transform bottle;
        // (host: what it last threw at whom, and till when that lasts: Minecraft's witch doesn't throw slowness at someone
        // already slowed, nor poison at someone poisoned)
        readonly Dictionary<PlayerControllerB, (float slow, float poison, float weak)> given = new Dictionary<PlayerControllerB, (float, float, float)>();

        public override void Start()
        {
            base.Start();
            if (agent != null) agent.stoppingDistance = Keep;
            bottle = transform.Find("Model/Arms/Bottle");
            if (bottle != null) bottle.gameObject.SetActive(false);
        }

        bool Sees(PlayerControllerB p) => p != null && !Physics.Linecast(eye.position, p.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
        bool Drinking => Time.time < drinkUntil;

        protected override bool BusyAI()
        {
            // (drinking: it stands still)
            if (!Drinking) return false;
            if (agent != null && agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
            return true;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (!IsServer) return;
            DevWhy = $"dead={isEnemyDead} drinking={Drinking} target={(targetPlayer != null ? targetPlayer.playerUsername : "-")} state={currentBehaviourStateIndex}";
            if (isEnemyDead || Drinking || targetPlayer == null || currentBehaviourStateIndex != Chase) return;
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            bool sees = d <= ThrowRange && Sees(targetPlayer);
            DevWhy = $"d={d:F1} sees={sees} next={nextThrow - Time.time:F1} hp={targetPlayer.health}";
            if (agent != null) agent.stoppingDistance = sees ? Keep : 1f;
            if (!sees || Time.time < nextThrow) return;
            nextThrow = Time.time + ThrowEvery;
            given.TryGetValue(targetPlayer, out var g);
            float now = Time.time;
            WitchPotion.Kind kind;
            // Minecraft's choice (WitchEntity.performRangedAttack): slowness from 8 blocks, poison while healthy, weakness up close
            if (d >= 8f * BlockWorld.S && g.slow < now) { kind = WitchPotion.Kind.Slowness; g.slow = now + 90f; }
            else if (targetPlayer.health >= 40 && g.poison < now) { kind = WitchPotion.Kind.Poison; g.poison = now + 45f; }
            else if (d <= 3f * BlockWorld.S && g.weak < now && Random.value < 0.25f) { kind = WitchPotion.Kind.Weakness; g.weak = now + 90f; }
            else kind = WitchPotion.Kind.Harming;
            given[targetPlayer] = g;
            var from = eye.position + transform.forward * 0.5f + Vector3.up * 0.2f;
            // (aimed where they'll be: Minecraft leads its throw by their movement)
            var to = targetPlayer.transform.position + Vector3.up * 0.8f + targetPlayer.thisController.velocity * 0.5f;
            BlockNet.ServerWitchPotion(from, WitchPotion.Aim(from, to), (byte)kind);
            Thrown++;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // facing whoever it's throwing at, while it stands at its distance
            if (currentBehaviourStateIndex == Chase && targetPlayer != null && !Drinking)
            {
                var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            }
            if (bottle != null && bottle.gameObject.activeSelf != Drinking) bottle.gameObject.SetActive(Drinking);
            if (drinkUntil > 0f && !Drinking)
            {
                // the potion's drunk (every machine: the host says when it starts, so they agree on its health)
                drinkUntil = -1f;
                if (drinking == 2) swiftUntil = Time.time + 180f; // (Minecraft's 3:00)
                else enemyHP = Mathf.Min(enemyHP + 1, MaxHp); // (Minecraft's: 4 of its 26)
                drinking = 0;
                Drinks++;
            }
            // (#244) host: Minecraft's witch, each tick it isn't drinking: hurt, 5% it drinks Healing; its target more than 11
            // blocks off and it isn't quick already, 50% Swiftness (it had drunk Healing at once on every hit, and nothing else)
            if (IsServer && !isEnemyDead && !Drinking)
            {
                drinkTick += Time.deltaTime;
                int n = 0;
                while (drinkTick >= 0.05f && n++ < 4)
                {
                    drinkTick -= 0.05f;
                    byte kind = 0;
                    if (enemyHP < MaxHp && Random.value < 0.05f) kind = 1;
                    else if (targetPlayer != null && Time.time >= swiftUntil && Vector3.Distance(targetPlayer.transform.position, transform.position) > 11f * BlockWorld.S && Random.value < 0.5f) kind = 2;
                    if (kind != 0) { BlockNet.ServerMobState(NetworkObjectId, 240, kind); drinkTick = 0f; break; }
                }
            }
        }

        public override void ReceiveState(byte a, byte b)
        {
            if (a != 240 || isEnemyDead) { base.ReceiveState(a, b); return; }
            // the host's word: it drinks (1 Healing, 2 Swiftness)
            drinking = b;
            drinkUntil = Time.time + DrinkTime;
            Sounds.Play("witch.drink", transform.position + Vector3.up * 1.6f, 0.8f, Random.Range(0.9f, 1.1f), 16f);
        }

        /// <summary>Host: Minecraft's drops: one to three rolls, each 0-2 of one of these.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            // (#244) killed by a player while drinking: the potion in its hand, 8.5% (Minecraft's hand drop chance)
            if (KilledByPlayer && Drinking && Random.value < 0.085f && ModItems.ByKey.TryGetValue(drinking == 2 ? "potion_swiftness" : "potion_healing", out var pot))
                { if (pot.spawnPrefab.GetComponent<StackItem>() != null) ModItems.ServerSpawnStack(pot, 1, at); else ModItems.ServerSpawnPlain(pot, at); }
            var pool = new[] { "glass_bottle", "glowstone", "gunpowder", "redstone_dust", "spider_eye", "sugar", "stick", "stick" };
            int rolls = Random.Range(1, 4);
            for (int i = 0; i < rolls; i++)
            {
                int n = Random.Range(0, 3);
                if (n > 0 && ModItems.ByKey.TryGetValue(pool[Random.Range(0, pool.Length)], out var it)) ModItems.ServerSpawnStack(it, n, at);
            }
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.WitchRarity.Value <= 0) return;
            Type = Register<WitchAI>("Witch", "Witch", "Throws potions", Plugin.WitchRarity.Value, MaxHp, 2.6f, 2, 1.6f, BuildModel, outside: true);
        }

        // ------------------------------------------------------------------ the model: Minecraft's witch (64x128)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.9f; // (as tall as a player, like the zombie and skeleton; the hat on top)
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Witch", "entity/witch", new Color(0.3f, 0.2f, 0.35f));
            // (Minecraft's villager body, y up from the feet here, the front +z)
            var head = new GameObject("Head").transform; head.SetParent(parent, false);
            EntityModels.Part(head, "Skull", new Vector3(0, 29, 0), new Vector3(8, 10, 8), 0, 0, k, sharedMat);
            EntityModels.Part(head, "Nose", new Vector3(0, 25, 5), new Vector3(2, 4, 2), 24, 0, k, sharedMat);
            // the hat: a brim, then three smaller pieces, each set back and tipped a bit further (its bent point)
            var hat = new GameObject("Hat").transform; hat.SetParent(head, false);
            EntityModels.Part(hat, "Brim", new Vector3(0, 33f, 0), new Vector3(10, 2, 10), 0, 64, k, sharedMat);
            var h2 = Pivot(hat, new Vector3(0, 34f, 0), k, -3f);
            EntityModels.Part(h2, "Hat2", new Vector3(0.25f, 2f, -0.5f), new Vector3(7, 4, 7), 0, 76, k, sharedMat);
            var h3 = Pivot(h2, new Vector3(0, 4f, -0.5f), k, -6f);
            EntityModels.Part(h3, "Hat3", new Vector3(0.5f, 2f, -0.5f), new Vector3(4, 4, 4), 0, 87, k, sharedMat);
            var h4 = Pivot(h3, new Vector3(0, 4f, -0.5f), k, -12f);
            EntityModels.Part(h4, "Hat4", new Vector3(0.75f, 1f, -0.5f), new Vector3(1, 2, 1), 0, 95, k, sharedMat);
            // the body, and the robe over it (a little bigger: Minecraft inflates it half a pixel)
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 6), 16, 20, k, sharedMat);
            var robe = EntityModels.Part(parent, "Robe", new Vector3(0, 14, 0), new Vector3(8, 20, 6), 0, 38, k, sharedMat);
            robe.transform.localScale = new Vector3(9f / 8f, 21f / 20f, 7f / 6f);
            // the crossed arms, held out in front
            var arms = Pivot(parent, new Vector3(0, 21, 1), k, -43f); arms.name = "Arms";
            EntityModels.Part(arms, "ArmR", new Vector3(-6, -2, 0), new Vector3(4, 8, 4), 44, 22, k, sharedMat);
            EntityModels.Part(arms, "ArmL", new Vector3(6, -2, 0), new Vector3(4, 8, 4), 44, 22, k, sharedMat);
            EntityModels.Part(arms, "Folded", new Vector3(0, -4, 0), new Vector3(8, 4, 4), 40, 38, k, sharedMat);
            // the potion it drinks (shown while it does)
            var b = new GameObject("Bottle"); b.transform.SetParent(arms, false); b.layer = 19;
            b.transform.localPosition = new Vector3(0f, -4f, 4f) * k;
            b.transform.localRotation = Quaternion.Euler(43f, 0f, 0f);
            b.transform.localScale = Vector3.one * 0.5f;
            b.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_potion_strength") ? "item_potion_strength" : "item_glass_bottle");
            var mr = b.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            // the legs, under the robe
            foreach (var (n, x) in new[] { ("LegL", 2f), ("LegR", -2f) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 12f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -6, 0), new Vector3(4, 12, 4), 0, 22, k, sharedMat);
            }
        }

        static Transform Pivot(Transform parent, Vector3 at, float k, float tilt)
        {
            var t = new GameObject("Pivot").transform; t.SetParent(parent, false);
            t.localPosition = at * k; t.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            return t;
        }
    }

    /// <summary>
    /// A witch's splash potion in flight (#134): every machine flies its own copy (like the skeleton's arrows) and checks
    /// its own player; where it breaks, whoever's within four blocks gets the effect, less the further off.
    /// </summary>
    public class WitchPotion : MonoBehaviour
    {
        public enum Kind : byte { Harming, Slowness, Poison, Weakness }
        public const float Speed = 11f, Gravity = 9f, Life = 5f;
        public static int Splashes, Hits; // (dev/tests: broken bottles; ones that reached the local player)
        Vector3 vel; float age; Kind kind;

        // (#244: Minecraft 1.19.4's effect colours: harming A9656A, slowness 8BAFE0, poison 87A363, weakness 484D48)
        public static Color ColorOf(Kind k) => k == Kind.Harming ? new Color32(0xA9, 0x65, 0x6A, 255) : k == Kind.Slowness ? new Color32(0x8B, 0xAF, 0xE0, 255)
            : k == Kind.Poison ? new Color32(0x87, 0xA3, 0x63, 255) : (Color)new Color32(0x48, 0x4D, 0x48, 255);

        /// <summary>The starting velocity to land at `to` from `from` (thrown up a little, for the drop).</summary>
        public static Vector3 Aim(Vector3 from, Vector3 to)
        {
            float t = Mathf.Max(0.3f, Vector3.Distance(from, to) / Speed);
            var v = (to - from) / t;
            v.y += 0.5f * Gravity * t;
            return v;
        }

        public static void Launch(Vector3 from, Vector3 vel, Kind kind)
        {
            var go = new GameObject("LMC_WitchPotion");
            go.transform.position = from;
            var w = go.AddComponent<WitchPotion>(); w.vel = vel; w.kind = kind;
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false);
            model.transform.localScale = Vector3.one * 0.45f;
            string tile = Atlas.Tiles.ContainsKey("item_potion_awkward") ? "item_potion_awkward" : "item_glass_bottle";
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(tile);
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            Sounds.Play("witch.throw", from, 0.8f, Random.Range(0.9f, 1.1f), 20f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            vel += Vector3.down * Gravity * Time.deltaTime;
            var p = transform.position;
            var step = vel * Time.deltaTime;
            transform.Rotate(Vector3.right, 720f * Time.deltaTime, Space.Self); // (tumbling)
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp != null && !lp.isPlayerDead)
            {
                var a = lp.transform.position + Vector3.up * 0.3f; var b = lp.transform.position + Vector3.up * 2.0f;
                var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector3.Distance(p, a + ab * t) < 0.6f) { Break(p, true); return; }
            }
            if (Physics.Raycast(p, step.normalized, out var hit, step.magnitude + 0.1f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<EnemyAI>() == null)
            {
                Break(hit.point + hit.normal * 0.1f, false); return;
            }
            transform.position = p + step;
        }

        void Break(Vector3 at, bool direct)
        {
            Splashes++;
            Sounds.Play("break.glass", at, 0.9f, Random.Range(0.9f, 1.1f), 20f);
            SplashBits.Play(at, ColorOf(kind));
            var lp = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (lp != null && !lp.isPlayerDead)
            {
                // (Minecraft: within 4 blocks, 1 - distance/4 of the effect; a direct hit, all of it)
                float d = Vector3.Distance(at, lp.transform.position + Vector3.up * 0.9f);
                float f = direct ? 1f : 1f - d / (4f * BlockWorld.S);
                if (f > 0f)
                {
                    Hits++;
                    switch (kind)
                    {
                        case Kind.Harming: lp.DamagePlayer(Mathf.Max(1, Mathf.RoundToInt(WitchAI.Harming * f)), true, true, CauseOfDeath.Unknown, 0, false, vel.normalized); break;
                        case Kind.Slowness: Potions.GiveEffect(Potions.Effect.Slowness, 1, 90f * f); break;
                        case Kind.Poison: Potions.GiveEffect(Potions.Effect.Poison, 1, 45f * f); break;
                        case Kind.Weakness: Potions.GiveEffect(Potions.Effect.Weakness, 1, 90f * f); break;
                    }
                }
            }
            Destroy(gameObject);
        }
    }

    /// <summary>A splash potion's burst: little coloured bits that fly out and fade.</summary>
    public class SplashBits : MonoBehaviour
    {
        const float Life = 0.9f;
        readonly List<(Transform t, Vector3 v)> bits = new List<(Transform, Vector3)>();
        Material mat; float age;

        public static void Play(Vector3 at, Color c)
        {
            var go = new GameObject("LMC_Splash"); go.transform.position = at;
            var s = go.AddComponent<SplashBits>();
            s.mat = Atlas.MakeLit("LMC_SplashBit", false, false, true);
            s.mat.SetColor("_BaseColor", c);
            if (s.mat.HasProperty("_EmissiveColor")) s.mat.SetColor("_EmissiveColor", c * 2f);
            for (int i = 0; i < 18; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(go.transform, false);
                b.transform.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
                b.GetComponent<MeshRenderer>().sharedMaterial = s.mat;
                var dir = Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y) + 0.3f;
                s.bits.Add((b.transform, dir.normalized * Random.Range(1.5f, 3.5f)));
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(mat); Destroy(gameObject); return; }
            for (int i = 0; i < bits.Count; i++)
            {
                var (t, v) = bits[i];
                v += Vector3.down * 6f * Time.deltaTime;
                t.localPosition += v * Time.deltaTime;
                t.localScale *= 1f - Time.deltaTime * 0.8f;
                bits[i] = (t, v);
            }
        }
    }
}
