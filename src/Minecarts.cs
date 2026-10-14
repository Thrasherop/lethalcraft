using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's rails (#160): a rail joins the rails beside it (straight, round a corner, or up a slope to one a block
    /// higher), worked out by the host as rails are laid and taken up. Powered rails speed a cart up while powered (the
    /// power runs along up to 8 of them) and brake it when not; a detector rail gives power while a cart is on it; an
    /// activator rail is there for the carts that use it. State: bits 0-3 the shape, 16 powered.
    /// </summary>
    public static class Rails
    {
        // shapes (Minecraft's RailShape order)
        public const int NS = 0, EW = 1, AscE = 2, AscW = 3, AscN = 4, AscS = 5, SE = 6, SW = 7, NW = 8, NE = 9;
        public const int Powered = 16;

        public static bool IsRail(BlockDef d) => d != null && d.Shape == BlockShape.Rail;
        public static bool Curves(BlockDef d) => d == Blocks.Rail; // (only the plain rail turns corners)

        static readonly Vector3Int N = new Vector3Int(0, 0, 1), S = new Vector3Int(0, 0, -1), E = new Vector3Int(1, 0, 0), W = new Vector3Int(-1, 0, 0);

        /// <summary>A shape's two ends: the direction out, and how much higher that end is (a slope's top end: 1).</summary>
        public static (Vector3Int dir, int up)[] Exits(int shape)
        {
            switch (shape)
            {
                case EW: return new[] { (E, 0), (W, 0) };
                case AscE: return new[] { (E, 1), (W, 0) };
                case AscW: return new[] { (W, 1), (E, 0) };
                case AscN: return new[] { (N, 1), (S, 0) };
                case AscS: return new[] { (S, 1), (N, 0) };
                case SE: return new[] { (S, 0), (E, 0) };
                case SW: return new[] { (S, 0), (W, 0) };
                case NW: return new[] { (N, 0), (W, 0) };
                case NE: return new[] { (N, 0), (E, 0) };
                default: return new[] { (N, 0), (S, 0) };
            }
        }

        static BlockKey At(BlockKey k, Vector3Int d) => new BlockKey(k.Frame, k.YOff, k.Pos + d);

        /// <summary>The shape a rail at k takes from the rails beside it (level, a block up, a block down).</summary>
        public static int ShapeFor(BlockWorld w, BlockKey k, BlockDef def, int current)
        {
            var dirs = new[] { N, S, E, W };
            var links = new List<(Vector3Int dir, bool up)>();
            foreach (var d in dirs)
            {
                if (IsRail(w.DefAt(At(k, d))) || IsRail(w.DefAt(At(k, d + Vector3Int.down)))) links.Add((d, false));
                else if (IsRail(w.DefAt(At(k, d + Vector3Int.up)))) links.Add((d, true));
            }
            bool Has(Vector3Int d) => links.Any(l => l.dir == d);
            bool Up(Vector3Int d) => links.Any(l => l.dir == d && l.up);
            // a slope up to a rail a block higher
            if (Up(N)) return AscN; if (Up(S)) return AscS; if (Up(E)) return AscE; if (Up(W)) return AscW;
            bool ns = Has(N) || Has(S), ew = Has(E) || Has(W);
            if (ns && !ew) return NS;
            if (ew && !ns) return EW;
            if (ns && ew && Curves(def))
            {
                if (Has(S) && Has(E)) return SE; if (Has(S) && Has(W)) return SW;
                if (Has(N) && Has(W)) return NW; if (Has(N) && Has(E)) return NE;
            }
            if (ns && ew) return current == EW ? EW : NS;
            return current <= EW ? current : NS;
        }

        /// <summary>Host: a rail was laid or taken up at k: it and the rails round it take their shapes again.</summary>
        public static void ServerReshape(BlockKey k)
        {
            var w = BlockWorld.Instance;
            if (w == null) return;
            var ops = new List<Op>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx != 0 && dz != 0) continue;
                        var nk = At(k, new Vector3Int(dx, dy, dz));
                        var b = w.Get(nk);
                        if (b == null || !IsRail(b.Data.Def)) continue;
                        int shape = ShapeFor(w, nk, b.Data.Def, b.Data.State & 15);
                        if (shape != (b.Data.State & 15)) { var d = b.Data; d.State = (byte)((d.State & ~15) | shape); ops.Add(Op.State(nk, d)); }
                    }
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
        }

        /// <summary>Where a cart is on a rail cell, at t (0: the first end, 1: the second), in the frame's local space.</summary>
        public static Vector3 PathPoint(BlockWorld w, BlockKey k, int shape, float t)
        {
            var c = w.LocalCenter(k) - Vector3.up * (BlockWorld.S * 0.5f - 0.02f); // (the cell's floor)
            var ex = Exits(shape);
            Vector3 End(int i) => c + new Vector3(ex[i].dir.x, 0f, ex[i].dir.z) * (BlockWorld.S * 0.5f) + Vector3.up * (ex[i].up * BlockWorld.S);
            bool slope = shape >= AscE && shape <= AscS;
            var mid = slope ? (End(0) + End(1)) * 0.5f : c;
            return t < 0.5f ? Vector3.Lerp(End(0), mid, t * 2f) : Vector3.Lerp(mid, End(1), (t - 0.5f) * 2f);
        }
    }

    /// <summary>
    /// The minecart (#160): placed on a rail ([Right-click] with it), it rolls along the track the host works out: down
    /// slopes, round corners, sped up by powered rails, slowed by friction, stopped at the end of the line. [E] gets in
    /// (Crouch gets out); walking into it pushes it. A couple of hits break it back into a minecart item.
    /// </summary>
    public class MinecartAI : MinecraftMobAI
    {
        public static EnemyType Type, ChestType, HopperType, TntType, FurnaceType;
        /// <summary>Its kind's item ("minecart", "chest_minecart"...), from its enemy type.</summary>
        public string Key => enemyType == ChestType ? "chest_minecart" : enemyType == HopperType ? "hopper_minecart" : enemyType == TntType ? "tnt_minecart" : enemyType == FurnaceType ? "furnace_minecart" : "minecart";
        public static readonly string[] Keys = { "minecart", "chest_minecart", "hopper_minecart", "tnt_minecart", "furnace_minecart" };
        bool Storage => enemyType == ChestType || enemyType == HopperType;
        /// <summary>A chest or hopper minecart's contents (Chests: the mobs' frame; y the slot count).</summary>
        public BlockKey StorageKey => new BlockKey(Chests.MobFrame, 0, new Vector3Int((int)(NetworkObjectId & 0xFFFFFF), enemyType == HopperType ? 5 : 27, 0));
        float fuel, primedAt = -1f, nextPull;
        protected override string Sound => "minecart";
        protected override bool Passive => true;
        protected override bool Swims => true; // (its own movement: no navmesh)
        protected override int Xp => 0;
        public const float MaxSpeed = 8f, Friction = 0.25f, SlopeAccel = 3.2f, PoweredAccel = 24f, Brake = 20f;

        BlockKey cell; bool onTrack; float t, speed; int sign = 1;
        public const ulong NoRider = ulong.MaxValue;
        public ulong Rider = NoRider;
        int hits;
        public static int Placed, DetectorTrips; // (dev/tests)
        public float Speed => speed;

        public override void Start()
        {
            SetEnemyOutside(transform.position.y > -100f);
            base.Start();
        }

        protected override void SwimAI() { }

        /// <summary>Host: set it on the rail at k.</summary>
        public void ServerPlace(BlockKey k)
        {
            cell = k; onTrack = true; t = 0.5f; speed = 0f; sign = 1;
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead || !IsServer || !onTrack) return;
            var w = BlockWorld.Instance;
            var b = w?.Get(cell);
            if (b == null || !Rails.IsRail(b.Data.Def)) { onTrack = false; return; }
            float dt = Time.deltaTime;
            int shape = b.Data.State & 15;
            // slopes, powered rails, friction
            if (shape >= Rails.AscE && shape <= Rails.AscS) speed += (sign > 0 ? 1f : -1f) * SlopeAccel * dt; // (end 0 is the high end: toward end 1 is downhill)
            ServerKind(w, b, dt);
            if (isEnemyDead) return;
            ServerPushes(w, shape);
            if (b.Data.Def == Blocks.PoweredRail)
            {
                if ((b.Data.State & Rails.Powered) != 0) speed = Mathf.Min(MaxSpeed, Mathf.Abs(speed) < 0.05f ? KickOff(w, shape) : speed + PoweredAccel * dt);
                else speed = Mathf.MoveTowards(speed, 0f, Brake * dt);
            }
            speed = Mathf.MoveTowards(speed, 0f, Friction * dt);
            if (speed < 0f) { speed = -speed; sign = -sign; }
            speed = Mathf.Min(speed, MaxSpeed);
            // along the cell, and on into the next
            t += sign * speed * dt / BlockWorld.S;
            if (t > 1f || t < 0f)
            {
                var ex = Rails.Exits(shape)[t > 1f ? 1 : 0];
                var next = FindNext(w, cell, ex.dir, ex.up, out int nextShape, out bool fromStart);
                if (next == null) { t = Mathf.Clamp01(t); speed = 0f; } // (the end of the line)
                else
                {
                    float over = (t > 1f ? t - 1f : -t);
                    cell = next.Value; shape = nextShape;
                    sign = fromStart ? 1 : -1;
                    t = fromStart ? over : 1f - over;
                }
            }
            var local = Rails.PathPoint(w, cell, shape, Mathf.Clamp01(t));
            var root = w.FrameRoot(cell.Frame, false);
            transform.position = root != null ? root.TransformPoint(local) : local;
            var ahead = Rails.PathPoint(w, cell, shape, Mathf.Clamp01(t + 0.05f * sign)) - Rails.PathPoint(w, cell, shape, Mathf.Clamp01(t - 0.05f * sign));
            if (ahead.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(root != null ? root.TransformDirection(ahead) : ahead);
            SyncPositionToClients();
            ServerDetector(w);
        }

        /// <summary>Host: what its kind does: a furnace minecart pushes itself while it has fuel; a TNT minecart primed by a
        /// powered activator rail blows up 4 s later; a hopper minecart takes from the container above it and items it
        /// passes over (not on a powered activator rail).</summary>
        void ServerKind(BlockWorld w, BlockInstance rail, float dt)
        {
            bool activated = rail.Data.Def == Blocks.ActivatorRail && (rail.Data.State & Rails.Powered) != 0;
            if (enemyType == FurnaceType && fuel > 0f) { fuel -= dt; speed = Mathf.Max(speed, 4f); }
            if (enemyType == TntType)
            {
                if (activated && primedAt < 0f) { primedAt = Time.time; BlockNet.ServerSound(transform.position, "fuse", 1f, 1f); }
                if (primedAt >= 0f && Time.time > primedAt + 4f) ServerExplode();
            }
            if (enemyType == HopperType && !activated && Time.time > nextPull)
            {
                nextPull = Time.time + 0.4f;
                var c = Chests.Ensure(StorageKey);
                if (ItemTransport.ServerPullInto(w, new BlockKey(cell.Frame, cell.YOff, cell.Pos + Vector3Int.up), transform.position + Vector3.up * 0.4f, c)) BlockNet.ServerChest(StorageKey, c);
            }
        }

        public void ServerExplode()
        {
            if (isEnemyDead) return;
            var at = transform.position + Vector3.up * 0.4f;
            float r = 4f * BlockWorld.S; // (Minecraft's TNT minecart: a little more than TNT, more at speed)
            ServerLogic.ExplodeBlocks(at, r, -1, carveGround: true);
            BlockNet.ServerExplosion(at, r);
            if (Rider != NoRider) BlockNet.ServerMobState(NetworkObjectId, 210, 255);
            KillEnemyOnOwnerClient(true);
        }

        /// <summary>A powered rail starts a still cart away from a block at one of its ends (Minecraft's), else onward.</summary>
        float KickOff(BlockWorld w, int shape)
        {
            var ex = Rails.Exits(shape);
            if (w.IsSolidAt(new BlockKey(cell.Frame, cell.YOff, cell.Pos + ex[1].dir))) sign = -1;
            else if (w.IsSolidAt(new BlockKey(cell.Frame, cell.YOff, cell.Pos + ex[0].dir))) sign = 1;
            return 2f;
        }

        /// <summary>The rail the track goes on to out of this end (level, a block up, a block down) that has an end facing
        /// back; fromStart: the cart enters it at its first end.</summary>
        static BlockKey? FindNext(BlockWorld w, BlockKey from, Vector3Int dir, int up, out int shape, out bool fromStart)
        {
            shape = 0; fromStart = true;
            foreach (int dy in up > 0 ? new[] { 1, 0 } : new[] { 0, -1 })
            {
                var k = new BlockKey(from.Frame, from.YOff, from.Pos + dir + new Vector3Int(0, dy, 0));
                var b = w.Get(k);
                if (b == null || !Rails.IsRail(b.Data.Def)) continue;
                shape = b.Data.State & 15;
                var ex = Rails.Exits(shape);
                for (int i = 0; i < 2; i++)
                    if (ex[i].dir == -dir) { fromStart = i == 0; return k; }
            }
            return null;
        }

        BlockKey lastDetector; bool onDetector;

        /// <summary>Host: a detector rail is powered while a cart is on it.</summary>
        void ServerDetector(BlockWorld w)
        {
            var b = w.Get(cell);
            bool now = b != null && b.Data.Def == Blocks.DetectorRail;
            if (onDetector && (!now || !lastDetector.Equals(cell))) { SetDetector(w, lastDetector, false); onDetector = false; }
            if (now && !onDetector) { SetDetector(w, cell, true); lastDetector = cell; onDetector = true; DetectorTrips++; }
        }

        static void SetDetector(BlockWorld w, BlockKey k, bool on)
        {
            var b = w.Get(k);
            if (b == null || b.Data.Def != Blocks.DetectorRail) return;
            var d = b.Data; d.State = (byte)(on ? d.State | Rails.Powered : d.State & ~Rails.Powered);
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            Redstone.MarkDirty();
        }

        /// <summary>Host: someone walking into it pushes it along the track, away from them.</summary>
        void ServerPushes(BlockWorld w, int shape)
        {
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead || p.actualClientId == Rider) continue;
                var off = transform.position - p.transform.position; off.y = 0f;
                if (off.magnitude > 0.9f) continue;
                var along = (Rails.PathPoint(w, cell, shape, 1f) - Rails.PathPoint(w, cell, shape, 0f)).normalized;
                int s = Vector3.Dot(w.ToFrameLocalDir(cell.Frame, off), along) >= 0f ? 1 : -1;
                if (speed < 0.1f || s == sign) { sign = s; speed = Mathf.Max(speed, 2.5f); }
            }
        }

        // ------------------------------------------------------------------ riding, breaking
        public override string UseTip(string heldKey)
        {
            if (Storage) return "Open";
            if (enemyType == FurnaceType) return heldKey != null && Crafting.FuelSeconds.ContainsKey(heldKey) ? "Fuel it" : null;
            if (enemyType == TntType) return null;
            return Rider == NoRider ? "Get in" : null;
        }

        public override void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            if (Storage) { BlockNet.ServerMobState(NetworkObjectId, 212, (byte)p.playerClientId); return; }
            if (enemyType == FurnaceType)
            {
                // coal (or any fuel) keeps it going three minutes per piece, off away from whoever fed it
                if (held is StackItem st && st.Count > 0 && Crafting.FuelSeconds.ContainsKey(st.ItemKey))
                {
                    st.ServerSetCount(st.Count - 1, despawnIfEmpty: true);
                    fuel += 180f;
                    var w = BlockWorld.Instance; var b = w?.Get(cell);
                    if (b != null)
                    {
                        var along = Rails.PathPoint(w, cell, b.Data.State & 15, 1f) - Rails.PathPoint(w, cell, b.Data.State & 15, 0f);
                        sign = Vector3.Dot(w.ToFrameLocalDir(cell.Frame, transform.position - p.transform.position), along) >= 0f ? 1 : -1;
                    }
                }
                return;
            }
            if (enemyType == TntType) return;
            if (Rider == p.actualClientId) { Rider = NoRider; BlockNet.ServerMobState(NetworkObjectId, 210, 255); return; }
            if (Rider != NoRider) return;
            Rider = p.actualClientId;
            BlockNet.ServerMobState(NetworkObjectId, 210, (byte)p.playerClientId);
        }

        public override void ReceiveState(byte a, byte b)
        {
            if (a == 212) { var me = GameNetworkManager.Instance?.localPlayerController; if (me != null && b == (byte)me.playerClientId) ChestUI.Open(StorageKey); return; }
            if (a != 210) return;
            var rider = b < 255 && b < StartOfRound.Instance.allPlayerScripts.Length ? StartOfRound.Instance.allPlayerScripts[b] : null;
            Rider = rider != null ? rider.actualClientId : NoRider;
            var local = GameNetworkManager.Instance?.localPlayerController;
            if (rider != null && rider == local) CartRiding.Cart = this;
            else if (CartRiding.Cart == this && rider != local) CartRiding.GetOut();
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (isEnemyDead) return;
            if (playerWhoHit != null && playerWhoHit.actualClientId == Rider) return;
            Sounds.Play("minecart.hit", transform.position, 0.7f, Random.Range(0.9f, 1.1f), 16f);
            if (!IsServer) return;
            hits += Mathf.Max(1, force);
            if (hits < 2) return;
            if (enemyType == TntType && force >= 1 && speed > 3f) { ServerExplode(); return; } // (Minecraft's: one broken at speed goes off)
            if (ModItems.ByKey.TryGetValue(Key, out var it)) ModItems.ServerSpawnStack(it, 1, transform.position + Vector3.up * 0.5f);
            if (Storage) { var contents = Chests.Of(StorageKey); if (contents != null) { Chests.SpawnContents(contents, transform.position + Vector3.up * 0.5f); Chests.All.Remove(StorageKey); } }
            if (Rider != NoRider) BlockNet.ServerMobState(NetworkObjectId, 210, 255);
            KillEnemyOnOwnerClient(true);
        }

        protected override void ServerDrops(Vector3 at) { }

        /// <summary>Host: a minecart set on the rail at k.</summary>
        public static void ServerSpawnOn(ulong sender, BlockKey k, byte kind = 0)
        {
            var w = BlockWorld.Instance;
            var type = new[] { Type, ChestType, HopperType, TntType, FurnaceType }[Mathf.Clamp(kind, 0, 4)];
            if (type == null || w == null || !Rails.IsRail(w.DefAt(k))) { Inventory.ServerSpawnFor(sender, Keys[Mathf.Clamp(kind, 0, 4)], 1); return; }
            MobSpawns.DevSpawnUntil = Time.time + 1f;
            var go = RoundManager.Instance.SpawnEnemyGameObject(w.WorldCenter(k), 0f, -1, type);
            var cart = go.TryGet(out Unity.Netcode.NetworkObject no) ? no.GetComponent<MinecartAI>() : null;
            if (cart != null) cart.ServerPlace(k);
            Placed++;
        }

        public static void Register()
        {
            Type = Register<MinecartAI>("Minecart", "Minecart", "A cart for the rails", 0, 2, 0.8f, 20, 0f, BuildModel, daytime: true, length: 0.9f);
            ChestType = Register<MinecartAI>("ChestMinecart", "Minecart with Chest", "A cart that carries a chest", 0, 2, 0.8f, 20, 0f, t => { BuildModel(t); Load(t, Blocks.Chest); }, daytime: true, length: 0.9f);
            HopperType = Register<MinecartAI>("HopperMinecart", "Minecart with Hopper", "Picks up items along the rails", 0, 2, 0.8f, 20, 0f, t => { BuildModel(t); Load(t, Blocks.Hopper); }, daytime: true, length: 0.9f);
            TntType = Register<MinecartAI>("TntMinecart", "Minecart with TNT", "Goes off on a powered activator rail", 0, 2, 0.8f, 20, 0f, t => { BuildModel(t); Load(t, Blocks.TNT); }, daytime: true, length: 0.9f);
            FurnaceType = Register<MinecartAI>("FurnaceMinecart", "Minecart with Furnace", "Fed coal, it pushes itself", 0, 2, 0.8f, 20, 0f, t => { BuildModel(t); Load(t, Blocks.Furnace); }, daytime: true, length: 0.9f);
        }

        static Material sharedMat;

        /// <summary>What a cart carries, a block in it (Minecraft's: 10 px, sitting in the cart).</summary>
        static void Load(Transform parent, BlockDef b)
        {
            if (b == null) return;
            var go = new GameObject("Load"); go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 9f * EntityModels.Px * BlockWorld.S, 0f);
            go.transform.localScale = Vector3.one * (10f / 16f);
            go.layer = 19;
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = MeshBuilder.For(b, 0, 0);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { Atlas.Opaque, Atlas.Emissive };
            Atlas.NoDecals(mr);
        }

        // the model: Minecraft's minecart (64x32): a floor and four sides
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (sharedMat == null) sharedMat = EntityModels.Material("LMC_Minecart", "entity/minecart", new Color(0.45f, 0.45f, 0.5f));
            EntityModels.Part(parent, "Floor", new Vector3(0, 2, 0), new Vector3(16, 2, 20), 0, 10, k, sharedMat);
            EntityModels.Part(parent, "SideL", new Vector3(7, 6, 0), new Vector3(2, 8, 20), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "SideR", new Vector3(-7, 6, 0), new Vector3(2, 8, 20), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Front", new Vector3(0, 6, 9), new Vector3(12, 8, 2), 0, 0, k, sharedMat);
            EntityModels.Part(parent, "Back", new Vector3(0, 6, -9), new Vector3(12, 8, 2), 0, 0, k, sharedMat);
        }
    }

    /// <summary>The local player in a minecart: seated in it every frame; Crouch gets out.</summary>
    [HarmonyPatch]
    public static class CartRiding
    {
        public static MinecartAI Cart;

        public static void GetOut()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            var c = Cart;
            Cart = null;
            if (p == null) return;
            p.disableMoveInput = false;
            if (c != null) p.TeleportPlayer(c.transform.position + c.transform.right * 1.2f + Vector3.up * 0.3f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool CrouchOut(PlayerControllerB __instance)
        {
            if (Cart == null || __instance != GameNetworkManager.Instance?.localPlayerController) return true;
            BlockNet.RequestAnimal(Cart.NetworkObjectId);
            return false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed"), HarmonyPrefix]
        static bool NoJump(PlayerControllerB __instance) => Cart == null || __instance != GameNetworkManager.Instance?.localPlayerController;

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Sit(PlayerControllerB __instance)
        {
            var p = __instance;
            var c = Cart;
            if (c == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            if (c.isEnemyDead || p.isPlayerDead) { GetOut(); return; }
            p.disableMoveInput = true;
            p.transform.position = c.transform.position + Vector3.up * 0.25f;
            p.fallValue = 0f; p.fallValueUncapped = 0f;
        }
    }
}
