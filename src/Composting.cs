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
        public static readonly Dictionary<string, float> Chance = new Dictionary<string, float>
        {
            ["wheat_seeds"] = 0.3f, ["pumpkin_seeds"] = 0.3f, ["leaves"] = 0.3f, ["vine"] = 0.5f, ["sugar_cane"] = 0.5f,
            ["wheat"] = 0.65f, ["carrot"] = 0.65f, ["potato"] = 0.65f, ["apple"] = 0.65f, ["pumpkin"] = 0.65f, ["carved_pumpkin"] = 0.65f,
            ["nether_wart"] = 0.65f, ["baked_potato"] = 0.85f, ["hay_block"] = 0.85f, ["golden_carrot"] = 1f,
        };
        public static int Composted, BoneMealMade; // (dev/tests)

        /// <summary>Host: one item into a composter at k (a player's or a hopper's). True if it took the item (Minecraft:
        /// it always takes it; the level only sometimes rises).</summary>
        public static bool ServerAdd(BlockKey k, string key)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            if (b == null || b.Data.Def != Blocks.Composter || key == null || !Chance.TryGetValue(ItemData.Base(key) ?? key, out float ch)) return false;
            int level = b.Data.State & 15;
            if (level >= 7) return false; // (full, or ready: nothing more goes in)
            var at = w.WorldCenter(k);
            if (Random.value < ch)
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
                if (st != null && st.Count > 0 && ServerAdd(k, st.ItemKey)) st.ServerSetCount(st.Count - 1, despawnIfEmpty: true);
                return;
            }
            if (b.Data.Def == Blocks.Cauldron)
            {
                // a glass bottle takes a third of its water; a water bottle puts one back
                int s = b.Data.State;
                if (st != null && st.ItemKey == "glass_bottle" && s >= 1 && s <= 3)
                {
                    st.ServerSetCount(st.Count - 1, despawnIfEmpty: true);
                    Inventory.ServerSpawnFor(sender, "potion_water", 1);
                    SetCauldron(k, s - 1);
                }
                else if (st != null && st.ItemKey == "potion_water" && s < 3)
                {
                    st.ServerSetCount(st.Count - 1, despawnIfEmpty: true);
                    Inventory.ServerSpawnFor(sender, "glass_bottle", 1);
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
    }
}
