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
        public int Temper;
        public ulong Rider = NoRider;
        Transform saddle;
        float buckAt = -1f;
        ulong heardRider = NoRider;

        protected override void ServerDrops(Vector3 at)
        {
            int leather = Random.Range(0, 3);
            if (leather > 0 && ModItems.ByKey.TryGetValue("leather", out var l)) ModItems.ServerSpawnStack(l, leather, at);
            if (Saddled && ModItems.ByKey.TryGetValue("saddle", out var s)) ModItems.ServerSpawnStack(s, 1, at);
        }

        public override void Start()
        {
            base.Start();
            saddle = transform.Find("Model/Saddle");
            if (saddle != null) saddle.gameObject.SetActive(Saddled);
        }

        public override void Update()
        {
            base.Update();
            if (IsServer && buckAt > 0f && Time.time >= buckAt) { buckAt = -1f; ServerBuck(); }
        }

        /// <summary>Every machine: the host's word on this horse.</summary>
        public void Receive(bool tamed, bool saddled, ulong rider, bool bucked)
        {
            // (what this machine last heard: on the host Rider is already the new one when its own message arrives)
            ulong was = heardRider; heardRider = rider;
            Tamed = tamed; Saddled = saddled; Rider = rider;
            if (saddle != null) saddle.gameObject.SetActive(saddled);
            var me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NoRider;
            if (rider == me && was != me) Riding.Mount(this);
            else if (was == me && rider != me) Riding.Dismount(bucked ? "The horse threw you off." : null);
            if (bucked) Sounds.Play("horse.angry", transform.position + Vector3.up * 2f, 0.9f, 1f, 24f);
        }

        // ------------------------------------------------------------------ the host
        /// <summary>Host: someone pressed [E] on it (holding a saddle, food, or nothing it wants: then they get on).</summary>
        public void ServerInteract(ulong sender)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || isEnemyDead || Vector3.Distance(p.transform.position, transform.position) > 5f) return;
            var held = p.currentlyHeldObjectServer as StackItem;
            string key = held != null ? held.ItemKey : null;
            if (key == "saddle")
            {
                if (!Tamed) { BlockNet.ServerToast(sender, "Tame it first: get on its back until it stops throwing you off."); return; }
                if (Saddled) { BlockNet.ServerToast(sender, "It has a saddle already."); return; }
                held.ServerSetCount(held.Count - 1, despawnIfEmpty: true);
                Saddled = true;
                BlockNet.ServerSound(transform.position + Vector3.up * 1.5f, "horse.saddle", 0.9f, 1f);
                BlockNet.ServerHorse(this, false);
                return;
            }
            if (!Tamed && (key == "golden_carrot" || key == "apple"))
            {
                held.ServerSetCount(held.Count - 1, despawnIfEmpty: true);
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
                if (Random.Range(0, 100) < Temper) { Tamed = true; BlockNet.ServerToast(sender, "The horse is tamed."); }
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
            // the saddle and bridle: Minecraft draws its texture over the same model, a touch bigger
            var s = new GameObject("Saddle").transform;
            s.SetParent(parent, false);
            Build(s, k, saddleMat, 1.04f, legs: false);
            s.gameObject.SetActive(false);
        }

        // (Minecraft's model is y down from 24 px with the head toward -z; here feet at 0, head toward +z)
        static void Build(Transform parent, float k, Material mat, float grow, bool legs = true)
        {
            Vector3 G(Vector3 v) => v * grow;
            EntityModels.Part(parent, "Body", new Vector3(0, 16, 1), G(new Vector3(10, 10, 22)), 0, 32, k, mat);
            var neck = new GameObject("Neck").transform;
            neck.SetParent(parent, false);
            neck.localPosition = new Vector3(0, 20, 12) * k;
            neck.localRotation = Quaternion.Euler(30f, 0f, 0f);
            EntityModels.Part(neck, "Neck", new Vector3(0, 0, -1.5f), G(new Vector3(4, 12, 7)), 0, 35, k, mat);
            EntityModels.Part(neck, "Head", new Vector3(0, 8.5f, -1.5f), G(new Vector3(6, 5, 7)), 0, 13, k, mat);
            EntityModels.Part(neck, "Mouth", new Vector3(0, 8.5f, 4.5f), G(new Vector3(4, 5, 5)), 0, 25, k, mat);
            if (!legs) return;
            EntityModels.Part(neck, "Mane", new Vector3(0, 3, -6), new Vector3(2, 16, 2), 56, 36, k, mat);
            EntityModels.Part(neck, "EarL", new Vector3(1.55f, 11.5f, -4.5f), new Vector3(2, 3, 1), 19, 16, k, mat);
            EntityModels.Part(neck, "EarR", new Vector3(-1.55f, 11.5f, -4.5f), new Vector3(2, 3, 1), 19, 16, k, mat);
            var tail = new GameObject("Tail").transform;
            tail.SetParent(parent, false);
            tail.localPosition = new Vector3(0, 20, -11) * k;
            tail.localRotation = Quaternion.Euler(30f, 0f, 0f);
            EntityModels.Part(tail, "Tail", new Vector3(0, -7, -2), new Vector3(3, 14, 4), 42, 36, k, mat);
            foreach (var (n, x, z) in new[] { ("LegL", 3f, 10f), ("LegR", -3f, 10f), ("LegBL", 3f, -7f), ("LegBR", -3f, -7f) })
            {
                var h = new GameObject(n).transform;
                h.SetParent(parent, false);
                h.localPosition = new Vector3(x, 10, z) * k;
                EntityModels.Part(h, "Leg", new Vector3(0, -4.5f, -0.5f), new Vector3(4, 11, 4), 48, 21, k, mat);
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
            BlockNet.RequestHorse(h, 0);
            return false;
        }

        /// <summary>The horse the player looks at, within reach.</summary>
        public static HorseAI Aimed(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide))
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
            p.cursorTip.text = key == "saddle" && h.Tamed && !h.Saddled ? "Saddle : [E]" : !h.Tamed && (key == "golden_carrot" || key == "apple") ? "Feed : [E]" : h.Rider != NoRider ? "" : "Ride : [E]";
        }
    }

    /// <summary>The local player on a horse: held on its back; walking steers a saddled one; [Crouch] gets off.</summary>
    [HarmonyPatch]
    public static class Riding
    {
        public static HorseAI Horse;
        static InputAction move, sprint;

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
            if (h.isEnemyDead || p.isPlayerDead || !p.isPlayerControlled) { Horse = null; p.disableMoveInput = false; if (!p.isPlayerDead) BlockNet.RequestHorse(h, 1); return; }
            p.disableMoveInput = true;
            p.fallValue = 0f; p.fallValueUncapped = 0f; p.takingFallDamage = false;
            if (!h.IsOwner || !h.Saddled || h.agent == null || !h.agent.isOnNavMesh) return;
            if (move == null) { try { var a = IngamePlayerSettings.Instance.playerInput.actions; move = a.FindAction("Move"); sprint = a.FindAction("Sprint"); } catch { } }
            var mv = move != null && !p.isTypingChat && !p.inTerminalMenu && !SlotScreen.AnyOpen ? move.ReadValue<Vector2>() : Vector2.zero;
            if (h.agent.hasPath) h.agent.ResetPath();
            if (mv.sqrMagnitude < 0.01f) return;
            var yaw = Quaternion.Euler(0f, p.gameplayCamera.transform.eulerAngles.y, 0f);
            var dir = yaw * new Vector3(mv.x, 0f, mv.y);
            float speed = sprint != null && sprint.IsPressed() ? HorseAI.GallopSpeed : HorseAI.RideSpeed;
            h.agent.Move(Vector3.ClampMagnitude(dir, 1f) * speed * Time.deltaTime);
            if (mv.y > 0.1f || Mathf.Abs(mv.x) > 0.1f) h.transform.rotation = Quaternion.Slerp(h.transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);
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
