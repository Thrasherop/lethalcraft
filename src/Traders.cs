using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's wandering trader (#152): on about 1 moon in 12 ([Mobs] WanderingTraderChance) one wanders near the
    /// ship with two trader llamas on leads. [E] opens its trades, Minecraft's, for emeralds (from emerald ore). Its
    /// llamas can be had the way Minecraft lets you: kill the trader, or [E] to take one off its lead; then ride it
    /// until it accepts you. A llama spits at whoever hits it, and a tamed one carries a chest ([E] with one in hand;
    /// crouch and [E] to open it).
    /// </summary>
    [HarmonyPatch]
    public class WanderingTraderAI : MinecraftMobAI
    {
        public static EnemyType Type;
        public const string Emerald = "scrap_emerald_ore";
        public static int Spawned, Trades; // (dev/tests)
        public static float DevChance = -1f;
        protected override string Sound => "trader";
        protected override bool Passive => true;
        protected override int Xp => 0;
        protected override float RoamSpeed => 1.6f;

        public class Offer { public string Give; public int GiveN; public string Get; public int GetN; public int Uses; }
        public readonly List<Offer> Offers = new List<Offer>();

        // (#244) Minecraft 26.3's wandering trader (trade_set/wandering_trader, villager_trade/wandering_trader): two of what it
        // buys, five of its common offers and two of its uncommon ones, from what this world has (it had a dozen of its own)
        public const string EnchantedPickaxe = "iron_pickaxe#enchanted";
        static readonly (string give, int giveN, string get, int getN, int uses)[] Common =
        {
            (Emerald, 1, "white_dye", 3, 12), (Emerald, 1, "orange_dye", 3, 12), (Emerald, 1, "magenta_dye", 3, 12),
            (Emerald, 1, "light_blue_dye", 3, 12), (Emerald, 1, "yellow_dye", 3, 12), (Emerald, 1, "lime_dye", 3, 12),
            (Emerald, 1, "pink_dye", 3, 12), (Emerald, 1, "gray_dye", 3, 12), (Emerald, 1, "light_gray_dye", 3, 12), (Emerald, 1, "cyan_dye", 3, 12),
            (Emerald, 1, "purple_dye", 3, 12), (Emerald, 1, "blue_dye", 3, 12), (Emerald, 1, "brown_dye", 3, 12), (Emerald, 1, "green_dye", 3, 12),
            (Emerald, 1, "red_dye", 3, 12), (Emerald, 1, "black_dye", 3, 12), (Emerald, 3, "tropical_fish_bucket", 1, 4),
            (Emerald, 3, "pufferfish_bucket", 1, 4), (Emerald, 2, "sea_pickle", 1, 5), (Emerald, 4, "slime_ball", 1, 5),
            (Emerald, 2, "glowstone", 1, 5), (Emerald, 5, "nautilus_shell", 1, 5), (Emerald, 1, "fern", 1, 12), (Emerald, 1, "sugar_cane", 1, 8),
            (Emerald, 1, "pumpkin", 1, 4), (Emerald, 3, "kelp", 1, 12), (Emerald, 3, "cactus", 1, 8), (Emerald, 1, "dandelion", 1, 12),
            (Emerald, 1, "poppy", 1, 12), (Emerald, 1, "blue_orchid", 1, 8), (Emerald, 1, "allium", 1, 12), (Emerald, 1, "azure_bluet", 1, 12),
            (Emerald, 1, "red_tulip", 1, 12), (Emerald, 1, "orange_tulip", 1, 12), (Emerald, 1, "white_tulip", 1, 12),
            (Emerald, 1, "pink_tulip", 1, 12), (Emerald, 1, "oxeye_daisy", 1, 12), (Emerald, 1, "cornflower", 1, 12),
            (Emerald, 1, "lily_of_the_valley", 1, 7), (Emerald, 1, "open_eyeblossom", 1, 7), (Emerald, 1, "wheat_seeds", 1, 12),
            (Emerald, 1, "beetroot_seeds", 1, 12), (Emerald, 1, "pumpkin_seeds", 1, 12), (Emerald, 1, "melon_seeds", 1, 12),
            (Emerald, 5, "acacia_sapling", 1, 8), (Emerald, 5, "birch_sapling", 1, 8), (Emerald, 5, "dark_oak_sapling", 1, 8),
            (Emerald, 5, "jungle_sapling", 1, 8), (Emerald, 5, "oak_sapling", 1, 8), (Emerald, 5, "spruce_sapling", 1, 8),
            (Emerald, 5, "cherry_sapling", 1, 8), (Emerald, 5, "pale_oak_sapling", 1, 8), (Emerald, 5, "mangrove_propagule", 1, 8),
            (Emerald, 5, "poplar_sapling", 1, 8), (Emerald, 3, "brain_coral_block", 1, 8), (Emerald, 3, "bubble_coral_block", 1, 8),
            (Emerald, 3, "fire_coral_block", 1, 8), (Emerald, 3, "horn_coral_block", 1, 8), (Emerald, 3, "tube_coral_block", 1, 8),
            (Emerald, 1, "vine", 3, 4), (Emerald, 1, "pale_hanging_moss", 3, 4), (Emerald, 1, "brown_mushroom", 3, 4),
            (Emerald, 1, "red_mushroom", 3, 4), (Emerald, 1, "lily_pad", 5, 2), (Emerald, 1, "small_dripleaf", 2, 5), (Emerald, 1, "sand", 8, 8),
            (Emerald, 1, "red_sand", 4, 6), (Emerald, 1, "pointed_dripstone", 2, 5), (Emerald, 1, "sulfur_spike", 2, 5),
            (Emerald, 1, "rooted_dirt", 2, 5), (Emerald, 1, "moss_block", 2, 5), (Emerald, 1, "pale_moss_block", 2, 5),
            (Emerald, 1, "wildflowers", 1, 12), (Emerald, 1, "tall_dry_grass", 1, 12), (Emerald, 3, "firefly_bush", 1, 12),
            (Emerald, 2, "golden_dandelion", 1, 12), (Emerald, 1, "name_tag", 1, 5), (Emerald, 1, "shelf_mushroom", 3, 12),
        };
        static readonly (string give, int giveN, string get, int getN, int uses)[] Uncommon =
        {
            (Emerald, 1, "packed_ice", 1, 6), (Emerald, 6, "blue_ice", 1, 6), (Emerald, 1, "gunpowder", 4, 2), (Emerald, 3, "podzol", 3, 6),
            (Emerald, 1, "acacia_log", 8, 4), (Emerald, 1, "birch_log", 8, 4), (Emerald, 1, "dark_oak_log", 8, 4), (Emerald, 1, "jungle_log", 8, 4),
            (Emerald, 1, "oak_log", 8, 4), (Emerald, 1, "spruce_log", 8, 4), (Emerald, 1, "cherry_log", 8, 4), (Emerald, 1, "mangrove_log", 8, 4),
            (Emerald, 1, "pale_oak_log", 8, 4), (Emerald, 1, "poplar_log", 8, 4), (Emerald, 1, "iron_pickaxe#enchanted", 1, 1),
            (Emerald, 5, "potion_long_invisibility", 1, 1),
        };
        static readonly (string give, int giveN, string get, int getN, int uses)[] Buys =
        {
            ("potion_water", 1, Emerald, 1, 2), ("water_bucket", 1, Emerald, 2, 2), ("milk_bucket", 1, Emerald, 2, 2),
            ("fermented_spider_eye", 1, Emerald, 3, 2), ("baked_potato", 4, Emerald, 1, 2), ("hay_block", 1, Emerald, 1, 2),
        };

        public override void Start()
        {
            SetEnemyOutside(true);
            base.Start();
            // (its offers: two it buys, five common, two uncommon, the same on every machine for this trader; only what this
            // world has, picked from: #244, it had picked first and dropped what wasn't here, some traders with three offers)
            var rng = new System.Random((int)(NetworkObjectId * 2654435761UL % int.MaxValue));
            bool Here(string k) => k == EnchantedPickaxe ? ModItems.ByKey.ContainsKey("iron_pickaxe") : ModItems.ByKey.ContainsKey(k);
            var pick = Buys.Where(s => Here(s.give) && Here(s.get)).OrderBy(_ => rng.Next()).Take(2)
                .Concat(Common.Where(s => Here(s.give) && Here(s.get)).OrderBy(_ => rng.Next()).Take(5))
                .Concat(Uncommon.Where(s => Here(s.give) && Here(s.get)).OrderBy(_ => rng.Next()).Take(2));
            foreach (var s in pick)
            {
                string get = s.get;
                if (get == EnchantedPickaxe)
                {
                    // (enchanted at 5-19 levels, Minecraft's enchant_with_levels: the table's choosing at that level for an iron
                    // pickaxe. #244: it had looked among the table's offers for a cost of 5-19, which is 1-3: always plain)
                    var list = Enchants.Select(new System.Random(rng.Next()), EnchTarget.Tool, 14, 5 + rng.Next(15));
                    int e = Enchants.Pack(list);
                    get = e > 0 ? ItemData.With("iron_pickaxe", Enchants.Data(0, e)) : "iron_pickaxe";
                }
                Offers.Add(new Offer { Give = s.give, GiveN = s.giveN, Get = get, GetN = s.getN, Uses = s.uses });
            }
        }

        // ------------------------------------------------------------------ (#244) Minecraft 26.3's WanderingTrader goals
        // UseItemGoal: a potion of invisibility once it's dark outside, milk once it's light again (it shows again); it keeps
        // away from zombies (8 blocks) and pillagers (15); it leaves after 48000 ticks (40 minutes).
        public bool Invisible;
        float drinkCheck, fleeCheck, fleeUntil, despawnAt = -1f, lastUseAt = -100f;
        Vector3 fleeFrom;
        public static int Drank, Fled, Despawned; // (dev/tests)
        public static float DevDespawnSeconds = -1f;

        static bool DarkOutside()
        {
            var tod = TimeOfDay.Instance;
            if (tod == null) return false;
            float clock = tod.normalizedTimeOfDay * 60f * tod.numberOfHours + 360f; // (minutes since midnight: the game's day starts at 6)
            return clock >= 19f * 60f || clock < 5f * 60f;
        }

        protected override bool BusyAI()
        {
            if (!IsServer) return false;
            if (despawnAt < 0f) despawnAt = Time.time + (DevDespawnSeconds > 0f ? DevDespawnSeconds : 2400f);
            if (Time.time > despawnAt && Time.time - lastUseAt > 30f) // (not while someone's trading: Minecraft's isTrading)
            {
                Despawned++;
                Plugin.Log.LogInfo("[trader] its time is up: it leaves");
                KillEnemyOnOwnerClient(overrideDestroy: true);
                return true;
            }
            if ((drinkCheck -= AIIntervalTime) <= 0f)
            {
                drinkCheck = 1f;
                bool dark = DarkOutside();
                if (dark != Invisible) StartCoroutine(Drink(dark));
            }
            if ((fleeCheck -= AIIntervalTime) <= 0f)
            {
                fleeCheck = 0.5f;
                foreach (var e in RoundManager.Instance.SpawnedEnemies)
                {
                    if (e == null || e.isEnemyDead) continue;
                    float range = e is PillagerAI ? 15f : (e is ZombieAI || e is ZombifiedPiglinAI) ? 8f : 0f;
                    if (range <= 0f || Vector3.Distance(e.transform.position, transform.position) > range * BlockWorld.S) continue;
                    if (Time.time > fleeUntil) Fled++;
                    fleeFrom = e.transform.position; fleeUntil = Time.time + 1f;
                    break;
                }
            }
            if (Time.time > fleeUntil) return false;
            if (currentSearch.inProgress) StopSearch(currentSearch);
            var away = transform.position - fleeFrom; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            agent.speed = RoamSpeed * 2f;
            SetDestinationToPosition(transform.position + away.normalized * 4f * BlockWorld.S, checkForPath: false);
            return true;
        }

        bool drinking;
        System.Collections.IEnumerator Drink(bool potion)
        {
            if (drinking) yield break;
            drinking = true;
            BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, potion ? "trader.drink_potion" : "trader.drink_milk", 0.8f, 1f);
            yield return new WaitForSeconds(1.6f); // (Minecraft's 32 ticks to drink)
            drinking = false;
            if (isEnemyDead) yield break;
            Invisible = potion; Drank++;
            BlockNet.ServerMobState(NetworkObjectId, 247, (byte)(potion ? 1 : 0));
            BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, potion ? "trader.disappeared" : "trader.reappeared", 0.8f, 1f);
        }

        void ShowInvisible()
        {
            var m = transform.Find("Model");
            if (m != null) foreach (var r in m.GetComponentsInChildren<Renderer>(true)) r.enabled = !Invisible;
        }

        public override void ServerSyncTo(ulong client) { base.ServerSyncTo(client); if (Invisible) BlockNet.ServerMobState(NetworkObjectId, 247, 1, client); }

        public override string UseTip(string heldKey) => "Trade"; // (" : [E]" is added to every mob tip)

        /// <summary>Host: [E] on it: the trades screen opens on that player's machine.</summary>
        public override void ServerUse(PlayerControllerB p, GrabbableObject held) { lastUseAt = Time.time; BlockNet.ServerMobState(NetworkObjectId, 230, (byte)p.playerClientId); BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, "trader.haggle", 0.7f, 1f); }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 230)
            {
                var local = GameNetworkManager.Instance?.localPlayerController;
                if (local != null && b == (byte)local.playerClientId) TradeUI.Open(this);
            }
            else if (a >= 231 && a < 231 + 16 && a - 231 < Offers.Count) { Offers[a - 231].Uses = b; TradeUI.Changed(this); }
            else if (a == 247) { Invisible = b != 0; ShowInvisible(); }
        }

        /// <summary>Host: a player took an offer, already paid on their machine (TradeUI): what it gives is handed over. (#244:
        /// the host had taken the payment out of their hotbar itself, which only works for stacks; emeralds are single items
        /// since #220, so nothing could be bought.) Turned down (sold out, too far), the payment goes back.</summary>
        public void ServerTrade(ulong sender, int index)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || index < 0 || index >= Offers.Count) return;
            var o = Offers[index];
            if (isEnemyDead || o.Uses <= 0 || Vector3.Distance(p.transform.position, transform.position) > 6f) { Inventory.ServerSpawnFor(sender, o.Give, o.GiveN); return; }
            Inventory.ServerSpawnFor(sender, o.Get, o.GetN);
            o.Uses--; lastUseAt = Time.time;
            BlockNet.ServerMobState(NetworkObjectId, (byte)(231 + index), (byte)o.Uses);
            BlockNet.ServerSound(transform.position + Vector3.up * 1.6f, "trader.yes", 0.8f, 1f);
            BlockNet.ServerXp(sender, Random.Range(3, 7), transform.position + Vector3.up); // (#244: Minecraft's 3-6 a trade)
            Trades++;
        }

        protected override void ServerDrops(Vector3 at) { }

        public static void Register()
        {
            Type = Register<WanderingTraderAI>("WanderingTrader", "Wandering Trader", "Trades for emeralds", 0, 4, 1.95f, 2, 1.6f, BuildModel, daytime: true, length: 0.6f);
            LlamaAI.Register();
        }

        // ------------------------------------------------------------------ one in twelve moons, as the ship lands
        static float landed = -100f;
        [HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc"), HarmonyPostfix]
        static void Landed(RoundManager __instance)
        {
            if (Type == null || !BlockNet.IsServer || TerrainCarver.AtCompany || !Landing.Once(ref landed)) return;
            float chance = DevChance >= 0f ? DevChance : Plugin.WanderingTraderChance.Value;
            if (Random.value >= chance) return;
            __instance.StartCoroutine(SpawnLater());
        }

        static IEnumerator SpawnLater()
        {
            yield return new WaitForSeconds(12f);
            if (ServerSpawnNear(StartOfRound.Instance.elevatorTransform.position)) Plugin.Log.LogInfo("[trader] a wandering trader came, with two llamas");
        }

        /// <summary>Host: a trader somewhere 15-40 m from a point, on open ground, and its two llamas on leads.</summary>
        public static bool ServerSpawnNear(Vector3 near)
        {
            for (int i = 0; i < 20; i++)
            {
                var at = near + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(15f, 40f);
                if (!UnityEngine.AI.NavMesh.SamplePosition(at, out var h, 6f, UnityEngine.AI.NavMesh.AllAreas) || h.position.y < -100f) continue;
                MobSpawns.DevSpawnUntil = Time.time + 1f;
                var tr = RoundManager.Instance.SpawnEnemyGameObject(h.position, Random.Range(0f, 360f), -1, Type);
                Spawned++;
                if (LlamaAI.Type != null)
                    for (int l = 0; l < 2; l++)
                    {
                        var lr = RoundManager.Instance.SpawnEnemyGameObject(h.position + new Vector3(l == 0 ? 1.5f : -1.5f, 0f, -1.5f), 0f, -1, LlamaAI.Type);
                        if (lr.TryGet(out Unity.Netcode.NetworkObject lno) && lno.GetComponent<LlamaAI>() is LlamaAI llama && tr.TryGet(out Unity.Netcode.NetworkObject tno))
                            llama.ServerLeashToTrader(tno.GetComponent<WanderingTraderAI>());
                    }
                return true;
            }
            return false;
        }

        static Material sharedMat;

        // ------------------------------------------------------------------ the model: Minecraft's villager shape (64x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_WanderingTrader", "entity/wandering_trader", new Color(0.2f, 0.3f, 0.6f));
            var head = new GameObject("Head").transform; head.SetParent(parent, false); head.localPosition = new Vector3(0, 24, 0) * k;
            EntityModels.Part(head, "Skull", new Vector3(0, 5, 0), new Vector3(8, 10, 8), 0, 0, k, sharedMat);
            EntityModels.Part(head, "Nose", new Vector3(0, 1, 5), new Vector3(2, 4, 2), 24, 0, k, sharedMat);
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 6), 16, 20, k, sharedMat);
            // (the robe: Minecraft's 8x18x6 box, half a pixel bigger all round; its texture as the box's)
            EntityModels.Part(parent, "Robe", new Vector3(0, 15, 0), new Vector3(8, 18, 6), 0, 38, k, sharedMat).transform.localScale = new Vector3(9f / 8f, 19f / 18f, 7f / 6f);
            // (its arms folded across its front, as Minecraft's villagers hold them)
            var arms = new GameObject("Folded").transform; arms.SetParent(parent, false);
            arms.localPosition = new Vector3(0, 21, 1) * k; arms.localRotation = Quaternion.Euler(-43f, 0f, 0f);
            EntityModels.Part(arms, "ArmL", new Vector3(6, -2, 0), new Vector3(4, 8, 4), 44, 22, k, sharedMat);
            EntityModels.Part(arms, "ArmR", new Vector3(-6, -2, 0), new Vector3(4, 8, 4), 44, 22, k, sharedMat);
            EntityModels.Part(arms, "Hands", new Vector3(0, -4, 0), new Vector3(8, 4, 4), 40, 38, k, sharedMat);
            foreach (var (n, x) in new[] { ("LegL", 2f), ("LegR", -2f) })
            {
                var l = new GameObject(n).transform; l.SetParent(parent, false); l.localPosition = new Vector3(x, 12, 0) * k;
                EntityModels.Part(l, "Leg", new Vector3(0, -6, 0), new Vector3(4, 12, 4), 0, 22, k, sharedMat);
            }
        }
    }

    /// <summary>A trader llama (#152): on its trader's lead until freed; then ride it till it accepts you. It spits at
    /// whoever hits it; tamed, it carries a chest.</summary>
    public class LlamaAI : MinecraftMobAI
    {
        public static EnemyType Type;
        protected override string Sound => "llama";
        protected override bool Passive => true;
        protected override int Xp => 1;
        public bool Tamed, HasChest;
        public int Temper;
        public ulong Rider = NoRider;
        public const ulong NoRider = ulong.MaxValue;
        static readonly string[] Coats = { "creamy", "white", "brown", "gray" };
        public static int Spits; // (dev/tests)

        // ------------------------------------------------------------------ (#244) Minecraft's llama
        // Its strength (1-3, one in 25 up to 5): its chest holds 3 a point; a young one's from its parents'. Hay breeds a tamed
        // one (a young llama, which grows into a plain llama), wheat and hay heal it, grow a young one and raise its temper. A
        // carpet on a tamed one. Llamas near a led one fall in behind it, nose to tail (a caravan, eight at most).
        public static EnemyType PlainType, BabyType;
        int bornStrength = -1, bornHealth = -1;
        public int Carpet = -1; // (Dyes.Colors' index)
        public LlamaAI CaravanHead, CaravanTail;
        float caravanCheck, healAcc;
        public static int Ate, Carpets, Joined; // (dev/tests)
        protected virtual bool TraderDecor => true;
        public int Strength
        {
            get
            {
                if (bornStrength > 0) return bornStrength;
                ulong h = LookId * 0x94D049BB133111EBUL;
                int max = (h % 100UL) < 4UL ? 5 : 3;
                return 1 + (int)((h >> 8) % (ulong)max);
            }
        }
        public int HealthStat => bornHealth >= 0 ? bornHealth : McHealthOf(LookId);
        protected override string[] Foods => new[] { "wheat", "hay_block" }; // (Minecraft's llama_food)
        protected override string[] TemptItems => new[] { "hay_block" }; // (llama_tempt_items)

        /// <summary>Host: a young one of these two (Minecraft's Llama.getBreedOffspring).</summary>
        public void ServerInherit(LlamaAI a, LlamaAI b)
        {
            int s = Random.Range(0, Mathf.Max(a.Strength, b.Strength)) + 1;
            if (Random.value < 0.03f) s++;
            bornStrength = Mathf.Clamp(s, 1, 5);
            bornHealth = Mathf.RoundToInt(HorseAI.Offspring(a.HealthStat, b.HealthStat, HorseAI.MinHealth, HorseAI.MaxHealth));
            ServerSendLlama(null);
        }

        void ServerSendLlama(ulong? to)
        {
            void Send(byte a, byte b) { if (to.HasValue) BlockNet.ServerMobState(NetworkObjectId, a, b, to.Value); else BlockNet.ServerMobState(NetworkObjectId, a, b); }
            if (bornStrength > 0) Send(244, (byte)bornStrength);
            if (bornHealth >= 0) Send(246, (byte)bornHealth);
            if (Carpet >= 0) Send(245, (byte)(Carpet + 1));
        }

        /// <summary>Host: Minecraft's Llama.handleEating: wheat (2 health, 10 s older, temper 3) and hay (10, 90 s, 6; a tamed
        /// grown one in love).</summary>
        bool ServerEat(PlayerControllerB p, StackItem held)
        {
            string key = held.ItemKey;
            float heal; int age, temper;
            if (key == "wheat") { heal = 2f; age = 10; temper = 3; }
            else if (key == "hay_block") { heal = 10f; age = 90; temper = 6; }
            else return false;
            bool used = false, eaten = false;
            if (key == "hay_block" && Tamed && !IsBaby && !InLove)
            {
                int born = AnimalCare.Born;
                ServerFeedToBreed(p, held);
                eaten = InLove || AnimalCare.Born != born;
                used |= eaten;
            }
            int full = LookHp > 0 ? LookHp : 4;
            if (enemyHP < full) { healAcc += heal / 5f; int whole = Mathf.FloorToInt(healAcc); if (whole > 0) { enemyHP = Mathf.Min(full, enemyHP + whole); healAcc -= whole; } used = true; }
            if (IsBaby) { AgeUp(age); used = true; }
            if ((used || !Tamed) && Temper < 100) { Temper = Mathf.Min(100, Temper + temper); used = true; }
            if (used && !eaten) { held.ServerUseUp(p); BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "llama.eat", 0.8f, 1f); }
            if (used) Ate++;
            return used;
        }

        /// <summary>Host: Minecraft's LlamaFollowCaravanGoal: not led itself, it falls in behind the last of a led llama's line
        /// within 9 blocks (and 4 up or down), two blocks back; the line broken (its head gone, or no lead at the front), it leaves.</summary>
        bool CaravanAI()
        {
            if (LeashedTo != NoLeash || Rider != NoRider) { LeaveCaravan(); return false; }
            if (CaravanHead != null && (CaravanHead.isEnemyDead || !FirstLed(CaravanHead) || Vector3.Distance(CaravanHead.transform.position, transform.position) > 40f * BlockWorld.S)) LeaveCaravan();
            if (CaravanHead == null)
            {
                if ((caravanCheck -= AIIntervalTime) > 0f) return false;
                caravanCheck = 1f;
                LlamaAI best = null; float bd = float.MaxValue;
                foreach (var pass in new[] { 0, 1 })
                {
                    foreach (var e in RoundManager.Instance.SpawnedEnemies)
                    {
                        if (!(e is LlamaAI c) || c == this || c.isEnemyDead || c.CaravanTail != null) continue;
                        var d = c.transform.position - transform.position;
                        if (Mathf.Abs(d.x) > 9f * BlockWorld.S || Mathf.Abs(d.z) > 9f * BlockWorld.S || Mathf.Abs(d.y) > 4f * BlockWorld.S) continue;
                        if (pass == 0 ? c.CaravanHead == null : c.LeashedTo == NoLeash) continue;
                        if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = c; }
                    }
                    if (best != null) break;
                }
                if (best == null || bd < 4f * BlockWorld.S * BlockWorld.S || !FirstLed(best) || Length(best) >= 8) return false;
                CaravanHead = best; best.CaravanTail = this; Joined++;
            }
            if (currentSearch.inProgress) StopSearch(currentSearch);
            var to = CaravanHead.transform.position - transform.position;
            float dist = to.magnitude;
            agent.speed = RoamSpeed * (dist > 26f * BlockWorld.S ? 3f : 2.1f);
            if (dist > 2f * BlockWorld.S) SetDestinationToPosition(transform.position + to.normalized * (dist - 2f * BlockWorld.S), checkForPath: false);
            else if (agent.hasPath) agent.ResetPath();
            return true;
        }

        static bool FirstLed(LlamaAI l) { for (int i = 0; l != null && i < 10; i++, l = l.CaravanHead) if (l.LeashedTo != NoLeash) return true; return false; }
        static int Length(LlamaAI l) { int n = 1; while (l.CaravanHead != null && n < 10) { l = l.CaravanHead; n++; } return n; }
        void LeaveCaravan() { if (CaravanHead != null && CaravanHead.CaravanTail == this) CaravanHead.CaravanTail = null; CaravanHead = null; }

        protected override bool BusyAI() => IsServer && CaravanAI();

        /// <summary>Every machine: its blanket: a carpet's colour, the trader's, or none.</summary>
        void ShowDecor()
        {
            var d = transform.Find("Model/Decor");
            if (d == null) return;
            bool on = Carpet >= 0 || TraderDecor;
            d.gameObject.SetActive(on);
            if (!on) return;
            var tex = Carpet >= 0 && Carpet < Dyes.Colors.Length ? "entity/equipment/llama_body/" + Dyes.Colors[Carpet] : "entity/equipment/llama_body/trader_llama";
            var mat = EntityModels.Material("LMC_LlamaDecor_" + tex.Substring(tex.LastIndexOf('/') + 1), tex, new Color(0.2f, 0.3f, 0.7f));
            foreach (var r in d.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = mat;
        }

        /// <summary>Its chest's storage key (Chests: a frame no block uses; y its slots, 3 a point of strength: 0 the old 15).</summary>
        public BlockKey ChestKey => new BlockKey(Chests.MobFrame, 0, new Vector3Int((int)(NetworkObjectId & 0xFFFFFF), Strength >= 5 ? 0 : 3 * Strength, 0));
        public override BlockKey? Storage => HasChest ? ChestKey : (BlockKey?)null; // (#227)
        public override string SaveState() => Tamed || HasChest || bornStrength > 0 || Carpet >= 0 ? $"{(Tamed ? 1 : 0)},{(HasChest ? 1 : 0)},{Temper}|s:{Strength}:{Carpet}:{bornHealth}" : null;
        public override void ServerSyncTo(ulong client)
        {
            if (HasChest) BlockNet.ServerMobState(NetworkObjectId, 241, 1, client);
            if (Tamed) BlockNet.ServerMobState(NetworkObjectId, 243, 1, client);
            ServerSendLlama(client);
        }
        /// <summary>(#227) Back from the save: tamed, its chest.</summary>
        public override void LoadState(string s)
        {
            // (#244: "|s:strength:carpet:health"; a llama saved before had a chest of 15: strength 5)
            int si = (s ?? "").IndexOf("|s:");
            if (si >= 0)
            {
                var g = s.Substring(si + 3).Split(':');
                if (g.Length >= 3) { int.TryParse(g[0], out bornStrength); int.TryParse(g[1], out Carpet); int.TryParse(g[2], out bornHealth); }
                s = s.Substring(0, si);
            }
            else if ((s ?? "").Split(',').Length >= 3 && s.Split(',')[1] == "1") bornStrength = 5;
            ShowDecor(); if (!IsBaby) LookHealth(4, HealthStat);
            ServerSendLlama(null);
            var p = (s ?? "").Split(',');
            if (p.Length < 3) return;
            Tamed = p[0] == "1"; HasChest = p[1] == "1"; int.TryParse(p[2], out Temper);
            ReceiveState(241, (byte)(HasChest ? 1 : 0));
            BlockNet.ServerMobState(NetworkObjectId, 241, (byte)(HasChest ? 1 : 0));
            BlockNet.ServerMobState(NetworkObjectId, 243, (byte)(Tamed ? 1 : 0));
        }

        public void ServerLeashToTrader(WanderingTraderAI t)
        {
            if (t == null) return;
            LeashedTo = TraderLeash; LeashMob = t;
            BlockNet.ServerMobState(NetworkObjectId, 202, 0);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyLook();
        }

        protected override void ApplyLook()
        {
            LookHealth(4, HealthStat); // (#244: its own health, 3-6)
            var coat = EntityModels.Material("LMC_Llama_" + Coats[(int)(LookId % 4UL)], "entity/llama/" + Coats[(int)(LookId % 4UL)], new Color(0.8f, 0.75f, 0.6f));
            var model = transform.Find("Model");
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = r.transform; bool other = false;
                while (t != null && t != model) { if (t.name == "Decor" || t.name == "Chests") { other = true; break; } t = t.parent; }
                if (!other) r.sharedMaterial = coat;
            }
            var chests = model.Find("Chests");
            if (chests != null) chests.gameObject.SetActive(HasChest);
            ShowDecor();
        }

        public override string UseTip(string heldKey)
        {
            if (LeashedTo == TraderLeash) return "Take it off the lead";
            if (Rider != NoRider) return null;
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (Tamed && !HasChest && heldKey == "chest") return "Put a chest on it";
            if (Tamed && !IsBaby && heldKey != null && heldKey.EndsWith("_carpet") && heldKey != "moss_carpet" && heldKey != "pale_moss_carpet") return "Put the carpet on it";
            if (heldKey == "wheat" || heldKey == "hay_block") return "Feed";
            if (Tamed && HasChest && p != null && p.isCrouching) return "Open its chest";
            return base.UseTip(heldKey) ?? (Tamed ? "Ride" : "Ride (tame it)");
        }

        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            var st = held as StackItem;
            if (LeashedTo == TraderLeash) { ServerUnleash(false); BlockNet.ServerSound(transform.position + Vector3.up, "lead.break", 0.7f, 1f); return; }
            if (Rider == p.actualClientId) { ServerGetOff(); return; }
            if (Rider != NoRider) return;
            if (Tamed && !HasChest && st != null && st.ItemKey == "chest")
            {
                st.ServerSetCount(st.Count - 1, despawnIfEmpty: true);
                HasChest = true; BlockNet.ServerMobState(NetworkObjectId, 241, 1);
                BlockNet.ServerSound(transform.position + Vector3.up, "llama.chest", 0.8f, 1f);
                return;
            }
            if (Tamed && HasChest && p.isCrouching) { BlockNet.ServerMobState(NetworkObjectId, 242, (byte)p.playerClientId); return; }
            if (st != null && UseTip(st.ItemKey) == "Put the carpet on it")
            {
                // (#244) Minecraft's: a carpet on a tamed llama (the one it wore drops)
                int c = System.Array.IndexOf(Dyes.Colors, st.ItemKey.Substring(0, st.ItemKey.Length - "_carpet".Length));
                if (c < 0) return;
                if (Carpet >= 0) ModItems.ServerSpawnPlainKeyedOrStack(Dyes.Colors[Carpet] + "_carpet", transform.position + Vector3.up);
                st.ServerUseUp(p); Carpet = c; Carpets++;
                BlockNet.ServerMobState(NetworkObjectId, 245, (byte)(c + 1));
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "llama.swag", 0.8f, 1f);
                return;
            }
            if (st != null && (st.ItemKey == "wheat" || st.ItemKey == "hay_block")) { ServerEat(p, st); return; }
            if (st != null && (st.ItemKey == "lead" || UseTip(st.ItemKey) == "Feed")) { base.ServerUse(p, held); return; }
            // ride it: untamed, it throws you off after a moment unless it's ready to accept you
            Rider = p.actualClientId;
            BlockNet.ServerMobState(NetworkObjectId, 240, (byte)p.playerClientId);
            if (!Tamed) StartCoroutine(Buck(p.actualClientId));
        }

        IEnumerator Buck(ulong who)
        {
            yield return new WaitForSeconds(Random.Range(1.5f, 3f));
            if (Rider != who || isEnemyDead) yield break;
            if (Random.Range(0, 100) < Temper) { Tamed = true; BlockNet.ServerMobState(NetworkObjectId, 243, 1); BlockNet.ServerToast(who, "The llama accepts you."); yield break; }
            Temper = Mathf.Min(100, Temper + 10);
            ServerGetOff();
            BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "llama.angry", 0.8f, 1f);
        }

        void ServerGetOff() { Rider = NoRider; BlockNet.ServerMobState(NetworkObjectId, 240, 255); }

        public override void ReceiveState(byte a, byte b)
        {
            var local = GameNetworkManager.Instance?.localPlayerController;
            if (a == 240)
            {
                var rider = b < 255 && b < StartOfRound.Instance.allPlayerScripts.Length ? StartOfRound.Instance.allPlayerScripts[b] : null;
                Rider = rider != null ? rider.actualClientId : NoRider;
                if (rider != null && rider == local) LlamaRiding.Llama = this;
                else if (LlamaRiding.Llama == this) LlamaRiding.GetOff();
            }
            else if (a == 241) { HasChest = b != 0; var c = transform.Find("Model/Chests"); if (c != null) c.gameObject.SetActive(HasChest); }
            else if (a == 242) { if (local != null && b == (byte)local.playerClientId) ChestUI.Open(ChestKey); }
            else if (a == 243) Tamed = b != 0;
            else if (a == 244) bornStrength = b;
            else if (a == 245) { Carpet = b - 1; ShowDecor(); }
            else if (a == 246) { bornHealth = b; if (!IsBaby) LookHealth(4, bornHealth); }
        }

        /// <summary>Hit: it spits at whoever hit it (a little hurt), and runs.</summary>
        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (!IsServer || isEnemyDead || playerWhoHit == null) return;
            var from = transform.position + transform.forward * 0.6f + Vector3.up * 1.7f;
            // (#244: Minecraft's LlamaSpit: aimed a third of the way up them, raised a fifth of the distance, 1.5 blocks a tick)
            var at = playerWhoHit.transform.position + Vector3.up * 0.6f;
            var d = at - from; float flat = new Vector2(d.x, d.z).magnitude;
            BlockNet.ServerSnowball(from, (d + Vector3.up * flat * 0.2f).normalized * 30f * BlockWorld.S, NetworkObjectId, Snowball.SpitKind);
            BlockNet.ServerSound(from, "llama.spit", 0.8f, 1f);
            Spits++;
        }

        protected override void ServerDrops(Vector3 at)
        {
            if (IsBaby) return; // (Minecraft's: a young one drops nothing)
            if (ModItems.ByKey.TryGetValue("leather", out var l)) { int n = Random.Range(0, 3); if (n > 0) ModItems.ServerSpawnStack(l, n, at); }
            if (Carpet >= 0) ModItems.ServerSpawnPlainKeyedOrStack(Dyes.Colors[Carpet] + "_carpet", at);
            if (HasChest)
            {
                if (ModItems.ByKey.TryGetValue("chest", out var c)) ModItems.ServerSpawnStack(c, 1, at);
                var contents = Chests.Of(ChestKey);
                if (contents != null) { Chests.SpawnContents(contents, at); Chests.All.Remove(ChestKey); }
            }
        }

        public static void Register()
        {
            Type = Register<LlamaAI>("TraderLlama", "Trader Llama", "A wandering trader's llama", 0, 4, 1.9f, 4, 1.4f, BuildModel, daytime: true, length: 1.0f);
            // (#244) a young llama (bred with hay) grows into a plain llama: Minecraft's
            PlainType = Register<PlainLlamaAI>("Llama", "Llama", "A llama: hay breeds it", 0, 4, 1.9f, 4, 1.4f, BuildModel, daytime: true, length: 1.0f);
            BabyType = RegisterBaby<PlainLlamaAI>(PlainType, "BabyLlama", "Young Llama", 4, 1.0f, 1.4f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; }, length: 0.6f);
            AnimalCare.BabyOf[Type] = BabyType;
        }

        static Material sharedMat, decorMat;

        // ------------------------------------------------------------------ the model: Minecraft's llama (128x64), its trader decor
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Llama", "entity/llama/creamy", new Color(0.8f, 0.75f, 0.6f));
            Build(parent, k, sharedMat);
            if (decorMat == null) decorMat = EntityModels.Material("LMC_TraderLlamaDecor", "entity/equipment/llama_body/trader_llama", new Color(0.2f, 0.3f, 0.7f));
            // (its trader's blanket and harness: the same model a touch bigger, round its middle)
            var decor = new GameObject("Decor").transform; decor.SetParent(parent, false);
            decor.localScale = Vector3.one * 1.04f; decor.localPosition = new Vector3(0f, -12f * k * 0.04f, 0f);
            Build(decor, k, decorMat);
            // two chests on its sides (shown once it carries one)
            var chests = new GameObject("Chests").transform; chests.SetParent(parent, false);
            foreach (var x in new[] { 7.5f, -7.5f })
                EntityModels.Part(chests, "ChestBox", new Vector3(x, 15, -2), new Vector3(3, 8, 8), 45, 28, k, sharedMat);
            chests.gameObject.SetActive(false);
        }

        // (Minecraft's model is y down from 24 px with the head toward -z; here feet at 0, head toward +z)
        static void Build(Transform parent, float k, Material mat)
        {
            Vector3 G(Vector3 v) => v;
            var head = new GameObject("Head").transform; head.SetParent(parent, false); head.localPosition = new Vector3(0, 17, 6) * k;
            EntityModels.Part(head, "Snout", new Vector3(0, 12, 5.5f), G(new Vector3(4, 4, 9)), 0, 0, k, mat);
            EntityModels.Part(head, "Neck", new Vector3(0, 7, 3), G(new Vector3(8, 18, 6)), 0, 14, k, mat);
            EntityModels.Part(head, "EarL", new Vector3(2.5f, 17.5f, 3), G(new Vector3(3, 3, 2)), 17, 0, k, mat);
            EntityModels.Part(head, "EarR", new Vector3(-2.5f, 17.5f, 3), G(new Vector3(3, 3, 2)), 17, 0, k, mat);
            // (the body: Minecraft's box stood on end and laid flat)
            var body = new GameObject("BodyPivot").transform; body.SetParent(parent, false);
            body.localPosition = new Vector3(0, 19, -2) * k; body.localRotation = Quaternion.Euler(90f, 0f, 0f);
            EntityModels.Part(body, "Body", new Vector3(0, 1, 2), G(new Vector3(12, 18, 10)), 29, 0, k, mat);
            foreach (var (n, x, z) in new[] { ("LegL", 3.5f, 5f), ("LegR", -3.5f, 5f), ("LegBL", 3.5f, -6f), ("LegBR", -3.5f, -6f) })
            {
                var l = new GameObject(n).transform; l.SetParent(parent, false); l.localPosition = new Vector3(x, 14, z) * k;
                EntityModels.Part(l, "Leg", new Vector3(0, -7, 0), G(new Vector3(4, 14, 4)), 29, 29, k, mat);
            }
        }
    }

    /// <summary>(#244) A plain llama (a trader llama's young, grown): no trader's blanket.</summary>
    public class PlainLlamaAI : LlamaAI
    {
        protected override bool TraderDecor => false;
    }

    /// <summary>The local player on a llama: seated every frame (it goes where it likes); Crouch gets off.</summary>
    [HarmonyPatch]
    public static class LlamaRiding
    {
        public static LlamaAI Llama;

        public static void GetOff()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            var l = Llama; Llama = null;
            if (p == null) return;
            p.disableMoveInput = false;
            if (l != null) p.TeleportPlayer(l.transform.position + l.transform.right * 1.4f + Vector3.up * 0.2f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool CrouchOff(PlayerControllerB __instance)
        {
            if (Llama == null || __instance != GameNetworkManager.Instance?.localPlayerController) return true;
            BlockNet.RequestAnimal(Llama.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Sit(PlayerControllerB __instance)
        {
            var p = __instance; var l = Llama;
            if (l == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            if (l.isEnemyDead || p.isPlayerDead) { GetOff(); return; }
            p.disableMoveInput = true;
            p.transform.position = l.transform.position + Vector3.up * (19f * EntityModels.Px * BlockWorld.S - 0.9f);
            p.fallValue = 0f; p.fallValueUncapped = 0f;
        }
    }

    /// <summary>The trades screen (#152): each offer a row, what it asks and what it gives; click one to trade.</summary>
    public class TradeUI : SlotScreen
    {
        public static TradeUI Instance;
        const int AreaOffer = 10;
        WanderingTraderAI trader;
        readonly List<(View give, View get)> rows = new List<(View, View)>();

        protected override void Awake() { base.Awake(); Instance = this; }
        protected override void OnDestroy() { base.OnDestroy(); if (Instance == this) Instance = null; }

        // (the hotbar below the last offer: seven offers had run over its "Hotbar" label)
        int OfferCount => trader != null ? trader.Offers.Count : 0;
        protected override Vector2 PanelSize => new Vector2(176, HotbarTop + 25);
        protected override string Title => "Wandering Trader";
        protected override int HotbarTop => Mathf.Max(141, 18 + OfferCount * 17 + 18);
        protected override bool WantsStorage => false;

        protected override void LayoutContent()
        {
            rows.Clear();
            int n = trader != null ? trader.Offers.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var g = MakeSlot(panel, new Vector2(30, -(18 + i * 17)), AreaOffer, i);
                MakeArrow(new Vector2(62, -(18 + i * 17)));
                var o = MakeSlot(panel, new Vector2(100, -(18 + i * 17)), AreaOffer, i);
                rows.Add((g, o));
            }
        }

        public static void Open(WanderingTraderAI t)
        {
            var ui = Instance;
            if (ui == null || t == null || !CanOpen(Local)) return;
            ui.trader = t;
            ui.Show();
        }

        public static void Changed(WanderingTraderAI t) { if (Instance != null && Instance.open && Instance.trader == t) Instance.Refresh(); }

        protected override void ClickContent(int area, int index, bool right, bool shift)
        {
            if (area != AreaOffer || trader == null || index >= trader.Offers.Count) return;
            var o = trader.Offers[index];
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null || o.Uses <= 0) return;
            // (#244) paid here, from your own hotbar (the host can't take a single item, like an emerald, out of your slots)
            string type = ItemData.TypeOf(o.Give);
            var slots = Enumerable.Range(0, p.ItemSlots.Length).Where(i => p.ItemSlots[i] != null && ItemData.Is(Crafting.KeyOf(p.ItemSlots[i]), type)).ToList();
            if (slots.Sum(i => Inventory.CountIn(p, i)) < o.GiveN) { McHud.Toast($"It wants {o.GiveN} {Crafting.NameOf(o.Give)}."); return; }
            int left = o.GiveN;
            foreach (int i in slots)
            {
                if (left <= 0) break;
                int t = Mathf.Min(left, Inventory.CountIn(p, i));
                if (t > 0 && Inventory.Take(p, i, t) != null) left -= t;
            }
            BlockNet.RequestTrade(trader.NetworkObjectId, (byte)index);
        }

        protected override int QuickMoveIn(string key, int n) => n;

        protected override void RefreshContent()
        {
            if (trader == null) return;
            for (int i = 0; i < rows.Count && i < trader.Offers.Count; i++)
            {
                var o = trader.Offers[i];
                float a = o.Uses > 0 ? 1f : 0.35f; // (sold out: faded)
                Show(rows[i].give, IconOf(o.Give), o.GiveN, a, o.Give);
                Show(rows[i].get, IconOf(o.Get), o.GetN, a, o.Get);
            }
        }

        protected override string HoverContent(int area, int index)
        {
            if (area != AreaOffer || trader == null || index >= trader.Offers.Count) return null;
            var o = trader.Offers[index];
            return o.Uses > 0 ? $"{o.GiveN} {Crafting.NameOf(o.Give)} for {o.GetN} {Crafting.NameOf(o.Get)} - click to trade" : "Sold out";
        }

        protected override void OnUpdate()
        {
            var p = Local;
            if (trader == null || trader.isEnemyDead || (p != null && Vector3.Distance(p.transform.position, trader.transform.position) > 7f)) Close();
        }

        protected override int DevArea(string name) => name == "offer" ? AreaOffer : -1;

        protected override string DevContent() => trader == null ? "-" : string.Join(" ; ", trader.Offers.Select((o, i) => $"{i}:{o.GiveN} {o.Give} -> {o.GetN} {o.Get} uses={o.Uses}"));
    }
}
