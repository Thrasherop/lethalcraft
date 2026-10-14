# The in-game test suite

`tools/regress.py` drives the real game through the DevServer: it lands a moon, builds, digs, spawns, presses keys and
clicks, and checks what happens. Offline unit tests (`tests/LethalMinecraft.Tests`, no game running) hold what doesn't
need the game: recipes, smelting and brewing tables, ore generation, block hardness, piston rules, name parsing,
enchant offers, ground classification.

## The two runs

| Run | Command | What | Time (speed 8) |
| --- | --- | --- | --- |
| One moon | `py tools/regress.py 0 --speed 8` | every test, on Experimentation | about 17 minutes |
| All moons | `py tools/regress.py 0 1 2 3 4 5 6 7 8 9 10 12 --speed 8` | only `MOON_TESTS`, on each moon | see below |

The owner's rule (2026-10-13): a run over 30 minutes is no longer practical and gets optimized or trimmed; over 45
minutes may run at most once a day. Each run prints where its time went (every test, slowest first): watch it.

`MOON_TESTS` are the tests whose result can differ by moon: the moon's ground (digging, molds, holes, sand, water on
natural ground, a piston in a trench, TNT craters), its trees, the facility (tunnels, the inside/outside switch,
deepslate under it), the AI nodes and bedrock. Everything else behaves the same on every moon, so the one-moon run
covers it.

## Review of 2026-10-13

The owner asked for every test to be evaluated: keep anything that catches regressions or sanity-checks a feature,
drop or merge what no longer earns its time. All 103 tests were read. None was fully redundant: each had at least
one check nothing else made. What changed:

- **Moved to unit tests** (recipe data, not game behaviour): ore blocks back into nine, the diamond pickaxe and sword,
  bread, paper, sugar, bone meal, rockets, arrows, granite walls, the netherite ingot (and no upgrade in the crafting
  grid), cooked beef and porkchop. `t_ore_blocks` went with them; `t_crafting` keeps the real clicks.
- **Merged** where two tests did the same thing twice:
  - `t_nether` into `t_portal_links`: both walked through a portal and back. Its own checks (no portal, no fortress;
    the fortress made; its floor holds you) moved over.
  - `t_pumpkin_head` into `t_enderman`: its "without the pumpkin, it comes for you" was the enderman test's own.
  - `t_creative_loot` into `t_creative`: both opened the same menu tab.
  - `t_quartz_xp` and `t_redstone_ore` into `t_ore_drops`: the same place, aim and mine, one spot and one pickaxe.
- **Faster, same checks:**
  - Floors, portal frames and Nether floor scans go to the game as one batch instead of a command per block.
  - Dev knobs for timers the tests only sat out: wool regrowth (`care wool`), brew time (`brewui time`) and phantom
    circling (`phantoms circle`). The behaviour is tested, not Minecraft's minute.
  - Polls instead of fixed waits: pets, pillar digging, the jukebox, vine growth, ship nav.
  - One furnace in `t_fishing` (`t_furnace_ui` is the furnace test); `t_inventory_screens` no longer waits for a smelt.
  - `t_horses` stops at the first direction that's clear.
- **Checks that couldn't fail, fixed:**
  - `t_store_terminal`: the typed-terminal check only ran in orbit, but sat in a test that runs after landing, so a
    suite never ran it. It is an orbit test now.
  - "No portal: no fortress" passed whenever any portal block existed anywhere.
  - "Eclipsed" was `check(..., True)`. It is dropped: `t_bastion`, `t_valley` and `t_forests` count the monsters as
    the Nether generates.
- **Fixed flakes found on the way:**
  - `t_bows` left its stone wall standing. Saved with the moon, it hid `t_witches`' witch on the next run.
  - The witch is now placed where it can see you.
  - "Climbing out above the facility" teleported the player into rock, and the fall beat the switch's quarter-second
    check. The player now stands on a block up there.
- **On the all-moon run** now: `t_piston` (a trench in natural ground), deepslate under the facility (split out of
  `t_block_families`) and water on natural ground (split out of `t_fluids`).

### Left as they are, for now

- `t_water` and `t_fluids` overlap: the bucket take, pour and dry-up. With the floor batched, merging them saves only a
  few seconds.
- `t_bastion`, `t_valley` and `t_forests` share a template and could be one table. Each is under 10 s.
- `t_chest`, `t_tool_wear_kept` and `t_durability`, `t_craft_lock` and `t_screen_clicks`, and `t_big_inventory` and
  `t_inventory_screens` could each be merged for a second or two each. Separate, a failure in one doesn't hide the
  others.
- `t_mob_drops` and `t_variants` kill several of a mob to beat the drop chances. A forced-drop knob would save about
  10 s, but would mean touching every mob's drop code.
- `t_flying_machine`'s `Bwest` engine only differs from `Beast` by direction. It is kept because it is the one engine
  flying west, where a facing bug would show.
- Spot-dependent: `t_beds` and the phantom dive have failed on one landing and passed on the next. Worth making them
  pick a clear spot as `t_witches` now does.
- `t_unwedge` passes when the game's own movement frees the player, so the mod's safety net may not be exercised.
