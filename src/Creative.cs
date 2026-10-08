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
            if (args.Length == 0) return Say("Usage: /gamemode <creative|survival> [player] ([Tab] completes names)");
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

        // ------------------------------------------------------------------ [Tab] completion in the chat box (#16)
        static List<string> cycle;      // the completions [Tab] goes through, while the text is still the last one it set
        static int cycleAt;
        static string cycleText, cycleHead;
        static float lastTab;
        static int normalLimit = -1;    // the chat box's own character limit, while a long completed line has it raised

        /// <summary>Every frame (local player): [Tab] while typing "/gamemode ..." completes the word being typed: the command,
        /// the mode, or a player's name (any part of it, any case). Pressing it again goes to the next match.</summary>
        public static void Tick()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            var hud = HUDManager.Instance;
            if (p == null || hud == null || hud.chatTextField == null || !p.isTypingChat)
            {
                cycle = null;
                if (normalLimit >= 0 && hud != null && hud.chatTextField != null) { hud.chatTextField.characterLimit = normalLimit; normalLimit = -1; }
                return;
            }
            string text = hud.chatTextField.text ?? "";
            var kb = Keyboard.current;
            // (the text box may also take the key as a tab character)
            bool tab = (kb != null && kb.tabKey.wasPressedThisFrame) || text.IndexOf('	') >= 0;
            if (text.IndexOf('	') >= 0) { text = text.Replace("	", ""); hud.chatTextField.text = text; }
            if (!tab || Time.unscaledTime - lastTab < 0.1f) return;
            lastTab = Time.unscaledTime;
            string next = Complete(text);
            if (next == null || next == text) return;
            // the chat box takes 30 characters: "/gamemode creative " leaves 11 for a name. Use the short mode word ("c"),
            // and if it still doesn't fit, let this line be as long as it needs (only while it's a /gamemode line)
            int limit = hud.chatTextField.characterLimit;
            if (limit > 0 && next.Length > limit)
            {
                var w = next.Split(' ');
                if (w.Length > 2 && w[1].Length > 1) { w[1] = w[1].Substring(0, 1); next = string.Join(" ", w); if (cycle != null) { cycleHead = w[0] + " " + w[1] + " "; cycleText = next; } }
                if (next.Length > limit) { if (normalLimit < 0) normalLimit = limit; hud.chatTextField.characterLimit = next.Length; }
            }
            hud.chatTextField.text = next;
            hud.chatTextField.caretPosition = hud.chatTextField.stringPosition = next.Length;
        }

        /// <summary>The text with its last word completed (or the next of several matches if [Tab] was just used), or null.</summary>
        public static string Complete(string text)
        {
            if (cycle != null && text == cycleText)
            {
                cycleAt = (cycleAt + 1) % cycle.Count;
                return cycleText = cycleHead + cycle[cycleAt];
            }
            cycle = null;
            int cut = text.LastIndexOf(' ') + 1;
            string head = text.Substring(0, cut), word = text.Substring(cut);
            var words = head.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> options;
            if (words.Length == 0)
            {
                string slash = word.StartsWith("/") ? "/" : "";
                options = word.Length > slash.Length && "gamemode".StartsWith(word.Substring(slash.Length).ToLowerInvariant()) ? new List<string> { slash + "gamemode " } : null;
            }
            else if (words[0].TrimStart('/').ToLowerInvariant() != "gamemode") return null;
            else if (words.Length == 1) options = new[] { "creative ", "survival " }.Where(m => m.StartsWith(word.ToLowerInvariant())).ToList();
            else
            {
                // names: those starting with it first, then those containing it
                string w = word.ToLowerInvariant();
                var names = StartOfRound.Instance.allPlayerScripts.Where(x => x != null && (x.isPlayerControlled || x.isPlayerDead))
                    .Select(x => x.playerUsername ?? "").Where(n => n.Length > 0).Distinct().ToList();
                options = names.Where(n => n.ToLowerInvariant().StartsWith(w))
                    .Concat(names.Where(n => !n.ToLowerInvariant().StartsWith(w) && n.ToLowerInvariant().Contains(w))).ToList();
                if ("@a".StartsWith(w) && w.Length > 0) options.Add("@a");
                // (a name with spaces is matched by the command as the rest of the line, so it can be completed whole)
                if (options.Count == 0 && words.Length > 2)
                {
                    string rest = string.Join(" ", words.Skip(2)) + " " + word;
                    var whole = names.Where(n => n.ToLowerInvariant().StartsWith(rest.ToLowerInvariant())).ToList();
                    if (whole.Count > 0) { head = words[0] + " " + words[1] + " "; options = whole; }
                }
            }
            if (options == null || options.Count == 0) return null;
            cycle = options; cycleAt = 0; cycleHead = head;
            return cycleText = head + options[0];
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
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer) { GameModes.ServerForget(clientId); Armor.ServerForget(clientId); Storage.ServerForget(clientId); }
        }
    }
}
