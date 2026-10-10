using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>A spawn egg in hand: which monster it hatches rides on its saved number (a hash of the monster's name).</summary>
    public class SpawnEggItem : GrabbableObject
    {
        public int EnemyHash;
        public EnemyType Enemy => SpawnEggs.ByHash(EnemyHash);

        public override int GetItemDataToSave() => EnemyHash;
        public override void LoadItemSaveData(int saveData) { EnemyHash = saveData; Refresh(); }
        void Awake() => SpawnFix.Clear(gameObject);

        /// <summary>Its look and scan text, for the monster it hatches.</summary>
        public void Refresh()
        {
            var e = Enemy;
            var model = mainObjectRenderer != null ? mainObjectRenderer.GetComponent<MeshFilter>() : null;
            if (model != null) model.sharedMesh = MeshBuilder.ExtrudedSprite(SpawnEggs.TileFor(e));
            var scan = GetComponentInChildren<ScanNodeProperties>();
            if (scan != null) scan.subText = e != null ? SpawnEggs.NameOf(e) : "";
        }

        public Sprite Icon => SpawnEggs.IconFor(Enemy);
    }

    /// <summary>
    /// Spawn eggs (#85): one for every monster the game knows, its own and other mods' (creepers too), in the creative
    /// menu. Right-click on the ground hatches it there. Each monster gets the Minecraft egg nearest to it (a spider's for
    /// the Bunker Spider, a fox's for the Kidnapper Fox, ...); monsters from other mods get one picked from their name.
    /// </summary>
    public static class SpawnEggs
    {
        public const string Key = "spawn_egg";
        public const string CreativePrefix = "egg:";

        /// <summary>Lethal Company's monsters (a part of their enemyName, lower case) and the Minecraft egg they get.</summary>
        static readonly (string match, string egg)[] Map =
        {
            ("creeper", "creeper"), ("bunker spider", "spider"), ("centipede", "silverfish"), ("hoarding", "armadillo"),
            ("flowerman", "creaking"), ("crawler", "hoglin"), ("blob", "slime"), ("girl", "vex"), ("puffer", "frog"),
            ("nutcracker", "pillager"), ("spring", "copper_golem"), ("jester", "witch"), ("masked", "zombie"),
            ("butler bees", "bee"), ("butler", "villager"), ("clay surgeon", "stray"), ("maneater", "tadpole"),
            ("earth leviathan", "sniffer"), ("forestgiant", "ravager"), ("mouthdog", "wolf"), ("radmech", "iron_golem"),
            ("baboon", "phantom"), ("bush wolf", "fox"), ("red locust", "bee"), ("docile locust", "bat"),
            ("manticoil", "parrot"), ("tulip", "allay"), ("giantkiwi", "chicken"), ("kiwi", "chicken"),
        };

        /// <summary>Eggs for other mods' monsters, picked by their name.</summary>
        static readonly string[] Spare =
        {
            "zombie", "skeleton", "enderman", "blaze", "ghast", "guardian", "evoker", "vindicator", "drowned", "husk",
            "shulker", "endermite", "magma_cube", "piglin", "breeze", "bogged", "warden", "wither_skeleton", "cave_spider",
            "elder_guardian", "zoglin", "polar_bear", "panda", "squid",
        };

        /// <summary>Every Minecraft egg used, for the atlas (tile "egg_&lt;name&gt;" from item/&lt;name&gt;_spawn_egg).</summary>
        public static IEnumerable<string> McEggs => Map.Select(m => m.egg).Concat(Spare).Distinct();

        static List<EnemyType> all;
        static float allAt = -100f;

        /// <summary>The monsters the game knows (loaded enemy types with a prefab), by name; looked up again now and then.</summary>
        public static List<EnemyType> All
        {
            get
            {
                if (all != null && (all.Count > 0 && Time.realtimeSinceStartup - allAt < 30f)) return all;
                allAt = Time.realtimeSinceStartup;
                all = Resources.FindObjectsOfTypeAll<EnemyType>()
                    .Where(e => e != null && e.enemyPrefab != null && !string.IsNullOrEmpty(e.enemyName))
                    .GroupBy(e => e.enemyName).Select(g => g.First())
                    .OrderBy(e => NameOf(e)).ToList();
                return all;
            }
        }

        /// <summary>A stable number for a monster's name (FNV-1a: the same in every game and on every client).</summary>
        public static int Hash(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in name) { h ^= c; h *= 16777619; }
                return (int)(h & 0x7fffffff) | 1; // (never 0: 0 is "no data")
            }
        }

        public static EnemyType ByHash(int h) => h == 0 ? null : All.FirstOrDefault(e => Hash(e.enemyName) == h);
        public static EnemyType ByName(string name) => All.FirstOrDefault(e => e.enemyName == name);

        /// <summary>What players call it: the scan node's title on its prefab (Bracken, not Flowerman).</summary>
        public static string NameOf(EnemyType e)
        {
            if (e == null) return "?";
            var scan = e.enemyPrefab != null ? e.enemyPrefab.GetComponentInChildren<ScanNodeProperties>(true) : null;
            return scan != null && !string.IsNullOrEmpty(scan.headerText) ? scan.headerText : e.enemyName;
        }

        public static string EggOf(EnemyType e)
        {
            if (e == null) return "zombie";
            string n = e.enemyName.ToLowerInvariant();
            foreach (var (match, egg) in Map) if (n.Contains(match)) return egg;
            return Spare[(Hash(e.enemyName) & 0x7fffffff) % Spare.Length];
        }

        public static string TileFor(EnemyType e)
        {
            string t = "egg_" + EggOf(e);
            if (Atlas.Tiles.ContainsKey(t)) return t;
            return Atlas.Tiles.ContainsKey("egg_zombie") ? "egg_zombie" : "item_slime_ball";
        }

        public static Sprite IconFor(EnemyType e) => Atlas.IconFor(TileFor(e));

        // ------------------------------------------------------------------ server
        /// <summary>Server: a spawn egg for a monster, into a creative player's hotbar.</summary>
        public static void ServerGive(ulong client, string enemyName)
        {
            var p = ServerLogic.PlayerFor(client);
            var e = ByName(enemyName);
            if (p == null || e == null || !ModItems.ByKey.TryGetValue(Key, out var item)) return;
            var g = ModItems.ServerSpawnPlain(item, p.transform.position + Vector3.up * 0.3f) as SpawnEggItem;
            if (g == null) return;
            g.EnemyHash = Hash(e.enemyName);
            g.Refresh();
            BlockNet.ServerItemData(g);
            BlockNet.ServerAutoGrab(client, g.NetworkObjectId);
        }

        /// <summary>Server: a player used their egg at a spot: the monster appears there (the egg is used up outside creative).</summary>
        public static void ServerHatch(ulong client, ulong eggId, Vector3 at, float yaw)
        {
            var p = ServerLogic.PlayerFor(client);
            var sor = StartOfRound.Instance;
            if (p == null || sor == null) return;
            if (!Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(eggId, out var no)) return;
            var egg = no.GetComponent<SpawnEggItem>();
            if (egg == null || egg.playerHeldBy != p || egg.Enemy == null) return;
            if (sor.inShipPhase || !sor.shipHasLanded) { BlockNet.ServerToast(client, "Spawn eggs hatch only on a moon."); return; }
            if (Vector3.Distance(p.transform.position, at) > 8f) return;
            var e = egg.Enemy;
            // (a monster with an inside and an outside kind under one name, like the creeper: the one for where you are)
            var twin = Resources.FindObjectsOfTypeAll<EnemyType>().FirstOrDefault(t => t != null && t.enemyName == e.enemyName && t.enemyPrefab != null && t.isOutsideEnemy == !p.isInsideFactory);
            if (twin != null) e = twin;
            RoundManager.Instance.SpawnEnemyGameObject(at, yaw, -1, e);
            if (!GameModes.IsCreative(client))
            {
                int slot = System.Array.IndexOf(p.ItemSlots, egg);
                if (slot >= 0) p.DestroyItemInSlotClientRpc(slot);
            }
            Plugin.Log.LogInfo($"Spawn egg: {NameOf(e)} at {at} for player {client}");
        }
    }
}
