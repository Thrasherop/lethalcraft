using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244 part G) Blocks for part G. Ids from 1200, after part E's (saves keep ids: new ones only at the end).
    /// </summary>
    public static partial class Blocks
    {
        static ushort nextGId = 1200;
        public static BlockDef RedstoneOreLit, DeepslateRedstoneOreLit;

        /// <summary>(#244) Minecraft's lit redstone ore (its LIT state), as a block of its own: the ore's twin, giving light 9;
        /// it drops as the ore does (Silk Touch: the ore). Not an item.</summary>
        static BlockDef LitTwin(BlockDef ore)
        {
            if (ore == null) return null;
            var d = ore.CopyAs(nextGId++, ore.Key + "_lit");
            d.LightIntensity = 1500f; d.LightRange = 9f; d.LightColor = new Color(1f, 0.55f, 0.5f);
            d.Unlit = ore; d.NoItem = true;
            d.DropAs = ore.DropAs ?? ore; d.OreBase = ore.OreBase ?? ore;
            RegisterDef(d);
            return d;
        }

        static void AddThoroughG()
        {
            RedstoneOreLit = LitTwin(RedstoneOre);
            DeepslateRedstoneOreLit = LitTwin(Get("deepslate_redstone_ore"));
        }

        public static bool IsRedstoneOre(BlockDef d) => d != null && (d == RedstoneOre || d == RedstoneOreLit || d.Key == "deepslate_redstone_ore" || d == DeepslateRedstoneOreLit);
    }

    /// <summary>(#244) Minecraft's RedStoneOreBlock: touched (hit, [E]) or stepped on (not sneaking), it lights up; a random
    /// tick puts it out again (about a minute: 3 random ticks a tick in 4096 blocks).</summary>
    public static class RedstoneOres
    {
        static readonly Dictionary<BlockKey, float> litUntil = new Dictionary<BlockKey, float>();
        public static int Lit, Unlit; // (dev/tests)
        static float nextStep;

        /// <summary>Host: someone touched the block there.</summary>
        public static void ServerTouch(BlockKey k)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || !Blocks.IsRedstoneOre(b.Data.Def)) return;
            litUntil[k] = Time.time + Mathf.Min(300f, -Mathf.Log(1f - Random.value * 0.999f) * 68f);
            if (b.Data.Def.Unlit != null) return; // (lit already: longer)
            var lit = b.Data.Def == Blocks.RedstoneOre ? Blocks.RedstoneOreLit : Blocks.DeepslateRedstoneOreLit;
            if (lit == null) return;
            var d = b.Data; d.Type = lit.Id;
            BlockNet.ServerBroadcastOp(Op.Set(k, d));
            Lit++;
        }

        /// <summary>Host, often: feet on an ore light it; lit ones go out in their time.</summary>
        public static void ServerTick()
        {
            var w = BlockWorld.Instance;
            if (w == null || StartOfRound.Instance == null) return;
            if (Time.time >= nextStep)
            {
                nextStep = Time.time + 0.2f;
                foreach (var p in StartOfRound.Instance.allPlayerScripts)
                    if (p != null && p.isPlayerControlled && !p.isPlayerDead && !p.isCrouching) Under(w, p.transform.position);
                if (RoundManager.Instance != null)
                    foreach (var e in RoundManager.Instance.SpawnedEnemies)
                        if (e != null && !e.isEnemyDead) Under(w, e.transform.position);
            }
            if (litUntil.Count == 0) return;
            List<BlockKey> done = null;
            foreach (var kv in litUntil) if (Time.time > kv.Value) (done ??= new List<BlockKey>()).Add(kv.Key);
            if (done == null) return;
            foreach (var k in done)
            {
                litUntil.Remove(k);
                var b = w.Get(k);
                if (b == null || b.Data.Def.Unlit == null || !Blocks.IsRedstoneOre(b.Data.Def)) continue;
                var d = b.Data; d.Type = b.Data.Def.Unlit.Id;
                BlockNet.ServerBroadcastOp(Op.Set(k, d));
                Unlit++;
            }
        }

        static void Under(BlockWorld w, Vector3 feet)
        {
            var k = Ground.KeyOf(Ground.CellOf(feet + Vector3.down * 0.15f * BlockWorld.S));
            var b = w.Get(k);
            if (b != null && Blocks.IsRedstoneOre(b.Data.Def) && b.Data.Def.Unlit == null) ServerTouch(k);
        }

        public static void Reset() => litUntil.Clear();
    }
}
