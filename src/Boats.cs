using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Boats and chest boats (#223, the owner's: every wood), Minecraft's AbstractBoat: put on water (or land) with the use
    /// button, [E] to get in (two can sit in a boat, one in a chest boat; crouch [E] on a chest boat opens its chest),
    /// Crouch to get out. The one in front steers: W forward, S back, A and D turn (Minecraft's controlBoat, every tick: a
    /// degree of turn, 0.04 of push, 0.9 of it kept on water); it floats on water (ours, and the moons' own) and drags on
    /// land. The host moves it; the driver's keys go to the host. Hit twice, it breaks into its item (and a chest boat's
    /// load spills). Model: Minecraft's BoatModel from entity/boat/&lt;wood&gt;.png, its paddles rowing.
    /// </summary>
    public class BoatAI : MinecraftMobAI
    {
        public static readonly Dictionary<EnemyType, (Blocks.WoodKind wood, bool chest)> Kinds = new Dictionary<EnemyType, (Blocks.WoodKind, bool)>();
        public static EnemyType TypeFor(string itemKey) => Kinds.FirstOrDefault(kv => ItemKey(kv.Value.wood, kv.Value.chest) == itemKey).Key;
        public static string ItemKey(Blocks.WoodKind w, bool chest) => BoatKeys.ItemKey(w, chest);
        public (Blocks.WoodKind wood, bool chest) Kind => Kinds.TryGetValue(enemyType, out var k) ? k : (Blocks.Oak, false);
        public BlockKey StorageKey => new BlockKey(Chests.MobFrame, 0, new Vector3Int((int)(NetworkObjectId & 0xFFFFFF), 27, 1)); // (y: its 27 slots; z 1: a boat's, for its screen's title)
        public override BlockKey? Storage => Kind.chest ? StorageKey : (BlockKey?)null; // (#227)

        protected override string Sound => "boat";
        protected override bool Passive => true;
        protected override bool Swims => true; // (it moves itself: no navmesh)
        protected override int Xp => 0;
        public override bool GnawsBlocks => false;

        public const ulong Nobody = ulong.MaxValue;
        public readonly ulong[] Seats = { Nobody, Nobody };
        public int SeatCount => Kind.chest ? 1 : 2;
        public static int Placed, Rowed; // (dev/tests)

        // the host's physics, in Minecraft's units (blocks a tick, degrees a tick)
        Vector3 vel; float deltaRot, tickAcc, waterLevel, landFriction = 0.6f;
        enum Status { InWater, UnderWater, OnLand, InAir }
        Status status;
        bool inUp, inDown, inLeft, inRight; float inputAt = -10f;
        int hits;
        // paddles (every machine: from how it moves)
        Transform paddleL, paddleR; float rowL, rowR;

        public override void Start()
        {
            SetEnemyOutside(transform.position.y > -100f);
            base.Start();
            paddleL = transform.Find("Model/Mc/left_paddle");
            paddleR = transform.Find("Model/Mc/right_paddle");
        }

        protected override void SwimAI() { }

        // ------------------------------------------------------------------ the host: Minecraft's boat, 20 ticks a second
        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            Animate();
            if (!IsServer) return;
            if (Time.time >= seatCheck) { seatCheck = Time.time + 0.5f; ServerFreeSeats(); }
            tickAcc += Time.deltaTime;
            int n = 0;
            while (tickAcc >= 0.05f && n++ < 4) { tickAcc -= 0.05f; Tick(); }
            SyncPositionToClients();
        }

        void Tick()
        {
            float S = BlockWorld.S;
            status = GetStatus(out float surface);
            if (status == Status.InWater) waterLevel = surface;
            // floatBoat
            float vspeed = -0.04f, buoyancy = 0f, inv = 0.05f;
            switch (status)
            {
                case Status.InWater: buoyancy = (waterLevel - transform.position.y) / S / 0.5625f; inv = 0.9f; break;
                case Status.UnderWater: buoyancy = 0.01f; inv = 0.45f; break;
                case Status.InAir: inv = 0.9f; break;
                case Status.OnLand: inv = landFriction; break; // (Minecraft halves it for a rider after using it, then reads the ground again: no effect)
            }
            vel = new Vector3(vel.x * inv, vel.y + vspeed, vel.z * inv);
            if (status == Status.OnLand && vel.y < 0f) vel.y = 0f; // (resting on the ground)
            deltaRot *= inv;
            if (buoyancy > 0f) vel.y = (vel.y + buoyancy * (0.04f / 0.65f)) * 0.75f;
            // controlBoat (the driver's keys, if they're fresh)
            bool driven = Seats[0] != Nobody && Time.time - inputAt < 0.5f;
            if (driven)
            {
                float acc = 0f;
                if (inLeft) deltaRot -= 1f;
                if (inRight) deltaRot += 1f;
                if (inRight != inLeft && !inUp && !inDown) acc += 0.005f;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + deltaRot, 0f);
                if (inUp) acc += 0.04f;
                if (inDown) acc -= 0.005f;
                vel += transform.forward * acc;
                rowL = (inRight && !inLeft) || inUp ? rowL + 0.3926991f : rowL;
                rowR = (inLeft && !inRight) || inUp ? rowR + 0.3926991f : rowR;
                if ((inUp || inLeft || inRight) && Time.time % 1f < 0.05f) { Rowed++; BlockNet.ServerSound(transform.position, status == Status.InWater ? "boat.paddle_water" : "boat.paddle_land", 1f, 0.6f); }
            }
            else transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + deltaRot, 0f);
            // move, stopping at what's solid
            var step = vel * S;
            // (the sphere's foot just over the boat's: a cast that starts inside the ground doesn't see it)
            var from = transform.position + Vector3.up * 0.5f * S;
            if (Physics.SphereCast(from, 0.45f * S, step.normalized, out var hit, step.magnitude, ServerLogic.WorldGeometryMask | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                var along = Vector3.ProjectOnPlane(step, hit.normal);
                step = step.normalized * Mathf.Max(0f, hit.distance - 0.02f) + along * 0.5f;
                if (hit.normal.y > 0.6f) vel.y = Mathf.Max(0f, vel.y);
                else { vel.x *= 0.5f; vel.z *= 0.5f; }
            }
            transform.position += step;
            if (status == Status.OnLand && transform.position.y < groundY) transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
            // (its heading to everyone: the game only sends where it is)
            float yaw = transform.eulerAngles.y;
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, sentYaw)) > 1.5f) { sentYaw = yaw; BlockNet.ServerMobState(NetworkObjectId, 230, (byte)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 256f)); }
        }
        float sentYaw = -999f, groundY;

        /// <summary>(dev, #245) What it floats in, and the moon's water volumes round it.</summary>
        public string Describe()
        {
            var st = GetStatus(out float surface);
            var sb = new System.Text.StringBuilder($"boat {V3(transform.position)} status={st} surface={surface:F2} waterLevel={waterLevel:F2} vel={V3(vel)} |");
            foreach (var c in Physics.OverlapSphere(transform.position, 3f, ~0, QueryTriggerInteraction.Collide))
            {
                var q = c.GetComponent<QuicksandTrigger>();
                if (q == null) continue;
                bool inside = Physics.OverlapSphere(transform.position, 0.3f * BlockWorld.S, ~0, QueryTriggerInteraction.Collide).Contains(c);
                sb.Append($" {c.name} {c.GetType().Name} water={q.isWater} top={c.bounds.max.y:F2} closest={V3(c.ClosestPoint(transform.position))} touching={inside};");
            }
            return sb.ToString();
        }
        static string V3(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";

        /// <summary>Where it floats: in our water or the moon's own (its surface), on land, or in the air.</summary>
        Status GetStatus(out float surface)
        {
            surface = 0f;
            float S = BlockWorld.S;
            var p = transform.position;
            // our water: the cell under its middle, its top
            if (WaterSwim.WaterCellAt(p + Vector3.up * 0.05f * S, out var wb) || WaterSwim.WaterCellAt(p - Vector3.up * 0.2f * S, out wb))
            {
                surface = wb.Go != null ? wb.Go.transform.position.y + 0.4f * S : p.y;
                if (WaterSwim.WaterCellAt(p + Vector3.up * 0.6f * S, out _)) return Status.UnderWater;
                return Status.InWater;
            }
            // the moon's water (its swimming volumes)
            foreach (var c in Physics.OverlapSphere(p, 0.3f * S, ~0, QueryTriggerInteraction.Collide))
            {
                var q = c.GetComponent<QuicksandTrigger>();
                if (q != null && q.isWater) { surface = c.bounds.max.y; return p.y < surface - 0.6f * S ? Status.UnderWater : Status.InWater; }
            }
            if (Physics.Raycast(p + Vector3.up * 0.2f * S, Vector3.down, out var g, 0.35f * S, ServerLogic.WorldGeometryMask | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore))
            {
                // (Minecraft's: the block's friction; ice is slippery)
                var b = g.collider.GetComponentInParent<BlockRef>();
                var def = b != null ? BlockWorld.Instance?.DefAt(b.Key) : null;
                landFriction = def == Blocks.Ice ? 0.98f : 0.6f; // (every tick, as Minecraft's getGroundFriction)
                groundY = g.point.y;
                return Status.OnLand;
            }
            return Status.InAir;
        }

        /// <summary>Host: the driver's keys.</summary>
        public void ServerInput(ulong sender, byte keys)
        {
            if (Seats[0] != sender) return;
            inUp = (keys & 1) != 0; inDown = (keys & 2) != 0; inLeft = (keys & 4) != 0; inRight = (keys & 8) != 0;
            inputAt = Time.time;
        }

        // ------------------------------------------------------------------ the paddles (BoatModel.animatePaddle)
        Vector3 lastPos; float rowAnim;
        void Animate()
        {
            if (paddleL == null || paddleR == null) return;
            var moved = transform.position - lastPos; lastPos = transform.position;
            bool rowing = Seats[0] != Nobody && moved.magnitude > 0.002f;
            if (rowing) rowAnim += Time.deltaTime * 20f * 0.3926991f;
            float t = rowing ? rowAnim : 0f;
            Paddle(paddleL, t, 0, -9f);
            Paddle(paddleR, t, 1, -9f);
        }

        static void Paddle(Transform paddle, float time, int side, float _)
        {
            float x = Mathf.Lerp(-1.0471976f, -0.2617994f, (Mathf.Sin(-time) + 1f) / 2f);
            float y = Mathf.Lerp(-0.7853982f, 0.7853982f, (Mathf.Sin(-time + 1f) + 1f) / 2f);
            if (side == 1) y = Mathf.PI - y;
            paddle.localRotation = BoatModel.McRotation(x, y, 0.19634955f) * BoatModel.ToOurs;
        }

        // ------------------------------------------------------------------ getting in and out, its chest, breaking it
        public override string UseTip(string heldKey)
        {
            var me = GameNetworkManager.Instance?.localPlayerController;
            if (Kind.chest && me != null && me.isCrouching) return "Open";
            return Seats.Take(SeatCount).Contains(Nobody) ? "Get in" : null;
        }

        float seatCheck;
        /// <summary>Host: a seat whose rider died or left is free again (their own machine gets out on its own).</summary>
        void ServerFreeSeats()
        {
            for (int i = 0; i < Seats.Length; i++)
            {
                if (Seats[i] == Nobody) continue;
                var p = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.actualClientId == Seats[i] && x.isPlayerControlled);
                if (p != null && !p.isPlayerDead) continue;
                Seats[i] = Nobody;
                BlockNet.ServerMobState(NetworkObjectId, (byte)(220 + i), 255);
            }
        }

        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            if (Kind.chest && p.isCrouching) { BlockNet.ServerMobState(NetworkObjectId, 212, (byte)p.playerClientId); return; }
            int seat = System.Array.IndexOf(Seats, p.actualClientId);
            if (seat >= 0) { Seats[seat] = Nobody; BlockNet.ServerMobState(NetworkObjectId, (byte)(220 + seat), 255); return; }
            for (int i = 0; i < SeatCount; i++)
                if (Seats[i] == Nobody)
                {
                    Seats[i] = p.actualClientId;
                    BlockNet.ServerMobState(NetworkObjectId, (byte)(220 + i), (byte)p.playerClientId);
                    return;
                }
        }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 212) { var me = GameNetworkManager.Instance?.localPlayerController; if (me != null && b == (byte)me.playerClientId) ChestUI.Open(StorageKey); return; }
            if (a == 230) { if (!IsServer) transform.rotation = Quaternion.Euler(0f, b / 256f * 360f, 0f); return; }
            if (a < 220 || a > 221) return;
            int seat = a - 220;
            var rider = b < 255 && b < StartOfRound.Instance.allPlayerScripts.Length ? StartOfRound.Instance.allPlayerScripts[b] : null;
            Seats[seat] = rider != null ? rider.actualClientId : Nobody;
            var local = GameNetworkManager.Instance?.localPlayerController;
            if (rider != null && rider == local) { BoatRiding.Boat = this; BoatRiding.Seat = seat; }
            else if (BoatRiding.Boat == this && BoatRiding.Seat == seat && rider != local) BoatRiding.GetOut();
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (isEnemyDead) return;
            if (playerWhoHit != null && Seats.Contains(playerWhoHit.actualClientId)) return;
            Sounds.Play("dig.wood", transform.position, 0.7f, Random.Range(0.9f, 1.1f), 16f);
            if (!IsServer) return;
            hits += Mathf.Max(1, force);
            if (hits < 2) return;
            if (ModItems.ByKey.TryGetValue(ItemKey(Kind.wood, Kind.chest), out var it)) ModItems.ServerSpawnStack(it, 1, transform.position + Vector3.up * 0.5f);
            if (Kind.chest) { var c = Chests.Of(StorageKey); if (c != null) { Chests.SpawnContents(c, transform.position + Vector3.up * 0.5f); Chests.All.Remove(StorageKey); } }
            for (int i = 0; i < 2; i++) if (Seats[i] != Nobody) BlockNet.ServerMobState(NetworkObjectId, (byte)(220 + i), 255);
            KillEnemyOnOwnerClient(true);
        }

        protected override void ServerDrops(Vector3 at) { }

        /// <summary>Host: a boat put down there, facing that way (its item already out of the hand).</summary>
        public static void ServerPlace(ulong sender, string itemKey, Vector3 at, float yaw)
        {
            var p = ServerLogic.PlayerFor(sender);
            var type = TypeFor(itemKey);
            if (p == null || type == null || Vector3.Distance(p.gameplayCamera.transform.position, at) > 8f * BlockWorld.S + 3f) { Inventory.ServerSpawnFor(sender, itemKey, 1); return; }
            MobSpawns.DevSpawnUntil = Time.time + 1f;
            RoundManager.Instance.SpawnEnemyGameObject(at, yaw, -1, type);
            Placed++;
        }

        public static void Register()
        {
            foreach (var w in Blocks.Woods.Where(x => !x.Nether))
                foreach (bool chest in new[] { false, true })
                {
                    var wood = w; bool c = chest;
                    var t = Register<BoatAI>((chest ? "ChestBoat_" : "Boat_") + w.Key, w.Name + (chest ? " Boat with Chest" : " Boat"), chest ? "A boat with a chest" : "A boat for two",
                        0, 2, 0.6f, 20, 0f, m => BoatModel.Build(m, wood.Key, c), daytime: true, length: 1.4f);
                    if (t != null) Kinds[t] = (w, chest);
                }
        }
    }

    /// <summary>(#223) Boats' item names (no game types here: the recipes, unit-tested offline, use them).</summary>
    public static class BoatKeys
    {
        public static string ItemKey(Blocks.WoodKind w, bool chest) => w.Key + (chest ? "_chest_boat" : "_boat");
    }

    /// <summary>Minecraft's BoatModel (128x64, the chest boat's 128x128 with its chest), placed as AbstractBoatRenderer
    /// draws it: 0.375 up, turned round, scaled -1, -1, 1 and a quarter turn; each box turned into our convention (y up,
    /// its front +z: Minecraft's model space turned half over about x) for EntityModels.Part.</summary>
    public static class BoatModel
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        /// <summary>Our boxes' frame inside Minecraft's model space: half a turn about x.</summary>
        public static readonly Quaternion ToOurs = Quaternion.AngleAxis(180f, Vector3.right);

        /// <summary>A ModelPart's rotation (Z, then Y, then X; Minecraft's matrices are Unity's, number for number).</summary>
        public static Quaternion McRotation(float x, float y, float z) =>
            Quaternion.AngleAxis(z * Mathf.Rad2Deg, Vector3.forward) * Quaternion.AngleAxis(y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right);

        public static void Build(Transform parent, string wood, bool chest)
        {
            float k = EntityModels.Px * BlockWorld.S;
            string tex = (chest ? "entity/chest_boat/" : "entity/boat/") + wood;
            if (!mats.TryGetValue(tex, out var mat)) mats[tex] = mat = EntityModels.Material("LMC_" + tex.Replace('/', '_'), tex, new Color(0.55f, 0.4f, 0.25f));
            var mc = new GameObject("Mc") { layer = parent.gameObject.layer }.transform;
            mc.SetParent(parent, false);
            // (the renderer: up 0.375, turned 180, scaled -1 -1 1 (half a turn about z), a quarter turn)
            mc.localPosition = new Vector3(0f, 0.375f * BlockWorld.S, 0f);
            mc.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.up);
            void P(string name, Vector3 offset, Vector3 rot, Vector3 min, Vector3 size, int u, int v)
            {
                var g = new GameObject(name) { layer = mc.gameObject.layer }.transform;
                g.SetParent(mc, false);
                g.localPosition = offset * k;
                g.localRotation = McRotation(rot.x, rot.y, rot.z) * ToOurs;
                var c = min + size / 2f;
                EntityModels.Part(g, "Box", new Vector3(c.x, -c.y, -c.z), size, u, v, k, mat, mc.gameObject.layer);
            }
            P("bottom", new Vector3(0, 3, 1), new Vector3(1.5707964f, 0, 0), new Vector3(-14, -9, -3), new Vector3(28, 16, 3), 0, 0);
            P("back", new Vector3(-15, 4, 4), new Vector3(0, 4.712389f, 0), new Vector3(-13, -7, -1), new Vector3(18, 6, 2), 0, 19);
            P("front", new Vector3(15, 4, 0), new Vector3(0, 1.5707964f, 0), new Vector3(-8, -7, -1), new Vector3(16, 6, 2), 0, 27);
            P("right", new Vector3(0, 4, -9), new Vector3(0, 3.1415927f, 0), new Vector3(-14, -7, -1), new Vector3(28, 6, 2), 0, 35);
            P("left", new Vector3(0, 4, 9), Vector3.zero, new Vector3(-14, -7, -1), new Vector3(28, 6, 2), 0, 43);
            // the paddles: a shaft and a blade each, turned by the rowing (BoatAI)
            foreach (var (name, z, u0, v0, rotY, bladeX) in new[] { ("left_paddle", 9f, 62, 0, 0f, -1.001f), ("right_paddle", -9f, 62, 20, 3.1415927f, 0.001f) })
            {
                var g = new GameObject(name) { layer = mc.gameObject.layer }.transform;
                g.SetParent(mc, false);
                g.localPosition = new Vector3(3, -5, z) * k;
                g.localRotation = McRotation(0f, rotY, 0.19634955f) * ToOurs;
                EntityModels.Part(g, "Shaft", new Vector3(0f, -1f, -4f), new Vector3(2, 2, 18), u0, v0, k, mat, mc.gameObject.layer);
                EntityModels.Part(g, "Blade", new Vector3(bladeX + 0.5f, 0f, -11.5f), new Vector3(1, 6, 7), u0, v0, k, mat, mc.gameObject.layer); // (the shaft's texOffs: Minecraft's builder keeps it)
            }
            if (chest)
            {
                P("chest_bottom", new Vector3(-2, -5, -6), new Vector3(0, -1.5707964f, 0), Vector3.zero, new Vector3(12, 8, 12), 0, 76);
                P("chest_lid", new Vector3(-2, -9, -6), new Vector3(0, -1.5707964f, 0), Vector3.zero, new Vector3(12, 4, 12), 0, 59);
                P("chest_lock", new Vector3(-1, -6, -1), new Vector3(0, -1.5707964f, 0), Vector3.zero, new Vector3(2, 4, 1), 0, 59);
            }
        }
    }

    /// <summary>The local player in a boat: seated in it every frame, its driver's keys to the host; Crouch gets out.</summary>
    [HarmonyPatch]
    public static class BoatRiding
    {
        public static BoatAI Boat;
        public static int Seat;
        static float nextSend; static byte lastKeys = 255;
        static UnityEngine.InputSystem.InputAction move;

        /// <summary>The local player's use button with a boat in hand: put it on the water (or ground) they look at, within
        /// five blocks, facing their way.</summary>
        public static void TryPlace(PlayerControllerB p, StackItem boat)
        {
            var cam = p.gameplayCamera.transform;
            float S = BlockWorld.S;
            Vector3? at = null;
            foreach (var h in Physics.RaycastAll(cam.position, cam.forward, 5f * S, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (h.collider.GetComponentInParent<PlayerControllerB>() != null) continue;
                if (h.collider.GetComponent<EnemyAICollisionDetect>() != null || h.collider.GetComponentInParent<EnemyAI>() != null || h.collider.GetComponentInParent<GrabbableObject>() != null) continue; // (not on a mob, a shoulder parrot, or an item)
                var q = h.collider.GetComponent<QuicksandTrigger>();
                if (q != null && q.isWater) { at = new Vector3(h.point.x, h.collider.bounds.max.y, h.point.z); break; }
                if (h.collider.isTrigger && h.collider.GetComponentInParent<BlockRef>() == null) continue;
                at = h.point + Vector3.up * 0.05f * S; break;
            }
            if (!at.HasValue) return;
            if (!GameModes.LocalCreative && Inventory.Take(p, p.currentItemSlot, 1) == null) return;
            BlockNet.RequestBoatPlace(boat.ItemKey, at.Value, p.transform.eulerAngles.y);
        }

        public static void GetOut()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            var b = Boat;
            Boat = null;
            if (p == null) return;
            p.disableMoveInput = false;
            if (b != null) p.TeleportPlayer(b.transform.position + b.transform.right * 1.3f + Vector3.up * 0.5f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool CrouchOut(PlayerControllerB __instance)
        {
            if (Boat == null || __instance != GameNetworkManager.Instance?.localPlayerController) return true;
            BlockNet.RequestAnimal(Boat.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed"), HarmonyPrefix]
        static bool NoJump(PlayerControllerB __instance) => Boat == null || __instance != GameNetworkManager.Instance?.localPlayerController;

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Sit(PlayerControllerB __instance)
        {
            var p = __instance;
            var b = Boat;
            if ((object)b == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            if (b == null || b.isEnemyDead || p.isPlayerDead) { GetOut(); return; } // (b == null: the boat itself destroyed under you)
            p.disableMoveInput = true;
            // (Minecraft's seats: one in the middle; with two, 0.2 forward and 0.6 back)
            float along = b.SeatCount > 1 ? (Seat == 0 ? 0.2f : -0.6f) : 0f;
            p.transform.position = b.transform.position + b.transform.forward * along * BlockWorld.S + Vector3.up * 0.1f * BlockWorld.S;
            p.fallValue = 0f; p.fallValueUncapped = 0f;
            if (Seat != 0) return;
            if (move == null) try { move = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Move"); } catch { }
            var mv = move != null && !p.inSpecialMenu && !p.isTypingChat ? move.ReadValue<Vector2>() : Vector2.zero;
            byte keys = (byte)((mv.y > 0.3f ? 1 : 0) | (mv.y < -0.3f ? 2 : 0) | (mv.x < -0.3f ? 4 : 0) | (mv.x > 0.3f ? 8 : 0));
            if (keys != lastKeys || Time.time >= nextSend)
            {
                lastKeys = keys; nextSend = Time.time + 0.2f;
                BlockNet.RequestBoatInput(b.NetworkObjectId, keys);
            }
        }
    }
}
