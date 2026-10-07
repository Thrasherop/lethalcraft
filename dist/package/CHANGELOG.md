## 1.3.0
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
- Store: multi-word items can be typed with spaces ("buy stone pickaxe", "buy oak log 3", even "buy stone pick").
  Before, the terminal only read the first word and "buy stone pickaxe" bought Stone.

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
