# Working rules for this repo

## Bugs: reproduce first, in the game
- **Reproduce every bug before fixing it.** Make it happen in the real game first (land a moon, use real input:
  tools/pilot.py, the DevServer's `keys` / `mouse` / `lmb` / `rmb`, screenshots), and confirm what you see matches
  the report. Only then write the fix.
- **Validate the fix the same way:** the same in-game steps now behave correctly. Where possible, also show the old
  build (or the fix switched off) still fails, so the fix is what made the difference.
- A fix that's only been compiled, unit-tested or reasoned about isn't done. If a bug truly can't be reproduced, say
  so in its issue (what was tried) instead of claiming it's fixed.
- **Play every new feature by hand, like a player, before any test runs (owner, 2026-10-13).** Scripted tests and unit
  tests are not evidence that a feature works: the 1.44 batch passed both, and the owner found about twenty bugs in a
  few minutes of play. For each new item or block: craft it, check its icon in the hotbar and in the creative menu
  (and that it sits in Minecraft's creative tab), place it, break it, use it, and combine it with what it's for (rails
  into lines on flat ground and slopes, powered rails passing power, a 2x2 piston door, a lever on a lamp, a door with
  [E]); look at it by day from several sides. Mobs: up close, wandering, taking hits, holding their items. Compare with
  real Minecraft (installed) and its decompiled source (official mappings) for behaviour: reimplement it, don't
  guess. Ten features take ten times one feature's play time. Write down what was tried, with screenshots.
  Then just play with them, as a player would: build something real with the new things together (new redstone: a
  machine out of it, a 2x2 piston door, a minecart line with powered rails and a detector; new mobs: let them wander,
  fight them, lead them), because that's where a player finds the bugs. A playtester agent may help (ChatGPT desktop,
  its @computer tool): give it the freeze-the-day and god-mode commands so a slow machine can build in peace.

## Hand play: before every merge, and before every release (owner's rule, 2026-10-15)
The owner, after the 1.45.x totem/bow/tips work: "That manual playtesting you did made a MASSIVE difference." It is
required, not optional:
- **Before a feature or fix merges** (into `next` or `main`): play that one change by hand in the real game.
- **Before a version goes to `main`**: a general play of the whole build (not just what changed): land a moon, mine,
  craft, build, fight, put things in a chest and take them out, take off and land, visit the Nether, at the speed and
  with the inventory a player has (not god mode the whole time).

What that play looked like when it worked (do at least this, then go a step further each time: one more case, one
more angle, one more combination than the last person tried):
- **The real thing, not a stand-in.** The totem was tested against a real snare flea grabbing the player, a real
  deadly hit, a real 40 m fall and a real Earth Leviathan bite, not a dev "damage" command. The bow was drawn with the
  real right mouse button at a live zombie, three shots, its health read before and after each. A lead went on a real
  cow with [E] and the player walked 10 m away to see if it followed.
- **Every way in.** The same feature from each source: crafted, from the store, from the creative menu, picked up off
  the floor, taken out of a chest. Each one held in hand (the tip text, the held size and angle), in the hotbar, in
  the inventory, dropped and picked up again.
- **Look at it.** Take a screenshot at each step and actually open and read it: the tip line fits and says the right
  button; the item isn't tiny or backwards; nothing else on screen broke (the scan's boxes over an open book were
  found that way). Compare side by side with real Minecraft (installed) and its decompiled code.
- **The interactions around it.** Whatever it touches: a totem against each kind of death and grab (masked, flea,
  worm, fall, zombie); an item with the chest, the ship's storage, the store, takeoff and landing, a second player.
- **Write down exactly what was done**, in the commit or the issue: each step, what was expected, what was seen, the
  screenshots' names. Someone reading it must be able to repeat it, and must see what was *not* tried.
- When a step fails, that's the bug: reproduce it, fix it, then play the whole list again, not just the failing step.

## Tracking
- GitHub issues are the tracker (https://github.com/Thrasherop/lethalcraft/issues): `bug` (with a severity label or
  `easy fix`), `enhancement`, `performance`. Reference the issue in the commit that fixes it (`Fixes #N`).

## Everything new must be obtainable (owner's rule, 2026-10-12)
- Before running a suite on a batch, sanity-check that everything the batch adds can be got the normal way in a real
  game, not only through dev commands: every item and block has a source (store, crafting recipe, furnace, a mob or
  block drop, loot, natural ground), every mob spawns on its own (on the moons, in the facility or in the Nether), and
  every recipe's ingredients are themselves obtainable. Dev `obtainable` lists items without a source and mobs that
  nothing spawns; anything it flags is fixed (or deliberately dev-only and said so) before the suite runs.
- Tests that set things up with dev commands (`invgive`, `enemy`, `nethermob`) don't prove this: check it by hand.
- **Complete, not just present (owner's rule, 2026-10-13):** a new block comes with Minecraft's variants (stairs,
  slabs, walls, polished/smooth/cut/bricks, mossy/cracked/chiseled where Minecraft has them); a new mob with its full
  Minecraft loot table (every drop and chance, its XP) and its variants (biome variants, babies, colors/coats). Items
  those need come with their uses. Check this with the obtainability check, before the suite.

## Testing
- **Suite length (owner's rule, 2026-10-13):** a suite that takes more than 30 minutes is no longer practical: optimize
  it or drop tests until it fits. Anything over 45 minutes may run at most once a day. Watch the time each run prints;
  when a run creeps toward 30 minutes, fix that before adding more tests. What each run covers and the last review of
  the tests: docs/TEST_SUITE.md.
- Start a suite from a freshly launched game (tools/restart.sh): a running game keeps every earlier run's blocks and
  dug cells on the moon (a run after several others on 2026-10-13 had 828 blocks and 192 dug cells left about), and the
  tests' spots and spawns assume a moon earlier runs haven't built over.
- Targeted tests first: `py tools/regress.py <moon> -t <test>` on the moon where it failed. The all-moon run (moons
  0-10 and 12; 11 is Liquidation, not playable): `py tools/regress.py 0 1 2 3 4 5 6 7 8 9 10 12 --speed 8` runs only
  `MOON_TESTS` in regress.py (the ones that can differ by moon) on each moon; the one-moon run covers the rest. (Every
  test on every moon took over four hours: don't go back to that.) When to run which suite: see the two points below.
  Stop a run that's already known bad (a fix for one of its failures exists) and restart it after.
- **Batch work on branches; regress in batches.** Each feature or fix goes on its own branch in its own worktree
  (`git worktree add ../lmc_<name> -b feature/<name>`; git there needs `-c safe.directory=<path>`, and create the
  untracked `shots/` folder). While the game is busy (a suite running, or the owner playing), write and compile-check
  other branches (`dotnet build -c Debug -o <scratch dir>`: Debug doesn't copy into the game). Each branch gets its
  hand play and targeted tests on its own. Once several are done, merge them into one integration branch (`next`) and
  run the suite once for the batch, not once per feature. Only `next` goes to `main`, after its suite passes.
- **Two sizes of suite.** The usual check for a batch is every test on one moon: `py tools/regress.py 0 --speed 8`
  (Experimentation). The all-moon run (above) is for the rarer cases: before a release that changes ground, terrain,
  digging, explosions or the facility interior (anything that differs by moon), or when the one-moon run fails in a
  way that may depend on the moon.
- `--speed N` runs the game N times faster; the tests' sleeps and clocks are in game time (tools/timing.py), so they
  mean the same at any speed. Tests that time real input tightly are capped (MAX_SPEED in regress.py). A failure only
  seen at speed: rerun that test at `--speed 1` before calling it a bug in the mod. The run prints where its time went.
- Offline unit tests: `dotnet test tests/LethalMinecraft.Tests` (needs the game installed for Unity's DLLs).

## Shipping
- Every push to `main` makes a **test build** (a GitHub pre-release; nothing goes to Thunderstore). Only push what has
  been tested in-game, and bump the version (dist/package/manifest.json and Plugin.Version) plus CHANGELOG when
  behaviour changes.
- The **`production`** branch is the store: a push there makes the real release and publishes on Thunderstore.
  **Never push or merge to `production`**: the owner tests a test build and moves it there themselves
  (docs/THUNDERSTORE.md).
- Don't run the game while the owner is playing on their other machine (same Steam account).
- Keep the Steam friends status **offline** while testing (the owner's friends get notified of every launch). Steam
  turns it back on by itself (it was found online again on 2026-10-08, probably after a Steam client restart), so the
  launch scripts (tools/restart*.sh, mp.sh, pathtest.sh) send `steam://friends/status/offline` before every launch.
  Launching the game any other way: send it first. Every hour or so of testing, check it: Steam window > Friends menu
  (the tick is on the current status). Don't go by `ePersonaState` in userdata/*/config/localconfig.vdf: it said 1
  (online) while the menu showed Offline. Steam's console_log.txt shows whether the command arrived.
