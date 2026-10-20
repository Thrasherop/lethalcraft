using System;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// A Minecraft melee mob inside the facility (#59): the wither skeleton, the zombie. It roams, goes for the nearest
    /// player it sees and hits whoever it walks into; hit, it flashes red and turns on whoever hit it. Lit up places keep
    /// it from spawning, like the creeper. Each kind gives its numbers, sounds ("&lt;Sound&gt;.idle/.hurt/.death/.hit"),
    /// drops and model; the AI, the walk and the swing are shared.
    /// </summary>
    public abstract class MinecraftMobAI : EnemyAI
    {
        /// <summary>Host: Start took it away again (a light, natural spawns off): a subclass's Start does nothing more.</summary>
        protected bool RemovedAtStart;

        // ------------------------------------------------------------------ (#227) kept with the ship
        public bool IsPassive => Passive;
        ulong lookId;
        /// <summary>The number its looks are picked by (coat, markings, feathers): its network id, or, brought back from a
        /// save, the one it had then, so it comes back looking the same.</summary>
        public ulong LookId => lookId != 0 ? lookId : NetworkObjectId;

        /// <summary>(#244) Minecraft's horse and llama health: 15, and 0-7 and 0-8 at random (15-30), over 5: each its own
        /// from its LookId (the same on every machine). Called from its looks: health moves by the difference, any harm kept.</summary>
        protected void LookHealth(int registered)
        {
            ulong h = LookId * 0xD6E8FEB86659FD93UL;
            int hp = Mathf.RoundToInt((15 + (int)(h % 8UL) + (int)((h >> 8) % 9UL)) / 5f);
            if (lookHp == 0) lookHp = registered;
            if (hp != lookHp) { enemyHP += hp - lookHp; lookHp = hp; }
        }
        int lookHp;
        public void SetLook(ulong id) { if (id == 0 || id == lookId) return; lookId = id; ApplyLook(); }
        /// <summary>Its looks from LookId (on spawn, and again when a saved one arrives).</summary>
        protected virtual void ApplyLook() { }
        /// <summary>Host: what it keeps when saved with the ship (tamed, sheared, saddled...), or null.</summary>
        public virtual string SaveState() => null;
        /// <summary>Host: the same, given back after it's spawned again from the save.</summary>
        public virtual void LoadState(string s) { }
        /// <summary>Host: its own state to a player who just joined (#227: a sheep's wool, a llama's chest).</summary>
        public virtual void ServerSyncTo(ulong client) { }
        /// <summary>The chest it carries (a chest boat's, a chest minecart's, a llama's), if any.</summary>
        public virtual BlockKey? Storage => null;
        public const int Roam = 0, Chase = 1;
        protected abstract string Sound { get; }
        protected virtual float SightRange => 20f;
        /// <summary>Its height in blocks where that matters: a gap under placed blocks lower than this stops it (#112).</summary>
        protected virtual float TallBlocks => 0f;
        public static int LowGapStops; // (dev/tests)
        public static bool DevLowGapsOff; // (dev: the check off, to compare)
        protected virtual float ChaseSpeed => 4.7f; // (#100: a walking player does 4.55 m/s; they keep up)
        protected virtual float RoamSpeed => 2.0f;
        protected virtual int Damage => 25;
        /// <summary>Arms held out in front (Minecraft's zombie) instead of swinging at its sides.</summary>
        protected virtual bool ArmsForward => false;
        /// <summary>Host: what it drops when killed.</summary>
        protected abstract void ServerDrops(Vector3 at);
        /// <summary>Experience for whoever kills it (#139: Minecraft's, 5 for a monster, 1-3 for an animal).</summary>
        protected virtual int Xp => Passive ? UnityEngine.Random.Range(1, 4) : 5 + EquipmentXp();
        /// <summary>(#244) How many things it holds or wears (Minecraft's: 1-3 more XP each).</summary>
        protected virtual int Equipment => 0;
        int EquipmentXp() { int x = 0; for (int i = 0; i < Equipment; i++) x += UnityEngine.Random.Range(1, 4); return x; }

        /// <summary>(#244) Host, in its drops: Minecraft's equipment drop (dropCustomDeathLoot): a player's kill, 8.5% and 1%
        /// a Looting level; badly worn, at random (its uses left 1 + a random number under a random number under its uses).</summary>
        protected void ServerDropEquipment(string key, int maxUses, Vector3 at)
        {
            if (!KilledByPlayer || UnityEngine.Random.value - ModItems.LootingBonus * 0.01f >= 0.085f) return;
            int left = 1 + UnityEngine.Random.Range(0, 1 + UnityEngine.Random.Range(0, Mathf.Max(maxUses - 3, 1)));
            int used = Mathf.Clamp(maxUses - left, 0, maxUses - 1);
            ModItems.ServerSpawnPlainKeyedOrStack(used > 0 ? ItemData.With(key, Enchants.Data(used, 0)) : key, at);
            EquipmentDropped++;
        }
        public static int EquipmentDropped; // (dev/tests)
        /// <summary>Host, as it spawns naturally: another kind to become instead (a husk on a desert moon, a baby, a jockey:
        /// Variants), or null.</summary>
        protected virtual EnemyType ReplaceWith() => null;
        /// <summary>(#244) Its spawn variants happen in the Nether too (a zombified piglin's babies).</summary>
        protected virtual bool VariesInNether => false;
        /// <summary>Every client, for its own player: just hit them (the husk's Hunger).</summary>
        protected virtual void OnHitPlayer(PlayerControllerB p) { }
        /// <summary>The head a charged creeper's blast knocks off it (#142), which, worn, halves how far it notices you.</summary>
        protected virtual string HeadKey => null;
        /// <summary>A water creature (the squid, #150): no navmesh; SwimAI and its own Update move it.</summary>
        protected virtual bool Swims => false;
        /// <summary>(#244) Host, while it drops its loot: a player killed it (Minecraft's killed_by_player drops: a spider's eye, a
        /// blaze rod, a phantom's membrane, a rabbit's foot, tipped arrows, a wither skull, a zombie's rare drops).</summary>
        protected bool KilledByPlayer;
        /// <summary>(#247) In water deeper than it's tall it floats up, its head out, as Minecraft's land mobs do (FloatGoal);
        /// its agent goes on along the bottom under it. Not a mob that sets its own height (a ghast, a blaze), nor a drowned.</summary>
        protected virtual bool FloatsInWater => !Swims;
        float floatLift;
        void FloatInWater()
        {
            if (agent == null || !agent.enabled) return;
            var foot = transform.position - Vector3.up * floatLift;
            float want = Mathf.Max(0f, MoonWater.Depth(foot) - agent.height * 0.75f);
            float next = Mathf.MoveTowards(floatLift, want, Time.deltaTime * 2f);
            if (Mathf.Approximately(next, floatLift)) return;
            agent.baseOffset += next - floatLift;
            floatLift = next;
        }
        /// <summary>(#244) Water hurts it (Minecraft's isSensitiveToWater: a blaze, a snow golem, an enderman): a hit every
        /// half second it's in it, about Minecraft's 2 health a second over its i-frames, over 5.</summary>
        protected virtual bool WaterHurts => false;
        float wetHurt;
        public static int WaterHurtHits; // (dev/tests)
        /// <summary>(#244) In water (the moon's, or placed): it is.</summary>
        public bool InWater
        {
            get
            {
                var p = transform.position + Vector3.up * 0.3f * BlockWorld.S;
                return MoonWater.Depth(p) > 0.05f || (WaterSwim.FluidCellAt(p, out var b) && b.Data.Def == Blocks.Water);
            }
        }
        /// <summary>(#244) Host, as water hurts it: an enderman teleports away.</summary>
        protected virtual void OnWaterHurt() { }
        void WaterHurt()
        {
            if (!InWater) { wetHurt = 0f; return; }
            wetHurt += Time.deltaTime * 0.4f;
            if (wetHurt < 1f) return;
            wetHurt -= 1f; WaterHurtHits++;
            HitEnemyOnLocalClient(1, Vector3.up, null, true, -1);
            OnWaterHurt();
        }

        /// <summary>It moves itself, off any navmesh (a swimmer, a phantom, a ghast in the air): its agent stays off.</summary>
        public virtual bool OffNavMesh => Swims || RidingCart != null;
        /// <summary>(#244) In a minecart (Minecraft's: a moving empty cart picks a mob up): the cart moves it, its own AI waits.</summary>
        public MinecartAI RidingCart;
        public virtual bool MayRideCart => !Swims && !(this is MinecartAI) && !(this is GolemAI) && !(this is HorseAI) && !(this is BoatAI) && OffNavMesh == false;
        protected virtual void SwimAI() { }

        // ------------------------------------------------------------------ [E] with something in hand: breeding, shearing (AnimalCare)
        /// <summary>A baby (its own enemy type, registered with RegisterBaby): grows up after AnimalCare.GrowSeconds.</summary>
        public bool IsBaby => enemyType != null && AnimalCare.AdultOf.ContainsKey(enemyType);
        float loveUntil = -1f, breedAgain = -1f, bornAt;
        /// <summary>What [E] does with this in hand (the tip shown), or null: nothing. Base: its food breeds it.</summary>
        public virtual string UseTip(string heldKey)
        {
            if (LeashedTo != NoLeash && LeashedTo == NetworkManager.Singleton?.LocalClientId) return "Unleash";
            if (heldKey == "lead" && Passive && LeashedTo == NoLeash && !Swims) return "Leash";
            // (#244) a spawn egg of its own kind on an adult: a baby, Minecraft's
            if (heldKey != null && heldKey.StartsWith(AnimalCare.EggPrefix) && !IsBaby && AnimalCare.BabyOf.ContainsKey(enemyType) && int.TryParse(heldKey.Substring(AnimalCare.EggPrefix.Length), out int eh) && SpawnEggs.ByHash(eh) == enemyType) return "Spawn a baby";
            // (#244: a baby too: it grows up sooner)
            return Passive && IsFood(heldKey) && (IsBaby || AnimalCare.BabyOf.ContainsKey(enemyType)) ? "Feed" : null;
        }

        /// <summary>Host: someone pressed [E] on it holding this.</summary>
        public virtual void ServerUse(PlayerControllerB p, GrabbableObject held)
        {
            var st = held as StackItem;
            // (#145: a lead: on, or off again)
            if (LeashedTo == p.actualClientId) { ServerUnleash(true); return; }
            if (st != null && st.ItemKey == "lead" && Passive && LeashedTo == NoLeash && !Swims)
            {
                st.ServerUseUp(p);
                LeashedTo = p.actualClientId;
                BlockNet.ServerMobState(NetworkObjectId, 200, (byte)p.playerClientId);
                BlockNet.ServerSound(transform.position + Vector3.up, "lead.tie", 0.7f, 1f);
                return;
            }
            if (held is SpawnEggItem egg && !IsBaby && egg.Enemy == enemyType && AnimalCare.BabyOf.TryGetValue(enemyType, out var babyType))
            {
                // (#244) Minecraft's: a spawn egg on an adult of its kind makes a baby beside it
                MobSpawns.DevSpawnUntil = Time.time + 1f;
                RoundManager.Instance.SpawnEnemyGameObject(transform.position + transform.right * 0.6f, UnityEngine.Random.Range(0f, 360f), -1, babyType);
                if (!GameModes.IsCreative(p.actualClientId)) BlockNet.ServerTakeHeld(egg);
                BabiesFromEggs++;
                return;
            }
            if (st == null || UseTip(st.ItemKey) != "Feed") return;
            if (IsBaby)
            {
                // (#244) Minecraft's ageUp: a tenth of the time left goes
                bornAt -= Mathf.Max(0f, AnimalCare.GrowSeconds - (Time.time - bornAt)) * 0.1f;
                st.ServerUseUp(p); BabiesFed++;
                BlockNet.ServerSound(transform.position + Vector3.up, "eat", 0.7f, 1.4f);
                return;
            }
            ServerFeedToBreed(p, st);
        }

        /// <summary>Host: fed its breeding food: in love for half a minute; with another of its kind in love near it, a baby.</summary>
        protected void ServerFeedToBreed(PlayerControllerB p, StackItem st)
        {
            if (st == null || IsBaby || !AnimalCare.BabyOf.ContainsKey(enemyType) || Time.time < breedAgain || Time.time < loveUntil) return;
            st.ServerUseUp(p);
            BlockNet.ServerSound(transform.position + Vector3.up, "eat", 0.7f, 1.2f);
            loveUntil = Time.time + 30f; // (Minecraft's love mode: half a minute to find a partner)
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
                if (e != this && e is MinecraftMobAI m && m.enemyType == enemyType && !m.isEnemyDead && m.loveUntil > Time.time && Vector3.Distance(m.transform.position, transform.position) < 8f)
                {
                    loveUntil = m.loveUntil = -1f;
                    breedAgain = m.breedAgain = Time.time + 300f; // (Minecraft's five minutes)
                    AnimalCare.ServerBreed(this, m, p);
                    break;
                }
        }

        /// <summary>Every client: state the host sent (MobState).</summary>
        public virtual void ReceiveState(byte a, byte b) { }
        /// <summary>Every client: a MobState message (200: the leash, b = the holder's player index or 255; else the kind's own).</summary>
        public void ReceiveMobState(byte a, byte b)
        {
            if (a == 202)
            {
                // (on a trader's lead: the nearest trader)
                LeashedTo = TraderLeash;
                LeashMob = RoundManager.Instance.SpawnedEnemies.Where(e => e is WanderingTraderAI && !e.isEnemyDead).OrderBy(e => Vector3.Distance(e.transform.position, transform.position)).FirstOrDefault();
                if (leash == null)
                {
                    var lg = new GameObject("Leash"); lg.transform.SetParent(transform, false);
                    leash = lg.AddComponent<LineRenderer>();
                    leash.positionCount = 2; leash.startWidth = leash.endWidth = 0.05f;
                    leash.material = AnimalCare.LeashMaterial; leash.startColor = leash.endColor = new Color(0.45f, 0.3f, 0.15f);
                }
                leash.enabled = LeashMob != null;
                return;
            }
            if (a != 200 && a != 201) { ReceiveState(a, b); return; }
            var holder = a == 200 && b < 255 && b < StartOfRound.Instance.allPlayerScripts.Length ? StartOfRound.Instance.allPlayerScripts[b] : null;
            LeashedTo = holder != null ? holder.actualClientId : a == 201 ? FenceLeash : NoLeash;
            leashHolder = holder;
            // (201: tied to a fence, #145: the nearest fence post it's by)
            knot = null;
            if (a == 201 && BlockWorld.Instance != null)
            {
                float best = 12f * BlockWorld.S;
                foreach (var kv in BlockWorld.Instance.Blocks)
                {
                    if (kv.Value.Data.Def.Shape != BlockShape.Fence) continue;
                    var c = BlockWorld.Instance.WorldCenter(kv.Key);
                    float d = Vector3.Distance(c, transform.position);
                    if (d < best) { best = d; knot = c + Vector3.up * (BlockWorld.S * 0.25f); }
                }
            }
            if (leash == null && (holder != null || knot != null))
            {
                var go = new GameObject("Leash"); go.transform.SetParent(transform, false);
                leash = go.AddComponent<LineRenderer>();
                leash.positionCount = 2; leash.startWidth = leash.endWidth = 0.05f;
                leash.material = AnimalCare.LeashMaterial; leash.startColor = leash.endColor = new Color(0.45f, 0.3f, 0.15f);
            }
            if (leash != null) leash.enabled = holder != null || knot != null;
        }

        // ------------------------------------------------------------------ a lead (#145): it follows whoever holds it
        public const ulong NoLeash = ulong.MaxValue;
        /// <summary>(LeashedTo) tied to a fence post (#145, Minecraft's): <see cref="TiedFence"/>.</summary>
        public const ulong FenceLeash = ulong.MaxValue - 1;
        /// <summary>(LeashedTo) on a mob's lead (#152: a wandering trader's llamas): <see cref="LeashMob"/>.</summary>
        public const ulong TraderLeash = ulong.MaxValue - 2;
        public EnemyAI LeashMob;
        public ulong LeashedTo = NoLeash;
        public BlockKey TiedFence;
        Vector3? knot;
        PlayerControllerB leashHolder;
        LineRenderer leash;

        /// <summary>Host: off the lead (dropped where it is, or broken when pulled too far).</summary>
        public void ServerUnleash(bool dropLead)
        {
            if (LeashedTo == NoLeash) return;
            LeashedTo = NoLeash;
            if (dropLead && ModItems.ByKey.TryGetValue("lead", out var l)) ModItems.ServerSpawnStack(l, 1, transform.position + Vector3.up);
            BlockNet.ServerMobState(NetworkObjectId, 200, 255);
        }

        /// <summary>Host, each AI step: led, it walks after its holder; pulled more than 10 blocks away, the lead snaps.</summary>
        bool ServerLeash()
        {
            if (LeashedTo == TraderLeash)
            {
                // following its trader; the trader gone, the lead breaks
                if (LeashMob == null || LeashMob.isEnemyDead) { ServerUnleash(false); return false; }
                if (agent == null || !agent.isOnNavMesh) return true;
                if (currentSearch.inProgress) StopSearch(currentSearch);
                float dm = Vector3.Distance(LeashMob.transform.position, transform.position);
                agent.speed = Mathf.Max(RoamSpeed, 2.5f);
                if (dm > 3f) SetDestinationToPosition(LeashMob.transform.position);
                else if (agent.hasPath) agent.ResetPath();
                return true;
            }
            if (LeashedTo == FenceLeash)
            {
                // tied to a fence: it stays within a few blocks of it; the fence gone, the lead drops
                var w = BlockWorld.Instance;
                if (w == null || w.DefAt(TiedFence)?.Shape != BlockShape.Fence) { ServerUnleash(true); return false; }
                var post = w.WorldCenter(TiedFence);
                if (agent == null || !agent.isOnNavMesh) return true;
                if (currentSearch.inProgress) StopSearch(currentSearch);
                if (Vector3.Distance(post, transform.position) > 3f * BlockWorld.S) SetDestinationToPosition(post);
                else if (agent.hasPath && UnityEngine.Random.value < 0.05f) agent.ResetPath();
                return true;
            }
            var h = ServerLogic.PlayerFor(LeashedTo);
            if (h == null || h.isPlayerDead || !h.isPlayerControlled) { ServerUnleash(true); return false; }
            float d = Vector3.Distance(h.transform.position, transform.position);
            if (d > 10f * BlockWorld.S) { ServerUnleash(true); return false; }
            if (agent == null || !agent.isOnNavMesh) return true;
            if (currentSearch.inProgress) StopSearch(currentSearch);
            agent.speed = Mathf.Max(RoamSpeed, 4.6f);
            if (d > 3f) SetDestinationToPosition(h.transform.position);
            else if (agent.hasPath) agent.ResetPath();
            return true;
        }

        /// <summary>A baby kind of an adult (its model built at half size), for breeding.</summary>
        protected static EnemyType RegisterBaby<T>(EnemyType adult, string id, string name, int hp, float height, float roamSpeed, Action<Transform> buildModel, bool daytime = true, float length = 0.5f) where T : MinecraftMobAI
        {
            if (adult == null) return null;
            var baby = Register<T>(id, name, "A baby: it grows up", 0, hp, height, 4, roamSpeed, buildModel, daytime, length);
            AnimalCare.BabyOf[adult] = baby; AnimalCare.AdultOf[baby] = adult;
            return baby;
        }
        bool dropped;
        /// <summary>Whether it goes for this player at all (the piglin leaves those in gold armor alone).</summary>
        protected virtual bool Wants(PlayerControllerB p) => true;
        /// <summary>Owner, each AI step: true while it's doing something of its own (the piglin with gold); the roam and chase wait.</summary>
        protected virtual bool BusyAI() => false;
        /// <summary>A farm animal (#95): never goes for anyone; hit, it runs off for a few seconds. Lit places don't stop it spawning.</summary>
        protected virtual bool Passive => false;
        public string SoundKey => Sound;
        /// <summary>A lit place stops it spawning (Minecraft's monsters); not animals or pets.</summary>
        protected virtual bool HatesLight => !Passive;
        protected virtual float PanicSpeed => 5f;
        /// <summary>Stuck against blocks, it gnaws through them (EnemiesBreakBlocks); farm animals and pets don't.</summary>
        public virtual bool GnawsBlocks => !Passive;
        /// <summary>What a passive animal follows when someone holds it (Minecraft's pig: a carrot; cow: wheat).</summary>
        protected virtual string Tempt => null;
        /// <summary>(#244) Every food Minecraft's animal takes (its tempt and breed items); base: its Tempt.</summary>
        protected virtual string[] Foods => Tempt != null ? new[] { Tempt } : null;
        public bool IsFood(string key) { var f = Foods; return key != null && f != null && System.Array.IndexOf(f, ItemData.TypeOf(key)) >= 0; }
        public static int BabiesFed, BabiesFromEggs; // (dev/tests)
        float panicUntil;
        PlayerControllerB panicFrom;

        Transform model;
        protected Transform armR, armL;
        readonly Transform[] legs = new Transform[4]; // (a four-legged animal's back legs: LegBL, LegBR)
        float walkPhase, swing, hitCooldown, idleIn = 6f, hurtFlash, stepNoiseIn;
        Vector3 lastPos;
        // the hurt flash (#189): the whole body tinted red, as Minecraft's overlay; not what it holds (Minecraft draws held
        // items without it). A property block on each part, so a model whose materials change later (a coat, a creeper's
        // swell) is tinted all the same, and the materials themselves are left alone.
        static MaterialPropertyBlock hurtBlock;
        Renderer[] hurtParts;
        bool hurtShown;
        public int HurtTinted { get; private set; } // (dev: the parts tinted just now)

        public override void Start()
        {
            // (#101, #122: spawned from a runtime prefab it inherited HideAndDontSave, which hides it from the game's
            // FindObjectsOfType: the round's end never despawned it, and it came along to the next moon or the Company)
            SpawnFix.Clear(gameObject);
            // (#245) the game's land monster that hasn't moved for 15 s counts as stuck "swimming", and 10 s later is put on
            // the nearest navmesh within 8 m. A boat (which moves itself, and syncs where it is every frame: 0.2 s on that
            // clock each time) got put on the lake's bed or its bank a moment after it slowed down, rider and all
            if (Swims && enemyType.WaterType == EnemyWaterType.LandOnly) enemyType.WaterType = EnemyWaterType.Amphibious;
            base.Start();
            model = transform.Find("Model");
            if (model != null) { armR = model.Find("ArmR"); armL = model.Find("ArmL"); legs[0] = model.Find("LegL"); legs[1] = model.Find("LegR"); legs[2] = model.Find("LegBL"); legs[3] = model.Find("LegBR"); }
            // in the Nether fortress: its own nodes to wander between (the game's are up in the facility)
            if (NetherFortress.Contains(transform.position) && NetherFortress.Nodes != null) allAINodes = NetherFortress.Nodes;
            // (a swimmer moves itself through its water, off any navmesh: the game's AI tick would otherwise ask its agent
            // for a path every interval, logging "Agent not on nav mesh" each time, thousands on March)
            if (Swims) { if (agent != null) agent.enabled = false; moveTowardsDestination = false; }
            if (!IsServer) return;
            bool restored = MobSpawns.IsRestored(this); // (#227)
            if (MobSpawns.DevNoNatural && Time.time > MobSpawns.DevSpawnUntil && !restored) { RemovedAtStart = true; KillEnemyOnOwnerClient(overrideDestroy: true); return; }
            if (HatesLight && !restored && MobSpawns.LightStops(transform.position, out string light)) // (#227: not one brought back aboard)
            {
                Plugin.Log.LogInfo($"[mobs] a {enemyType.enemyName} spawned {light}: removed");
                MobSpawns.Prevented++;
                RemovedAtStart = true;
                KillEnemyOnOwnerClient(overrideDestroy: true);
                return;
            }
            // (a natural spawn only: a dev-spawned mob stays what it was asked to be; none in the Nether)
            bool natural = (Time.time > MobSpawns.DevSpawnUntil || Time.time <= MobSpawns.DevNaturalUntil) && !restored;
            var other = natural && (VariesInNether || !NetherFortress.Contains(transform.position)) ? ReplaceWith() : null;
            if (other != null && other != enemyType) { Variants.Replace(this, other); return; }
            bornAt = Time.time;
            if (Swims) return;
            agent.speed = RoamSpeed;
            Stroll();
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;
            if (RidingCart != null) { if (RidingCart.isEnemyDead) RidingCart = null; else return; } // (#244: riding a minecart)
            // (on a lead: it follows its holder)
            if (IsServer && LeashedTo != NoLeash && ServerLeash()) return;
            // (a baby grows up: the grown-up takes its place)
            if (IsServer && IsBaby && Time.time > bornAt + AnimalCare.GrowSeconds && AnimalCare.AdultOf.TryGetValue(enemyType, out var grown)) { Variants.Replace(this, grown); return; }
            if (IsServer && MobFoeAI()) return; // (#244: a mob that hurt it, first)
            if (BusyAI()) return;
            if (Swims) { SwimAI(); return; }
            if (Passive) { PassiveAI(); return; }
            switch (currentBehaviourStateIndex)
            {
                case Roam:
                    {
                        // (Minecraft's mobs notice a player in any direction, given a clear line of sight)
                        var seen = CheckLineOfSightForClosestPlayer(360f, (int)SightRange, 6);
                        // (wearing its head, it notices you from only half as far: Minecraft's)
                        if (seen != null && HeadKey != null && MobHeads.Wears(seen, HeadKey) && Vector3.Distance(seen.transform.position, transform.position) > SightRange * 0.5f) seen = null;
                        if (seen != null && PlayerIsTargetable(seen) && Wants(seen))
                        {
                            if (currentSearch.inProgress) StopSearch(currentSearch);
                            targetPlayer = seen; agent.speed = ChaseSpeed;
                            SwitchToBehaviourState(Chase);
                        }
                        else Wander();
                        break;
                    }
                case Chase:
                    if (targetPlayer == null || !PlayerIsTargetable(targetPlayer) || !Wants(targetPlayer) || Vector3.Distance(transform.position, targetPlayer.transform.position) > SightRange * 1.5f)
                    {
                        targetPlayer = null; movingTowardsTargetPlayer = false; agent.speed = RoamSpeed;
                        SwitchToBehaviourState(Roam);
                        Wander();
                        break;
                    }
                    SetMovingTowardsTargetPlayer(targetPlayer);
                    if (TallBlocks > 0f && !DevLowGapsOff) KeepOutOfLowGaps();
                    break;
            }
        }

        // ------------------------------------------------------------------ (#244) mobs fighting each other
        // Minecraft's HurtByTargetGoal: a monster hurt by another (a skeleton's stray arrow) goes for that one a while: up
        // to it and hits it, or (a skeleton) shoots it.
        EnemyAI mobFoe; float mobFoeUntil, mobHitAt;
        public static int Infights; // (dev/tests)
        public EnemyAI MobFoe => mobFoe;

        /// <summary>Host: another mob hurt it.</summary>
        public void ServerAngerAt(EnemyAI attacker)
        {
            if (Passive || attacker == null || attacker == this || attacker.isEnemyDead || this is PetAI) return;
            if (mobFoe != attacker) Infights++;
            mobFoe = attacker; mobFoeUntil = Time.time + 20f;
        }

        bool MobFoeAI()
        {
            if (mobFoe == null) return false;
            float d = Vector3.Distance(mobFoe.transform.position, transform.position);
            if (mobFoe.isEnemyDead || Time.time > mobFoeUntil || d > 24f * BlockWorld.S) { mobFoe = null; return false; }
            if (currentSearch.inProgress) StopSearch(currentSearch);
            if (ShootAtMob(mobFoe, d)) return true;
            agent.speed = ChaseSpeed;
            SetDestinationToPosition(mobFoe.transform.position, checkForPath: false);
            if (d < 2f && Time.time >= mobHitAt)
            {
                mobHitAt = Time.time + 1f;
                mobFoe.HitEnemyOnLocalClient(1, (mobFoe.transform.position - transform.position).normalized, null, true, -1);
                if (mobFoe is MinecraftMobAI back) back.ServerAngerAt(this);
            }
            return true;
        }

        /// <summary>(#244) Host: a shooter's way with a mob foe (a skeleton's arrows); false: walk up and hit it.</summary>
        protected virtual bool ShootAtMob(EnemyAI foe, float dist) => false;

        // ------------------------------------------------------------------ wandering, Minecraft's (#172)
        /// <summary>Owner: Minecraft's stroll (RandomStrollGoal, DefaultRandomPos): standing about, now and then (a 1 in 60
        /// chance every other tick: about every 6 seconds) it picks a spot up to 10 blocks off and 7 up or down that it
        /// can walk to, walks there, and stands about again. (The game's search had walked it from node to node across
        /// the whole map, never stopping.)</summary>
        protected void Stroll() => Strolling.Step(this);
        /// <summary>Host, each AI step with nobody to go for: Minecraft's stroll; a pillager walks its patrol (#223).</summary>
        protected virtual void Wander() => Stroll();

        /// <summary>Owner: a farm animal's day: wander; run from whoever hit it; walk after someone holding its food.</summary>
        void PassiveAI()
        {
            if (Time.time < panicUntil)
            {
                if (!agent.hasPath || agent.remainingDistance < 1f)
                {
                    // (away from who hit it, somewhere it can walk to)
                    var away = transform.position - (panicFrom != null ? panicFrom.transform.position : transform.position - transform.forward);
                    away.y = 0f;
                    var to = transform.position + Quaternion.Euler(0f, UnityEngine.Random.Range(-50f, 50f), 0f) * away.normalized * 7f;
                    if (NavMesh.SamplePosition(to, out var h, 4f, agent.areaMask)) SetDestinationToPosition(h.position);
                }
                return;
            }
            if (agent.speed > RoamSpeed) agent.speed = RoamSpeed;
            var lure = Foods != null ? Lured() : null;
            Following = lure;
            if (lure != null)
            {
                if (currentSearch.inProgress) StopSearch(currentSearch);
                // (follows to a couple of metres off, as Minecraft's do)
                if (Vector3.Distance(transform.position, lure.transform.position) > 2.2f) SetDestinationToPosition(lure.transform.position, checkForPath: true);
                else SetDestinationToPosition(transform.position);
                return;
            }
            // (#244) Minecraft's FollowParentGoal: a baby keeps near a grown-up of its kind within 8 blocks
            if (IsBaby && AnimalCare.AdultOf.TryGetValue(enemyType, out var adultType))
            {
                MinecraftMobAI parent = null; float pd = 8f * BlockWorld.S;
                foreach (var e in RoundManager.Instance.SpawnedEnemies)
                    if (e is MinecraftMobAI a && a.enemyType == adultType && !a.isEnemyDead) { float d = Vector3.Distance(a.transform.position, transform.position); if (d < pd) { pd = d; parent = a; } }
                Following = null; FollowingParent = parent;
                if (parent != null && pd > 3f * BlockWorld.S) { if (currentSearch.inProgress) StopSearch(currentSearch); SetDestinationToPosition(parent.transform.position, checkForPath: true); return; }
            }
            Stroll();
        }

        /// <summary>(dev/tests) Who a passive animal is following for their food.</summary>
        public PlayerControllerB Following;
        public MinecraftMobAI FollowingParent; // (dev/tests)
        public bool Panicking => Time.time < panicUntil;

        /// <summary>The nearest player within 8 m holding its food, if any.</summary>
        PlayerControllerB Lured()
        {
            PlayerControllerB best = null; float bd = 8f;
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead || !PlayerIsTargetable(p)) continue;
                if (!(p.currentlyHeldObjectServer is StackItem held) || !IsFood(held.ItemKey)) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// Host, chasing: a tall mob (an enderman, a wither skeleton) doesn't fit where placed blocks hang lower than it
        /// is tall (#112), Minecraft's two-high hiding hole. The navmesh doesn't know heights, so it walks the path to
        /// its target and stops at the last point it fits, short of the gap, until the way is clear.
        /// </summary>
        void KeepOutOfLowGaps()
        {
            if (agent == null || !agent.isOnNavMesh || targetPlayer == null) return;
            var path = new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(transform.position, targetPlayer.transform.position, agent.areaMask, path) || path.corners.Length < 2) return;
            float h = TallBlocks * BlockWorld.S, step = 0.35f, walked = 0f;
            var lastGood = transform.position;
            // (already under one: let it walk out; otherwise every step counts, or it creeps in a bit each time)
            bool under = LowCeiling(transform.position, h);
            for (int i = 1; i < path.corners.Length; i++)
            {
                var a = path.corners[i - 1]; var b = path.corners[i];
                float len = Vector3.Distance(a, b);
                for (float d = 0f; d < len; d += step)
                {
                    var q = Vector3.Lerp(a, b, d / Mathf.Max(len, 1e-4f));
                    walked += step;
                    // (under one already: let it walk out)
                    if ((!under || walked > 0.8f) && LowCeiling(q, h))
                    {
                        movingTowardsTargetPlayer = false;
                        SetDestinationToPosition(lastGood);
                        LowGapStops++;
                        return;
                    }
                    lastGood = q;
                }
            }
        }

        // (#193: its body's width too, not only where its centre stands: it had stopped with half of itself under a roof it
        // doesn't fit under. Minecraft's enderman is 0.6 blocks wide: 0.4 m each side)
        static readonly Vector3[] bodyReach = { Vector3.zero, new Vector3(0.4f, 0f, 0f), new Vector3(-0.4f, 0f, 0f), new Vector3(0f, 0f, 0.4f), new Vector3(0f, 0f, -0.4f) };

        static bool LowCeiling(Vector3 floor, float h)
        {
            // (only placed blocks: the facility's own doorways are what they are)
            foreach (var o in bodyReach)
                if (Physics.Raycast(floor + o + Vector3.up * 0.25f, Vector3.up, out var hit, h - 0.25f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<BlockRef>() != null) return true;
            return false;
        }

        /// <summary>Every client, for its own player: walked into, it hits (a second between swings).</summary>
        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
            if (hitCooldown > 0f || isEnemyDead) return;
            var p = MeetsStandardPlayerCollisionConditions(other);
            if (p == null) return;
            // only when it's after someone (#110: a calm enderman or piglin hurt whoever bumped into it). The state is
            // the same on every machine; who it's angry with is the host's, so the host checks that too.
            if (currentBehaviourStateIndex != Chase) return;
            if (IsServer && !Wants(p)) return;
            if (Damage <= 0) return; // (a small slime, a witch: no hit up close)
            hitCooldown = 1f;
            swing = 1f;
            p.DamagePlayer(Damage, true, true, CauseOfDeath.Bludgeoning);
            OnHitPlayer(p);
            Sounds.Play(Sound + ".hit", transform.position + Vector3.up, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 16f);
        }

        public override void Update()
        {
            base.Update();
            if (isEnemyDead) return;
            if (IsOwner && FloatsInWater) FloatInWater();
            if (IsServer && WaterHurts) WaterHurt();
            hitCooldown -= Time.deltaTime;
            idleIn -= Time.deltaTime;
            if (idleIn <= 0f)
            {
                idleIn = UnityEngine.Random.Range(5f, 11f);
                Sounds.Play(Sound + ".idle", transform.position + Vector3.up * 1.6f, 0.7f, UnityEngine.Random.Range(0.9f, 1.1f), 20f);
                // (#153: an animal's call is a noise the game's monsters hear, like a baboon hawk's: eyeless dogs come for it)
                if (Passive && IsServer && isOutside) RoundManager.Instance.PlayAudibleNoise(transform.position + Vector3.up, 22f, 0.8f, 0, false, 0);
            }
            // walking legs and arms, Minecraft's way; the right arm swings down when it hits
            float speed = Vector3.Distance(transform.position, lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            // (#153: an animal walking about makes footsteps the dogs can hear, as a player's do)
            if (Passive && IsServer && isOutside && speed > 0.5f && (stepNoiseIn -= Time.deltaTime) <= 0f)
            {
                stepNoiseIn = 1.5f;
                RoundManager.Instance.PlayAudibleNoise(transform.position, 10f, 0.5f, 0, false, 0);
            }
            lastPos = transform.position;
            walkPhase += Time.deltaTime * Mathf.Clamp(speed, 0f, 6f) * 3f;
            float leg = Mathf.Clamp01(speed / 2f) * 35f * Mathf.Sin(walkPhase);
            if (legs[0] != null) legs[0].localRotation = Quaternion.Euler(leg, 0f, 0f);
            if (legs[1] != null) legs[1].localRotation = Quaternion.Euler(-leg, 0f, 0f);
            if (legs[2] != null) legs[2].localRotation = Quaternion.Euler(-leg, 0f, 0f); // (back legs: diagonal pairs together)
            if (legs[3] != null) legs[3].localRotation = Quaternion.Euler(leg, 0f, 0f);
            swing = Mathf.Max(0f, swing - Time.deltaTime * 3f);
            float s = 70f * Mathf.Sin(swing * Mathf.PI);
            if (ArmsForward)
            {
                // (Minecraft's zombie: both arms straight out, bobbing a little; a hit swings them down and back up)
                float bob = 4f * Mathf.Sin(Time.time * 2.5f);
                if (armR != null) armR.localRotation = Quaternion.Euler(-90f + bob + s * 0.6f, 0f, 0f);
                if (armL != null) armL.localRotation = Quaternion.Euler(-90f - bob + s * 0.6f, 0f, 0f);
            }
            else
            {
                if (armR != null) armR.localRotation = Quaternion.Euler(-leg * 0.6f - s, 0f, 0f);
                if (armL != null) armL.localRotation = Quaternion.Euler(leg * 0.6f, 0f, 0f);
            }
            if (leash != null && leash.enabled && LeashedTo == TraderLeash && LeashMob != null)
            {
                leash.SetPosition(0, transform.position + Vector3.up * 1.1f);
                leash.SetPosition(1, LeashMob.transform.position + Vector3.up * 1.2f);
            }
            if (leash != null && leash.enabled && leashHolder == null && knot.HasValue)
            {
                leash.SetPosition(0, transform.position + Vector3.up * 1.1f);
                leash.SetPosition(1, knot.Value);
            }
            if (leash != null && leash.enabled && leashHolder != null)
            {
                leash.SetPosition(0, transform.position + Vector3.up * 1.1f);
                var hand = leashHolder.gameplayCamera != null ? leashHolder.gameplayCamera.transform.position + leashHolder.gameplayCamera.transform.right * 0.3f - Vector3.up * 0.5f : leashHolder.transform.position + Vector3.up;
                leash.SetPosition(1, hand);
            }
            hurtFlash = Mathf.Max(0f, hurtFlash - Time.deltaTime * 3f);
            ShowHurt();
        }

        void ShowHurt()
        {
            bool on = hurtFlash > 0f;
            if ((!on && !hurtShown) || model == null) return;
            if (on && !hurtShown) // (the parts as they are now: a model can change, a sheep shorn, a horse saddled)
                hurtParts = model.GetComponentsInChildren<MeshRenderer>().Where(r => !Held(r)).ToArray<Renderer>();
            if (hurtBlock == null) hurtBlock = new MaterialPropertyBlock();
            int n = 0;
            foreach (var r in hurtParts)
            {
                if (r == null) continue;
                if (!on) { r.SetPropertyBlock(null); continue; }
                // (Minecraft mixes 30% red over the texture: red even on a black one. Here the colour times red, and the
                // model's own glow (its ambient light, the colour's) part red whatever the colour, as the mix's red part)
                var m = r.sharedMaterial;
                var tint = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f), hurtFlash);
                hurtBlock.Clear();
                if (m.HasProperty("_BaseColor")) hurtBlock.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                if (m.HasProperty("_EmissiveColor")) hurtBlock.SetColor("_EmissiveColor", m.GetColor("_EmissiveColor") * tint);
                if (m.HasProperty("_AlbedoAffectEmissive")) hurtBlock.SetFloat("_AlbedoAffectEmissive", m.GetFloat("_AlbedoAffectEmissive") * (1f - 0.3f * hurtFlash));
                r.SetPropertyBlock(hurtBlock); n++;
            }
            HurtTinted = n;
            hurtShown = on;
        }

        /// <summary>A held item (a sword, a bow: drawn from the block atlas), which keeps its colours.</summary>
        static bool Held(Renderer r)
        {
            var m = r.sharedMaterial;
            return m == null || (m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") == Atlas.Texture);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;
            hurtFlash = 1f;
            Sounds.Play(Sound + ".hurt", transform.position + Vector3.up * 1.6f, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f), 20f);
            enemyHP -= force;
            if (enemyHP <= 0)
            {
                Sounds.Play(Sound + ".death", transform.position + Vector3.up * 1.6f, 0.9f, 1f, 20f);
                DeathPoof.Play(model); // (#113: red, over it goes, a puff of smoke)
                if (!Passive && playerWhoHit != null && playerWhoHit == GameNetworkManager.Instance?.localPlayerController) Advancements.Grant("monster_hunter"); // (a monster: not a pig)
                // (once: when another machine owns it, more hits can land here before its death comes back, #126)
                if (IsServer && !dropped)
                {
                    dropped = true; KilledByPlayer = playerWhoHit != null;
                    ModItems.LootingBonus = playerWhoHit != null && playerWhoHit.currentlyHeldObjectServer is ToolItem lt ? Enchants.Level(lt.Ench, EnchKind.Looting) : 0; // (#244)
                    ModItems.CookDrops = MobEffects.Of(this)?.Burning == true; // (#244: dying on fire, its meat comes cooked)
                    try { if (!IsBaby) ServerDrops(transform.position + Vector3.up * 0.6f); } finally { ModItems.LootingBonus = 0; ModItems.CookDrops = false; }
                    if (this is PetAI pd) pd.ServerDeathTold(); // (#232: its owner hears of it)
                    if (LeashedTo != NoLeash) ServerUnleash(true);
                    if (HeadKey != null && MobHeads.KilledByChargedBlast(transform.position)) MobHeads.ServerDropHead(HeadKey, transform.position + Vector3.up * 0.6f);
                    // (#244: a baby animal gives none, Minecraft's; a baby monster does, Monster.shouldDropExperience)
                    if (playerWhoHit != null && Xp > 0 && (!IsBaby || !Passive)) BlockNet.ServerXp(playerWhoHit.actualClientId, Xp, transform.position + Vector3.up * 0.5f);
                }
                if (IsOwner) KillEnemyOnOwnerClient(overrideDestroy: true);
            }
            else if (IsOwner && Passive && Swims) { panicUntil = Time.time + 4f; }
            else if (IsOwner && Passive)
            {
                // (hurt, it squeals: a louder noise, the dogs close in)
                if (isOutside) RoundManager.Instance.PlayAudibleNoise(transform.position + Vector3.up, 14f, 0.9f, 0, false, 0);
                // (it runs: faster, away from who hit it, a few seconds)
                panicUntil = Time.time + 5f; panicFrom = playerWhoHit; agent.speed = PanicSpeed;
                if (currentSearch.inProgress) StopSearch(currentSearch);
                if (agent.isOnNavMesh) agent.ResetPath(); // (off it: falling, or held by the dev hold)
            }
            else if (IsOwner && playerWhoHit != null && currentBehaviourStateIndex == Roam)
            {
                targetPlayer = playerWhoHit; agent.speed = ChaseSpeed; SwitchToBehaviourState(Chase);
            }
        }

        // ------------------------------------------------------------------ registering one (startup)
        /// <summary>
        /// A mob's network prefab and enemy type, registered with LethalLib to spawn inside at that rarity: the agent, a
        /// trigger box to hit and be hit, its eye, voice, scan node and its model (built by `model`, feet at 0, facing +z).
        /// </summary>
        public static EnemyType Register<T>(string id, string name, string scanText, int rarity, int hp, float height, int maxCount, float roamSpeed, Action<Transform> buildModel,
                                            bool daytime = false, float length = 0.8f, bool outside = false) where T : MinecraftMobAI
        {
            var prefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab("LMC_" + id);
            prefab.layer = 19;
            prefab.GetComponent<NetworkObject>().AutoObjectParentSync = false;
            var agent = prefab.AddComponent<NavMeshAgent>();
            agent.speed = roamSpeed; agent.angularSpeed = 400f; agent.acceleration = 12f;
            agent.radius = 0.4f; agent.height = height; agent.stoppingDistance = 0.3f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var modelGo = new GameObject("Model");
            modelGo.transform.SetParent(prefab.transform, false);
            modelGo.layer = 19;
            modelGo.AddComponent<Animator>();
            buildModel(modelGo.transform);

            var col = new GameObject("Collision");
            col.transform.SetParent(prefab.transform, false);
            col.layer = 19; col.tag = "Enemy";
            var box = col.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(0.8f, height, length); box.center = new Vector3(0f, height / 2f, 0f);
            var rb = col.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var detect = col.AddComponent<EnemyAICollisionDetect>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(prefab.transform, false);
            eye.localPosition = new Vector3(0f, height - 0.2f, 0.2f);

            AudioSource Audio(string n)
            {
                var go = new GameObject(n); go.transform.SetParent(prefab.transform, false); go.transform.localPosition = Vector3.up;
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.maxDistance = 25f;
                return a;
            }

            var scan = new GameObject("ScanNode");
            scan.transform.SetParent(prefab.transform, false);
            scan.transform.localPosition = Vector3.up * (height * 0.6f);
            scan.layer = 22;
            var sc = scan.AddComponent<BoxCollider>(); sc.isTrigger = true; sc.size = Vector3.one * 0.6f;
            var sn = scan.AddComponent<ScanNodeProperties>();
            sn.headerText = name; sn.subText = scanText; sn.maxRange = 30; sn.minRange = 1;
            sn.requiresLineOfSight = true; sn.nodeType = 1; sn.creatureScanID = -1;

            var type = ScriptableObject.CreateInstance<EnemyType>();
            type.name = "LMC_" + id + "Type";
            type.enemyName = name;
            type.enemyPrefab = prefab;
            // (a farm animal: out on the moon by day, as the game's own daytime creatures are)
            // (outside: the moon's night-time monsters, like the creeper's outside kind)
            type.isOutsideEnemy = daytime || outside;
            type.isDaytimeEnemy = daytime;
            type.MaxCount = maxCount;
            type.PowerLevel = daytime ? 0.5f : 1f;
            type.probabilityCurve = AnimationCurve.Constant(0f, 1f, 1f);
            type.numberSpawnedFalloff = AnimationCurve.Constant(0f, 1f, 1f);
            type.useNumberSpawnedFalloff = false;
            type.canDie = true;
            type.destroyOnDeath = true;
            type.canBeStunned = true;
            type.stunTimeMultiplier = 1f;
            type.doorSpeedMultiplier = 1f;
            type.canSeeThroughFog = false;
            type.disableAnimatorWhenFar = false;
            type.pushPlayerForce = 0.3f;
            type.pushPlayerDistance = 1.2f;
            type.miscAnimations = new MiscAnimation[0];
            type.audioClips = new AudioClip[0];

            var ai = prefab.AddComponent<T>();
            ai.enemyType = type;
            ai.agent = agent;
            ai.eye = eye;
            ai.creatureSFX = Audio("SFX");
            ai.creatureVoice = Audio("Voice");
            ai.creatureAnimator = modelGo.GetComponent<Animator>();
            ai.enemyHP = hp;
            ai.AIIntervalTime = 0.2f;
            ai.updatePositionThreshold = 0.2f;
            ai.syncMovementSpeed = 0.2f;
            ai.exitVentAnimationTime = 0f;
            ai.ventAnimationFinished = true;
            ai.enemyBehaviourStates = new[]
            {
                new EnemyBehaviourState { name = "Roam", parameterString = "roam" },
                new EnemyBehaviourState { name = "Chase", parameterString = "chase" },
            };
            detect.mainScript = ai;
            detect.canCollideWithEnemies = false;

            LethalLib.Modules.Enemies.RegisterEnemy(type, rarity, LethalLib.Modules.Levels.LevelTypes.All,
                daytime ? LethalLib.Modules.Enemies.SpawnType.Daytime : outside ? LethalLib.Modules.Enemies.SpawnType.Outside : LethalLib.Modules.Enemies.SpawnType.Default,
                (TerminalNode)null, (TerminalKeyword)null);
            Plugin.Log.LogInfo($"{name} registered ({(daytime ? "outside by day" : outside ? "outside" : "inside")}, rarity {rarity})");
            return type;
        }

        /// <summary>A humanoid's limbs on their joints (Minecraft's biped): arms at the shoulders (ArmR, ArmL), legs at the
        /// hips (LegL, LegR), each `limb` texture pixels thick; the texture patches as (u, v) per limb.</summary>
        public static void Biped(Transform parent, float k, Material mat, int limb, (int u, int v) head, (int u, int v) body,
                                 (int u, int v) armR, (int u, int v) armL, (int u, int v) legR, (int u, int v) legL, Action<Transform> inRightHand = null)
        {
            float half = 4f + limb / 2f; // (arm centres: beside the 8-wide body)
            EntityModels.Part(parent, "Head", new Vector3(0, 28, 0), new Vector3(8, 8, 8), head.u, head.v, k, mat);
            EntityModels.Part(parent, "Body", new Vector3(0, 18, 0), new Vector3(8, 12, 4), body.u, body.v, k, mat);
            foreach (var (n, x, uv) in new[] { ("ArmR", -half, armR), ("ArmL", half, armL) })
            {
                var sh = new GameObject(n).transform;
                sh.SetParent(parent, false);
                sh.localPosition = new Vector3(x, 23f, 0f) * k;
                EntityModels.Part(sh, "Arm", new Vector3(0, -5, 0), new Vector3(limb, 12, limb), uv.u, uv.v, k, mat);
                if (n == "ArmR") inRightHand?.Invoke(sh);
            }
            foreach (var (n, x, uv) in new[] { ("LegL", 2f, legL), ("LegR", -2f, legR) }) // (Minecraft: the hips 2 px either side, whatever the legs' width)
            {
                var hip = new GameObject(n).transform;
                hip.SetParent(parent, false);
                hip.localPosition = new Vector3(x, 12f, 0f) * k;
                EntityModels.Part(hip, "Leg", new Vector3(0, -6, 0), new Vector3(limb, 12, limb), uv.u, uv.v, k, mat);
            }
        }
    }
}
