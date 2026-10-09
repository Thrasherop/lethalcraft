## 1.6.0
- **Water and buckets**: Minecraft's water. A bucket (3 iron ingots, or the store) fills from a water source or from
  the moon's own rivers and lakes, and pours a source wherever you like. Water flows down and out (a few blocks:
  it doesn't run off down a whole hillside), dries up when its source is gone, and two sources make a third. It
  washes torches and redstone away, puts out fire and turns lava to obsidian. Swim in it ([Space] to rise); land
  in it to take no fall damage (pour one under you as you fall). With your head under, the game's own oxygen runs
  out: don't stay down too long.
- **Honey blocks** (store): sticky for pistons like slime, but they don't stick to slime (flying machines!). Slow
  to walk on, low jumps, and they soften a fall.

## 1.5.1
- **Jukebox and music discs**: craft a jukebox (8 planks around a diamond) and find music discs inside facilities (rare
  scrap, all 21 of Minecraft's). [E] with a disc plays its track from the jukebox for everyone nearby (monsters hear
  it too, like a boombox); [E] again gives the disc back. The music comes from your own Minecraft install.
- **Redstone repeaters**: pass a signal on at full strength, one way, 1-4 ticks later ([E] sets the delay). Placed
  pointing the way you look.
- **Enchanted items shimmer purple**, like Minecraft's: in the hotbar, the inventory screens and in your hand.

## 1.5.0
- **Enchanting**:
  - **Lapis lazuli** ore turns up 20+ blocks down. It's rare on purpose: enchanting could get out of hand. A stone
    pickaxe gets 2-4 lapis out of it.
  - **Enchanting table** (crafted: a bookshelf over 2 diamonds and 4 obsidian). [E] opens Minecraft's enchanting
    screen: put in a tool, sword or piece of armor and some lapis, and pick one of three offers. They cost 1-3 lapis
    and as many levels, and need up to level 30. Bookshelves around the table, two blocks out (up to 15), make the
    offers better.
  - **Efficiency** (mining speed, up to x3.5), **Unbreaking** (tools wear out more slowly), **Sharpness** (+10% damage
    a level), **Protection** (armor takes more off), **Feather Falling** (boots: less fall damage).
  - Enchantments stay with the item everywhere: chests, the [I] storage, armor slots, the ship's save.
- **Armor shows on players**: everyone sees what you wear, with Minecraft's armor textures, moving with your model.
  Your own shows in the [I] inventory. Rather not see it? `[HUD] ShowArmorOnPlayers = false` hides it (for you; it
  still protects).
- **New blocks**:
  - **Slabs**: oak, cobblestone, stone brick and stone. Top or bottom half by where you click. Two in one space make
    a full block, and you walk up onto a slab without jumping.
  - **Oak trapdoor**: a hatch over a hole (or on the floor); [E] opens and shuts it.
- **Fixes**:
  - Tools keep their wear when put in a chest, the [I] storage or moved around in the inventory screens (they came back
    as new). Chest and inventory slots show the wear bar too.
  - Stairs and glass panes no longer pop off when the block under them is broken.
  - A player who joins mid-game sees the right wear on tools already lying around.
  - The Totem of Undying's burst and Minecraft's totem sound go off both where you were about to die and where you
    arrive in the ship, for everyone nearby (it went off once, wherever each player happened to see you mid-teleport).

## 1.4.10
- **New blocks** (store, and crafted with Minecraft's recipes):
  - **Stairs**: oak, cobblestone and stone brick. Walk straight up them, no jumping.
  - **Oak doors**: two blocks tall, [E] opens and shuts both halves. Monsters can't walk through a shut one.
  - **Ladders**: put them on a wall, [E] to climb (with a hand free), like the game's own ladders. A column of them
    is one climb.
  - **Glass panes**: thin windows that join up with the walls and panes beside them.
  - **Diorite**.
- **Tools wear out**, like Minecraft: a use per block mined or monster hit (wood 59, stone 131, iron 250, diamond 1561),
  shown as a bar on the hotbar slot; a worn-out tool breaks. `[Balance] ToolDurability`, `DurabilityMultiplier`.
- **Critical hits**: a swing while you're falling (jump, then hit on the way down) does half as much again, with
  Minecraft's crit sparks.
- **Underground**:
  - **Redstone ore** (15+ blocks down, veins of 3-8): mine it with an iron pickaxe for 4-5 redstone dust.
  - **Lava pockets** (20+ blocks down): digging straight down is a risk again. In lava you burn fast, a few seconds
    to get out. Place a block into lava to fill it in. `[Ore Spawning] LavaMinDepth`, `RedstoneMinDepth`, and
    "per half moon" amounts like the ores (0 = none).
- Items left on blocks attached to the ship (a porch, a house on the roof) stay there between moons, like items in
  the ship.
- Store: "buy cobblestones" orders cobblestone again (not cobblestone stairs).

## 1.4.9
- **LethalMinecraft is now LethalCraft.** Same mod, new name: remove the old LethalMinecraft package. Your settings
  carry over by themselves (BepInEx/config/thrasherop.lethalcraft.cfg, copied from the old file the first time).
- **Fixes from the 1.4.8 playtest:**
  - Ores put into the crafting grid (diamonds, gold, emeralds) no longer vanish; they also stay put in chests now.
  - Hungry players can always jump (one jump used to leave them "exhausted" for good: stuck in any one-block hole).
  - Moon blocks no longer stay behind for one player as unmineable ghosts on later moons (a block arriving just
    after the moon unloaded used to keep that player's whole block world from being cleared).
  - Jumping on blocks attached to the ship as it takes off no longer leaves you behind (killed, items lost).
  - Store orders reach the item you meant: "buy bookshelves" bought boomboxes, "observers" obsidian, "cooked
    porkchops" cookies. The TNT pack is now the "TNT Crate" ("buy tnt x20" ordered ten crates); "buy jack o lantern"
    is ours (the game has a decoration of the same name).
  - Floor stains (puddles, blood) no longer show through blocks.
  - No blood trail after healing up.
- **Starting supplies**: the ship has a supply chest (beside the terminal); each player's share goes into it the first
  time they're aboard on a save: 48 steak and 32 oak planks ([Starter]). A team wipe and being fired start it over.
- **Ore balance** ([Ore Spawning]):
  - Ore comes in veins: iron 2-9 blocks (about 4.5), coal 4-12, gold 2-6, diamond 1-5, emerald 1-2.
  - How much there is is set as "<ore> per half moon": what one player mining for 8 in-game hours with a stone
    pickaxe gets on average (iron 22, coal 30, gold 6, emerald 2, diamond 4).
  - Diamonds need 30+ blocks below the surface.
  - More ore on harder moons (by risk level, or per moon).
  - Facility diamonds vary around the average (DiamondRandomness).
  - Raw iron is worth nothing (it's for crafting, and stacks); diamonds sell for 45-65. (These value settings have
    new names, so the old defaults in your config don't stay.)
- **Chat commands** (the host, and players the host makes operators with /op): /tp <player> [player|ship],
  /give <player> <item> [count], /gamemode, /op, /deop, /keepInventory <true|false> (also /gamerule keepInventory).
  [Tab] completes commands, player names and items.
- **Totem of Undying** (store, 150, 15 lb): anywhere in your hotbar when you would die, it's used up instead: full
  health, back in the ship, with Minecraft's totem animation.
- **[Q]** throws one item from the stack in hand (and drops a single item or tool), like Minecraft.
- Middle-click picks the block you're looking at, in creative.
- Optional auto-pickup of Minecraft items you walk over ([Controls] AutoPickupItems, off by default).
- Metal armor draws lightning in a storm ([Balance] MetalArmorDrawsLightning).
- A stack of scrap shows what the whole stack sells for when scanned.

## 1.4.8
- Balance pass. Every number below is in the config (BepInEx/config, LethalMinecraft): change it to taste.
  - **Store** ([Store Prices], [Store Stacks]; 0 = not sold, crafted-only items are listed at 0):
    - wood is 10 per 16 planks' worth;
    - torches are 10 for 32, light blocks 100 for 32;
    - TNT is 20 for one or 200 for a pack of 20;
    - flint and steel is 30;
    - the flying-machine parts are late-game: pistons and sticky pistons 100 for 4, observers 100 for 2, slime blocks 800 for 6 (one machine is about 1000);
    - the rest of the redstone stays cheap.
  - **Hunger** ([Survival] FoodPerDay): just being on a moon uses up about 2 steaks of food from 8am to 6pm, on top of sprinting and mining; never in orbit. A steak is 4 credits (6 for 24), and other food is priced by how much it fills you.
  - **Weapons** ([Damage]): pickaxes, shovels and wooden or stone swords and axes do half a shovel hit; the iron sword and axe hit like a shovel; diamond hits like two.
  - **Armor** ([Armor]): it works as extra health through damage reduction. Full iron is +30%, full diamond +65%, full gold +22%.
  - **Ore** ([Ore Spawning], [Ore Values]):
    - there's far less ore in dug stone; iron is 5x rarer than before;
    - diamonds only turn up 30+ blocks from open space, rarer than iron;
    - each facility gets 2 diamond ore per player, in its own small veins;
    - fewer veins overall;
    - ore sells for a little less.
  - Only the host's config counts: players who join get the host's store prices, and ore is decided by the host.
    (Which items are sold at all, 0 or not, still has to match.)
- Slimeballs: found inside facilities now and then (worth a little; [Ore Spawning] SlimeballRarity). 9 make a slime block (and back); a sticky piston is a slimeball on a piston, like Minecraft.
- When the whole crew dies, whatever is stored in chests on the ship is lost ([Balance] WipeShipChestsOnTeamWipe, on by default).
- New HUD option HideHudUntilBlockBroken (default off): the Minecraft HUD stays hidden until someone breaks a block, then appears for everyone.

## 1.4.7
- Big TNT blasts cost the host far less (#18): working out the ground around a crater is 3-7x cheaper (chain
  reactions about 2.5x, a field of craters about 3.4x overall). The ground comes out exactly as before.
- No more lasting lag where lots of items lie around (mined or blasted areas): the item-merging check that ran four
  times a second compared every pair of stacks in the level; it now only looks at stacks right next to each other
  (about 20x cheaper, the same merges).

## 1.4.6
- Optional big inventory (#15): Minecraft's 3x9 storage grid in the [I] inventory, above the hotbar. Off by default:
  the host turns it on with `BigInventory` in the config (HUD section). Minecraft items only; click or shift-click
  stacks in and out like a chest. What you store weighs what it would in your hotbar, drops where you die, stays with
  you from round to round and is kept with the save. If the host turns it off, what's stored can still come out.
- (1.4.5 note) One-handed tools from the creative menu go to your utility belt when it's empty, like any pickup.

## 1.4.5
- The creative menu has a Lethal Company tab: everything the store sells, plus shotguns, shells, kitchen knives, keys
  and homemade flashbangs. A click puts one in your hotbar (#17). The menu's tabs now sit above it, like Minecraft's.
- /gamemode: [Tab] completes the command, the mode and player names (any part of a name, any case; press it again for
  the next match). Long names fit now: the chat box only takes 30 characters, which left 12 for a name (#16).

## 1.4.4
- Fire lights on sloped ground (it only checked the ground under its center, which a slope can leave in the air) (#26).
- Digging at a lake shore no longer cuts the lake's water surface: a hole there shows water up to the lake's level (#27).

## 1.4.3
- No ore at the Company: with digging allowed there, Gordion's ground had ore, so quota could be strip-mined (#5).
- The inventory character turns toward the mouse (it turned away horizontally) (#6).
- Observers look right facing every way (the face was sideways facing east/west, the back upside down) (#7).
- Raw iron is worth a fixed $10 and stacks (a stack sells for $10 each) (#9).
- No more pop on every slot change; the pop plays when you pick something up (#10).
- Ender pearls are in the creative menu (#11).
- A block of redstone crafts into 9 redstone dust, and back (#13).

## 1.4.2
- Fixed the hotbar (and ladders) locking up for good when an item vanished while you were picking it up: the game's
  pickup got stuck half-way (#2). It now recovers on its own.
- Picking up a block or item tops up a matching stack in your hotbar instead of taking a new slot, even when the hotbar
  is full (no more rows of single cobblestone, no more "Inventory full!" with room in a stack) (#3, #8).
- Picking something up no longer switches what you're holding (#12).
- Fixed carry weight creeping up: items leaving a slot you weren't holding (crafting, armor, chests) kept their weight (#4).
- Items made or bought for you no longer wait seconds on the ground before landing in your hotbar.

## 1.4.1
- Tool and sword swings work like the shovel: one swing hits every monster in front of you (once each), and a dead
  monster lying in the way no longer soaks up your swings (you could be stuck hitting a corpse).

## 1.4.0
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
- Game modes are saved with the ship: whoever was in creative is again when they rejoin.
- Monster vents can't be dug through (their cover couldn't be cut and was left floating, still solid).
- Creative menu: armor joins the tools and swords in a Tools & Combat tab.

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
