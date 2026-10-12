using GameNetcodeStuff;
using HarmonyLib;

namespace LethalMinecraft
{
    /// <summary>
    /// Creative mode: the shotgun reloads without shells (#104, the owner's ask). The game's reload looks for a shell in
    /// your slots and uses it up at the end; in creative, with none, it goes ahead and uses nothing.
    /// </summary>
    [HarmonyPatch]
    static class CreativeShotgun
    {
        [HarmonyPatch(typeof(ShotgunItem), "ReloadedGun"), HarmonyPostfix]
        static void FreeReload(ShotgunItem __instance, ref bool __result, ref int ___ammoSlotToUse)
        {
            if (__result || __instance.playerHeldBy == null || !__instance.playerHeldBy.IsOwner || !GameModes.LocalCreative) return;
            ___ammoSlotToUse = -1;
            __result = true;
        }

        // (the reload's end destroys the shell's slot: none was used)
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DestroyItemInSlotAndSync)), HarmonyPrefix]
        static bool NoSlot(int itemSlot) => itemSlot >= 0;
    }
}
