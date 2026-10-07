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
    }
}
