# Zeo Nav 1.0.2 — drive catalog and GPS search

Based on the published 1.0.1 overlay-lifetime release. Audited 2026-09-24.

## Built-in main drives

The installed Space Engineers Workshop files contain 31 SDX main-drive definitions in Workshop item 2815514917, under Data/CubeBlocks/Drives and Drives/mes. Each subtype now has an explicit, case-insensitive compiled entry in MainDriveCatalog.cs. The prior SDX-family matcher already covered these names; this change makes that coverage explicit and regression-tested. It is not evidence that the separately reported missed-drive issue was caused by a missing subtype.

Identity does not depend on current thrust, enabled state or display language. Existing family/metadata recognition remains for future and other modded Epstein drives. RCS stays separate. All recognized thrusters still use actual game-reported available thrust; no definition rating overrides damage, fuel or power checks. Existing mechanically connected subgrid scanning and attitude control are unchanged.

| Subtype | Display name | Definition thrust (MN) | Fuel |
| --- | --- | ---: | --- |
| `sdx_driveCivilian3x3` | Epstein Technologies “Rockhopper” Series Drive | 35 | Water |
| `sdx_driveCivilian3x3_small` | Epstein Technologies “Manéo” Series Drive | 10 | Water |
| `sdx_driveCivilian3x3_smallMes` | Epstein Technologies “Manéo” Series Drive | 55 | Water |
| `sdx_driveCivilian3x3Mes` | Epstein Technologies “Rockhopper” Series Drive | 35 | Water |
| `sdx_driveCivilian5x5` | Epstein Technologies “Esquibel” Series Drive | 77 | Water |
| `sdx_driveCivilian5x5Mes` | Epstein Technologies “Esquibel” Series Drive | 77 | Water |
| `sdx_driveCivilian7x7` | Epstein Technologies “Solomon” Series Drive | 170 | Water |
| `sdx_driveCivilian7x7Mes` | Epstein Technologies “Solomon” Series Drive | 170 | Water |
| `sdx_driveIndustrial7x7` | Industrial “Canterbury” Series Drive | 350 | Water |
| `sdx_driveIndustrial7x7Mes` | Industrial “Canterbury” Series Drive | 350 | Water |
| `sdx_driveIndustrial9x9` | Industrial “Scopuli” Series Drive | 703 | Water |
| `sdx_driveIndustrial9x9Mes` | Industrial “Scopuli” Series Drive | 703 | Water |
| `sdx_driveMcrnMilitary3x3` | RT6-B "Morrigan" Series Drive | 60.5 | Water |
| `sdx_driveMcrnMilitary3x3Mes` | RT6-B "Morrigan" Series Drive | 60.5 | Water |
| `sdx_driveMcrnMilitary5x5` | RT7 "Tachi" Series Drive | 133 | Water |
| `sdx_driveMcrnMilitary5x5Mes` | RT7 "Tachi" Series Drive | 133 | Water |
| `sdx_driveMcrnMilitary7x7` | RTF-B "Scirocco" Series Drive | 292 | Water |
| `sdx_driveMcrnMilitary7x7Mes` | RTF-B "Scirocco" Series Drive | 292 | Water |
| `sdx_driveOpaMilitary3x3` | G-750 "Michio" Series Drive | 46.75 | Water |
| `sdx_driveOpaMilitary3x3Mes` | G-750 "Michio" Series Drive | 46.75 | Water |
| `sdx_driveOpaMilitary5x5` | G-1000 "Kamina" Series Drive | 102.85 | Water |
| `sdx_driveOpaMilitary5x5Mes` | G-1000 "Kamina" Series Drive | 102.85 | Water |
| `sdx_driveOpaMilitary7x7` | G-2000 "Gatamang" Series Drive | 226 | Water |
| `sdx_driveOpaMilitary7x7Mes` | G-2000 "Gatamang" Series Drive | 226 | Water |
| `sdx_driveTorch3x3_small` | Small Fusion Torch Drive | 3 | Water |
| `sdx_driveUnnMilitary3x3` | S-100 "Phantom" Series Drive | 52.25 | Water |
| `sdx_driveUnnMilitary3x3Mes` | S-100 "Phantom" Series Drive | 52.25 | Water |
| `sdx_driveUnnMilitary5x5` | S-250 "Leonidas" Series Drive | 115 | Water |
| `sdx_driveUnnMilitary5x5Mes` | S-250 "Leonidas" Series Drive | 115 | Water |
| `sdx_driveUnnMilitary7x7` | S-700 "Xerxes" Series Drive | 252 | Water |
| `sdx_driveUnnMilitary7x7Mes` | S-700 "Xerxes" Series Drive | 252 | Water |

The small-grid civilian MES definition is rated 55 MN in these files, versus 10 MN for the ordinary small-grid version. These are inspected mod values, not fixed flight-controller limits. The checked-in MainDrives.tsv records the exact definition files used by the regression tests.

## Native GPS picker

Type in SEARCH GPS beside the dropdown to filter by any part of the name. Matching ignores case; multiple words narrow the list in any order. Clear the field to restore all destinations. Choose a result, then START ROUTE. Enter in the search field moves focus to the dropdown and never starts flight.

Filtering does not rebuild the whole settings screen or commit unrelated settings. Duplicate names remain distinct by coordinates. A hidden, removed or moved selection is cleared; Nav never automatically chooses the first replacement. Incoming GPS additions and renames refresh the filtered list. The existing FULL / LEGACY SETTINGS window is unchanged.

## Validation

- Catalog runtime, overlay and both test projects: zero build warnings/errors.
- 223 isolated control/catalog/search assertions passed, including all 31 exact drive IDs at zero/unknown runtime thrust, RCS separation, partial and multiword search, duplicate-name identity, refreshed lists and blocked START after selection clears.
- 1,568 existing UI/persistence/rendering assertions passed.
- Installed Pulsar compiler: success with no diagnostics.
- Manual installer sources are built separately, and installer syntax/package hashes are checked before delivery.

Native in-game focus/dropdown appearance, real ship availability and server flight still require an in-game check. Offline logic and renderer fixtures are not a live native UI test.
