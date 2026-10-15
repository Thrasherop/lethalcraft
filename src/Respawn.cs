using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// /respawn [player] (#129): a dead player back in the ship mid-round, like the game's own revive in orbit
    /// (StartOfRound.ReviveDeadPlayers) but for one player, on every machine: their body goes, they stand at their spawn
    /// point in the ship with full health, and their own machine leaves the spectator view.
    /// </summary>
    public static class Respawn
    {
        public static int Revived; // (dev/tests)

        /// <summary>Server: the command.</summary>
        public static string ServerRespawn(PlayerControllerB t)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || t == null) return "No such player.";
            if (!t.isPlayerDead) return $"{t.playerUsername} is alive.";
            if (sor.inShipPhase || !sor.shipHasLanded) return "In orbit everyone's back anyway.";
            if (sor.shipIsLeaving) return "The ship is leaving.";
            BlockNet.ServerRespawn((int)t.playerClientId);
            return null; // (announced to everyone by each machine)
        }

        /// <summary>Every machine: player i is back, in the ship.</summary>
        public static void Apply(int i)
        {
            var sor = StartOfRound.Instance;
            if (sor == null || i < 0 || i >= sor.allPlayerScripts.Length) return;
            var p = sor.allPlayerScripts[i];
            if (p == null || !p.isPlayerDead) return;

            // the body goes (dropped first if someone carries it)
            foreach (var rg in Object.FindObjectsOfType<RagdollGrabbableObject>())
            {
                if (rg == null || rg.ragdoll == null || rg.ragdoll.playerObjectId != i) continue;
                if (rg.isHeld && rg.playerHeldBy != null) rg.playerHeldBy.DropAllHeldItems();
                if (sor.IsServer && rg.NetworkObject != null && rg.NetworkObject.IsSpawned) rg.NetworkObject.Despawn();
            }
            foreach (var b in Object.FindObjectsOfType<DeadBodyInfo>(true)) if (b != null && b.playerObjectId == i) Object.Destroy(b.gameObject);
            p.deadBody = null;

            // the game's revive, for this one player
            p.ResetPlayerBloodObjects(true);
            p.isClimbingLadder = false;
            p.clampLooking = false;
            p.inVehicleAnimation = false;
            p.overridePoisonValue = false;
            p.disableMoveInput = false;
            p.ResetZAndXRotation();
            p.thisController.enabled = true;
            p.health = 100;
            p.hasBeenCriticallyInjured = false;
            p.disableLookInput = false;
            p.disableInteract = false;
            p.nightVisionRadar.enabled = false;
            p.isPlayerDead = false;
            p.enemyWaitingForBodyRagdoll = null;
            p.isPlayerControlled = true;
            p.isInElevator = true;
            p.isInHangarShipRoom = true;
            p.isInsideFactory = false;
            p.parentedToElevatorLastFrame = false;
            p.overrideGameOverSpectatePivot = null;
            p.TeleportPlayer(sor.GetPlayerSpawnPosition(i));
            p.setPositionOfDeadPlayer = false;
            p.DisablePlayerModel(sor.allPlayerObjects[i], enable: true, disableLocalArms: true);
            p.helmetLight.enabled = false;
            p.Crouch(false);
            p.criticallyInjured = false;
            if (p.playerBodyAnimator != null) p.playerBodyAnimator.SetBool("Limp", false);
            p.bleedingHeavily = false;
            p.activatingItem = false;
            p.twoHanded = false;
            p.inShockingMinigame = false;
            p.inSpecialInteractAnimation = false;
            p.freeRotationInInteractAnimation = false;
            p.disableSyncInAnimation = false;
            p.inAnimationWithEnemy = null;
            p.holdingWalkieTalkie = false;
            p.speakingToWalkieTalkie = false;
            p.isSinking = false;
            p.isUnderwater = false;
            p.sinkingValue = 0f;
            p.statusEffectAudio.Stop();
            p.DisableJetpackControlsLocally();
            p.mapRadarDotAnimator.SetBool("dead", false);
            p.externalForceAutoFade = Vector3.zero;
            p.carryWeight = 1f;
            p.voiceMuffledByEnemy = false;
            if (SoundManager.Instance != null) { SoundManager.Instance.playerVoicePitchTargets[i] = 1f; SoundManager.Instance.SetPlayerPitch(1f, i); }
            sor.livingPlayers++;
            sor.allPlayersDead = false;

            if (p.IsOwner)
            {
                // out of the spectator view: the game's camera, HUD and listener back on this player
                var hud = HUDManager.Instance;
                hud.SetCracksOnVisor(100f);
                hud.gasHelmetAnimator.SetBool("gasEmitting", false);
                p.hasBegunSpectating = false;
                hud.RemoveSpectateUI();
                // (the spectator's lines are only refreshed while dead: cleared, or they'd stay on screen)
                hud.spectatingPlayerText.text = "";
                hud.holdButtonToEndGameEarlyText.text = "";
                hud.holdButtonToEndGameEarlyVotesText.text = "";
                hud.holdButtonToEndGameEarlyMeter.gameObject.SetActive(false);
                p.hinderedMultiplier = 1f;
                p.isMovementHindered = 0;
                p.sourcesCausingSinking = 0;
                p.reverbPreset = sor.shipReverb;
                p.spectatedPlayerScript = null;
                hud.audioListenerLowPass.enabled = false;
                hud.cadaverFilter = 0f;
                sor.SetSpectateCameraToGameOverMode(false, p);
                sor.SwitchCamera(p.gameplayCamera);
                p.ChangeAudioListenerToObject(p.gameplayCamera.gameObject);
                hud.HideHUD(false);
                hud.UpdateHealthUI(100, false);
            }
            Revived++;
            HUDManager.Instance?.AddChatMessage($"{p.playerUsername} is back in the ship.");
            Plugin.Log.LogInfo($"[respawn] {p.playerUsername} revived (living {sor.livingPlayers})");
        }
    }
}
