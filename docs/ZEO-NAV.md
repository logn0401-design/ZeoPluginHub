# Zeo Nav for Pulsar

**1.1.25 — Current Nav build and Left Shift target aim default**

Install **Zeo Nav** from the [Zeo Plugins catalog](../README.md#add-the-catalog). Requires Pulsar 2.4.2 or later, Legacy, Windows, and Space Engineers 1. The matching external HUD downloads automatically; a separate Nav installer or .NET SDK is not needed for players.

## First installation

1. Add the Zeo Plugins remote hub using the repository README, then refresh sources.
2. Enable **Zeo Nav** and fully restart the game.
3. In Pulsar, use Nav's **Open Config** action. Type part of a name in **SEARCH GPS**, choose a destination from the dropdown, set your speed limit and MAX SIG, then press START ROUTE.

MAX SIG accepts a finite, nonnegative whole-kilometer value with no 750 KM ceiling. **0 means no SIG restriction** and is the fresh-install default; saved limits remain unchanged. Positive limits use the farthest of your own ship's four Spectrum detection ranges, keep 3% range headroom, and require compatible fresh own-ship telemetry. Nav does not install server mods. Unrestricted cruise with a positive departure or arrival limit still waits for a fresh model so it can plan braking. With all limits unrestricted, own-SIG telemetry does not gate thrust. Speed settings support servers up to 50,000 m/s; thrust still depends on drives, fuel, heading, motion and braking authority.

Quiet departure holds its selected MAX SIG throughout the chosen departure distance, then releases toward cruise over the same distance again. Quiet arrival finishes reducing toward its chosen MAX SIG before entering the arrival zone and holds that limit throughout the zone. Overlapping limits use the stricter ceiling; a zero zone adds no restriction beyond cruise. These are limits on Nav's commanded thrust and turn-bank planning. Other ship systems can emit additional signal, so compare the Spectrum own-ship reading during the first live test.

The ETA predicts arrival rather than dividing distance by current speed: it samples future SIG-limited acceleration, the speed cap, learned flip allowance, remaining coast/braking, RCS terminal speed taper and final settling. Its display smooths small telemetry changes but shows material delays promptly. Recovery braking displays `ETA REPLANNING` and measured turn progress; actual flight, server lag and course corrections can change the estimate.

## Switching from the one-click/local install

Close the game and overlay. Back up your Pulsar profiles and `%APPDATA%/Pulsar/ZeoNav`. Disable the old **local Zeo Nav** entry before enabling the catalog entry; never enable two Nav controllers together. Keep your settings folder. Fully restart after switching.

The catalog uses the same settings folder and loads its matching overlay from Pulsar's verified asset cache. It does not overwrite your old local DLL or overlay. To roll back, disable the catalog entry, re-enable the old local entry, and restart. Keep your previous installer available.

## Included behavior

The current Nav build includes the SDX/Epstein main-drive catalog, searchable GPS, auto docking, target selection, intercept and speed matching, configurable flip and terminal controls, and the external HUD. Version 1.1.24 added bounded world-motion revalidation and faster aligned intercept engagement. Version 1.1.25 avoids a duplicate full-flip reserve when already aligned and aborts stalled recovery turns after a bounded retry; a working RCS turn bank can be tried only when the configured mode and active SIG budget permit it. Holding Left Shift opens the target picker by default on a fresh installation. Existing saved key bindings remain unchanged, and the aim key is editable on the Keys page. The [1.0.2 notes](ZEO-NAV-1.0.2.md) document earlier drive and GPS changes.

## Updating and checking

Refresh Pulsar sources and fully restart. The Nav log in `%APPDATA%/Pulsar/ZeoNav/zeonav.log` should identify `1.1.25`. The descriptor pins an immutable source commit and SHA-256 hashes for both runtime assets.

Offline validation passed: 893 flight/control checks, 3,231 UI/settings assertions and the actual Pulsar compiler/loader/asset checks. These do not prove live SIG compliance, turn-bank recovery or flight on a mate's PC. Prefer a creative test before live-server use. A native AMD graphics-path crash was recorded both before and after the local 1.1.25 install; its exact trigger is unresolved, so this release does not claim to fix it.

Compact-menu ON/OFF toggles now save independently of unfinished number edits. ENTER/APPLY commits its own numeric field while retaining other drafts. Rebuilt pages clear old input handlers, and clicks/validation failures are recorded in the Nav log. Close and emergency Abort remain available.

## Building and publishing

Build `src/ZeoNav/ZeoNav/ZeoNav.csproj` in Release/net48 with `-p:Bin64Dir=<your Space Engineers Bin64>`. Build `src/ZeoNav/ZeoNavOverlay/ZeoNavOverlay.csproj` in Release/net48. Publish only `ZeoNav.dll` and an overlay ZIP containing `ZeoNavOverlay.exe` and `ZeoNavOverlay.exe.config` at its root. Do not include game libraries, build caches or player files.

Flight tests: build `tests/NavFlight/Tests.csproj`, then run its EXE with the game Bin64 path. UI tests: build `tests/NavUi/UiTests.csproj`, then run with game Bin64, the built overlay EXE, and a disposable results directory. Both projects accept Bin64Dir when building. Tests use isolated fixtures and do not start a game or flight.

Use `tests/NavCatalog/Compile-Loader.ps1 -GameBin <Bin64> -PulsarDir <Pulsar> -RuntimeDll <runtime> -OutputDir <scratch>` to exercise the installed Pulsar compiler. `ValidateCatalog.cs` checks the installed Pulsar descriptor parser and actual loader/runtime/overlay handoff without calling Init. Compile it using the .NET Framework C# compiler with System.Xml.Linq, System.IO.Compression and System.IO.Compression.FileSystem references; its five arguments are documented in the source.

Commit source/assets first, then pin that full commit in `Plugins/ZeoNav.xml` and update asset hashes in a second commit. Preserve Core and other catalog entries when publishing. Disable the catalog entry before switching to a local installer.

## Third-party notice

Connector face geometry incorporates work from OwendB1/AutoDock. The preserved [MIT notice](../src/ZeoNav/THIRD_PARTY_NOTICES/OwendB1-AutoDock-LICENSE.txt) is also supplied as a catalog asset.
