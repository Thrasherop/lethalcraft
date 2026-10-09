using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LethalMinecraft
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("evaisa.lethallib", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(ModKeys.InputUtilsGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
#if OBFUSCATED
        // the one-off playtest build that doesn't say what it is (its own config file, too)
        public const string Guid = "crew.comforts";
        public const string Name = "CrewComforts";
#else
        public const string Guid = "thrasherop.lethalcraft";
        public const string Name = "LethalCraft";
        /// <summary>The id it had as LethalMinecraft (up to 1.4.8): its config file is carried over once.</summary>
        public const string OldGuid = "thrasherop.lethalminecraft";
#endif
        public const string Version = "1.5.1";

        public static Plugin Instance;
        public static ManualLogSource Log;
        public static string PluginDir;

        // ---- config
        public static ConfigEntry<float> BlockSize;
        public static ConfigEntry<string> MinecraftDir;
        public static ConfigEntry<string> MinecraftVersion;
        public static ConfigEntry<bool> UseMinecraftAssets;
        public static ConfigEntry<bool> EnemiesBreakBlocks;
        public static ConfigEntry<bool> BlocksBlockEnemyPaths;
        public static ConfigEntry<float> PriceMultiplier;
        public static ConfigEntry<bool> SpawnOres;
        public static ConfigEntry<bool> DevMode;
        public static ConfigEntry<int> DevPort;
        public static ConfigEntry<bool> DevAutoHost;
        public static ConfigEntry<bool> ExplosionsBreakBlocks;
        public static ConfigEntry<float> TorchBrightness;
        public static ConfigEntry<bool> HungerEnabled;
        public static ConfigEntry<float> HungerRate;
        public static ConfigEntry<bool> HardcoreStarvation;
        public static ConfigEntry<bool> BigInventory;
        public static ConfigEntry<bool> AutoPickup;
        public static ConfigEntry<bool> HideHudUntilBlockBroken;
        public static ConfigEntry<int> HotbarSlots;
        public static ConfigEntry<bool> MinecraftHud;
        public static ConfigEntry<bool> ShowArmorOnPlayers;
        public static ConfigEntry<bool> HideVanillaHealth;
        public static ConfigEntry<bool> PlaceWithLeftClick;
        public static ConfigEntry<bool> AllowDigging;
        public static ConfigEntry<bool> MinesDigGround;
        public static ConfigEntry<bool> DigAtCompany;
        public static ConfigEntry<string> CraftKey;
        public static ConfigEntry<bool> PearlsEnabled, PearlsSpawnInside, PearlsBuyable;
        public static ConfigEntry<int> PearlSpawnRarity, PearlPrice;
        public static ConfigEntry<bool> DiscsSpawnInside;
        public static ConfigEntry<int> DiscSpawnRarity;

        public static bool IsServerNow => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer;

        /// <summary>LethalMinecraft became LethalCraft (1.4.9) and its config file is named after the new id: the first time,
        /// start from the old one, so nobody's settings reset. (The old file stays, for going back.)</summary>
        void CarryOverOldConfig()
        {
#if !OBFUSCATED
            try
            {
                string now = Config.ConfigFilePath, old = Path.Combine(Path.GetDirectoryName(now), OldGuid + ".cfg");
                if (File.Exists(now) || !File.Exists(old)) return;
                File.Copy(old, now);
                Config.Reload();
                Logger.LogInfo($"Settings carried over from {OldGuid}.cfg (LethalMinecraft is LethalCraft now)");
            }
            catch (System.Exception e) { Logger.LogWarning("Couldn't carry over the old config: " + e.Message); }
#endif
        }

        void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);
            CarryOverOldConfig();

            BlockSize = Config.Bind("Blocks", "BlockSize", 1.4f, "Edge length of one block in Lethal Company units. 1.4 matches Minecraft proportions (the player is ~1.8 blocks tall and can jump exactly one block).");
            BlocksBlockEnemyPaths = Config.Bind("Blocks", "BlocksBlockEnemyPaths", true, "Placed blocks carve the navmesh so monsters path around them (lets you build barricades).");
            EnemiesBreakBlocks = Config.Bind("Blocks", "EnemiesBreakBlocks", true, "Monsters that get stuck on your blocks slowly break through them.");
            ExplosionsBreakBlocks = Config.Bind("Blocks", "ExplosionsBreakBlocks", true, "Landmines, Old Bird missiles and other explosions destroy nearby blocks.");
            TorchBrightness = Config.Bind("Blocks", "LightBrightness", 1.0f, "Multiplier for torch / glowstone / lamp brightness.");
            HungerEnabled = Config.Bind("Survival", "Hunger", true, "Minecraft hunger: sprinting, jumping and mining make you hungry. Full hunger regenerates health; empty hunger starves you and stops sprinting.");
            HungerRate = Config.Bind("Survival", "HungerRate", 0.5f, "How fast hunger drains relative to Minecraft (1.0 = vanilla Minecraft rate).");
            HardcoreStarvation = Config.Bind("Survival", "StarvationCanKill", false, "If true, starving can kill you (Minecraft Hard difficulty). Otherwise it stops at half a heart.");
#if OBFUSCATED
            const bool hideHudDefault = true;
#else
            const bool hideHudDefault = false;
#endif
            HideHudUntilBlockBroken = Config.Bind("HUD", "HideHudUntilBlockBroken", hideHudDefault, "Keep the Minecraft HUD (hotbar, hearts, hunger, XP) hidden until someone breaks a block; then it appears for everyone. For surprising players who don't know what mod they're playing.");
            AutoPickup = Config.Bind("Controls", "AutoPickupItems", false, "Walk over a Minecraft item (blocks, tools, materials) to pick it up into an empty hotbar slot, like Minecraft (items matching a stack you carry always top it up). Not loot (ore, pearls, slimeballs): those stay a deliberate [E]. Your own choice; each player sets it.");
            BigInventory = Config.Bind("HUD", "BigInventory", false, "Host setting: Minecraft's 3x9 storage grid in the [I] inventory, on top of the hotbar (Minecraft items only; what's stored weighs as much as in the hotbar and drops where you die). Off by default: it's a lot of extra carrying.");
            HotbarSlots = Config.Bind("HUD", "HotbarSlots", 9, new ConfigDescription("Inventory slots (Minecraft hotbar). Vanilla Lethal Company has 4.", new AcceptableValueRange<int>(4, 9)));
            MinecraftHud = Config.Bind("HUD", "MinecraftHud", true, "Show the Minecraft hotbar, hearts, hunger and XP bar.");
            ShowArmorOnPlayers = Config.Bind("HUD", "ShowArmorOnPlayers", true, "Draw the armor players wear on their models (yours in the [I] inventory). Off: armor is hidden visually; it still protects. Your own choice; each player sets it.");
            CraftKey = Config.Bind("Controls", "PocketCraftingKey", "i", "Keyboard key that opens pocket crafting (2x2 recipes). A Crafting Table ([E]) shows every recipe.");
            PearlsEnabled = Config.Bind("Ender Pearls", "Enabled", true, "Ender pearls exist at all. Throw one with [Right-click]: you teleport to where it lands and take 2.5 hearts of damage. Set to false for no pearls.");
            PearlsSpawnInside = Config.Bind("Ender Pearls", "SpawnInsideFacility", true, "Ender pearls can be found inside facilities as scrap (and sold like scrap).");
            DiscsSpawnInside = Config.Bind("Music Discs", "SpawnInsideFacility", true, "Music discs (for the jukebox) can be found inside facilities as scrap.");
            DiscSpawnRarity = Config.Bind("Music Discs", "SpawnRarity", 1, new ConfigDescription("How often each of the 21 discs spawns inside, as a scrap rarity weight (an ender pearl's is 15).", new AcceptableValueRange<int>(1, 100)));
            PearlSpawnRarity = Config.Bind("Ender Pearls", "SpawnRarity", 15, new ConfigDescription("How often pearls spawn inside, as a scrap rarity weight (vanilla scrap uses roughly 1-100; higher = more common).", new AcceptableValueRange<int>(1, 100)));
            PearlsBuyable = Config.Bind("Ender Pearls", "Buyable", false, "Ender pearls can be bought from the terminal store.");
            PearlPrice = Config.Bind("Ender Pearls", "Price", 40, new ConfigDescription("Store price of one ender pearl (when Buyable).", new AcceptableValueRange<int>(1, 2000)));
            MinesDigGround = Config.Bind("Blocks", "LandminesBreakGround", false, "If true, landmines, Old Bird missiles and other non-TNT explosions also blast craters into the moon ground and facility floors (like TNT does). Off by default because it can open holes in facility floors.");
            AllowDigging = Config.Bind("Blocks", "AllowDiggingTerrain", true, "Left-click moon ground, rocks and facility floors/walls/ceilings to dig real holes and tunnels (down to bedrock at the bottom of the world).");
            DigAtCompany = Config.Bind("Blocks", "AllowDiggingAtCompany", false, "Also allow digging on Gordion (the Company building moon). Off by default so the sell counter area stays intact.");
            PlaceWithLeftClick = Config.Bind("Controls", "PlaceWithLeftClick", false, "false = Minecraft controls (left-click breaks, right-click places/eats while holding blocks or food; right-click still scans otherwise). true = left-click places/eats.");
            HideVanillaHealth = Config.Bind("HUD", "HideVanillaHealthIndicator", false, "Hide Lethal Company's body-silhouette health indicator (hearts replace it).");
            SpawnOres = Config.Bind("Mining", "SpawnOreVeins", true, "Ore veins (coal, iron, gold, diamond, emerald) spawn inside facilities. Mine them with a pickaxe for sellable scrap.");
            PriceMultiplier = Config.Bind("Store", "PriceMultiplier", 1.0f, "Multiplier for all store prices.");
            UseMinecraftAssets = Config.Bind("Assets", "UseLocalMinecraftAssets", true, "Load block textures and sounds from your own local Minecraft installation (nothing is redistributed). Falls back to built-in generated art.");
            MinecraftDir = Config.Bind("Assets", "MinecraftDirectory", "", "Where to read Minecraft textures/sounds from, if your install is not in the default place.\nLeave empty to auto-detect (%APPDATA%\\.minecraft, CurseForge, Prism, Modrinth, TLauncher, GDLauncher, ATLauncher).\nAccepts: your .minecraft folder, a launcher or instance folder, a versions folder, or a client .jar file.\nQuotes, forward slashes, ~ and %ENVIRONMENT% variables are fine. Examples:\n  D:\\Games\\.minecraft\n  %APPDATA%\\PrismLauncher\n  C:/Users/me/curseforge/minecraft/Install\n  D:\\mc\\versions\\1.21.1\\1.21.1.jar\nCheck BepInEx/LogOutput.log for the line \"Minecraft assets:\" to see what was found.");
            MinecraftVersion = Config.Bind("Assets", "MinecraftVersion", "", "Use a specific installed version (e.g. 1.20.1). Empty = newest release found. Must have been launched at least once.");
            DevMode = Config.Bind("Debug", "DevMode", false, "Enables a localhost command server + debug keys for testing. Leave off.");
            DevPort = Config.Bind("Debug", "DevPort", 28771, "Port for the dev command server.");
            DevAutoHost = Config.Bind("Debug", "AutoHost", false, "Dev: skip menus and host save file 3 automatically.");

            Blocks.Init();
            Balance.Init(Config); // (prices, ore values and spawning: before the items are made)
            ModKeys.Init();
            McAssets.Init();
            Atlas.Build();
            Sounds.Init();
            Crafting.Init();
            ModItems.Register();

            var harmony = new Harmony(Guid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            if (DevMode.Value || CmdArg("-lmc-mode") != null)
            {
                var go = new GameObject("LMC_Dev");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<DevServer>();
            }
            Log.LogInfo($"{Name} {Version} loaded. Minecraft assets: {(McAssets.Available ? McAssets.SourceDescription : "not found (using built-in art)")}");
        }

        public static float S => BlockSize.Value;

        // dev: per-instance overrides from the command line (two-instance multiplayer testing)
        public static string CmdArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
        public static string DevLaunchMode => CmdArg("-lmc-mode") ?? (DevAutoHost.Value ? "host" : null);
        public static int DevPortEffective => int.TryParse(CmdArg("-lmc-port"), out var p) ? p : DevPort.Value;
    }
}
