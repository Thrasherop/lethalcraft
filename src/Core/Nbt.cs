using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LethalMinecraft
{
    /// <summary>
    /// A reader for Minecraft's NBT (#223: its structure files, read out of the player's own Minecraft, as its textures
    /// are). Gzipped, big-endian, named tags: compounds come back as dictionaries, lists as lists, numbers as their type,
    /// strings as strings, arrays as arrays.
    /// </summary>
    public static class Nbt
    {
        public static object ReadGzip(byte[] data)
        {
            using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                return Read(ms.ToArray());
            }
        }

        public static object Read(byte[] d)
        {
            int p = 0;
            byte type = d[p++];
            int nl = U16(d, ref p); p += nl; // (the root's name)
            return Tag(d, ref p, type);
        }

        static int U16(byte[] d, ref int p) { int v = (d[p] << 8) | d[p + 1]; p += 2; return v; }
        static int I32(byte[] d, ref int p) { int v = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; return v; }
        static long I64(byte[] d, ref int p) { long hi = (uint)I32(d, ref p), lo = (uint)I32(d, ref p); return (hi << 32) | lo; }

        static object Tag(byte[] d, ref int p, byte type)
        {
            switch (type)
            {
                case 1: return (sbyte)d[p++];
                case 2: { int v = U16(d, ref p); return (short)v; }
                case 3: return I32(d, ref p);
                case 4: return I64(d, ref p);
                case 5: { var b = new[] { d[p + 3], d[p + 2], d[p + 1], d[p] }; p += 4; return BitConverter.ToSingle(b, 0); }
                case 6: { var b = new byte[8]; for (int i = 0; i < 8; i++) b[i] = d[p + 7 - i]; p += 8; return BitConverter.ToDouble(b, 0); }
                case 7: { int n = I32(d, ref p); var a = new byte[n]; Array.Copy(d, p, a, 0, n); p += n; return a; }
                case 8: { int n = U16(d, ref p); var s = Encoding.UTF8.GetString(d, p, n); p += n; return s; }
                case 9:
                    {
                        byte et = d[p++]; int n = I32(d, ref p);
                        var list = new List<object>(Math.Max(0, n));
                        for (int i = 0; i < n; i++) list.Add(Tag(d, ref p, et));
                        return list;
                    }
                case 10:
                    {
                        var c = new Dictionary<string, object>();
                        while (true)
                        {
                            byte et = d[p++];
                            if (et == 0) return c;
                            int n = U16(d, ref p); var name = Encoding.UTF8.GetString(d, p, n); p += n;
                            c[name] = Tag(d, ref p, et);
                        }
                    }
                case 11: { int n = I32(d, ref p); var a = new int[n]; for (int i = 0; i < n; i++) a[i] = I32(d, ref p); return a; }
                case 12: { int n = I32(d, ref p); var a = new long[n]; for (int i = 0; i < n; i++) a[i] = I64(d, ref p); return a; }
                default: throw new InvalidDataException("NBT tag " + type);
            }
        }

        /// <summary>A structure file's blocks: (x, y, z, block id with "minecraft:" taken off, its properties).</summary>
        public static List<(int x, int y, int z, string id, Dictionary<string, string> props)> StructureBlocks(object root)
        {
            var outp = new List<(int, int, int, string, Dictionary<string, string>)>();
            var r = (Dictionary<string, object>)root;
            var palette = (List<object>)r["palette"];
            foreach (Dictionary<string, object> b in (List<object>)r["blocks"])
            {
                var pos = (List<object>)b["pos"];
                var st = (Dictionary<string, object>)palette[(int)b["state"]];
                // (the palette's name is "Name" in older files, "id" in 26.x's)
                string id = (string)(st.TryGetValue("id", out var i) ? i : st["Name"]);
                if (id.StartsWith("minecraft:")) id = id.Substring(10);
                var props = new Dictionary<string, string>();
                object pr = st.TryGetValue("properties", out var p1) ? p1 : st.TryGetValue("Properties", out var p2) ? p2 : null;
                if (pr is Dictionary<string, object> pd) foreach (var kv in pd) props[kv.Key] = kv.Value as string;
                outp.Add(((int)pos[0], (int)pos[1], (int)pos[2], id, props));
            }
            return outp;
        }
    }
}
