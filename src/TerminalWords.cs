using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Typing a multi-word store item ("buy stone pickaxe") used to buy the wrong thing: the terminal reads one word per
    /// noun and strips punctuation, while LethalLib's keyword is the name with dashes ("stone-pickaxe", untypeable), so it
    /// stopped at "stone". Our multi-word keywords are squashed ("stonepickaxe") and, before a sentence is parsed, the words
    /// that spell one are joined: "buy stone pickaxe 2" -> "buy stonepickaxe 2" (a partial "stone pick" works too).
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "ParsePlayerSentence")]
    static class TerminalWords
    {
        static readonly List<string> ours = new List<string>();
        static readonly Dictionary<string, string> aliases = new Dictionary<string, string>();
        static int keywordCount = -1;

        static string lastTyped;

        static void Prefix(Terminal __instance)
        {
            lastTyped = null;
            try
            {
                var t = __instance;
                if (t.terminalNodes == null || t.terminalNodes.allKeywords == null) return;
                SquashOurKeywords(t);
                string all = t.screenText.text;
                if (t.textAdded <= 0 || t.textAdded > all.Length) return;
                string typed = all.Substring(all.Length - t.textAdded);
                lastTyped = typed;
                string joined = TerminalText.JoinKeywords(typed, ours.ToArray(), aliases);
                if (joined == typed) return;
                t.screenText.text = all.Substring(0, all.Length - t.textAdded) + joined;
                t.textAdded = joined.Length;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Terminal words: " + e.Message); }
        }

        static readonly Dictionary<string, TerminalNode> notSoldNodes = new Dictionary<string, TerminalNode>();

        /// <summary>
        /// Tools, ingots and diamonds are crafting only. Ordering one ("buy stone pickaxe", which the terminal would cut down
        /// to "buy stone") says so and how to get it, instead of ordering something else.
        /// </summary>
        static void Postfix(Terminal __instance, ref TerminalNode __result)
        {
            try
            {
                var t = __instance;
                if (lastTyped == null || t.buyableItemsList == null) return;
                string resolved = __result != null && __result.buyItemIndex >= 0 && __result.buyItemIndex < t.buyableItemsList.Length ? t.buyableItemsList[__result.buyItemIndex].itemName : null;
                var sold = new HashSet<Item>(t.buyableItemsList);
                var notSold = ModItems.ByKey.Values.Where(i => i != null && !i.isScrap && !sold.Contains(i)).ToList();
                string name = TerminalText.NotSoldMatch(lastTyped, notSold.Select(i => i.itemName), resolved);
                if (name == null) return;
                if (!notSoldNodes.TryGetValue(name, out var node) || node == null)
                {
                    node = ScriptableObject.CreateInstance<TerminalNode>();
                    node.name = "LMC_NotSold_" + name;
                    node.clearPreviousText = true;
                    node.maxCharactersToType = 40;
                    node.buyItemIndex = -1;
                    node.displayText = $"{name} isn't sold here: craft it.\n\n{HowToGet(notSold.First(i => i.itemName == name))}\n\n";
                    notSoldNodes[name] = node;
                }
                __result = node;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Terminal not-sold: " + e.Message); }
        }

        static string HowToGet(Item item)
        {
            string key = ModItems.ByKey.FirstOrDefault(kv => kv.Value == item).Key ?? "";
            if (key == "diamond") return "Mine diamond ore, or buy a Block of Diamond and craft it into nine diamonds.";
            if (key == "iron_ingot") return "Smelt raw iron in a furnace, or buy a Block of Iron and craft it into nine ingots.";
            if (key == "gold_ingot") return "Smelt raw gold in a furnace, or buy a Block of Gold and craft it into nine ingots.";
            if (key == "coal") return "Mine coal ore, smelt oak logs, or buy a Block of Coal and craft it into nine coal.";
            if (key.Contains("pickaxe") || key.Contains("shovel") || key.Contains("axe"))
                return "Buy Oak Logs, craft planks, sticks and a crafting table, make a wooden pickaxe and work your way up (stone, iron, diamond).";
            return "Press [I] (or use a crafting table) to see the recipes.";
        }

        /// <summary>"stone-pickaxe" -> "stonepickaxe" for this mod's items (once per keyword list).</summary>
        static void SquashOurKeywords(Terminal t)
        {
            var kws = t.terminalNodes.allKeywords;
            if (kws.Length == keywordCount) return;
            keywordCount = kws.Length;
            var multi = ModItems.ByKey.Values.Where(i => i != null && i.itemName.Contains(' ')).ToList();
            var names = new HashSet<string>(multi.Select(i => i.itemName.ToLowerInvariant().Replace(" ", "-")));
            ours.Clear(); aliases.Clear();
            foreach (var it in multi)
                foreach (var al in TerminalText.Aliases(it.itemName)) aliases[al] = TerminalText.Squash(it.itemName);
            foreach (var k in kws)
            {
                if (k == null || string.IsNullOrEmpty(k.word)) continue;
                if (names.Contains(k.word)) k.word = TerminalText.Squash(k.word);
                if (names.Any(n => TerminalText.Squash(n) == k.word)) ours.Add(k.word);
            }
        }
    }
    [HarmonyPatch(typeof(Terminal), "TextChanged")]
    static class TerminalRoomToType
    {
        /// <summary>
        /// Some terminal screens (the one after a purchase) cap typing at 15 characters: "buy slime block 2" lost its amount
        /// and "buy redstone block" became "buy redstone bl" (which bought a Redstone Lamp). Give every screen room for a
        /// full multi-word order.
        /// </summary>
        static void Prefix(Terminal __instance)
        {
            var n = __instance.currentNode;
            if (n != null && n.maxCharactersToType < 40) n.maxCharactersToType = 40;
        }
    }
}
