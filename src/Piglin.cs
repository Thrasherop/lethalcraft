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
        public const string Name = "Piglin";
        public const int Admire = 2;
        public const float AdmireSeconds = 6f, GoldSight = 10f, Reach = 1.7f;
        public static EnemyType Type;
        protected override string Sound => "piglin";
        protected override float ChaseSpeed => 3.2f;
        protected override float RoamSpeed => 1.8f;
        protected override int Damage => 25;

        /// <summary>Who has hit it: it goes for them, gold or not.</summary>
        readonly HashSet<ulong> angry = new HashSet<ulong>();
        float admireUntil, goingSince;
        StackItem goingFor;
        /// <summary>Gold it gave up on (couldn't get to it): left alone a while.</summary>
        readonly Dictionary<ulong, float> gaveUp = new Dictionary<ulong, float>();
        Transform ingot;
        public static int Barters; // (dev/tests)

        /// <summary>Minecraft's barter table (Java's weights), with what this world has.</summary>
        static readonly (string key, int min, int max, int weight)[] Table =
        {
            ("potion_fire_resistance", 1, 1, 8), ("potion_water", 1, 1, 10), ("iron_ingot", 1, 4, 10), ("ender_pearl", 2, 4, 10),
            ("quartz", 5, 12, 20), ("obsidian", 1, 1, 40), ("soul_sand", 2, 8, 40), ("nether_bricks", 2, 8, 40), ("gravel", 8, 16, 40),
        };

        public static bool WearsGold(PlayerControllerB p) => p != null && Armor.Of(p.actualClientId).Any(k => k != null && ItemData.Base(k).StartsWith("golden_"));

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
                if (IsServer) ServerBarter();
                agent.speed = RoamSpeed;
                SwitchToBehaviourState(Roam);
                StartSearch(transform.position);
                return true;
            }
            if (currentBehaviourStateIndex == Chase && targetPlayer != null && angry.Contains(targetPlayer.actualClientId)) return false;
            var gold = NearestGold();
            if (gold == null) { goingFor = null; return false; }
            // (gold it can't get to in 8 s, it gives up on for half a minute: it doesn't stand staring at it for ever)
            if (gold == goingFor && Time.time - goingSince > 8f)
            {
                gaveUp[gold.NetworkObjectId] = Time.time + 30f;
                goingFor = null;
                agent.speed = RoamSpeed;
                StartSearch(transform.position);
                return false;
            }
            if (Vector3.Distance(transform.position, gold.transform.position) <= Reach)
            {
                // picked up: one ingot, then six seconds looking at it
                if (gold.Count > 1) gold.ServerSetCount(gold.Count - 1);
                else gold.ServerSetCount(0, despawnIfEmpty: true);
                goingFor = null;
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

        StackItem NearestGold()
        {
            if (!IsServer) return null;
            StackItem best = null; float bd = GoldSight;
            foreach (var s in StackItem.Live)
            {
                if (s == null || s.ItemKey != "gold_ingot" || s.isHeld || s.isHeldByEnemy || s.isPocketed || s.Despawning || !s.IsSpawned || s.Count <= 0) continue;
                // (a player's gold, not the loot lying about; nor gold it's given up on)
                if (NetherLife.LootGold.Contains(s.NetworkObjectId)) continue;
                if (gaveUp.TryGetValue(s.NetworkObjectId, out var until) && Time.time < until) continue;
                if (Time.time - s.SpawnTime < 0.5f) continue;
                float d = Vector3.Distance(transform.position, s.transform.position);
                if (d < bd && Mathf.Abs(s.transform.position.y - transform.position.y) < 3f) { bd = d; best = s; }
            }
            return best;
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
            BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, "piglin.celebrate", 0.9f, 0.7f);
            Plugin.Log.LogInfo($"[piglin] bartered: {row.key}");
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (playerWhoHit != null) angry.Add(playerWhoHit.actualClientId);
            if (IsOwner && !isEnemyDead && currentBehaviourStateIndex == Admire && playerWhoHit != null)
            {
                // (hit while it's looking at the gold: the gold's gone and so is its patience)
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
            if (Random.value < 0.085f && ModItems.ByKey.TryGetValue("golden_sword", out var sw)) ModItems.ServerSpawnPlain(sw, at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (!Plugin.NetherPiglins.Value) return;
            // (only the Nether's spawning makes them: a weight of 0 in the moons' own lists)
            Type = Register<PiglinAI>("Piglin", Name, "Wear gold, or bring some", 0, 4, 2.2f, 8, 1.8f, BuildModel);
            var ai = Type.enemyPrefab.GetComponent<PiglinAI>();
            ai.enemyBehaviourStates = ai.enemyBehaviourStates.Concat(new[] { new EnemyBehaviourState { name = "Admire", parameterString = "admire" } }).ToArray();
        }

        // ------------------------------------------------------------------ the model: Minecraft's piglin, 64x64
        static void BuildModel(Transform parent)
        {
            // (a player-sized biped, like the zombie: about 2.2 m)
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Piglin", "entity/piglin/piglin", new Color(0.85f, 0.6f, 0.55f));
            // body (16,16), arms (40,16) and (32,48), legs (0,16) and (16,48), limbs 4 thick
            Biped(parent, k, sharedMat, 4, (0, 0), (16, 16), (40, 16), (32, 48), (0, 16), (16, 48), arm => Held(arm, k, "item_golden_sword", "Sword", 1f));
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
            if (armL != null) Held(armL, k, "item_gold_ingot", "Ingot", 0.7f).SetActive(false);
        }

        /// <summary>An item in a hand (its own extruded sprite), like the wither skeleton's sword.</summary>
        static GameObject Held(Transform arm, float k, string tile, string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(arm, false);
            go.transform.localPosition = new Vector3(0f, -11f, 3f) * k;
            go.transform.localRotation = Quaternion.Euler(0f, 90f, -45f);
            go.transform.localScale = Vector3.one * (size * 0.9f * k / 0.0805f);
            go.layer = 19;
            go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey(tile) ? tile : "item_pickaxe");
            var mr = go.AddComponent<MeshRenderer>();
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
}
