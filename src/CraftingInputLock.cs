using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;

namespace LethalMinecraft
{
    /// <summary>
    /// While the crafting screen is open the character doesn't act: the game's own handlers for crouch, jump, emotes,
    /// item use/drop/switch, interact and scan only check the game's menus, so shift-clicking a stack crouched the player
    /// and right-click scanned. Movement and looking are already off (disableMoveInput / inSpecialMenu).
    /// </summary>
    [HarmonyPatch]
    static class CraftingInputLock
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var player = typeof(PlayerControllerB);
            foreach (var name in new[]
            {
                "Crouch_performed", "Jump_performed", "Emote1_performed", "Emote2_performed", "ActivateItem_performed",
                "ScrollMouse_performed", "UseUtilitySlot_performed", "InspectItem_performed", "QEItemInteract_performed",
                "ItemSecondaryUse_performed", "ItemTertiaryUse_performed", "Interact_performed", "Discard_performed",
            })
            {
                var m = AccessTools.Method(player, name);
                if (m != null) yield return m;
            }
            var scan = AccessTools.Method(typeof(HUDManager), "PingScan_performed");
            if (scan != null) yield return scan;
            var chat = AccessTools.Method(typeof(HUDManager), "EnableChat_performed");
            if (chat != null) yield return chat;
        }

        static bool Prefix() => !CraftingUI.IsOpen;
    }
}
