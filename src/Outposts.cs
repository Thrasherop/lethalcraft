using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Pillager outposts (#223, the owner's: "rare"): now and then a moon has Minecraft's watchtower
    /// (data/minecraft/structure/pillager_outpost/watchtower.nbt, read out of the player's own Minecraft as its textures
    /// are; with Minecraft's 5% of the overgrown one's mossy cobblestone and vines over it, its list pool element and
    /// outpost_rot processor) on open ground well away from the ship and the facility's doors, its eight ominous wall
    /// banners and its chest of Minecraft's chests/pillager_outpost loot, and pillagers about it (not a patrol's
    /// captains: Minecraft's structure spawns). [Outposts] OutpostChance: how many landings get one. Where the ground
    /// isn't level, the tower stands on its highest point and the ground under the rest is filled up to it with dirt
    /// (Minecraft's beard_thin terrain adaptation).
    /// </summary>
    public static class Outposts
    {
        /// <summary>Host: true while an outpost spawns its pillagers.</summary>
        public static bool Spawning;
        public static int Built; // (dev/tests)
        public static BlockKey? LastAt;
        public static string LastWhy = "-";

        static List<(int x, int y, int z, string id, Dictionary<string, string> props)> tower, overgrown;

        static bool Load()
        {
            if (tower != null) return true;
            try
            {
                var a = McAssets.ReadJar("data/minecraft/structure/pillager_outpost/watchtower.nbt");
                var b = McAssets.ReadJar("data/minecraft/structure/pillager_outpost/watchtower_overgrown.nbt");
                if (a == null) { LastWhy = "no watchtower in Minecraft's jar"; return false; }
                tower = Nbt.StructureBlocks(Nbt.ReadGzip(a));
                overgrown = b != null ? Nbt.StructureBlocks(Nbt.ReadGzip(b)) : new List<(int, int, int, string, Dictionary<string, string>)>();
                return true;
            }
            catch (System.Exception e) { LastWhy = "reading the watchtower: " + e.Message; Plugin.Log.LogWarning("[outposts] " + LastWhy); return false; }
        }

        // ------------------------------------------------------------------ as the ship lands
        static float landed = -100f;
        [HarmonyLib.HarmonyPatch(typeof(RoundManager), "FinishGeneratingNewLevelClientRpc"), HarmonyLib.HarmonyPostfix]
        static void Landed(RoundManager __instance)
        {
            if (!BlockNet.IsServer || TerrainCarver.AtCompany || PillagerAI.Type == null || !Landing.Once(ref landed)) return;
            if (Random.value >= Plugin.OutpostChance.Value) return;
            __instance.StartCoroutine(BuildLater());
        }

        static System.Collections.IEnumerator BuildLater()
        {
            yield return new WaitForSeconds(11f);
            try { ServerBuild(null); } catch (System.Exception e) { Plugin.Log.LogWarning("[outposts] " + e); }
        }

        /// <summary>Host: build one: on the best open ground (or, for the dev, near `near`). True if built.</summary>
        public static bool ServerBuild(Vector3? near)
        {
            var w = BlockWorld.Instance;
            if (w == null || !w.WorldFrameAvailable || !Load()) return false;
            if (!FindSite(near, out var origin, out var heights)) { Plugin.Log.LogInfo("[outposts] no room for one: " + LastWhy); return false; }
            var ops = new List<Op>();
            var rnd = new System.Random(Random.Range(int.MinValue, int.MaxValue));
            var placed = new Dictionary<Vector3Int, BlockData>();
            BlockKey? chest = null;
            void Put(int x, int y, int z, string id, Dictionary<string, string> props)
            {
                if (!ToBlock(id, props, out var data)) return;
                var pos = origin + new Vector3Int(x, y, z);
                placed[pos] = data;
                if (id == "chest") chest = Ground.KeyOf(pos);
            }
            foreach (var b in tower) Put(b.x, b.y, b.z, b.id, b.props);
            foreach (var b in overgrown) if (rnd.NextDouble() < 0.05) Put(b.x, b.y, b.z, b.id, b.props);
            // the ground under the footprint filled up to the tower's foot (beard_thin)
            foreach (var kv in heights)
                for (int y = kv.Value; y < origin.y; y++)
                {
                    var pos = new Vector3Int(kv.Key.x, y, kv.Key.y);
                    if (!placed.ContainsKey(pos) && !w.Has(Ground.KeyOf(pos)) && !Ground.IsUndugGround(Ground.KeyOf(pos))) placed[pos] = new BlockData(Blocks.Dirt.Id, (byte)Face.Up, 0);
                }
            foreach (var kv in placed) ops.Add(Op.Set(Ground.KeyOf(kv.Key), kv.Value));
            BlockNet.ServerBroadcastOps(ops);
            if (chest.HasValue) FillChest(chest.Value, rnd);
            Built++;
            LastAt = Ground.KeyOf(origin);
            Plugin.Log.LogInfo($"[outposts] a pillager outpost at {Ground.Center(origin)} ({placed.Count} blocks)");
            // pillagers about it, round its foot
            Spawning = true;
            try
            {
                int n = 2 + rnd.Next(3);
                for (int i = 0; i < n; i++)
                {
                    var spot = Ground.Center(origin + new Vector3Int(rnd.Next(-3, 18), 0, rnd.Next(-3, 18)));
                    if (!UnityEngine.AI.NavMesh.SamplePosition(spot, out var hit, 6f * BlockWorld.S, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    PillagerAI.NoPatrol(RoundManager.Instance.SpawnEnemyGameObject(hit.position, rnd.Next(0, 360), -1, PillagerAI.Type)); // (its Start comes a frame later: marked now)
                }
            }
            finally { Spawning = false; }
            return true;
        }

        /// <summary>Open ground for its 15 x 15 foot: a moon outside node at least 50 m from the ship and 30 m from every
        /// entrance, the one where the ground under the footprint is levelest (at most four blocks from its highest to its
        /// lowest). origin: its corner cell, its foot level with the highest ground; heights: each column's ground cell.</summary>
        static bool FindSite(Vector3? near, out Vector3Int origin, out Dictionary<Vector2Int, int> heights)
        {
            origin = default; heights = null;
            float S = BlockWorld.S;
            var ship = StartOfRound.Instance.elevatorTransform.position;
            var doors = Object.FindObjectsOfType<EntranceTeleport>().Where(e => e != null && e.isEntranceToBuilding).Select(e => e.transform.position).ToList();
            var candidates = new List<Vector3>();
            if (near.HasValue) for (int i = 0; i < 12; i++) candidates.Add(near.Value + Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward * (12f * S));
            else
            {
                var nodes = RoundManager.Instance.outsideAINodes;
                if (nodes != null)
                    foreach (var n in nodes.Where(n => n != null).OrderBy(_ => Random.value).Take(40))
                    {
                        var p = n.transform.position;
                        if (Vector3.Distance(p, ship) < 50f || doors.Any(d => Vector3.Distance(d, p) < 30f)) continue;
                        candidates.Add(p);
                    }
            }
            float bestSpread = float.MaxValue;
            foreach (var c in candidates)
            {
                var cell = Ground.CellOf(c);
                var corner = new Vector3Int(cell.x - 7, 0, cell.z - 7);
                var hs = new Dictionary<Vector2Int, int>();
                bool ok = true; int lo = int.MaxValue, hi = int.MinValue;
                for (int x = 0; x < 15 && ok; x++)
                    for (int z = 0; z < 15 && ok; z++)
                    {
                        var top = Ground.Center(new Vector3Int(corner.x + x, cell.y + 30, corner.z + z));
                        if (!Physics.Raycast(top, Vector3.down, out var hit, 60f * S, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.6f) { ok = false; break; }
                        int gy = Ground.CellOf(hit.point + Vector3.up * 0.05f).y;
                        hs[new Vector2Int(corner.x + x, corner.z + z)] = gy;
                        lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy);
                    }
                if (!ok) continue;
                float spread = hi - lo;
                if (spread > 4f || spread >= bestSpread) continue;
                bestSpread = spread;
                origin = new Vector3Int(corner.x, hi, corner.z);
                heights = hs;
            }
            if (heights == null) { LastWhy = candidates.Count == 0 ? "no outside nodes far enough from the ship and doors" : "no level enough ground"; return false; }
            return true;
        }

        // ------------------------------------------------------------------ Minecraft's blocks as ours
        /// <summary>Minecraft's facing names as ours (the structure's x and z are our grid's: Minecraft's north is -z, our
        /// South; its south +z, our North; west -x; east +x).</summary>
        static byte Dir(string mc)
        {
            switch (mc) { case "north": return (byte)Face.South; case "south": return (byte)Face.North; case "west": return (byte)Face.West; case "east": return (byte)Face.East; case "up": return (byte)Face.Up; default: return (byte)Face.Down; }
        }

        static bool ToBlock(string id, Dictionary<string, string> props, out BlockData data)
        {
            data = default;
            string P(string k) => props.TryGetValue(k, out var v) ? v : null;
            if (id == "air" || id == "structure_void" || id == "jigsaw") return false;
            string key = id == "dark_oak_planks" ? "dark_planks" : id == "white_wall_banner" ? Banners.OminousKey : id;
            var def = Blocks.Get(key);
            if (def == null) { Plugin.Log.LogWarning("[outposts] no block for minecraft:" + id); return false; }
            byte facing = (byte)Face.Up, state = 0;
            switch (def.Shape)
            {
                case BlockShape.Stairs:
                    // (Minecraft's facing is where the high side is; ours, where the low side is. half=top: upside down)
                    facing = Faces.Opposite(Dir(P("facing")));
                    if (P("half") == "top") state = Blocks.StairsUpsideDown;
                    break;
                case BlockShape.Slab: state = (byte)(P("type") == "top" ? 1 : P("type") == "double" ? 2 : 0); break;
                case BlockShape.Banner: facing = Dir(P("facing")); break;
                case BlockShape.Ladder:
                    // (a vine faces away from what it's on: Minecraft says which sides it's on)
                    if (def == Blocks.Vine) facing = P("east") == "true" ? (byte)Face.West : P("west") == "true" ? (byte)Face.East : P("north") == "true" ? (byte)Face.North : (byte)Face.South;
                    else facing = Dir(P("facing"));
                    break;
                default:
                    if (P("axis") != null) facing = P("axis") == "x" ? (byte)Face.East : P("axis") == "z" ? (byte)Face.North : (byte)Face.Up;
                    else if (P("facing") != null) facing = Dir(P("facing"));
                    break;
            }
            data = new BlockData(def.Id, facing, state);
            return true;
        }

        // ------------------------------------------------------------------ the chest: Minecraft's chests/pillager_outpost
        static void FillChest(BlockKey k, System.Random r)
        {
            var c = new Chests.Contents();
            Chests.All[k] = c;
            int slots = Chests.SlotsAt(k);
            var free = Enumerable.Range(0, slots).OrderBy(_ => r.Next()).ToList();
            void Add(string key, int n)
            {
                if (n <= 0 || free.Count == 0 || !ModItems.ByKey.ContainsKey(key)) return;
                int slot = free[0]; free.RemoveAt(0);
                Chests.PutInto(c, slot, key, n, false);
            }
            int U(int lo, int hi) => r.Next(lo, hi + 1);
            string Pick(params (string key, int w)[] e) { int t = e.Sum(x => x.w), v = r.Next(t); foreach (var x in e) { if ((v -= x.w) < 0) return x.key; } return e[0].key; }
            if (U(0, 1) == 1 && Crossbow.On) Add(Crossbow.Key, 1);
            for (int i = U(2, 3); i > 0; i--)
            {
                var f = Pick(("wheat", 7), ("potato", 5), ("carrot", 5));
                Add(f, f == "wheat" ? U(3, 5) : f == "potato" ? U(2, 5) : U(3, 5));
            }
            for (int i = U(1, 3); i > 0; i--) Add("dark_oak_log", U(2, 3));
            for (int i = U(2, 3); i > 0; i--)
            {
                // (Minecraft's book is a randomly enchanted one: the mod has no enchanted books, so a plain book)
                var f = Pick((ExperienceBottle.Key, 7), ("string", 4), ("arrow", 4), ("tripwire_hook", 3), ("iron_ingot", 3), ("book", 1));
                Add(f, f == "string" ? U(1, 6) : f == "arrow" ? U(2, 7) : f == "tripwire_hook" || f == "iron_ingot" ? U(1, 3) : 1);
            }
            if (U(0, 1) == 1) Add(GoatHorn.KeyPrefix + GoatHorn.Instruments[r.Next(GoatHorn.Instruments.Length)], 1);
            if (r.Next(4) == 0) Add("sentry_armor_trim_smithing_template", 2);
            if (r.Next(5) == 0) Add(NameTags.Key, 1); // (#228: the owner's, not Minecraft's outpost loot)
            BlockNet.ServerChest(k, c);
        }
    }
}
