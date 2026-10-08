using System;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>#2: the game's pickup coroutine (PlayerControllerB.GrabObject) throwing resets the pickup state.</summary>
    [HarmonyPatch]
    public static class GrabSafety
    {
        static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PlayerControllerB), "GrabObject"));

        static Exception Finalizer(Exception __exception, object __instance)
        {
            if (__exception == null) return null;
            var p = Traverse.Create(__instance).Field("<>4__this").GetValue<PlayerControllerB>();
            if (p != null) Pickup.Reset(p, "the item was gone before the pickup finished: " + __exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Picking items up the Minecraft way, and keeping the game's own pickup from getting stuck:
    /// - [E] on a stack that matches one in your hotbar with room tops that stack up (even with a full hotbar) instead of
    ///   taking a new slot (#3, #8);
    /// - picking something up doesn't change what you're holding (#12);
    /// - an item leaving a slot you aren't holding takes its weight with it (the game only does that for the held slot) (#4);
    /// - if the item being picked up disappears before the server answers, the game's pickup coroutine throws and leaves
    ///   the player "mid-pickup" for good: no hotbar, no pickups, no ladders (#2). That state is reset.
    /// </summary>
    [HarmonyPatch]
    public static class Pickup
    {
        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ #2: a pickup that can't finish (see GrabSafety)
        /// <summary>Puts a player's pickup state back the way the game does when a pickup is refused.</summary>
        public static void Reset(PlayerControllerB p, string why)
        {
            var g = p.currentlyGrabbingObject;
            if ((object)g != null)
            {
                // (a destroyed item still has its fields; its weight was added when the pickup started)
                if (g.itemProperties != null) p.carryWeight = Mathf.Clamp(p.carryWeight - (g.itemProperties.weight - 1f), 1f, 10f);
                if (g != null && g.parentObject == p.localItemHolder) g.parentObject = null;
            }
            p.currentlyGrabbingObject = null;
            p.grabInvalidated = false;
            p.isGrabbingObjectAnimation = false;
            p.twoHanded = p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer.itemProperties.twoHanded;
            try
            {
                p.SetSpecialGrabAnimationBool(false);
                p.playerBodyAnimator.SetBool("GrabInvalidated", true);
            }
            catch { }
            stuckSince = -1f;
            Plugin.Log.LogWarning("Pickup reset (" + why + ")");
        }

        static float stuckSince = -1f;

        /// <summary>Every frame (local player): a pickup that has been "in progress" far too long is reset.</summary>
        public static void Tick()
        {
            var p = Local;
            if (p == null) return;
            if (!p.isGrabbingObjectAnimation) { stuckSince = -1f; }
            else if (stuckSince < 0f) stuckSince = Time.time;
            else if (Time.time - stuckSince > 5f) Reset(p, "stuck for 5 s");
            RestoreSlot(p);
        }

        // ------------------------------------------------------------------ #3, #8: top up a matching stack
        /// <summary>A hotbar stack of the same item with room for more, or -1.</summary>
        static int MatchingStack(PlayerControllerB p, StackItem ground)
        {
            string key = Crafting.KeyOf(ground);
            if (key == null) return -1;
            for (int i = 0; i < p.ItemSlots.Length; i++)
                if (p.ItemSlots[i] is StackItem st && st != ground && Crafting.KeyOf(st) == key && Inventory.CountIn(p, i) < st.MaxStack) return i;
            return -1;
        }

        static StackItem Aimed(PlayerControllerB p)
        {
            var ray = new Ray(p.gameplayCamera.transform.position, p.gameplayCamera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, p.grabDistance, p.interactableObjectsMask) || hit.collider.gameObject.layer == 8 || !hit.collider.CompareTag("PhysicsProp")) return null;
            var st = hit.collider.GetComponent<StackItem>();
            if (st == null || st.isHeld || st.isPocketed || st.Despawning || !st.IsSpawned || st.Count <= 0) return null;
            if (Physics.Linecast(p.gameplayCamera.transform.position, st.transform.position + p.transform.up * 0.16f, 1073741824, QueryTriggerInteraction.Ignore)) return null;
            return st;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "BeginGrabObject"), HarmonyPrefix]
        static bool TopUpInsteadOfGrab(PlayerControllerB __instance)
        {
            var p = __instance;
            if (!p.IsOwner || p.twoHanded || p.inSpecialInteractAnimation) return true;
            var ground = Aimed(p);
            if (ground != null)
            {
                int slot = MatchingStack(p, ground);
                if (slot >= 0)
                {
                    var target = (StackItem)p.ItemSlots[slot];
                    int add = Mathf.Min(ground.Count, target.MaxStack - Inventory.CountIn(p, slot));
                    Inventory.NotePendingAdd(target, add);
                    BlockNet.RequestMergeGround(ground, target);
                    Sounds.Play2D("pop", 0.35f, UnityEngine.Random.Range(1.4f, 2.0f));
                    return false;
                }
            }
            // a normal pickup: keep holding what's in hand (the game would switch to the slot the item goes into)
            NoteSlotToRestore(p);
            return true;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "SetHoverTipAndCurrentInteractTrigger"), HarmonyPostfix]
        static void TipWhenStackHasRoom(PlayerControllerB __instance)
        {
            var p = __instance;
            if (!p.IsOwner || p.cursorTip == null || p.cursorTip.text != "Inventory full!") return;
            var ground = Aimed(p);
            if (ground != null && MatchingStack(p, ground) >= 0) p.cursorTip.text = ground.customGrabTooltip;
        }

        /// <summary>Server: a player tops up their hotbar stack from a stack on the ground (what fits; the rest stays).</summary>
        public static void ServerMerge(ulong sender, ulong groundId, ulong targetId)
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(groundId, out var gn) || !nm.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var tn)) return;
            var ground = gn.GetComponent<StackItem>(); var target = tn.GetComponent<StackItem>();
            var p = ServerLogic.PlayerFor(sender);
            if (ground == null || target == null || p == null || ground.isHeld || ground.Despawning || target.playerHeldBy != p) return;
            if (Crafting.KeyOf(ground) != Crafting.KeyOf(target)) return;
            if (Vector3.Distance(p.transform.position, ground.transform.position) > p.grabDistance + 3f) return;
            int add = Mathf.Min(ground.Count, target.MaxStack - target.Count);
            if (add <= 0) { BlockNet.ServerStackCount(target); return; }
            target.ServerSetCount(target.Count + add);
            ground.ServerSetCount(ground.Count - add, despawnIfEmpty: true);
        }

        // ------------------------------------------------------------------ #12: picking up doesn't change your hand
        static int restoreSlot = -1;
        static float restoreUntil;
        static bool sawGrab;

        public static void NoteSlotToRestore(PlayerControllerB p)
        {
            // only when holding something: with an empty hand the item may as well land in it
            if (p.currentItemSlot == 50 || p.currentItemSlot >= p.ItemSlots.Length || p.ItemSlots[p.currentItemSlot] == null) { restoreSlot = -1; return; }
            restoreSlot = p.currentItemSlot;
            restoreUntil = Time.time + 4f;
            sawGrab = false;
        }

        /// <summary>Once the pickup is done, back to the slot that was in hand (once: scrolling afterwards is the player's business).</summary>
        static void RestoreSlot(PlayerControllerB p)
        {
            if (restoreSlot < 0) return;
            if (Time.time > restoreUntil || restoreSlot >= p.ItemSlots.Length || p.ItemSlots[restoreSlot] == null) { restoreSlot = -1; return; }
            if (p.isGrabbingObjectAnimation) { sawGrab = true; return; }
            if (!sawGrab) return;
            if (p.currentItemSlot == restoreSlot) { restoreSlot = -1; return; }
            HotbarInput.SelectSlot(p, restoreSlot); // (retried next frame if the game is still settling the switch)
        }

        // ------------------------------------------------------------------ #4: weight of items leaving other slots
        [HarmonyPatch(typeof(PlayerControllerB), nameof(PlayerControllerB.DestroyItemInSlot)), HarmonyPrefix]
        static void WeightOfOtherSlots(PlayerControllerB __instance, int itemSlot)
        {
            var p = __instance;
            if (!p.IsOwner || itemSlot == p.currentItemSlot) return; // (the game does the held slot itself)
            var g = itemSlot == 50 ? p.ItemOnlySlot : itemSlot >= 0 && itemSlot < p.ItemSlots.Length ? p.ItemSlots[itemSlot] : null;
            if (g != null && g.itemProperties != null) p.carryWeight = Mathf.Clamp(p.carryWeight - (g.itemProperties.weight - 1f), 1f, 10f);
        }
    }
}
