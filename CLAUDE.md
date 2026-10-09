# Working rules for this repo

## Bugs: reproduce first, in the game
- **Reproduce every bug before fixing it.** Make it happen in the real game first (land a moon, use real input:
  tools/pilot.py, the DevServer's `keys` / `mouse` / `lmb` / `rmb`, screenshots), and confirm what you see matches
  the report. Only then write the fix.
- **Validate the fix the same way:** the same in-game steps now behave correctly. Where possible, also show the old
  build (or the fix switched off) still fails, so the fix is what made the difference.
- A fix that's only been compiled, unit-tested or reasoned about isn't done. If a bug truly can't be reproduced, say
  so in its issue (what was tried) instead of claiming it's fixed.
- New features get played by hand in-game before any test suite runs.

## Tracking
- GitHub issues are the tracker (https://github.com/Thrasherop/lethalcraft/issues): `bug` (with a severity label or
  `easy fix`), `enhancement`, `performance`. Reference the issue in the commit that fixes it (`Fixes #N`).

## Testing
- Targeted tests first: `py tools/regress.py <moon> -t <test>` on the moon where it failed. The all-moon run (moons
  0-10 and 12; 11 is Liquidation, not playable): `py tools/regress.py 0 1 2 3 4 5 6 7 8 9 10 12 --speed 8`, about
  20-30 minutes (1 h 40 at speed 1). When to run which suite: see the two points below. Stop a run that's already known bad (a fix for one of its
  failures exists) and restart it after.
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
- Every push to `main` builds and publishes a release zip (.github/workflows/build.yml). Only push what has been
  tested in-game, and bump the version (dist/package/manifest.json and Plugin.Version) plus CHANGELOG when behaviour
  changes.
- Don't run the game while the owner is playing on their other machine (same Steam account).
- Keep the Steam friends status **offline** while testing (the owner's friends get notified of every launch). Steam
  turns it back on by itself (it was found online again on 2026-10-08, probably after a Steam client restart), so the
  launch scripts (tools/restart*.sh, mp.sh, pathtest.sh) send `steam://friends/status/offline` before every launch.
  Launching the game any other way: send it first. Every hour or so of testing, check it: Steam window > Friends menu
  (the tick is on the current status). Don't go by `ePersonaState` in userdata/*/config/localconfig.vdf: it said 1
  (online) while the menu showed Offline. Steam's console_log.txt shows whether the command arrived.
