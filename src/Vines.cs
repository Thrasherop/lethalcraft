using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's vines (#158): a thin green layer on a wall, climbed like a ladder ([E]). Only shears take one (by hand
    /// or another tool it's just gone). A vine with open air under it grows down now and then, a column at most
    /// <see cref="MaxLength"/> long. Natural ones hang on the trunks of the moons' trees (see <see cref="ServerPopulate"/>).
    /// A vine with cobblestone or stone bricks makes the mossy kind.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class Vines
    {
        public const int MaxLength = 8;
        public static int Grown, Placed; // (dev/tests)
        public static float DevGrowChance = -1f; // (dev/tests: the chance per vine per check; -1 = the usual)
        static float nextGrow;

        /// <summary>Host, each frame: every 15 s, each vine end with air under it may grow one down (1 in 8, Minecraft's pace
        /// is about one block in a few minutes).</summary>
        public static void ServerTick(BlockWorld world)
        {
            if (!BlockNet.IsServer || Blocks.Vine == null || Time.time < nextGrow) return;
            nextGrow = Time.time + 15f;
            float chance = DevGrowChance >= 0f ? DevGrowChance : 0.125f;
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                if (kv.Value.Data.Def != Blocks.Vine) continue;
                var below = kv.Key.Offset((int)Face.Down);
                if (world.Has(below) || Random.value >= chance || ServerLogic.Obstructed(below, 0.6f)) continue;
                // (not past MaxLength: count the column up from here)
                int len = 1; var up = kv.Key.Offset((int)Face.Up);
                while (len <= MaxLength && world.Get(up)?.Data.Def == Blocks.Vine) { len++; up = up.Offset((int)Face.Up); }
                if (len >= MaxLength) continue;
                ops.Add(Op.Set(below, new BlockData(Blocks.Vine.Id, kv.Value.Data.Facing)));
            }
            if (ops.Count == 0) return;
            Grown += ops.Count;
            BlockNet.ServerBroadcastOps(ops);
        }

        /// <summary>Host, after landing: vines down the side of some of the moon's tree trunks (outside, near enough the
        /// ship to matter). Returns how many columns.</summary>
        public static int ServerPopulate(int maxColumns = 12)
        {
            var world = BlockWorld.Instance;
            if (world == null || Blocks.Vine == null || !world.WorldFrameAvailable) return 0;
            float S = BlockWorld.S;
            int columns = 0;
            var ops = new List<Op>();
            var ship = StartOfRound.Instance.elevatorTransform.position;
            // (the game's trees: tall thin colliders named for trees, out on the moon)
            var trunks = Object.FindObjectsOfType<Collider>()
                .Where(c => c != null && c.enabled && !c.isTrigger && IsTree(c.transform) && c.bounds.size.y > 4f && c.bounds.size.x < 3f && c.bounds.size.z < 3f
                    && c.bounds.center.y > -100f && Vector3.Distance(c.bounds.center, ship) < 160f)
                .OrderBy(_ => Random.value).ToList();
            var used = new HashSet<BlockKey>();
            foreach (var t in trunks)
            {
                if (columns >= maxColumns) break;
                if (Random.value > 0.4f) continue; // (some trees, not all)
                var b = t.bounds;
                // a side of the trunk: the cell just outside it, a little above the ground
                var dir = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }[Random.Range(0, 4)];
                var at = new Vector3(b.center.x, b.min.y + S * 2.5f, b.center.z) + dir * (Mathf.Max(b.extents.x, b.extents.z) + S * 0.5f);
                var key = Ground.KeyOf(Ground.CellOf(at)); // (the moon's own grid, the one the ground is on)
                if (used.Contains(key)) continue;
                // (the vine faces away from the trunk: its back to it, as a ladder's to its wall)
                byte face = Faces.FromVectorHorizontal(dir);
                int len = Random.Range(2, 6);
                int placed = 0;
                for (int i = 0; i < len; i++)
                {
                    var k = key.Offset(new Vector3Int(0, -i, 0));
                    if (world.Has(k) || used.Contains(k) || ServerLogic.Obstructed(k, 0.6f)) break;
                    ops.Add(Op.Set(k, new BlockData(Blocks.Vine.Id, face))); used.Add(k); placed++;
                }
                if (placed > 0) { columns++; Placed += placed; }
            }
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
            return columns;
        }

        // ------------------------------------------------------------------ on the moon's trees, as the ship lands
        static float landed = -100f;
        [HarmonyLib.HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc"), HarmonyLib.HarmonyPostfix]
        static void Landed(RoundManager __instance)
        {
            if (Blocks.Vine == null || !BlockNet.IsServer || TerrainCarver.AtCompany || !Landing.Once(ref landed)) return;
            __instance.StartCoroutine(PopulateLater());
        }

        static System.Collections.IEnumerator PopulateLater()
        {
            yield return new WaitForSeconds(9f);
            int n = 0;
            try { n = ServerPopulate(); } catch (System.Exception e) { Plugin.Log.LogWarning("vines: " + e.Message); }
            if (n > 0) Plugin.Log.LogInfo($"[vines] {n} columns on the moon's trees");
        }

        static bool IsTree(Transform t)
        {
            for (int i = 0; i < 3 && t != null; i++, t = t.parent)
                if (t.name.ToLower().Contains("tree")) return true;
            return false;
        }
    }
}
