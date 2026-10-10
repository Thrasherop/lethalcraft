using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Farming (#60), Minecraft's: a hoe turns dirt or grass into farmland (the moon's own ground too); wheat seeds planted
    /// on it grow through Minecraft's eight stages, about one moon day from seed to harvest with water within 4 blocks of
    /// the farmland, twice as long without. Ripe wheat drops wheat and 0-3 seeds; three wheat make bread. Sugar cane grows
    /// on dirt, grass or sand beside water, up to three tall (breaking the bottom brings the rest down); it makes paper.
    /// The host grows them, on the moon and on the ship (where they keep growing between moons).
    /// </summary>
    public static class Farming
    {
        /// <summary>Seconds per wheat stage with water near the farmland (seven stages: about ten minutes, one moon day).</summary>
        public static float StageSeconds = 85f;
        /// <summary>Seconds per new block of sugar cane.</summary>
        public static float CaneSeconds = 110f;
        public const byte Moist = 1; // farmland state bit: water within 4 blocks
        public static int Grown; // (dev/tests)

        static BlockWorld W => BlockWorld.Instance;
        public static void Reset() { Grown = 0; }

        // ------------------------------------------------------------------ rules
        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        /// <summary>Sugar cane can stand at k: on cane, or on dirt, grass or sand (a block, or the moon's own ground) with water beside that.</summary>
        public static bool CaneCanStand(BlockKey k)
        {
            var w = W;
            if (w == null) return false;
            var below = k.Offset((int)Face.Down);
            var bd = w.DefAt(below);
            if (bd == Blocks.SugarCane) return true;
            bool ground = bd == Blocks.Dirt || bd == Blocks.Grass || bd == Blocks.Sand
                || (bd == null && below.Frame == 0 && below.YOff == Ground.GridYOff && Ground.MaterialOf(below.Pos) is BlockDef m && (m == Blocks.Dirt || m == Blocks.Grass || m == Blocks.Sand));
            if (!ground) return false;
            foreach (int f in Horizontal)
            {
                var n = below.Offset(f);
                if (w.DefAt(n) == Blocks.Water) return true;
                if (WaterSwim.WaterCellAt(w.WorldCenter(n), out _)) return true;
            }
            return false;
        }

        /// <summary>Water within 4 blocks of farmland, level with it or one up (Minecraft's moisture).</summary>
        static bool WaterNear(BlockKey farmland)
        {
            var w = W;
            for (int dy = 0; dy <= 1; dy++)
                for (int dx = -4; dx <= 4; dx++)
                    for (int dz = -4; dz <= 4; dz++)
                    {
                        var c = new BlockKey(farmland.Frame, farmland.YOff, farmland.Pos + new Vector3Int(dx, dy, dz));
                        if (w.DefAt(c) == Blocks.Water) return true;
                    }
            return false;
        }

        // ------------------------------------------------------------------ the host's tick (every second)
        static float nextMoisture;
        static readonly Dictionary<BlockKey, float> grow = new Dictionary<BlockKey, float>();

        public static void ServerTick(float dt)
        {
            var w = W;
            if (w == null) return;
            var ops = new List<Op>();
            bool checkMoisture = Time.time >= nextMoisture;
            if (checkMoisture) nextMoisture = Time.time + 4f;
            var seen = new HashSet<BlockKey>();
            foreach (var kv in w.Blocks.ToList())
            {
                var k = kv.Key; var d = kv.Value.Data;
                if (d.Def == Blocks.Farmland && checkMoisture)
                {
                    byte want = (byte)(WaterNear(k) ? Moist : 0);
                    if ((d.State & Moist) != want) { var nd = d; nd.State = (byte)((d.State & ~Moist) | want); ops.Add(Op.State(k, nd)); }
                }
                else if (d.Def != null && d.Def.Shape == BlockShape.Crop && (d.State & 7) < 7)
                {
                    seen.Add(k);
                    var soil = w.Get(k.Offset((int)Face.Down));
                    bool moist = soil != null && soil.Data.Def == Blocks.Farmland && (soil.Data.State & Moist) != 0;
                    float t = (grow.TryGetValue(k, out var g) ? g : 0f) + dt / (moist ? StageSeconds : StageSeconds * 2f);
                    // (a little random, like Minecraft's random ticks: crops beside each other don't all grow at once)
                    if (t >= 1f + Random.Range(-0.15f, 0.15f))
                    {
                        t = 0f;
                        var nd = d; nd.State = (byte)((d.State & ~7) | ((d.State & 7) + 1));
                        ops.Add(Op.State(k, nd));
                        Grown++;
                    }
                    grow[k] = t;
                }
                else if (d.Def == Blocks.SugarCane && w.DefAt(k.Offset((int)Face.Down)) != Blocks.SugarCane)
                {
                    // the bottom of a stalk: it grows up to three tall
                    seen.Add(k);
                    int h = 1; var top = k;
                    while (h < 3 && w.DefAt(top.Offset((int)Face.Up)) == Blocks.SugarCane) { top = top.Offset((int)Face.Up); h++; }
                    if (h >= 3) continue;
                    float t = (grow.TryGetValue(k, out var g) ? g : 0f) + dt / CaneSeconds;
                    var up = top.Offset((int)Face.Up);
                    if (t >= 1f + Random.Range(-0.15f, 0.15f))
                    {
                        t = 0f;
                        if (!w.Has(up) && !ServerLogic.Obstructed(up, 0.8f)) { ops.Add(Op.Set(up, new BlockData(Blocks.SugarCane.Id, (byte)Face.Up, 0))); Grown++; }
                    }
                    grow[k] = t;
                }
            }
            foreach (var k in grow.Keys.Where(x => !seen.Contains(x)).ToList()) grow.Remove(k);
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
        }

        // ------------------------------------------------------------------ drops
        /// <summary>Server: what a crop drops (ripe wheat: wheat and 0-3 seeds; not yet: one seed). False: not a crop.</summary>
        public static bool ServerCropDrop(BlockData d, Vector3 at)
        {
            if (d.Def == null || d.Def.Shape != BlockShape.Crop) return false;
            bool ripe = (d.State & 7) >= 7;
            if (d.Def == Blocks.WheatCrop)
            {
                if (ripe && ModItems.ByKey.TryGetValue("wheat", out var wheat)) ModItems.ServerSpawnStack(wheat, 1, at);
                int seeds = ripe ? Random.Range(0, 4) : 1;
                if (seeds > 0 && ModItems.ByKey.TryGetValue("wheat_seeds", out var s)) ModItems.ServerSpawnStack(s, seeds, at);
            }
            else if (ModItems.ByKey.TryGetValue(d.Def == Blocks.CarrotCrop ? "carrot" : "potato", out var food))
                ModItems.ServerSpawnStack(food, ripe ? Random.Range(2, 6) : 1, at); // (Minecraft's: 2-5 ripe)
            return true;
        }

        /// <summary>A crop's texture: wheat has eight, carrots and potatoes four over the same eight stages.</summary>
        public static string CropTile(BlockDef def, byte state)
        {
            int s = state & 7;
            if (def == Blocks.WheatCrop) return "wheat_stage" + s;
            return def.Key + "_stage" + FourLooks[s];
        }
        static readonly int[] FourLooks = { 0, 0, 1, 1, 2, 2, 2, 3 };

        // ------------------------------------------------------------------ loot
        static readonly (string key, int min, int max)[] Loot = { ("wheat_seeds", 2, 5), ("sugar_cane", 1, 3), ("carrot", 1, 3), ("potato", 1, 3) };

        /// <summary>Host, after the facility's scrap: a few piles of farm things on its scrap spots, worth nothing.</summary>
        public static void ServerSpawnLoot(RoundManager rm)
        {
            try
            {
                if (Balance.FarmLootPerMoon <= 0) return;
                var spots = Object.FindObjectsOfType<RandomScrapSpawn>();
                if (spots.Length == 0) return;
                var rng = new System.Random(StartOfRound.Instance.randomMapSeed + 60);
                for (int i = 0; i < Balance.FarmLootPerMoon; i++)
                {
                    var (key, min, max) = Loot[rng.Next(Loot.Length)];
                    if (!ModItems.ByKey.TryGetValue(key, out var item)) continue;
                    var at = spots[rng.Next(spots.Length)].transform.position + Vector3.up * 0.3f;
                    ModItems.ServerSpawnStack(item, rng.Next(min, max + 1), at);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogError("Farm loot: " + e); }
        }

        /// <summary>Server: the rest of a stalk above a broken sugar cane comes down with it (each drops itself).</summary>
        public static void ServerCaneAbove(BlockKey broken, List<Op> ops, bool drop)
        {
            var w = W;
            // (the one right above stands on the broken one: it already came down with the blocks the break pops)
            var k = broken.Offset((int)Face.Up).Offset((int)Face.Up);
            if (w.DefAt(broken.Offset((int)Face.Up)) != Blocks.SugarCane) return;
            while (w.DefAt(k) == Blocks.SugarCane)
            {
                ops.Add(Op.Remove(k, true));
                if (drop) ServerLogic.SpawnDrop(Blocks.SugarCane, w.WorldCenter(k));
                k = k.Offset((int)Face.Up);
            }
        }

        // ------------------------------------------------------------------ the hoe
        /// <summary>
        /// Server: a hoe on dirt or grass makes farmland (a block, or the moon's own ground: that cell becomes farmland).
        /// Nothing may be on top of it.
        /// </summary>
        public static void ServerTill(ulong sender, BlockKey k, bool ground)
        {
            var w = W;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            var above = k.Offset((int)Face.Up);
            if (w.Has(above) && w.DefAt(above) != Blocks.Fire) return;
            var b = w.Get(k);
            if (b != null)
            {
                if (b.Data.Def != Blocks.Dirt && b.Data.Def != Blocks.Grass) return;
            }
            else
            {
                // the moon's ground: only its top cell, only where it's dirt or grass
                if (!ground || k.Frame != 0 || k.YOff != Ground.GridYOff) return;
                var top = Ground.MaterialOf(k.Pos);
                if (top == null) return;
                if (top != Blocks.Dirt && top != Blocks.Grass) { BlockNet.ServerToast(sender, "A hoe tills dirt and grass."); return; }
                Ground.OpenCellForBlock(k.Pos);
            }
            BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Blocks.Farmland.Id, (byte)Face.Up, (byte)(WaterNear(k) ? Moist : 0))));
            BlockNet.ServerSound(w.WorldCenter(k), "till", 0.8f, 1f);
            if (p.currentlyHeldObjectServer is ToolItem t && !GameModes.IsCreative(sender)) t.ServerUse(1);
        }

        public static string Describe()
        {
            var w = W;
            if (w == null) return "-";
            int farmland = 0, moist = 0, cane = 0; var stages = new int[8];
            foreach (var b in w.Blocks.Values)
            {
                if (b.Data.Def == Blocks.Farmland) { farmland++; if ((b.Data.State & Moist) != 0) moist++; }
                else if (b.Data.Def != null && b.Data.Def.Shape == BlockShape.Crop) stages[b.Data.State & 7]++;
                else if (b.Data.Def == Blocks.SugarCane) cane++;
            }
            return $"farmland={farmland} moist={moist} crops=[{string.Join(",", stages)}] cane={cane} grown={Grown} stageSeconds={StageSeconds}";
        }
    }
}
