using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LethalMinecraft.Tests
{
    /// <summary>Harmony's PatchAll skips a class without its own [HarmonyPatch]: patch methods there never run (the
    /// outposts' landing roll and the tamed wolf's "what my owner hit" were found that way, 2026-10-16). Read from the
    /// sources: every class holding a patch method carries the attribute itself.</summary>
    public class HarmonyTests
    {
        static string Src()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src"))) d = d.Parent;
            return d == null ? null : Path.Combine(d.FullName, "src");
        }

        [Fact] public void EveryPatchMethodsClassIsAnnotated()
        {
            var src = Src();
            Assert.NotNull(src);
            var missing = new List<string>();
            var classDecl = new Regex(@"\b(class|struct)\s+(\w+)");
            foreach (var file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                var stack = new Stack<(string name, bool annotated, int depth)>();
                var pendingAttrs = new List<string>();
                int depth = 0;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = Regex.Replace(raw, "//.*$", "").Trim();
                    if (line.StartsWith("[")) { pendingAttrs.Add(line); continue; }
                    var m = classDecl.Match(line);
                    if (m.Success && !line.Contains("new ") && !line.Contains("where "))
                        stack.Push((m.Groups[2].Value, pendingAttrs.Any(a => a.Contains("HarmonyPatch")), depth));
                    else if (line.Length > 0 && pendingAttrs.Any(a => a.Contains("HarmonyPatch(")))
                    {
                        var cls = stack.Count > 0 ? stack.Peek() : default;
                        if (cls.name != null && !cls.annotated) missing.Add(Path.GetFileName(file) + ": " + cls.name);
                    }
                    if (line.Length > 0) pendingAttrs.Clear();
                    foreach (char c in line) { if (c == '{') depth++; else if (c == '}') { depth--; while (stack.Count > 0 && depth <= stack.Peek().depth) stack.Pop(); } }
                }
            }
            Assert.Empty(missing.Distinct());
        }
    }
}
