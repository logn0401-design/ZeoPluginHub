# Zeo Plugins for Pulsar

First catalog candidate: **Zeo Core V1.4g — Pulsar distribution test**, based on the V1.4f ammo HUD fix. **Zeo Ore Helper v0.7.2** is also available as a Pulsar test entry. Zeo Nav is not included yet.

This is a test release. Local build and packaging checks do not replace an in-game test. It retains the existing open-test Battle Manager networking; it is not the proposed backend security upgrade.

## Add this catalog

Use Pulsar **2.4.2 or later**, **Legacy**, on Windows with Space Engineers 1. Add `-sources` to your existing Pulsar Steam launch options, then restart.

In Pulsar's plugin menu, open **Sources**. Under **Hubs**, choose **Add Remote Hub**:

| Field | Value |
| --- | --- |
| Display Name | Zeo Plugins |
| GitHub User | logn0401-design |
| Repo Name | ZeoPluginHub |
| Branch Name | main |

Apply the source changes and refresh if needed. The catalog contains **Zeo Core (Pulsar Test)** and **Zeo Ore Helper (Pulsar Test)**.

## V1.4h tracking update

Known, locally available ship tracks now read their current grid position on every marker projection instead of waiting for the slower track-building pass. Fast camera marker updates also wake the external overlay when fresh packets arrive. Detection-position mode, stale tracks and remote-only signals retain their existing fallback behavior; no nearest-ship snapping is introduced.

For the first test, use **Home > SCOPE**: keep **Fast camera marker updates** ON, and enable **Smooth / predict track motion between sensor updates** for signals whose ships are not locally available. Leave the prediction limit at 1 second initially. Under MARKERS, use **Auto** or **Grid Center** anchoring. These preferences are not changed automatically. The separate external overlay still has some display latency; an exact render-synchronized lock is not claimed.

Refresh Pulsar sources and fully restart to update. The old local Core entry should stay disabled. Confirm V1.4h in Core's version/debug output, then compare moving targets and camera panning.

## Existing manually installed Core users

Before the first switch, close the game and overlay and back up `%APPDATA%/Pulsar/ZeoCore` and your Pulsar profiles. In Pulsar, disable the old local Core entry before enabling the catalog version. Leave your existing configuration and HUD layout files in place; the catalog version uses the same data folder. Never enable both entries together. Restart the game completely.

If rolling back, disable the catalog entry, re-enable your previous local Core entry, and restart. Keep the previous universal installer available. Do not delete the ZeoCore settings folder.

## Updates

Pulsar checks source metadata at startup, subject to its cache age (normally two hours), or when sources are explicitly refreshed. Changes to the pinned source commit or declared assets invalidate its plugin cache. Restart to load the new release; updates do not replace code in a running game.

The repository contains a small Pulsar entry point, the full Core/overlay source, a compiled Core runtime dependency, and a matching overlay archive. Pulsar verifies SHA-256 hashes for both runtime assets. This preserves Core's existing .NET Framework serializer instead of replacing it for the catalog.

The compiled runtime and overlay are cached by Pulsar. Persistent settings remain in `%APPDATA%/Pulsar/ZeoCore`. No game DLLs, credentials, player payloads, or machine-specific configuration files are distributed here.

## Maintainers

See [release procedure](docs/RELEASING.md). Only publish tested, commit-pinned descriptors. Source changes alone do not rebuild the supplied Core runtime: rebuild both components, replace the assets, and refresh their hashes as part of every release.

## Zeo Ore Helper v0.7.2

Enable **Zeo Ore Helper (Pulsar Test)**, then fully restart. Before switching from the universal installer, disable the old local **ZeosOreHelper** entry and close its overlay. Do not run both versions together. Settings, ore selections, HUD positions, cached asteroids and learned baselines remain under `%APPDATA%/Pulsar/ZeosOreHelper`.

Open the menu with PageUp (or your saved key), select ores, and START SEARCH. The helper still starts OFF each launch. SDX2 completed client scan estimates are optional under LEARNING; nearby deposit guidance is under PINGS. The deposit range defaults to 5 km, with four deposit markers inside your total ping cap. SDX2's inspected server API does not supply exact quantities: these are sampled client estimates. Full feature notes and limitations: [Ore Helper guide](docs/ORE_HELPER.md).

Pulsar downloads the paired runtime and overlay with SHA-256 verification. No manual installer or SDK is needed. Refresh sources and restart for updates. For rollback, disable this catalog entry and re-enable your prior local version; keep the settings folder. Catalog loading and live deposit alignment still need in-game verification.
