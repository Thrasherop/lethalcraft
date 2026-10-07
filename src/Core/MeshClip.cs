using System.Collections.Generic;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Pure geometry used by the terrain carver: removes the parts of a triangle that lie inside a set of
    /// axis-aligned boxes. No engine calls, so it is covered by the offline unit tests.
    /// </summary>
    public static class MeshClip
    {
        /// <summary>A polygon vertex: either an original mesh vertex (Src0 == Src1) or a point between two others.</summary>
        public class PV
        {
            public int Src0 = -1, Src1 = -1;
            public float T;
            public Vector3 W;
            public PV PA, PB;

            public static PV Original(int index, Vector3 world) => new PV { Src0 = index, Src1 = index, T = 0, W = world };
        }

        /// <summary>Splits a convex polygon by the plane axis=value. The part on the "outside" side is added to pieces; returns the rest.</summary>
        public static List<PV> Split(List<PV> poly, int axis, float value, bool outsideIsLess, List<List<PV>> pieces)
        {
            if (poly.Count < 3) return poly;
            var outside = new List<PV>();
            var inside = new List<PV>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                float dp = p.W[axis] - value, dq = q.W[axis] - value;
                if (!outsideIsLess) { dp = -dp; dq = -dq; }
                bool pOut = dp < 0, qOut = dq < 0;
                if (pOut) outside.Add(p); else inside.Add(p);
                if (pOut != qOut)
                {
                    float t = dp / (dp - dq);
                    var x = new PV { T = t, W = Vector3.Lerp(p.W, q.W, t), PA = p, PB = q };
                    outside.Add(x);
                    inside.Add(x);
                }
            }
            if (outside.Count >= 3) pieces.Add(outside);
            return inside;
        }

        static bool Overlaps(Vector3 tmn, Vector3 tmx, Vector3 mn, Vector3 mx) =>
            tmx.x > mn.x && tmn.x < mx.x && tmx.z > mn.z && tmn.z < mx.z && tmx.y > mn.y && tmn.y < mx.y;

        /// <summary>
        /// The pieces of triangle (a, b, c) left after removing everything inside the boxes, as convex polygons.
        /// Returns null when no box touches the triangle (keep it as is).
        /// </summary>
        public static List<List<PV>> ClipOutsideBoxes(PV a, PV b, PV c, Vector3[] mins, Vector3[] maxs)
        {
            Vector3 tmn = Vector3.Min(a.W, Vector3.Min(b.W, c.W)), tmx = Vector3.Max(a.W, Vector3.Max(b.W, c.W));
            List<List<PV>> pieces = null;
            for (int k = 0; k < mins.Length; k++)
            {
                Vector3 mn = mins[k], mx = maxs[k];
                if (!Overlaps(tmn, tmx, mn, mx)) continue;
                if (pieces == null) pieces = new List<List<PV>> { new List<PV> { a, b, c } };
                var next = new List<List<PV>>();
                foreach (var poly0 in pieces)
                {
                    var poly = Split(poly0, 0, mn.x, true, next);
                    poly = Split(poly, 0, mx.x, false, next);
                    poly = Split(poly, 2, mn.z, true, next);
                    poly = Split(poly, 2, mx.z, false, next);
                    poly = Split(poly, 1, mn.y, true, next);
                    Split(poly, 1, mx.y, false, next); // remainder is inside the box: dropped
                }
                pieces = next;
            }
            return pieces;
        }

        /// <summary>Interpolation weights of the original vertices that make up a clipped vertex.</summary>
        public static void Accumulate(PV pv, float w, Dictionary<int, float> acc)
        {
            if (pv.Src0 >= 0)
            {
                acc.TryGetValue(pv.Src0, out float a0); acc[pv.Src0] = a0 + w * (1 - pv.T);
                if (pv.T > 0) { acc.TryGetValue(pv.Src1, out float a1); acc[pv.Src1] = a1 + w * pv.T; }
                return;
            }
            Accumulate(pv.PA, w * (1 - pv.T), acc);
            Accumulate(pv.PB, w * pv.T, acc);
        }

        /// <summary>Area of a planar convex polygon (fan triangulation).</summary>
        public static float Area(List<PV> poly)
        {
            float area = 0;
            for (int k = 1; k + 1 < poly.Count; k++)
                area += Vector3.Cross(poly[k].W - poly[0].W, poly[k + 1].W - poly[0].W).magnitude * 0.5f;
            return area;
        }
    }
}
