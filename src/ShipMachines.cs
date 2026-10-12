using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>
    /// What's inside the ship's furnaces and brewing stands, saved with the ship (#120: only the blocks and the chests
    /// were; a reload left every furnace empty). Saved and loaded with the ship's blocks (ShipPersistence), in orbit.
    /// </summary>
    public static class ShipMachines
    {
        public const string FurnaceKey = "LMC_ShipFurnaces_v1", StandKey = "LMC_ShipBrewing_v1";

        static void Key(BinaryWriter w, BlockKey k) { w.Write(k.YOff); w.Write(k.Pos.x); w.Write(k.Pos.y); w.Write(k.Pos.z); }
        static BlockKey Key(BinaryReader r, byte frame) { short yoff = r.ReadInt16(); return new BlockKey(frame, yoff, new UnityEngine.Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32())); }
        static void Str(BinaryWriter w, string s) => w.Write(s ?? "");
        static string Str(BinaryReader r) { var s = r.ReadString(); return s.Length > 0 ? s : null; }

        public static void SaveAll(string file, BlockWorld world)
        {
            var fms = new MemoryStream(); var fw = new BinaryWriter(fms);
            var furnaces = Crafting.Furnaces.Where(kv => kv.Key.Frame == 1 && world.DefAt(kv.Key) == Blocks.Furnace).ToList();
            fw.Write(furnaces.Count);
            foreach (var kv in furnaces)
            {
                var f = kv.Value; Key(fw, kv.Key);
                Str(fw, f.In); fw.Write(f.InCount); Str(fw, f.FuelKey); fw.Write(f.Fuel); Str(fw, f.Out); fw.Write(f.OutCount);
                fw.Write(f.Burn); fw.Write(f.BurnMax); fw.Write(f.Progress);
            }
            ES3.Save(FurnaceKey, Convert.ToBase64String(fms.ToArray()), file);

            var sms = new MemoryStream(); var sw = new BinaryWriter(sms);
            var stands = Brewing.Stands.Where(kv => kv.Key.Frame == 1 && world.DefAt(kv.Key) == Blocks.BrewingStand).ToList();
            sw.Write(stands.Count);
            foreach (var kv in stands)
            {
                var s = kv.Value; Key(sw, kv.Key);
                sw.Write(s.Bottle.Length); foreach (var b in s.Bottle) Str(sw, b);
                Str(sw, s.IngKey); sw.Write(s.IngCount); sw.Write(s.Powder); sw.Write(s.FuelLeft);
            }
            ES3.Save(StandKey, Convert.ToBase64String(sms.ToArray()), file);
        }

        public static void LoadAll(string file)
        {
            if (ES3.KeyExists(FurnaceKey, file))
            {
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(FurnaceKey, file))));
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var k = Key(r, 1);
                    var f = new Crafting.Furnace { In = Str(r), InCount = r.ReadInt32(), FuelKey = Str(r), Fuel = r.ReadInt32(), Out = Str(r), OutCount = r.ReadInt32(), Burn = r.ReadSingle(), BurnMax = r.ReadSingle(), Progress = r.ReadSingle() };
                    Crafting.Furnaces[k] = f;
                    BlockNet.ServerFurnace(k, f);
                }
                Plugin.Log.LogInfo($"Loaded {n} ship furnaces");
            }
            if (ES3.KeyExists(StandKey, file))
            {
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(StandKey, file))));
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var k = Key(r, 1);
                    var s = new Brewing.Stand();
                    int bottles = r.ReadInt32();
                    for (int b = 0; b < bottles; b++) { var key = Str(r); if (b < s.Bottle.Length) s.Bottle[b] = key; }
                    s.IngKey = Str(r); s.IngCount = r.ReadInt32(); s.Powder = r.ReadInt32(); s.FuelLeft = r.ReadInt32();
                    Brewing.Stands[k] = s;
                    BlockNet.ServerBrewing(k, s);
                }
                Plugin.Log.LogInfo($"Loaded {n} ship brewing stands");
            }
        }
    }
}
