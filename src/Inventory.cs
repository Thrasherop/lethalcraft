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
            if (n > 0)
            {
                int max = Mathf.Max(1, StackLimit(key)), stacks = stackable ? (n + max - 1) / max : n;
                for (int i = 0; i < stacks; i++) reserved.Add(Time.time);
                BlockNet.RequestSpawnForMe(key, n);
            }
        }

        /// <summary>How many of an item fit in one slot: 64 for blocks and materials (16 ender pearls), 1 for tools.</summary>
        public static int MaxStackOf(string key)
        {
            if (key == null || !ModItems.ByKey.TryGetValue(key, out var item)) return 64;
            var st = item.spawnPrefab != null ? item.spawnPrefab.GetComponent<StackItem>() : null;
            if (st == null) return 1;
            return StackLimit(key);
        }

        /// <summary>
        /// How many of an item make a stack, Minecraft's: one of a filled bucket, a potion, a minecart, a bed, a saddle,
        /// horse armor, a book and quill, a disc, a stew; 16 of an empty bucket, an ender pearl, a snowball, an egg, a
        /// sign; 64 of the rest. (Two tables had disagreed: creative handed out 64 water buckets, minecarts and beds.)
        /// </summary>
        public static int StackLimit(string key)
        {
            key = ItemData.TypeOf(key); // (#228: a named or written one is still one of them)
            if (key == null) return 64;
            if (Potions.IsPotion(key) || One.Contains(key) || System.Array.IndexOf(MinecartAI.Keys, key) >= 0 || key.EndsWith("_horse_armor") || key.StartsWith("music_disc_")
                || key.EndsWith("_bed") || key.EndsWith("_boat") || key.EndsWith("_stew") || key.EndsWith("_soup")
                || key.StartsWith("goat_horn") || key.EndsWith("_banner_pattern")) return 1; // (#244: Minecraft's, a horn and a pattern one at a time)
            if (Sixteen.Contains(key) || key.EndsWith("_sign") || key.EndsWith("_banner")) return 16;
            return 64;
        }
        static readonly HashSet<string> One = new HashSet<string> { "water_bucket", "lava_bucket", "milk_bucket", "saddle", "writable_book", "written_book", "enchanted_book", "cake", "totem_of_undying", "elytra", "spyglass", // (#229: the spyglass)
            "bow", "crossbow", "fishing_rod", "shears", "flint_and_steel", "carrot_on_a_stick" }; // (#244: they wear out: one at a time, Minecraft's)
        static readonly HashSet<string> Sixteen = new HashSet<string> { "bucket", "ender_pearl", "snowball", "egg", "honey_bottle", "armor_stand" };

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
        public static void QueueGrab(ulong id, int slot = -1) { toGrab.Add((id, Time.time)); if (slot >= 0) intoSlot[id] = slot; if (reserved.Count > 0) reserved.RemoveAt(0); }
        /// <summary>(#223) Items the host sent into a slot of their own (a powered shelf's swap with the hotbar's end).</summary>
        static readonly Dictionary<ulong, int> intoSlot = new Dictionary<ulong, int>();

        // slots promised to items asked for but not here yet (a shift-click out of a chest: what's on its way takes room)
        static readonly List<float> reserved = new List<float>();
        static int Reserved() { reserved.RemoveAll(t => Time.time - t > 4f); return reserved.Count + toGrab.Count; }

        /// <summary>
        /// How many of an item the hotbar can still take (#209: quick shift-clicks out of a chest took more than fitted, and
        /// the rest landed at your feet): room in matching stacks, plus empty slots not already promised to something on
        /// its way. Minecraft's shift-click moves only what fits.
        /// </summary>
        public static int RoomFor(string key)
        {
            var p = Local;
            if (p == null || key == null) return 0;
            int max = MaxStackOf(key), room = 0;
            bool stackable = ModItems.ByKey.TryGetValue(key, out var item) && item.spawnPrefab.GetComponent<StackItem>() != null;
            for (int i = 0; i < p.ItemSlots.Length; i++)
            {
                if (stackable && p.ItemSlots[i] is StackItem st && Crafting.KeyOf(st) == key) room += Mathf.Max(0, st.MaxStack - CountIn(p, i));
            }
            int free = p.ItemSlots.Count(g => g == null) - Reserved();
            return room + Mathf.Max(0, free) * max;
        }

        /// <summary>Something is on its way into or out of the hotbar (a pickup queued, a count not yet confirmed).</summary>
        public static bool Pending => toGrab.Count > 0 || pendingTake.Values.Any(v => Time.time - v.t < 3f) || pendingAdd.Values.Any(v => Time.time - v.t < 3f);

        void Update()
        {
            Pickup.Tick();
            Commands.Tick();
            SlotSync.Tick();
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
                // (its own slot still holding what's on its way out, a stack just emptied: wait for it a moment)
                if (intoSlot.TryGetValue(toGrab[i].id, out int w0) && w0 >= 0 && w0 < p.ItemSlots.Length && p.ItemSlots[w0] != null && Time.time - toGrab[i].t < 1.5f) continue;
                // (its own slot free: in hand first, then the grab; the game puts a pickup into the selected slot. A switch
                // right after another is ignored for a moment, so this waits for it rather than grabbing into the wrong one)
                if (w0 >= 0 && w0 < p.ItemSlots.Length && intoSlot.ContainsKey(toGrab[i].id) && p.ItemSlots[w0] == null && p.currentItemSlot != w0 && Time.time - toGrab[i].t < 2.5f)
                {
                    Pickup.RestoreTo(p.currentItemSlot);
                    HotbarInput.SelectSlot(p, w0);
                    return;
                }
                toGrab.RemoveAt(i);
                var g = no.GetComponent<GrabbableObject>();
                if (g == null || g.isHeld || g.isPocketed || p.FirstEmptyItemSlot() == -1) { intoSlot.Remove(no.NetworkObjectId); return; } // no room: it stays at our feet
                intoSlot.Remove(no.NetworkObjectId);
                GrabDirect(p, g);
                grabWait = 0.15f;
                // (the rest sent into slots of their own wait their turn: their clocks start over, nine from a row of
                // powered shelves take longer than any one wait)
                for (int j = 0; j < toGrab.Count; j++) if (intoSlot.ContainsKey(toGrab[j].id)) toGrab[j] = (toGrab[j].id, Time.time);
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
            if (g == null) return;
            if (item.isScrap)
            {
                // scrap gets a value the way the game rolls one for a moon's scrap (#86)
                float mult = RoundManager.Instance != null ? RoundManager.Instance.scrapValueMultiplier : 0.4f;
                int value = Mathf.Max(1, (int)(Random.Range(item.minValue, item.maxValue) * mult));
                g.SetScrapValue(value);
                BlockNet.ServerScrapValue(g.NetworkObjectId, value);
            }
            BlockNet.ServerAutoGrab(client, g.NetworkObjectId);
        }

        /// <summary>Server: spawn n items for a player at their feet and tell their client to pick them up.</summary>
        /// <summary>Host: takes n of an item from a player's hotbar (a trade's price, #152); false (taking nothing) if they
        /// haven't that many.</summary>
        public static bool ServerTakeFromHotbar(PlayerControllerB p, string key, int n)
        {
            var stacks = p.ItemSlots.OfType<StackItem>().Where(s => s != null && ItemData.Is(s.ItemKey, ItemData.TypeOf(key)) && s.Count > 0).ToList(); // (by type: a named one too, #228)
            if (stacks.Sum(s => s.Count) < n) return false;
            foreach (var s in stacks)
            {
                if (n <= 0) break;
                int t = Mathf.Min(n, s.Count);
                s.ServerSetCount(s.Count - t, despawnIfEmpty: true);
                n -= t;
            }
            return true;
        }

        public static void ServerSpawnFor(ulong client, string key, int n, bool pickUp = true, int slot = -1)
        {
            var p = ServerLogic.PlayerFor(client);
            if (p == null || n <= 0 || key == null || !ModItems.ByKey.TryGetValue(key, out var item)) return;
            var pos = p.transform.position + (pickUp ? Vector3.up * 0.3f : p.transform.forward * 0.8f + Vector3.up * 0.8f);
            if (item.spawnPrefab.GetComponent<StackItem>() != null)
            {
                // (#244: stacks of its own size: two bows shift-crafted had come out as one slot of two)
                int max = Mathf.Max(1, StackLimit(key));
                for (int left = n; left > 0; left -= max)
                {
                    var st = ModItems.ServerSpawnStack(item, Mathf.Min(max, left), pos);
                    ItemNames.ServerApply(st, key); // (#228)
                    if (st != null && pickUp) BlockNet.ServerAutoGrab(client, st.NetworkObjectId, slot);
                }
            }
            else
                for (int i = 0; i < n; i++)
                {
                    var g = ModItems.ServerSpawnPlainKeyed(key, pos);
                    if (g != null && pickUp) BlockNet.ServerAutoGrab(client, g.NetworkObjectId, slot);
                }
        }
    }
}
