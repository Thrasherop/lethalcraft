using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's sheep (#148): out on the moons by day ([Mobs] SheepRarity), in Minecraft's natural colors and odds
    /// (mostly white; grey, light grey, black, brown, and a rare pink). Shears ([E] holding them) take 1-3 wool of its
    /// color; it grows its wool back after a while. Killed: its wool (unless sheared) and 1-2 raw mutton. It follows
    /// wheat, and two fed wheat make a lamb.
    /// </summary>
    public class SheepAI : MinecraftMobAI
    {
        public static EnemyType Type, BabyType;
        protected override string Sound => "sheep";
        protected override bool Passive => true;
        protected override string Tempt => "wheat";
        protected override float RoamSpeed => 1.2f;
        protected override float PanicSpeed => 4.4f;

        /// <summary>Minecraft's sixteen colors (DyeColor order), and the tint its wool takes in each.</summary>
        public static readonly string[] Colors = { "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black" };
        static readonly uint[] Tints = { 0xF9FFFE, 0xF9801D, 0xC74EBD, 0x3AB3DA, 0xFED83D, 0x80C71F, 0xF38BAA, 0x474F52, 0x9D9D97, 0x169C9C, 0x8932B8, 0x3C44AA, 0x835432, 0x5E7C16, 0xB02E26, 0x1D1D21 };
        public static string WoolKey(int color) => "wool_" + Colors[Mathf.Clamp(color, 0, 15)];

        public int Color { get; private set; }
        public bool Sheared { get; private set; }

        /// <summary>(#244) Host: sheared (by a player, or a dispenser): its wool drops; false if it can't be.</summary>
        public bool ServerShearFree()
        {
            if (Sheared || IsBaby || isEnemyDead) return false;
            Sheared = true; Shorn++;
            if (ModItems.ByKey.TryGetValue(WoolKey(Color), out var wool)) ModItems.ServerSpawnStack(wool, Random.Range(1, 4), transform.position + Vector3.up);
            BlockNet.ServerSound(transform.position + Vector3.up, "sheep.shear", 0.8f, 1f);
            BlockNet.ServerMobState(NetworkObjectId, 1, (byte)Color);
            return true;
        }
        public static int Shorn; // (dev/tests)

        /// <summary>(Minecraft's natural odds: white 81.8%, grey, light grey and black 5% each, brown 3%, pink 0.16%)</summary>
        static int NaturalColor(ulong id)
        {
            float r = (float)((id * 2654435761UL) % 100000UL) / 1000f;
            return r < 81.836f ? 0 : r < 86.836f ? 7 : r < 91.836f ? 8 : r < 96.836f ? 15 : r < 99.836f ? 12 : 6;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Color = NaturalColor(NetworkObjectId);
            ShowWool();
        }

        static readonly Dictionary<int, Material> woolMats = new Dictionary<int, Material>();
        Material jebMat;

        /// <summary>(#244) Named jeb_: Minecraft's rainbow, its wool through the sixteen colours a step every 25 ticks
        /// (SheepWoolLayer); its own colour (what it drops) stays. Not jeb_: its own wool back.</summary>
        public void Jeb(bool on)
        {
            var model = transform.Find("Model");
            if (model == null) return;
            if (!on) { if (jebMat != null) { Destroy(jebMat); jebMat = null; ShowWool(); } return; }
            if (jebMat == null)
            {
                ShowWool();
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.gameObject.name.StartsWith("Wool")) { if (jebMat == null) jebMat = new Material(r.sharedMaterial) { name = "LMC_SheepWool_jeb" }; r.sharedMaterial = jebMat; }
                if (jebMat == null) return;
            }
            float ticks = Time.time * 20f;
            int i = (int)(ticks / 25f) + (int)(NetworkObjectId % 16UL);
            float f = (ticks % 25f) / 25f;
            jebMat.SetColor("_BaseColor", Color32.Lerp(Tint(i % 16), Tint((i + 1) % 16), f));
        }

        static Color32 Tint(int c) { uint t = Tints[c]; return new Color32((byte)(t >> 16), (byte)(t >> 8), (byte)t, 255); }

        void ShowWool()
        {
            var model = transform.Find("Model");
            if (model == null) return;
            if (!woolMats.TryGetValue(Color, out var m))
            {
                m = EntityModels.Material("LMC_SheepWool_" + Colors[Color], "entity/sheep/sheep_wool", UnityEngine.Color.white);
                uint t = Tints[Color];
                m.SetColor("_BaseColor", new Color(((t >> 16) & 255) / 255f, ((t >> 8) & 255) / 255f, (t & 255) / 255f));
                woolMats[Color] = m;
            }
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (r.gameObject.name.StartsWith("Wool")) { r.sharedMaterial = m; r.gameObject.SetActive(!Sheared); }
        }

        public override void ReceiveState(byte a, byte b) { Sheared = a != 0; Color = b; ShowWool(); }

        public override string SaveState() => (Sheared ? "1," : "0,") + Color;
        public override void ServerSyncTo(ulong client) => BlockNet.ServerMobState(NetworkObjectId, (byte)(Sheared ? 1 : 0), (byte)Color, client); // (#227)
        /// <summary>(#227) Back from the save: its colour, sheared or not.</summary>
        public override void LoadState(string s)
        {
            var p = (s ?? "").Split(',');
            if (p.Length < 2 || !int.TryParse(p[1], out int c)) return;
            Sheared = p[0] == "1"; Color = Mathf.Clamp(c, 0, 15); ShowWool();
            BlockNet.ServerMobState(NetworkObjectId, (byte)(Sheared ? 1 : 0), (byte)Color);
        }

        public override string UseTip(string heldKey) => heldKey == "shears" && !Sheared && !IsBaby ? "Shear" : Dyes.IndexOf(heldKey) is int d && d >= 0 && d != Color ? "Dye" : base.UseTip(heldKey);
        public static int DyedCount; // (dev/tests)

        /// <summary>(#244) Minecraft's getOffspringColor: the dye the parents' two colours make together in a crafting grid
        /// (red and yellow: orange), or one parent's at random.</summary>
        public static int OffspringColor(int a, int b)
        {
            string ca = Colors[Mathf.Clamp(a, 0, 15)], cb = Colors[Mathf.Clamp(b, 0, 15)];
            foreach (var (x, y, to) in Mixes)
                if ((x == ca && y == cb) || (x == cb && y == ca)) return System.Array.IndexOf(Colors, to);
            return Random.value < 0.5f ? a : b;
        }
        // (Minecraft's two-dye recipes)
        static readonly (string, string, string)[] Mixes =
        {
            ("red", "yellow", "orange"), ("red", "white", "pink"), ("blue", "white", "light_blue"), ("green", "white", "lime"),
            ("black", "white", "gray"), ("gray", "white", "light_gray"), ("blue", "green", "cyan"), ("red", "blue", "purple"), ("purple", "pink", "magenta"),
        };

        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            // (#244) a dye on it: its colour (one dye used), Minecraft's
            if (held is StackItem dy && Dyes.IndexOf(dy.ItemKey) is int d && d >= 0 && d != Color)
            {
                Color = d; DyedCount++;
                dy.ServerUseUp(p);
                BlockNet.ServerSound(transform.position + Vector3.up, "dye.use", 1f, 1f);
                BlockNet.ServerMobState(NetworkObjectId, (byte)(Sheared ? 1 : 0), (byte)Color);
                ShowWool();
                return;
            }
            if (held is ToolItem t && t.ItemKey == "shears")
            {
                if (!ServerShearFree()) return;
                if (!GameModes.IsCreative(p.actualClientId)) t.ServerUse(1); // (#244: shearing wears the shears, Minecraft's)
                return;
            }
            base.ServerUse(p, held);
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            // (it eats grass and its wool grows back: Minecraft's about a minute)
            if (IsServer && Sheared && !isEnemyDead && Random.value < AIIntervalTime / WoolSeconds)
            {
                Sheared = false;
                BlockNet.ServerMobState(NetworkObjectId, 0, (byte)Color);
            }
        }

        /// <summary>About how long sheared wool takes to grow back (Minecraft's about a minute; dev `care wool`).</summary>
        public static float WoolSeconds = 60f;

        /// <summary>Host: Minecraft's drops: its wool (unless sheared) and 1-2 raw mutton.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (!Sheared && ModItems.ByKey.TryGetValue(WoolKey(Color), out var wool)) ModItems.ServerSpawnStack(wool, 1, at);
            if (ModItems.ByKey.TryGetValue("raw_mutton", out var m)) ModItems.ServerSpawnStack(m, Random.Range(1, 3), at);
        }

        static Material sharedMat;

        public static void Register()
        {
            if (Plugin.SheepRarity.Value <= 0) return;
            Type = Register<SheepAI>("Sheep", "Sheep", "Baa. Shear it for wool", Plugin.SheepRarity.Value, 2, 1.3f, 4, 1.2f, BuildModel, daytime: true, length: 1.3f);
            BabyType = RegisterBaby<SheepAI>(Type, "BabySheep", "Lamb", 1, 0.7f, 1.2f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; });
        }

        // ------------------------------------------------------------------ the model: Minecraft's sheep (64x32) and its wool layer
        public static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Sheep", "entity/sheep/sheep", new Color(0.85f, 0.8f, 0.75f));
            var wool = EntityModels.Material("LMC_SheepWool_white", "entity/sheep/sheep_wool", UnityEngine.Color.white);
            // (Minecraft's: y down from 24, the head toward -z; here feet at 0, the head toward +z)
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 15, 0), new Vector3(8, 16, 6), 28, 8, k, sharedMat);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            EntityModels.Part(parent, "Head", new Vector3(0, 19, 10), new Vector3(6, 6, 8), 0, 0, k, sharedMat);
            PigAI.Legs(parent, k, sharedMat, 12f, 3f, 5f, -7f, new Vector3(4, 12, 4), 0, 16);
            // the wool: a size up over the body, the head, the tops of the legs (Minecraft's inflated boxes)
            var wb = EntityModels.Part(parent, "WoolBody", new Vector3(0, 15, 0), new Vector3(8, 16, 6), 28, 8, k, wool);
            wb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wb.transform.localScale = new Vector3(11.5f / 8f, 19.5f / 16f, 9.5f / 6f);
            var wh = EntityModels.Part(parent, "WoolHead", new Vector3(0, 19, 9), new Vector3(6, 6, 6), 0, 0, k, wool);
            wh.transform.localScale = Vector3.one * (7.2f / 6f);
            foreach (var n in new[] { "LegL", "LegR", "LegBL", "LegBR" })
            {
                var hip = parent.Find(n);
                if (hip == null) continue;
                var wl = EntityModels.Part(hip, "WoolLeg", new Vector3(0, -3, 0), new Vector3(4, 6, 4), 0, 16, k, wool);
                wl.transform.localScale = new Vector3(5f / 4f, 7f / 6f, 5f / 4f);
            }
        }
    }

    /// <summary>
    /// Minecraft's rabbit (#149): out on the moons by day ([Mobs] RabbitRarity), hopping. Its coat by moon: white on snowy
    /// ones, gold on desert ones, brown, black or salt-and-pepper elsewhere. Drops rabbit hide (four make leather), raw
    /// rabbit, and now and then a rabbit's foot (a potion of Leaping). It follows carrots; two fed carrots make a kit.
    /// </summary>
    public class RabbitAI : MinecraftMobAI
    {
        public static EnemyType Type, BabyType;
        protected override string Sound => "rabbit";
        protected override bool Passive => true;
        protected override string Tempt => "carrot";
        protected override string[] Foods => new[] { "carrot", "golden_carrot", "dandelion" }; // (#244: Minecraft's rabbit food)

        // (#244) Minecraft's RaidGardenGoal: a rabbit goes for a ripe carrot crop near it and eats it down a stage (the
        // youngest gone); then it isn't ripe, and it looks for another
        BlockKey raiding; bool hasRaid; float raidCheck;
        public static int Raids; // (dev/tests)
        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (!IsServer || isEnemyDead || IsBaby || Panicking || BlockWorld.Instance == null) return;
            var w = BlockWorld.Instance;
            if (hasRaid)
            {
                var b = w.Get(raiding);
                if (b == null || b.Data.Def != Blocks.CarrotCrop || (b.Data.State & 7) != 7) { hasRaid = false; return; }
                var at = w.WorldCenter(raiding);
                // (beside it, or under it by up to a block: Minecraft's hops up onto the farmland; this one reaches)
                var flat = at - transform.position; float up = flat.y; flat.y = 0f;
                if (flat.magnitude > 1.4f * BlockWorld.S || up > 1.6f * BlockWorld.S) { if (agent.speed < RoamSpeed) agent.speed = RoamSpeed; SetDestinationToPosition(at, checkForPath: false); return; } // (its hops slow it to a crawl between bounds: on its way, it hops on)
                var d = b.Data; int age = d.State & 7;
                if (age <= 0) BlockNet.ServerBroadcastOp(Op.Remove(raiding, false));
                else { d.State = (byte)((d.State & ~7) | (age - 1)); BlockNet.ServerBroadcastOp(Op.State(raiding, d)); }
                BlockNet.ServerSound(at, "eat", 0.6f, 1.4f);
                hasRaid = false; Raids++;
                return;
            }
            if ((raidCheck -= AIIntervalTime) > 0f) return;
            raidCheck = 3f;
            float best = 8f * BlockWorld.S;
            foreach (var b in w.Blocks.Values)
            {
                if (b.Data.Def != Blocks.CarrotCrop || (b.Data.State & 7) != 7) continue;
                float dist = Vector3.Distance(w.WorldCenter(b.Key), transform.position);
                if (dist < best) { best = dist; raiding = b.Key; hasRaid = true; }
            }
        }
        protected override float RoamSpeed => 1.6f;
        protected override float PanicSpeed => 5.5f;
        float hop;

        protected virtual string Coat()
        {
            if (ShipMobs.NameOf(this) == "Toast") return "toast"; // (#244: Minecraft's, for Toast)
            ulong h = LookId * 2654435761UL;
            int r = (int)(h % 100UL);
            if (Variants.Snowy) return r < 80 ? "white" : "white_splotched";
            if (Variants.Desert) return "gold";
            return r < 50 ? "brown" : r < 90 ? "salt" : "black";
        }

        static readonly Dictionary<string, Material> coats = new Dictionary<string, Material>();
        /// <summary>(#244) Its coat again (named Toast, or not any more).</summary>
        public void Relook() => ApplyLook();
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyLook();
        }

        protected override void ApplyLook()
        {
            var c = Coat();
            if (!coats.TryGetValue(c, out var m)) coats[c] = m = EntityModels.Material("LMC_Rabbit_" + c, "entity/rabbit/" + c, new Color(0.55f, 0.4f, 0.3f));
            var model = transform.Find("Model");
            if (model != null) foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = m;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            // (it hops: a bound, a pause)
            hop += Time.deltaTime / 0.5f;
            float f = hop % 1f;
            bool air = f < 0.5f;
            if (IsOwner && agent != null && (agent.speed > 0.1f || (currentBehaviourStateIndex == Chase && !Passive))) agent.speed = air ? (Panicking ? PanicSpeed : currentBehaviourStateIndex == Chase && !Passive ? ChaseSpeed : RoamSpeed) * 1.8f : 0.05f;
            var model = transform.Find("Model");
            if (model != null) model.localPosition = Vector3.up * (air ? Mathf.Sin(f / 0.5f * Mathf.PI) * 0.18f : 0f);
        }

        /// <summary>Host: Minecraft's drops: 0-1 rabbit hide, 0-1 raw rabbit, a rabbit's foot one time in ten.</summary>
        protected override void ServerDrops(Vector3 at)
        {
            if (Random.value < 0.5f && ModItems.ByKey.TryGetValue("rabbit_hide", out var hide)) ModItems.ServerSpawnStack(hide, 1, at);
            if (ModItems.ByKey.TryGetValue("raw_rabbit", out var meat)) ModItems.ServerSpawnStack(meat, 1, at); // (#244: always one, Minecraft's)
            if (KilledByPlayer && Random.value < 0.1f && ModItems.ByKey.TryGetValue("rabbit_foot", out var foot)) ModItems.ServerSpawnStack(foot, 1, at);
        }

        static Material sharedMat;

        /// <summary>(#244, owner) Now and then a rabbit is the Killer Bunny: one in 200.</summary>
        public const float KillerChance = 1f / 200f;
        protected override EnemyType ReplaceWith() => KillerBunnyAI.Type != null && Random.value < KillerChance ? KillerBunnyAI.Type : null;

        public static void Register()
        {
            if (Plugin.RabbitRarity.Value <= 0) return;
            Type = Register<RabbitAI>("Rabbit", "Rabbit", "Hops about", Plugin.RabbitRarity.Value, 1, 0.6f, 4, 1.6f, BuildModel, daytime: true, length: 0.5f);
            BabyType = RegisterBaby<RabbitAI>(Type, "BabyRabbit", "Kit", 1, 0.4f, 1.6f, b => { BuildModel(b); b.localScale = Vector3.one * 0.5f; });
            KillerBunnyAI.Type = Register<KillerBunnyAI>("KillerBunny", "The Killer Bunny", "Run away", 0, 1, 0.6f, 2, 1.6f, BuildModel, daytime: true, length: 0.5f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's rabbit
        /// <summary>(#239) Minecraft 26.1 made the rabbit over: a new model on a new 64x64 texture. The player's Minecraft gives the
        /// texture, so its model goes with it: 26.1's (AdultRabbitModel) for a 64x64 one, 1.21's for the old 64x32 one (its
        /// texture had been put on the old model: the coat came out in patches).</summary>
        public static void BuildModel(Transform parent)
        {
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Rabbit", "entity/rabbit/brown", new Color(0.55f, 0.4f, 0.3f));
            var tex = sharedMat.mainTexture;
            if (tex != null && tex.height == 64) BuildModel26(parent); else BuildModel121(parent);
        }

        /// <summary>Minecraft 26.1's AdultRabbitModel, in our axes (Minecraft's y down and z back turned over: a part's offset
        /// (x, -y, -z), the root's from the feet, 24 - y; turns about x the same, about y the other way).</summary>
        static void BuildModel26(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            Transform Pivot(Transform under, string name, Vector3 at, Vector3 euler)
            {
                var t = new GameObject(name).transform; t.SetParent(under, false);
                t.localPosition = at * k; t.localRotation = Quaternion.Euler(euler);
                return t;
            }
            var body = Pivot(parent, "BodyPivot", new Vector3(0f, 1f, -4f), new Vector3(-22.5f, 0f, 0f));
            EntityModels.Part(body, "Body", new Vector3(0f, 3f, 4f), new Vector3(8, 6, 10), 0, 0, k, sharedMat);
            var tail = Pivot(body, "TailPivot", new Vector3(0f, 4.9916f, -0.0125f), Vector3.zero);
            EntityModels.Part(tail, "Tail", new Vector3(0f, 1.0084f, -0.9875f), new Vector3(4, 4, 4), 20, 16, k, sharedMat);
            var head = Pivot(body, "HeadPivot", new Vector3(0f, 5.2929f, 8.1213f), new Vector3(22.5f, 0f, 0f));
            EntityModels.Part(head, "Head", new Vector3(0f, 0.5f, 1.5f), new Vector3(5, 5, 5), 0, 16, k, sharedMat);
            foreach (var (n, x, u) in new[] { ("EarL", 1.5f, 32), ("EarR", -1.5f, 26) })
            {
                var ear = Pivot(head, n, new Vector3(x, 3.7071f, 0.8787f), Vector3.zero);
                EntityModels.Part(ear, "Ear", new Vector3(0f, 1.7929f, -0.3787f), new Vector3(2, 5, 1), u, 0, k, sharedMat);
            }
            var front = Pivot(body, "FrontLegs", new Vector3(0f, 1.5349f, 6.3108f), Vector3.zero);
            var lr = Pivot(front, "LegFR", new Vector3(-2f, -1.9239f, -0.3827f), new Vector3(22.5f, 0f, 0f));
            EntityModels.Part(lr, "Leg", new Vector3(0.1f, -1f, -0.1f), new Vector3(2, 4, 2), 36, 18, k, sharedMat);
            var ll = Pivot(front, "LegFL", new Vector3(2f, -1.9239f, -0.4827f), new Vector3(22.5f, 0f, 0f));
            EntityModels.Part(ll, "Leg", new Vector3(0f, -1f, 0f), new Vector3(2, 4, 2), 44, 18, k, sharedMat);
            var back = Pivot(parent, "BackLegs", new Vector3(0f, 1f, -4f), Vector3.zero);
            foreach (var (n, x, turn, u) in new[] { ("HindR", -3f, -22.5f, 20), ("HindL", 3f, 22.5f, 36) })
            {
                var hip = Pivot(back, n, new Vector3(x, -0.5f, 0f), Vector3.zero);
                var haunch = Pivot(hip, "Haunch", new Vector3(0f, 0.5f, 0f), new Vector3(0f, turn, 0f));
                EntityModels.Part(haunch, "Foot", new Vector3(0f, -0.5f, 2f), new Vector3(2, 1, 6), u, 24, k, sharedMat);
            }
        }

        /// <summary>Minecraft 1.21's rabbit (64x32), shown at 0.6.</summary>
        static void BuildModel121(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.6f;
            var body = new GameObject("BodyPivot").transform; body.SetParent(parent, false);
            body.localPosition = new Vector3(0, 5, -8) * k; body.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            EntityModels.Part(body, "Body", new Vector3(0, -0.5f, 5), new Vector3(6, 5, 10), 0, 0, k, sharedMat);
            var head = new GameObject("HeadPivot").transform; head.SetParent(parent, false);
            head.localPosition = new Vector3(0, 8, 1) * k;
            EntityModels.Part(head, "Head", new Vector3(0, 2, 2.5f), new Vector3(5, 4, 5), 32, 0, k, sharedMat);
            EntityModels.Part(head, "Nose", new Vector3(0, 2, 5), new Vector3(1, 1, 1), 32, 9, k, sharedMat);
            var er = EntityModels.Part(head, "EarR", new Vector3(-1.5f, 6.5f, 0.5f), new Vector3(2, 5, 1), 52, 0, k, sharedMat); er.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
            var el = EntityModels.Part(head, "EarL", new Vector3(1.5f, 6.5f, 0.5f), new Vector3(2, 5, 1), 58, 0, k, sharedMat); el.transform.localRotation = Quaternion.Euler(0f, -15f, 0f);
            var tail = EntityModels.Part(parent, "Tail", new Vector3(0, 4, -8), new Vector3(3, 3, 2), 52, 6, k, sharedMat); tail.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            foreach (var (n, x, uh, uf) in new[] { ("LegBL", 3f, 30, 26), ("LegBR", -3f, 16, 8) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 6.5f, -3.7f) * k;
                var haunch = EntityModels.Part(hip, "Haunch", new Vector3(0, -2, -2.5f), new Vector3(2, 4, 5), uh, 15, k, sharedMat); haunch.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                EntityModels.Part(hip, "Foot", new Vector3(0, -6, 0.2f), new Vector3(2, 1, 7), uf, 24, k, sharedMat);
            }
            foreach (var (n, x, u) in new[] { ("LegL", 3f, 8), ("LegR", -3f, 0) })
            {
                var hip = new GameObject(n).transform; hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 7, 1) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -3.5f, 0), new Vector3(2, 7, 2), u, 15, k, sharedMat);
            }
        }
    }

    /// <summary>
    /// (#244, owner) Minecraft's Killer Bunny (the rabbit of Caerbannog): a white rabbit with red eyes that goes for you and
    /// bites hard (Minecraft's 5 damage on Normal, against a zombie's 3), any time of day and in any light. Its drops are a
    /// rabbit's. Spawns in place of one rabbit in 200.
    /// </summary>
    public class KillerBunnyAI : RabbitAI
    {
        public static new EnemyType Type;
        protected override bool Passive => false;
        protected override bool HatesLight => false;
        public override bool GnawsBlocks => false;
        protected override int Damage => 33;
        protected override float ChaseSpeed => 4.8f;
        protected override string[] Foods => new string[0];
        protected override EnemyType ReplaceWith() => null;
        protected override string Coat() => "caerbannog";
    }
}
