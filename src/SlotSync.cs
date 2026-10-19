using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// #209: every game keeps its own copy of each player's hotbar, and the game only ever tells the others "a slot to
    /// the left/right" and "an item went into the first empty slot", each working that slot out for itself. When a slot
    /// is freed while a pickup is on its way (a chest put, a craft, the last block of a stack placed, with a little lag),
    /// the player's own game and the host's choose different slots, and from then on disagree for good: the host checks
    /// the wrong item ("needs a pickaxe" with one in hand), throws or destroys the wrong one (items vanish, a ghost in the
    /// hand). The player's own hotbar is what they see, so it's the truth: once it has been still for a moment, the
    /// player's game sends it, and when the host's copy still differs on the next one, the host and every other player
    /// take the player's (and the host's log says what was wrong).
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class SlotSync
    {
        /// <summary>
        /// Another player picked something up, but our copy of their hotbar is full (it's out of step): the game would ask
        /// for slot -1 and throw halfway through the pickup (the host's log: IndexOutOfRangeException in SwitchToItemSlot),
        /// leaving the item half picked up here. Give it a slot; the next layout from that player puts it right.
        /// </summary>
        static bool inGrab;
        [HarmonyLib.HarmonyPatch(typeof(PlayerControllerB), "GrabObjectClientRpc"), HarmonyLib.HarmonyPrefix]
        static void GrabStarts() => inGrab = true;
        [HarmonyLib.HarmonyPatch(typeof(PlayerControllerB), "GrabObjectClientRpc"), HarmonyLib.HarmonyFinalizer]
        static void GrabEnds() => inGrab = false;

        [HarmonyLib.HarmonyPatch(typeof(PlayerControllerB), "FirstEmptyItemSlot"), HarmonyLib.HarmonyPostfix]
        static void NeverFullForOthers(PlayerControllerB __instance, ref int __result)
        {
            if (__result != -1 || !inGrab || __instance.IsOwner || !Enabled) return;
            __result = __instance.currentItemSlot >= 0 && __instance.currentItemSlot < __instance.ItemSlots.Length ? __instance.currentItemSlot : 0;
            Plugin.Log.LogWarning($"[slotsync] {__instance.playerUsername}'s hotbar is full here but they picked something up: slot {__result} until their layout comes");
        }

        public static bool Enabled = true; // (dev: slotsync 0 brings the old behaviour back)
        const float Every = 0.5f;

        // ------------------------------------------------------------------ the player's own game
        static float next;
        static string lastSig; static int stable; static float lastSent; static string lastSentSig;
        static float heldWrongSince = -1f;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        public static string Signature(PlayerControllerB p) =>
            p.currentItemSlot + ":" + string.Join(",", p.ItemSlots.Select(Id)) + "|" + Id(p.ItemOnlySlot);
        static ulong Id(GrabbableObject g) => g != null && g.NetworkObject != null && g.NetworkObject.IsSpawned ? g.NetworkObjectId : 0UL;

        static bool Busy(PlayerControllerB p) =>
            p.isGrabbingObjectAnimation || p.throwingObject || p.activatingItem || p.inSpecialInteractAnimation || p.isTypingChat
            || Inventory.Pending; // (not currentlyGrabbingObject: the game leaves it set after a pickup)

        /// <summary>Every frame (Inventory.Update).</summary>
        public static void Tick()
        {
            if (!Enabled || Time.time < next) return;
            next = Time.time + Every;
            var p = Local;
            if (p == null || !p.isPlayerControlled || p.isPlayerDead || !BlockNet.IsConnected) { lastSig = null; return; }
            if (Busy(p)) { stable = 0; lastSig = null; heldWrongSince = -1f; return; }
            FixOwnHand(p);
            string sig = Signature(p);
            if (sig != lastSig) { lastSig = sig; stable = 0; return; }
            stable++;
            // still for a moment: send it (when it changed, and every 2 s anyway so a wrong copy is found)
            if (sig != lastSentSig || Time.time - lastSent > 2f)
            {
                lastSentSig = sig; lastSent = Time.time;
                if (!BlockNet.IsServer) BlockNet.RequestSlotLayout(p.currentItemSlot, p.ItemSlots.Select(Id).ToArray(), Id(p.ItemOnlySlot));
                else BlockNet.ServerSlotLayout(p, p.currentItemSlot, p.ItemSlots.Select(Id).ToArray(), Id(p.ItemOnlySlot), force: false); // (the host's own, to the others)
            }
        }

        /// <summary>
        /// The hand and the hotbar disagree in our own game (Lum: something in her hand, nothing in the slot): put what's in
        /// hand back in a slot if it isn't in one, then hold what the current slot holds, the game's own way.
        /// </summary>
        static void FixOwnHand(PlayerControllerB p)
        {
            var should = p.currentItemSlot == 50 ? p.ItemOnlySlot : p.currentItemSlot >= 0 && p.currentItemSlot < p.ItemSlots.Length ? p.ItemSlots[p.currentItemSlot] : null;
            var held = p.currentlyHeldObjectServer;
            bool wrong = held != should || (should != null && !p.isHoldingObject);
            if (!wrong) { heldWrongSince = -1f; return; }
            if (heldWrongSince < 0f) { heldWrongSince = Time.time; return; }
            if (Time.time - heldWrongSince < 1f) return;
            heldWrongSince = -1f;
            string what = $"hand={(held != null ? held.itemProperties.itemName : "-")} slot {p.currentItemSlot}={(should != null ? should.itemProperties.itemName : "-")} holding={p.isHoldingObject}";
            if (held != null && held.NetworkObject != null && held.NetworkObject.IsSpawned && held.playerHeldBy == p && !p.ItemSlots.Contains(held) && p.ItemOnlySlot != held)
            {
                // an item in hand that's in no slot: back into the inventory (the slot it was in, if that's empty, else the first)
                int k = should == null && p.currentItemSlot != 50 ? p.currentItemSlot : System.Array.IndexOf(p.ItemSlots, null);
                if (k >= 0)
                {
                    p.ItemSlots[k] = held;
                    var icon = HUDManager.Instance.itemSlotIcons[k];
                    icon.sprite = held.itemProperties.itemIcon; icon.enabled = true;
                    if (k == p.currentItemSlot) { p.isHoldingObject = true; Plugin.Log.LogWarning("[slotsync] fixed my hand: " + what + " -> back in slot " + k); return; }
                }
            }
            // hold what the slot holds (the game's switch: the other one is pocketed, this one equipped)
            p.currentlyHeldObjectServer = held != null && (p.ItemSlots.Contains(held) || p.ItemOnlySlot == held) ? held : null;
            p.SwitchToItemSlot(p.currentItemSlot);
            p.isHoldingObject = p.currentlyHeldObjectServer != null;
            p.twoHanded = p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer.itemProperties.twoHanded;
            Plugin.Log.LogWarning("[slotsync] fixed my hand: " + what);
        }

        public static string DevState()
        {
            var p = Local;
            if (p == null) return "no player";
            return $"busy={Busy(p)} (grab={p.isGrabbingObjectAnimation} throw={p.throwingObject} act={p.activatingItem} special={p.inSpecialInteractAnimation} typing={p.isTypingChat} grabbing={(p.currentlyGrabbingObject != null)} pending={Inventory.Pending}) stable={stable} sent={Time.time - lastSent:F1}s ago sig={lastSig}";
        }

        // ------------------------------------------------------------------ the host
        static readonly Dictionary<ulong, string> mismatch = new Dictionary<ulong, string>();
        public static int Corrections;

        /// <summary>Host: a player's own hotbar. Taken when the host's copy differs twice in a row with the same layout.</summary>
        public static void ServerReceive(ulong sender, int cur, ulong[] ids, ulong belt)
        {
            if (!Enabled) return;
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || p.isPlayerDead || ids.Length != p.ItemSlots.Length) return;
            if (p.isGrabbingObjectAnimation || p.throwingObject) { mismatch.Remove(sender); return; }
            string theirs = cur + ":" + string.Join(",", ids) + "|" + belt;
            string mine = Signature(p);
            if (theirs == mine) { mismatch.Remove(sender); return; }
            if (!mismatch.TryGetValue(sender, out var prev) || prev != theirs) { mismatch[sender] = theirs; return; } // (once could be in flight)
            mismatch.Remove(sender);
            Corrections++;
            Plugin.Log.LogWarning($"[slotsync] {p.playerUsername}'s hotbar was out of step on the host: host {mine} / theirs {theirs}: taking theirs");
            Apply(p, cur, ids, belt);
            BlockNet.ServerSlotLayout(p, cur, ids, belt, force: true);
        }

        static readonly Dictionary<int, string> clientMismatch = new Dictionary<int, string>();

        /// <summary>The other players: a player's hotbar as they see it (force: the host already found it out of step).</summary>
        public static void ClientReceive(PlayerControllerB p, int cur, ulong[] ids, ulong belt, bool force)
        {
            if (!Enabled || p == null || p.IsOwner || ids.Length != p.ItemSlots.Length) return;
            int pid = (int)p.playerClientId;
            string theirs = cur + ":" + string.Join(",", ids) + "|" + belt;
            if (!force)
            {
                if (theirs == Signature(p) || p.isGrabbingObjectAnimation) { clientMismatch.Remove(pid); return; }
                if (!clientMismatch.TryGetValue(pid, out var prev) || prev != theirs) { clientMismatch[pid] = theirs; return; }
            }
            clientMismatch.Remove(pid);
            if (theirs == Signature(p)) return;
            Plugin.Log.LogWarning($"[slotsync] {p.playerUsername}'s hotbar was out of step here: {Signature(p)} / theirs {theirs}");
            Apply(p, cur, ids, belt);
        }

        /// <summary>Host and the other players: a player's hotbar, as they see it.</summary>
        public static void Apply(PlayerControllerB p, int cur, ulong[] ids, ulong belt)
        {
            if (p == null || p.IsOwner || ids.Length != p.ItemSlots.Length) return;
            var objs = Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            GrabbableObject Get(ulong id) => id != 0 && objs.TryGetValue(id, out var no) ? no.GetComponent<GrabbableObject>() : null;
            var before = p.ItemSlots.ToList(); before.Add(p.ItemOnlySlot);
            for (int i = 0; i < ids.Length; i++) p.ItemSlots[i] = Get(ids[i]);
            p.ItemOnlySlot = Get(belt);
            foreach (var g in p.ItemSlots.Concat(new[] { p.ItemOnlySlot }))
            {
                if (g == null) continue;
                g.playerHeldBy = p; g.isHeld = true; g.parentObject = p.serverItemHolder;
                g.EnablePhysics(false);
            }
            // anything the host had in this player's slots that isn't in theirs: their game has no such item in hand
            foreach (var g in before)
                if (g != null && !p.ItemSlots.Contains(g) && p.ItemOnlySlot != g) Plugin.Log.LogWarning($"[slotsync] {p.playerUsername}: {g.itemProperties.itemName} ({g.NetworkObjectId}) isn't in their hotbar");
            // the game's own switch: pockets what was in hand, equips what the slot holds
            var held = cur == 50 ? p.ItemOnlySlot : cur >= 0 && cur < p.ItemSlots.Length ? p.ItemSlots[cur] : null;
            if (p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer != held && !p.ItemSlots.Contains(p.currentlyHeldObjectServer) && p.ItemOnlySlot != p.currentlyHeldObjectServer)
                p.currentlyHeldObjectServer = null;
            p.SwitchToItemSlot(cur);
            p.isHoldingObject = p.currentlyHeldObjectServer != null;
            p.twoHanded = p.currentlyHeldObjectServer != null && p.currentlyHeldObjectServer.itemProperties.twoHanded;
            foreach (var g in p.ItemSlots.Concat(new[] { p.ItemOnlySlot }))
                if (g != null) { g.isPocketed = g != p.currentlyHeldObjectServer; g.EnableItemMeshes(g == p.currentlyHeldObjectServer); }
        }
    }
}
