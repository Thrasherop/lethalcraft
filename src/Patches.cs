using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>Per-session runtime objects (created when the ship scene starts).</summary>
    public class Runtime : MonoBehaviour
    {
        public static Runtime Instance;

        void Awake()
        {
            Instance = this;
            gameObject.AddComponent<BlockWorld>();
            gameObject.AddComponent<Builder>();
            gameObject.AddComponent<Survival>();
            gameObject.AddComponent<McHud>();
            gameObject.AddComponent<ClientEffects>();
            gameObject.AddComponent<HotbarInput>();
            gameObject.AddComponent<CraftingUI>();
            gameObject.AddComponent<ChestUI>();
            gameObject.AddComponent<CreativeUI>();
            gameObject.AddComponent<CreativeFlight>();
            gameObject.AddComponent<Inventory>();
            GameModes.Reset();
            gameObject.AddComponent<EnderPearls>();
            Redstone.Reset();
            Crafting.Reset();
        }

        IEnumerator Start()
        {
            BlockNet.Register();
            yield return null;
            if (BlockNet.IsServer) ShipPersistence.Load();
            else
            {
                yield return new WaitForSeconds(1.0f);
                BlockNet.RequestSync();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            BlockNet.Unregister();
            Redstone.Reset();
        }
    }

    /// <summary>Number keys 1-9 select hotbar slots (Shift+1/2 still emote).</summary>
    public class HotbarInput : MonoBehaviour
    {
        int pending = -1;
        readonly System.Action<InputAction.CallbackContext>[] handlers = new System.Action<InputAction.CallbackContext>[9];

        void Awake()
        {
            // keys come from ModKeys (rebindable in Settings > Controls with LethalCompanyInputUtils)
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                handlers[i] = _ => pending = idx;
                if (ModKeys.Hotbar[i] != null) ModKeys.Hotbar[i].performed += handlers[i];
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < 9; i++) if (ModKeys.Hotbar[i] != null) ModKeys.Hotbar[i].performed -= handlers[i];
        }

        void Update()
        {
            if (pending < 0) return;
            int target = pending;
            pending = -1;
            var p = GameNetworkManager.Instance?.localPlayerController;
            var kb = Keyboard.current;
            if (p == null || !Builder.CanAct(p)) return;
            if ((kb != null && kb.shiftKey.isPressed) || p.twoHanded || p.activatingItem || p.throwingObject) return;
            if (target >= p.ItemSlots.Length) return;
            SelectSlot(p, target);
        }

        public static void SelectSlot(PlayerControllerB p, int target)
        {
            if (p.currentItemSlot == target || p.timeSinceSwitchingSlots < 0.05f) return;
            int n = p.ItemSlots.Length;
            int cur = p.currentItemSlot == 50 ? 0 : p.currentItemSlot;
            int fwd = ((target - cur) % n + n) % n;
            int back = n - fwd;
            bool forward = fwd <= back;
            int steps = forward ? fwd : back;
            if (p.currentItemSlot == 50) { steps = 0; }
            ShipBuildModeManager.Instance?.CancelBuildMode();
            p.playerBodyAnimator.SetBool("GrabValidated", false);
            if (p.currentItemSlot == 50)
            {
                // leave the utility slot first
                p.SwitchToItemSlot(p.NextItemSlot(true));
                p.SwitchItemSlotsServerRpc(true);
                cur = p.currentItemSlot;
                fwd = ((target - cur) % n + n) % n; back = n - fwd; forward = fwd <= back; steps = forward ? fwd : back;
            }
            for (int s = 0; s < steps; s++)
            {
                p.SwitchToItemSlot(p.NextItemSlot(forward));
                p.SwitchItemSlotsServerRpc(forward);
            }
            if (p.currentlyHeldObjectServer != null)
                p.currentlyHeldObjectServer.gameObject.GetComponent<AudioSource>()?.PlayOneShot(p.currentlyHeldObjectServer.itemProperties.grabSFX, 0.6f);
            p.timeSinceSwitchingSlots = 0f;
        }
    }

    public class ClientEffects : MonoBehaviour
    {
        static readonly Dictionary<(BlockKey, ulong), GameObject> cracks = new Dictionary<(BlockKey, ulong), GameObject>();

        public static void Explosion(Vector3 pos, float radius)
        {
            Sounds.Play("explode", pos, 1f, UnityEngine.Random.Range(0.8f, 1f), 60f);
            ServerLogic.SuppressGameExplosionHook = true;
            try { Landmine.SpawnExplosion(pos, true, radius * 0.55f, radius * 1.35f, 50, 30f); }
            catch (Exception e) { Plugin.Log.LogWarning("explosion: " + e.Message); }
            finally { ServerLogic.SuppressGameExplosionHook = false; }
        }

        public static void RemoteMineProgress(BlockKey key, sbyte stage, ulong who)
        {
            var k = (key, who);
            var world = BlockWorld.Instance;
            var bi = world?.Get(key);
            if (stage < 0 || bi == null || bi.Go == null)
            {
                if (cracks.TryGetValue(k, out var old) && old != null) Destroy(old);
                cracks.Remove(k);
                return;
            }
            if (!cracks.TryGetValue(k, out var go) || go == null)
            {
                go = new GameObject("LMC_RemoteCrack");
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Atlas.Crack;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                cracks[k] = go;
            }
            go.transform.SetParent(bi.Go.transform, false);
            go.transform.localPosition = bi.Data.Def.Solid ? bi.Col.center : bi.Mf.sharedMesh.bounds.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = bi.Data.Def.Solid ? bi.Col.size : bi.Mf.sharedMesh.bounds.size + Vector3.one * 0.02f;
            go.GetComponent<MeshFilter>().sharedMesh = MeshBuilder.Crack(stage);
            if (who == ulong.MaxValue && stage % 3 == 0) Sounds.Play("step." + Sounds.Family(bi.Data.Def), go.transform.position, 0.4f, 0.5f);
        }

        void OnDestroy() => cracks.Clear();
    }

    /// <summary>Blocks built inside the ship persist in the save file.</summary>
    public static class ShipPersistence
    {
        const string SaveKey = "LMC_ShipBlocks_v1";
        const string ChestKey = "LMC_ShipChests_v1";

        public static void Save()
        {
            var world = BlockWorld.Instance;
            if (world == null || !BlockNet.IsServer) return;
            // only when the game saves the ship too (in orbit): a quit mid-round rolls the ship back to its last orbit
            // save, and saving chests/blocks anyway would duplicate or lose whatever moved between them and the floor
            var sor = StartOfRound.Instance;
            if (sor == null || sor.isChallengeFile || !sor.inShipPhase || sor.beganLoadingNewLevel
                || (RoundManager.Instance != null && RoundManager.Instance.dungeonIsGenerating)) return;
            try
            {
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                var list = new List<KeyValuePair<BlockKey, BlockInstance>>();
                foreach (var kv in world.Blocks) if (kv.Key.Frame == 1) list.Add(kv);
                w.Write(list.Count);
                foreach (var kv in list)
                {
                    w.Write(kv.Key.YOff);
                    w.Write(kv.Key.Pos.x); w.Write(kv.Key.Pos.y); w.Write(kv.Key.Pos.z);
                    var d = kv.Value.Data;
                    byte state = d.State;
                    if (d.Def == Blocks.TNT) state = 0;
                    w.Write(d.Type); w.Write(d.Facing); w.Write(state);
                }
                ES3.Save(SaveKey, Convert.ToBase64String(ms.ToArray()), GameNetworkManager.Instance.currentSaveFileName);
                var cms = new MemoryStream();
                Chests.Write(new BinaryWriter(cms), Chests.All.Where(kv => kv.Key.Frame == 1 && world.DefAt(kv.Key) == Blocks.Chest));
                ES3.Save(ChestKey, Convert.ToBase64String(cms.ToArray()), GameNetworkManager.Instance.currentSaveFileName);
                Plugin.Log.LogInfo($"Saved {list.Count} ship blocks");
            }
            catch (Exception e) { Plugin.Log.LogError("Ship block save failed: " + e); }
        }

        public static void Load()
        {
            var world = BlockWorld.Instance;
            if (world == null) return;
            try
            {
                string file = GameNetworkManager.Instance.currentSaveFileName;
                if (!ES3.KeyExists(SaveKey, file)) return;
                var data = Convert.FromBase64String(ES3.Load<string>(SaveKey, file));
                var r = new BinaryReader(new MemoryStream(data));
                int n = r.ReadInt32();
                var ops = new List<Op>(n);
                var heads = new List<Op>();
                for (int i = 0; i < n; i++)
                {
                    short yoff = r.ReadInt16();
                    var pos = new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                    var d = new BlockData(r.ReadByte(), r.ReadByte(), r.ReadByte());
                    if (d.Def == null) continue;
                    var op = Op.Set(new BlockKey(1, yoff, pos), d);
                    if (d.Def.Shape == BlockShape.PistonHead) heads.Add(op); else ops.Add(op);
                }
                ops.AddRange(heads);
                BlockNet.ServerBroadcastOps(ops);
                Plugin.Log.LogInfo($"Loaded {ops.Count} ship blocks");
                if (ES3.KeyExists(ChestKey, file))
                {
                    var cr = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(ChestKey, file))));
                    foreach (var kv in Chests.Read(cr, 1))
                    {
                        Chests.All[kv.Key] = kv.Value;
                        BlockNet.ServerChest(kv.Key, kv.Value);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Ship block load failed: " + e); }
        }

        public static void Clear()
        {
            var world = BlockWorld.Instance;
            if (world == null || !BlockNet.IsServer) return;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks) if (kv.Key.Frame == 1) ops.Add(Op.Remove(kv.Key, false));
            BlockNet.ServerBroadcastOps(ops);
            foreach (var k in Chests.All.Keys.Where(k => k.Frame == 1).ToList()) { Chests.All.Remove(k); BlockNet.ServerChest(k, null); }
        }
    }

    [HarmonyPatch]
    public static class Patches
    {
        // ------------------------------------------------------------------ lifecycle
        [HarmonyPatch(typeof(StartOfRound), "Start"), HarmonyPostfix]
        static void StartOfRoundStart(StartOfRound __instance)
        {
            if (Runtime.Instance != null) UnityEngine.Object.Destroy(Runtime.Instance.gameObject);
            var go = new GameObject("LethalMinecraft_Runtime");
            go.AddComponent<Runtime>();
            LogLayersOnce();
            ModItems.RefreshSfx();
        }

        [HarmonyPatch(typeof(StartOfRound), "OnDestroy"), HarmonyPrefix]
        static void StartOfRoundDestroy()
        {
            if (Runtime.Instance != null) UnityEngine.Object.Destroy(Runtime.Instance.gameObject);
        }

        static bool loggedLayers;
        static void LogLayersOnce()
        {
            if (loggedLayers) return;
            loggedLayers = true;
            var sb = new System.Text.StringBuilder("Layers: ");
            for (int i = 0; i < 32; i++) { var n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) sb.Append(i).Append('=').Append(n).Append(' '); }
            Plugin.Log.LogInfo(sb.ToString());
            var p = GameNetworkManager.Instance?.localPlayerController ?? UnityEngine.Object.FindObjectOfType<PlayerControllerB>();
            if (p != null)
                Plugin.Log.LogInfo($"Player CC height={p.thisController.height} radius={p.thisController.radius} step={p.thisController.stepOffset} jumpForce={p.jumpForce} => jump height ~{p.jumpForce * p.jumpForce / (2 * 38f):F2}");
        }

        // ------------------------------------------------------------------ 9-slot hotbar
        [HarmonyPatch(typeof(PlayerControllerB), "Awake"), HarmonyPostfix]
        static void PlayerAwake(PlayerControllerB __instance)
        {
            int n = Mathf.Clamp(Plugin.HotbarSlots.Value, 4, 9);
            if (__instance.ItemSlots == null || __instance.ItemSlots.Length != n) __instance.ItemSlots = new GrabbableObject[n];
        }

        [HarmonyPatch(typeof(HUDManager), "Awake"), HarmonyPostfix]
        static void HudAwake(HUDManager __instance)
        {
            try
            {
                int n = Mathf.Clamp(Plugin.HotbarSlots.Value, 4, 9);
                var icons = __instance.itemSlotIcons;
                var frames = __instance.itemSlotIconFrames;
                if (frames == null || frames.Length >= n || frames.Length == 0) return;
                var newFrames = new Image[n];
                var newIcons = new Image[n];
                for (int i = 0; i < frames.Length; i++) { newFrames[i] = frames[i]; newIcons[i] = icons[i]; }
                var template = frames[frames.Length - 1];
                for (int i = frames.Length; i < n; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                    clone.name = "Slot" + i;
                    var rt = (RectTransform)clone.transform;
                    var prev = (RectTransform)newFrames[i - 1].transform;
                    var prev2 = (RectTransform)newFrames[i - 2].transform;
                    rt.anchoredPosition = prev.anchoredPosition + (prev.anchoredPosition - prev2.anchoredPosition);
                    newFrames[i] = clone.GetComponent<Image>();
                    var iconImgs = clone.GetComponentsInChildren<Image>(true);
                    Image icon = null;
                    foreach (var im in iconImgs) if (im.gameObject != clone) { icon = im; break; }
                    newIcons[i] = icon ?? newFrames[i];
                    newIcons[i].enabled = false;
                }
                __instance.itemSlotIconFrames = newFrames;
                __instance.itemSlotIcons = newIcons;
            }
            catch (Exception e) { Plugin.Log.LogError("Hotbar HUD extend failed: " + e); }
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Emote1_performed"), HarmonyPrefix]
        static bool Emote1(InputAction.CallbackContext context) => Keyboard.current == null || Keyboard.current.shiftKey.isPressed || Plugin.HotbarSlots.Value < 1;

        [HarmonyPatch(typeof(PlayerControllerB), "Emote2_performed"), HarmonyPrefix]
        static bool Emote2(InputAction.CallbackContext context) => Keyboard.current == null || Keyboard.current.shiftKey.isPressed || Plugin.HotbarSlots.Value < 2;

        // ------------------------------------------------------------------ saving
        [HarmonyPatch(typeof(GameNetworkManager), "SaveGame"), HarmonyPostfix]
        static void SaveGame() => ShipPersistence.Save();

        [HarmonyPatch(typeof(StartOfRound), "ResetShip"), HarmonyPostfix]
        static void ResetShip() => ShipPersistence.Clear();

        // ------------------------------------------------------------------ explosions break blocks
        [HarmonyPatch(typeof(Landmine), "SpawnExplosion"), HarmonyPostfix]
        static void GameExplosion(Vector3 explosionPosition, float killRange, float damageRange)
        {
            if (ServerLogic.SuppressGameExplosionHook || !Plugin.ExplosionsBreakBlocks.Value || !BlockNet.IsServer) return;
            float r = Mathf.Clamp(Mathf.Max(killRange + 1f, damageRange * 0.5f), 1.5f, 6f);
            ServerLogic.ExplodeBlocks(explosionPosition, r, -1, carveGround: Plugin.MinesDigGround.Value);
        }

        // ------------------------------------------------------------------ survival hooks
        [HarmonyPatch(typeof(PlayerControllerB), "DamagePlayer"), HarmonyPrefix]
        static void DamagePrefix(PlayerControllerB __instance, ref int damageNumber)
        {
            if (!__instance.IsOwner || __instance != GameNetworkManager.Instance?.localPlayerController) return;
            var sv = Survival.Instance;
            if (sv == null) return;
            damageNumber = sv.AbsorbDamage(damageNumber);
            sv.LastHurtTime = Time.time;
            Survival.AddExhaustion(0.1f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "PlayerHitGroundEffects"), HarmonyPrefix]
        static void HitGround(PlayerControllerB __instance)
        {
            if (!__instance.IsOwner || __instance != GameNetworkManager.Instance?.localPlayerController) return;
            if (__instance.fallValueUncapped > -10f && __instance.fallValue > -10f) return;
            if (!Physics.Raycast(__instance.transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)) return;
            var br = hit.collider.GetComponent<BlockRef>();
            if (br == null || BlockWorld.Instance?.DefAt(br.Key) != Blocks.Slime) return;
            __instance.takingFallDamage = false;
            __instance.fallValueUncapped = -7f;
            __instance.fallValue = -7f;
            if (__instance.isCrouching) return; // sneaking cancels the bounce like in Minecraft
            float fallHeight = Survival.Instance != null ? Survival.Instance.AirPeakY - __instance.transform.position.y : 3f;
            float apex = fallHeight * 0.55f;
            if (apex < 0.6f) return;
            // empirically: apex ~= (v/7)^2 for an externalForceAutoFade impulse v
            float speed = Mathf.Min(7f * Mathf.Sqrt(apex), 40f);
            __instance.externalForceAutoFade = new Vector3(__instance.externalForceAutoFade.x, 0f, __instance.externalForceAutoFade.z) + Vector3.up * speed;
            Sounds.Play("bounce", __instance.transform.position, 0.6f, 1f);
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void PlayerUpdate(PlayerControllerB __instance)
        {
            if (!Plugin.HungerEnabled.Value || __instance != GameNetworkManager.Instance?.localPlayerController) return;
            var sv = Survival.Instance;
            if (sv != null && sv.Hunger <= 6 && __instance.sprintMeter > 0.1f) __instance.sprintMeter = 0.1f;
        }

        // ------------------------------------------------------------------ xp sources
        static readonly Dictionary<EnemyAI, float> lastLocalHit = new Dictionary<EnemyAI, float>();

        [HarmonyPatch(typeof(EnemyAI), "HitEnemy"), HarmonyPostfix]
        static void HitEnemy(EnemyAI __instance, PlayerControllerB playerWhoHit)
        {
            if (playerWhoHit != null && playerWhoHit == GameNetworkManager.Instance?.localPlayerController) lastLocalHit[__instance] = Time.time;
        }

        [HarmonyPatch(typeof(EnemyAI), "KillEnemy"), HarmonyPostfix]
        static void KillEnemy(EnemyAI __instance)
        {
            if (lastLocalHit.TryGetValue(__instance, out float t) && Time.time - t < 5f)
            {
                lastLocalHit.Remove(__instance);
                int hp = __instance.enemyType != null ? Mathf.Max(1, __instance.enemyType.PowerLevel > 0 ? (int)(__instance.enemyType.PowerLevel * 5) : 5) : 5;
                Survival.AddXp(Mathf.Clamp(hp, 3, 30));
            }
        }

        [HarmonyPatch(typeof(GrabbableObject), "GrabItemOnClient"), HarmonyPrefix]
        static void GrabItem(GrabbableObject __instance)
        {
            if (__instance.itemProperties != null && __instance.itemProperties.isScrap && !__instance.hasBeenHeld && !__instance.scrapPersistedThroughRounds)
                Survival.AddXp(1 + __instance.scrapValue / 20);
        }

        // ------------------------------------------------------------------ right-click places/eats instead of scanning while holding a stack
        [HarmonyPatch(typeof(HUDManager), "PingScan_performed"), HarmonyPrefix]
        static bool PingScan()
        {
            if (Plugin.PlaceWithLeftClick.Value) return true;
            var p = GameNetworkManager.Instance?.localPlayerController;
            return !(p != null && p.isHoldingObject && p.currentlyHeldObjectServer is StackItem);
        }

        // ------------------------------------------------------------------ dev auto-host
        [HarmonyPatch(typeof(PreInitSceneScript), "Start"), HarmonyPostfix]
        static void PreInitStart(PreInitSceneScript __instance)
        {
            if (Plugin.DevMode.Value && Plugin.DevLaunchMode != null) __instance.StartCoroutine(AutoLaunch(__instance));
        }

        static IEnumerator AutoLaunch(PreInitSceneScript s)
        {
            yield return new WaitForSeconds(0.5f);
            s.ChooseLaunchOption(Plugin.DevLaunchMode == "host");
        }

        static bool autoHosted;
        [HarmonyPatch(typeof(MenuManager), "Start"), HarmonyPostfix]
        static void MenuStart(MenuManager __instance)
        {
            if (!Plugin.DevMode.Value || Plugin.DevLaunchMode == null || __instance.isInitScene || autoHosted) return;
            autoHosted = true;
            __instance.StartCoroutine(AutoHost(__instance));
        }

        static IEnumerator AutoHost(MenuManager m)
        {
            yield return new WaitForSeconds(1.5f);
            if (Plugin.DevLaunchMode == "join")
            {
                var ut = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                ut.ConnectionData.Address = "127.0.0.1";
                for (int attempt = 0; attempt < 30 && !NetworkManager.Singleton.IsConnectedClient; attempt++)
                {
                    if (!NetworkManager.Singleton.IsClient) m.StartAClient();
                    yield return new WaitForSeconds(4f);
                }
                yield break;
            }
            if (Plugin.DevLaunchMode == "lanhost") m.LAN_HostSetLocal();
            GameNetworkManager.Instance.currentSaveFileName = "LCSaveFile3";
            GameNetworkManager.Instance.saveFileNum = 2;
            m.ClickHostButton();
            yield return new WaitForSeconds(0.5f);
            if (string.IsNullOrEmpty(m.lobbyNameInputField.text)) m.lobbyNameInputField.text = "LethalMinecraft Test";
            m.HostSetLobbyPublic(false);
            m.ConfirmHostButton();
        }

        [HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc"), HarmonyPostfix]
        static void LevelReady(RoundManager __instance)
        {
            DevDespawnLog.LevelLoadedAt = Time.time;
            // fresh level: nothing dug yet (also covers moons where no world-frame block was ever created)
            TerrainCarver.Reset(); ServerLogic.ResetGround();
            try { TerrainCarver.ConvertTerrains(); } catch (Exception e) { Plugin.Log.LogError("Terrain conversion: " + e); }
            try { TerrainCarver.MakeFacilityShellConcave(); } catch (Exception e) { Plugin.Log.LogError("Facility colliders: " + e); }
            if (BlockWorld.Instance != null) BlockWorld.Instance.StartCoroutine(TerrainCarver.PrewarmLevel());
        }

        // ------------------------------------------------------------------ ore veins
        [HarmonyPatch(typeof(RoundManager), "SpawnScrapInLevel"), HarmonyPostfix]
        static void SpawnScrap(RoundManager __instance)
        {
            if (!Plugin.SpawnOres.Value || !BlockNet.IsServer) return;
            __instance.StartCoroutine(OreVeins.SpawnDelayed(__instance));
        }
    }

    public static class OreVeins
    {
        public static IEnumerator SpawnDelayed(RoundManager rm)
        {
            yield return new WaitForSeconds(2f);
            List<List<Op>> veins = null;
            try { veins = Spawn(rm); } catch (Exception e) { Plugin.Log.LogError("Ore spawn failed: " + e); }
            if (veins == null || veins.Count == 0) yield break;

            // add veins one at a time; roll back any vein that cuts off or badly lengthens a route through the facility
            var origin = PathOrigin(rm);
            var baseline = Routes(rm, origin);
            int kept = 0, rejected = 0, blocks = 0;
            foreach (var vein in veins)
            {
                if (BlockWorld.Instance == null) yield break;
                BlockNet.ServerBroadcastOps(vein);
                yield return new WaitForSeconds(0.7f); // let NavMeshObstacle carving settle
                var now = Routes(rm, origin);
                bool bad = false;
                foreach (var kv in baseline)
                {
                    if (!now.TryGetValue(kv.Key, out var after)) continue;
                    if (kv.Value.ok && !after.ok) { bad = true; break; }
                    if (kv.Value.ok && after.len > kv.Value.len * 1.25f + 4f) { bad = true; break; }
                }
                if (bad)
                {
                    BlockNet.ServerBroadcastOps(vein.Select(o => Op.Remove(o.Key, false)).ToList());
                    rejected++;
                    yield return new WaitForSeconds(0.5f);
                }
                else { kept++; blocks += vein.Count; }
            }
            Plugin.Log.LogInfo($"Spawned {kept} ore veins ({blocks} blocks); rejected {rejected} that would block paths");
        }

        static Vector3 PathOrigin(RoundManager rm)
        {
            var exit = UnityEngine.Object.FindObjectsOfType<EntranceTeleport>().FirstOrDefault(e => !e.isEntranceToBuilding && e.entranceId == 0);
            var p = exit != null && exit.entrancePoint != null ? exit.entrancePoint.position : rm.insideAINodes[0].transform.position;
            return UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 4f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : p;
        }

        static Dictionary<int, (bool ok, float len)> Routes(RoundManager rm, Vector3 origin)
        {
            var res = new Dictionary<int, (bool, float)>();
            var path = new UnityEngine.AI.NavMeshPath();
            for (int i = 0; i < rm.insideAINodes.Length; i++)
            {
                var n = rm.insideAINodes[i];
                if (n == null) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(n.transform.position, out var h, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                bool ok = UnityEngine.AI.NavMesh.CalculatePath(origin, h.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                float len = 0;
                var c = path.corners;
                for (int k = 1; k < c.Length; k++) len += Vector3.Distance(c[k - 1], c[k]);
                res[i] = (ok, len);
            }
            return res;
        }

        static List<List<Op>> Spawn(RoundManager rm)
        {
            var world = BlockWorld.Instance;
            var veinsOut = new List<List<Op>>();
            if (world == null || rm.insideAINodes == null || rm.insideAINodes.Length == 0) return veinsOut;
            if (!world.WorldFrameAvailable) return veinsOut;
            world.FrameRoot(0, true);
            var rng = new System.Random(StartOfRound.Instance.randomMapSeed + 777);
            int wanted = Mathf.Clamp(rm.insideAINodes.Length / 9, 3, 12);
            var ops = new List<Op>();
            var used = new HashSet<BlockKey>();
            var veinCenters = new List<Vector3>();
            float S = Plugin.S;
            int floorMask = (1 << 8) | (1 << 11) | 1;
            int blockMask = floorMask | (1 << 9) | (1 << 26) | (1 << 28);
            int placed = 0;
            for (int attempt = 0; attempt < wanted * 6 && placed < wanted; attempt++)
            {
                var node = rm.insideAINodes[rng.Next(rm.insideAINodes.Length)];
                if (node == null) continue;
                var np = node.transform.position;
                if (veinCenters.Any(c => Vector3.Distance(c, np) < 12f)) continue;
                if (!Physics.Raycast(np + Vector3.up * 0.5f, Vector3.down, out var floor, 3f, floorMask, QueryTriggerInteraction.Ignore)) continue;
                float floorY = floor.point.y;
                // find a wall the vein can hug, reachable in a straight line from the node on the same floor
                bool ok = false;
                Vector3 basePos = default, along = default, dir = default;
                float a0 = (float)rng.NextDouble() * 360f;
                for (int k = 0; k < 8 && !ok; k++)
                {
                    float ang = (a0 + k * 45f) * Mathf.Deg2Rad;
                    dir = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
                    if (!Physics.Raycast(new Vector3(np.x, floorY + 1.0f, np.z), dir, out var wall, 5f, floorMask, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(wall.normal.y) > 0.3f || wall.distance < S) continue;
                    var cand = new Vector3(wall.point.x, floorY, wall.point.z) - dir * (S * 0.55f);
                    if (!Physics.Raycast(cand + Vector3.up * 1f, Vector3.down, out var cf, 2f, floorMask, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(cf.point.y - floorY) > 0.35f) continue;
                    if (Physics.Linecast(new Vector3(np.x, floorY + 1.2f, np.z), cand + Vector3.up * 0.7f, blockMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(cand, out _, S, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (NearDoor(cand, 2.2f)) continue;
                    basePos = cand;
                    along = Vector3.Cross(Vector3.up, dir);
                    ok = true;
                }
                if (!ok) continue;

                float yBottom = floorY / S;
                int cellY = Mathf.FloorToInt(yBottom);
                short yoff = (short)Mathf.Clamp(Mathf.RoundToInt((yBottom - cellY) * 1000f), 0, 999);
                var origin = new Vector3Int(Mathf.FloorToInt(basePos.x / S), cellY, Mathf.FloorToInt(basePos.z / S));
                // vein cells: spread along the wall and up to 2 high
                var alongI = new Vector3Int(Mathf.RoundToInt(along.x), 0, Mathf.RoundToInt(along.z));
                if (alongI == Vector3Int.zero) alongI = new Vector3Int(1, 0, 0);
                BlockDef ore = PickOre(rng);
                int len = rng.Next(2, 5);
                var cells = new List<Vector3Int>();
                for (int i = 0; i < len; i++)
                {
                    cells.Add(alongI * i);
                    if (rng.NextDouble() < 0.5) cells.Add(alongI * i + Vector3Int.up);
                }
                int veinBlocks = 0;
                var veinOps = new List<Op>();
                foreach (var c in cells.OrderBy(c => c.y))
                {
                    var key = new BlockKey(0, yoff, origin + c);
                    if (used.Contains(key) || world.Has(key)) continue;
                    if (ObstructedFor(key, blockMask)) continue;
                    if (c.y == 0 && !ServerLogic.Supported(key)) continue;
                    if (c.y > 0 && !used.Contains(new BlockKey(0, yoff, origin + c + Vector3Int.down))) continue;
                    var wc = world.WorldCenter(key);
                    if (NearDoor(wc, 1.8f)) continue;
                    // keep the hallway open: at least 2.5m of clear space in front of the vein
                    if (Physics.Raycast(wc, -dir, S * 0.5f + 2.5f, blockMask, QueryTriggerInteraction.Ignore)) continue;
                    used.Add(key);
                    var def = rng.NextDouble() < 0.6 ? ore : Blocks.Stone;
                    veinOps.Add(Op.Set(key, new BlockData(def.Id, 1, (byte)(def == Blocks.Stone ? 1 : 0)))); // state 1 = natural vein stone
                    veinBlocks++;
                }
                if (veinBlocks > 0) { placed++; veinCenters.Add(basePos); veinsOut.Add(veinOps); }
            }
            return veinsOut;
        }

        static bool ObstructedFor(BlockKey k, int mask)
        {
            var w = BlockWorld.Instance;
            var hits = Physics.OverlapBox(w.WorldCenter(k), Vector3.one * (Plugin.S * 0.48f), w.FrameRotation(k.Frame), mask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<GrabbableObject>() != null) continue;
                return true;
            }
            return false;
        }

        static bool NearDoor(Vector3 p, float r)
        {
            foreach (var h in Physics.OverlapSphere(p, r, ~0, QueryTriggerInteraction.Collide))
                if (h.GetComponentInParent<DoorLock>() != null || h.GetComponentInParent<EntranceTeleport>() != null) return true;
            return false;
        }

        static BlockDef PickOre(System.Random rng)
        {
            double r = rng.NextDouble();
            if (r < 0.34) return Blocks.CoalOre;
            if (r < 0.62) return Blocks.IronOre;
            if (r < 0.82) return Blocks.GoldOre;
            if (r < 0.94) return Blocks.DiamondOre;
            return Blocks.EmeraldOre;
        }
    }
}
