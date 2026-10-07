using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace LethalMinecraft
{
    public struct BlockKey : IEquatable<BlockKey>
    {
        public byte Frame;   // 0 = moon/world grid, 1 = ship grid (moves with the ship)
        public short YOff;   // vertical sub-grid offset in 1/1000 block (lets floors of any height align)
        public Vector3Int Pos;

        public BlockKey(byte frame, short yoff, Vector3Int pos) { Frame = frame; YOff = yoff; Pos = pos; }
        public BlockKey Offset(int face) => new BlockKey(Frame, YOff, Pos + Faces.Dir[face]);
        public BlockKey Offset(Vector3Int d) => new BlockKey(Frame, YOff, Pos + d);
        public bool Equals(BlockKey o) => Frame == o.Frame && YOff == o.YOff && Pos == o.Pos;
        public override bool Equals(object obj) => obj is BlockKey k && Equals(k);
        public override int GetHashCode() => (((Pos.x * 73856093) ^ (Pos.y * 19349663) ^ (Pos.z * 83492791)) * 31 + YOff) * 7 + Frame;
        public override string ToString() => $"[{Frame}:{YOff}] {Pos}";
    }

    public struct BlockData
    {
        public byte Type, Facing, State;
        public BlockData(byte type, byte facing = 1, byte state = 0) { Type = type; Facing = facing; State = state; }
        public BlockDef Def => Blocks.Get(Type);
    }

    /// <summary>Marks a collider as belonging to a placed block.</summary>
    public class BlockRef : MonoBehaviour
    {
        public BlockKey Key;
    }

    public class BlockInstance
    {
        public BlockKey Key;
        public BlockData Data;
        public GameObject Go;
        public MeshFilter Mf;
        public MeshRenderer Mr;
        public BoxCollider Col;
        public HDAdditionalLightData Light;
        public NavMeshObstacle Obstacle;
        public InteractTrigger Trigger;
        public int Variant;
        // movement animation (pistons / falling)
        public Vector3 AnimFrom, AnimTo;
        public float AnimT = 1f, AnimDur;
        public float FuseStart = -1f;
        public AudioSource FuseAudio;
        public float EnemyDamage;
        public float LightLumens;
    }

    public enum OpType : byte { Set = 1, Remove = 2, Move = 3, State = 4 }

    public struct Op
    {
        public OpType Type;
        public BlockKey Key, Key2;
        public BlockData Data;
        public byte Fx; // Remove: 1 = break effects; Move: duration in ticks (1 tick = 50ms); Set: 1 = slide in from Key2

        public static Op Set(BlockKey k, BlockData d) => new Op { Type = OpType.Set, Key = k, Data = d };
        public static Op SetSlide(BlockKey k, BlockData d, BlockKey from) => new Op { Type = OpType.Set, Key = k, Data = d, Key2 = from, Fx = 1 };
        public static Op Remove(BlockKey k, bool fx) => new Op { Type = OpType.Remove, Key = k, Fx = (byte)(fx ? 1 : 0) };
        public static Op Move(BlockKey from, BlockKey to, byte ticks) => new Op { Type = OpType.Move, Key = from, Key2 = to, Fx = ticks };
        public static Op State(BlockKey k, BlockData d) => new Op { Type = OpType.State, Key = k, Data = d };
    }

    public class BlockWorld : MonoBehaviour
    {
        public static BlockWorld Instance;
        public readonly Dictionary<BlockKey, BlockInstance> Blocks = new Dictionary<BlockKey, BlockInstance>();
        readonly HashSet<BlockInstance> animating = new HashSet<BlockInstance>();
        Transform shipRoot, worldRoot;
        string worldRootScene;
        public static int SolidLayer = 8;      // "Room" - players walk on it, items land on it, blocks LOS
        public static int NonSolidLayer = 9;   // "InteractableObject" - triggers for torches/levers/dust
        public Material FlashMaterial;
        readonly System.Random rng = new System.Random();

        public static float S => Plugin.S;
        public static float DevLightScale = 1f;
        /// <summary>Molded natural blocks: per-cell surface heights (0..255 of a block) on a MoldRes x MoldRes grid.</summary>
        public static readonly Dictionary<BlockKey, byte[]> Molds = new Dictionary<BlockKey, byte[]>();

        void Awake()
        {
            Instance = this;
            FlashMaterial = Atlas.MakeLit("LMC_Flash", false, false, true);
            FlashMaterial.SetTexture("_EmissiveColorMap", Atlas.Texture);
            HDMaterial.SetEmissiveIntensity(FlashMaterial, 7f, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================== frames
        public Transform FrameRoot(byte frame, bool create)
        {
            if (frame == 1)
            {
                if (shipRoot == null && create && StartOfRound.Instance != null)
                {
                    var go = new GameObject("LMC_ShipBlocks");
                    shipRoot = go.transform;
                    var ship = StartOfRound.Instance.elevatorTransform;
                    shipRoot.SetParent(ship, false);
                    shipRoot.localPosition = Vector3.zero;
                    shipRoot.localRotation = Quaternion.identity;
                    var ls = ship.lossyScale;
                    shipRoot.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                }
                return shipRoot;
            }
            if (worldRoot == null && create)
            {
                var go = new GameObject("LMC_WorldBlocks");
                worldRoot = go.transform;
                var lvl = StartOfRound.Instance != null ? StartOfRound.Instance.currentLevel : null;
                if (lvl != null && !StartOfRound.Instance.inShipPhase)
                {
                    var scene = SceneManager.GetSceneByName(lvl.sceneName);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        SceneManager.MoveGameObjectToScene(go, scene);
                        worldRootScene = lvl.sceneName;
                    }
                }
                go.AddComponent<WorldRootWatcher>();
            }
            return worldRoot;
        }

        class WorldRootWatcher : MonoBehaviour
        {
            void OnDestroy()
            {
                // moon unloaded => every world-frame block is gone
                Instance?.ForgetFrame(0);
            }
        }

        public void ForgetFrame(byte frame)
        {
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] ForgetFrame {frame}");
            var dead = new List<BlockKey>();
            foreach (var kv in Blocks) if (kv.Key.Frame == frame) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                if (Blocks.TryGetValue(k, out var bi))
                {
                    if (bi.Go != null) Destroy(bi.Go);
                    Blocks.Remove(k);
                    animating.Remove(bi);
                }
            }
            if (frame == 0) { worldRoot = null; TerrainCarver.Reset(); ServerLogic.ResetGround(); Molds.Clear(); Chests.ResetFrame(0); }
            foreach (var fk in Crafting.Furnaces.Keys.Where(k => k.Frame == frame).ToList()) Crafting.Furnaces.Remove(fk);
            Redstone.MarkDirty();
        }

        public bool WorldFrameAvailable =>
            StartOfRound.Instance != null && !StartOfRound.Instance.inShipPhase && StartOfRound.Instance.currentLevel != null &&
            SceneManager.GetSceneByName(StartOfRound.Instance.currentLevel.sceneName).isLoaded;

        public static bool InShip(Vector3 worldPos)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return false;
            if (sor.shipBounds != null && sor.shipBounds.bounds.Contains(worldPos)) return true;
            if (sor.shipInnerRoomBounds != null && sor.shipInnerRoomBounds.bounds.Contains(worldPos)) return true;
            return false;
        }

        public Vector3 LocalCenter(BlockKey k) => new Vector3((k.Pos.x + 0.5f) * S, (k.Pos.y + k.YOff / 1000f + 0.5f) * S, (k.Pos.z + 0.5f) * S);

        public Vector3 WorldCenter(BlockKey k)
        {
            var root = FrameRoot(k.Frame, false);
            var lc = LocalCenter(k);
            return root != null ? root.TransformPoint(lc) : lc;
        }

        public Quaternion FrameRotation(byte frame)
        {
            var r = FrameRoot(frame, false);
            return r != null ? r.rotation : Quaternion.identity;
        }

        public Vector3 ToFrameLocal(byte frame, Vector3 world)
        {
            var r = FrameRoot(frame, false);
            return r != null ? r.InverseTransformPoint(world) : world;
        }

        public Vector3 ToFrameLocalDir(byte frame, Vector3 dir)
        {
            var r = FrameRoot(frame, false);
            return r != null ? r.InverseTransformDirection(dir) : dir;
        }

        public Vector3 FrameDirToWorld(byte frame, Vector3 dir)
        {
            var r = FrameRoot(frame, false);
            return r != null ? r.TransformDirection(dir) : dir;
        }

        public BlockInstance Get(BlockKey k) => Blocks.TryGetValue(k, out var b) ? b : null;
        public bool Has(BlockKey k) => Blocks.ContainsKey(k);
        public BlockDef DefAt(BlockKey k) => Blocks.TryGetValue(k, out var b) ? b.Data.Def : null;

        /// <summary>Distinct vertical sub-grids in use in a frame (for snapping new structures onto existing ones).</summary>
        public IEnumerable<short> YOffsInUse(byte frame)
        {
            var seen = new HashSet<short>();
            foreach (var k in Blocks.Keys) if (k.Frame == frame && seen.Add(k.YOff)) yield return k.YOff;
        }

        // ================================================================== applying ops
        public void Apply(List<Op> ops)
        {
            foreach (var op in ops) Apply(op);
        }

        public void Apply(Op op)
        {
            switch (op.Type)
            {
                case OpType.Set:
                    {
                        if (Blocks.TryGetValue(op.Key, out var old)) DestroyInstance(old);
                        var bi = CreateInstance(op.Key, op.Data);
                        if (bi == null) return;
                        if (op.Fx == 1)
                        {
                            var from = LocalCenter(op.Key2);
                            StartAnim(bi, from, LocalCenter(op.Key), 0.1f);
                        }
                        RefreshNeighbors(op.Key);
                    }
                    break;
                case OpType.Remove:
                    if (Blocks.TryGetValue(op.Key, out var rem))
                    {
                        if (op.Fx == 1) BreakEffects(rem);
                        DestroyInstance(rem);
                        RefreshNeighbors(op.Key);
                    }
                    break;
                case OpType.Move:
                    if (Blocks.TryGetValue(op.Key, out var mv))
                    {
                        if (Blocks.TryGetValue(op.Key2, out var occupant) && occupant != mv) DestroyInstance(occupant);
                        Blocks.Remove(op.Key);
                        Molds.Remove(op.Key); // a pushed molded block becomes a plain block
                        var fromPos = mv.Go != null ? mv.Go.transform.localPosition : LocalCenter(op.Key);
                        mv.Key = op.Key2;
                        Blocks[op.Key2] = mv;
                        if (mv.Go != null)
                        {
                            foreach (var r in mv.Go.GetComponentsInChildren<BlockRef>(true)) r.Key = op.Key2;
                            var parent = FrameRoot(op.Key2.Frame, true);
                            if (mv.Go.transform.parent != parent) mv.Go.transform.SetParent(parent, false);
                        }
                        StartAnim(mv, fromPos, LocalCenter(op.Key2), Mathf.Max(1, op.Fx) * 0.05f);
                        RefreshNeighbors(op.Key);
                        RefreshNeighbors(op.Key2);
                    }
                    break;
                case OpType.State:
                    if (Blocks.TryGetValue(op.Key, out var st))
                    {
                        bool typeChanged = st.Data.Type != op.Data.Type;
                        st.Data = op.Data;
                        if (typeChanged) { DestroyInstance(st); CreateInstance(op.Key, op.Data); }
                        else UpdateVisual(st);
                        RefreshNeighbors(op.Key);
                    }
                    break;
            }
            if (BlockNet.IsServer)
            {
                Redstone.OnChanged(op.Key);
                if (op.Type == OpType.Move) Redstone.OnChanged(op.Key2);
            }
            Redstone.MarkDirty();
            Gravity.MarkDirty();
        }

        void StartAnim(BlockInstance bi, Vector3 from, Vector3 to, float dur)
        {
            bi.AnimFrom = from;
            bi.AnimTo = to;
            bi.AnimT = 0f;
            bi.AnimDur = dur;
            if (bi.Go != null) bi.Go.transform.localPosition = from;
            if (bi.Obstacle != null) bi.Obstacle.enabled = false;
            animating.Add(bi);
        }

        public event Action<BlockInstance, Vector3> OnBlockMoved; // world delta this frame (for carrying players)

        void Update()
        {
            ItemGravity.Tick();
            if (animating.Count > 0)
            {
                var done = new List<BlockInstance>();
                foreach (var bi in animating)
                {
                    if (bi.Go == null) { done.Add(bi); continue; }
                    var before = bi.Go.transform.position;
                    bi.AnimT += Time.deltaTime / Mathf.Max(0.01f, bi.AnimDur);
                    float t = Mathf.Clamp01(bi.AnimT);
                    bi.Go.transform.localPosition = Vector3.Lerp(bi.AnimFrom, bi.AnimTo, t);
                    var delta = bi.Go.transform.position - before;
                    if (delta.sqrMagnitude > 0) OnBlockMoved?.Invoke(bi, delta);
                    if (t >= 1f)
                    {
                        done.Add(bi);
                        if (bi.Obstacle != null) bi.Obstacle.enabled = true;
                    }
                }
                foreach (var d in done) animating.Remove(d);
            }

            // primed TNT flashing
            foreach (var bi in Blocks.Values)
            {
                if (bi.FuseStart < 0 || bi.Mr == null) continue;
                float el = Time.time - bi.FuseStart;
                bool flash = ((int)(el * 4f)) % 2 == 0;
                bi.Mr.sharedMaterial = flash ? FlashMaterial : (Outdoors(bi) ? Atlas.OpaqueAmb : Atlas.Opaque);
                float sc = 1f + Mathf.Clamp01((el - 3f)) * 0.12f;
                bi.Go.transform.localScale = Vector3.one * S * sc;
            }

            if (Plugin.IsServerNow) { Redstone.ServerTick(); Crafting.ServerTick(Time.deltaTime); }

            UpdateNearLights();

            // HDRP can reset a light's intensity during its first frames: re-assert it
            lightFixTimer -= Time.deltaTime;
            if (lightFixTimer <= 0f)
            {
                lightFixTimer = 0.5f;
                foreach (var b in Blocks.Values)
                    if (b.Light != null && b.Light.gameObject.activeSelf && !nearLights.Contains(b) && Mathf.Abs(b.Light.intensity - b.LightLumens) > 1f) ApplyLight(b);
            }

            ambientTimer -= Time.deltaTime;
            if (ambientTimer <= 0f)
            {
                ambientTimer = 1.5f;
                float ev = DesiredAmbientEV();
                if (Mathf.Abs(ev - Atlas.AmbientEV) > 0.05f) Atlas.SetAmbient(ev);
            }
        }

        float ambientTimer, lightFixTimer;

        static void ApplyLight(BlockInstance bi)
        {
            if (bi.Light == null) return;
            bi.Light.lightUnit = LightUnit.Lumen;
            bi.Light.intensity = bi.LightLumens * NearCameraDim(bi.Light.transform.position);
        }

        /// <summary>
        /// A torch right next to your face would blow out the whole view (light grows with 1/d^2 and the game's exposure
        /// is set for dark rooms): lights within ~2 blocks of your own camera are dimmed, like eyes adjusting.
        /// Only affects what you see; other players see the light normally.
        /// </summary>
        static float NearCameraDim(Vector3 lightPos)
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null || p.gameplayCamera == null) return 1f;
            float d = Vector3.Distance(p.gameplayCamera.transform.position, lightPos);
            float full = 2.2f * S;
            if (d >= full) return 1f;
            float t = d / full;
            return Mathf.Max(0.12f, t * t);
        }

        readonly List<BlockInstance> nearLights = new List<BlockInstance>();
        float nearLightScan;

        /// <summary>Re-applies the near-camera dimming to lights around the local player every frame.</summary>
        void UpdateNearLights()
        {
            var p = GameNetworkManager.Instance?.localPlayerController;
            if (p == null || p.gameplayCamera == null) return;
            var cam = p.gameplayCamera.transform.position;
            nearLightScan -= Time.deltaTime;
            if (nearLightScan <= 0f)
            {
                // which lights could matter (within ~4 blocks), refreshed a few times per second
                nearLightScan = 0.25f;
                foreach (var b in nearLights) if (b.Light != null) ApplyLight(b);
                nearLights.Clear();
                foreach (var b in Blocks.Values)
                    if (b.Light != null && b.Light.gameObject.activeSelf && (b.Light.transform.position - cam).sqrMagnitude < 16f * S * S) nearLights.Add(b);
            }
            foreach (var b in nearLights) if (b.Light != null) ApplyLight(b);
        }

        /// <summary>Ambient fill follows daylight: bright at noon, gone at night / during eclipses.</summary>
        static float DesiredAmbientEV()
        {
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase) return 1.0f;
            var tod = TimeOfDay.Instance;
            if (tod == null) return 1.0f;
            float t = tod.normalizedTimeOfDay;
            float day = t < 0.62f ? 1f : Mathf.Clamp01(1f - (t - 0.62f) / 0.2f);
            if (tod.currentLevelWeather == LevelWeatherType.Eclipsed) day *= 0.25f;
            else if (tod.currentLevelWeather == LevelWeatherType.Stormy || tod.currentLevelWeather == LevelWeatherType.Flooded || tod.currentLevelWeather == LevelWeatherType.Foggy) day *= 0.7f;
            return Mathf.Lerp(-6f, 2f, day);
        }

        // ================================================================== instances / visuals
        BlockInstance CreateInstance(BlockKey k, BlockData data)
        {
            var def = data.Def;
            if (def == null) return null;
            var root = FrameRoot(k.Frame, true);
            if (root == null) return null;
            var bi = new BlockInstance { Key = k, Data = data };
            var go = new GameObject("LMC_" + def.Key);
            go.transform.SetParent(root, false);
            go.transform.localPosition = LocalCenter(k);
            // natural ground is a hair larger than its cell: off the grid planes, so it never z-fights with a player block
            // built half into the same space, and neighbours overlap instead of leaving hairline gaps (only the faces a
            // dig exposes are drawn, so a gap would show straight through to the void)
            go.transform.localScale = Vector3.one * S * ((data.State & LethalMinecraft.Blocks.NaturalGround) != 0 ? 1.0005f : 1f);
            bi.Go = go;
            bi.Mf = go.AddComponent<MeshFilter>();
            bi.Mr = go.AddComponent<MeshRenderer>();
            bi.Mr.shadowCastingMode = def.Solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = go.AddComponent<BoxCollider>();
            bi.Col = col;
            go.AddComponent<BlockRef>().Key = k;
            if (def.Solid)
            {
                go.layer = SolidLayer;
                if (Plugin.BlocksBlockEnemyPaths.Value && def.Shape == BlockShape.Cube && (data.State & LethalMinecraft.Blocks.NaturalGround) == 0)
                {
                    var ob = go.AddComponent<NavMeshObstacle>();
                    ob.shape = NavMeshObstacleShape.Box;
                    ob.size = Vector3.one * 1.02f;
                    ob.carving = true;
                    ob.carveOnlyStationary = true;
                    ob.carvingMoveThreshold = 0.1f;
                    ob.carvingTimeToStationary = 0.2f;
                    bi.Obstacle = ob;
                }
            }
            else
            {
                go.layer = NonSolidLayer;
                col.isTrigger = true;
            }
            Blocks[k] = bi;
            UpdateVisual(bi);
            return bi;
        }

        void AttachTrigger(BlockInstance bi)
        {
            var def = bi.Data.Def;
            GameObject tgo;
            if (def == LethalMinecraft.Blocks.NoteBlock)
            {
                // separate trigger child slightly larger than the block, on the interactable layer
                tgo = new GameObject("trigger");
                tgo.transform.SetParent(bi.Go.transform, false);
                var c = tgo.AddComponent<BoxCollider>();
                c.isTrigger = true;
                c.size = Vector3.one * 1.04f;
                tgo.AddComponent<BlockRef>().Key = bi.Key;
            }
            else tgo = bi.Go;
            tgo.layer = NonSolidLayer;
            tgo.tag = "InteractTrigger";
            var trig = tgo.AddComponent<InteractTrigger>();
            trig.hoverIcon = HudAssets.HandIcon;
            trig.interactable = true;
            trig.oneHandedItemAllowed = true;
            trig.twoHandedItemAllowed = true;
            trig.interactCooldown = true;
            trig.cooldownTime = 0.25f;
            trig.holdInteraction = false;
            trig.onInteract = new InteractEvent();
            trig.onInteractEarly = new InteractEvent();
            trig.onStopInteract = new InteractEvent();
            trig.onCancelAnimation = new InteractEvent();
            trig.onInteractEarlyOtherClients = new InteractEvent();
            trig.holdingInteractEvent = new InteractEventFloat();
            var key = bi;
            trig.onInteract.AddListener(_ => BlockNet.RequestUse(key.Key));
            bi.Trigger = trig;
            UpdateHoverTip(bi);
        }

        void UpdateHoverTip(BlockInstance bi)
        {
            if (bi.Trigger == null) return;
            var def = bi.Data.Def;
            if (def == LethalMinecraft.Blocks.Lever) bi.Trigger.hoverTip = (bi.Data.State & 1) != 0 ? "Lever ON - flip : [E]" : "Lever OFF - flip : [E]";
            else if (def == LethalMinecraft.Blocks.Button) bi.Trigger.hoverTip = "Press button : [E]";
            else if (def == LethalMinecraft.Blocks.NoteBlock) bi.Trigger.hoverTip = $"Note block ({NoteName(bi.Data.State)}) - tune : [E]";
        }

        public static string HoverTip(BlockInstance bi)
        {
            var def = bi.Data.Def;
            if (def == LethalMinecraft.Blocks.Lever) return (bi.Data.State & 1) != 0 ? "Lever ON - flip : [E]" : "Lever OFF - flip : [E]";
            if (def == LethalMinecraft.Blocks.Button) return "Press button : [E]";
            if (def == LethalMinecraft.Blocks.NoteBlock) return $"Note block ({NoteName(bi.Data.State)}) - tune : [E]";
            if (def == LethalMinecraft.Blocks.CraftingTable) return "Crafting Table - craft : [E]";
            if (def == LethalMinecraft.Blocks.Furnace) return Crafting.Describe(bi.Key);
            if (def == LethalMinecraft.Blocks.Chest) return Chests.Describe(bi.Key);
            return null;
        }

        public static string NoteName(int n)
        {
            string[] names = { "F#", "G", "G#", "A", "A#", "B", "C", "C#", "D", "D#", "E", "F" };
            return names[n % 12] + (n < 6 ? "3" : n < 18 ? "4" : "5");
        }

        public void UpdateVisual(BlockInstance bi)
        {
            var def = bi.Data.Def;
            var go = bi.Go;
            if (go == null) return;
            int variant = 0;
            Quaternion rot = Quaternion.identity;
            byte f = bi.Data.Facing;
            switch (def.Shape)
            {
                case BlockShape.Cube:
                case BlockShape.PistonHead:
                    if (def.Directional)
                        rot = def.FacingIncludesVertical ? Quaternion.FromToRotation(Vector3.up, Faces.Dir[f]) : Faces.Rotation(f);
                    break;
                case BlockShape.Torch:
                case BlockShape.Lever:
                case BlockShape.Button:
                    if (f == (byte)Face.Up) { variant = 0; rot = Quaternion.identity; }
                    else if (f == (byte)Face.Down) { variant = 0; rot = Quaternion.Euler(180, 0, 0); }
                    else { variant = 1; rot = Faces.Rotation(f); }
                    break;
                case BlockShape.Dust:
                    variant = DustConnections(bi.Key);
                    break;
            }
            bi.Variant = variant;
            go.transform.localRotation = rot;
            Molds.TryGetValue(bi.Key, out var mold);
            if (mold != null)
            {
                // molded natural blocks: the mold's "top" faces the open side (floor, wall or ceiling)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Faces.Dir[f < 6 ? f : 1]);
                if (bi.Mf.sharedMesh == null || !bi.Mf.sharedMesh.name.StartsWith("LMC_mold_")) bi.Mf.sharedMesh = MeshBuilder.Mold(def, mold, withTop: false, exposed: CanonicalExposure(mold, go.transform.localRotation));
            }
            else
            {
                bi.Mf.sharedMesh = MeshBuilder.For(def, StateForMesh(bi), variant);
                // was molded before (moved by a piston): back to a plain box collider
                var oldMc = go.GetComponent<MeshCollider>();
                if (oldMc != null) { if (oldMc.sharedMesh != null && oldMc.sharedMesh.name.StartsWith("LMC_moldcol_")) Destroy(oldMc.sharedMesh); Destroy(oldMc); bi.Col.enabled = true; }
            }

            bool lit = IsLit(bi);
            Material mat;
            if (def.Shape == BlockShape.Torch) mat = Atlas.Cutout;
            else if (def.Shape == BlockShape.Dust) mat = bi.Data.State > 0 ? Atlas.CutoutEmissive : Atlas.Cutout;
            else if (def.Shape == BlockShape.Lever || def.Shape == BlockShape.Button || def.Shape == BlockShape.Plate) mat = Atlas.Opaque;
            else if (def == LethalMinecraft.Blocks.RedstoneLamp) mat = lit ? Atlas.Emissive : Atlas.Opaque;
            else if (def == LethalMinecraft.Blocks.JackOLantern) mat = Atlas.Opaque;
            else mat = Atlas.ForRender(def.Render);
            if (def == LethalMinecraft.Blocks.TNT && (bi.Data.State & 1) != 0 && bi.FuseStart < 0)
            {
                bi.FuseStart = Time.time;
                bi.Col.isTrigger = false;
            }
            if (Outdoors(bi)) mat = Atlas.Ambient(mat);
            if (bi.FuseStart < 0)
            {
                if (MeshBuilder.HasGlow(bi.Mf.sharedMesh))
                    bi.Mr.sharedMaterials = new[] { mat, mat == Atlas.Cutout ? Atlas.CutoutEmissive : Atlas.Emissive };
                else bi.Mr.sharedMaterial = mat;
            }

            // collider (in canonical/local unit space, transform rotation applies)
            var b = bi.Mf.sharedMesh.bounds;
            if (mold != null)
            {
                // collide with the whole molded shape (the drawn mesh may be just the exposed faces); the box stays
                // (disabled) to size the outline. The collider mesh is rebuilt when the shape data changes.
                var mc = go.GetComponent<MeshCollider>() ?? go.AddComponent<MeshCollider>();
                if (mc.sharedMesh == null || !mc.sharedMesh.name.StartsWith("LMC_moldcol_")) mc.sharedMesh = MeshBuilder.Mold(def, mold, withTop: true);
                b = mc.sharedMesh.bounds;
                bi.Col.center = b.center; bi.Col.size = b.size; bi.Col.enabled = false;
            }
            else if (def.Solid)
            {
                if (def == LethalMinecraft.Blocks.Piston || def == LethalMinecraft.Blocks.StickyPiston || def.Shape == BlockShape.PistonHead)
                { bi.Col.center = b.center; bi.Col.size = b.size; }
                else { bi.Col.center = Vector3.zero; bi.Col.size = Vector3.one; }
            }
            else
            {
                // generous trigger so thin things are easy to hit
                var size = b.size;
                size.x = Mathf.Max(size.x, 0.35f); size.y = Mathf.Max(size.y, 0.25f); size.z = Mathf.Max(size.z, 0.35f);
                bi.Col.center = b.center;
                bi.Col.size = size;
            }
            if (IsVisualOnly(bi))
            {
                // generated bedrock only closes off the side of a hole visually: the protected geometry it stands for is
                // still there and solid, so the block never gets collision of its own (it can't block a door or a path)
                bi.Col.enabled = false;
                var vmc = go.GetComponent<MeshCollider>();
                if (vmc != null) vmc.enabled = false;
            }

            // light
            float intensity = LightFor(bi, lit);
            if (intensity > 0)
            {
                if (bi.Light == null)
                {
                    var lgo = new GameObject("light");
                    lgo.transform.SetParent(go.transform, false);
                    lgo.transform.localPosition = def.Shape == BlockShape.Torch ? new Vector3(0, 0.45f, 0) : Vector3.zero;
                    var hd = lgo.AddHDLight(HDLightTypeAndShape.Point);
                    hd.EnableShadows(false);
                    // a small sphere, not a point: caps the glare on the wall a torch hangs on and on a player standing
                    // right next to it (a point light's brightness grows without bound up close)
                    hd.shapeRadius = 0.35f * S;
                    // no fog glow: with the camera next to a torch (one on the wall beside you) the in-scattered light
                    // fills the whole screen
                    hd.affectsVolumetric = false;
                    bi.Light = hd;
                }
                bi.Light.GetComponent<Light>().color = def.LightColor;
                bi.LightLumens = intensity * Plugin.TorchBrightness.Value * DevLightScale;
                ApplyLight(bi);
                bi.Light.range = def.LightRange * S;
                bi.Light.gameObject.SetActive(true);
            }
            else if (bi.Light != null) bi.Light.gameObject.SetActive(false);
            UpdateHoverTip(bi);
        }

        /// <summary>Which canonical faces of a molded block (bit 0 +x, 1 -x, 2 +y top, 3 -y bottom, 4 +z, 5 -z) are exposed.</summary>
        static int CanonicalExposure(byte[] mold, Quaternion rot)
        {
            var canon = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int mask = 0;
            for (int i = 0; i < 6; i++)
                if (MoldData.IsExposed(mold, Ground.MoldRes, Faces.FromVector(rot * canon[i]))) mask |= 1 << i;
            return mask;
        }

        /// <summary>A molded block's shape data changed (a neighbour was dug): rebuild its meshes.</summary>
        public void RefreshMold(BlockKey k)
        {
            if (!Blocks.TryGetValue(k, out var bi) || bi.Go == null) return;
            if (bi.Mf != null && bi.Mf.sharedMesh != null && bi.Mf.sharedMesh.name.StartsWith("LMC_mold")) { Destroy(bi.Mf.sharedMesh); bi.Mf.sharedMesh = null; }
            var mc = bi.Go.GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != null && mc.sharedMesh.name.StartsWith("LMC_moldcol_")) { var old = mc.sharedMesh; mc.sharedMesh = null; Destroy(old); }
            UpdateVisual(bi);
        }

        /// <summary>Natural bedrock: drawn, but without collision (see UpdateVisual).</summary>
        public static bool IsVisualOnly(BlockInstance bi) =>
            bi.Data.Def == LethalMinecraft.Blocks.Bedrock && (bi.Data.State & LethalMinecraft.Blocks.NaturalGround) != 0;

        /// <summary>Blocks in the ship or on the surface get a little ambient fill; inside the facility it's dark.</summary>
        public static bool Outdoors(BlockInstance bi)
        {
            if (bi.Key.Frame == 1) return true;
            return bi.Go != null && bi.Go.transform.position.y > -80f;
        }

        static byte StateForMesh(BlockInstance bi)
        {
            var def = bi.Data.Def;
            if (def.Shape == BlockShape.Dust) return (byte)(bi.Data.State > 0 ? 1 : 0);
            if (def == LethalMinecraft.Blocks.NoteBlock || def == LethalMinecraft.Blocks.TNT) return 0;
            return bi.Data.State;
        }

        static bool IsLit(BlockInstance bi)
        {
            var def = bi.Data.Def;
            if (def == LethalMinecraft.Blocks.RedstoneLamp) return (bi.Data.State & 1) != 0;
            if (def == LethalMinecraft.Blocks.RedstoneTorch) return (bi.Data.State & 1) == 0;
            return def.LightIntensity > 0;
        }

        static float LightFor(BlockInstance bi, bool lit)
        {
            var def = bi.Data.Def;
            if (def == LethalMinecraft.Blocks.RedstoneLamp) return lit ? 4000f : 0f;
            if (def == LethalMinecraft.Blocks.Furnace) return (bi.Data.State & 1) != 0 ? 1200f : 0f;
            if (def == LethalMinecraft.Blocks.RedstoneTorch) return lit ? def.LightIntensity : 0f;
            if (def.Shape == BlockShape.Dust) return 0f;
            return def.LightIntensity;
        }

        int DustConnections(BlockKey k)
        {
            int c = 0;
            int[] faces = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };
            int[] bits = { 1, 2, 4, 8 };
            for (int i = 0; i < 4; i++)
            {
                var n = k.Offset(faces[i]);
                if (ConnectsToDust(n) || ConnectsToDust(n.Offset((int)Face.Down)) || (ConnectsToDust(n.Offset((int)Face.Up)) && !IsSolidAt(k.Offset((int)Face.Up))))
                    c |= bits[i];
            }
            // a single connection draws a full line through the block like Minecraft
            if (c == 1 || c == 2) c = 3;
            if (c == 4 || c == 8) c = 12;
            return c;
        }

        bool ConnectsToDust(BlockKey k)
        {
            var d = DefAt(k);
            return d != null && d.IsRedstoneComponent;
        }

        public bool IsSolidAt(BlockKey k)
        {
            var d = DefAt(k);
            return d != null && d.Solid;
        }

        void RefreshNeighbors(BlockKey k)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int f = 2; f < 6; f++)
                {
                    var n = k.Offset(f).Offset(new Vector3Int(0, dy, 0));
                    if (Blocks.TryGetValue(n, out var bi) && bi.Data.Def.Shape == BlockShape.Dust) UpdateVisual(bi);
                }
            if (Blocks.TryGetValue(k, out var self) && self.Data.Def.Shape == BlockShape.Dust) UpdateVisual(self);
            var up = k.Offset((int)Face.Up);
            if (Blocks.TryGetValue(up, out var u) && u.Data.Def.Shape == BlockShape.Dust) UpdateVisual(u);
            var down = k.Offset((int)Face.Down);
            if (Blocks.TryGetValue(down, out var dn) && dn.Data.Def.Shape == BlockShape.Dust) UpdateVisual(dn);
        }

        void DestroyInstance(BlockInstance bi)
        {
            if (Blocks.TryGetValue(bi.Key, out var cur) && cur == bi) Blocks.Remove(bi.Key);
            animating.Remove(bi);
            if (bi.FuseAudio != null) Destroy(bi.FuseAudio.gameObject);
            if (bi.Go != null) ItemGravity.Removed(new Bounds(bi.Go.transform.position, Vector3.one * S));
            if (bi.Go != null)
            {
                // an InteractTrigger being destroyed while hovered leaves the HUD tip stale; clear it
                var lp = GameNetworkManager.Instance?.localPlayerController;
                if (lp != null && bi.Trigger != null && lp.hoveringOverTrigger == bi.Trigger) lp.hoveringOverTrigger = null;
                if (bi.Mf != null && bi.Mf.sharedMesh != null && bi.Mf.sharedMesh.name.StartsWith("LMC_mold")) Destroy(bi.Mf.sharedMesh);
                var moldCol = bi.Go.GetComponent<MeshCollider>();
                if (moldCol != null && moldCol.sharedMesh != null && moldCol.sharedMesh.name.StartsWith("LMC_moldcol_")) Destroy(moldCol.sharedMesh);
                Destroy(bi.Go);
            }
            Molds.Remove(bi.Key);
        }

        // ================================================================== effects
        public void BreakEffects(BlockInstance bi)
        {
            var def = bi.Data.Def;
            Vector3 pos = bi.Go != null ? bi.Go.transform.position : WorldCenter(bi.Key);
            if (def == LethalMinecraft.Blocks.Glass || def == LethalMinecraft.Blocks.Ice || def == LethalMinecraft.Blocks.RedstoneLamp || def == LethalMinecraft.Blocks.Glowstone)
                Sounds.Play("break.glass", pos, 1f, UnityEngine.Random.Range(0.85f, 1.05f));
            else
                Sounds.Play("dig." + Sounds.Family(def), pos, 1f, UnityEngine.Random.Range(0.8f, 1.0f));
            SpawnParticles(def, pos, 14);
        }

        public void SpawnParticles(BlockDef def, Vector3 pos, int count)
        {
            string tile = def.TileSide;
            if (def.Shape == BlockShape.Torch) tile = "item_torch";
            if (def.Shape == BlockShape.Dust) tile = "redstone_block";
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("LMC_frag");
                go.transform.position = pos + UnityEngine.Random.insideUnitSphere * 0.35f * S;
                go.transform.localScale = Vector3.one * S * UnityEngine.Random.Range(0.6f, 1.0f);
                go.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.Fragment(tile, rng);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Atlas.Particle;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var p = go.AddComponent<Fragment>();
                p.Velocity = (UnityEngine.Random.insideUnitSphere + Vector3.up * 0.9f) * 2.2f * S;
            }
        }

        class Fragment : MonoBehaviour
        {
            public Vector3 Velocity;
            float life = 0.6f;
            void Update()
            {
                life -= Time.deltaTime;
                if (life <= 0) { Destroy(gameObject.GetComponent<MeshFilter>().sharedMesh); Destroy(gameObject); return; }
                Velocity += Physics.gravity * 0.9f * Time.deltaTime;
                var step = Velocity * Time.deltaTime;
                if (Physics.Raycast(transform.position, step.normalized, out var hit, step.magnitude + 0.02f, 1 << 8 | 1 << 11 | 1 << 25 | 1, QueryTriggerInteraction.Ignore))
                {
                    transform.position = hit.point + hit.normal * 0.02f;
                    Velocity = Vector3.Reflect(Velocity, hit.normal) * 0.3f;
                }
                else transform.position += step;
                transform.localScale *= 1f - Time.deltaTime * 0.6f;
            }
        }

        // ================================================================== serialization helpers
        public List<Op> SnapshotOps(Func<BlockKey, bool> filter = null)
        {
            var ops = new List<Op>(Blocks.Count);
            foreach (var kv in Blocks)
            {
                if (filter != null && !filter(kv.Key)) continue;
                if (kv.Value.Data.Def.Shape == BlockShape.PistonHead) continue; // re-created from piston state below
                ops.Add(Op.Set(kv.Key, kv.Value.Data));
            }
            foreach (var kv in Blocks)
            {
                if (filter != null && !filter(kv.Key)) continue;
                if (kv.Value.Data.Def.Shape == BlockShape.PistonHead) ops.Add(Op.Set(kv.Key, kv.Value.Data));
            }
            return ops;
        }
    }
}
