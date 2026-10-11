using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// What lives in the Nether fortress (#66, phase 4), run by the host once the fortress is there: wither skeletons
    /// and blazes from the start, the blaze room's spawner making more while a player is near it (Minecraft's: a
    /// player within 16 blocks, a few at a time), and loot lying around: the moon's own scrap worth about 1.6 times as
    /// much (S+), with blaze rods and the odd wither skull among it. Nothing is spawned while the tests switch the
    /// mobs off.
    /// It's always eclipsed down there (the owner's idea): full of monsters from the start, no morning grace, and the
    /// host tops it back up every 20 s whatever the time of day ([Mobs] NetherMonsters, how many).
    /// </summary>
    public static class NetherLife
    {
        public const float SpawnerEvery = 15f, SpawnerRange = 22f, ValueMultiplier = 1.6f, RefillEvery = 20f;
        static float nextRefill;

        /// <summary>How many monsters the fortress keeps (eclipsed: as many as it'll ever have, from the start).</summary>
        public static int Cap => Mathf.Clamp(Plugin.NetherMonsters.Value + NetherFortress.TileCount / 4, 0, 30);
        public const int SpawnerMax = 3;
        static float nextSpawner;
        public static int Spawned, LootSpawned; // (dev/tests)

        /// <summary>Host, when the fortress has just been made: its first monsters and its loot.</summary>
        public static void ServerPopulate()
        {
            if (!BlockNet.IsServer || NetherFortress.Nodes == null || NetherFortress.Nodes.Length == 0) return;
            var rng = new System.Random(NetherFortress.Seed + 7);
            var nodes = NetherFortress.Nodes.Select(n => n.transform.position).Where(p => Vector3.Distance(p, NetherFortress.StartPoint) > 12f).ToList();
            if (nodes.Count == 0) nodes = NetherFortress.Nodes.Select(n => n.transform.position).ToList();
            if (!MobSpawns.DevNoNatural)
                for (int i = 0; i < Cap; i++) Spawn(PickType(rng), nodes[rng.Next(nodes.Count)]); // (eclipsed: all of them at once)
            int loot = 6 + nodes.Count / 3;
            for (int i = 0; i < loot; i++) SpawnLoot(nodes[rng.Next(nodes.Count)] + new Vector3((float)rng.NextDouble() * 2f - 1f, 0.6f, (float)rng.NextDouble() * 2f - 1f), rng);
            Plugin.Log.LogInfo($"[nether] populated: {Spawned} monsters, {LootSpawned} loot");
        }

        /// <summary>Wither skeletons, and blazes and piglins a bit less often.</summary>
        static EnemyType PickType(System.Random rng)
        {
            if (PiglinAI.Type != null && rng.NextDouble() < 0.3) return PiglinAI.Type;
            bool blaze = BlazeAI.Type != null && (WitherSkeletonAI.Type == null || rng.NextDouble() < 0.4);
            return blaze ? BlazeAI.Type : WitherSkeletonAI.Type;
        }

        static int Alive() => RoundManager.Instance == null ? 0 : RoundManager.Instance.SpawnedEnemies.Count(e => e != null && !e.isEnemyDead && NetherFortress.Contains(e.transform.position));

        public static void Spawn(EnemyType type, Vector3 at)
        {
            if (type == null || RoundManager.Instance == null) return;
            RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, type);
            Spawned++;
        }

        static void SpawnLoot(Vector3 at, System.Random rng)
        {
            var rm = RoundManager.Instance;
            if (rng.NextDouble() < 0.25)
            {
                // the Nether's own: blaze rods, a wither skull now and then
                double r = rng.NextDouble();
                if (r < 0.35 && ModItems.ByKey.TryGetValue("nether_wart", out var nw)) ModItems.ServerSpawnStack(nw, 1 + rng.Next(3), at); // (#88: brewing starts here)
                else ModItems.ServerSpawnScrapItem(r < 0.85 ? BlazeRod.Key : WitherSkull.Key, at);
                LootSpawned++;
                return;
            }
            var list = rm != null && rm.currentLevel != null ? rm.currentLevel.spawnableScrap : null;
            if (list == null || list.Count == 0) return;
            int total = list.Sum(s => Mathf.Max(0, s.rarity));
            if (total <= 0) return;
            int pick = rng.Next(total);
            SpawnableItemWithRarity chosen = null;
            foreach (var s in list) { pick -= Mathf.Max(0, s.rarity); if (pick < 0) { chosen = s; break; } }
            var item = chosen?.spawnableItem;
            if (item == null || item.spawnPrefab == null) return;
            var parent = rm.spawnedScrapContainer != null ? rm.spawnedScrapContainer : StartOfRound.Instance.propsContainer;
            var go = Object.Instantiate(item.spawnPrefab, at, Quaternion.identity, parent);
            var g = go.GetComponent<GrabbableObject>();
            if (g == null) { Object.Destroy(go); return; }
            int value = Mathf.RoundToInt(rng.Next(item.minValue, item.maxValue + 1) * rm.scrapValueMultiplier * ValueMultiplier);
            g.SetScrapValue(value);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn();
            BlockNet.ServerScrapValue(no.NetworkObjectId, value);
            LootSpawned++;
        }

        /// <summary>Host, every second: the spawners make blazes while someone's near.</summary>
        public static void ServerTick()
        {
            if (!BlockNet.IsServer || NetherFortress.Root == null || MobSpawns.DevNoNatural) return;
            // eclipsed: whatever's killed comes back, away from the players
            if (Time.time >= nextRefill && NetherFortress.Nodes != null)
            {
                nextRefill = Time.time + RefillEvery;
                if (Alive() < Cap)
                {
                    var here = StartOfRound.Instance.allPlayerScripts.Where(p => p != null && p.isPlayerControlled && !p.isPlayerDead).Select(p => p.transform.position).ToList();
                    var far = NetherFortress.Nodes.Select(n => n.transform.position).Where(n => here.All(p => Vector3.Distance(p, n) > 15f)).ToList();
                    if (far.Count > 0) Spawn(PickType(new System.Random(Random.Range(0, int.MaxValue))), far[Random.Range(0, far.Count)]);
                }
            }
            if (BlazeAI.Type == null || Time.time < nextSpawner) return;
            nextSpawner = Time.time + SpawnerEvery;
            var players = StartOfRound.Instance.allPlayerScripts.Where(p => p != null && p.isPlayerControlled && !p.isPlayerDead).ToList();
            foreach (var sp in NetherFortress.Spawners)
            {
                if (!players.Any(p => Vector3.Distance(p.transform.position, sp) < SpawnerRange)) continue;
                int near = RoundManager.Instance.SpawnedEnemies.Count(e => e != null && !e.isEnemyDead && e is BlazeAI && Vector3.Distance(e.transform.position, sp) < SpawnerRange);
                if (near >= SpawnerMax) continue;
                Spawn(BlazeAI.Type, sp + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)));
                BlockNet.ServerSound(sp, "fire", 1f, 0.6f);
            }
        }

        public static void Reset() { Spawned = 0; LootSpawned = 0; nextSpawner = 0f; nextRefill = 0f; }

        public static string Describe() => $"spawned={Spawned} alive={Alive()} cap={Cap} loot={LootSpawned} spawners={NetherFortress.Spawners.Count}";
    }
}
