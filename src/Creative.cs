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

        public static void Reset() { creative.Clear(); restored.Clear(); saved = null; }

        // ------------------------------------------------------------------ saving (host, with the ship, in orbit)
        const string SaveKey = "LMC_GameModes_v1";
        static readonly HashSet<string> restored = new HashSet<string>();
        static HashSet<string> saved;

        static HashSet<string> LoadSet(string file) =>
            ES3.KeyExists(SaveKey, file) ? new HashSet<string>(ES3.Load<string>(SaveKey, file).Split('\n').Where(x => x.Length > 0)) : new HashSet<string>();

        /// <summary>Host: who's in creative, kept with the save (players who aren't here keep what they had).</summary>
        public static void Save(string file)
        {
            var set = LoadSet(file);
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled) continue;
                var id = Armor.IdentityOf(p);
                if (IsCreative(p.actualClientId)) set.Add(id); else set.Remove(id);
            }
            ES3.Save(SaveKey, string.Join("\n", set), file);
            saved = set;
        }

        /// <summary>Server, now and then: a player who was in creative when the game was saved gets it back on joining.</summary>
        public static void ServerRestore()
        {
            var sor = StartOfRound.Instance;
            if (!BlockNet.IsServer || sor == null || GameNetworkManager.Instance == null) return;
            if (saved == null) saved = LoadSet(GameNetworkManager.Instance.currentSaveFileName);
            if (saved.Count == 0) return;
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled) continue;
                string id = Armor.IdentityOf(p);
                if (string.IsNullOrEmpty(id) || id == "Player" || !restored.Add(id)) continue;
                if (saved.Contains(id) && !IsCreative(p.actualClientId))
                {
                    ServerSet(p.actualClientId, true);
                    Plugin.Log.LogInfo($"Game mode restored for {p.playerUsername}: creative");
                }
            }
        }

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
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) { GameModes.ServerForget(clientId); Armor.ServerForget(clientId); Storage.ServerForget(clientId); }
        }
    }
}
