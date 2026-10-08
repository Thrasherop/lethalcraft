## 1.3.0
- Armor: helmets, chestplates, leggings and boots in gold, iron and diamond (crafted). Worn in the four armor slots
  of the **I** inventory, which now looks like Minecraft's, with your character turning to follow the mouse (or
  right-click a piece in hand). Minecraft's damage reduction; an armor row above the hearts; drops where you die;
  saved with the ship.
- Swords in wood, stone, iron, gold and diamond (crafted). Tools now swing at monsters at most once every 0.8 s, like
  the shovel (they could be spam-clicked).
- The moons' trees can be chopped down (an axe helps): they shatter like when the cruiser hits them and drop oak logs.
- Store: tools are no longer sold (craft a wooden pickaxe and work up); blocks of iron (400) and diamond (750) and a
  new block of coal (150) are the way to buy materials, and craft back into ingots / diamonds / coal. Ordering
  something that's crafting-only explains how to get it.
- Lightning goes for metal you carry or drop: iron/gold tools, armor, ingots, blocks, flint & steel, redstone,
  pistons, including things made or bought after the storm began (the game only looked once, at the start).
- Dig anywhere: tunnel through facility walls between rooms, dig through floors and ceilings, and dig straight
  down from the surface to the facility. No more fixed bedrock depth (bedrock only at the bottom of the world and
  around the ship, doors and entrances).
- Molded edge blocks now follow walls and ceilings too, not just the ground.
- Meshes the game locks on the GPU (some moons' terrain, rocks and underlays) can now be dug.
- Walking into or out of the facility through a tunnel switches inside/outside state (lighting, audio, monsters).
- Items now fall when the ground or block under them is removed (no more floating items).
- Deeper rock has more ores; diamonds and emeralds are rarer near the surface.
- Pistons can push natural blocks into open space (the ground opens up where they were) but never into solid ground.
- Fixed a pushed molded block keeping its odd shape/collider, and natural blocks z-fighting with blocks built half into the ground.
- Moons built from Unity terrains (March, Vow, Adamance, Embrion...) are now diggable: the terrain is swapped for an
  identical mesh when you land.
- The block grid lines up with the facility's floors, so two-block tunnels from a hallway have full headroom.
- Torches, levers and redstone on ground that gets dug away pop off as items.
- New config `AllowDiggingAtCompany` (off by default).
- Real crafting grid: a 3x3 grid at the Crafting Table and a 2x2 pocket grid, with Minecraft's recipe patterns
  (shaped, mirrored, shapeless) and Minecraft mouse controls (stack pick-up, right-click half/one, shift-click).
  Items really move: results go into your hotbar, leftovers come back when you close the screen and drop at your
  feet if there's no room. The recipe list is gone: you craft from memory, like in Minecraft.
- Fixed a paper-thin film of terrain staying over a hole when the ground surface sat just above a block boundary.
- Ender pearls: throw to teleport (2.5 hearts of damage). Configurable: off, found inside as scrap (with a spawn
  rarity), buyable (with a price).
- Keybinds can be changed in-game (Settings > Change keybinds) through LethalCompanyInputUtils (new dependency).
- Snowy moons: snow on top, then dirt (was snow several blocks deep).
- Invisible facility collision (stair ramps, gap fillers) is dug along with the walls, so tunnels never keep
  invisible walls; props like shelves stay solid.
- Like Minecraft, the ground is stone (facility concrete floors too, which used to be cobblestone) and mining stone
  drops cobblestone; grass drops dirt.
- Mineshaft doorway rocks are diggable (they used to act as bedrock).
- TNT craters now remove the top layer of ground too (a thin skin could stay over the crater).
- Big security doors and normal doors are protected like entrances: a tunnel can't be dug into a door frame.
- Gordion stays undiggable by default, including the ground under it.
- Flint and steel lights TNT with right-click (the use button), like Minecraft.
- Torches, levers, buttons and redstone can be placed in the cell you're standing in (like Minecraft).
- A torch right next to you no longer blows out the whole view; block materials no longer pick up the moons'
  ground decals (leaf shadows).
- The hand's control hints clear when you use up the last item of a stack (no more "Torch x0").
- Wooden pickaxe, shovel and axe (craft them from planks and sticks: the first tools you can make from a tree).
  Harvest levels now follow Minecraft: wood mines stone and coal, stone mines iron, iron mines gold/diamond/emerald,
  diamond mines obsidian.
- Fixed: mining the floor or a wall of a building or room could make the whole thing vanish (any level object
  made of several materials, like Experimentation's entrance area or facility rooms). Checked on every moon by a new
  regression test that flags any object losing more than the cells that were dug.
- Generated ground blocks no longer poke out of slopes: a dug cell's neighbours only draw the faces the hole exposes
  (the terrain itself draws the surface), and a cell is only a full cube if the ground really fills it.
- Generated bedrock (next to doors, entrances and the ship) is shaped to the ground and has no collision of its own,
  so it can never block a door or a path; the protected geometry behind it stays solid.
- Friends who join after you've dug see the ground shaped correctly (shapes are part of the join sync).
- Chests: 27 slots, craftable from 8 planks or bought; ship chests keep their contents between days; broken or
  blown-up chests drop their contents (furnaces too).
- Observers: watch the block in front of them and send a pulse out of the back when it changes.
- Slime blocks stick: pistons move a slime block together with every block touching it (and whatever is in their
  way), pushing and pulling, up to 12 blocks.
- Your character no longer crouches, jumps, scans or drops items while a crafting or chest screen is open.
- Fixed TNT (or digging) filling open air with blocks: on the Company platform a TNT against the wall put blocks 4-5
  out from it, and smaller pockets of open air on most moons (under rock arches, by facility doorways, on ramps) counted
  as underground. Open air is now recognised everywhere monsters can walk.
- Fixed tunnels through some facility walls leaving the wall looking solid while you could walk into it (Experimentation's
  start room, beside the first door).
- Fixed a right- or left-click made in a crafting or chest screen acting in the world when the screen closed (it placed
  the torch in your hand).
- Store: "buy redstone block" (and iron/gold/diamond block) buys the Block of Redstone; long orders typed right after a
  purchase keep their amount ("buy slime block 2" used to buy one).
- Taking a furnace's output puts it straight into your hotbar (it used to drop at your feet).
- Ender pearls found as scrap keep their value when they end up in the same stack, and no longer merge on the ground
  by themselves.
- Torchlight in tight tunnels is softer (walls next to a torch no longer glow solid orange).
- Store: multi-word items can be typed with spaces ("buy stone pickaxe", "buy oak log 3", even "buy stone pick").
  Before, the terminal only read the first word and "buy stone pickaxe" bought Stone.
- Quitting in the middle of a day no longer duplicates (or loses) items moved between a ship chest and the floor:
  ship blocks and chests are saved together with the rest of the ship, in orbit.
- Fire: flint and steel lights the ground or a block (or TNT), with Minecraft's animated fire. It burns out after a
  few seconds on stone and dirt, spreads through wood, planks, leaves, wool and bookshelves and burns them away,
  lights TNT it touches, hurts you while you stand in it and hurts monsters too. Punch it to put it out.
- Flying machines work like Minecraft's: observers moved by a piston fire when they land, and a sticky piston given
  a short pulse leaves the blocks it pushed (block dropping). The wiki's engines fly; whoever and whatever rides one
  goes along. Blocks pistons move stop at the edge of the world instead of flying off forever.
- Standing (or an item lying) on blocks attached to the ship takes you along when it flies; you used to be left behind.
- Observers, chests and wooden tools use your Minecraft's textures (they had stand-ins).
- Creative mode for demos: the host types `/gamemode creative [player]` (or `@a`; `/gamemode survival` to go back).
  Blocks never run out and break with one click, [I] opens a creative menu with every item, double-tap jump to fly,
  no damage or hunger. Everyone sees the mode change in chat; players who join later get it too.
- In every slot screen, putting the held stack on an empty hotbar slot puts it in that slot (it used to go to the
  first free one).
- Ender pearls' hint says Throw (not Place); sticks, coal and ingots no longer offer to be placed.
- Pillaring works like Minecraft: look down, jump and right-click to put a block under your feet. The game can't look
  straight down, so at its look-down limit you now aim straight down: you can dig down a 1x1 shaft without hitting
  its walls, and dig back down a pillar you're standing on. Right-clicking down at your own feet no longer drops a
  stray block beside you.
- Redstone lamps go out 4 ticks after losing power, like Minecraft, so an observer's short pulse is visible (~0.3 s).
- The hand's control hints no longer show a stale extra line (e.g. "Bread x2" twice after holding a block).

## 1.2.0
- Digging rewritten: exact cube cuts (no more whole walls/strips vanishing), walls/buildings no longer diggable,
  no more blocks spawning around you, molded edge blocks that meet the terrain with no gap, tunnelling into
  hillsides, ore pockets underground, bedrock ~7 blocks down.
- Digging works on static-batched terrain (Offense and others).
- TNT blows craters into the ground; new config `LandminesBreakGround` for landmines/other explosions.
- Tools: pickaxe/shovel/axe in stone, iron and diamond with Minecraft hardness, tool types and harvest tiers.
- Crafting Table (E) and pocket crafting (I) with 30+ recipes; Furnace smelting (raw iron/gold -> ingots, sand -> glass...).
- New items: stick, coal, iron ingot, gold ingot; coal ore now drops coal.

## 1.1.1
- Config `MinecraftDirectory` now accepts quotes, env vars, ~, forward slashes, launcher/instance folders or a .jar; auto-detects CurseForge/Prism/Modrinth/TLauncher/GDLauncher/ATLauncher; clear log warnings.

## 1.1.0
- Dig real holes into moon terrain and facility floors (exact mesh cuts, navmesh carved, multiplayer synced).
- Ore veins are validated against facility pathfinding so they never block hallways.
- Fixed wall lever handle, wall torch texture, straight-down targeting.

## 1.0.0
- Initial release.
