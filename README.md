# Zeo Plugins

Space Engineers plugins for Windows and Pulsar Legacy, with native settings menus and external HUDs.

| Plugin | Release | Purpose |
|---|---|---|
| Zeo Core | 1.0.8 | Tactical HUD, shared tracks, distress, docked refill and configurable signals |
| Zeo Nav | 1.1.25 | Navigation, signal intercept, velocity matching and docking |
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

For immediate Nav testing, download [Nav fast-update setup](https://github.com/logn0401-design/ZeoPluginHub/raw/refs/heads/main/install/Zeo-Nav-Fast-Update.zip), extract it, close Space Engineers, and run `FAST-NAV-UPDATE.cmd`. It enables only Nav's catalog entry, retains saved settings, backs up the active profile/source configuration, and forces a fresh Zeo source check on the next launch. It also sets Pulsar's global source-cache age to zero, so all catalog sources are checked on each future launch. A full game restart is still required to load updates; this setting must be applied separately on each player's PC.

## This release

Core's MENU KEY now supports click, press a key, then APPLY; Escape/Cancel discards a draft. Existing shortcuts are retained. Clear+Apply disables Core's shortcut; Pulsar Configure can reopen it. Core also includes the accumulated HUD, refill, tracking, help and performance-candidate work. PDC and Ore preserve their existing capture UI and protect text entry from menu hotkeys. PDC now receives its overlay from the same verified catalog release as its runtime.

Nav 1.1.25 includes the 1.1.24 motion-recovery and intercept corrections, then holds selected departure/arrival SIG limits throughout their protected zones, removes a duplicate flip allowance that could cause premature braking, bounds stalled-turn recovery, and steadies the displayed ETA. Existing settings and keys remain unchanged. Only Nav changes in this update; live flight and signal-cap behavior still need a creative-mode check. See [Nav notes](docs/ZEO-NAV.md) and [validation](docs/ZEO-NAV-VALIDATION.md).

Build, settings, migration and isolated loader/asset tests passed. These checks do not replace in-game multiplayer, flight, capture or heavy-combat validation. See [release validation](docs/CATALOG_RELEASE_2026-09-26.md) for the earlier catalog release.

## Rollback

Setup prints its backup directory under `%APPDATA%/Pulsar/Legacy/ZeoCatalogBackups`. With the game closed, restore that backup's `Current.xml` to `Legacy/Profiles/Current.xml` and `sources.xml` to `Legacy/Sources/sources.xml`, or switch the selected plugin back to its retained local entry in Pulsar. Do not enable duplicate copies.
