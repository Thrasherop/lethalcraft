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
    public class StackItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);
        public byte BlockType;
        public string ItemKey;
        public FoodDef Food;
        public int DefaultCount = 1;
        public int Count = -1;
        public float SpawnTime;
        public bool Despawning;
        public int MaxStack => ItemKey == "ender_pearl" ? 16 : 64;
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
            // FoodDef isn't Unity-serialized, so clones lose it: resolve from the (serialized) key
            if (Food == null && !string.IsNullOrEmpty(ItemKey)) ModItems.Foods.TryGetValue(ItemKey, out Food);
            base.Start();
            RefreshLabels();
        }

        public override int GetItemDataToSave() => Mathf.Max(0, Count);

        public override void LoadItemSaveData(int saveData)
        {
            Count = Mathf.Clamp(saveData, 1, MaxStack);
            RefreshLabels();
        }

        public void ServerSetCount(int c, bool despawnIfEmpty = false)
        {
            Count = Mathf.Clamp(c, 0, MaxStack);
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
            RefreshLabels();
            if (Count <= 0 && IsOwner && isHeld && !isPocketed && playerHeldBy != null && playerHeldBy == GameNetworkManager.Instance.localPlayerController && playerHeldBy.currentlyHeldObjectServer == this)
            {
                HUDManager.Instance.ClearControlTips(); // (the hand is empty now: no "Torch x0" hints)
                Despawning = true;
                playerHeldBy.DespawnHeldObject();
            }
        }

        public string DisplayName => itemProperties.itemName;

        public void RefreshLabels()
        {
            customGrabTooltip = $"Pick up {DisplayName} x{Mathf.Max(Count, 0)} : [E]";
            var scan = GetComponentInChildren<ScanNodeProperties>();
            if (scan != null) scan.subText = $"x{Mathf.Max(Count, 0)}";
            if (IsOwner && isHeld && !isPocketed && playerHeldBy == GameNetworkManager.Instance?.localPlayerController) SetControlTipsForItem();
        }

        public override void SetControlTipsForItem()
        {
            string[] tips;
            string use = Plugin.PlaceWithLeftClick.Value ? "[LMB]" : "[Right-click]";
            string brk = Plugin.PlaceWithLeftClick.Value ? null : "Break : hold [LMB]";
            if (Food != null) tips = new[] { $"Eat : hold {use}", $"{DisplayName} x{Count}" };
            else tips = new[] { $"Place : {use}", brk ?? "", $"{DisplayName} x{Count}" };
            string key = string.Join("|", tips);
            lastTip = key;
            // the game only rewrites the lines it's given: pad so a shorter list blanks what an earlier, longer one left
            var lines = HUDManager.Instance.controlTipLines;
            int room = lines != null ? lines.Length - 1 : tips.Length;
            if (tips.Length < room) { var padded = new string[room]; for (int i = 0; i < room; i++) padded[i] = i < tips.Length ? tips[i] : ""; tips = padded; }
            HUDManager.Instance.ChangeControlTipMultiple(tips, holdingItem: true, itemProperties);
        }

        public override void ItemActivate(bool used, bool buttonDown = true) { }

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

    public class ToolItem : GrabbableObject
    {
        public ToolKind Kind = ToolKind.Pickaxe;
        public int Tier = 1;
        public float Speed = 4f;
        public string ItemKey;
        void Awake() => SpawnFix.Clear(gameObject);
        Transform model;
        Quaternion baseRot;
        float swingT = 1f;

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
            Swing();
        }

        public void PlaySwingAnim() => swingT = 0f;

        void Swing()
        {
            PlaySwingAnim();
            Sounds.Play("swing", transform.position, 0.5f, Random.Range(0.9f, 1.1f));
            var p = playerHeldBy;
            var cam = p.gameplayCamera.transform;
            var hits = Physics.SphereCastAll(cam.position + cam.right * -0.35f, 0.75f, cam.forward, 1.6f, 1084754248, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).ToList();
            var hitEnemies = new HashSet<EnemyAI>();
            foreach (var h in hits)
            {
                if (h.transform.gameObject.layer == 8 || h.transform.gameObject.layer == 11) continue;
                if (!h.transform.TryGetComponent<IHittable>(out var hittable)) continue;
                if (h.transform == p.transform) continue;
                if (h.point != Vector3.zero && Physics.Linecast(cam.position, h.point, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
                var col = h.transform.GetComponent<EnemyAICollisionDetect>();
                if (col != null) { if (col.mainScript == null || hitEnemies.Contains(col.mainScript)) continue; hitEnemies.Add(col.mainScript); }
                try
                {
                    if (hittable.Hit(1, cam.forward, p, true, 1))
                    {
                        Sounds.Play("attack", h.point, 0.7f, 1f);
                        Survival.AddExhaustion(0.1f);
                        break;
                    }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("pickaxe hit: " + e.Message); }
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

    public class FlintAndSteelItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);

        // ignition is driven by Builder (left-click on a block) so it shares one input path
        public override void ItemActivate(bool used, bool buttonDown = true) { }

        public void Strike(BlockKey target, bool hasTarget)
        {
            Sounds.Play("ignite", transform.position, 0.8f, Random.Range(0.9f, 1.1f));
            if (!hasTarget) return;
            var bi = BlockWorld.Instance?.Get(target);
            if (bi != null && bi.Data.Def == Blocks.TNT) BlockNet.RequestIgnite(target);
        }
    }

    public class OreScrapItem : GrabbableObject
    {
        void Awake() => SpawnFix.Clear(gameObject);
        public override void Start()
        {
            base.Start();
            if (BlockNet.PendingScrap.TryGetValue(NetworkObjectId, out int v))
            {
                BlockNet.PendingScrap.Remove(NetworkObjectId);
                SetScrapValue(v);
                if (RoundManager.Instance != null) RoundManager.Instance.totalScrapValueInLevel += v;
            }
        }
    }
}
