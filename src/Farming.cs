using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Farming (#60), Minecraft's: a hoe turns dirt or grass into farmland (the moon's own ground too); wheat seeds planted
    /// on it grow through Minecraft's eight stages, about one moon day from seed to harvest; they need water within 4 blocks
    /// of the farmland, and dry farmland with nothing on it turns back into dirt. Ripe wheat drops wheat and 0-3 seeds; three wheat make bread. Sugar cane grows
    /// on dirt, grass or sand beside water, up to three tall (breaking the bottom brings the rest down); it makes paper.
    /// The host grows them, on the moon and on the ship (where they keep growing between moons).
    /// </summary>
    public static class Farming
    {
        /// <summary>Seconds per wheat stage with water near the farmland (seven stages: about ten minutes, one moon day).</summary>
        public static float StageSeconds = 85f;
        /// <summary>Seconds per new block of sugar cane.</summary>
        public static float CaneSeconds = 110f;
        /// <summary>Seconds dry farmland with nothing planted on it lasts before it's dirt again (Minecraft's).</summary>
        public static float DrySeconds = 60f;
        public const byte Moist = 1; // farmland state bit: water within 4 blocks
        public const byte Attached = 8; // pumpkin stem state bit: a pumpkin beside it (#130)
        static readonly int[] Sides = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };
        public static int Carved; // (dev/tests)
        public static int Grown; // (dev/tests)

        static BlockWorld W => BlockWorld.Instance;
        public static void Reset() { Grown = 0; grow.Clear(); dry.Clear(); }

        // ------------------------------------------------------------------ rules
        static readonly int[] Horizontal = { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East };

        /// <summary>(#244) What sugar cane grows on, Minecraft 26.3's tags/block/supports_sugar_cane: the dirts, mud, moss,
        /// grass, podzol, mycelium and the sands (it had been dirt, grass and sand).</summary>
        static bool CaneSoil(BlockDef d) => d != null && (d == Blocks.Dirt || d == Blocks.Grass || d == Blocks.Sand || d.Key == "red_sand" || d.Key == "suspicious_sand"
            || d.Key == "coarse_dirt" || d.Key == "rooted_dirt" || d.Key == "mud" || d.Key == "muddy_mangrove_roots" || d.Key == "moss_block" || d.Key == "pale_moss_block"
            || d.Key == "podzol" || d.Key == "mycelium");

        /// <summary>Sugar cane can stand at k: on cane, or on dirt, grass, sand and the like (a block, or the moon's own ground) with water beside that.</summary>
        public static bool CaneCanStand(BlockKey k)
        {
            var w = W;
            if (w == null) return false;
            var below = k.Offset((int)Face.Down);
            var bd = w.DefAt(below);
            if (bd == Blocks.SugarCane) return true;
            bool ground = CaneSoil(bd) || (bd == null && Ground.MaterialUnder(k) is BlockDef m && CaneSoil(m)); // (#223: on a slope too)
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
        static readonly Dictionary<BlockKey, float> dry = new Dictionary<BlockKey, float>();

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
                if (d.Def == Blocks.Farmland)
                {
                    if (checkMoisture)
                    {
                        byte want = (byte)(WaterNear(k) ? Moist : 0);
                        if ((d.State & Moist) != want) { var nd = d; nd.State = (byte)((d.State & ~Moist) | want); ops.Add(Op.State(k, nd)); d = nd; }
                    }
                    // dry, with nothing planted: back to dirt after a while (Minecraft's)
                    var top = w.DefAt(k.Offset((int)Face.Up));
                    if ((d.State & Moist) == 0 && (top == null || top.Shape != BlockShape.Crop))
                    {
                        seen.Add(k);
                        float t = (dry.TryGetValue(k, out var dd) ? dd : 0f) + dt;
                        if (t >= DrySeconds) { ops.Add(Op.Set(k, new BlockData(Blocks.Dirt.Id, (byte)Face.Up, 0))); dry.Remove(k); }
                        else dry[k] = t;
                    }
                }
                else if (d.Def != null && d.Def.Shape == BlockShape.Crop && (d.State & 7) < 7)
                {
                    seen.Add(k);
                    var soil = w.Get(k.Offset((int)Face.Down));
                    bool wart = d.Def == Blocks.NetherWart;
                    bool moist = wart ? soil != null && soil.Data.Def == Blocks.SoulSand : soil != null && soil.Data.Def == Blocks.Farmland && (soil.Data.State & Moist) != 0;
                    if (!moist) continue; // (no water within 4 blocks: it waits; nether wart wants soul sand, no water)
                    float t = (grow.TryGetValue(k, out var g) ? g : 0f) + dt / (wart ? StageSeconds * 1.5f : StageSeconds);
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
                else if (d.Def == Blocks.PumpkinStem || d.Def == Blocks.MelonStem)
                {
                    // grown (#130): now and then it sets a pumpkin (#244: a melon) on the ground beside it; while one's there, it waits
                    seen.Add(k);
                    var fruit = d.Def == Blocks.MelonStem ? Blocks.Melon : Blocks.Pumpkin;
                    bool has = false;
                    foreach (int f in Sides) if (w.DefAt(k.Offset(f)) == fruit) has = true;
                    bool attached = (d.State & Attached) != 0;
                    if (attached != has) { var nd = d; nd.State = (byte)(has ? d.State | Attached : d.State & ~Attached); ops.Add(Op.State(k, nd)); }
                    if (has) continue;
                    var soil = w.Get(k.Offset((int)Face.Down));
                    if (soil == null || soil.Data.Def != Blocks.Farmland || (soil.Data.State & Moist) == 0) continue;
                    float t = (grow.TryGetValue(k, out var g) ? g : 0f) + dt / StageSeconds;
                    if (t >= 1f + Random.Range(-0.15f, 0.15f))
                    {
                        t = 0f;
                        var spots = new List<BlockKey>();
                        foreach (int f in Sides)
                        {
                            var n = k.Offset(f);
                            var below = w.DefAt(n.Offset((int)Face.Down));
                            if (w.Has(n) || ServerLogic.Obstructed(n, 0.8f)) continue;
                            if (below == Blocks.Dirt || below == Blocks.Grass || below == Blocks.Farmland || (below == null && n.Frame == 0 && n.YOff == Ground.GridYOff && Ground.IsSolidCell(n.Offset((int)Face.Down).Pos))) spots.Add(n);
                        }
                        if (spots.Count > 0)
                        {
                            ops.Add(Op.Set(spots[Random.Range(0, spots.Count)], new BlockData(fruit.Id, (byte)Face.Up, 0)));
                            var nd = d; nd.State = (byte)(d.State | Attached); ops.Add(Op.State(k, nd)); // (it bends to it at once)
                            Grown++;
                        }
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
            foreach (var k in dry.Keys.Where(x => !seen.Contains(x)).ToList()) dry.Remove(k);
            if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
        }

        // ------------------------------------------------------------------ drops
        /// <summary>Server: what a crop drops (ripe wheat: wheat and 0-3 seeds; not yet: one seed). False: not a crop.</summary>
        public static bool ServerCropDrop(BlockData d, Vector3 at)
        {
            if (d.Def == null || d.Def.Shape != BlockShape.Crop) return false;
            bool ripe = (d.State & 7) >= 7;
            if (d.Def == Blocks.PumpkinStem || d.Def == Blocks.MelonStem)
            {
                // (Minecraft's stem drops a seed or a few once grown)
                if (ModItems.ByKey.TryGetValue(d.Def == Blocks.MelonStem ? "melon_seeds" : "pumpkin_seeds", out var ps)) ModItems.ServerSpawnStack(ps, ripe ? Random.Range(1, 4) : 1, at);
                return true;
            }
            if (d.Def == Blocks.WheatCrop)
            {
                if (ripe && ModItems.ByKey.TryGetValue("wheat", out var wheat)) ModItems.ServerSpawnStack(wheat, 1, at);
                int seeds = ripe ? Random.Range(0, 4) : 1;
                if (seeds > 0 && ModItems.ByKey.TryGetValue("wheat_seeds", out var s)) ModItems.ServerSpawnStack(s, seeds, at);
            }
            else if (d.Def == Blocks.NetherWart)
            {
                if (ModItems.ByKey.TryGetValue("nether_wart", out var nw)) ModItems.ServerSpawnStack(nw, ripe ? Random.Range(2, 5) : 1, at);
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
            if (def == Blocks.PumpkinStem) return (state & Attached) != 0 ? "attached_pumpkin_stem" : "pumpkin_stem";
            if (def == Blocks.MelonStem) return (state & Attached) != 0 ? "attached_melon_stem" : "melon_stem";
            if (def == Blocks.NetherWart) return "nether_wart_stage" + WartLooks[s];
            return def.Key + "_stage" + FourLooks[s];
        }
        static readonly int[] FourLooks = { 0, 0, 1, 1, 2, 2, 2, 3 };
        static readonly int[] WartLooks = { 0, 0, 0, 1, 1, 1, 1, 2 };

        /// <summary>What a crop is planted on: farmland, or soul sand for nether wart.</summary>
        public static BlockDef SoilFor(BlockDef crop) => crop == Blocks.NetherWart ? Blocks.SoulSand : Blocks.Farmland;

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
        /// <summary>
        /// Server: bone meal on a crop (#59): Minecraft's 2 to 5 growth stages at once (nether wart doesn't take it).
        /// The client has already used one up.
        /// </summary>
        public static void ServerBoneMeal(ulong sender, BlockKey k)
        {
            var w = W;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            ServerBoneMeal(k);
        }

        /// <summary>Host: bone meal on a crop (a player's, or a dispenser's, #160): true if it grew.</summary>
        public static bool ServerBoneMeal(BlockKey k)
        {
            var w = W;
            if (w == null) return false;
            if (Saplings.ServerBoneMeal(k)) { BoneMealed++; return true; } // (#223: a sapling)
            if (Dyes.ServerBoneMealPlant(k) || Dyes.ServerBoneMeal(k)) { BoneMealed++; return true; } // (#225: grass and flowers)
            var b = w.Get(k);
            if (b == null || b.Data.Def == null || b.Data.Def.Shape != BlockShape.Crop || b.Data.Def == Blocks.NetherWart) return false;
            int stage = b.Data.State & 7;
            if (stage >= 7) return false;
            var nd = b.Data; nd.State = (byte)((nd.State & ~7) | Mathf.Min(7, stage + Random.Range(2, 6)));
            BlockNet.ServerBroadcastOps(new List<Op> { Op.State(k, nd) });
            BlockNet.ServerSound(w.WorldCenter(k), "bonemeal", 1f, 0.8f);
            BoneMealed++;
            return true;
        }

        public static int BoneMealed; // (dev/tests)

        /// <summary>Server: shears on a pumpkin (#130): a carved pumpkin facing whoever carved it, and four seeds.</summary>
        public static int Stripped; // (dev/tests)
        public static void ServerCarve(ulong sender, BlockKey k)
        {
            var w = W;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null) return;
            if (Copper.Waxable(w.DefAt(k)) && (p.currentlyHeldObjectServer as StackItem)?.ItemKey == "honeycomb") { if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) <= 9f * BlockWorld.S + 3f) Copper.ServerWax(p, k); return; }
            // (#223) an axe strips a log or wood, its axis kept
            if (Blocks.StrippedOf(w.DefAt(k)) is BlockDef strip && p.currentlyHeldObjectServer is ToolItem sx && sx.Kind == ToolKind.Axe)
            {
                if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
                var sb = w.Get(k).Data;
                BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(strip.Id, sb.Facing, sb.State)));
                BlockNet.ServerSound(w.WorldCenter(k), "axe.strip", 1f, 1f);
                if (!GameModes.IsCreative(sender)) sx.ServerUse(1);
                Stripped++;
                return;
            }
            if (Copper.Scrapable(w.DefAt(k))) { if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) <= 9f * BlockWorld.S + 3f) Copper.ServerScrape(p, k); return; } // (#160: an axe on copper)
            if (w.DefAt(k) != Blocks.Pumpkin) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            if (!(p.currentlyHeldObjectServer is ToolItem t) || t.Kind != ToolKind.Shears) return;
            var center = w.WorldCenter(k);
            var toward = Quaternion.Inverse(w.FrameRotation(k.Frame)) * (p.transform.position - center);
            byte face = Faces.FromVectorHorizontal(toward);
            BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Blocks.CarvedPumpkin.Id, face, 0)));
            if (ModItems.ByKey.TryGetValue("pumpkin_seeds", out var ps)) ModItems.ServerSpawnStack(ps, 4, center + (p.transform.position - center).normalized * 0.6f * BlockWorld.S);
            BlockNet.ServerSound(center, "pumpkin.carve", 1f, 1f);
            if (!GameModes.IsCreative(sender)) t.ServerUse(1);
            Carved++;
        }

        public static void ServerTill(ulong sender, BlockKey k, bool ground)
        {
            var w = W;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null) return;
            if (Vector3.Distance(p.gameplayCamera.transform.position, w.WorldCenter(k)) > 9f * BlockWorld.S + 3f) return;
            var above = k.Offset((int)Face.Up);
            if (w.Has(above) && w.DefAt(above) != Blocks.Fire) return;
            // (#244) Minecraft's: a hoe tills dirt and grass (coarse dirt it turns to dirt), a shovel makes a dirt path
            bool shovel = p.currentlyHeldObjectServer is ToolItem ht && ht.Kind == ToolKind.Shovel;
            var b = w.Get(k);
            if (b != null)
            {
                if (b.Data.Def != Blocks.Dirt && b.Data.Def != Blocks.Grass && b.Data.Def != Blocks.CoarseDirt) return;
                if (b.Data.Def == Blocks.CoarseDirt && !shovel)
                {
                    BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Blocks.Dirt.Id, (byte)Face.Up, 0)));
                    BlockNet.ServerSound(w.WorldCenter(k), "till", 0.8f, 1f);
                    if (!GameModes.IsCreative(sender)) ((ToolItem)p.currentlyHeldObjectServer).ServerUse(1);
                    return;
                }
            }
            else
            {
                // the moon's ground: only its top cell, only where it's dirt or grass, with nothing of the ground over it
                // (Minecraft's hoe only tills dirt with air above: under a slope's edge the seeds would have nowhere to go, #93)
                if (!ground || k.Frame != 0 || k.YOff != Ground.GridYOff) return;
                if (!Ground.IsAirCell(above.Pos)) { BlockNet.ServerToast(sender, "There's ground over it."); return; }
                var top = Ground.MaterialOf(k.Pos);
                if (top == null) return;
                if (top != Blocks.Dirt && top != Blocks.Grass) { BlockNet.ServerToast(sender, shovel ? "A shovel makes a path on grass and dirt." : "A hoe tills dirt and grass."); return; }
                Ground.OpenCellForBlock(k.Pos);
            }
            if (shovel) { BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Blocks.DirtPath.Id, (byte)Face.Up, 0))); BlockNet.ServerSound(w.WorldCenter(k), "dig.grass", 0.8f, 0.9f); }
            else
            {
                BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(Blocks.Farmland.Id, (byte)Face.Up, (byte)(WaterNear(k) ? Moist : 0))));
                BlockNet.ServerSound(w.WorldCenter(k), "till", 0.8f, 1f);
            }
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
            return $"farmland={farmland} moist={moist} crops=[{string.Join(",", stages)}] cane={cane} grown={Grown} stageSeconds={StageSeconds} drySeconds={DrySeconds}";
        }
    }
}
