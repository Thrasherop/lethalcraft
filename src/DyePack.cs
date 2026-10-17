using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// The store's dye pack (#225, the owner's): one order brings two of the game's own belt bags (the owner: the bag isn't
    /// changed; its screen shows 15), each holding eight of the sixteen dyes, sixteen of each. It arrives as a "Dye Pack"
    /// in the delivery and, once it's on the ground, the host opens it: the two bags where it lay, the dyes put in them
    /// the way the game puts things in a bag (BeltBagItem.TryAddObjectToBagServerRpc's work).
    /// </summary>
    public class DyePack : MonoBehaviour
    {
        public const string Key = "dye_pack";
        public const int PerColor = 16;
        public static int Opened; // (dev/tests)
        float settle = -1f; bool done;

        void Update()
        {
            if (done || !BlockNet.IsServer) return;
            var st = GetComponent<StackItem>();
            if (st == null || st.isHeld || st.isHeldByEnemy || st.isPocketed || st.heldByPlayerOnServer) { settle = -1f; return; } // (also: put in a bag)
            // (once it's lain still a moment: the delivery drops it)
            if (settle < 0f) { settle = Time.time + 1.5f; return; }
            if (Time.time < settle) return;
            done = true;
            if (ServerOpen(transform.position)) st.ServerSetCount(0, despawnIfEmpty: true);
        }

        /// <summary>Host: two belt bags at `at`, the dyes in them. False: the game's belt bag wasn't found.</summary>
        public static bool ServerOpen(Vector3 at)
        {
            var bagItem = StartOfRound.Instance?.allItemsList?.itemsList?.FirstOrDefault(i => i != null && i.spawnPrefab != null && i.spawnPrefab.GetComponent<BeltBagItem>() != null);
            if (bagItem == null) { Plugin.Log.LogWarning("[dye pack] no belt bag item in the game"); return false; }
            for (int b = 0; b < 2; b++)
            {
                var pos = at + new Vector3(b == 0 ? -0.3f : 0.3f, 0.2f, 0f);
                var bag = ModItems.ServerSpawnPlain(bagItem, pos) as BeltBagItem;
                if (bag == null) continue;
                for (int c = b * 8; c < b * 8 + 8; c++)
                {
                    if (!ModItems.ByKey.TryGetValue(Dyes.Key(Dyes.Colors[c]), out var dye)) continue;
                    var st = ModItems.ServerSpawnStack(dye, PerColor, pos);
                    if (st == null) continue;
                    st.heldByPlayerOnServer = true;
                    bag.PutObjectInBagLocalClient(st);
                    bag.TryAddObjectToBagClientRpc(new NetworkObjectReference(st.NetworkObject), 0);
                }
            }
            Opened++;
            Plugin.Log.LogInfo($"[dye pack] opened at {at}: two belt bags of dyes");
            return true;
        }
    }
}
