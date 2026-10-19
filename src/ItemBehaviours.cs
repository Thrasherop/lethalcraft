using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{

    public static class SpawnFix
    {
        /// <summary>Runtime prefabs are created with HideAndDontSave; clones inherit it, which hides them from
        /// FindObjectsOfType (ship saving, etc). Clear it on every spawned instance.</summary>
        public static void Clear(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
        }
    }
    /// <summary>A stack of blocks or food that lives in one hotbar slot (Minecraft-style counts, max 64).</summary>
    /// <summary>[Q] with one of our items in hand (#52): throw one of a stack, drop anything else.</summary>
    public static class QKey
    {
        public static void InHand(GrabbableObject g, bool held)
        {
            if (g.IsOwner && g.playerHeldBy != null && g.playerHeldBy == GameNetworkManager.Instance?.localPlayerController) g.playerHeldBy.equippedUsableItemQE = held;
        }

        public static bool CanThrow(GrabbableObject g) =>
            g.IsOwner && g.playerHeldBy != null && g.playerHeldBy.currentlyHeldObjectServer == g && !g.playerHeldBy.isGrabbingObjectAnimation && !SlotScreen.AnyOpen;
    }

    public class StackItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);
        /// <summary>Every active stack (what FindObjectsOfType would find, without searching the whole scene).</summary>
        public static readonly HashSet<StackItem> Live = new HashSet<StackItem>();
        void OnEnable() => Live.Add(this);
        void OnDisable() => Live.Remove(this);
        public ushort BlockType;
        public string ItemKey;
        public FoodDef Food;
        public int DefaultCount = 1;
        public int Count = -1;
        public float SpawnTime;
        /// <summary>Server: not pulled into a nearby player's stack before this (thrown with [Q]: like Minecraft's pickup delay).</summary>
        public float NoMergeUntil;
        public bool Despawning;
        public int MaxStack => Inventory.StackLimit(ItemKey); // (Minecraft's, one table: #203)
        /// <summary>Scrap that stacks (raw iron): each one is worth this much, so the stack sells for Count x UnitValue.</summary>
        public int UnitValue;

        void SyncValue() { if (UnitValue > 0 && itemProperties != null && itemProperties.isScrap) SetScrapValue(Mathf.Max(0, Count) * UnitValue); }
        public BlockDef Block => BlockType != 0 ? Blocks.Get(BlockType) : null;
        Transform model;
        Vector3 modelBasePos, groundScale;
        public float HeldScale = 0.6f;
        string lastTip;

        public override void Start()
        {
            model = transform.Find("model");
            if (model != null) { modelBasePos = model.localPosition; groundScale = model.localScale; }
            if (BlockNet.PendingCounts.TryGetValue(NetworkObjectId, out int pc)) { Count = pc; BlockNet.PendingCounts.Remove(NetworkObjectId); }
            if (Count < 0) Count = DefaultCount;
            if (SpawnTime <= 0) SpawnTime = Time.time;
            if (ItemData.TypeOf(ItemKey) == "enchanted_golden_apple") Glint.ApplyModel(this); // (#244: it shimmers in hand and on the ground)
            // FoodDef isn't Unity-serialized, so clones lose it: resolve from the (serialized) key
            if (Food == null && !string.IsNullOrEmpty(ItemKey)) ModItems.Foods.TryGetValue(ItemKey, out Food);
            base.Start();
            SyncValue();
            RefreshLabels();
        }

        public override int GetItemDataToSave() => Mathf.Max(0, Count);

        public override void LoadItemSaveData(int saveData)
        {
            Count = Mathf.Clamp(saveData, 1, Mathf.Max(MaxStack, 64)); // (a stack saved before the limits were Minecraft's stays whole)
            SyncValue();
            RefreshLabels();
        }

        /// <summary>Host: one (or n) used up by its holder, as Minecraft's ItemStack.consume: not in creative (#200: feeding,

        /// taming, composting, a lead, a book in a shelf used them up in creative too).</summary>

        public void ServerUseUp(PlayerControllerB holder, int n = 1)

        {

            if (holder != null && GameModes.IsCreative(holder)) return;

            ServerSetCount(Count - n, despawnIfEmpty: true);

        }


        public void ServerSetCount(int c, bool despawnIfEmpty = false)
        {
            Count = Mathf.Clamp(c, 0, MaxStack);
            SyncValue();
            BlockNet.ServerStackCount(this);
            if (Count <= 0 && despawnIfEmpty && !isHeld && playerHeldBy == null && IsSpawned)
            {
                Despawning = true;
                NetworkObject.Despawn(true);
            }
        }

        public void SetCountLocal(int c)
        {
            Count = c;
            SyncValue();
            RefreshLabels();
            if (Count <= 0 && IsOwner && isHeld && !isPocketed && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController && playerHeldBy.currentlyHeldObjectServer == this)
            {
                HUDManager.Instance.ClearControlTips(); // (the hand is empty now: no "Torch x0" hints)
                Despawning = true;
                playerHeldBy.DespawnHeldObject();
            }
            else if (Count <= 0 && IsOwner && isPocketed && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController)
            {
                // (an empty stack in a slot not held, a powered shelf's swap: out of that slot, not left as "Stone x0")
                int i = System.Array.IndexOf(playerHeldBy.ItemSlots, this);
                if (i >= 0) { Despawning = true; playerHeldBy.DestroyItemInSlotAndSync(i); }
            }
        }

        public string DisplayName => itemProperties.itemName;

        public void RefreshLabels()
        {
            bool scrap = itemProperties != null && itemProperties.isScrap;
            customGrabTooltip = $"Pick up {DisplayName} x{Mathf.Max(Count, 0)}" + (scrap ? $" (${Mathf.Max(Count, 0) * UnitValue})" : "") + " : [E]";
            var scan = GetComponentInChildren<ScanNodeProperties>();
            // (scrap that stacks: what the whole stack sells for, as the game shows for its own scrap)
            if (scan != null) scan.subText = scrap ? $"Value: ${Mathf.Max(Count, 0) * UnitValue} (x{Mathf.Max(Count, 0)})" : $"x{Mathf.Max(Count, 0)}";
            if (IsOwner && isHeld && !isPocketed && playerHeldBy == GameNetworkManager.Instance?.localPlayerController) SetControlTipsForItem();
        }

        public override void SetControlTipsForItem()
        {
            string[] tips;
            string use = Plugin.PlaceWithLeftClick.Value ? "[LMB]" : "[Right-click]"; // (not "[RMB]": the game turns it into "[LMB]" in an item's tips. A tip line holds 27 characters; longer ones wrapped onto the next)
            string brk = Plugin.PlaceWithLeftClick.Value ? null : "Break : hold [LMB]";
            if (Food != null && Block != null) tips = new[] { $"Eat : hold {use}", $"Plant : {use}", $"{DisplayName} x{Count}" };
            else if (Food != null) tips = new[] { $"{(Food.Drink ? "Drink" : "Eat")} : hold {use}", $"{DisplayName} x{Count}" }; // (#244: honey and milk are drunk)
            else if (Potions.IsSplash(ItemKey) || ItemKey == "ender_pearl") tips = new[] { $"Throw : {use}", brk ?? "", $"{DisplayName} x{Count}" }; // (#244: splash potions)
            else if (Potions.IsPotion(ItemKey)) tips = new[] { $"Drink : hold {use}", brk ?? "", $"{DisplayName} x{Count}" }; // (#244: they had no tip)
            else if (ItemKey == Spyglass.Key) tips = new[] { $"Zoom : hold {use}", brk ?? "", DisplayName }; // (#229)
            else if (Block == null) tips = new[] { brk ?? "", $"{DisplayName} x{Count}" }; // sticks, coal, ingots: nothing to place
            else tips = new[] { $"Place : {use}", brk ?? "", $"{DisplayName} x{Count}" };
            if (Count > 1) tips = tips.Take(tips.Length - 1).Concat(new[] { "Throw one : [Q]", tips[tips.Length - 1] }).ToArray(); // (#52)
            string key = string.Join("|", tips);
            lastTip = key;
            // the game only rewrites the lines it's given: pad so a shorter list blanks what an earlier, longer one left
            var lines = HUDManager.Instance.controlTipLines;
            int room = lines != null ? lines.Length - 1 : tips.Length;
            if (tips.Length < room) { var padded = new string[room]; for (int i = 0; i < room; i++) padded[i] = i < tips.Length ? tips[i] : ""; tips = padded; }
            HUDManager.Instance.ChangeControlTipMultiple(tips, holdingItem: true, itemProperties);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) { }

        // [Q] (the game's "secondary use"): throw one, like Minecraft (#52). The game only passes Q to an item that says
        // it uses Q/E while it's in hand.
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (right || !QKey.CanThrow(this)) return;
            if (Count > 1) BlockNet.RequestThrowOne(this);
            else playerHeldBy.DiscardHeldObject();
        }

        public override void Update()
        {
            base.Update();
            if (model == null) return;
            bool onGround = !isHeld && !isHeldByEnemy && parentObject == null;
            model.localScale = isHeld ? groundScale * HeldScale : groundScale;
            if (onGround)
            {
                float t = Time.time + NetworkObjectId * 0.37f;
                model.localPosition = modelBasePos + Vector3.up * (0.06f + Mathf.Sin(t * 2.2f) * 0.04f) * Plugin.S;
                model.localRotation = Quaternion.Euler(0, (t * 50f) % 360f, 0);
            }
            else if (model.localPosition != modelBasePos)
            {
                model.localPosition = modelBasePos;
                model.localRotation = Quaternion.identity;
            }
        }
    }

    /// <summary>A piece of armor in hand: right-click (or the [I] inventory's armor slots) puts it on.</summary>
    public class ArmorItem : GrabbableObject
    {
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (!right && QKey.CanThrow(this)) playerHeldBy.DiscardHeldObject(); // [Q]: drop it (#52)
        }
        public string ItemKey;
        /// <summary>Its enchantments (#46, Enchants), saved with the item.</summary>
        public int Ench;
        /// <summary>The elytra's wear (#61), in the same bits as a tool's.</summary>
        public int Used;
        public override int GetItemDataToSave() => Enchants.Data(Used, Ench);
        public override void LoadItemSaveData(int saveData) { Used = Enchants.UsesOf(saveData); Ench = Enchants.EnchOf(saveData); Glint.ApplyModel(this); }
        void Awake() => SpawnFix.Clear(gameObject);
    }

    public class ToolItem : GrabbableObject
    {
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (!right && QKey.CanThrow(this)) playerHeldBy.DiscardHeldObject(); // [Q]: drop it (#52)
        }
        public ToolKind Kind = ToolKind.Pickaxe;
        public int Tier = 1;
        public float Speed = 4f;
        public string ItemKey;
        /// <summary>Damage per hit in Lethal Company terms (the shovel's is 1); fractions carry over to the next hit on the same monster.</summary>
        public float AttackForce = 1f;
        /// <summary>Seconds between swings (the shovel swings about every 0.8 s).</summary>
        public float AttackCooldown = 0.8f;

        // ------------------------------------------------------------------ durability (#48)
        /// <summary>How many uses a tool of this tier has, like Minecraft's: wood 59, stone 131, iron 250, diamond 1561.</summary>
        public virtual int MaxUses => Balance.ToolDurability ? Mathf.Max(1, Mathf.RoundToInt((Kind == ToolKind.Shears ? 238 : ItemKey != null && ItemKey.StartsWith("golden_") ? 32 : ItemKey != null && ItemKey.StartsWith("copper_") ? 190 : Tier <= 1 ? 59 : Tier == 2 ? 131 : Tier == 3 ? 250 : Tier == 4 ? 1561 : 2031) * Balance.DurabilityMultiplier)) : 0; // (#244: shears 238, Minecraft's)
        /// <summary>Uses so far (the server counts; everyone gets told). Saved with the item.</summary>
        public int Used;
        /// <summary>Its enchantments (#46, Enchants), saved with it above the uses.</summary>
        public int Ench;
        public float Wear => MaxUses > 0 ? Mathf.Clamp01(Used / (float)MaxUses) : 0f;
        public override int GetItemDataToSave() => Enchants.Data(Used, Ench);
        public override void LoadItemSaveData(int saveData) { Used = Enchants.UsesOf(saveData); Ench = Enchants.EnchOf(saveData); Glint.ApplyModel(this); }

        /// <summary>Server: the tool was used n times (a block mined, a monster hit); worn out, it breaks.</summary>
        public void ServerUse(int n)
        {
            if (!BlockNet.IsServer || MaxUses <= 0 || n <= 0) return;
            // Unbreaking: a use only wears it now and then
            int ub = Enchants.Level(Ench, EnchKind.Unbreaking), worn = 0;
            for (int i = 0; i < n; i++) if (ub <= 0 || Random.value < Enchants.WearChance(ub)) worn++;
            if (worn == 0) return;
            Used += worn;
            BlockNet.ServerToolUses(this, Used >= MaxUses);
        }
        void Awake() => SpawnFix.Clear(gameObject);
        Transform model;
        Quaternion baseRot;
        float swingT = 1f, nextSwing;
        static readonly Dictionary<int, float> carry = new Dictionary<int, float>();

        public override void Start()
        {
            model = transform.Find("model");
            if (model != null) baseRot = model.localRotation;
            base.Start();
        }

        public override void ItemActivate(bool used, bool buttonDown = true)
        {
            // block mining is handled by the Builder (hold LMB); a click that isn't aimed at a block swings at things
            if (!buttonDown || playerHeldBy == null || !IsOwner) return;
            if (Builder.Instance != null && Builder.Instance.HasTarget) return;
            if (Time.time < nextSwing) return;
            nextSwing = Time.time + AttackCooldown;
            Swing();
        }

        /// <summary>Whole damage for this hit on a monster, the fraction kept for the next (a diamond sword's 1.4: 1, 2, 1, 2, 1...).</summary>
        public static int Swept; // (dev/tests: monsters a sweep caught)

        int ForceFor(Component target, float mult = 1f, float extra = 0f)
        {
            float force = Mathf.Max(0f, AttackForce * mult + Potions.StrengthBonus + extra);
            if (Mathf.Approximately(force, Mathf.Round(force))) return Mathf.RoundToInt(force);
            int id = target != null ? target.GetInstanceID() : 0;
            carry.TryGetValue(id, out float c);
            if (!carry.ContainsKey(id)) c = 0.5f;
            c += force;
            int f = Mathf.FloorToInt(c);
            carry[id] = c - f;
            return f;
        }

        public void PlaySwingAnim() => swingT = 0f;

        void Swing()
        {
            PlaySwingAnim();
            Sounds.Play("swing", transform.position, 0.5f, Random.Range(0.9f, 1.1f));
            var p = playerHeldBy;
            var cam = p.gameplayCamera.transform;
            // a critical hit (#1), like Minecraft: swinging while falling does half as much again
            bool crit = Crits.Enabled && !p.thisController.isGrounded && p.fallValue < 0f && !p.isClimbingLadder && !p.isUnderwater && !CreativeFlight.Flying;
            var hits = Physics.SphereCastAll(cam.position + cam.right * -0.35f, 0.75f, cam.forward, 1.6f, 1084754248, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).ToList();
            // like the game's shovel: one swing hits every monster in its arc once (a dead one doesn't soak it up) and at
            // most one player
            var hitEnemies = new HashSet<EnemyAI>();
            bool hitPlayer = false, landed = false;
            var skipped = Plugin.DevMode.Value ? new List<string>() : null;
            foreach (var h in hits)
            {
                if (h.transform.gameObject.layer == 8 || h.transform.gameObject.layer == 11) continue;
                if (!h.transform.TryGetComponent<IHittable>(out var hittable)) { if (h.transform.GetComponentInParent<EnemyAI>() != null) skipped?.Add(h.transform.name + " (no IHittable)"); continue; }
                if (h.transform == p.transform) continue;
                if (h.point != Vector3.zero && Physics.Linecast(cam.position, h.point, out var wall, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore))
                {
                    skipped?.Add($"{h.transform.name} behind {wall.collider.name}");
                    continue;
                }
                var col = h.transform.GetComponent<EnemyAICollisionDetect>();
                if (col != null)
                {
                    var e = col.mainScript;
                    if (e == null || e.isEnemyDead || hitEnemies.Contains(e)) continue;
                    if (e is ParrotAI sp && sp.Perched) continue; // (#232: a parrot on a shoulder isn't there to hit, Minecraft's)
                    if (StartOfRound.Instance.hangarDoorsClosed && e.isInsidePlayerShip != p.isInHangarShipRoom) { skipped?.Add($"{e.enemyType.enemyName} (shut door: inside {e.isInsidePlayerShip}, you {p.isInHangarShipRoom})"); continue; }
                    hitEnemies.Add(e);
                }
                else if (h.transform.GetComponent<PlayerControllerB>() != null)
                {
                    if (hitPlayer) continue;
                    hitPlayer = true;
                }
                try
                {
                    // (#244) Smite against the undead, Bane of Arthropods against spiders and endermites: Minecraft's +2.5 a level
                    // (a fifth of that here: 5 of its health to one of ours)
                    float extra = 0f;
                    if (col != null && MobEffects.Undead(col.mainScript)) extra += Enchants.SmiteBonus(Enchants.Level(Ench, EnchKind.Smite)) / 5f;
                    if (col != null && MobEffects.Arthropod(col.mainScript)) extra += Enchants.SmiteBonus(Enchants.Level(Ench, EnchKind.BaneOfArthropods)) / 5f;
                    int force = ForceFor(col != null ? (Component)col.mainScript : h.transform, (crit ? 1.5f : 1f) * Enchants.SharpnessFactor(Enchants.Level(Ench, EnchKind.Sharpness)), extra);
                    if (force <= 0) { Sounds.Play("attack", h.point, 0.35f, 1.3f); skipped?.Add(h.transform.name + " (glancing)"); continue; } // a glancing blow
                    bool took = hittable.Hit(force, cam.forward, p, true, 1);
                    if (!took) skipped?.Add(h.transform.name + " (didn't take it)");
                    if (took) { landed = true; if (crit) Crits.Show(h.point != Vector3.zero ? h.point : h.transform.position + Vector3.up); }
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning("tool hit: " + (Plugin.DevMode.Value ? ex.ToString() : ex.Message)); }
            }
            // (#244) Sweeping Edge: Minecraft's sweep, the monsters within a block of the one struck take 1 + level/(level+1)
            // of the hit
            int sweep = Kind == ToolKind.Sword ? Enchants.Level(Ench, EnchKind.SweepingEdge) : 0;
            if (sweep > 0 && hitEnemies.Count > 0 && RoundManager.Instance != null)
            {
                var first = hitEnemies.First();
                float sweepForce = (1f + AttackForce * 5f * sweep / (sweep + 1f)) / 5f;
                foreach (var other in RoundManager.Instance.SpawnedEnemies.ToList())
                {
                    if (other == null || other.isEnemyDead || hitEnemies.Contains(other) || Vector3.Distance(other.transform.position, first.transform.position) > 1.5f * BlockWorld.S) continue;
                    int sf = ForceFor(other, sweepForce / Mathf.Max(0.01f, AttackForce));
                    if (sf > 0) { other.HitEnemyOnLocalClient(sf, cam.forward, p, true, 1); Swept++; }
                }
            }
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] {ItemKey} swing{(crit ? " (crit)" : "")}: {(landed ? "hit " + string.Join(",", hitEnemies.Select(x => x.enemyType.enemyName)) + (hitPlayer ? " +player" : "") : "nothing")} ({hits.Count} in the arc: {string.Join(",", hits.Select(x => x.transform.name))}{(skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped) : "")})");
            if (landed)
            {
                Sounds.Play("attack", transform.position, 0.7f, 1f);
                Survival.AddExhaustion(0.1f);
                BlockNet.RequestToolUse(this, Kind == ToolKind.Sword ? 1 : 2); // (#244: Minecraft's: a sword 1 a hit, every other tool 2)
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (model == null) return;
            if (isHeld && Builder.Instance != null && Builder.Instance.IsMiningWith(this) && swingT >= 1f) swingT = 0f;
            if (swingT < 1f)
            {
                swingT += Time.deltaTime * 4f;
                float a = Mathf.Sin(Mathf.Clamp01(swingT) * Mathf.PI) * 55f;
                model.localRotation = baseRot * Quaternion.Euler(0, 0, -a);
            }
            else model.localRotation = baseRot;
        }
    }

    /// <summary>A bucket (#19): empty, right-click a water source (ours, or the moon's own water) to fill it; full,
    /// right-click to pour a source out. Driven by Builder, like flint and steel.</summary>
    public class BucketItem : StackItem
    {
        /// <summary>What's in it: nothing, water or lava (from the item's key).</summary>
        public BlockDef Fluid => ItemKey == "water_bucket" ? Blocks.Water : ItemKey == "lava_bucket" ? Blocks.Lava : null;
        public bool Full => Fluid != null;
    }

    public class FlintAndSteelItem : ToolItem
    {
        /// <summary>(#244) Minecraft's 64 uses, one a strike.</summary>
        public override int MaxUses => Balance.ToolDurability ? Mathf.Max(1, Mathf.RoundToInt(64 * Balance.DurabilityMultiplier)) : 0;

        // ignition is driven by Builder (left-click on a block) so it shares one input path
        public override void ItemActivate(bool used, bool buttonDown = true) { }

        public void Strike(BlockKey target, bool hasTarget)
        {
            Sounds.Play("ignite", transform.position, 0.8f, Random.Range(0.9f, 1.1f));
            if (hasTarget) BlockNet.RequestIgnite(target); // the server lights the TNT there, or starts a fire in the cell
        }
    }

    /// <summary>(#244) A bow: a tool with Minecraft's 384 uses (one a shot) and its enchantments (Power, Punch, Flame,
    /// Infinity); the Builder draws and shoots it (Bow).</summary>
    public class BowItem : ToolItem
    {
        public override int MaxUses => Balance.ToolDurability ? Mathf.Max(1, Mathf.RoundToInt(384 * Balance.DurabilityMultiplier)) : 0;
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        public override void SetControlTipsForItem()
        {
            string use = Plugin.PlaceWithLeftClick.Value ? "[LMB]" : "[Right-click]";
            HUDManager.Instance.ChangeControlTipMultiple(new[] { $"Shoot : hold {use}", Plugin.PlaceWithLeftClick.Value ? "" : "Break : hold [LMB]", itemProperties.itemName }, holdingItem: true, itemProperties);
        }
    }

    /// <summary>(#244) An enchanted book: its enchantments in its number, as a tool's (it doesn't wear); the anvil puts them
    /// on things.</summary>
    public class EnchantedBookItem : ToolItem
    {
        public override int MaxUses => 0;
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        public override void SetControlTipsForItem()
        {
            HUDManager.Instance.ChangeControlTipMultiple(new[] { Plugin.PlaceWithLeftClick.Value ? "" : "Break : hold [LMB]", itemProperties.itemName }, holdingItem: true, itemProperties);
        }
    }

    /// <summary>(#244) A fishing rod: Minecraft's 64 uses (one a catch), Lure and Luck of the Sea (Fishing).</summary>
    public class RodItem : ToolItem
    {
        public override int MaxUses => Balance.ToolDurability ? Mathf.Max(1, Mathf.RoundToInt(64 * Balance.DurabilityMultiplier)) : 0;
        public override void ItemActivate(bool used, bool buttonDown = true) { }
        public override void SetControlTipsForItem()
        {
            string use = Plugin.PlaceWithLeftClick.Value ? "[LMB]" : "[Right-click]";
            HUDManager.Instance.ChangeControlTipMultiple(new[] { $"Cast / reel : {use}", Plugin.PlaceWithLeftClick.Value ? "" : "Break : hold [LMB]", itemProperties.itemName }, holdingItem: true, itemProperties);
        }
    }

    public class OreScrapItem : GrabbableObject
    {
        public override void EquipItem() { base.EquipItem(); QKey.InHand(this, true); }
        public override void PocketItem() { base.PocketItem(); QKey.InHand(this, false); }
        public override void DiscardItem() { QKey.InHand(this, false); base.DiscardItem(); }
        public override void ItemInteractLeftRight(bool right)
        {
            base.ItemInteractLeftRight(right);
            if (!right && QKey.CanThrow(this)) playerHeldBy.DiscardHeldObject(); // [Q]: drop it (#52)
        }
        void Awake() => SpawnFix.Clear(gameObject);
        public override void Start()
        {
            base.Start();
            if (BlockNet.PendingScrap.TryGetValue(NetworkObjectId, out int v))
            {
                BlockNet.PendingScrap.Remove(NetworkObjectId);
                SetScrapValue(Mathf.Abs(v));
                if (v > 0 && RoundManager.Instance != null) RoundManager.Instance.totalScrapValueInLevel += v; // (negative: out of a chest, #208)
            }
        }
    }
}

namespace LethalMinecraft
{
    /// <summary>A tool wearing out (#48).</summary>
    public static class Durability
    {
        /// <summary>(#244) Any item key's wear and its most (0, 0: it doesn't wear): tools, the bow, rod and flint and steel,
        /// a crossbow, armor, an elytra.</summary>
        public static (int used, int max) Of(string key)
        {
            if (key == null) return (0, 0);
            int d = ItemData.Of(key);
            if (ItemData.Is(key, Elytra.Key)) return (Enchants.UsesOf(d), Elytra.MaxUses);
            if (Armor.Get(key) != null) return (ArmorWear.Damage(key), ArmorWear.Max(key));
            if (ModItems.ByKey.TryGetValue(key, out var it) && it.spawnPrefab != null && it.spawnPrefab.GetComponent<ToolItem>() is ToolItem t)
                return (t is CrossbowItem ? CrossbowData.WearOf(d) : Enchants.UsesOf(d), t.MaxUses);
            return (0, 0);
        }

        /// <summary>Every client: a tool broke. Its holder loses it, with Minecraft's break sound; everyone hears it.</summary>
        public static void Broke(ToolItem t)
        {
            if (t == null) return;
            var p = t.playerHeldBy;
            Sounds.Play("tool.break", t.transform.position, 0.9f, Random.Range(0.9f, 1.1f));
            if (p == null || p != GameNetworkManager.Instance?.localPlayerController) return;
            for (int i = 0; i < p.ItemSlots.Length; i++)
            {
                if (p.ItemSlots[i] != t) continue;
                p.DestroyItemInSlotAndSync(i);
                McHud.Toast(t.itemProperties.itemName + " broke");
                break;
            }
        }
    }
}

namespace LethalMinecraft
{
    /// <summary>Critical hits (#1): a swing that lands while you're falling does 1.5x, with Minecraft's crit sound and sparks.</summary>
    public static class Crits
    {
        public static bool Enabled = true;
        static Material spark;
        static Mesh quad;

        public static void Show(Vector3 at)
        {
            Sounds.Play("crit", at, 0.8f, Random.Range(0.9f, 1.1f));
            if (spark == null)
            {
                spark = new Material(Shader.Find("HDRP/Unlit")) { name = "LMC_Crit" };
                var c = new Color(0.95f, 0.95f, 0.85f);
                spark.SetColor("_UnlitColor", c); spark.SetColor("_BaseColor", c);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(spark);
                quad = new Mesh { name = "LMC_CritQuad" };
                quad.SetVertices(new System.Collections.Generic.List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }, 0);
                quad.RecalculateBounds();
            }
            for (int i = 0; i < 14; i++)
            {
                var go = new GameObject("LMC_CritSpark");
                go.transform.position = at + Random.insideUnitSphere * 0.25f;
                go.transform.localScale = Vector3.one * Random.Range(0.04f, 0.08f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = spark; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var s = go.AddComponent<CritSpark>();
                s.Velocity = Random.onUnitSphere * Random.Range(1.5f, 3.5f);
            }
        }

        class CritSpark : MonoBehaviour
        {
            public Vector3 Velocity;
            float age;
            const float life = 0.6f;
            Vector3 size;
            void Start() => size = transform.localScale;
            void Update()
            {
                age += Time.deltaTime;
                if (age >= life) { Destroy(gameObject); return; }
                Velocity *= 1f - 3f * Time.deltaTime;
                transform.position += Velocity * Time.deltaTime;
                var cam = Camera.main != null ? Camera.main.transform : null;
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
                transform.localScale = size * (1f - age / life);
            }
        }
    }
}
