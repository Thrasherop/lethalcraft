# LethalMinecraft

Minecraft, inside Lethal Company. Buy blocks from the Company store, build forts on the moon or a house on your
ship, wire up redstone, light the facility with torches, mine ore veins for scrap, and keep your hunger up.

Everything is networked (host-authoritative), so the whole crew builds in the same world, and late joiners
receive everything that's already built.

## Textures and sounds come from **your** Minecraft

The mod ships **no Minecraft assets**. At startup it reads the block textures, item sprites, HUD sprites,
font and sounds straight from your own local Minecraft Java installation (`%APPDATA%\.minecraft`, the newest
installed release, configurable). Without Minecraft installed, it falls back to built-in, procedurally
generated pixel art and synthesized sounds, so it still works.

**Custom install location?** Open `BepInEx/config/thrasherop.lethalminecraft.cfg` and set `[Assets] MinecraftDirectory`
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
| Light TNT | Right-click it with **Flint and Steel** |
| Pocket crafting (2x2 recipes) | **I** |
| Crafting table / furnace | **E** on the block |
| Select hotbar slot | **1–9** |
| Throw an ender pearl | **Right-click** while holding it |

**Rebind keys in-game**: the mod's keys (pocket crafting, hotbar slots) appear in the game's
**Settings > Change keybinds** menu (via LethalCompanyInputUtils, installed automatically as a dependency).
Placing, breaking and E use the game's own bindings, so rebinding those in the game's menu works too.
Prefer left-click to place? Set `Controls.PlaceWithLeftClick = true`. Want to change any setting in-game?
Install **LethalConfig**: it lists every option of this mod in an in-game menu.

## Blocks and items (terminal → type `MINECRAFT`)

- **Building**: grass, dirt, stone, cobblestone, oak/dark planks, logs (orientable), glass, sand and gravel
  (they fall), bricks, stone bricks, obsidian (blast-proof, unpushable), leaves, wool ×4, bookshelf, ice,
  and blocks of iron, gold and diamond.
- **Light**: torches (floor and wall), glowstone, jack o'lanterns, redstone lamps. Each is a real dynamic
  light, so they actually light up the facility.
- **Redstone**: dust (15-block falloff, connects up and down steps), levers, buttons, pressure plates
  (players *and monsters* trigger them), redstone torches (inverters, with burnout when used as a fast
  clock), blocks of redstone, pistons and sticky pistons (push up to 12 blocks, pull with sticky, carry
  players), TNT (chain reactions), note blocks (instrument depends on the block below), and slime blocks
  (bounce, no fall damage, sneak to cancel). Bedrock and snow appear when digging.
- **Tools**: pickaxes, shovels and axes in stone, iron and diamond, plus flint & steel. Breaking uses Minecraft's
  rules: each block has a hardness and a right tool, and stone/ores only drop when mined with a good enough
  pickaxe (iron ore needs stone+, gold/diamond/emerald need iron+, obsidian needs diamond). The store sells
  stone tools; iron and diamond tools are crafted.

## Crafting & smelting

- **Crafting Table** (buy it, or craft from 4 planks): **E** opens a real 3x3 crafting grid. Lay out the Minecraft
  recipe pattern (pickaxe = 3 on top + 2 sticks down the middle, and so on; shaped recipes work anywhere in the grid
  and mirrored) and take the result from the output slot; it goes into your hotbar. No recipe book: you know these.
  **I** opens the 2x2 pocket grid for small recipes (planks, sticks, crafting table, torches...).
  Mouse works like Minecraft: left-click picks up / places a whole stack, right-click takes half / places one,
  shift-click a hotbar stack to move it into the grid, shift-click the output to craft as many as you can, click
  outside the window to throw the held stack. Closing the screen puts what's left in the grid back into your hotbar
  (topping up stacks first); whatever doesn't fit drops at your feet, like in Minecraft.
- **Furnace** (buy it, or craft from 8 cobblestone): hold ore/sand/cobblestone/logs and press **E** to load it, hold
  coal/planks/logs and press **E** to fuel it, press **E** empty-handed to take the result. Raw iron / raw gold
  become ingots (or sell them as scrap instead: your choice).
- Recipes include stone/iron/diamond tools, torches, iron/gold/diamond blocks, stone bricks, levers, buttons,
  pressure plates, pistons, sticky pistons, redstone lamps, note blocks, flint & steel and golden apples.
- Coal and iron also turn up as ore pockets when you dig deep into the ground.

## Lethal Company integration

- **Ore veins** (coal, iron, gold, diamond, emerald) spawn inside facilities. Mine them for scrap you can sell.
- **Monsters** path around your walls (the navmesh is carved). A monster that wants a player behind a wall
  **chews through it**. Cobblestone buys you time and obsidian buys you a lot.
- **Dig anywhere**: hold left-click on moon terrain, hillsides, rocks, or the facility's floors, walls and ceilings
  to dig real block-sized holes and tunnels. Tunnel from one facility room into the next, or dig straight down from
  the surface all the way to the facility far below (a long way: bring blocks to build stairs, the fall is deadly).
  Only the exact cube you mine is cut out of the level's meshes; the hole's walls become real blocks (top layer,
  dirt, then stone, with richer ore pockets the deeper into the rock you are), and blocks at a floor, wall or
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
  block travels with the ship and is **saved with your save file**. A structure you build on the ground
  that touches the ship is pulled into the ship when you take off. Getting fired resets it, like everything else.
- Torch light and ambient fill follow the time of day and weather (eclipses are dark).
- **Ender pearls**: right-click to throw; you teleport where it lands and take 2.5 hearts of damage (Minecraft's
  speed, gravity and damage). By default they're found inside facilities as scrap; they can also be sold in the
  store. All configurable under `[Ender Pearls]`: `Enabled`, `SpawnInsideFacility`, `SpawnRarity`, `Buyable`, `Price`.
- Snowy moons have a layer of snow on top, then dirt, then stone.

## Config (`BepInEx/config/thrasherop.lethalminecraft.cfg`)

`BlockSize` (1.4 = Minecraft proportions), `AllowDiggingTerrain`, `AllowDiggingAtCompany`, `LandminesBreakGround`, `PriceMultiplier`, `SpawnOreVeins`, `EnemiesBreakBlocks`,
`BlocksBlockEnemyPaths`, `ExplosionsBreakBlocks`, `LightBrightness`, `Hunger`, `HungerRate`,
`StarvationCanKill`, `HotbarSlots` (4–9), `MinecraftHud`, `PlaceWithLeftClick`,
`UseLocalMinecraftAssets`, `MinecraftDirectory`, `MinecraftVersion`.

All players should run the same mod version and config.

## Install

Requires BepInExPack and LethalLib (which pulls in HookGenPatcher and MonoDetour). Drop
`LethalMinecraft.dll` into `BepInEx/plugins/LethalMinecraft/`.

## Building from source

```
dotnet build -c Release            # .NET 8 SDK; GameDir property points at your Lethal Company install
py tools/gen_textures.py           # regenerates the fallback art (PIL + numpy)
```
