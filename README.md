# Zeo Plugins

Zeo plugins for Space Engineers 1 on Windows with Pulsar Legacy.

| Plugin | Public release | Purpose |
| --- | --- | --- |
| Zeo Core | 1.0.6 | Tactical HUD, TOS scope, ship and ammunition status, friendly fleet roster and Battle Manager contact sharing. |
| Zeo Nav | 1.0.3 | GPS navigation, speed control, flip-and-burn guidance, precision attitude controls and navigation HUD. |
| Zeo Ore Helper | 1.0.4 | Learned ore search, configurable pings, optional SDX2 scan estimates and approximate nearby deposit guidance. |

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

Each plugin has its own release version. Updates include matching runtime and overlay assets and preserve existing settings. These plugin updates do not complete the separate backend authorization migration.

## Current HUD update

Core 1.0.6 removes retained Spectrum ghost signals, stabilizes confirmed contact numbers and adds aggregate performance timings. Nav 1.0.3 and Ore Helper 1.0.2 add edge/corner resizing. Across all three, width adjusts columns and height scales rows/text without overwriting font preferences. Nav retains its SDX drive catalog and GPS search. [Changes, test evidence and limitations](docs/HUD_CONSISTENCY_2026-09-24.md).

## Feature notes

- **Core:** Includes configurable draggable HUD panels and a native Zeo settings menu. Spectrum / Auto follows the native Spectrum signal timing and position; Grid Center remains available for locally replicated entities. Remote tracks still depend on telemetry and prediction. The external overlay has some display latency. Battle Manager sharing requires compatible telemetry and configuration; the existing open-test sharing endpoints have not yet completed the authenticated migration.
- **Nav:** Includes the matching external HUD, Epstein main-drive support and own-ship Spectrum signature information. Spectrum features require compatible world/server telemetry. See [Zeo Nav guide](docs/ZEO-NAV.md).
- **Ore Helper:** Search starts off each launch. Nearby deposit guidance defaults to 5 km. Ore quantities are sampled estimates, not exact server totals. Existing selections, HUD positions and learned data are preserved. See [Ore Helper guide](docs/ORE_HELPER.md).
- **PDC:** Distributed separately as a local package. Its 1.0 rebrand and rebuilt installer are pending; this catalog does not yet install it. Its bank-queue planner remains observational and is not a completed replacement for native target control.

## Maintainers

See the [release procedure](docs/RELEASING.md). Keep descriptors commit-pinned and verify asset SHA-256 hashes. Rebuild matching runtime and overlay assets when changing compiled code; source edits alone do not update supplied binaries. No game DLLs, credentials, player payloads or machine-specific settings belong in this repository.

## Zeo Core 1.0.1 — saved distress GPS

Active distress locations received from any sector now save to the normal Space Engineers GPS list, with Show on HUD off initially. Names include the reporting sector. Refreshes update existing points rather than duplicating them; points stay saved when a call ends. Zeo Nav can read these hidden GPS entries. This does not start navigation automatically or change sector travel mechanics.

Uses the existing main Zeo Core catalog entry; the separate inventory candidate is not included. Refresh Pulsar sources and fully restart the game. Keep only the main Zeo Core enabled.

Core and overlay builds, 25 GPS lifecycle/API-adapter tests and the installed Pulsar entry-point compiler passed. Multiplayer save/rejoin, sector transfer and NavOS runtime behavior still need in-game verification. Full details are in docs/DISTRESS_GPS_1.0.1.md.

## Zeo Nav 1.0.2 — main drives and GPS search

All 31 inspected SDX main-drive variants now have explicit built-in entries. Type in SEARCH GPS beside the native dropdown to narrow destinations by name, choose a result and start. The 1.0.1 overlay-lifetime fix is retained. Refresh Pulsar sources and fully restart. [Drive catalog and validation](docs/ZEO-NAV-1.0.2.md).

## Ore Helper 1.0.3 — native menu key binding

The native menu now has a **MENU KEY** button in its footer. Click it, click the current key, press a new keyboard key, then **APPLY**. **CLEAR** disables the shortcut only after APPLY. Closing with ESC or navigating away discards the binding draft. Pulsar Configure or `/ore menu` can reopen the menu if the shortcut is disabled.

This follows PDC's menu-key capture pattern and preserves existing bindings. The key can now be any supported non-modifier keyboard key (ESC stays reserved), including keys outside the former short dropdown. This is a single-key menu binding, like PDC's menu key. Ore Helper's shortcut pauses while a binding draft is being edited. The 1.0.2 resizing, text scaling, saved layout and automatic overlay shutdown changes remain intact. Refresh sources and restart to update. [Validation](docs/ORE_KEYBIND_1.0.3.md).

## Ore Helper 1.0.4 — simpler menus

Common search settings have one home on SEARCH, including an ANY / ALL ore-match switch. The footer has one MOVE / RESIZE HUD button and the existing MENU KEY button. Shorter labels, simpler category names, fewer repeated controls and explanatory tooltips make the remaining settings easier to navigate. Existing settings and saved choices are preserved. Refresh Pulsar sources and restart to update. [Validation](docs/ORE_MENU_1.0.4.md).
