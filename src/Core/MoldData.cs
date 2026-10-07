namespace LethalMinecraft
{
    /// <summary>
    /// Engine-free layout of a natural block's shape data: res x res surface heights (0..255 of a block), then one byte
    /// with the faces (bit = Face index, frame directions) that border a dug cell. Only those faces are drawn: the rest of
    /// the block is inside the ground, and its top is drawn by the real, uncut terrain.
    /// </summary>
    public static class MoldData
    {
        /// <summary>Data written before exposure existed (heights only) counts as exposed everywhere.</summary>
        public static bool HasExposure(byte[] m, int res) => m != null && m.Length > res * res;

        public static bool IsExposed(byte[] m, int res, int face) => !HasExposure(m, res) || (m[res * res] & (1 << face)) != 0;

        /// <summary>A copy with <paramref name="face"/> marked exposed (or the same array if it already was).</summary>
        public static byte[] WithExposed(byte[] m, int res, int face)
        {
            if (HasExposure(m, res) && (m[res * res] & (1 << face)) != 0) return m;
            var r = new byte[res * res + 1];
            System.Array.Copy(m, r, System.Math.Min(m.Length, res * res));
            r[res * res] = (byte)((HasExposure(m, res) ? m[res * res] : 0) | (1 << face));
            return r;
        }

        /// <summary>Heights only, with no face exposed yet.</summary>
        public static byte[] FromHeights(byte[] heights, int res)
        {
            var r = new byte[res * res + 1];
            System.Array.Copy(heights, r, res * res);
            return r;
        }
    }
}
