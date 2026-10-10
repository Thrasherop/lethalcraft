using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Hardcore mode (#87, [Game] Hardcore, the host's setting, off by default): a player who dies is out for the rest of
    /// the run. The game brings the dead back when the ship is in orbit again; in hardcore they die again straight away
    /// (no body), and watch the others. Once every player is out, the crew is fired, as if the quota had been missed.
    /// A totem of undying still saves you (you didn't die). Who is out is kept in the save file.
    /// </summary>
    [HarmonyPatch]
    public static class Hardcore
    {
        const string SaveKey = "LMC_Hardcore_v1";
        static readonly HashSet<string> outPlayers = new HashSet<string>();
        static bool loaded, fireWhenReady;
        static float killAgainAt = -1f;

        public static bool On => Plugin.Hardcore != null && Plugin.Hardcore.Value;
        public static bool IsOut(PlayerControllerB p) => p != null && outPlayers.Contains(Armor.IdentityOf(p));
        public static string Describe() => $"on={On} out=[{string.Join(",", outPlayers)}] fire={fireWhenReady}";

        public static void Reset() { outPlayers.Clear(); loaded = false; fireWhenReady = false; killAgainAt = -1f; }

        static IEnumerable<PlayerControllerB> Connected()
        {
            var sor = StartOfRound.Instance;
            return sor == null ? Enumerable.Empty<PlayerControllerB>() : sor.allPlayerScripts.Where(p => p != null && (p.isPlayerControlled || p.isPlayerDead) && sor.ClientPlayerList.ContainsKey(p.actualClientId));
        }

        /// <summary>Server, now and then: the dead are out; once all are, the crew is fired; the out die again after a revive.</summary>
        public static void ServerTick()
        {
            if (!On || !BlockNet.IsServer) return;
            var sor = StartOfRound.Instance;
            if (sor == null || GameNetworkManager.Instance == null) return;
            if (!loaded) { loaded = true; Load(GameNetworkManager.Instance.currentSaveFileName); }
            foreach (var p in Connected())
                if (p.isPlayerDead && outPlayers.Add(Armor.IdentityOf(p)))
                    Plugin.Log.LogInfo($"Hardcore: {p.playerUsername} is out");
            if (killAgainAt > 0f && Time.time >= killAgainAt)
            {
                killAgainAt = -1f;
                var all = Connected().ToList();
                if (all.Count > 0 && all.All(IsOut)) fireWhenReady = true;
                else foreach (var p in all) if (IsOut(p) && !p.isPlayerDead) BlockNet.ServerHardcoreOut(p.actualClientId);
            }
            if (fireWhenReady && sor.inShipPhase && !sor.firingPlayersCutsceneRunning && !sor.travellingToNewLevel)
            {
                fireWhenReady = false;
                Plugin.Log.LogInfo("Hardcore: everyone is out: the crew is fired");
                foreach (var p in Connected()) BlockNet.ServerToast(p.actualClientId, "Hardcore: the whole crew is dead.");
                sor.ManuallyEjectPlayersServerRpc();
            }
        }

        /// <summary>The game revived everyone (the ship is back in orbit): the ones who are out go again, in a moment.</summary>
        [HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ReviveDeadPlayers)), HarmonyPostfix]
        static void AfterRevive()
        {
            if (!On || !BlockNet.IsServer || outPlayers.Count == 0) return;
            killAgainAt = Time.time + 1.5f;
        }

        /// <summary>A player joining (or a save loaded) who is out: they go as soon as they're in.</summary>
        public static void ServerJoined(ulong client)
        {
            if (!On) return;
            var p = Connected().FirstOrDefault(x => x.actualClientId == client);
            if (IsOut(p)) killAgainAt = Time.time + 3f;
        }

        // ------------------------------------------------------------------ the out player's own client
        static bool forcing;

        /// <summary>Owner client: out of the run: dead, no body (the game won't let anyone die in orbit otherwise).</summary>
        public static void LocalOut()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (p == null || p.isPlayerDead) return;
            forcing = true;
            try { p.KillPlayer(Vector3.zero, spawnBody: false, CauseOfDeath.Unknown); }
            finally { forcing = false; }
            HUDManager.Instance?.DisplayTip("Hardcore", "You died this run: you watch the others now.", isWarning: true);
        }

        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.AllowPlayerDeath)), HarmonyPostfix]
        static void AllowOut(ref bool __result) { if (forcing) __result = true; }

        // ------------------------------------------------------------------ the save
        public static void Save(string file)
        {
            if (!On && outPlayers.Count == 0) return;
            ES3.Save(SaveKey, string.Join("\n", outPlayers), file);
        }

        static void Load(string file)
        {
            outPlayers.Clear();
            try { if (ES3.KeyExists(SaveKey, file)) foreach (var l in ES3.Load<string>(SaveKey, file).Split('\n')) if (l.Length > 0) outPlayers.Add(l); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Hardcore save: " + e.Message); }
            if (outPlayers.Count > 0) killAgainAt = Time.time + 5f;
        }

        /// <summary>Fired (the run is over): everyone's back in for the next one.</summary>
        public static void ClearSave(string file)
        {
            outPlayers.Clear(); fireWhenReady = false; killAgainAt = -1f;
            try { if (ES3.KeyExists(SaveKey, file)) ES3.DeleteKey(SaveKey, file); } catch { }
        }
    }
}
