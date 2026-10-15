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
        public static bool MmbClick; // (dev "mmb": a middle click)
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
                StartCoroutine(EndOfFrames());
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
                                if (line.Trim() == "gtime")
                                {
                                    // (as of the end of the last update: the clock only moves between frames anyway)
                                    w.WriteLine(System.FormattableString.Invariant($"{System.Threading.Volatile.Read(ref pubTime):F4} {System.Threading.Volatile.Read(ref pubReal):F4} {System.Threading.Volatile.Read(ref pubScale)} {pubFrameMs:F1}ms"));
                                    continue;
                                }
                                var t = new TaskCompletionSourceLite();
                                queue.Enqueue((line, t));
                                t.Done.Wait(line.StartsWith("wait ") ? 120000 : 15000);
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
            Drain(all: false);
            Publish();
            var p = P;
            if (p == null || Time.time > walkUntil) return;
            var fwd = p.transform.forward; fwd.y = 0; fwd.Normalize();
            var right = p.transform.right; right.y = 0; right.Normalize();
            var dir = (fwd * walkDir.y + right * walkDir.x).normalized;
            p.thisController.Move(dir * walkSpeed * Time.deltaTime);
        }

        static string lastCmd = "";
        static int savedFrameCap = int.MinValue, savedVSync; // (the player's frame cap, while "speed" has it uncapped)

        void Update()
        {
            frameTimes.Enqueue(Time.unscaledDeltaTime);
            while (frameTimes.Count > 120) frameTimes.Dequeue();
            if (Time.unscaledDeltaTime > 0.4f) Plugin.Log.LogWarning($"[dev] hitch {Time.unscaledDeltaTime * 1000f:F0} ms (last command: {lastCmd})");
            Drain();
            Publish();
            if (LmbUntil > 0 && Time.time > LmbUntil) { LmbHeld = false; LmbUntil = 0; }
            if (RmbUntil > 0 && Time.time > RmbUntil) { RmbHeld = false; RmbUntil = 0; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) Exec("giveall");
            WatchNavmesh();
        }

        // commands are run on the main thread: at the start of a frame (Update), after the game's own updates
        // (LateUpdate) and once it's drawn (end of frame), so one doesn't wait a whole frame for its turn
        readonly List<(float until, int frame, TaskCompletionSourceLite reply)> waits = new List<(float, int, TaskCompletionSourceLite)>();

        // queries that only read: these may also run late in a frame (LateUpdate, end of frame). Anything that acts (input,
        // teleports, placing...) runs at the start of the next frame, in order, the way it always did: a look and a
        // right-click must not land in the same frame
        static readonly HashSet<string> ReadOnly = new HashSet<string> { "state", "flags", "flags2", "cellabs", "cellinfo",
            "obstructed", "near", "find", "objs", "fpsinfo", "enemies", "place?", "mine?", "fire?", "leave_check", "levels",
            "groundstats", "strays", "slotsall", "props", "cursortip", "gamemodes", "grabq", "storeprices", "itemkeys", "lcitems" };
        static bool IsReadOnly(string c)
        {
            var w = c.Split(' ');
            if (w[0] == "batch") return c.Substring(6).Split(new[] { " ;; " }, StringSplitOptions.None).All(x => IsReadOnly(x.Trim()));
            if (w[0] == "chattext") return w.Length == 1;
            if (w[0] == "creativeui" || w[0] == "craftui" || w[0] == "chestui") return w.Length > 1 && (w[1] == "state" || w[1] == "pos");
            return ReadOnly.Contains(w[0]);
        }

        void Drain(bool all = true)
        {
            // "wait <seconds>" answers once the game's clock has moved that far (the tests' sleeps, in game time) and at
            // least 3 frames have gone by (sped up with slow frames, a short sleep could otherwise be a single frame: not
            // enough for a request to reach the server and its answer to come back)
            for (int i = waits.Count - 1; i >= 0; i--)
                if (Time.time >= waits[i].until && Time.frameCount >= waits[i].frame) { waits[i].reply.Result = $"{Time.time:F4}"; waits[i].reply.Done.Set(); waits.RemoveAt(i); }
            while (queue.TryPeek(out var next))
            {
                if (!all && !next.cmd.TrimStart().StartsWith("wait ") && !IsReadOnly(next.cmd.Trim())) break;
                if (!queue.TryDequeue(out var item)) break;
                string c = item.cmd.Trim();
                if (c.StartsWith("wait ") && float.TryParse(c.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float secs))
                {
                    waits.Add((Time.time + secs, Time.frameCount + 3, item.reply));
                    continue;
                }
                string res;
                lastCmd = c;
                var cmdSw = System.Diagnostics.Stopwatch.StartNew();
                try { res = Exec(c); }
                catch (Exception e) { res = "ERR " + e.Message; Plugin.Log.LogWarning($"[dev] command '{c}' failed: {e}"); }
                if (cmdSw.ElapsedMilliseconds > 300) Plugin.Log.LogWarning($"[dev] command '{lastCmd}' took {cmdSw.ElapsedMilliseconds} ms");
                item.reply.Result = res;
                item.reply.Done.Set();
            }
        }


        System.Collections.IEnumerator EndOfFrames()
        {
            var eof = new WaitForEndOfFrame();
            while (true) { yield return eof; Drain(all: false); Publish(); }
        }

        // the game's clock for "gtime", answered straight from the network thread (no wait for the main thread's turn)
        static double pubTime, pubReal, pubScale = 1, pubFrameMs;
        void Publish()
        {
            System.Threading.Volatile.Write(ref pubTime, Time.timeAsDouble);
            System.Threading.Volatile.Write(ref pubReal, Time.realtimeSinceStartupAsDouble);
            System.Threading.Volatile.Write(ref pubScale, Time.timeScale);
            pubFrameMs = frameTimes.Count > 0 ? frameTimes.Average() * 1000f : 0f;
        }

        // #25: a monster whose agent is off the navmesh (the game then logs "Agent not on nav mesh" every time it paths):
        // where it is, and whether dug ground is under it, once per monster
        static float navCheckAt;
        static readonly HashSet<int> offNavReported = new HashSet<int>();
        static void WatchNavmesh()
        {
            if (Time.time < navCheckAt || !BlockNet.IsServer || RoundManager.Instance == null) return;
            navCheckAt = Time.time + 2f;
            foreach (var e in RoundManager.Instance.SpawnedEnemies)
            {
                if (e == null || e.isEnemyDead || !e.enabled || e.agent == null || !e.agent.enabled || e.agent.isOnNavMesh) continue;
                if (!offNavReported.Add(e.GetInstanceID())) continue;
                var c = Ground.CellOf(e.transform.position);
                var dugNear = new List<string>();
                for (int dx = -1; dx <= 1; dx++) for (int dy = -2; dy <= 0; dy++) for (int dz = -1; dz <= 1; dz++)
                    { var n = c + new Vector3Int(dx, dy, dz); if (Ground.IsDug(n)) dugNear.Add(n.ToString()); }
                string nearest = UnityEngine.AI.NavMesh.SamplePosition(e.transform.position, out var nh, 10f, UnityEngine.AI.NavMesh.AllAreas)
                    ? $"{Vector3.Distance(nh.position, e.transform.position):F1} m away" : "none within 10 m";
                Plugin.Log.LogWarning($"[dev] off the navmesh: {e.enemyType?.enemyName} #{e.thisEnemyIndex} at {e.transform.position} cell {c} " +
                    $"inside={e.isOutside == false} state={e.currentBehaviourStateIndex} nearest navmesh {nearest}; dug cells around/under it: {(dugNear.Count > 0 ? string.Join(" ", dugNear) : "none")}");
            }
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

        System.Collections.IEnumerator ShipRec(GameNetcodeStuff.PlayerControllerB p, float secs)
        {
            var sor = StartOfRound.Instance;
            float end = Time.time + secs, t0 = Time.time;
            while (Time.time < end)
            {
                Plugin.Log.LogInfo($"[shiprec] {Time.time - t0:0.000} ship={sor.elevatorTransform.position.y:0.000} player={p.transform.position.y:0.000} rel={p.transform.position.y - sor.elevatorTransform.position.y:0.000} grounded={p.thisController.isGrounded} jumping={p.isJumping} fall={p.fallValue:0.0} inEl={p.isInElevator} parent={(p.transform.parent != null ? p.transform.parent.name : "-")} onBlock={ShipCarry.StandsOnShipBlock(p.transform.position)} lastContains={ShipCarry.DevLast} falses={ShipCarry.DevFalseCount} zoneSaves={ShipCarry.DevZoneSaves}");
                yield return null;
            }
        }

        static string V(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";

        System.Collections.IEnumerator GrabRace(PlayerControllerB p, StackItem item)
        {
            yield return null;
            Inventory.GrabDirect(p, item);
            yield return null;
            if (item != null && item.IsSpawned) item.NetworkObject.Despawn(true);
        }

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
                        sb.Append($"wt={(p.carryWeight - 1f) * 105f:F0}lb ");
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
                case "lcspawn":
                    {
                        // lcspawn <item name> : one of the game's own items (by its name, e.g. Large axle) a metre in front of you
                        if (!BlockNet.IsServer) return "server only";
                        string want = string.Join(" ", a.Skip(1)).ToLower();
                        var it = StartOfRound.Instance.allItemsList.itemsList.FirstOrDefault(x => x != null && x.itemName != null && x.itemName.ToLower() == want);
                        if (it == null || it.spawnPrefab == null) return "no such item";
                        var f = p.transform.forward; f.y = 0f; f.Normalize();
                        var go = Instantiate(it.spawnPrefab, p.transform.position + f * 1.2f + Vector3.up * 0.5f, Quaternion.identity, StartOfRound.Instance.propsContainer);
                        go.GetComponent<Unity.Netcode.NetworkObject>().Spawn();
                        return $"spawned {it.itemName} twoHanded={it.twoHanded}";
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
                case "loot":
                    {
                        // loot <game item name ('_' = space)> [n] : (server) the game's own items on the ship's floor, spread out (#69)
                        if (!BlockNet.IsServer) return "server only";
                        var item = CreativeUI.VanillaItem(a[1].Replace("_", " "));
                        if (item == null) return "no item " + a[1];
                        int n = a.Length > 2 ? int.Parse(a[2]) : 1;
                        var sor = StartOfRound.Instance;
                        for (int i = 0; i < n; i++)
                        {
                            var at = sor.elevatorTransform.position + new Vector3(-3f + (i % 6) * 1.1f, 0.6f, -1.5f - (i / 6) * 0.8f);
                            var g = ModItems.ServerSpawnPlain(item, at);
                            if (g != null && g.itemProperties.isScrap) g.SetScrapValue(20 + i);
                        }
                        return $"spawned {n} {item.itemName}";
                    }
                case "despawn":
                    {
                        // despawn <name part ('_' = space)> | despawn all : (server) remove loose items (not held) matching the name (cleaning a test save)
                        if (!BlockNet.IsServer) return "server only";
                        string q = a[1] == "all" ? "" : a[1].Replace("_", " ").ToLowerInvariant();
                        int n = 0;
                        foreach (var o in FindObjectsOfType<GrabbableObject>().Where(o => !o.isHeld && !o.isPocketed && o.itemProperties != null && o.itemProperties.itemName.ToLowerInvariant().Contains(q)).ToList())
                        {
                            if (o.itemProperties.itemName == "clipboard" || o.itemProperties.itemName == "Sticky note") continue;
                            var no = o.GetComponent<Unity.Netcode.NetworkObject>(); if (no != null && no.IsSpawned) { no.Despawn(); n++; }
                        }
                        return "despawned " + n;
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
                case "mobspawns":
                    // mobspawns 0|1 : (tests) natural spawns of the Minecraft mobs off/on (the dev 'enemy' command still spawns them)
                    if (a.Length > 1) MobSpawns.DevNoNatural = a[1] == "0";
                    return "natural mob spawns " + (MobSpawns.DevNoNatural ? "off" : "on");
                case "facilitytiles":
                    {
                        // facilitytiles : (dev) how many rooms the facility has (tiles of the game's own generator) and its size
                        var gen = RoundManager.Instance?.dungeonGenerator?.Generator;
                        var d = gen?.CurrentDungeon;
                        if (d == null) return "no facility";
                        var b = new Bounds(d.AllTiles[0].transform.position, Vector3.zero);
                        foreach (var t in d.AllTiles) b.Encapsulate(t.transform.position);
                        return $"tiles={d.AllTiles.Count} mainPath={d.MainPathTiles.Count} flow={gen.DungeonFlow?.name} span={b.size.x:F0}x{b.size.z:F0} m";
                    }
                case "interior":
                    {
                        // interior [id|-1] : (dev, host) the interior the next landings make (-1: the moon's own pick); lists them (#64)
                        if (a.Length > 1) DevInterior.Force = int.Parse(a[1]);
                        var rm = RoundManager.Instance;
                        return $"force={DevInterior.Force} current={rm.currentDungeonType} : " + string.Join(", ", rm.dungeonFlowTypes.Select((t, i) => $"{i}={(t.dungeonFlow != null ? t.dungeonFlow.name : "-")}"));
                    }
                case "geom":
                    {
                        // geom <name part> [r] [n] : level colliders whose name (or parent's) contains the text, nearest first (#64: pipes)
                        float r = a.Length > 2 ? float.Parse(a[2]) : 60f; int nmax = a.Length > 3 ? int.Parse(a[3]) : 15;
                        string want = a[1].ToLowerInvariant();
                        var list = Physics.OverlapSphere(p.transform.position, r, ~0, QueryTriggerInteraction.Ignore)
                            .Where(c => (c.name + "|" + (c.transform.parent != null ? c.transform.parent.name : "")).ToLowerInvariant().Contains(want))
                            .OrderBy(c => Vector3.Distance(c.ClosestPoint(p.transform.position), p.transform.position)).Take(nmax)
                            .Select(c => $"{c.name}<{(c.transform.parent != null ? c.transform.parent.name : "-")}> L{c.gameObject.layer} {c.GetType().Name} c={V(c.bounds.center)} s={V(c.bounds.size)} d={Vector3.Distance(c.ClosestPoint(p.transform.position), p.transform.position):F1}");
                        return string.Join(" ; ", list);
                    }
                case "terrcheck":
                    {
                        // terrcheck : each active Unity terrain and why it is or isn't dug (layer, collider, scene vs the level's scene)
                        var lvl = StartOfRound.Instance.currentLevel;
                        return $"level scene='{lvl?.sceneName}' | " + string.Join(" ; ", Terrain.activeTerrains.Where(t => t != null).Select(t =>
                            $"{t.name} layer={t.gameObject.layer}({LayerMask.LayerToName(t.gameObject.layer)}) inMask={((1 << t.gameObject.layer) & TerrainCarver.LevelMask) != 0} col={(t.GetComponent<TerrainCollider>() != null ? t.GetComponent<TerrainCollider>().enabled.ToString() : "none")} scene='{t.gameObject.scene.name}' active={t.gameObject.activeInHierarchy}"));
                    }
                case "objs":
                    {
                        var list = FindObjectsOfType<GrabbableObject>().OrderBy(o => Vector3.Distance(o.transform.position, p.transform.position)).Take(12)
                            .Select(o => $"{o.itemProperties?.itemName}@{V(o.transform.position)} d={Vector3.Distance(o.transform.position, p.transform.position):F1} held={o.isHeld} grab={o.grabbable} spawned={o.IsSpawned} active={o.gameObject.activeInHierarchy} id={o.NetworkObjectId}");
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
                            .Select(b => $"{b.Data.Def.Key}{b.Key.Pos}y{b.Key.YOff} f{b.Data.Facing} s{b.Data.State} v{b.Variant}" +
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
                    // hurt n [cause] [fall] : damage the local player (cause: Mauling, Gravity, Burning...; fall: as fall damage)
                    p.DamagePlayer(int.Parse(a[1]), true, true, a.Length > 2 ? (CauseOfDeath)System.Enum.Parse(typeof(CauseOfDeath), a[2], true) : CauseOfDeath.Unknown, 0, a.Contains("fall"));
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
                case "armormodels":
                    // armormodels [0|1] : the armor drawn on players' models (#20)
                    if (a.Length > 1 && a[1] == "bones") return ArmorModels.DevBones();
                    if (a.Length > 1 && a[1] == "layers") return ArmorModels.DevLayers();
                    if (a.Length > 1 && a[1] == "axes") return string.Join(" | ", ArmorModels.DevAxes);
                    if (a.Length > 1 && a[1] == "show") ArmorModels.DevShow(a.Length < 3 || a[2] == "1");
                    else if (a.Length > 1) ArmorModels.Enabled = a[1] == "1";
                    return "enabled=" + ArmorModels.Enabled + " " + ArmorModels.Describe();
                case "cfg":
                    {
                        // cfg <section> <key> [value] : (dev) a config setting, changed in memory only (the file isn't saved)
                        var file = Plugin.Instance.Config;
                        var def = new BepInEx.Configuration.ConfigDefinition(a[1], a[2]);
                        if (!file.ContainsKey(def)) return "no setting " + a[1] + "." + a[2];
                        var e = file[def];
                        if (a.Length > 3)
                        {
                            bool save = file.SaveOnConfigSet; file.SaveOnConfigSet = false;
                            try { e.SetSerializedValue(a[3]); } finally { file.SaveOnConfigSet = save; }
                        }
                        return $"{a[1]}.{a[2]} = {e.GetSerializedValue()}";
                    }
                case "armorall":
                    // armorall : what everyone wears, as this game instance knows it
                    return string.Join(" ; ", Armor.All.Select(kv => kv.Key + "=" + string.Join(",", kv.Value.Select(k => k ?? "-"))));
                case "grabrace":
                    {
                        // grabrace : start picking up a fresh stack, then despawn it before the server answers (Ace's frozen hotbar, #2)
                        if (!BlockNet.IsServer) return "server only";
                        var item = ModItems.ServerSpawnStack(ModItems.ByKey["cobblestone"], 1, p.transform.position + p.transform.forward * 0.8f + Vector3.up * 0.5f);
                        StartCoroutine(GrabRace(p, item));
                        return "racing " + item.NetworkObjectId;
                    }
                case "grabstall":
                    {
                        // grabstall : two items queued for pickup, the first gone before it's picked up (does the second wait?)
                        if (!BlockNet.IsServer) return "server only";
                        var at = p.transform.position + Vector3.up * 0.3f;
                        var first = ModItems.ServerSpawnStack(ModItems.ByKey["dirt"], 1, at);
                        var second = ModItems.ServerSpawnStack(ModItems.ByKey["sand"], 1, at);
                        BlockNet.ServerAutoGrab(Unity.Netcode.NetworkManager.Singleton.LocalClientId, first.NetworkObjectId);
                        BlockNet.ServerAutoGrab(Unity.Netcode.NetworkManager.Singleton.LocalClientId, second.NetworkObjectId);
                        first.NetworkObject.Despawn(true);
                        return $"queued {first.NetworkObjectId} (gone) then {second.NetworkObjectId}";
                    }
                case "digcompany":
                    // digcompany 0|1 : the AllowDiggingAtCompany setting (saved in the config file: set it back after)
                    if (a.Length > 1) Plugin.DigAtCompany.Value = a[1] == "1";
                    return "AllowDiggingAtCompany=" + Plugin.DigAtCompany.Value;
                case "shiproof":
                    // shiproof 0|1|reset : the AllowDiggingShipRoof setting (#124; saved in the config file: set it back after);
                    // reset: the dug roof whole again (it stays dug through saves, #179)
                    if (a.Length > 1 && a[1] == "reset") { ShipRoof.Clear(); BlockNet.ServerRules(); }
                    else if (a.Length > 1) { Plugin.DigShipRoof.Value = a[1] == "1"; BlockNet.ServerRules(); }
                    return "AllowDiggingShipRoof=" + Plugin.DigShipRoof.Value + " on=" + ShipRoof.On + " parts=" + TerrainCarver.DescribeShip();
                case "boom":
                    {
                        // boom x y z [carve 0|1] : a game explosion (what landmines and Old Bird missiles do) at a world point
                        var pos = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        ServerLogic.DevCarveGameExplosions = a.Length > 4 && a[4] == "1";
                        try { Landmine.SpawnExplosion(pos, true, 5.7f, 6.4f, 50, 30f); }
                        finally { ServerLogic.DevCarveGameExplosions = false; }
                        return "boom at " + V(pos);
                    }
                case "blast":
                    return $"last blast: {BlastDamage.LastDistance:F2} blocks, {BlastDamage.LastDamage} damage";
                case "creeperboom":
                    {
                        // creeperboom [x y z] : a creeper's blast (its own code path) at a world point, or 1 m in front of you
                        var pos = a.Length > 3 ? new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3])) : p.transform.position + p.transform.forward + Vector3.up * 0.9f;
                        if (a.Length > 1 && a[1] == "chest")
                        {
                            // (beside the nearest chest, a block away)
                            var ch = BlockWorld.Instance.Blocks.Values.Where(b => b.Data.Def == Blocks.Chest && b.Go != null).OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                            if (ch == null) return "no chest";
                            pos = BlockWorld.Instance.WorldCenter(ch.Key) + Vector3.right * BlockWorld.S;
                        }
                        float r = 3f * BlockWorld.S;
                        ServerLogic.ExplodeBlocks(pos, r, -1, carveGround: Plugin.CreeperCraters.Value);
                        BlockNet.ServerExplosion(pos, r);
                        return "creeper blast at " + V(pos);
                    }
                case "igniteabs":
                    // igniteabs x y z : light the TNT in that natural-grid cell
                    ServerLogic.Ignite(Ground.KeyOf(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]))), 20);
                    return "ok";
                case "prof":
                    // prof on | off | dump : time the mod's work (explosions...) and every frame
                    if (a.Length > 1 && a[1] == "on") { Prof.Start(); return "profiling"; }
                    if (a.Length > 1 && a[1] == "off") { Prof.On = false; return Prof.Dump(); }
                    return Prof.Dump();
                case "dropall":
                    // dropall : drop everything the way the game does when a Kidnapper Fox's tongue catches you
                    p.DropAllHeldItemsAndSync(p.transform.position, p.localItemHolder.position, p.localItemHolder.eulerAngles, p.playerEye.transform.position, p.playerEye.transform.eulerAngles);
                    return "dropped";
                case "slotsall":
                    // slotsall : every player object's slots (unused lobby seats too)
                    return string.Join(" ; ", StartOfRound.Instance.allPlayerScripts.Where(x => x != null).Select((x, i) =>
                        $"#{i} {x.playerUsername} controlled={x.isPlayerControlled} client={x.actualClientId} held={(x.currentlyHeldObjectServer != null ? x.currentlyHeldObjectServer.itemProperties.itemName : "-")} grabbing={(x.currentlyGrabbingObject != null ? x.currentlyGrabbingObject.itemProperties.itemName : "-")} " +
                        $"wt={x.carryWeight:F2} slots=[{string.Join(",", x.ItemSlots.Select(g => g != null ? g.itemProperties.itemName + "#" + g.NetworkObjectId : "-"))}]"));
                case "strays":
                    {
                        // strays : items marked held that aren't in anybody's slots
                        var everyone = StartOfRound.Instance.allPlayerScripts.Where(x => x != null).ToList();
                        var list = FindObjectsOfType<GrabbableObject>().Where(g => g.isHeld && !everyone.Any(x => x.ItemSlots.Contains(g) || x.ItemOnlySlot == g))
                            .Select(g => $"{g.itemProperties.itemName}#{g.NetworkObjectId} by={(g.playerHeldBy != null ? g.playerHeldBy.playerUsername : "-")} onServer={g.heldByPlayerOnServer} pocketed={g.isPocketed} " +
                                $"parent={(g.parentObject != null ? g.parentObject.name : "-")} tparent={(g.transform.parent != null ? g.transform.parent.name : "-")} owner={g.OwnerClientId} grabbable={g.grabbable} " +
                                $"grabbing={(p.currentlyGrabbingObject == g)} held={(p.currentlyHeldObjectServer == g)} started={g.radarIcon != null || g.propColliders != null}");
                        return $"wt={p.carryWeight:F2} belt={(p.ItemOnlySlot != null ? p.ItemOnlySlot.itemProperties.itemName.Replace(" ", "_") : "-")} grabbingAnim={p.isGrabbingObjectAnimation} grabbingObj={(p.currentlyGrabbingObject != null ? p.currentlyGrabbingObject.itemProperties.itemName : "-")} | " + string.Join(" ; ", list);
                    }
                case "lcitems":
                    {
                        // lcitems : the game's own items (index, name, scrap?, in the store?, icon?), ours left out
                        var store = FindObjectOfType<Terminal>()?.buyableItemsList ?? new Item[0];
                        var all = StartOfRound.Instance.allItemsList.itemsList;
                        return string.Join(" ; ", all.Select((it, i) => (it, i)).Where(x => x.it != null && !ModItems.ByKey.Values.Contains(x.it))
                            .Select(x => $"{x.i}:{x.it.itemName}{(x.it.isScrap ? " scrap" : "")}{(store.Contains(x.it) ? " store" : "")}{(x.it.itemIcon != null ? " icon=" + x.it.itemIcon.name : " noicon")}{(x.it.spawnPrefab != null ? "" : " noprefab")}"));
                    }
                case "itemkeys":
                    return string.Join(",", ModItems.ByKey.Keys.OrderBy(k => k));
                case "glint":
                    // glint ev <n> : (dev) the enchanted model's purple glow strength
                    Glint.DevSetEV(float.Parse(a[2]));
                    return "glint ev " + Glint.DevEV;
                case "jukebox":
                    // jukebox : (dev) what's playing in jukeboxes (this client)
                    return Jukebox.Describe();
                case "moonwater":
                    {
                        // moonwater : (dev) the game's own water volumes (rivers, lakes) on this moon, nearest first
                        var ws = FindObjectsOfType<QuicksandTrigger>().Where(q => q.isWater && q.GetComponent<Collider>() != null)
                            .Select(q => q.GetComponent<Collider>().bounds).OrderBy(b => Vector3.Distance(b.center, p.transform.position)).Take(4);
                        return string.Join(" ; ", ws.Select(b => $"{V(b.center)} size {V(b.size)} top={b.max.y:F1}"));
                    }
                case "bucketaim":
                    return Builder.DevBucketAim();
                case "swimup":
                    // swimup 0|1 : (dev) hold "jump" in water
                    WaterSwim.DevSwimUp = a[1] == "1";
                    return "swimup " + WaterSwim.DevSwimUp + " body=" + WaterSwim.BodyIn + " head=" + WaterSwim.HeadIn;
                case "water":
                    // water : (dev) the host's flowing water
                    return WaterFlow.Describe() + " blocks=" + BlockWorld.Instance.Blocks.Values.Count(b => b.Data.Def == Blocks.Water)
                        + " lava=" + BlockWorld.Instance.Blocks.Values.Count(b => b.Data.Def == Blocks.Lava);
                case "fluidmap":
                    // fluidmap [clear <frame> [x y z r]] : (dev) water and lava by grid (frame/yoff): count, sources, cell bounds; or
                    // remove a grid's (all of it, or within r cells of a cell)
                    if (a.Length > 2 && a[1] == "clear")
                    {
                        byte fr = byte.Parse(a[2]);
                        bool near = a.Length > 6;
                        var at = near ? new Vector3Int(int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5])) : Vector3Int.zero;
                        int rr = near ? int.Parse(a[6]) : 0;
                        var gone = BlockWorld.Instance.Blocks.Values.Where(b => (b.Data.Def == Blocks.Water || b.Data.Def == Blocks.Lava) && b.Key.Frame == fr
                            && (!near || (Mathf.Abs(b.Key.Pos.x - at.x) <= rr && Mathf.Abs(b.Key.Pos.y - at.y) <= rr && Mathf.Abs(b.Key.Pos.z - at.z) <= rr)))
                            .Select(b => Op.Remove(b.Key, false)).ToList();
                        BlockNet.ServerBroadcastOps(gone);
                        return "removed " + gone.Count;
                    }
                    return string.Join(" ; ", BlockWorld.Instance.Blocks.Values.Where(b => b.Data.Def == Blocks.Water || b.Data.Def == Blocks.Lava)
                        .GroupBy(b => (b.Data.Def.Key, b.Key.Frame, b.Key.YOff)).Select(g =>
                            $"{g.Key.Item1} f{g.Key.Item2}/{g.Key.Item3}: n={g.Count()} src={g.Count(b => b.Data.State == 0)} x={g.Min(b => b.Key.Pos.x)}..{g.Max(b => b.Key.Pos.x)} y={g.Min(b => b.Key.Pos.y)}..{g.Max(b => b.Key.Pos.y)} z={g.Min(b => b.Key.Pos.z)}..{g.Max(b => b.Key.Pos.z)}"));
                case "hardcore":
                    // hardcore : (dev) hardcore mode: on, who's out, whether the crew is about to be fired
                    return Hardcore.Describe();
                case "raydown":
                    {
                        // raydown x y z [dist] : (dev) every collider straight down from a point (any layer, triggers too): name, layer, distance
                        var o = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                        float dist = a.Length > 4 ? float.Parse(a[4]) : 12f;
                        return string.Join(" ; ", Physics.RaycastAll(o, Vector3.down, dist, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance)
                            .Select(h => $"{h.collider.name}<{(h.collider.transform.parent != null ? h.collider.transform.parent.name : "-")}> L{h.collider.gameObject.layer}{(h.collider.isTrigger ? " trig" : "")} d={h.distance:F2} y={h.point.y:F2}"));
                    }
                case "slotsrender":
                    {
                        // slotsrender : (dev) every hotbar item: its renderers on or off, held/pocketed, parent (#70: all items drawn in hand)
                        return string.Join(" ; ", p.ItemSlots.Select((g, i) => g == null ? $"{i}:-" :
                            $"{i}:{g.itemProperties?.itemName} held={g.isHeld} pocketed={g.isPocketed} cur={(p.currentItemSlot == i)} on={g.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled)}/{g.GetComponentsInChildren<Renderer>(true).Length} parent={(g.transform.parent != null ? g.transform.parent.name : "-")} heldBy={(g.playerHeldBy != null ? g.playerHeldBy.playerUsername : "-")}"));
                    }
                case "heldrender":
                    {
                        // heldrender : (dev) the held item's renderers, on or off (#81: a held item not drawn after a ladder)
                        var h = p.currentlyHeldObjectServer;
                        if (h == null) return "nothing held";
                        return $"{h.itemProperties?.itemName} main={(h.mainObjectRenderer != null ? h.mainObjectRenderer.enabled.ToString() : "-")} climbing={p.isClimbingLadder} anim={p.inSpecialInteractAnimation} | " +
                            string.Join(", ", h.GetComponentsInChildren<Renderer>(true).Select(r => $"{r.name}:{(r.enabled ? "on" : "off")}{(r.gameObject.activeInHierarchy ? "" : "(inactive)")}"));
                    }
                case "bee":
                    // bee : (dev) the big Minecraft bees (#84)
                    return BigBee.Describe();
                case "bossbar":
                    // bossbar [sold] : (dev) the Company's boss bar (#63); sold: set how much of the quota is met
                    if (a.Length > 1 && TimeOfDay.Instance != null) TimeOfDay.Instance.quotaFulfilled = int.Parse(a[1]);
                    return CompanyBossBar.Describe();
                case "elytra":
                    // elytra [open|close|boost|wear n] : (dev) the glide (#61)
                    if (a.Length > 1 && a[1] == "open") Elytra.Open(p);
                    else if (a.Length > 1 && a[1] == "close") Elytra.Close(true);
                    else if (a.Length > 1 && a[1] == "boost") Elytra.BoostTicks = 25;
                    else if (a.Length > 1 && a[1] == "clearhit") Elytra.LastHit = 0;
                    else if (a.Length > 2 && a[1] == "spread") ElytraWings.DevSpread = a[2] == "1";
                    else if (a.Length > 1 && a[1] == "wings") return ElytraWings.DevDescribe();
                    else if (a.Length > 1 && a[1] == "cast") return Elytra.DevCast(a.Length > 2 ? float.Parse(a[2]) : 5f);
                    else if (a.Length > 2 && a[1] == "wear" && Elytra.WornKey != null)
                    {
                        int d = ItemData.Of(Elytra.WornKey);
                        Armor.SetLocal(1, ItemData.With(Elytra.Key, Enchants.Data(int.Parse(a[2]), Enchants.EnchOf(d))));
                    }
                    return Elytra.Describe();
                case "nether":
                    {
                        // nether [gen [seed] | tp | clear] : (dev) the Nether fortress (#66): generate it here, go there
                        if (a.Length > 1 && a[1] == "rooms")
                        {
                            // nether rooms <n|0> : (dev) the main path's length, overriding the config and the facility
                            if (a.Length > 2) NetherFortress.DevRooms = int.Parse(a[2]);
                            return "rooms=" + NetherFortress.DevRooms;
                        }
                        if (a.Length > 1 && a[1] == "kind")
                        {
                            // nether kind [seed|fortress|bastion] : (dev) what the next one is (#68)
                            if (a.Length > 2) NetherFortress.DevForceKind = a[2] == "bastion" ? 1 : a[2] == "valley" ? 2 : a[2] == "crimson" ? 3 : a[2] == "warped" ? 4 : a[2] == "delta" ? 5 : a[2] == "fortress" ? 0 : -1;
                            return "force=" + NetherFortress.DevForceKind;
                        }
                        if (a.Length > 1 && a[1] == "gen")
                        {
                            int seed = a.Length > 2 ? int.Parse(a[2]) : StartOfRound.Instance.randomMapSeed + 66;
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            bool ok = NetherFortress.Generate(seed);
                            return (ok ? "ok " : "failed ") + sw.ElapsedMilliseconds + " ms: " + NetherFortress.Describe();
                        }
                        if (a.Length > 1 && a[1] == "tp")
                        {
                            if (NetherFortress.Root == null) return "no fortress";
                            p.TeleportPlayer(NetherFortress.StartPoint);
                            p.isInsideFactory = true;
                            return "ok " + V(NetherFortress.StartPoint);
                        }
                        if (a.Length > 1 && a[1] == "clear") { NetherFortress.Clear(); return "cleared"; }
                        if (a.Length > 1 && a[1] == "tiles") return NetherFortress.DevTiles();
                        if (a.Length > 1 && a[1] == "life") return NetherLife.Describe();
                        if (a.Length > 1 && a[1] == "tpexit")
                        {
                            // tpexit [n] : (into the fortress's portal n, standing in it: 0 the portal room's, then the exit rooms')
                            int n = a.Length > 2 ? int.Parse(a[2]) : 0;
                            if (n >= NetherFortress.Exits.Count) return "no fortress or no exit " + n;
                            var ex = NetherFortress.Exits[n];
                            var at = ex.position - ex.up * (1.45f * BlockWorld.S);
                            p.TeleportPlayer(at);
                            return "ok " + V(at);
                        }
                        return NetherTravel.Describe();
                    }
                case "potions":
                    // potions [give key | clear] : (dev) the local player's effects (#88)
                    if (a.Length > 2 && a[1] == "give" && Potions.Defs.TryGetValue(a[2], out var pdef)) Potions.Give(pdef);
                    if (a.Length > 1 && a[1] == "clear") Potions.Clear();
                    return Potions.Describe();
                case "unwedge":
                    // unwedge : (dev) how many times a caught player was moved free (#64)
                    return $"freed={Unwedge.Freed} fits={Commands.BodyFits(p.transform.position, p, 0.03f)}";
                case "endermen":
                    // endermen : (dev) the endermen, who they're after, how often they've teleported (#59)
                    if (a.Length > 1 && a[1] == "haste") EndermanAI.DevBlockHaste = a.Length < 3 || a[2] != "off";
                    // (endermen anger: every enderman after you, as if you'd looked one in the eyes)
                    if (a.Length > 1 && a[1] == "anger")
                    {
                        var ens = RoundManager.Instance.SpawnedEnemies.OfType<EndermanAI>().Where(x => !x.isEnemyDead).ToArray();
                        foreach (var en in ens) en.ServerProvoke(p.actualClientId);
                        return $"angered {ens.Length} for client {p.actualClientId} found={ServerLogic.PlayerFor(p.actualClientId) != null} | " + EndermanAI.Describe();
                    }
                    return EndermanAI.Describe();
                case "piglins":
                    // piglins : (dev) the piglins and their barters (#67)
                    return PiglinAI.Describe();
                case "lightabs":
                    // lightabs x y z : flint and steel at that natural-grid cell (a frame around it lights)
                    return "lit=" + NetherPortal.ServerTryLight(Ground.KeyOf(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]))));
                case "netherportal":
                    {
                        if (a.Length > 1 && a[1] == "clear")
                        {
                            // netherportal clear : (tests) take away the portals (and their obsidian) in the Nether
                            var gone = BlockWorld.Instance.Blocks.Values.Where(b => (b.Data.Def == Blocks.NetherPortal || b.Data.Def == Blocks.Obsidian) && b.Go != null && NetherFortress.Contains(b.Go.transform.position))
                                .Select(b => Op.Remove(b.Key, false)).ToList();
                            if (gone.Count > 0) BlockNet.ServerBroadcastOps(gone);
                            return "cleared " + gone.Count;
                        }
                        // netherportal step : stand in the nearest portal (its bottom block) within 8 m
                        var pb = BlockWorld.Instance.Blocks.Values.Where(b => b.Data.Def == Blocks.NetherPortal && b.Go != null && Vector3.Distance(b.Go.transform.position, p.transform.position) < 8f)
                            .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).ThenBy(b => b.Go.transform.position.y).FirstOrDefault();
                        if (pb == null) return "no portal near";
                        var bottom = BlockWorld.Instance.Blocks.Values.Where(b => b.Data.Def == Blocks.NetherPortal && b.Go != null && Vector3.ProjectOnPlane(b.Go.transform.position - pb.Go.transform.position, Vector3.up).magnitude < 0.1f)
                            .OrderBy(b => b.Go.transform.position.y).First();
                        p.TeleportPlayer(bottom.Go.transform.position - Vector3.up * (0.5f * BlockWorld.S) + Vector3.up * 0.05f);
                        return "in the portal at " + V(bottom.Go.transform.position);
                    }
                case "portal":
                    // portal : (dev) Nether portal blocks (#66)
                    return NetherPortal.Describe();
                case "farm":
                    // farm [stageSeconds caneSeconds drySeconds] : (dev) farmland, wheat by stage, sugar cane; set how long a wheat stage takes (#60)
                    if (a.Length > 1) Farming.StageSeconds = float.Parse(a[1]);
                    if (a.Length > 2) Farming.CaneSeconds = float.Parse(a[2]);
                    if (a.Length > 3) Farming.DrySeconds = float.Parse(a[3]);
                    return Farming.Describe();
                case "gridyoff":
                    // gridyoff : (dev) the moon grid's height offset (thousandths of a block; it differs per landing)
                    return Ground.GridYOff.ToString();
                case "soundinfo":
                    {
                        // soundinfo <id> : (dev) where a mod sound comes from (Minecraft's file or a stand-in); soundinfo recent
                        if (a[1] == "recent") return string.Join(" ; ", Sounds.Recent);
                        var c = Sounds.Get(a[1]);
                        return c == null ? "none" : $"{a[1]}: {(Sounds.FromMinecraft.Contains(a[1]) ? "Minecraft's file" : "synthesized stand-in")} {c.length:F2}s";
                    }
                case "soundlog":
                    DevSoundLog.On = a.Length > 1 && a[1] == "1";
                    return "soundlog=" + DevSoundLog.On;
                case "grabq":
                    return Inventory.DevQueue();
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
                        // (a modded moon's scene loads after routing, LethalLevelLoader keeps the lever locked till then)
                        if (lever.triggerScript != null && !lever.triggerScript.interactable) return "not ready: lever locked";
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
                        // (wrapping round: once earlier tests have built on many spots, a high n clamped to the last one would
                        // give every attempt the same, least flat spot)
                        var best = cands.OrderBy(c => c.score).ElementAt(want % cands.Count);
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
                        var list = BlockWorld.Instance.Blocks.Where(kv => kv.Value.Data.Def.DropsScrap).Take(5).Select(kv => kv.Value.Data.Def.Key + "@" + V(BlockWorld.Instance.WorldCenter(kv.Key)));
                        return string.Join(" ", list);
                    }
                case "screenshot":
                    {
                        string path = a.Length > 1 ? a[1] : Path.Combine(Plugin.PluginDir, "shot.png");
                        ScreenCapture.CaptureScreenshot(path);
                        return "ok " + path;
                    }
                case "headat":
                    {
                        // headat <key> [metres ahead] : the worn-head model (MobHeads.Build) in front of you, to look at (dev)
                        var gc = p.gameplayCamera.transform;
                        var holder = new GameObject("LMC_HeadAt");
                        holder.transform.position = gc.position + gc.forward * (a.Length > 2 ? float.Parse(a[2]) : 1.2f);
                        holder.transform.rotation = Quaternion.LookRotation(-Vector3.ProjectOnPlane(gc.forward, Vector3.up), Vector3.up);
                        var hg = MobHeads.Build(a[1], holder.transform, Vector3.zero, 0.5f);
                        if (hg != null) foreach (var t in hg.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                        Destroy(holder, 20f);
                        return hg != null ? "ok" : "no such head";
                    }
                case "ghastmove":
                    // ghastmove 0|1 : (dev, #191) ghasts out of sight of you float round to see you again, or don't (as before)
                    GhastAI.DevNoReposition = a.Length > 1 && a[1] == "0";
                    return "ghasts reposition: " + !GhastAI.DevNoReposition;
                case "camshot":
                    {
                        // camshot <path> [ahead up side] : a picture from a camera that far from the local player (in their
                        // look's frame, metres), looking back at their head, every layer drawn (your own body too: what you
                        // wear, which your own view never shows)
                        string path = a[1];
                        float ah = a.Length > 2 ? float.Parse(a[2]) : 1.6f, up = a.Length > 3 ? float.Parse(a[3]) : 0.2f, sd = a.Length > 4 ? float.Parse(a[4]) : 0f;
                        var gc = p.gameplayCamera;
                        var head = gc.transform.position;
                        var fwd = Vector3.ProjectOnPlane(gc.transform.forward, Vector3.up).normalized;
                        var right = Vector3.Cross(Vector3.up, fwd);
                        var go = new GameObject("LMC_CamShot");
                        try
                        {
                            var cam = go.AddComponent<Camera>();
                            cam.CopyFrom(gc);
                            cam.cullingMask = ~0;
                            cam.transform.position = head + fwd * ah + Vector3.up * up + right * sd;
                            cam.transform.LookAt(head - Vector3.up * 0.25f);
                            var rt = new RenderTexture(960, 540, 24);
                            cam.targetTexture = rt;
                            cam.Render();
                            var prev = RenderTexture.active; RenderTexture.active = rt;
                            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                            RenderTexture.active = prev;
                            File.WriteAllBytes(path, tex.EncodeToPNG());
                            cam.targetTexture = null; Destroy(rt); Destroy(tex);
                        }
                        finally { Destroy(go); }
                        return "ok " + path;
                    }
                case "enemy":
                    {
                        // enemy <name> [minDistance]  - spawns at the inside/outside AI node closest to minDistance away
                        if (!BlockNet.IsServer) return "server only";
                        var all = Resources.FindObjectsOfTypeAll<EnemyType>();
                        // (a name of several words: "enemy chicken jockey 3")
                        // (... natural: rolls for a variant as a natural spawn would)
                        bool asNatural = a[a.Length - 1] == "natural";
                        if (asNatural) a = a.Take(a.Length - 1).ToArray();
                        bool numLast = a.Length > 2 && float.TryParse(a[a.Length - 1], out _);
                        string ename = string.Join(" ", a.Skip(1).Take(a.Length - 1 - (numLast ? 1 : 0))).ToLower();
                        var et = all.Where(e => e.enemyName.ToLower() == ename).FirstOrDefault() ?? all.Where(e => e.enemyName.ToLower().Contains(ename))
                            .OrderBy(e => e.isOutsideEnemy == p.isInsideFactory ? 1 : 0).FirstOrDefault(); // (inside or outside, as you are)
                        if (et == null) return "none: " + string.Join(",", all.Select(e => e.enemyName).Distinct());
                        float want = numLast ? float.Parse(a[a.Length - 1]) : 15f;
                        var nodes = p.isInsideFactory ? RoundManager.Instance.insideAINodes : RoundManager.Instance.outsideAINodes;
                        if (NetherFortress.Contains(p.transform.position) && NetherFortress.Nodes != null) nodes = NetherFortress.Nodes; // (in the Nether: its own)
                        var n = nodes.Where(x => x != null).OrderBy(x => Mathf.Abs(Vector3.Distance(x.transform.position, p.transform.position) - want)).First();
                        MobSpawns.DevSpawnUntil = Time.time + 2f; // (allowed even when natural mob spawns are off)
                        MobSpawns.DevNaturalUntil = asNatural ? Time.time + 2f : -1f; // (2 s: at speed 8 a frame can be 0.8 s, and the spawn starts a frame later)
                        RoundManager.Instance.SpawnEnemyGameObject(n.transform.position, 0, -1, et);
                        return $"spawned {et.enemyName} at {V(n.transform.position)} d={Vector3.Distance(n.transform.position, p.transform.position):F1}";
                    }
                case "loose":
                    {
                        // loose [radius] : the items lying around you (key x count, in the ship or not), and their total
                        float r = a.Length > 1 ? float.Parse(a[1]) : 12f;
                        var gs = FindObjectsOfType<GrabbableObject>().Where(g => !g.isHeld && Vector3.Distance(g.transform.position, p.transform.position) < r).ToList();
                        int total = gs.Sum(g => Crafting.CountOf(g));
                        return $"loose={gs.Count} total={total} : " + string.Join(", ", gs.GroupBy(g => (Crafting.KeyOf(g) ?? g.itemProperties?.itemName) + (g.isInShipRoom ? "" : "(NOT in ship)"))
                            .Select(gr => $"{gr.Key} x{gr.Sum(g => Crafting.CountOf(g))}"));
                    }
                case "lowgaps":
                    // lowgaps [off|on] : how often a tall mob stopped short of a low gap (#112); off/on: the check itself
                    if (a.Length > 1) MinecraftMobAI.DevLowGapsOff = a[1] == "off";
                    return "lowGapStops=" + MinecraftMobAI.LowGapStops + " off=" + MinecraftMobAI.DevLowGapsOff;
                case "hitenemy":
                    {
                        // hitenemy <name> <force> : a hit from you on the nearest such monster (the sword's path: HitEnemy)
                        float f = float.Parse(a[a.Length - 1]);
                        string nm = string.Join(" ", a.Skip(1).Take(a.Length - 2)).ToLower();
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(nm))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "none";
                        // (the networked way, like a sword from any machine: HitEnemyOnLocalClient)
                        e.HitEnemyOnLocalClient((int)f, p.transform.forward, p, true, -1);
                        return $"hit {e.enemyType.enemyName} for {f}: hp={e.enemyHP} dead={e.isEnemyDead}";
                    }
                case "provoke":
                    {
                        // provoke <name> : a 0-damage hit from you on the nearest such monster (it chases you, as when hit)
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(string.Join(" ", a.Skip(1)).ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "none";
                        e.HitEnemy(0, p, true, -1);
                        return $"provoked {e.enemyType.enemyName} d={Vector3.Distance(e.transform.position, p.transform.position):F1}";
                    }
                case "enemyfacing":
                    {
                        // enemyfacing <name> [yaw] : the nearest such monster's facing (its forward's yaw), or turn it to face yaw
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(a[1].ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "none";
                        if (a.Length > 2) e.transform.rotation = Quaternion.Euler(0f, float.Parse(a[2]), 0f);
                        return $"{e.enemyType.enemyName} yaw={e.transform.eulerAngles.y:F0} at {V(e.transform.position)}";
                    }
                case "enemyheight":
                    {
                        // enemyheight : how tall each monster's model stands (its renderers' bounds), and the player's controller
                        var sb = new StringBuilder($"player cc={p.thisController.height:F2} ; ");
                        foreach (var e in RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead))
                        {
                            var rs = e.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToList();
                            if (rs.Count == 0) continue;
                            var bd = rs[0].bounds; foreach (var r in rs) bd.Encapsulate(r.bounds);
                            sb.Append($"{e.enemyType.enemyName}={bd.max.y - e.transform.position.y:F2} ; ");
                        }
                        return sb.ToString();
                    }
                case "enemynet":
                    {
                        // enemynet : every EnemyAI object on this machine: network state, parent, where (#101: leftovers in orbit)
                        return string.Join(" ; ", Resources.FindObjectsOfTypeAll<global::EnemyAI>().Where(e => e.gameObject.scene.IsValid()).Select(e => $"{e.enemyType?.enemyName} id={e.NetworkObjectId} spawned={e.NetworkObject != null && e.NetworkObject.IsSpawned} owner={e.OwnerClientId} parent={(e.transform.parent != null ? e.transform.parent.name : "-")} scene={e.gameObject.scene.name} at={V(e.transform.position)} active={e.gameObject.activeInHierarchy} inList={RoundManager.Instance.SpawnedEnemies.Contains(e)}"));
                    }
                case "enemyhold":
                    {
                        // enemyhold <name> [dist] : stop the nearest such monster's AI and stand it in front of the player (hits still land)
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(a[1].ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "none";
                        float d = a.Length > 2 ? float.Parse(a[2]) : 1.8f;
                        // (its search runs as a coroutine, which a disabled AI keeps running: stop it, or it errors when the level goes)
                        if (e.currentSearch != null && e.currentSearch.inProgress) e.StopSearch(e.currentSearch);
                        if (e.agent != null) e.agent.enabled = false;
                        e.enabled = false;
                        var f = p.transform.forward; f.y = 0; f.Normalize();
                        e.transform.position = p.transform.position + f * d;
                        e.serverPosition = e.transform.position;
                        // (what a swing's line of sight would hit on the way, like the shovel's check: a tree, a wall)
                        var cam = p.gameplayCamera.transform.position;
                        string blocked = Physics.Linecast(cam, e.transform.position + Vector3.up * 0.8f, out var wall, StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
                            && wall.collider.GetComponentInParent<EnemyAI>() != e ? wall.collider.name : "-";
                        return $"holding {e.enemyType.enemyName} hp={e.enemyHP} at {V(e.transform.position)} blocked={blocked}";
                    }
                case "enemyrelease":
                    {
                        // enemyrelease <name> : the held monster (enemyhold) runs its AI again from where it stands
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(a[1].ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null) return "none";
                        e.enabled = true;
                        if (e.agent != null) { e.agent.enabled = true; e.agent.Warp(e.transform.position); }
                        // (the game sends a destination again only when it changes: one chasing before the hold, after the warp,
                        // would stand there with no path to the same spot)
                        HarmonyLib.AccessTools.Field(typeof(EnemyAI), "prevDestination").SetValue(e, Vector3.zero);
                        if (a.Length > 2 && a[2] == "outside") e.isOutside = !p.isInsideFactory; // (an inside monster let out, as if it had followed you)
                        return $"released {e.enemyType.enemyName} onNav={(e.agent != null && e.agent.isOnNavMesh)} outside={e.isOutside}";
                    }
                case "enemystrand":
                    {
                        // enemystrand <name> : lift the nearest such monster 30 m into the air with its AI still running (off the navmesh, #25)
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(a[1].ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null || e.agent == null) return "none";
                        e.agent.enabled = false;
                        e.transform.position += Vector3.up * 30f;
                        e.agent.enabled = true;
                        return $"{e.enemyType.enemyName} at {V(e.transform.position)} onNav={e.agent.isOnNavMesh}";
                    }
                case "vents":
                    {
                        // vents : monster vents and their duct colliders (bounds), nearest first
                        var vs = FindObjectsOfType<EnemyVent>().OrderBy(v => Vector3.Distance(v.transform.position, p.transform.position));
                        return string.Join(" ; ", vs.Take(8).Select(v => $"{v.name}@{V(v.transform.position)} d={Vector3.Distance(v.transform.position, p.transform.position):F1} cols=[" +
                            string.Join(",", v.transform.root.GetComponentsInChildren<Collider>().Where(c => c.name.Contains("vent") || c.name.Contains("Vent")).Take(4)
                                .Select(c => $"{c.name} {c.GetType().Name} L{c.gameObject.layer} {(c.enabled ? "" : "off ")}b={V(c.bounds.center)}/{V(c.bounds.size)}")) + "]"));
                    }
                case "trees":
                    {
                        // trees [r] [x y z] : tree trunks (tagged Tree, layer 25 like the game's own tree breaking looks for) near the
                        // player or a point, nearest first
                        float r = a.Length > 1 ? float.Parse(a[1]) : 30f;
                        var at = a.Length > 4 ? new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])) : p.transform.position;
                        var cols = Physics.OverlapSphere(at, r, 1 << 25, QueryTriggerInteraction.Ignore).Where(c => c.CompareTag("Tree"))
                            .OrderBy(c => Vector3.Distance(c.transform.position, at)).ToList();
                        return $"layer25={LayerMask.LayerToName(25)} n={cols.Count} : " + string.Join(" ; ", cols.Take(30).Select(c =>
                            $"{c.name} tag={c.tag} {c.GetType().Name} at {V(c.transform.position)} d={Vector3.Distance(c.transform.position, at):F1} parent={(c.transform.parent != null ? c.transform.parent.name : "-")} rend={c.GetComponentsInChildren<Renderer>().Length} kids={c.transform.childCount}"));
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
                case "oob":
                    return NetherVoid.Describe();
                case "entrances":
                    {
                        // every entrance: id, outside/inside, and whether its pair (same id, other side) exists
                        var all = FindObjectsOfType<EntranceTeleport>();
                        return NetherGenGuard.Describe() + " | " + string.Join(" ; ", all.Select((e, i) => $"{i}:{e.name} id={e.entranceId} {(e.isEntranceToBuilding ? "out" : "in")} paired={all.Any(o => o != e && o.entranceId == e.entranceId && o.isEntranceToBuilding != e.isEntranceToBuilding)} at={e.transform.position.x:F0},{e.transform.position.y:F0},{e.transform.position.z:F0}"));
                    }
                case "useentrance":
                    {
                        // use entrance n the way pressing E on it does (tip "blocked" if it has no pair)
                        var all = FindObjectsOfType<EntranceTeleport>();
                        var e = all[int.Parse(a[1])];
                        var before = p.transform.position;
                        e.TeleportPlayer();
                        return $"used {e.name} id={e.entranceId}: {before} -> {p.transform.position} moved={(Vector3.Distance(before, p.transform.position) > 1f)}";
                    }
                case "shipcols":
                    {
                        // shipcols [minY] : the ship's colliders (not blocks or items), their layer, tag, mesh and world bounds
                        float minY = a.Length > 1 ? float.Parse(a[1]) : -999f;
                        var sb = new StringBuilder();
                        foreach (var c in StartOfRound.Instance.elevatorTransform.GetComponentsInChildren<Collider>(true))
                        {
                            if (!ShipAttach.IsShipCollider(c) || c.isTrigger || c.bounds.max.y < minY) continue;
                            var b = c.bounds;
                            sb.Append($"[{c.name} L{c.gameObject.layer} tag={c.tag} {(c is MeshCollider mc ? "mesh=" + mc.sharedMesh?.name + " v=" + mc.sharedMesh?.vertexCount + " rd=" + mc.sharedMesh?.isReadable : c.GetType().Name)} on={c.enabled && c.gameObject.activeInHierarchy} min={V(b.min)} max={V(b.max)} parent={c.transform.parent?.name}] ");
                        }
                        return sb.ToString();
                    }
                case "shiprens":
                    {
                        // shiprens [minY] : the ship's mesh renderers reaching above minY (world), their layer, mesh and bounds
                        float minY = a.Length > 1 ? float.Parse(a[1]) : -999f;
                        var sb = new StringBuilder();
                        foreach (var r in StartOfRound.Instance.elevatorTransform.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (r.bounds.max.y < minY || r.GetComponentInParent<GrabbableObject>() != null || r.GetComponentInParent<BlockRef>() != null || r.GetComponentInParent<PlayerControllerB>() != null) continue;
                            var mf = r.GetComponent<MeshFilter>(); var b = r.bounds;
                            sb.Append($"[{r.name} L{r.gameObject.layer} mesh={mf?.sharedMesh?.name} v={mf?.sharedMesh?.vertexCount} rd={mf?.sharedMesh?.isReadable} on={r.enabled && r.gameObject.activeInHierarchy} col={r.GetComponent<Collider>() != null} min={V(b.min)} max={V(b.max)} parent={r.transform.parent?.name}] ");
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
                case "scenestats":
                    {
                        // scenestats : what's in the scene (what a lasting per-frame cost could come from)
                        var items = FindObjectsOfType<GrabbableObject>();
                        var bodies = FindObjectsOfType<Rigidbody>();
                        var rends = FindObjectsOfType<Renderer>();
                        var lights = FindObjectsOfType<Light>();
                        var audio = FindObjectsOfType<AudioSource>();
                        var cols = FindObjectsOfType<Collider>();
                        return $"items={items.Length} (ours {items.Count(i => Crafting.KeyOf(i) != null)}, falling {items.Count(i => !i.isHeld && !i.hasHitGround)}) " +
                            $"bodies={bodies.Length} awake={bodies.Count(b => !b.IsSleeping() && !b.isKinematic)} " +
                            $"renderers={rends.Count(r => r.enabled && r.gameObject.activeInHierarchy)}/{rends.Length} lights={lights.Count(l => l.enabled && l.gameObject.activeInHierarchy)} " +
                            $"audio={audio.Count(a2 => a2.isPlaying)}/{audio.Length} colliders={cols.Count(c => c.enabled)} groundChunks={cols.Count(c => c.name == "LMC_GroundChunk")} " +
                            $"blocks={BlockWorld.Instance?.Blocks.Count} cuts={TerrainCarver.Cuts.Count}";
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
                        return $"have={ok} bounds={fb} inTile={Facility.DebugInTile(p.transform.position)} inside={p.isInsideFactory} elev={p.isInElevator} hangar={p.isInHangarShipRoom} cuts={TerrainCarver.Cuts.Count}" +
                               // (what a monster's PlayerIsTargetable looks at, besides inside)
                               $" controlled={p.isPlayerControlled} withEnemy={(p.inAnimationWithEnemy != null ? p.inAnimationWithEnemy.enemyType.enemyName : "-")} sinking={p.sinkingValue:F2} terminal={p.inTerminalMenu} special={p.inSpecialInteractAnimation}";
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
                case "mmb":
                    MmbClick = true;
                    return "middle click";
                case "autopick":
                    // autopick 1|0 : the AutoPickupItems setting (written to the config file: set it back after)
                    if (a.Length > 1) Plugin.AutoPickup.Value = a[1] == "1";
                    return "autopick=" + Plugin.AutoPickup.Value;
                case "setprice":
                    {
                        // setprice <key> <price> : (host) change a store price as a config edit would; joiners get it
                        if (!Balance.ShopItems.TryGetValue(a[1], out var item)) return "not a store item";
                        int pr = int.Parse(a[2]);
                        Balance.ShopPrices[a[1]] = pr;
                        LethalLib.Modules.Items.UpdateShopItemPrice(item, pr);
                        return $"{a[1]} = {item.creditsWorth}";
                    }
                case "slotrace":
                    {
                        // slotrace <netId> <slot> : (#209) pick an item up and free a hotbar slot in the same frame, the way a
                        // player does with lag (a chest put while a pickup is on its way): each side picks its own empty slot
                        var nm = Unity.Netcode.NetworkManager.Singleton;
                        if (!nm.SpawnManager.SpawnedObjects.TryGetValue(ulong.Parse(a[1]), out var gno)) return "no object";
                        Inventory.GrabDirect(p, gno.GetComponent<GrabbableObject>());
                        p.DestroyItemInSlotAndSync(int.Parse(a[2]));
                        return "ok";
                    }
                case "slotsync":
                    // slotsync [0|1] : (#209) players' hotbars kept in step with what they see; the host's corrections so far
                    if (a.Length > 1) SlotSync.Enabled = a[1] == "1";
                    return $"slotsync={SlotSync.Enabled} corrections={SlotSync.Corrections} {SlotSync.DevState()}";
                case "allslots":
                    {
                        // allslots : every player's hotbar as this instance sees it (#209: the host's view and the player's own disagreed)
                        var sb = new System.Text.StringBuilder();
                        foreach (var q in StartOfRound.Instance.allPlayerScripts)
                        {
                            if (q == null || (!q.isPlayerControlled && !q.isPlayerDead)) continue;
                            sb.Append($"#{q.playerClientId} {q.playerUsername} cur={q.currentItemSlot} held={(q.currentlyHeldObjectServer != null ? q.currentlyHeldObjectServer.itemProperties.itemName + ":" + q.currentlyHeldObjectServer.NetworkObjectId : "-")} [");
                            sb.Append(string.Join(",", q.ItemSlots.Select(g => g == null ? "-" : g.itemProperties.itemName + ":" + g.NetworkObjectId)));
                            sb.Append("] ; ");
                        }
                        return sb.ToString();
                    }
                case "shippets":
                    // shippets [0|1] : (#210) tamed pets in the ship ride along with it (1) or are respawned at the next landing (0, the old way)
                    if (a.Length > 1) ShipPassengers.Enabled = a[1] == "1";
                    return $"shippets={ShipPassengers.Enabled} {ShipPassengers.Describe()}";
                case "slotvalues":
                    // slotvalues : each hotbar slot's item, count, and what it sells for (scrap)
                    return string.Join(",", p.ItemSlots.Select(g => g == null ? "-" : $"{g.itemProperties.itemName}x{Crafting.CountOf(g)}:{(g.itemProperties.isScrap ? "$" + g.scrapValue : "-")}"));
                case "orecensus":
                    {
                        // orecensus [size] : what GroundRules.OreFor gives over a size^3 cube of stone (depth 10, 40 below the surface)
                        int n = a.Length > 1 ? int.Parse(a[1]) : 64;
                        var counts = new Dictionary<GroundRules.Ore, int>();
                        for (int x = 0; x < n; x++) for (int y = -n; y < 0; y++) for (int z = 0; z < n; z++)
                        {
                            var o = GroundRules.OreFor(x + 5000, y - 200, z + 5000, 10f, 40f);
                            counts[o] = (counts.TryGetValue(o, out var k) ? k : 0) + 1;
                        }
                        var lvl = StartOfRound.Instance.currentLevel;
                        return $"{lvl?.PlanetName} risk='{lvl?.riskLevel}' wants x{Balance.OreMultiplierFor(lvl)} has x{GroundVeins.MoonMultiplier} " + string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")) + " | chances " + string.Join(", ", GroundVeins.Kinds.Select(k => $"{k.Ore}={k.Chance:0.####}"));
                    }
                case "findore":
                    {
                        // findore <ore> [radius] [down] : nearest cells under the player the ore rule makes that ore (depth
                        // taken as deep; blocks below the surface counted from the player's feet)
                        var ore = (GroundRules.Ore)Enum.Parse(typeof(GroundRules.Ore), a[1], true);
                        int r = a.Length > 2 ? int.Parse(a[2]) : 12, down = a.Length > 3 ? int.Parse(a[3]) : 50;
                        var f = Ground.CellOf(p.transform.position);
                        var found = new List<Vector3Int>();
                        for (int dy = 1; dy <= down; dy++) for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++)
                            if (GroundRules.OreFor(f.x + dx, f.y - dy, f.z + dz, 10f, dy) == ore) found.Add(new Vector3Int(f.x + dx, f.y - dy, f.z + dz));
                        return $"feet {f} : " + string.Join(" ; ", found.OrderBy(c => (c - f).sqrMagnitude).Take(12).Select(c => $"{c} d{f.y - c.y}"));
                    }
                case "blockcount":
                    {
                        // blockcount [key] : how many blocks of each kind (or of one) are in the world
                        var counts = BlockWorld.Instance.Blocks.Values.GroupBy(b => b.Data.Def.Key).ToDictionary(g => g.Key, g => g.Count());
                        if (a.Length > 1) return $"{a[1]}={(counts.TryGetValue(a[1], out var c) ? c : 0)}";
                        return string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}"));
                    }
                case "walkh":
                    // walkh <m> : how high above walkable navmesh counts as open air (to compare)
                    if (a.Length > 1) Ground.WalkableHeight = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    return "walkable height " + Ground.WalkableHeight;
                case "ceilray":
                    {
                        // ceilray x y z : straight up from each of a cell's 9 samples: what real collider is overhead (#29)
                        var c = new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var ctr = Ground.Center(c); float o = 0.35f * Plugin.S;
                        var sb = new System.Text.StringBuilder($"hitBackfaces={Physics.queriesHitBackfaces} ");
                        for (int k = 0; k < 9; k++)
                        {
                            var off = k == 0 ? Vector3.zero : new Vector3((k & 1) != 0 ? o : -o, (k & 2) != 0 ? o : -o, (k & 4) != 0 ? o : -o);
                            if (k == 8) off = new Vector3(-o, -o, -o);
                            var q = ctr + off;
                            string up = Physics.Raycast(q, Vector3.up, out var h, 8f, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore) ? $"{h.collider.name} {h.distance:F2} n{h.normal.y:F2}" : "-";
                            string dn = Physics.Raycast(q, Vector3.down, out var h2, 8f, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore) ? $"{h2.collider.name} {h2.distance:F2} n{h2.normal.y:F2}" : "-";
                            bool nav = UnityEngine.AI.NavMesh.SamplePosition(q, out var nh, 4f, UnityEngine.AI.NavMesh.AllAreas);
                            sb.Append($"#{k} y{q.y:F2} up[{up}] down[{dn}] nav={(nav ? $"{q.y - nh.position.y:F2} up, {new Vector2(q.x - nh.position.x, q.z - nh.position.z).magnitude:F2} off" : "-")} || ");
                        }
                        return sb.ToString();
                    }
                case "ceilless":
                    // ceilless <m> : how high above walkable navmesh, with no collider overhead, counts as open air (#29; 0 off)
                    if (a.Length > 1) Ground.CeilinglessHeight = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    return "ceilingless height " + Ground.CeilinglessHeight;
                case "samplesat":
                    // samplesat x y z : the 9 sample points of a cell: rays, walkable air, solid or not
                    return Ground.SamplesWhy(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
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
                    // the utility belt (the game's own DestroyItemInSlot(50) throws while holding something: it indexes the hotbar icons with 50)
                    if (p.ItemOnlySlot != null && BlockNet.IsServer)
                    {
                        var belt = p.ItemOnlySlot;
                        p.carryWeight = Mathf.Clamp(p.carryWeight - (belt.itemProperties.weight - 1f), 1f, 10f);
                        p.ItemOnlySlot = null;
                        if (HUDManager.Instance != null && HUDManager.Instance.itemOnlySlotIcon != null) HUDManager.Instance.itemOnlySlotIcon.enabled = false;
                        belt.NetworkObject.Despawn();
                    }
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
                case "smithui":
                    {
                        // smithui open | close | state | click slot|out|hot|outside index [right] [shift] (#159)
                        var ui = SmithingUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a.Length > 1 ? a[1] : "state")
                        {
                            case "open": SmithingUI.Open(); return ui.DevState();
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
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
                case "furnaceui":
                    {
                        // furnaceui open near | close | state | click furnace|hot|outside index [right] [shift]
                        var ui = FurnaceUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open":
                                {
                                    var w = BlockWorld.Instance;
                                    var near = w.Blocks.Values.Where(b => b.Data.Def == Blocks.Furnace && b.Go != null)
                                        .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                                    if (near == null) return "no furnace";
                                    FurnaceUI.Open(near.Key);
                                    return ui.DevState();
                                }
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "brewui":
                    {
                        // brewui open near | close | state | click stand|hot|outside index [right] [shift]  (#88)
                        // brewui time s : seconds a brew takes (20, Minecraft's)
                        if (a.Length > 2 && a[1] == "time") { Brewing.BrewSeconds = float.Parse(a[2]); return $"brew time {Brewing.BrewSeconds}"; }
                        var ui = BrewingUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open":
                                {
                                    var w = BlockWorld.Instance;
                                    var near = w.Blocks.Values.Where(b => b.Data.Def == Blocks.BrewingStand && b.Go != null)
                                        .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                                    if (near == null) return "no brewing stand";
                                    BrewingUI.Open(near.Key);
                                    return ui.DevState();
                                }
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "enchantui":
                    {
                        // enchantui open x y z|near | close | state | click item|lapis|offer|hot|outside index [right] [shift]
                        var ui = EnchantUI.Instance;
                        if (ui == null) return "no ui";
                        switch (a[1])
                        {
                            case "open":
                                if (a[2] == "near")
                                {
                                    var w = BlockWorld.Instance;
                                    var near = w.Blocks.Values.Where(b => b.Data.Def == Blocks.EnchantingTable && b.Go != null)
                                        .OrderBy(b => Vector3.Distance(b.Go.transform.position, p.transform.position)).FirstOrDefault();
                                    if (near == null) return "no table";
                                    EnchantUI.Open(near.Key);
                                }
                                else EnchantUI.Open(Ground.KeyOf(new Vector3Int(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]))));
                                return ui.DevState();
                            case "close": ui.Close(); return "closed";
                            case "click": return ui.DevClick(a[2], a.Length > 3 ? int.Parse(a[3]) : 0, a.Contains("right"), a.Contains("shift"));
                            case "pos": return ui.DevSlotPos(a[2], int.Parse(a[3]));
                            default: return ui.DevState();
                        }
                    }
                case "xplevel":
                    {
                        // xplevel [n] : (dev) your XP level (set it)
                        if (a.Length > 1) { Survival.XpTotal = Survival.TotalForLevel(int.Parse(a[1])); }
                        return "level=" + (Survival.Instance != null ? Survival.Instance.XpLevel : -1) + " total=" + Survival.XpTotal;
                    }
                case "enchant":
                    {
                        // enchant <kind> <level> [<kind> <level>...] : (dev, server) put enchantments on the held item
                        var g = p.currentlyHeldObjectServer;
                        var list = new List<(EnchKind, int)>();
                        for (int i = 1; i + 1 < a.Length; i += 2) list.Add(((EnchKind)System.Enum.Parse(typeof(EnchKind), a[i], true), int.Parse(a[i + 1])));
                        if (g is ToolItem tl) tl.Ench = Enchants.Pack(list);
                        else if (g is ArmorItem ar) ar.Ench = Enchants.Pack(list);
                        else return "not a tool or armor";
                        Glint.ApplyModel(g);
                        BlockNet.ServerItemData(g);
                        return "ok " + Crafting.KeyOf(g);
                    }
                case "itemdata":
                    {
                        // itemdata : (dev) the held item's saved number: uses and enchantments
                        var g = p.currentlyHeldObjectServer;
                        if (g == null) return "nothing held";
                        int d = g.GetItemDataToSave();
                        return $"{g.itemProperties?.itemName} data={d} uses={Enchants.UsesOf(d)} ench={Enchants.Describe(Enchants.EnchOf(d))} key={Crafting.KeyOf(g)} breaktime_stone={(g is ToolItem tt ? Builder.BreakTime(Blocks.Stone, tt) : -1):F2}";
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
                case "wear":
                    {
                        // wear <key>... | none : (host) the local player wears these armor pieces
                        var keys = new string[Armor.Slots];
                        foreach (var k in a.Skip(1)) { var d = Armor.Get(k); if (d != null) keys[d.Slot] = d.Key; }
                        Armor.ServerSet(p.actualClientId, keys, false, Vector3.zero);
                        return string.Join(",", Armor.Of(p.actualClientId).Select(k => k ?? "-"));
                    }
                case "armorstrike":
                    // armorstrike : (dev) the storm picks a metal-armored player at the next tick (to test without waiting)
                    Storms.DevForceArmorStrike = true;
                    return "strikes so far " + Storms.ArmorStrikes;
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
                case "decals":
                    {
                        // decals [radius] : HDRP decal projectors near the player (#40: stains showing on blocks)
                        float r = a.Length > 1 ? float.Parse(a[1]) : 10f;
                        var hd = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset;
                        string head = $"decalLayers={hd?.currentPlatformRenderPipelineSettings.supportDecalLayers} opaqueKeyword={Atlas.Opaque.IsKeywordEnabled("_DISABLE_DECALS")} ";
                        var list = FindObjectsOfType<UnityEngine.Rendering.HighDefinition.DecalProjector>()
                            .Where(d => Vector3.Distance(d.transform.position, p.transform.position) < r)
                            .OrderBy(d => Vector3.Distance(d.transform.position, p.transform.position)).Take(12)
                            .Select(d => $"{d.name}@{V(d.transform.position)} size={d.size} pivot={d.pivot} fwd={V(d.transform.forward)} mat={d.material?.name} layer={d.decalLayerMask} en={d.enabled}");
                        var meshes = FindObjectsOfType<Renderer>()
                            .Where(rd => rd.sharedMaterial != null && rd.sharedMaterial.shader != null && rd.sharedMaterial.shader.name.Contains("Decal")
                                    && Vector3.Distance(rd.bounds.center, p.transform.position) < r)
                            .OrderBy(rd => Vector3.Distance(rd.bounds.center, p.transform.position)).Take(12)
                            .Select(rd => $"MESH {rd.name}@{V(rd.bounds.center)} size={rd.bounds.size} mat={rd.sharedMaterial.name} shader={rd.sharedMaterial.shader.name} layers={rd.renderingLayerMask}");
                        var mine = Atlas.Opaque;
                        head += $"blockSupportDecals={mine.GetFloat("_SupportDecals")} blockRenderingLayers={BlockWorld.Instance?.GetComponentInChildren<Renderer>()?.renderingLayerMask} ";
                        return head + string.Join(" ; ", list.Concat(meshes));
                    }
                case "storecheck":
                    {
                        // storecheck : every store item, typed as its name (and plurals), resolves to a buy node for that
                        // same item (#39: "buy bookshelves" bought boomboxes). No landing needed.
                        var term = FindObjectOfType<Terminal>();
                        if (term == null) return "no terminal";
                        TerminalWords.SquashOurKeywords(term); // (as typing a sentence does first)
                        var buy = term.terminalNodes.allKeywords.FirstOrDefault(k => k != null && k.word == "buy");
                        if (buy == null) return "no buy verb";
                        var parse = HarmonyLib.AccessTools.Method(typeof(Terminal), "ParseWord");
                        var gotVerb = HarmonyLib.AccessTools.Field(typeof(Terminal), "hasGottenVerb");
                        var bad = new List<string>();
                        int n = 0, checkedWords = 0;
                        for (int i = 0; i < term.buyableItemsList.Length; i++)
                        {
                            var it = term.buyableItemsList[i];
                            if (it == null) continue;
                            n++;
                            // the word the store gives it (squashed, maybe with "block" on the end), as a typed sentence ends up
                            string name = buy.compatibleNouns.FirstOrDefault(c => c.result != null && c.result.buyItemIndex == i)?.noun?.word;
                            if (name == null) { bad.Add($"{it.itemName}: no buy word"); continue; }
                            foreach (var typed in new[] { name, name + "s", name + "es", name.EndsWith("f") ? name.Substring(0, name.Length - 1) + "ves" : null })
                            {
                                if (typed == null) continue;
                                gotVerb.SetValue(term, true);
                                var kw = (TerminalKeyword)parse.Invoke(term, new object[] { typed, 2 });
                                gotVerb.SetValue(term, false);
                                checkedWords++;
                                var cn = kw == null ? null : buy.compatibleNouns.FirstOrDefault(c => c.noun == kw);
                                int idx = cn?.result != null ? cn.result.buyItemIndex : -99;
                                string got = idx >= 0 && idx < term.buyableItemsList.Length ? term.buyableItemsList[idx].itemName : (kw == null ? "(no word)" : $"(word '{kw.word}', index {idx})");
                                // (a typed word that is another item's own name is that item: fine, only check our item's words)
                                if (got != it.itemName && !(typed != name && buy.compatibleNouns.Any(c => c.noun != null && c.noun.word == typed)))
                                    bad.Add($"'{typed}' -> {got} (want {it.itemName})");
                            }
                        }
                        return $"{n} store items, {checkedWords} words checked, {bad.Count} wrong" + (bad.Count > 0 ? ": " + string.Join(" ; ", bad.Take(40)) : "");
                    }
                case "termwords":
                    {
                        // termwords <part> : terminal keywords containing it, and what "buy" does with each
                        var term = FindObjectOfType<Terminal>();
                        var buy = term.terminalNodes.allKeywords.FirstOrDefault(k => k != null && k.word == "buy");
                        return string.Join(" ; ", term.terminalNodes.allKeywords.Where(k => k != null && k.word != null && k.word.Contains(a[1]))
                            .Select(k => { var cn = buy.compatibleNouns.Where(c => c.noun == k).ToList();
                                return $"'{k.word}' verb={k.isVerb} buyNouns={cn.Count} idx=[{string.Join(",", cn.Select(c => c.result != null ? c.result.buyItemIndex : -99))}] node=[{string.Join(",", cn.Select(c => c.result?.name))}]"; }));
                    }
                case "starter":
                    {
                        // starter : the supply chest's candidate spots and what's in the way; starter here : the spot under you
                        if (a.Length > 1 && a[1] == "scan")
                        {
                            var free = new List<string>();
                            for (float x = -8f; x <= 12f; x += 0.7f)
                                for (float z = -11f; z <= -2f; z += 0.7f)
                                {
                                    var r = Starter.DevSpots(new Vector3(x, 0.4f, z));
                                    if (r.Contains("blockers=[]") && r.Contains("floor=ShipInside")) free.Add($"{x:0.0},{z:0.0}");
                                }
                            return string.Join(" ", free);
                        }
                        if (a.Length > 1 && a[1] == "here") return Starter.DevSpots(StartOfRound.Instance.elevatorTransform.InverseTransformPoint(p.transform.position));
                        return Starter.DevSpots();
                    }
                case "decalfix":
                    {
                        // decalfix <0|1> [layers]: block materials receive decals (1: and so are in the early depth pass) / not (0)
                        foreach (var m in new[] { Atlas.Opaque, Atlas.Cutout, Atlas.Emissive, Atlas.CutoutEmissive, Atlas.Crack, Atlas.OpaqueAmb, Atlas.CutoutAmb, BlockWorld.Instance.FlashMaterial })
                        {
                            if (m == null) continue;
                            m.SetFloat("_SupportDecals", a[1] == "1" ? 1f : 0f);
                            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
                        }
                        if (a.Length > 2 && a[2] == "layers")
                            foreach (var rd in BlockWorld.Instance.GetComponentsInChildren<Renderer>(true).Concat(FindObjectsOfType<BlockRef>().SelectMany(b => b.GetComponentsInChildren<Renderer>())))
                                rd.renderingLayerMask = 1; // light layer default only: no decal layer
                        return "supportDecals=" + Atlas.Opaque.GetFloat("_SupportDecals") + " keyword=" + Atlas.Opaque.IsKeywordEnabled("_DISABLE_DECALS");
                    }
                case "bleedfix":
                    Survival.DevBleedFix = a[1] == "1";
                    return "bleed fix " + Survival.DevBleedFix;
                case "lateinjury":
                    // lateinjury : the game's critical-injury message arriving late (after healing past 20), as it does to the
                    // injured player itself a round trip after they were hurt
                    p.criticallyInjured = true; p.bleedingHeavily = true; p.playerBodyAnimator.SetBool("Limp", true);
                    return $"hp={p.health} critical={p.criticallyInjured} bleeding={p.bleedingHeavily}";
                case "uibtn":
                    {
                        // uibtn list [filter] | uibtn click <name or text> : the active UI buttons (menus), invoke one like a click
                        var btns = FindObjectsOfType<UnityEngine.UI.Button>().Where(bt => bt.isActiveAndEnabled).ToList();
                        string Label(UnityEngine.UI.Button bt) => bt.name + "'" + (bt.GetComponentInChildren<TMPro.TMP_Text>()?.text ?? "").Replace('\n', ' ').Trim() + "'";
                        if (a[1] == "list")
                            return string.Join(" ; ", btns.Select(Label).Where(l => a.Length < 3 || l.IndexOf(a[2], System.StringComparison.OrdinalIgnoreCase) >= 0));
                        string want = string.Join(" ", a.Skip(2));
                        var hit = btns.FirstOrDefault(bt => Label(bt).IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0);
                        if (hit == null) return "no button " + want;
                        hit.onClick.Invoke();
                        return "clicked " + Label(hit);
                    }
                case "rebinds":
                    {
                        // rebinds [start <i>] : InputUtils' rebind buttons (the controls menu), start rebinding one (then press a key)
                        var t = System.Type.GetType("LethalCompanyInputUtils.Components.RebindButton, LethalCompanyInputUtils");
                        if (t == null) return "no InputUtils";
                        var all = FindObjectsOfType(t).Cast<Component>().Where(c => c.gameObject.activeInHierarchy).ToList();
                        var keyF = HarmonyLib.AccessTools.Field(t, "_key");
                        string Desc(Component c, int i)
                        {
                            var k = keyF.GetValue(c) as RemappableKey;
                            var act = k?.currentInput?.action;
                            return $"{i}:{k?.ControlName}[{k?.rebindingIndex}]={(act != null && k.rebindingIndex >= 0 && k.rebindingIndex < act.bindings.Count ? act.bindings[k.rebindingIndex].effectivePath : act?.bindings.FirstOrDefault().effectivePath)} on={act?.enabled}";
                        }
                        if (a.Length > 2 && a[1] == "start")
                        {
                            int i = int.Parse(a[2]);
                            HarmonyLib.AccessTools.Method(t, "StartRebinding").Invoke(all[i], null);
                            return "rebinding " + Desc(all[i], i);
                        }
                        return string.Join(" ; ", all.Select(Desc).Where(d => a.Length < 3 || d.IndexOf(a[2], System.StringComparison.OrdinalIgnoreCase) >= 0));
                    }
                case "actions":
                    {
                        // actions : the game's input actions that are switched off (can't move / crouch... #38)
                        var off = UnityEngine.InputSystem.InputSystem.actions.Where(ac => !ac.enabled).Select(ac => ac.actionMap.name + "/" + ac.name).ToList();
                        return $"{UnityEngine.InputSystem.InputSystem.actions.Count()} actions, {off.Count} off" + (off.Count > 0 ? ": " + string.Join(", ", off.Take(40)) : "");
                    }
                case "maskedkill":
                    {
                        // maskedkill <playerIndex> : (#212) the nearest masked comes up to that player and starts its own strangling (the game's RPC)
                        if (!BlockNet.IsServer) return "server only";
                        var victim = StartOfRound.Instance.allPlayerScripts[int.Parse(a[1])];
                        var mk = FindObjectsOfType<MaskedPlayerEnemy>().Where(x => !x.isEnemyDead).OrderBy(x => Vector3.Distance(x.transform.position, victim.transform.position)).FirstOrDefault();
                        if (mk == null) return "no masked";
                        if (mk.agent != null && mk.agent.enabled) mk.agent.Warp(victim.transform.position + victim.transform.forward * 1.2f); else mk.transform.position = victim.transform.position + victim.transform.forward * 1.2f;
                        mk.KillPlayerAnimationServerRpc((int)victim.playerClientId);
                        return $"masked {mk.NetworkObjectId} strangles {victim.playerUsername}";
                    }
                case "totem":
                    // totem 0|1 : (dev) deaths ignore / use the Totem of Undying
                    if (a[1] == "fx") return Totem.DevFx();
                    if (a[1] == "maskedfix") { if (a.Length > 2) Totem.MaskedFix = a[2] == "1"; return "maskedfix=" + Totem.MaskedFix; }
                    if (a[1] == "worm") { Totem.DevWorm(); return "bitten"; }
                    if (a[1] == "launch") { Totem.LaunchUp = float.Parse(a[2]); Totem.LaunchOut = float.Parse(a[3]); return $"launch {Totem.LaunchUp} {Totem.LaunchOut}"; }
                    if (a[1] == "pops") return string.Join(" ; ", Totem.LastPops.Select(x => $"died={x.died.x:F1},{x.died.y:F1},{x.died.z:F1} arrive={x.arrive.x:F1},{x.arrive.y:F1},{x.arrive.z:F1}"));
                    Totem.DevNoTotem = a[1] == "0";
                    return "totem " + (Totem.DevNoTotem ? "off" : "on");
                case "tooluse":
                    {
                        // tooluse [n] : (server) the held tool's uses (n: set them, to get it near breaking)
                        if (!(p.currentlyHeldObjectServer is ToolItem t)) return "no tool in hand";
                        if (a.Length > 1) { t.Used = int.Parse(a[1]); BlockNet.ServerToolUses(t, false); }
                        return $"{t.ItemKey} used {t.Used}/{t.MaxUses}";
                    }
                case "rootcheck":
                    if (a.Length > 1) BlockWorld.DevRootCheck = a[1] == "1";
                    return $"check={BlockWorld.DevRootCheck} " + BlockWorld.Instance.DevRootState();
                case "termclosest":
                    TerminalClosestWord.Enabled = a[1] == "1";
                    return "closest word " + TerminalClosestWord.Enabled;
                case "shiprec":
                    {
                        // shiprec <secs> : log every frame: the ship's height, the local player's, grounded, in-ship flags (#37)
                        StartCoroutine(ShipRec(p, float.Parse(a[1])));
                        return "recording";
                    }
                case "shipcarry":
                    if (a.Length > 1 && a[1] == "old") { ShipCarry.DevOldCheck = true; return "shipcarry: the old ray check"; }
                    if (a.Length > 1) { ShipCarry.Enabled = a[1] == "1"; ShipCarry.DevOldCheck = false; }
                    return "shipcarry=" + ShipCarry.Enabled;
                case "rmkey":
                    {
                        // rmkey frame yoff x y z : remove one block from any grid (test cleanup)
                        var k = new BlockKey(byte.Parse(a[1]), short.Parse(a[2]), new Vector3Int(int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5])));
                        if (!BlockWorld.Instance.Has(k)) return "none";
                        BlockNet.ServerBroadcastOp(Op.Remove(k, false));
                        return "removed " + k;
                    }
                case "setname":
                    // setname <name> : (local only) the local player's name, to test name matching ("_" = space)
                    p.playerUsername = string.Join(" ", a.Skip(1)).Replace("_", " ");
                    return p.playerUsername;
                case "chattext":
                    {
                        // chattext <text> : what's in the chat box ("_" = space; the box takes keystrokes as IMGUI events,
                        // which injected input doesn't make); no text: read it
                        var hud = HUDManager.Instance;
                        if (a.Length > 1) hud.chatTextField.text = string.Join(" ", a.Skip(1)).Replace("_", " ");
                        return $"typing={p.isTypingChat} text='{hud.chatTextField.text}' limit={hud.chatTextField.characterLimit} chat='{hud.chatText.text.Replace("\n", " | ")}'";
                    }
                case "typetext":
                    {
                        // typetext <text> : the keyboard's text events, as typing makes them ("_" = space, "\b" = backspace)
                        var kb = UnityEngine.InputSystem.Keyboard.current;
                        if (kb == null) return "no keyboard";
                        string t = string.Join(" ", a.Skip(1)).Replace("_", " ").Replace("\\b", "\b");
                        foreach (char c in t) UnityEngine.InputSystem.InputSystem.QueueTextEvent(kb, c);
                        return "queued " + t.Length;
                    }
                case "gamemode":
                    // gamemode <mode> [player] : the /gamemode chat command, as the local player
                    return Commands.ServerRun(Unity.Netcode.NetworkManager.Singleton.LocalClientId, string.Join(" ", a)) ?? "ok";
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
                case "breakship":
                    {
                        // breakship x y z yoff : break a block on the ship's grid (frame 1; 'near' shows its y offset as yNNN)
                        var key = new BlockKey(1, short.Parse(a[4]), new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        if (!BlockWorld.Instance.Has(key)) return "none at " + key;
                        ServerLogic.HandleBreak(Unity.Netcode.NetworkManager.Singleton.LocalClientId, key, false);
                        return "broke " + key;
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
                        return c + " " + Ground.Describe(c) + " mat=" + (Ground.MaterialOf(c)?.Key ?? "-");
                    }
                case "cellinfo":
                    {
                        var fc = Ground.CellOf(p.transform.position + Vector3.up * 0.2f);
                        var c = fc + new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3]));
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var d = Ground.Describe(c);
                        return c + " " + d + $" ({sw.ElapsedMilliseconds} ms) inside={p.isInsideFactory}";
                    }
                case "clearabs":
                    {
                        // clearabs x1 y1 z1 x2 y2 z2 : (dev) take away the blocks placed in that box of the natural grid (no drops)
                        var w = BlockWorld.Instance;
                        int[] v = a.Skip(1).Take(6).Select(int.Parse).ToArray();
                        var ops = new List<Op>();
                        for (int x = Mathf.Min(v[0], v[3]); x <= Mathf.Max(v[0], v[3]); x++)
                            for (int y = Mathf.Min(v[1], v[4]); y <= Mathf.Max(v[1], v[4]); y++)
                                for (int z = Mathf.Min(v[2], v[5]); z <= Mathf.Max(v[2], v[5]); z++)
                                {
                                    var k = Ground.KeyOf(new Vector3Int(x, y, z));
                                    var b = w.Get(k);
                                    if (b != null && (b.Data.State & Blocks.NaturalGround) == 0) ops.Add(Op.Remove(k, false));
                                }
                        BlockNet.ServerBroadcastOps(ops);
                        return "removed " + ops.Count;
                    }
                case "clearworld":
                    {
                        var w = BlockWorld.Instance;
                        var ops = w.Blocks.Where(kv => kv.Key.Frame == 0 && !kv.Value.Data.Def.DropsScrap && !(kv.Value.Data.Def == Blocks.Stone && kv.Value.Data.State == 1) && (kv.Value.Data.State & Blocks.NaturalGround) == 0)
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
                case "shipnav":
                    // shipnav [off|on] : the navmesh baked over the ship's blocks (#128); off: as before, to compare
                    if (a.Length > 4 && a[1] == "at") return ShipNav.Probe(new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])));
                    if (a.Length > 7 && a[1] == "path")
                    {
                        // shipnav path x y z x y z : from the game's navmesh at the first point to ours at the second
                        var f0 = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = 0, areaMask = ~(1 << ShipNav.Area) };
                        var f1 = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = 0, areaMask = 1 << ShipNav.Area };
                        if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])), out var s0, 3f, f0)) return "no start";
                        if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(float.Parse(a[5]), float.Parse(a[6]), float.Parse(a[7])), out var s1, 1f, f1)) return "no end on ours";
                        var np = new UnityEngine.AI.NavMeshPath();
                        UnityEngine.AI.NavMesh.CalculatePath(s0.position, s1.position, UnityEngine.AI.NavMesh.AllAreas, np);
                        return np.status + " to " + V(s1.position) + ": " + string.Join(" ", np.corners.Select(c => $"{c.x:F2},{c.y:F2},{c.z:F2}"));
                    }
                    if (a.Length > 1 && a[1] == "agents")
                        return string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.Where(x => x != null && x.agent != null).Select(x =>
                            $"{x.enemyType.enemyName} type={x.agent.agentTypeID} mask={x.agent.areaMask} dest={V(x.agent.destination)} end={V(x.agent.pathEndPosition)} status={x.agent.pathStatus} at={V(x.transform.position)}"));
                    if (a.Length > 2 && a[1] == "obstacles")
                    {
                        int k = 0;
                        foreach (var bi in BlockWorld.Instance.Blocks.Values) if (bi.Key.Frame == 1 && bi.Obstacle != null) { bi.Obstacle.enabled = a[2] == "on"; k++; }
                        return "obstacles " + a[2] + " on " + k + " ship blocks";
                    }
                    if (a.Length > 1) { ShipNav.Off = a[1] == "off"; if (ShipNav.Off) ShipNav.Clear(); }
                    return ShipNav.Describe();
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
                case "machinesave":
                    return ShipMachines.DescribeSaved(GameNetworkManager.Instance.currentSaveFileName);
                case "shotgun":
                    {
                        // shotgun : the held shotgun's shells and state
                        var sg = p.currentlyHeldObjectServer as ShotgunItem;
                        return sg == null ? "not holding a shotgun" : $"shells={sg.shellsLoaded} reloading={sg.isReloading} safety={sg.safetyOn}";
                    }
                case "advtoast":
                    return "toasts=" + Advancements.Toast.Shown + $" visible={Advancements.Toast.Visible:F2}";
                case "leave_check":
                    return "landed=" + StartOfRound.Instance.shipHasLanded;
                case "absorb":
                    return "absorbed " + ShipAttach.AbsorbAttachedStructures();
                case "find":
                    {
                        string q = string.Join(" ", a.Skip(1)).ToLower();
                        var list = FindObjectsOfType<GrabbableObject>().Where(o => o.itemProperties != null && o.itemProperties.itemName.ToLower().Contains(q))
                            .Select(o => $"{o.itemProperties.itemName}{(o is StackItem st ? "x" + st.Count : "")}@{V(o.transform.position)} held={o.isHeld} val={o.scrapValue} scan='{o.GetComponentInChildren<ScanNodeProperties>()?.subText}' room={o.isInShipRoom} elev={o.isInElevator} parent={(o.transform.parent != null ? o.transform.parent.name : "-")} inBounds={(StartOfRound.Instance.shipBounds != null && StartOfRound.Instance.shipBounds.bounds.Contains(o.transform.position))} ground={o.hasHitGround}/{o.reachedFloorTarget}");
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
                        // mouse look <dx> <dy> [frames] | mouse left|right [seconds] | mouse wheel <notches> | mouse release
                        var di = DevInput.Instance ?? gameObject.AddComponent<DevInput>();
                        switch (a[1])
                        {
                            case "look": di.Look(new Vector2(float.Parse(a[2]), float.Parse(a[3])), a.Length > 4 ? int.Parse(a[4]) : 10); return "ok";
                            case "left": di.Click(UnityEngine.InputSystem.LowLevel.MouseButton.Left, a.Length > 2 ? float.Parse(a[2]) : 0.08f); return "ok";
                            case "right": di.Click(UnityEngine.InputSystem.LowLevel.MouseButton.Right, a.Length > 2 ? float.Parse(a[2]) : 0.08f); return "ok";
                            case "release": di.ReleaseAll(); return "ok";
                            case "moveto": di.MoveTo(new Vector2(float.Parse(a[2]), float.Parse(a[3]))); return "ok";
                            case "wheel": di.Wheel(int.Parse(a[2])); return "ok"; // mouse wheel <notches> (+ up, - down)
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
                case "carts":
                    // carts : (dev) every minecart: on the track or not, its cell, place along it, speed
                    return string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<MinecartAI>().Where(c => c != null && !c.isEnemyDead).Select(c => c.DevDescribe()));
                case "interact?":
                    {
                        // interact? : (dev) what the game's own interact ray hits (its mask, its reach): every hit in order
                        var cam = p.gameplayCamera.transform;
                        var hits = Physics.RaycastAll(cam.position, cam.forward, p.grabDistance, p.interactableObjectsMask, QueryTriggerInteraction.Collide).OrderBy(h => h.distance);
                        return $"mask={p.interactableObjectsMask} reach={p.grabDistance:F1} : " + string.Join(" ; ", hits.Select(h => $"{h.collider.name}@{LayerMask.LayerToName(h.collider.gameObject.layer)} d={h.distance:F2} trig={h.collider.isTrigger} tag={h.collider.tag}"));
                    }
                case "scroll?":
                    // scroll? : (dev, #168) how many hotbar switches the wheel has made
                    return $"switched={HotbarScroll.Switched}";
                case "mob?":
                    {
                        // mob? : (dev) the mod mob the crosshair is on (what [E] would use), its tip, and whether [E] may act now
                        var am = AnimalCare.Aimed(p);
                        return am == null ? $"none canAct={Builder.CanAct(p)}" : $"{am.enemyType.enemyName} tip={am.UseTip((p.currentlyHeldObjectServer as StackItem)?.ItemKey ?? (p.currentlyHeldObjectServer as ToolItem)?.ItemKey) ?? "-"} canAct={Builder.CanAct(p)} grabbing={p.isGrabbingObjectAnimation}";
                    }
                case "hover":
                    // (a trigger's tip, or a block's: Builder writes those into the cursor tip)
                    return p.hoveringOverTrigger != null ? (p.hoveringOverTrigger.hoverTip ?? "") + " @" + p.hoveringOverTrigger.name
                        : p.cursorTip != null && !string.IsNullOrEmpty(p.cursorTip.text) ? p.cursorTip.text + " @block" : "-";
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
                            if (Ground.IsSolidCell(c)) bad.Add($"{V(nh.position)}{c}[{Ground.DevClassifyFresh(c)}]");
                        }
                        return $"{n} nodes, {bad.Count} in phantom ground" + (bad.Count > 0 ? ": " + string.Join(" ", bad.Take(12)) : "");
                    }
                case "pulses":
                    return Redstone.ObserverPulses.ToString();
                case "flags2":
                    return $"crouching={p.isCrouching} jumping={p.isJumping} grounded={p.thisController.isGrounded} craftOpen={CraftingUI.IsOpen}";
                case "flags":
                    return $"controlled={p.isPlayerControlled} dead={p.isPlayerDead} terminal={p.inTerminalMenu} chat={p.isTypingChat} specialAnim={p.inSpecialInteractAnimation} grabbingAnim={p.isGrabbingObjectAnimation} specialMenu={p.inSpecialMenu} holding={p.isHoldingObject} held={(p.currentlyHeldObjectServer != null ? p.currentlyHeldObjectServer.name : "-")} canAct={Builder.CanAct(p)} craftOpen={CraftingUI.IsOpen} sinking={p.isSinking || p.sourcesCausingSinking > 0} underwater={p.isUnderwater} stamina={p.sprintMeter:0.00} exhausted={p.isExhausted} sprinting={p.isSprinting} hp={p.health} critical={p.criticallyInjured} bleeding={p.bleedingHeavily} step={p.thisController.stepOffset:0.00} stairsNear={StairsStep.Near} ccH={p.thisController.height:0.00} ccR={p.thisController.radius:0.00} grounded={p.thisController.isGrounded} slope={p.thisController.slopeLimit:0}";
                case "jumpinfo":
                    // what the game checks before a jump (Jump_performed)
                    return $"hindered={p.isMovementHindered} ourWater={WaterSwim.BodyIn} underwater={p.isUnderwater} exhausted={p.isExhausted} grounded={p.thisController.isGrounded} jumping={p.isJumping} sliding={p.isPlayerSliding} crouching={p.isCrouching} sinking={p.isSinking} fallValue={p.fallValue:F2} stamina={p.sprintMeter:F2}";
                case "stairstep":
                    StairsStep.DevWant = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    return "stairstep " + StairsStep.DevWant;
                case "hungerjumpfix":
                    Patches.DevHungerJumpFix = a[1] == "1";
                    return "hunger jump fix " + Patches.DevHungerJumpFix;
                case "clearenemies":
                    {
                        // clearenemies [radius [all]] : despawn enemies near the player (tests teleport a god-mode player around:
                        // an enemy latched onto them gets dragged to places it can't path from)
                        float r = a.Length > 1 ? float.Parse(a[1]) : 60f;
                        int n = 0;
                        foreach (var e in RoundManager.Instance.SpawnedEnemies.Where(e => e != null && !e.isEnemyDead).ToList())
                        {
                            if (Vector3.Distance(e.transform.position, p.transform.position) > r) continue;
                            if (!e.enabled && !(a.Length > 2 && a[2] == "all")) continue; // (one held in place by enemyhold for a test; "all": those too)
                            var no = e.GetComponent<Unity.Netcode.NetworkObject>();
                            if (no != null && no.IsSpawned) { no.Despawn(true); n++; }
                        }
                        return "despawned " + n;
                    }
                case "horse":
                    {
                        // horse tame|saddle|wild : the nearest horse tamed, saddled too, or wild again (tests)
                        var h = RoundManager.Instance.SpawnedEnemies.OfType<HorseAI>().Where(x => !x.isEnemyDead).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (h == null) return "no horse";
                        if (a.Length > 1) { h.Tamed = a[1] != "wild"; h.Saddled = a[1] == "saddle"; if (a[1] == "wild") h.Temper = 0; BlockNet.ServerHorse(h, false); }
                        return $"horse tamed={h.Tamed} saddled={h.Saddled} temper={h.Temper} riding={(Riding.Horse != null)} aimed={(HorseAI.Aimed(p) != null)} d={Vector3.Distance(h.transform.position, p.transform.position):F1} canAct={Builder.CanAct(p)}";
                    }
                case "pet":
                    {
                        // pet tame|sit|wild [wolf|cat] : the nearest pet tamed (yours), sitting down / up, or wild again (tests)
                        var kind = a.Length > 2 ? a[2] : "";
                        var pt = RoundManager.Instance.SpawnedEnemies.OfType<PetAI>().Where(x => !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(kind)).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (pt == null) return "no pet";
                        if (a.Length > 1 && a[1] == "tame") pt.ServerTame(p.actualClientId, quiet: true);
                        else if (a.Length > 1 && a[1] == "sit") { pt.Sitting = !pt.Sitting; BlockNet.ServerPet(pt); }
                        else if (a.Length > 1 && a[1] == "wild") { pt.Tamed = false; pt.Owner = PetAI.NoOwner; pt.Sitting = false; BlockNet.ServerPet(pt); }
                        return $"{pt.enemyType.enemyName}@{V(pt.transform.position)} d={Vector3.Distance(pt.transform.position, p.transform.position):F1} tamed={pt.Tamed} owner={(pt.Owner == PetAI.NoOwner ? "-" : pt.Owner.ToString())} sitting={pt.Sitting} outside={pt.isOutside} aimed={(PetAI.Aimed(p) != null)} fled={CreeperAI.FledCats}";
                    }
                case "parrot":
                    {
                        // parrot [mimic 0|1|2] : the nearest parrot (its state), or make it imitate now (0 a monster, 1 a voice, 2 a chirp)
                        var pr = RoundManager.Instance.SpawnedEnemies.OfType<ParrotAI>().Where(x => !x.isEnemyDead).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (a.Length > 2 && a[1] == "mimic" && pr != null)
                        {
                            var mon = RoundManager.Instance.SpawnedEnemies.Where(e => e != null && !e.isEnemyDead && !(e is PetAI) && !(e is MinecraftMobAI mm && mm.IsPassive)).OrderBy(e => Vector3.Distance(e.transform.position, pr.transform.position)).FirstOrDefault();
                            BlockNet.ServerParrotMimic(pr, byte.Parse(a[2]), mon != null ? mon.NetworkObjectId : 0);
                        }
                        if (a.Length > 2 && a[1] == "voices") { Plugin.ParrotVoices.Value = a[2] == "1"; BlockNet.ServerRules(); }
                        if (a.Length > 2 && a[1] == "optout") { Plugin.ParrotsMayCopyMyVoice.Value = a[2] != "1"; BlockNet.RequestVoiceOptOut(a[2] == "1"); }
                        if (a.Length > 1 && a[1] == "feed") return "fed " + ParrotVoices.DevFeed() + " taps; " + ParrotVoices.Describe();
                        if (a.Length > 1 && a[1] == "voicetest")
                        {
                            // (no one can talk into a test: a sound played through each other player's voice playback, as speech would be)
                            var clip = Sounds.Get("zombie.idle");
                            int n = 0; string info = "";
                            foreach (var op in StartOfRound.Instance.allPlayerScripts)
                                if (op != null && op != p && op.isPlayerControlled && op.currentVoiceChatAudioSource != null && clip != null)
                                {
                                    var vs = op.currentVoiceChatAudioSource;
                                    vs.PlayOneShot(clip, 1f); n++;
                                    info += $" [{op.playerUsername}: go={vs.gameObject.name} active={vs.gameObject.activeInHierarchy} enabled={vs.enabled} mute={vs.mute} vol={vs.volume:F2} spatial={vs.spatialBlend:F1} max={vs.maxDistance:F0} filters={string.Join(",", vs.GetComponents<MonoBehaviour>().Select(c => c.GetType().Name))}]";
                                }
                            return "played into " + n + " voice sources" + info + "; " + ParrotVoices.Describe();
                        }
                        return pr == null ? "no parrot; " + ParrotVoices.Describe() : $"Parrot@{V(pr.transform.position)} d={Vector3.Distance(pr.transform.position, p.transform.position):F1} tamed={pr.Tamed} perched={pr.Perched} mimics={ParrotAI.Mimics} last={ParrotAI.LastMimic} agent={pr.agent.enabled} | {ParrotVoices.Describe()}";
                    }
                case "nethermob":
                    {
                        // nethermob ghast|magma4|magma2|magma1|slime4|slime2|slime1|witch [dist] : one of #62's Nether mobs, that far in front of you (and
                        // the counts: ghast shots, fireball blasts, magma splits)
                        if (a.Length > 1)
                        {
                            var et = a[1] == "ghast" ? GhastAI.Type : a[1] == "magma4" ? MagmaCubeAI.Big : a[1] == "magma2" ? MagmaCubeAI.Medium : a[1] == "magma1" ? MagmaCubeAI.Small
                                : a[1] == "witch" ? WitchAI.Type : a[1] == "slime4" ? SlimeAI.Big : a[1] == "slime2" ? SlimeAI.Medium : a[1] == "slime1" ? SlimeAI.Small : null;
                            if (et == null) return "no such mob (or it's off)";
                            float dist = a.Length > 2 ? float.Parse(a[2]) : 4f;
                            var at = p.transform.position + p.transform.forward * dist;
                            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var nh, 4f, UnityEngine.AI.NavMesh.AllAreas)) at = nh.position;
                            MobSpawns.DevSpawnUntil = Time.time + 2f; // (allowed while the tests keep natural spawns off)
                            RoundManager.Instance.SpawnEnemyGameObject(at, p.transform.eulerAngles.y + 180f, -1, et);
                        }
                        return $"ghastShots={GhastAI.Shots} ghastMoves={GhastAI.Repositions} blasts={Fireball.Blasts} splits={MagmaCubeAI.Splits} cubes={RoundManager.Instance.SpawnedEnemies.Count(e => e is MagmaCubeAI && !(e is SlimeAI) && !e.isEnemyDead)} slimes={RoundManager.Instance.SpawnedEnemies.Count(e => e is SlimeAI && !e.isEnemyDead)} witches={RoundManager.Instance.SpawnedEnemies.Count(e => e is WitchAI && !e.isEnemyDead)} witchThrown={WitchAI.Thrown} witchDrinks={WitchAI.Drinks} witchWhy=[{WitchAI.DevWhy}] splashes={WitchPotion.Splashes} splashHits={WitchPotion.Hits} effects={Potions.Describe()} ghasts={RoundManager.Instance.SpawnedEnemies.Count(e => e is GhastAI && !e.isEnemyDead)}";
                    }
                case "fish":
                    {
                        // fish [bite] : the fishing state (and make your bobber's fish bite now: the host's)
                        var fb = Fishing.Of(p.actualClientId);
                        if (a.Length > 1 && a[1] == "bite" && fb != null) Fishing.ServerBite(p.actualClientId);
                        return fb == null ? $"no bobber; catches={Fishing.Catches} last={Fishing.LastCatch}" : $"bobber@{V(fb.transform.position)} floating={fb.Floating} grounded={fb.Grounded} biting={fb.Biting} catches={Fishing.Catches} last={Fishing.LastCatch}";
                    }
                case "advforget":
                    // advforget <id> : (tests) un-make an advancement for you, so it can be made again
                    Advancements.DevForget(p, a[1]);
                    return "forgot " + a[1];
                case "icondump":
                    {
                        // icondump <dir> : (dev) every item's icon as the game has it, a PNG each (read back through the GPU,
                        // so textures that aren't readable work too)
                        var dir = string.Join(" ", a.Skip(1)); Directory.CreateDirectory(dir); int n = 0;
                        foreach (var kv in ModItems.ByKey)
                        {
                            var sp = kv.Value.itemIcon; if (sp == null) continue;
                            var r = sp.rect; int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height); // (the whole icon: textureRect leaves out the see-through border)
                            var rt = RenderTexture.GetTemporary(sp.texture.width, sp.texture.height, 0, RenderTextureFormat.ARGB32);
                            Graphics.Blit(sp.texture, rt);
                            var prev = RenderTexture.active; RenderTexture.active = rt;
                            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                            t.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0); t.Apply();
                            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                            File.WriteAllBytes(Path.Combine(dir, kv.Key + ".png"), t.EncodeToPNG()); Destroy(t); n++;
                        }
                        return "dumped " + n;
                    }
                case "iconredo":
                    {
                        // iconredo <item> [ex ey ez dx dy dz] : (dev) draw its model icon again, turned and seen from there
                        var nums = a.Skip(1).Where(x => float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        var it = CreativeUI.VanillaItem(string.Join(" ", a.Skip(1).Where(x => !float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))));
                        if (nums.Length >= 3) ModelIcons.DevEuler = new Vector3(nums[0], nums[1], nums[2]);
                        if (nums.Length >= 6) ModelIcons.ViewFrom = new Vector3(nums[3], nums[4], nums[5]);
                        ModelIcons.DevRedo(it);
                        return it == null ? "no item" : "redrawing";
                    }
                case "iconinfo":
                    {
                        // iconinfo <game item name> [save path] : (dev) its meshes as the model icon sees them; save its drawn icon
                        var it = CreativeUI.VanillaItem(string.Join(" ", a.Skip(1).TakeWhile(x => !x.Contains("\\") && !x.Contains("/"))));
                        if (it == null) return "no item";
                        var root = it.spawnPrefab.transform;
                        var parts = it.spawnPrefab.GetComponentsInChildren<MeshFilter>(true).Select(mf =>
                        {
                            var mr = mf.GetComponent<MeshRenderer>();
                            var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                            return $"{mf.name} en={(mr != null && mr.enabled)} act={mf.gameObject.activeSelf} mesh={(mf.sharedMesh != null ? mf.sharedMesh.name : "-")} size={(mf.sharedMesh != null ? Vector3.Scale(mf.sharedMesh.bounds.size, m.lossyScale).ToString("F2") : "-")} at={((Vector3)m.GetColumn(3)).ToString("F2")} mats={(mr != null ? string.Join("/", mr.sharedMaterials.Select(x => x != null ? x.name : "null")) : "-")}";
                        });
                        var path = a.FirstOrDefault(x => x.Contains("\\") || x.Contains("/"));
                        var sp = ModelIcons.IconOf(it);
                        if (path != null && sp != null && sp != it.itemIcon) File.WriteAllBytes(path, sp.texture.EncodeToPNG());
                        return $"needs={ModelIcons.Needs(it)} drawn={(sp != null && sp != it.itemIcon)} rest={it.restingRotation} :: " + string.Join(" ; ", parts);
                    }
                case "daytime":
                    { var tod = TimeOfDay.Instance; return tod == null ? "none" : $"time={tod.normalizedTimeOfDay:F4} global={tod.globalTime:F1} speed={tod.globalTimeSpeedMultiplier}"; }
                case "strolls":
                    return "strolls=" + Strolling.Strolls;
                case "spiders":
                    return $"climbs={SpiderAI.Climbs} " + string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<SpiderAI>().Where(x => !x.isEnemyDead).Select(x => $"{V(x.transform.position)} climbing={x.Climbing}"));
                case "fireballs":
                    return Fireball.Describe(p.gameplayCamera.transform.position);
                case "bowpose":
                    // bowpose x y z rx ry rz : (dev) the drawn bow's place in the hand, to tune it by eye
                    Bow.DrawPos = new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3]));
                    Bow.DrawRot = new Vector3(float.Parse(a[4]), float.Parse(a[5]), float.Parse(a[6]));
                    return $"pos={Bow.DrawPos} rot={Bow.DrawRot}";
                case "bows":
                    // bows [on 0|1|-1] [recover 0|1|-1] : (dev) bows on/off and arrow recovery for this session (-1: the config's); the counts
                    for (int i = 1; i + 1 < a.Length; i += 2)
                    {
                        if (a[i] == "on") Bow.DevOn = int.Parse(a[i + 1]);
                        if (a[i] == "recover") Bow.DevRecover = int.Parse(a[i + 1]);
                    }
                    return $"on={Bow.On} recover={Bow.Recover} shots={Bow.Shots} playerHits={Arrow.PlayerHits} recovered={Arrow.Recovered} draw={Bow.DrawTime:F2}";
                case "variants":
                    // variants [desert|snowy|swamp|none|moon] : (dev) what the moon counts as for the variant roll; how many were replaced
                    if (a.Length > 1) Variants.DevMoon = a[1] == "moon" ? null : a[1];
                    return $"moon={Variants.DevMoon ?? "(its name)"} desert={Variants.Desert} snowy={Variants.Snowy} swamp={Variants.Swampy} replaced={Variants.Replaced}";
                case "endermites":
                    // endermites [chance] : (dev) how often a landing pearl brings an endermite (-1: Minecraft's 5%); how many have
                    if (a.Length > 1) EndermiteAI.DevChance = float.Parse(a[1]);
                    return $"chance={(EndermiteAI.DevChance >= 0f ? EndermiteAI.DevChance : 0.05f)} spawned={EndermiteAI.Spawned}";
                case "enemypath":
                    {
                        // enemypath <name> : (dev) the nearest such monster's path to you: status, corners, and where your spot snaps to
                        var e = RoundManager.Instance.SpawnedEnemies.Where(x => x != null && !x.isEnemyDead && x.enemyType.enemyName.ToLower().Contains(a[1].ToLower()))
                            .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (e == null || e.agent == null) return "none";
                        var snap = RoundManager.Instance.GetNavMeshPosition(p.transform.position, RoundManager.Instance.navHit, 3f, e.agentMask);
                        var path = new UnityEngine.AI.NavMeshPath();
                        bool ok = UnityEngine.AI.NavMesh.CalculatePath(e.transform.position, snap, e.agent.areaMask, path);
                        return $"ok={ok} status={path.status} snap={V(snap)} mask={e.agentMask} area={e.agent.areaMask} type={e.agent.agentTypeID} corners=" + string.Join(" ", path.corners.Select(c => V(c))) + $" moving={e.movingTowardsTargetPlayer} special={e.inSpecialAnimation} dest={V(e.destination)}";
                    }
                case "golems":
                    // golems : (dev) golems built, their foes, an iron golem's swings, snowballs that hit
                    return $"built={Golems.Built} swings={IronGolemAI.Swings} snowballHits={Snowball.Hits} : " + string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<GolemAI>().Where(g => !g.isEnemyDead)
                        .Select(g => $"{g.enemyType.enemyName}@{V(g.transform.position)} foe={(g.Foe != null ? g.Foe.enemyType.enemyName : "-")}"));
                case "clock":
                    return PhantomAI.Clock();
                case "book":
                    // book [type text | turn n | sign title | close] : (dev, #160) the book screen; books saved, signed; lectern pulses
                    if (a.Length > 2 && a[1] == "type") BookUI.DevType(string.Join(" ", a.Skip(2)).Replace("_", " "));
                    if (a.Length > 2 && a[1] == "turn") BookUI.DevTurn(int.Parse(a[2]));
                    if (a.Length > 2 && a[1] == "sign") BookUI.DevSign(a[2].Replace("_", " "));
                    if (a.Length > 1 && a[1] == "close") BookUI.Instance?.Close(save: true);
                    return $"saved={Books.Saved} signed={Books.Signed} books={Books.All.Count} pulses={Lecterns.Pulses} ui: {BookUI.DevState()}";
                case "trader":
                    {
                        // trader [spawn | ui | click i] : (dev, #152) a trader and its llamas near you; the trade screen; their state
                        if (a.Length > 1 && a[1] == "spawn" && BlockNet.IsServer) WanderingTraderAI.ServerSpawnNear(p.transform.position);
                        if (a.Length > 2 && a[1] == "click" && TradeUI.Instance != null) TradeUI.Instance.DevClick("offer", int.Parse(a[2]), false, false);
                        var tr = RoundManager.Instance.SpawnedEnemies.OfType<WanderingTraderAI>().Where(t => !t.isEnemyDead).OrderBy(t => Vector3.Distance(t.transform.position, p.transform.position)).FirstOrDefault();
                        var ll = RoundManager.Instance.SpawnedEnemies.OfType<LlamaAI>().Where(l => !l.isEnemyDead).ToList();
                        return $"spawned={WanderingTraderAI.Spawned} trades={WanderingTraderAI.Trades} spits={LlamaAI.Spits} ui={(TradeUI.Instance != null && TradeUI.Instance.DevState().Contains("open=True"))} "
                            + (tr == null ? "no trader" : $"Trader@{V(tr.transform.position)} d={Vector3.Distance(tr.transform.position, p.transform.position):F1} offers=[{string.Join(" ; ", tr.Offers.Select(o => $"{o.GiveN} {o.Give}->{o.GetN} {o.Get} x{o.Uses}"))}]")
                            + " | " + string.Join(" ; ", ll.Select(l => $"Llama@{V(l.transform.position)} d={Vector3.Distance(l.transform.position, p.transform.position):F1} lead={(l.LeashedTo == MinecraftMobAI.TraderLeash ? "trader" : l.LeashedTo == MinecraftMobAI.NoLeash ? "-" : "other")} tamed={l.Tamed} temper={l.Temper} chest={l.HasChest} rider={(l.Rider != LlamaAI.NoRider)}"));
                    }
                case "copper":
                    // copper [chance x] : (dev) weathering chance per half-minute check (-1: the usual); weathered, scraped, rod strikes
                    if (a.Length > 2 && a[1] == "chance") Copper.DevChance = float.Parse(a[2]);
                    return $"weathered={Copper.Weathered} scraped={Copper.Scraped} rod={Copper.RodStrikes} chance={Copper.DevChance}";
                case "transport":
                    // transport : (dev) items hoppers moved, droppers/dispensers fired; a container's contents (transport x y z)
                    if (a.Length > 3)
                    {
                        var ck = Ground.KeyOf(new Vector3Int(int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])));
                        var cc = Chests.Of(ck);
                        return $"{BlockWorld.Instance.DefAt(ck)?.Key}: " + (cc == null ? "empty" : string.Join(",", Enumerable.Range(0, cc.Slots).Select(i => cc.Count[i] > 0 ? $"{cc.Key[i]}x{cc.Count[i]}" : ".")));
                    }
                    return $"moved={ItemTransport.Moved} fired={ItemTransport.Fired}";
                case "vines":
                    {
                        // vines [grow chance | populate] : (dev) vine growth chance per check (-1: the usual), or vines on the trees now
                        if (a.Length > 2 && a[1] == "grow") Vines.DevGrowChance = float.Parse(a[2]);
                        int cols = a.Length > 1 && a[1] == "populate" && BlockNet.IsServer ? Vines.ServerPopulate() : -1;
                        var vs = BlockWorld.Instance.Blocks.Where(kv => kv.Value.Data.Def == Blocks.Vine).ToList();
                        return $"vines={vs.Count} grown={Vines.Grown} placed={Vines.Placed}{(cols >= 0 ? " populated=" + cols : "")} near: "
                            + string.Join(" ; ", vs.OrderBy(kv => Vector3.Distance(BlockWorld.Instance.WorldCenter(kv.Key), p.transform.position)).Take(4).Select(kv => $"{kv.Key.Pos} f{kv.Value.Data.Facing} d={Vector3.Distance(BlockWorld.Instance.WorldCenter(kv.Key), p.transform.position):F1}"));
                    }
                case "phantoms":
                    {
                        // phantoms [spawn | chance x | circle s] : (dev) a phantom over you now, the spawn chance (-1: the config's), or
                        // how long they circle before diving (-1: Minecraft's); their state
                        if (a.Length > 1 && a[1] == "spawn" && BlockNet.IsServer) PhantomAI.ServerSpawnOver(p);
                        if (a.Length > 2 && a[1] == "circle") PhantomAI.DevCircle = float.Parse(a[2]);
                        if (a.Length > 2 && a[1] == "chance") PhantomAI.DevChance = float.Parse(a[2]);
                        return $"spawned={PhantomAI.Spawned} dives={PhantomAI.Dives} screeches={PhantomAI.Screeches} blinded={PhantomAI.Blinded} : " + string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<PhantomAI>().Where(f => !f.isEnemyDead)
                            .Select(f => $"Phantom@{V(f.transform.position)} d={Vector3.Distance(f.transform.position, p.transform.position):F1} {f.State} {f.LightDebug(p)}"));
                    }
                case "care":
                    // care [grow seconds | wool seconds] : (dev) every mod animal's state: baby, lead, wool, horse armor; babies born;
                    // how long a baby takes to grow up, sheared wool to grow back
                    if (a.Length > 2 && a[1] == "grow") AnimalCare.GrowSeconds = float.Parse(a[2]);
                    if (a.Length > 2 && a[1] == "wool") SheepAI.WoolSeconds = float.Parse(a[2]);
                    return $"born={AnimalCare.Born} grow={AnimalCare.GrowSeconds} shorn={SheepAI.Shorn} : " + string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<MinecraftMobAI>().Where(m => m != null && !m.isEnemyDead && m.IsPassive)
                        .Select(m => $"{m.enemyType.enemyName}@{V(m.transform.position)} d={Vector3.Distance(m.transform.position, p.transform.position):F1} baby={m.IsBaby} lead={(m.LeashedTo == MinecraftMobAI.NoLeash ? "-" : m.LeashedTo.ToString())}"
                            + (m is SheepAI sh ? $" wool={SheepAI.Colors[sh.Color]} sheared={sh.Sheared}" : "") + (m is HorseAI ho ? $" armor={ho.HorseArmor} tamed={ho.Tamed} hp={ho.enemyHP}" : "") + (m is ParrotAI pa ? $" dancing={pa.Dancing}" : "")));
                case "squid":
                    // squid [n] : (dev) n squid in each of the moon's water volumes (and how many there are)
                    if (a.Length > 1 && BlockNet.IsServer) SquidAI.ServerPopulate(int.Parse(a[1]));
                    return $"spawned={SquidAI.Spawned} alive={RoundManager.Instance.SpawnedEnemies.Count(e => e is SquidAI && !e.isEnemyDead)} water={UnityEngine.Object.FindObjectsOfType<QuicksandTrigger>().Count(q => q.isWater)}";
                case "lightning":
                    // lightning [dist] : (dev) a strike that far in front of you (the storm's own call: charges creepers, zombifies pigs)
                    {
                        float ld = a.Length > 1 ? float.Parse(a[1]) : 4f;
                        var at = p.transform.position + p.transform.forward * ld;
                        // (the game's own strike needs a storm going; without one, ours alone, a lightning rod drawing it too)
                        if (TimeOfDay.Instance.currentLevelWeather != LevelWeatherType.Stormy) Copper.Redirect(ref at);
                        if (BlockNet.IsServer && TimeOfDay.Instance.currentLevelWeather == LevelWeatherType.Stormy) RoundManager.Instance.LightningStrikeServerRpc(at);
                        else { MobHeads.Strike(at); Sounds.Play("ignite", at, 1f, 0.5f, 40f); }
                        return $"strike at {V(at)} charged={MobHeads.Charged} pigs={MobHeads.PigsZombified} endermites={EndermiteAI.Spawned}";
                    }
                case "spawnweights":
                    {
                        // spawnweights [inside|outside|day] : this moon's spawn list with the weights (how likely each is)
                        var lv = RoundManager.Instance.currentLevel;
                        string which = a.Length > 1 ? a[1] : "outside";
                        var list = which == "inside" ? lv.Enemies : which == "day" ? lv.DaytimeEnemies : lv.OutsideEnemies;
                        int total = list.Sum(e => e.rarity);
                        return $"{lv.PlanetName} {which} total={total}: " + string.Join(", ", list.OrderByDescending(e => e.rarity).Select(e => $"{e.enemyType.enemyName}={e.rarity}(pw{e.enemyType.PowerLevel} max{e.enemyType.MaxCount})"))
                            + $" | outside power max={lv.maxOutsideEnemyPowerCount}";
                    }
                case "obtainable":
                    // obtainable [all] : items with no normal way to get them, recipes stuck on one, and how each of this mod's
                    // mobs gets spawned (the owner's rule: run before a suite)
                    return Obtainable.Report(a.Length > 1 && a[1] == "all");
                case "animparams":
                    {
                        // animparams [name value] : the player body animator's parameters (and set one: a bool or a float)
                        var an = p.playerBodyAnimator;
                        if (an == null) return "no animator";
                        if (a.Length > 2)
                        {
                            var par = an.parameters.FirstOrDefault(x => x.name == a[1]);
                            if (par == null) return "no parameter " + a[1];
                            if (par.type == AnimatorControllerParameterType.Bool) an.SetBool(a[1], a[2] == "1");
                            else if (par.type == AnimatorControllerParameterType.Float) an.SetFloat(a[1], float.Parse(a[2]));
                            else if (par.type == AnimatorControllerParameterType.Trigger) an.SetTrigger(a[1]);
                            else an.SetInteger(a[1], int.Parse(a[2]));
                        }
                        return string.Join(" ", an.parameters.Select(x => x.name + ":" + x.type.ToString()[0]));
                    }
                case "animals":
                    // animals : the farm animals (#95): where, following whom for their food, panicking, speed
                    return string.Join(" ; ", RoundManager.Instance.SpawnedEnemies.OfType<MinecraftMobAI>().Where(x => x is PigAI || x is CowAI || x is HorseAI).Select(x =>
                        $"{x.enemyType.enemyName}@{V(x.transform.position)} d={Vector3.Distance(x.transform.position, p.transform.position):F1} following={(x.Following != null ? x.Following.playerUsername : "-")} panic={x.Panicking}{(x is HorseAI hz ? $" tamed={hz.Tamed} saddled={hz.Saddled} temper={hz.Temper} rider={(hz.Rider == HorseAI.NoRider ? "-" : hz.Rider.ToString())}" : "")} speed={x.agent.speed:F1} v={x.agent.velocity.magnitude:F1} dead={x.isEnemyDead} outside={x.isOutside}"));
                case "enemies":
                    {
                        var list = RoundManager.Instance.SpawnedEnemies.Where(e => e != null).Select(e =>
                            $"{e.enemyType?.enemyName}@{V(e.transform.position)} d={Vector3.Distance(e.transform.position, p.transform.position):F1} v={(e.agent != null ? e.agent.velocity.magnitude : -1):F1} target={(e.targetPlayer != null ? e.targetPlayer.playerUsername : "-")} chase={e.movingTowardsTargetPlayer} state={e.currentBehaviourStateIndex} path={(e.agent != null ? e.agent.pathStatus.ToString() : "-")} onNav={(e.agent != null && e.agent.enabled ? e.agent.isOnNavMesh.ToString() : "-")} dead={e.isEnemyDead} hp={e.enemyHP} dest={(e.agent != null && e.agent.enabled ? V(e.agent.destination) : "-")} rem={(e.agent != null && e.agent.enabled && e.agent.isOnNavMesh ? e.agent.remainingDistance : -1):F1} stopped={(e.agent != null && e.agent.enabled && e.agent.isOnNavMesh && e.agent.isStopped)} spd={(e.agent != null ? e.agent.speed : -1):F1}{(e is MinecraftMobAI mm ? " tinted=" + mm.HurtTinted + "/" + mm.GetComponentsInChildren<MeshRenderer>().Length : "")}");
                        return string.Join(" ; ", list);
                    }
                case "time":
                    Time.timeScale = float.Parse(a[1]);
                    return "ok";
                case "speed":
                    {
                        // speed <x> : run the game x times faster (tests: tools/timing.py sleeps in game time); above 1 the
                        // frame rate is uncapped too (more frames: game commands answer sooner, physics keeps its step)
                        float x = Mathf.Clamp(float.Parse(a[1]), 0.1f, 20f);
                        if (x > 1f && savedFrameCap == int.MinValue) { savedFrameCap = Application.targetFrameRate; savedVSync = QualitySettings.vSyncCount; }
                        if (x > 1f) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
                        else if (savedFrameCap != int.MinValue) { Application.targetFrameRate = savedFrameCap; QualitySettings.vSyncCount = savedVSync; savedFrameCap = int.MinValue; }
                        Time.timeScale = x;
                        // (a slow frame may cover up to this much game time; more would make physics skip)
                        Time.maximumDeltaTime = Mathf.Max(0.3333f, 0.1f * x);
                        return $"speed={Time.timeScale} fps cap={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} maxDelta={Time.maximumDeltaTime}";
                    }
                case "biginv":
                    // biginv 1|0 : the big inventory on/off for this session (the config file isn't touched); "biginv" alone: my storage
                    if (a.Length > 1) { Storage.DevForce = a[1] == "1"; Storage.ServerResendAll(); }
                    return Storage.DevDescribe();
                case "raycheck":
                    // raycheck [n] [radius] : the fast carved-mesh ray test against testing every triangle, n random rays per
                    // carved object around the player (dig or blow something up first so there are carved objects)
                    return TerrainCarver.DevCheckRays(p.transform.position, a.Length > 2 ? float.Parse(a[2]) : 20f, a.Length > 1 ? int.Parse(a[1]) : 2000);
                case "classifydump":
                    {
                        // classifydump <file> [nodes] : fresh classification of every cell in a 5x5x5 box around the first n inside
                        // and outside AI nodes, written to a file (the same seed on two builds must give the same file)
                        int nn = a.Length > 2 ? int.Parse(a[2]) : 40;
                        var lines = new List<string>();
                        foreach (var nodes in new[] { RoundManager.Instance.insideAINodes, RoundManager.Instance.outsideAINodes })
                        {
                            if (nodes == null) continue;
                            foreach (var nd in nodes.Where(x => x != null).OrderBy(x => x.transform.position.x).ThenBy(x => x.transform.position.z).Take(nn))
                            {
                                var c0 = Ground.CellOf(nd.transform.position);
                                for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) for (int dz = -2; dz <= 2; dz++)
                                    lines.Add(Ground.DevFingerprint(c0 + new Vector3Int(dx, dy, dz)));
                            }
                        }
                        File.WriteAllLines(a[1], lines);
                        return $"{lines.Count} cells -> {a[1]} (solid {lines.Count(l => l.Contains(" Solid "))}, partial {lines.Count(l => l.Contains(" Partial "))})";
                    }
                case "seed":
                    {
                        // seed <n>|off : the next landings use this map seed (the same facility layout, to compare builds)
                        var sor = StartOfRound.Instance;
                        if (a.Length > 1 && a[1] == "off") sor.overrideRandomSeed = false;
                        else if (a.Length > 1) { sor.overrideRandomSeed = true; sor.overrideSeedNumber = int.Parse(a[1]); }
                        return $"override={sor.overrideRandomSeed} seed={sor.overrideSeedNumber} current={sor.randomMapSeed}";
                    }
                case "unsink":
                    {
                        // unsink : stop sinking (quicksand, water). The game only stops it when you walk out of the trigger, so a
                        // teleport out of a lake left the player "sinking" everywhere after; real quicksand starts it again
                        int n = 0;
                        foreach (var q in FindObjectsOfType<QuicksandTrigger>()) if (q.sinkingLocalPlayer) { q.StopSinkingLocalPlayer(p); n++; }
                        // (the player's own counters can be out of step with the triggers: start clean)
                        p.sourcesCausingSinking = 0; p.isSinking = false; p.sinkingValue = 0f;
                        p.isMovementHindered = 0; p.hinderedMultiplier = 1f; p.isUnderwater = false;
                        return $"stopped {n}; sinking={p.isSinking} sources={p.sourcesCausingSinking} underwater={p.isUnderwater}";
                    }
                case "batch":
                    // batch cmd1 ;; cmd2 ;; ... : several commands in one frame (each costs a frame on its own); the replies
                    // come back joined by " <<>> "
                    return string.Join(" <<>> ", string.Join(" ", a.Skip(1)).Split(new[] { " ;; " }, StringSplitOptions.None)
                        .Select(c => { try { return Exec(c.Trim()); } catch (Exception e) { return "ERR " + e.Message; } }));
                case "res":
                    {
                        // res <w> <h> : render at this resolution, same window mode (test launches: 1280x720 keeps the GPU from
                        // setting the pace of a sped-up run)
                        Screen.SetResolution(int.Parse(a[1]), int.Parse(a[2]), Screen.fullScreenMode);
                        return $"res {a[1]}x{a[2]} {Screen.fullScreenMode}";
                    }
                case "fpsinfo":
                    // fpsinfo [target] : what limits the frame rate (optionally set Application.targetFrameRate)
                    if (a.Length > 1) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = int.Parse(a[1]); }
                    return $"target={Application.targetFrameRate} vsync={QualitySettings.vSyncCount} refresh={Screen.currentResolution.refreshRateRatio.value:F0} focused={Application.isFocused} " +
                        $"runInBackground={Application.runInBackground} fullscreen={Screen.fullScreenMode} res={Screen.width}x{Screen.height} gpu='{SystemInfo.graphicsDeviceName}' frame={(frameTimes.Count > 0 ? frameTimes.Average() * 1000f : 0f):F1}ms";
                case "gtime":
                    // gtime : game time, real time, speed, and the average frame time over the last 120 frames
                    return $"{Time.time:F4} {Time.realtimeSinceStartup:F4} {Time.timeScale} {(frameTimes.Count > 0 ? frameTimes.Average() * 1000f : 0f):F1}ms";
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

        /// <summary>(dev) What made the ship leave: suites saw it take off mid-run, early in the day, with the player alive.</summary>
        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ShipLeave)), HarmonyLib.HarmonyPrefix]
        static void Leaving(StartOfRound __instance)
        {
            if (!Plugin.DevMode.Value) return;
            var tod = TimeOfDay.Instance;
            Plugin.Log.LogWarning($"[dev] ShipLeave: time={(tod != null ? tod.normalizedTimeOfDay : -1f):F3} allDead={__instance.allPlayersDead} living={__instance.livingPlayers}\n{System.Environment.StackTrace}");
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

namespace LethalMinecraft
{
    /// <summary>(dev) "soundlog 1": logs every one-shot sound played, to check what plays when (no effect otherwise).</summary>
    [HarmonyLib.HarmonyPatch]
    static class DevSoundLog
    {
        public static bool On;
        [HarmonyLib.HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) }), HarmonyLib.HarmonyPostfix]
        static void Played(AudioSource __instance, AudioClip clip)
        {
            if (On && clip != null) Plugin.Log.LogInfo($"[dev] sound {clip.name} on {__instance.gameObject.name}");
        }

        [HarmonyLib.HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new System.Type[0]), HarmonyLib.HarmonyPostfix]
        static void PlayedClip(AudioSource __instance)
        {
            if (On && __instance.clip != null) Plugin.Log.LogInfo($"[dev] sound {__instance.clip.name} on {__instance.gameObject.name} (play)");
        }
    }

    /// <summary>(dev) The interior a landing makes, whatever the moon's weights say (#64: the mineshaft's pipes).</summary>
    [HarmonyLib.HarmonyPatch(typeof(RoundManager), nameof(RoundManager.GenerateNewFloor))]
    static class DevInterior
    {
        public static int Force = -1;
        static IntWithRarity[] saved;

        static void Prefix(RoundManager __instance)
        {
            saved = null;
            if (Force < 0 || !Plugin.DevMode.Value || __instance.currentLevel == null) return;
            saved = __instance.currentLevel.dungeonFlowTypes;
            __instance.currentLevel.dungeonFlowTypes = new[] { new IntWithRarity(Force, 1, null) };
        }

        static void Postfix(RoundManager __instance)
        {
            if (saved != null && __instance.currentLevel != null) __instance.currentLevel.dungeonFlowTypes = saved;
            saved = null;
        }
    }
}
