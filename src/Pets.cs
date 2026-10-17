using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's tameable pets (#96): wolves and cats, out on the moons by day ([Mobs] WolfRarity, CatRarity).
    /// [E] holding its food (a bone for a wolf; raw porkchop or beef for a cat: this world has no fish) tames it one try
    /// in three. A tamed pet follows its owner (and catches up from far off, as Minecraft's do); [E] on your own pet
    /// sits it down or gets it up. A tamed wolf goes for monsters that go for you, and whatever you hit; creepers keep
    /// away from cats. Pets in the ship when it takes off come along to the next landing.
    /// </summary>
    [HarmonyPatch]
    public abstract class PetAI : MinecraftMobAI
    {
        public const ulong NoOwner = ulong.MaxValue;
        public bool Tamed, Sitting;
        public ulong Owner = NoOwner;
        /// <summary>(#227) Its owner's Steam id (or name): who it's whose across a reload, when everyone's numbers change.</summary>
        public string OwnerIdentity;
        /// <summary>(#232) What last hurt it, and when (PetDeaths: its death message).</summary>
        public string LastHurt; public float LastHurtAt = -100f;
        bool deathTold;
        /// <summary>Host: it died: tamed, its owner hears of it, once (its body is gone at once: no isEnemyDead to see later).</summary>
        public void ServerDeathTold() { if (!Tamed || deathTold) return; deathTold = true; PetDeaths.ServerDied(this); }
        public override bool GnawsBlocks => false;
        protected override bool HatesLight => false; // (a pet coming back with the ship, into a lit ship)
        /// <summary>The foods that tame it.</summary>
        protected abstract string[] Food { get; }
        protected virtual float FollowSpeed => 5.2f;
        public PlayerControllerB OwnerPlayer => Owner == NoOwner ? null : StartOfRound.Instance.allPlayerScripts.FirstOrDefault(p => p != null && p.isPlayerControlled && p.actualClientId == Owner);
        Transform modelT;
        /// <summary>Sits by tipping the whole model (back end down); false for a model that poses its own parts.</summary>
        protected virtual bool TiltToSit => true;

        public override void Start()
        {
            base.Start();
            modelT = transform.Find("Model");
        }

        public override void Update()
        {
            base.Update();
            // (sitting: back end down, head up)
            if (modelT != null && TiltToSit) modelT.localRotation = Quaternion.Slerp(modelT.localRotation, Quaternion.Euler(Sitting ? -18f : 0f, 0f, 0f), Time.deltaTime * 6f);
        }

        /// <summary>Owner of the AI (the host, for a pet): a tamed pet's day. Untamed, the usual roam.</summary>
        protected override bool BusyAI()
        {
            if (!Tamed) return false;
            if (currentSearch.inProgress) StopSearch(currentSearch);
            var o = OwnerPlayer;
            if (Sitting || o == null || o.isPlayerDead) { if (agent.hasPath) agent.ResetPath(); return true; }
            if (TamedAI(o)) return true;
            float d = Vector3.Distance(transform.position, o.transform.position);
            bool otherSide = o.isInsideFactory == isOutside; // (its owner went in, or came out)
            if (d > 24f || otherSide)
            {
                // far behind: it turns up beside its owner (Minecraft's pets do)
                if (NavMesh.SamplePosition(o.transform.position - o.transform.forward * 1.5f, out var h, 3f, agent.areaMask))
                {
                    agent.Warp(h.position);
                    if (otherSide) { isOutside = !o.isInsideFactory; allAINodes = isOutside ? RoundManager.Instance.outsideAINodes : RoundManager.Instance.insideAINodes; }
                }
                return true;
            }
            agent.speed = d > 10f ? FollowSpeed * 1.4f : FollowSpeed;
            if (d > 3.5f) SetDestinationToPosition(o.transform.position, checkForPath: false);
            else if (agent.hasPath && d < 2.5f) agent.ResetPath();
            return true;
        }

        /// <summary>Something of its own to do this step, tamed (a wolf's fight); true: done.</summary>
        protected virtual bool TamedAI(PlayerControllerB owner) => false;

        // the owner never attacked by it; hit by its owner it doesn't turn on them
        protected override bool Wants(PlayerControllerB p) => !Tamed && base.Wants(p);

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (Tamed && playerWhoHit != null && playerWhoHit.actualClientId == Owner)
            {
                LastHurt = "player:" + playerWhoHit.playerUsername; LastHurtAt = Time.time; // (#232: its death message still names who did it)
                base.HitEnemy(force, null, playHitSFX, hitID);
                return;
            }
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        // ------------------------------------------------------------------ the host
        public void ServerInteract(ulong sender)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || isEnemyDead || Vector3.Distance(p.transform.position, transform.position) > 5f) return;
            var held = p.currentlyHeldObjectServer as StackItem;
            if (Tamed)
            {
                if (Owner != sender) { BlockNet.ServerToast(sender, $"It's {(OwnerPlayer != null ? OwnerPlayer.playerUsername : "someone")}'s."); return; }
                Sitting = !Sitting;
                BlockNet.ServerPet(this);
                return;
            }
            if (held == null || !Food.Contains(held.ItemKey)) return;
            held.ServerUseUp(p);
            BlockNet.ServerSound(transform.position + Vector3.up, Sound + ".eat", 0.8f, 1f);
            if (Random.Range(0, 3) == 0) ServerTame(sender);
            else BlockNet.ServerToast(sender, "Not yet...");
        }

        public void ServerTame(ulong owner, bool quiet = false)
        {
            Tamed = true; Owner = owner; Sitting = false;
            var op = ServerLogic.PlayerFor(owner); if (op != null) OwnerIdentity = Armor.IdentityOf(op); // (#227)
            targetPlayer = null; movingTowardsTargetPlayer = false;
            if (currentBehaviourStateIndex != Roam) SwitchToBehaviourState(Roam);
            if (!quiet) { BlockNet.ServerToast(owner, $"The {enemyType.enemyName.ToLowerInvariant()} is tamed."); Advancements.ServerGrant(owner, "bff"); }
            BlockNet.ServerPet(this);
        }

        /// <summary>Every machine: the host's word.</summary>
        public virtual void Receive(bool tamed, ulong owner, bool sitting)
        {
            bool was = Tamed;
            Tamed = tamed; Owner = owner; Sitting = sitting;
            if (tamed && !was) Sounds.Play(Sound + ".tamed", transform.position + Vector3.up, 0.8f, 1f, 16f);
        }

        public override string SaveState()
        {
            var o = OwnerPlayer; if (o != null) OwnerIdentity = Armor.IdentityOf(o);
            return Tamed ? (Sitting ? "1," : "0,") + (OwnerIdentity ?? "") : null;
        }

        /// <summary>(#227) Back from the save: tamed, sitting, its owner once they're here (ShipMobs looks for them).</summary>
        public override void LoadState(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int c = s.IndexOf(',');
            Tamed = true; Sitting = s.StartsWith("1"); Owner = NoOwner;
            OwnerIdentity = c >= 0 && c + 1 < s.Length ? s.Substring(c + 1) : null;
            ServerFindOwner();
            BlockNet.ServerPet(this);
        }

        /// <summary>Host: its saved owner, if they're in the game now. True: found (or nobody to look for).</summary>
        public bool ServerFindOwner()
        {
            if (!Tamed || Owner != NoOwner || string.IsNullOrEmpty(OwnerIdentity)) return true;
            var p = StartOfRound.Instance.allPlayerScripts.FirstOrDefault(x => x != null && x.isPlayerControlled && Armor.IdentityOf(x) == OwnerIdentity);
            if (p == null) return false;
            Owner = p.actualClientId;
            BlockNet.ServerPet(this);
            return true;
        }

        public static void ServerSyncAll(ulong client)
        {
            if (RoundManager.Instance == null) return;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is PetAI pet && !pet.isEnemyDead && pet.Tamed) BlockNet.ServerPet(pet, client);
        }

        // ------------------------------------------------------------------ [E] on a pet
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix]
        static bool OnInteract(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || !Builder.CanAct(p) || Riding.Horse != null) return true;
            var pet = Aimed(p);
            if (pet == null) return true;
            string key = (p.currentlyHeldObjectServer as StackItem)?.ItemKey;
            bool mine = pet.Tamed && pet.Owner == p.actualClientId;
            if (!mine && !(!pet.Tamed && key != null && pet.Food.Contains(key))) return true;
            BlockNet.RequestPet(pet);
            return false;
        }

        public static PetAI Aimed(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 19, QueryTriggerInteraction.Collide).OrderBy(h => h.distance)) // (nearest first)
            {
                var d = hit.collider.GetComponent<EnemyAICollisionDetect>();
                if (d != null && d.mainScript is PetAI pet && !pet.isEnemyDead) return pet;
            }
            return null;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Tip(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != GameNetworkManager.Instance?.localPlayerController || p.cursorTip == null || Riding.Horse != null || !Builder.CanAct(p)) return;
            var pet = Aimed(p);
            if (pet == null) return;
            string key = (p.currentlyHeldObjectServer as StackItem)?.ItemKey;
            if (pet.Tamed && pet.Owner == p.actualClientId) p.cursorTip.text = pet.Sitting ? "Stand up : [E]" : "Sit : [E]";
            else if (!pet.Tamed && key != null && pet.Food.Contains(key)) p.cursorTip.text = "Tame : [E]";
        }

        // ------------------------------------------------------------------ along in the ship (host)
        struct Passenger { public EnemyType Type; public ulong Owner; public bool Sitting; }
        static readonly List<Passenger> aboard = new List<Passenger>();
        static bool landedSeen;

        [HarmonyPatch(typeof(StartOfRound), "ShipLeave"), HarmonyPrefix]
        static void Board()
        {
            if (!BlockNet.IsServer || RoundManager.Instance == null) return;
            aboard.Clear();
            if (ShipPassengers.Enabled) return; // (#210: they stay alive and ride along instead)
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is PetAI pet && pet.Tamed && !pet.isEnemyDead && BlockWorld.InShip(pet.transform.position))
                    aboard.Add(new Passenger { Type = pet.enemyType, Owner = pet.Owner, Sitting = pet.Sitting });
                else if (e is GolemAI g && !g.isEnemyDead && BlockWorld.InShip(g.transform.position)) // (#140: a golem the crew built aboard)
                    aboard.Add(new Passenger { Type = g.enemyType, Owner = NoOwner });
            if (aboard.Count > 0) Plugin.Log.LogInfo($"[pets] {aboard.Count} aboard as the ship leaves");
        }

        /// <summary>Host, every frame (BlockWorld.Update): on the next landing, the pets that were aboard step out of the ship.</summary>
        public static void Tick()
        {
            var sor = StartOfRound.Instance;
            if (!BlockNet.IsServer || sor == null) return;
            bool landed = !sor.inShipPhase && sor.shipHasLanded;
            if (landed && !landedSeen && aboard.Count > 0)
            {
                var list = aboard.ToList(); aboard.Clear();
                foreach (var a in list)
                {
                    if (a.Type == null) continue;
                    var at = sor.shipInnerRoomBounds != null ? sor.shipInnerRoomBounds.bounds.center : sor.elevatorTransform.position;
                    at += new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-1.5f, 1.5f));
                    if (NavMesh.SamplePosition(at, out var h, 4f, NavMesh.AllAreas)) at = h.position;
                    MobSpawns.DevSpawnUntil = Time.time + 1f; // (back from the ship: not a natural spawn)
                    var r = RoundManager.Instance.SpawnEnemyGameObject(at, Random.Range(0f, 360f), -1, a.Type);
                    if (r.TryGet(out var no) && no.GetComponent<PetAI>() is PetAI pet)
                    {
                        pet.ServerTame(a.Owner, quiet: true);
                        pet.Sitting = a.Sitting;
                        BlockNet.ServerPet(pet);
                    }
                }
                Plugin.Log.LogInfo($"[pets] {list.Count} back with the ship");
            }
            landedSeen = landed;
        }

        public static void ResetSession() { aboard.Clear(); landedSeen = false; }
    }

    // ====================================================================================== the wolf
    [HarmonyPatch] // (Harmony's PatchAll skips a class without it: a tamed wolf never learned what its owner hit)
    public class WolfAI : PetAI
    {
        public const string Name = "Wolf";
        public static EnemyType Type;
        protected override string Sound => "wolf";
        protected override string[] Food => new[] { "bone" };
        protected override float ChaseSpeed => 5.5f;
        protected override float RoamSpeed => 1.6f;
        protected override int Damage => 15;
        EnemyAI fight;
        float biteAt;
        public static readonly Dictionary<ulong, (EnemyAI e, float t)> LastHitBy = new Dictionary<ulong, (EnemyAI, float)>();

        protected override void ServerDrops(Vector3 at) { }

        // (wild, it's neutral, Minecraft's: it goes only for whoever hit it)
        ulong angryAt = NoOwner;
        protected override bool Wants(PlayerControllerB p) => !Tamed && p != null && p.actualClientId == angryAt;

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (!Tamed && playerWhoHit != null) angryAt = playerWhoHit.actualClientId;
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        }

        /// <summary>Host: the monster to go for: one after its owner, or one its owner just hit.</summary>
        protected override bool TamedAI(PlayerControllerB owner)
        {
            if (fight == null || fight.isEnemyDead || Vector3.Distance(fight.transform.position, owner.transform.position) > 18f) fight = Pick(owner);
            if (fight == null) return false;
            agent.speed = ChaseSpeed;
            SetDestinationToPosition(fight.transform.position, checkForPath: false);
            if (Vector3.Distance(transform.position, fight.transform.position) < 1.9f && Time.time >= biteAt)
            {
                biteAt = Time.time + 1f;
                Sounds.Play("wolf.hit", transform.position + Vector3.up * 0.6f, 0.8f, Random.Range(0.9f, 1.1f), 16f);
                // (a bite for its owner: the monster's anger and the kill go to them)
                fight.HitEnemyOnLocalClient(1, transform.forward, owner, true, -1);
            }
            return true;
        }

        EnemyAI Pick(PlayerControllerB owner)
        {
            EnemyAI best = null; float bd = 14f;
            if (LastHitBy.TryGetValue(owner.actualClientId, out var lh) && lh.e != null && !lh.e.isEnemyDead && Time.time - lh.t < 10f && lh.e != this && !(lh.e is PetAI pp && pp.Tamed && pp.Owner == owner.actualClientId))
                return lh.e;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (e == null || e == this || e.isEnemyDead || !e.enemyType.canDie || e is PetAI || (e is MinecraftMobAI m && m.IsPassive)) continue;
                if (e.targetPlayer != owner) continue;
                float d = Vector3.Distance(e.transform.position, owner.transform.position);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.HitEnemy)), HarmonyPostfix]
        static void Noted(EnemyAI __instance, PlayerControllerB playerWhoHit)
        {
            if (playerWhoHit != null && BlockNet.IsServer && !(__instance is PetAI)) LastHitBy[playerWhoHit.actualClientId] = (__instance, Time.time);
        }

        public override void Receive(bool tamed, ulong owner, bool sitting)
        {
            base.Receive(tamed, owner, sitting);
            SetSkin(tamed ? tameMat : wildMat);
        }

        void SetSkin(Material m)
        {
            var model = transform.Find("Model");
            if (model == null || m == null) return;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = m;
        }

        static Material wildMat, tameMat;

        public static void Register()
        {
            if (Plugin.WolfRarity.Value <= 0) return;
            Type = Register<WolfAI>("Wolf", Name, "Tame it with a bone", Plugin.WolfRarity.Value, 4, 1.3f, 3, 1.6f, BuildModel, daytime: true, length: 1.3f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's wolf (64x32)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S;
            if (wildMat == null) wildMat = EntityModels.Material("LMC_Wolf", "entity/wolf/wolf", new Color(0.75f, 0.72f, 0.7f));
            // (#233: a tamed wolf wears Minecraft's red collar, entity/wolf/wolf_collar over its tamed coat)
            if (tameMat == null) tameMat = EntityModels.Material("LMC_WolfTame", EntityModels.Layered("entity/wolf/wolf_tame", "entity/wolf/wolf_collar", EntityModels.CollarRed), new Color(0.8f, 0.78f, 0.75f));
            var m = wildMat;
            EntityModels.Part(parent, "Head", new Vector3(0, 10.5f, 7), new Vector3(6, 6, 4), 0, 0, k, m);
            EntityModels.Part(parent, "EarL", new Vector3(2, 14.5f, 6.5f), new Vector3(2, 2, 1), 16, 14, k, m);
            EntityModels.Part(parent, "EarR", new Vector3(-2, 14.5f, 6.5f), new Vector3(2, 2, 1), 16, 14, k, m);
            EntityModels.Part(parent, "Nose", new Vector3(0, 9, 10), new Vector3(3, 3, 4), 0, 10, k, m);
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 10, -4.5f), new Vector3(6, 9, 6), 18, 14, k, m);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var mane = EntityModels.Part(parent, "Mane", new Vector3(0, 10.5f, 3), new Vector3(8, 6, 7), 21, 0, k, m);
            mane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tail = new GameObject("Tail").transform;
            tail.SetParent(parent, false);
            tail.localPosition = new Vector3(0, 12, -8) * k;
            tail.localRotation = Quaternion.Euler(36f, 0f, 0f);
            EntityModels.Part(tail, "Tail", new Vector3(0, -4, 0), new Vector3(2, 8, 2), 9, 18, k, m);
            foreach (var (n, x, z) in new[] { ("LegL", 1.5f, 4f), ("LegR", -1.5f, 4f), ("LegBL", 1.5f, -7f), ("LegBR", -1.5f, -7f) })
            {
                var h = new GameObject(n).transform;
                h.SetParent(parent, false);
                h.localPosition = new Vector3(x, 8, z) * k;
                EntityModels.Part(h, "Leg", new Vector3(0, -4, 0), new Vector3(2, 8, 2), 0, 18, k, m);
            }
        }
    }

    // ====================================================================================== the cat
    public class CatAI : PetAI
    {
        public const string Name = "Cat";
        public static EnemyType Type;
        protected override string Sound => "cat";
        protected override bool Passive => true;
        protected override string[] Food => new[] { "raw_cod", "raw_salmon", "tropical_fish" }; // (Minecraft's: fish, from fishing)
        protected override float RoamSpeed => 1.4f;
        protected override float PanicSpeed => 5.5f;
        protected override void ServerDrops(Vector3 at) // (Minecraft's: 0-2 string)
        {
            int n = Random.Range(0, 3);
            if (n > 0 && ModItems.ByKey.TryGetValue("string", out var s)) ModItems.ServerSpawnStack(s, n, at);
        }
        static readonly string[] Coats = { "tabby", "black", "red", "siamese", "british_shorthair", "calico", "persian", "ragdoll", "white", "jellie", "all_black" };
        static readonly Dictionary<string, Material> coats = new Dictionary<string, Material>();

        protected override bool TiltToSit => false;
        // (the parts as built, standing; the sitting pose moves them)
        readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> stand = new Dictionary<Transform, (Vector3, Quaternion)>();
        bool posedSitting;

        public override void Update()
        {
            base.Update();
            if (stand.Count == 0) return;
            if (posedSitting != Sitting)
            {
                posedSitting = Sitting;
                foreach (var kv in stand) { kv.Key.localPosition = kv.Value.pos; kv.Key.localRotation = kv.Value.rot; }
                if (Sitting) Sit();
            }
            else if (Sitting) Sit(legsOnly: true); // (the walk sets the legs every frame; a sitting cat keeps its pose)
        }

        /// <summary>Minecraft's sitting cat (AdultFelineModel.setupAnim, isSitting), part by part: the body tipped up at
        /// 45 degrees, the head up, the tail along the ground, the front legs straight, the hind legs folded forward under
        /// it. (Minecraft's model space: y down from 24, z backwards; here y up, z forwards.)</summary>
        void Sit(bool legsOnly = false)
        {
            var model = transform.Find("Model");
            if (model == null) return;
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            void Pose(string n, Vector3 pos, float xRot) { var t = model.Find(n); if (t != null) { t.localPosition = pos * k; t.localRotation = Quaternion.Euler(xRot, 0f, 0f); } }
            Pose("LegL", new Vector3(1.2f, 7.9f, 7f), -9f); Pose("LegR", new Vector3(-1.2f, 7.9f, 7f), -9f);
            Pose("LegBL", new Vector3(1.1f, 3f, -1f), -90f); Pose("LegBR", new Vector3(-1.1f, 3f, -1f), -90f);
            if (legsOnly) return;
            Pose("Body", new Vector3(0f, 4.686f, 0.757f), 45f); // (the box's centre, turned about Minecraft's pivot, raised 4 and 5 forward)
            Pose("Tail", new Vector3(0f, 1f, -6f), 99f);
            var t2 = model.Find("Tail/Tail2"); if (t2 != null) t2.localRotation = Quaternion.Euler(54f, 0f, 0f);
            foreach (var n in new[] { "Head", "Nose", "EarL", "EarR" }) { var t = model.Find(n); if (t != null) t.localPosition += new Vector3(0f, 3.3f, -1f) * k; }
        }

        public override void Start()
        {
            base.Start();
            ApplyLook();
            var model = transform.Find("Model");
            if (model != null)
                foreach (var n in new[] { "Head", "Nose", "EarL", "EarR", "Body", "Tail", "Tail/Tail2", "LegL", "LegR", "LegBL", "LegBR" })
                { var t = model.Find(n); if (t != null) stand[t] = (t.localPosition, t.localRotation); }
        }

        protected override void ApplyLook()
        {
            // (each cat its own coat, the same on every machine; tamed, Minecraft's red collar over it, #233)
            var coat = Coats[(int)(LookId % (ulong)Coats.Length)];
            string mk = Tamed ? coat + "_tame" : coat;
            if (!coats.TryGetValue(mk, out var m))
                coats[mk] = m = Tamed ? EntityModels.Material("LMC_Cat_" + mk, EntityModels.Layered("entity/cat/" + coat, "entity/cat/cat_collar", EntityModels.CollarRed), new Color(0.6f, 0.45f, 0.3f))
                                      : EntityModels.Material("LMC_Cat_" + coat, "entity/cat/" + coat, new Color(0.6f, 0.45f, 0.3f));
            var model = transform.Find("Model");
            if (model != null) foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = m;
        }

        public override void Receive(bool tamed, ulong owner, bool sitting)
        {
            base.Receive(tamed, owner, sitting);
            ApplyLook(); // (#233: the collar on, or off; on the host Tamed is set before this comes, so no "was it" check)
        }

        /// <summary>A cat within this many metres of a point (creepers keep away: Minecraft's).</summary>
        public static CatAI Near(Vector3 pos, float r)
        {
            if (RoundManager.Instance == null) return null;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e is CatAI c && !c.isEnemyDead && Vector3.Distance(c.transform.position, pos) < r) return c;
            return null;
        }

        public static void Register()
        {
            if (Plugin.CatRarity.Value <= 0) return;
            Type = Register<CatAI>("Cat", Name, "Tame it with raw meat. Creepers fear it", Plugin.CatRarity.Value, 2, 0.8f, 3, 1.4f, BuildModel, daytime: true, length: 1.0f);
        }

        // ------------------------------------------------------------------ the model: Minecraft's cat (64x32, drawn at 0.8)
        static void BuildModel(Transform parent)
        {
            float k = EntityModels.Px * BlockWorld.S * 0.8f;
            var m = EntityModels.Material("LMC_Cat_tabby", "entity/cat/tabby", new Color(0.6f, 0.45f, 0.3f));
            EntityModels.Part(parent, "Head", new Vector3(0, 9, 9.5f), new Vector3(5, 4, 5), 0, 0, k, m);
            EntityModels.Part(parent, "Nose", new Vector3(0, 8, 12), new Vector3(3, 2, 2), 0, 24, k, m);
            EntityModels.Part(parent, "EarL", new Vector3(1.5f, 11.5f, 8), new Vector3(1, 1, 2), 6, 10, k, m);
            EntityModels.Part(parent, "EarR", new Vector3(-1.5f, 11.5f, 8), new Vector3(1, 1, 2), 0, 10, k, m);
            var body = EntityModels.Part(parent, "Body", new Vector3(0, 7, -1), new Vector3(4, 16, 6), 20, 0, k, m);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var t1 = new GameObject("Tail").transform;
            t1.SetParent(parent, false);
            t1.localPosition = new Vector3(0, 9, -8) * k;
            t1.localRotation = Quaternion.Euler(52f, 0f, 0f);
            EntityModels.Part(t1, "Tail1", new Vector3(0, -4, 0), new Vector3(1, 8, 1), 0, 15, k, m);
            var t2 = new GameObject("Tail2").transform;
            t2.SetParent(t1, false);
            t2.localPosition = new Vector3(0, -8, 0) * k;
            t2.localRotation = Quaternion.Euler(45f, 0f, 0f);
            EntityModels.Part(t2, "Tail2", new Vector3(0, -4, 0), new Vector3(1, 8, 1), 4, 15, k, m);
            foreach (var (n, x, y, z, h, u, v) in new[] { ("LegL", 1.2f, 9.9f, 5f, 10f, 40, 0), ("LegR", -1.2f, 9.9f, 5f, 10f, 40, 0), ("LegBL", 1.1f, 6f, -5f, 6f, 8, 13), ("LegBR", -1.1f, 6f, -5f, 6f, 8, 13) })
            {
                var hip = new GameObject(n).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, y, z) * k;
                // (Minecraft's boxes: the front legs 0..2 px behind the hip, the hind legs 1..3)
                EntityModels.Part(hip, "Leg", new Vector3(0, -h / 2f, n.StartsWith("LegB") ? -2f : -1f), new Vector3(2, h, 2), u, v, k, m);
            }
        }
    }
}
