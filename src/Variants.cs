using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's zombie and skeleton variants (#136), babies and chicken jockeys (#137), spider jockeys (#138). A zombie,
    /// skeleton or spider that spawns naturally can turn into one, as Minecraft rolls it: on a desert moon ([Mobs]
    /// DesertMoons) most zombies are husks, on a snowy one (SnowyMoons) most skeletons are strays, on a swampy one
    /// (SwampMoons) skeletons are mostly bogged and some zombies drowned. One zombie in twenty is a baby (fast, small),
    /// one baby in twenty rides a chicken, one spider in a hundred carries a skeleton. Each has Minecraft's look, sounds and
    /// drops. (Mobs spawned with the dev commands stay what they were asked to be.)
    /// </summary>
    public static class Variants
    {
        public static int Replaced; // (dev/tests)
        /// <summary>(dev/tests: treat the moon as "desert", "snowy" or "swamp"; null = by its name)</summary>
        public static string DevMoon;
        static string[] Moons(string list) => (list ?? "").Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Whether the current moon is in a config list of moon names.</summary>
        public static bool On(string list)
        {
            var lv = RoundManager.Instance != null ? RoundManager.Instance.currentLevel : null;
            if (lv == null || lv.PlanetName == null) return false;
            foreach (var m in Moons(list))
                if (lv.PlanetName.IndexOf(m.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        public static bool Desert => DevMoon != null ? DevMoon == "desert" : On(Plugin.DesertMoons.Value);
        public static bool Snowy => DevMoon != null ? DevMoon == "snowy" : On(Plugin.SnowyMoons.Value);
        public static bool Swampy => DevMoon != null ? DevMoon == "swamp" : On(Plugin.SwampMoons.Value);

        /// <summary>Host, as a mob spawns naturally: replace it with another kind (it's gone, the other takes its place).</summary>
        public static void Replace(EnemyAI e, EnemyType with)
        {
            if (e == null || with == null || !e.IsServer) return;
            var refr = RoundManager.Instance.SpawnEnemyGameObject(e.transform.position, e.transform.eulerAngles.y, -1, with);
            Replaced++;
            // (#227, #228) the same animal grown up or changed: its name, look and own state (Minecraft's keep the name)
            if (refr.TryGet(out Unity.Netcode.NetworkObject no) && no.GetComponent<EnemyAI>() is EnemyAI to)
            {
                var name = ShipMobs.NameOf(e);
                if (e is MinecraftMobAI from && to is MinecraftMobAI m2)
                {
                    m2.SetLook(from.LookId);
                    if (from.GetType() == m2.GetType()) { var st = from.SaveState(); if (!string.IsNullOrEmpty(st)) m2.LoadState(st); }
                    if (MobSpawns.IsRestored(e)) MobSpawns.Restored.Add(to);
                }
                if (name != null) ShipMobs.ServerSetName(to, name); else BlockNet.ServerMobInfo(to);
            }
            e.KillEnemyOnOwnerClient(overrideDestroy: true);
        }

        /// <summary>A second, slightly bigger biped over the first (Minecraft's outer layers: the stray's rags, the bogged's
        /// moss, the drowned's seaweed): its parts go on the matching parts so they move together.</summary>
        public static void Overlay(Transform parent, float k, Material mat, int limb, (int u, int v) head, (int u, int v) body,
                                   (int u, int v) armR, (int u, int v) armL, (int u, int v) legR, (int u, int v) legL)
        {
            var tmp = new GameObject("overlay").transform; tmp.SetParent(parent, false);
            MinecraftMobAI.Biped(tmp, k, mat, limb, head, body, armR, armL, legR, legL);
            foreach (var (from, to) in new[] { ("Head", "Head"), ("Body", "Body"), ("ArmR/Arm", "ArmR"), ("ArmL/Arm", "ArmL"), ("LegL/Leg", "LegL"), ("LegR/Leg", "LegR") })
            {
                var part = tmp.Find(from); var onto = parent.Find(to);
                if (part == null || onto == null) continue;
                part.SetParent(onto, true);
                part.localScale *= 1.08f;
                part.name = "Overlay";
            }
            Object.Destroy(tmp.gameObject);
        }

        /// <summary>A baby's proportions: half size, Minecraft's bigger head.</summary>
        public static void Babyish(Transform parent, float k)
        {
            var head = parent.Find("Head");
            if (head == null) return;
            head.localScale = Vector3.one * 1.5f;
            head.localPosition = new Vector3(0f, 30f, 0f) * k;
        }

        /// <summary>A rider's legs: bent at the hips, sitting.</summary>
        public static void Sitting(Transform rider)
        {
            foreach (var n in new[] { "LegL", "LegR" })
            {
                var l = rider.Find(n);
                if (l != null) l.localRotation = Quaternion.Euler(-80f, n == "LegL" ? -10f : 10f, 0f);
            }
            foreach (var n in new[] { "ArmL", "ArmR" })
            {
                var a = rider.Find(n);
                if (a != null) a.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            }
        }
    }

    // ================================================================== zombies
    /// <summary>Husk (#136): the desert zombie. Its hit makes you hungry (Minecraft's Hunger); drops like a zombie's.</summary>
    public class HuskAI : ZombieAI
    {
        protected override string HeadKey => null; // (Minecraft's variants drop no head)
        public static new EnemyType Type, BabyType;
        protected override string Sound => "husk";
        protected override void OnHitPlayer(PlayerControllerB p) { if (p == GameNetworkManager.Instance?.localPlayerController) Potions.GiveEffect(Potions.Effect.Hunger, 1, 7f); }
        protected override EnemyType ReplaceWith() => Random.value < 0.05f ? BabyType : null;

        public static new void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0) return;
            Type = Register<HuskAI>("Husk", "Husk", "A desert zombie: its hit makes you hungry", 0, 4, 2.7f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/husk", new Color(0.55f, 0.5f, 0.35f), 1.0625f));
            BabyType = Register<BabyHuskAI>("BabyHusk", "Baby Husk", "A baby husk: fast", 0, 4, 1.3f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/husk", new Color(0.55f, 0.5f, 0.35f), 0.5f, baby: true));
        }
    }

    /// <summary>Drowned (#136): the swamp's waterlogged zombie (Minecraft's: rotten flesh; its copper is left out: no copper yet).</summary>
    public class DrownedAI : ZombieAI
    {
        protected override bool FloatsInWater => false; // (#247: it walks the bottom)
        protected override string HeadKey => null; // (Minecraft's variants drop no head)
        public static new EnemyType Type, BabyType;
        protected override string Sound => "drowned";
        protected override EnemyType ReplaceWith() => Random.value < 0.05f ? BabyType : null;
        protected override void ServerDrops(Vector3 at)
        {
            int flesh = Random.Range(0, 3);
            if (flesh > 0 && ModItems.ByKey.TryGetValue("rotten_flesh", out var rf)) ModItems.ServerSpawnStack(rf, flesh, at);
        }

        public static new void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0) return;
            Type = Register<DrownedAI>("Drowned", "Drowned", "A drowned zombie", 0, 4, 2.5f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/drowned", new Color(0.3f, 0.55f, 0.55f), 1f, overlay: "entity/zombie/drowned_outer_layer"));
            BabyType = Register<BabyDrownedAI>("BabyDrowned", "Baby Drowned", "A baby drowned: fast", 0, 4, 1.3f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/drowned", new Color(0.3f, 0.55f, 0.55f), 0.5f, overlay: "entity/zombie/drowned_outer_layer", baby: true));
        }
    }

    /// <summary>A baby zombie (#137): half size, much faster, the same drops, more experience. One in twenty rides a chicken.</summary>
    public class BabyZombieAI : ZombieAI
    {
        public static EnemyType BabyType;
        protected override float ChaseSpeed => 6.4f; // (Minecraft's: half again as fast as a zombie)
        protected override int Xp => 12;
        protected override EnemyType ReplaceWith() => Random.value < 0.05f ? ChickenJockeyAI.Type : null;

        public static new void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0) return;
            BabyType = Register<BabyZombieAI>("BabyZombie", "Baby Zombie", "A baby zombie: fast", 0, 4, 1.3f, 3, 1.6f, b => BuildZombie(b, "entity/zombie/zombie", new Color(0.25f, 0.45f, 0.3f), 0.5f, baby: true));
        }
    }
    public class BabyHuskAI : HuskAI { protected override float ChaseSpeed => 6.4f; protected override int Xp => 12; protected override EnemyType ReplaceWith() => null; }
    public class BabyDrownedAI : DrownedAI { protected override float ChaseSpeed => 6.4f; protected override int Xp => 12; protected override EnemyType ReplaceWith() => null; }

    /// <summary>A chicken jockey (#137): a baby zombie riding a chicken. Killed, the zombie falls and the chicken runs off.</summary>
    public class ChickenJockeyAI : BabyZombieAI
    {
        public static new EnemyType Type;
        protected override EnemyType ReplaceWith() => null;
        protected override void ServerDrops(Vector3 at)
        {
            base.ServerDrops(at);
            if (ChickenAI.Type == null) return;
            MobSpawns.DevSpawnUntil = Time.time + 1f; // (its mount: not a natural spawn to be stopped)
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, ChickenAI.Type);
        }

        public static new void Register()
        {
            if (Plugin.ZombieRarity.Value <= 0 || Plugin.ChickenRarity.Value <= 0) return;
            Type = Register<ChickenJockeyAI>("ChickenJockey", "Chicken Jockey", "A baby zombie on a chicken", 0, 4, 1.6f, 2, 1.6f, BuildJockey);
        }

        static void BuildJockey(Transform parent)
        {
            ChickenAI.BuildModel(parent);
            var rider = new GameObject("Rider").transform; rider.SetParent(parent, false);
            float kc = EntityModels.Px * BlockWorld.S;
            rider.localPosition = new Vector3(0f, 6f * kc, -0.5f * kc);
            BuildZombie(rider, "entity/zombie/zombie", new Color(0.25f, 0.45f, 0.3f), 0.5f, baby: true);
            Variants.Sitting(rider);
        }
    }

    // ================================================================== skeletons
    /// <summary>Stray (#136): the snowy skeleton. Its arrows slow you; drops bones, arrows and an arrow of Slowness.</summary>
    public class StrayAI : SkeletonAI
    {
        protected override string HeadKey => null; // (Minecraft's variants drop no head)
        public static new EnemyType Type;
        protected override string Sound => "stray";
        protected override Potions.Effect ArrowEffect => Potions.Effect.Slowness;
        protected override EnemyType ReplaceWith() => null;
        protected override void ServerDrops(Vector3 at)
        {
            base.ServerDrops(at);
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("arrow_slowness", out var a)) ModItems.ServerSpawnStack(a, 1, at);
        }

        public static new void Register()
        {
            if (Plugin.SkeletonRarity.Value <= 0) return;
            Type = Register<StrayAI>("Stray", "Stray", "A snowy skeleton: its arrows slow you", 0, 4, 2.5f, 3, 1.8f, b => BuildSkeleton(b, "entity/skeleton/stray", new Color(0.7f, 0.75f, 0.78f), "entity/skeleton/stray_overlay"));
        }
    }

    /// <summary>Bogged (#136): the swamp skeleton. Its arrows poison; drops bones, arrows and an arrow of Poison.</summary>
    public class BoggedAI : SkeletonAI
    {
        protected override string HeadKey => null; // (Minecraft's variants drop no head)
        public static new EnemyType Type;
        protected override string Sound => "bogged";
        protected override Potions.Effect ArrowEffect => Potions.Effect.Poison;
        protected override EnemyType ReplaceWith() => null;
        protected override void ServerDrops(Vector3 at)
        {
            base.ServerDrops(at);
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("arrow_poison", out var a)) ModItems.ServerSpawnStack(a, 1, at);
        }

        public static new void Register()
        {
            if (Plugin.SkeletonRarity.Value <= 0) return;
            Type = Register<BoggedAI>("Bogged", "Bogged", "A swamp skeleton: its arrows poison", 0, 4, 2.5f, 3, 1.8f, b => BuildSkeleton(b, "entity/skeleton/bogged", new Color(0.45f, 0.5f, 0.35f), "entity/skeleton/bogged_overlay"));
        }
    }

    // ================================================================== the spider jockey
    /// <summary>A spider jockey (#138): a skeleton riding a spider, shooting as the spider runs at you. Killed, the skeleton
    /// falls off and keeps fighting.</summary>
    public class SpiderJockeyAI : SpiderAI
    {
        public static new EnemyType Type;
        float nextShot = 2f;
        protected override EnemyType ReplaceWith() => null;

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || !IsServer || targetPlayer == null || currentBehaviourStateIndex != Chase || Time.time < nextShot) return;
            var from = transform.position + Vector3.up * 2.1f + transform.forward * 0.3f;
            if (Vector3.Distance(from, targetPlayer.transform.position) > SkeletonAI.ShootRange) return;
            if (Physics.Linecast(from, targetPlayer.gameplayCamera.transform.position, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) return;
            nextShot = Time.time + Random.Range(1.8f, 3f);
            var to = targetPlayer.gameplayCamera.transform.position + Vector3.down * 0.5f + Random.insideUnitSphere * 0.4f;
            BlockNet.ServerArrow(from, Arrow.Aim(from, to));
        }

        protected override void ServerDrops(Vector3 at)
        {
            base.ServerDrops(at);
            if (SkeletonAI.Type == null) return;
            MobSpawns.DevSpawnUntil = Time.time + 1f; // (its rider: not a natural spawn to be stopped)
            RoundManager.Instance.SpawnEnemyGameObject(at, transform.eulerAngles.y, -1, SkeletonAI.Type);
        }

        public static new void Register()
        {
            if (Plugin.SpiderRarity.Value <= 0 || Plugin.SkeletonRarity.Value <= 0) return;
            Type = Register<SpiderJockeyAI>("SpiderJockey", "Spider Jockey", "A skeleton riding a spider", 0, 4, 2.4f, 2, 2.2f, BuildJockey);
        }

        static void BuildJockey(Transform parent)
        {
            SpiderAI.BuildModel(parent);
            var rider = new GameObject("Rider").transform; rider.SetParent(parent, false);
            float ks = EntityModels.Px * BlockWorld.S * 0.8f, kr = EntityModels.Px * BlockWorld.S * 0.9f;
            // (sat on the spider's back: its hips at the top of the body)
            rider.localPosition = new Vector3(0f, 13f * ks - 12f * kr, -7f * ks);
            SkeletonAI.BuildSkeleton(rider, "entity/skeleton/skeleton", new Color(0.8f, 0.8f, 0.78f));
            Variants.Sitting(rider);
        }
    }
}
