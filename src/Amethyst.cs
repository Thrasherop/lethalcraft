using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Amethyst (#229, the owner's): Minecraft's geodes deep in the moons' ground (GeodeGen: GeodeFeature reimplemented;
    /// Geodes: where one can really be, and opening its hollow), and their blocks: the block of amethyst, budding amethyst
    /// (it grows buds through Minecraft's four stages on its random ticks, BuddingAmethystBlock; it drops nothing and a
    /// piston breaks it), the small, medium and large buds and the cluster (AmethystClusterBlock: out of the face they're
    /// on, Minecraft's sizes and light), calcite and smooth basalt (the shell), and tinted glass. A cluster drops 4 shards to
    /// a pickaxe, 2 otherwise; buds drop nothing (silk touch's, which the mod hasn't). The block of amethyst and budding
    /// amethyst chime when walked on (Entity.playAmethystStepSound), and every amethyst block when a projectile hits it
    /// (AmethystBlock.onProjectileHit). Ids 700-749.
    /// </summary>
    [HarmonyPatch]
    public static class Amethyst
    {
        public const string ShardKey = "amethyst_shard";
        public static int Grown; // (dev/tests: buds grown or grown on by budding amethyst)

        /// <summary>A bud's or cluster's block by stage (0 small, 1 medium, 2 large, 3 cluster).</summary>
        public static BlockDef BudOf(int stage) => stage <= 0 ? Blocks.SmallAmethystBud : stage == 1 ? Blocks.MediumAmethystBud : stage == 2 ? Blocks.LargeAmethystBud : Blocks.AmethystCluster;

        /// <summary>A bud's or cluster's stage, or -1.</summary>
        public static int StageOf(BlockDef d) => d == null ? -1 : d == Blocks.SmallAmethystBud ? 0 : d == Blocks.MediumAmethystBud ? 1 : d == Blocks.LargeAmethystBud ? 2 : d == Blocks.AmethystCluster ? 3 : -1;

        /// <summary>Minecraft's AmethystClusterBlock shapes: (height, width) in pixels, out of the face it's on.</summary>
        public static (float h, float w) Size(BlockDef d)
        {
            switch (StageOf(d))
            {
                case 0: return (3f, 8f);
                case 1: return (4f, 10f);
                case 2: return (5f, 10f);
                default: return (7f, 10f);
            }
        }

        /// <summary>Minecraft's #crystal_sound_blocks: they chime when walked on.</summary>
        public static bool Chimes(BlockDef d) => d != null && (d == Blocks.AmethystBlock || d == Blocks.BuddingAmethyst);

        /// <summary>Minecraft's AmethystBlock and what extends it (budding amethyst, the buds and the cluster): a projectile
        /// hitting it chimes.</summary>
        public static bool IsAmethyst(BlockDef d) => Chimes(d) || StageOf(d) >= 0;

        /// <summary>Budding amethyst and the buds: nothing (Minecraft's loot tables; the buds' only drop is silk touch's).</summary>
        public static bool DropsNothing(BlockDef d) => d != null && (d == Blocks.BuddingAmethyst || (StageOf(d) >= 0 && StageOf(d) < 3));

        /// <summary>Minecraft's PushReaction.POPPED for budding amethyst: a piston breaks it (the buds pop as any thin block).</summary>
        public static bool PistonPops(BlockDef d) => d != null && d == Blocks.BuddingAmethyst;

        /// <summary>Host: a cluster broken by a player: 4 shards with a pickaxe (Minecraft's #cluster_max_harvestables), else
        /// the 2 its block drops (SpawnDrop). True: handled.</summary>
        public static bool ServerClusterDrop(BlockData d, Vector3 at, PlayerControllerB breaker)
        {
            if (d.Def != Blocks.AmethystCluster) return false;
            var tool = breaker != null ? breaker.currentlyHeldObjectServer as ToolItem : null;
            if (tool == null || tool.Kind != ToolKind.Pickaxe || !ModItems.ByKey.TryGetValue(ShardKey, out var shard)) return false;
            ModItems.ServerSpawnStack(shard, 4, at);
            return true;
        }

        /// <summary>Host: a projectile hit a block. Amethyst chimes (AmethystBlock.onProjectileHit: pitch 0.5-1.7). True: it was.</summary>
        public static bool ServerProjectileHit(BlockKey k, BlockDef d)
        {
            if (!IsAmethyst(d)) return false;
            BlockNet.ServerSound(BlockWorld.Instance.WorldCenter(k), "amethyst.chime", 0.5f + Random.value * 1.2f, ChimeVolume);
            return true;
        }

        /// <summary>sounds.json's volume for block.amethyst_block.chime.</summary>
        const float ChimeVolume = 0.2f;

        // ------------------------------------------------------------------ the host: budding amethyst grows
        static readonly List<BlockKey> ticked = new List<BlockKey>();

        /// <summary>Host, each second: Minecraft's random ticks (each block about every 68 s), and on each, one in five,
        /// BuddingAmethystBlock.randomTick: a side picked at random; open air there gets a small bud, a bud there growing
        /// out of this block goes on a stage (small, medium, large, cluster).</summary>
        public static void ServerTick(float dt)
        {
            var w = BlockWorld.Instance;
            if (w == null || Blocks.BuddingAmethyst == null) return;
            ticked.Clear();
            foreach (var kv in w.Blocks)
                if (kv.Value.Data.Def == Blocks.BuddingAmethyst && Random.value < dt / GeodeGen.RandomTickSeconds) ticked.Add(kv.Key);
            foreach (var k in ticked) RandomTick(k);
        }

        static void RandomTick(BlockKey k)
        {
            var w = BlockWorld.Instance;
            if (Random.Range(0, GeodeGen.GrowthChance) != 0) return;
            byte f = GeodeGen.DirectionOrder[Random.Range(0, 6)];
            var nk = k.Offset(f);
            var there = w.Get(nk);
            bool open = there == null && !Ground.IsUndugGround(nk) && (nk.Frame != 0 || w.WorldFrameAvailable);
            int stage = there != null ? StageOf(there.Data.Def) : -1;
            int next = GeodeGen.Grow(open, stage, there != null && there.Data.Facing == f);
            if (next < 0) return;
            BlockNet.ServerBroadcastOp(Op.Set(nk, new BlockData(BudOf(next).Id, f, 0)));
            Grown++;
        }

        // ------------------------------------------------------------------ every machine: the chime underfoot
        class Steps { public Vector3 Last; public float MoveDist, NextStep = 1f, LastChime = -100f, Intensity; }
        static readonly Dictionary<PlayerControllerB, Steps> steps = new Dictionary<PlayerControllerB, Steps>();

        /// <summary>Each player's steps, on every machine (Entity.move: a step every 1/0.6 blocks walked); on a block of
        /// amethyst or budding amethyst, a chime at most once a second, louder and higher the more you walk on it
        /// (Entity.playAmethystStepSound).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPostfix]
        static void StepChime(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p == null || !p.isPlayerControlled || p.isPlayerDead || BlockWorld.Instance == null || Blocks.AmethystBlock == null) return;
            var pos = p.transform.position;
            if (!steps.TryGetValue(p, out var s)) { steps[p] = new Steps { Last = pos }; return; }
            var d = pos - s.Last; d.y = 0f; s.Last = pos;
            float moved = d.magnitude;
            if (moved > 2f) return; // (a teleport)
            s.MoveDist += moved / BlockWorld.S * 0.6f;
            if (s.MoveDist <= s.NextStep) return;
            var under = BlockUnder(pos);
            if (under == null) return; // (in the air: the step comes on landing)
            s.NextStep = (int)s.MoveDist + 1;
            if (!Chimes(under) || Time.time < s.LastChime + 1f) return;
            s.Intensity *= Mathf.Pow(0.997f, (Time.time - s.LastChime) * 20f);
            s.Intensity = Mathf.Min(1f, s.Intensity + 0.07f);
            float pitch = 0.5f + s.Intensity * Random.value * 1.2f, volume = 0.1f + s.Intensity * 1.2f;
            Sounds.Play("amethyst.chime", pos, volume * ChimeVolume, pitch);
            s.LastChime = Time.time;
        }

        /// <summary>The block a player stands on (null: none, or the level's own ground).</summary>
        static BlockDef BlockUnder(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.2f, Vector3.down, out var hit, 0.5f, 1 << BlockWorld.SolidLayer, QueryTriggerInteraction.Ignore)) return null;
            var br = hit.collider.GetComponentInParent<BlockRef>();
            return br != null ? BlockWorld.Instance.DefAt(br.Key) : null;
        }
    }

    /// <summary>
    /// Where a geode can really be (#229): GeodeGen says where each would be; the host keeps the ones lying wholly in the
    /// moon's solid ground, deep enough (GeodeGen.MinBelow under the surface straight above its middle), with nothing of
    /// the level's own (the moon's surface, rocks, the facility's rooms) within its reach and above the world's floor. Its
    /// hollow counts as open space under the ground: met by a dig (a cell beside a dug one), all of it opens at once and
    /// its first buds and clusters go on its walls. Hooked into the ground's material lookup (Ground: Classify, Ensure,
    /// ShallowMaterial, OpenMany), so it can move onto another ore system as it is.
    /// </summary>
    public static class Geodes
    {
        static readonly Dictionary<(int, int, int), bool> usable = new Dictionary<(int, int, int), bool>();
        static readonly List<Vector3Int> met = new List<Vector3Int>();
        public static int Opened; // (dev/tests)

        public static void Reset() { usable.Clear(); met.Clear(); GeodeGen.Reset(); }

        /// <summary>Each moon its own geodes (the same each landing).</summary>
        public static int Salt
        {
            get
            {
                var lvl = StartOfRound.Instance != null ? StartOfRound.Instance.currentLevel : null;
                return lvl != null ? lvl.levelID + 1 : 0;
            }
        }

        /// <summary>The geode a natural cell is part of, if it can really be there (null: none).</summary>
        public static GeodeGen.Geode At(Vector3Int c)
        {
            var g = GeodeGen.Containing(c.x, c.y, c.z, Salt);
            if (g == null) return null;
            var k = (g.RX, g.RY, g.RZ);
            if (!usable.TryGetValue(k, out bool ok)) usable[k] = ok = Check(g);
            return ok ? g : null;
        }

        /// <summary>(dev) Why a region's geode is kept or not.</summary>
        public static string Why(GeodeGen.Geode g)
        {
            if (TerrainCarver.AtCompany) return "the Company";
            var center = Ground.Center(new Vector3Int(g.CX, g.CY, g.CZ));
            if (NetherGround.InRegion(center)) return "the Nether";
            if (Ground.IsBoundary(new Vector3Int(g.CX, g.CY - GeodeGen.Reach - 1, g.CZ))) return "below the world's floor";
            if (!TerrainCarver.RayOriginal(center, Vector3.up, 600f, out float up, out bool front, out _)) return "open sky above";
            if (front) return "in open space";
            if (up / BlockWorld.S < GeodeGen.MinBelow) return $"only {up / BlockWorld.S:F0} blocks down";
            float half = (GeodeGen.Reach + 1) * BlockWorld.S;
            foreach (var h in Physics.OverlapBox(center, Vector3.one * half, Quaternion.identity, TerrainCarver.LevelMask, QueryTriggerInteraction.Ignore))
            {
                if (h == null || h.GetComponentInParent<BlockRef>() != null || h.GetComponentInParent<GrabbableObject>() != null) continue; // (blocks, items)
                if (h.GetComponentInParent<PlayerControllerB>() != null || h.GetComponentInParent<EnemyAI>() != null) continue;
                return "near " + h.name;
            }
            return null;
        }

        static bool Check(GeodeGen.Geode g)
        {
            string why;
            try { why = Why(g); }
            catch (System.Exception e) { why = e.Message; }
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] geode at {g.CX},{g.CY},{g.CZ}: {why ?? "kept"}");
            return why == null;
        }

        /// <summary>What a natural cell is in a geode that's really there (None: not in one).</summary>
        public static GeodeGen.Layer LayerAt(Vector3Int c) => At(c)?.At(c.x, c.y, c.z) ?? GeodeGen.Layer.None;

        /// <summary>Is this cell a geode's hollow (or crack): open space under the ground?</summary>
        public static bool IsHollow(Vector3Int c) => LayerAt(c) == GeodeGen.Layer.Air;

        /// <summary>What a geode's shell cell is made of (null: not one).</summary>
        public static BlockDef MaterialAt(Vector3Int c)
        {
            var g = At(c);
            if (g == null) return null;
            switch (g.At(c.x, c.y, c.z))
            {
                case GeodeGen.Layer.Amethyst: return Blocks.AmethystBlock;
                case GeodeGen.Layer.Budding: return Blocks.BuddingAmethyst;
                case GeodeGen.Layer.Calcite: return Blocks.Calcite;
                case GeodeGen.Layer.Basalt: return Blocks.SmoothBasalt;
            }
            return null;
        }

        /// <summary>Host: a hollow cell beside a dug one (Ground.Ensure).</summary>
        public static void Met(Vector3Int c)
        {
            if (!met.Contains(c) && IsHollow(c)) met.Add(c);
        }

        /// <summary>Host, after cells were opened: the hollows met open whole (through the ground's own opening, so the
        /// walls around them become blocks), and the buds Minecraft's generation put on their budding amethyst go on.</summary>
        public static void ServerOpenMet(System.Action<List<Vector3Int>> open)
        {
            if (met.Count == 0) return;
            var geodes = met.Select(At).Where(g => g != null).Distinct().ToList();
            met.Clear();
            var w = BlockWorld.Instance;
            foreach (var g in geodes)
            {
                var cells = g.Hollow.Select(h => new Vector3Int(h.x, h.y, h.z)).Where(c => !Ground.IsDug(c)).ToList();
                if (cells.Count == 0) continue;
                open(cells);
                var ops = new List<Op>();
                foreach (var b in g.Buds)
                {
                    var k = Ground.KeyOf(new Vector3Int(b.X, b.Y, b.Z));
                    if (w.Has(k) || w.DefAt(k.Offset(Faces.Opposite(b.Face))) != Blocks.BuddingAmethyst) continue; // (taken, or its budding block mined first)
                    ops.Add(Op.Set(k, new BlockData(Amethyst.BudOf(b.Stage).Id, b.Face, 0)));
                }
                if (ops.Count > 0) BlockNet.ServerBroadcastOps(ops);
                Opened++;
                if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] geode at {g.CX},{g.CY},{g.CZ} opened: {cells.Count} cells, {ops.Count} buds");
            }
        }
    }

    public static partial class Blocks
    {
        public static BlockDef AmethystBlock, BuddingAmethyst, AmethystCluster, LargeAmethystBud, MediumAmethystBud, SmallAmethystBud, Calcite, SmoothBasalt, TintedGlass;

        /// <summary>(#229) Amethyst, ids from 700. Calcite and smooth basalt have no other kinds in Minecraft (no stairs,
        /// slabs, walls, polished: checked against its blocks).</summary>
        static void AddAmethyst()
        {
            BlockDef A(ushort id, string key, string name, float hardness, ToolKind tool, int tier, string sound, string desc, System.Action<BlockDef> cfg = null)
            {
                FamilyTiles[key] = key;
                var d = Add(id, key, name, key, x => { x.Sound = sound; x.Description = desc; cfg?.Invoke(x); });
                familyHardness[key] = (hardness, tool, tier);
                return d;
            }
            var P = ToolKind.Pickaxe;
            var purple = new Color(0.78f, 0.58f, 1f);
            void Cluster(BlockDef x, float lumens, float range)
            {
                x.Shape = BlockShape.Cluster; x.Directional = true; x.Render = RenderKind.Cutout;
                x.LightIntensity = lumens; x.LightRange = range; x.LightColor = purple;
            }
            AmethystBlock = A(700, "amethyst_block", "Block of Amethyst", 1.5f, P, 1, "amethyst",
                "Amethyst, from the walls of a geode deep underground (or four shards). It chimes when you walk on it.");
            BuddingAmethyst = A(701, "budding_amethyst", "Budding Amethyst", 1.5f, P, 1, "amethyst",
                "Amethyst that grows buds on its open sides, slowly, into clusters: mine those for shards. It can't be taken: broken, it's gone.");
            // (Minecraft's light levels: the cluster 5, the large bud 4, the medium 2, the small 1)
            AmethystCluster = A(702, "amethyst_cluster", "Amethyst Cluster", 1.5f, P, 0, "amethyst_cluster",
                "A grown amethyst cluster: four shards to a pickaxe, two otherwise.", x => { Cluster(x, 300f, 5f); x.DropKey = Amethyst.ShardKey; x.DropMin = x.DropMax = 2; });
            LargeAmethystBud = A(703, "large_amethyst_bud", "Large Amethyst Bud", 1.5f, P, 0, "amethyst_cluster",
                "An amethyst bud, nearly a cluster. Broken, it gives nothing.", x => Cluster(x, 200f, 4f));
            MediumAmethystBud = A(704, "medium_amethyst_bud", "Medium Amethyst Bud", 1.5f, P, 0, "amethyst_cluster",
                "An amethyst bud, half grown. Broken, it gives nothing.", x => Cluster(x, 60f, 2f));
            SmallAmethystBud = A(705, "small_amethyst_bud", "Small Amethyst Bud", 1.5f, P, 0, "amethyst_cluster",
                "A new amethyst bud. Broken, it gives nothing.", x => Cluster(x, 20f, 1.5f));
            Calcite = A(706, "calcite", "Calcite", 0.75f, P, 1, "calcite", "White rock, a geode's middle layer.");
            SmoothBasalt = A(707, "smooth_basalt", "Smooth Basalt", 1.25f, P, 1, "basalt", "Dark rock, a geode's outer shell.");
            TintedGlass = A(708, "tinted_glass", "Tinted Glass", 0.3f, ToolKind.None, 0, "glass",
                "Dark glass: four amethyst shards round a glass block make two. Unlike glass, it drops itself.", x => x.Render = RenderKind.Gel);
            ItemTiles["item_" + Amethyst.ShardKey] = "item/amethyst_shard";
            ItemTiles["item_" + Spyglass.Key] = "item/spyglass";
        }
    }
}
