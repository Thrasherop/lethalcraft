# LethalCraft: changes by day

A day-by-day log of development: releases, new features, bugs fixed, investigations, and what's waiting on the owner.
Newest day first. Times are local (MDT). Issue numbers are on https://github.com/Thrasherop/lethalcraft/issues.
Player-facing details for each version are in `dist/package/CHANGELOG.md`.

Builds: every push to `main` makes a GitHub release `v<version>-build.<n>` (CI build number n), since build 1 on
2026-10-07. Before that, releases were hand-made zips (USB copies).

## 2026-10-19

**Version 1.54.0** (late afternoon, to `main`): #244 part D, blocks and recipes. Played with `tools/blocks_play.py`
(storage, smelting, colours, ice, paths, lanterns, drops, the grid, stairs, pillars, nether wood, the creative menu),
each block crafted, placed, broken and looked at by day.
- Found by playing and fixed before merging: the block id table and the texture atlas were full (now 2048 ids and a
  48x48 atlas); lanterns were dark (their flame is now lit); stained panes had no colour (the pane mesh had plain glass
  fixed in it).
- Suite on `next`: at speed 8, the usual speed-sensitive tests failed, plus three bed tests and one lapis test. All
  passed rerun at speed 1 or on a fresh game, except lapis: its test expected the old 2-4, and Minecraft's is 4-9 (test
  fixed). General play passed.
- Question open on #244: the screaming goat horns' source (item 80); chainmail's and cocoa's sources (part E).

**Version 1.53.2** (afternoon, to `main`): the owner noticed glass panes looked like long vertical rectangles.
Reproduced (each pane split in two by its post's edge-textured sides) and fixed to Minecraft's pane models: the post
has the edge only on its top and bottom, and its sides with no arm show the glass. Checked by day against a stone wall
on 1.53.2 and on part D's build, where stained panes show their tint.

**Version 1.53.1** (afternoon, to `main`, a hotfix): ship items loaded as other items. The game saves them by
their place in its item list, and 1.53.0 had put 23 items in the middle of it. Found while building part D, whose
new blocks did the same. Reproduced first: a 1.52 save with 19 ship items opened in 1.53.0 came back with 13 of
them turned into other items. Fixed: each saved item's name is saved with it (`ShipItemIds`), and a save without
names is read in its own list (1.52's, or 1.53.0's when it has 1.53.0's enchantment lists). Checked on the same
save: from 1.52, all 19 come back as they were. A 1.53.1 save reloads by name. A save written by 1.53.0 keeps what
1.53.0 saved. One case can't be told apart: a save that went from 1.53.0 back to 1.52 is read as 1.53.0's.

**Version 1.53.0** (early afternoon, to `main`): #244 part C, enchanting. Built in four steps and played with
`tools/ench_play.py` (46 checks, real hits, falls, fire, TNT, arrows and water) and `tools/ench_mp.py` (two games):
- C1: a host-numbered registry for enchantment lists (Minecraft 26.3's 40-odd kinds don't fit the old 3-a-number code),
  26.3's table (enchantability, weighted offers, shelves with air, glyphs, Galactic labels), enchanted books, the anvil.
- C2: every enchantment's effect; XP orbs, Mending, XP dropped on death; Protection is Minecraft's 4% a point.
- C3: durability for armor (16 steps, by chance), the bow, rod and flint and steel; bars; grid repair; glint on armor.
- C4: the 17 other trim templates, by moon (the owner's answer), with copy recipes.
- Found by playing and fixed before merging: enchanted books losing their enchantments in the hotbar, the table
  refusing a book from a stack, no orbs on death (the host ignored a dead player), weak Knockback, Depth Strider only
  on entering water, starvation and wither wearing armor.
- Owner's answers on #244: Silk Touch gives scrap ores as blocks (they build with the ship); templates in facility
  loot by moon. Question open: the screaming goat horns' source (part D, #80).
- Suite on `next`: 751/765 at speed 8; the 14 failures all passed rerun at speed 1 (one, the vine's climb tip at the
  fox's spot, passed on a fresh game too). General play passed.

**Version 1.52.0** (mid-morning, to `main`): #244 part B, brewing, in three steps, each played before the next:
- B1a: the ingredients and foods. The glowstone block now breaks into dust; melons and their stem; mushrooms; the
  fermented spider eye and glistering melon; stews; honey bottles from a hive; milk from a cow; the enchanted golden
  apple with Resistance.
- B1b: 26.3's brews (mundane/thick, water breathing, weakness, slowness, invisibility, healing, harming) and splash
  potions (thrown by players, 4-block falloff). Invisibility follows the owner's rules for which monsters it fools,
  and was checked with two games (the host gone from the client's view and back). Wither from wither skeletons;
  green and black hearts.
- B2: effects on monsters (tipped arrows, splashes; undead reversed), swirls, effect polish, the HUD's two rows and
  the [I] screen's list, brewing stand hoppers and comparator.
Played with tools/brew_play.py, tools/effects_play.py and tools/ff_fall.py (water breathing: 40 s on the bottom of a
well alive, 12 s without and drowned). Found on the way: Feather Falling had never reduced a real fall (the game sends
it as Gravity without its fall flag): Feather Falling IV boots, 25 m, 50 damage before, 20 now. Also the creative
grid's missing glints, and potions without a "Drink" tip. Obtainability: 762 items, none without a source.
Suite x8: 757/768, 19 min. The new ones were the Wither effect itself: it ticked during t_mobs' next check, so the
test now clears effects between monsters. The rest are the known flaky ones (horse jump, fishing, boat, vine,
dog, beds, enderman); all passed rerun alone. General play 7/7.

**Version 1.51.0** (morning, to `main`): the first part of the thoroughness round (#244), 34 places where our numbers
or rules weren't Minecraft 26.3's, each played in the game (tools/thorough_play.py, 78 checks, a section per area:
crafting at a table, stacks, furnace, food, pistons, dispenser, pearls, panes, tool wear on a real zombie, ore XP,
the trader, drops, doors, composter, cane, minecart, redstone torch, witch, arrows, enderman, the Nether's mobs,
brewing). The play found eight bugs of ours, now fixed:
- #248: since 1.49 nothing could be bought with emeralds (an emerald is a single item since the ore rework; the
  trade only counted stacks). The suite's trade check had never clicked.
- #249: every Minecraft mob paid twice its XP.
- Pistons crushed hoppers and lecterns.
- Several bows given at once came out as one stack.
- Goat horns stacked.
- The witch's Swiftness didn't speed it up.
- Long tooltips ran off the screen.
- [E] on a block behind water did nothing (and t_brewing failed on it now and then).
Suite x8: 761/767, 19 min. The six: boat and bed placement, a horse's jump, fishing (all pass rerun alone), the ghast
and the dog (the long-session ones).
General play (tools/general_play.py) 7/7 after fixing the script: it fought bare-handed (after a clear, the sword
lands in the second slot), it had a kidnapper fox's bush for the fight, and the pillar stood across two cells.
Noticed: the test save's ship floor has stacks lying on it (left by test runs) that a landing tops your stacks up
from; it looked like duplication at first (it isn't: Minecraft's pickup).

**Version 1.50.0** (early morning, to `main` for the owner): the owner's 1.49.0 playtest, worked overnight. Each bug
reproduced in the game first, the fix played, the commits have the steps and shots.
- #214 (reopened: I had called it fixed from one creeper on one staircase): the game's monsters' navmesh area mask
  left out the navmesh baked over blocks; a Thumper on the old build circled a tower's foot for 40 s (path invalid),
  on this one it ran up the steps to me, on flat ground and by the catwalk (the owner's spot). The owner's creeper
  failing isn't reproduced on these towers (a creeper climbs them on both builds): asked on the issue which tower.
- #245: boats sank on March: the game's 15 s "stuck, let it swim" rule then put the boat on the nearest navmesh (the
  lake bed), its clock running 12x fast for a mob that syncs every frame. Bank drop: under on the old build 3/3,
  afloat on this one 3/3; still 44 s empty and ridden: -3.16 the whole time.
- #247: March's lake bed near the shores is ordinary walkable navmesh (only the middle is Water): animals strolled
  there 1-2.6 m under. Strolls to dry ground only, and a mob in deep water floats, head out; 180 s watches: none under.
- #246: a ridden horse is moved by a character controller (steps a block, Minecraft's gravity, the jump bar and each
  horse's jump strength) instead of along its navmesh; 1-wide stairs on the ground: stuck at the foot before, up now;
  a full jump matches Minecraft's height formula.
- #240 elytra wind, #238 lectern book (and the lectern's top, which slanted away from its reader), #237 shelves (the
  atlas now keeps 32-pixel tiles for them), #239 the 26.1 rabbit, #242 creative scroll drag, #243 banner copy, /gm.
  Shelves, lectern and rabbit compared with real Minecraft 1.21.11 run offline (26.3 needs Java 25, not installed).
- #241 (T makes you dance in a menu): not reproduced (T, held or typed, in the creative menu, its search box and the
  2x2 crafting screen); asked the owner about their key bindings.
- Thoroughness checklist #244: 150 items, checked against Minecraft 26.3 (its data and decompiled code; corrections
  posted on the issue); the owner ticked 142. Part A (34 wrong numbers and bugs) written on `feature/thorough-244a`,
  compiled and unit-tested, not played yet: it goes in the next batch.
- Suite (x8, 19 min): 760/767; rerun alone: cow, parrot and boat pass; t_horse_climb and t_moon_nav fixed (a weak
  horse can't clear two blocks; a Thumper gives up on someone out of sight, so the path is checked); the vine's hover
  is flaky (passes and fails on the same build). General play (tools/general_play.py, speed 1): mined, pillared,
  crafted at a table, fought a zombie, chest in and out, took off and landed, through a Nether portal and back.

## 2026-10-17

**Version 1.49.0** (night, on `next` only: the owner plays it before it goes to main): #220's ore rework (built
2026-10-16 to the owner's design, held since; merged onto 1.48.2) and #235, found playing it: digging straight down
from a shaft's rim, the swing finished again and again without breaking anything (a strip of the level's mesh left
at a dug cell's edge; the dig picked the dug cell). Reproduced at three spots on Experimentation, fixed, played.
Ore played by hand (tools/stripmine.py, Experimentation, level 1x2 tunnels of 30 blocks): ~200 blocks exposed per
tunnel; 8 down: 3 iron, 20 down: 5 coal, 32 down: 2 iron; no gold/redstone/diamond seen. Census (64^3, default
0.15): a vein in ~19% of chunks; ore ~1.3% of stone near the top. Works as designed, stingy: the owner's knob.
Suite (x8, 19 min): 753/757; the four failing tests pass alone (49/49).

**Version 1.48.2** (evening): #234, found by 1.48.1's suite, was a real bug: a hoglin's toss left the game's
"falling from a jump" flag set (only its own jump clears it), and no fall did damage until the next jump. Reproduced by
falls after each part of t_creeper_mobs (only the toss did it), fixed, played (a toss, then a 40 m fall hurts).
Suite (x8, 21 min): 747/755, t_totem passing in it now. Failures rerun alone on a fresh launch: t_redstone_tab,
t_fence_lead, t_creeper_mobs, t_variants, t_flying_machine pass; t_ghast_flight failed twice (at two different steps,
depending on the Nether generated) and passed 6/6 the third time: flaky, as seen once in 1.48.1's suite too.

**Version 1.48.1** (afternoon): the owner's answers (melee stays the shovel's range; collars dyeable) and "give
everything unplayed a play". Played by hand, each fix reproduced first; the commits have the steps and shots.
- #233: dyed collars (wolf and cat): the owner's dye on [E], the same colour does nothing, kept through takeoff and a
  save and relaunch.
- Signs and books, compared with real Minecraft (1.21.11 run offline at 1280x720, its 26.3 source decompiled): the
  owner found the sign's text far too small and the book off. Both screens rebuilt on Minecraft's layout (McGui:
  its GUI scale, pictures, font, buttons, word wrap); side-by-side shots match. The chat box's "Press '/' to talk."
  hint showed on empty sign lines and book pages.
- Shelves: a wall on a shelf showed nothing; a powered shelf's swap left "Stone x0" ghosts and filled the wrong
  slots; a row of three lost the last item to the first free slot.
- Loom: pattern tips were internal ids ("cross"); now Minecraft's names ("Red Saltire"), and the output slot shows
  the woven banner.
- Pets: a sitting wolf hit by its owner stayed sitting (Minecraft's wolf and parrot stand up when hurt).
- Played and fine: the dye pack (ordered, delivered, two belt bags of eight dyes x16), hanging signs (under a block,
  on a wall, not on the ground), sign dye and wax, the outpost chest (Minecraft's loot, goat horn sounds), Dine's dark
  oak and March's mangrove (chopped; a 2x2 dark oak and a propagule grown with bone meal; roots, muddy roots), trims
  in amethyst and resin (icon and worn), a geode (dug into, buds grew, a cluster by hand gave 2 shards), #232 deaths
  (anvil, slain cat and parrot, left behind), and the cruiser breaking a vined tree on Vow (its vines went with it).
- Noted, not changed: a patterned banner is named "Banner" (Minecraft keeps "White Banner").
- Suite (Experimentation, x8, 20 min): 747/756. Each failing test rerun alone on a fresh launch: t_tall_gap,
  t_nether_mining, t_ghast_flight, t_decorated_pot, t_trader, t_creeper_mobs pass; t_totem's worm-throw landing does
  no fall damage when run after t_creeper_mobs, on 1.48.0 too (alone: 9/9 on both): filed #234.

**Version 1.48.0** (night): the owner's 2026-10-16 batch, #223-#230 and #232, played by hand (the owner gave the
desktop and Steam for it). Each fix below was found by playing, reproduced, fixed and played again; the commits have
the steps.
- #227 ship mobs: a boat placed in orbit was left on the moon at landing; restored mobs weren't saved again; a
  shoulder parrot wasn't saved; a mob back from the save couldn't be broken or killed in orbit (KillEnemy threw:
  thisNetworkObject is set by the game's Start, which a rider never ran); a trader's llamas rode off with the ship.
- Placement: an anvil went down inside a boat; any block with a collision shape is now refused where a mob, boat,
  minecart or player is (Minecraft's isUnobstructed).
- Icons: banners, signs and hanging signs showed a plank block in the hotbar.
- #223: saplings and sugar cane can be planted on the moon's own dirt (a block on a slope sits at its own height in
  the cell); Experimentation's leafless trees can be chopped (tagged Wood, not Tree); a felled tree takes its vines.
- The suite found one more: #230's resin clump case had swallowed the torch/lever/button/hook/ladder support rule
  (vines too): they hung on the block under them. And a popped vine dropped without shears.
- Played: the anvil (repair, rename, wear), name tags on a cow, a parrot and a creeper (kept through reloads), boats
  and minecarts with chests through reloads, pet deaths in the chat (fire, a creeper, left behind), host bows in a
  two-player LAN game (three cases), trims, a geode (dug into it), the spyglass, resin, banners and the loom
  (through a reload), the crossbow, saplings, bone meal flowers, signs, a pillager patrol and its captain's drops.
  Not played: the outpost's chest, shelves, mud, Dine's and March's woods.
- Suites (Experimentation, x8): 743/756 first (the hand session's chestplate under the damage checks, the vine bug,
  flaky spots); 730/744 after the fixes, a run where [E] checks failed in a row late in it. Every failing test rerun
  on its own on a fresh launch: t_enchanting 11/11, t_enderman 9/9 (once 8/9: #193's "puts it down"), t_nether_exits,
  t_parrots, t_pets, t_mobs (no armor), t_block_families, t_redstone_tab(2), t_ladders all pass; t_trader passes at
  x1, fails at x2 (the chest's [E] while still riding): capped at 1.
- Asked the owner: melee reach (the sword reaches ~1.7 blocks, Minecraft 3); filed #233 (no collar on tamed pets).

## 2026-10-15

**Version 1.47.4** (night): #193's two flaky tests turned out to be two real bugs; #152 checked.
- t_enderman's "puts it down" failed run after run: an enderman puts a block only into a grid cell of clear air over a
  solid one, and where the floor runs through the middle of the grid's cells (the grid's offset changes each landing)
  none qualified: it carried the block for good. Now it falls back to the floor's own sub-grid (seen on the server
  room's floor). The test also stopped the player's gaze from provoking it while it fetched the block.
- t_tall_gap: the low-ceiling check looked only above the mob's centre, so an enderman stopped half under a two-high
  roof (2.1-2.2 m, edge at 2.1): its body's width counts now (2.6-2.9 m, four runs).
- #152 the wandering trader: everything on the issue is in; a hand look at its trade screen found the seventh offer on
  the hotbar's label (fixed). The decorated pot: an arrow (the real bow) shatters it; a piston breaks it whole.
- Suite (Experimentation, x8): 720/738, a rough landing; rerun on their own at x2: all passed but t_pets (the cat and
  the creeper) and t_ladders ("Climb : [E]"), which then passed twice each on next and on main: flaky. General play:
  ores through a chest ($49/$54 in and out), the bow and the zombie, eating, the sound sliders, the parrot through
  orbit, the Nether (a ghast hit me once), 0 errors; its building step aimed at blocks the test save keeps in the ship
  (placing there by hand works).

**Version 1.47.3** (evening): the owner's #221, and #160 finished.
- #221 keep ship builds when fired: `[Blocks] KeepShipBuildsWhenFired`, off by default. Tested by a real firing (dev
  `deadline last`, then leave with the quota unmet): off, the build goes as before; on, three blocks and a chest stay,
  the chest emptied, and the game relaunched into the save still has them. Found: each firing placed one more starter
  supply chest; now the share goes back into the same one (two firings, the same five chests).
- #160 the decorated pot, the last of Minecraft's Redstone tab, from 26.3's own code (decompiled): its model and
  textures, one stack inside ([E] puts one in), whole by hand, shattered into bricks by a tool, hoppers and comparators.
  Bricks (smelted dirt, as dirt already stands in for clay) for it. Played by hand: furnace, crafting grid, placed and
  seen from four sides, filled, broken both ways, a hopper over it, a comparator by it, the creative menu. Its first
  look was grey: its tiles weren't in the atlas.
- Suite (Experimentation, x8): 738/753; rerun on their own at x2, all passed but t_enderman, which fails when a
  second run finds the first run's enderman already provoked (on main too: #193). General play of 1.47.3: crafting,
  ores through a chest ($43 in and out), bow and food, sound sliders, a parrot through orbit and landing, the Nether
  (two ghast fireballs killed me without god mode: Minecraft's damage), 0 errors in the log.

**Version 1.47.2** (afternoon to evening): #207, #191 and #222, the owner's #221 filed, #216 tried.
- #207 creative breaks protected ground: it now digs entrances, the world floor, the Nether's edge and portal, door
  frames, interactables and vents (still not the ship, the uncuttable shell, invisible walls, or the Company when the
  host turned digging off). Found on the way: the world floor's bedrock had no collision at all (natural bedrock
  stands for the protected geometry behind it, but there is none behind the world floor): a survival player who dug
  down to it fell through into the void. Reproduced (y -261 to -428 in 3.5 s), fixed; the same for the Nether's edge.
  Below the floor, creative digs on layer by layer until the moon's out-of-bounds trigger kills (Minecraft's void).
- #191 ghasts fly: hidden down a hole with nowhere on its navmesh to see from, a ghast takes off and hovers where it
  sees you, then lands again when it gives up. Same bed, same build: flight off, 0 shots in 40 s; on, 8. Finding a
  fair test bed in the Nether took most of the afternoon (rooms 7 m high, corridors lower, invisible ramp boxes, rock
  that has no collider until it's dug). Found in hand play: hovering over the hole, its fireball caught the rim and
  killed it; it now fires along the line it saw you by, and holds fire when the burst would reach it.
- #222 (filed today, the owner): phantoms slid along inside blocks. They never checked for anything in their way;
  6 of 310 samples were inside the roof or wall of a test hut, 0 of 348 after. A swoop that hits something ends, and
  none under a roof.
- #216 the zoomed-out terminal: TooManyEmotes installed (the owner's OK) in its own profile. Typing `moons` in the
  terminal fires its random-emote key (M): that's the line in the owner's log, but TooManyEmotes then refuses it and
  the terminal looks normal. Four ways tried, none zoomed out: asked the owner for a screenshot.
- #221 filed: a config option to keep ship builds when fired (per save, not per run).
- #211 (the Nether lag) lead: the owner's log had 29,240 "Failed to create agent" warnings, each with a stack trace.
  Found how: the game turns its owner's navmesh agent back on every frame; a mob that moves itself off any navmesh (a
  squid, a phantom, a ghast in the air) turns its own off, and turned back on away from a navmesh it logs that. Two
  phantoms: 4 warnings in 20 s before, 1-2 after (at spawn). The same thing had kept the "flying" ghast's agent on,
  holding it to the navmesh: its flight is real now (it hovers and shoots; 0 warnings). Whether this was the owner's
  stutter, their next log's [hitch] lines will say.
- Suite (Experimentation, x8): 719/736. Rerun on their own at x2 (with the agent fix): all passed but these. The
  ghast test was too strict (a ledge by the hole can see in: it now checks the shot, flying if it must). t_bedrock's
  floor check was a cell off (the player held still). "Two beds drop": not a duplication; by hand exactly one drops,
  and the test counted a bed kept in the test save's ship (it counts nearby beds now). t_totem's worm landing: flaky
  (passes alone on next and main, twice each). Enderman and tall gap: #193.
- General play of 1.47.2 (shots/pilot_gen_*): land, craft a pickaxe, ores through a chest ($46/$56 in and out), the
  bow strafing (left goes left), eat, the sound sliders, a tamed parrot through orbit and landing, blocks placed and
  broken, the Nether (a ghast shot at me), 0 errors in the log. Its first dig was on the ship's ramp and its Nether dig
  by the portal (both protected): not tried this time.

**Version 1.47.1** (morning to afternoon): the owner's word on two of last night's leftovers.
- #214 monsters onto placed blocks: MoonNav, a navmesh over each group of blocks built on the moon, with step links
  (the ground beside a block to its top, from the blocks themselves) so monsters climb a whole block. Three findings on
  the way: a block's obstacle cut the top of the block under it out of every navmesh (blocks on blocks have none
  now); Unity dropped a stair's narrow step top as too small a region (0.2 m2 now); a taller bake agent made the
  ship's low spots unwalkable (kept at the game's metre). A creeper provoked 12 m off: off, it stayed on the ground
  three landings out of three; on, it climbed to the top three out of three.
- #215 crouching into a house: the owner said it was the stairs side. Reproduced: a house a block up, a stair as the
  step, a lintel: in, stopped on the stair; out, fine; crouched, fine. Unity's controller lifts the capsule by its
  step height before moving, and next to stairs the step is 0.78 m: the lift hit the lintel. The step now never
  exceeds the room overhead. Before/after on fresh landings, twice each.
- #205 the phantom test finds open sky first. #216 (emote on the terminal): it's TooManyEmotes' own emote path
  (vanilla already refuses emotes in the terminal); waiting on the owner's OK to install it here.
Suite (Experimentation, x8): 731/745; every failure passed on its own at x2 except the known enderman flake (#193).

**Version 1.47.0** (night, 00:15 to 05:30): the owner's game night with friends (1.45.5/1.46.0, five or more
players, MoreCompany and others) brought a long list. Thirteen issues filed (#208-#220) and worked through:
- #208 (critical) ores lost their value in chests: a slot kept only key and count; now the key carries the value
  (scrap_diamond_ore#45). Reproduced first: Raw Gold $30 in, $0 out. After: $43 and $45 and $108 through the chest and
  the big inventory; a valued diamond still crafts, raw gold still smelts.
- #209 (critical, Lum's report) hotbars out of step between the player and the host: reproduced on two instances
  by freeing a slot while a pickup is in flight (each game picks its own "first empty slot"). The host then checked
  the wrong item (stone broke, nothing dropped, with an iron pickaxe in hand). SlotSync: each player's own layout
  wins, corrected within about 4 s, logged. A 170-step fuzz of chest and hotbar actions found no false
  corrections. Also: shift-clicks out of a chest no longer take more than fits.
- #210 (critical) a parrot left behind on takeoff: pets were noted, deleted with the monsters, re-made at the next
  landing. Now they ride along in the ship through takeoff, orbit and landing (the same parrot).
- #212 a totem against a masked: after the save the masked's own code made a masked copy of the player (on the host
  even when a client was saved), and the masked re-grabbed the moment the shield ran out. Now it lets go everywhere,
  no copy, and monsters by you are stunned 3 s. Shown on two instances: 2 masked before, 1 after.
- #213 drawing a bow or eating pushed you backwards when carrying 27 lb or more (the push was a full-speed walk's,
  the walk is less when carrying): now a fifth of the walk, measured at exactly 0.20.
- #217 cobbled deepslate and blackstone in stone tools, the furnace and the brewing stand (Minecraft 26.3's tags).
- #218 advancements in Minecraft's own chat (Minecraft's font from the jar, its grey bar, fading).
- #219 Minecraft's Music & Sound Options from the pause menu (disc volume and the other categories).
- #211 (Nether lag) and #215 (crouching through a doorway), #216 (terminal zoomed out): not reproduced here (written up
  on each); #211 gets a [hitch] log line for every long frame so the next game says why.
- #214 (mobs onto placed blocks): work in progress on a branch (a navmesh over moon blocks, step links), not in 1.47.0.
- CLAUDE.md: the owner's new rule, hand play before every merge and every release, and what that play looked like.
Suite (Experimentation, x8): 710/725; every failure passed on its own at x1 except the known phantom spot (#205).

**Version 1.46.0** (just after midnight): a new Thunderstore icon, a furnace and crafting table over a pit dug in front of
the facility door. The owner played through several candidates as local zips (the ship, a snow golem, the facility,
a trench, then the pit), aiming for what shows at a glance that you can *mine and build*. It also ships creative
digging around entrances (#207's quick part, which the owner used to dig that pit). Creative breaking all
protected ground stays open in #207.

## 2026-10-14

**Version 1.45.3** (evening, #196 to #198, #203, #204): the owner asked for the totem to save you in place (#204,
high priority, their idea in full: no teleport by default; full health; cleansed; a 1-2 s "suzu"-like shield; stamina
refilled and free; a snare flea dies with the pop; a worm's bite still throws you so the landing can kill). Played: a
40 m fall (saved at the bottom); a real snare flea (its grab dropped the totem along with everything else, so it could
never save you from one: now the totem stays, and a deadly hit popped it and killed the flea); a real Earth Leviathan
(at first no throw: its body over the bite held you down; now moved 6 m clear, thrown about 36 m, landed on two
hearts). The throw was tuned by height and landing damage; it fades frame by frame, so t_totem runs at speed 1.
Also: the enchanting table's 12 px shape and floating book (BookModel and the table's animation, Minecraft's numbers;
looked at near, far and from the side at the Company, as the owner suggested for building), the piglin head's parts and
head icons from Minecraft's skull models, slime and honey shells, one Minecraft stack-size table (creative had handed
out 64 water buckets). Suite 713/729: each failure passed rerun on its own except the phantom's (its dive blocked by a
building by the test spot: #205); a vanilla terminal TMP error is now ignored by the log check.

**Version 1.45.2** (afternoon, #195, #199 to #202): played by hand, as the owner asked ("actually play"; "mostly
right at a glance doesn't mean right": the composter icon's see-through hole), and every icon compared with Minecraft's.
- **Icons (#195)**: the inventory's renders from the wiki read pixel by pixel in the browser. Our block view was mirrored
  (fronts on the left), its sides 10% short (Minecraft's GUI transform is 30/225 degrees at 0.625: worked out and used
  exactly) and lit flat (0.86/0.72; Minecraft's GUI shader with its two lights gives 0.81/0.62). Stairs, walls, fences,
  gates, buttons, trapdoors, plates, slabs drawn from Minecraft's own inventory models with each model's gui display:
  silhouettes now identical to the wiki's. The bed: Minecraft 26.1's block model (legs, headboard, foot end), its
  textures cut from the older entity texture for older Minecraft (matched pixel for pixel); its icon identical to the
  wiki's. Dispenser, dropper and barrel fronts had never been drawn (now in every facing). Found on the way: #196
  (enchanting table is a full cube with no book), #197 (piglin head), #198 (slime/honey blocks).
- **Search in the creative menu (#199)**, the owner's request: Minecraft's Search Items tab.
- **Hand play**: a lever/dust/lamp circuit (works); every held item looked at (#200: the lever held as a dark box, a
  hopper as a block, the lamp held lit; creative couldn't swap into a full hotbar); a 2x2 sticky-piston door built
  block by block (works, open, shut, walked through); a minecart line with powered rails, a hill and a detector rail,
  ridden (#201: W didn't push the cart, the detector's pulse too short to light its lamp; creative used up minecarts,
  animal food, buckets...).
- **Water (#202)**, the owner's note: reproduced (water off an 8-high pillar on the ground in 0.58 s, Minecraft's 2 s;
  two blocks a step on the floor): each step read its own results. Now a block a step (1.86 s; t_fluids checks it).
- The one-moon suite's first run lost the second half: the new /gamerule doDaylightCycle (tester commands) restarted the
  clock the suite had stopped; the day ran out and the ship left. Fixed (the rule restarts only a clock it stopped).
  Second run 727/738 in 20 minutes; each failure passed rerun on its own (flaky ones, #193; my play session's 64 beds
  left in the ship counted as drops in t_beds).
- Owner's tip: do the hand building at the Company building (no takeoff, big flat roof).

**Version 1.45.1** (morning, critical, #194): the game left idle in orbit after the night's work had crashed at 05:49.
Its log: 27,921 rounds of the ship's roof being cut again. 1.45's kept roof (#179) re-cut adds a navmesh hole marker
under the moon's block root; in orbit that root is dropped as "left outside the moon", which resets the carver, which
re-cuts the roof and makes the root again, every frame. Reproduced on 1.45.0 (263 rebuilds in 20 s of orbit); the
marker now goes under the ship's root (0 in 20 s; t_ship_roof 8/8). Also: a leftover UnityCrashHandler from the
crashed game kept Steam saying the game was already running; ended by hand.

**Version 1.45.0** (early morning): the owner's playtest of 1.44.0 (25 issues, #166 to #190), fixed one by one and each
played by hand in the game first (the owner's new rule, CLAUDE.md: tests and unit tests aren't evidence; play every
feature as a player would, longer the more features there are, and build something real with them). What it took:

- **Pistons (#185)**: the wiki's 2x2 flush door (the "Jeb door") built from its schematic and watched tick by tick.
  Its top south-facing pistons fired at once (shoving the door blocks into the wall, jamming the first stage) because
  the mod fired any powered piston on every recompute; Minecraft's piston looks at its power only on a block update,
  and those pistons are quasi-connected but out of the dust's update reach (a BUD) until the first stage pushes them
  under it. Block updates now reach as Minecraft's (neighbours; dust and torches two out; levers, buttons, repeaters,
  comparators, observers into what they power); a moved block landing updates around it. Two intermittent failures on
  the way: a piston evaluated while the block in front was still sliding in lost its update (now it tries again).
  The door closes and opens 4 times out of 4; 75/75 redstone checks; new test t_piston_door.
- **Drawing (#184)**: the mesh cache's key packed a block's state into 8 bits next to a 16-bit id: a lever that's on
  or an extended piston drew as a copper block 256 ids on. **Icons** (#167, #181, #183) drawn from the block's own
  mesh in software, pixel for pixel, as Minecraft's inventory does.
- **Mobs**: the hurt flash tinted only parts still sharing the first part's material (#189: a property block on every
  part now, part of the glow red as Minecraft's 30% mix); swords held as ItemInHandLayer and the handheld display
  transform put them, worked through in Minecraft's space (#166); cats sit part by part as AdultFelineModel (#173);
  wandering as RandomStrollGoal (#172: they had marched node to node across the map); spiders climb blocks (#182: off
  the navmesh, walls read off the block grid; a first try walked straight across a catwalk gap and fell 7 m, so the
  climb starts only at the foot of the blocks). The Butler errors (#182) were an inside monster hatched outside taking
  its kind's (inside) AI nodes.
- **Ghast fireballs** hit back (#187), **the bow** in hand (#188) and its slowdown (#190), **the hotbar wheel** (#168,
  a flick's notches came as one event), **book text** where BookEditScreen writes it (#180), **powered rails** over
  slopes (#176), **ship ladders** (#174: the game won't start a climb in the ship's room), **the ship's roof** kept
  dug (#179), the **advancement toast** as Minecraft's (#171), **creative tabs** in Minecraft 26.3's order (#177: read
  off CreativeModeTabs, item ids as data), the **Mapper's icon** drawn from its model with an off-screen camera (#178).
- Minecraft's code read for all of this: 26.3's client jar decompiled locally (IntelliJ's Fernflower), for reference
  only, not committed.
- Not changed, asked on the issues: #170 (the smithing table's order already matches 26.3's SmithingMenu) and #175
  (powered rails join on flat ground here; waiting on how they were laid).
- Filed while testing: #191 (a ghast stops shooting once it loses sight of you) and #192 (a ladder can hang off
  another ladder).
- One-moon suite on `next`: 708 of 720 checks in 20 minutes; of the 12, three were real and fixed (a chest facing up paired with itself, the roof test meeting the roof the save keeps dug, the terminal helper pressing during the start-of-game animation), five passed at speed 1, and the enderman ones fail the same on 1.44 (#193).

**Production rollback** (night, owner's request): 1.44.0 had gone to the store by mistake. Thunderstore won't take an
old version number again, so 1.25.0's code went out as **1.44.1**; the next release from `main` is 1.45.0.

## 2026-10-13

**Version 1.44.0** (night): the second half of the redstone tab (#160: hoppers, droppers, dispensers, crafter,
composter, cauldron, chiseled bookshelf, fence gates, rails and five minecarts, copper and its bulb, the lightning rod,
the lectern and books), the wandering trader (#152) and large chests. Its first test runs found the batch half
working: books opened on left-click (the game's activate) not right-click; a minecart's right-click sat behind
Builder's catch-all for plain items, so carts couldn't be placed; a still cart on a powered rail rolled into the dead
end; [E] on a mob took whichever mob RaycastAll listed first (the trader's llama behind it); the trader test pressed
[E] mid-grab (given emeralds are picked up one by one); fence gates were #164. Then the owner's completeness rule:
copper got its waxed kinds, honeycomb (shears on the Circuit Bees' hive) and the raw copper block. Models checked in
screenshots (blocks, carts, trader, llama).

**Version 1.43.1** (night): fences, walls and glass panes could be walked through once another was placed beside
them, and couldn't be aimed at (#164). Found testing 1.44's fence gates: a block's look update removed any mesh
collider, meant only for a mold's, and Unity's deferred destroy took the one the same update had just set up, every
other update. Reproduced with real input on 1.43.0 (walked through all three), fixed, and t_joined_collide added.

**Test suite review (evening, owner's request).** The all-moon run had grown to every test on every moon: over four
hours, stopped part way. Owner's rule since (CLAUDE.md): a run over 30 minutes gets optimized or trimmed, over 45
minutes at most once a day. The all-moon run is now only the moon-dependent tests (`MOON_TESTS`) on each moon; the
one-moon run every test, 20.7 minutes before the review and about 17.5 after. Every one of the 103 tests was read:
none was redundant outright. Recipe-only checks went to unit tests; tests doing the same thing twice were merged
(t_nether into t_portal_links, t_pumpkin_head into t_enderman, t_creative_loot into t_creative, the ore tests into
t_ore_drops); floors and frames go to the game in one batch; dev knobs for wool regrowth, brew time and phantom
circling; polls for fixed waits. Found on the way: the typed-terminal check never ran in a suite (it needed orbit,
it's an orbit test now); two checks couldn't fail; t_bows' wall, saved with the moon, hid t_witches' witch on the
next run; three flakes (a ghast firing before its shots were counted, a pig standing between the player and the fence
post, a fall through rock beating the facility switch's tick). Details: docs/TEST_708 of 720 checks in 20 minutes; of the 12, three were real and fixed (a chest facing up paired with itself, the roof test meeting the roof the save keeps dug, the terminal helper pressing during the start-of-game animation), five passed at speed 1, and the enderman ones fail the same on 1.44 (#193).md. Also #161 (Minecraft
26.x renamed its mob textures: pigs, cows, chickens and blazes untextured; found by @dvuiodvojd) and #162 (the
repeater's delay torch moved the wrong way) for 1.43.

**Version 1.43.0**: blocks and the first half of the redstone tab. Block ids were one byte with 117 used and the
families needed about 150 more, so they're two bytes now (network, ship saves with a format marker so old saves load).
Stone families with walls (a new shape: posts and arms, Minecraft's post rule) and fences; granite, diorite and
andesite as patches after the ore veins (the ores' veins stay put; the yield tests calibrate them too); deepslate below
y -100 (the facility), its ores dropping the stone ores' own scrap through DropAs; vines as ladder-shaped blocks on
tree trunks found by name at landing; the smithing table (its own three-slot screen; the crafting-grid upgrade is
gone) with netherite armor; leads tied to fences. Redstone: quasi-connectivity (BUD timing not emulated: pistons
re-check on any redstone change), doors by redstone (on the change only, so [E] still works), iron doors, plates and
buttons by shape (weighted plates count things; merged stacks count once, as Minecraft's), target, daylight detector
(a new 6 px shape), tripwire (string places a tripwire like seeds place crops), trapped chest (a viewing message to
the host), barrel. Every piece played in game with tests (t_block_families, t_redstone_tab, t_fence_lead).
Still to come on #160: dispensers, droppers, hoppers, the crafter, rails and minecarts, fence gates, the lectern,
chiseled bookshelf, composter, cauldron, decorated pot, copper bulb and lightning rod (copper).

**Version 1.42.0**: the farm-animal and golem half of the owner's list. Sheep (#148), rabbits (#149), squid (#150),
breeding and babies for every farm animal, horse coats, markings, armor and foals plus leads (#145; tying to fences
waits on fences), parrot colors and dancing (#151), iron and snow golems (#140: the pumpkin builds the golem instead of
being placed, so no block is left behind), phantoms (#141: circling, screech, dive; a Pro-flashlight or laser pointer
on a diving one blinds it). Then, at the owner's request, a look at every new mob's model in game: the skeleton's bow
was held flat (now upright when aiming), the snow golem's arms were flattened by the walk animation and then pointed
up (now down and out, as the owner wants), and horse armor's legs stood still while the horse's walked (the armor and
markings layers have their own leg copies; they now copy the walking legs' turn). t_golems_phantoms had aimed the
flashlight with pilot's stepwise turning, too slow for a 15 m/s dive; it now aims at once. Not done: the iron golem's
poppy (no flowers yet).

**Version 1.41.0** (morning): the owner's long list after reading docs/MISSING_FROM_MINECRAFT.md, filed as #136-#160,
plus the new rule (CLAUDE.md): everything new comes complete, Minecraft's variants and full drops. This batch: full
drops and kill XP (#139), golden tools, Poison and Regeneration potions; zombie/skeleton variants by moon (#136,
rolled at a natural spawn like Minecraft's), babies and chicken jockeys (#137), spider jockeys (#138); charged
creepers and mob heads (#142, worn heads checked by hand: a zombie 13 m off ignored a player in its head),
endermites (#143), zombified piglins (#146), hoglins (#147: the toss had to start like a jump, the game pins a
grounded player's fall speed); slimes x5 on March (#144, MoonSpawns); eyeless dogs hunting animals (#153: watched a
dog hear a cow from 19 m and kill it). Not done: the drowned's copper (no copper yet), parched skeletons (unsure of
their mechanics), endermen fighting endermites.

**Version 1.40.1** (night): the big inventory's grid in every screen (#135, owner's report: crafting at a table or
smelting meant moving things to the hotbar first). The grid moved from the [I] screen into the shared screen code,
so the table, furnace, brewing stand, enchanting table and chests all show it. Played with the real mouse: a furnace
recipe laid out at the table from stored cobblestone, then that furnace fed from storage; t_inventory_screens.

**Version 1.40.0** (night): slimes (#133) and witches (#134), the owner's two new outside monsters, and bows.
- Slimes and witches, played by hand first: each killed me fast (a big slime's split lands two to four medium ones at
  once; a witch killed me standing still in ten seconds). I'd softened both; the owner wants them very lethal, so
  that's reverted to Minecraft's numbers and the witch throws every 2 s instead of 3: about 8 s to kill someone
  standing still from its first throw (measured: 8.7 s). Both spawn by themselves: three sped-up nights brought a
  slime and two witches (dev `spawnweights`: slimes 20 of 185 outside weight, witches 15).
- Bows (owner's design): store 200, a hit is one shovel hit (checked: a cow 2 -> 1; a client's arrow took the host
  from 100 to 80, a shovel's 20), Minecraft's spread and draw curve, no arrow no shot. Arrows only crafted
  unless [Bows] ArrowsInStore (8 for 850); recovery from walls behind [Bows] ArrowsRecoverable (off); [Bows]
  PlayerBows. Gravel out of the store and into rare underground veins (calibrated to about one 1-3 block pocket per
  half moon of stone-pickaxe digging), flint one time in ten (60 digs: 7 flint). Chickens for feathers. Skeletons
  drop an arrow or two 8% of the time. Multiplayer checked with two instances.
- Owner's mid-session asks: spread and configurable recovery (done), and the big inventory in the crafting table and
  furnace screens (#135, next).
- docs/MISSING_FROM_MINECRAFT.md: ~420 things Minecraft has that the mod doesn't, for the owner to choose from (it
  caught the README promising netherite armor, which doesn't exist: README fixed).
- Ship-leaving hunt (1.39.2 suite): no early take-off in a full run; the dev log of every take-off stays in.

## 2026-10-12

**Version:** 1.39.2 (on `main` after the one-moon suite; the owner promotes it to `production`).

**Version 1.39.2** (late evening): the README and store description brought up to date (and a bug-report link), ender
pearls in every Nether's loot (#126, owner: keep them in warped forests and add them elsewhere). Owner's calls: #64
closed (the way out works), #38/#70/#25 closed, #29 dropped. The store's missing thumbnail: Thunderstore serves our
icon fine (checked its API and CDN, and the page renders it); the owner's view also lacked Thunderstore's own banner,
so it's on the browser's side (blocked CDN images or a hiccup).

**Version 1.39.1** (evening): parrot voice repeating on by default (owner's call), under a new setting name so old
configs' saved "off" doesn't keep it off; checked with two instances from an old config.

**New rule (owner, 18:56):** everything added must be obtainable the normal way, checked before any suite (CLAUDE.md).
New dev `obtainable`: sources from the store, recipes and brews whose ingredients are obtainable, the furnace, block
drops, the ground, fishing and declared drops/loot; mobs against every moon's spawn lists and the Nether. First run:
nothing without a source (ore blocks, nylium, bedrock, fluids and spawn eggs are creative-only on purpose, as in
Minecraft), every mob of the mod spawns on its own.

**Version 1.39.0** (evening): fishing, so cats take fish as the owner first asked (#96): rod, bobber and line on
every machine, host-timed bites, Minecraft's catch table; the furnace checked cooking cod, beef and porkchop in game.
1.38.1's suite: one speed-8 artifact in t_pillar (its blocks went into the ship's frame, the player fell and the round
ended; 9/9 at speed 1, and the 14 tests after it 79/79 at speed 8).

**Version 1.38.1** (evening): riders seen sitting (the game's couch pose on other machines; two instances), Best
Friends Forever, Monster Hunter no longer for pigs and cows (checked in game).

**Version 1.38.0** (evening): ghasts and magma cubes for the Nether (#62's last missing mobs; t_nether_mobs: a big
cube hops after you and splits 3 ways, mediums split again; a ghast floats ~1.5 m up in a corridor and its fireballs
blow up). 1.37.0's suite found my new animal/pet tests unsafe: with god mode off a stray creeper killed the player and
the round ended; they keep god mode on now (55/55 rerun).

**Version 1.37.0** (evening): parrots (#97): taming, a shoulder perch synced on every machine, monster imitation as a
warning; voice snippets only with the host's option (off by default) and each player's consent. The voice tap was
checked with two instances (it sees the other player's voice playback; synthetic speech fed into it was repeated; an
opt-out drops it); real speech couldn't be produced on the test machine. Comments on #95, #96 (cats take raw meat:
there's no fish).

**Version 1.36.0** (late afternoon): Minecraft animals. Pigs and cows (#95: daytime outdoor spawns, panic when hit,
follow their food, meat and leather; models from 1.21.5+'s textures), horses (#95: Minecraft's temper taming by
mounting, a saddle to steer, 8.6 m/s riding and 13 m/s galloping measured; two instances: a client rides and the host
sees horse and rider move), wolves and cats (#96: taming one in three, following, sitting, a wolf killed a zombie
coming at its owner in 4.5 s, creepers flee cats: with a cat the creeper never fused, without one it did; a tamed
wolf in the ship came back at the next landing). Found on the way: the existing "monsters gnaw through blocks" rule
would have had pigs and horses eating walls; animals and pets are exempt.

**Version 1.35.0** (afternoon): advancements (#131: toast and chat line, interior and kill hooks checked by hand,
two instances: a client's advancement in both chats, the toast only on its machine), monsters onto ship-built blocks
(#128: a navmesh baked over the ship's blocks and linked to the game's; with it off a zombie stops at a slab-and-stone
platform's foot and gnaws through it, on it climbs up after you; the blocks' obstacles had to be lowered, they cut the
new navmesh at every step), the ship's roof and antenna diggable as an option (#124: hull cells only over the room,
walls stay; patched on takeoff on every machine, checked with two instances). 1.34.0's suite: 488/492, the 4 fails
from t_portal_links leaving its Nether portal in the start room (fixed in the test; 29/29 rerun).

**Version 1.34.0** (after the owner's answers, 12:00 on): Minecraft blast damage (#105: the old landmine model did a flat
50 beyond 2.3 m; measured curve 165/94/65/40/18/0 at 1-6 blocks), tall mobs stop under a two-high platform (#112:
reproduced with the check off, 0.3 m; on, 2.4-3.1 m), portals linked at 1/8 with a portal made in the Nether room
(#118, t_portal_links), spectator mode (#129, two instances). #126 measured at 50% (25 pearls in 50 kills, single
player and a client's networked hits); asked the owner about warped-forest loot pearls.

**Version 1.33.0**: /respawn (#129 part 2), a one-player version of the game's own revive, checked with two instances.

**Version 1.32.0**: mobs despawned at the round's end (#101, #122: they inherited HideAndDontSave from LethalLib's
runtime prefab, so the game's FindObjectsOfType cleanup never saw them; reproduced and checked with two instances),
commands while dead (#129 part; two instances, screenshots).

**Version 1.31.0**: pumpkins (#130): pumpkin and carved pumpkin blocks, seeds and stems, shears, the carved pumpkin
as a helmet that keeps endermen calm (with the pumpkin view and the head model). Tests t_pumpkins, t_pumpkin_head.
Noted: t_enderman's "puts it down" check fails at one spot on some landings (no free cell there), unrelated.

**Version 1.30.0**: dust climbing a block's face (#103), humanoid mobs at player height (#100, measured 2.52 vs the
player's 2.50). The spider sword test now looks down at it (it failed on main at speed 1: a level swing passes over it).

**Version 1.29.0**: held swords and bows by their grips (#99, screenshots before/after), blazes face their target
(#102: measured up to 120 deg off while shooting; now 0), creative shotgun reload (#104, real input). Not changed but
looked into: creeper damage already kills within 2.3 m (#105, asked the owner), the take-off shakes everyone ~0.3 m,
porch or not (#114, asked the owner whether to pin players on ship blocks).

**Version 1.28.0** (after 1.27.0): two-handed pickups (#127, reproduced with a large axle: it was pocketed and [G]
dropped the torches), ship furnaces/brewing stands saved (#120, reproduced: a furnace came back empty after a relaunch;
fixed: 34 cobblestone kept through a menu quit and relaunch), aggro looks (#111, checked by screenshots).

**Version 1.27.0** (batch after 1.26.0; one-moon suite before main):
- Fixed with an in-game repro before and after each: calm mobs hurting on contact (#110), stuck in one-deep water
  (#115), hotbar wheel direction (#117), quartz XP (#121), TNT Crate icon (#125, old build showed stone). Checked in
  game: the Company bar a second after the doors open (#123), mob death effect (#113). Recipes (#116, #119) by unit
  tests. #126 (pearls dropping more than once) is a guard against repeated killing hits from several players; not
  reproducible alone.
- Not reproduced: a creeper at the Company / mobs staying in orbit (#122, #101): in single player the creeper was gone
  in orbit and nothing came to the Company; likely a client-side thing in multiplayer.
- Parked: tall mobs and two-high gaps (#112). Code on fix/tall-mobs-low-gaps, but the test couldn't yet tell it from no
  fix (in the Nether the enderman couldn't path into a block tunnel at all); asked the owner where it happened.

### Bugs fixed (from the owner's playtest list, filed as #107-#131)
- **Kast: entering the Nether killed you** (#107). Reproduced with the night's modpack: Kast's scene has an
  `OutOfBoundsTriggerFactory` plane at y -399, right at the Nether's floor, and the game kills an "inside" player who
  touches one. The Nether now ignores those triggers (players and bodies); falling out of its bottom goes back to the
  ship. The "loot by the ship on Kast" part wasn't reproduced: the Nether's loot stayed in the Nether.
- **"The entrance appears to be blocked"** (#108). Root cause in the owner's log: LethalLevelLoader's prefix on
  DunGen's Generate runs for the Nether's generator too and renumbers every door in the scene. Reproduced (all four
  doors unpaired after the Nether generated), fixed (LLL's prefix skipped for the Nether; ids restored afterwards).
- **Mobs too slow** (#100, now critical): measured a walking player at 4.55 m/s; every mob chased slower. All now
  4.6-6.0.
- **Creeper vs ship chests** (#109, partly): tools/armor/swords out of a blown-up chest weren't the ship's and were
  deleted at take-off unless they lay in the ship's inner room. Fixed. A chest by the bunks kept everything even before,
  so the owner's case may be something else: questions on the issue.
- Totem of Undying 200 (owner's request).

### Waiting on the owner (2026-10-12, evening)
- #109 (a creeper blew up the ship's chests): one real loss found and fixed in 1.26.0 (non-stack items from a
  blown chest outside the inner room were deleted at take-off); the bunk-bed case didn't reproduce. A log next time.
- #126 (pearls too often): measured 50%; asked whether the warped-forest loot pearls should go.
- ~~#96: cats tamed with raw meat~~: fishing added in 1.39.0, cats take fish.
- #62: everything from the brainstorm is in except the Company "Nether contract" day: close it, or want that?
- #97: parrot voice snippets need real speech to try; on by default since 1.39.1 (host can turn off, each player can refuse).
- Not reproduced, open for details: #70, #64 (mitigated by /unstuck and the automatic unwedge), #38, #29, #25.
- #89 (our own moons): not started; needs a generated landscape this mod's per-block world can't carry at moon size.

## 2026-10-11

**Versions and builds:** 1.22.0 (43, 00:06), 1.23.0 (44, 00:33), 1.24.0 (45, 00:59). Each passed the one-moon suite before going out.

### New features
- **Crimson and warped forests** (#68, 1.22.0): two more kinds of Nether: nylium, huge fungi, shroomlights; piglins
  (crimson) or endermen (warped).
- **Basalt deltas** (#68, 1.23.0): basalt, blackstone, glowing magma, lava pools, blazes. With these, every Nether
  biome in #68 has a kind of its own (fortress, bastion, soul sand valley, crimson, warped, delta).
- **The owner's answers on #61** (1.23.0): elytra 750, rockets 32 for 100 (configs on the old defaults follow), and
  a worn elytra mends at a crafting table with string, a quarter per string.

- **Endermen move blocks** (#94, 1.24.0), the owner's question: they pick up Minecraft's list of blocks (placed ones
  only) and put them down elsewhere.
- The owner's new ideas filed: farm animals and horses (#95), cats and wolves (#96), parrots that mimic voices (#97).

### Bugs fixed
- Endermen spamming navmesh errors when hit while unable to move (1.22.0).

## 2026-10-10

**Versions and builds:** 1.8.1 (build 28, 00:50), 1.9.0 (29, 05:46), 1.10.0 (30, 06:43), 1.10.1 (31, 07:16),
1.11.0 (32, 10:14), 1.12.0 (33, 12:37), 1.13.0 (34, 13:47), 1.14.0 (35, 14:31), 1.15.0 (36, 18:11), 1.16.0 (37,
18:58), 1.17.0 (38, 20:00), 1.18.0 (39, 21:15), 1.19.0 (40, 22:01), 1.20.0 (41, 23:02), 1.21.0 (42, 23:40). Each was
played in game and passed the one-moon suite before going out; 1.9.0 and 1.15.0 changed the ground, so the all-moon
suite ran for them too.

### New features
- **Brewing** (#88, 1.16.0): soul sand and nether wart from the Nether, glass bottles filled at water, Minecraft's
  brewing stand and screen (blaze powder fuel), Swiftness, Strength, Fire Resistance and Night Vision (longer with
  redstone, level II with glowstone), drunk by holding right-click, shown at the top right with Minecraft's icons.
  Each effect was measured in game (Swiftness +21%/+42% walking, Night Vision 18 -> 87 brightness in a dark corridor).
- **Piglins** (#67, 1.17.0): in the Nether fortress, they go for anyone without gold armor and trade for gold ingots
  dropped near them (Minecraft's barter table, with this world's items).
- **Skeletons, spiders and endermen** (#59, 1.18.0), completing the owner's mob list: skeletons shoot arrows and drop
  bones (bone meal grows crops), spiders are fast and drop string (wool), endermen are neutral until you look them
  in the eyes, then scream, chase and teleport (ender pearls).
- **Bastions** (#68, 1.19.0): about one day in three the Nether is a bastion remnant instead of the fortress:
  blackstone (gilded walls give gold nuggets), a treasure room of gold blocks, piglins, gold loot.
- **Soul sand valleys and piglin brutes** (#68, 1.21.0): a third kind of Nether (soul sand, basalt, a fossil, blue
  light, skeletons and endermen), and brutes guarding the bastions.
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
- **The Nether, first part** (#66, 1.14.0), asked for by the owner at ~13:00: portals, the fortress as a second
  interior (a second instance of the game's own generator, tiles built in code from Minecraft's blocks, the same on
  every machine), travelling there and back, blazes with fireballs, a blaze spawner, S+ loot with blaze rods.
  Played in single player and as a LAN client.
- **The Nether's ground and netherite** (#66, 1.15.0): fortress walls dig to nether bricks, netherrack beyond with
  quartz, lava pockets and rare ancient debris; netherite scrap, ingots ($500) and tools; fire on netherrack never
  goes out; beds (explode in the Nether); fire exits (each extra portal lights an exit room); the Nether always
  eclipsed (full of monsters from the start, refilled): both the owner's ideas; a stair hall. **Wither skeletons at
  Minecraft's size** (2.4 blocks), as the owner asked.
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
- A player in the Nether was set "outside" once anything had been dug (the facility's tunnel check): reproduced and
  fixed before 1.14.0 went out.
- Items vanishing at the Company (#78, 1.17.0): it was the **ramp** up from the dock. Thrown items and broken blocks'
  drops fell through it (the game's item fall skips its layer) and lay hidden under it. Reproduced as host and as a
  LAN client, fixed, checked on both.
- Swords couldn't hit a monster standing in front of a block someone had placed (#92, 1.18.0): the click mined the
  block behind it. Found chasing the suite's intermittent "sword hits kill the ..." failures (the test areas are full
  of earlier tests' blocks); reproduced with a zombie in front of a cobblestone wall, fixed; the next suite was
  418/418.
- The Nether without fire exits on some days (1.19.0): a seed now and then grew the fortress no branches; it tries the
  next seed then. Piglins standing for good facing gold they couldn't reach (1.19.0): seen live, fixed (they give up
  after 8 s, and leave loot gold alone).
- **Stuck fast** (#64, 1.20.0): a hard stuck reproduced at last. A body even slightly inside something can't move
  at all; pillaring under the factory's lamp bars did it, and so did ender pearls landing against a pipe. Pillaring
  and pearls now check the body fits, and a safety net moves a player caught like that to free ground after 1.5 s
  of trying to walk. Left open for the owner to confirm against the original report.
- A hoe tilling dirt under a sliver of hillside, so seeds couldn't go on it (#93, 1.21.0): found when t_farming
  failed on three landings (1.19.0 too); Minecraft's rule now (only dirt with air above).
- The honey test failing on Dine (#91): a test bug. On one spot, 14 m from the start, a hill rose over the test's
  floor and the piston rig was built inside it; the piston rightly wouldn't push into the ground. Found by running
  the rig on 21 spots; the test now checks the cells are air.

### Tests
- The all-moon run for 1.15.0 came back 3707/3796. The misses were today's new tests, not the mod. The elytra
  test let a player die on Rend, which ended the round and failed every test after it there. The bed test read a
  natural block sharing the bed's cell. The farming test left its farm in place and used up flat spots. All fixed;
  the moons' failing tests rerun alone pass (Artifice 23/23, Titan 29/29, Assurance 34/35 before the last fix).
- Two "suffocation" deaths on Experimentation (same spot, x -45 z 229) were the game's quicksand: the elytra test's
  short glides landed in it with god mode off. That test now keeps god mode on and reads the hit's damage from the
  elytra itself.
- The suite's "no flat outdoor spot" failures had a root cause: the dev spot finder drops spots that earlier tests
  built on and clamped a high index to its last spot, so as today's new tests built more, whole runs of tests got the
  same hillside spot inside a rock. It wraps round now.
- Still intermittent, older than today: the integrity test now and then finds an object left uncut in a dug cell,
  and the flying-machine engines sometimes don't move.

### Investigated, not reproduced
- #70 (inventory gone wrong as a client): tried in game, details on the issue. #57 (modded moons) closed: its fixes
  shipped in 1.9.0.
- #64 (stuck on pipes): first tried in the mineshaft (never stuck; the mansion has no pipes; a new dev command,
  `interior <id>`, forces a landing's interior), then reproduced in the factory (above).

### Waiting on the owner
- ~~#61's open choices~~ answered after midnight (see 2026-10-11): elytra 750, rockets 32 for 100, elytras repairable.
- ~~Wither skeleton height~~: the owner agreed Minecraft's true 2.4 blocks is right.

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
