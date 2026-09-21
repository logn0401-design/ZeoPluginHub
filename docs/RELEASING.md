# Releasing Zeo Core

1. Start from canonical source in `src/ZeoCore`. Do not re-run an old universal installer's transformations over this already transformed source.
2. Keep the loader entry point's version aligned with Core. Build ClientPlugin and ZeoOverlay in Release/net48; ClientPlugin needs `-p:Bin64="path/to/SpaceEngineers/Bin64"`. The plugin assembly name must remain `ZeoCore.Runtime` so the entry point can load it as a dependency.
3. Place `ZeoCore.Runtime.dll` and `ZeoOverlay.zip` (containing the EXE and its .config file) into a version-specific assets folder. Do not package PDBs, obj/bin folders, game assemblies, logs, live settings, or credentials.
4. Calculate SHA-256 hashes. Verify the entry point using Pulsar's own compiler with the runtime DLL as its reference asset. Test loading and configuration preservation, then initial install and an update in-game.
5. Commit source and assets first. Use that full Git commit SHA as the descriptor's `Commit`, with matching asset paths/hashes. Commit the descriptor separately; never use a floating branch in its Commit field.
6. Push both commits together. Use a testing branch until the release is accepted, then promote the tested descriptor to main. Keep old pinned commits/assets available for rollback.

The descriptor compiles only `loader/ZeoCore/`, not the runtime source tree. Pulsar's compiler does not include System.Web.Extensions in its standard references; the compiled runtime retains that standard Windows .NET Framework dependency.

## Pending runtime verification

- Initial installation into a clean Windows/Pulsar Legacy profile.
- Existing local Core disabled; no second Core instance running.
- Native menu, Home shortcut, HUD startup, ammo empty state, saved HUD positions.
- Website sharing behavior unchanged from the base release.
- Full shutdown closes the old overlay before a refresh/restart update.
- Advance to a second pinned release, then roll back; settings and layout persist.

Compilation and offline asset tests alone do not establish these results.
