# LethalCraft

*(Formerly LethalMinecraft. Coming from it: remove the old LethalMinecraft package; your settings carry over by
themselves.)*

Minecraft, inside Lethal Company. Buy blocks from the Company store, build forts on the moon or a house on your
ship, wire up redstone, light the facility with torches, mine ore veins for scrap, and keep your hunger up.

Everything is networked (host-authoritative), so the whole crew builds in the same world, and late joiners
receive everything that's already built.

**Found a bug, or have an idea?** Please report it on GitHub:
**https://github.com/Thrasherop/lethalcraft/issues**. A log helps a lot: `BepInEx/LogOutput.log` from the game
folder (or your mod manager's profile), right after it happens, before you relaunch.

## At a glance

- **Dig and build anywhere**: the moon's ground, hills and rocks, the facility's floors and walls, down to the
  facility from the surface; real Minecraft blocks, with ore veins, lava pockets and diamonds deep down.
- **Survival**: hearts, hunger, XP, crafting (tools, weapons, armor), furnaces, chests, enchanting, brewing, farming,
  fishing, the elytra.
- **Redstone**: dust, repeaters, comparators, pistons, observers, TNT, slime flying machines.
- **Minecraft's mobs** in the facility (creepers, zombies, skeletons, spiders, endermen, wither skeletons) and on the
  moons by day (pigs, cows, chickens, sheep, rabbits, horses, wolves, cats, parrots, squid in the lakes). Tame a horse
  and ride it, tame a wolf to fight for you, carry a parrot on your shoulder, breed animals, build iron and snow golems.
- **The Nether**: build an obsidian portal and light it. Fortresses, bastions, soul sand valleys, crimson and warped
  forests and basalt deltas, with blazes, piglins, ghasts and magma cubes, netherrack to dig and netherite to find.
- **Advancements** (Minecraft's toast), **creative mode**, **spectator mode** and chat commands for the host.

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
  (Regeneration plus 2 golden absorption hearts). Or grow it (wheat, carrots, potatoes), get it from animals (raw beef
  and porkchops, cooked in a furnace) or fish for it.
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
| Crafting table / furnace / chest / brewing stand | **E** on the block |
| Drink a potion | hold **Right-click** |
| Select hotbar slot | **1–9** |
| Throw an ender pearl | **Right-click** while holding it |
| Fish | **Right-click** with a fishing rod to cast; again when the bobber dips, to reel in |
| Ride a horse / get off | **E** on it / **Crouch** |
| Tame an animal | **E** holding its food (a bone for a wolf, fish for a cat, seeds for a parrot) |
| Sit your pet down or up; put your parrot on your shoulder | **E** on it (jump to send the parrot off) |
| Breed two animals; shear a sheep; put on a lead or horse armor | **E** on it holding its food / shears / a lead / the armor |
| Glide with an elytra / boost | jump, then **Jump** again in the air / **Right-click** a firework rocket |
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
armor are crafted, never bought (ordering one, like `buy stone pickaxe`, tells you how to get it instead). The one
exception is the **bow** (200 credits).

- **Building**: grass, dirt, stone, cobblestone, diorite, oak/dark planks, logs (orientable), glass, glass panes
  (they join up with the walls and panes beside them), sand (it falls), bricks, stone bricks, obsidian
  (blast-proof, unpushable), leaves, wool ×4, bookshelf, ice,
  and blocks of iron (400 credits), diamond (750), coal (150) and gold: expensive on purpose, they craft back into
  nine ingots / diamonds / coal for good gear.
- **Minecraft's block families**: granite, diorite and andesite (patches in the moons' stone) and their polished
  kinds; smooth stone; mossy cobblestone and mossy, cracked and chiseled stone bricks; sandstone and red sandstone
  (cut, chiseled, smooth); quartz (block, chiseled, pillar, bricks, smooth); red, cracked and chiseled nether bricks;
  polished, chiseled and cracked blackstone; hay bales. Deep down, in and around the facility, the ground is
  **deepslate** with deepslate ores (cobbled, polished, bricks, tiles, chiseled, cracked). Each family has
  Minecraft's stairs, slabs and walls, crafted the way Minecraft's are.
- **Stairs** and **slabs** of every family: walk straight up stairs; a slab is the top or bottom half by where you
  click, two make a full block. **Walls** join the walls and blocks beside them (a post where Minecraft has one).
  **Fences** (oak, dark oak, nether brick) join likewise; tie a lead to one (below). **Doors**: oak (**E** opens
  them) and iron (only redstone does); monsters can't walk through a shut one. **Trapdoors**: oak and iron, likewise.
  **Ladders**: put them on a wall and press **E** to climb, like the game's own ladders (with a hand free).
- **Light**: torches (floor and wall), glowstone, jack o'lanterns, redstone lamps. Each is a real dynamic
  light, so they actually light up the facility.
- **Redstone**: dust (15-block falloff, connects up and down steps), repeaters (one way, full strength, a 1-4 tick
  delay set with **E**), comparators (pass on the signal behind at its strength, or subtract the sides with **E**;
  they read how full a chest, barrel, furnace or jukebox behind them is), levers, buttons (stone, oak, dark oak,
  polished blackstone; wooden ones stay in longer), pressure plates (stone ones feel players *and monsters*, wooden
  ones items too; weighted ones give a signal by how much is on them), tripwire (two hooks facing each other, string
  laid between them), the target (an arrow or snowball: a pulse, strongest at the middle), the daylight detector
  (**E** turns it to night), the trapped chest (power while someone looks in), doors and trapdoors (they open while
  powered), redstone torches (inverters, with burnout when used as a fast
  clock), blocks of redstone, pistons and sticky pistons (push up to 12 blocks, pull with sticky, carry
  players), observers (watch the block in front of their face and pulse out of their back when it changes),
  TNT (chain reactions), note blocks (instrument depends on the block below), and slime blocks (bounce, no fall
  damage, sneak to cancel; pistons move them together with every block stuck to them, like Minecraft's flying
  machines). Pistons have Java's **quasi-connectivity** (powered by what would power the space above them;
  `[Redstone] QuasiConnectivity`) and act on **block updates** as Minecraft's do: a piston powered without one waits
  until something next to it changes, so Java's piston doors (the 2x2 flush "Jeb door") work. Snow and stone appear
  when digging.
- **Tools**: pickaxes, shovels and axes in wood, stone, iron, gold and diamond, plus flint & steel. Breaking uses
  Minecraft's rules: each block has a hardness and a right tool, and stone/ores only drop when mined with a good
  enough pickaxe (stone and coal need wood+, iron ore needs stone+, gold/diamond/emerald need iron+, obsidian needs
  diamond). Mined stone drops cobblestone. All tools are crafted: buy oak logs, make a wooden pickaxe and work your
  way up. A tool swings at monsters about as often as the shovel (once every 0.8 s) and hits as hard.
  Tools wear out like Minecraft's (gold 32 uses, wood 59, stone 131, iron 250, diamond 1561: a use per block, one or two per
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
- **Water** and **buckets** (3 iron ingots, or the store; empty ones stack to 16): fill a bucket from a water source
  or the moon's own rivers and lakes, pour it anywhere, the ship too. Water flows down and out a few blocks like
  Minecraft's (two sources a block apart make a third), washes away torches and redstone, puts out fire and turns lava
  to obsidian. Land in it to take no fall damage. It's a hazard like the
  game's own water: it slows you, you can't swim up, under it your oxygen runs out (`[Water] Swimming = true` for
  Minecraft swimming).
- **Lava** flows too, slowly and only 3 blocks, and burns what water would wash away. A bucket takes a lava source
  and pours it back out. Water meeting flowing lava makes cobblestone; lava running into water, stone.
- **Honey blocks**: sticky for pistons (but not to slime), slow to walk on, they soften a fall.
- **Jukebox** (8 planks around a diamond) and **music discs** (all 21 of Minecraft's, found inside facilities as rare
  scrap, never sold): **E** with a disc plays its track from the jukebox, heard by everyone nearby and by monsters;
  **E** again takes it out. `[Music Discs]` sets how often they turn up.
- **Trees** on the moons can be chopped down: hold left-click on a trunk (about 9 s by hand, 4.5 s with a wooden
  axe, about a second with a diamond one). It shatters like when the cruiser hits it and drops 4-6 oak logs.
- **Lightning** goes for metal: iron and gold tools, armor, ingots and blocks, flint & steel, redstone and pistons
  draw the storm like the game's own metal scrap (also things made or bought after the storm started).
- **Vines** hang on some of the moons' tree trunks: climb them like a ladder (**E**); only shears take them (they
  make mossy cobblestone and stone bricks); they grow slowly down.
- **Smithing table**: the netherite upgrade happens here: a netherite upgrade template (found in bastions; seven
  diamonds and netherrack copy one), a diamond tool, weapon or armor piece, and a netherite ingot make its netherite
  kind, enchantments kept. Netherite armor is the toughest there is.
- **Hoppers** take items from the container above them (or lying on them) and pass them on, one at a time, into
  whatever their spout points at (furnaces too: in from the top, fuel from the side, results out of the bottom);
  powered, they stop. **Droppers** and **dispensers** act on a redstone pulse: a dropper drops an item (or puts it
  into a container in front); a dispenser shoots arrows and snowballs, pours and fills buckets, lights fires, primes
  TNT, bone-meals crops and hatches spawn eggs. The **crafter** crafts its 3x3 grid once per pulse. The **composter**
  turns plant matter into bone meal; the **cauldron** holds water or lava; the **chiseled bookshelf** holds six books.
- **Rails** (plain, powered, detector, activator) join up, round corners and up slopes; **minecarts** ride them
  (**E** to get in, Crouch out; walk into one to push it), sped up by powered rails. Minecarts with a chest, a hopper,
  TNT (it goes off on a powered activator rail) or a furnace (fed coal, it pushes itself).
- **Copper**: copper ore in the moons' stone, ingots, and the copper blocks (cut, chiseled, grates, doors,
  trapdoors, bulbs), which weather over the days from copper to oxidized; an axe (**Right-click**) scrapes a stage
  off. **Honeycomb** (**Right-click** with shears on the Circuit Bees' hive: three, once a hive) waxes copper
  (**Right-click** with it, or together in the grid): waxed, it no longer weathers; an axe takes the wax off. The
  **copper bulb** toggles on each redstone pulse. The **lightning rod** draws the storm's strikes within 32 blocks onto
  itself and gives a pulse when struck.
- **Books**: the **book and quill** (**Right-click** to write in it, page by page; signed, it's a **written book**)
  and the **lectern** (**E** puts your book on it, **E** again reads it, for anyone; turning its page gives a pulse
  of power, and a comparator reads which page is open).
- **Fence gates** (oak, dark oak): **E** or redstone opens them.
- **Barrels** hold as much as a chest. **Chests** (buy one, or craft from 8 planks): **E** opens 27 slots, with the same mouse controls as crafting.
  Two chests side by side (facing the same way) are one **large chest** of 54.
  Chests in the ship keep their contents between days (saved with the ship, in orbit: quitting mid-day rolls them
  back with everything else); a broken chest drops everything inside.

## Mobs

Minecraft's monsters spawn alongside the game's own. Like Minecraft, they don't spawn in the light: none appears
within 8 blocks of a placed torch or other light. `[Mobs]` in the config sets how often each one spawns (0 turns it
off; every player needs the same setting).

Every mob drops what it does in Minecraft and gives experience when you kill it.

- **Creeper**: roams the facility (and now and then the moon outside). It walks up to you, hisses and swells, then
  explodes with Minecraft's blast (deadly up close, armor helps). Get away in time and it gives up; it drops
  gunpowder (TNT is gunpowder and sand). Creepers keep away from cats. Struck by a storm's lightning it's
  **charged** (a crackling aura, twice the blast), and whatever its blast kills drops its **head**: zombie,
  skeleton, creeper or piglin. Wear a head in the helmet slot and that mob notices you from only half as far.
- **Zombie**: slow, arms out in front; drops rotten flesh, now and then an iron ingot, a carrot or a potato. One in
  twenty is a **baby** (small and fast), and one baby in twenty rides a chicken (a **chicken jockey**).
- **Skeleton**: keeps its distance and shoots arrows; drops bones (bone meal grows crops; a bone tames a wolf).
- **Spider**: low, wide and fast; climbs blocks (stand on a pillar and it comes up after you); drops string and spider
  eyes. One in a hundred carries a skeleton (a **spider jockey**): kill the spider and the skeleton fights on.
- **By moon**, as Minecraft's biomes: on desert moons (Assurance, Offense) most zombies are **husks** (their hit
  makes you hungry); on snowy ones (Rend, Dine, Titan) most skeletons are **strays** (their arrows slow you); on
  swampy ones (March, Vow) skeletons are **bogged** (their arrows poison) and some zombies **drowned**. Strays and
  bogged drop tipped arrows that your bow shoots. `[Mobs] DesertMoons`, `SnowyMoons`, `SwampMoons` set which moons.
- **Endermite**: now and then where an ender pearl lands.
- **Enderman**: rare and tall. It leaves you alone until you look it in the eyes (a carved pumpkin on your head
  stops that), picks up blocks and puts them down elsewhere, and drops ender pearls.
- **Wither skeleton**: rare, with a stone sword. Very rarely it drops its skull, a trophy the Company pays well for.
- **Slime**: out on the moons at night, far more often on swampy March. It hops after you and splits when killed,
  big into medium into small; the small ones are harmless and drop slimeballs (sticky pistons, magma cream).
- **Witch**: out on the moons at night, and deadly: stand still and it kills you in about eight seconds. It keeps
  its distance and throws a splash potion every two seconds: slowness, poison (never takes the last of your health),
  weakness (your weapons hit softer) and harming. Hurt, it stops to drink a healing potion.
  Drops glass bottles, glowstone, gunpowder, redstone, sugar or sticks.
- **Bee**: the game's Circuit Bees are one big Minecraft bee, angry while it guards its hive or chases you
  (`[Mobs] MinecraftBee`).
- Tall mobs (endermen, wither skeletons) don't fit under a two-block-high roof, Minecraft's hiding spot. A monster
  that wants you behind a wall chews through it, and monsters follow you up stairs onto blocks built onto the ship.
- In the Nether: **blazes**, **piglins** and **piglin brutes**, **zombified piglins**, **hoglins**, **ghasts** and
  **magma cubes** (see The Nether).
- The game's **eyeless dogs** hunt the mod's animals: a cow's calls and steps carry, and the dogs come for it.
- **Phantom**: outdoors after 6 PM (far more often in an eclipse or a storm). It circles silently high over you,
  screeches, then dives and bites. Aim a switched-on **Pro-flashlight** or **laser pointer** at it as it dives: blinded,
  it breaks off and climbs away. Drops phantom membrane (mends an elytra; brews Slow Falling).
- **Golems**, built Minecraft's way and on your side: a carved pumpkin put on a T of four **iron blocks** makes an
  **iron golem** (it fights monsters, drops iron), on two **snow blocks** a **snow golem** (it pelts them with
  snowballs). Golems in the ship at take-off come along.

## The wandering trader

On about one moon in twelve (`[Mobs] WanderingTraderChance`) a wandering trader is out near the ship with two trader
llamas on leads. **E** opens its trades: emeralds (from emerald ore) for sand, red sand, vines, sugar cane, pumpkins,
slimeballs, gunpowder, glowstone, ice, seeds and carrots, and a few things it buys. **E** on a llama takes it off the
trader's lead; ride it again and again (**E**) until it accepts you. A tamed llama takes a chest (**E** with one in hand;
Crouch and **E** to open it). Hit one and it spits at you.

## Animals, pets and riding

On the moons by day, alongside the game's daytime creatures. None of them hurts you; hit one and it runs.

- **Pigs**, **cows** and **chickens** follow you while you hold a carrot (pig), wheat (cow) or wheat seeds (chicken).
  Pigs drop raw porkchops; cows raw beef and leather; chickens feathers (arrows) and raw chicken. Cook the meat in a
  furnace.
- **Horses**: press **E** to get on. An untamed horse throws you off; keep trying (or feed it a golden carrot or an
  apple) and it's tamed. Put a **saddle** on a tamed one (**E** holding it; three leather and an iron ingot) and
  ride where you look: fast, faster sprinting. **Crouch** gets off. Everyone sees you riding.
- **Sheep** in Minecraft's colors (mostly white; grey, black, brown, now and then pink): **shears** take 1-3 wool and it
  grows back. Mutton. **Rabbits** (coats by moon: white on snowy moons, gold on desert ones) hop about: hide, meat,
  sometimes a **rabbit's foot** (brews Leaping). **Squid** swim in the moons' lakes and rivers: ink sacs (black wool).
- **Breeding**: **E** on two animals of a kind, near each other, holding their food (wheat for cows and sheep, carrots
  for pigs, seeds for chickens, carrots for rabbits, golden carrots or golden apples for tamed
  horses): a baby, which grows up after five minutes.
- **Leads** (four string and a slimeball): **E** on an animal ties it to you and it follows; **E** again lets it go.
  **E** on a fence ties the animals you lead to it; **E** on the fence again takes them back.
- Horses come in Minecraft's seven coats and five markings, and wear **horse armor** (leather, iron, gold, diamond:
  more health). **Parrots** come in five colors and dance by a playing jukebox.
- **Wolves** (tame with a bone) and **cats** (tame with fish), one try in three. A tamed pet follows you and catches up
  when left behind; **E** sits it down or gets it up. A tamed wolf fights monsters that come for you and whatever you
  hit. Pets in the ship when it takes off come along to the next moon.
- **Parrots** (tame with seeds): **E** puts yours on your shoulder, a jump sends it off. A parrot imitates monsters
  that are near, so listen to it. It also repeats bits of what players said over voice chat; the host can turn that
  off (`[Mobs] ParrotsRepeatVoices`), and anyone can refuse to have their own voice repeated
  (`[Privacy] ParrotsMayCopyMyVoice = false`). Nothing is recorded to disk.

## The Nether

Build an obsidian frame (Minecraft's sizes; obsidian comes from water on lava, or the store) and light it with flint
and steel. Once the ship has landed, stand in the portal for 4 seconds and you're in the Nether, a second interior far
below the moon. It counts as inside: anyone still there when the ship leaves is lost.

- Portals link Minecraft's way: you come out at 1/8 of where your portal is, in the nearest room with space, and a
  portal appears there. Going back takes you out of the portal you came in by (one in the facility leads back into the
  facility).
- Each day's Nether is one of: a **fortress** (nether brick corridors, bridges over lava, a blaze spawner), a
  **bastion** (blackstone, gold, piglins and their brutes), a **soul sand valley** (skeletons, endermen, ghasts), a
  **crimson** or **warped forest** (piglins, hoglins, zombified piglins), or a **basalt delta** (magma cubes,
  blazes, ghasts).
- It's always eclipsed down there: full of monsters from the start. **Blazes** shoot fireballs; **piglins** leave you
  alone if you wear a piece of gold armor, and trade you something for a gold ingot dropped near them; **ghasts**
  spit fireballs that blow up (and drop ghast tears): hit one back at the ghast to kill it; **magma cubes** split when
  killed. **Zombified piglins** leave
  you alone until someone hits one, then the whole group comes; **hoglins** charge and toss you into the air. A pig
  struck by lightning becomes a zombified piglin.
- Dig it: nether bricks, then netherrack with nether quartz ore, lava pockets and rare **ancient debris**. Smelt the
  debris into netherite scrap; four scrap and four gold ingots make a netherite ingot, which upgrades a diamond tool
  or sword. Fire on netherrack never goes out, and a bed used in the Nether explodes.
- The loot is worth more than the moon's (blaze rods, wither skulls, gold, nether wart for brewing).

## Farming and fishing

- A **hoe** turns dirt or grass into farmland. Wheat seeds, carrots, potatoes and pumpkin seeds grow on it (crops need
  water within 4 blocks); sugar cane grows on dirt or sand beside water. Three wheat make bread. **Shears** carve a
  pumpkin; wear the carved pumpkin, or put a torch under it for a jack o'lantern. Crops on the ship keep growing.
- **Fishing**: a **fishing rod** (three sticks, two string) casts a bobber onto any water, the moon's own or placed.
  When the bobber dips, reel in: cod and salmon mostly (cook them in a furnace), sometimes a pufferfish, junk, or a
  treasure.

## Bows and arrows

- A **bow** costs 200 credits (or craft one: three sticks, three string). Hold **Right-click** to draw it (a second for
  a full draw), let go to shoot an arrow from your inventory. A hit does what one shovel hit does, to a monster or a
  player. Arrows spread a little and drop with distance, as in Minecraft.
- **Arrows** are crafted: flint over a stick over a feather makes four. Flint comes from **gravel** (one time in ten
  when you dig it up: put it back down and dig it again), and gravel only turns up in small pockets underground,
  about one in half a moon of digging. Feathers come from **chickens**. Skeletons now and then drop an arrow or two.
- `[Bows]`: `PlayerBows` (off: no bows for players at all), `ArrowsInStore` (8 for 850 credits; off by default),
  `ArrowsRecoverable` (an arrow that hits a wall can be picked back up there; off by default).

## The elytra

Buy one in the store, wear it in the chestplate slot, jump, and press **Jump** again in the air to glide, with
Minecraft's physics: look down to gain speed, up to climb. A **firework rocket** (right-click while gliding; crafted
from paper and gunpowder, or bought) boosts you, loudly. Hitting a wall at speed hurts. It wears out after about
seven minutes of gliding; mend it at a crafting table with string (each string mends a quarter).

## Crafting & smelting

- **Crafting Table** (buy it, or craft from 4 planks): **E** opens a real 3x3 crafting grid (your character stands
  still while a crafting or chest screen is open). Lay out the Minecraft
  recipe pattern (pickaxe = 3 on top + 2 sticks down the middle, and so on; shaped recipes work anywhere in the grid
  and mirrored) and take the result from the output slot; it goes into your hotbar. No recipe book: you know these.
  **I** opens your inventory, like Minecraft's: your four armor slots, your character (it turns to follow the mouse)
  and the 2x2 pocket grid for small recipes (planks, sticks, crafting table, torches...). With the host's
  `[HUD] BigInventory` on, Minecraft's 3x9 storage grid is there too, and in every other screen (crafting table,
  furnace, brewing stand, enchanting table, chests), so you can use what's stored without moving it to the hotbar.
  Mouse works like Minecraft: left-click picks up / places a whole stack, right-click takes half / places one,
  shift-click a hotbar stack to move it into the grid, shift-click the output to craft as many as you can, click
  outside the window to throw the held stack. Closing the screen puts what's left in the grid back into your hotbar
  (topping up stacks first); whatever doesn't fit drops at your feet, like in Minecraft.
- **Furnace** (buy it, or craft from 8 cobblestone): **E** opens Minecraft's furnace screen: ore/sand/cobblestone/logs
  in the top slot, coal/planks/logs in the fuel slot below, the result on the right (shift-click puts things where
  they go). Raw iron / raw gold become ingots (or sell them as scrap instead: your choice).
- **Brewing** (Minecraft's): craft a **brewing stand** (a blaze rod over three cobblestone) and **glass bottles**
  (three glass), and fill the bottles by right-clicking water. **E** on the stand: three bottles below, an ingredient
  on top, **blaze powder** for fuel (one lasts 20 brews; a brew takes 20 s). Nether wart (it grows on soul sand, both
  from the Nether) makes **awkward potions**; then sugar makes **Swiftness**, blaze powder **Strength**, magma cream
  **Fire Resistance**, a golden carrot **Night Vision**, a spider eye **Poison** and a ghast tear **Regeneration**.
  Redstone makes a potion last longer, glowstone makes it level II (Swiftness, Strength, Poison, Regeneration). Hold right-click to drink; the effects show at the top right.
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
- **I** opens the creative menu instead of pocket crafting: every item, in Minecraft's tabs and order (Building,
  Colored, Natural, Functional and Redstone Blocks, Tools & Utilities, Combat, Food & Drinks, Ingredients), a **Spawn
  Eggs** tab with an egg for every monster (right-click the ground with one to hatch it there), and a Lethal Company
  tab with all of the game's items (scrap too, with a value, and other mods' items). Left-click an item for a full stack
  on the mouse, right-click for one, shift-click to put a full stack straight into the hotbar; click the item grid
  while holding something to put it away (delete it). Items from the menu are real items and stay if you go back to
  survival.
- Double-tap jump to fly: hold jump to rise, crouch to sink, sprint to fly faster; touching down ends flight.
- No damage and no hunger (the hearts, hunger and XP bars are hidden). Falling out of the world still counts.

**Hardcore** (`[Survival] Hardcore = true`, the host's setting): a player who dies stays dead for the rest of the
run, back in orbit too, and watches the others. Once the whole crew is dead it's fired. A totem still saves you.

## Chat commands

Typed in chat (the game's chat key is `/` itself, so the slash is optional). **[Tab]** completes commands, player
names and item names. The host can use them, and anyone the host makes an operator with `/op` (kept with the save).

| Command | Does |
|---|---|
| `/unstuck` | **Anyone**, no `/op` needed: wedged in the level's geometry? Moves you to the nearest free spot within 6 m (once per 10 s) |
| `/gamemode <creative\|survival> [player\|@a]` | Creative or survival (see above) |
| `/tp <player> [player\|ship]` | `tp megg`: you to Megg; `tp thra megg`: Thra to Megg; `tp thra ship`: back to the ship |
| `/give <player> <item> [count]` | Minecraft items by name (`cobblestone`, `diamond_sword`...) and the game's own (`flashlight`) |
| `/op <player>`, `/deop <player>` | Host only: who else may use commands |
| `/keepInventory <true\|false>` | Dying keeps what you carry: it comes back into your hotbar once you're revived (also `/gamerule keepInventory`) |
| `/gamemode spectator [player]` | Fly through walls, invisible to everyone; monsters ignore you and you can't touch anything |
| `/respawn <player>` | Bring a dead player back mid-day: they stand in the ship with full health |
| `/advancements [player]` | **Anyone**: what you (or they) have made and what's still to go |

## Advancements

Minecraft's advancements where this world has the pieces (Stone Age, Suit Up, Diamonds!, We Need to Go Deeper, A
Terrible Fortress, Best Friends Forever...) plus Lethal Company ones (every interior, every moon, the Company, making it
home on the ship). Whoever makes one gets Minecraft's toast, sliding in at the top right. Each player makes each one
once per save; `/advancements` lists them.

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
  dirt, then stone with ore veins: iron in veins of 2-9, rare small pockets of **gravel** 8+ blocks down (flint for
  arrows), redstone 15+ blocks down, diamonds only 30+ blocks below
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
`UseLocalMinecraftAssets`, `MinecraftDirectory`, `MinecraftVersion`, `AllowDiggingShipRoof` (dig the ship's roof and
antenna while landed; patched up on take-off). `[Mobs]` sets how often each mob and animal spawns,
`ParrotsRepeatVoices`; `[Redstone] QuasiConnectivity`; `[Bows] PlayerBows`, `ArrowsInStore`, `ArrowsRecoverable`; `[Privacy] ParrotsMayCopyMyVoice`; `[Survival] Hardcore`. With **LethalConfig** installed you
can change them in game.

All players should run the same mod version and config.

## Install

Install with a mod manager (r2modman, Gale, Thunderstore Mod Manager): it brings BepInExPack, LethalLib and
LethalCompanyInputUtils with it. By hand: drop `LethalCraft.dll` into `BepInEx/plugins/LethalCraft/` with those three
installed. Everyone in the lobby needs the mod, the same version.

Bugs and ideas: **https://github.com/Thrasherop/lethalcraft/issues**.

## Building from source

```
dotnet build -c Release            # .NET 8 SDK; GameDir property points at your Lethal Company install
py tools/gen_textures.py           # regenerates the fallback art (PIL + numpy)
```

Without the game installed, the project compiles against the same libraries from NuGet
(`dotnet build -c Release -p:UseNuGetRefs=true`, which is automatic when `GameDir` has no game). Every push to
`main` builds the Thunderstore zip on GitHub Actions and publishes it as a release (Releases → latest). Once the
Thunderstore token is set up ([docs/THUNDERSTORE.md](docs/THUNDERSTORE.md)), each new version also goes up on Thunderstore.
