using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Being in our water (#19), for the local player: in it you move slowly (like the game's own water), sink slowly and
    /// swim up holding [Space]; landing in it takes no fall damage (a bucket clutch works). With your head under, the
    /// game's own underwater screen, oxygen and drowning take over: one trigger box follows the water cell your camera is
    /// in and stands in for the game's water volume (PlayerControllerB checks the camera is inside underwaterCollider).
    /// </summary>
    public static class WaterSwim
    {
        public static float Hinder = 1.6f, SwimUp = 4f, SinkCap = -2.5f;
        static BoxCollider volume;
        static bool bodyIn, hindered, wasIn;
        public static bool BodyIn => bodyIn;
        public static bool HeadIn;

        // the (frame, grid height) pairs that hold water, looked up once a second (water is on one grid or a few)
        static readonly System.Collections.Generic.List<(byte frame, short yoff)> grids = new System.Collections.Generic.List<(byte, short)>();
        static float nextGrids;

        static void RefreshGrids(BlockWorld w)
        {
            if (Time.time < nextGrids) return;
            nextGrids = Time.time + 1f;
            grids.Clear();
            foreach (var b in w.Blocks.Values)
                if (b.Data.Def == Blocks.Water && !grids.Contains((b.Key.Frame, b.Key.YOff))) grids.Add((b.Key.Frame, b.Key.YOff));
        }

        /// <summary>The water block whose cell a world point is in (surface or not), if any.</summary>
        public static bool WaterCellAt(Vector3 world, out BlockInstance block)
        {
            block = null;
            var w = BlockWorld.Instance;
            if (w == null) return false;
            RefreshGrids(w);
            foreach (var (frame, yoff) in grids)
            {
                var local = w.ToFrameLocal(frame, world) / BlockWorld.S;
                var cell = new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.y - yoff / 1000f), Mathf.FloorToInt(local.z));
                var b = w.Get(new BlockKey(frame, yoff, cell));
                if (b != null && b.Data.Def == Blocks.Water) { block = b; return true; }
            }
            return false;
        }

        /// <summary>Whether a world point is in our water (under its surface), and which cell.</summary>
        public static bool WaterAt(Vector3 world, out BlockKey key)
        {
            key = default;
            var w = BlockWorld.Instance;
            if (w == null) return false;
            RefreshGrids(w);
            foreach (var (frame, yoff) in grids)
            {
                var local = w.ToFrameLocal(frame, world) / BlockWorld.S;
                float gy = local.y - yoff / 1000f;
                var cell = new Vector3Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(gy), Mathf.FloorToInt(local.z));
                key = new BlockKey(frame, yoff, cell);
                var b = w.Get(key);
                if (b == null || b.Data.Def != Blocks.Water) continue;
                // (the surface: a source fills 14/16 of the cell, flowing water less; water with water above, all of it)
                float top = (b.Data.State & WaterFlow.Falling) != 0 || w.DefAt(key.Offset((int)Face.Up)) == Blocks.Water ? 1f
                          : (b.Data.State & 7) == 0 ? 14f / 16f : Mathf.Max(2f, 14f - (b.Data.State & 7) * 1.75f) / 16f;
                if (gy - cell.y <= top) return true;
            }
            return false;
        }

        /// <summary>Local player, every frame (from Builder).</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (p == null || p.isPlayerDead) { Leave(p); return; }
            var feet = p.transform.position + Vector3.up * 0.3f;
            bool body = WaterAt(feet, out _) || WaterAt(p.transform.position + Vector3.up * 1.2f, out _);
            bool head = WaterAt(p.gameplayCamera.transform.position, out var headKey);
            HeadIn = head;

            // slowed in it, like the game's own water
            if (body && !hindered) { p.isMovementHindered++; p.hinderedMultiplier *= Hinder; hindered = true; }
            else if (!body && hindered) Unhinder(p);
            if (body)
            {
                // landing in water: no fall damage
                if (!wasIn && p.fallValueUncapped < SinkCap) { p.fallValueUncapped = SinkCap; p.takingFallDamage = false; }
                // swimming ([Water] Swimming): sinking slowly, [Space] swims up. Off (the default), water is Lethal
                // Company's hazard: you go to the bottom and can't swim up
                if (Plugin.WaterSwimming.Value)
                {
                    if (p.fallValueUncapped < SinkCap) { p.fallValueUncapped = SinkCap; p.takingFallDamage = false; }
                    if (p.fallValue < SinkCap) p.fallValue = SinkCap;
                    bool jump = false;
                    try { jump = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump").IsPressed(); } catch { }
                    if (jump || DevSwimUp) p.externalForces += Vector3.up * SwimUp;
                }
                else p.takingFallDamage = false; // (down to the bottom of the pool: that's not a fall)
            }
            wasIn = body;
            bodyIn = body;

            // head under: the game's underwater view, oxygen and drowning, through a volume around the camera's cell
            if (head)
            {
                if (volume == null)
                {
                    var go = new GameObject("LMC_WaterVolume");
                    Object.DontDestroyOnLoad(go);
                    go.layer = 2; // (Ignore Raycast)
                    volume = go.AddComponent<BoxCollider>();
                    volume.isTrigger = true;
                }
                var w = BlockWorld.Instance;
                volume.transform.position = w.WorldCenter(headKey);
                volume.transform.rotation = w.FrameRotation(headKey.Frame);
                volume.size = Vector3.one * BlockWorld.S * 1.02f;
                volume.enabled = true;
                p.underwaterCollider = volume;
                p.isUnderwater = true;
            }
            else if (volume != null && p.underwaterCollider == volume)
            {
                p.isUnderwater = false;
                p.underwaterCollider = null;
                volume.enabled = false;
            }
        }

        public static bool DevSwimUp;

        static void Unhinder(PlayerControllerB p)
        {
            if (p != null)
            {
                p.isMovementHindered = Mathf.Max(0, p.isMovementHindered - 1);
                p.hinderedMultiplier = Mathf.Max(1f, p.hinderedMultiplier / Hinder);
            }
            hindered = false;
        }

        static void Leave(PlayerControllerB p)
        {
            if (hindered) Unhinder(p);
            if (p != null && volume != null && p.underwaterCollider == volume) { p.isUnderwater = false; p.underwaterCollider = null; }
            bodyIn = false; HeadIn = false;
        }
    }
}
