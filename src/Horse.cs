using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's horse (#95): out on the moons by day ([Mobs] HorseRarity). [E] on one gets you on its back; an untamed
    /// horse throws you off, and each try (or a golden carrot or an apple given to it) makes the next likelier to tame
    /// it, as Minecraft's temper does. A tamed horse with a saddle on ([E] holding the saddle: three leather and an iron
    /// ingot) goes where you look when you walk, fast, faster sprinting; [Crouch] gets off. Without a saddle it carries
    /// you where it likes. Whoever rides it drives it (the horse's AI moves to their machine); everyone sees the saddle.
    /// </summary>
    [HarmonyPatch]
    public class HorseAI : MinecraftMobAI
    {
        public const string Name = "Horse";
        public static EnemyType Type;
        public const ulong NoRider = ulong.MaxValue;
        public const float RideSpeed = 8.5f, GallopSpeed = 13f;
        protected override string Sound => "horse";
        protected override bool Passive => true;
        protected override float RoamSpeed => 1.6f;
        protected override float PanicSpeed => 6f;
        protected override bool BusyAI() => Rider != NoRider; // (ridden: the rider moves it, or it stands)

        // (the host's, told to everyone: BlockNet.ServerHorse)
        public bool Tamed, Saddled;
        /// <summary>Its horse armor (#145): 0 none, 1 leather, 2 iron, 3 gold, 4 diamond.</summary>
        public byte HorseArmor;
        /// <summary>(#244) Its leather armor's dye (0xRRGGBB), -1: undyed (LeatherDye).</summary>
        public int ArmorColor = -1;
        public static readonly string[] ArmorKeys = { null, "leather_horse_armor", "iron_horse_armor", "golden_horse_armor", "diamond_horse_armor" };
        static readonly string[] ArmorTex = { null, "leather", "iron", "gold", "diamond" };
        static readonly int[] ArmorHp = { 0, 1, 1, 2, 3 }; // (Minecraft's 3, 5, 7, 11 armor points: more health here)
        /// <summary>Minecraft's coats and markings: each horse its own, the same on every machine.</summary>
        static readonly string[] Coats = { "white", "creamy", "chestnut", "brown", "black", "gray", "darkbrown" };
        static readonly string[] Markings = { null, "white", "whitefield", "whitedots", "blackdots" };
        static readonly System.Collections.Generic.Dictionary<string, Material> coatMats = new System.Collections.Generic.Dictionary<string, Material>();
        static Material CoatMat(string tex) { if (!coatMats.TryGetValue(tex, out var m)) coatMats[tex] = m = EntityModels.Material("LMC_" + tex.Replace('/', '_'), tex, new Color(0.45f, 0.28f, 0.16f)); return m; }
        public int Temper;
        public ulong Rider = NoRider;
        /// <summary>(#246) Its jump strength, Minecraft's (AbstractHorse.generateJumpStrength: 0.4 + three 0..0.2 rolls; blocks a
        /// tick, 0.7 on average: about 2.8 blocks high at a full jump): each horse its own, the same on every machine.</summary>
        public float JumpStrength
        {
            get
            {
                if (bornJump >= 0f) return bornJump; // (#244: a foal's, from its parents)
                ulong id = LookId * 0x9E3779B97F4A7C15UL;
                float r(int k) => ((id >> (k * 16)) & 0xFFFF) / 65535f;
                return 0.4f + r(0) * 0.2f + r(1) * 0.2f + r(2) * 0.2f;
            }
        }

        // (#244) Minecraft's horse: its own speed (AbstractHorse.generateSpeed: (0.45 + three 0..0.3 rolls) x 0.25, blocks a
        // tick), jump and health; a foal's from its parents' (createOffspringAttribute). Ridden, it goes at its speed's share
        // of the average (0.225).
        public const float MinHealth = 15f, MaxHealth = 30f, MinJump = 0.4f, MaxJump = 1f, MinSpeed = 0.1125f, MaxSpeed = 0.3375f;
        float bornJump = -1f, bornSpeed = -1f; int bornHealth = -1;
        public float SpeedStat
        {
            get
            {
                if (bornSpeed >= 0f) return bornSpeed;
                ulong id = LookId * 0xC2B2AE3D27D4EB4FUL;
                float r(int k) => ((id >> (k * 16)) & 0xFFFF) / 65535f;
                return (0.45f + r(0) * 0.3f + r(1) * 0.3f + r(2) * 0.3f) * 0.25f;
            }
        }
        public int HealthStat => bornHealth >= 0 ? bornHealth : McHealthOf(LookId);
        public float SpeedFactor => SpeedStat / 0.225f;
        public static int Inherited, Ate, Rears; // (dev/tests)

        /// <summary>Minecraft's AbstractHorse.createOffspringAttribute.</summary>
        public static float Offspring(float a, float b, float min, float max)
        {
            a = Mathf.Clamp(a, min, max); b = Mathf.Clamp(b, min, max);
            float margin = 0.15f * (max - min);
            float range = Mathf.Abs(a - b) + margin * 2f;
            float quality = (Random.value + Random.value + Random.value) / 3f - 0.5f;
            float v = (a + b) / 2f + range * quality;
            if (v > max) return max - (v - max);
            if (v < min) return min + (min - v);
            return v;
        }

        /// <summary>Host: a foal of these two.</summary>
        public void ServerInherit(HorseAI a, HorseAI b)
        {
            bornHealth = Mathf.RoundToInt(Offspring(a.HealthStat, b.HealthStat, MinHealth, MaxHealth));
            bornJump = Offspring(a.JumpStrength, b.JumpStrength, MinJump, MaxJump);
            bornSpeed = Offspring(a.SpeedStat, b.SpeedStat, MinSpeed, MaxSpeed);
            Inherited++;
            ServerSendStats(null);
        }

        void ServerSendStats(ulong? to)
        {
            if (bornJump < 0f) return;
            byte j = (byte)Mathf.RoundToInt(Mathf.InverseLerp(MinJump, MaxJump, bornJump) * 255f);
            byte s = (byte)Mathf.RoundToInt(Mathf.InverseLerp(MinSpeed, MaxSpeed, bornSpeed) * 255f);
            if (to.HasValue) { BlockNet.ServerMobState(NetworkObjectId, 10, j, to.Value); BlockNet.ServerMobState(NetworkObjectId, 11, s, to.Value); BlockNet.ServerMobState(NetworkObjectId, 12, (byte)bornHealth, to.Value); }
            else { BlockNet.ServerMobState(NetworkObjectId, 10, j); BlockNet.ServerMobState(NetworkObjectId, 11, s); BlockNet.ServerMobState(NetworkObjectId, 12, (byte)bornHealth); }
        }
        public override void ServerSyncTo(ulong client) { base.ServerSyncTo(client); ServerSendStats(client); }

        float rearUntil, standAnim;
        public override void ReceiveState(byte a, byte b)
        {
            if (a == 10) bornJump = Mathf.Lerp(MinJump, MaxJump, b / 255f);
            else if (a == 11) bornSpeed = Mathf.Lerp(MinSpeed, MaxSpeed, b / 255f);
            else if (a == 12) { bornHealth = b; if (!IsBaby) LookHealth(3, bornHealth); }
            else if (a == 13) rearUntil = Time.time + 1f; // (Minecraft's setStanding(20))
            else base.ReceiveState(a, b);
        }

        /// <summary>Every machine: Minecraft's rearing (standAnim): up on its hind legs, the body up to 45 degrees.</summary>
        void Rear()
        {
            bool up = Time.time < rearUntil;
            if (!up && standAnim <= 0f) return;
            float dt = Time.deltaTime * 20f;
            standAnim = up ? Mathf.Min(1f, standAnim + ((1f - standAnim) * 0.4f + 0.05f) * dt) : Mathf.Max(0f, standAnim + ((0.8f * standAnim * standAnim * standAnim - standAnim) * 0.6f - 0.05f) * dt);
            var model = transform.Find("Model");
            var hind = model != null ? model.Find("LegBL") : null;
            if (model == null || hind == null) return;
            var rot = Quaternion.Euler(-45f * standAnim, 0f, 0f);
            var pivot = new Vector3(0f, 0f, hind.localPosition.z);
            model.localRotation = rot;
            model.localPosition = pivot - rot * pivot;
        }

        /// <summary>Minecraft's horse_food: what it eats (wheat, sugar, hay, apples, carrots and the golden foods).</summary>
        public static readonly string[] HorseFoods = { "wheat", "sugar", "hay_block", "apple", "carrot", "golden_carrot", "golden_apple", "enchanted_golden_apple" };
        protected override string[] TemptItems => new[] { "golden_carrot", "golden_apple", "enchanted_golden_apple" }; // (Minecraft's horse_tempt_items)
        float healAcc;

        /// <summary>Host: Minecraft's AbstractHorse.handleEating: it heals, a foal grows, its temper rises; the golden foods put a
        /// tamed grown one in love. Eaten only if one of those happened.</summary>
        bool ServerEat(PlayerControllerB p, StackItem held, string key)
        {
            float heal = 0f; int age = 0, temper = 0; bool love = false;
            switch (key)
            {
                case "wheat": heal = 2f; age = 20; temper = 3; break;
                case "sugar": heal = 1f; age = 30; temper = 3; break;
                case "hay_block": heal = 20f; age = 180; break;
                case "apple": case "carrot": heal = 3f; age = 60; temper = 3; break;
                case "golden_carrot": heal = 4f; age = 60; temper = 5; love = true; break;
                case "golden_apple": case "enchanted_golden_apple": heal = 10f; age = 240; temper = 10; love = true; break;
                default: return false;
            }
            bool used = false, eaten = false;
            if (love && Tamed && !IsBaby && !InLove)
            {
                int born = AnimalCare.Born;
                ServerFeedToBreed(p, held); // (eats it, if it may be in love again)
                eaten = InLove || AnimalCare.Born != born;
                used |= eaten;
            }
            int full = LookHp > 0 ? LookHp : 3;
            if (heal > 0f && enemyHP < full)
            {
                healAcc += heal / 5f; int whole = Mathf.FloorToInt(healAcc);
                if (whole > 0) { enemyHP = Mathf.Min(full, enemyHP + whole); healAcc -= whole; }
                used = true;
            }
            if (IsBaby && age > 0) { AgeUp(age); used = true; }
            if (temper > 0 && (used || !Tamed) && Temper < 100) { Temper = Mathf.Min(100, Temper + temper); used = true; }
            if (used && !eaten) { held.ServerUseUp(p); BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.eat", 0.8f, IsBaby ? 1.3f : 1f); }
            if (used) Ate++;
            return used;
        }
        /// <summary>(#246) Ridden, its rider's machine moves it as Minecraft moves a horse (Riding): off its navmesh, which
        /// can't take it up a step it can't walk, nor into the air.</summary>
        public override bool OffNavMesh => Riding.Horse == this && IsOwner;
        Transform saddle;
        float buckAt = -1f;
        ulong heardRider = NoRider;

        protected override void ServerDrops(Vector3 at)
        {
            int leather = Random.Range(0, 3);
            if (leather > 0 && ModItems.ByKey.TryGetValue("leather", out var l)) ModItems.ServerSpawnStack(l, leather, at);
            if (Saddled && ModItems.ByKey.TryGetValue("saddle", out var s)) ModItems.ServerSpawnStack(s, 1, at);
            if (HorseArmor > 0) ModItems.ServerSpawnPlainKeyedOrStack(ArmorItemKey, at);
        }

        /// <summary>(#244) The armor it wears as an item's key (leather with its colour).</summary>
        string ArmorItemKey => HorseArmor == 1 && ArmorColor >= 0 ? LeatherDye.With(LeatherDye.HorseKey, ArmorColor) ?? LeatherDye.HorseKey : ArmorKeys[HorseArmor];

        public override void Start()
        {
            base.Start();
            saddle = transform.Find("Model/Saddle");
            if (saddle != null) saddle.gameObject.SetActive(Saddled);
            // (the markings' and the armor's own legs: turned with the horse's, which the walk swings)
            var model = transform.Find("Model");
            if (model != null)
                foreach (var layer in new[] { "Markings", "HorseArmor" })
                    foreach (var n in new[] { "LegL", "LegR", "LegBL", "LegBR" })
                    {
                        Transform leg = model.Find(n), over = model.Find(layer + "/" + n);
                        if (leg != null && over != null) layerLegs.Add((leg, over));
                    }
        }

        readonly System.Collections.Generic.List<(Transform leg, Transform over)> layerLegs = new System.Collections.Generic.List<(Transform, Transform)>();

        public override string SaveState() => Tamed || Saddled || HorseArmor > 0 || bornJump >= 0f ? $"{(Tamed ? 1 : 0)},{(Saddled ? 1 : 0)},{HorseArmor},{Temper}" + (HorseArmor == 1 && ArmorColor >= 0 ? "," + ArmorColor : "")
            + (bornJump >= 0f ? System.FormattableString.Invariant($"|g:{bornJump:F4}:{bornSpeed:F4}:{bornHealth}") : "") : null;

        /// <summary>(#227) Back from the save: tamed, saddled, its armor (and the health that comes with it).</summary>
        public override void LoadState(string s)
        {
            // (#244: what it was born with, a foal's: "|g:jump:speed:health")
            int gi = (s ?? "").IndexOf("|g:");
            if (gi >= 0)
            {
                var g = s.Substring(gi + 3).Split(':');
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (g.Length >= 3 && float.TryParse(g[0], System.Globalization.NumberStyles.Float, inv, out var gj) && float.TryParse(g[1], System.Globalization.NumberStyles.Float, inv, out var gs) && int.TryParse(g[2], out var gh))
                { bornJump = gj; bornSpeed = gs; bornHealth = gh; if (!IsBaby) LookHealth(3, bornHealth); ServerSendStats(null); }
                s = s.Substring(0, gi);
            }
            var p = (s ?? "").Split(',');
            if (p.Length < 4) return;
            int.TryParse(p[2], out int armor); int.TryParse(p[3], out Temper);
            int color = -1; if (p.Length > 4) int.TryParse(p[4], out color); // (#244: its leather's dye)
            Receive(p[0] == "1", p[1] == "1", NoRider, false, (byte)Mathf.Clamp(armor, 0, 4), color);
            BlockNet.ServerHorse(this, false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyLook();
        }

        protected override void ApplyLook()
        {
            if (!IsBaby) LookHealth(3, HealthStat); // (#244: its own health, 3-6)
            ulong id = LookId * 2654435761UL;
            var coat = CoatMat("entity/horse/horse_" + Coats[(int)(id % 7UL)]);
            var mark = Markings[(int)((id / 7UL) % 5UL)];
            var model = transform.Find("Model");
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = r.transform; bool other = false;
                while (t != null && t != model) { if (t.name == "Saddle" || t.name == "Markings" || t.name == "HorseArmor") { other = true; break; } t = t.parent; }
                if (!other) r.sharedMaterial = coat;
            }
            var mk = model.Find("Markings");
            if (mk != null)
            {
                mk.gameObject.SetActive(mark != null);
                if (mark != null) foreach (var r in mk.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = CoatMat("entity/horse/horse_markings_" + mark);
            }
            ShowArmor();
        }

        void ShowArmor()
        {
            var a = transform.Find("Model/HorseArmor");
            if (a == null) return;
            a.gameObject.SetActive(HorseArmor > 0);
            if (HorseArmor > 0) foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = HorseArmor == 1 ? LeatherMat(ArmorColor < 0 ? LeatherDye.Default : ArmorColor) : CoatMat("entity/equipment/horse_body/" + ArmorTex[HorseArmor]);
        }

        /// <summary>(#244) Leather horse armor in a colour (undyed: Minecraft's brown), its overlay over it.</summary>
        static Material LeatherMat(int rgb)
        {
            string id = "horse_leather_" + rgb.ToString("X6");
            if (!coatMats.TryGetValue(id, out var m))
                coatMats[id] = m = EntityModels.Material("LMC_" + id, LeatherDye.Texture("entity/equipment/horse_body/leather", "entity/equipment/horse_body/leather_overlay", rgb), new Color(0.63f, 0.4f, 0.25f));
            return m;
        }

        public override void Update()
        {
            base.Update();
            foreach (var (leg, over) in layerLegs) over.localRotation = leg.localRotation;
            if (IsServer && buckAt > 0f && Time.time >= buckAt) { buckAt = -1f; ServerBuck(); }
            Rear();
        }

        /// <summary>Every machine: the host's word on this horse.</summary>
        public void Receive(bool tamed, bool saddled, ulong rider, bool bucked, byte armor, int armorColor = -1)
        {
            // (armor: more health, the same on every machine)
            if (armor != HorseArmor || armorColor != ArmorColor) { enemyHP += ArmorHp[Mathf.Min(armor, 4)] - ArmorHp[Mathf.Min(HorseArmor, 4)]; HorseArmor = armor; ArmorColor = armorColor; ShowArmor(); }
            // (what this machine last heard: on the host Rider is already the new one when its own message arrives)
            ulong was = heardRider; heardRider = rider;
            Tamed = tamed; Saddled = saddled; Rider = rider;
            if (saddle != null) saddle.gameObject.SetActive(saddled);
            var me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NoRider;
            if (rider == me && was != me) Riding.Mount(this);
            else if (was == me && rider != me) Riding.Dismount(bucked ? "The horse threw you off." : null);
            // (the others see the rider sit, with the couch's pose: not on the rider's own machine, where it'd move their arms)
            if (rider != was)
            {
                if (was != NoRider && was != me) Pose(was, "SA_stopAnimation");
                if (rider != NoRider && rider != me) Pose(rider, "SA_SitOnCouch");
            }
            if (bucked) Sounds.Play("horse.angry", transform.position + Vector3.up * 2f, 0.9f, 1f, 24f);
        }

        static void Pose(ulong client, string trigger)
        {
            var pl = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.actualClientId == client);
            if (pl != null && pl.playerBodyAnimator != null) pl.playerBodyAnimator.SetTrigger(trigger);
        }

        // ------------------------------------------------------------------ the host
        /// <summary>Host: someone pressed [E] on it (holding a saddle, food, or nothing it wants: then they get on).</summary>
        public void ServerInteract(ulong sender)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || isEnemyDead || Vector3.Distance(p.transform.position, transform.position) > 5f) return;
            var held = p.currentlyHeldObjectServer as StackItem;
            var heldLeather = p.currentlyHeldObjectServer as LeatherHorseArmorItem; // (#244: an item with its colour, not a stack)
            string key = held != null ? held.ItemKey : heldLeather != null ? LeatherDye.HorseKey : null;
            if (IsBaby) { if (held != null) ServerEat(p, held, key); return; } // (a foal: no rider; #244: food heals it and it grows)
            int ak = key != null ? System.Array.IndexOf(ArmorKeys, key) : -1;
            if (ak > 0)
            {
                // (#145: horse armor, on a tamed horse; what it wore drops)
                if (!Tamed) { BlockNet.ServerToast(sender, "Tame it first: get on its back until it stops throwing you off."); return; }
                if (HorseArmor > 0) ModItems.ServerSpawnPlainKeyedOrStack(ArmorItemKey, transform.position + Vector3.up);
                int color = heldLeather != null ? LeatherDye.ColorOf(Crafting.KeyOf(heldLeather)) : -1;
                if (held != null) held.ServerUseUp(p);
                else if (!GameModes.IsCreative(p)) BlockNet.ServerTakeHeld(heldLeather);
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.armor", 0.9f, 1f);
                Receive(Tamed, Saddled, Rider, false, (byte)ak, color); // (the host's own copy first: its health)
                BlockNet.ServerHorse(this, false);
                return;
            }
            if (held != null && System.Array.IndexOf(HorseFoods, key) >= 0)
            {
                // (#145: Minecraft's breeding food: two tamed horses fed one each make a foal; #244: and its other foods)
                ServerEat(p, held, key);
                return;
            }
            if (key == "saddle")
            {
                if (!Tamed) { BlockNet.ServerToast(sender, "Tame it first: get on its back until it stops throwing you off."); return; }
                if (Saddled) { BlockNet.ServerToast(sender, "It has a saddle already."); return; }
                held.ServerUseUp(p);
                Saddled = true;
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.saddle", 0.9f, 1f);
                BlockNet.ServerHorse(this, false);
                return;
            }
            if (Rider != NoRider) return;
            Rider = sender;
            ChangeOwnershipOfEnemy(sender);
            if (!Tamed)
            {
                // (Minecraft's: a random 0-99 under its temper tames it; each failed try adds 5)
                if (Random.Range(0, 100) < Temper) { Tamed = true; BlockNet.ServerToast(sender, "The horse is tamed."); Advancements.ServerGrant(sender, "bff"); }
                else { Temper = Mathf.Min(100, Temper + 5); buckAt = Time.time + Random.Range(1.2f, 2.5f); }
            }
            BlockNet.ServerHorse(this, false);
        }

        public void ServerDismount(ulong sender)
        {
            if (Rider != sender) return;
            Rider = NoRider;
            BlockNet.ServerHorse(this, false);
        }

        void ServerBuck()
        {
            if (Rider == NoRider) return;
            // (#244) Minecraft's makeMad: it rears, neighing
            BlockNet.ServerMobState(NetworkObjectId, 13, 1); Rears++;
            BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.angry", 1f, 1f);
            Rider = NoRider;
            BlockNet.ServerHorse(this, true);
        }

        /// <summary>Host: a late joiner hears about every horse.</summary>
        public static void ServerSyncAll(ulong client)
        {
            if (RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is HorseAI h && !h.isEnemyDead) BlockNet.ServerHorse(h, false, client);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (playerWhoHit != null && Rider == playerWhoHit.actualClientId) return; // (your own mount: a swing at what's ahead)
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        static Material sharedMat, saddleMat;

        public static void Register()
        {
            if (Plugin.HorseRarity.Value <= 0) return;
            Type = Register<HorseAI>("Horse", Name, "Tame it; ride it with a saddle", Plugin.HorseRarity.Value, 3, 2.2f, 3, 1.6f, BuildModel, daytime: true, length: 2.2f);
            RegisterBaby<HorseAI>(Type, "BabyHorse", "Foal", 2, 1.3f, 1.6f, b => { BuildModel(b); b.localScale = Vector3.one * 0.55f; });
        }

        /// <summary>Where the rider's feet go (its back, legs either side), local to the horse.</summary>
        public static Vector3 Seat => new Vector3(0f, 21f * EntityModels.Px * BlockWorld.S - 1.1f, 0.1f);

        // ------------------------------------------------------------------ the model: Minecraft's horse (64x64)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Horse", "entity/horse/horse_brown", new Color(0.45f, 0.28f, 0.16f));
            if (saddleMat == null) saddleMat = EntityModels.Material("LMC_HorseSaddle", "entity/equipment/horse_saddle/saddle", new Color(0.35f, 0.2f, 0.1f));
            Build(parent, k, sharedMat, 1f);
            // its markings (Minecraft's second layer) and its horse armor, over it: shown as each horse is
            var mk = new GameObject("Markings").transform; mk.SetParent(parent, false);
            Build(mk, k, sharedMat, 1.005f);
            var ar = new GameObject("HorseArmor").transform; ar.SetParent(parent, false);
            Build(ar, k, sharedMat, 1.07f);
            ar.gameObject.SetActive(false);
            // the saddle and bridle: Minecraft draws its texture over the same model, a touch bigger
            var s = new GameObject("Saddle").transform;
            s.SetParent(parent, false);
            Build(s, k, saddleMat, 1.04f, legs: false);
            s.gameObject.SetActive(false);
        }

        // (Minecraft's model is y down from 24 px with the head toward -z; here feet at 0, head toward +z)
        static void Build(Transform parent, float k, Material mat, float grow, bool legs = true)
        {
            // (a layer over the horse, a touch bigger: each box its own size, scaled up round its middle, so its texture
            // stays where Minecraft has it; the legs too, so they don't flicker into the horse's own)
            GameObject P(Transform par, string n, Vector3 c, Vector3 size, int u, int v) { var g = EntityModels.Part(par, n, c, size, u, v, k, mat); g.transform.localScale = Vector3.one * grow; return g; }
            P(parent, "Body", new Vector3(0, 16, 1), new Vector3(10, 10, 22), 0, 32);
            var neck = new GameObject("Neck").transform;
            neck.SetParent(parent, false);
            neck.localPosition = new Vector3(0, 20, 12) * k;
            neck.localRotation = Quaternion.Euler(30f, 0f, 0f);
            P(neck, "Neck", new Vector3(0, 0, -1.5f), new Vector3(4, 12, 7), 0, 35);
            P(neck, "Head", new Vector3(0, 8.5f, -1.5f), new Vector3(6, 5, 7), 0, 13);
            P(neck, "Mouth", new Vector3(0, 8.5f, 4.5f), new Vector3(4, 5, 5), 0, 25);
            if (!legs) return;
            P(neck, "Mane", new Vector3(0, 3, -6), new Vector3(2, 16, 2), 56, 36);
            P(neck, "EarL", new Vector3(1.55f, 11.5f, -4.5f), new Vector3(2, 3, 1), 19, 16);
            P(neck, "EarR", new Vector3(-1.55f, 11.5f, -4.5f), new Vector3(2, 3, 1), 19, 16);
            var tail = new GameObject("Tail").transform;
            tail.SetParent(parent, false);
            tail.localPosition = new Vector3(0, 20, -11) * k;
            tail.localRotation = Quaternion.Euler(30f, 0f, 0f);
            P(tail, "Tail", new Vector3(0, -7, -2), new Vector3(3, 14, 4), 42, 36);
            foreach (var (n, x, z) in new[] { ("LegL", 3f, 10f), ("LegR", -3f, 10f), ("LegBL", 3f, -7f), ("LegBR", -3f, -7f) })
            {
                var h = new GameObject(n).transform;
                h.SetParent(parent, false);
                h.localPosition = new Vector3(x, 10, z) * k;
                P(h, "Leg", new Vector3(0, -4.5f, -0.5f), new Vector3(4, 11, 4), 48, 21);
            }
        }

        // ------------------------------------------------------------------ getting on: [E] while looking at it
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix]
        static bool OnInteract(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !Builder.CanAct(p) || Riding.Horse != null) return true;
            var h = Aimed(p);
            if (h == null) return true;
            // (a lead on it, or off it: AnimalCare's)
            if ((p.currentlyHeldObjectServer as StackItem)?.ItemKey == "lead" || h.LeashedTo != NoLeash) return true;
            BlockNet.RequestHorse(h, 0);
            return false;
        }

        /// <summary>The horse the player looks at, within reach.</summary>
        public static HorseAI Aimed(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide).OrderBy(h => h.distance)) // (nearest first)
            {
                var d = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (d != null && d.mainScript is HorseAI h && !h.isEnemyDead) return h;
            }
            return null;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Tip(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || p.cursorTip == null || Riding.Horse != null || !Builder.CanAct(p)) return;
            var h = Aimed(p);
            if (h == null) return;
            string key = (p.currentlyHeldObjectServer as StackItem)?.ItemKey;
            if (key == "lead" || h.LeashedTo != NoLeash) return;
            int ak = key != null ? System.Array.IndexOf(ArmorKeys, key) : -1;
            bool food = key != null && System.Array.IndexOf(HorseFoods, key) >= 0;
            p.cursorTip.text = h.IsBaby ? (food ? "Feed : [E]" : "") : ak > 0 && h.Tamed ? "Put on armor : [E]" : key == "saddle" && h.Tamed && !h.Saddled ? "Saddle : [E]"
                : food ? "Feed : [E]" : h.Rider != NoRider ? "" : "Ride : [E]";
        }
    }

    /// <summary>The local player on a horse: held on its back; walking steers a saddled one; [Crouch] gets off.</summary>
    [HarmonyPatch]
    public static class Riding
    {
        public static HorseAI Horse;
        static InputAction move, sprint, jump;
        // (#246) the ridden horse's body: a character controller that steps up a block (Minecraft's STEP_HEIGHT 1), falls,
        // and jumps; speeds in blocks a tick, Minecraft's
        static CharacterController body;
        static Vector3 vel;
        static float charge = -1f, cooldownUntil, ticks;
        public static int Jumps; public static float LastJumpScale, LastJumpTop; // (dev/tests)
        static float jumpFrom;
        public static bool Airborne => body != null && body.enabled && !body.isGrounded;
        public static string Debug => body == null ? "body=none" : $"body={body.enabled} grounded={body.isGrounded} vel={vel.x:F3},{vel.y:F3},{vel.z:F3} flags={body.collisionFlags} agent={(Horse != null && Horse.agent != null && Horse.agent.enabled)}";

        public static void Mount(HorseAI h)
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null) return;
            Horse = h;
            if (h.IsOwner && h.agent != null && h.agent.isOnNavMesh) h.agent.ResetPath();
            Sounds.Play("horse.idle", h.transform.position + Vector3.up * 2f, 0.7f, 1f, 20f);
        }

        public static void Dismount(string why)
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            var h = Horse;
            Unride(h);
            Horse = null;
            if (p == null) return;
            p.disableMoveInput = false;
            if (h != null)
            {
                // (beside it, on the ground)
                var at = h.transform.position + h.transform.right * 1.6f;
                if (NavMesh.SamplePosition(at, out var nh, 3f, NavMesh.AllAreas)) at = nh.position;
                p.TeleportPlayer(at + Vector3.up * 0.1f);
            }
            if (why != null) McHud.Toast(why);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool CrouchOff(PlayerControllerB __instance)
        {
            if (Horse == null || __instance != GameNetworkManager.Instance?.localPlayerController) return true;
            BlockNet.RequestHorse(Horse, 1);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed"), HarmonyPrefix]
        static bool NoJump(PlayerControllerB __instance) => Horse == null || __instance != GameNetworkManager.Instance?.localPlayerController;

        /// <summary>Its rider's machine: walking steers it (where the camera looks), sprinting gallops.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Steer(PlayerControllerB __instance)
        {
            var p = __instance;
            var h = Horse;
            if (h == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            if (h.isEnemyDead || p.isPlayerDead || !p.isPlayerControlled) { Unride(h); Horse = null; p.disableMoveInput = false; if (!p.isPlayerDead) BlockNet.RequestHorse(h, 1); return; }
            p.disableMoveInput = true;
            p.fallValue = 0f; p.fallValueUncapped = 0f; p.takingFallDamage = false;
            if (!h.IsOwner || !h.Saddled || h.agent == null) { Unride(h); return; }
            if (move == null) { try { var a = IngamePlayerSettings.Instance.playerInput.actions; move = a.FindAction("Move"); sprint = a.FindAction("Sprint"); jump = a.FindAction("Jump"); } catch { } }
            bool free = !p.isTypingChat && !p.inTerminalMenu && !SlotScreen.AnyOpen;
            var mv = move != null && free ? move.ReadValue<Vector2>() : Vector2.zero;
            var yaw = Quaternion.Euler(0f, p.gameplayCamera.transform.eulerAngles.y, 0f);
            var dir = Vector3.ClampMagnitude(yaw * new Vector3(mv.x, 0f, mv.y), 1f);
            float speed = (sprint != null && sprint.IsPressed() ? HorseAI.GallopSpeed : HorseAI.RideSpeed) * h.SpeedFactor; // (#244: its own speed)
            Ride(h, p);
            float S = BlockWorld.S;
            bool grounded = body.isGrounded;
            // [Jump]: held, it charges (Minecraft's jump bar: full at half a second, then weaker); let go, it jumps
            bool held = jump != null && free && jump.IsPressed();
            if (held && grounded && Time.time >= cooldownUntil) { if (charge < 0f) charge = 0f; charge += Time.deltaTime * 20f; }
            else if (charge >= 0f)
            {
                int t = Mathf.FloorToInt(charge); charge = -1f;
                float bar = t < 10 ? t * 0.1f : 0.8f + 2f / (t - 9) * 0.1f;
                int amount = Mathf.FloorToInt(bar * 100f);
                float scale = amount >= 90 ? 1f : 0.4f + 0.4f * amount / 90f; // (PlayerRideableJumping.getPlayerJumpPendingScale)
                if (grounded)
                {
                    // (Minecraft's executeRidersJump: up at its jump strength times the bar, a little more forward going forward)
                    vel.y = h.JumpStrength * scale;
                    if (mv.y > 0.1f) { vel.x += h.transform.forward.x * 0.4f * scale; vel.z += h.transform.forward.z * 0.4f * scale; }
                    cooldownUntil = Time.time + 0.5f; // (10 ticks before the bar charges again)
                    Jumps++; LastJumpScale = scale; jumpFrom = h.transform.position.y; LastJumpTop = 0f;
                    Sounds.Play("horse.jump", h.transform.position + Vector3.up, 0.4f, 1f, 16f);
                    grounded = false;
                }
            }
            // Minecraft's physics a tick at a time: gravity and drag; on the ground it goes where it's steered, in the air it
            // keeps going, steered a little
            var want = dir * speed / S / 20f;
            ticks += Time.deltaTime * 20f;
            int n = 0;
            while (ticks >= 1f && n++ < 4)
            {
                ticks -= 1f;
                vel.y = grounded && vel.y <= 0f ? -0.08f : (vel.y - 0.08f) * 0.98f;
                if (grounded && vel.y <= 0f) { vel.x = want.x; vel.z = want.z; }
                else { vel.x = vel.x * 0.91f + want.x * 0.09f; vel.z = vel.z * 0.91f + want.z * 0.09f; }
            }
            if (ticks >= 1f) ticks = 0f;
            bool wasAir = !grounded;
            body.Move(vel * S * 20f * Time.deltaTime);
            if (!body.isGrounded) LastJumpTop = Mathf.Max(LastJumpTop, h.transform.position.y - jumpFrom);
            else if (wasAir && vel.y < -0.2f) Sounds.Play("horse.land", h.transform.position, 0.4f, 1f, 16f);
            if ((body.collisionFlags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;
            if (mv.y > 0.1f || Mathf.Abs(mv.x) > 0.1f) h.transform.rotation = Quaternion.Slerp(h.transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
        }

        /// <summary>(#246) The ridden horse's body (its rider's machine, which owns it): off its navmesh, a character
        /// controller of its size that steps up a block.</summary>
        static void Ride(HorseAI h, PlayerControllerB p)
        {
            if (body != null && body.gameObject == h.gameObject && body.enabled) return;
            float S = BlockWorld.S;
            if (h.agent.enabled) { if (h.agent.isOnNavMesh) h.agent.ResetPath(); h.agent.enabled = false; }
            h.moveTowardsDestination = false;
            body = h.GetComponent<CharacterController>() ?? h.gameObject.AddComponent<CharacterController>();
            body.radius = 0.5f; body.height = 2.0f; body.center = new Vector3(0f, 1.0f, 0f);
            body.stepOffset = 1.0f * S + 0.05f; body.slopeLimit = 50f; body.skinWidth = 0.05f; body.minMoveDistance = 0f;
            body.enabled = true;
            foreach (var c in h.GetComponentsInChildren<Collider>(true)) if (c != body) Physics.IgnoreCollision(body, c);
            // (nor its rider, who sits inside it: every collider of every player, as players don't stop a horse)
            foreach (var pl in StartOfRound.Instance.allPlayerScripts)
                if (pl != null) foreach (var c in pl.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(body, c);
            vel = Vector3.zero; ticks = 0f; charge = -1f;
        }

        /// <summary>Off it: the controller gone, back on the navmesh where it stands (or the nearest).</summary>
        static void Unride(HorseAI h)
        {
            if (body == null) return;
            var b = body; body = null;
            if (b != null) b.enabled = false;
            if (h == null || h.agent == null || !h.IsOwner) return;
            if (NavMesh.SamplePosition(h.transform.position, out var nh, 3f * BlockWorld.S, h.agent.areaMask)) h.transform.position = nh.position;
            h.agent.enabled = true;
            if (h.agent.isOnNavMesh) h.agent.Warp(h.transform.position);
        }

        /// <summary>On its back, every frame (after the game moved the player).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Sit(PlayerControllerB __instance)
        {
            var p = __instance;
            var h = Horse;
            if (h == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            p.transform.position = h.transform.TransformPoint(HorseAI.Seat);
            p.fallValue = 0f; p.fallValueUncapped = 0f;
        }
    }
}
