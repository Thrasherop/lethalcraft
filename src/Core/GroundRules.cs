namespace LethalMinecraft
{
    /// <summary>
    /// Pure rules for natural ground (no engine calls, covered by the offline unit tests):
    /// solid/air from six axis rays, and what a cell is made of.
    /// </summary>
    public static class GroundRules
    {
        public enum Hit : byte { None, Front, Back }

        /// <summary>
        /// hits[f] = first surface along Faces.Dir[f] (0 down, 1 up, 2..5 horizontal), seen from its front (we are on its
        /// open side) or its back (we are inside whatever it bounds). Open sky above = air. An enclosed room (floor below,
        /// front faces around) = air. Under an overhang = air. Anything else with something overhead = solid.
        /// </summary>
        public static bool IsSolid(Hit[] hits)
        {
            if (hits[1] == Hit.None) return false; // open sky
            int air = 0, solid = 0;
            foreach (var h in hits)
            {
                if (h == Hit.Front) air++;
                else if (h == Hit.Back) solid++;
            }
            bool downFront = hits[0] == Hit.Front, upFront = hits[1] == Hit.Front;
            // a room: walls/ceiling seen from the front all around (the floor may have an opening, e.g. catwalk pits).
            // With the back of a surface overhead we're under ground (buried rocks and props show their outer faces
            // sideways), unless literally every other direction is room: a room with a hole in its ceiling.
            if (hits[0] != Hit.Back && air >= 4 && air > solid * 2 && (hits[1] == Hit.Front || (air >= 5 && solid == 1))) return false;
            if (upFront && downFront && solid == 0) return false; // under an overhang / tree / bridge
            // something overhead and no sign of open space: rock (e.g. under the facility's outer bottom face)
            return true;
        }

        public enum Layer { Top, Soil, Snow, Stone, Planks }

        /// <summary>Layering: the surface material at the top / in molded cells, a soil layer, then rock.</summary>
        public static Layer LayerFor(bool partial, float depth, bool stonyTop, bool snowTop, bool planksTop)
        {
            if (partial || depth < 0.5f) return Layer.Top;
            // snowy ground: a layer of snow on top (the Top layer), then dirt like everywhere else
            if (depth < 2.5f && !stonyTop && !planksTop) return Layer.Soil;
            if (depth < 1.5f) return planksTop ? Layer.Planks : Layer.Stone;
            return Layer.Stone;
        }

        public enum Ore { None, Coal, Iron, Gold, Diamond, Emerald }

        /// <summary>Deterministic per-cell hash in [0, 1).</summary>
        public static float Roll(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ 0x9E3779B9u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h % 100000) / 100000f;
        }

        /// <summary>Chance of each ore in a stone cell, near open space and deep in the rock (the config sets these).</summary>
        public class OreRates
        {
            public float Coal = 0.05f, CoalDeep = 0.05f, Iron = 0.025f, IronDeep = 0.035f, Gold = 0.008f, GoldDeep = 0.018f;
            public float Diamond = 0.0035f, DiamondDeep = 0.0115f, Emerald = 0.002f, EmeraldDeep = 0.005f;
            /// <summary>Diamonds only this many blocks (or more) from any open space.</summary>
            public float DiamondMinDepth = 4.5f;
        }
        public static readonly OreRates Rates = new OreRates();

        /// <summary>Ore in a stone cell. Deeper (further from any open space) = richer; gold, diamonds and emeralds never right at a wall.</summary>
        public static Ore OreFor(int x, int y, int z, float depth)
        {
            float r = Roll(x, y, z);
            float rich = depth <= 3f ? 0f : depth >= 23f ? 1f : (depth - 3f) / 20f;
            var k = Rates;
            float At(float near, float deep) => near + rich * (deep - near);
            float coal = At(k.Coal, k.CoalDeep), iron = coal + At(k.Iron, k.IronDeep), gold = iron + (depth > 3.5f ? At(k.Gold, k.GoldDeep) : 0f);
            float diamond = gold + (depth >= k.DiamondMinDepth ? At(k.Diamond, k.DiamondDeep) : 0f), emerald = diamond + (depth > 4.5f ? At(k.Emerald, k.EmeraldDeep) : 0f);
            if (r < coal) return Ore.Coal;
            if (r < iron) return Ore.Iron;
            if (r < gold) return Ore.Gold;
            if (r < diamond) return Ore.Diamond;
            if (r < emerald) return Ore.Emerald;
            return Ore.None;
        }
    }
}
