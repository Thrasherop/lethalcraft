using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's copper (#160: the lightning rod and the copper bulb are copper): ore in the moons' stone (raw copper,
    /// smelted into ingots), and the copper blocks, which weather out in the open from copper through exposed and
    /// weathered to oxidized (about one stage a day here); an axe ([Right-click]) scrapes a stage off. The lightning rod
    /// draws the storm's strikes within 32 blocks onto itself (and gives a pulse of power when struck); the copper bulb
    /// toggles on each redstone pulse, dimmer the more it has weathered.
    /// </summary>
    [HarmonyPatch]
    public static class Copper
    {
        public static readonly string[] Stages = { "", "exposed_", "weathered_", "oxidized_" };
        public static readonly string[] StageNames = { "", "Exposed ", "Weathered ", "Oxidized " };
        /// <summary>Each copper block kind's four stages, in order (by key).</summary>
        static readonly Dictionary<string, string[]> lines = new Dictionary<string, string[]>();
        static readonly Dictionary<string, (string[] line, int stage)> stageOf = new Dictionary<string, (string[], int)>();
        public static int Weathered, Scraped, RodStrikes; // (dev/tests)
        public static float DevChance = -1f;

        /// <summary>(Blocks) a kind's four stages, registered for weathering and scraping.</summary>
        public static void AddLine(params string[] keys)
        {
            foreach (var k in keys) { lines[k] = keys; stageOf[k] = (keys, System.Array.IndexOf(keys, k)); }
        }

        public static int StageOf(BlockDef d) => d != null && stageOf.TryGetValue(d.Key, out var s) ? s.stage : -1;
        public static bool Scrapable(BlockDef d) => StageOf(d) > 0;

        static BlockDef Step(BlockDef d, int by)
        {
            if (d == null || !stageOf.TryGetValue(d.Key, out var s)) return null;
            int n = s.stage + by;
            return n < 0 || n > 3 ? null : Blocks.Get(s.line[n]);
        }

        // ------------------------------------------------------------------ weathering
        static float nextWeather;

        /// <summary>Host, each frame: every half minute, each copper block that isn't fully oxidized may weather a stage
        /// (about one stage per in-game day, ~13 minutes; Minecraft's is slower, its days shorter for building).</summary>
        public static void ServerTick(BlockWorld world)
        {
            if (!BlockNet.IsServer || Time.time < nextWeather || stageOf.Count == 0) return;
            nextWeather = Time.time + 30f;
            float chance = DevChance >= 0f ? DevChance : 30f / (13f * 60f);
            var ops = new List<Op>();
            foreach (var kv in world.Blocks)
            {
                var d = kv.Value.Data.Def;
                if (!stageOf.ContainsKey(d.Key) || Random.value >= chance) continue;
                var next = Step(d, 1);
                if (next == null) continue;
                var nd = kv.Value.Data; nd.Type = next.Id;
                ops.Add(Op.Set(kv.Key, nd));
            }
            if (ops.Count == 0) return;
            Weathered += ops.Count;
            BlockNet.ServerBroadcastOps(ops);
        }

        /// <summary>Host: an axe's right-click on weathered copper scrapes a stage off (Minecraft's).</summary>
        public static bool ServerScrape(GameNetcodeStuff.PlayerControllerB p, BlockKey k)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || !(p.currentlyHeldObjectServer is ToolItem t) || t.Kind != ToolKind.Axe) return false;
            var back = Step(b.Data.Def, -1);
            if (back == null) return false;
            var nd = b.Data; nd.Type = back.Id;
            BlockNet.ServerBroadcastOp(Op.Set(k, nd));
            BlockNet.ServerSound(w.WorldCenter(k), "dig.stone", 0.8f, 1.4f);
            if (!GameModes.IsCreative(p.actualClientId)) t.ServerUse(1);
            Scraped++;
            return true;
        }

        // ------------------------------------------------------------------ the copper bulb
        /// <summary>A bulb's light by its stage (Minecraft's 15, 12, 8, 4 light levels).</summary>
        public static float BulbLight(BlockDef d) => new[] { 4000f, 3000f, 1800f, 800f }[Mathf.Clamp(StageOf(d), 0, 3)];
        public static bool IsBulb(BlockDef d) => d != null && d.Key.EndsWith("copper_bulb");

        // ------------------------------------------------------------------ the lightning rod
        public static bool IsRod(BlockDef d) => d != null && d == Blocks.LightningRod;
        public const float RodRange = 32f;

        /// <summary>Every machine (the strike is the same everywhere): a strike within 32 blocks of a lightning rod with open
        /// sky above it lands on the rod instead; the host gives the rod its pulse.</summary>
        [HarmonyPatch(typeof(StormyWeather), nameof(StormyWeather.LightningStrike)), HarmonyPrefix]
        static void ToTheRod(ref Vector3 strikePosition)
        {
            var w = BlockWorld.Instance;
            if (w == null || Blocks.LightningRod == null) return;
            float best = RodRange * BlockWorld.S; BlockKey? rod = null; Vector3 top = default;
            foreach (var kv in w.Blocks)
            {
                if (kv.Value.Data.Def != Blocks.LightningRod) continue;
                var c = w.WorldCenter(kv.Key);
                float d = Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(strikePosition.x, 0f, strikePosition.z));
                if (d >= best) continue;
                if (Physics.Raycast(c + Vector3.up * (BlockWorld.S * 0.55f), Vector3.up, 300f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                best = d; rod = kv.Key; top = c + Vector3.up * (BlockWorld.S * 0.5f);
            }
            if (rod == null) return;
            strikePosition = top;
            if (!BlockNet.IsServer) return;
            RodStrikes++;
            var key = rod.Value;
            var b = w.Get(key);
            if (b == null) return;
            var nd = b.Data; nd.State = 1;
            BlockNet.ServerBroadcastOp(Op.State(key, nd));
            Redstone.MarkDirty();
            Redstone.Schedule(8, () =>
            {
                var bb = BlockWorld.Instance?.Get(key);
                if (bb == null || bb.Data.Def != Blocks.LightningRod) return;
                var d2 = bb.Data; d2.State = 0;
                BlockNet.ServerBroadcastOp(Op.State(key, d2));
                Redstone.MarkDirty();
            });
        }
    }

    public static partial class Blocks
    {
        public static BlockDef CopperOre, LightningRod;

        /// <summary>Copper (#160): the ores, then each kind's four stages, then the lightning rod. Called at the end of the
        /// block families (ids in that range, in this order).</summary>
        static void AddCopper()
        {
            CopperOre = Fam("copper_ore", "Copper Ore", "copper_ore", 3f, "Copper in the stone: raw copper (2-5), smelted into ingots.", tier: 2, cfg: x => { x.DropKey = "raw_copper"; x.DropMin = 2; x.DropMax = 5; });
            var deep = Fam("deepslate_copper_ore", "Deepslate Copper Ore", "deepslate_copper_ore", 4.5f, CopperOre.Description, tier: 2, cfg: x => { x.DropAs = CopperOre; x.OreBase = CopperOre; x.DropMin = 2; x.DropMax = 5; });
            deepOres[CopperOre] = deep;
            foreach (var (kind, name, tile) in new[] { ("copper", "Block of Copper", "copper_block"), ("cut_copper", "Cut Copper", "cut_copper"), ("chiseled_copper", "Chiseled Copper", "chiseled_copper"), ("copper_grate", "Copper Grate", "copper_grate") })
            {
                var keys = new string[4];
                for (int s = 0; s < 4; s++)
                {
                    string key = s == 0 && kind == "copper" ? "copper_block" : Copper.Stages[s] + kind;
                    string t = s == 0 ? tile : Copper.Stages[s] + kind;
                    string nm = s == 0 ? name : Copper.StageNames[s] + (kind == "copper" ? "Copper" : name);
                    keys[s] = key;
                    Fam(key, nm, t, 3f, s < 3 ? "Copper: it weathers in the open over the days. An axe scrapes it back." : "Fully oxidized copper. An axe scrapes it back.", tier: 1, sound: "metal",
                        cfg: x => { if (kind == "copper_grate") x.Render = RenderKind.Cutout; });
                }
                Copper.AddLine(keys);
                if (kind == "cut_copper")
                {
                    var stairs = new string[4]; var slabs = new string[4];
                    for (int s = 0; s < 4; s++)
                    {
                        Shapes(Get(keys[s]), keys[s], Get(keys[s]).Name);
                        stairs[s] = keys[s] + "_stairs"; slabs[s] = keys[s] + "_slab";
                    }
                    Copper.AddLine(stairs); Copper.AddLine(slabs);
                }
            }
            // the bulb, doors and trapdoors, by stage
            var bulbs = new string[4]; var doors = new string[4]; var traps = new string[4];
            for (int s = 0; s < 4; s++)
            {
                string pre = Copper.Stages[s], Pre = Copper.StageNames[s];
                bulbs[s] = pre + "copper_bulb";
                Fam(bulbs[s], Pre + "Copper Bulb", pre + "copper_bulb", 3f, "Each redstone pulse turns it on, or off. Dimmer the more it has weathered.", tier: 1, sound: "metal",
                    cfg: x => { x.IsRedstoneComponent = true; FamilyTiles[pre + "copper_bulb_lit"] = pre + "copper_bulb_lit"; });
                doors[s] = pre + "copper_door";
                Fam(doors[s], Pre + "Copper Door", pre + "copper_door_bottom", 3f, "A copper door: [E] or redstone opens it.", tier: 1, sound: "metal",
                    cfg: x => { x.Shape = BlockShape.Door; x.Directional = true; x.FacingIncludesVertical = false; x.Render = RenderKind.Cutout; x.TileFront = pre + "copper_door_top"; x.TileBottom = pre + "copper_door_bottom"; x.IsRedstoneComponent = true; FamilyTiles[pre + "copper_door_top"] = pre + "copper_door_top"; });
                traps[s] = pre + "copper_trapdoor";
                Fam(traps[s], Pre + "Copper Trapdoor", pre + "copper_trapdoor", 3f, "A copper trapdoor: [E] or redstone opens it.", tier: 1, sound: "metal",
                    cfg: x => { x.Shape = BlockShape.Trapdoor; x.Directional = true; x.FacingIncludesVertical = false; x.IsRedstoneComponent = true; });
            }
            Copper.AddLine(bulbs); Copper.AddLine(doors); Copper.AddLine(traps);
            LightningRod = Fam("lightning_rod", "Lightning Rod", "lightning_rod", 3f, "Draws the storm's lightning within 32 blocks onto itself (with open sky above it), away from you and your gear. Gives a pulse of power when struck.",
                tier: 1, sound: "metal", cfg: x => { x.Shape = BlockShape.Rod; x.Directional = true; x.IsRedstoneComponent = true; });
        }
    }
}
