using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LethalMinecraft
{
    /// <summary>
    /// Reads textures and sounds from the player's OWN local Minecraft installation at runtime.
    /// Nothing from Minecraft is shipped with this mod. If no installation is found we fall back
    /// to the procedurally generated art embedded in the DLL.
    /// </summary>
    public static class McAssets
    {
        public static bool Available;
        public static string SourceDescription = "";
        static string jarPath;
        static string assetsDir;
        static Dictionary<string, string> soundIndex; // "minecraft/sounds/dig/stone1.ogg" -> absolute file path
        static ZipArchive jar;

        class Candidate { public string Jar, Json, Version, AssetsDir, Source; public Version V; }

        public static void Init()
        {
            if (!Plugin.UseMinecraftAssets.Value) { Plugin.Log.LogInfo("Minecraft assets disabled in config; using built-in art."); return; }
            try
            {
                var list = new List<Candidate>();
                string custom = CleanPath(Plugin.MinecraftDir.Value);
                if (!string.IsNullOrEmpty(custom))
                {
                    Scan(custom, "custom path", list);
                    if (list.Count == 0)
                        Plugin.Log.LogWarning($"MinecraftDirectory '{Plugin.MinecraftDir.Value}' -> '{custom}': no Minecraft version jars found there. " +
                            "Point it at your .minecraft folder (the one containing 'versions' and 'assets'), a launcher folder, an instance folder, or a client .jar. Falling back to auto-detection.");
                }
                if (list.Count == 0)
                {
                    string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var auto = new[]
                    {
                        Path.Combine(appdata, ".minecraft"),
                        Path.Combine(home, "curseforge", "minecraft", "Install"),
                        Path.Combine(appdata, "PrismLauncher"),
                        Path.Combine(appdata, "ModrinthApp"),
                        Path.Combine(appdata, "com.modrinth.theseus"),
                        Path.Combine(appdata, ".tlauncher", "legacy", "Minecraft", "game"),
                        Path.Combine(appdata, "gdlauncher_next"),
                        Path.Combine(appdata, "ATLauncher"),
                    };
                    foreach (var a in auto) { Scan(a, "auto: " + a, list); if (list.Count > 0) break; }
                }
                if (list.Count == 0)
                {
                    Plugin.Log.LogInfo("No local Minecraft installation found; using built-in art and sounds. Set [Assets] MinecraftDirectory if yours is somewhere unusual.");
                    return;
                }

                Candidate pick = null;
                string wantVer = (Plugin.MinecraftVersion.Value ?? "").Trim();
                if (wantVer.Length > 0)
                {
                    pick = list.FirstOrDefault(c => c.Version == wantVer);
                    if (pick == null) Plugin.Log.LogWarning($"MinecraftVersion '{wantVer}' not found (have: {string.Join(", ", list.Select(c => c.Version).Distinct().Take(12))}); using the newest instead.");
                }
                if (pick == null) pick = list.OrderByDescending(c => c.V).First();

                jarPath = pick.Jar;
                jar = ZipFile.OpenRead(jarPath);
                assetsDir = pick.AssetsDir;
                LoadSoundIndex(pick);

                Available = true;
                SourceDescription = $"Minecraft {pick.Version} ({pick.Source}: {jarPath})" + (soundIndex != null ? $", {soundIndex.Count} sounds" : ", no sounds found");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read local Minecraft assets: " + e.Message);
                Available = false;
            }
        }

        /// <summary>Accepts quotes, %ENV% variables, ~, forward slashes and trailing separators.</summary>
        public static string CleanPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string p = raw.Trim().Trim('"', '\'').Trim();
            p = Environment.ExpandEnvironmentVariables(p);
            if (p.StartsWith("~")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = p.Replace('/', Path.DirectorySeparatorChar);
            try { p = Path.GetFullPath(p); } catch { }
            return p.TrimEnd(Path.DirectorySeparatorChar);
        }

        static readonly System.Text.RegularExpressions.Regex ReleaseName = new System.Text.RegularExpressions.Regex(@"^\d+\.\d+(\.\d+)?$");

        static bool TryVersion(string name, out Version v)
        {
            v = null;
            if (!ReleaseName.IsMatch(name)) return false;
            return Version.TryParse(name.Split('.').Length == 2 ? name + ".0" : name, out v);
        }

        /// <summary>Finds usable client jars under a path. Understands: a .jar file, a single version folder,
        /// a 'versions' folder, a .minecraft-style folder, Prism/MultiMC-style libraries, and launcher roots with instances.</summary>
        static void Scan(string path, string source, List<Candidate> outList)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path) && path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(path);
                    var name = Path.GetFileNameWithoutExtension(path);
                    var verName = TryVersion(name, out _) ? name : Path.GetFileName(dir);
                    var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+\.\d+(\.\d+)?)");
                    if (!TryVersion(verName, out var vv) && m.Success) { verName = m.Value; TryVersion(verName, out vv); }
                    Add(outList, path, Path.Combine(dir, name + ".json"), verName, vv ?? new Version(0, 0), FindAssets(dir), source);
                    return;
                }
                if (!Directory.Exists(path)) return;

                // a single version folder: <ver>/<ver>.jar
                var single = Path.Combine(path, Path.GetFileName(path) + ".jar");
                if (File.Exists(single) && TryVersion(Path.GetFileName(path), out var sv))
                    Add(outList, single, Path.ChangeExtension(single, ".json"), Path.GetFileName(path), sv, FindAssets(path), source);

                // a versions folder, or a root containing one
                foreach (var versions in new[] { path, Path.Combine(path, "versions") })
                {
                    if (!Directory.Exists(versions)) continue;
                    foreach (var dir in Directory.GetDirectories(versions))
                    {
                        var name = Path.GetFileName(dir);
                        if (!TryVersion(name, out var v)) continue;
                        var jarFile = Path.Combine(dir, name + ".jar");
                        Add(outList, jarFile, Path.Combine(dir, name + ".json"), name, v, FindAssets(versions), source);
                    }
                }

                // Prism / MultiMC: libraries/com/mojang/minecraft/<ver>/minecraft-<ver>-client.jar
                var libs = Path.Combine(path, "libraries", "com", "mojang", "minecraft");
                if (Directory.Exists(libs))
                    foreach (var dir in Directory.GetDirectories(libs))
                    {
                        var name = Path.GetFileName(dir);
                        if (!TryVersion(name, out var v)) continue;
                        var jarFile = Path.Combine(dir, $"minecraft-{name}-client.jar");
                        // Prism keeps version metadata in meta/net.minecraft/<ver>.json
                        var meta = Path.Combine(path, "meta", "net.minecraft", name + ".json");
                        Add(outList, jarFile, meta, name, v, FindAssets(path), source);
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Scanning '{path}' failed: {e.Message}"); }
        }

        static void Add(List<Candidate> list, string jarFile, string json, string ver, Version v, string assets, string source)
        {
            if (!File.Exists(jarFile) || new FileInfo(jarFile).Length < 1_000_000) return;
            if (list.Any(c => string.Equals(c.Jar, jarFile, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(new Candidate { Jar = jarFile, Json = json, Version = ver, V = v, AssetsDir = assets, Source = source });
        }

        /// <summary>Looks for the shared 'assets' folder (indexes + objects) near a path, then in the default .minecraft.</summary>
        static string FindAssets(string near)
        {
            var dir = near;
            for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir); up++)
            {
                var a = Path.Combine(dir, "assets");
                if (Directory.Exists(Path.Combine(a, "indexes")) && Directory.Exists(Path.Combine(a, "objects"))) return a;
                dir = Path.GetDirectoryName(dir);
            }
            var def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "assets");
            return Directory.Exists(Path.Combine(def, "indexes")) ? def : null;
        }

        static void LoadSoundIndex(Candidate pick)
        {
            try
            {
                if (pick.AssetsDir == null) return;
                string indexId = null;
                if (File.Exists(pick.Json))
                {
                    var verJson = JObject.Parse(File.ReadAllText(pick.Json));
                    indexId = (string)verJson["assetIndex"]?["id"] ?? (string)verJson["assets"];
                }
                var indexes = Path.Combine(pick.AssetsDir, "indexes");
                string indexFile = indexId != null ? Path.Combine(indexes, indexId + ".json") : null;
                if (indexFile == null || !File.Exists(indexFile))
                {
                    // no metadata: use the biggest (newest-style) index available
                    indexFile = Directory.Exists(indexes) ? Directory.GetFiles(indexes, "*.json").OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault() : null;
                }
                if (indexFile == null) return;
                soundIndex = new Dictionary<string, string>();
                var objects = (JObject)JObject.Parse(File.ReadAllText(indexFile))["objects"];
                foreach (var prop in objects.Properties())
                {
                    if (!prop.Name.EndsWith(".ogg")) continue;
                    string hash = (string)prop.Value["hash"];
                    soundIndex[prop.Name] = Path.Combine(pick.AssetsDir, "objects", hash.Substring(0, 2), hash);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Minecraft sound index unavailable: " + e.Message); }
        }

        /// <summary>Returns an RGBA texture (readable) from the jar, e.g. "block/stone" or "gui/sprites/hud/heart/full".</summary>
        public static Texture2D LoadTexture(string path)
        {
            if (jar == null) return null;
            try
            {
                var entry = jar.GetEntry("assets/minecraft/textures/" + path + ".png");
                if (entry == null) return null;
                byte[] data;
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(data, false)) return null;
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("texture " + path + ": " + e.Message);
                return null;
            }
        }

        public static string SoundFile(string mcPath)
        {
            if (soundIndex == null) return null;
            return soundIndex.TryGetValue("minecraft/sounds/" + mcPath + ".ogg", out var f) && File.Exists(f) ? f : null;
        }

        public static IEnumerable<string> SoundVariants(string baseName, int max = 8)
        {
            // e.g. "dig/stone" -> dig/stone1..4
            for (int i = 1; i <= max; i++)
            {
                var f = SoundFile(baseName + i);
                if (f != null) yield return f;
            }
            var single = SoundFile(baseName);
            if (single != null) yield return single;
        }
    }
}
