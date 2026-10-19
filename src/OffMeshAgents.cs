using HarmonyLib;

namespace LethalMinecraft
{
    /// <summary>
    /// The game turns its owner's navmesh agent back on every frame (EnemyAI.Update: SetClientCalculatingAI(true)). A mob
    /// that moves itself off any navmesh (a squid, a phantom, a ghast in the air, #191) turns its agent off; turned on
    /// again away from a navmesh, Unity fails to place it and logs a warning with a stack trace, each time (#211: the
    /// owner's log had 29,240). For those, the game's switch keeps the agent off.
    /// </summary>
    [HarmonyPatch]
    static class OffMeshAgents
    {
        public static int Kept; // (dev)

        [HarmonyPatch(typeof(EnemyAI), nameof(EnemyAI.SetClientCalculatingAI)), HarmonyPrefix]
        static bool Prefix(EnemyAI __instance, bool enable)
        {
            if (!enable || !(__instance is MinecraftMobAI m) || !m.OffNavMesh) return true;
            __instance.isClientCalculatingAI = true;
            if (__instance.agent != null && __instance.agent.enabled) __instance.agent.enabled = false;
            Kept++;
            return false;
        }
    }
}
