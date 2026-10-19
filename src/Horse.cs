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
                ulong id = LookId * 0x9E3779B97F4A7C15UL;
                float r(int k) => ((id >> (k * 16)) & 0xFFFF) / 65535f;
                return 0.4f + r(0) * 0.2f + r(1) * 0.2f + r(2) * 0.2f;
            }
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
            if (HorseArmor > 0 && ModItems.ByKey.TryGetValue(ArmorKeys[HorseArmor], out var ha)) ModItems.ServerSpawnStack(ha, 1, at);
        }

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

        public override string SaveState() => Tamed || Saddled || HorseArmor > 0 ? $"{(Tamed ? 1 : 0)},{(Saddled ? 1 : 0)},{HorseArmor},{Temper}" : null;

        /// <summary>(#227) Back from the save: tamed, saddled, its armor (and the health that comes with it).</summary>
        public override void LoadState(string s)
        {
            var p = (s ?? "").Split(',');
            if (p.Length < 4) return;
            int.TryParse(p[2], out int armor); int.TryParse(p[3], out Temper);
            Receive(p[0] == "1", p[1] == "1", NoRider, false, (byte)Mathf.Clamp(armor, 0, 4));
            BlockNet.ServerHorse(this, false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ApplyLook();
        }

        protected override void ApplyLook()
        {
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
            if (HorseArmor > 0) foreach (var r in a.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = CoatMat("entity/equipment/horse_body/" + ArmorTex[HorseArmor]);
        }

        public override void Update()
        {
            base.Update();
            foreach (var (leg, over) in layerLegs) over.localRotation = leg.localRotation;
            if (IsServer && buckAt > 0f && Time.time >= buckAt) { buckAt = -1f; ServerBuck(); }
        }

        /// <summary>Every machine: the host's word on this horse.</summary>
        public void Receive(bool tamed, bool saddled, ulong rider, bool bucked, byte armor)
        {
            // (armor: more health, the same on every machine)
            if (armor != HorseArmor) { enemyHP += ArmorHp[Mathf.Min(armor, 4)] - ArmorHp[Mathf.Min(HorseArmor, 4)]; HorseArmor = armor; ShowArmor(); }
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
            string key = held != null ? held.ItemKey : null;
            if (IsBaby) { if (key == "golden_carrot" || key == "apple") { held.ServerUseUp(p); BlockNet.ServerSound(transform.position + Vector3.up, "horse.eat", 0.8f, 1.3f); } return; } // (a foal: no rider)
            int ak = key != null ? System.Array.IndexOf(ArmorKeys, key) : -1;
            if (ak > 0)
            {
                // (#145: horse armor, on a tamed horse; what it wore drops)
                if (!Tamed) { BlockNet.ServerToast(sender, "Tame it first: get on its back until it stops throwing you off."); return; }
                if (HorseArmor > 0 && ModItems.ByKey.TryGetValue(ArmorKeys[HorseArmor], out var old)) ModItems.ServerSpawnStack(old, 1, transform.position + Vector3.up);
                held.ServerUseUp(p);
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.armor", 0.9f, 1f);
                Receive(Tamed, Saddled, Rider, false, (byte)ak); // (the host's own copy first: its health)
                BlockNet.ServerHorse(this, false);
                return;
            }
            if (Tamed && (key == "golden_carrot" || key == "golden_apple"))
            {
                // (#145: Minecraft's breeding food: two tamed horses fed one each make a foal)
                ServerFeedToBreed(p, held);
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
            if (!Tamed && (key == "golden_carrot" || key == "apple"))
            {
                held.ServerUseUp(p);
                Temper = Mathf.Min(100, Temper + (key == "golden_carrot" ? 10 : 3));
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.eat", 0.8f, 1f);
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
            p.cursorTip.text = h.IsBaby ? "" : ak > 0 && h.Tamed ? "Put on armor : [E]" : key == "saddle" && h.Tamed && !h.Saddled ? "Saddle : [E]"
                : (!h.Tamed && (key == "golden_carrot" || key == "apple")) || (h.Tamed && (key == "golden_carrot" || key == "golden_apple")) ? "Feed : [E]" : h.Rider != NoRider ? "" : "Ride : [E]";
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
            float speed = sprint != null && sprint.IsPressed() ? HorseAI.GallopSpeed : HorseAI.RideSpeed;
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
