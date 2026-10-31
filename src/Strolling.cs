using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Minecraft's wander for our mobs (#172; RandomStrollGoal, DefaultRandomPos): standing about, now and then (a 1 in
    /// 60 chance every other tick: about every 6 seconds) one picks a spot up to 10 blocks off and 7 up or down that it
    /// can walk to, walks there, and stands about again. (The game's search had walked them from node to node across the
    /// whole map, never stopping.)
    /// </summary>
    public static class Strolling
    {
        public static int Strolls, Wet; // (dev/tests)
        static NavMeshPath path;

        /// <summary>Owner, each AI step while it has nothing else to do.</summary>
        public static void Step(EnemyAI e)
        {
            var agent = e.agent;
            if (!e.IsOwner || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
            if (e.currentSearch != null && e.currentSearch.inProgress) e.StopSearch(e.currentSearch);
            if (agent.pathPending || (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.3f)) return; // (still on its way)
            float p = 1f - Mathf.Pow(59f / 60f, Mathf.Max(0.05f, e.AIIntervalTime) / 0.1f);
            if (Random.value > p) return;
            if (path == null) path = new NavMeshPath();
            float S = BlockWorld.S;
            for (int i = 0; i < 10; i++)
            {
                var off = new Vector3(Random.Range(-10, 11), Random.Range(-7, 8), Random.Range(-10, 11)) * S;
                if (!NavMesh.SamplePosition(e.transform.position + off, out var h, 1.5f * S, agent.areaMask)) continue;
                // (#247) dry ground only, as Minecraft's land mobs (a drowned walks the bottom)
                if (!(e is DrownedAI) && MoonWater.Wet(h.position)) { Wet++; continue; }
                if (!agent.CalculatePath(h.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                e.SetDestinationToPosition(h.position);
                Strolls++;
                return;
            }
        }
    }
}
