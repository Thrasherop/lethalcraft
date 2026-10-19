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
    /// (#244) A list can also hold a dye colour (dyed leather armor): such an item always has a list's number, even with no
    /// enchantments. And the same numbers keep other texts an item's number points at, kept as they are ("F:..." a firework's
    /// or a firework star's: Fireworks).
    /// </summary>
    public static class EnchSets
    {
        public const int Marker = 56, IdBits = 12, MaxId = (1 << IdBits) - 1;

        static readonly Dictionary<int, (List<(EnchKind kind, int level)> list, int work, int color)> byId = new Dictionary<int, (List<(EnchKind, int)>, int, int)>();
        static readonly Dictionary<string, int> byText = new Dictionary<string, int>();
        static readonly Dictionary<int, string> raw = new Dictionary<int, string>();
        /// <summary>A text kept as it is (not an enchantment list).</summary>
        public static bool IsRaw(string text) => text != null && text.StartsWith("F:");
        public static string RawOf(int id) => raw.TryGetValue(id, out var s) ? s : null;
        static int nextId = 1;
        public static readonly HashSet<string> Asked = new HashSet<string>();

        /// <summary>The game's side: the host registers a new list (gives its number, tells everyone); another machine sends
        /// the host the text and gives 0. Unset (the unit tests): numbers are given here.</summary>
        public static Func<string, int> Allocate;

        public static int Count => byId.Count + raw.Count;
        public static IEnumerable<(int id, string text)> All => byId.Keys.Select(k => (k, Text(byId[k].list, byId[k].work, byId[k].color))).Concat(raw.Select(kv => (kv.Key, kv.Value))).OrderBy(e => e.Item1);

        public static void Reset() { byId.Clear(); byText.Clear(); raw.Clear(); Asked.Clear(); nextId = 1; }

        /// <summary>A raw text's number: known, or (the host) a new one; 0 while another machine waits for the host's.</summary>
        public static int IdOfRaw(string text)
        {
            if (byText.TryGetValue(text, out int id)) return id;
            if (Allocate != null) return Allocate(text);
            return Register(text);
        }

        /// <summary>"3:5,2:3;1": kind:level pairs in order, then the work count; then a dye colour, if any ("...;1;A06540").</summary>
        public static string Text(IEnumerable<(EnchKind kind, int level)> list, int work, int color = -1) =>
            string.Join(",", list.Select(e => (int)e.kind + ":" + e.level)) + ";" + Math.Max(0, work) + (color >= 0 ? ";" + (color & 0xFFFFFF).ToString("X6") : "");

        public static (List<(EnchKind kind, int level)> list, int work, int color) Parse(string text)
        {
            var list = new List<(EnchKind, int)>();
            int work = 0, color = -1;
            if (string.IsNullOrEmpty(text)) return (list, 0, -1);
            var parts = text.Split(';');
            foreach (var p in parts[0].Split(','))
            {
                var kv = p.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int k) && int.TryParse(kv[1], out int l) && k > 0 && l > 0) list.Add(((EnchKind)k, l));
            }
            if (parts.Length > 1) int.TryParse(parts[1], out work);
            if (parts.Length > 2 && int.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out int c)) color = c & 0xFFFFFF;
            return (list, work, color);
        }

        public static bool TryGet(int id, out List<(EnchKind kind, int level)> list, out int work) => TryGet(id, out list, out work, out _);
        public static bool TryGet(int id, out List<(EnchKind kind, int level)> list, out int work, out int color)
        {
            if (byId.TryGetValue(id, out var v)) { list = v.list; work = v.work; color = v.color; return true; }
            list = null; work = 0; color = -1; return false;
        }

        /// <summary>A list's number: known already, or (the host) a new one; 0 while another machine waits for the host's.</summary>
        public static int IdOf(IEnumerable<(EnchKind kind, int level)> list, int work, int color = -1)
        {
            string text = Text(list, work, color);
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
            if (IsRaw(text)) { raw[id] = text; byText[text] = id; return id; }
            var p = Parse(text);
            byId[id] = p; byText[Text(p.list, p.work, p.color)] = id; byText[text] = id;
            return id;
        }

        /// <summary>Every machine: a list's number from the host (or the save).</summary>
        public static void Receive(int id, string text)
        {
            if (id <= 0 || id > MaxId) return;
            if (IsRaw(text)) { raw[id] = text; byText[text] = id; if (id >= nextId) nextId = id + 1; Asked.Remove(text); return; }
            var p = Parse(text);
            byId[id] = p; byText[Text(p.list, p.work, p.color)] = id; byText[text] = id;
            if (id >= nextId) nextId = id + 1;
            Asked.Remove(text);
        }
    }
}
