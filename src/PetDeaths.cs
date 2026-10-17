using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#232, the owner's) A tamed pet's death in Minecraft's chat, as Minecraft tells a pet's owner (LivingEntity.die: a
    /// TamableAnimal's death message to its owner only): its name (a name tag's, or its kind's) and how it died, in
    /// Minecraft's words (death.attack.*: "Rex was slain by Steve", "Polly was blown up by Creeper", "Rex burned to
    /// death"...). And one Minecraft has no need of: a pet the ship left on the moon (sat down and never fetched, wandered
    /// off) is lost at the round's end: "Rex was left behind on Experimentation".
    /// </summary>
    [HarmonyPatch]
    public static class PetDeaths
    {
        /// <summary>What's hurting now, around a damage call that has no player (fire, an anvil, a blast...).</summary>
        public static string Context;
        /// <summary>Host: the next blast's maker ("Creeper"), for the explosion that follows in the same frame.</summary>
        public static string NextBlastBy; static int nextBlastFrame = -1;
        public static int Told; // (dev/tests)
        public static string LastText = "";

        public static void BlastBy(string who) { NextBlastBy = who; nextBlastFrame = Time.frameCount; }
        public static string TakeBlastBy() => Time.frameCount - nextBlastFrame <= 2 ? NextBlastBy : null;

        /// <summary>Every machine: what hit a tamed pet (the host's copy is the one that counts).</summary>
        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.HitEnemy)), HarmonyPrefix]
        static void Hurt(EnemyAI __instance, PlayerControllerB playerWhoHit)
        {
            if (!(__instance is PetAI pet) || !pet.Tamed || pet.isEnemyDead) return;
            // (one hit comes twice: the hitter's own call, with the player, and the game's RPC echo without: keep the player)
            if (playerWhoHit == null && Context == null && pet.LastHurt != null && pet.LastHurt.StartsWith("player:") && Time.time - pet.LastHurtAt < 0.5f) return;
            pet.LastHurt = playerWhoHit != null ? "player:" + playerWhoHit.playerUsername : Context ?? "generic";
            pet.LastHurtAt = Time.time;
        }

        static string NameOf(PetAI pet) => ShipMobs.NameOf(pet) ?? pet.enemyType.enemyName;

        /// <summary>Minecraft's death message (en_us death.attack.*) for what last hurt it.</summary>
        public static string Message(string name, string cause)
        {
            if (cause == null) return name + " died";
            int c = cause.IndexOf(':');
            string kind = c >= 0 ? cause.Substring(0, c) : cause, by = c >= 0 ? cause.Substring(c + 1) : null;
            switch (kind)
            {
                case "player": case "mob": return $"{name} was slain by {by}";
                case "explosion": return by != null ? $"{name} was blown up by {by}" : $"{name} blew up";
                case "fireball": return $"{name} was fireballed by {by ?? "Blaze"}";
                case "inFire": return $"{name} went up in flames";
                case "lava": return $"{name} tried to swim in lava";
                case "anvil": return $"{name} was squashed by a falling anvil";
                case "fall": return $"{name} fell from a high place";
                default: return $"{name} died";
            }
        }

        /// <summary>Host: a tamed pet died: its owner is told.</summary>
        public static void ServerDied(PetAI pet)
        {
            string cause = Time.time - pet.LastHurtAt < 10f ? pet.LastHurt : null;
            Tell(pet, Message(NameOf(pet), cause));
        }

        /// <summary>Host: a tamed pet the ship left behind (it goes with the round's monsters).</summary>
        public static void ServerLeftBehind(PetAI pet)
        {
            string moon = StartOfRound.Instance?.currentLevel?.PlanetName ?? "the moon";
            int sp = moon.IndexOf(' '); if (sp > 0 && char.IsDigit(moon[0])) moon = moon.Substring(sp + 1); // ("41 Experimentation")
            Tell(pet, $"{NameOf(pet)} was left behind on {moon}");
        }

        static void Tell(PetAI pet, string text)
        {
            var owner = pet.OwnerPlayer;
            Told++; LastText = text;
            Plugin.Log.LogInfo($"[pets] {text} (owner {(owner != null ? owner.playerUsername : "away")})");
            if (owner != null) BlockNet.ServerPetDeath(owner.actualClientId, text);
        }

        /// <summary>The owner's machine: the line in Minecraft's chat (a system message: white).</summary>
        public static void Receive(string text)
        {
            LastText = text;
            try { McChat.Add((text, McChat.White)); } catch (System.Exception e) { Plugin.Log.LogWarning("Minecraft chat: " + e.Message); }
        }
    }
}
