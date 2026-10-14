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
    public static class HotbarScroll
    {
        static float Read(ref InputAction.CallbackContext c)
        {
            float v = c.ReadValue<float>();
            return Plugin.MinecraftScroll != null && Plugin.MinecraftScroll.Value ? -v : v;
        }

        // (#168) the game moves one slot per wheel event and ignores any within 0.3 s of the last; a quick flick's
        // notches come as several events in one frame, so it moved one. Minecraft moves a slot a notch: the hotbar's
        // wheel is done here, each event a slot, the way the game switches (its slot switch and the server's); the
        // terminal keeps the game's scrolling.
        static readonly MethodInfo switchTo = AccessTools.Method(typeof(GameNetcodeStuff.PlayerControllerB), "SwitchToItemSlot");
        static readonly MethodInfo switchRpc = AccessTools.Method(typeof(GameNetcodeStuff.PlayerControllerB), "SwitchItemSlotsServerRpc");
        public static int Switched; // (dev/tests)

        [HarmonyPatch(typeof(GameNetcodeStuff.PlayerControllerB), "ScrollMouse_performed"), HarmonyPrefix]
        static bool Wheel(GameNetcodeStuff.PlayerControllerB __instance, InputAction.CallbackContext context)
        {
            var p = __instance;
            if (p == null || switchTo == null || switchRpc == null || p.inTerminalMenu) return true;
            if (!context.performed || !p.IsOwner || !p.isPlayerControlled || p.isPlayerDead) return false;
            if (p.isGrabbingObjectAnimation || p.throwingObject || p.inSpecialMenu || p.isTypingChat || (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen)) return false;
            if (p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer.itemProperties != null && p.currentlyHeldObjectServer.itemProperties.twoHanded) return false;
            float v = context.ReadValue<float>();
            if (UnityEngine.Mathf.Abs(v) < 0.01f) return false;
            bool forward = Plugin.MinecraftScroll != null && Plugin.MinecraftScroll.Value ? v < 0f : v > 0f;
            // (the action's value is only the direction: how many notches, from the wheel's own delta this frame, 120 each)
            float raw = Mouse.current != null ? Mouse.current.scroll.y.ReadValue() : 0f;
            int notches = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Abs(raw) / 120f), 1, 9);
            int n = p.ItemSlots.Length;
            for (int k = 0; k < notches; k++)
            {
                int next = forward ? (p.currentItemSlot + 1) % n : (p.currentItemSlot - 1 + n) % n;
                switchTo.Invoke(p, new object[] { next, null });
                switchRpc.Invoke(p, new object[] { forward });
            }
            if (p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer.itemProperties != null && p.currentlyHeldObjectServer.itemProperties.grabSFX != null)
                p.itemAudio.PlayOneShot(p.currentlyHeldObjectServer.itemProperties.grabSFX, 0.6f);
            p.timeSinceSwitchingSlots = 0f;
            Switched += notches;
            return false;
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
