using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// A storm picks its lightning targets from the metal items lying around when it starts (15 seconds into the
    /// landing) and never looks again, so anything crafted, mined, bought or dropped out of a stack later would be
    /// safe. Metal items that appear during a storm join its list.
    /// </summary>
    [HarmonyPatch]
    public static class Storms
    {
        public static bool Enabled = true; // (dev: off = the game's own behaviour)

        [HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.Start)), HarmonyPostfix]
        static void JoinStorm(GrabbableObject __instance)
        {
            if (!Enabled || __instance == null || __instance.itemProperties == null || !__instance.itemProperties.isConductiveMetal) return;
            var storm = Object.FindObjectOfType<StormyWeather>();
            if (storm == null || storm.metalObjects.Contains(__instance)) return;
            storm.metalObjects.Add(__instance);
        }

        public static int TargetedStrikes;

        // ------------------------------------------------------------------ #21: metal armor draws lightning
        /// <summary>Per second, per metal piece worn, during a storm, outdoors: the chance the storm picks that player.</summary>
        const float ChancePerPiece = 0.015f;
        const float WarningSeconds = 5f;
        static readonly System.Collections.Generic.Dictionary<ulong, float> pending = new System.Collections.Generic.Dictionary<ulong, float>();
        public static int ArmorStrikes;
        public static bool DevForceArmorStrike;

        static int MetalPieces(ulong client)
        {
            int n = 0;
            foreach (var k in Armor.Of(client)) { var d = Armor.Get(k); if (d != null && d.Metal) n++; }
            return n;
        }

        static bool Exposed(GameNetcodeStuff.PlayerControllerB p) =>
            p != null && p.isPlayerControlled && !p.isPlayerDead && !p.isInsideFactory && !p.isInHangarShipRoom && !p.isInElevator;

        /// <summary>Server, about once a second: a storm may pick a player wearing iron or gold armor; they get a warning
        /// (the static crackle) and the lightning comes a few seconds later, where they stand then, if they're still out
        /// in it with the metal on (take it off, or get under the ship's roof, and it misses).</summary>
        public static void ServerArmorTick()
        {
            if (!BlockNet.IsServer || !Balance.MetalArmorDrawsLightning || RoundManager.Instance == null) return;
            var storm = Object.FindObjectOfType<StormyWeather>();
            if (storm == null || !storm.isActiveAndEnabled) { pending.Clear(); return; }
            var sor = StartOfRound.Instance;
            foreach (var p in sor.allPlayerScripts)
            {
                if (!Exposed(p)) continue;
                ulong id = p.actualClientId;
                int pieces = MetalPieces(id);
                if (pending.TryGetValue(id, out float at))
                {
                    if (Time.time < at) continue;
                    pending.Remove(id);
                    if (pieces == 0) continue;
                    RoundManager.Instance.LightningStrikeServerRpc(p.transform.position);
                    ArmorStrikes++;
                    if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] lightning struck {p.playerUsername}'s metal armor ({pieces} pieces)");
                    continue;
                }
                if (pieces == 0) continue;
                if (!DevForceArmorStrike && Random.value >= ChancePerPiece * pieces) continue;
                DevForceArmorStrike = false;
                pending[id] = Time.time + WarningSeconds;
                BlockNet.ServerToast(id, "Your metal armor crackles with static...");
                BlockNet.ServerSound(p.transform.position, "fuse", 1.6f, 0.8f);
            }
        }

        [HarmonyPatch(typeof(StormyWeather), nameof(StormyWeather.LightningStrike)), HarmonyPostfix]
        static void CountStrike(Vector3 strikePosition, bool useTargetedObject)
        {
            if (!useTargetedObject) return;
            TargetedStrikes++;
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] lightning struck metal at {strikePosition}");
        }

        /// <summary>(dev) metal items the storm can strike, or null without a storm.</summary>
        public static string Describe()
        {
            var storm = Object.FindObjectOfType<StormyWeather>();
            if (storm == null) return null;
            var names = new System.Collections.Generic.List<string>();
            foreach (var o in storm.metalObjects) if (o != null) names.Add(o.itemProperties.itemName);
            return $"{names.Count} metal: " + string.Join(",", names) + (storm.targetingMetalObject != null ? " | targeting " + storm.targetingMetalObject.itemProperties.itemName : "") + $" | strikes on metal {TargetedStrikes}";
        }
    }
}
