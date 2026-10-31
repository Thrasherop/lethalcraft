using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Fire, like Minecraft's. Flint and steel lights it on whatever surface you strike (the ground or a block), or
    /// lights TNT. On stone or dirt it burns out after a few seconds; next to flammable blocks (wood, planks, leaves,
    /// wool, bookshelves, crafting tables, note blocks) it keeps going, spreads to them and burns them away (no drops),
    /// and lights TNT it touches. Standing in it hurts, monsters standing in it get hit too, and a punch puts it out.
    /// The server runs the fire; each client hurts its own player.
    /// </summary>
    public static class Fire
    {
        static BlockWorld W => BlockWorld.Instance;
        static readonly Dictionary<BlockKey, int> age = new Dictionary<BlockKey, int>();
        static readonly System.Random rng = new System.Random();

        public static void Reset() => age.Clear();

        static bool IsFire(BlockInstance b) => b != null && Blocks.IsFireDef(b.Data.Def);

        /// <summary>
        /// Something to burn on: a solid block under it, or the ground anywhere from the middle of its cell to just
        /// under it (a fire on the moons' grid often sits partly in uneven ground; a ray from inside the ground, or a box
        /// wholly inside it, wouldn't see that ground).
        /// </summary>
        static bool HasGround(BlockKey k)
        {
            var world = W;
            var below = world.Get(k.Offset((int)Face.Down));
            if (below != null && below.Data.Def.Solid) return true;
            var c = world.WorldCenter(k);
            var down = world.FrameDirToWorld(k.Frame, Vector3.down);
            var right = world.FrameDirToWorld(k.Frame, Vector3.right);
            var fwd = world.FrameDirToWorld(k.Frame, Vector3.forward);
            float S = BlockWorld.S;
            // over the same 3x3 footprint placement rests a block on (its highest ground point): on a slope the ground
            // right under the middle can be well below the cell
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                {
                    var o = c - down * (S * 0.1f) + (right * sx + fwd * sz) * (S * 0.4f);
                    // (down to the middle of the cell below: ground lying low in it, which placement puts the fire
                    // above, still counts; it reached 0.25 into it, and a fire on such ground wouldn't light)
                    foreach (var h in Physics.RaycastAll(o, down, S * 1.1f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
                        if (h.collider.GetComponentInParent<BlockRef>() == null) return true;
                }
            return false;
        }

        static bool NextToFuel(BlockKey k)
        {
            for (int f = 0; f < 6; f++)
            {
                var n = W.Get(k.Offset(f));
                if (n != null && (n.Data.Def.Flammable || n.Data.Def == Blocks.TNT)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ server
        /// <summary>Server: flint and steel struck at a cell: light TNT there, or start a fire in the (empty) cell.</summary>
        public static void ServerLight(ulong sender, BlockKey k)
        {
            var world = W;
            if (world == null) return;
            var b = world.Get(k);
            if (b != null) { if (b.Data.Def == Blocks.TNT) ServerLogic.Ignite(k, 80); return; }
            var p = ServerLogic.PlayerFor(sender);
            // inside an obsidian frame: a Nether portal (#66)
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, world.WorldCenter(k)) <= 9f * BlockWorld.S + 3f && NetherPortal.ServerTryLight(k)) return;
            string why = null;
            if (p != null && Vector3.Distance(p.gameplayCamera.transform.position, world.WorldCenter(k)) > 9f * BlockWorld.S + 3f) why = "out of reach";
            else if (k.Frame == 0 && !world.WorldFrameAvailable) why = "no world frame";
            // only the middle of the cell must be clear: a fire sits on (and a little into) uneven ground
            else if (Redstone.OutOfWorld(k)) why = "out of the world";
            else if (ServerLogic.Obstructed(k, 0.6f)) why = "obstructed";
            else if (!HasGround(k) && !NextToFuel(k)) why = "nothing to burn on"; // needs something to burn on
            if (why != null)
            {
                if (Plugin.DevMode.Value)
                {
                    // (how far down the ground really is, and what it is)
                    var c0 = world.WorldCenter(k); var dn = world.FrameDirToWorld(k.Frame, Vector3.down);
                    string ground = "none within 4 blocks";
                    foreach (var h in Physics.RaycastAll(c0, dn, BlockWorld.S * 4f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                        if (h.collider.GetComponentInParent<BlockRef>() == null) { ground = $"{h.collider.name} L{h.collider.gameObject.layer} {h.distance / BlockWorld.S:F2} blocks below the middle"; break; }
                    Plugin.Log.LogInfo($"[dev] no fire at {k}: {why} (ground: {ground})");
                }
                return;
            }
            Place(k, null);
        }

        /// <summary>Server: a fire where something burning landed (a blaze's fireball), if the cell there can hold one.</summary>
        public static void ServerIgniteAt(Vector3 world)
        {
            var w = W;
            if (w == null) return;
            var k = Ground.KeyOf(Ground.CellOf(world));
            if (w.Has(k) || Redstone.OutOfWorld(k) || ServerLogic.Obstructed(k, 0.6f) || (!HasGround(k) && !NextToFuel(k))) return;
            Place(k, null);
        }

        /// <summary>Host: fire put here (a dispenser's flint and steel): soul fire on soul sand or soul soil.</summary>
        public static void ServerPlace(BlockKey k) => Place(k, null);

        static void Place(BlockKey k, List<Op> ops)
        {
            age[k] = 0;
            var w = W;
            // (#244: on soul sand or soul soil it's soul fire, Minecraft's)
            var def = w != null && SoulBase(Below(w, k)) && Blocks.SoulFire != null ? Blocks.SoulFire : Blocks.Fire;
            var op = Op.Set(k, new BlockData(def.Id, (byte)Face.Up, 0));
            if (ops != null) ops.Add(op); else BlockNet.ServerBroadcastOp(op);
        }

        // ------------------------------------------------------------------ (#244) Minecraft 26.3's FireBlock
        /// <summary>Each block's own odds (FireBlock.bootStrap): how readily fire spreads beside it (ignite), how readily it
        /// burns away (burn).</summary>
        static readonly Dictionary<string, (int ignite, int burn)> Odds = new Dictionary<string, (int, int)>
        {
            ["oak_planks"] = (5, 20), ["spruce_planks"] = (5, 20), ["birch_planks"] = (5, 20), ["jungle_planks"] = (5, 20),
            ["acacia_planks"] = (5, 20), ["cherry_planks"] = (5, 20), ["dark_oak_planks"] = (5, 20), ["pale_oak_planks"] = (5, 20),
            ["poplar_planks"] = (5, 20), ["mangrove_planks"] = (5, 20), ["bamboo_planks"] = (5, 20), ["bamboo_mosaic"] = (5, 20),
            ["oak_slab"] = (5, 20), ["spruce_slab"] = (5, 20), ["birch_slab"] = (5, 20), ["jungle_slab"] = (5, 20), ["acacia_slab"] = (5, 20),
            ["cherry_slab"] = (5, 20), ["dark_oak_slab"] = (5, 20), ["pale_oak_slab"] = (5, 20), ["poplar_slab"] = (5, 20),
            ["mangrove_slab"] = (5, 20), ["bamboo_slab"] = (5, 20), ["bamboo_mosaic_slab"] = (5, 20), ["oak_fence_gate"] = (5, 20),
            ["spruce_fence_gate"] = (5, 20), ["birch_fence_gate"] = (5, 20), ["jungle_fence_gate"] = (5, 20), ["acacia_fence_gate"] = (5, 20),
            ["cherry_fence_gate"] = (5, 20), ["dark_oak_fence_gate"] = (5, 20), ["pale_oak_fence_gate"] = (5, 20), ["poplar_fence_gate"] = (5, 20),
            ["mangrove_fence_gate"] = (5, 20), ["bamboo_fence_gate"] = (5, 20), ["oak_fence"] = (5, 20), ["spruce_fence"] = (5, 20),
            ["birch_fence"] = (5, 20), ["jungle_fence"] = (5, 20), ["acacia_fence"] = (5, 20), ["cherry_fence"] = (5, 20),
            ["dark_oak_fence"] = (5, 20), ["pale_oak_fence"] = (5, 20), ["poplar_fence"] = (5, 20), ["mangrove_fence"] = (5, 20),
            ["bamboo_fence"] = (5, 20), ["oak_stairs"] = (5, 20), ["birch_stairs"] = (5, 20), ["spruce_stairs"] = (5, 20),
            ["jungle_stairs"] = (5, 20), ["acacia_stairs"] = (5, 20), ["cherry_stairs"] = (5, 20), ["dark_oak_stairs"] = (5, 20),
            ["pale_oak_stairs"] = (5, 20), ["poplar_stairs"] = (5, 20), ["mangrove_stairs"] = (5, 20), ["bamboo_stairs"] = (5, 20),
            ["bamboo_mosaic_stairs"] = (5, 20), ["oak_log"] = (5, 5), ["spruce_log"] = (5, 5), ["birch_log"] = (5, 5), ["jungle_log"] = (5, 5),
            ["acacia_log"] = (5, 5), ["cherry_log"] = (5, 5), ["pale_oak_log"] = (5, 5), ["poplar_log"] = (5, 5), ["dark_oak_log"] = (5, 5),
            ["mangrove_log"] = (5, 5), ["bamboo_block"] = (5, 5), ["stripped_oak_log"] = (5, 5), ["stripped_spruce_log"] = (5, 5),
            ["stripped_birch_log"] = (5, 5), ["stripped_jungle_log"] = (5, 5), ["stripped_acacia_log"] = (5, 5), ["stripped_cherry_log"] = (5, 5),
            ["stripped_dark_oak_log"] = (5, 5), ["stripped_pale_oak_log"] = (5, 5), ["stripped_poplar_log"] = (5, 5),
            ["stripped_mangrove_log"] = (5, 5), ["stripped_bamboo_block"] = (5, 5), ["stripped_oak_wood"] = (5, 5),
            ["stripped_spruce_wood"] = (5, 5), ["stripped_birch_wood"] = (5, 5), ["stripped_jungle_wood"] = (5, 5),
            ["stripped_acacia_wood"] = (5, 5), ["stripped_cherry_wood"] = (5, 5), ["stripped_dark_oak_wood"] = (5, 5),
            ["stripped_pale_oak_wood"] = (5, 5), ["stripped_poplar_wood"] = (5, 5), ["stripped_mangrove_wood"] = (5, 5), ["oak_wood"] = (5, 5),
            ["spruce_wood"] = (5, 5), ["birch_wood"] = (5, 5), ["jungle_wood"] = (5, 5), ["acacia_wood"] = (5, 5), ["cherry_wood"] = (5, 5),
            ["pale_oak_wood"] = (5, 5), ["poplar_wood"] = (5, 5), ["dark_oak_wood"] = (5, 5), ["mangrove_wood"] = (5, 5),
            ["mangrove_roots"] = (5, 20), ["oak_leaves"] = (30, 60), ["spruce_leaves"] = (30, 60), ["birch_leaves"] = (30, 60),
            ["jungle_leaves"] = (30, 60), ["acacia_leaves"] = (30, 60), ["cherry_leaves"] = (30, 60), ["dark_oak_leaves"] = (30, 60),
            ["pale_oak_leaves"] = (30, 60), ["mangrove_leaves"] = (30, 60), ["red_poplar_leaves"] = (30, 60), ["orange_poplar_leaves"] = (30, 60),
            ["yellow_poplar_leaves"] = (30, 60), ["bookshelf"] = (30, 20), ["tnt"] = (15, 100), ["short_grass"] = (60, 100), ["fern"] = (60, 100),
            ["dead_bush"] = (60, 100), ["short_dry_grass"] = (60, 100), ["tall_dry_grass"] = (60, 100), ["sunflower"] = (60, 100),
            ["lilac"] = (60, 100), ["rose_bush"] = (60, 100), ["peony"] = (60, 100), ["red_shrub"] = (60, 100), ["tall_grass"] = (60, 100),
            ["large_fern"] = (60, 100), ["dandelion"] = (60, 100), ["golden_dandelion"] = (60, 100), ["poppy"] = (60, 100),
            ["open_eyeblossom"] = (60, 100), ["closed_eyeblossom"] = (60, 100), ["blue_orchid"] = (60, 100), ["allium"] = (60, 100),
            ["azure_bluet"] = (60, 100), ["red_tulip"] = (60, 100), ["orange_tulip"] = (60, 100), ["white_tulip"] = (60, 100),
            ["pink_tulip"] = (60, 100), ["oxeye_daisy"] = (60, 100), ["cornflower"] = (60, 100), ["lily_of_the_valley"] = (60, 100),
            ["torchflower"] = (60, 100), ["pitcher_plant"] = (60, 100), ["wither_rose"] = (60, 100), ["pink_petals"] = (60, 100),
            ["wildflowers"] = (60, 100), ["leaf_litter"] = (60, 100), ["cactus_flower"] = (60, 100), ["vine"] = (15, 100), ["coal_block"] = (5, 5),
            ["hay_block"] = (60, 20), ["target"] = (15, 20), ["pale_moss_block"] = (5, 100), ["pale_moss_carpet"] = (5, 100),
            ["pale_hanging_moss"] = (5, 100), ["dried_kelp_block"] = (30, 60), ["bamboo"] = (60, 60), ["scaffolding"] = (60, 60),
            ["lectern"] = (30, 20), ["composter"] = (5, 20), ["sweet_berry_bush"] = (60, 100), ["beehive"] = (5, 20), ["bee_nest"] = (30, 20),
            ["azalea_leaves"] = (30, 60), ["flowering_azalea_leaves"] = (30, 60), ["cave_vines"] = (15, 60), ["cave_vines_plant"] = (15, 60),
            ["spore_blossom"] = (60, 100), ["azalea"] = (30, 60), ["flowering_azalea"] = (30, 60), ["big_dripleaf"] = (60, 100),
            ["big_dripleaf_stem"] = (60, 100), ["small_dripleaf"] = (60, 100), ["hanging_roots"] = (30, 60), ["glow_lichen"] = (15, 100),
            ["firefly_bush"] = (60, 100), ["bush"] = (60, 100), ["acacia_shelf"] = (30, 20), ["bamboo_shelf"] = (30, 20), ["birch_shelf"] = (30, 20),
            ["cherry_shelf"] = (30, 20), ["dark_oak_shelf"] = (30, 20), ["jungle_shelf"] = (30, 20), ["mangrove_shelf"] = (30, 20),
            ["poplar_shelf"] = (30, 20), ["oak_shelf"] = (30, 20), ["pale_oak_shelf"] = (30, 20), ["spruce_shelf"] = (30, 20),
            ["straw_bed"] = (30, 60),
        };
        public static (int ignite, int burn) OddsOf(BlockDef d)
        {
            if (d == null) return (0, 0);
            if (Odds.TryGetValue(d.Key, out var o)) return o;
            string k = d.Key;
            if (k == "leaves" || k.EndsWith("_leaves")) return (30, 60);
            if (k.StartsWith("wool") || k.EndsWith("_wool")) return (30, 60);
            if (k.EndsWith("_carpet")) return (60, 20);
            if (k.Contains("crimson") || k.Contains("warped")) return (0, 0); // (the Nether's woods don't burn)
            if (k.EndsWith("_log") || k.EndsWith("_wood")) return (5, 5);
            if (d.Flammable && (k == "planks" || k.EndsWith("_planks") || k.EndsWith("_stairs") || k.EndsWith("_slab") || k.EndsWith("_fence") || k.EndsWith("_fence_gate"))) return (5, 20);
            if (d.Shape == BlockShape.Plant) return (60, 100);
            return d.Flammable ? (5, 20) : (0, 0);
        }

        static bool Raining()
        {
            var lvl = StartOfRound.Instance?.currentLevel;
            return lvl != null && (lvl.currentWeather == LevelWeatherType.Rainy || lvl.currentWeather == LevelWeatherType.Stormy || lvl.currentWeather == LevelWeatherType.Flooded);
        }

        /// <summary>Rain falls on a cell: open sky over it, outside.</summary>
        static bool RainAt(BlockWorld w, BlockKey k)
        {
            var c = w.WorldCenter(k);
            return c.y > -100f && !Physics.Raycast(c + Vector3.up * BlockWorld.S * 0.45f, Vector3.up, 200f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
        }
        static bool NearRain(BlockWorld w, BlockKey k)
        {
            if (RainAt(w, k)) return true;
            foreach (int f in new[] { (int)Face.North, (int)Face.South, (int)Face.West, (int)Face.East }) if (RainAt(w, k.Offset(f))) return true;
            return false;
        }

        static bool SoulBase(BlockDef d) => d == Blocks.SoulSand || d == Blocks.SoulSoil;
        static BlockDef Below(BlockWorld w, BlockKey k) => w.DefAt(k.Offset((int)Face.Down)) ?? Ground.MaterialUnder(k);
        static bool CanBurn(BlockDef d) => OddsOf(d).ignite > 0;
        static bool ValidLocation(BlockWorld w, BlockKey k) { for (int f = 0; f < 6; f++) if (CanBurn(w.DefAt(k.Offset(f)))) return true; return false; }
        static int IgniteOddsAt(BlockWorld w, BlockKey k)
        {
            int best = 0;
            for (int f = 0; f < 6; f++) best = System.Math.Max(best, OddsOf(w.DefAt(k.Offset(f))).ignite);
            return best;
        }

        static readonly Dictionary<BlockKey, float> nextTick = new Dictionary<BlockKey, float>();
        public static int RainedOut, Spreads, BurnedAway, LavaFires; // (dev/tests)
        static float Delay() => (30 + rng.Next(10)) / 20f; // (Minecraft's getFireTickDelay)

        /// <summary>Server, twice a second: each fire's own tick (Minecraft's every 30-39 game ticks): rain, age, what
        /// burns away beside it, and where it spreads.</summary>
        public static void ServerTick(bool hurtMonsters)
        {
            var world = W;
            if (world == null || !BlockNet.IsServer) return;
            var fires = world.Blocks.Where(kv => IsFire(kv.Value)).Select(kv => kv.Key).ToList();
            foreach (var k in age.Keys.ToList()) if (!IsFire(world.Get(k))) age.Remove(k);
            foreach (var k in nextTick.Keys.ToList()) if (!IsFire(world.Get(k))) nextTick.Remove(k);
            ServerLavaTick(world);
            if (fires.Count == 0) return;
            bool raining = Raining();
            var ops = new List<Op>();
            var lit = new Dictionary<BlockKey, int>();
            foreach (var k in fires)
            {
                var b = world.Get(k);
                var below = Below(world, k);
                if (b.Data.Def == Blocks.SoulFire)
                {
                    // (Minecraft's SoulFireBlock: only on soul sand or soul soil; it never spreads nor burns out)
                    if (!SoulBase(below)) { ops.Add(Op.Remove(k, false)); age.Remove(k); }
                    continue;
                }
                if (!nextTick.TryGetValue(k, out float due)) { nextTick[k] = Time.time + Delay(); continue; }
                if (Time.time < due) continue;
                nextTick[k] = Time.time + Delay();
                bool infini = below == Blocks.Netherrack || below == Blocks.Magma; // (the Overworld's infiniburn: netherrack, magma)
                age.TryGetValue(k, out int a);
                if (!infini && raining && NearRain(world, k) && rng.NextDouble() < 0.2 + a * 0.03) { ops.Add(Op.Remove(k, false)); age.Remove(k); RainedOut++; BlockNet.ServerSound(world.WorldCenter(k), "extinguish", 0.5f, 1.2f); continue; }
                age[k] = System.Math.Min(15, a + rng.Next(3) / 2);
                if (!infini)
                {
                    if (!ValidLocation(world, k))
                    {
                        if (!HasGround(k) || a > 3) { ops.Add(Op.Remove(k, false)); age.Remove(k); }
                        continue;
                    }
                    if (a == 15 && rng.Next(4) == 0 && !CanBurn(below)) { ops.Add(Op.Remove(k, false)); age.Remove(k); continue; }
                }
                // what's beside it burns away, now and then leaving fire where it was (TNT: lit)
                for (int f = 0; f < 6; f++)
                {
                    var nk = k.Offset(f);
                    var n = world.Get(nk);
                    if (n == null) continue;
                    int burn = n.Data.Def == Blocks.TNT ? 100 : OddsOf(n.Data.Def).burn;
                    if (burn <= 0 || rng.Next(f <= 1 ? 250 : 300) >= burn) continue;
                    if (n.Data.Def == Blocks.TNT) { ServerLogic.Ignite(nk, 80); continue; }
                    ServerLogic.BreakBlock(nk, false);
                    BurnedAway++;
                    if (rng.Next(a + 10) < 5 && !(raining && RainAt(world, nk))) lit[nk] = System.Math.Min(15, a + rng.Next(5) / 4);
                }
                // it spreads to open cells beside burnable blocks: a block round it, four up (less likely the higher)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 4; dy++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            var c = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(dx, dy, dz));
                            if (world.Get(c) != null || lit.ContainsKey(c)) continue;
                            int ignite = IgniteOddsAt(world, c);
                            if (ignite <= 0) continue;
                            int odds = (ignite + 40 + 2 * 7) / (a + 30); // (Normal difficulty)
                            int rate = 100 + (dy > 1 ? (dy - 1) * 100 : 0);
                            if (odds > 0 && rng.Next(rate) <= odds && !(raining && NearRain(world, c))) { lit[c] = System.Math.Min(15, a + rng.Next(5) / 4); Spreads++; }
                        }
            }
            foreach (var kv in lit)
                if (world.Get(kv.Key) == null && !ServerLogic.Obstructed(kv.Key, 0.6f) && !Redstone.OutOfWorld(kv.Key) && !Ground.IsSolidCell(Ground.CellOf(world.WorldCenter(kv.Key)))) { Place(kv.Key, ops); age[kv.Key] = kv.Value; }
            BlockNet.ServerBroadcastOps(ops);
            if (hurtMonsters) HurtMonsters(world);
        }

        /// <summary>(#244, #145) Minecraft's LavaFluid.randomTick (three random ticks a game tick in 4096 blocks): now and then
        /// lava sets fire near it: in the air up to three blocks over it beside something burnable, or on top of a burnable
        /// block beside it.</summary>
        static void ServerLavaTick(BlockWorld w)
        {
            const double Chance = 10 * 3 / 4096.0; // (half a second: 10 game ticks)
            foreach (var kv in w.Blocks.Where(x => x.Value.Data.Def == Blocks.Lava).ToList())
            {
                if (rng.NextDouble() >= Chance) continue;
                var k = kv.Key;
                int passes = rng.Next(3);
                if (passes > 0)
                {
                    var p = k;
                    for (int i = 0; i < passes; i++)
                    {
                        p = new BlockKey(p.Frame, p.YOff, p.Pos + new Vector3Int(rng.Next(3) - 1, 1, rng.Next(3) - 1));
                        var there = w.Get(p);
                        bool empty = there == null && !Ground.IsSolidCell(Ground.CellOf(w.WorldCenter(p)));
                        if (empty) { if (IgniteOddsAt(w, p) > 0 && !ServerLogic.Obstructed(p, 0.6f)) { Place(p, null); LavaFires++; break; } }
                        else if (there == null || there.Data.Def.Solid) break;
                    }
                }
                else
                    for (int i = 0; i < 3; i++)
                    {
                        var p2 = new BlockKey(k.Frame, k.YOff, k.Pos + new Vector3Int(rng.Next(3) - 1, 0, rng.Next(3) - 1));
                        var up = p2.Offset((int)Face.Up);
                        if (w.Get(up) == null && !Ground.IsSolidCell(Ground.CellOf(w.WorldCenter(up))) && OddsOf(w.DefAt(p2)).ignite > 0 && !ServerLogic.Obstructed(up, 0.6f)) { Place(up, null); LavaFires++; }
                    }
            }
        }

        static void HurtMonsters(BlockWorld world)
        {
            var fires = world.Blocks.Values.Where(IsFire).Select(b => world.WorldCenter(b.Key)).ToList();
            if (fires.Count == 0) return;
            float h = BlockWorld.S * 0.5f;
            foreach (var e in Object.FindObjectsOfType<EnemyAI>())
            {
                if (e == null || e.isEnemyDead || !e.IsSpawned) continue;
                var p = e.transform.position;
                foreach (var c in fires)
                {
                    if (Mathf.Abs(p.x - c.x) > h || Mathf.Abs(p.z - c.z) > h || p.y < c.y - h - 0.4f || p.y > c.y + h) continue;
                    PetDeaths.Context = "inFire"; // (#232)
                    try { e.HitEnemyOnLocalClient(1, Vector3.zero, null, true, -1); } catch (System.Exception ex) { Plugin.Log.LogWarning("Fire hit " + e.name + ": " + ex.Message); }
                    finally { PetDeaths.Context = null; }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ every client
        static float nextHurt, nextCrackle;

        /// <summary>Every client: its own player standing in fire gets hurt (the game's damage, so god/creative rules apply).</summary>
        public static void ClientTick()
        {
            var world = W;
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (world == null || p == null) return;
            if (Time.time >= nextCrackle)
            {
                nextCrackle = Time.time + 1.6f;
                var near = world.Blocks.Values.Where(b => IsFire(b) && b.Go != null && Vector3.Distance(b.Go.transform.position, p.transform.position) < 16f).ToList();
                if (near.Count > 0)
                {
                    var f = near[Random.Range(0, near.Count)];
                    Sounds.Play("fire", f.Go.transform.position, Random.Range(0.7f, 1.0f), 0.55f);
                }
            }
            if (Time.time < nextHurt || p.isPlayerDead || !p.isPlayerControlled) return;
            nextHurt = Time.time + 0.5f;
            var pb = p.thisController.bounds;
            foreach (var b in world.Blocks.Values)
            {
                if (!IsFire(b) || b.Go == null) continue;
                var cell = new Bounds(b.Go.transform.position, Vector3.one * BlockWorld.S * 0.85f);
                if (!cell.Intersects(pb)) continue;
                // Minecraft: half a heart every half second in fire (a tenth of your health a second)
                p.DamagePlayer(b.Data.Def == Blocks.SoulFire ? 10 : 5, hasDamageSFX: true, callRPC: true, causeOfDeath: CauseOfDeath.Burning); // (#244: soul fire, twice)
                break;
            }
        }
    }
}
