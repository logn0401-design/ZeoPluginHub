# Catalog release validation — 2026-09-26

Public versions: Core 1.0.8, Nav 1.1.15, PDC Manager 0.3.31, Ore Helper 1.0.5.

- Eight Release/net48 runtime/overlay builds against installed Space Engineers assemblies passed. PDC retains one pre-existing unused-field warning.
- All four installed Pulsar compiler checks passed with no diagnostics.
- Twenty-four isolated asset/entry-point checks passed: missing package, missing executable and missing executable configuration are rejected; the correct extracted overlay is selected and the actual compiled catalog wrapper preserves Configure and asset forwarding. Init/Update and overlay processes were not launched by these checks.
- Twelve profile/source migration fixtures passed, including other plugin/mod preservation, duplicate local-entry removal, version-pin clearing for selected plugins only, idempotence and XML disk round trips.
- Core menu-binding/settings suite: 360 checks; help catalog suite: 675 checks. Legacy preferences, custom key round trips, disabled bindings, collisions and concurrent settings are covered.
- Nav completed-package validation reports 709 flight/input and 2,933 UI/renderer assertions. Its latest source was copied from that package; this release task did not alter its flight code.
- Ore 1.0.4 baseline reports 2,023 assertions; 1.0.5 changes the native text-input hotkey guard and release identity. Runtime and paired overlay were rebuilt for this release.

No claims of completed live multiplayer or flight validation are made. Immutable asset SHA-256 values are in each plugin descriptor. Two commits are used: first loader/source/assets, then catalog descriptors pinned to that first commit. The publication script refuses to move main if another release advanced it during preparation.
