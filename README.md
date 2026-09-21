# Zeo Plugins

Zeo plugins for Space Engineers 1 on Windows with Pulsar Legacy.

| Plugin | Public release | Purpose |
| --- | --- | --- |
| Zeo Core | 1.0 | Tactical HUD, TOS scope, ship and ammunition status, friendly fleet roster and Battle Manager contact sharing. |
| Zeo Nav | 1.0 | GPS navigation, speed control, flip-and-burn guidance, precision attitude controls and navigation HUD. |
| Zeo Ore Helper | 1.0 | Learned ore search, configurable pings, optional SDX2 scan estimates and approximate nearby deposit guidance. |

## Add the catalog

Use Pulsar Legacy 2.4.2 or later on Windows. Add `-sources` to your existing Pulsar Steam launch options and restart. In Pulsar, open **Sources > Hubs > Add Remote Hub**:

| Field | Value |
| --- | --- |
| Display Name | Zeo Plugins |
| GitHub User | logn0401-design |
| Repo Name | ZeoPluginHub |
| Branch Name | main |

Apply and refresh sources. Enable **Zeo Core**, **Zeo Nav** or **Zeo Ore Helper**, then fully restart. Disable any older local entry for the same plugin before enabling its catalog entry. Keep your existing settings folders.

## Updating

Close Space Engineers and all Zeo overlays before refreshing and restarting. A leftover overlay process can lock files in Pulsar's cache and prevent an update. Pulsar checks source metadata at startup, subject to cache age, or when sources are explicitly refreshed. Updates load after a full restart.

Plugin IDs, settings locations and working runtime assets are preserved by this 1.0 naming release. The public release number is 1.0; internal build identifiers currently remain Core V1.4h, Nav v0.1.23 and Ore Helper v0.7.2. These are the same runtime builds, not a functional replacement or a completed backend security migration.

## Feature notes

- **Core:** Includes configurable draggable HUD panels and a native Zeo settings menu. Locally available tracked ships use live position anchors; remote-only tracks still depend on telemetry and prediction. The external overlay has some display latency. Battle Manager sharing requires compatible telemetry and configuration; the existing open-test sharing endpoints have not yet completed the authenticated migration.
- **Nav:** Includes the matching external HUD, Epstein main-drive support and own-ship Spectrum signature information. Spectrum features require compatible world/server telemetry. See [Zeo Nav guide](docs/ZEO-NAV.md).
- **Ore Helper:** Search starts off each launch. Nearby deposit guidance defaults to 5 km. Ore quantities are sampled estimates, not exact server totals. Existing selections, HUD positions and learned data are preserved. See [Ore Helper guide](docs/ORE_HELPER.md).
- **PDC:** Distributed separately as a local package. Its 1.0 rebrand and rebuilt installer are pending; this catalog does not yet install it. Its bank-queue planner remains observational and is not a completed replacement for native target control.

## Maintainers

See the [release procedure](docs/RELEASING.md). Keep descriptors commit-pinned and verify asset SHA-256 hashes. Rebuild matching runtime and overlay assets when changing compiled code; source edits alone do not update supplied binaries. No game DLLs, credentials, player payloads or machine-specific settings belong in this repository.
