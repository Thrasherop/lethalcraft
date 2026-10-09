using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft armor: helmet, chestplate, leggings and boots in gold, iron and diamond, crafted (never sold) and worn in
    /// the four armor slots of the [I] inventory (or right-click one in hand to put it on). Worn armor turns damage down
    /// the way Minecraft's does (armor points and toughness); falls, drowning and suffocation go straight through, and
    /// the monsters that simply kill you still do. It drops where you die, and the host's orbit save keeps what everyone
    /// wears. The server keeps everyone's armor; each client applies its own player's.
    /// </summary>
    [HarmonyPatch]
    public static class Armor
    {
        public const int Slots = 4; // helmet, chestplate, leggings, boots
        public static readonly string[] PieceNames = { "Helmet", "Chestplate", "Leggings", "Boots" };
        public static readonly string[] PieceKeys = { "helmet", "chestplate", "leggings", "boots" };

        public class Def
        {
            public string Key, Name, Material, Ingredient; public int Slot, Points; public float Toughness; public bool Metal;
        }

        public static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>();

        static Armor()
        {
            // Minecraft's armor points per piece (helmet, chestplate, leggings, boots) and toughness
            foreach (var (mat, name, ingredient, pts, tough, metal) in new[]
            {
                ("golden", "Golden", "gold_ingot", new[] { 2, 5, 3, 1 }, 0f, true),
                ("iron", "Iron", "iron_ingot", new[] { 2, 6, 5, 2 }, 0f, true),
                ("diamond", "Diamond", "diamond", new[] { 3, 8, 6, 3 }, 2f, false),
            })
                for (int s = 0; s < Slots; s++)
                {
                    var d = new Def { Key = mat + "_" + PieceKeys[s], Name = name + " " + PieceNames[s], Material = mat, Ingredient = ingredient, Slot = s, Points = pts[s], Toughness = tough, Metal = metal };
                    Defs[d.Key] = d;
                }
        }

        public static Def Get(string key) => key != null && Defs.TryGetValue(key, out var d) ? d : null;

        // ------------------------------------------------------------------ who wears what
        static readonly Dictionary<ulong, string[]> worn = new Dictionary<ulong, string[]>();

        public static string[] Of(ulong client) => worn.TryGetValue(client, out var a) ? a : (worn[client] = new string[Slots]);
        static ulong LocalId => Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
        public static string[] Local => Of(LocalId);
        public static IEnumerable<KeyValuePair<ulong, string[]>> All => worn;

        public static int PointsOf(string[] a) => a.Sum(k => Get(k)?.Points ?? 0);
        public static float ToughnessOf(string[] a) => a.Sum(k => Get(k)?.Toughness ?? 0f);

        public static void Reset() { worn.Clear(); restored.Clear(); }

        /// <summary>
        /// Minecraft's armor formula: damage x (1 - min(20, max(points / 5, points - damage / (2 + toughness / 4))) / 25),
        /// in Minecraft health (Lethal Company's 100 health is Minecraft's 20).
        /// </summary>
        public static int Reduce(int damage, int points, float toughness)
        {
            if (damage <= 0 || points <= 0) return damage;
            // armor as extra effective health (the balance config): full iron +30%, full diamond +65%
            float bonus = points * Balance.ArmorPerPoint + toughness * Balance.ArmorPerToughness;
            return Mathf.Max(1, Mathf.RoundToInt(damage / (1f + bonus)));
        }

        /// <summary>Damage armor doesn't stop (Minecraft: falling, drowning, suffocating).</summary>
        static bool Bypasses(CauseOfDeath cause, bool fallDamage) =>
            fallDamage || cause == CauseOfDeath.Gravity || cause == CauseOfDeath.Drowning || cause == CauseOfDeath.Suffocation || cause == CauseOfDeath.Abandoned;

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void ReduceDamage(PlayerControllerB __instance, ref int damageNumber, CauseOfDeath causeOfDeath, bool fallDamage)
        {
            if (__instance == null || !__instance.IsOwner || Bypasses(causeOfDeath, fallDamage)) return;
            var a = Local;
            int pts = PointsOf(a);
            if (pts <= 0) return;
            int before = damageNumber;
            damageNumber = Reduce(damageNumber, pts, ToughnessOf(a));
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] armor {pts} pts: {causeOfDeath} damage {before} -> {damageNumber}");
        }

        // ------------------------------------------------------------------ the owner changes what it wears
        /// <summary>Owner client: wear this piece in its slot (null takes it off); the item itself is already out of the hotbar / onto the cursor.</summary>
        public static void SetLocal(int slot, string key)
        {
            var a = (string[])Local.Clone();
            a[slot] = key;
            Of(LocalId)[slot] = key; // at once here, the server confirms
            BlockNet.RequestArmor(a, false, Vector3.zero);
        }

        /// <summary>Owner client: right-click with a piece in hand puts it on (swapping with what's worn there, like Minecraft).</summary>
        public static bool EquipHeld(PlayerControllerB p)
        {
            var g = p.currentlyHeldObjectServer;
            var d = Get(Crafting.KeyOf(g));
            if (d == null) return false;
            string old = Local[d.Slot];
            if (Inventory.Take(p, p.currentItemSlot, 1) == null) return false;
            SetLocal(d.Slot, d.Key);
            if (old != null) Inventory.Give(old, 1);
            Sounds.Play2D("armor." + d.Material, 0.6f, 1f);
            McHud.Toast("Wearing " + d.Name);
            return true;
        }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPostfix]
        static void DropOnDeath(PlayerControllerB __instance)
        {
            if (__instance == null || !__instance.IsOwner || !__instance.isPlayerDead || Commands.KeepInventory) return; // (keepInventory: still worn)
            var a = Local;
            if (a.All(k => k == null)) return;
            BlockNet.RequestArmor(new string[Slots], true, __instance.positionOfDeath);
            for (int i = 0; i < Slots; i++) a[i] = null;
        }

        // ------------------------------------------------------------------ server
        public static void ServerSet(ulong client, string[] keys, bool dropOld, Vector3 at)
        {
            var old = Of(client);
            if (dropOld)
                foreach (var k in old)
                    if (k != null && ModItems.ByKey.TryGetValue(k, out var item))
                        ModItems.ServerSpawnPlain(item, at + Vector3.up * 0.6f + Random.insideUnitSphere * 0.3f);
            var a = new string[Slots];
            for (int i = 0; i < Slots; i++) { var d = Get(keys[i]); a[i] = d != null && d.Slot == i ? d.Key : null; }
            worn[client] = a;
            BlockNet.ServerArmor(client, a);
        }

        /// <summary>Every client (the host included): a player's armor changed.</summary>
        public static void Receive(ulong client, string[] keys) => worn[client] = keys;

        public static void ServerForget(ulong client) { worn.Remove(client); }

        // ------------------------------------------------------------------ saving (host, with the ship, in orbit)
        const string SaveKey = "LMC_Armor_v1";
        static readonly HashSet<string> restored = new HashSet<string>();
        static Dictionary<string, string[]> saved;

        /// <summary>Who a player is across sessions (their Steam id; their name on LAN).</summary>
        public static string IdentityOf(PlayerControllerB p) => p.playerSteamId != 0 ? p.playerSteamId.ToString() : p.playerUsername;

        public static void Save(string file)
        {
            var map = LoadMap(file);
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled) continue;
                map[IdentityOf(p)] = Of(p.actualClientId);
            }
            ES3.Save(SaveKey, string.Join("\n", map.Select(kv => kv.Key + "=" + string.Join(",", kv.Value.Select(k => k ?? "")))), file);
        }

        static Dictionary<string, string[]> LoadMap(string file)
        {
            var map = new Dictionary<string, string[]>();
            if (!ES3.KeyExists(SaveKey, file)) return map;
            foreach (var line in ES3.Load<string>(SaveKey, file).Split('\n'))
            {
                int eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                var parts = line.Substring(eq + 1).Split(',');
                if (parts.Length != Slots) continue;
                map[line.Substring(0, eq)] = parts.Select(k => string.IsNullOrEmpty(k) ? null : k).ToArray();
            }
            return map;
        }

        public static void ClearSave(string file) { if (ES3.KeyExists(SaveKey, file)) ES3.DeleteKey(SaveKey, file); saved = null; restored.Clear(); }

        /// <summary>Server, now and then: players who just joined get back what they wore when the game was saved.</summary>
        public static void ServerRestore()
        {
            var sor = StartOfRound.Instance;
            if (!BlockNet.IsServer || sor == null || GameNetworkManager.Instance == null) return;
            if (saved == null) saved = LoadMap(GameNetworkManager.Instance.currentSaveFileName);
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                string id = IdentityOf(p);
                if (string.IsNullOrEmpty(id) || id == "Player" || !restored.Add(id)) continue;
                if (saved.TryGetValue(id, out var a) && a.Any(k => k != null) && Of(p.actualClientId).All(k => k == null))
                {
                    ServerSet(p.actualClientId, a, false, Vector3.zero);
                    Plugin.Log.LogInfo($"Armor restored for {p.playerUsername}: {string.Join(",", a.Select(k => k ?? "-"))}");
                }
            }
        }
    }
}
