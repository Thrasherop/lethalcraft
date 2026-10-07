using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>Local player's block interaction: targeting outline, mining (hold LMB), placing (LMB with a block stack).</summary>
    public class Builder : MonoBehaviour
    {
        public static Builder Instance;
        public bool HasTarget;           // looking at a placed block
        public BlockKey TargetKey;
        bool hasSurface;                 // looking at any surface within reach
        RaycastHit surfaceHit;
        GameObject outline, crack;
        BlockKey miningKey;
        bool mining;
        float progress, hitSoundTimer, breakCooldown, placeCooldown;
        sbyte lastStage = -1;
        InputAction activate, interact, secondary;
        bool rmbLatch, prevRmb;
        void OnSecondary(InputAction.CallbackContext c) { rmbLatch = true; }

        /// <summary>Right mouse (the game's "PingScan" action) is held.</summary>
        public static bool RmbHeld => Instance != null && ((Instance.secondary != null && Instance.secondary.IsPressed()) || DevServer.RmbHeld);
        float useCooldown;
        ToolItem miningTool;
        bool prevLmb;
        bool activateLatch, interactLatch;
        void OnActivate(InputAction.CallbackContext c) { activateLatch = true; }
        void OnInteract(InputAction.CallbackContext c) { interactLatch = true; }
        public static string LastPlaceFailReason = "";
        public static string LastBlocker = "";
        public static bool GroundPlacement;
        public string Probe()
        {
            if (!hasSurface || surfaceHit.collider == null) return "no surface";
            var c = surfaceHit.collider;
            var g = TerrainCarver.GroundObject(c);
            var mc = g != null ? g.GetComponent<MeshCollider>() : null;
            bool ok = mc != null && TerrainCarver.CanCarve(mc, out string why2);
            string why = ""; if (mc != null) TerrainCarver.CanCarve(mc, out why);
            return $"hit={c.name} tag={c.tag} layer={LayerMask.LayerToName(c.gameObject.layer)} ground={(g != null ? g.name : "-")} canCarve={ok} why='{why}' normal={surfaceHit.normal} dist={surfaceHit.distance:F2} hasTarget={HasTarget}";
        }

        public float Reach => Mathf.Max(4.5f * Plugin.S, 4.5f);

        void Awake()
        {
            Instance = this;
            outline = new GameObject("LMC_Outline");
            outline.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.Outline();
            var mr = outline.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Atlas.Outline;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.SetActive(false);
            crack = new GameObject("LMC_Crack");
            crack.AddComponent<MeshFilter>();
            var cmr = crack.AddComponent<MeshRenderer>();
            cmr.sharedMaterial = Atlas.Crack;
            cmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            crack.SetActive(false);
        }

        void Start()
        {
            if (BlockWorld.Instance != null) BlockWorld.Instance.OnBlockMoved += CarryPlayer;
        }

        void OnDestroy()
        {
            if (activate != null) activate.performed -= OnActivate;
            if (secondary != null) secondary.performed -= OnSecondary;
            if (interact != null) interact.performed -= OnInteract;
            if (Instance == this) Instance = null;
            if (outline != null) Destroy(outline);
            if (crack != null) Destroy(crack);
            if (BlockWorld.Instance != null) BlockWorld.Instance.OnBlockMoved -= CarryPlayer;
        }

        public bool IsMiningWith(ToolItem p) => mining && miningTool == p;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        public static bool CanAct(PlayerControllerB p)
        {
            if (p == null || !p.isPlayerControlled || p.isPlayerDead) return false;
            if (p.inTerminalMenu || p.isTypingChat || p.inSpecialInteractAnimation || p.isGrabbingObjectAnimation || p.inSpecialMenu) return false;
            if (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen) return false;
            if (p.isClimbingLadder || p.inAnimationWithEnemy != null) return false;
            return true;
        }

        int RayMask => (1 << 8) | (1 << 11) | (1 << 0) | (1 << 25) | (1 << 26) | (1 << 28);

        void Update()
        {
            var p = Local;
            Facility.Tick(p);
            var world = BlockWorld.Instance;
            if (p == null || world == null || !CanAct(p))
            {
                Clear();
                return;
            }
            if (activate == null)
            {
                try
                {
                    activate = IngamePlayerSettings.Instance.playerInput.actions.FindAction("ActivateItem");
                    if (activate != null) activate.performed += OnActivate;
                }
                catch { }
            }
            if (secondary == null)
            {
                try
                {
                    secondary = IngamePlayerSettings.Instance.playerInput.actions.FindAction("PingScan");
                    if (secondary != null) secondary.performed += OnSecondary;
                }
                catch { }
            }
            if (interact == null)
            {
                try
                {
                    interact = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Interact");
                    if (interact != null) interact.performed += OnInteract;
                }
                catch { }
            }
            breakCooldown -= Time.deltaTime;
            placeCooldown -= Time.deltaTime;
            useCooldown -= Time.deltaTime;

            Raycast(p);
            UpdateOutline();

            // [E] on levers / buttons / note blocks
            bool ePressed = interactLatch && Time.time - CraftingUI.LastClosed > 0.35f;
            interactLatch = false;
            if (HasTarget && ePressed && useCooldown <= 0f)
            {
                var tb = world.Get(TargetKey);
                if (tb != null && tb.Data.Def == Blocks.CraftingTable) { CraftingUI.Open(true); useCooldown = 0.3f; }
                else if (tb != null && tb.Data.Def == Blocks.Furnace) { UseFurnace(p, TargetKey); useCooldown = 0.3f; }
                else if (tb != null && tb.Data.Def == Blocks.Chest) { ChestUI.Open(TargetKey); useCooldown = 0.3f; }
                else if (tb != null && BlockWorld.HoverTip(tb) != null)
                {
                    BlockNet.RequestUse(TargetKey);
                    useCooldown = 0.25f;
                }
            }

            var held = p.isHoldingObject ? p.currentlyHeldObjectServer : null;
            bool latched = activateLatch;
            activateLatch = false;
            bool lmb = (activate != null && activate.IsPressed()) || DevServer.LmbHeld || latched;
            bool lmbDown = latched || (lmb && !prevLmb);
            prevLmb = lmb;

            bool rLatched = rmbLatch || DevServer.RmbClick;
            rmbLatch = false; DevServer.RmbClick = false;
            bool rmb = RmbHeld || rLatched;
            bool rmbDown = rLatched || (rmb && !prevRmb);
            prevRmb = rmb;

            // Minecraft controls: right-click places, left-click breaks (bare-hand speed unless holding a pickaxe)
            bool placeWithLeft = Plugin.PlaceWithLeftClick.Value;
            if (held is StackItem st && st.Block != null)
            {
                bool placeBtn = placeWithLeft ? lmb : rmb;
                bool placeDown = placeWithLeft ? lmbDown : rmbDown;
                if (placeBtn && placeCooldown <= 0f && (placeDown || placeCooldown <= -0.05f))
                {
                    TryPlace(p, st);
                    placeCooldown = 0.22f;
                }
                if (!placeWithLeft && lmb && breakCooldown <= 0f) MineAny(p, null, lmbDown);
                else StopMining();
                return;
            }
            if (held is StackItem pearl && pearl.ItemKey == "ender_pearl")
            {
                // ender pearl: the place button throws it; left-click still breaks blocks
                if ((placeWithLeft ? lmbDown : rmbDown) && placeCooldown <= 0f) { EnderPearls.Throw(p); placeCooldown = 0.5f; }
                else if ((placeWithLeft ? lmbDown : rmbDown) && Plugin.DevMode.Value) Plugin.Log.LogWarning($"[dev] pearl click ignored: cooldown {placeCooldown:F2}");
                if (!placeWithLeft && lmb && breakCooldown <= 0f) MineAny(p, null, lmbDown);
                else StopMining();
                return;
            }
            if (held is StackItem)
            {
                // food: right-click eats (Survival); left-click still breaks blocks
                if (lmb && breakCooldown <= 0f && !Survival.EatingNow) MineAny(p, null, lmbDown);
                else StopMining();
                return;
            }
            if (held is FlintAndSteelItem flint)
            {
                // like Minecraft: the use button (right-click) strikes it; left-click still breaks blocks
                if ((placeWithLeft ? lmbDown : rmbDown) && placeCooldown <= 0f) { flint.Strike(TargetKey, HasTarget); placeCooldown = 0.3f; }
                if (!placeWithLeft && lmb && breakCooldown <= 0f) MineAny(p, null, lmbDown);
                else StopMining();
                return;
            }
            if (held == null || held is ToolItem)
            {
                if (lmb && breakCooldown <= 0f) MineAny(p, held as ToolItem, lmbDown);
                else StopMining();
                return;
            }
            StopMining();
        }

        /// <summary>[E] on a furnace: load the held item (whole stack) as input/fuel, or take the output with an empty hand.</summary>
        static void UseFurnace(PlayerControllerB p, BlockKey k)
        {
            var held = p.isHoldingObject ? p.currentlyHeldObjectServer : null;
            string key = Crafting.KeyOf(held);
            bool usable = key != null && (Crafting.SmeltResult.ContainsKey(key) || Crafting.FuelSeconds.ContainsKey(key));
            if (!usable)
            {
                if (Crafting.Furnaces.TryGetValue(k, out var f) && f.OutCount > 0) BlockNet.RequestFurnaceTake(k);
                else McHud.Toast(held == null ? "Nothing to take yet." : "That doesn't go in a furnace.");
                return;
            }
            if (held is StackItem st) BlockNet.RequestFurnaceInsert(k, key, st.NetworkObjectId, st.Count);
            else
            {
                // single items (raw ore): hand it over, then remove it from the hotbar
                BlockNet.RequestFurnaceInsert(k, key, 0, 1);
                p.DestroyItemInSlotAndSync(p.currentItemSlot);
            }
        }

        void LateUpdate()
        {
            // show a Lethal-Company style hover tip for usable blocks (runs after the game's own hover logic)
            var p = Local;
            if (p == null || !HasTarget || BlockWorld.Instance == null || p.cursorTip == null) return;
            var bi = BlockWorld.Instance.Get(TargetKey);
            if (bi == null) return;
            var tip = BlockWorld.HoverTip(bi);
            if (tip == null) return;
            p.cursorTip.text = tip;
            if (p.cursorIcon != null && HudAssets.HandIcon != null)
            {
                p.cursorIcon.enabled = true;
                p.cursorIcon.sprite = HudAssets.HandIcon;
            }
        }

        void Clear()
        {
            HasTarget = false;
            hasSurface = false;
            if (outline.activeSelf) outline.SetActive(false);
            StopMining();
        }

        void Raycast(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            var ray = new Ray(cam.position, cam.forward);
            HasTarget = false;
            hasSurface = false;
            float best = float.MaxValue;
            // solid (non-trigger) colliders on the interactable layer, like closed doors, also block the ray
            foreach (var hit in Physics.RaycastAll(ray, Reach, RayMask | (1 << BlockWorld.NonSolidLayer), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                // ignore our own body (the player rig has colliders that a straight-down look would hit)
                if (hit.collider.transform.IsChildOf(p.transform)) continue;
                if (hit.collider.GetComponentInParent<GrabbableObject>() != null) continue;
                best = hit.distance;
                surfaceHit = hit;
                hasSurface = true;
                break;
            }
            // non-solid blocks are triggers on layer 9
            var trig = Physics.RaycastAll(ray, Mathf.Min(best, Reach), 1 << BlockWorld.NonSolidLayer, QueryTriggerInteraction.Collide);
            foreach (var t in trig.OrderBy(t => t.distance))
            {
                if (t.collider.GetComponent<BlockRef>() == null) continue;
                if (t.distance < best) { best = t.distance; surfaceHit = t; hasSurface = true; }
                break;
            }
            if (hasSurface)
            {
                var br = surfaceHit.collider.GetComponent<BlockRef>();
                if (br != null && BlockWorld.Instance.Has(br.Key))
                {
                    HasTarget = true;
                    TargetKey = br.Key;
                }
            }
        }

        /// <summary>Place an overlay (outline / cracks) over a block without parenting it (blocks get destroyed).</summary>
        public static void Overlay(GameObject ov, BlockInstance bi)
        {
            var t = bi.Go.transform;
            bool solid = bi.Data.Def.Solid;
            Vector3 c = solid ? bi.Col.center : bi.Mf.sharedMesh.bounds.center;
            Vector3 size = solid ? bi.Col.size : bi.Mf.sharedMesh.bounds.size + Vector3.one * 0.02f;
            ov.transform.position = t.TransformPoint(c);
            ov.transform.rotation = t.rotation;
            ov.transform.localScale = Vector3.Scale(t.lossyScale, size);
        }

        void EnsureOverlays()
        {
            if (outline == null || crack == null) { if (outline != null) Destroy(outline); if (crack != null) Destroy(crack); Awake(); }
        }

        void UpdateOutline()
        {
            EnsureOverlays();
            if (!HasTarget) { if (outline.activeSelf) outline.SetActive(false); return; }
            var bi = BlockWorld.Instance.Get(TargetKey);
            if (bi == null || bi.Go == null) { outline.SetActive(false); return; }
            Overlay(outline, bi);
            if (!outline.activeSelf) outline.SetActive(true);
        }

        // ------------------------------------------------------------------ mining
        public static float BreakTime(BlockDef def, ToolItem tool) =>
            tool != null ? Blocks.BreakTime(def, tool.Kind, tool.Tier, tool.Speed) : Blocks.BreakTime(def, ToolKind.None, 0, 1f);

        void Mine(PlayerControllerB p, ToolItem tool)
        {
            var world = BlockWorld.Instance;
            var bi = world.Get(TargetKey);
            if (bi == null) { StopMining(); return; }
            if (!mining || !miningKey.Equals(TargetKey))
            {
                StopMining();
                mining = true;
                miningKey = TargetKey;
                progress = 0f;
                hitSoundTimer = 0f;
            }
            miningTool = tool;
            var def = bi.Data.Def;
            if (def.Unbreakable) { if (progress == 0f) { McHud.Toast(def.Name + " can't be broken."); progress = 0.0001f; } return; }
            float t = BreakTime(def, tool);
            if (Survival.Instance != null && Survival.Instance.Hunger <= 0) t *= 1.15f;
            progress += Time.deltaTime / Mathf.Max(0.05f, t);
            hitSoundTimer -= Time.deltaTime;
            var center = world.WorldCenter(TargetKey);
            if (hitSoundTimer <= 0f)
            {
                hitSoundTimer = 0.25f;
                Sounds.Play("step." + Sounds.Family(def), center, 0.3f, 0.55f);
                if (surfaceHit.collider != null) world.SpawnParticles(def, surfaceHit.point, 2);
                tool?.PlaySwingAnim();
            }
            sbyte stage = (sbyte)Mathf.Clamp((int)(progress * 10f), 0, 9);
            if (stage != lastStage)
            {
                lastStage = stage;
                BlockNet.SendMineProgress(TargetKey, stage);
            }
            // crack overlay
            EnsureOverlays();
            Overlay(crack, bi);
            crack.GetComponent<MeshFilter>().sharedMesh = MeshBuilder.Crack(stage);
            if (!crack.activeSelf) crack.SetActive(true);

            if (progress >= 1f)
            {
                BlockNet.RequestBreak(TargetKey, tool != null);
                Survival.AddExhaustion(0.005f);
                StopMining();
                breakCooldown = t < 0.1f ? 0.1f : 0.25f;
            }
        }

        // ------------------------------------------------------------------ digging the ground itself
        bool groundMining;
        Vector3Int groundCell;
        BlockDef groundDef;
        float groundY;

        void MineAny(PlayerControllerB p, ToolItem tool, bool justPressed)
        {
            if (HasTarget) { groundMining = false; Mine(p, tool); return; }
            if (!hasSurface || surfaceHit.collider == null || !Plugin.AllowDigging.Value) { StopMining(); return; }
            var groundGo = TerrainCarver.GroundObject(surfaceHit.collider);
            string why = "";
            if (groundGo == null || !TerrainCarver.CanCarve(groundGo, out why))
            {
                if (justPressed && tool == null && !string.IsNullOrEmpty(why) && why != "Too hard to dig here.") McHud.Toast(why);
                StopMining();
                return;
            }
            float S = Plugin.S;
            var cell = Ground.PickCell(surfaceHit.point, surfaceHit.normal, out _);
            // a natural block (molded edge) can sit right under the uncut surface: mine that block
            var under = Ground.KeyOf(cell);
            if (BlockWorld.Instance.Has(under)) { groundMining = false; HasTarget = true; TargetKey = under; Mine(p, tool); return; }
            if (!groundMining || cell != groundCell)
            {
                if (Ground.IsBedrock(cell))
                {
                    if (justPressed) McHud.Toast("Bedrock: can't dig here.");
                    StopMining();
                    return;
                }
                groundDef = Ground.EstimateAt(cell, groundGo);
            }
            var def = groundDef ?? Blocks.Dirt;
            if (!groundMining || cell != groundCell)
            {
                StopMining();
                groundMining = true;
                groundCell = cell;
                groundY = surfaceHit.point.y;
                progress = 0f;
                hitSoundTimer = 0f;
            }
            mining = false;
            miningTool = tool;
            float t = BreakTime(def, tool);
            if (Survival.Instance != null && Survival.Instance.Hunger <= 0) t *= 1.15f;
            progress += Time.deltaTime / Mathf.Max(0.05f, t);
            hitSoundTimer -= Time.deltaTime;
            if (hitSoundTimer <= 0f)
            {
                hitSoundTimer = 0.25f;
                Sounds.Play("step." + Sounds.Family(def), surfaceHit.point, 0.3f, 0.55f);
                BlockWorld.Instance.SpawnParticles(def, surfaceHit.point, 2);
                tool?.PlaySwingAnim();
            }
            sbyte stage = (sbyte)Mathf.Clamp((int)(progress * 10f), 0, 9);
            EnsureOverlays();
            crack.transform.position = new Vector3((cell.x + 0.5f) * S, (cell.y + 0.5f) * S, (cell.z + 0.5f) * S);
            crack.transform.rotation = Quaternion.identity;
            crack.transform.localScale = Vector3.one * S;
            crack.GetComponent<MeshFilter>().sharedMesh = MeshBuilder.Crack(stage);
            if (!crack.activeSelf) crack.SetActive(true);
            if (progress >= 1f)
            {
                BlockNet.RequestGroundDig(surfaceHit.point, surfaceHit.normal);
                Survival.AddExhaustion(0.005f);
                StopMining();
                breakCooldown = 0.25f;
            }
        }

        void StopMining()
        {
            groundMining = false;
            if (mining && lastStage >= 0) BlockNet.SendMineProgress(miningKey, -1);
            mining = false;
            miningTool = null;
            progress = 0;
            lastStage = -1;
            if (crack != null && crack.activeSelf) crack.SetActive(false);
        }

        // ------------------------------------------------------------------ placing
        /// <summary>Dev: what a right-click with the held block would do right now.</summary>
        public string DebugPlace(PlayerControllerB p)
        {
            var st = p.currentlyHeldObjectServer as StackItem;
            if (st == null || st.Block == null) return "not holding a block";
            if (!hasSurface) return "no surface in reach";
            var hitInfo = $"hit={surfaceHit.collider?.name} n={surfaceHit.normal} d={surfaceHit.distance:F2}";
            bool ok = ComputePlacement(p, st.Block, out var key, out var facing);
            return $"{hitInfo} ok={ok} key={key} facing={facing} has={BlockWorld.Instance.Has(key)} fail='{LastPlaceFailReason}' blocker='{LastBlocker}'";
        }

        void TryPlace(PlayerControllerB p, StackItem st)
        {
            if (!hasSurface) return;
            var def = st.Block;
            if (!ComputePlacement(p, def, out var key, out var facing))
            {
                if (!string.IsNullOrEmpty(LastPlaceFailReason)) McHud.Toast(LastPlaceFailReason);
                return;
            }
            BlockNet.RequestPlace(st, key, def.Id, facing);
        }

        public bool ComputePlacement(PlayerControllerB p, BlockDef def, out BlockKey key, out byte facing)
        {
            LastPlaceFailReason = "";
            LastBlocker = "";
            var world = BlockWorld.Instance;
            key = default;
            facing = (byte)Face.Up;
            var cam = p.gameplayCamera.transform;
            byte frame;
            int face;
            var br = surfaceHit.collider.GetComponent<BlockRef>();
            if (br != null && world.Has(br.Key))
            {
                frame = br.Key.Frame;
                var nLocal = world.ToFrameLocalDir(frame, surfaceHit.normal);
                face = Faces.FromVector(nLocal);
                var target = world.Get(br.Key);
                if (!target.Data.Def.Solid)
                {
                    // clicking a torch/dust/etc: build on its supporting position instead
                    key = br.Key;
                    if (world.Has(key)) { key = br.Key.Offset(face); }
                }
                else key = br.Key.Offset(face);
            }
            else
            {
                bool inShip = BlockWorld.InShip(surfaceHit.point) || ShipAttach.IsShipCollider(surfaceHit.collider);
                frame = (byte)(inShip ? 1 : 0);
                if (frame == 0 && !world.WorldFrameAvailable) { LastPlaceFailReason = "Blocks can only be placed in the ship while in orbit."; return false; }
                world.FrameRoot(frame, true);
                float S = Plugin.S;
                var lp = world.ToFrameLocal(frame, surfaceHit.point) / S;
                var n = world.ToFrameLocalDir(frame, surfaceHit.normal);
                face = Faces.FromVector(n);
                Vector3 pc = (n.y > 0.7f || n.y < -0.7f) ? lp : lp + n * 0.5f;
                int cx = Mathf.FloorToInt(pc.x), cz = Mathf.FloorToInt(pc.z);
                float yBottom;
                GroundPlacement = n.y > 0.7f;
                if (n.y > 0.7f)
                {
                    // rest the block on the highest ground point under its footprint (terrain is rarely flat)
                    yBottom = lp.y;
                    var down = world.FrameDirToWorld(frame, Vector3.down);
                    for (int sx = 0; sx < 3; sx++)
                        for (int sz = 0; sz < 3; sz++)
                        {
                            var probeLocal = new Vector3(cx + 0.1f + sx * 0.4f, lp.y + 0.6f, cz + 0.1f + sz * 0.4f) * S;
                            var probe = world.FrameRoot(frame, true).TransformPoint(probeLocal);
                            if (Physics.Raycast(probe, down, out var gh, 1.2f * S, RayMask, QueryTriggerInteraction.Ignore) && gh.collider.GetComponent<BlockRef>() == null)
                                yBottom = Mathf.Max(yBottom, world.ToFrameLocal(frame, gh.point).y / S);
                        }
                }
                else if (n.y < -0.7f) yBottom = lp.y - 1f;
                else
                {
                    // wall: align with the floor below so it lines up with blocks standing on that floor
                    var origin = surfaceHit.point + surfaceHit.normal * 0.5f * S;
                    var down = world.FrameDirToWorld(frame, Vector3.down);
                    if (Physics.Raycast(origin, down, out var fh, 3.5f * S, RayMask, QueryTriggerInteraction.Ignore) && fh.collider.GetComponent<BlockRef>() == null)
                    {
                        float floorY = world.ToFrameLocal(frame, fh.point).y / S;
                        yBottom = floorY + Mathf.Floor(lp.y - floorY);
                    }
                    else yBottom = Mathf.Floor(lp.y);
                }
                int cellY = Mathf.FloorToInt(yBottom);
                int yoff = Mathf.RoundToInt((yBottom - cellY) * 1000f);
                if (yoff >= 1000) { yoff -= 1000; cellY++; }
                // join the grid of a nearby structure so separate placements line up (allow sinking 1/4 block into the ground)
                BlockKey? near = null; float bestD = 8.5f;
                foreach (var k in world.Blocks.Keys)
                {
                    if (k.Frame != frame) continue;
                    float d = new Vector2(k.Pos.x - cx, k.Pos.z - cz).magnitude + Mathf.Abs(k.Pos.y + k.YOff / 1000f - yBottom) * 0.5f;
                    if (d < bestD) { bestD = d; near = k; }
                }
                if (near.HasValue)
                {
                    float g = near.Value.YOff / 1000f;
                    int cy2 = Mathf.CeilToInt(yBottom - g - 0.25f);
                    cellY = cy2;
                    yoff = near.Value.YOff;
                }
                key = new BlockKey(frame, (short)yoff, new Vector3Int(Mathf.FloorToInt(pc.x), cellY, Mathf.FloorToInt(pc.z)));
            }

            // facing
            var lookLocal = world.ToFrameLocalDir(frame, cam.forward);
            switch (def.Shape)
            {
                case BlockShape.Cube:
                    if (def == Blocks.Piston || def == Blocks.StickyPiston) facing = Faces.FromVector(-lookLocal);
                    else if (def == Blocks.Log) facing = (byte)face;
                    else if (def.Directional && !def.FacingIncludesVertical) facing = Faces.FromVectorHorizontal(-lookLocal);
                    else facing = (byte)Face.Up;
                    break;
                case BlockShape.Torch:
                    if (face == (int)Face.Down) { LastPlaceFailReason = "Torches can't hang from ceilings."; return false; }
                    facing = (byte)face;
                    break;
                case BlockShape.Lever:
                case BlockShape.Button:
                    facing = (byte)face;
                    break;
                case BlockShape.Dust:
                case BlockShape.Plate:
                    if (face != (int)Face.Up) { LastPlaceFailReason = ""; return false; }
                    facing = (byte)Face.Up;
                    break;
            }

            if (world.Has(key)) return false;
            if (!CellFree(key, def, p)) { LastPlaceFailReason = ""; return TryNudge(p, def, ref key); }
            return true;
        }

        bool TryNudge(PlayerControllerB p, BlockDef def, ref BlockKey key)
        {
            // only for floor placement on level geometry: try the neighbor cells closest to where we clicked
            if (surfaceHit.collider.GetComponent<BlockRef>() != null) return false;
            var world = BlockWorld.Instance;
            var n = world.ToFrameLocalDir(key.Frame, surfaceHit.normal);
            if (n.y < 0.7f) return false;
            var orig = key;
            var hitLocal = world.ToFrameLocal(key.Frame, surfaceHit.point);
            var options = new List<BlockKey>();
            for (int i = 2; i < 6; i++) options.Add(orig.Offset(i));
            foreach (var o in options.OrderBy(o => (world.LocalCenter(o) - hitLocal).sqrMagnitude))
            {
                if (world.Has(o)) continue;
                if (CellFree(o, def, p) && ServerLogic.Supported(o)) { key = o; return true; }
            }
            return false;
        }

        bool CellFree(BlockKey key, BlockDef def, PlayerControllerB p)
        {
            var world = BlockWorld.Instance;
            var c = world.WorldCenter(key);
            var rot = world.FrameRotation(key.Frame);
            float S = Plugin.S;
            int mask = (1 << 8) | (1 << 11) | (1 << 0) | (1 << 26) | (1 << 28);
            if (def.Solid) mask |= (1 << 3) | (1 << 19);
            var half = Vector3.one * (S * 0.5f * (def.Solid ? 0.94f : 0.5f));
            var hits = Physics.OverlapBox(c, half, rot, mask, def.Solid ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
            // level geometry may poke into the bottom quarter (uneven ground); players/enemies use the full box
            var upper = c + rot * Vector3.up * (S * 0.15f);
            var halfUpper = new Vector3(half.x * 0.92f, half.y - S * 0.15f, half.z * 0.92f);
            var geomHits = new HashSet<Collider>(Physics.OverlapBox(upper, halfUpper, rot, mask & ~((1 << 3) | (1 << 19)), QueryTriggerInteraction.Ignore));
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<BlockRef>() != null) continue;
                if (h.GetComponentInParent<GrabbableObject>() != null) continue;
                var pl = h.GetComponentInParent<PlayerControllerB>();
                if (pl != null)
                {
                    if (!def.Solid) continue; // like Minecraft: torches, levers, dust... can go where someone stands
                    if (!pl.isPlayerControlled || pl.isPlayerDead) continue;
                    if (h.isTrigger && h.gameObject.layer != 3) continue;
                    LastPlaceFailReason = pl == p ? "" : "Someone is standing there.";
                    return false;
                }
                if (h.gameObject.layer == 19)
                {
                    var e = h.GetComponentInParent<EnemyAICollisionDetect>();
                    if (e == null || e.mainScript == null || e.mainScript.isEnemyDead) continue;
                    return false;
                }
                if (h.isTrigger) continue;
                if (!geomHits.Contains(h)) continue; // only grazes the bottom of the cell
                LastBlocker = h.name + "@" + LayerMask.LayerToName(h.gameObject.layer);
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ moving platforms
        void CarryPlayer(BlockInstance bi, Vector3 delta)
        {
            var p = Local;
            if (p == null || !p.isPlayerControlled || p.isPlayerDead || bi.Go == null) return;
            var cc = p.thisController;
            if (cc == null) return;
            float S = Plugin.S;
            var center = bi.Go.transform.position;
            var pb = cc.bounds;
            var feet = new Vector3(pb.center.x, pb.min.y, pb.center.z);
            var local = Quaternion.Inverse(bi.Go.transform.rotation) * (feet - center);
            // standing on top (in block space allow any orientation: use world up)
            var top = center.y + S * 0.5f;
            bool onTop = Mathf.Abs(feet.x - center.x) < S * 0.5f + cc.radius * 0.6f &&
                         Mathf.Abs(feet.z - center.z) < S * 0.5f + cc.radius * 0.6f &&
                         feet.y > top - 0.3f - Mathf.Max(0, -delta.y) && feet.y < top + 0.35f + Mathf.Max(0, delta.y);
            var blockBounds = new Bounds(center, Vector3.one * S * 0.98f);
            bool overlapping = blockBounds.Intersects(pb);
            if (onTop || overlapping)
            {
                var move = delta;
                if (overlapping && !onTop)
                {
                    // shove sideways out of the block's path, never pull down
                    move.y = Mathf.Max(0, move.y);
                }
                if (move.y > 0) p.fallValue = Mathf.Max(p.fallValue, 0f);
                cc.Move(move);
            }
        }
    }
}
