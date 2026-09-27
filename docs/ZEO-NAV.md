# Zeo Nav for Pulsar

**1.1.22 — Current Nav build and Left Shift target aim default**

Install **Zeo Nav** from the [Zeo Plugins catalog](../README.md#add-the-catalog). Requires Pulsar 2.4.2 or later, Legacy, Windows, and Space Engineers 1. The matching external HUD downloads automatically; a separate Nav installer or .NET SDK is not needed for players.

## First installation

1. Add the Zeo Plugins remote hub using the repository README, then refresh sources.
2. Enable **Zeo Nav** and fully restart the game.
3. In Pulsar, use Nav's **Open Config** action. Type part of a name in **SEARCH GPS**, choose a destination from the dropdown, set your speed limit and MAX SIG, then press START ROUTE.

For Spectrum-based flight, the world/server must supply compatible Spectrum telemetry. Nav waits for fresh own-ship data; this client plugin does not install server mods. MAX SIG uses the farthest of your own ship's four reported detection ranges. It supports up to 750 KM, and speed settings support servers up to 50,000 m/s. Actual thrust is also limited by available drives, fuel, heading, braking and fresh signal feedback.

## Switching from the one-click/local install

Close the game and overlay. Back up your Pulsar profiles and `%APPDATA%/Pulsar/ZeoNav`. Disable the old **local Zeo Nav** entry before enabling the catalog entry; never enable two Nav controllers together. Keep your settings folder. Fully restart after switching.

The catalog uses the same settings folder and loads its matching overlay from Pulsar's verified asset cache. It does not overwrite your old local DLL or overlay. To roll back, disable the catalog entry, re-enable the old local entry, and restart. Keep your previous installer available.

## Included behavior

The current Nav build includes the SDX/Epstein main-drive catalog, searchable GPS, auto docking, target selection, intercept and speed matching, configurable flip and terminal controls, and the external HUD. Holding Left Shift opens the target picker by default on a fresh installation. Existing saved key bindings remain unchanged, and the aim key is editable on the Keys page. The [1.0.2 notes](ZEO-NAV-1.0.2.md) document earlier drive and GPS changes.

## Updating and checking

Refresh Pulsar sources and fully restart. The Nav log in `%APPDATA%/Pulsar/ZeoNav/zeonav.log` should identify `1.1.22`. The descriptor pins an immutable source commit and SHA-256 hashes for both runtime assets.

The isolated flight and UI suites passed for 1.1.22. A clean catalog installation and flight on a mate's PC still require an in-game test. This release does not claim that every server's wobble has been eliminated.

## Building and publishing

Build `src/ZeoNav/ZeoNav/ZeoNav.csproj` in Release/net48 with `-p:Bin64Dir=<your Space Engineers Bin64>`. Build `src/ZeoNav/ZeoNavOverlay/ZeoNavOverlay.csproj` in Release/net48. Publish only `ZeoNav.dll` and an overlay ZIP containing `ZeoNavOverlay.exe` and `ZeoNavOverlay.exe.config` at its root. Do not include game libraries, build caches or player files.

Flight tests: build `tests/NavFlight/Tests.csproj`, then run its EXE with the game Bin64 path. UI tests: build `tests/NavUi/UiTests.csproj`, then run with game Bin64, the built overlay EXE, and a disposable results directory. Both projects accept Bin64Dir when building. Tests use isolated fixtures and do not start a game or flight.

Use `tests/NavCatalog/Compile-Loader.ps1 -GameBin <Bin64> -PulsarDir <Pulsar> -RuntimeDll <runtime> -OutputDir <scratch>` to exercise the installed Pulsar compiler. `ValidateCatalog.cs` checks the installed Pulsar descriptor parser and actual loader/runtime/overlay handoff without calling Init. Compile it using the .NET Framework C# compiler with System.Xml.Linq, System.IO.Compression and System.IO.Compression.FileSystem references; its five arguments are documented in the source.

Commit source/assets first, then pin that full commit in `Plugins/ZeoNav.xml` and update asset hashes in a second commit. Preserve Core and other catalog entries when publishing. Disable the catalog entry before switching to a local installer.
