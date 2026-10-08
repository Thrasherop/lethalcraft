using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (dev) A tiny profiler for finding where the host's time goes (explosions): named, inclusive timers around the mod's
    /// own work, plus every frame's length and the garbage collections, so what isn't ours (physics, navmesh carving,
    /// rendering) shows up as the frame time the timers don't account for. Off unless "prof on".
    /// </summary>
    public static class Prof
    {
        public static bool On;
        static readonly Dictionary<string, (double ms, int n, double max)> acc = new Dictionary<string, (double, int, double)>();
        static readonly List<(float t, float ms)> frames = new List<(float, float)>();
        static int gc0;
        static float startT;

        public struct Scope : IDisposable
        {
            readonly string name; readonly long t0;
            public Scope(string name) { this.name = name; t0 = Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                if (name == null) return;
                double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                acc.TryGetValue(name, out var e);
                acc[name] = (e.ms + ms, e.n + 1, Math.Max(e.max, ms));
            }
        }

        public static void Add(string name, double ms)
        {
            acc.TryGetValue(name, out var e);
            acc[name] = (e.ms + ms, e.n + 1, Math.Max(e.max, ms));
        }

        /// <summary>using (Prof.S("name")) { ... }: free when profiling is off.</summary>
        public static Scope S(string name) => On ? new Scope(name) : default;

        public static void Start() { acc.Clear(); frames.Clear(); gc0 = GC.CollectionCount(0); startT = Time.realtimeSinceStartup; On = true; }

        public static void Frame()
        {
            if (On) frames.Add((Time.realtimeSinceStartup - startT, Time.unscaledDeltaTime * 1000f));
        }

        public static string Dump()
        {
            var sb = new System.Text.StringBuilder();
            if (frames.Count > 0)
            {
                var ms = frames.Select(f => f.ms).OrderBy(x => x).ToList();
                float total = ms.Sum();
                sb.Append($"frames={frames.Count} over {total / 1000f:F1}s avg={ms.Average():F1}ms p50={ms[ms.Count / 2]:F1} p95={ms[(int)(ms.Count * 0.95f)]:F1} p99={ms[(int)(ms.Count * 0.99f)]:F1} max={ms[ms.Count - 1]:F1}ms; ");
                sb.Append($"frames over 33ms: {ms.Count(x => x > 33f)}, over 100ms: {ms.Count(x => x > 100f)}; gc0={GC.CollectionCount(0) - gc0} | ");
                // the 5 worst frames, with when they happened
                sb.Append("worst: " + string.Join(",", frames.OrderByDescending(f => f.ms).Take(5).Select(f => $"{f.ms:F0}ms@{f.t:F2}s")) + " | ");
            }
            foreach (var kv in acc.OrderByDescending(kv => kv.Value.ms))
                sb.Append($"{kv.Key}={kv.Value.ms:F1}ms/{kv.Value.n}x(max {kv.Value.max:F1}) ; ");
            return sb.ToString();
        }
    }
}

namespace LethalMinecraft
{
    /// <summary>(dev) The timers, attached with Harmony to the methods an explosion runs through (and the per-frame ticks).</summary>
    [HarmonyLib.HarmonyPatch]
    static class ProfHooks
    {
        static readonly (Type type, string name, Type[] args)[] Targets =
        {
            (typeof(ServerLogic), "ExplodeBlocks", null),
            (typeof(ServerLogic), "SpawnDrop", null),
            (typeof(Ground), "Explode", null),
            (typeof(Ground), "OpenMany", null),
            (typeof(Ground), "Ensure", null),
            (typeof(Ground), "PopUnsupported", null),
            (typeof(Ground), "Classify", null),
            (typeof(Ground), "IsSolid", null),
            (typeof(Ground), "BedrockReason", null),
            (typeof(BlockWorld), "Apply", new[] { typeof(List<Op>) }),
            (typeof(BlockWorld), "Apply", new[] { typeof(Op) }),
            (typeof(BlockWorld), "CreateInstance", null),
            (typeof(BlockWorld), "DestroyInstance", null),
            (typeof(BlockWorld), "UpdateVisual", null),
            (typeof(BlockWorld), "RefreshMold", null),
            (typeof(BlockWorld), "UpdateNearLights", null),
            (typeof(BlockWorld), "Update", null),
            (typeof(TerrainCarver), "Apply", null),
            (typeof(TerrainCarver), "RebuildChunk", null),
            (typeof(TerrainCarver), "ApplyTerrain", null),
            (typeof(BlockNet), "ServerBroadcastOps", null),
            (typeof(BlockNet), "ServerMolds", null),
            (typeof(BlockNet), "ServerCut", null),
            (typeof(Redstone), "ServerTick", null),
            (typeof(Redstone), "Recompute", null),
            (typeof(Gravity), "Tick", null),
            (typeof(ItemGravity), "Tick", null),
            (typeof(StackMerging), "Tick", null),
            (typeof(Chests), "ServerDropContents", null),
            (typeof(Fire), "ServerTick", null),
            (typeof(TerrainCarver), "RayOriginal", null),
            (typeof(TerrainCarver), "RayCarved", null),
            (typeof(TerrainCarver), "RayTerrain", null),
            (typeof(TerrainCarver), "Excluded", null),
            (typeof(TerrainCarver), "PlainFloorBelow", null),
            (typeof(Ground), "WalkableAir", null),
            (typeof(TerrainCarver), "WriteRenderMesh", null),
            (typeof(TerrainCarver), "AddHoleObstacle", null),
            (typeof(TerrainCarver), "Prepare", null),
            // vanilla, per item (what dropped items cost every frame)
            (typeof(GrabbableObject), "Update", null),
            (typeof(GrabbableObject), "LateUpdate", null),
        };

        static bool Prepare() => Plugin.DevMode.Value; // (only in dev mode)

        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var (type, name, args) in Targets)
            {
                var m = args != null ? HarmonyLib.AccessTools.Method(type, name, args) : HarmonyLib.AccessTools.Method(type, name);
                if (m != null) yield return m;
                else Plugin.Log.LogWarning($"[prof] no method {type.Name}.{name}");
            }
        }

        static void Prefix(out long __state) => __state = Prof.On ? Stopwatch.GetTimestamp() : 0;

        static void Postfix(long __state, System.Reflection.MethodBase __originalMethod)
        {
            if (__state == 0 || !Prof.On) return;
            Prof.Add(__originalMethod.DeclaringType.Name + "." + __originalMethod.Name + (__originalMethod.GetParameters().Length == 1 && __originalMethod.GetParameters()[0].ParameterType == typeof(Op) ? "(op)" : ""),
                (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency);
        }
    }
}
