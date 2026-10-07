using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Keeps the vanilla STORE page tidy (one line for all Minecraft goods) and adds a MINECRAFT terminal page
    /// listing every block/tool/food by category with stack sizes and current sale prices.
    /// </summary>
    [HarmonyPatch(typeof(Terminal))]
    public static class TerminalPage
    {
        static TerminalNode page;

        static bool IsOurs(Item i) => i != null && i.name != null && i.name.StartsWith("LMC_");

        [HarmonyPatch("Awake"), HarmonyPostfix]
        static void Awake(Terminal __instance)
        {
            try
            {
                page = ScriptableObject.CreateInstance<TerminalNode>();
                page.name = "LMC_MinecraftPage";
                page.clearPreviousText = true;
                page.maxCharactersToType = 25;
                page.displayText = "[lmcList]\n\n";
                var list = __instance.terminalNodes.allKeywords.ToList();
                foreach (var word in new[] { "minecraft", "mc", "blocks" })
                {
                    if (list.Any(k => k.word == word)) continue;
                    var kw = ScriptableObject.CreateInstance<TerminalKeyword>();
                    kw.name = "LMC_kw_" + word;
                    kw.word = word;
                    kw.isVerb = false;
                    kw.specialKeywordResult = page;
                    list.Add(kw);
                }
                __instance.terminalNodes.allKeywords = list.ToArray();
            }
            catch (System.Exception e) { Plugin.Log.LogError("Terminal page setup failed: " + e); }
        }

        [HarmonyPatch("TextPostProcess"), HarmonyPrefix]
        static void TextPostProcess(Terminal __instance, ref string modifiedDisplayText)
        {
            if (modifiedDisplayText.Contains("[buyableItemsList]") && __instance.buyableItemsList != null && __instance.buyableItemsList.Length > 0)
            {
                var sb = new StringBuilder();
                bool any = false;
                for (int k = 0; k < __instance.buyableItemsList.Length; k++)
                {
                    var it = __instance.buyableItemsList[k];
                    if (IsOurs(it)) { any = true; continue; }
                    sb.Append("\n* " + it.itemName + "  //  Price: $" + (int)(it.creditsWorth * (__instance.itemSalesPercentages[k] / 100f)));
                    if (__instance.itemSalesPercentages[k] != 100) sb.Append($"   ({100 - __instance.itemSalesPercentages[k]}% OFF!)");
                }
                if (any) sb.Append("\n\n* MINECRAFT blocks, tools & food  //  Type \"MINECRAFT\"");
                modifiedDisplayText = modifiedDisplayText.Replace("[buyableItemsList]", sb.ToString());
            }
            if (modifiedDisplayText.Contains("[lmcList]"))
                modifiedDisplayText = modifiedDisplayText.Replace("[lmcList]", BuildPage(__instance));
        }

        static string BuildPage(Terminal t)
        {
            var sb = new StringBuilder();
            sb.Append("MINECRAFT SUPPLY CO.\n");
            sb.Append("Buy with: BUY <NAME> [amount]   e.g. \"buy cobblestone 3\"\n");
            sb.Append("Each order is one stack. Stacks merge up to 64.\n");
            sb.Append("Better tools: mine iron/gold/diamonds, smelt them in a Furnace,\ncraft at a Crafting Table. Pocket crafting: [" + Plugin.CraftKey.Value.ToUpper() + "]\n");
            var groups = new List<(string title, System.Func<Item, bool> pred)>
            {
                ("BUILDING", i => BlockOf(i) is BlockDef b && b.Shape == BlockShape.Cube && !b.IsRedstoneComponent && b.LightIntensity <= 0 && b != Blocks.Slime && b != Blocks.CraftingTable && b != Blocks.Furnace),
                ("LIGHT", i => BlockOf(i) is BlockDef b && b.LightIntensity > 0 && b != Blocks.RedstoneTorch),
                ("REDSTONE & TRAPS", i => BlockOf(i) is BlockDef b && (b.IsRedstoneComponent || b == Blocks.Slime)),
                ("TOOLS", i => i.spawnPrefab != null && (i.spawnPrefab.GetComponent<ToolItem>() != null || i.spawnPrefab.GetComponent<FlintAndSteelItem>() != null)),
                ("CRAFTING", i => BlockOf(i) == Blocks.CraftingTable || BlockOf(i) == Blocks.Furnace),
                ("FOOD", i => ModItems.Foods.ContainsKey(i.name.Substring(4))),
                ("ITEMS", i => true), // anything else (ender pearls...)
            };
            var shown = new HashSet<Item>();
            foreach (var (title, pred) in groups)
            {
                var lines = new StringBuilder();
                for (int k = 0; k < t.buyableItemsList.Length; k++)
                {
                    var it = t.buyableItemsList[k];
                    if (!IsOurs(it) || shown.Contains(it) || !pred(it)) continue;
                    shown.Add(it);
                    int price = (int)(it.creditsWorth * (t.itemSalesPercentages[k] / 100f));
                    string amount = AmountOf(it);
                    lines.Append($"\n* {it.itemName}{amount}  //  ${price}");
                    if (t.itemSalesPercentages[k] != 100) lines.Append($" ({100 - t.itemSalesPercentages[k]}% OFF!)");
                }
                if (lines.Length == 0) continue;
                sb.Append("\n").Append(title).Append(":").Append(lines);
                sb.Append("\n");
            }
            return sb.ToString();
        }

        static BlockDef BlockOf(Item i) => i != null && i.name.StartsWith("LMC_") ? Blocks.Get(i.name.Substring(4)) : null;

        static string AmountOf(Item i)
        {
            var b = BlockOf(i);
            if (b != null) return b.ShopStack > 1 ? $" (x{b.ShopStack})" : "";
            var key = i.name.Substring(4);
            if (ModItems.Foods.TryGetValue(key, out var f)) return f.Stack > 1 ? $" (x{f.Stack})" : "";
            return "";
        }
    }
}
