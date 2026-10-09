using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;

namespace LethalMinecraft
{
    /// <summary>
    /// Issue #25: monsters now and then lose the navmesh ("Agent 'X' #n: Agent not on nav mesh when trying to set
    /// destination", every frame). Not reproduced on purpose yet, so when the game logs it, this writes down once per
    /// monster where it is and what's around it: how far the navmesh is, dug cells and placed blocks near it, and the
    /// nearest player. Costs nothing until that error shows up.
    /// </summary>
    public static class NavDiag
    {
        static readonly Regex Pattern = new Regex(@"Agent '(.+)' #(\d+): Agent not on nav mesh");
        static readonly HashSet<int> reported = new HashSet<int>(); // (instance ids: once per monster)
        static int total;

        public static void Init() => Application.logMessageReceived += OnLog;

        static void OnLog(string msg, string trace, LogType type)
        {
            if (type != LogType.Error || msg == null || !msg.Contains("Agent not on nav mesh")) return;
            total++;
            var m = Pattern.Match(msg);
            if (!m.Success || reported.Count > 40) return;
            try { Report(m.Groups[1].Value, int.Parse(m.Groups[2].Value)); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[navdiag] " + e.Message); }
        }

        static void Report(string name, int index)
        {
            var rm = RoundManager.Instance;
            if (rm == null) return;
            var e = rm.SpawnedEnemies.FirstOrDefault(x => x != null && x.thisEnemyIndex == index && x.enemyType != null && x.enemyType.enemyName == name);
            if (e == null || !reported.Add(e.GetInstanceID())) return;
            var pos = e.transform.position;
            var sb = new StringBuilder();
            sb.Append($"[navdiag #25] '{name}' #{index} at {pos.x:F1},{pos.y:F1},{pos.z:F1} outside={e.isOutside} ");
            sb.Append($"agentEnabled={e.agent != null && e.agent.enabled} state={e.currentBehaviourStateIndex} special={e.inSpecialAnimation} ");
            sb.Append($"dead={e.isEnemyDead} owner={e.IsOwner} serverPos={e.serverPosition.x:F1},{e.serverPosition.y:F1},{e.serverPosition.z:F1}");
            // the navmesh nearest to it
            if (NavMesh.SamplePosition(pos, out var hit, 10f, NavMesh.AllAreas))
                sb.Append($" | navmesh {Vector3.Distance(hit.position, pos):F2} m away at {hit.position.x:F1},{hit.position.y:F1},{hit.position.z:F1}");
            else sb.Append(" | no navmesh within 10 m");
            // dug cells and placed blocks around it (3 cells each way), and the ground cell it stands in / on
            if (BlockWorld.Instance != null)
            {
                var c = Ground.CellOf(pos);
                int dug = 0, near = 0;
                var nearest = (Vector3Int?)null; float best = float.MaxValue;
                for (int dx = -3; dx <= 3; dx++)
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dz = -3; dz <= 3; dz++)
                        {
                            var cc = c + new Vector3Int(dx, dy, dz);
                            if (!Ground.IsDug(cc)) continue;
                            dug++;
                            float d = new Vector3(dx, dy, dz).sqrMagnitude;
                            if (d < best) { best = d; nearest = cc; }
                        }
                foreach (var b in BlockWorld.Instance.Blocks.Values)
                    if (b.Go != null && Vector3.Distance(b.Go.transform.position, pos) < 3f * BlockWorld.S) near++;
                sb.Append($" | cell {c} dug={Ground.IsDug(c)} below dug={Ground.IsDug(c + Vector3Int.down)}; {dug} dug cells within 3");
                if (nearest.HasValue) sb.Append($" (nearest {nearest.Value})");
                sb.Append($", {near} placed blocks within 3");
            }
            // the nearest player (tests teleport god-mode players among monsters)
            var sor = StartOfRound.Instance;
            if (sor != null)
            {
                var pl = sor.allPlayerScripts.Where(p => p != null && p.isPlayerControlled).OrderBy(p => Vector3.Distance(p.transform.position, pos)).FirstOrDefault();
                if (pl != null) sb.Append($" | nearest player {pl.playerUsername} {Vector3.Distance(pl.transform.position, pos):F1} m");
            }
            sb.Append($" | errors so far {total}");
            Plugin.Log.LogWarning(sb.ToString());
        }

        /// <summary>A new round: report monsters afresh.</summary>
        public static void Reset() { reported.Clear(); total = 0; }
    }
}
