using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's golems (#140), built the way Minecraft builds them: a carved pumpkin (or a jack o'lantern) put on top of
    /// two blocks of iron with an iron block either side of the upper one (an iron golem), or on two snow blocks (a snow
    /// golem). They're on the crew's side: the iron golem goes for the monsters near it (not creepers, as in Minecraft) and
    /// hits hard; the snow golem pelts them with snowballs. Hit one and it turns on you a while. In the ship when it
    /// takes off, a golem comes along to the next moon (like the pets). Drops: iron ingots / snowballs.
    /// </summary>
    public static class Golems
    {
        public static int Built; // (dev/tests)

        /// <summary>Host, as a block is about to go down: a pumpkin on a golem's body makes it a golem (the pumpkin isn't
        /// placed, the body goes, the golem stands there). True if it did.</summary>
        public static bool ServerTryBuild(BlockKey key, BlockDef top)
        {
            var w = BlockWorld.Instance;
            if (w == null || (top != Blocks.CarvedPumpkin && top != Blocks.JackOLantern)) return false;
            var b1 = key.Offset((int)Face.Down); var b2 = b1.Offset((int)Face.Down);
            if (SnowGolemAI.Type != null && w.DefAt(b1) == Blocks.Snow && w.DefAt(b2) == Blocks.Snow)
            {
                var at = w.WorldCenter(b2) - Vector3.up * BlockWorld.S * 0.5f;
                Remove(b1, b2);
                Spawn(SnowGolemAI.Type, at);
                return true;
            }
            if (IronGolemAI.Type == null || w.DefAt(b1) != Blocks.IronBlock || w.DefAt(b2) != Blocks.IronBlock) return false;
            foreach (var (a, b) in new[] { (Face.East, Face.West), (Face.North, Face.South) })
            {
                var l = b1.Offset((int)a); var r = b1.Offset((int)b);
                if (w.DefAt(l) != Blocks.IronBlock || w.DefAt(r) != Blocks.IronBlock) continue;
                var at = w.WorldCenter(b2) - Vector3.up * BlockWorld.S * 0.5f;
                Remove(b1, b2, l, r);
                Spawn(IronGolemAI.Type, at);
                return true;
            }
            return false;
        }

        static void Remove(params BlockKey[] keys) { foreach (var k in keys) BlockNet.ServerBroadcastOp(Op.Remove(k, false)); }

        static void Spawn(EnemyType t, Vector3 at)
        {
            MobSpawns.DevSpawnUntil = Time.time + 1f; // (built, not a natural spawn to be stopped)
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, t);
            BlockNet.ServerSound(at + Vector3.up, t == IronGolemAI.Type ? "golem.repair" : "snowgolem.idle", 0.9f, 1f);
            Built++;
        }

        /// <summary>A monster a golem goes for: the game's and this mod's hostile ones, not animals, pets, golems or creepers.</summary>
        public static bool IsFoe(EnemyAI e)
        {
            if (e == null || e.isEnemyDead || e.enemyType == null) return false;
            if (e is GolemAI || e is CreeperAI || e is PetAI) return false;
            if (e is MinecraftMobAI m && m.IsPassive) return false;
            if (e is ZombifiedPiglinAI z && !z.AngryAtAnyone) return false;
            return !e.enemyType.canDie ? false : true;
        }
    }

    /// <summary>The golems' AI: guard where they are; go for the nearest monster; turn on a player who hits them.</summary>
    public abstract class GolemAI : MinecraftMobAI
    {
        protected override bool HatesLight => false;
        public override bool GnawsBlocks => false;
        protected virtual float Reach => 2.2f;
        protected virtual float AttackEvery => 1.5f;
        protected abstract void ServerAttack(EnemyAI foe);
        public EnemyAI Foe { get; private set; }
        float nextAttack;
        ulong angryAt = ulong.MaxValue; float angryUntil;
        protected override bool Wants(PlayerControllerB p) => p != null && p.actualClientId == angryAt && Time.time < angryUntil;

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (playerWhoHit != null) { angryAt = playerWhoHit.actualClientId; angryUntil = Time.time + 20f; }
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        protected override bool BusyAI()
        {
            if (!IsServer) return false;
            if (Foe == null || !Golems.IsFoe(Foe) || Vector3.Distance(Foe.transform.position, transform.position) > 20f)
            {
                Foe = null; float best = 16f;
                foreach (var e in RoundManager.Instance.SpawnedEnemies)
                {
                    if (!Golems.IsFoe(e)) continue;
                    float d = Vector3.Distance(e.transform.position, transform.position);
                    if (d < best) { best = d; Foe = e; }
                }
            }
            if (Foe == null) return false;
            float dist = Vector3.Distance(Foe.transform.position, transform.position);
            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = ChaseSpeed;
                if (dist > Reach * 0.8f) SetDestinationToPosition(Foe.transform.position);
                else if (agent.hasPath) agent.ResetPath();
            }
            var to = Foe.transform.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
            if (dist <= Reach && Time.time >= nextAttack) { nextAttack = Time.time + AttackEvery; ServerAttack(Foe); }
            return true;
        }
    }

    /// <summary>Minecraft's iron golem (#140): strong, slow; one swing does a lot, and throws what it hits.</summary>
    public class IronGolemAI : GolemAI
    {
        public static EnemyType Type;
        public static int Swings, Repaired; // (dev/tests)
        public const int MaxHp = 20;

        // (#244) Minecraft's repair: an iron ingot on it, a quarter of its health back; its cracks show how hurt it is
        public override string UseTip(string heldKey) => heldKey == "iron_ingot" && enemyHP < MaxHp ? "Repair" : base.UseTip(heldKey);

        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            if (held is StackItem st && st.ItemKey == "iron_ingot" && enemyHP < MaxHp && !isEnemyDead)
            {
                enemyHP = Mathf.Min(MaxHp, enemyHP + MaxHp / 4); Repaired++;
                if (!GameModes.IsCreative(p.actualClientId)) st.ServerUseUp(p);
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "golem.repair", 1f, 1f + (Random.value - Random.value) * 0.2f);
                BlockNet.ServerMobState(NetworkObjectId, (byte)enemyHP, 0);
                return;
            }
            base.ServerUse(p, held);
        }

        public override void ReceiveState(byte a, byte b) { if (!IsServer) enemyHP = a; }

        static readonly Material[] cracked = new Material[4];
        int shownCrack = -1;
        public override void Update()
        {
            base.Update();
            // (Minecraft's IronGolemCrackinessLayer: under 3/4, 1/2 and 1/4 of its health, more cracks)
            float f = enemyHP / (float)MaxHp;
            int c = isEnemyDead ? shownCrack : f < 0.25f ? 3 : f < 0.5f ? 2 : f < 0.75f ? 1 : 0;
            if (c == shownCrack || c < 0) return;
            shownCrack = c;
            if (cracked[c] == null)
                cracked[c] = c == 0 ? sharedMat : EntityModels.Material("LMC_IronGolemCrack" + c, EntityModels.Layered("entity/iron_golem/iron_golem", "entity/iron_golem/iron_golem_crackiness_" + (c == 1 ? "low" : c == 2 ? "medium" : "high"), Color.white), new Color(0.8f, 0.78f, 0.75f));
            var model = transform.Find("Model");
            if (model != null && cracked[c] != null) foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = cracked[c];
        }
        protected override string Sound => "golem";
        protected override float ChaseSpeed => 3.6f;
        protected override float RoamSpeed => 1.2f;
        protected override int Damage => 35;
        protected override int Xp => 0;

        protected override void ServerAttack(EnemyAI foe)
        {
            Swings++;
            BlockNet.ServerSound(transform.position + Vector3.up * 2f, "golem.attack", 1f, 1f);
            foe.HitEnemyOnLocalClient(3, (foe.transform.position - transform.position).normalized, null, true);
            MobEffects.Toss(foe); // (#244: Minecraft's: what it hits goes up)
        }

        /// <summary>Host: Minecraft's drops: 3-5 iron ingots and 0-2 poppies (#244).</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (ModItems.ByKey.TryGetValue("iron_ingot", out var i)) ModItems.ServerSpawnStack(i, Random.Range(3, 6), at);
            int pop = Random.Range(0, 3);
            if (pop > 0 && ModItems.ByKey.TryGetValue("poppy", out var pp)) ModItems.ServerSpawnStack(pp, pop, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.Golems.Value) return;
            Type = Register<IronGolemAI>("IronGolem", "Iron Golem", "Built from iron and a pumpkin: it fights monsters", 0, 20, 3.4f, 4, 1.2f, BuildModel, outside: true, length: 1.6f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's iron golem (128x128)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.95f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_IronGolem", "entity/iron_golem/iron_golem", new Color(0.8f, 0.78f, 0.75f));
            // (Minecraft's: y down from 24 px (its feet at 24+...); here feet at 0, the front +z)
            EntityModels.Part(parent, "Head", new Vector3(0, 38, 3.5f), new Vector3(8, 10, 8), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Nose", new Vector3(0, 34, 8.5f), new Vector3(2, 4, 2), 24, 0, k, sharedMat);
            EntityModels.Part(parent, "Chest", new Vector3(0, 27, 0), new Vector3(18, 12, 11), 0, 40, k, sharedMat);
            EntityModels.Part(parent, "Waist", new Vector3(0, 18.5f, 0), new Vector3(9, 5, 6), 0, 70, k, sharedMat);
            foreach (var (n, x, u) in new[] { ("ArmR", -11f, 60), ("ArmL", 11f, 60) })
            {
                var sh = new GameObject(n).transform; sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 31f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(0, -13, 0), new Vector3(4, 30, 6), u, n == "ArmR" ? 21 : 58, k, sharedMat);
            }
            foreach (var (n, x, u) in new[] { ("LegL", 4f, 60), ("LegR", -4f, 37) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 16f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -8, 0), new Vector3(6, 16, 5), u, 0, k, sharedMat);
            }
        }
    }

    /// <summary>Minecraft's snow golem (#140): throws snowballs at monsters (they only sting; a blaze they hurt).</summary>
    public class SnowGolemAI : GolemAI
    {
        protected override bool WaterHurts => true; // (#244: Minecraft's isSensitiveToWater)
        public static EnemyType Type;
        protected override string Sound => "snowgolem";

        // (#244) Minecraft's snow golem: shears take its pumpkin (it drops; the snow head shows); it leaves snow where it
        // walks somewhere cold enough (a snowy moon); somewhere hot (a desert moon, the Nether) it melts, slowly
        public bool Pumpkin = true;
        public static int Sheared, SnowLaid, Melted; // (dev/tests)
        float heat, trailCheck;

        public override string UseTip(string heldKey) => heldKey == "shears" && Pumpkin ? "Shear" : base.UseTip(heldKey);
        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            if (held is ToolItem t && t.Kind == ToolKind.Shears && Pumpkin && !isEnemyDead)
            {
                Pumpkin = false; Sheared++;
                ModItems.ServerSpawnPlainKeyedOrStack("carved_pumpkin", transform.position + Vector3.up * 1.7f * BlockWorld.S);
                BlockNet.ServerSound(transform.position + Vector3.up, "sheep.shear", 0.8f, 1f);
                BlockNet.ServerMobState(NetworkObjectId, 2, 0);
                if (!GameModes.IsCreative(p.actualClientId)) t.ServerUse(1);
                return;
            }
            base.ServerUse(p, held);
        }
        public override void ReceiveState(byte a, byte b) { if (a == 2) { Pumpkin = false; ShowHead(); } }
        public override string SaveState() => Pumpkin ? null : "nopumpkin";
        public override void LoadState(string s) { if (s == "nopumpkin") { Pumpkin = false; ShowHead(); BlockNet.ServerMobState(NetworkObjectId, 2, 0); } }
        public override void ServerSyncTo(ulong client) { if (!Pumpkin) BlockNet.ServerMobState(NetworkObjectId, 2, 0, client); }
        void ShowHead() { var h = transform.Find("Model/Head"); if (h != null) h.gameObject.SetActive(Pumpkin); }

        public override void Update()
        {
            base.Update();
            if (!IsServer || isEnemyDead) return;
            bool hot = Variants.Desert || NetherFortress.Contains(transform.position);
            if (hot && (heat += Time.deltaTime * 0.4f) >= 1f) { heat -= 1f; Melted++; HitEnemyOnLocalClient(1, Vector3.up, null, true, -1); }
            if (!Variants.Snowy || (trailCheck -= Time.deltaTime) > 0f) return;
            trailCheck = 0.25f;
            var w = BlockWorld.Instance;
            if (w == null) return;
            var cell = Ground.CellOf(transform.position + Vector3.up * 0.1f * BlockWorld.S);
            var key = Ground.KeyOf(cell);
            var below = Ground.KeyOf(cell + Vector3Int.down);
            if (w.Get(key) != null || !Ground.IsAirCell(cell) || !(w.IsSolidAt(below) || (w.Get(below) == null && !Ground.IsAirCell(cell + Vector3Int.down)))) return; // (on a block, or the ground: a slope's part-filled cell too)
            BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(Blocks.SnowLayer.Id, (byte)Face.Up, 0)));
            SnowLaid++;
        }
        protected override float ChaseSpeed => 3.0f;
        protected override float RoamSpeed => 1.4f;
        protected override int Damage => 0;
        protected override int Xp => 0;
        protected override float Reach => 10f;
        protected override float AttackEvery => 1.0f;

        protected override void ServerAttack(EnemyAI foe)
        {
            var from = transform.position + Vector3.up * 2.2f + transform.forward * 0.4f;
            var to = foe.transform.position + Vector3.up * 1.0f;
            BlockNet.ServerSnowball(from, Snowball.Aim(from, to), Arrow.NoShooter);
        }

        /// <summary>Host: Minecraft's drops: 0-15 snowballs.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            int n = Random.Range(0, 16);
            if (n > 0 && ModItems.ByKey.TryGetValue("snowball", out var s)) ModItems.ServerSpawnStack(s, n, at);
        }

        static Material sharedMat, headMat;

        public static void Register()
        {
            if (!Plugin.Golems.Value) return;
            Type = Register<SnowGolemAI>("SnowGolem", "Snow Golem", "Built from snow and a pumpkin: it throws snowballs at monsters", 0, 1, 2.4f, 6, 1.4f, BuildModel, outside: true, length: 0.9f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's snow golem (64x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_SnowGolem", "entity/snow_golem", new Color(0.95f, 0.95f, 0.97f));
            EntityModels.Part(parent, "Bottom", new Vector3(0, 6, 0), new Vector3(12, 12, 12), 0, 36, k, sharedMat);
            EntityModels.Part(parent, "Body", new Vector3(0, 16, 0), new Vector3(10, 10, 10), 0, 16, k, sharedMat);
            // (#244) its own head of snow, under the pumpkin (Minecraft's: shown when the pumpkin's sheared off)
            EntityModels.Part(parent, "SnowHead", new Vector3(0, 25, 0), new Vector3(8, 8, 8), 0, 0, k, sharedMat);
            // its head: a carved pumpkin (Minecraft's: the block, a size over the snowball head)
            var head = new GameObject("Head"); head.transform.SetParent(parent, false);
            head.transform.localPosition = new Vector3(0, 25, 0) * k;
            head.transform.localScale = Vector3.one * (8f * k * 1.15f);
            head.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.For(Blocks.CarvedPumpkin, 0, 0);
            // (the block's own material is lit only by the scene: at night it was black; the mobs' faint glow, as theirs)
            if (headMat == null)
            {
                headMat = new Material(Atlas.Opaque);
                headMat.SetFloat("_AlbedoAffectEmissive", 1f);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetUseEmissiveIntensity(headMat, true);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveColor(headMat, Color.white);
                UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(headMat, ArmorModels.AmbientEV, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(headMat);
            }
            var hr = head.AddComponent<MeshRenderer>(); hr.sharedMaterials = new[] { headMat, Atlas.Emissive }; Atlas.NoDecals(hr);
            head.layer = 19;
            // (sticks angled down and out, as the owner asked; named apart from the walking arms: the base animation would swing them flat)
            foreach (var (n, x, rz) in new[] { ("StickR", -5f, 50f), ("StickL", 5f, -50f) })
            {
                var sh = new GameObject(n).transform; sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 18f, 0f) * k; sh.localRotation = Quaternion.Euler(0f, 0f, rz);
                EntityModels.Part(sh, "Arm", new Vector3(Mathf.Sign(x) * 5f, 0, 0), new Vector3(12, 2, 2), 32, 0, k, sharedMat);
            }
        }
    }

    /// <summary>
    /// A snowball (#140): thrown by a player (the use button) or a snow golem. Every machine flies its own; the thrower's
    /// machine (the host for a golem's) lands it: a monster is stung (a blaze is hurt), a player is pushed back.
    /// </summary>
    public class Snowball : MonoBehaviour
    {
        public const float Speed = 18f, Gravity = 9f, Life = 4f;
        public static int Hits; // (dev/tests)
        Vector3 vel; float age; ulong thrower; byte kind; // (#244: 0 a snowball, 1 an egg)
        public const byte SnowballKind = 0, EggKind = 1, SpitKind = 2;
        public static int SpitHits; // (dev/tests)
        public static int Hatched; // (dev/tests)

        public static Vector3 Aim(Vector3 from, Vector3 to)
        {
            float t = Mathf.Max(0.2f, Vector3.Distance(from, to) / Speed);
            var v = (to - from) / t; v.y += 0.5f * Gravity * t;
            return v;
        }

        /// <summary>The local player's use button with snowballs in hand.</summary>
        public static void Throw(PlayerControllerB p, byte kind = SnowballKind)
        {
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return;
            var cam = p.gameplayCamera.transform;
            BlockNet.RequestSnowball(cam.position + cam.forward * 0.5f, cam.forward * Speed + Vector3.up * 1.5f, kind);
        }

        public static void Launch(Vector3 from, Vector3 vel, ulong thrower, byte kind = SnowballKind)
        {
            var go = new GameObject(kind == EggKind ? "LMC_Egg" : kind == SpitKind ? "LMC_LlamaSpit" : "LMC_Snowball"); go.transform.position = from;
            var s = go.AddComponent<Snowball>(); s.vel = vel; s.thrower = thrower; s.kind = kind;
            if (kind == SpitKind) { s.spit = true; return; } // (#244: Minecraft's llama spit: a puff of white bits, no item)
            var model = new GameObject("model"); model.transform.SetParent(go.transform, false); model.transform.localScale = Vector3.one * 0.3f;
            string tile = kind == EggKind ? "item_egg" : "item_snowball";
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey(tile) ? tile : "item_bone_meal");
            var mr = model.AddComponent<MeshRenderer>(); mr.sharedMaterial = Atlas.Cutout; Atlas.NoDecals(mr);
            Sounds.Play(kind == EggKind ? "egg.throw" : "snowball.throw", from, 0.6f, Random.Range(0.9f, 1.1f), 16f);
        }

        bool Mine => thrower == Arrow.NoShooter ? BlockNet.IsServer : GameNetworkManager.Instance?.localPlayerController?.actualClientId == thrower;

        bool spit; float puff;
        void Update()
        {
            age += Time.deltaTime;
            if (age > Life) { Destroy(gameObject); return; }
            if (spit && (puff -= Time.deltaTime) <= 0f) { puff = 0.03f; Totem.SwirlSpark(transform.position + Random.insideUnitSphere * 0.06f, new Color(0.92f, 0.92f, 0.88f), Random.insideUnitSphere * 0.2f, 0.06f, 0.25f); }
            vel += Vector3.down * Gravity * Time.deltaTime;
            var p = transform.position; var step = vel * Time.deltaTime; float len = step.magnitude;
            if (len > 1e-4f)
                foreach (var h in Physics.SphereCastAll(p, 0.18f, step / len, len, 1084754248, QueryTriggerInteraction.Collide))
                {
                    if (!h.transform.TryGetComponent<IHittable>(out var hittable)) continue;
                    var pl = h.transform.GetComponent<PlayerControllerB>();
                    if (pl != null && (pl.actualClientId == thrower || pl.isPlayerDead)) continue;
                    var col = h.transform.GetComponent<EnemyAICollisionDetect>();
                    if (col != null && (col.mainScript == null || col.mainScript.isEnemyDead || col.mainScript is GolemAI || col.mainScript.NetworkObjectId == thrower)) continue; // (#244: not its own thrower: a llama's spit)
                    if (pl == null && col == null) continue;
                    Splat(h.point != Vector3.zero ? h.point : p, kind);
                    if (pl != null && pl == GameNetworkManager.Instance?.localPlayerController)
                    {
                        if (kind == SpitKind) { pl.DamagePlayer(7, true, true, CauseOfDeath.Unknown, 0, false, vel.normalized); SpitHits++; } // (#244: Minecraft's 1 health)
                        else Knockback.Toss(pl, new Vector3(vel.x, 0f, vel.z).normalized * 4f, 2f);
                    }
                    if (Mine && col != null) { try { col.mainScript.HitEnemyOnLocalClient(col.mainScript is BlazeAI ? 1 : 0, vel.normalized, thrower == Arrow.NoShooter ? null : GameNetworkManager.Instance.localPlayerController, true); } catch { } Hits++; }
                    Destroy(gameObject); return;
                }
            if (Physics.Raycast(p, step.normalized, out var hit, len + 0.05f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<EnemyAI>() == null) { Redstone.ServerProjectileHit(hit, false); Splat(hit.point, kind); Destroy(gameObject); return; }
            transform.position = p + step;
        }

        static void Splat(Vector3 at, byte kind)
        {
            if (kind == EggKind)
            {
                // (#244) Minecraft's ThrownEgg: it breaks (the egg's own bits); on the host, one in eight hatches a chick, and one
                // of those in thirty-two four
                for (int i = 0; i < 8; i++) Totem.SwirlSpark(at + Random.insideUnitSphere * 0.15f, new Color(0.93f, 0.87f, 0.72f), Random.insideUnitSphere * 1.2f + Vector3.up * 0.8f, 0.05f, 0.5f);
                if (BlockNet.IsServer && Random.Range(0, 8) == 0) Hatch(at, Random.Range(0, 32) == 0 ? 4 : 1);
                return;
            }
            if (kind == SpitKind) { for (int i = 0; i < 6; i++) Totem.SwirlSpark(at, new Color(0.92f, 0.92f, 0.88f), Random.insideUnitSphere * 1.0f, 0.06f, 0.4f); return; }
            Sounds.Play("dig.snow", at, 0.5f, 1.3f, 12f);
            if (BlockWorld.Instance != null) BlockWorld.Instance.SpawnParticles(Blocks.Snow, at, 6);
        }

        /// <summary>(#244) Host: chicks out of a thrown egg, where it broke.</summary>
        static void Hatch(Vector3 at, int n)
        {
            if (ChickenAI.Type == null || !AnimalCare.BabyOf.TryGetValue(ChickenAI.Type, out var baby) || RoundManager.Instance == null) return;
            var ground = at;
            if (Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out var gh, 3f * BlockWorld.S, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) ground = gh.point;
            for (int i = 0; i < n; i++)
            {
                MobSpawns.DevSpawnUntil = Time.time + 1f; // (not a natural spawn to be stopped)
                RoundManager.Instance.SpawnEnemyGameObject(ground, Random.Range(0f, 360f), -1, baby);
                Hatched++;
            }
        }
    }
}
