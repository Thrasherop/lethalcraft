using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's decorated pot (#160; DecoratedPotBlock): it holds one stack. [E] with something in hand puts one in (the
    /// same thing, up to a full stack); empty-handed, or full, it only clinks. Broken by hand it drops whole; with a tool
    /// (Minecraft's breaks_decorated_pots: every tool) it shatters into its four bricks. Either way what's in it spills.
    /// Its contents are a one-slot container (Chests), so hoppers fill and empty it and a comparator reads it.
    /// (The sherd patterns need archaeology, which the mod doesn't have: plain pots only.)
    /// </summary>
    public static class Pots
    {
        public static int Inserted, Shattered; // (dev/tests)

        /// <summary>Host: [E] on a pot with what's in hand.</summary>
        public static void ServerUse(ulong sender, BlockKey k)
        {
            var w = BlockWorld.Instance;
            var b = w?.Get(k);
            var p = ServerLogic.PlayerFor(sender);
            if (b == null || p == null || b.Data.Def != Blocks.DecoratedPot) return;
            var at = w.WorldCenter(k);
            var c = Chests.Ensure(k);
            var st = p.currentlyHeldObjectServer as StackItem;
            string key = st != null && st.Count > 0 ? Crafting.KeyOf(st) : null;
            if (key != null && Chests.PutInto(c, 0, key, 1, false).n == 0)
            {
                st.ServerUseUp(p);
                BlockNet.ServerChest(k, c);
                float full = (float)c.Count[0] / Mathf.Max(1, Inventory.MaxStackOf(key));
                BlockNet.ServerSound(at, "pot.insert", 1f, 0.7f + 0.5f * full);
                Redstone.MarkDirty();
                Inserted++;
                return;
            }
            BlockNet.ServerSound(at, "pot.insert_fail", 1f, 1f);
        }

        /// <summary>Host: broken with a tool: its four bricks, and what was in it.</summary>
        public static void ServerShatter(BlockKey k)
        {
            var w = BlockWorld.Instance;
            if (w?.Get(k) == null) return;
            var at = w.WorldCenter(k);
            ServerLogic.BreakBlock(k, false);
            Chests.ServerDropContents(k, at);
            if (ModItems.ByKey.TryGetValue("brick", out var brick)) ModItems.ServerSpawnStack(brick, 4, at);
            BlockNet.ServerSound(at, "pot.shatter", 1f, 1f);
            Shattered++;
        }

        public static string HoverTip(BlockInstance b)
        {
            var c = Chests.Of(b.Key);
            if (c == null || c.Count[0] <= 0) return "Decorated Pot - put in : [E]";
            string name = ModItems.ByKey.TryGetValue(c.Key[0], out var it) ? it.itemName : c.Key[0];
            return $"Decorated Pot ({name} x{c.Count[0]}) - put in : [E]";
        }
    }
}
