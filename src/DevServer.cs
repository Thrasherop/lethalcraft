using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Debug-only localhost command server used for automated play-testing. Disabled unless Debug.DevMode=true.
    /// One command per line; replies with one line.
    /// </summary>
    public class DevServer : MonoBehaviour
    {
        TcpListener listener;
        Thread thread;
        readonly ConcurrentQueue<(string cmd, TaskCompletionSourceLite reply)> queue = new ConcurrentQueue<(string, TaskCompletionSourceLite)>();
        public static bool LmbHeld, RmbHeld;
        public static bool LmbClick; // latched like a real click: a short dev click isn't lost to a long frame
        public static float RmbUntil;
        static float savedDaySpeed;
        public static bool RmbClick; // a click that lands even if a frame hitch outlasts the hold time
        public static float LmbUntil;

        class TaskCompletionSourceLite
        {
            public string Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        static int tracedErrors;

        void Start()
        {
            // dev: native Unity errors carry no managed stack; record where they came from
            Application.logMessageReceived += (msg, trace, type) =>
            {
                if (type == LogType.Error && msg.StartsWith("Failed getting triangles") && tracedErrors++ < 3)
                    Plugin.Log.LogWarning("[dev] native error from: " + System.Environment.StackTrace);
            };
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Plugin.DevPortEffective);
                listener.Start();
                thread = new Thread(Loop) { IsBackground = true };
                thread.Start();
                Plugin.Log.LogInfo("DevServer listening on 127.0.0.1:" + Plugin.DevPortEffective);
            }
            catch (Exception e) { Plugin.Log.LogError("DevServer failed: " + e.Message); }
        }

        void OnDestroy()
        {
            try { listener?.Stop(); } catch { }
        }

        void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (c)
                        using (var s = c.GetStream())
                        using (var r = new StreamReader(s))
                        using (var w = new StreamWriter(s) { AutoFlush = true })
                        {
                            string line;
                            while ((line = r.ReadLine()) != null)
                            {
                                var t = new TaskCompletionSourceLite();
                                queue.Enqueue((line, t));
                                t.Done.Wait(15000);
                                w.WriteLine((t.Result ?? "timeout").Replace("\n", " | "));
                            }
                        }
                    }
                    catch { }
                });
            }
        }

        readonly Queue<float> frameTimes = new Queue<float>();
        Vector2 walkDir; float walkUntil, walkSpeed;

        void FixedUpdate() { }

        void LateUpdate()
        {
            var p = P;
            if (p == null || Time.time > walkUntil) return;
            var fwd = p.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = p.transform.right; right.y = 0; right.Normalize();
            var dir = (fwd * walkDir.y + right * walkDir.x).normalized;
            p.thisController.Move(dir * walkSpeed * Time.deltaTime);
        }

        static string lastCmd = "";

        void Update()
        {
            frameTimes.Enqueue(Time.unscaledDeltaTime);
            while (frameTimes.Count > 120) frameTimes.Dequeue();
            if (Time.unscaledDeltaTime > 0.4f) Plugin.Log.LogWarning($"[dev] hitch {Time.unscaledDeltaTime * 1000f:F0} ms (last command: {lastCmd})");
            while (queue.TryDequeue(out var item))
            {
                string res;
                lastCmd = item.cmd.Trim();
                var cmdSw = System.Diagnostics.Stopwatch.StartNew();
                try { res = Exec(item.cmd.Trim()); }
                catch (Exception e) { res = "ERR " + e.Message; }
                if (cmdSw.ElapsedMilliseconds > 300) Plugin.Log.LogWarning($"[dev] command '{lastCmd}' took {cmdSw.ElapsedMilliseconds} ms");
                item.reply.Result = res;
                item.reply.Done.Set();
            }
            if (LmbUntil > 0 && Time.time > LmbUntil) { LmbHeld = false; LmbUntil = 0; }
            if (RmbUntil > 0 && Time.time > RmbUntil) { RmbHeld = false; RmbUntil = 0; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) Exec("giveall");
        }

        public static float PhotoLight;

        System.Collections.IEnumerator ReleaseKeys(UnityEngine.InputSystem.Keyboard kb)
        {
            yield return null; yield return null;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }

        System.Collections.IEnumerator PhotoRoutine(Camera cam, RenderTexture rt, string path, UnityEngine.Rendering.HighDefinition.HDAdditionalLightData light = null)
        {
            // HDRP needs a few frames for exposure/history to settle on a new camera
            for (int i = 0; i < 6; i++)
            {
                if (light != null) { light.lightUnit = UnityEngine.Rendering.HighDefinition.LightUnit.Lumen; light.intensity = PhotoLight; }
                yield return new WaitForEndOfFrame();
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            cam.targetTexture = null;
            Destroy(cam.gameObject);
            rt.Release();
        }

        static PlayerControllerB P => GameNetworkManager.Instance?.localPlayerController;

        static string V(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";

        string Exec(string cmd)
        {
            var a = cmd.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return "empty";
            var p = P;
            switch (a[0].ToLowerInvariant())
            {
                case "ping": return "pong";
                case "state":
                    {
                        if (p == null) return "no player";
                        var sb = new StringBuilder();
                        sb.Append($"pos={V(p.transform.position)} yaw={p.transform.eulerAngles.y:F0} pitch={p.cameraUp:F0} hp={p.health} dead={p.isPlayerDead} ");
                        sb.Append($"slot={p.currentItemSlot} held={(p.currentlyHeldObjectServer != null ? p.currentlyHeldObjectServer.itemProperties.itemName : "-")} ");
                        sb.Append("slots=[" + string.Join(",", p.ItemSlots.Select(s => s == null ? "-" : s.itemProperties.itemName + (s is StackItem st ? "x" + st.Count : ""))) + "] ");
                        var sv = Survival.Instance;
                        if (sv != null) sb.Append($"hunger={sv.Hunger} sat={sv.Saturation:F1} exh={sv.Exhaustion:F2} xp={Survival.XpTotal} lvl={sv.XpLevel} abs={sv.Absorption} ");
                        sb.Append($"blocks={BlockWorld.Instance?.Blocks.Count ?? -1} ship={BlockWorld.Instance?.Blocks.Keys.Count(k => k.Frame == 1) ?? -1} inShipPhase={StartOfRound.Instance?.inShipPhase} level={StartOfRound.Instance?.currentLevel?.PlanetName} ");
                        sb.Append($"gm={(GameModes.LocalCreative ? "creative" : "survival")} fly={CreativeFlight.Flying} grounded={p.thisController.isGrounded} ");
                        var t = FindObjectOfType<Terminal>();
                        if (t != null) sb.Append($"credits={t.groupCredits} ");
                        var b = Builder.Instance;
                        if (b != null) sb.Append($"target={(b.HasTarget ? b.TargetKey.ToString() + " " + BlockWorld.Instance.DefAt(b.TargetKey)?.Key : "-")} ");
                        sb.Append($"inShip={BlockWorld.InShip(p.transform.position)} grounded={p.thisController.isGrounded}");
                        return sb.ToString();
                    }
                case "give":
                    {
                        if (!BlockNet.IsServer) return "server only";
                        int count = a.Length > 2 ? int.Parse(a[2]) : 64;
                        if (a[1] == "all") return Exec("giveall");
                        ModItems.ServerGive(p, a[1], count);
                        return "ok";
                    }
                case "giveat":
                    {
                        // giveat x y z key [count]  (server)
                        if (!BlockNet.IsServer) return "server only";
                        var pos = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        if (!ModItems.ByKey.TryGetValue(a[4], out var it)) return "no item";
                        int cnt = a.Length > 5 ? int.Parse(a[5]) : 1;
                        if (it.spawnPrefab.GetComponent<StackItem>() != null) ModItems.ServerSpawnStack(it, cnt, pos);
                        else { var go = Instantiate(it.spawnPrefab, pos, Quaternion.identity, StartOfRound.Instance.propsContainer); go.GetComponent<Unity.Netcode.NetworkObject>().Spawn(); }
                        return "ok";
                    }
                case "giveall":
                    {
                        foreach (var k in new[] { "cobblestone", "oak_planks", "torch", "piston", "lever", "redstone_dust", "pickaxe" })
                            ModItems.ServerGive(p, k, 64);
                        return "ok";
                    }
                case "items": return string.Join(",", ModItems.ByKey.Keys);
                case "credits":
                    {
                        var t = FindObjectOfType<Terminal>();
                        t.groupCredits = int.Parse(a[1]);
                        if (BlockNet.IsServer) t.SyncGroupCreditsClientRpc(t.groupCredits, t.numberOfItemsInDropship);
                        return "ok " + t.groupCredits;
                    }
                case "tp":
                    p.TeleportPlayer(new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3])));
                    return "ok";
                case "tprel":
                    p.TeleportPlayer(p.transform.position + new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3])));
                    return "ok";
                case "look":
                    {
                        float yaw = float.Parse(a[1]), pitch = float.Parse(a[2]);
                        p.thisPlayerBody.eulerAngles = new Vector3(0, yaw, 0);
                        p.cameraUp = pitch;
                        p.gameplayCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
                        return "ok";
                    }
                case "slot":
                    HotbarInput.SelectSlot(p, int.Parse(a[1]));
                    return "ok slot=" + p.currentItemSlot;
                case "lmb":
                    {
                        if (a.Length > 1 && a[1] == "down") { LmbHeld = true; LmbUntil = 0; }
                        else if (a.Length > 1 && a[1] == "up") { LmbHeld = false; }
                        else { LmbHeld = true; LmbClick = true; LmbUntil = Time.time + (a.Length > 1 ? float.Parse(a[1]) : 0.1f); }
                        return "ok";
                    }
                case "rmb":
                    {
                        if (a.Length > 1 && a[1] == "down") { RmbHeld = true; RmbUntil = 0; }
                        else if (a.Length > 1 && a[1] == "up") { RmbHeld = false; }
                        else { RmbHeld = true; RmbClick = true; RmbUntil = Time.time + (a.Length > 1 ? float.Parse(a[1]) : 0.1f); }
                        return "ok";
                    }
                case "grab":
                    {
                        // pick up nearest grabbable within 4m
                        var g = FindObjectsOfType<GrabbableObject>().Where(o => !o.isHeld && o.grabbable && Vector3.Distance(o.transform.position, p.transform.position) < 4f)
                            .OrderBy(o => Vector3.Distance(o.transform.position, p.transform.position)).FirstOrDefault();
                        if (g == null) return "nothing";
                        var cam = p.gameplayCamera.transform;
                        var dir = g.transform.position - cam.position;
                        p.thisPlayerBody.eulerAngles = new Vector3(0, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0);
                        float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                        p.cameraUp = pitch;
                        p.gameplayCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
                        p.BeginGrabObject();
                        return "grabbing " + g.itemProperties.itemName;
                    }
                case "objs":
                    {
                        var list = FindObjectsOfType<GrabbableObject>().OrderBy(o => Vector3.Distance(o.transform.position, p.transform.position)).Take(12)
                            .Select(o => $"{o.itemProperties?.itemName}@{V(o.transform.position)} d={Vector3.Distance(o.transform.position, p.transform.position):F1} held={o.isHeld} grab={o.grabbable} spawned={o.IsSpawned} active={o.gameObject.activeInHierarchy}");
                        return string.Join(" ; ", list);
                    }
                case "hold":
                    {
                        // hold px py pz rx ry rz [modelScale]  - live-tune the held item's pose
                        var h = p.currentlyHeldObjectServer;
                        if (h == null) return "nothing held";
                        var ip = h.itemProperties;
                        if (a.Length >= 7)
                        {
                            ip.positionOffset = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                            ip.rotationOffset = new Vector3(float.Parse(a[4]), float.Parse(a[5]), float.Parse(a[6]));
                        }
                        var model = h.transform.Find("model");
                        if (a.Length >= 8 && model != null) model.localScale = Vector3.one * float.Parse(a[7]);
                        return $"pos={ip.positionOffset} rot={ip.rotationOffset} scale={(model != null ? model.localScale.x : 0)}";
                    }
                case "emit":
                    {
                        float ev = float.Parse(a[1]);
                        foreach (var m in new[] { Atlas.Emissive, Atlas.CutoutEmissive })
                            UnityEngine.Rendering.HighDefinition.HDMaterial.SetEmissiveIntensity(m, ev, UnityEditor.Rendering.HighDefinition.EmissiveIntensityUnit.EV100);
                        Atlas.EmissiveEV = ev;
                        return "ok kw=" + string.Join(",", Atlas.Emissive.shaderKeywords);
                    }
                case "amb":
                    Atlas.SetAmbient(float.Parse(a[1]));
                    return "ok";
                case "lights":
                    {
                        BlockWorld.DevLightScale = float.Parse(a[1]); // not persisted to config
                        foreach (var bi in BlockWorld.Instance.Blocks.Values) BlockWorld.Instance.UpdateVisual(bi);
                        return "ok";
                    }
                case "near":
                    {
                        var w = BlockWorld.Instance;
                        // near [radius] [x y z] : blocks around the player, or around a world point
                        var pos = a.Length > 4 ? new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])) : p.transform.position;
                        float radius = a.Length > 1 ? float.Parse(a[1]) : 6f;
                        var list = w.Blocks.Values.Where(b => b.Go != null && Vector3.Distance(b.Go.transform.position, pos) < radius)
                            .OrderBy(b => Vector3.Distance(b.Go.transform.position, pos))
                            .Select(b => $"{b.Data.Def.Key}{b.Key.Pos}y{b.Key.YOff} f{b.Data.Facing} s{b.Data.State}" +
                                (BlockWorld.Molds.TryGetValue(b.Key, out var md) ? $" mold:exp{(MoldData.HasExposure(md, Ground.MoldRes) ? md[Ground.MoldRes * Ground.MoldRes] : 63)}" : " cube"));
                        return string.Join(" ; ", list);
                    }
                case "where":
                    {
                        // where <blockkey> [radius] : world centres of those blocks near the player (any frame)
                        var w = BlockWorld.Instance;
                        float r = a.Length > 2 ? float.Parse(a[2]) : 15f;
                        return string.Join(" ; ", w.Blocks.Values.Where(b => b.Data.Def.Key == a[1] && b.Go != null && Vector3.Distance(b.Go.transform.position, p.transform.position) < r)
                            .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position))
                            .Select(b => { var c = b.Go.GetComponentInChildren<Renderer>() != null ? b.Go.GetComponentInChildren<Renderer>().bounds.center : b.Go.transform.position; return $"{b.Key.Pos}@{c.x:F2},{c.y:F2},{c.z:F2}"; }));
                    }
                case "aim":
                    {
                        // aim <blockkey> [standoff]  - teleport near the nearest block of that type and look at it
                        var w = BlockWorld.Instance;
                        var target = w.Blocks.Values.Where(b => b.Data.Def.Key == a[1] && b.Go != null)
                            .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                        if (target == null) return "none";
                        var c = target.Go.transform.position;
                        if (a.Length > 2)
                        {
                            // stand at an offset from the block (world units, horizontal dir from block toward player)
                            var dir = p.transform.position - c; dir.y = 0; dir.Normalize();
                            var stand = c + dir * float.Parse(a[2]);
                            stand.y = c.y - Plugin.S * 0.5f;
                            p.TeleportPlayer(stand);
                        }
                        var cam = p.gameplayCamera.transform.position;
                        if (a.Length > 2) cam = p.transform.position + Vector3.up * 2.3f;
                        var d = c - cam;
                        p.thisPlayerBody.eulerAngles = new Vector3(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
                        float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                        p.cameraUp = pitch;
                        p.gameplayCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
                        return "ok " + V(c);
                    }
                case "clearship":
                    ShipPersistence.Clear();
                    return "ok";
                case "usekey":
                    {
                        // usekey x y z  (ship frame, uses yoff of first ship block)
                        var w = BlockWorld.Instance;
                        var k = w.Blocks.Keys.FirstOrDefault(b => b.Pos == new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        BlockNet.RequestUse(k);
                        return "ok " + k;
                    }
                case "matinfo":
                    {
                        var sb = new StringBuilder();
                        foreach (var m in new[] { Atlas.Opaque, Atlas.Emissive, Atlas.CutoutEmissive })
                        {
                            var em = m.GetTexture("_EmissiveColorMap");
                            sb.Append($"{m.name}: shader={m.shader.name} emMap={(em != null ? em.name : "null")} emCol={m.GetColor("_EmissiveColor")} albedoAffect={(m.HasProperty("_AlbedoAffectEmissive") ? m.GetFloat("_AlbedoAffectEmissive") : -1)} kw=[{string.Join(",", m.shaderKeywords)}] queue={m.renderQueue}; ");
                        }
                        return sb.ToString();
                    }
                case "dumpatlas":
                    File.WriteAllBytes(Path.Combine(Plugin.PluginDir, "atlas_dump.png"), Atlas.Texture.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(Plugin.PluginDir, "atlas_emit_dump.png"), Atlas.Emission.EncodeToPNG());
                    return "ok";
                case "mine?":
                    return Builder.Instance != null ? Builder.Instance.DebugMine(p) : "no builder";
                case "place?":
                    {
                        var b = Builder.Instance;
                        return $"fail='{Builder.LastPlaceFailReason}' blocker='{Builder.LastBlocker}' now: {(b != null ? b.DebugPlace(p) : "-")}";
                    }
                case "drop":
                    p.DiscardHeldObject();
                    return "ok";
                case "hunger":
                    Survival.Instance.Hunger = int.Parse(a[1]);
                    if (a.Length > 2) Survival.Instance.Saturation = float.Parse(a[2]);
                    return "ok";
                case "hp":
                    p.health = int.Parse(a[1]);
                    HUDManager.Instance.UpdateHealthUI(p.health, false);
                    return "ok";
                case "hurt":
                    // hurt n [cause] : damage the local player (cause: Mauling, Gravity, Burning...)
                    p.DamagePlayer(int.Parse(a[1]), true, true, a.Length > 2 ? (CauseOfDeath)System.Enum.Parse(typeof(CauseOfDeath), a[2], true) : CauseOfDeath.Unknown);
                    return "ok hp=" + p.health;
                case "hier":
                    {
                        // hier [depth] [childPath] : the local player's transform tree with components (to find model parts)
                        int depth = a.Length > 1 ? int.Parse(a[1]) : 3;
                        var t0 = a.Length > 2 ? p.transform.Find(a[2]) : p.transform;
                        if (t0 == null) return "no " + a[2];
                        var sb = new System.Text.StringBuilder();
                        void Walk(Transform t, int d)
                        {
                            sb.Append(new string(' ', d * 2)).Append(t.name).Append(" L").Append(t.gameObject.layer).Append(t.gameObject.activeSelf ? "" : " (off)").Append(" [")
                              .Append(string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name))).Append("]\n");
                            if (d < depth) foreach (Transform c in t) Walk(c, d + 1);
                        }
                        Walk(t0, 0);
                        return sb.ToString();
                    }
                case "armorall":
                    // armorall : what everyone wears, as this game instance knows it
                    return string.Join(" ; ", Armor.All.Select(kv => kv.Key + "=" + string.Join(",", kv.Value.Select(k => k ?? "-"))));
                case "preview":
                    return ArmorPreview.DevTune(a);
                case "heal":
                    p.health = 100; HUDManager.Instance.UpdateHealthUI(100, false);
                    return "ok hp=" + p.health;
                case "xp":
                    Survival.AddXp(int.Parse(a[1]));
                    return "ok " + Survival.XpTotal;
                case "place":
                    {
                        // place <blockkey> dx dy dz [facing]  relative to the player's feet cell in the current frame
                        var def = Blocks.Get(a[1]);
                        if (def == null) return "no block " + a[1];
                        var world = BlockWorld.Instance;
                        byte frame = (byte)(BlockWorld.InShip(p.transform.position) ? 1 : 0);
                        world.FrameRoot(frame, true);
                        var feet = p.transform.position;
                        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var gh, 5f, (1 << 8) | (1 << 11) | (1 << 25) | 1, QueryTriggerInteraction.Ignore)) feet = gh.point;
                        var lp = world.ToFrameLocal(frame, feet) / Plugin.S;
                        int cy = Mathf.FloorToInt(lp.y);
                        short yoff = (short)Mathf.Clamp(Mathf.RoundToInt((lp.y - cy) * 1000f), 0, 999);
                        var baseCell = new Vector3Int(Mathf.FloorToInt(lp.x), cy, Mathf.FloorToInt(lp.z));
                        var key = new BlockKey(frame, yoff, baseCell + new Vector3Int(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4])));
                        byte facing = a.Length > 5 ? byte.Parse(a[5]) : (byte)1;
                        BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(def.Id, facing)));
                        Redstone.MarkDirty(); Gravity.MarkDirty();
                        return "ok " + key;
                    }
                case "placeg":
                    {
                        // placeg <blockkey> dx dy dz [facing] : place on the natural ground grid (frame 0, no offset), relative to the feet cell
                        var def = Blocks.Get(a[1]);
                        if (def == null) return "no block " + a[1];
                        BlockWorld.Instance.FrameRoot(0, true);
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var key = Ground.KeyOf(fc + new Vector3Int(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4])));
                        byte facing = a.Length > 5 ? byte.Parse(a[5]) : (byte)1;
                        BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(def.Id, facing)));
                        Redstone.MarkDirty(); Gravity.MarkDirty();
                        return "ok " + key;
                    }
                case "breakg":
                    {
                        // breakg dx dy dz : break whatever block is at that natural-grid cell (as the player would, with drops)
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var key = Ground.KeyOf(fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        if (!BlockWorld.Instance.Has(key)) return "none at " + key;
                        ServerLogic.HandleBreak(Unity.Netcode.NetworkManager.Singleton.LocalClientId, key, false);
                        return "broke " + key;
                    }
                case "use":
                    {
                        var b = Builder.Instance;
                        if (b == null || !b.HasTarget) return "no target";
                        BlockNet.RequestUse(b.TargetKey);
                        return "ok";
                    }
                case "land":
                    {
                        var lever = FindObjectOfType<StartMatchLever>();
                        if (lever == null) return "no lever";
                        lever.singlePlayerEnabled = true;
                        lever.leverHasBeenPulled = true;
                        lever.StartGame();
                        return "ok";
                    }
                case "leave":
                    {
                        StartOfRound.Instance.EndGameServerRpc(0);
                        return "ok landed=" + StartOfRound.Instance.shipHasLanded;
                    }
                case "route":
                    {
                        // route <index into levels>  (the RPC wants the level's own levelID)
                        int idx = int.Parse(a[1]);
                        var lvl = StartOfRound.Instance.levels[idx];
                        StartOfRound.Instance.ChangeLevelServerRpc(lvl.levelID, FindObjectOfType<Terminal>().groupCredits);
                        return "ok levelID=" + lvl.levelID;
                    }
                case "levels":
                    return string.Join(",", StartOfRound.Instance.levels.Select((l, i) => i + "=" + l.PlanetName));
                case "tpship":
                    p.TeleportPlayer(StartOfRound.Instance.playerSpawnPositions[0].position);
                    p.isInsideFactory = false;
                    return "ok";
                case "tpmain":
                    {
                        var e = FindObjectsOfType<EntranceTeleport>().FirstOrDefault(x => x.isEntranceToBuilding && x.entranceId == 0);
                        if (e == null) return "no entrance";
                        p.TeleportPlayer(e.entrancePoint.position);
                        p.isInsideFactory = false;
                        return "ok " + V(e.entrancePoint.position);
                    }
                case "flatspot":
                    {
                        // flatspot [n] : teleport to the n-th flattest outdoor AI node (7x7 cells, open sky), away from the
                        // ship and entrances (protected bedrock there) and from props/trees over the test area
                        int want = a.Length > 1 ? int.Parse(a[1]) : 0;
                        var nodes = RoundManager.Instance.outsideAINodes;
                        if (nodes == null || nodes.Length == 0) return "no nodes";
                        int mask = (1 << 8) | (1 << 11) | (1 << 25) | 1;
                        var protectedSpots = Facility.Entrances().Where(t => t != null).Select(t => t.position).ToList();
                        protectedSpots.Add(StartOfRound.Instance.elevatorTransform.position);
                        var cands = new List<(GameObject n, float score)>();
                        foreach (var n in nodes)
                        {
                            if (n == null) continue;
                            if (protectedSpots.Any(q => Vector3.Distance(q, n.transform.position) < 18f)) continue;
                            float minY = float.MaxValue, maxY = float.MinValue; int hits = 0; bool blocked = false;
                            for (int x = -3; x <= 3; x++)
                                for (int z = -3; z <= 3; z++)
                                {
                                    var o = n.transform.position + new Vector3(x * 1.4f, 6f, z * 1.4f);
                                    if (Physics.Raycast(o, Vector3.down, out var h, 12f, mask, QueryTriggerInteraction.Ignore))
                                    {
                                        hits++; minY = Mathf.Min(minY, h.point.y); maxY = Mathf.Max(maxY, h.point.y);
                                        if (h.collider.GetComponentInParent<BlockRef>() != null) blocked = true; // our own blocks from earlier tests
                                    }
                                }
                            if (hits < 49 || blocked) continue;
                            if (Physics.Raycast(n.transform.position + Vector3.up * 2f, Vector3.up, 60f, mask, QueryTriggerInteraction.Ignore)) continue; // need open sky
                            cands.Add((n, maxY - minY + Vector3.Distance(n.transform.position, StartOfRound.Instance.elevatorTransform.position) * 0.002f));
                        }
                        if (cands.Count == 0) return "none";
                        var best = cands.OrderBy(c => c.score).ElementAt(Mathf.Min(want, cands.Count - 1));
                        p.TeleportPlayer(best.n.transform.position + Vector3.up * 0.3f);
                        return $"ok {V(best.n.transform.position)} flat={best.score:F2} of {cands.Count}";
                    }
                case "slopespot":
                    {
                        // slopespot [n] : teleport to the n-th most "hillside" outdoor AI node (3-6 m height range over 5x5 cells, open sky)
                        int want = a.Length > 1 ? int.Parse(a[1]) : 0;
                        var nodes = RoundManager.Instance.outsideAINodes;
                        if (nodes == null || nodes.Length == 0) return "no nodes";
                        int mask = (1 << 8) | (1 << 11) | (1 << 25) | 1;
                        var cands = new List<(GameObject n, float score, float range)>();
                        foreach (var n in nodes)
                        {
                            if (n == null) continue;
                            float minY = float.MaxValue, maxY = float.MinValue; int hits = 0;
                            for (int x = -2; x <= 2; x++)
                                for (int z = -2; z <= 2; z++)
                                {
                                    var o = n.transform.position + new Vector3(x * 1.4f, 10f, z * 1.4f);
                                    if (Physics.Raycast(o, Vector3.down, out var h, 20f, mask, QueryTriggerInteraction.Ignore))
                                    { hits++; minY = Mathf.Min(minY, h.point.y); maxY = Mathf.Max(maxY, h.point.y); }
                                }
                            if (hits < 25) continue;
                            if (Physics.Raycast(n.transform.position + Vector3.up * 2f, Vector3.up, 60f, mask, QueryTriggerInteraction.Ignore)) continue;
                            float range = maxY - minY;
                            if (range < 2.5f || range > 7f) continue;
                            cands.Add((n, Mathf.Abs(range - 4f), range));
                        }
                        if (cands.Count == 0) return "none";
                        var pick = cands.OrderBy(c => c.score).ElementAt(Mathf.Min(want, cands.Count - 1));
                        p.TeleportPlayer(pick.n.transform.position + Vector3.up * 0.3f);
                        return $"ok {V(pick.n.transform.position)} range={pick.range:F1} of {cands.Count}";
                    }
                case "gotoblock":
                    {
                        // gotoblock <key>: teleport to the AI node nearest a block of that type, then look at it
                        var w = BlockWorld.Instance;
                        var blk = w.Blocks.Values.Where(b => b.Data.Def.Key == a[1] && b.Go != null).OrderBy(b => b.Go.transform.position.y < -100 ? 0 : 1).FirstOrDefault();
                        if (blk == null) return "none";
                        var c = blk.Go.transform.position;
                        var nodes = c.y < -100 ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
                        var node = nodes.Where(n => n != null && Vector3.Distance(n.transform.position, c) > 1.5f)
                            .OrderBy(n => Vector3.Distance(n.transform.position, c)).FirstOrDefault();
                        if (node == null) return "no node";
                        p.TeleportPlayer(node.transform.position);
                        p.isInsideFactory = c.y < -100;
                        var cam = node.transform.position + Vector3.up * 2.3f;
                        var d = c - cam;
                        p.thisPlayerBody.eulerAngles = new Vector3(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
                        float pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                        p.cameraUp = pt;
                        p.gameplayCamera.transform.localEulerAngles = new Vector3(pt, 0, 0);
                        return $"ok block={V(c)} node={V(node.transform.position)} dist={Vector3.Distance(node.transform.position, c):F1}";
                    }
                case "tpinside":
                    {
                        var e = FindObjectsOfType<EntranceTeleport>().FirstOrDefault(x => x.isEntranceToBuilding && x.entranceId == 0);
                        if (e == null) return "no entrance";
                        e.TeleportPlayer();
                        return "ok";
                    }
                case "tpnode":
                    {
                        var nodes = RoundManager.Instance.insideAINodes;
                        if (nodes == null || nodes.Length == 0) return "no nodes";
                        var n = nodes[int.Parse(a[1]) % nodes.Length];
                        p.TeleportPlayer(n.transform.position);
                        p.isInsideFactory = true;
                        return "ok " + V(n.transform.position);
                    }
                case "ores":
                    {
                        var list = BlockWorld.Instance.Blocks.Where(kv => kv.Value.Data.Def.ScrapValueMin > 0).Take(5).Select(kv => kv.Value.Data.Def.Key + "@" + V(BlockWorld.Instance.WorldCenter(kv.Key)));
                        return string.Join(" ", list);
                    }
                case "screenshot":
                    {
                        string path = a.Length > 1 ? a[1] : Path.Combine(Plugin.PluginDir, "shot.png");
                        ScreenCapture.CaptureScreenshot(path);
                        return "ok " + path;
                    }
                case "enemy":
                    {
                        // enemy <name> [minDistance]  - spawns at the inside/outside AI node closest to minDistance away
                        if (!BlockNet.IsServer) return "server only";
                        var all = Resources.FindObjectsOfTypeAll<EnemyType>();
                        var et = all.FirstOrDefault(e => e.enemyName.ToLower().Contains(a[1].ToLower()));
                        if (et == null) return "none: " + string.Join(",", all.Select(e => e.enemyName).Distinct());
                        float want = a.Length > 2 ? float.Parse(a[2]) : 15f;
                        var nodes = p.isInsideFactory ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
                        var n = nodes.Where(x => x != null).OrderBy(x => Mathf.Abs(Vector3.Distance(x.transform.position, p.transform.position) - want)).First();
                        RoundManager.Instance.SpawnEnemyGameObject(n.transform.position, 0, -1, et);
                        return $"spawned {et.enemyName} at {V(n.transform.position)} d={Vector3.Distance(n.transform.position, p.transform.position):F1}";
                    }
                case "surfaces":
                    return string.Join(",", StartOfRound.Instance.footstepSurfaces.Select(f => f.surfaceTag)) + " | under=" +
                        (Physics.Raycast(p.transform.position + Vector3.up, Vector3.down, out var sh, 3f, (1 << 8) | (1 << 11) | (1 << 25) | 1, QueryTriggerInteraction.Ignore) ? sh.collider.tag + "/" + sh.collider.name + "@" + LayerMask.LayerToName(sh.collider.gameObject.layer) : "-");
                case "terraininfo":
                    {
                        var sb = new StringBuilder();
                        if (!Physics.Raycast(p.transform.position + Vector3.up, Vector3.down, out var th, 5f, (1 << 8) | (1 << 11) | (1 << 25) | 1, QueryTriggerInteraction.Ignore)) return "no ground";
                        var col = th.collider;
                        sb.Append($"collider={col.GetType().Name} name={col.name} tag={col.tag} layer={LayerMask.LayerToName(col.gameObject.layer)} static={col.gameObject.isStatic} scene={col.gameObject.scene.name} ");
                        if (col is MeshCollider mc && mc.sharedMesh != null)
                            sb.Append($"colMesh={mc.sharedMesh.name} readable={mc.sharedMesh.isReadable} verts={mc.sharedMesh.vertexCount} convex={mc.convex} ");
                        var mf = col.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) sb.Append($"renderMesh={mf.sharedMesh.name} readable={mf.sharedMesh.isReadable} verts={mf.sharedMesh.vertexCount} subs={mf.sharedMesh.subMeshCount} ");
                        else sb.Append("no MeshFilter on collider; ");
                        var t = col.GetComponent<Terrain>();
                        if (t != null) sb.Append("UNITY TERRAIN ");
                        var rends = col.GetComponentsInChildren<MeshRenderer>().Length;
                        sb.Append($"childRenderers={rends} pos={V(col.transform.position)} scale={col.transform.lossyScale}");
                        // nearby render meshes that look like terrain
                        var near = FindObjectsOfType<MeshFilter>().Where(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 5000 && f.GetComponent<Renderer>() != null && f.GetComponent<Renderer>().bounds.Contains(new Vector3(p.transform.position.x, f.GetComponent<Renderer>().bounds.center.y, p.transform.position.z)))
                            .Take(5).Select(f => $"{f.name}:{f.sharedMesh.vertexCount}v readable={f.sharedMesh.isReadable}");
                        sb.Append(" | big meshes here: " + string.Join(", ", near));
                        return sb.ToString();
                    }
                case "walk":
                    {
                        // walk fwd right seconds [speed]  - moves through the real CharacterController each frame (collides like WASD)
                        walkDir = new Vector2(float.Parse(a[2]), float.Parse(a[1]));
                        walkUntil = Time.time + float.Parse(a[3]);
                        walkSpeed = a.Length > 4 ? float.Parse(a[4]) : 4.5f;
                        return "ok";
                    }
                case "crouch":
                    p.Crouch(a.Length < 2 || a[1] == "1");
                    return "crouching=" + p.isCrouching;
                case "jump":
                    p.Jump_performed(default);
                    return "ok";
                case "photo":
                    {
                        // photo path x y z yaw pitch [fov]  - renders a free camera to a PNG (player doesn't move)
                        var pos = new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4]));
                        var rot = Quaternion.Euler(float.Parse(a[6]), float.Parse(a[5]), 0);
                        var src = p.gameplayCamera;
                        var go = new GameObject("LMC_PhotoCam");
                        go.transform.SetPositionAndRotation(pos, rot);
                        var cam = go.AddComponent<Camera>();
                        cam.CopyFrom(src);
                        cam.transform.SetPositionAndRotation(pos, rot);
                        cam.fieldOfView = a.Length > 7 ? float.Parse(a[7]) : 66f;
                        cam.nearClipPlane = 0.05f;
                        var srcHd = src.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                        var hd = go.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>() ?? go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                        if (srcHd != null) srcHd.CopyTo(hd);
                        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                        cam.targetTexture = rt;
                        UnityEngine.Rendering.HighDefinition.HDAdditionalLightData pl = null;
                        if (PhotoLight > 0)
                        {
                            var lgo = new GameObject("LMC_PhotoLight");
                            lgo.transform.SetParent(go.transform, false);
                            lgo.transform.localPosition = new Vector3(0.3f, 0.4f, 0f);
                            pl = UnityEngine.Rendering.HighDefinition.GameObjectExtension.AddHDLight(lgo, UnityEngine.Rendering.HighDefinition.HDLightTypeAndShape.Point);
                            pl.EnableShadows(false);
                            pl.range = 25f;
                        }
                        StartCoroutine(PhotoRoutine(cam, rt, a[1], pl));
                        return "ok";
                    }
                case "holes":
                    {
                        var hs = Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshObstacle>().Where(o => o.name == "LMC_HoleNav").Select(o => $"{V(o.transform.position)} size={o.size} carve={o.carving} en={o.enabled} active={o.gameObject.activeInHierarchy} scale={o.transform.lossyScale}");
                        return string.Join(" ; ", hs);
                    }
                case "navat":
                    {
                        var q = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        bool on = UnityEngine.AI.NavMesh.SamplePosition(q, out var nh, 0.3f, UnityEngine.AI.NavMesh.AllAreas);
                        bool near = UnityEngine.AI.NavMesh.SamplePosition(q, out var nh2, 3f, UnityEngine.AI.NavMesh.AllAreas);
                        return $"onNav(0.3m)={on} nearest={(near ? V(nh2.position) + " d=" + Vector3.Distance(q, nh2.position).ToString("F2") : "none")}";
                    }
                case "ignite":
                    {
                        var t = BlockWorld.Instance.Blocks.Values.Where(b => b.Data.Def == Blocks.TNT).OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                        if (t == null) return "no tnt";
                        ServerLogic.Ignite(t.Key, a.Length > 1 ? int.Parse(a[1]) : 80);
                        return "lit " + t.Key;
                    }
                case "deadline":
                    {
                        var tod = TimeOfDay.Instance;
                        tod.timeUntilDeadline = tod.totalTime * 3f + 10f;
                        tod.daysUntilDeadline = 3;
                        if (BlockNet.IsServer) tod.SyncTimeClientRpc(tod.globalTime, (int)tod.timeUntilDeadline);
                        return "ok " + tod.timeUntilDeadline;
                    }
                case "objinfo":
                    {
                        var sb = new StringBuilder();
                        foreach (var t in FindObjectsOfType<Transform>().Where(t => a[1].StartsWith("~") ? t.name.Contains(a[1].Substring(1)) : t.name == a[1]).Take(a.Length > 2 ? int.Parse(a[2]) : 5)) try
                        {
                            sb.Append($"[{t.name} L{t.gameObject.layer} tag={t.tag} scene={t.gameObject.scene.name} active={t.gameObject.activeInHierarchy} comps=");
                            sb.Append(string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name)));
                            var mc = t.GetComponent<MeshCollider>(); var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                            sb.Append($" pos={V(t.position)} parent={t.parent?.name}");
                            foreach (var c in t.GetComponents<Collider>()) sb.Append($" {c.GetType().Name}(en={c.enabled} trig={c.isTrigger} b={c.bounds.size})");
                            if (mc != null) sb.Append($" col={mc.sharedMesh?.name} rd={mc.sharedMesh?.isReadable} v={mc.sharedMesh?.vertexCount} en={mc.enabled} convex={mc.convex} same={(mf != null && mf.sharedMesh == mc.sharedMesh)} cuv={(mc.sharedMesh != null && mc.sharedMesh.isReadable ? mc.sharedMesh.uv.Length : -1)} csub={mc.sharedMesh?.subMeshCount} mats={(mr != null ? mr.sharedMaterials.Length : -1)}");
                            if (mf != null) sb.Append($" mf={mf.sharedMesh?.name} rd={mf.sharedMesh?.isReadable}");
                            if (mr != null) sb.Append($" mr.en={mr.enabled} sb={mr.isPartOfStaticBatch} b={mr.bounds}");
                            TerrainCarver.CanCarve(t.gameObject, out _);
                            sb.Append(" why=" + TerrainCarver.WhyNot + "] ");
                        } catch (System.Exception e) { sb.Append(" ERR " + e.Message + "] "); }
                        {
                        }
                        return sb.ToString();
                    }
                case "colliders":
                    {
                        float r = a.Length > 1 ? float.Parse(a[1]) : 4f;
                        var sb = new StringBuilder();
                        foreach (var h in Physics.OverlapSphere(p.transform.position + Vector3.up, r, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore).Take(25))
                        {
                            string extra = h is MeshCollider mc ? $"mesh={mc.sharedMesh?.name} rd={mc.sharedMesh?.isReadable} v={mc.sharedMesh?.vertexCount} convex={mc.convex}" : h is BoxCollider bc ? $"box size={bc.size} scale={h.transform.lossyScale}" : h.GetType().Name;
                            var mf = h.GetComponent<MeshFilter>();
                            string ren = mf != null && mf.sharedMesh != null ? $" render={mf.sharedMesh.name} rrd={mf.sharedMesh.isReadable} same={(h is MeshCollider m2 && m2.sharedMesh == mf.sharedMesh)}" : " norender";
                            sb.Append($"[{h.name} L{h.gameObject.layer} tag={h.tag} {extra}{ren} parent={h.transform.parent?.name}] ");
                        }
                        return sb.ToString();
                    }
                case "gpuread":
                    {
                        // try reading a non-readable mesh from the GPU
                        var mf = FindObjectsOfType<MeshFilter>().Where(f => f.sharedMesh != null && !f.sharedMesh.isReadable).OrderBy(f => Vector3.Distance(f.transform.position, p.transform.position)).FirstOrDefault();
                        if (mf == null) return "no unreadable mesh";
                        var m = mf.sharedMesh;
                        try
                        {
                            var vb = m.GetVertexBuffer(0);
                            var ib = m.GetIndexBuffer();
                            var data = new byte[vb.count * vb.stride];
                            vb.GetData(data);
                            int posOff = m.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
                            var fmt = m.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position);
                            float x = System.BitConverter.ToSingle(data, posOff), y = System.BitConverter.ToSingle(data, posOff + 4), z = System.BitConverter.ToSingle(data, posOff + 8);
                            string r = $"mesh={m.name} verts={m.vertexCount} vb.count={vb.count} stride={vb.stride} posFmt={fmt} posOff={posOff} streams={m.vertexBufferCount} ib={ib?.count} ibStride={ib?.stride} v0=({x:F2},{y:F2},{z:F2}) bounds={m.bounds}";
                            vb.Dispose(); ib?.Dispose();
                            return r;
                        }
                        catch (System.Exception e) { return "gpu read failed: " + e.Message; }
                    }
                case "groundstats":
                    {
                        var w = BlockWorld.Instance;
                        int nat = w.Blocks.Values.Count(b => (b.Data.State & Blocks.NaturalGround) != 0);
                        int molds = BlockWorld.Molds.Count;
                        int bed = w.Blocks.Values.Count(b => b.Data.Def == Blocks.Bedrock);
                        return $"natural={nat} molded={molds} bedrock={bed} cuts={TerrainCarver.Cuts.Count} total={w.Blocks.Count}";
                    }
                case "digrel":
                    {
                        // digrel dx dy dz : remove the natural cell at feet-cell + offset (raw ground or block)
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var c = fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var k = Ground.KeyOf(c);
                        if (BlockWorld.Instance.Has(k)) { ServerLogic.BreakBlock(k, true, p); return "broke block " + c; }
                        // raw ground: dig from straight above the cell
                        var top = new Vector3((c.x + 0.5f) * Plugin.S, (c.y + 3) * Plugin.S, (c.z + 0.5f) * Plugin.S);
                        if (!Physics.Raycast(top, Vector3.down, out var gh, 6 * Plugin.S, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore)) return "no ground";
                        Ground.Dig(Unity.Netcode.NetworkManager.Singleton.LocalClientId, gh.point, gh.normal);
                        return "dug raw " + c + " hit=" + V(gh.point);
                    }
                case "digcell":
                    {
                        // digcell dx dy dz : remove the natural cell at feet-cell + offset, whatever it is (walls, ceilings...)
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var c = fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var info = Ground.Describe(c);
                        return Ground.DigCell(Unity.Netcode.NetworkManager.Singleton.LocalClientId, c) + " " + c + " " + info;
                    }
                case "digcol":
                    {
                        // digcol x z yFrom yTo : dig a vertical shaft (absolute cell coords)
                        int x = int.Parse(a[1]), z = int.Parse(a[2]), y0 = int.Parse(a[3]), y1 = int.Parse(a[4]);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var res = new Dictionary<string, int>();
                        for (int y = y0; y >= y1; y--)
                        {
                            var r = Ground.DigCell(Unity.Netcode.NetworkManager.Singleton.LocalClientId, new Vector3Int(x, y, z));
                            res[r] = res.TryGetValue(r, out int n) ? n + 1 : 1;
                            if (r == "bedrock") break;
                        }
                        return string.Join(", ", res.Select(kv => kv.Key + " x" + kv.Value)) + $" in {sw.ElapsedMilliseconds} ms";
                    }
                case "facility":
                    {
                        bool ok = Facility.Bounds(out var fb);
                        return $"have={ok} bounds={fb} inTile={Facility.DebugInTile(p.transform.position)} inside={p.isInsideFactory} elev={p.isInElevator} hangar={p.isInHangarShipRoom} cuts={TerrainCarver.Cuts.Count}";
                    }
                case "showname":
                    {
                        // showname <name> 0|1 : toggle renderers of objects with this name (debug what draws what)
                        int n = 0;
                        foreach (var r in FindObjectsOfType<Renderer>(true).Where(r => r.name == a[1])) { r.enabled = a[2] == "1"; n++; }
                        return "toggled " + n;
                    }
                case "rayinfo":
                    {
                        // rayinfo x y z dx dy dz : everything a ray hits (all layers), with renderer/material info
                        var o = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        var d = new Vector3(float.Parse(a[4]), float.Parse(a[5]), float.Parse(a[6])).normalized;
                        var sb = new StringBuilder();
                        bool prevBf = Physics.queriesHitBackfaces;
                        Physics.queriesHitBackfaces = true;
                        try
                        {
                            foreach (var h in Physics.RaycastAll(o, d, 30f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
                                sb.Append($"[{h.distance:F2} {h.collider.name} L{h.collider.gameObject.layer} trig={h.collider.isTrigger} back={Vector3.Dot(h.normal, d) > 0} par={h.collider.transform.parent?.name}] ");
                            // what the player's capsule would hit
                            var cc = p.thisController;
                            var top = o + Vector3.up * (cc.height * 0.5f - cc.radius); var bot = o - Vector3.up * (cc.height * 0.5f - cc.radius);
                            foreach (var h in Physics.CapsuleCastAll(bot, top, cc.radius * 0.95f, d, 5f, ~((1 << 3) | (1 << 18) | (1 << 13) | (1 << 22) | (1 << 9) | (1 << 2)), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(4))
                                sb.Append($"<CAP {h.distance:F2} {h.collider.name} L{h.collider.gameObject.layer} at={V(h.point)} n={h.normal} par={h.collider.transform.parent?.name}> ");
                        }
                        finally { Physics.queriesHitBackfaces = prevBf; }
                        return sb.ToString();
                    }
                case "cutlist":
                    return string.Join(" ; ", TerrainCarver.Cuts.Select(c => c.Path.Substring(Math.Max(0, c.Path.Length - 60)) + " x" + c.Mins.Length));
                case "hdrpholes":
                    {
                        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset;
                        if (asset == null) return "no hdrp asset";
                        var st = asset.currentPlatformRenderPipelineSettings;
                        var t = Terrain.activeTerrain;
                        string kw = t != null && t.materialTemplate != null ? string.Join(",", t.materialTemplate.shaderKeywords) : "-";
                        return $"supportTerrainHole={st.supportTerrainHole} terrainMat={t?.materialTemplate?.name} kw=[{kw}] instanced={t?.drawInstanced}";
                    }
                case "terrpatch":
                    {
                        // terrpatch half step lift [drawTerrain 0/1]
                        var t = Terrain.activeTerrains.OrderBy(x => Vector3.Distance(x.transform.position + x.terrainData.size * 0.5f, p.transform.position)).FirstOrDefault();
                        if (t == null) return "no terrain";
                        var go = TerrainCarver.BuildTerrainPatch(t, p.transform.position, float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        if (a.Length > 4) t.drawHeightmap = a[4] == "1";
                        return "ok " + go.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                    }
                case "raysat":
                    {
                        var c = new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        return Ground.RaysAt(Ground.Center(c));
                    }
                case "carvedinfo":
                    return TerrainCarver.CarvedDebug(a[1]);
                case "presskey":
                    {
                        // presskey <key> : press and release a keyboard key through the Input System (tests real bindings)
                        if (!System.Enum.TryParse<UnityEngine.InputSystem.Key>(a[1], true, out var key)) return "unknown key";
                        var kb = UnityEngine.InputSystem.Keyboard.current;
                        if (kb == null) return "no keyboard";
                        UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                        StartCoroutine(ReleaseKeys(kb));
                        return "pressed " + key;
                    }
                case "clearinv":
                    for (int i = 0; i < p.ItemSlots.Length; i++) if (p.ItemSlots[i] != null) p.DestroyItemInSlotAndSync(i);
                    return "ok";
                case "invgive":
                    Inventory.Give(a[1], int.Parse(a[2]));
                    return "ok";
                case "craftui":
                    {
                        // craftui open [table] | close | state | click grid|out|hot|outside index [right] [shift]
                        var ui = CraftingUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open": CraftingUI.Open(a.Length > 2 && a[2] == "table"); return ui.DevState();
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "chestui":
                    {
                        // chestui open x y z | close | state | click chest|hot|outside index [right] [shift]
                        var ui = ChestUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open":
                                if (a[2] == "near")
                                {
                                    // the closest chest block (any frame, e.g. in the ship)
                                    var w = BlockWorld.Instance;
                                    var near = w.Blocks.Values.Where(b => b.Data.Def == Blocks.Chest && b.Go != null)
                                        .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                                    if (near == null) return "no chest";
                                    ChestUI.Open(near.Key);
                                }
                                else ChestUI.Open(Ground.KeyOf(new Vector3Int(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]))));
                                return ui.DevState();
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "creativeui":
                    {
                        // creativeui open | close | state | click items|tabs|hot|outside index [right] [shift] | pos area index
                        var ui = CreativeUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open": CreativeUI.Open(); return ui.DevState();
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "ship":
                    {
                        // ship : where the ship is, its bounds, and the local player's ship flags
                        var sor = StartOfRound.Instance;
                        return $"elevator={V(sor.elevatorTransform.position)} bounds={sor.shipBounds.bounds.min}..{sor.shipBounds.bounds.max} room={sor.shipInnerRoomBounds.bounds.min}..{sor.shipInnerRoomBounds.bounds.max} " +
                               $"player inElevator={p.isInElevator} inRoom={p.isInHangarShipRoom} parent={(p.transform.parent != null ? p.transform.parent.name : "-")} pos={V(p.transform.position)} landed={sor.shipHasLanded} leaving={sor.shipIsLeaving}";
                    }
                case "fire?":
                    {
                        // fire? : where a flint and steel strike would put fire right now, and the server's checks there
                        var bld = FindObjectOfType<Builder>();
                        if (!bld.ComputePlacement(p, Blocks.Fire, out var fk, out _)) return "no placement: " + bld.DebugMine(p);
                        var w = BlockWorld.Instance;
                        return $"key={fk} has={w.Has(fk)} obstructed0.6={ServerLogic.Obstructed(fk, 0.6f)} obstructed0.9={ServerLogic.Obstructed(fk)} supported={ServerLogic.Supported(fk)} out={Redstone.OutOfWorld(fk)} worldFrame={w.WorldFrameAvailable}";
                    }
                case "storm":
                    // storm [0|1] : metal items the storm can strike; 0/1 turns joining mid-storm off/on
                    if (a.Length > 1) Storms.Enabled = a[1] == "1";
                    return (Storms.Describe() ?? "no storm") + " join=" + Storms.Enabled;
                case "weather":
                    {
                        // weather <None|Rainy|Stormy|Foggy|Flooded|Eclipsed> : the routed moon's weather for the next landing (in orbit)
                        if (a.Length > 1) StartOfRound.Instance.currentLevel.currentWeather = (LevelWeatherType)System.Enum.Parse(typeof(LevelWeatherType), a[1], true);
                        return StartOfRound.Instance.currentLevel.PlanetName + " " + StartOfRound.Instance.currentLevel.currentWeather;
                    }
                case "shipcarry":
                    if (a.Length > 1) ShipCarry.Enabled = a[1] == "1";
                    return "shipcarry=" + ShipCarry.Enabled;
                case "rmkey":
                    {
                        // rmkey frame yoff x y z : remove one block from any grid (test cleanup)
                        var k = new BlockKey(byte.Parse(a[1]), short.Parse(a[2]), new Vector3Int(int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5])));
                        if (!BlockWorld.Instance.Has(k)) return "none";
                        BlockNet.ServerBroadcastOp(Op.Remove(k, false));
                        return "removed " + k;
                    }
                case "chattext":
                    {
                        // chattext <text> : what's in the chat box ("_" = space; the box takes keystrokes as IMGUI events,
                        // which injected input doesn't make); no text: read it
                        var hud = HUDManager.Instance;
                        if (a.Length > 1) hud.chatTextField.text = string.Join(" ", a.Skip(1)).Replace("_", " ");
                        return $"typing={p.isTypingChat} text='{hud.chatTextField.text}' chat='{hud.chatText.text.Replace("\n", " | ")}'";
                    }
                case "gamemode":
                    // gamemode <mode> [player] : the /gamemode chat command, as the local player
                    return GameModeCommand.Run(p, a.Skip(1).ToArray());
                case "gamemodes":
                    return "creative=[" + string.Join(",", GameModes.All) + "] local=" + Unity.Netcode.NetworkManager.Singleton.LocalClientId +
                        " players=" + string.Join(",", StartOfRound.Instance.allPlayerScripts.Where(x => x.isPlayerControlled).Select(x => $"{x.playerUsername}#{x.actualClientId}"));
                case "terrinfo":
                    return TerrainCarver.TerrainDebug();
                case "god":
                    DevDeathLog.God = a.Length < 2 || a[1] == "1";
                    return "god=" + DevDeathLog.God;
                case "photolight":
                    PhotoLight = float.Parse(a[1]);
                    return "ok";
                case "layers":
                    return string.Join(", ", Enumerable.Range(0, 32).Select(i => i + "=" + LayerMask.LayerToName(i)).Where(s => !s.EndsWith("=")));
                case "cellobjs":
                    {
                        // cellobjs dx dy dz : every renderer/collider overlapping that cell and whether we can cut it
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var c = fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var S = Plugin.S;
                        var box = new Bounds(Ground.Center(c), Vector3.one * S);
                        var sb = new StringBuilder();
                        foreach (var r in FindObjectsOfType<Renderer>().Where(r => r.bounds.Intersects(box)))
                        {
                            bool ok = TerrainCarver.CanCarve(r.gameObject, out _);
                            var mf = r.GetComponent<MeshFilter>();
                            sb.Append($"[R {r.name} L{r.gameObject.layer} en={r.enabled} sb={r.isPartOfStaticBatch} col={(r.GetComponent<Collider>()?.GetType().Name ?? "-")} mesh={mf?.sharedMesh?.name} rd={mf?.sharedMesh?.isReadable} size={r.bounds.size.magnitude:F0} carve={ok}:{TerrainCarver.WhyNot} par={r.transform.parent?.name}] ");
                        }
                        foreach (var h in Physics.OverlapBox(box.center, box.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (h.GetComponent<Renderer>() != null) continue;
                            bool ok = TerrainCarver.CanCarve(TerrainCarver.GroundObject(h), out _);
                            sb.Append($"[C {h.name} {h.GetType().Name} L{h.gameObject.layer} carve={ok}:{TerrainCarver.WhyNot} par={h.transform.parent?.name}] ");
                        }
                        return sb.ToString();
                    }
                case "digabs":
                    {
                        // digabs x y z : remove the natural cell at absolute grid coords
                        // digabs x y z [force] : force = dig it like a player hitting whatever is in the cell (thin fences, trim...)
                        var c = new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        GameObject hitObj = null;
                        if (a.Length > 4 && a[4] == "force")
                        {
                            var ctr = Ground.Center(c); var half = Vector3.one * (Plugin.S * 0.5f);
                            hitObj = TerrainCarver.ObjectsIn(ctr - half, ctr + half).FirstOrDefault();
                        }
                        return Ground.DigCell(Unity.Netcode.NetworkManager.Singleton.LocalClientId, c, hitObj) + " " + c;
                    }
                case "placeabs":
                    {
                        // placeabs <blockkey> x y z [facing] : place on the natural grid at absolute cell coords
                        var def = Blocks.Get(a[1]);
                        if (def == null) return "no block " + a[1];
                        BlockWorld.Instance.FrameRoot(0, true);
                        var key = Ground.KeyOf(new Vector3Int(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4])));
                        BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(def.Id, a.Length > 5 ? byte.Parse(a[5]) : (byte)1)));
                        Redstone.MarkDirty(); Gravity.MarkDirty();
                        return "ok " + key;
                    }
                case "breakabs":
                    {
                        var key = Ground.KeyOf(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        if (!BlockWorld.Instance.Has(key)) return "none at " + key;
                        ServerLogic.HandleBreak(Unity.Netcode.NetworkManager.Singleton.LocalClientId, key, false);
                        return "broke " + key;
                    }
                case "props":
                    {
                        // props x y z : furniture in that natural-grid cell (solid, uncuttable, not level mesh): shelves, crates...
                        var c = new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var k = Ground.KeyOf(c);
                        var wc = BlockWorld.Instance.WorldCenter(k);
                        var names = new List<string>();
                        foreach (var h in Physics.OverlapBox(wc, Vector3.one * (BlockWorld.S * 0.4f), Quaternion.identity, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
                        {
                            if (h is MeshCollider mc && !mc.convex) continue;
                            if (h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<PlayerControllerB>() != null) continue;
                            if (TerrainCarver.CanCarve(h, out _)) continue;
                            names.Add(h.name);
                        }
                        return names.Count == 0 ? "none" : string.Join(",", names);
                    }
                case "obstructed":
                    {
                        // obstructed x y z : would level geometry / props stop a block in this natural-grid cell?
                        var k = Ground.KeyOf(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        return ServerLogic.Obstructed(k, 0.8f) ? "yes" : "no";
                    }
                case "cellabs":
                    {
                        var c = new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        return c + " " + Ground.Describe(c);
                    }
                case "cellinfo":
                    {
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var c = fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var d = Ground.Describe(c);
                        return c + " " + d + $" ({sw.ElapsedMilliseconds} ms) inside={p.isInsideFactory}";
                    }
                case "clearworld":
                    {
                        var w = BlockWorld.Instance;
                        var ops = w.Blocks.Where(kv => kv.Key.Frame == 0 && kv.Value.Data.Def.ScrapValueMin == 0 && !(kv.Value.Data.Def == Blocks.Stone && kv.Value.Data.State == 1) && (kv.Value.Data.State & Blocks.NaturalGround) == 0)
                            .Select(kv => Op.Remove(kv.Key, false)).ToList();
                        BlockNet.ServerBroadcastOps(ops);
                        return "removed " + ops.Count;
                    }
                case "probe":
                    return Builder.Instance != null ? Builder.Instance.Probe() : "no builder";
                case "fps":
                    {
                        float sum = 0; int n = 0;
                        foreach (var f in frameTimes) { sum += f; n++; }
                        return n == 0 ? "no data" : $"avg_fps={n / sum:F1} worst_ms={frameTimes.Max() * 1000f:F1} blocks={BlockWorld.Instance?.Blocks.Count}";
                    }
                case "fill":
                    {
                        // fill dx1 dy1 dz1 dx2 dy2 dz2 block   (relative to feet cell, current frame)
                        var def = Blocks.Get(a[7]);
                        var world = BlockWorld.Instance;
                        byte frame = (byte)(BlockWorld.InShip(p.transform.position) ? 1 : 0);
                        world.FrameRoot(frame, true);
                        var lp = world.ToFrameLocal(frame, p.transform.position) / Plugin.S;
                        int cy = Mathf.FloorToInt(lp.y);
                        short yoff = (short)Mathf.Clamp(Mathf.RoundToInt((lp.y - cy) * 1000f), 0, 999);
                        var b0 = new Vector3Int(Mathf.FloorToInt(lp.x), cy, Mathf.FloorToInt(lp.z));
                        int[] v = a.Skip(1).Take(6).Select(int.Parse).ToArray();
                        var ops = new List<Op>();
                        for (int x = Mathf.Min(v[0], v[3]); x <= Mathf.Max(v[0], v[3]); x++)
                            for (int y = Mathf.Min(v[1], v[4]); y <= Mathf.Max(v[1], v[4]); y++)
                                for (int z = Mathf.Min(v[2], v[5]); z <= Mathf.Max(v[2], v[5]); z++)
                                {
                                    var k = new BlockKey(frame, yoff, b0 + new Vector3Int(x, y, z));
                                    if (!world.Has(k)) ops.Add(Op.Set(k, new BlockData(def.Id)));
                                }
                        BlockNet.ServerBroadcastOps(ops);
                        return "filled " + ops.Count;
                    }
                case "noise":
                    RoundManager.Instance.PlayAudibleNoise(p.transform.position, 40f, 1f, 0, false, 0);
                    return "ok";
                case "boxme":
                    {
                        // enclose the local player in a 3x3 (inner 1x1) cobblestone hut: 2-high ring + roof
                        var w = BlockWorld.Instance;
                        byte fr = (byte)(BlockWorld.InShip(p.transform.position) ? 1 : 0);
                        w.FrameRoot(fr, true);
                        var feet = p.transform.position;
                        float yb = w.ToFrameLocal(fr, feet).y / Plugin.S; int cy = Mathf.FloorToInt(yb);
                        short yo = (short)Mathf.Clamp(Mathf.RoundToInt((yb - cy) * 1000f), 0, 999);
                        var lp = w.ToFrameLocal(fr, feet) / Plugin.S;
                        var c0 = new Vector3Int(Mathf.FloorToInt(lp.x), cy, Mathf.FloorToInt(lp.z));
                        var ops = new List<Op>();
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dy = 0; dy <= 2; dy++)
                                {
                                    bool ring = !(dx == 0 && dz == 0);
                                    if (dy < 2 && !ring) continue;
                                    var k = new BlockKey(fr, yo, c0 + new Vector3Int(dx, dy, dz));
                                    if (!w.Has(k)) ops.Add(Op.Set(k, new BlockData(Blocks.Cobblestone.Id)));
                                }
                        BlockNet.ServerBroadcastOps(ops);
                        return "boxed with " + ops.Count;
                    }
                case "boxenemy":
                    {
                        // surround the nearest living enemy with a 2-high cobblestone ring
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "no enemy";
                        var w = BlockWorld.Instance;
                        w.FrameRoot(0, true);
                        var ep = e.transform.position;
                        if (Physics.Raycast(ep + Vector3.up, Vector3.down, out var eh, 3f, (1 << 8) | (1 << 11) | 1, QueryTriggerInteraction.Ignore)) ep = eh.point;
                        float yb = ep.y / Plugin.S; int cy = Mathf.FloorToInt(yb);
                        short yo = (short)Mathf.Clamp(Mathf.RoundToInt((yb - cy) * 1000f), 0, 999);
                        var c0 = new Vector3Int(Mathf.FloorToInt(ep.x / Plugin.S), cy, Mathf.FloorToInt(ep.z / Plugin.S));
                        var ops = new List<Op>();
                        for (int dx = -2; dx <= 2; dx++)
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                if (Mathf.Abs(dx) < 2 && Mathf.Abs(dz) < 2) continue;
                                for (int dy = 0; dy < 2; dy++)
                                {
                                    var k = new BlockKey(0, yo, c0 + new Vector3Int(dx, dy, dz));
                                    if (!w.Has(k) && !ServerLogic.Obstructed(k, 0.8f)) ops.Add(Op.Set(k, new BlockData(Blocks.Cobblestone.Id)));
                                }
                            }
                        BlockNet.ServerBroadcastOps(ops);
                        return $"boxed {e.enemyType.enemyName} with {ops.Count} blocks";
                    }
                case "toterminal":
                    {
                        var t = FindObjectOfType<Terminal>();
                        var trig = t.GetComponentInChildren<InteractTrigger>() ?? t.transform.parent?.GetComponentInChildren<InteractTrigger>();
                        var tp = trig != null ? trig.transform.position : t.transform.position;
                        // stand ~1.6m in front of the terminal screen on the ship floor
                        var fwd = (trig != null ? trig.transform : t.transform).forward; fwd.y = 0; fwd.Normalize();
                        var stand = tp + fwd * 1.6f;
                        if (Physics.Raycast(stand + Vector3.up, Vector3.down, out var fh, 5f, (1 << 8) | (1 << 11) | 1, QueryTriggerInteraction.Ignore)) stand = fh.point;
                        p.TeleportPlayer(stand);
                        var d = tp - (stand + Vector3.up * 2.3f);
                        p.thisPlayerBody.eulerAngles = new Vector3(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
                        float pt = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                        p.cameraUp = pt; p.gameplayCamera.transform.localEulerAngles = new Vector3(pt, 0, 0);
                        return "ok " + V(tp);
                    }
                case "dropship":
                    {
                        var ds = FindObjectOfType<ItemDropship>();
                        if (ds == null) return "none";
                        string st = $"landed={ds.shipLanded} opened={ds.shipDoorsOpened} delivering={ds.deliveringOrder} timer={ds.shipTimer:F1} pos={V(ds.transform.position)}";
                        if (a.Length > 1 && a[1] == "open") { ds.TryOpeningShip(); st += " -> opening"; }
                        if (a.Length > 1 && a[1] == "go") p.TeleportPlayer(ds.transform.position + ds.transform.forward * -4f);
                        return st;
                    }
                case "dayfreeze":
                    {
                        // dayfreeze 1|0 : stop/restart the day clock (long play-tests outlast a day)
                        var tod = TimeOfDay.Instance;
                        if (a.Length > 1 && a[1] == "1") { if (tod.globalTimeSpeedMultiplier > 0) savedDaySpeed = tod.globalTimeSpeedMultiplier; tod.globalTimeSpeedMultiplier = 0f; }
                        else if (savedDaySpeed > 0) tod.globalTimeSpeedMultiplier = savedDaySpeed;
                        return "day speed " + tod.globalTimeSpeedMultiplier;
                    }
                case "navpath":
                    {
                        // navpath x y z : walking route (navmesh corners) from the player to a point, for the play-test pilot
                        var to = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        UnityEngine.AI.NavMesh.SamplePosition(p.transform.position, out var hs, 3f, UnityEngine.AI.NavMesh.AllAreas);
                        UnityEngine.AI.NavMesh.SamplePosition(to, out var ht, 5f, UnityEngine.AI.NavMesh.AllAreas);
                        var path = new UnityEngine.AI.NavMeshPath();
                        if (!UnityEngine.AI.NavMesh.CalculatePath(hs.position, ht.position, UnityEngine.AI.NavMesh.AllAreas, path)) return "no path";
                        return path.status + " " + string.Join(" ", path.corners.Select(c => $"{c.x:F2},{c.y:F2},{c.z:F2}"));
                    }
                case "lightshape":
                    {
                        // lightshape <radiusBlocks> <nearFloor> : live-tune block lights (sphere radius, near-camera dim floor)
                        BlockWorld.DevLightRadius = float.Parse(a[1]);
                        if (a.Length > 2) BlockWorld.DevNearFloor = float.Parse(a[2]);
                        foreach (var b in BlockWorld.Instance.Blocks.Values)
                            if (b.Light != null) b.Light.shapeRadius = BlockWorld.DevLightRadius * BlockWorld.S;
                        return "ok";
                    }
                case "lightinfo":
                    {
                        var all = FindObjectsOfType<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                        var near = all.OrderBy(l => Vector3.Distance(l.transform.position, p.transform.position)).Take(int.Parse(a.Length > 1 ? a[1] : "8"));
                        var sb = new StringBuilder();
                        foreach (var l in near)
                        {
                            var lt = l.GetComponent<Light>();
                            sb.Append($"{l.name} d={Vector3.Distance(l.transform.position, p.transform.position):F1} type={lt.type} int={l.intensity:F1} unit={l.lightUnit} lightInt={lt.intensity:F1} range={lt.range:F1} layers={l.lightlayersMask} vol={l.affectsVolumetric} col={lt.color} en={lt.enabled}/{l.gameObject.activeInHierarchy}; ");
                        }
                        var held = p.currentlyHeldObjectServer;
                        return sb.ToString();
                    }
                case "save":
                    GameNetworkManager.Instance.SaveGame();
                    return "ok";
                case "leave_check":
                    return "landed=" + StartOfRound.Instance.shipHasLanded;
                case "absorb":
                    return "absorbed " + ShipAttach.AbsorbAttachedStructures();
                case "find":
                    {
                        string q = string.Join(" ", a.Skip(1)).ToLower();
                        var list = FindObjectsOfType<GrabbableObject>().Where(o => o.itemProperties != null && o.itemProperties.itemName.ToLower().Contains(q))
                            .Select(o => $"{o.itemProperties.itemName}{(o is StackItem st ? "x" + st.Count : "")}@{V(o.transform.position)} held={o.isHeld} val={o.scrapValue}");
                        return string.Join(" ; ", list);
                    }
                case "meshtest":
                    {
                        // meshtest <name> <step> : swap the object's render mesh for a copy of its ORIGINAL (collider) mesh rebuilt up to
                        // a step: 0 original, 1 plain copy, 2 +vertices, 3 +normals/tangents/colors, 4 +uvs (Vector4), 5 +triangles
                        var go = FindObjectsOfType<MeshFilter>().FirstOrDefault(f => f.name == a[1] && f.GetComponent<MeshCollider>() != null)?.gameObject;
                        if (go == null) return "not found";
                        var orig = go.GetComponent<MeshCollider>().sharedMesh;
                        var mf = go.GetComponent<MeshFilter>();
                        int step = int.Parse(a[2]);
                        if (step == 0) { mf.sharedMesh = orig; return "original " + orig.name; }
                        var m = Instantiate(orig); m.name = orig.name + "_test" + step;
                        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        var pos = new List<Vector3>(); orig.GetVertices(pos);
                        if (step >= 2) m.SetVertices(pos);
                        if (step >= 3)
                        {
                            var n = new List<Vector3>(); orig.GetNormals(n); if (n.Count == pos.Count) m.SetNormals(n);
                            var t = new List<Vector4>(); orig.GetTangents(t); if (t.Count == pos.Count) m.SetTangents(t);
                            var c = new List<Color>(); orig.GetColors(c); if (c.Count == pos.Count) m.SetColors(c);
                        }
                        var dims = new List<string>();
                        for (int ch = 0; ch < 8; ch++)
                        {
                            dims.Add(orig.GetVertexAttributeDimension((UnityEngine.Rendering.VertexAttribute)((int)UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch)).ToString());
                            if (step < 4) continue;
                            var l = new List<Vector4>(); orig.GetUVs(ch, l); if (l.Count == pos.Count) m.SetUVs(ch, l);
                        }
                        if (step >= 5) for (int sm = 0; sm < orig.subMeshCount; sm++) m.SetTriangles(orig.GetTriangles(sm), sm, false);
                        m.RecalculateBounds();
                        mf.sharedMesh = m;
                        return $"step {step}: verts={m.vertexCount} sub={m.subMeshCount} uvdims=[{string.Join(",", dims)}] attrs=[{string.Join(",", orig.GetVertexAttributes().Select(x => x.attribute + ":" + x.format + "x" + x.dimension))}]";
                    }
                case "carvecheck":
                    {
                        var probs = TerrainCarver.IntegrityProblems();
                        return probs.Count == 0 ? "ok" : string.Join(" | ", probs);
                    }
                case "storeprices":
                    {
                        // storeprices : every item the store sells, name=price (before sales)
                        var t = FindObjectOfType<Terminal>();
                        if (t == null) return "no terminal";
                        return string.Join(", ", t.buyableItemsList.Select(i => i.itemName + "=" + i.creditsWorth));
                    }
                case "termparse":
                    {
                        // termparse <text> : what the terminal would do with a typed line (the node it resolves to; nothing is bought)
                        var t = FindObjectOfType<Terminal>();
                        if (t == null) return "no terminal";
                        string typed = string.Join(" ", a.Skip(1));
                        string keep = t.screenText.text, keepCurrent = t.currentText; int keepAdded = t.textAdded;
                        // (currentText must match too: the terminal reverts any edit that looks like deleting into old text)
                        t.modifyingText = true; t.screenText.text = "\n\n>" + typed; t.currentText = t.screenText.text; t.textAdded = typed.Length;
                        var node = (TerminalNode)HarmonyLib.AccessTools.Method(typeof(Terminal), "ParsePlayerSentence").Invoke(t, null);
                        string seen = t.screenText.text.Substring(t.screenText.text.Length - t.textAdded);
                        t.modifyingText = true; t.screenText.text = keep; t.currentText = keepCurrent; t.textAdded = keepAdded; t.modifyingText = false;
                        if (node == null) return $"parsed '{seen}' -> nothing";
                        string item = node.buyItemIndex >= 0 && node.buyItemIndex < t.buyableItemsList.Length ? t.buyableItemsList[node.buyItemIndex].itemName : "-";
                        return $"parsed '{seen}' -> {node.name} item={item}";
                    }
                case "keys":
                    {
                        // keys <key[+key...]> <seconds> : real key presses (W, A, S, D, LeftShift, LeftCtrl, Space, E, Q...)
                        var di = DevInput.Instance ?? gameObject.AddComponent<DevInput>();
                        var keys = new List<UnityEngine.InputSystem.Key>();
                        foreach (var name in a[1].Split('+'))
                        {
                            if (!System.Enum.TryParse<UnityEngine.InputSystem.Key>(name, true, out var k)) return "unknown key " + name;
                            keys.Add(k);
                        }
                        di.Hold(keys, a.Length > 2 ? float.Parse(a[2]) : 0.1f);
                        return "holding " + di.Describe();
                    }
                case "mouse":
                    {
                        // mouse look <dx> <dy> [frames] | mouse left|right [seconds] | mouse release
                        var di = DevInput.Instance ?? gameObject.AddComponent<DevInput>();
                        switch (a[1])
                        {
                            case "look": di.Look(new Vector2(float.Parse(a[2]), float.Parse(a[3])), a.Length > 4 ? int.Parse(a[4]) : 10); return "ok";
                            case "left": di.Click(UnityEngine.InputSystem.LowLevel.MouseButton.Left, a.Length > 2 ? float.Parse(a[2]) : 0.08f); return "ok";
                            case "right": di.Click(UnityEngine.InputSystem.LowLevel.MouseButton.Right, a.Length > 2 ? float.Parse(a[2]) : 0.08f); return "ok";
                            case "release": di.ReleaseAll(); return "ok";
                            case "moveto": di.MoveTo(new Vector2(float.Parse(a[2]), float.Parse(a[3]))); return "ok";
                            case "screen": return $"{Screen.width}x{Screen.height}";
                        }
                        return "?";
                    }
                case "type":
                    {
                        // type <text> : keyboard text input ("_" = space); press Enter separately with: keys Enter 0.05
                        var di = DevInput.Instance ?? gameObject.AddComponent<DevInput>();
                        di.Type(string.Join(" ", a.Skip(1)).Replace("_", " "));
                        return "typing";
                    }
                case "cursortip":
                    {
                        var ray = new Ray(p.gameplayCamera.transform.position, p.gameplayCamera.transform.forward);
                        string hit = Physics.Raycast(ray, out var h, p.grabDistance, p.interactableObjectsMask) ? $"{h.collider.name} L{h.collider.gameObject.layer} d={h.distance:F2}" : "nothing";
                        return $"tip='{p.cursorTip.text}' grabDistance={p.grabDistance} ray: {hit}";
                    }
                case "nodepos":
                    {
                        // nodepos <i> [outside] : position of an AI node (for walking somewhere, not teleporting)
                        var nodes = a.Length > 2 && a[2] == "outside" ? RoundManager.Instance.outsideAINodes : RoundManager.Instance.insideAINodes;
                        if (nodes == null || nodes.Length == 0) return "no nodes";
                        var n = nodes[int.Parse(a[1]) % nodes.Length];
                        return V(n.transform.position);
                    }
                case "camera":
                    {
                        var c = p.gameplayCamera.transform;
                        return $"{V(c.position)} fwd={V(c.forward)}";
                    }
                case "hover":
                    return p.hoveringOverTrigger != null ? (p.hoveringOverTrigger.hoverTip ?? "") + " @" + p.hoveringOverTrigger.name : "-";
                case "termscreen":
                    {
                        var t = FindObjectOfType<Terminal>();
                        var txt = t.screenText.text;
                        return txt.Substring(Mathf.Max(0, txt.Length - 300)).Replace("\n", " | ");
                    }
                case "termtype":
                    {
                        // termtype <text> : type a line into the open terminal and press Enter (its input field reads legacy
                        // GUI events, which the input system can't fake): the text lands in the field, then the terminal's own submit
                        var t = FindObjectOfType<Terminal>();
                        if (t == null || !t.terminalInUse) return "terminal not open";
                        // one character at a time, like the input field does (each runs the terminal's TextChanged)
                        foreach (var ch in string.Join(" ", a.Skip(1))) t.screenText.text += ch;
                        t.OnSubmit();
                        return "submitted";
                    }
                case "inputstate":
                    return DevInput.Instance != null ? DevInput.Instance.Describe() : "idle";
                case "samples":
                    return Ground.SampleDebug(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                case "gapselftest":
                    {
                        // hide one natural block's face toward a dug cell: gapcheck must report it; then restore
                        foreach (var kv in BlockWorld.Molds.ToList())
                        {
                            if (!MoldData.HasExposure(kv.Value, Ground.MoldRes)) continue;
                            int bits = kv.Value[Ground.MoldRes * Ground.MoldRes];
                            if (bits == 0) continue;
                            var hidden = (byte[])kv.Value.Clone();
                            hidden[Ground.MoldRes * Ground.MoldRes] = 0;
                            BlockWorld.Molds[kv.Key] = hidden;
                            var g = Ground.Gaps();
                            BlockWorld.Molds[kv.Key] = kv.Value;
                            return $"hid faces of {kv.Key}: gapcheck saw {g.Count} gaps" + (g.Count > 0 ? " (" + g[0] + ")" : "");
                        }
                        return "no molded block next to a hole";
                    }
                case "gapcheck":
                    {
                        var g = Ground.Gaps();
                        return $"{g.Count} gaps" + (g.Count > 0 ? ": " + string.Join(" | ", g.Take(8)) : "");
                    }
                case "ghostcheck":
                    {
                        // ghostcheck : every dug cell must be free of visible level geometry (shrunk box: neighbours' faces don't count)
                        var bad = new List<string>();
                        int n = 0;
                        foreach (var c in Ground.DugCells.ToList())
                        {
                            n++;
                            var ctr = Ground.Center(c); var h = Vector3.one * (BlockWorld.S * 0.36f);
                            var g = TerrainCarver.GhostGeometry(ctr - h, ctr + h);
                            if (g.Count > 0) bad.Add($"{c}:{string.Join("/", g.Distinct().Take(3))}");
                        }
                        return $"{n} dug cells, {bad.Count} with uncut visible geometry" + (bad.Count > 0 ? ": " + string.Join(" ", bad.Take(10)) : "");
                    }
                case "ghostselftest":
                    {
                        // show the uncut originals of carved batched meshes instead of their cut proxies: ghostcheck must flag
                        // the dug cells they cover (the static-batch path of GhostGeometry works), then put everything back
                        var originals = FindObjectsOfType<MeshRenderer>().Where(r => !r.enabled && r.transform.Find("LMC_GroundProxy") != null).ToList();
                        var proxies = originals.Select(r => r.transform.Find("LMC_GroundProxy").GetComponent<MeshRenderer>()).ToList();
                        int flagged = 0, n = 0;
                        try
                        {
                            foreach (var r in originals) r.enabled = true;
                            foreach (var pr in proxies) pr.enabled = false;
                            foreach (var c in Ground.DugCells.ToList())
                            {
                                n++;
                                var ctr = Ground.Center(c); var h = Vector3.one * (BlockWorld.S * 0.36f);
                                if (TerrainCarver.GhostGeometry(ctr - h, ctr + h).Count > 0) flagged++;
                            }
                        }
                        finally
                        {
                            foreach (var r in originals) r.enabled = false;
                            foreach (var pr in proxies) pr.enabled = true;
                        }
                        if (a.Length > 1 && a[1] == "v")
                        {
                            // verbose: how each original sees each dug cell (shown again just for this)
                            var sbv = new System.Text.StringBuilder();
                            foreach (var r in originals) r.enabled = true;
                            foreach (var c in Ground.DugCells.ToList().Take(4))
                            {
                                var ctr = Ground.Center(c); var h = Vector3.one * (BlockWorld.S * 0.36f);
                                foreach (var r in originals) sbv.Append($"{c}: {TerrainCarver.GhostDebug(r, ctr - h, ctr + h)} ; ");
                            }
                            foreach (var r in originals) r.enabled = false;
                            return sbv.ToString();
                        }
                        return $"{originals.Count} batched originals shown uncut: {flagged}/{n} dug cells flagged";
                    }
                case "nodecheck":
                    {
                        // nodecheck [inside|outside] : AI nodes are where monsters walk, so the space at head height above
                        // each one is open air. Lists nodes whose cell there reads as solid ground (phantom ground).
                        bool inside = a.Length > 1 && a[1] == "inside";
                        var nodes = inside ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
                        if (nodes == null) return "no nodes";
                        var bad = new List<string>();
                        int n = 0;
                        foreach (var nd in nodes)
                        {
                            if (nd == null) continue;
                            // measure from the walkable surface under the node (some nodes sit a bit off it, even underground)
                            if (!UnityEngine.AI.NavMesh.SamplePosition(nd.transform.position, out var nh, 1.0f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                            n++;
                            var c = Ground.CellOf(nh.position + Vector3.up * 1.6f);
                            if (Ground.IsSolidCell(c)) bad.Add($"{V(nh.position)}{c}");
                        }
                        return $"{n} nodes, {bad.Count} in phantom ground" + (bad.Count > 0 ? ": " + string.Join(" ", bad.Take(12)) : "");
                    }
                case "pulses":
                    return Redstone.ObserverPulses.ToString();
                case "flags2":
                    return $"crouching={p.isCrouching} jumping={p.isJumping} grounded={p.thisController.isGrounded} craftOpen={CraftingUI.IsOpen}";
                case "flags":
                    return $"controlled={p.isPlayerControlled} dead={p.isPlayerDead} terminal={p.inTerminalMenu} chat={p.isTypingChat} specialAnim={p.inSpecialInteractAnimation} grabbingAnim={p.isGrabbingObjectAnimation} specialMenu={p.inSpecialMenu} holding={p.isHoldingObject} held={p.currentlyHeldObjectServer?.name} canAct={Builder.CanAct(p)} craftOpen={CraftingUI.IsOpen}";
                case "clearenemies":
                    {
                        // clearenemies [radius] : despawn enemies near the player (tests teleport a god-mode player around:
                        // an enemy latched onto them gets dragged to places it can't path from)
                        float r = a.Length > 1 ? float.Parse(a[1]) : 60f;
                        int n = 0;
                        foreach (var e in RoundManager.Instance.SpawnedEnemies.Where(e => e != null && !e.isEnemyDead).ToList())
                        {
                            if (Vector3.Distance(e.transform.position, p.transform.position) > r) continue;
                            var no = e.GetComponent<Unity.Netcode.NetworkObject>();
                            if (no != null && no.IsSpawned) { no.Despawn(true); n++; }
                        }
                        return "despawned " + n;
                    }
                case "enemies":
                    {
                        var list = RoundManager.Instance.SpawnedEnemies.Where(e => e != null).Select(e =>
                            $"{e.enemyType?.enemyName}@{V(e.transform.position)} d={Vector3.Distance(e.transform.position, p.transform.position):F1} v={(e.agent != null ? e.agent.velocity.magnitude : -1):F1} target={(e.targetPlayer != null ? e.targetPlayer.playerUsername : "-")} chase={e.movingTowardsTargetPlayer} state={e.currentBehaviourStateIndex} path={(e.agent != null ? e.agent.pathStatus.ToString() : "-")} onNav={(e.agent != null && e.agent.enabled ? e.agent.isOnNavMesh.ToString() : "-")} dead={e.isEnemyDead} hp={e.enemyHP}");
                        return string.Join(" ; ", list);
                    }
                case "time":
                    Time.timeScale = float.Parse(a[1]);
                    return "ok";
                case "log":
                    Plugin.Log.LogInfo("[dev] " + string.Join(" ", a.Skip(1)));
                    return "ok";
                default:
                    return "unknown command";
            }
        }
    }
}

namespace LethalMinecraft
{
    /// <summary>Dev only: log what kills/hurts the local player, and optional god mode for long QA runs.</summary>
    [HarmonyLib.HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB))]
    static class DevDeathLog
    {
        public static bool God;

        [HarmonyLib.HarmonyPatch("KillPlayer"), HarmonyLib.HarmonyPrefix]
        static bool Kill(GameNetcodeStuff.PlayerControllerB __instance, CauseOfDeath causeOfDeath)
        {
            if (!Plugin.DevMode.Value || !__instance.IsOwner) return true;
            Plugin.Log.LogWarning($"[dev] KillPlayer cause={causeOfDeath} pos={__instance.transform.position} god={God}\n{System.Environment.StackTrace}");
            return !God;
        }

        [HarmonyLib.HarmonyPatch("DamagePlayer"), HarmonyLib.HarmonyPrefix]
        static bool Damage(GameNetcodeStuff.PlayerControllerB __instance, int damageNumber, CauseOfDeath causeOfDeath, bool fallDamage)
        {
            if (!Plugin.DevMode.Value || !__instance.IsOwner) return true;
            Plugin.Log.LogInfo($"[dev] DamagePlayer {damageNumber} cause={causeOfDeath} fall={fallDamage} pos={__instance.transform.position}");
            return !God;
        }
    }
}
