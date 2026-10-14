using System.Collections.Generic;
using System.Linq;
using GameNetcodeStuff;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LethalMinecraft
{
    /// <summary>
    /// Advancements (#131): Minecraft's, where this world has the pieces, and Lethal Company ones (every interior, every
    /// moon...). Each machine watches its own player (what's in the hotbar, what's worn, where you are, what you just
    /// did) and tells the server; the server keeps everyone's (with the save, by player) and, the first time, tells every
    /// machine: a line in everyone's chat, and Minecraft's "Advancement Made!" toast for whoever made it.
    /// Some take several parts (every Nether kind, every interior, every moon): the parts are kept the same way.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    public static class Advancements
    {
        public class Def { public string Id, Title, Description, Icon; public string[] Needs; }

        public static readonly List<Def> All = new List<Def>
        {
            new Def { Id = "stone_age", Title = "Stone Age", Description = "Mine stone with your new pickaxe", Icon = "cobblestone" },
            new Def { Id = "upgrade", Title = "Getting an Upgrade", Description = "Make a better pickaxe", Icon = "item_stone_pickaxe" },
            new Def { Id = "hardware", Title = "Acquire Hardware", Description = "Smelt an iron ingot", Icon = "item_iron_ingot" },
            new Def { Id = "suit_up", Title = "Suit Up", Description = "Protect yourself with a piece of iron armor", Icon = "item_iron_chestplate" },
            new Def { Id = "iron_pick", Title = "Isn't It Iron Pick", Description = "Upgrade your pickaxe", Icon = "item_iron_pickaxe" },
            new Def { Id = "diamonds", Title = "Diamonds!", Description = "Acquire diamonds", Icon = "item_diamond" },
            new Def { Id = "diamond_armor", Title = "Cover Me with Diamonds", Description = "Diamond armor saves lives", Icon = "item_diamond_chestplate" },
            new Def { Id = "hot_stuff", Title = "Hot Stuff", Description = "Fill a bucket with lava", Icon = "item_lava_bucket" },
            new Def { Id = "obsidian", Title = "Ice Bucket Challenge", Description = "Obtain a block of obsidian", Icon = "obsidian" },
            new Def { Id = "nether", Title = "We Need to Go Deeper", Description = "Build, light and enter a Nether Portal", Icon = "obsidian" },
            new Def { Id = "fortress", Title = "A Terrible Fortress", Description = "Break your way into a Nether Fortress", Icon = "nether_bricks" },
            new Def { Id = "bastion", Title = "Those Were the Days", Description = "Enter a Bastion Remnant", Icon = "polished_blackstone_bricks" },
            new Def { Id = "nether_tour", Title = "Hot Tourist Destinations", Description = "Explore every kind of Nether", Icon = "netherrack",
                      Needs = new[] { "kind:Fortress", "kind:Bastion", "kind:Valley", "kind:Crimson", "kind:Warped", "kind:Delta" } },
            new Def { Id = "return_to_sender", Title = "Return to Sender", Description = "Destroy a Ghast with a fireball", Icon = "item_fire_charge" },
            new Def { Id = "blaze_rod", Title = "Into Fire", Description = "Relieve a Blaze of its rod", Icon = "item_blaze_rod" },
            new Def { Id = "brewery", Title = "Local Brewery", Description = "Brew a potion", Icon = "item_potion" },
            new Def { Id = "enchanter", Title = "Enchanter", Description = "Enchant an item at an Enchanting Table", Icon = "enchanting_table" },
            new Def { Id = "monster_hunter", Title = "Monster Hunter", Description = "Kill a Minecraft monster", Icon = "item_iron_sword" },
            new Def { Id = "bff", Title = "Best Friends Forever", Description = "Tame an animal", Icon = "item_bone" },
            new Def { Id = "oh_shiny", Title = "Oh Shiny", Description = "Distract Piglins with gold", Icon = "item_gold_ingot" },
            new Def { Id = "elytra", Title = "Sky's the Limit", Description = "Glide with an elytra", Icon = "item_elytra" },
            new Def { Id = "postmortal", Title = "Postmortal", Description = "Use a Totem of Undying to cheat death", Icon = "item_totem_of_undying" },
            new Def { Id = "pumpkin", Title = "Who's Looking?", Description = "Wear a carved pumpkin", Icon = "carved_pumpkin" },
            // Lethal Company's
            new Def { Id = "interiors", Title = "Interior Designer", Description = "Go inside every kind of facility: the factory, the mansion and the mineshaft", Icon = "stone_bricks",
                      Needs = new[] { "interior:Level1Flow", "interior:Level2Flow", "interior:Level3Flow" } },
            new Def { Id = "moons", Title = "Tourist", Description = "Land on every moon", Icon = "grass",
                      Needs = new[] { "moon:0", "moon:1", "moon:2", "moon:4", "moon:5", "moon:6", "moon:7", "moon:8", "moon:9", "moon:10", "moon:12" } },
            new Def { Id = "company", Title = "Company Visit", Description = "Land at the Company building", Icon = "gold_block" },
            new Def { Id = "home", Title = "Made It Home", Description = "Be in the ship, alive, when it leaves a moon", Icon = "oak_door" },
        };
        static readonly Dictionary<string, Def> byId = All.ToDictionary(d => d.Id);
        public static Def Get(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

        // ------------------------------------------------------------------ this machine's player: noticing them
        static readonly HashSet<string> sent = new HashSet<string>(); // (told the server this session)
        static float nextLook;
        static bool wasLanded;

        /// <summary>This machine's player did something (an advancement's id, or a part like "kind:Bastion").</summary>
        public static void Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || !sent.Add(id)) return;
            BlockNet.RequestAdvancement(id);
        }

        public static void ResetSession() { sent.Clear(); wasLanded = false; }

        /// <summary>Local player, now and then: what they have, wear and where they are.</summary>
        public static void Tick(PlayerControllerB p)
        {
            if (p == null || Time.time < nextLook) return;
            nextLook = Time.time + 1f;
            var sor = StartOfRound.Instance;
            if (sor == null) return;
            // home: alive in the ship as it goes back up
            bool landed = sor.shipHasLanded;
            if (wasLanded && sor.shipIsLeaving && !p.isPlayerDead && p.isInHangarShipRoom) Grant("home");
            wasLanded = landed || (wasLanded && sor.shipIsLeaving);
            if (p.isPlayerDead || !p.isPlayerControlled) return;
            foreach (var g in p.ItemSlots)
            {
                var k = ItemData.Base(Crafting.KeyOf(g));
                if (k == null) continue;
                if (k == "cobblestone") Grant("stone_age");
                else if (k == "stone_pickaxe") Grant("upgrade");
                else if (k == "iron_ingot") Grant("hardware");
                else if (k == "iron_pickaxe") Grant("iron_pick");
                else if (k == "diamond" || k == "scrap_diamond_ore") Grant("diamonds");
                else if (k == "lava_bucket") Grant("hot_stuff");
                else if (k == "obsidian") Grant("obsidian");
                else if (k == BlazeRod.Key) Grant("blaze_rod");
                else if (Potions.IsPotion(k)) Grant("brewery");
            }
            foreach (var w in Armor.Local)
            {
                var k = ItemData.Base(w);
                if (k == null) continue;
                if (k.StartsWith("iron_")) Grant("suit_up");
                else if (k.StartsWith("diamond_")) Grant("diamond_armor");
                else if (k == "carved_pumpkin") Grant("pumpkin");
            }
            if (Elytra.Gliding) Grant("elytra");
            // where you are
            if (NetherFortress.Root != null && NetherFortress.Contains(p.transform.position))
            {
                Grant("nether");
                Grant("kind:" + NetherFortress.Kind);
                if (NetherFortress.Kind == NetherFortress.NetherKind.Fortress) Grant("fortress");
                if (NetherFortress.Kind == NetherFortress.NetherKind.Bastion) Grant("bastion");
            }
            else if (p.isInsideFactory && RoundManager.Instance != null)
            {
                var rm = RoundManager.Instance;
                if (rm.currentDungeonType >= 0 && rm.currentDungeonType < rm.dungeonFlowTypes.Length && rm.dungeonFlowTypes[rm.currentDungeonType].dungeonFlow != null)
                    Grant("interior:" + rm.dungeonFlowTypes[rm.currentDungeonType].dungeonFlow.name);
            }
            if (landed && !sor.inShipPhase && sor.currentLevel != null)
            {
                if (sor.currentLevel.levelID == 3) Grant("company");
                else Grant("moon:" + sor.currentLevel.levelID);
            }
        }

        // ------------------------------------------------------------------ the server: who has what
        const string SaveKey = "LMC_Advancements_v1";
        static Dictionary<string, HashSet<string>> done; // player identity -> ids and parts

        static Dictionary<string, HashSet<string>> Done
        {
            get
            {
                if (done != null) return done;
                done = new Dictionary<string, HashSet<string>>();
                try
                {
                    var file = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.currentSaveFileName : null;
                    if (file != null && ES3.KeyExists(SaveKey, file))
                        foreach (var line in ES3.Load<string>(SaveKey, file).Split('\n'))
                        {
                            int t = line.IndexOf('\t');
                            if (t > 0) done[line.Substring(0, t)] = new HashSet<string>(line.Substring(t + 1).Split(',').Where(x => x.Length > 0));
                        }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("advancements load: " + e.Message); }
                return done;
            }
        }

        public static void ResetServer() => done = null;

        /// <summary>(dev, tests) Un-make one for a player, so it can be made again.</summary>
        public static void DevForget(PlayerControllerB p, string id) { Of(p).Remove(id); sent.Remove(id); }

        public static void Save(string file)
        {
            if (done == null) return;
            ES3.Save(SaveKey, string.Join("\n", done.Select(kv => kv.Key + "\t" + string.Join(",", kv.Value))), file);
        }

        static HashSet<string> Of(PlayerControllerB p)
        {
            var id = Armor.IdentityOf(p);
            if (!Done.TryGetValue(id, out var set)) Done[id] = set = new HashSet<string>();
            return set;
        }

        /// <summary>Server: a player did something; new advancements are told to everyone.</summary>
        public static void ServerGrant(ulong sender, string id)
        {
            var p = ServerLogic.PlayerFor(sender);
            if (p == null || string.IsNullOrEmpty(id) || id.Length > 64) return;
            var set = Of(p);
            if (!set.Add(id)) return;
            var made = new List<string>();
            if (byId.ContainsKey(id)) made.Add(id);
            // parts: an advancement made of them, complete now
            foreach (var d in All)
                if (d.Needs != null && !set.Contains(d.Id) && d.Needs.All(set.Contains)) { set.Add(d.Id); made.Add(d.Id); }
            foreach (var m in made)
            {
                Plugin.Log.LogInfo($"[advancement] {p.playerUsername}: {m}");
                BlockNet.ServerAdvancement((int)p.playerClientId, m);
            }
        }

        /// <summary>Server: someone's list, for /advancements.</summary>
        public static string Describe(PlayerControllerB p)
        {
            var set = Of(p);
            var have = All.Where(d => set.Contains(d.Id)).ToList();
            string parts(Def d) => d.Needs == null || set.Contains(d.Id) ? "" : $" ({d.Needs.Count(set.Contains)}/{d.Needs.Length})";
            return $"{p.playerUsername}: {have.Count}/{All.Count} advancements. Made: {(have.Count > 0 ? string.Join(", ", have.Select(d => d.Title)) : "none yet")}. " +
                   $"To go: {string.Join(", ", All.Where(d => !set.Contains(d.Id)).Select(d => d.Title + parts(d)))}";
        }

        // ------------------------------------------------------------------ every machine: hearing of one
        public static void Announce(int playerIdx, string id)
        {
            var d = Get(id);
            var sor = StartOfRound.Instance;
            if (d == null || sor == null || playerIdx < 0 || playerIdx >= sor.allPlayerScripts.Length) return;
            var p = sor.allPlayerScripts[playerIdx];
            HUDManager.Instance?.AddChatMessage($"{p.playerUsername} has made the advancement <color=#55ff55>[{d.Title}]</color>");
            if (p == GameNetworkManager.Instance?.localPlayerController) Toast.Show(d);
        }

        /// <summary>Every frame (with the HUD): this machine's player, and the toast.</summary>
        [HarmonyLib.HarmonyPatch(typeof(HUDManager), "Update"), HarmonyLib.HarmonyPostfix]
        static void HudTick()
        {
            var p = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.localPlayerController : null;
            try { Tick(p); Toast.Tick(); } catch (System.Exception e) { Plugin.Log.LogWarning("advancements: " + e.Message); nextLook = Time.time + 10f; }
        }

        /// <summary>Minecraft's "Advancement Made!" box, top right, a few seconds.</summary>
        public static class Toast
        {
            static GameObject root; static TextMeshProUGUI title, text; static Image icon; static float until;
            static readonly Queue<Def> queue = new Queue<Def>();
            public static int Shown;

            public static void Show(Def d) { queue.Enqueue(d); Shown++; }

            public static void Tick()
            {
                if (root != null && root.activeSelf && Time.time > until) root.SetActive(false);
                if (queue.Count == 0 || (root != null && root.activeSelf)) return;
                var d = queue.Dequeue();
                Build();
                if (root == null) return;
                title.text = "Advancement Made!";
                text.text = d.Title;
                icon.sprite = Atlas.IconFor(d.Icon) ?? Atlas.IconFor("item_" + d.Icon);
                icon.enabled = icon.sprite != null;
                root.SetActive(true);
                until = Time.time + 5f;
                Sounds.Play2D("toast.challenge", 0.5f, 1f);
            }

            static void Build()
            {
                if (root != null || HUDManager.Instance == null) return;
                root = new GameObject("LMC_AdvancementToast", typeof(RectTransform));
                Object.DontDestroyOnLoad(root);
                var c = root.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 7;
                var box = new GameObject("box", typeof(RectTransform)); box.transform.SetParent(root.transform, false);
                var br = (RectTransform)box.transform;
                br.anchorMin = br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 1f);
                br.sizeDelta = new Vector2(400f, 80f); br.anchoredPosition = new Vector2(-20f, -20f);
                var bg = box.AddComponent<Image>(); bg.color = new Color(0.13f, 0.13f, 0.13f, 0.92f); bg.raycastTarget = false;
                var ig = new GameObject("icon", typeof(RectTransform)); ig.transform.SetParent(box.transform, false);
                var ir = (RectTransform)ig.transform; ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
                ir.sizeDelta = new Vector2(52f, 52f); ir.anchoredPosition = new Vector2(14f, 0f);
                icon = ig.AddComponent<Image>(); icon.raycastTarget = false; icon.preserveAspect = true;
                TextMeshProUGUI Line(string name, float y, Color col, float size)
                {
                    var g = new GameObject(name, typeof(RectTransform)); g.transform.SetParent(box.transform, false);
                    var r = (RectTransform)g.transform; r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
                    r.offsetMin = new Vector2(80f, y - 15f); r.offsetMax = new Vector2(-10f, y + 15f);
                    var t = g.AddComponent<TextMeshProUGUI>();
                    t.font = HUDManager.Instance.chatText.font; t.fontSize = size; t.color = col; t.raycastTarget = false;
                    t.alignment = TextAlignmentOptions.Left; t.enableWordWrapping = false;
                    return t;
                }
                title = Line("title", 14f, new Color(1f, 1f, 0.33f), 22f);
                text = Line("text", -14f, Color.white, 22f);
                root.SetActive(false);
            }
        }
    }
}
