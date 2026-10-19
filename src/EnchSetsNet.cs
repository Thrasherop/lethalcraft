using System;
using System.IO;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) The game's side of EnchSets (enchantment lists kept by number): the host gives a new list its number and
    /// tells everyone; another machine asks the host and waits (its table or anvil shows the result once the number's
    /// come). Saved with the game and loaded before the ship's items come out (their saved numbers point at them).
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class EnchSetsNet
    {
        const string Save = "LMC_EnchSets_v1";
        public static int Asked, Given; // (dev/tests)

        public static void Init()
        {
            EnchSets.Allocate = text =>
            {
                if (BlockNet.IsServer) return ServerRegister(text);
                if (EnchSets.Asked.Add(text)) { BlockNet.RequestEnchSet(text); Asked++; }
                return 0;
            };
        }

        /// <summary>Host: a list's number (a new one told to everyone).</summary>
        public static int ServerRegister(string text)
        {
            int before = EnchSets.Count;
            int id = EnchSets.Register(text);
            if (id > 0 && EnchSets.Count > before) { BlockNet.ServerEnchSet(id, text); Given++; }
            return id;
        }

        /// <summary>Every machine: a list's number from the host; the glints of what's lying about or held may change.</summary>
        public static void Receive(int id, string text)
        {
            EnchSets.Receive(id, text);
            try { foreach (var g in UnityEngine.Object.FindObjectsOfType<GrabbableObject>()) if (g is ToolItem || g is ArmorItem) Glint.ApplyModel(g); } catch { }
        }

        public static void ServerSyncTo(ulong client)
        {
            foreach (var (id, text) in EnchSets.All) BlockNet.ServerEnchSet(id, text, client);
        }

        // ------------------------------------------------------------------ saved with the game
        public static void SaveTo(string file)
        {
            try
            {
                var ms = new MemoryStream(); var w = new BinaryWriter(ms);
                var all = EnchSets.All.ToList();
                w.Write(all.Count);
                foreach (var (id, text) in all) { w.Write(id); w.Write(text); }
                ES3.Save(Save, Convert.ToBase64String(ms.ToArray()), file);
            }
            catch (Exception e) { Plugin.Log.LogWarning("enchantment lists save: " + e.Message); }
        }

        /// <summary>Host, before the game puts the ship's saved items out: their lists first.</summary>
        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "LoadShipGrabbableItems"), HarmonyLib.HarmonyPrefix]
        static void BeforeShipItems()
        {
            try
            {
                string file = GameNetworkManager.Instance.currentSaveFileName;
                if (!ES3.KeyExists(Save, file)) return;
                var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(ES3.Load<string>(Save, file))));
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) { int id = r.ReadInt32(); string text = r.ReadString(); EnchSets.Receive(id, text); BlockNet.ServerEnchSet(id, text); }
                Plugin.Log.LogInfo($"[enchantments] {n} lists loaded");
            }
            catch (Exception e) { Plugin.Log.LogWarning("enchantment lists load: " + e.Message); }
        }

        /// <summary>A new game's lists (before the game's own load brings the save's back).</summary>
        [HarmonyLib.HarmonyPatch(typeof(StartOfRound), "Start"), HarmonyLib.HarmonyPrefix]
        static void NewGame() => EnchSets.Reset();
    }
}
