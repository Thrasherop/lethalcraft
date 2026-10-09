# Publishing on Thunderstore

CI (`.github/workflows/build.yml`) publishes LethalCraft on [Thunderstore](https://thunderstore.io/c/lethal-company/)
by itself once it has a token. Until then it builds and checks the Thunderstore package on every push (the
"Thunderstore package (dry run)" step) and skips publishing.

## What you do (once)

1. **Sign in to Thunderstore** at <https://thunderstore.io> (it signs in with GitHub, Discord or Overwolf).
2. **Create a team.** Click your name (top right) > Teams > Create team. Its name becomes the namespace the mod is listed
   under (`<team>-LethalCraft`). The config assumes **`Thrasherop`**. If you pick a different name, add it in GitHub:
   repo > Settings > Secrets and variables > Actions > **Variables** tab > New variable, name `THUNDERSTORE_NAMESPACE`,
   value = the team name.
3. **Make a token for CI.** On the team's page: Service Accounts > Add service account (any name, e.g. `github-ci`).
   Thunderstore shows its API token **once**; copy it.
4. **Give it to GitHub.** Go to repo > Settings > Secrets and variables > Actions > **Secrets** tab > New repository secret.
   Name it `TCLI_AUTH_TOKEN` and paste the token as its value.

That's all. The next push to `main` publishes the current version. To publish right away without a push, use
Actions > Build release zip > Run workflow.

## How it works

- Every push to `main` builds the mod and makes the GitHub release (as before). Then
  `tcli build` makes the Thunderstore zip from `thunderstore.toml`:
  - the namespace, the community (`lethal-company`) and the categories (Mods, BepInEx, Items, Client-side, Server-side) come from that file;
  - the version, the dll, README, CHANGELOG and icon come from `dist/package` and `README.md`.
- The step fails the build if the dependencies or the description in `thunderstore.toml` differ from
  `dist/package/manifest.json`. Change both together.
- **Publish** runs only when `TCLI_AUTH_TOKEN` is set, and only when this version isn't on Thunderstore yet. Thunderstore
  takes each version once, so a push without a version bump just skips it. The usual rule (bump the version and the
  CHANGELOG when behaviour changes) is what puts a new version on Thunderstore.
- To try the package build locally: `dotnet tool install -g tcli`, put the dll in
  `dist/package/plugins/LethalCraft/`, then run `tcli build --package-version <version>`. The zip lands in `build/`.

## If something goes wrong

- *"Namespace ... not found" / 403*: the token's team and the namespace differ. Set `THUNDERSTORE_NAMESPACE` (step 2).
- *A version is wrong on Thunderstore*: versions can't be replaced. Bump the version and push again. A team owner can
  deprecate the old version on the website.
- *Stop publishing*: delete the `TCLI_AUTH_TOKEN` secret, and the step goes back to skipping.
