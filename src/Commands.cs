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
    /// Chat commands, like Minecraft's (#45): /gamemode, /tp, /give, /op, /deop, /keepInventory (also /gamerule
    /// keepInventory). Typed in the chat box (with or without the slash; [Tab] completes commands, player names, items).
    /// The server runs them: the host, and players the host made operators with /op (kept with the save).
    /// </summary>
    [HarmonyPatch]
    public static class Commands
    {
        class Cmd
        {
            public string Name, Usage;
            public bool HostOnly;
            public Func<int, string[], List<string>> Complete; // (argument index, the arguments so far) -> options
            public Func<PlayerControllerB, string[], string> Run;  // server: (who ran it, arguments) -> what to tell them
        }

        static readonly List<Cmd> cmds = new List<Cmd>
        {
            new Cmd { Name = "gamemode", Usage = "/gamemode <creative|survival> [player|@a]",
                Complete = (i, a) => i == 0 ? new List<string> { "creative", "survival" } : Players(true), Run = GameMode },
            new Cmd { Name = "tp", Usage = "/tp <player> [to player] (or ship)",
                Complete = (i, a) => i < 2 ? Players(false).Concat(i == 1 || a.Length > 0 ? new[] { "ship" } : new string[0]).ToList() : null, Run = Teleport },
            new Cmd { Name = "give", Usage = "/give <player> <item> [count]",
                Complete = (i, a) => i == 0 ? Players(true) : i == 1 ? ItemNames() : null, Run = Give },
            new Cmd { Name = "op", Usage = "/op <player>  (lets them use commands)", HostOnly = true,
                Complete = (i, a) => i == 0 ? Players(false) : null, Run = (p, a) => SetOp(a, true) },
            new Cmd { Name = "deop", Usage = "/deop <player>", HostOnly = true,
                Complete = (i, a) => i == 0 ? Players(false) : null, Run = (p, a) => SetOp(a, false) },
            new Cmd { Name = "keepinventory", Usage = "/keepInventory <true|false>",
                Complete = (i, a) => i == 0 ? new List<string> { "true", "false" } : null, Run = (p, a) => KeepInv(a) },
            new Cmd { Name = "gamerule", Usage = "/gamerule keepInventory <true|false>",
                Complete = (i, a) => i == 0 ? new List<string> { "keepInventory" } : i == 1 ? new List<string> { "true", "false" } : null,
                Run = (p, a) => a.Length > 0 && a[0].Equals("keepinventory", StringComparison.OrdinalIgnoreCase) ? KeepInv(a.Skip(1).ToArray()) : "Only the keepInventory rule is supported." },
        };

        static Cmd Find(string word) => cmds.FirstOrDefault(c => c.Name == word.TrimStart('/').ToLowerInvariant());

        // ------------------------------------------------------------------ typing one (client)
        [HarmonyPatch(typeof(HUDManager), "SubmitChat_performed"), HarmonyPrefix]
        static bool SubmitChat(HUDManager __instance, InputAction.CallbackContext context)
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (!context.performed || p == null || !p.isTypingChat || __instance.chatTextField == null) return true;
            // the game's chat key is "/" itself and doesn't type it, so accept a command with or without the slash
            string line = (__instance.chatTextField.text ?? "").Trim();
            var words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || Find(words[0]) == null) return true;
            BlockNet.RequestCommand(line);
            // close the chat box the way the game does after sending
            p.isTypingChat = false;
            __instance.chatTextField.text = "";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (__instance.typingIndicator != null) __instance.typingIndicator.enabled = false;
            return false;
        }

        /// <summary>A line in your own chat only (results, errors, usage).</summary>
        public static void Say(string msg) => HUDManager.Instance?.AddChatMessage(msg);

        // ------------------------------------------------------------------ running one (server)
        /// <summary>Server: a command line from a player; returns what to tell them.</summary>
        public static string ServerRun(ulong sender, string line)
        {
            var words = (line ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = words.Length > 0 ? Find(words[0]) : null;
            var who = ServerLogic.PlayerFor(sender);
            if (cmd == null || who == null) return null;
            Load();
            bool host = sender == NetworkManager.ServerClientId;
            if (cmd.HostOnly && !host) return "Only the host can do that.";
            if (!host && !ops.Contains(Armor.IdentityOf(who))) return "You need to be an operator: the host can /op you.";
            var args = words.Skip(1).ToArray();
            try
            {
                string said = cmd.Run(who, args);
                Plugin.Log.LogInfo($"{who.playerUsername}: /{cmd.Name} {string.Join(" ", args)} -> {said}");
                return said;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"/{cmd.Name}: {e}"); return "Usage: " + cmd.Usage; }
        }

        static string Usage(string name) => "Usage: " + cmds.First(c => c.Name == name).Usage;

        static string GameMode(PlayerControllerB self, string[] args)
        {
            if (args.Length == 0) return Usage("gamemode");
            bool? on = args[0].ToLowerInvariant() switch { "creative" => true, "c" => true, "1" => true, "survival" => false, "s" => false, "0" => false, _ => (bool?)null };
            if (on == null) return $"Unknown game mode '{args[0]}'. Use creative or survival.";
            string err = null;
            var targets = args.Length > 1 ? FindPlayers(self, string.Join(" ", args.Skip(1)), out err) : new List<PlayerControllerB> { self };
            if (targets.Count == 0) return err;
            foreach (var t in targets)
            {
                GameModes.ServerSet(t.actualClientId, on.Value);
                HUDManager.Instance?.AddTextToChatOnServer($"Set {t.playerUsername}'s game mode to {(on.Value ? "Creative" : "Survival")} Mode"); // (everyone sees it)
            }
            return null;
        }

        static string Teleport(PlayerControllerB self, string[] args)
        {
            if (args.Length == 0) return Usage("tp");
            // "tp megg" = me to megg; "tp thra megg" = thra to megg ("ship" for either: the ship)
            string whoName = args.Length >= 2 ? args[0] : null, toName = args.Length >= 2 ? string.Join(" ", args.Skip(1)) : args[0];
            List<PlayerControllerB> who;
            string err = null;
            if (whoName == null) who = new List<PlayerControllerB> { self };
            else { who = FindPlayers(self, whoName, out err); if (who.Count == 0) return err; }
            var sor = StartOfRound.Instance;
            bool toShip = toName.Equals("ship", StringComparison.OrdinalIgnoreCase);
            PlayerControllerB to = null;
            if (!toShip)
            {
                var hits = FindPlayers(self, toName, out err);
                if (hits.Count != 1) return hits.Count == 0 ? err : "Teleport to one player at a time.";
                to = hits[0];
                if (to.isPlayerDead || !to.isPlayerControlled) return $"{to.playerUsername} isn't alive to go to.";
            }
            foreach (var p in who)
            {
                if (p.isPlayerDead || !p.isPlayerControlled) continue;
                if (toShip) BlockNet.ServerTeleport(p.actualClientId, sor.GetPlayerSpawnPosition((int)p.playerClientId), false, true, true);
                else if (p != to) BlockNet.ServerTeleport(p.actualClientId, to.transform.position, to.isInsideFactory, to.isInElevator, to.isInHangarShipRoom);
                HUDManager.Instance?.AddTextToChatOnServer($"Teleported {p.playerUsername} to {(toShip ? "the ship" : to.playerUsername)}");
            }
            return null;
        }

        static string Give(PlayerControllerB self, string[] args)
        {
            if (args.Length < 2) return Usage("give");
            var targets = FindPlayers(self, args[0], out string err);
            if (targets.Count == 0) return err;
            int n = 1;
            var itemWords = args.Skip(1).ToList();
            if (itemWords.Count > 1 && int.TryParse(itemWords.Last(), out int parsed)) { n = Mathf.Clamp(parsed, 1, 64 * 9); itemWords.RemoveAt(itemWords.Count - 1); }
            string want = TerminalText.Squash(string.Join("", itemWords));
            // ours: by key or by name; then the game's own items, by name
            string key = ModItems.ByKey.Keys.FirstOrDefault(k => TerminalText.Squash(k) == want)
                         ?? ModItems.ByKey.FirstOrDefault(kv => TerminalText.Squash(kv.Value.itemName) == want).Key;
            Item vanilla = key == null ? StartOfRound.Instance.allItemsList.itemsList.FirstOrDefault(it => it != null && TerminalText.Squash(it.itemName) == want) : null;
            if (key == null && vanilla == null) return $"Unknown item '{string.Join(" ", itemWords)}'. ([Tab] completes item names.)";
            string name = key != null ? ModItems.ByKey[key].itemName : vanilla.itemName;
            foreach (var t in targets)
            {
                if (key != null) Inventory.ServerSpawnFor(t.actualClientId, key, n);
                else for (int i = 0; i < Mathf.Min(n, 9); i++) Inventory.ServerSpawnVanillaFor(t.actualClientId, vanilla.itemName);
                HUDManager.Instance?.AddTextToChatOnServer($"Gave {n} [{name}] to {t.playerUsername}");
            }
            return null;
        }

        static string SetOp(string[] args, bool on)
        {
            if (args.Length == 0) return Usage(on ? "op" : "deop");
            var targets = FindPlayers(null, string.Join(" ", args), out string err);
            if (targets.Count == 0) return err;
            foreach (var t in targets)
            {
                string id = Armor.IdentityOf(t);
                if (on) ops.Add(id); else ops.Remove(id);
                HUDManager.Instance?.AddTextToChatOnServer(on ? $"Made {t.playerUsername} a server operator" : $"Made {t.playerUsername} no longer a server operator");
            }
            Dirty();
            return null;
        }

        static string KeepInv(string[] args)
        {
            if (args.Length == 0) return $"keepInventory is {(KeepInventory ? "true" : "false")}. Usage: /keepInventory <true|false>";
            bool? v = args[0].ToLowerInvariant() switch { "true" => true, "on" => true, "1" => true, "false" => false, "off" => false, "0" => false, _ => (bool?)null };
            if (v == null) return "Use true or false.";
            KeepInventory = v.Value;
            BlockNet.ServerRules();
            Dirty();
            HUDManager.Instance?.AddTextToChatOnServer($"Gamerule keepInventory is now set to: {(v.Value ? "true" : "false")}");
            return null;
        }

        /// <summary>Players by name: exact (any case), else the only one starting with it, else the only one containing it;
        /// @a everyone, @s / @p yourself.</summary>
        static List<PlayerControllerB> FindPlayers(PlayerControllerB self, string name, out string err)
        {
            err = null;
            var players = StartOfRound.Instance.allPlayerScripts.Where(x => x != null && (x.isPlayerControlled || x.isPlayerDead)).ToList();
            string n = name.Trim().ToLowerInvariant();
            if (n == "@a") return players;
            if ((n == "@s" || n == "@p") && self != null) return new List<PlayerControllerB> { self };
            foreach (var match in new Func<string, bool>[] { u => u == n, u => u.StartsWith(n), u => u.Contains(n) })
            {
                var hits = players.Where(x => match((x.playerUsername ?? "").ToLowerInvariant())).ToList();
                if (hits.Count == 1) return hits;
                if (hits.Count > 1) { err = $"More than one player matches '{name}': {string.Join(", ", hits.Select(h => h.playerUsername))}"; return new List<PlayerControllerB>(); }
            }
            err = $"No player named '{name}'.";
            return new List<PlayerControllerB>();
        }

        // ------------------------------------------------------------------ teleporting (the player's own client)
        /// <summary>Client: the server moves me (players move themselves): to the spot, inside the facility or out, in the
        /// ship or not, the way an entrance would.</summary>
        public static void TeleportLocal(Vector3 pos, bool inside, bool inShip, bool inRoom)
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || p.isPlayerDead) return;
            if (p.isInsideFactory != inside) Facility.SetLocalInside(p, inside);
            p.TeleportPlayer(pos);
            p.isInElevator = inShip;
            p.isInHangarShipRoom = inRoom;
            p.ResetFallGravity();
        }

        // ------------------------------------------------------------------ ops and rules (server; kept with the save)
        const string SaveKey = "LMC_Commands_v1";
        static readonly HashSet<string> ops = new HashSet<string>();
        /// <summary>Everyone: dying keeps what you carry (#45). Clients get it from the server.</summary>
        public static bool KeepInventory;
        static string loadedFor;

        static void Load()
        {
            string file = GameNetworkManager.Instance?.currentSaveFileName;
            if (file == null || file == loadedFor) return;
            loadedFor = file;
            ops.Clear(); KeepInventory = false;
            try
            {
                if (ES3.KeyExists(SaveKey, file))
                    foreach (var line in ES3.Load<string>(SaveKey, file).Split('\n'))
                    {
                        if (line.StartsWith("op=")) ops.Add(line.Substring(3));
                        else if (line == "keepInventory=true") KeepInventory = true;
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Commands: couldn't read the save: " + e.Message); }
            BlockNet.ServerRules();
        }

        /// <summary>Server, now and then: load this save's ops and rules (and tell everyone the rules).</summary>
        public static void ServerTick() { if (BlockNet.IsServer) { Load(); ServerReturnKept(); } }

        static void Dirty() { if (StartOfRound.Instance != null && StartOfRound.Instance.inShipPhase) ShipPersistence.Save(); }

        public static void Save(string file)
        {
            if (file != loadedFor) return;
            var lines = ops.Select(o => "op=" + o).ToList();
            if (KeepInventory) lines.Add("keepInventory=true");
            ES3.Save(SaveKey, string.Join("\n", lines), file);
        }

        // ------------------------------------------------------------------ keepInventory
        // Dying, with the rule on: what you carried drops as usual but counts as the ship's (so the round's end doesn't
        // clear it away); once you're back on your feet, it comes back into your hotbar. Your storage and armor stay on you.
        static readonly Dictionary<string, List<ulong>> kept = new Dictionary<string, List<ulong>>();

        // (the dying player's own client is already "dead" when it drops everything; everyone else's copy of them only
        // gets marked dead after: while the game's kill message is being handled, they count as dying)
        static int dying = -1;
        [HarmonyPatch(typeof(PlayerControllerB), "KillPlayerClientRpc"), HarmonyPrefix]
        static void Dying(int playerId) => dying = playerId;
        [HarmonyPatch(typeof(PlayerControllerB), "KillPlayerClientRpc"), HarmonyFinalizer]
        static void NotDying() => dying = -1;

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems)), HarmonyPrefix]
        static void KeepOnDeath(PlayerControllerB __instance, bool disconnecting, out List<GrabbableObject> __state)
        {
            __state = null;
            if (!KeepInventory || disconnecting || __instance == null || !(__instance.isPlayerDead || (int)__instance.playerClientId == dying)) return;
            __state = __instance.ItemSlots.Where(g => g != null).ToList();
            if (__instance.ItemOnlySlot != null) __state.Add(__instance.ItemOnlySlot);
        }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DropAllHeldItems)), HarmonyPostfix]
        static void KeptOnDeath(PlayerControllerB __instance, List<GrabbableObject> __state)
        {
            if (__state == null || __state.Count == 0) return;
            // (the drop itself takes the dead player's own in-ship flags, so mark them afterwards: the round's end keeps
            // what's "in the ship", and only scrap goes in a team wipe)
            foreach (var g in __state) { if (g == null) continue; g.isInShipRoom = true; g.isInElevator = true; g.scrapPersistedThroughRounds = true; }
            if (!BlockNet.IsServer) return;
            string id = Armor.IdentityOf(__instance);
            if (!kept.TryGetValue(id, out var list)) kept[id] = list = new List<ulong>();
            foreach (var g in __state)
                if (g != null && g.NetworkObject != null && g.NetworkObject.IsSpawned && !list.Contains(g.NetworkObjectId)) list.Add(g.NetworkObjectId);
            Plugin.Log.LogInfo($"keepInventory: kept {list.Count} items for {__instance.playerUsername}");
        }

        static void ServerReturnKept()
        {
            if (kept.Count == 0) return;
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                if (!kept.TryGetValue(Armor.IdentityOf(p), out var list)) continue;
                foreach (var nid in list)
                    if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(nid, out var no) && no.GetComponent<GrabbableObject>() is GrabbableObject g && !g.isHeld && !g.isHeldByEnemy)
                        BlockNet.ServerAutoGrab(p.actualClientId, nid);
                kept.Remove(Armor.IdentityOf(p));
            }
        }

        // ------------------------------------------------------------------ [Tab] completion in the chat box
        static List<string> cycle;      // the completions [Tab] goes through, while the text is still the last one it set
        static int cycleAt;
        static string cycleText, cycleHead;
        static float lastTab;
        static int normalLimit = -1;    // the chat box's own character limit, while a long command line has it raised

        /// <summary>Every frame (local player): [Tab] while typing a command completes the word being typed; again: the
        /// next match. A command line may be longer than the chat box's usual 30 characters.</summary>
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
            // room for a whole command line (names and item names don't fit in 30)
            var first = text.TrimStart().Split(' ')[0];
            if (first.Length > 0 && cmds.Any(c => c.Name.StartsWith(first.TrimStart('/').ToLowerInvariant())) && hud.chatTextField.characterLimit > 0 && hud.chatTextField.characterLimit < 80)
            {
                if (normalLimit < 0) normalLimit = hud.chatTextField.characterLimit;
                hud.chatTextField.characterLimit = 80;
            }
            var kb = Keyboard.current;
            // (the text box may also take the key as a tab character)
            bool tab = (kb != null && kb.tabKey.wasPressedThisFrame) || text.IndexOf('\t') >= 0;
            if (text.IndexOf('\t') >= 0) { text = text.Replace("\t", ""); hud.chatTextField.text = text; }
            if (!tab || Time.unscaledTime - lastTab < 0.1f) return;
            lastTab = Time.unscaledTime;
            string next = Complete(text);
            if (next == null || next == text) return;
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
                string w0 = word.Substring(slash.Length).ToLowerInvariant();
                options = w0.Length == 0 ? null : cmds.Where(c => c.Name.StartsWith(w0)).Select(c => slash + (c.Name == "keepinventory" ? "keepInventory" : c.Name)).ToList();
            }
            else
            {
                var cmd = Find(words[0]);
                if (cmd == null) return null;
                var all = cmd.Complete?.Invoke(words.Length - 1, words.Skip(1).ToArray());
                if (all == null) return null;
                string w = word.ToLowerInvariant();
                // those starting with it first, then those containing it
                options = all.Where(o => o.ToLowerInvariant().StartsWith(w)).Concat(all.Where(o => !o.ToLowerInvariant().StartsWith(w) && o.ToLowerInvariant().Contains(w))).Distinct().ToList();
            }
            if (options == null || options.Count == 0) return null;
            options = options.Select(o => o + " ").ToList();
            cycle = options; cycleAt = 0; cycleHead = head;
            return cycleText = head + options[0];
        }

        static List<string> Players(bool everyone)
        {
            var names = StartOfRound.Instance.allPlayerScripts.Where(x => x != null && (x.isPlayerControlled || x.isPlayerDead))
                .Select(x => x.playerUsername ?? "").Where(n => n.Length > 0 && !n.Contains(' ')).Distinct().ToList();
            if (everyone) names.Add("@a");
            return names;
        }

        static List<string> ItemNames()
        {
            // ours by key (one word), then the game's own items by name, squashed into one word
            var list = ModItems.ByKey.Keys.Where(k => !k.StartsWith("scrap_")).OrderBy(k => k).ToList();
            if (StartOfRound.Instance != null)
                list.AddRange(StartOfRound.Instance.allItemsList.itemsList.Where(it => it != null && !string.IsNullOrEmpty(it.itemName) && !ModItems.ByKey.Values.Contains(it))
                    .Select(it => TerminalText.Squash(it.itemName)).Distinct().OrderBy(k => k));
            return list;
        }
    }
}
