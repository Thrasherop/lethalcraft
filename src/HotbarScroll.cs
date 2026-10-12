using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// The mouse wheel on the hotbar, Minecraft's way (#117): wheel down goes to the next slot (to the right), wheel up
    /// to the one before. The game does the opposite. [Controls] MinecraftScrollDirection; the terminal's scrolling is
    /// left as it is.
    /// </summary>
    [HarmonyPatch]
    static class HotbarScroll
    {
        static float Read(ref InputAction.CallbackContext c)
        {
            float v = c.ReadValue<float>();
            return Plugin.MinecraftScroll != null && Plugin.MinecraftScroll.Value ? -v : v;
        }

        // the second ReadValue<float> in ScrollMouse_performed is the hotbar's (the first is the terminal's)
        [HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB), "ScrollMouse_performed"), HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Flip(IEnumerable<CodeInstruction> code)
        {
            var mine = AccessTools.Method(typeof(HotbarScroll), nameof(Read));
            int seen = 0;
            foreach (var ci in code)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo m && m.Name == "ReadValue" && m.DeclaringType == typeof(InputAction.CallbackContext) && ++seen == 2)
                {
                    ci.opcode = OpCodes.Call; ci.operand = mine;
                }
                yield return ci;
            }
            if (seen < 2) Plugin.Log.LogWarning("HotbarScroll: the game's scroll code changed; the wheel keeps the game's direction");
        }
    }
}
