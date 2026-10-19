using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Honeycomb (Minecraft's: shears on a bee nest): shears used on the Circuit Bees' hive, lying about or carried home,
    /// give three honeycomb, once a hive. Honeycomb waxes copper (Copper) and makes the honeycomb block.
    /// </summary>
    public static class Honeycomb
    {
        public const string HiveName = "Hive";
        static readonly HashSet<ulong> taken = new HashSet<ulong>(); // (hives already sheared: host)
        public static int Sheared, Bottled; // (dev/tests)

        /// <summary>The hive the crosshair is on, within reach (a held one isn't).</summary>
        public static GrabbableObject AimedHive(PlayerControllerB p)
        {
            var cam = p.gameplayCamera.transform;
            foreach (var hit in Physics.RaycastAll(cam.position, cam.forward, 4f, 1 << 6, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                var g = hit.collider.GetComponentInParent<GrabbableObject>();
                if (g != null && !g.isHeld && g.itemProperties != null && g.itemProperties.itemName == HiveName) return g;
            }
            return null;
        }

        /// <summary>Host: a player's shears on a hive: three honeycomb beside it, once; (#244) or a glass bottle on it: a honey
        /// bottle (Minecraft's full nest: one or the other, then it's empty).</summary>
        public static void ServerShear(ulong sender, ulong hiveId, bool bottle = false)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || !Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(hiveId, out var no)) return;
            var hive = no.GetComponent<GrabbableObject>();
            if (hive == null || hive.itemProperties == null || hive.itemProperties.itemName != HiveName || hive.isHeld) return;
            if (bottle)
            {
                if (!(p.currentlyHeldObjectServer is StackItem b) || b.ItemKey != "glass_bottle" || b.Count <= 0) return;
                if (Vector3.Distance(p.transform.position, hive.transform.position) > 5f) return;
                if (!taken.Add(hiveId)) { BlockNet.ServerToast(sender, "It's empty: its honey's been taken."); return; }
                if (!GameModes.IsCreative(p.actualClientId)) b.ServerUseUp(p);
                Inventory.ServerSpawnFor(sender, "honey_bottle", 1);
                BlockNet.ServerSound(hive.transform.position, "bottle.fill", 0.8f, 1f);
                Bottled++;
                return;
            }
            if (!(p.currentlyHeldObjectServer is ToolItem t) || t.Kind != ToolKind.Shears) return;
            if (Vector3.Distance(p.transform.position, hive.transform.position) > 5f || !taken.Add(hiveId)) return;
            if (ModItems.ByKey.TryGetValue("honeycomb", out var comb)) ModItems.ServerSpawnStack(comb, 3, hive.transform.position + Vector3.up * 0.4f);
            BlockNet.ServerSound(hive.transform.position, "sheep.shear", 0.8f, 1.2f);
            if (!GameModes.IsCreative(p.actualClientId)) t.ServerUse(1);
            Sheared++;
        }

        public static void Reset() => taken.Clear();
    }
}
