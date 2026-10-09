using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Knows where the generated facility is, and keeps "inside the facility" state right when a player walks in or out
    /// through a dug tunnel instead of the main entrance / fire exits (lighting, audio, enemy targeting, item state).
    /// Only acts once something has been dug, so vanilla behaviour is untouched otherwise.
    /// </summary>
    public static class Facility
    {
        static List<Bounds> tiles;
        static Bounds all;
        static bool haveAll;
        static Transform root;
        static List<Transform> entrances;
        static float nextTick;

        public static void Reset() { tiles = null; haveAll = false; root = null; entrances = null; }

        static void Build()
        {
            if (tiles != null) return;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase) return;
            tiles = new List<Bounds>();
            try
            {
                var gen = RoundManager.Instance?.dungeonGenerator;
                root = gen != null && gen.Root != null ? gen.Root.transform : null;
                var dungeon = gen?.Generator?.CurrentDungeon;
                if (dungeon != null)
                    foreach (var t in dungeon.AllTiles)
                        if (t != null) tiles.Add(t.Bounds);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Facility bounds: " + e.Message); }
            haveAll = tiles.Count > 0;
            if (haveAll)
            {
                all = tiles[0];
                foreach (var b in tiles) all.Encapsulate(b);
            }
        }

        public static bool Bounds(out Bounds b)
        {
            Build();
            b = all;
            return haveAll;
        }

        public static bool Contains(GameObject go)
        {
            Build();
            return root != null && go != null && go.transform.IsChildOf(root);
        }

        /// <summary>Entrance/exit teleport points (kept clear of digging).</summary>
        public static List<Transform> Entrances()
        {
            if (entrances == null || entrances.Any(t => t == null))
                entrances = Object.FindObjectsOfType<EntranceTeleport>().Where(e => e.entrancePoint != null).Select(e => e.entrancePoint).ToList();
            return entrances;
        }

        public static bool DebugInTile(Vector3 p) { Build(); return tiles != null && InTile(p); }

        static bool InTile(Vector3 p)
        {
            foreach (var b in tiles)
            {
                var e = b; e.Expand(1.5f); // doorways sit on tile boundaries
                if (e.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>Local player: called every frame by Builder.</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + 0.25f;
            if (p == null || p.isPlayerDead || !p.isPlayerControlled || TerrainCarver.Cuts.Count == 0) return;
            var sor = StartOfRound.Instance;
            if (sor == null || sor.inShipPhase) return;
            if (sor.shipBounds != null && sor.shipBounds.bounds.Contains(p.transform.position)) return;
            Build();
            if (!haveAll) return;
            var pos = p.transform.position;
            bool want;
            if (!p.isInsideFactory)
            {
                // anywhere inside the facility's overall box is underground next to it (tile bounds miss some rooms)
                var box = all; box.Expand(1f);
                want = box.Contains(pos) || InTile(pos);
            }
            else
            {
                // leave only when clearly out of the building (dug up above it or far to the side)
                var far = all; far.Expand(new Vector3(16f, 0f, 16f));
                want = pos.y < all.max.y + 3f && far.Contains(new Vector3(pos.x, all.center.y, pos.z));
            }
            if (want == p.isInsideFactory) return;
            Plugin.Log.LogInfo($"Tunnel: local player is now {(want ? "inside" : "outside")} the facility");
            SetLocalInside(p, want);
        }

        /// <summary>Local player: now inside the facility (or out), as an entrance would set it: flags, the server told,
        /// the matching reverb.</summary>
        public static void SetLocalInside(PlayerControllerB p, bool want)
        {
            Apply(p, want);
            BlockNet.RequestInside(want);
            // reverb like the matching entrance would set
            try
            {
                var tp = Object.FindObjectsOfType<EntranceTeleport>().FirstOrDefault(e => e.isEntranceToBuilding == want && e.audioReverbPreset != -1);
                var presets = Object.FindObjectOfType<AudioReverbPresets>();
                if (tp != null && presets != null && tp.audioReverbPreset < presets.audioPresets.Length)
                    presets.audioPresets[tp.audioReverbPreset].ChangeAudioReverbForPlayer(p);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Tunnel reverb: " + e.Message); }
        }

        static void Apply(PlayerControllerB p, bool inside)
        {
            p.isInsideFactory = inside;
            p.isInElevator = false;
            p.isInHangarShipRoom = false;
            foreach (var it in p.ItemSlots) if (it != null) it.isInFactory = inside;
            if (p.ItemOnlySlot != null) p.ItemOnlySlot.isInFactory = inside;
        }

        public static void ApplyRemote(int playerIdx, bool inside)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || playerIdx < 0 || playerIdx >= sor.allPlayerScripts.Length) return;
            var p = sor.allPlayerScripts[playerIdx];
            if (p == null || p == GameNetworkManager.Instance.localPlayerController) return;
            Apply(p, inside);
        }
    }
}
