using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's piglin (#67), in the Nether fortress ([Mobs] NetherPiglins). It goes for players who aren't wearing
    /// any gold armor; one golden piece and it leaves you alone, unless you hit it. And it barters: drop a gold ingot
    /// near it ([Q]) and it walks over, picks it up, looks at it for six seconds and throws you something for it,
    /// Minecraft's way: ender pearls, nether quartz, obsidian, soul sand, nether bricks, gravel, iron, a water bottle or
    /// now and then a potion of fire resistance. Its model comes from your Minecraft textures (entity/piglin/piglin.png).
    /// </summary>
    public class PiglinAI : MinecraftMobAI
    {
        protected override string HeadKey => "piglin_head";
        public const string Name = "Piglin";
        public const int Admire = 2;
        public const float AdmireSeconds = 6f, GoldSight = 10f, Reach = 1.7f;
        public static EnemyType Type;
        protected override string Sound => "piglin";
        protected override float ChaseSpeed => 4.7f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 25;
        protected override bool ArmsForward => currentBehaviourStateIndex == Chase; // (#111: arms up when it's after you, Minecraft's attack pose)

        /// <summary>Who has hit it: it goes for them, gold or not.</summary>
        readonly HashSet<ulong> angry = new HashSet<ulong>();
        float admireUntil, goingSince;
        GrabbableObject goingFor;
        /// <summary>Gold it gave up on (couldn't get to it): left alone a while.</summary>
        readonly Dictionary<ulong, float> gaveUp = new Dictionary<ulong, float>();
        Transform ingot;
        public static int Barters; // (dev/tests)

        // (#244) Minecraft 26.3's tags: piglin_loved (what it admires; only the gold ingot it barters for), piglin_food,
        // guarded_by_piglins (open or break one where they are and they're after you) and piglin_repellents (they keep away)
        public static readonly string[] LovedList =
        {
            "gold_ore", "deepslate_gold_ore", "nether_gold_ore", "gold_block", "gilded_blackstone", "light_weighted_pressure_plate",
            "gold_ingot", "bell", "clock", "golden_carrot", "glistering_melon_slice", "golden_apple", "enchanted_golden_apple",
            "golden_helmet", "golden_chestplate", "golden_leggings", "golden_boots", "golden_horse_armor", "golden_sword",
            "golden_spear", "golden_pickaxe", "golden_shovel", "golden_axe", "golden_hoe", "raw_gold", "raw_gold_block",
            "golden_dandelion", "scrap_gold_ore", "scrap_deepslate_gold_ore", // (this world's raw gold: the gold ore's scrap)
        };
        public static bool Loved(string key) => key != null && System.Array.IndexOf(LovedList, ItemData.TypeOf(key)) >= 0;
        public static bool Food(string key) { var t = key != null ? ItemData.TypeOf(key) : null; return t == "raw_porkchop" || t == "porkchop"; }
        static readonly HashSet<string> GuardedKeys = new HashSet<string> { "gold_block", "barrel", "chest", "ender_chest", "gilded_blackstone", "trapped_chest", "raw_gold_block", "gold_ore", "deepslate_gold_ore", "nether_gold_ore" };
        public static bool Guarded(BlockDef d) => d != null && (GuardedKeys.Contains(d.Key) || d.Key.EndsWith("shulker_box") || d.Key.Contains("copper_chest"));
        static bool Repels(BlockDef d) => d != null && (d.Key == "soul_torch" || d.Key == "soul_wall_torch" || d.Key == "soul_lantern" || d.Key == "soul_fire" || d.Key == "soul_campfire");
        public static int Ate, Admired, Kept, GuardAngered, FledRepellent, FledZombified, Hunts; // (dev/tests)
        public bool IsBabyPiglin => this is BabyPiglinAI;
        /// <summary>What it's looking at (its key), and what it's put away (dropped when it dies).</summary>
        string admired;
        public readonly List<string> Pocket = new List<string>();
        float ateUntil, fleeUntil, fleeCheck, huntCheck;
        Vector3 fleeFrom;
        static float huntedUntil; // (Minecraft's HUNTED_RECENTLY, for the piglins about: one hunt every 30-120 s)

        /// <summary>Minecraft's barter table (Java's weights), with what this world has.</summary>
        static readonly (string key, int min, int max, int weight)[] Table =
        {
            ("potion_fire_resistance", 1, 1, 8), ("potion_water", 1, 1, 10), ("iron_nugget", 10, 36, 10), ("ender_pearl", 2, 4, 10),
            ("string", 3, 9, 20), ("quartz", 5, 12, 20), ("obsidian", 1, 1, 40), ("leather", 2, 4, 40), ("soul_sand", 2, 8, 40),
            ("nether_brick", 2, 8, 40), ("gravel", 8, 16, 40), ("blackstone", 8, 16, 40), // (#244: 26.3's, iron nuggets and the brick item; string, leather, blackstone)
        };

        public static bool WearsGold(PlayerControllerB p) => p != null && Armor.Of(p.actualClientId).Any(k => k != null && ItemData.TypeOf(k).StartsWith("golden_"));

        protected override bool Wants(PlayerControllerB p) => angry.Contains(p.actualClientId) || !WearsGold(p);

        public override void Start()
        {
            base.Start();
            var m = transform.Find("Model");
            ingot = m != null ? m.Find("ArmL/Ingot") : null;
        }

        /// <summary>Owner: gold first. Lying near it, it goes and picks it up (unless it's angry and after someone).</summary>
        protected override bool BusyAI()
        {
            if (currentBehaviourStateIndex == Admire)
            {
                agent.speed = 0f;
                if (Time.time < admireUntil) return true;
                if (IsServer) ServerDoneAdmiring();
                agent.speed = RoamSpeed;
                SwitchToBehaviourState(Roam);
                Stroll();
                return true;
            }
            if (IsServer && FleeAI()) return true;
            if (IsServer && !IsBabyPiglin && (huntCheck -= AIIntervalTime) <= 0f) { huntCheck = 1f; ServerMaybeHunt(); }
            if (currentBehaviourStateIndex == Chase && targetPlayer != null && angry.Contains(targetPlayer.actualClientId)) return false;
            var gold = NearestWanted();
            if (gold == null) { goingFor = null; return false; }
            // (gold it can't get to in 8 s, it gives up on for half a minute: it doesn't stand staring at it for ever)
            if (gold == goingFor && Time.time - goingSince > 8f)
            {
                gaveUp[gold.NetworkObjectId] = Time.time + 30f;
                goingFor = null;
                agent.speed = RoamSpeed;
                Stroll();
                return false;
            }
            if (Vector3.Distance(transform.position, gold.transform.position) <= Reach)
            {
                // picked up: one of it (Minecraft's pickUpItem); food it eats, the rest it looks at six seconds
                string key = Crafting.KeyOf(gold);
                if (gold is StackItem st) { if (st.Count > 1) st.ServerSetCount(st.Count - 1); else st.ServerSetCount(0, despawnIfEmpty: true); }
                else if (gold.NetworkObject != null && gold.NetworkObject.IsSpawned) gold.NetworkObject.Despawn(true);
                goingFor = null;
                if (Food(key))
                {
                    ateUntil = Time.time + 10f; Ate++; // (Minecraft's ATE_RECENTLY, 200 ticks)
                    BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, "eat", 0.8f, 0.9f);
                    return true;
                }
                admired = key; Admired++;
                BlockNet.ServerMobState(NetworkObjectId, 3, (byte)Mathf.Max(0, System.Array.IndexOf(LovedList, ItemData.TypeOf(key))));
                if (currentSearch.inProgress) StopSearch(currentSearch);
                targetPlayer = null; movingTowardsTargetPlayer = false;
                admireUntil = Time.time + AdmireSeconds;
                agent.speed = 0f;
                SwitchToBehaviourState(Admire);
                return true;
            }
            if (goingFor != gold)
            {
                goingFor = gold; goingSince = Time.time;
                if (currentSearch.inProgress) StopSearch(currentSearch);
                targetPlayer = null; movingTowardsTargetPlayer = false;
                if (currentBehaviourStateIndex != Roam) SwitchToBehaviourState(Roam);
            }
            agent.speed = RoamSpeed * 1.4f;
            SetDestinationToPosition(gold.transform.position);
            return true;
        }

        static readonly List<GrabbableObject> loose = new List<GrabbableObject>();
        static float looseAt = -10f;
        /// <summary>Items lying about (looked up twice a second for every piglin).</summary>
        static List<GrabbableObject> Loose()
        {
            if (Time.time - looseAt < 0.5f) return loose;
            looseAt = Time.time; loose.Clear();
            foreach (var g in Object.FindObjectsOfType<GrabbableObject>())
                if (g != null && !g.isHeld && !g.isHeldByEnemy && !g.isPocketed && g.IsSpawned && g.itemProperties != null) loose.Add(g);
            return loose;
        }

        /// <summary>(#244) Minecraft's wantsToPickup: anything it loves (not while it holds one), food when it hasn't just eaten.</summary>
        GrabbableObject NearestWanted()
        {
            if (!IsServer) return null;
            GrabbableObject best = null; float bd = GoldSight;
            foreach (var g in Loose())
            {
                if (g == null || g.isHeld || g.isPocketed || !g.IsSpawned) continue;
                if (g is StackItem s && (s.Despawning || s.Count <= 0 || Time.time - s.SpawnTime < 0.5f)) continue;
                var key = Crafting.KeyOf(g);
                if (!(Loved(key) || (Food(key) && Time.time > ateUntil))) continue;
                // (a player's gold, not the loot lying about; nor what it's given up on)
                if (NetherLife.LootGold.Contains(g.NetworkObjectId)) continue;
                if (gaveUp.TryGetValue(g.NetworkObjectId, out var until) && Time.time < until) continue;
                float d = Vector3.Distance(transform.position, g.transform.position);
                if (d < bd && Mathf.Abs(g.transform.position.y - transform.position.y) < 3f) { bd = d; best = g; }
            }
            return best;
        }

        /// <summary>Host: six seconds up: a grown-up barters a gold ingot; anything else, or a baby's, it keeps.</summary>
        void ServerDoneAdmiring()
        {
            var key = admired; admired = null;
            if (key == null) return;
            if (!IsBabyPiglin && ItemData.TypeOf(key) == "gold_ingot") ServerBarter();
            else { Pocket.Add(key); Kept++; }
        }

        /// <summary>(#244) Host: Minecraft's avoidRepellent (soul fire, a soul torch or lantern within 8 blocks: it walks off
        /// to 8 away) and avoidZombified (a zombified piglin within 6, not while it's fighting: off to 12 away, 5-7 s).</summary>
        bool FleeAI()
        {
            if ((fleeCheck -= AIIntervalTime) <= 0f)
            {
                fleeCheck = 0.5f;
                if (NearestRepellent(out var rp)) { if (Time.time > fleeUntil) FledRepellent++; fleeFrom = rp; fleeUntil = Time.time + 1f; }
                else if (currentBehaviourStateIndex != Chase)
                {
                    foreach (var e in RoundManager.Instance.SpawnedEnemies)
                        if (e is ZombifiedPiglinAI z && !z.isEnemyDead && Vector3.Distance(z.transform.position, transform.position) < 6f * BlockWorld.S)
                        { if (Time.time > fleeUntil) FledZombified++; fleeFrom = z.transform.position; fleeUntil = Time.time + Random.Range(5f, 7f); break; }
                }
            }
            if (Time.time > fleeUntil) return false;
            if (currentSearch.inProgress) StopSearch(currentSearch);
            if (currentBehaviourStateIndex == Chase) { targetPlayer = null; movingTowardsTargetPlayer = false; SwitchToBehaviourState(Roam); }
            var away = transform.position - fleeFrom; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            if (away.magnitude > 12f * BlockWorld.S) { fleeUntil = 0f; return false; }
            agent.speed = ChaseSpeed;
            SetDestinationToPosition(transform.position + away.normalized * 4f * BlockWorld.S, checkForPath: false);
            return true;
        }

        bool NearestRepellent(out Vector3 at)
        {
            at = default;
            var w = BlockWorld.Instance;
            if (w == null) return false;
            var c = Ground.CellOf(transform.position + Vector3.up * 0.5f * BlockWorld.S);
            float bd = float.MaxValue; bool found = false;
            for (int x = -8; x <= 8; x++)
                for (int y = -4; y <= 4; y++)
                    for (int z = -8; z <= 8; z++)
                    {
                        var b = w.Get(Ground.KeyOf(c + new Vector3Int(x, y, z)));
                        if (b == null || !Repels(b.Data.Def)) continue;
                        float d = x * x + y * y + z * z;
                        if (d < bd) { bd = d; at = w.WorldCenter(b.Key); found = true; }
                    }
            return found;
        }

        /// <summary>(#244) Host: Minecraft 26.3's StartHuntingHoglin: a grown hoglin in sight, no piglin about having hunted in
        /// the last 30-120 s, and they're not outnumbered: it and the grown piglins about go for it.</summary>
        void ServerMaybeHunt()
        {
            if (Time.time < huntedUntil || HoglinAI.Type == null || angry.Count > 0 || MobFoe != null) return;
            HoglinAI prey = null; int hoglins = 0, piglins = 0;
            float range = 16f * BlockWorld.S;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (e == null || e.isEnemyDead || Vector3.Distance(e.transform.position, transform.position) > range) continue;
                if (e is HoglinAI h && !(h is BabyHoglinAI)) { hoglins++; if (prey == null && SeesPoint(h.transform.position + Vector3.up)) prey = h; }
                else if (e is PiglinAI pg && !pg.IsBabyPiglin) piglins++;
            }
            if (prey == null || hoglins > piglins) return;
            huntedUntil = Time.time + Random.Range(30f, 120f); Hunts++;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is PiglinAI pg && !pg.IsBabyPiglin && !pg.isEnemyDead && Vector3.Distance(pg.transform.position, transform.position) < range) pg.ServerAngerAt(prey);
        }

        bool SeesPoint(Vector3 p) => eye != null && !Physics.Linecast(eye.position, p, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);

        /// <summary>(#244) Host: Minecraft's angerNearbyPiglins: a guarded block (a chest, gold) opened or broken by a player: the
        /// piglins within 16 blocks that aren't busy (and, for an opened chest, see them) go for them.</summary>
        public static void ServerGuarded(ulong client, BlockDef def, Vector3 at, bool onlyIfSeen)
        {
            if (Type == null || !Guarded(def) || RoundManager.Instance == null) return;
            var p = ServerLogic.PlayerFor(client);
            if (p == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (!(e is PiglinAI pg) || pg.isEnemyDead || pg.IsBabyPiglin || pg.currentBehaviourStateIndex == Chase || pg.MobFoe != null) continue;
                if (Vector3.Distance(pg.transform.position, p.transform.position) > 16f * BlockWorld.S) continue;
                if (onlyIfSeen && !pg.SeesPoint(p.gameplayCamera.transform.position)) continue;
                pg.ServerAngerAtPlayer(p); GuardAngered++;
            }
        }

        public void ServerAngerAtPlayer(PlayerControllerB p)
        {
            angry.Add(p.actualClientId);
            if (currentBehaviourStateIndex == Admire) ServerCancelAdmiring();
            if (currentSearch.inProgress) StopSearch(currentSearch);
            targetPlayer = p; agent.speed = ChaseSpeed; SwitchToBehaviourState(Chase);
        }

        /// <summary>Host: Minecraft's cancelAdmiring: what it was looking at drops.</summary>
        void ServerCancelAdmiring()
        {
            if (admired != null) ModItems.ServerSpawnPlainKeyedOrStack(admired, transform.position + Vector3.up * 1.2f * BlockWorld.S);
            admired = null;
        }

        /// <summary>Host: what it throws back for the ingot, towards the nearest player.</summary>
        void ServerBarter()
        {
            int total = Table.Sum(t => t.weight), pick = Random.Range(0, total);
            var row = Table[0];
            foreach (var t in Table) { pick -= t.weight; if (pick < 0) { row = t; break; } }
            var near = StartOfRound.Instance.allPlayerScripts.Where(p => p != null && p.isPlayerControlled && !p.isPlayerDead)
                .OrderBy(p => Vector3.Distance(p.transform.position, transform.position)).FirstOrDefault();
            var dir = near != null ? (near.transform.position - transform.position) : transform.forward;
            dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            var at = transform.position + dir * 1.0f + Vector3.up * 1.0f;
            if (ModItems.ByKey.TryGetValue(row.key, out var item))
            {
                if (item.spawnPrefab.GetComponent<StackItem>() != null) ModItems.ServerSpawnStack(item, Random.Range(row.min, row.max + 1), at);
                else ModItems.ServerSpawnPlain(item, at);
            }
            Barters++;
            if (near != null) Advancements.ServerGrant(near.actualClientId, "oh_shiny"); // (the nearest player gets the credit)
            BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, "piglin.celebrate", 0.9f, 0.7f);
            Plugin.Log.LogInfo($"[piglin] bartered: {row.key}");
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (playerWhoHit != null) angry.Add(playerWhoHit.actualClientId);
            if (IsServer && !isEnemyDead && playerWhoHit != null && IsBabyPiglin)
            {
                // (#244: a baby runs off, Minecraft's 100 ticks)
                if (currentBehaviourStateIndex == Admire) { ServerCancelAdmiring(); SwitchToBehaviourState(Roam); }
                fleeFrom = playerWhoHit.transform.position; fleeUntil = Time.time + 5f;
            }
            else if (IsOwner && !isEnemyDead && currentBehaviourStateIndex == Admire && playerWhoHit != null)
            {
                // (hit while it's looking at the gold: it drops it (#244: Minecraft's cancelAdmiring), and its patience goes)
                if (IsServer) ServerCancelAdmiring();
                agent.speed = ChaseSpeed; targetPlayer = playerWhoHit; SwitchToBehaviourState(Chase);
            }
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            bool admiring = currentBehaviourStateIndex == Admire;
            if (ingot != null && ingot.gameObject.activeSelf != admiring) ingot.gameObject.SetActive(admiring);
            // looking at the gold: the left arm up in front of its face
            if (admiring && armL != null) armL.localRotation = Quaternion.Euler(-70f, 25f, 0f);
        }

        /// <summary>Host: Minecraft's drops: a golden sword now and then.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (!IsBabyPiglin && !(this is CrossbowPiglinAI)) ServerDropEquipment("golden_sword", 32, at); // (#244: Minecraft's rule: a player's kill, Looting, worn)
            if (this is CrossbowPiglinAI) ServerDropEquipment(Crossbow.Key, 465, at);
            // (#244: what it kept: Minecraft's piglin inventory drops; and what it was looking at)
            foreach (var k in Pocket) ModItems.ServerSpawnPlainKeyedOrStack(k, at);
            if (admired != null) ModItems.ServerSpawnPlainKeyedOrStack(admired, at);
            Pocket.Clear(); admired = null;
        }
        protected override int Equipment => IsBabyPiglin ? 0 : 1;
        // (#244) Minecraft's spawn: one in five a baby; a grown one has a crossbow half the time
        protected override EnemyType ReplaceWith() => Random.value < 0.2f ? BabyPiglinAI.BabyType : Random.value < 0.5f ? CrossbowPiglinAI.CrossbowType : null;
        protected override bool VariesInNether => true;

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 3) ShowAdmired(b < LovedList.Length ? LovedList[b] : "gold_ingot");
        }

        /// <summary>Every machine: the thing in its left hand while it admires, as it looks dropped.</summary>
        void ShowAdmired(string key)
        {
            if (ingot == null) return;
            var mf = ingot.GetComponentInChildren<MeshFilter>(true);
            var mr = ingot.GetComponentInChildren<MeshRenderer>(true);
            if (mf == null || mr == null) return;
            var block = Blocks.Get(key);
            var flat = block != null ? Atlas.FlatItemTile(block) : null;
            if (block != null && flat == null) { mf.sharedMesh = MeshBuilder.For(block, 0, 0); mr.sharedMaterial = Atlas.Opaque; mf.transform.localScale = Vector3.one * 0.6f; return; }
            mf.transform.localScale = Vector3.one;
            string tile = flat ?? (Atlas.Tiles.ContainsKey("item_" + key) ? "item_" + key : Atlas.Tiles.ContainsKey("food_" + key) ? "food_" + key : Atlas.Tiles.ContainsKey(key) ? key : "item_gold_ingot");
            mf.sharedMesh = MeshBuilder.ExtrudedSprite(tile); mr.sharedMaterial = Atlas.Cutout;
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherPiglins.Value) return;
            // (only the Nether's spawning makes them: a weight of 0 in the moons' own lists)
            Type = Register<PiglinAI>("Piglin", Name, "Wear gold, or bring some", 0, 4, 2.5f, 8, 1.8f, BuildModel);
            var ai = Type.enemyPrefab.GetComponent<PiglinAI>();
            ai.enemyBehaviourStates = ai.enemyBehaviourStates.Concat(new[] { new EnemyBehaviourState { name = "Admire", parameterString = "admire" } }).ToArray();
            // (#244) Minecraft's baby piglin (half size, faster, never hostile) and the crossbow piglin
            BabyPiglinAI.BabyType = Register<BabyPiglinAI>("BabyPiglin", "Baby Piglin", "Takes your gold and keeps it", 0, 4, 1.3f, 8, 2.2f, b => { BuildModel(b, null); b.localScale = Vector3.one * 0.5f; });
            CrossbowPiglinAI.CrossbowType = Register<CrossbowPiglinAI>("CrossbowPiglin", Name, "Wear gold, or bring some", 0, 4, 2.5f, 8, 1.8f, b => BuildModel(b, "item_crossbow_standby"));
            foreach (var t in new[] { BabyPiglinAI.BabyType, CrossbowPiglinAI.CrossbowType })
            {
                var a2 = t.enemyPrefab.GetComponent<PiglinAI>();
                a2.enemyBehaviourStates = a2.enemyBehaviourStates.Concat(new[] { new EnemyBehaviourState { name = "Admire", parameterString = "admire" } }).ToArray();
            }
        }

        // ------------------------------------------------------------------ the model: Minecraft's piglin, 64x64
        static void BuildModel(Transform parent) => BuildModel(parent, "item_golden_sword");
        static void BuildModel(Transform parent, string held)
        {
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Piglin", "entity/piglin/piglin", new Color(0.85f, 0.6f, 0.55f));
            BuildPiglin(parent, sharedMat, held, true);
        }

        /// <summary>Minecraft's piglin shape (the brute's too: the same model, its own texture), what's in its right hand.</summary>
        public static void BuildPiglin(Transform parent, Material sharedMat, string heldTile, bool barters)
        {
            // (a player-sized biped, like the zombie: about 2.2 m)
            float k = EntityModels.Px * BlockWorld.S * 0.9f; // (#100: as tall as a player, about 2.5 m; it was 0.8, 2.2 m)
            // body (16,16), arms (40,16) and (32,48), legs (0,16) and (16,48), limbs 4 thick
            Biped(parent, k, sharedMat, 4, (0, 0), (16, 16), (40, 16), (32, 48), (0, 16), (16, 48), arm => { if (heldTile != null) Held(arm, k, heldTile, "Weapon", 1f); });
            // its head is wider than a player's (10 x 8 x 8), with a snout, two tusks and floppy ears
            var oldHead = parent.Find("Head");
            if (oldHead != null) Object.DestroyImmediate(oldHead.gameObject);
            EntityModels.Part(parent, "Head", new Vector3(0, 28, 0), new Vector3(10, 8, 8), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Snout", new Vector3(0, 26, 4.5f), new Vector3(4, 4, 1), 31, 1, k, sharedMat);
            EntityModels.Part(parent, "TuskR", new Vector3(-2.5f, 25, 4.5f), new Vector3(1, 2, 1), 2, 0, k, sharedMat);
            EntityModels.Part(parent, "TuskL", new Vector3(2.5f, 25, 4.5f), new Vector3(1, 2, 1), 2, 4, k, sharedMat);
            foreach (var (n, x, u, ang) in new[] { ("EarL", 4.5f, 51, 30f), ("EarR", -4.5f, 39, -30f) })
            {
                var pivot = new GameObject(n).transform;
                pivot.SetParent(parent, false);
                pivot.localPosition = new Vector3(x, 30f, 0f) * k;
                pivot.localRotation = Quaternion.Euler(0f, 0f, ang);
                EntityModels.Part(pivot, "Ear", new Vector3(Mathf.Sign(x) * 0.5f, -2.5f, 0f), new Vector3(1, 5, 4), u, 6, k, sharedMat);
            }
            // the gold it's looking at, in its left hand (shown while it admires)
            var armL = parent.Find("ArmL");
            if (armL != null && barters) Held(armL, k, "item_gold_ingot", "Ingot", 0.7f).SetActive(false);
        }

        /// <summary>An item in a hand (its own extruded sprite), like the wither skeleton's sword.</summary>
        static GameObject Held(Transform arm, float k, string tile, string name, float size)
        {
            // (a weapon by its handle, pixel (3.5, 3.5) from the bottom left, Minecraft's way, #99; an ingot by its middle)
            bool weapon = tile.Contains("sword") || tile.Contains("axe");
            if (weapon) return EntityModels.HeldWeapon(arm, k, tile, name); // (#166: Minecraft's hold)
            var go = new GameObject(name);
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 3f) * k;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, -45f);
            go.transform.localScale = Vector3.one * (size * 0.9f * k / 0.0805f);
            go.layer = 19;
            var sp = new GameObject("Sprite"); sp.transform.SetParent(go.transform, false); sp.layer = 19;
            if (weapon) sp.transform.localPosition = new Vector3(4.5f, 4.5f, 0f) / 16f;
            sp.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey(tile) ? tile : "item_pickaxe");
            var mr = sp.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Cutout;
            Atlas.NoDecals(mr);
            return go;
        }

        public static string Describe()
        {
            var all = RoundManager.Instance != null ? RoundManager.Instance.SpawnedEnemies.OfType<PiglinAI>().Where(e => !e.isEnemyDead).ToList() : new List<PiglinAI>();
            return $"piglins={all.Count} admiring={all.Count(e => e.currentBehaviourStateIndex == Admire)} barters={Barters} " +
                   string.Join(" ; ", all.Select(e => $"{e.transform.position.x:F1},{e.transform.position.y:F1},{e.transform.position.z:F1} state={e.currentBehaviourStateIndex} target={(e.targetPlayer != null ? e.targetPlayer.playerUsername : "-")} angry={e.angry.Count}"));
        }
    }

    /// <summary>(#244) Minecraft's baby piglin: half size, faster, never goes for anyone; it takes gold (and keeps it: no
    /// bartering); hit, it runs.</summary>
    public class BabyPiglinAI : PiglinAI
    {
        public static EnemyType BabyType;
        protected override float ChaseSpeed => 5.6f; // (Minecraft's: +20% speed)
        protected override float RoamSpeed => 2.2f;
        protected override int Damage => 0;
        protected override bool Wants(PlayerControllerB p) => false;
        protected override EnemyType ReplaceWith() => null;
    }

    /// <summary>(#244) Minecraft's piglin with a crossbow (half the grown ones): it keeps 5 blocks off (BackUpIfTooClose) and
    /// shoots, as a pillager does (CrossbowAttack).</summary>
    public class CrossbowPiglinAI : PiglinAI
    {
        public static EnemyType CrossbowType;
        protected override int Damage => 0;
        protected override bool ArmsForward => false;
        protected override EnemyType ReplaceWith() => null;
        public static int Shots; // (dev/tests)
        enum Bow : byte { Uncharged, Charging, Charged, Ready }
        Bow bow;
        int seeTicks, attackDelay, chargeTicks;
        float tickAcc;
        byte pose; float poseSince;
        MeshFilter crossbowMesh;
        int shownFrame = -2;

        public override void Start()
        {
            base.Start();
            var w = transform.Find("Model/ArmR/Weapon");
            crossbowMesh = w != null ? w.GetComponentInChildren<MeshFilter>() : null;
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (!IsServer || isEnemyDead) return;
            if (currentBehaviourStateIndex != Chase || targetPlayer == null) { if (bow != Bow.Charged) bow = Bow.Uncharged; seeTicks = 0; if (pose == 1) SetPose(0); return; }
            float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
            movingTowardsTargetPlayer = false;
            if (d < 5f * BlockWorld.S)
            {
                // (Minecraft's BackUpIfTooClose(5, 0.75))
                var away = transform.position - targetPlayer.transform.position; away.y = 0f;
                agent.speed = ChaseSpeed * 0.75f;
                SetDestinationToPosition(transform.position + away.normalized * 2f * BlockWorld.S, checkForPath: false);
            }
            else if ((d > PillagerAI.AttackRadius || seeTicks < 5) && attackDelay == 0)
            {
                agent.speed = bow == Bow.Uncharged ? ChaseSpeed : ChaseSpeed * 0.5f;
                SetDestinationToPosition(targetPlayer.transform.position);
            }
            else if (agent.isOnNavMesh) { agent.ResetPath(); agent.velocity = Vector3.zero; }
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            if (IsServer) CrossbowTicks();
            if (currentBehaviourStateIndex == Chase && targetPlayer != null && (agent == null || !agent.hasPath))
            {
                var to = targetPlayer.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            }
            Animate();
        }

        /// <summary>Host: Minecraft's CrossbowAttack, 20 ticks a second (the pillager's).</summary>
        void CrossbowTicks()
        {
            tickAcc += Time.deltaTime;
            while (tickAcc >= 0.05f)
            {
                tickAcc -= 0.05f;
                if (currentBehaviourStateIndex != Chase || targetPlayer == null) continue;
                bool sees = !Physics.Linecast(eye.position, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
                if (sees != seeTicks > 0) seeTicks = 0;
                seeTicks += sees ? 1 : -1;
                float d = Vector3.Distance(transform.position, targetPlayer.transform.position);
                bool needsToMove = (d > PillagerAI.AttackRadius || seeTicks < 5) && attackDelay == 0;
                switch (bow)
                {
                    case Bow.Uncharged:
                        if (!needsToMove) { bow = Bow.Charging; chargeTicks = 0; SetPose(1); }
                        break;
                    case Bow.Charging:
                        if (++chargeTicks >= CrossbowData.ChargeTicks(0))
                        {
                            bow = Bow.Charged; attackDelay = 20 + Random.Range(0, 20); SetPose(2);
                            BlockNet.ServerSound(transform.position + Vector3.up, "crossbow.load_end", 1f / (Random.value * 0.5f + 1f) + 0.2f, 0.8f);
                        }
                        break;
                    case Bow.Charged:
                        if (--attackDelay <= 0) { attackDelay = 0; bow = Bow.Ready; }
                        break;
                    case Bow.Ready:
                        if (sees) { Shoot(targetPlayer); bow = Bow.Uncharged; SetPose(0); }
                        break;
                }
            }
        }

        void Shoot(PlayerControllerB t)
        {
            var from = eye.position + transform.forward * 0.5f - Vector3.up * 0.15f;
            var to = t.transform.position + Vector3.up * 0.6f;
            var v = Arrow.Aim(from, to);
            var dir = (v.normalized + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 0.0172275f * 6f).normalized;
            BlockNet.ServerArrow(from, dir * v.magnitude, Arrow.NoShooter, 0, Arrow.Flags(0, true, true));
            BlockNet.ServerSound(from, "crossbow.shoot", 1f, 1f);
            Shots++;
        }

        void SetPose(byte p) { if (pose == p) return; pose = p; poseSince = Time.time; BlockNet.ServerMobState(NetworkObjectId, 2, p); }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 2) { pose = b; poseSince = Time.time; }
            else base.ReceiveState(a, b);
        }

        /// <summary>Minecraft's piglin crossbow poses (PiglinArmPose.CROSSBOW_CHARGE / CROSSBOW_HOLD: the illager's).</summary>
        void Animate()
        {
            if (armR == null || armL == null || currentBehaviourStateIndex == Admire) return;
            if (pose == 1)
            {
                float a = Mathf.Clamp01((Time.time - poseSince) / (CrossbowData.ChargeTicks(0) / 20f));
                armR.localRotation = PillagerAI.McRot(-0.97079635f, -0.8f, 0f);
                armL.localRotation = PillagerAI.McRot(Mathf.Lerp(-0.97079635f, -1.5707964f, a), Mathf.Lerp(0.4f, 0.85f, a), 0f);
                ShowCrossbow(a < 0.58f ? 0 : a < 1f ? 1 : 2);
            }
            else if (pose == 2)
            {
                armR.localRotation = PillagerAI.McRot(-1.5707964f + 0.1f, -0.3f, 0f);
                armL.localRotation = PillagerAI.McRot(-1.5f, 0.6f, 0f);
                ShowCrossbow(3);
            }
            else ShowCrossbow(-1);
        }

        void ShowCrossbow(int f)
        {
            if (crossbowMesh == null || f == shownFrame) return;
            shownFrame = f;
            string tile = f == 3 ? "item_crossbow_arrow" : f >= 0 ? "item_crossbow_pulling_" + f : "item_crossbow_standby";
            if (Atlas.Tiles.ContainsKey(tile)) crossbowMesh.sharedMesh = MeshBuilder.ExtrudedSprite(tile);
        }
    }

    /// <summary>
    /// Minecraft's piglin brute (#68): the bastion's guard. Always angry (gold armor doesn't fool it, gold doesn't buy it),
    /// a golden axe, harder hits and twice a piglin's health. Only in bastions.
    /// </summary>
    public class PiglinBruteAI : MinecraftMobAI
    {
        public const string Name = "Piglin Brute";
        public static EnemyType Type;
        protected override string Sound => "brute";
        protected override float ChaseSpeed => 5.0f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 35;
        protected override bool ArmsForward => currentBehaviourStateIndex == Chase; // (#111: arms up when it's after you, Minecraft's attack pose)

        /// <summary>Host: now and then a gold ingot (Minecraft's brute drops its axe, which this world hasn't got).</summary>
        protected override void ServerDrops(Vector3 at)
        {
            // (Minecraft's: the golden axe it carries, 8.5% of the time; 20 experience)
            ServerDropEquipment("golden_axe", 32, at); // (#244: Minecraft's rule: a player's kill, Looting, worn)
        }
        protected override int Xp => 20 + UnityEngine.Random.Range(1, 4); // (its axe's 1-3, #244)

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherPiglins.Value) return;
            // (only the Nether's spawning makes them: a weight of 0 in the moons' own lists)
            Type = Register<PiglinBruteAI>("PiglinBrute", Name, "The bastion's guard: always angry", 0, 10, 2.5f, 4, 1.8f, parent =>
            {
                if (sharedMat == null) sharedMat = EntityModels.Material("LMC_PiglinBrute", "entity/piglin/piglin_brute", new Color(0.75f, 0.5f, 0.45f));
                PiglinAI.BuildPiglin(parent, sharedMat, "item_golden_axe", false);
            });
        }
    }
}
