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

        /// <summary>(dev) metal items the storm can strike, or null without a storm.</summary>
        public static string Describe()
        {
            var storm = Object.FindObjectOfType<StormyWeather>();
            if (storm == null) return null;
            var names = new System.Collections.Generic.List<string>();
            foreach (var o in storm.metalObjects) if (o != null) names.Add(o.itemProperties.itemName);
            return $"{names.Count} metal: " + string.Join(",", names) + (storm.targetingMetalObject != null ? " | targeting " + storm.targetingMetalObject.itemProperties.itemName : "");
        }
    }
}
