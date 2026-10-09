# LethalCraft

*(Formerly LethalMinecraft. Coming from it: remove the old LethalMinecraft package; your settings carry over by
themselves.)*

Minecraft, inside Lethal Company. Buy blocks from the Company store, build forts on the moon or a house on your
ship, wire up redstone, light the facility with torches, mine ore veins for scrap, and keep your hunger up.

Everything is networked (host-authoritative), so the whole crew builds in the same world, and late joiners
receive everything that's already built.

## Textures and sounds come from **your** Minecraft

The mod ships **no Minecraft assets**. At startup it reads the block textures, item sprites, HUD sprites,
font and sounds straight from your own local Minecraft Java installation (`%APPDATA%\.minecraft`, the newest
installed release, configurable). Without Minecraft installed, it falls back to built-in, procedurally
generated pixel art and synthesized sounds, so it still works.

**Custom install location?** Open `BepInEx/config/thrasherop.lethalcraft.cfg` and set `[Assets] MinecraftDirectory`
to your `.minecraft` folder, a launcher/instance folder (CurseForge, Prism, Modrinth...) or a client `.jar`. Quotes,
`/` or `\`, `~` and `%APPDATA%`-style variables all work. Common launchers are auto-detected when it's left empty.
The version you use must have been launched at least once. `BepInEx/LogOutput.log` shows what was found
(`Minecraft assets: ...`) or warns if the path was wrong.

## Minecraft HUD and survival

- **9-slot hotbar** with item icons, stack counts, and battery shown as a durability bar.
  Keys **1–9** select slots (Shift+1/2 still emote). The scroll wheel works as usual.
- **Hearts**: Lethal Company's 100 HP is shown as 10 hearts, with damage blinking and low-health shake.
- **Hunger and saturation**: sprinting, jumping, mining and fighting make you hungry. A full bar regenerates
  health. An empty bar starves you (down to half a heart; configurable to lethal), and at 3 drumsticks or
  fewer you can't sprint.
- **Food** from the store: bread, steak, apple, cookie, cooked porkchop, and the golden apple
  (Regeneration plus 2 golden absorption hearts).
- **XP bar and levels**: earned by mining ore, killing monsters and finding scrap. XP is saved per player.
- Minecraft crosshair, block outline, crack animation, and break particles.

## Controls (Minecraft-style)

| Action | Input |
|---|---|
| Break block (hands are slow; the right tool is fast) | hold **Left-click** |
| Place block / eat food (while holding a stack) | **Right-click** (scanning still works with anything else in hand) |
| Flip lever / press button / tune note block | **E** |
| Light a fire / TNT | Right-click the ground, a block or TNT with **Flint and Steel**; punch fire to put it out |
| Pocket crafting (2x2 recipes) | **I** |
| Crafting table / furnace / chest | **E** on the block |
| Select hotbar slot | **1–9** |
| Throw an ender pearl | **Right-click** while holding it |
| Throw one item from the stack in hand (drop a tool) | **Q** |
| Pick block (creative) | **Middle-click** |
| Pillar up | look all the way down, jump, and right-click (hold it) while in the air |

**Rebind keys in-game**: the mod's keys (pocket crafting, hotbar slots) appear in the game's
**Settings > Change keybinds** menu (via LethalCompanyInputUtils, installed automatically as a dependency).
Placing, breaking and E use the game's own bindings, so rebinding those in the game's menu works too.
Prefer left-click to place? Set `Controls.PlaceWithLeftClick = true`. Want to change any setting in-game?
Install **LethalConfig**: it lists every option of this mod in an in-game menu.

## Blocks and items (terminal → type `MINECRAFT`)

Buy with the item's name, spaces and all: `buy oak log 3`, `buy redstone block`, `buy block of iron` (or just the
start of it: `buy redstone to`). The store sells raw materials, light, redstone and utility blocks; tools, weapons and
armor are crafted, never bought (ordering one, like `buy stone pickaxe`, tells you how to get it instead).

- **Building**: grass, dirt, stone, cobblestone, diorite, oak/dark planks, logs (orientable), glass, glass panes
  (they join up with the walls and panes beside them), sand and gravel (they fall), bricks, stone bricks, obsidian
  (blast-proof, unpushable), leaves, wool ×4, bookshelf, ice,
  and blocks of iron (400 credits), diamond (750), coal (150) and gold: expensive on purpose, they craft back into
  nine ingots / diamonds / coal for good gear.
- **Stairs** (oak, cobblestone, stone brick): walk straight up them, no jumping. **Slabs** (oak, cobblestone, stone
  brick, stone): top or bottom half by where you click, two make a full block. **Oak doors**: two blocks tall, **E**
  opens and shuts them, monsters can't walk through a shut one. **Oak trapdoors**: a hatch, **E** opens it.
  **Ladders**: put them on a wall and press **E** to climb, like the game's own ladders (with a hand free).
- **Light**: torches (floor and wall), glowstone, jack o'lanterns, redstone lamps. Each is a real dynamic
  light, so they actually light up the facility.
- **Redstone**: dust (15-block falloff, connects up and down steps), repeaters (one way, full strength, a 1-4 tick
  delay set with **E**), levers, buttons, pressure plates
  (players *and monsters* trigger them), redstone torches (inverters, with burnout when used as a fast
  clock), blocks of redstone, pistons and sticky pistons (push up to 12 blocks, pull with sticky, carry
  players), observers (watch the block in front of their face and pulse out of their back when it changes),
  TNT (chain reactions), note blocks (instrument depends on the block below), and slime blocks (bounce, no fall
  damage, sneak to cancel; pistons move them together with every block stuck to them, like Minecraft's flying
  machines). Snow and stone appear when digging.
- **Tools**: pickaxes, shovels and axes in wood, stone, iron and diamond, plus flint & steel. Breaking uses
  Minecraft's rules: each block has a hardness and a right tool, and stone/ores only drop when mined with a good
  enough pickaxe (stone and coal need wood+, iron ore needs stone+, gold/diamond/emerald need iron+, obsidian needs
  diamond). Mined stone drops cobblestone. All tools are crafted: buy oak logs, make a wooden pickaxe and work your
  way up. A tool swings at monsters about as often as the shovel (once every 0.8 s) and hits as hard.
  Tools wear out like Minecraft's (wood 59 uses, stone 131, iron 250, diamond 1561: a use per block, one or two per
  hit), shown as a bar on the hotbar slot, and break when worn out (`[Balance] ToolDurability`).
  **Critical hits**: swing while falling (jump, hit on the way down) for half as much damage again.
- **Swords** (crafted only: two of the material over a stick): wooden, stone, iron, golden and diamond. One swing
  every 0.7 s; a wooden sword is about as deadly as the shovel, a diamond one kills a baboon hawk in three swings
  instead of four.
- **Armor** (crafted only, Minecraft's shapes): helmet, chestplate, leggings and boots in gold, iron and diamond. Wear
  it in the four armor slots of the **I** inventory (or right-click a piece in your hand). It turns damage down the
  way Minecraft's armor does (full diamond takes most of a monster's bite off); falls and drowning go straight
  through, and the monsters that just kill you still do. Your armor shows as a row above the hearts and on your
  character for everyone to see, drops where you die, and stays on between days (saved with the ship).
- **Enchanting**: mine rare **lapis lazuli** deep down (20+ blocks, a stone pickaxe) and craft an **enchanting table**
  (a bookshelf over 2 diamonds and 4 obsidian). **E** on it opens Minecraft's screen: a tool, sword or armor piece and
  some lapis in, then one of three offers (1-3 lapis and as many levels; up to level 30 with 15 bookshelves around the
  table, two blocks out). Efficiency, Unbreaking, Sharpness, Protection and Feather Falling. Enchantments stay with
  the item wherever it goes, and enchanted items shimmer purple.
- **Water** and **buckets** (3 iron ingots, or the store): fill a bucket from a water source or the moon's own
  rivers and lakes, pour it anywhere. Water flows down and out a few blocks like Minecraft's, washes away torches and
  redstone, puts out fire and turns lava to obsidian. Swim in it (**Space** to rise), land in it to take no fall
  damage; under it, the game's oxygen runs out.
- **Honey blocks**: sticky for pistons (but not to slime), slow to walk on, they soften a fall.
- **Jukebox** (8 planks around a diamond) and **music discs** (all 21 of Minecraft's, found inside facilities as rare
  scrap, never sold): **E** with a disc plays its track from the jukebox, heard by everyone nearby and by monsters;
  **E** again takes it out. `[Music Discs]` sets how often they turn up.
- **Trees** on the moons can be chopped down: hold left-click on a trunk (about 9 s by hand, 4.5 s with a wooden
  axe, about a second with a diamond one). It shatters like when the cruiser hits it and drops 4-6 oak logs.
- **Lightning** goes for metal: iron and gold tools, armor, ingots and blocks, flint & steel, redstone and pistons
  draw the storm like the game's own metal scrap (also things made or bought after the storm started).
- **Chests** (buy one, or craft from 8 planks): **E** opens 27 slots, with the same mouse controls as crafting.
  Chests in the ship keep their contents between days (saved with the ship, in orbit: quitting mid-day rolls them
  back with everything else); a broken chest drops everything inside.

## Crafting & smelting

- **Crafting Table** (buy it, or craft from 4 planks): **E** opens a real 3x3 crafting grid (your character stands
  still while a crafting or chest screen is open). Lay out the Minecraft
  recipe pattern (pickaxe = 3 on top + 2 sticks down the middle, and so on; shaped recipes work anywhere in the grid
  and mirrored) and take the result from the output slot; it goes into your hotbar. No recipe book: you know these.
  **I** opens your inventory, like Minecraft's: your four armor slots, your character (it turns to follow the mouse)
  and the 2x2 pocket grid for small recipes (planks, sticks, crafting table, torches...).
  Mouse works like Minecraft: left-click picks up / places a whole stack, right-click takes half / places one,
  shift-click a hotbar stack to move it into the grid, shift-click the output to craft as many as you can, click
  outside the window to throw the held stack. Closing the screen puts what's left in the grid back into your hotbar
  (topping up stacks first); whatever doesn't fit drops at your feet, like in Minecraft.
- **Furnace** (buy it, or craft from 8 cobblestone): hold ore/sand/cobblestone/logs and press **E** to load it, hold
  coal/planks/logs and press **E** to fuel it, press **E** empty-handed to take the result (it goes into your hotbar). Raw iron / raw gold
  become ingots (or sell them as scrap instead: your choice).
- Recipes include wood/stone/iron/diamond tools, swords, armor, torches, chests, stairs, slabs, doors, trapdoors,
  ladders, glass panes, the enchanting table, iron/gold/diamond/coal blocks (and
  back into nine ingots, diamonds or coal), stone bricks, levers,
  buttons, pressure plates, pistons, sticky pistons, observers (iron stands in for quartz), redstone lamps, note
  blocks, flint & steel and golden apples.
- Coal and iron also turn up as ore pockets when you dig deep into the ground.

## Creative mode

For demos and building. The host (or an operator) opens chat and types `/gamemode creative` (the game's chat key is `/`
itself, so plain `gamemode creative` works too); `/gamemode creative <player>` sets someone else, `@a` everyone, and
`/gamemode survival` switches back. In creative, like Minecraft:

- Blocks never run out, and break with one click (nothing drops; a chest still spills what's inside).
- **I** opens the creative menu instead of pocket crafting: every item, in tabs. Left-click an item for a full stack
  on the mouse, right-click for one, shift-click to put a full stack straight into the hotbar; click the item grid
  while holding something to put it away (delete it). Items from the menu are real items and stay if you go back to
  survival.
- Double-tap jump to fly: hold jump to rise, crouch to sink, sprint to fly faster; touching down ends flight.
- No damage and no hunger (the hearts, hunger and XP bars are hidden). Falling out of the world still counts.

## Chat commands

Typed in chat (the game's chat key is `/` itself, so the slash is optional). **[Tab]** completes commands, player
names and item names. The host can use them, and anyone the host makes an operator with `/op` (kept with the save).

| Command | Does |
|---|---|
| `/gamemode <creative\|survival> [player\|@a]` | Creative or survival (see above) |
| `/tp <player> [player\|ship]` | `tp megg`: you to Megg; `tp thra megg`: Thra to Megg; `tp thra ship`: back to the ship |
| `/give <player> <item> [count]` | Minecraft items by name (`cobblestone`, `diamond_sword`...) and the game's own (`flashlight`) |
| `/op <player>`, `/deop <player>` | Host only: who else may use commands |
| `/keepInventory <true\|false>` | Dying keeps what you carry: it comes back into your hotbar once you're revived (also `/gamerule keepInventory`) |

## Lethal Company integration

- **Starting supplies**: the ship has a supply chest by the terminal; each player's share (48 steak, 32 oak planks by
  default, `[Starter]`) goes in the first time they're aboard on a save. A team wipe or getting fired starts it over.
- **Totem of Undying** (store): anywhere in your hotbar when you would die, it's used up and you're back in the ship
  at full health.

- **Ore veins** (coal, iron, gold, diamond, emerald) spawn inside facilities. Mine them for scrap you can sell.
- **Monsters** path around your walls (the navmesh is carved). A monster that wants a player behind a wall
  **chews through it**. Cobblestone buys you time and obsidian buys you a lot.
- **Dig anywhere**: hold left-click on moon terrain, hillsides, rocks, or the facility's floors, walls and ceilings
  to dig real block-sized holes and tunnels. Tunnel from one facility room into the next, or dig straight down from
  the surface all the way to the facility far below (a long way: bring blocks to build stairs, the fall is deadly).
  Only the exact cube you mine is cut out of the level's meshes; the hole's walls become real blocks (top layer,
  dirt, then stone with ore veins: iron in veins of 2-9, redstone 15+ blocks down, diamonds only 30+ blocks below
  the surface, more ore on harder moons; **lava pockets** 20+ blocks down burn you fast (place a block in lava to fill
  it); how much is set in `[Ore Spawning]` as what half a moon of mining yields), and blocks at a floor, wall or
  ceiling are molded to its shape so edges meet the level with no gap. Walk through a tunnel into or out of the
  facility and the game treats you as inside / outside (lighting, sound, monsters) just like the doors do.
  Bedrock only stops you at the very bottom of the world and around the ship, doors and entrances.
  Monsters' navmesh is carved around holes, every player sees the same holes, and holes reset when you leave the
  moon. Items lying on the ground fall into holes dug under them (and off blocks you break).
  You're 1.8 blocks tall, like in Minecraft: two-block tunnels fit, but where a room's floor sits higher than the
  block grid you may need to crouch through the entrance (or dig it one block taller). Digging at the Company
  (Gordion) is off unless you enable `AllowDiggingAtCompany`.
- **TNT blows craters** into the ground. Set `LandminesBreakGround = true` to let landmines, Old Birds and other
  explosions do it too.
- **Ore veins never block hallways**: each vein is checked against facility pathfinding and removed if it would
  cut off or significantly lengthen a route.
- **Explosions** (landmines, Old Birds, TNT) break blocks.
- **Your ship is your base**: anything built inside the ship, on its hull or roof, or attached to a ship
  block travels with the ship and is **saved with your save file**, along with items left lying on it. A structure you build on the ground
  that touches the ship is pulled into the ship when you take off. Getting fired resets it, like everything else.
- Torch light and ambient fill follow the time of day and weather (eclipses are dark).
- **Ender pearls**: right-click to throw; you teleport where it lands and take 2.5 hearts of damage (Minecraft's
  speed, gravity and damage). By default they're found inside facilities as scrap; they can also be sold in the
  store. All configurable under `[Ender Pearls]`: `Enabled`, `SpawnInsideFacility`, `SpawnRarity`, `Buyable`, `Price`.
- Snowy moons have a layer of snow on top, then dirt, then stone.

## Config (`BepInEx/config/thrasherop.lethalcraft.cfg`)

`BlockSize` (1.4 = Minecraft proportions), `AllowDiggingTerrain`, `AllowDiggingAtCompany`, `LandminesBreakGround`, `PriceMultiplier`, `SpawnOreVeins`, `EnemiesBreakBlocks`,
`BlocksBlockEnemyPaths`, `ExplosionsBreakBlocks`, `LightBrightness`, `Hunger`, `HungerRate`,
`StarvationCanKill`, `HotbarSlots` (4–9), `MinecraftHud`, `ShowArmorOnPlayers`, `PlaceWithLeftClick`,
`UseLocalMinecraftAssets`, `MinecraftDirectory`, `MinecraftVersion`.

All players should run the same mod version and config.

## Install

Requires BepInExPack and LethalLib (which pulls in HookGenPatcher and MonoDetour). Drop
`LethalCraft.dll` into `BepInEx/plugins/LethalCraft/`.

## Building from source

```
dotnet build -c Release            # .NET 8 SDK; GameDir property points at your Lethal Company install
py tools/gen_textures.py           # regenerates the fallback art (PIL + numpy)
```
