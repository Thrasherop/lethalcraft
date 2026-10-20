using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's composter and cauldron (#160), both read by comparators. The composter: [E] with plant matter in hand
    /// (or a hopper above) may raise its level (Minecraft's chances: seeds and leaves 30%, wheat, carrots, potatoes,
    /// apples, pumpkins 65%...); full, it turns to bone meal, which [E] (or a hopper under it) takes. The cauldron holds
    /// three bottles' worth of water (a water bucket fills it, an empty bucket takes it back, a glass bottle takes a
    /// third) or a bucket of lava.
    /// </summary>
    public static class Composting
    {
        /// <summary>(#244) Minecraft 26.3's compostables (Items.java .compostable: low 30%, low-medium 50%, medium 65%,
        /// medium-high 85%, always), whichever of them this world has.</summary>
        public static readonly ItemData.TypeMap<float> Chance = new ItemData.TypeMap<float> // (#228: by type)
        {
            ["oak_sapling"] = 0.3f, ["spruce_sapling"] = 0.3f, ["birch_sapling"] = 0.3f, ["jungle_sapling"] = 0.3f, ["acacia_sapling"] = 0.3f,
            ["cherry_sapling"] = 0.3f, ["dark_oak_sapling"] = 0.3f, ["pale_oak_sapling"] = 0.3f, ["mangrove_propagule"] = 0.3f,
            ["poplar_sapling"] = 0.3f, ["mangrove_roots"] = 0.3f, ["oak_leaves"] = 0.3f, ["spruce_leaves"] = 0.3f, ["birch_leaves"] = 0.3f,
            ["jungle_leaves"] = 0.3f, ["acacia_leaves"] = 0.3f, ["cherry_leaves"] = 0.3f, ["dark_oak_leaves"] = 0.3f, ["pale_oak_leaves"] = 0.3f,
            ["mangrove_leaves"] = 0.3f, ["red_poplar_leaves"] = 0.3f, ["orange_poplar_leaves"] = 0.3f, ["yellow_poplar_leaves"] = 0.3f,
            ["azalea_leaves"] = 0.3f, ["short_grass"] = 0.3f, ["bush"] = 0.3f, ["red_shrub"] = 0.3f, ["firefly_bush"] = 0.3f,
            ["dry_short_grass"] = 0.3f, ["dry_tall_grass"] = 0.3f, ["seagrass"] = 0.3f, ["kelp"] = 0.3f, ["pink_petals"] = 0.3f,
            ["wildflowers"] = 0.3f, ["leaf_litter"] = 0.3f, ["moss_carpet"] = 0.3f, ["pale_moss_carpet"] = 0.3f, ["pale_hanging_moss"] = 0.3f,
            ["hanging_roots"] = 0.3f, ["small_dripleaf"] = 0.3f, ["cactus_flower"] = 0.3f, ["wheat_seeds"] = 0.3f, ["dried_kelp"] = 0.3f,
            ["pumpkin_seeds"] = 0.3f, ["melon_seeds"] = 0.3f, ["torchflower_seeds"] = 0.3f, ["pitcher_pod"] = 0.3f, ["beetroot_seeds"] = 0.3f,
            ["sweet_berries"] = 0.3f, ["glow_berries"] = 0.3f, ["leaves"] = 0.3f, ["grass"] = 0.3f,
            ["flowering_azalea_leaves"] = 0.5f, ["nether_sprouts"] = 0.5f, ["weeping_vines"] = 0.5f, ["twisting_vines"] = 0.5f,
            ["sugar_cane"] = 0.5f, ["cactus"] = 0.5f, ["vine"] = 0.5f, ["glow_lichen"] = 0.5f, ["tall_grass"] = 0.5f, ["dried_kelp_block"] = 0.5f,
            ["melon_slice"] = 0.5f,
            ["fern"] = 0.65f, ["azalea"] = 0.65f, ["sea_pickle"] = 0.65f, ["dandelion"] = 0.65f, ["open_eyeblossom"] = 0.65f,
            ["closed_eyeblossom"] = 0.65f, ["poppy"] = 0.65f, ["blue_orchid"] = 0.65f, ["allium"] = 0.65f, ["azure_bluet"] = 0.65f,
            ["red_tulip"] = 0.65f, ["orange_tulip"] = 0.65f, ["white_tulip"] = 0.65f, ["pink_tulip"] = 0.65f, ["oxeye_daisy"] = 0.65f,
            ["cornflower"] = 0.65f, ["lily_of_the_valley"] = 0.65f, ["wither_rose"] = 0.65f, ["spore_blossom"] = 0.65f, ["brown_mushroom"] = 0.65f,
            ["red_mushroom"] = 0.65f, ["shelf_mushroom"] = 0.65f, ["crimson_fungus"] = 0.65f, ["warped_fungus"] = 0.65f, ["crimson_roots"] = 0.65f,
            ["warped_roots"] = 0.65f, ["moss_block"] = 0.65f, ["pale_moss_block"] = 0.65f, ["big_dripleaf"] = 0.65f, ["pumpkin"] = 0.65f,
            ["carved_pumpkin"] = 0.65f, ["mushroom_stem"] = 0.65f, ["melon"] = 0.65f, ["lily_pad"] = 0.65f, ["sunflower"] = 0.65f, ["lilac"] = 0.65f,
            ["rose_bush"] = 0.65f, ["peony"] = 0.65f, ["large_fern"] = 0.65f, ["apple"] = 0.65f, ["wheat"] = 0.65f, ["cocoa_beans"] = 0.65f,
            ["nether_wart"] = 0.65f, ["carrot"] = 0.65f, ["potato"] = 0.65f, ["beetroot"] = 0.65f, ["shroomlight"] = 0.65f,
            ["flowering_azalea"] = 0.85f, ["torchflower"] = 0.85f, ["pitcher_plant"] = 0.85f, ["brown_mushroom_block"] = 0.85f,
            ["red_mushroom_block"] = 0.85f, ["hay_block"] = 0.85f, ["nether_wart_block"] = 0.85f, ["warped_wart_block"] = 0.85f, ["bread"] = 0.85f,
            ["straw_bed"] = 0.85f, ["cookie"] = 0.85f, ["baked_potato"] = 0.85f,
            ["cake"] = 1f, ["pumpkin_pie"] = 1f,
        };
        public static int Composted, BoneMealMade; // (dev/tests)

        /// <summary>Host: one item into a composter at k (a player's or a hopper's). True if it took the item (Minecraft:
        /// it always takes it; the level only sometimes rises).</summary>
        public static bool ServerAdd(BlockKey k, string key)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || b.Data.Def != Blocks.Composter || key == null || !Chance.TryGetValue(key, out float ch)) return false;
            int level = b.Data.State & 15;
            if (level >= 7) return false; // (full, or ready: nothing more goes in)
            var at = w.WorldCenter(k);
            if (level == 0 || Random.value < ch) // (#244: into an empty one it always counts, Minecraft's)
            {
                level++;
                var d = b.Data; d.State = (byte)level;
                BlockNet.ServerBroadcastOp(Op.State(k, d));
                Redstone.MarkDirty();
                if (level == 7)
                    Redstone.Schedule(20, () =>
                    {
                        var bb = BlockWorld.Instance?.Get(k);
                        if (bb == null || bb.Data.Def != Blocks.Composter || (bb.Data.State & 15) != 7) return;
                        var dd = bb.Data; dd.State = 8;
                        BlockNet.ServerBroadcastOp(Op.State(k, dd));
                        BlockNet.ServerSound(BlockWorld.Instance.WorldCenter(k), "bonemeal", 0.8f, 0.8f);
                        Redstone.MarkDirty();
                    });
            }
            BlockNet.ServerSound(at, "dig.grass", 0.6f, 1.2f);
            Composted++;
            return true;
        }

        /// <summary>Host: a ready composter's bone meal (for [E] or a hopper under it); null if it isn't ready.</summary>
        public static string ServerTakeBoneMeal(BlockKey k)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || b.Data.Def != Blocks.Composter || (b.Data.State & 15) != 8) return null;
            var d = b.Data; d.State = 0;
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            Redstone.MarkDirty();
            BoneMealMade++;
            return "bone_meal";
        }

        /// <summary>Host: [E] on a composter or a cauldron with what's in hand.</summary>
        public static void ServerUse(ulong sender, BlockKey k)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            var p = ServerLogic.PlayerFor(sender);
            if (b == null || p == null) return;
            var st = p.currentlyHeldObjectServer as StackItem;
            var top = w.WorldCenter(k) + Vector3.up * (BlockWorld.S * 0.6f);
            if (b.Data.Def == Blocks.Composter)
            {
                if ((b.Data.State & 15) == 8)
                {
                    if (ServerTakeBoneMeal(k) != null && ModItems.ByKey.TryGetValue("bone_meal", out var bm)) ModItems.ServerSpawnStack(bm, 1, top);
                    return;
                }
                if (st != null && st.Count > 0 && ServerAdd(k, st.ItemKey)) st.ServerUseUp(p);
                return;
            }
            if (b.Data.Def == Blocks.Cauldron)
            {
                // a glass bottle takes a third of its water; a water bottle puts one back
                int s = b.Data.State;
                if (LeatherDye.ServerWash(p, k, s)) return; // (#244: dyed leather washes clean)
                if (ServerWashBanner(p, k, s)) return; // (#244: a banner's top pattern washes off)
                if (st != null && st.ItemKey == "glass_bottle" && s >= 1 && s <= 3)
                {
                    st.ServerUseUp(p);
                    Inventory.ServerSpawnFor(sender, "potion_water", 1);
                    SetCauldron(k, s - 1);
                }
                else if (st != null && st.ItemKey == "potion_water" && s < 3)
                {
                    st.ServerUseUp(p);
                    if (!GameModes.IsCreative(p)) Inventory.ServerSpawnFor(sender, "glass_bottle", 1); // (creative: the water bottle stays)
                    SetCauldron(k, s + 1);
                }
            }
        }

        /// <summary>Host: a cauldron's contents (0 empty, 1-3 water, 4 lava).</summary>
        public static void SetCauldron(BlockKey k, int state)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || b.Data.Def != Blocks.Cauldron) return;
            var d = b.Data; d.State = (byte)state;
            BlockNet.ServerBroadcastOp(Op.State(k, d));
            BlockNet.ServerSound(w.WorldCenter(k), "bucket.empty", 0.5f, 1.2f);
            Redstone.MarkDirty();
        }

        /// <summary>What a comparator reads: a composter's level, a cauldron's water (lava: 3); -1 for anything else.</summary>
        public static int ComparatorLevel(BlockInstance b) =>
            b.Data.Def == Blocks.Composter ? b.Data.State & 15 : b.Data.Def == Blocks.Cauldron ? (b.Data.State == 4 ? 3 : b.Data.State & 3) : -1;

        public static string HoverTip(BlockInstance b)
        {
            if (b.Data.Def == Blocks.Composter) { int l = b.Data.State & 15; return l == 8 ? "Composter - take the bone meal : [E]" : $"Composter ({l}/7) - compost : [E]"; }
            if (b.Data.Def == Blocks.Cauldron) return b.Data.State == 4 ? "Cauldron (lava)" : $"Cauldron (water {b.Data.State & 3}/3) : [E]";
            return null;
        }

        // ------------------------------------------------------------------ (#244) the cauldron: banners, rain
        public static int BannersWashed, RainFills; // (dev/tests)
        public static float DevRainChance = -1f;

        /// <summary>Host: Minecraft's CauldronInteraction.BANNER: a patterned banner in a water cauldron loses its top
        /// pattern (the last one, gone: a plain banner of its colour), for a third of the water.</summary>
        static bool ServerWashBanner(GameNetcodeStuff.PlayerControllerB p, BlockKey k, int level)
        {
            var g = p != null ? p.currentlyHeldObjectServer : null;
            if (level < 1 || level > 3 || g == null) return false;
            var key = Crafting.KeyOf(g);
            int id = BannerDesigns.IdOf(key);
            if (id <= 0 || !BannerDesigns.Designs.TryGetValue(id, out var spec)) return false;
            var (b, layers) = BannerDesigns.Parse(spec);
            if (b < 0 || layers.Count == 0) return false;
            layers.RemoveAt(layers.Count - 1);
            string washed = layers.Count == 0 ? BannerDesigns.BlockKeyFor(b) : ItemData.With(BannerDesigns.ItemKey, BannerDesigns.ServerRegister(BannerDesigns.Spec(b, layers)));
            washed = ItemData.WithName(washed, ItemNames.IdOf(g));
            BlockNet.ServerTakeHeld(g);
            Inventory.ServerSpawnFor(p.actualClientId, washed, 1, true, p.currentItemSlot);
            SetCauldron(k, level - 1);
            BannersWashed++;
            return true;
        }

        static float nextRain;
        /// <summary>Host, each second: Minecraft's precipitation tick: a column of a chunk is picked one tick in 48 (one of its
        /// 256), and rain falling on a cauldron there fills it a third one time in 20: about once in 3.4 hours for each
        /// cauldron out in the rain.</summary>
        public static void ServerRainTick()
        {
            if (Time.time < nextRain || Blocks.Cauldron == null) return;
            nextRain = Time.time + 1f;
            var lvl = StartOfRound.Instance?.currentLevel;
            if (lvl == null || !(lvl.currentWeather == LevelWeatherType.Rainy || lvl.currentWeather == LevelWeatherType.Stormy || lvl.currentWeather == LevelWeatherType.Flooded)) return;
            var w = BlockWorld.Instance;
            if (w == null) return;
            var tod = TimeOfDay.Instance;
            // (owner, #244: a cauldron out in the rain fills about halfway over a rainy moon's day: a level and a half a day;
            // Minecraft's own rate, a level in about half an hour of rain, would barely show in a day here)
            float chance = DevRainChance >= 0f ? DevRainChance : 1.5f / Mathf.Max(60f, tod != null ? tod.totalTime : 900f);
            foreach (var kv in System.Linq.Enumerable.ToList(w.Blocks))
            {
                if (kv.Value.Data.Def != Blocks.Cauldron || (kv.Value.Data.State & 15) >= 3 || Random.value >= chance) continue;
                var at = w.WorldCenter(kv.Key);
                if (at.y < -100f || Physics.Raycast(at + Vector3.up * (BlockWorld.S * 0.55f), Vector3.up, 200f, StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                SetCauldron(kv.Key, (kv.Value.Data.State & 15) + 1);
                RainFills++;
            }
        }
    }
}
