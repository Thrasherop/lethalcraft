using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>Dev: logs network objects despawned shortly after a level loads (with who did it).</summary>
    [HarmonyPatch(typeof(NetworkObject), nameof(NetworkObject.Despawn))]
    static class DevDespawnLog
    {
        public static float LevelLoadedAt = -100f;
        static void Prefix(NetworkObject __instance)
        {
            if (!Plugin.DevMode.Value || Time.time - LevelLoadedAt > 15f) return;
            var g = __instance.GetComponent<GrabbableObject>();
            if (g == null) return;
            Plugin.Log.LogWarning($"[dev] despawn {g.itemProperties?.itemName} id {__instance.NetworkObjectId} scrap={g.itemProperties?.isScrap}\n{System.Environment.StackTrace}");
        }
    }
}
