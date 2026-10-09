using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Starting supplies (#33): a new crew can't feed itself on 60 credits. The ship has a supply chest; each player gets
    /// their share put in it (steak and oak planks, [Starter] in the config) the first time they're aboard on this save.
    /// Being in the ship's chest, the food is still there after someone dies. A team wipe (which empties the ship's
    /// chests) and being fired (the ship's blocks go) start everyone's share over.
    /// </summary>
    public static class Starter
    {
        const string SaveKey = "LMC_Starter_v1";
        static readonly HashSet<string> given = new HashSet<string>();
        static BlockKey? chest;
        static string loadedFor;
        static float nextNoRoomLog;

        /// <summary>Where the chest goes, relative to the ship (elevatorTransform), first free one wins: the floor along
        /// the back wall, then beside the door.</summary>
        public static Vector3[] Spots =
        {
            new Vector3(8.1f, 0.4f, -9.6f), new Vector3(7.4f, 0.4f, -9.6f), new Vector3(8.1f, 0.4f, -8.9f),
            new Vector3(6.7f, 0.4f, -9.6f), new Vector3(4.6f, 0.4f, -9.6f), new Vector3(1.8f, 0.4f, -8.2f),
        };

        static bool Enabled => Balance.StarterSteakPerPlayer > 0 || Balance.StarterPlanksPerPlayer > 0;

        /// <summary>Server, about once a second: anyone aboard who hasn't had their share gets it.</summary>
        public static void ServerTick()
        {
            var sor = StartOfRound.Instance;
            var world = BlockWorld.Instance;
            if (!Enabled || !BlockNet.IsServer || sor == null || world == null || GameNetworkManager.Instance == null) return;
            if (sor.isChallengeFile) return;
            string file = GameNetworkManager.Instance.currentSaveFileName;
            if (loadedFor != file) Load(file);
            var due = new List<PlayerControllerB>();
            foreach (var p in sor.allPlayerScripts)
            {
                if (p == null || !p.isPlayerControlled || p.isPlayerDead) continue;
                string id = Armor.IdentityOf(p);
                if (string.IsNullOrEmpty(id) || id == "Player" || given.Contains(id)) continue; // (not known yet while joining)
                due.Add(p);
            }
            if (due.Count == 0) return;
            var c = EnsureChest(world);
            if (c == null) return;
            foreach (var p in due)
            {
                var left = Chests.PutInto(c, -1, "steak", Balance.StarterSteakPerPlayer, false).n;
                left += Chests.PutInto(c, -1, "oak_planks", Balance.StarterPlanksPerPlayer, false).n;
                given.Add(Armor.IdentityOf(p));
                Plugin.Log.LogInfo($"Starting supplies for {p.playerUsername}: {Balance.StarterSteakPerPlayer} steak, {Balance.StarterPlanksPerPlayer} oak planks in the ship's supply chest" + (left > 0 ? $" ({left} didn't fit)" : ""));
            }
            BlockNet.ServerChest(chest.Value, c);
            if (sor.inShipPhase) ShipPersistence.Save();
        }

        static Chests.Contents EnsureChest(BlockWorld world)
        {
            if (chest.HasValue && world.DefAt(chest.Value) == Blocks.Chest)
            {
                if (!Chests.All.TryGetValue(chest.Value, out var have)) Chests.All[chest.Value] = have = new Chests.Contents();
                return have;
            }
            var sor = StartOfRound.Instance;
            world.FrameRoot(1, true);
            foreach (var spot in Spots)
            {
                var wp = sor.elevatorTransform.TransformPoint(spot);
                // standing on the ship's floor there (the ship's grid has a sub-block height offset, like placing by hand)
                var up = sor.elevatorTransform.up;
                if (!Physics.Raycast(wp + up, -up, out var fh, 2.5f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) continue;
                if (fh.collider.GetComponentInParent<BlockRef>() != null) continue;
                var lp = world.ToFrameLocal(1, fh.point) / BlockWorld.S;
                int cy = Mathf.FloorToInt(lp.y), yoff = Mathf.RoundToInt((lp.y - cy) * 1000f);
                if (yoff >= 1000) { yoff -= 1000; cy++; }
                var key = new BlockKey(1, (short)yoff, new Vector3Int(Mathf.FloorToInt(lp.x), cy, Mathf.FloorToInt(lp.z)));
                if (world.Has(key)) continue;
                var center = world.WorldCenter(key);
                // (the ship's furniture, walls, and anything else solid in the way)
                if (Physics.CheckBox(center, Vector3.one * BlockWorld.S * 0.4f, world.FrameRotation(1), StartOfRound.Instance.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore)) continue;
                // face into the ship (towards its middle)
                var to = sor.elevatorTransform.position - wp;
                byte facing = Faces.FromVectorHorizontal(world.ToFrameLocalDir(1, to));
                BlockNet.ServerBroadcastOp(Op.Set(key, new BlockData(Blocks.Chest.Id, facing, 0)));
                chest = key;
                var c = new Chests.Contents();
                Chests.All[key] = c;
                Plugin.Log.LogInfo($"Placed the ship's supply chest at {key.Pos}");
                return c;
            }
            if (Time.time > nextNoRoomLog) { Plugin.Log.LogWarning("No room in the ship for the supply chest"); nextNoRoomLog = Time.time + 300f; }
            return null;
        }

        /// <summary>Dev: what each candidate spot hits (why the chest can't go there).</summary>
        public static string DevSpots(Vector3? extra = null)
        {
            var sor = StartOfRound.Instance; var world = BlockWorld.Instance;
            var sb = new System.Text.StringBuilder();
            foreach (var spot in extra.HasValue ? new[] { extra.Value } : Spots)
            {
                var wp = sor.elevatorTransform.TransformPoint(spot);
                var up = sor.elevatorTransform.up;
                sb.Append($"{spot}: ");
                if (!Physics.Raycast(wp + up, -up, out var fh, 2.5f, sor.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)) { sb.Append("no floor ; "); continue; }
                var lp = world.ToFrameLocal(1, fh.point) / BlockWorld.S;
                int cy = Mathf.FloorToInt(lp.y), yoff = Mathf.RoundToInt((lp.y - cy) * 1000f);
                if (yoff >= 1000) { yoff -= 1000; cy++; }
                var key = new BlockKey(1, (short)yoff, new Vector3Int(Mathf.FloorToInt(lp.x), cy, Mathf.FloorToInt(lp.z)));
                var hits = Physics.OverlapBox(world.WorldCenter(key), Vector3.one * BlockWorld.S * 0.4f, world.FrameRotation(1), sor.collidersAndRoomMaskAndDefault | (1 << BlockWorld.SolidLayer), QueryTriggerInteraction.Ignore);
                sb.Append($"floor={fh.collider.name} key={key.Pos}/{key.YOff} has={world.Has(key)} blockers=[{string.Join(",", hits.Select(h => h.name + "@" + h.gameObject.layer))}] ; ");
            }
            return sb.ToString();
        }

        /// <summary>Everyone's share again (a team wipe; being fired, the chest went with the ship's blocks).</summary>
        public static void ServerStartOver(string why)
        {
            if (!BlockNet.IsServer) return;
            given.Clear();
            Plugin.Log.LogInfo("Starting supplies again: " + why);
        }

        public static void Save(string file)
        {
            if (!Enabled) return;
            var lines = new List<string>();
            if (chest.HasValue) lines.Add($"chest={chest.Value.YOff},{chest.Value.Pos.x},{chest.Value.Pos.y},{chest.Value.Pos.z}");
            lines.AddRange(given.Select(g => "given=" + g));
            ES3.Save(SaveKey, string.Join("\n", lines), file);
        }

        static void Load(string file)
        {
            loadedFor = file;
            given.Clear();
            chest = null;
            try
            {
                if (!ES3.KeyExists(SaveKey, file)) return;
                foreach (var line in ES3.Load<string>(SaveKey, file).Split('\n'))
                {
                    if (line.StartsWith("given=")) given.Add(line.Substring(6));
                    else if (line.StartsWith("chest="))
                    {
                        var v = line.Substring(6).Split(',').Select(int.Parse).ToArray();
                        chest = new BlockKey(1, (short)v[0], new Vector3Int(v[1], v[2], v[3]));
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Starting supplies: couldn't read the save: " + e.Message); }
        }

        public static void ClearSave(string file)
        {
            given.Clear();
            chest = null;
            try { if (ES3.KeyExists(SaveKey, file)) ES3.DeleteKey(SaveKey, file); } catch { }
        }
    }
}
