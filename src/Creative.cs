using System;
using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft game modes, per player: survival (the default) or creative. The host sets them with the chat command
    /// /gamemode creative|survival [player]; the server keeps everyone's mode and mirrors it to every client.
    /// Creative, like Minecraft: placing doesn't use up blocks, blocks break instantly and drop nothing, [I] opens the
    /// creative menu (every item), double-tap jump to fly, no damage and no hunger.
    /// </summary>
    public static class GameModes
    {
        static readonly HashSet<ulong> creative = new HashSet<ulong>(); // netcode client ids

        public static bool IsCreative(ulong clientId) => creative.Contains(clientId);
        public static bool IsCreative(PlayerControllerB p) => p != null && creative.Contains(p.actualClientId);
        public static bool LocalCreative => NetworkManager.Singleton != null && creative.Contains(NetworkManager.Singleton.LocalClientId);
        public static IEnumerable<ulong> All => creative;

        public static void Reset() => creative.Clear();

        /// <summary>Every client (the host too): the server's list of creative players.</summary>
        public static void Receive(IEnumerable<ulong> ids)
        {
            bool was = LocalCreative;
            creative.Clear();
            foreach (var id in ids) creative.Add(id);
            if (was && !LocalCreative)
            {
                CreativeFlight.Flying = false;
                if (CreativeUI.IsOpen) CreativeUI.Instance.Close();
            }
        }

        /// <summary>Server: set one player's mode and tell everyone (the host applies it when its own copy arrives).</summary>
        public static void ServerSet(ulong clientId, bool on)
        {
            var next = new HashSet<ulong>(creative);
            if (on) next.Add(clientId); else next.Remove(clientId);
            BlockNet.ServerGameModes(next);
        }

        /// <summary>Server: someone left.</summary>
        public static void ServerForget(ulong clientId)
        {
            if (creative.Contains(clientId)) ServerSet(clientId, false);
        }
    }

    /// <summary>The /gamemode chat command (host only), like Minecraft's: /gamemode creative|survival [player|@a].</summary>
    [HarmonyPatch]
    public static class GameModeCommand
    {
        [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed"), HarmonyPrefix]
        static bool SubmitChat(HUDManager __instance, InputAction.CallbackContext context)
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (!context.performed || p == null || !p.isTypingChat || __instance.chatTextField == null) return true;
            // the game's chat key is "/" itself and doesn't type it, so accept the command with or without the slash
            var words = (__instance.chatTextField.text ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0].TrimStart('/').ToLowerInvariant() != "gamemode") return true;
            Run(p, words.Skip(1).ToArray());
            // close the chat box the way the game does after sending
            p.isTypingChat = false;
            __instance.chatTextField.text = "";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (__instance.typingIndicator != null) __instance.typingIndicator.enabled = false;
            return false;
        }

        /// <summary>Runs the command as the local player; returns what it said (also shown in chat).</summary>
        public static string Run(PlayerControllerB self, string[] args)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return Say("Only the host can change game modes.");
            if (args.Length == 0) return Say("Usage: /gamemode <creative|survival> [player]");
            bool? on = Parse(args[0]);
            if (on == null) return Say($"Unknown game mode '{args[0]}'. Use creative or survival.");
            string err = null;
            var targets = args.Length > 1 ? Find(self, string.Join(" ", args.Skip(1)), out err) : new List<PlayerControllerB> { self };
            if (targets.Count == 0) return Say(err);
            var said = new List<string>();
            foreach (var t in targets)
            {
                GameModes.ServerSet(t.actualClientId, on.Value);
                string msg = $"Set {t.playerUsername}'s game mode to {(on.Value ? "Creative" : "Survival")} Mode";
                HUDManager.Instance?.AddTextToChatOnServer(msg); // everyone sees it
                said.Add(msg);
            }
            return string.Join("; ", said);
        }

        static bool? Parse(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "creative": case "c": case "1": return true;
                case "survival": case "s": case "0": return false;
                default: return null;
            }
        }

        /// <summary>Players by name: exact (any case), else the only one starting with it, else the only one containing it.</summary>
        static List<PlayerControllerB> Find(PlayerControllerB self, string name, out string err)
        {
            err = null;
            var players = StartOfRound.Instance.allPlayerScripts.Where(x => x != null && (x.isPlayerControlled || x.isPlayerDead)).ToList();
            string n = name.Trim().ToLowerInvariant();
            if (n == "@a") return players;
            if (n == "@s" || n == "@p") return new List<PlayerControllerB> { self };
            foreach (var match in new Func<string, bool>[] { u => u == n, u => u.StartsWith(n), u => u.Contains(n) })
            {
                var hits = players.Where(x => match((x.playerUsername ?? "").ToLowerInvariant())).ToList();
                if (hits.Count == 1) return hits;
                if (hits.Count > 1) { err = $"More than one player matches '{name}': {string.Join(", ", hits.Select(h => h.playerUsername))}"; return new List<PlayerControllerB>(); }
            }
            err = $"No player named '{name}'.";
            return new List<PlayerControllerB>();
        }

        /// <summary>A line in your own chat only (errors, usage).</summary>
        static string Say(string msg)
        {
            HUDManager.Instance?.AddChatMessage(msg);
            return msg;
        }
    }

    /// <summary>
    /// Creative flight, like Minecraft: double-tap jump to start or stop flying; hold jump to rise, crouch to sink,
    /// sprint to go faster; landing ends it. Creative players also never run out of stamina.
    /// </summary>
    [HarmonyPatch]
    public class CreativeFlight : MonoBehaviour
    {
        public static bool Flying;
        public const float Vertical = 8f, DoubleTap = 0.35f;
        static float lastJumpPress = -10f, startedAt;
        static InputAction jump, crouch, sprint;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        static void FindActions()
        {
            if (jump != null) return;
            try
            {
                var a = IngamePlayerSettings.Instance.playerInput.actions;
                jump = a.FindAction("Jump"); crouch = a.FindAction("Crouch"); sprint = a.FindAction("Sprint");
            }
            catch { }
        }

        static bool InMenu(PlayerControllerB p) =>
            p.isTypingChat || p.inTerminalMenu || SlotScreen.AnyOpen || p.inSpecialInteractAnimation ||
            (p.quickMenuManager != null && p.quickMenuManager.isMenuOpen);

        void OnDestroy() => Flying = false;

        void Update()
        {
            var p = Local;
            if (p == null || !GameModes.LocalCreative || p.isPlayerDead || !p.isPlayerControlled) { Flying = false; return; }
            FindActions();
            if (jump != null && jump.WasPressedThisFrame() && !InMenu(p))
            {
                if (Time.time - lastJumpPress < DoubleTap)
                {
                    Flying = !Flying;
                    startedAt = Time.time;
                    lastJumpPress = -10f;
                }
                else lastJumpPress = Time.time;
            }
            if (p.isClimbingLadder) Flying = false;
            // creative never tires
            p.sprintMeter = 1f;
            p.isExhausted = false;
        }

        /// <summary>Before the game moves the player: hold them in the air and steer up/down (its gravity runs after the move).</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Update"), HarmonyPrefix]
        static void Fly(PlayerControllerB __instance)
        {
            if (!Flying || __instance != Local) return;
            var p = __instance;
            bool menu = InMenu(p);
            float vy = 0f;
            if (!menu && jump != null && jump.IsPressed()) vy += Vertical;
            if (!menu && crouch != null && crouch.IsPressed()) vy -= Vertical;
            // touching down ends flight, like Minecraft (not in the first moment: the double-tap can start on the ground)
            if (p.thisController.isGrounded && vy <= 0f && Time.time - startedAt > 0.3f) { Flying = false; return; }
            p.fallValue = vy;
            p.fallValueUncapped = 0f;
            p.isFallingNoJump = false;
            // Minecraft flies at about 2.5x walking speed (sprint: twice that)
            var mv = p.moveInputVector;
            if (!menu && mv.sqrMagnitude > 0.01f)
            {
                var dir = p.transform.right * mv.x + p.transform.forward * mv.y;
                dir.y = 0f;
                bool fast = sprint != null && sprint.IsPressed();
                p.externalForces += Vector3.ClampMagnitude(dir, 1f) * p.movementSpeed * (fast ? 2.2f : 1.4f);
            }
        }

        /// <summary>The crouch key sinks while flying instead of crouching.</summary>
        [HarmonyPatch(typeof(PlayerControllerB), "Crouch_performed"), HarmonyPrefix]
        static bool NoCrouchWhileFlying(PlayerControllerB __instance) => !(Flying && __instance == Local);
    }

    /// <summary>Creative players take no damage and don't die (falling out of the world still counts).</summary>
    [HarmonyPatch(typeof(PlayerControllerB))]
    static class CreativeInvulnerable
    {
        [HarmonyPatch("DamagePlayer"), HarmonyPrefix]
        static bool Damage(PlayerControllerB __instance) => !(__instance.IsOwner && GameModes.IsCreative(__instance));

        [HarmonyPatch("KillPlayer"), HarmonyPrefix]
        static bool Kill(PlayerControllerB __instance, bool spawnBody, CauseOfDeath causeOfDeath)
        {
            if (!__instance.IsOwner || !GameModes.IsCreative(__instance)) return true;
            // the void (below the map, or out of the facility's bounds) still kills, so nobody falls forever
            return !spawnBody && (causeOfDeath == CauseOfDeath.Gravity || causeOfDeath == CauseOfDeath.Unknown) && __instance.transform.position.y < -300f
                || (!spawnBody && causeOfDeath == CauseOfDeath.Unknown && __instance.isInsideFactory);
        }

        /// <summary>Server: forget a leaving player's mode.</summary>
        [HarmonyPatch(typeof(StartOfRound), "OnClientDisconnect"), HarmonyPostfix]
        static void Left(ulong clientId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) { GameModes.ServerForget(clientId); Armor.ServerForget(clientId); }
        }
    }
}
