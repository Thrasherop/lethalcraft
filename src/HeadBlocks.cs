using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244, owner) Mob heads put down as blocks, Minecraft's SkullBlock and WallSkullBlock: on the ground, turned toward
    /// you in 16ths of a turn (the state); on a wall, flat against it. Right-click a block with a head in hand puts it
    /// there (right-click at nothing still puts it on your head). Broken, it gives the head back. On top of a note block, a
    /// head plays its mob's sound when the note block is played. The skeleton, wither skeleton, zombie, creeper and piglin
    /// heads; the wither skeleton skull stays the trophy it is (the head that comes back sells as before).
    /// </summary>
    public static class HeadBlocks
    {
        /// <summary>Each head's item key, its block, its texture, its width in pixels and its sound on a note block.</summary>
        public class Head { public string Item, Texture, Sound; public int W; public BlockDef Block; }
        public static readonly Dictionary<string, Head> ByItem = new Dictionary<string, Head>();
        static readonly Dictionary<BlockDef, Head> byBlock = new Dictionary<BlockDef, Head>();
        public static int Placed; // (dev/tests)

        public static Head OfBlock(BlockDef d) => d != null && byBlock.TryGetValue(d, out var h) ? h : null;
        public static BlockDef BlockFor(string itemKey) { var t = ItemData.TypeOf(itemKey); return t != null && ByItem.TryGetValue(t, out var h) ? h.Block : null; }

        /// <summary>Its blocks, ids from 1300 (none of them an item of its own: the head items are).</summary>
        public static void Register()
        {
            ushort id = 1300;
            foreach (var (item, name, tex, w, sound) in new[]
            {
                ("skeleton_skull", "Skeleton Skull", "entity/skeleton/skeleton", 8, "skeleton.idle"),
                (WitherSkull.Key, "Wither Skeleton Skull", "entity/skeleton/wither_skeleton", 8, "wskel.idle"),
                ("zombie_head", "Zombie Head", "entity/zombie/zombie", 8, "zombie.idle"),
                ("creeper_head", "Creeper Head", "entity/creeper/creeper", 8, "fuse"),
                ("piglin_head", "Piglin Head", "entity/piglin/piglin", 10, "piglin.idle"),
            })
            {
                var def = Blocks.AddHead(id++, "placed_" + item, name, d =>
                {
                    d.Shape = BlockShape.Head; d.Directional = true; d.FacingIncludesVertical = true;
                    d.Sound = "stone"; d.Hardness = 1f; d.HandTime = 1.5f; d.PickTime = 1.5f; d.Pushable = false; d.NoItem = true; d.DropKey = item;
                    d.Description = name + ", put down.";
                });
                var h = new Head { Item = item, Texture = tex, W = w, Sound = sound, Block = def };
                ByItem[item] = h; byBlock[def] = h;
            }
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>Its model under the block (BlockWorld turns the block: its 16ths on the ground, its wall's way on one):
        /// Minecraft's head, 8 px a side (the piglin's 10 wide, with its snout, tusks and ears), on the floor or flat
        /// against the wall behind.</summary>
        public static void Attach(BlockInstance bi)
        {
            var block = bi?.Go;
            var h = OfBlock(bi?.Data.Def);
            if (block == null || h == null) return;
            bool floor = bi.Data.Facing == (byte)Face.Up;
            var old = block.transform.Find("LMC_HeadAt");
            if (old != null) { if (old.name == "LMC_HeadAt" && (old.localPosition.y < -0.1f) == floor) return; Object.Destroy(old.gameObject); }
            var at = new GameObject("LMC_HeadAt").transform;
            at.SetParent(block.transform, false);
            at.localPosition = floor ? new Vector3(0f, -0.25f, 0f) : new Vector3(0f, 0f, -0.25f);
            if (MobHeads.All.ContainsKey(h.Item)) MobHeads.Build(h.Item, at, Vector3.zero, 0.5f);
            else
            {
                if (!mats.TryGetValue(h.Item, out var m)) mats[h.Item] = m = EntityModels.Material("LMC_Head_" + h.Item, h.Texture, Color.gray);
                var root = new GameObject("LMC_MobHead"); root.transform.SetParent(at, false);
                EntityModels.Part(root.transform, "Head", Vector3.zero, new Vector3(h.W, 8, 8), 0, 0, 0.5f / 8f, m);
            }
            foreach (var r in at.GetComponentsInChildren<MeshRenderer>(true)) Atlas.NoDecals(r);
        }

        /// <summary>Host: a player's head in hand put down at k (the facing: a wall's, or 16 + its turn on the ground).</summary>
        public static void ServerPlace(ulong sender, ulong itemId, BlockKey k, byte facing)
        {
            var w = BlockWorld.Instance;
            var p = ServerLogic.PlayerFor(sender);
            if (w == null || p == null || Unity.Netcode.NetworkManager.Singleton == null) return;
            if (!Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(itemId, out var no)) return;
            var g = no.GetComponent<GrabbableObject>();
            if (g == null || g.playerHeldBy != p) return;
            var def = BlockFor(Crafting.KeyOf(g));
            if (def == null) return;
            if (k.Frame == 0 && !w.WorldFrameAvailable) { BlockNet.ServerToast(sender, "You can't build out here."); return; }
            if (w.Has(k) || ServerLogic.Obstructed(k, 0.6f)) { if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] head at {k.Pos} f{facing}: has={w.Has(k)} obstructed={ServerLogic.Obstructed(k, 0.6f)}"); return; }
            var center = w.WorldCenter(k);
            if (Vector3.Distance(p.gameplayCamera.transform.position, center) > 9f * BlockWorld.S + 3f) return;
            byte state = 0;
            if (facing >= 16) { state = (byte)((facing - 16) & 15); facing = (byte)Face.Up; }
            else if (facing < 2 || facing > 5) return;
            BlockNet.ServerBroadcastOp(Op.Set(k, new BlockData(def.Id, facing, state)));
            if (!GameModes.IsCreative(sender)) BlockNet.ServerTakeHeld(g);
            BlockNet.ServerSound(center, "dig.stone", 0.8f, 1f);
            Placed++;
        }

        /// <summary>Client: right-click with a head in hand at a block: put it there (true), else it goes on your head.</summary>
        public static bool TryPlace(PlayerControllerB p, GrabbableObject held, Builder b, bool surface)
        {
            var def = BlockFor(Crafting.KeyOf(held));
            if (def == null || !surface || !b.ComputePlacement(p, def, out var k, out byte facing)) return false;
            if (Plugin.DevMode.Value) Plugin.Log.LogInfo($"[dev] head place {k.Frame}/{k.YOff}/{k.Pos} f{facing}");
            BlockNet.RequestHeadPlace(held.NetworkObjectId, k, facing);
            return true;
        }

        /// <summary>Host: the note block at k was played: a head on top plays its mob's sound (true).</summary>
        public static bool ServerNote(BlockKey k)
        {
            var w = BlockWorld.Instance;
            var up = w?.Get(k.Offset((int)Face.Up));
            var h = up != null && up.Data.Facing == (byte)Face.Up ? OfBlock(up.Data.Def) : null;
            if (h == null) return false;
            BlockNet.ServerSound(w.WorldCenter(k), h.Sound, 1f, 1f);
            return true;
        }
    }

    public static partial class Blocks
    {
        public static BlockDef AddHead(ushort id, string key, string name, System.Action<BlockDef> cfg) => Add(id, key, name, "stone", cfg);
    }
}
