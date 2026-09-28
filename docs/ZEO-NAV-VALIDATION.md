# Zeo Nav 1.1.23 catalog validation

Validated September 27, 2026 against the installed Space Engineers Bin64 and Pulsar Legacy compiler/parser.

- Runtime and matching overlay: Release/net48, assembly version 1.1.23.0, zero warnings/errors. Published payload hashes match the tested staged files.
- 823 isolated flight/control checks passed. Coverage includes unrestricted thrust without own-SIG gating, finite-zone model requirements, smooth profile bounds, retained braking authority, and ETA agreement with an analytic arrival solution.
- 3,231 UI/settings/rendering assertions passed. Coverage includes zero and large SIG limits, saved-config round trips, retained custom bindings and concurrent settings, close/invalid-edit policy, HUD layout and rendering fixtures.
- All 65 files in the staged source snapshot match the publish checkout. Loader metadata was then aligned to 1.1.23 and validated with the installed Pulsar compiler: success, no diagnostics.
- 12 integration checks passed through the installed Pulsar descriptor parser and actual compiled loader. Checks include all three asset hashes, overlay ZIP contents, missing-package rejection and exact runtime-to-overlay binding. Init was not invoked.
- Existing 1.1.22 and older assets are retained. The current catalog identity and settings path remain unchanged; unrelated plugin entries and assets are not part of this update.

## Published assets

| Asset | SHA-256 |
| --- | --- |
| ZeoNav.dll | `67b2f79b1e38e8d615990a8e7ed09705ce1f519be2a600e60bafa707b903f561` |
| ZeoNavOverlay.zip | `e75834f6468146f30176fbf359e7484a35abb893040e4eaed6f4b70a3ffd590c` |
| OwendB1-AutoDock-LICENSE.txt | `5ca2920d4f56954100c8f365461aba7b6c6154da49734f7b2703d9275ad49d11` |

The overlay ZIP contains only ZeoNavOverlay.exe and ZeoNavOverlay.exe.config at its root. The separate notice asset preserves AutoDock's MIT license. No game libraries, player settings, logs or credentials are distributed.

## Live validation boundary

Publication does not update a running game. Refresh sources and fully restart through Pulsar; the next Nav startup log should identify 1.1.23. This release has not yet demonstrated the menu fix, SIG transitions or ETA accuracy in-game or on a multiplayer server.

Prefer creative for the first check: Abort, turn departure OFF, apply a limit and reopen the menu; then test 0 with zone limits OFF and finite departure/arrival profiles with a buffer. Check actual own signature against the displayed blended ceiling and observe ETA through acceleration, flip, braking and terminal settle. Only one Nav entry should be enabled.

Existing saved limits are retained. A fresh installation defaults to 0/unrestricted. A positive enabled departure/arrival limit still applies to unrestricted cruise and requires fresh Spectrum telemetry. Flight recovery or unavailable authority yields an unknown ETA rather than a fabricated countdown.
