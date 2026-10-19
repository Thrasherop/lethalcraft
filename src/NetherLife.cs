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
        /// <summary>Host: the gold ingots lying about as loot (piglins leave those; they trade for a player's).</summary>
        public static readonly HashSet<ulong> LootGold = new HashSet<ulong>();

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

        /// <summary>Wither skeletons, and blazes and piglins a bit less often; a bastion's are mostly piglins (no blazes).</summary>
        static EnemyType PickType(System.Random rng)
        {
            if (NetherFortress.Kind == NetherFortress.NetherKind.Delta)
            {
                // a basalt delta: fire's creatures: magma cubes most of all, blazes, a ghast or two (#62)
                double dv = rng.NextDouble();
                if (dv < 0.4 && MagmaCubeAI.Big != null) return MagmaCubeAI.Big;
                if (dv < 0.65 && BlazeAI.Type != null) return BlazeAI.Type;
                if (dv < 0.8 && GhastAI.Type != null) return GhastAI.Type;
                return WitherSkeletonAI.Type ?? BlazeAI.Type;
            }
            if (NetherFortress.Kind == NetherFortress.NetherKind.Crimson)
            {
                // a crimson forest: piglins, hoglins and zombified piglins live there (#146, #147), the odd wither skeleton wandered in
                double cv = rng.NextDouble();
                if (cv < 0.25 && HoglinAI.Type != null) return HoglinAI.Type;
                if (cv < 0.45 && ZombifiedPiglinAI.Type != null) return ZombifiedPiglinAI.Type;
                if (PiglinAI.Type != null && cv < 0.9) return PiglinAI.Type;
                return WitherSkeletonAI.Type ?? PiglinAI.Type;
            }
            if (NetherFortress.Kind == NetherFortress.NetherKind.Warped)
            {
                // a warped forest: Minecraft's endermen's, a few skeletons
                if (EndermanAI.Type != null && rng.NextDouble() < 0.75) return EndermanAI.Type;
                return SkeletonAI.Type ?? EndermanAI.Type;
            }
            if (NetherFortress.Valley)
            {
                // a soul sand valley: skeletons (Minecraft's spawn there), endermen now and then, a few wither skeletons
                double v = rng.NextDouble();
                if (v < 0.2 && GhastAI.Type != null) return GhastAI.Type; // (#62: and ghasts drifting over it)
                if (v < 0.6 && SkeletonAI.Type != null) return SkeletonAI.Type;
                if (v < 0.75 && EndermanAI.Type != null) return EndermanAI.Type;
                return WitherSkeletonAI.Type ?? SkeletonAI.Type;
            }
            if (NetherFortress.Bastion && PiglinAI.Type != null)
            {
                // a bastion: piglins, its brutes guarding it, a few wither skeletons
                double r = rng.NextDouble();
                if (r < 0.2 && PiglinBruteAI.Type != null) return PiglinBruteAI.Type;
                return r < 0.8 || WitherSkeletonAI.Type == null ? PiglinAI.Type : WitherSkeletonAI.Type;
            }
            if (PiglinAI.Type != null && rng.NextDouble() < 0.3) return PiglinAI.Type;
            if (ZombifiedPiglinAI.Type != null && rng.NextDouble() < 0.2) return ZombifiedPiglinAI.Type; // (#146)
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
            if (NetherFortress.Forest)
            {
                // a forest's: nether wart (crimson), ender pearls (warped), gold now and then (its piglins trade for it)
                double roll = rng.NextDouble();
                bool warped = NetherFortress.Kind == NetherFortress.NetherKind.Warped;
                if (roll < 0.15 && ModItems.ByKey.TryGetValue(warped ? "ender_pearl" : "nether_wart", out var fi)) { ModItems.ServerSpawnStack(fi, 1 + rng.Next(warped ? 2 : 3), at); LootSpawned++; return; }
                if (!warped && roll < 0.25 && ModItems.ByKey.TryGetValue("gold_ingot", out var fg)) { var st = ModItems.ServerSpawnStack(fg, 1 + rng.Next(3), at); if (st != null) LootGold.Add(st.NetworkObjectId); LootSpawned++; return; }
                // (#244: Minecraft's Nether grows brown and red mushrooms: the fermented spider eye's)
                if (roll < 0.4 && ModItems.ByKey.TryGetValue(rng.Next(2) == 0 ? "brown_mushroom" : "red_mushroom", out var mu)) { ModItems.ServerSpawnStack(mu, 1 + rng.Next(2), at); LootSpawned++; return; }
            }
            if (NetherFortress.Valley)
            {
                // a valley's: bones lying about (bone meal), nether wart (it grows on the soul sand here)
                double roll = rng.NextDouble();
                if (roll < 0.2 && ModItems.ByKey.TryGetValue("bone", out var bn)) { ModItems.ServerSpawnStack(bn, 2 + rng.Next(4), at); LootSpawned++; return; }
                if (roll < 0.3 && ModItems.ByKey.TryGetValue("nether_wart", out var nw)) { ModItems.ServerSpawnStack(nw, 1 + rng.Next(3), at); LootSpawned++; return; }
            }
            if (NetherFortress.Bastion)
            {
                // a bastion's treasure: gold, much of it (raw gold sells; ingots trade with its piglins)
                double roll = rng.NextDouble();
                if (roll < 0.35) { ModItems.ServerSpawnScrap(Blocks.GoldOre, at); LootSpawned++; return; }
                if (roll < 0.55 && ModItems.ByKey.TryGetValue("gold_ingot", out var gi)) { var st = ModItems.ServerSpawnStack(gi, 2 + rng.Next(5), at); if (st != null) LootGold.Add(st.NetworkObjectId); LootSpawned++; return; }
                // (#159: Minecraft's bastion chests hold the netherite upgrade template)
                if (roll < 0.62 && ModItems.ByKey.TryGetValue(Smithing.TemplateKey, out var tpl)) { ModItems.ServerSpawnStack(tpl, 1, at); LootSpawned++; return; }
                // (#244: and now and then an enchanted golden apple, never crafted)
                if (roll < 0.64 && ModItems.ByKey.TryGetValue("enchanted_golden_apple", out var ega)) { ModItems.ServerSpawnStack(ega, 1, at); LootSpawned++; return; }
            }
            if (rng.NextDouble() < 0.25)
            {
                // the Nether's own: nether wart, ender pearls (#126: in every kind of Nether, not only warped forests), blaze
                // rods, a wither skull now and then
                double r = rng.NextDouble();
                // (#145: Minecraft's fortress chests hold horse armor and saddles)
                if (r < 0.015 && ModItems.ByKey.TryGetValue(NameTags.Key, out var ntg)) ModItems.ServerSpawnStack(ntg, 1, at); // (#228: the owner's)
                else if (r < 0.02) { var bp = new[] { "mojang_banner_pattern", "globe_banner_pattern", "piglin_banner_pattern", "flow_banner_pattern", "guster_banner_pattern" }[rng.Next(5)]; if (ModItems.ByKey.TryGetValue(bp, out var bpi)) ModItems.ServerSpawnStack(bpi, 1, at); } // (#225: the patterns that aren't crafted)
                else if (r < 0.06) { var ha = new[] { "iron_horse_armor", "golden_horse_armor", "golden_horse_armor", "diamond_horse_armor", "diamond_horse_armor", "saddle" }[rng.Next(6)]; if (ModItems.ByKey.TryGetValue(ha, out var hai)) ModItems.ServerSpawnStack(hai, 1, at); }
                else if (r < 0.3 && ModItems.ByKey.TryGetValue("nether_wart", out var nw)) ModItems.ServerSpawnStack(nw, 1 + rng.Next(3), at); // (#88: brewing starts here)
                else if (r < 0.45 && ModItems.ByKey.TryGetValue("ender_pearl", out var ep)) ModItems.ServerSpawnStack(ep, 1 + rng.Next(2), at);
                else if (r < 0.55 && ModItems.ByKey.TryGetValue(rng.Next(2) == 0 ? "brown_mushroom" : "red_mushroom", out var mu)) ModItems.ServerSpawnStack(mu, 1 + rng.Next(2), at); // (#244)
                else ModItems.ServerSpawnScrapItem(r < 0.88 ? BlazeRod.Key : WitherSkull.Key, at);
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

        public static void Reset() { Spawned = 0; LootSpawned = 0; nextSpawner = 0f; nextRefill = 0f; LootGold.Clear(); }

        public static string Describe() => $"spawned={Spawned} alive={Alive()} cap={Cap} loot={LootSpawned} spawners={NetherFortress.Spawners.Count}";
    }
}
