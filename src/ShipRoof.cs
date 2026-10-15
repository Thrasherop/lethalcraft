using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Digging the ship's roof and its antenna (the light post on top), #124: off unless the host turns it on
    /// (Blocks.AllowDiggingShipRoof). Only while the ship is landed, and only the hull above the room's ceiling: its
    /// walls and floor stay. What's dug is ship plating, which drops nothing. What's dug stays dug (#179): through orbit,
    /// to other moons and in the save, kept as boxes in the ship's own space and cut again wherever the ship is.
    /// </summary>
    [HarmonyPatch]
    public static class ShipRoof
    {
        /// <summary>The host's rule (clients get it with the other rules).</summary>
        public static bool HostAllows;
        public static bool On => BlockNet.IsServer ? Plugin.DigShipRoof.Value : HostAllows;

        static bool Landed
        {
            get { var s = StartOfRound.Instance; return s != null && !s.inShipPhase && s.shipHasLanded && !s.shipIsLeaving; }
        }

        /// <summary>The hull (its outside is the roof) or a piece of the light post on top of it.</summary>
        public static bool IsPart(GameObject go)
        {
            var sor = StartOfRound.Instance;
            if (go == null || sor == null || sor.elevatorTransform == null || !go.transform.IsChildOf(sor.elevatorTransform)) return false;
            if (go.name == "ShipInside" && go.GetComponent<MeshCollider>() != null) return true;
            for (var t = go.transform; t != null && t != sor.elevatorTransform; t = t.parent)
                if (t.name == "ShipLightsPost") return true;
            return false;
        }

        public static bool Diggable(GameObject go) => On && Landed && IsPart(go);

        /// <summary>The light post's pieces with nothing to collide (its lamps and fittings), while digging there is on.</summary>
        public static IEnumerable<MeshRenderer> Fittings()
        {
            if (!On || !Landed || Post == null) yield break;
            foreach (var r in Post.GetComponentsInChildren<MeshRenderer>())
                if (r.GetComponent<Collider>() == null) yield return r;
        }

        public static IEnumerable<MeshCollider> PostColliders() => Post != null ? Post.GetComponentsInChildren<MeshCollider>(true) : Enumerable.Empty<MeshCollider>();

        static Transform post;
        static Transform Post
        {
            get
            {
                var sor = StartOfRound.Instance;
                if (post == null && sor != null && sor.elevatorTransform != null)
                    post = sor.elevatorTransform.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "ShipLightsPost");
                return post;
            }
        }

        /// <summary>The ship's room (its ceiling's underside is the top, about 4.5 m over the floor).</summary>
        static Bounds Room()
        {
            var sor = StartOfRound.Instance;
            var b = sor.shipInnerRoomBounds != null ? sor.shipInnerRoomBounds.bounds : default;
            if (b.size.y > 1f) return b;
            var e = sor.elevatorTransform.position; // (fallback: the room's place on a landed ship)
            return new Bounds(e + new Vector3(1.4f, 2.3f, -6.7f), new Vector3(17.5f, 5.3f, 7f));
        }

        /// <summary>
        /// A cell of a ship part that may go: the light post anywhere; the hull only over the room: above its ceiling
        /// and, where the cell reaches down past the ceiling, over the room's floor (the walls and door frame stay whole).
        /// </summary>
        public static bool CellOk(GameObject go, Vector3 center)
        {
            if (go == null || go.name != "ShipInside") return true;
            var room = Room(); float h = Plugin.S * 0.5f;
            if (center.y - h >= room.max.y - 0.05f) return true;
            if (center.y <= room.max.y) return false;
            return center.x - h >= room.min.x && center.x + h <= room.max.x && center.z - h >= room.min.z && center.z + h <= room.max.z;
        }

        // ------------------------------------------------------------------ what's dug stays dug (#179)
        // (the digging cuts boxes in the world, the moon's grid; the ship moves, so they're kept in the ship's own space,
        // and cut again at take-off and landing, where the ship is then: the carver's world positions stay true)
        public static readonly Dictionary<string, List<(Vector3 min, Vector3 max)>> LocalCuts = new Dictionary<string, List<(Vector3, Vector3)>>();
        static int folded; // (TerrainCarver.Cuts already taken in)

        /// <summary>The ship's cuts made since the last time, into its own space.</summary>
        static void Fold()
        {
            var ship = StartOfRound.Instance?.elevatorTransform;
            if (ship == null) return;
            var cuts = TerrainCarver.Cuts;
            if (folded > cuts.Count) folded = 0; // (the moon's cuts were cleared)
            for (int i = folded; i < cuts.Count; i++)
            {
                var c = cuts[i];
                var go = TerrainCarver.FindByPath(c.Path);
                if (go == null || !IsPart(go) || c.Mins == null) continue;
                if (!LocalCuts.TryGetValue(c.Path, out var list)) LocalCuts[c.Path] = list = new List<(Vector3, Vector3)>();
                for (int k = 0; k < c.Mins.Length; k++)
                {
                    Vector3 a = ship.InverseTransformPoint(c.Mins[k]), b = ship.InverseTransformPoint(c.Maxs[k]);
                    list.Add((Vector3.Min(a, b), Vector3.Max(a, b)));
                }
            }
            folded = cuts.Count;
        }

        /// <summary>The hull put back whole, then the kept cuts made again where the ship is now.</summary>
        public static void Rebuild()
        {
            var ship = StartOfRound.Instance?.elevatorTransform;
            if (ship == null) return;
            TerrainCarver.RestoreShip();
            foreach (var kv in LocalCuts)
            {
                if (kv.Value.Count == 0 || TerrainCarver.FindByPath(kv.Key) == null) continue;
                var mins = new Vector3[kv.Value.Count]; var maxs = new Vector3[kv.Value.Count];
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    Vector3 a = ship.TransformPoint(kv.Value[i].min), b = ship.TransformPoint(kv.Value[i].max);
                    mins[i] = Vector3.Min(a, b); maxs[i] = Vector3.Max(a, b);
                }
                TerrainCarver.Apply(new TerrainCarver.Cut { Path = kv.Key, Mins = mins, Maxs = maxs }, record: false);
            }
        }

        [HarmonyPatch(typeof(StartOfRound), "ShipLeave"), HarmonyPostfix]
        static void OnShipLeave()
        {
            try
            {
                if (BlockNet.IsServer && BlockWorld.Instance != null)
                {
                    // (the plating blocks the digging left behind: in the moon's frame, they'd hang in the air)
                    var ops = BlockWorld.Instance.Blocks.Values.Where(b => b.Key.Frame == 0 && b.Data.Def == Blocks.ShipPlating)
                        .Select(b => Op.Remove(b.Key, false)).ToList();
                    if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
                }
                Fold();
                Rebuild();
                int n = LocalCuts.Values.Sum(l => l.Count);
                if (n > 0) Plugin.Log.LogInfo($"[ship] the dug roof kept ({n} cells)");
            }
            catch (System.Exception e) { Plugin.Log.LogError("Ship roof: " + e); }
        }

        [HarmonyPatch(typeof(StartOfRound), "OnShipLandedMiscEvents"), HarmonyPostfix]
        static void OnLanded()
        {
            try { folded = TerrainCarver.Cuts.Count; if (LocalCuts.Count > 0) Rebuild(); }
            catch (System.Exception e) { Plugin.Log.LogError("Ship roof (landing): " + e); }
        }

        // the save, and late joiners (with the rules)
        const string SaveKey = "LMC_ShipRoof_v1";
        public static void Save(string file)
        {
            Fold();
            var flat = LocalCuts.ToDictionary(kv => kv.Key, kv => kv.Value.Select(b => new[] { b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z }).ToList());
            ES3.Save(SaveKey, Newtonsoft.Json.JsonConvert.SerializeObject(flat), file);
        }

        public static void Load(string file)
        {
            LocalCuts.Clear(); folded = TerrainCarver.Cuts.Count;
            if (!ES3.KeyExists(SaveKey, file)) return;
            var flat = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<float[]>>>(ES3.Load<string>(SaveKey, file));
            foreach (var kv in flat) LocalCuts[kv.Key] = kv.Value.Select(f => (new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5]))).ToList();
            Rebuild();
            Plugin.Log.LogInfo($"[ship] the dug roof loaded ({LocalCuts.Values.Sum(l => l.Count)} cells)");
        }

        public static void Clear() { LocalCuts.Clear(); folded = TerrainCarver.Cuts.Count; TerrainCarver.RestoreShip(); }

        public static void Write(ref Unity.Netcode.FastBufferWriter w)
        {
            Fold();
            w.WriteValueSafe(LocalCuts.Count);
            foreach (var kv in LocalCuts)
            {
                w.WriteValueSafe(kv.Key); w.WriteValueSafe(kv.Value.Count);
                foreach (var (mn, mx) in kv.Value) { w.WriteValueSafe(mn); w.WriteValueSafe(mx); }
            }
        }

        public static void Read(ref Unity.Netcode.FastBufferReader r)
        {
            r.ReadValueSafe(out int n);
            var got = new Dictionary<string, List<(Vector3, Vector3)>>();
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out string path); r.ReadValueSafe(out int m);
                var list = new List<(Vector3, Vector3)>();
                for (int j = 0; j < m; j++) { r.ReadValueSafe(out Vector3 mn); r.ReadValueSafe(out Vector3 mx); list.Add((mn, mx)); }
                got[path] = list;
            }
            if (BlockNet.IsServer) return;
            bool differs = got.Count != LocalCuts.Count || got.Any(kv => !LocalCuts.TryGetValue(kv.Key, out var l) || l.Count != kv.Value.Count);
            LocalCuts.Clear(); foreach (var kv in got) LocalCuts[kv.Key] = kv.Value;
            folded = TerrainCarver.Cuts.Count;
            if (differs) Rebuild();
        }
    }
}
