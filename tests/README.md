# Tests

## Offline unit tests (no game needed, ~1 s)

```
dotnet test tests/LethalMinecraft.Tests
```

Covers the engine-free code in `src/Core` plus the block/recipe data:

- `MeshClipTests`: cutting cubes out of triangles (area conservation, multi-box unions, walls, attribute interpolation).
- `GroundRulesTests`: solid vs air voting (sky, rooms, catwalk pits, between rooms, under the facility, overhangs),
  soil/stone layering, deterministic ore rolls and ore rates by depth.
- `DataTests`: unique block ids/keys, bedrock, tool speed and harvest tiers, every recipe/smelting/fuel entry refers
  to a real item, the stone → iron tool progression exists, pocket recipes fit a 2x2 grid, face/key helpers.

## In-game regression suite (needs the game with `DevMode = true`)

```
bash tools/restart.sh                 # build, launch, auto-host
py tools/regress.py 0 2 4             # moons by index (0 Experimentation ... 12 Embrion)
py tools/regress.py -t t_inside       # rerun one test on the moon you're on
```

Each moon: dig + item gravity + layering, placed blocks next to holes, torch pop-off, falling sand, pistons,
TNT crater, tunnel through a facility wall and actually walk it, facility floor/ceiling, inside/outside switching,
bedrock protection at the entrance and under the ship, and no mod errors in the log. Exit code = failed checks.

`tools/qa.py <scenario>` builds the same situations and writes multi-angle photo sheets to `shots/` for visual review;
`tools/moontour.py` digs on every moon and photographs the results.
