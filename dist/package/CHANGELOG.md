## 1.31.0
- **Pumpkins** (#130):
  - Buy pumpkins in the store, or grow them: pumpkin seeds on wet farmland grow a stem, and the grown stem sets a
    pumpkin on the dirt, grass or farmland beside it.
  - **Shears** (two iron ingots, diagonal) carve a pumpkin ([Right-click]): a carved pumpkin and four seeds.
  - **Wear a carved pumpkin** (the helmet slot in [I]) and endermen don't mind you looking them in the eyes. You see
    through its eye holes, Minecraft's way, and others see it on your head.
  - A carved pumpkin over a torch crafts a jack o'lantern.

## 1.30.0
- **Redstone dust climbs blocks the way Minecraft draws it** (#103): dust beside a block and dust on top of it are
  joined by a line up the block's face (the signal already went up; now you can see it).
- **Zombies, skeletons, piglins and brutes are as tall as a player** (#100), 2.5 m instead of 2.2.

## 1.29.0
- **Mobs hold their swords by the handle** (piglins, brutes, wither skeletons) and skeletons hold their bows by the
  grip, not the string (#99).
- **A blaze faces you while it chases you** (#102): it used to turn with its path and shoot with its back to you.
- **Creative: the shotgun reloads without shells** (#104).

## 1.28.0
- **Picking up a body (or anything two-handed) while holding a stack works** (#127). It used to stay in your pocket
  while you kept holding the stack, and dropping then threw the stack (your torches) instead.
- **The ship's furnaces and brewing stands keep what's in them** after the game is closed and reloaded (#120). Only
  the blocks and chests were saved before.
- **You can see when a mob is after you** (#111): wither skeletons and piglins raise their arms, and a blaze catches
  fire, glowing orange with embers rising off it.
- The Nether's monsters are no longer removed for spawning near its lava (spawning near a light keeps mobs away on the
  moons, Minecraft's way; the Nether's monsters spawn in any light).

## 1.27.0
- **Calm mobs don't hurt you when you bump into them** (#110): an enderman you haven't looked at, or a piglin while
  you wear gold, used to hit whoever walked into it. Only a mob that's after someone hits now.
- **You can jump out of water one block deep** (#115). With swimming off (the default) you couldn't jump in shallow
  water, so a one-block pool (farm water) trapped you. The water still slows you.
- **The mouse wheel goes through the hotbar Minecraft's way** (#117): wheel down moves right. `[Controls]
  MinecraftScrollDirection` (on by default) switches back to the game's way.
- **Mob deaths look like Minecraft's** (#113): the mob flashes red, tips over, and vanishes in a puff of smoke.
- **Books** (#116): three paper and a string make a book (no leather in this world). Books make bookshelves, and the
  enchanting table takes a book (or a bookshelf, as before).
- **The jukebox and the enchanting table take the diamonds you mine** (#119), not only crafted ones.
- **Nether quartz ore gives XP** when mined (2-5, like Minecraft) (#121).
- **The Company's boss bar shows a second after the ship's doors open** (#123), not only once you step out of the ship.
- **The TNT Crate shows TNT in the hotbar** (#125), not a stone block.
- A mob killed by several players at once drops its loot only once (#126: endermen could drop more than one pearl).

## 1.26.0
- **Kast: the Nether no longer kills you on arrival** (#107). Kast has a void trigger at the Nether's depth; the
  Nether now ignores a moon's void triggers. Falling out of the bottom of the Nether puts you back at the ship, like
  falling off a moon.
- **"The entrance appears to be blocked" fixed** (#108). With LethalLevelLoader installed (any modded moon pack),
  generating the Nether renumbered the facility's doors, so you couldn't get back out. The Nether no longer touches
  them.
- **Minecraft mobs keep up with you** (#100): every mob now chases at least as fast as a walking player (zombies,
  skeletons, creepers and blazes 4.6 m/s; spiders 5.4, endermen 6). Sprint to get away.
- **Tools and armor from a chest blown up in the ship are kept** (#109). They used to vanish when the ship left if
  they landed outside the ship's main room (by the door, on the catwalk). The same goes for crafted tools and furnace
  drops there.
- The Totem of Undying costs **200** (was 150; configs still on the old price follow).

## 1.25.0
- **The Nether is much bigger:** at least as big as the facility on the moon you're on (a bigger moon, a bigger Nether),
  where it used to be about a dozen rooms. More rooms mean more fire exits, monsters and loot. `[Nether] Rooms` sets
  the smallest it can be.

## 1.24.1
- No gameplay changes: a test build to check the new release pipeline (test builds from `main` no longer go to
  Thunderstore; only the `production` branch publishes).

## 1.24.0
- **Endermen move blocks** (#94), as in Minecraft: while wandering, an enderman now and then picks up a block nearby
  (grass, dirt, sand, gravel, TNT, jack o'lanterns, netherrack, nylium, soul sand or soul soil), carries it in its
  hands and puts it down somewhere else. Only blocks players placed, so your dirt bridge may go missing, but the
  facility's walls stay put. Kill it while it carries one and the block drops.

## 1.23.0
- **Basalt deltas** (#68): the last kind of Nether, about one day in ten. Basalt and blackstone, glowing **magma
  blocks** in the floor, and a lava pool with basalt columns where the fortress has its blaze room. Blazes and wither
  skeletons (Minecraft's magma cubes and ghasts aren't in this world). `[Nether] DeltaChance`.
- **Elytra and rockets, as the owner asked** (#61): the elytra is **750** in the store, rockets are **32 for 100**
  (configs still on the old defaults move to these; prices you set yourself stay). A worn elytra can be **mended**:
  put it and string in a crafting grid; each string mends a quarter (Minecraft uses phantom membrane, and there are
  no phantoms here).

## 1.22.0
- **Crimson and warped forests** (#68): two more kinds of Nether, about one day in ten each. Nylium underfoot,
  netherrack walls patched with wart blocks, roofs of wart blocks lit by glowing **shroomlights**, and a huge fungus
  where the fortress has its blaze room.
  - **Crimson:** warm red light and piglins. Nether wart and some gold lie about.
  - **Warped:** teal light and endermen. Ender pearls lie about.
  - `[Nether] CrimsonChance` and `WarpedChance`.
- **Fixed:** endermen spammed the log with navmesh errors when hit while they couldn't move.

## 1.21.0
- **Soul sand valleys** (#68): about one day in four the Nether is a soul sand valley: soul sand and soul soil
  underfoot (nether wart grows on it), basalt, a fossil of bone blocks where the fortress has its blaze room, and
  cold blue light. Skeletons haunt it, with endermen and wither skeletons. Bones (bone meal) and nether wart lie
  about. `[Nether] ValleyChance`. Bone blocks make nine bone meal.
- **Piglin brutes** (#68) guard the bastions: always angry (gold armor doesn't fool them, and they don't trade), a
  golden axe, hard hits.
- **Fixed:** a hoe tilled dirt with a sliver of hillside still over it, and the farmland then couldn't take seeds. Like
  Minecraft's, a hoe now only tills dirt with air above (#93).

## 1.20.0
- **Fixed: getting stuck fast** (#64). If your body ended up even slightly inside something (a pipe, the lamp bars
  hanging in the factory's halls, a block), you couldn't move or jump at all. Ways it happened, now fixed:
  - **Pillaring** under something low: the block isn't placed when you wouldn't fit standing on it.
  - **Ender pearls** landing against a pipe or a block: you land where you fit, or nearby.
  - **A safety net** for anything else: if you're trying to walk and haven't moved for a second and a half while
    caught inside something, you're moved to the nearest spot where you fit (`/unstuck`, done for you).

## 1.19.0
- **Bastions** (#68): some days (about one in three) the Nether is a **bastion remnant** instead of the fortress:
  - Built of **polished blackstone bricks**, some cracked, some plain blackstone, and some **gilded blackstone**
    (mined, it gives 2-5 gold nuggets).
  - A **treasure room** with a heap of **gold blocks** between lava pools, where the fortress has its blaze room.
  - Mostly **piglins** (no blazes), and gold in its loot: raw gold that sells, and gold ingots to trade with them.
  - `[Nether] BastionChance` sets how often. The day's seed decides, the same for everyone.
- **Fixed:** now and then a day's Nether had no fire exits (its layout grew no branches). It now always has them.
- **Fixed:** a piglin could stand still for good, facing a gold ingot it couldn't get to, and ignore you. Piglins now
  give up on gold they can't reach, and leave the bastion's loot gold alone (they trade only for yours).

## 1.18.0
- **Skeletons** (#59) inside the facility, Minecraft's: they keep their distance and shoot arrows at you when they can
  see you (the arrows drop a little over distance and stick in walls), and walk in when they can't. They drop
  **bones**: a bone makes three **bone meal**, and bone meal on a growing crop grows it two to five stages.
  `[Mobs] SkeletonRarity`.
- **Spiders** (#59) inside the facility: low, wide and faster than you walk, eight legs scuttling and red eyes
  glowing in the dark. They drop **string**: four make a block of white wool. `[Mobs] SpiderRarity`.
- **Endermen** (#59), rare, inside the facility: three metres tall, purple eyes glowing. They leave you alone until you
  look one in the eyes (or hit it). Then it screams, shakes and comes for you fast, blinking in close every few
  seconds and away when you hurt it. Half the time it drops an **ender pearl**. `[Mobs] EndermanRarity`.
- **Fixed:** a sword couldn't hit a monster standing in front of a block someone had placed (a wall, a bridge): the
  click mined the block behind it instead. The monster in front takes the hit now, like Minecraft (#92).

## 1.17.0
- **Piglins** (#67), in the Nether fortress, Minecraft's:
  - They go for anyone not wearing gold: **one piece of golden armor** and they leave you alone (unless you hit one).
  - **Bartering:** drop a gold ingot near a piglin ([Q] throws one). It walks over, picks it up, holds it up to look
    at it for six seconds, then throws you something for it: ender pearls, nether quartz, obsidian, soul sand,
    nether bricks, gravel, iron, a water bottle, or now and then a potion of fire resistance. Gold has a use now.
  - Minecraft's piglin look (wide head, snout, tusks, ears, a golden sword) and sounds. `[Mobs] NetherPiglins`.
- **Fixed:** at the Company, items thrown or dropped from a broken block onto the **ramp** up from the dock fell
  through it and lay hidden under it (still scannable). They land on the ramp now (#78).

## 1.16.0
- **Brewing** (#88), Minecraft's:
  - **Nether wart and soul sand:** soul sand veins in the Nether's netherrack, nether wart in its loot. Nether wart
    grows on soul sand (no water needed), Minecraft's three looks, 2-4 when ripe.
  - **Glass bottles** (three glass) fill with water when you right-click water with them.
  - **The brewing stand** (a blaze rod over three cobblestone): **E** opens Minecraft's brewing screen. Three bottles,
    an ingredient and **blaze powder** for fuel (a blaze rod makes two; one powder lasts 20 brews). A brew takes
    20 seconds; the bubbles and the arrow show how far along it is. Breaking the stand drops what's in it.
  - **Potions:** water + nether wart makes an awkward potion; then sugar makes **Swiftness** (walk 20% faster a
    level), blaze powder **Strength** (Minecraft weapons hit harder), magma cream (blaze powder + a slime ball)
    **Fire Resistance** (burning doesn't hurt) and a golden carrot (8 gold nuggets round a carrot) **Night Vision**.
    Redstone makes them last 8:00 instead of 3:00; glowstone makes Swiftness and Strength level II (1:30).
  - Hold right-click to drink one; the bottle comes back. The effects show at the top right with Minecraft's icons
    and the time left. Potions don't stack.

## 1.15.0
- **The Nether: its ground and netherite** (#66):
  - **Dig the fortress:** its walls dig like the facility's and give **nether bricks**. Beyond them is solid
    **netherrack** to tunnel through, with veins of **nether quartz ore**, lava pockets and, rarely and away from the
    fortress, **ancient debris** (a diamond pickaxe gets it out, as scrap worth $70-90).
  - **Netherite:** smelt ancient debris into **netherite scrap**. Four scrap and four gold ingots make a **netherite
    ingot**, which sells for $500. A diamond tool or sword plus an ingot makes the **netherite** one (the best tier).
  - **Fire on netherrack never goes out**, like Minecraft's.
  - **It's always eclipsed in the Nether:** the fortress is full of monsters from the moment it opens (no morning
    grace), and whatever you kill comes back, away from you, every 20 seconds. `[Mobs] NetherMonsters` sets how many.
  - **Fire exits:** every branch of the fortress ends in an exit room with its own portal frame. Build more portals on
    the moon and each one lights one of them: going in by your second portal brings you out in an exit room, and that
    room's portal takes you back out of your second portal.
  - **Beds** (3 red wool, 3 planks): two blocks long, with Minecraft's look. Sleeping doesn't set where you come back
    on a moon, and in the Nether a bed **explodes** when you use it, bigger than TNT, setting fires.
- **Wither skeletons are Minecraft's size:** 2.4 blocks tall (they were smaller than a player's suit). `[Mobs]
  WitherSkeletonHeight` changes it.

## 1.14.0
- **The Nether, first part** (#66):
  - **Nether portals:** build an obsidian frame (its inside 2 to 21 wide and 3 to 21 tall, corners optional) and
    light it with flint and steel. It fills with Minecraft's purple portal, which glows and hums, and goes out if any
    block of its frame is broken.
  - **The Nether fortress:** once you've landed, a lit portal anywhere (on the moon or built onto the ship) opens a
    second interior far below: Minecraft's nether-brick fortress, with corridors, crossings, bridges over lava, a blaze
    room with a spawner, and the portal room you arrive in. Every player gets the same fortress.
  - **Going there:** stand in a portal for 4 seconds (the screen turns purple). Its own portal brings you back out of
    the one you went in by. It counts as inside, like the facility: anyone still there when the ship leaves is lost.
  - **What's in it:** **blazes** (they float, burn on touch and shoot bursts of fireballs that set fires), more
    wither skeletons, and the spawner keeps making blazes while you're near. The loot is worth about 1.6 times the
    moon's own (S+), with **blaze rods** ($30-60, also dropped by blazes) and the odd wither skull.
    `[Mobs] NetherBlazes`.
  - Still to come: digging netherrack, quartz, ancient debris and netherite, beds.

## 1.13.0
- **Zombies** (#59), Minecraft's: inside the facility, slow (a walking player gets away), arms out in front, groaning.
  One that sees you comes for you and hits; four sword hits kill it. It drops **rotten flesh** (food, barely
  filling), and one time in 40 an iron ingot, a carrot or a potato. Lit places keep them from spawning, like the other
  Minecraft mobs. `[Mobs] ZombieRarity` (12; 0 = none).
- Minecraft's mobs (zombies, wither skeletons) notice you in every direction, not only in front of them.

## 1.12.0
- **Farming** (#60), Minecraft's: a **hoe** (wood to diamond) turns dirt or grass into **farmland**, the moon's own
  ground too. **Wheat seeds, carrots and potatoes** planted on it grow through eight stages in about a moon day. Like
  Minecraft, crops need **water within 4 blocks**, and dry farmland with nothing on it turns back into dirt. Ripe wheat
  gives wheat and seeds (three wheat make **bread**); carrots and potatoes give 2-5 (a carrot or potato plants on
  farmland and is eaten anywhere else; the furnace bakes potatoes). **Sugar cane** grows on dirt, grass or sand beside
  water, up to three tall, and makes **paper** and **sugar**. Crops on the ship keep growing between moons. Seeds,
  cane, carrots and potatoes are cheap in the store, and a few turn up inside facilities, worth nothing
  (`[Farming] LootPerMoon`).
- **The elytra and firework rockets** (#61): wear the elytra in the chestplate slot, jump, and press [Jump] again in
  the air to glide, with Minecraft's physics: look down to gain speed, up to climb. A **firework rocket**
  ([Right-click] while gliding) boosts you, loudly (monsters hear it). Flying into a wall at speed hurts, and so does
  diving into the ground. About seven minutes of gliding wear it out. The store sells the elytra (1000, more than the
  jetpack) and rockets (16 for 120); rockets are also crafted from paper and gunpowder. Everyone sees your wings.
- **A boss bar at the Company** (#63): Minecraft's purple boss bar titled The Company at the top of the screen while
  you're there. It stays full (selling isn't hurting the Company); `[HUD] CompanyBossBarQuota` fills it with the quota
  sold instead. `[HUD] CompanyBossBar = false` hides it.

## 1.11.0
- **Wither skeletons** (#59): a rare monster inside the facility, Minecraft's own, with a stone sword. It goes for the
  nearest player it sees; four hits kill it. Lit places keep it from spawning, like the creeper. It drops coal now and
  then, and very rarely its **skull**: a trophy the Company pays well for. `[Mobs] WitherSkeletonRarity`,
  `WitherSkullChance`.
- **The Circuit Bees are one big Minecraft bee** (#84): it flies where the swarm would be, buzzing, angry while it
  guards its hive or chases you. Their sounds, hive and zaps are the game's own. `[Mobs] MinecraftBee = false` for the
  game's swarm.
- **More ore in the ground** (#71): about four times as much ore in every block dug. The amounts assumed a player
  tunnelling all day; a crew digs far less (on Kast, one iron vein by 3 pm against five expected).
  `[Ore Spawning] MiningSecondsPerHalfMoon` (60) tunes it.
- **Water and lava no longer run into solid ground**: water poured near uneven ground could sink 30 blocks down
  through it, and took a long time to dry up.

## 1.10.1
- **Clients work alongside mods that break the round's start** (#72): with some modpacks (Minecraft Cave Sounds among
  them), clients had no blocks, no hotbar and no mining, because another mod's error on the client stopped LethalCraft
  from starting there. LethalCraft now starts first, and starts anyway if it was skipped.

## 1.10.0
- **The furnace has a screen** (#83), Minecraft's: [E] opens it. What smelts goes in the top slot and the fuel below,
  and the result comes out on the right, with the flame and the arrow showing how it's going. Shift-click puts things
  where they go.
- **Spawn eggs** (#85): the creative menu has one for every monster the game knows (Lethal Company's, the creeper,
  other mods'). Right-click the ground to hatch it there; outside creative the egg is used up. Each one gets the
  Minecraft egg nearest to it: a spider's for the Bunker Spider, a fox's for the Kidnapper Fox, and so on.
- **Every item in the creative menu** (#86): the Lethal Company tab has all of the game's items, scrap included (with a
  value, like scrap found on a moon), and other mods' items.
- **Hardcore** (#87, `[Survival] Hardcore`, off by default, the host's setting): a player who dies stays dead for the
  rest of the run, back in orbit too, and watches the others. When the whole crew is dead it's fired. A totem of
  undying still saves you.
- **The held item shows again after climbing a ladder** (#81): it stayed hidden until you changed slots.
- The fire animation has all its frames again (the texture atlas was full).

## 1.9.0
- **Lava flows** (#74), like Minecraft's: a lava source runs out 3 blocks, slowly, lower the further it goes, and dries
  up when its source is gone. Water running into flowing lava makes cobblestone, into a lava source obsidian; lava
  running into water makes stone or cobblestone. Lava burns torches, dust and the like (water washes them away).
- **Lava buckets**: an empty bucket takes a lava source, and pours it back out.
- **Empty buckets stack** (up to 16). Filling one from a stack puts the full bucket in a free slot.
- **Water works in the ship** (#75): it spreads over the ship's floor, and the bucket takes it back. Water stays on
  the ship (inside it, or over blocks you built onto it) rather than pouring out of the door.
- **Infinite water on natural ground** (#79): two water sources with a gap between them make a third, on the moon's
  own ground too, not only on blocks.
- **Loot left on blocks built onto the outside of the ship stays there after a reload** (#69). Before, the game put it
  back by the door.
- Water and lava no longer stick to the ship when it takes off (water touching it took a whole sheet of water along),
  and a waterfall dries up quickly once its source is gone.
- **Flint and steel lights the ground** where it lies low in its block (it did nothing there).
- **Modded moons** (#57, #73): Kast's ground (made of boxes) can be dug, and on E Gypt the raised blocks dig and the
  giant walls no longer stand uncut over a hole when seen from a distance. Tested on Kast, Ether, Aquatis, Orion and
  E Gypt.

## 1.8.1
- **/unstuck** (#64): wedged in the level's geometry (pipes by a dug-out wall could trap you for good)? Type
  `/unstuck` in chat: it moves you to the nearest spot within 6 m where you fit, on real floor. Anyone can use it (no
  operator needed), once every 10 seconds.
- **The creative menu scrolls** (#65): the mouse wheel moves it a row at a time, with a scroll bar beside it. Items
  past the first 45 of a tab (the Building tab has more now) couldn't be reached before.
- **Ship loot no longer disappears after reloading a save** (#69). Loot the game put back by the door when it loaded the
  save was taken off the ship by LethalCraft, and the next round's end deleted it. In orbit, everything loose on the
  ship is now the ship's.
- **The totem saves you from the Earth Leviathan** (#76): the worm's bite counted twice, so the totem was used up
  and the second bite still killed you.
- **Doors hinge opposite the doorknob** (#77), and breaking a door's bottom half drops one door, not two (#82).
- Buckets make Minecraft's bucket sounds, not slime's (#80).
- **Creepers are more common** (#90): CreeperRarity 80 inside (was 30), 15 outside (was 5). Configs still at the old
  defaults move up once.

## 1.8.0
- **Creepers** (#59): Minecraft's creeper, as a Lethal Company monster. It roams the facility (and, now and then, the
  moon outside). It walks up to you, and close by it stops, hisses and swells for a second and a half, then explodes
  like TNT (blocks, a crater, the game's blast damage). Get 7 blocks away and it gives up, but its swell only drains
  back down slowly: come back quickly and it goes off sooner. Three hits kill it, and it drops gunpowder.
- **Gunpowder** and **TNT's recipe** (gunpowder and sand, like Minecraft).
- **Light keeps Minecraft's monsters away**: none spawns within 8 blocks of a placed torch, lit lamp, glowstone, jack
  o'lantern or lava, inside or out. Light up your base.
- [Mobs] in the config: Creepers (on/off; every player needs the same), CreeperRarity (inside), CreeperOutsideRarity,
  CreeperMaxCount, CreeperCraters, LightBlocksSpawnRadius.

## 1.7.1
- Ready for Thunderstore: CI builds and checks the Thunderstore package on every release, and publishes each new
  version there once the team's token is set up (docs/THUNDERSTORE.md). The package links to the GitHub page.
- When a monster loses the navmesh (#25), the log says where it is and what's around it, once per monster, to track
  that bug down.

## 1.7.0
- **Redstone comparators** (#23): point one the way you look, like a repeater. It passes on the signal behind it at
  its strength, unless the strongest one at its sides (dust, repeaters, comparators, redstone blocks) is stronger;
  [E] switches it to subtract mode (behind minus the side; the front torch lights). Behind it, a chest, furnace or
  jukebox gives how full it is (a jukebox: the disc's number), like Minecraft, even through a block. Craft: three
  redstone torches over redstone dust (no nether quartz here) on three stone; or the store.
- Redstone strengths: a comparator's signal keeps its strength through dust and blocks (dust from a comparator at 5
  reaches 5 blocks, not 15).

## 1.6.0
- **Water and buckets**: Minecraft's water. A bucket (3 iron ingots, or the store) fills from a water source or from
  the moon's own rivers and lakes, and pours a source wherever you like. Water flows down and out (a few blocks:
  it doesn't run off down a whole hillside), dries up when its source is gone, and two sources make a third. It
  washes torches and redstone away, puts out fire and turns lava to obsidian. Land in it to take no fall damage
  (pour one under you as you fall). It's Lethal Company's hazard: it slows you, you can't swim up, and with your
  head under the game's own oxygen runs out. Want Minecraft swimming ([Space] to rise)? `[Water] Swimming = true`.
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
