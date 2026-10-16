using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The moons' trees can be chopped down: hold left-click on a trunk (an axe is much faster) and it shatters the way
    /// it does when the cruiser hits it, dropping logs of the moon's wood (#223, the owner's: Dine's are dark oak, March's
    /// mangrove, every other moon's oak). Not a Minecraft tree: it goes all at once, nothing floats.
    /// The client chops, the server checks and drops the logs, every client breaks the tree (the game's own effect).
    /// </summary>
    public static class Trees
    {
        const int TerrainLayer = 25; // the game keeps its trees on the terrain layer (that's what its tree breaking looks for)

        /// <summary>The trunk collider of the tree this collider belongs to, or null.</summary>
        public static Collider TreeOf(Collider c)
        {
            if (c == null) return null;
            for (var t = c.transform; t != null; t = t.parent)
            {
                if (!t.CompareTag("Tree") || t.gameObject.layer != TerrainLayer) continue;
                foreach (var tc in t.GetComponents<Collider>()) if (!tc.isTrigger) return tc;
            }
            return null;
        }

        /// <summary>(#223) The wood the moon's trees are: Dine's dark oak, March's mangrove, the rest oak.</summary>
        public static Blocks.WoodKind WoodHere()
        {
            string moon = StartOfRound.Instance?.currentLevel?.PlanetName ?? "";
            if (moon.IndexOf("Dine", System.StringComparison.OrdinalIgnoreCase) >= 0) return Blocks.DarkOak;
            if (moon.IndexOf("March", System.StringComparison.OrdinalIgnoreCase) >= 0) return Blocks.Mangrove;
            return Blocks.Oak;
        }

        /// <summary>Chopping time: three oak logs' worth.</summary>
        public static float TimeFor(ToolItem tool) => 3f * Builder.BreakTime(Blocks.Log, tool);

        // ------------------------------------------------------------------ server
        public static void ServerChop(ulong sender, Vector3 at)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || Vector3.Distance(p.gameplayCamera.transform.position, at) > 9f * BlockWorld.S + 3f) return;
            Collider tree = null; float best = 2f;
            foreach (var c in Physics.OverlapSphere(at, 1.5f, 1 << TerrainLayer, QueryTriggerInteraction.Ignore))
            {
                var t = TreeOf(c);
                if (t == null) continue;
                float d = Vector3.Distance(t.ClosestPoint(at), at);
                if (d < best) { best = d; tree = t; }
            }
            if (tree == null) return;
            var center = tree.bounds.center;
            // the logs land on the ground in front of the trunk, on the chopper's side (the trunk itself reaches well into
            // the ground: its bottom is no place to drop anything)
            var toward = p.transform.position - at; toward.y = 0;
            var from = at + (toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward) * 0.6f + Vector3.up * 1.5f;
            var drop = from + Vector3.down * 1.5f;
            float nearest = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 30f, ServerLogic.WorldGeometryMask, QueryTriggerInteraction.Ignore))
                if (TreeOf(h.collider) == null && h.distance < nearest) { nearest = h.distance; drop = h.point; }
            BlockNet.ServerTreeFell(center);
            if (GameModes.IsCreative(sender) || !ModItems.ByKey.TryGetValue(WoodHere().Log.Key, out var log)) return;
            ModItems.ServerSpawnStack(log, Random.Range(4, 7), drop + Vector3.up * 0.5f);
            // (#223) and a sapling or two of its wood, as its leaves would give
            if (WoodHere().Sapling != null && ModItems.ByKey.TryGetValue(WoodHere().Sapling.Key, out var sap)) ModItems.ServerSpawnStack(sap, Random.Range(1, 3), drop + Vector3.up * 0.5f);
        }

        // ------------------------------------------------------------------ every client
        /// <summary>The game's own tree breaking (shatter, sound, shake), just for this one tree.</summary>
        public static void Fell(Vector3 center)
        {
            if (RoundManager.Instance == null) return;
            if (!RoundManager.Instance.DestroyTreeAtPosition(center, 0.6f) && Plugin.DevMode.Value) Plugin.Log.LogWarning($"[dev] no tree to fell at {center}");
        }
    }
}
