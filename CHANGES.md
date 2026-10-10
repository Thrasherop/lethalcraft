# LethalCraft: changes by day

A day-by-day log of development: releases, new features, bugs fixed, investigations, and what's waiting on the owner.
Newest day first. Times are local (MDT). Issue numbers are on https://github.com/Thrasherop/lethalcraft/issues.
Player-facing details for each version are in `dist/package/CHANGELOG.md`.

Builds: every push to `main` makes a GitHub release `v<version>-build.<n>` (CI build number n), since build 1 on
2026-10-07. Before that, releases were hand-made zips (USB copies).

## 2026-10-10

**Versions and builds:** 1.8.1 (build 28, 00:50), 1.9.0 (29, 05:46), 1.10.0 (30, 06:43), 1.10.1 (31, 07:16),
1.11.0 (32, 10:14), 1.12.0 (33, 12:37), 1.13.0 (34, afternoon). Each was played in game and passed the one-moon suite (all moons for
1.9.0, which changed the ground and liquids) before going out.

### New features
- **Farming** (#60, 1.12.0): hoes, farmland (on blocks and the moon's own ground), wheat, carrots, potatoes and sugar
  cane; bread, paper, sugar, baked potatoes. Crops need water within 4 blocks, as the owner asked after seeing dry
  wheat grow on the ship; dry, empty farmland turns back into dirt. Crops keep growing on the ship in orbit. Cheap in
  the store, and a few piles worth nothing inside each facility.
- **Elytra and firework rockets** (#61, 1.12.0): Minecraft's glide physics, rockets (loud), wall hits and dives hurt,
  wears out after about 7 minutes, wings drawn on everyone's back. Store: elytra 1000, rockets 16 for 120; rockets
  also crafted.
- **Company boss bar** (#63, 1.12.0): stays full by default, per the owner (a filling bar read as hurting the
  Company); `[HUD] CompanyBossBarQuota` fills it with the quota instead.
- **Zombies** (#59, 1.13.0): slow, arms out, rotten flesh; the wither skeleton and the zombie now share one Minecraft
  mob base.
- **Nether portals** (#66 phase 1, on the Nether branch, not released yet): the owner asked for the Nether next.
- **Wither skeletons** (#59), **the Minecraft bee** (#84), **about four times more ore** per block dug (#71) (1.11.0).
- **Furnace screen** (#83), **spawn eggs** (#85), **every item in the creative menu** (#86), **hardcore** (#87)
  (1.10.0).
- **Lava flows, lava buckets, stacking buckets, water in the ship, infinite water on natural ground** (#74, #75, #79),
  modded moons dig (#57, #73) (1.9.0).
- **/unstuck** (#64), the creative menu scrolls (#65), more creepers (#90) (1.8.1).

### Bugs fixed
- Clients with some modpacks had no blocks or hotbar (#72, 1.10.1): another mod's error stopped LethalCraft starting
  on the client. Reproduced with the owner's modpack as a LAN client, then fixed.
- Ship loot vanishing after a reload (#69, 1.8.1 and 1.9.0), the totem against the Earth Leviathan (#76), door hinges
  and double drops (#77, #82), bucket sounds (#80), the pickaxe staying hidden after a ladder (#81, 1.10.0), liquids
  flowing into solid ground and riding the ship into orbit (1.9.0, 1.11.0).

### Investigated, not reproduced
- #70 (inventory gone wrong as a client), #78 (items through the Company pad), #91 (honey and pistons on Dine): tried
  in game, details on the issues. #57 (modded moons) closed: its fixes shipped in 1.9.0.

### Waiting on the owner
- #61's open choices: the elytra's price (1000 for now), rockets per stack (16 for 120), whether a worn-out elytra can
  be repaired (not yet: it's bought again).
- Brewing (#88) waits on the Nether's ingredients. The Nether (#66) is under way since the owner asked for it
  (~13:00): portals first, then the fortress interior.

## 2026-10-09

**Versions and builds:** 1.4.9 (build 16, 03:39), 1.4.10 (17, 05:28), 1.5.0 (18, 09:21), 1.5.1 (19, 10:26),
1.6.0 (20, 12:47), 1.7.0 (21, 13:40), 1.7.1 (22, 13:58; 23, 15:36, tooling only; 24, 17:48, this file),
**1.8.0 (build 25, ~18:05): tonight's playtest build, with creepers.** Each was played in-game and passed the
regression suite before going out.

### New features
- **Redstone comparators** (#23, 1.7.0): compare and subtract modes, real signal strengths through dust and blocks;
  they read how full a chest, furnace or jukebox is. This finished #23 (more Minecraft blocks).
- **Water and buckets** (#19, 1.6.0): flowing water, buckets, lava to obsidian, no fall damage into water, drowning.
  Swimming is **off by default** (`[Water] Swimming`), per the owner: water stays a hazard.
- **Honey blocks** (#23, 1.6.0): sticky for pistons but not to slime (flying machines), slow to walk on, soften falls.
- **Jukebox and music discs** (#31, 1.5.1): 21 discs found inside as scrap, played with Minecraft's tracks.
- **Redstone repeaters** (#23) and **enchantment glint** (#46) (1.5.1).
- **Enchanting** (#46: lapis ore, the table, five enchantments), **armor shows on players' models** (#20, with
  `[HUD] ShowArmorOnPlayers` to hide it), **slabs and trapdoors** (#23) (1.5.0).
- **Stairs** you walk straight up and **diorite** (#50), **doors** (#54), **ladders** (#30), **glass panes** (#49),
  **tool durability** (#48), **critical hits** (#1), **redstone ore** (#47), **lava pockets** deep down (#32), items
  left on ship-attached blocks stay between moons (#55) (1.4.10).
- **Totem of Undying** (#53), **[Q] throws one** of a stack (#52), **chat commands** /gamemode /tp /give /op
  /keepInventory (#45), **starting supplies** for new crews (#33), **ore balance** with real veins and more ore on
  harder moons (#42, #43, #44). The mod is now called **LethalCraft** (1.4.9).
- **Thunderstore publishing from CI** (#58, 1.7.1): ready; waits on the account and token.

### Bugs fixed
- From last night's playtest (1.4.9): blood trail after healing (#41), floor stains through blocks (#40), ghost
  blocks for one player (#36), dying at takeoff on ship blocks (#37), store orders delivering the wrong item (#39),
  diamonds vanishing in the crafting grid (#34), can't jump when hungry (#35), keepInventory for non-hosts.
- Totem: the burst and sound play at both ends (where you'd have died, and in the ship), for everyone (1.5.0).
- A worn tool keeps its wear through chests and the inventory (#56, 1.5.0); a stack of ore shows its total value (#51).
- Explosions breaking many blocks no longer lag the host (#18, closed after the owner's playtest).

### Investigated (not reproduced, or waiting)
- **#38 keybinds locking movement:** your answers narrowed it to the saved keybindings (only "Reset all to default"
  cleared it). Found that InputUtils keeps mod keybinds in two places (global and local), and Reset only empties the
  local one. **Paused** until it happens again; the issue lists which files to grab.
- **#29 phantom blocks in mid-air:** a fix works on the three known spots and passed six fixed map seeds, but the
  all-moon run then showed a failure digging a shaft on Vow that came and went (2 failures in 6 runs). **Held back**
  from tonight's build until that's understood.
- **#25 monsters losing the navmesh:** the game log now records where and why when it happens (1.7.1).
- **#14 chest broken at takeoff:** closed for now (9 tries, host and client, never reproduced).
- **#57 moon mods:** the code side is checked (nothing tied to vanilla moons; five likely trouble spots on the
  issue). The live test needs a permission from you (below).

### Creepers (1.8.0, tonight)
- **Creepers** (#59): Minecraft's creeper, built in code with your own Minecraft texture. It spawns **inside** the
  facility (and now and then outside), walks up, and within 2.4 m stops, hisses and swells for 1.5 s, then explodes
  like TNT. Get 7 m away and it gives up, but the swell only drains back slowly. Three hits kill it (two with a diamond
  sword) and it drops **gunpowder**; TNT now has Minecraft's recipe (gunpowder + sand). **No Minecraft mob spawns
  within 8 blocks of a placed torch** or other light, inside or out.
- Tested by hand: the approach, fuse, explosion, walking away, the torch rule, spawning inside, sword kills with the
  gunpowder drop. Its regression test passed twice at one spot, then failed at another: naturally spawned creepers
  joined in and one sat still with a target. **If creepers misbehave tonight, `[Mobs] Creepers = false` turns them
  off** (everyone needs the same setting).

### Tooling
- Tests for comparators and creepers; test spots no longer run out on small moons; the Dine tree check; the keybind
  test backs up and restores your keybinds; dev commands log their stack traces.

### Waiting on you
- **#58:** make the Thunderstore team and add the `TCLI_AUTH_TOKEN` secret (docs/THUNDERSTORE.md).
- **#57:** installing the downloaded moon mods into the test game was blocked by Claude Code's permission check
  (running third-party code). Allow it in your Claude Code permission settings, or install LethalLevelLoader 1.7.13,
  Orion and SCP Foundation Dungeon on the test machine yourself.
- **Backlog filed today:** farming (#60), elytra and rockets (#61), Nether brainstorm (#62), Company boss bar (#63),
  wither skeletons (on #59), creepers (#59).

## 2026-10-08

**Versions and builds:** 1.4.2 (build 3, 01:17), 1.4.3 (4 and 5, 02:07-02:21), 1.4.4 (6-9, 07:18-07:47), 1.4.5
(10-12, 08:26-12:09), 1.4.6 (13, 14:47), 1.4.7 (14, 16:04), 1.4.8 (15, 17:44). Evening playtest on 1.4.8.

### New features
- **Creative menu: a Lethal Company tab** (store items, shotguns, shells, knives, keys, flashbangs) (1.4.5);
  /gamemode with [Tab] completion.
- **Big inventory** (optional, off by default): Minecraft's 3x9 storage grid in [I] (1.4.6).
- **Middle-click picks the block** you're looking at in creative (#22); **auto-pickup** of Minecraft items you walk over
  (optional, #24); **metal armor draws lightning** in storms (#21); **slimeballs** found inside; balance settings;
  the HUD can stay hidden until a block breaks; store prices come from the host (1.4.7-1.4.8).
- The GitHub issues became the tracker; CLAUDE.md rules: reproduce every bug in-game first, work in branches and
  batches.

### Bugs fixed
- Pickups that can't get stuck, stack top-ups, no slot switching, no weight leak (1.4.2).
- Raw iron worth a fixed $10 and stacking; no ore at the Company; observer textures for every facing; the inventory
  character turns the right way (1.4.3).
- Fire on slopes (#26); digging at a lake's shore no longer cuts its surface (#27) (1.4.4).
- **Explosion lag (#18):** ground classification without allocations, cheaper rays, a bigger cache, cheaper stack
  merging. Measured and confirmed in the owner's playtest.
- From the evening playtest (fixed overnight into 1.4.9): hungry players can always jump (#35); ores in the crafting
  grid and chests no longer vanish (#34).

### Investigated
- **#14** (chest broken during takeoff): not reproduced as host or client. **#25** (navmesh errors): repro tool,
  not reproduced. **#29** (phantom ground at three nodes): cause understood, written up.

### Tooling
- Tests run the game up to 8x faster (`--speed`): the 12-moon run went from 1 h 42 to about 20 minutes.
- Launch scripts keep the Steam friends status offline; fixed map seeds and classification dumps to compare builds.

## 2026-10-07

**Versions and builds:** 1.4.0 (18:56, hand-made zip), 1.4.1 (19:25; CI builds 1 and 2 at 21:52). The first CI
release: every push to main now builds and publishes the Thunderstore zip.

### New features
- **Creative mode** (/gamemode, host only), saved per player with the ship.
- **Flying machines**: Minecraft Java's piston and observer mechanics; items and players ride blocks a piston moves,
  and standing on blocks attached to the ship travels with it.
- **Fire** (flint and steel), animated like Minecraft's: spreads, burns wood, lights TNT, hurts.
- **Armor** (iron, gold, diamond; crafted), four armor slots in [I] with your character beside them.
- **Swords** (wood to diamond; crafted only), Minecraft damage scaled to Lethal Company.
- **Trees**: chop a moon's trees down with an axe or by hand; they shatter like the cruiser hits them.
- **Store policy**: no tiered tools or weapons in the store (craft them); expensive ore blocks; a block of coal.
- **Storms**: lightning goes for metal items (tools, ingots, redstone).
- **Pillaring** and digging straight down at the camera's look limit; furnace output goes into your hands.

### Bugs fixed
- TNT filled open air with blocks on the Company platform; phantom ground (open air reading as ground) wherever
  monsters walk; tunnels left a solid-looking wall you could walk into; clicks in a crafting or chest screen acted in
  the world after it closed; "redstone block" ordered the right item; scrap ender pearls merged and lost value;
  mid-round quitting duplicated ship-chest items; torchlight blew out in tight spaces; the redstone lamp now flashes
  long enough to see; swings hit every live monster in the arc (dead ones no longer soak them up).

### Tooling
- Play-testing with real input (camera aiming, routes to AI nodes); integrity, gap and ghost checks after digging;
  the release packaging script.

## 2026-10-06

**Version:** 1.3.0 work in progress (the repository starts here, at 22:19).

- Natural blocks draw only what digging exposes, and every cell near a surface is molded to the ground's shape.
- The crafting screen freezes the character; a common base for Minecraft-style inventory screens.
- **Chests**; **sticky slime blocks and observers**; pistons move whole structures.
- An integrity check for carved level objects, with a regression test.
