using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Ender pearls: [Right-click] throws one (Minecraft speed and gravity, scaled to the block size). Every client sees
    /// the pearl fly; the thrower's own client teleports to where it lands and takes Minecraft's 5 (of 20) damage.
    /// </summary>
    public class EnderPearls : MonoBehaviour
    {
        public static EnderPearls Instance;
        void Awake() => Instance = this;

        // Minecraft: 1.5 blocks/tick launch, 0.03 blocks/tick^2 gravity, 0.99 drag per tick
        static float Speed => 1.5f * 20f * Plugin.S;
        static float Gravity => 0.03f * 400f * Plugin.S;
        const float Drag = 0.99f;
        const int LandDamage = 25; // 5 of Minecraft's 20 health = 25 of Lethal Company's 100

        class Flight
        {
            public ulong Owner;
            public Vector3 Pos, Vel;
            public float Age;
            public GameObject Visual;
        }

        readonly List<Flight> flights = new List<Flight>();

        static int Mask => (1 << 0) | (1 << 8) | (1 << 11) | (1 << 25) | (1 << 26) | (1 << 28) | (1 << 19);

        /// <summary>Owner client: throw the held pearl.</summary>
        public static void Throw(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            if (Inventory.Take(p, p.currentItemSlot, 1) == null)
            {
                if (Plugin.DevMode.Value) Plugin.Log.LogWarning($"[dev] pearl throw: nothing taken from slot {p.currentItemSlot} (count {Inventory.CountIn(p, p.currentItemSlot)})");
                return;
            }
            var start = cam.position + cam.forward * 0.5f;
            BlockNet.RequestPearlThrow(start, cam.forward * Speed + Vector3.up * 0.1f * Plugin.S);
            Sounds.Play("pearl.throw", start, 0.8f, Random.Range(0.4f, 0.55f));
        }

        /// <summary>Every client: a pearl was thrown by a player.</summary>
        public static void StartFlight(ulong owner, Vector3 start, Vector3 vel)
        {
            if (Instance == null) return;
            var vis = new GameObject("LMC_EnderPearl");
            var model = new GameObject("model");
            model.transform.SetParent(vis.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = MeshBuilder.ExtrudedSprite(Atlas.Tiles.ContainsKey("item_ender_pearl") ? "item_ender_pearl" : "item_coal");
            model.AddComponent<MeshRenderer>().sharedMaterial = Atlas.Cutout;
            model.transform.localScale = Vector3.one * 0.3f;
            vis.transform.position = start;
            Instance.flights.Add(new Flight { Owner = owner, Pos = start, Vel = vel, Visual = vis });
        }

        void Update()
        {
            if (flights.Count == 0) return;
            float dt = Time.deltaTime;
            var cam = StartOfRound.Instance != null ? StartOfRound.Instance.activeCamera : null;
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                f.Age += dt;
                var next = f.Pos + f.Vel * dt;
                f.Vel.y -= Gravity * dt;
                f.Vel *= Mathf.Pow(Drag, dt * 20f);
                var owner = OwnerPlayer(f.Owner);
                if (Hit(f.Pos, next, owner, out var hit) || f.Age > 12f || next.y < -600f)
                {
                    if (f.Age <= 12f && next.y >= -600f) Land(f, hit, owner);
                    if (f.Visual != null) Destroy(f.Visual);
                    flights.RemoveAt(i);
                    continue;
                }
                f.Pos = next;
                if (f.Visual != null)
                {
                    f.Visual.transform.position = f.Pos;
                    if (cam != null) f.Visual.transform.rotation = Quaternion.LookRotation(f.Visual.transform.position - cam.transform.position);
                }
            }
        }

        static PlayerControllerB OwnerPlayer(ulong clientId)
        {
            var sor = StartOfRound.Instance;
            if (sor == null) return null;
            foreach (var pl in sor.allPlayerScripts) if (pl != null && pl.actualClientId == clientId && pl.isPlayerControlled) return pl;
            return null;
        }

        static bool Hit(Vector3 a, Vector3 b, PlayerControllerB owner, out RaycastHit best)
        {
            best = default;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return false;
            float bestDist = float.MaxValue;
            bool found = false;
            foreach (var h in Physics.RaycastAll(a, d / len, len, Mask, QueryTriggerInteraction.Ignore))
            {
                if (owner != null && h.collider.transform.IsChildOf(owner.transform)) continue;
                if (h.collider.GetComponentInParent<GrabbableObject>() != null) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        static void Land(Flight f, RaycastHit hit, PlayerControllerB owner)
        {
            Sounds.Play("pearl.land", hit.point, 1f, Random.Range(0.9f, 1.1f));
            if (BlockWorld.Instance != null) BlockWorld.Instance.SpawnParticles(Blocks.Obsidian, hit.point, 10);
            var local = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            if (owner == null || owner != local || owner.isPlayerDead) return;
            // stand on what was hit: back off walls/ceilings a little, feet on floors
            var n = hit.normal;
            var dest = hit.point + n * 0.4f;
            if (n.y < 0.5f)
            {
                // hit a wall or ceiling: drop to the floor under that point if there is one close by
                if (Physics.Raycast(dest + Vector3.up * 0.1f, Vector3.down, out var floor, 3f, Mask, QueryTriggerInteraction.Ignore)) dest = floor.point + Vector3.up * 0.05f;
                else dest += Vector3.down * 1.0f;
            }
            else dest = hit.point + Vector3.up * 0.05f;
            owner.TeleportPlayer(dest);
            owner.fallValue = 0f; owner.fallValueUncapped = 0f;
            owner.DamagePlayer(LandDamage, hasDamageSFX: true, callRPC: true, CauseOfDeath.Unknown);
        }
    }
}
