using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Armor wearing out, Minecraft's durability (its base a piece, 11 13 15 16 over boots to helmet... times the
    /// material's: leather 5, chainmail and iron 15, gold 7, diamond 33, netherite 37, turtle 25). The armor's number keeps its
    /// trim in its low 9 bits (ArmorTrims), so its wear has the 4 above them: 16 steps, each a sixteenth of it, a hit's wear
    /// a step by chance (on the whole, Minecraft's life for it: finer than Minecraft's own 13-pixel bar shows).
    /// </summary>
    public static class ArmorWear
    {
        public const int Shift = 9, Steps = 16, LowMask = (1 << Shift) - 1;
        static readonly int[] BaseOf = { 11, 16, 15, 13 }; // (helmet, chestplate, leggings, boots)
        public static int Broken; // (dev/tests)

        static int Multiplier(string material) => material == "leather" ? 5 : material == "chainmail" || material == "iron" ? 15 : material == "golden" ? 7 : material == "diamond" ? 33 : material == "netherite" ? 37 : material == "turtle" ? 25 : 0;

        /// <summary>A piece's durability (0: it doesn't wear here: a pumpkin, a head, the elytra, which wears its own way).</summary>
        public static int Max(string key)
        {
            var d = Armor.Get(key);
            if (d == null || !Balance.ToolDurability || ItemData.Is(key, Elytra.Key)) return 0;
            int m = Multiplier(d.Material);
            return m > 0 ? Mathf.Max(1, Mathf.RoundToInt(BaseOf[d.Slot] * m * Balance.DurabilityMultiplier)) : 0;
        }

        public static int StepOf(string key) => (Enchants.UsesOf(ItemData.Of(key)) >> Shift) & (Steps - 1);
        /// <summary>Its wear in Minecraft's points.</summary>
        public static int Damage(string key) { int mx = Max(key); return mx > 0 ? Mathf.RoundToInt(StepOf(key) * mx / (float)Steps) : 0; }

        static string WithStep(string key, int step)
        {
            int d = ItemData.Of(key);
            int low = (Enchants.UsesOf(d) & LowMask) | (Mathf.Clamp(step, 0, Steps - 1) << Shift);
            return ItemData.With(key, Enchants.Data(low, Enchants.EnchOf(d)));
        }

        /// <summary>The key worn to that much (the anvil's: a step's part rounds up, never a free repair; chance: by chance).</summary>
        public static string WithDamage(string key, int damage, bool chance = false)
        {
            int mx = Max(key);
            if (mx <= 0) return key;
            float s = Mathf.Max(0, damage) * Steps / (float)mx;
            int step = chance ? Mathf.FloorToInt(s) + (Random.value < s - Mathf.Floor(s) ? 1 : 0) : Mathf.CeilToInt(s - 0.001f);
            return WithStep(key, step);
        }

        /// <summary>Worn that many points more: the new key, or broke (worn past its last step).</summary>
        public static string Wear(string key, int points, out bool broke)
        {
            broke = false;
            int mx = Max(key);
            if (mx <= 0 || points <= 0) return key;
            float s = points * Steps / (float)mx;
            int add = Mathf.FloorToInt(s) + (Random.value < s - Mathf.Floor(s) ? 1 : 0);
            if (add <= 0) return key;
            int step = StepOf(key) + add;
            if (step >= Steps) { broke = true; return null; }
            return WithStep(key, step);
        }
    }

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

        public static readonly ItemData.TypeMap<Def> Defs = new ItemData.TypeMap<Def>(); // (#228: by type)

        static Armor()
        {
            // Minecraft's armor points per piece (helmet, chestplate, leggings, boots) and toughness
            foreach (var (mat, name, ingredient, pts, tough, metal) in new[]
            {
                ("golden", "Golden", "gold_ingot", new[] { 2, 5, 3, 1 }, 0f, true),
                ("iron", "Iron", "iron_ingot", new[] { 2, 6, 5, 2 }, 0f, true),
                ("diamond", "Diamond", "diamond", new[] { 3, 8, 6, 3 }, 2f, false),
                // (#159: not crafted; diamond armor upgraded at a smithing table)
                ("netherite", "Netherite", null, new[] { 3, 8, 6, 3 }, 3f, false),
            })
                for (int s = 0; s < Slots; s++)
                {
                    var d = new Def { Key = mat + "_" + PieceKeys[s], Name = name + " " + PieceNames[s], Material = mat, Ingredient = ingredient, Slot = s, Points = pts[s], Toughness = tough, Metal = metal };
                    Defs[d.Key] = d;
                }
            // the elytra (#61): worn in the chestplate's place, no armor; glides (Elytra)
            // a carved pumpkin on your head (#130): no armor, but endermen don't mind your stare
            Defs["carved_pumpkin"] = new Def { Key = "carved_pumpkin", Name = "Carved Pumpkin", Material = "pumpkin", Ingredient = null, Slot = 0, Points = 0, Toughness = 0f, Metal = false };
            // mob heads (#142): worn like the pumpkin
            foreach (var h in MobHeads.All.Values) Defs[h.Key] = new Def { Key = h.Key, Name = h.Name, Material = "pumpkin", Ingredient = null, Slot = 0, Points = 0, Toughness = 0f, Metal = false };
            Defs[Banners.OminousKey] = new Def { Key = Banners.OminousKey, Name = "Ominous Banner", Material = "pumpkin", Ingredient = null, Slot = 0, Points = 0, Toughness = 0f, Metal = false }; // (#223: worn as the pillager captain wears it)
            Defs[Elytra.Key] = new Def { Key = Elytra.Key, Name = "Elytra", Material = "elytra", Ingredient = null, Slot = 1, Points = 0, Toughness = 0f, Metal = false };
        }

        public static Def Get(string key) => Defs.TryGetValue(key, out var d) ? d : null; // (by type: a worn key may carry its enchantments, and a name)

        // ------------------------------------------------------------------ who wears what
        static readonly Dictionary<ulong, string[]> worn = new Dictionary<ulong, string[]>();

        public static string[] Of(ulong client) => worn.TryGetValue(client, out var a) ? a : (worn[client] = new string[Slots]);
        static ulong LocalId => Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
        public static string[] Local => Of(LocalId);
        public static IEnumerable<KeyValuePair<ulong, string[]>> All => worn;

        public static int PointsOf(string[] a) => a.Sum(k => Get(k)?.Points ?? 0);
        /// <summary>Protection levels over everything worn (#46).</summary>
        public static int ProtectionOf(string[] a) => LevelsOf(a, EnchKind.Protection);
        public static int LevelsOf(string[] a, EnchKind k) => a.Sum(x => x == null ? 0 : Enchants.Level(Enchants.EnchOf(ItemData.Of(x)), k));
        public static int LevelOn(int slot, EnchKind k) { var a = Local; return slot < a.Length && a[slot] != null ? Enchants.Level(Enchants.EnchOf(ItemData.Of(a[slot])), k) : 0; }

        /// <summary>(#244) Minecraft's enchantment protection points (EPF) against a kind of damage, over all that's worn:
        /// Protection 1 a level against anything; Fire, Blast, Projectile Protection 2 a level against theirs; Feather Falling 3
        /// a level against falls; 20 at most.</summary>
        public static int EpfOf(string[] a, CauseOfDeath cause, bool fall)
        {
            int epf = LevelsOf(a, EnchKind.Protection);
            if (cause == CauseOfDeath.Burning) epf += 2 * LevelsOf(a, EnchKind.FireProtection);
            if (cause == CauseOfDeath.Blast) epf += 2 * LevelsOf(a, EnchKind.BlastProtection);
            if (cause == CauseOfDeath.Stabbing || cause == CauseOfDeath.Gunshots) epf += 2 * LevelsOf(a, EnchKind.ProjectileProtection); // (arrows, tridents, guns)
            if (fall) epf += 3 * LevelsOf(a, EnchKind.FeatherFalling);
            return Mathf.Min(20, epf);
        }
        public static int Thorned; // (dev/tests)
        public static float ToughnessOf(string[] a) => a.Sum(k => Get(k)?.Toughness ?? 0f);

        public static void Reset() { worn.Clear(); restored.Clear(); }

        /// <summary>
        /// Minecraft's armor formula: damage x (1 - min(20, max(points / 5, points - damage / (2 + toughness / 4))) / 25),
        /// in Minecraft health (Lethal Company's 100 health is Minecraft's 20).
        /// </summary>
        public static int Reduce(int damage, int points, float toughness, int protection = 0)
        {
            if (damage <= 0 || (points <= 0 && protection <= 0)) return damage;
            // armor as extra effective health (the balance config): full iron +30%, full diamond +65%; Protection adds 2.5%
            // a level (all four pieces at IV: +40%)
            float bonus = points * Balance.ArmorPerPoint + toughness * Balance.ArmorPerToughness + protection * 0.025f;
            return Mathf.Max(1, Mathf.RoundToInt(damage / (1f + bonus)));
        }

        /// <summary>Damage armor doesn't stop (Minecraft: falling, drowning, suffocating).</summary>
        /// (#244: and what comes as Unknown: starving, wither, poison and harming, an ender pearl's landing: Minecraft's starve,
        /// wither, magic and ender_pearl, all in its bypasses_armor; armor doesn't wear from them either. Protection still
        /// takes its share of them, as Minecraft's does of everything)
        static bool Bypasses(CauseOfDeath cause, bool fallDamage) =>
            fallDamage || cause == CauseOfDeath.Gravity || cause == CauseOfDeath.Drowning || cause == CauseOfDeath.Suffocation || cause == CauseOfDeath.Abandoned || cause == CauseOfDeath.Unknown;

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.Low)]
        static void ReduceDamage(PlayerControllerB __instance, ref int damageNumber, CauseOfDeath causeOfDeath, bool fallDamage)
        {
            if (__instance == null || !__instance.IsOwner) return;
            var a = Local;
            // a fall: Feather Falling on the boots (#46) and Protection, Minecraft's 4% a point (armor itself doesn't stop one).
            // (#244: a real fall comes as Gravity, without the fall flag: Feather Falling had never touched one)
            bool fall = fallDamage || causeOfDeath == CauseOfDeath.Gravity;
            if (fall)
            {
                int fepf = EpfOf(a, causeOfDeath, true), fb = damageNumber;
                if (fepf > 0 && damageNumber > 0) damageNumber = Mathf.Max(1, Mathf.RoundToInt(damageNumber * Enchants.ProtectionFactor(fepf)));
                if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] fall epf {fepf}: damage {fb} -> {damageNumber}");
                return;
            }
            if (Bypasses(causeOfDeath, fallDamage))
            {
                int bepf = causeOfDeath == CauseOfDeath.Abandoned ? 0 : EpfOf(a, causeOfDeath, false);
                if (bepf > 0 && damageNumber > 0) damageNumber = Reduce(damageNumber, 0, 0f, bepf);
                return;
            }
            int thornsSlot = Thorns(__instance, a, causeOfDeath);
            if (damageNumber > 0) WearArmor(__instance, damageNumber, thornsSlot);
            a = Local;
            int pts = PointsOf(a), epf = EpfOf(a, causeOfDeath, false);
            if (pts <= 0 && epf <= 0) return;
            int before = damageNumber;
            // (the enchantments' points as Protection was, 2.5% more health each: #46's balance)
            damageNumber = Reduce(damageNumber, pts, ToughnessOf(a), epf);
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] armor {pts} pts: {causeOfDeath} damage {before} -> {damageNumber}");
        }

        /// <summary>(#244) Thorns: Minecraft's, each piece with it a 15% chance a level to hurt what hit you (1-4 of its
        /// health: a fifth of that here, rounded up to one) — the monster close enough to have struck.</summary>
        static int Thorns(PlayerControllerB p, string[] a, CauseOfDeath cause)
        {
            if (cause == CauseOfDeath.Blast || cause == CauseOfDeath.Burning || cause == CauseOfDeath.Unknown || cause == CauseOfDeath.Electrocution || RoundManager.Instance == null) return -1;
            for (int s = 0; s < a.Length; s++)
            {
                var k = a[s];
                int t = k == null ? 0 : Enchants.Level(Enchants.EnchOf(ItemData.Of(k)), EnchKind.Thorns);
                if (t <= 0 || Random.value >= 0.15f * t) continue;
                EnemyAI near = null; float best = 3f;
                foreach (var e in RoundManager.Instance.SpawnedEnemies)
                {
                    if (e == null || e.isEnemyDead || e is BoatAI || e is MinecartAI) continue;
                    float d = Vector3.Distance(e.transform.position, p.transform.position);
                    if (d < best) { best = d; near = e; }
                }
                if (near == null) return -1;
                near.HitEnemyOnLocalClient(1, near.transform.position - p.transform.position, p, true, -1);
                Sounds.Play("thorns", near.transform.position, 0.7f, 1f);
                Thorned++;
                return s; // (Minecraft's: that piece wears 2 more)
            }
            return -1;
        }

        /// <summary>(#244) Respiration on the helmet: Minecraft's, a breath under water is kept level/(level+1) of the time
        /// (Respiration III: breath lasts four times as long).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "SetFaceUnderwaterFilters")]
        static class Respiration
        {
            static void Prefix(out float __state) => __state = StartOfRound.Instance != null ? StartOfRound.Instance.drowningTimer : 0f;
            static void Postfix(PlayerControllerB __instance, float __state)
            {
                var sor = StartOfRound.Instance;
                if (sor == null || __instance != GameNetworkManager.Instance?.localPlayerController) return;
                int r = LevelOn(0, EnchKind.Respiration);
                if (r > 0 && sor.drowningTimer < __state) sor.drowningTimer = __state - (__state - sor.drowningTimer) / (r + 1);
            }
        }

        /// <summary>(#244) Owner client: a hit wears what's worn, Minecraft's (Inventory.hurtArmor: a quarter of the damage, one
        /// at least, a piece; Unbreaking spares some; a piece worn out breaks). extra: more on one piece (Thorns' 2).</summary>
        static void WearArmor(PlayerControllerB p, int damage, int extraSlot = -1)
        {
            if (GameModes.LocalCreative) return;
            int points = Mathf.Max(1, Mathf.FloorToInt(damage / 5f / 4f)); // (ours 100 health to Minecraft's 20)
            var a = Local;
            for (int s = 0; s < Slots; s++)
            {
                var k = a[s];
                if (k == null || ArmorWear.Max(k) <= 0) continue;
                int ub = Enchants.Level(Enchants.EnchOf(ItemData.Of(k)), EnchKind.Unbreaking), n = 0;
                for (int i = 0; i < points + (s == extraSlot ? 2 : 0); i++) if (Random.value < Enchants.ArmorWearChance(ub)) n++;
                if (n == 0) continue;
                var nk = ArmorWear.Wear(k, n, out bool broke);
                if (broke)
                {
                    SetLocal(s, null);
                    Sounds.Play2D("tool.break", 0.9f, Random.Range(0.9f, 1.1f));
                    McHud.Toast((Get(k)?.Name ?? "Armor") + " broke");
                    ArmorWear.Broken++;
                }
                else if (nk != k) SetLocal(s, nk);
            }
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
            var key = Crafting.KeyOf(g); // (with its enchantments)
            var d = Get(key);
            if (d == null) return false;
            string old = Local[d.Slot];
            if (Inventory.Take(p, p.currentItemSlot, 1) == null) return false;
            SetLocal(d.Slot, key);
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
                    if (k != null && ModItems.ByKey.ContainsKey(k))
                        ModItems.ServerSpawnPlainKeyed(k, at + Vector3.up * 0.6f + Random.insideUnitSphere * 0.3f);
            var a = new string[Slots];
            for (int i = 0; i < Slots; i++) { var d = Get(keys[i]); a[i] = d != null && d.Slot == i ? keys[i] : null; }
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
