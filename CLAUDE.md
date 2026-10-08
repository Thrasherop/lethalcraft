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
- Targeted tests first: `py tools/regress.py <moon> -t <test>` on the moon where it failed. The full run (moons 0-10
  and 12; 11 is Liquidation, not playable) only as the final check: `py tools/regress.py 0 1 2 3 4 5 6 7 8 9 10 12
  --speed 8`, about 20 minutes (1 h 40 at speed 1). Stop a run that's already known bad (a fix for one of its
  failures exists) and restart it after.
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
  (the tick is on the current status); the saved `ePersonaState` in userdata/*/config/localconfig.vdf (0 = offline,
  1 = online) only updates a while later.
