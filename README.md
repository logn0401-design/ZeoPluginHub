# Zeo Plugins for Pulsar

First catalog candidate: **Zeo Core V1.4g — Pulsar distribution test**, based on the V1.4f ammo HUD fix. Zeo Nav and Zeo Ore Helper are not included yet.

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

Apply the source changes and refresh if needed. The catalog contains **Zeo Core (Pulsar Test)** once the release descriptor is published.

## Existing manually installed Core users

Before the first switch, close the game and overlay and back up `%APPDATA%/Pulsar/ZeoCore` and your Pulsar profiles. In Pulsar, disable the old local Core entry before enabling the catalog version. Leave your existing configuration and HUD layout files in place; the catalog version uses the same data folder. Never enable both entries together. Restart the game completely.

If rolling back, disable the catalog entry, re-enable your previous local Core entry, and restart. Keep the previous universal installer available. Do not delete the ZeoCore settings folder.

## Updates

Pulsar checks source metadata at startup, subject to its cache age (normally two hours), or when sources are explicitly refreshed. Changes to the pinned source commit or declared assets invalidate its plugin cache. Restart to load the new release; updates do not replace code in a running game.

The repository contains a small Pulsar entry point, the full Core/overlay source, a compiled Core runtime dependency, and a matching overlay archive. Pulsar verifies SHA-256 hashes for both runtime assets. This preserves Core's existing .NET Framework serializer instead of replacing it for the catalog.

The compiled runtime and overlay are cached by Pulsar. Persistent settings remain in `%APPDATA%/Pulsar/ZeoCore`. No game DLLs, credentials, player payloads, or machine-specific configuration files are distributed here.

## Maintainers

See [release procedure](docs/RELEASING.md). Only publish tested, commit-pinned descriptors. Source changes alone do not rebuild the supplied Core runtime: rebuild both components, replace the assets, and refresh their hashes as part of every release.
