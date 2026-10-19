using System;
using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>
    /// (#244) Enchantment lists too big for an item's saved number (more than three, or kinds past the first eight, or
    /// three with an anvil work count), kept once each by number: the host gives each new list its number, sends it to
    /// everyone and saves them with the game (as custom names are, ItemNames). The item's number then holds a marker (its
    /// first enchantment's six bits: code 0, level 7, which the old packing never makes) and the list's number (12 bits).
    /// A machine that isn't the host asks for a number and waits for it (Pack gives -1 meanwhile).
    /// </summary>
    public static class EnchSets
    {
        public const int Marker = 56, IdBits = 12, MaxId = (1 << IdBits) - 1;

        static readonly Dictionary<int, (List<(EnchKind kind, int level)> list, int work)> byId = new Dictionary<int, (List<(EnchKind, int)>, int)>();
        static readonly Dictionary<string, int> byText = new Dictionary<string, int>();
        static int nextId = 1;
        public static readonly HashSet<string> Asked = new HashSet<string>();

        /// <summary>The game's side: the host registers a new list (gives its number, tells everyone); another machine sends
        /// the host the text and gives 0. Unset (the unit tests): numbers are given here.</summary>
        public static Func<string, int> Allocate;

        public static int Count => byId.Count;
        public static IEnumerable<(int id, string text)> All => byId.Keys.OrderBy(k => k).Select(k => (k, Text(byId[k].list, byId[k].work)));

        public static void Reset() { byId.Clear(); byText.Clear(); Asked.Clear(); nextId = 1; }

        /// <summary>"3:5,2:3;1": kind:level pairs in order, then the work count.</summary>
        public static string Text(IEnumerable<(EnchKind kind, int level)> list, int work) =>
            string.Join(",", list.Select(e => (int)e.kind + ":" + e.level)) + ";" + Math.Max(0, work);

        public static (List<(EnchKind kind, int level)> list, int work) Parse(string text)
        {
            var list = new List<(EnchKind, int)>();
            int work = 0;
            if (string.IsNullOrEmpty(text)) return (list, 0);
            var parts = text.Split(';');
            foreach (var p in parts[0].Split(','))
            {
                var kv = p.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int k) && int.TryParse(kv[1], out int l) && k > 0 && l > 0) list.Add(((EnchKind)k, l));
            }
            if (parts.Length > 1) int.TryParse(parts[1], out work);
            return (list, work);
        }

        public static bool TryGet(int id, out List<(EnchKind kind, int level)> list, out int work)
        {
            if (byId.TryGetValue(id, out var v)) { list = v.list; work = v.work; return true; }
            list = null; work = 0; return false;
        }

        /// <summary>A list's number: known already, or (the host) a new one; 0 while another machine waits for the host's.</summary>
        public static int IdOf(IEnumerable<(EnchKind kind, int level)> list, int work)
        {
            string text = Text(list, work);
            if (byText.TryGetValue(text, out int id)) return id;
            if (Allocate != null) return Allocate(text);
            return Register(text);
        }

        /// <summary>The host: a new list's number (or the one it has).</summary>
        public static int Register(string text)
        {
            if (byText.TryGetValue(text, out int id)) return id;
            if (nextId > MaxId) return 0;
            id = nextId++;
            var p = Parse(text);
            byId[id] = p; byText[Text(p.list, p.work)] = id; byText[text] = id;
            return id;
        }

        /// <summary>Every machine: a list's number from the host (or the save).</summary>
        public static void Receive(int id, string text)
        {
            if (id <= 0 || id > MaxId) return;
            var p = Parse(text);
            byId[id] = p; byText[Text(p.list, p.work)] = id; byText[text] = id;
            if (id >= nextId) nextId = id + 1;
            Asked.Remove(text);
        }
    }
}
