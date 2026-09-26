# Zeo Plugins

Space Engineers plugins for Windows and Pulsar Legacy, with native settings menus and external HUDs.

| Plugin | Release | Purpose |
|---|---|---|
| Zeo Core | 1.0.8 | Tactical HUD, shared tracks, distress, docked refill and configurable signals |
| Zeo Nav | 1.1.15 | Navigation, signal intercept, velocity matching and docking |
| Zeo PDC Manager | 0.3.31 | WeaponCore point-defense management and threat/weapon diagnostics |
| Zeo Ore Helper | 1.0.5 | Ore surveys, asteroid/deposit search and configurable mining HUD |

## Install once, receive later releases through Pulsar

Download [Zeo automatic-update setup](https://github.com/logn0401-design/ZeoPluginHub/releases/tag/zeo-suite-2026-09-26), extract it, close Space Engineers, and run INSTALL.cmd. Pulsar Legacy must already be installed and launched once. The combined setup enables the four plugins above; individual plugin ZIPs enable only that plugin.

Setup adds/enables `logn0401-design/ZeoPluginHub` on branch `main` as a trusted source, switches the selected plugins from matching local DLL entries to their catalog entries, and backs up the current profile/source configuration. It retains plugin settings, saved named profiles, unrelated plugins and workshop mods. Old local DLLs are retained but disabled in the current profile, so they can be re-enabled for rollback. Do not enable both local and catalog copies of the same plugin.

Launch Space Engineers through Pulsar Legacy again. Pulsar downloads the selected runtimes and their checksum-verified matching overlays. No SDK or manual DLL copying is needed. Named profiles can select a different plugin set; run setup again after choosing a different profile if you want to migrate that profile too.

For manual source setup, expose Pulsar's Sources screen with the `-sources` launch option and add the repository above. Enable the catalog plugins you want and disable their older local/candidate equivalents.

## Updates

A source-code push is not a plugin release. Published entries pin a loader commit plus runtime/overlay asset hashes. Pulsar fetches a changed release when its source list refreshes and the plugin loads on a full game restart. Its normal source-list cache can last two hours. Setup clears only this hub's saved refresh metadata once, forcing a fresh check next launch; it does not shorten the global cache policy. Use source refresh/re-run setup after closing the game if a just-published release is not visible yet. Network/download failures and selected alternate-version pins can prevent an update.

Standalone local DLLs do not auto-update from this catalog. Users must make the one-time switch to catalog entries. A running game is not hot-patched.

## This release

Core's MENU KEY now supports click, press a key, then APPLY; Escape/Cancel discards a draft. Existing shortcuts are retained. Clear+Apply disables Core's shortcut; Pulsar Configure can reopen it. Core also includes the accumulated HUD, refill, tracking, help and performance-candidate work. PDC and Ore preserve their existing capture UI and protect text entry from menu hotkeys. PDC now receives its overlay from the same verified catalog release as its runtime.

Nav includes the latest prepared 1.1.15 release: Left Ctrl target reticle, intercept continuity, guarded flip assist and reduced repeated gyro/RCS state writes. Its source was taken from the completed 1.1.15 package, without rewriting flight logic in this publication task.

Build, settings, migration and isolated loader/asset tests passed. These checks do not replace in-game multiplayer, flight, capture or heavy-combat validation of this release. See [release validation](docs/CATALOG_RELEASE_2026-09-26.md).

## Rollback

Setup prints its backup directory under `%APPDATA%/Pulsar/Legacy/ZeoCatalogBackups`. With the game closed, restore that backup's `Current.xml` to `Legacy/Profiles/Current.xml` and `sources.xml` to `Legacy/Sources/sources.xml`, or switch the selected plugin back to its retained local entry in Pulsar. Do not enable duplicate copies.
