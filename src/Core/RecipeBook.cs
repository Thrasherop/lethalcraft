using System.Collections.Generic;
using System.Linq;

namespace LethalMinecraft
{
    /// <summary>A crafting recipe: a Minecraft shaped pattern (rows of symbols) or a shapeless list of ingredients.</summary>
    public class Recipe
    {
        public string Result;
        public int Count = 1;
        public string Category;
        public string[] Pattern;                    // shaped: rows, ' ' = empty
        public Dictionary<char, string> Map;        // shaped: symbol -> item key
        public bool Shapeless;
        public (string key, int n)[] Needs;         // total ingredients (one item per filled cell)
        public int Width => Shapeless ? 0 : Pattern.Max(r => r.Length);
        public int Height => Shapeless ? 0 : Pattern.Length;
        /// <summary>Fits the 2x2 pocket grid (like Minecraft's inventory crafting).</summary>
        public bool Pocket => Shapeless ? Needs.Sum(n => n.n) <= 4 : Width <= 2 && Height <= 2;

        public static Recipe Shaped(string cat, string result, int count, string[] rows, params (char sym, string key)[] map)
        {
            var r = new Recipe { Category = cat, Result = result, Count = count, Pattern = rows, Map = map.ToDictionary(m => m.sym, m => m.key) };
            r.Needs = rows.SelectMany(row => row).Where(ch => ch != ' ').GroupBy(ch => ch).Select(g => (r.Map[g.Key], g.Count())).ToArray();
            return r;
        }

        public static Recipe ShapelessOf(string cat, string result, int count, params string[] ingredients)
            => new Recipe { Category = cat, Result = result, Count = count, Shapeless = true, Needs = ingredients.GroupBy(k => k).Select(g => (g.Key, g.Count())).ToArray() };
    }

    /// <summary>Matches a crafting grid against recipes (pure logic, unit tested).</summary>
    public static class RecipeBook
    {
        /// <summary>grid: size x size item keys, row-major from the top-left, null = empty. Returns the first matching recipe.</summary>
        public static Recipe Match(IEnumerable<Recipe> recipes, string[] grid, int size)
        {
            foreach (var r in recipes)
                if (Matches(r, grid, size)) return r;
            return null;
        }

        public static bool Matches(Recipe r, string[] grid, int size)
        {
            int minR = size, maxR = -1, minC = size, maxC = -1;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (!string.IsNullOrEmpty(grid[y * size + x]))
                    {
                        minR = System.Math.Min(minR, y); maxR = System.Math.Max(maxR, y);
                        minC = System.Math.Min(minC, x); maxC = System.Math.Max(maxC, x);
                    }
            if (maxR < 0) return false;
            if (r.Shapeless)
            {
                var have = new Dictionary<string, int>();
                foreach (var k in grid) if (!string.IsNullOrEmpty(k)) have[k] = have.TryGetValue(k, out int n) ? n + 1 : 1;
                return have.Count == r.Needs.Length && r.Needs.All(n => have.TryGetValue(n.key, out int c) && c == n.n);
            }
            int h = maxR - minR + 1, w = maxC - minC + 1;
            if (h != r.Height || w != r.Width) return false;
            return Fits(r, grid, size, minR, minC, false) || Fits(r, grid, size, minR, minC, true);
        }

        static bool Fits(Recipe r, string[] grid, int size, int r0, int c0, bool mirror)
        {
            int w = r.Width;
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < w; x++)
                {
                    var row = r.Pattern[y];
                    int px = mirror ? w - 1 - x : x;
                    char ch = px < row.Length ? row[px] : ' ';
                    string want = ch == ' ' ? null : r.Map[ch];
                    string got = grid[(r0 + y) * size + c0 + x];
                    if (string.IsNullOrEmpty(got)) got = null;
                    if (want != got) return false;
                }
            return true;
        }
    }
}
