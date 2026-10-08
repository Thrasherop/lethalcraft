using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Moving Minecraft items between the hotbar and screens (crafting grid): taking items out of hotbar slots, and
    /// putting items back like Minecraft does: first topping up stacks of the same item, then into free slots, and
    /// whatever doesn't fit drops at your feet. Lethal Company items are networked objects, so "putting into a slot"
    /// means the server spawns the item next to you and your client picks it up straight away.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        public static Inventory Instance;
        // items the server spawned for us to pick up, with when they were queued
        static readonly List<(ulong id, float t)> toGrab = new List<(ulong, float)>();
        static readonly Dictionary<ulong, (int n, float t)> pendingTake = new Dictionary<ulong, (int, float)>();
        static readonly Dictionary<ulong, (int n, float t)> pendingAdd = new Dictionary<ulong, (int, float)>();
        float grabWait;

        void Awake() => Instance = this;

        static PlayerControllerB Local => GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;

        // ------------------------------------------------------------------ counts (including requests still in flight)
        /// <summary>How many items a slot holds right now, counting removals/additions the server hasn't confirmed yet.</summary>
        public static int CountIn(PlayerControllerB p, int slot)
        {
            var g = p.ItemSlots[slot];
            if (g == null) return 0;
            int c = Crafting.CountOf(g);
            if (g is StackItem st)
            {
                if (pendingTake.TryGetValue(st.NetworkObjectId, out var t) && Time.time - t.t < 3f) c -= t.n;
                if (pendingAdd.TryGetValue(st.NetworkObjectId, out var a) && Time.time - a.t < 3f) c += a.n;
            }
            return Mathf.Max(0, c);
        }

        /// <summary>Owner client: n more items are on their way into this stack (the server confirms the count).</summary>
        public static void NotePendingAdd(StackItem st, int n)
        {
            pendingAdd.TryGetValue(st.NetworkObjectId, out var a);
            pendingAdd[st.NetworkObjectId] = (a.n + n, Time.time);
        }

        /// <summary>The server confirmed a stack's new count: drop our local guesses for it.</summary>
        public static void OnCountConfirmed(ulong id) { pendingTake.Remove(id); pendingAdd.Remove(id); }

        // ------------------------------------------------------------------ taking out
        /// <summary>Owner client: removes n items from a hotbar slot. Returns the item key taken (null if nothing).</summary>
        public static string Take(PlayerControllerB p, int slot, int n)
        {
            var g = p.ItemSlots[slot];
            string key = Crafting.KeyOf(g);
            int have = CountIn(p, slot);
            if (key == null || n <= 0 || have <= 0) return null;
            n = Mathf.Min(n, have);
            if (n >= have)
            {
                p.DestroyItemInSlotAndSync(slot);
                if (g is StackItem s0) { pendingTake.Remove(s0.NetworkObjectId); pendingAdd.Remove(s0.NetworkObjectId); }
            }
            else
            {
                var st = (StackItem)g;
                // note it before sending: on the host the server's confirmation arrives within this call
                pendingTake.TryGetValue(st.NetworkObjectId, out var t);
                pendingTake[st.NetworkObjectId] = (t.n + n, Time.time);
                BlockNet.RequestConsume(st, n);
            }
            return key;
        }

        // ------------------------------------------------------------------ putting back
        /// <summary>
        /// Owner client: gives n items of a key to the local player: tops up stacks of the same item, the rest is spawned
        /// next to the player and picked up into free slots (and stays on the ground if there's no room).
        /// </summary>
        public static void Give(string key, int n)
        {
            var p = Local;
            if (p == null || key == null || n <= 0) return;
            bool stackable = ModItems.ByKey.TryGetValue(key, out var item) && item.spawnPrefab.GetComponent<StackItem>() != null;
            if (stackable)
            {
                for (int i = 0; i < p.ItemSlots.Length && n > 0; i++)
                {
                    if (!(p.ItemSlots[i] is StackItem st) || Crafting.KeyOf(st) != key) continue;
                    int room = st.MaxStack - CountIn(p, i);
                    if (room <= 0) continue;
                    int add = Mathf.Min(room, n);
                    pendingAdd.TryGetValue(st.NetworkObjectId, out var a);
                    pendingAdd[st.NetworkObjectId] = (a.n + add, Time.time);
                    BlockNet.RequestAddToStack(st, add);
                    n -= add;
                }
            }
            if (n > 0) BlockNet.RequestSpawnForMe(key, n);
        }

        /// <summary>How many of an item fit in one slot: 64 for blocks and materials (16 ender pearls), 1 for tools.</summary>
        public static int MaxStackOf(string key)
        {
            if (key == null || !ModItems.ByKey.TryGetValue(key, out var item)) return 64;
            var st = item.spawnPrefab != null ? item.spawnPrefab.GetComponent<StackItem>() : null;
            if (st == null) return 1;
            return key == "ender_pearl" ? 16 : 64;
        }

        /// <summary>Owner client: put n items into one specific slot holding a stack of the same item. Returns how many fit.</summary>
        public static int GiveToSlot(PlayerControllerB p, int slot, string key, int n)
        {
            if (!(p.ItemSlots[slot] is StackItem st) || Crafting.KeyOf(st) != key || n <= 0) return 0;
            int add = Mathf.Min(n, st.MaxStack - CountIn(p, slot));
            if (add <= 0) return 0;
            pendingAdd.TryGetValue(st.NetworkObjectId, out var a);
            pendingAdd[st.NetworkObjectId] = (a.n + add, Time.time);
            BlockNet.RequestAddToStack(st, add);
            return add;
        }

        /// <summary>Owner client: throw items on the ground in front of the player.</summary>
        public static void Drop(string key, int n)
        {
            if (key != null && n > 0) BlockNet.RequestSpawnForMe(key, n, pickUp: false);
        }

        /// <summary>The server spawned something for us: pick it up as soon as we can (or leave it if the hotbar is full).</summary>
        public static void QueueGrab(ulong id) => toGrab.Add((id, Time.time));

        void Update()
        {
            Pickup.Tick();
            GameModeCommand.Tick();
            if (toGrab.Count == 0) return;
            var p = Local;
            if (p == null || p.isPlayerDead) { toGrab.Clear(); return; }
            grabWait -= Time.deltaTime;
            if (grabWait > 0f || p.isGrabbingObjectAnimation || p.inSpecialInteractAnimation) return;
            // the first one that has reached us; one that hasn't (or never will: picked up or merged meanwhile) doesn't hold up
            // the rest, and is dropped after a few seconds (it stays on the ground)
            toGrab.RemoveAll(e => Time.time - e.t > 4f);
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return;
            for (int i = 0; i < toGrab.Count; i++)
            {
                if (!nm.SpawnManager.SpawnedObjects.TryGetValue(toGrab[i].id, out var no)) continue;
                toGrab.RemoveAt(i);
                var g = no.GetComponent<GrabbableObject>();
                if (g == null || g.isHeld || g.isPocketed || p.FirstEmptyItemSlot() == -1) return; // no room: it stays at our feet
                GrabDirect(p, g);
                grabWait = 0.15f;
                return;
            }
        }


        /// <summary>(dev) the auto-pickup queue: ids, and whether each still exists.</summary>
        public static string DevQueue()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return $"{toGrab.Count} queued: " + string.Join(",", toGrab.Select(e => e.id + (nm != null && nm.SpawnManager.SpawnedObjects.ContainsKey(e.id) ? "" : "(gone)")));
        }

        /// <summary>The game's own pick-up (PlayerControllerB.BeginGrabObject) without the aiming raycast.</summary>
        internal static void GrabDirect(PlayerControllerB p, GrabbableObject g)
        {
            Pickup.NoteSlotToRestore(p); // (the game switches to the slot the item goes into: keep holding what you held)
            p.currentlyGrabbingObject = g;
            p.grabInvalidated = false;
            var no = g.NetworkObject;
            if (no == null || !no.IsSpawned) return;
            g.InteractItem();
            if (!g.grabbable || p.FirstEmptyItemSlot() == -1) return;
            p.playerBodyAnimator.SetBool("GrabInvalidated", false);
            p.playerBodyAnimator.SetBool("GrabValidated", false);
            p.playerBodyAnimator.SetBool("cancelHolding", false);
            p.playerBodyAnimator.ResetTrigger("Throw");
            p.SetSpecialGrabAnimationBool(true);
            p.isGrabbingObjectAnimation = true;
            p.cursorIcon.enabled = false;
            p.cursorTip.text = "";
            p.twoHanded = g.itemProperties.twoHanded;
            p.carryWeight = Mathf.Clamp(p.carryWeight + (g.itemProperties.weight - 1f), 1f, 10f);
            StartOfRound.Instance.SendChangedWeightEvent();
            p.grabObjectAnimationTime = g.itemProperties.grabAnimationTime > 0f ? g.itemProperties.grabAnimationTime : 0.4f;
            p.GrabObjectServerRpc(no);
            if (p.grabObjectCoroutine != null) p.StopCoroutine(p.grabObjectCoroutine);
            p.grabObjectCoroutine = p.StartCoroutine(p.GrabObject());
        }

        // ------------------------------------------------------------------ server side
        /// <summary>Server: one of the game's own items (creative menu), at a player's feet and into their hotbar (or the
        /// utility belt, where the game puts one-handed tools when it's empty).</summary>
        public static void ServerSpawnVanillaFor(ulong client, string itemName)
        {
            var p = ServerLogic.PlayerFor(client);
            var item = CreativeUI.VanillaItem(itemName);
            if (p == null || item == null) return;
            var g = ModItems.ServerSpawnPlain(item, p.transform.position + Vector3.up * 0.3f);
            if (g != null) BlockNet.ServerAutoGrab(client, g.NetworkObjectId);
        }

        /// <summary>Server: spawn n items for a player at their feet and tell their client to pick them up.</summary>
        public static void ServerSpawnFor(ulong client, string key, int n, bool pickUp = true)
        {
            var p = ServerLogic.PlayerFor(client);
            if (p == null || n <= 0 || key == null || !ModItems.ByKey.TryGetValue(key, out var item)) return;
            var pos = p.transform.position + (pickUp ? Vector3.up * 0.3f : p.transform.forward * 0.8f + Vector3.up * 0.8f);
            if (item.spawnPrefab.GetComponent<StackItem>() != null)
            {
                for (int left = n; left > 0; left -= 64)
                {
                    var st = ModItems.ServerSpawnStack(item, Mathf.Min(64, left), pos);
                    if (st != null && pickUp) BlockNet.ServerAutoGrab(client, st.NetworkObjectId);
                }
            }
            else
                for (int i = 0; i < n; i++)
                {
                    var g = ModItems.ServerSpawnPlain(item, pos);
                    if (g != null && pickUp) BlockNet.ServerAutoGrab(client, g.NetworkObjectId);
                }
        }
    }
}
