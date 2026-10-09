using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>Engine-free: rewriting typed terminal sentences so multi-word store items can be typed with spaces.</summary>
    public static class TerminalText
    {
        /// <summary>A store keyword without spaces/punctuation (the terminal strips punctuation from what you type).</summary>
        public static string Squash(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>
        /// Joins the longest run of words (right after the verb, or from the start) that spells one of the given keywords
        /// (squashed, e.g. "stonepickaxe"): "buy stone pickaxe 2" -> "buy stonepickaxe 2"; a partial "buy stone pick" ->
        /// "buy stonepickaxe" when only one keyword starts that way. Words that are fine on their own ("buy stone 5") stay.
        /// </summary>
        /// <summary>Other names players type for an item: "Block of Redstone" is a "redstone block" too.</summary>
        /// <summary>
        /// The word a typed word most likely means, when it isn't one exactly: the one sharing the longest start with it
        /// (at least <paramref name="minPrefix"/> letters; the first such word on a tie). The game takes the first word in
        /// its list sharing any 3 letters, so "bookshelves" meant "boombox" (#39). -1: none.
        /// </summary>
        public static int BestPrefixMatch(string typed, IList<string> words, int minPrefix = 3)
        {
            int best = -1, bestLen = minPrefix - 1;
            if (string.IsNullOrEmpty(typed)) return -1;
            // the word itself, or what it's the plural of, before the longest shared start: "cobblestones" is cobblestone,
            // not the start of "cobblestonestairs"
            int exact = IndexOf(words, typed);
            if (exact >= 0) return exact;
            foreach (var sing in Singulars(typed))
                if (sing.Length >= minPrefix && (exact = IndexOf(words, sing)) >= 0) return exact;
            for (int i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (string.IsNullOrEmpty(w)) continue;
                int n = 0, max = System.Math.Min(w.Length, typed.Length);
                while (n < max && w[n] == typed[n]) n++;
                if (n > bestLen) { best = i; bestLen = n; }
            }
            return best;
        }

        static int IndexOf(IList<string> words, string w)
        {
            for (int i = 0; i < words.Count; i++) if (words[i] == w) return i;
            return -1;
        }

        /// <summary>What a squashed plural might be the plural of ("torches" -> "torche", "torch"; "shelves" -> "shelf").</summary>
        public static IEnumerable<string> Singulars(string w)
        {
            if (w.EndsWith("ves")) yield return w.Substring(0, w.Length - 3) + "f";
            if (w.EndsWith("es")) yield return w.Substring(0, w.Length - 2);
            if (w.EndsWith("s")) yield return w.Substring(0, w.Length - 1);
        }

        public static IEnumerable<string> Aliases(string itemName)
        {
            var n = itemName.ToLowerInvariant();
            if (n.StartsWith("block of ")) yield return Squash(n.Substring(9) + " block");
        }

        /// <summary>As below, with aliases (alias -> the keyword it stands for).</summary>
        public static string JoinKeywords(string typed, string[] multiWordKeywords, IDictionary<string, string> aliases)
        {
            var all = aliases == null ? multiWordKeywords : multiWordKeywords.Concat(aliases.Keys).Distinct().ToArray();
            string joined = JoinKeywords(typed, all);
            if (aliases == null || joined == typed) return joined;
            var words = joined.Split(' ');
            for (int i = 0; i < words.Length; i++) if (aliases.TryGetValue(words[i], out var real)) words[i] = real;
            return string.Join(" ", words);
        }

        public static string JoinKeywords(string typed, string[] multiWordKeywords)
        {
            var words = typed.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || multiWordKeywords.Length == 0) return typed;
            foreach (int start in new[] { 1, 0 })
            {
                for (int n = words.Length - start; n >= 2; n--)
                {
                    string cand = Squash(string.Concat(words.Skip(start).Take(n)));
                    if (cand.Length < 4) continue;
                    string match = multiWordKeywords.Contains(cand) ? cand : null;
                    // a plural: "redstone torches" is the redstone torch
                    if (match == null)
                        foreach (var single in Singulars(cand))
                            if (multiWordKeywords.Contains(single)) { match = single; break; }
                    if (match == null)
                    {
                        var starts = multiWordKeywords.Where(k => k.StartsWith(cand)).Distinct().ToList();
                        if (starts.Count == 1) match = starts[0];
                    }
                    if (match == null) continue;
                    return string.Join(" ", words.Take(start).Concat(new[] { match }).Concat(words.Skip(start + n)));
                }
            }
            return typed;
        }
    
        /// <summary>
        /// "buy (something not sold)": the craft-only item (one of notSold, display names) the words after "buy" spell out
        /// in full or start to (two words or more), or null. Null too when the terminal already resolved the order to a
        /// sold item those words start (resolved, its display name), so "buy stone" stays Stone while "buy stone pickaxe"
        /// (which the terminal cuts down to Stone) is caught.
        /// </summary>
        public static string NotSoldMatch(string typed, IEnumerable<string> notSold, string resolved)
        {
            var words = typed.ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count < 2 || words[0] != "buy") return null;
            words.RemoveAt(0);
            if (words.Count > 1 && words[words.Count - 1].All(char.IsDigit)) words.RemoveAt(words.Count - 1);
            string joined = Squash(string.Join("", words));
            if (joined.Length == 0) return null;
            if (resolved != null && Squash(resolved).StartsWith(joined)) return null;
            var names = notSold.ToList();
            var exact = names.FirstOrDefault(n => Squash(n) == joined);
            if (exact != null) return exact;
            if (words.Count < 2) return null;
            var partial = names.Where(n => Squash(n).StartsWith(joined)).ToList();
            return partial.Count == 1 ? partial[0] : null;
        }
    }
}
