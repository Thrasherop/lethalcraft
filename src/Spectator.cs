using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// Spectator mode (#129), Minecraft's /gamemode spectator: you fly (always), through walls and floors, nobody sees you,
    /// monsters don't notice you, nothing hurts you, and you can't touch anything (no mining, building, using, picking up,
    /// doors or levers). The server keeps who's spectating and tells everyone (each machine hides them).
    /// </summary>
    [HarmonyPatch]
    public static class Spectator
    {
        static readonly HashSet<ulong> spectators = new HashSet<ulong>(); // netcode client ids
        public static bool Is(ulong clientId) => spectators.Contains(clientId);
        public static bool Is(PlayerControllerB p) => p != null && spectators.Contains(p.actualClientId);
        public static bool Local => Unity.Netcode.NetworkManager.Singleton != null && spectators.Contains(Unity.Netcode.NetworkManager.Singleton.LocalClientId);
        public static IEnumerable<ulong> All => spectators;
        public const float Speed = 9f, FastSpeed = 20f, Vertical = 8f;

        public static void Reset() { spectators.Clear(); }

        /// <summary>Server: one player's spectator mode on or off, told to everyone.</summary>
        public static void ServerSet(ulong clientId, bool on)
        {
            var next = new HashSet<ulong>(spectators);
            if (on) next.Add(clientId); else next.Remove(clientId);
            BlockNet.ServerSpectators(next);
        }

        /// <summary>Every machine: the server's list.</summary>
        public static void Receive(IEnumerable<ulong> ids)
        {
            spectators.Clear();
            foreach (var id in ids) spectators.Add(id);
        }

        static PlayerControllerB LocalPlayer => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
        static InputAction jump, crouch, sprint;
        static void FindActions()
        {
            if (jump != null) return;
            try { var a = IngamePlayerSettings.Instance.playerInput.actions; jump = a.FindAction("Jump"); crouch = a.FindAction("Crouch"); sprint = a.FindAction("Sprint"); } catch { }
        }

        // ------------------------------------------------------------------ flying through everything (the spectator's own machine)
        static bool noclip, layerSet;
        const int NoclipLayer = 31; // (unused by the game)
        static int normalLayer = 3;

        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Fly(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p != LocalPlayer) return;
            bool on = Local && !p.isPlayerDead && p.isPlayerControlled;
            // (through walls: the player's object on a layer that collides with nothing; the controller follows the layer
            // matrix, its own exclude list it doesn't)
            if (on != noclip)
            {
                if (!layerSet) { for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(NoclipLayer, i, true); layerSet = true; }
                if (on) { normalLayer = p.gameObject.layer; p.gameObject.layer = NoclipLayer; }
                else p.gameObject.layer = normalLayer;
                noclip = on;
            }
            if (!on) return;
            FindActions();
            bool menu = p.isTypingChat || p.inTerminalMenu || SlotScreen.AnyOpen || (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen);
            float vy = 0f;
            if (!menu && jump != null && jump.IsPressed()) vy += Vertical;
            if (!menu && crouch != null && crouch.IsPressed()) vy -= Vertical;
            p.fallValue = vy; p.fallValueUncapped = 0f; p.isFallingNoJump = false; p.takingFallDamage = false;
            var mv = p.moveInputVector;
            if (!menu && mv.sqrMagnitude > 0.01f)
            {
                var cam = p.gameplayCamera.transform;
                // (where you look, up and down too, like Minecraft's spectator)
                var dir = cam.right * mv.x + cam.forward * mv.y;
                bool fast = sprint != null && sprint.IsPressed();
                p.externalForces += Vector3.ClampMagnitude(dir, 1f) * (fast ? FastSpeed : Speed);
            }
            p.sprintMeter = 1f; p.isExhausted = false;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool NoCrouch(PlayerControllerB __instance) => !(Local && __instance == LocalPlayer);

        [HarmonyPatch(typeof(PlayerControllerB), "Jump_performed"), HarmonyPrefix]
        static bool NoJump(PlayerControllerB __instance) => !(Local && __instance == LocalPlayer);

        // ------------------------------------------------------------------ touching nothing
        static bool Hands(PlayerControllerB p) => !(p == LocalPlayer && Local);

        [HarmonyPatch(typeof(PlayerControllerB), "ActivateItem_performed"), HarmonyPrefix] static bool A(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "ItemSecondaryUse_performed"), HarmonyPrefix] static bool B(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "ItemTertiaryUse_performed"), HarmonyPrefix] static bool C(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "Interact_performed"), HarmonyPrefix] static bool D(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "Discard_performed"), HarmonyPrefix] static bool E(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "QEItemInteract_performed"), HarmonyPrefix] static bool F(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "UseUtilitySlot_performed"), HarmonyPrefix] static bool G(PlayerControllerB __instance) => Hands(__instance);
        [HarmonyPatch(typeof(PlayerControllerB), "BeginGrabObject"), HarmonyPrefix, HarmonyPriority(Priority.First)] static bool H(PlayerControllerB __instance) => Hands(__instance);

        // nothing hurts you (and the void doesn't: you fly)
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DamagePlayer)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool NoDamage(PlayerControllerB __instance) => !(__instance.IsOwner && Is(__instance));
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.KillPlayer)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        static bool NoKill(PlayerControllerB __instance) => !(__instance.IsOwner && Is(__instance));

        // monsters don't notice you
        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.PlayerIsTargetable)), HarmonyPostfix]
        static void NotTargetable(PlayerControllerB playerScript, ref bool __result) { if (__result && Is(playerScript)) __result = false; }

        // ------------------------------------------------------------------ nobody sees you (every other machine)
        // (what was showing is switched off, and back on when they stop: their body, armor, what they hold, their name)
        static readonly Dictionary<ulong, List<Renderer>> hiddenBy = new Dictionary<ulong, List<Renderer>>();
        static readonly Dictionary<ulong, float> nextSweep = new Dictionary<ulong, float>();

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate"), HarmonyPostfix]
        static void Visibility(PlayerControllerB __instance)
        {
            var p = __instance;
            if (p == null || p == LocalPlayer) return;
            ulong id = p.actualClientId;
            bool hide = Is(p) && !p.isPlayerDead && p.isPlayerControlled;
            hiddenBy.TryGetValue(id, out var list);
            if (hide)
            {
                if (list == null) { list = new List<Renderer>(); hiddenBy[id] = list; nextSweep[id] = 0f; }
                if (Time.time < nextSweep[id]) return;
                nextSweep[id] = Time.time + 0.5f; // (now and then: armor put on, an item picked up)
                foreach (var r in p.GetComponentsInChildren<Renderer>(false))
                    if (r.enabled) { r.enabled = false; list.Add(r); }
                if (p.usernameCanvas != null) p.usernameCanvas.enabled = false;
            }
            else if (list != null)
            {
                foreach (var r in list) if (r != null) r.enabled = true;
                hiddenBy.Remove(id);
                if (p.usernameCanvas != null) p.usernameCanvas.enabled = true;
            }
        }
    }
}
