using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// #211: the owner's game stuttered (frames of half a second) with the Nether about, on every player's machine; it
    /// didn't on ours. Always on, cheap: a frame over 250 ms writes one line to the log with what was going on then, so
    /// the next such game says why (a garbage collection in that frame, how many monsters, items, blocks, the Nether).
    /// At most one line every 5 seconds; the ones in between are counted into the next.
    /// </summary>
    public class HitchLog : MonoBehaviour
    {
        const float Threshold = 0.25f;
        int lastGc; float nextLine; int skipped; float worstSkipped;

        void Update()
        {
            int gc = System.GC.CollectionCount(0);
            bool gcThisFrame = gc != lastGc;
            lastGc = gc;
            float dt = Time.unscaledDeltaTime;
            if (dt < Threshold) return;
            if (Time.realtimeSinceStartup < nextLine) { skipped++; worstSkipped = Mathf.Max(worstSkipped, dt); return; }
            nextLine = Time.realtimeSinceStartup + 5f;
            try
            {
                var rm = RoundManager.Instance; var sor = StartOfRound.Instance;
                int enemies = rm != null ? rm.SpawnedEnemies.Count : -1;
                int items = Object.FindObjectsOfType<GrabbableObject>().Length; // (only on a hitch)
                int blocks = BlockWorld.Instance != null ? BlockWorld.Instance.Blocks.Count : -1;
                var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
                bool inNether = p != null && NetherFortress.Root != null && NetherFortress.Contains(p.transform.position);
                Plugin.Log.LogWarning($"[hitch] {dt * 1000f:F0} ms frame; garbage collection in it: {(gcThisFrame ? "yes" : "no")} (gc count {gc}); "
                    + $"heap {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB; monsters {enemies}, items {items}, blocks {blocks}; "
                    + $"Nether {(NetherFortress.Root != null ? "up" : "none")}{(inNether ? " (I'm in it)" : "")}; players {(sor != null ? sor.connectedPlayersAmount + 1 : 0)}; "
                    + $"since the last line: {skipped} more over 250 ms (worst {worstSkipped * 1000f:F0} ms)");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[hitch] {dt * 1000f:F0} ms ({e.GetType().Name})"); }
            skipped = 0; worstSkipped = 0f;
        }
    }
}
