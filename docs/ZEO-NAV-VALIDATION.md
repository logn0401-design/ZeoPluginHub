# Zeo Nav 1.1.25 catalog validation

Validated October 1, 2026 against the installed Space Engineers Bin64 and Pulsar Legacy 2.4.2 compiler/parser.

- The 1.1.25 runtime, overlay, flight tests and UI tests build from the release checkout in Release/net48 with zero warnings/errors.
- 893 isolated flight/controller checks pass. They cover the 1.1.24 motion-recovery and intercept changes, hard departure/arrival SIG zones, aligned momentum capture, bounded stalled-turn handling, RCS retry budget gates and ETA presentation.
- 3,231 UI/settings/rendering assertions pass. The recovery-turn HUD fixture is included in the local test evidence.
- The installed Pulsar compiler accepts the 1.1.25 loader with no diagnostics. Twelve integration checks pass through Pulsar's descriptor parser and actual compiled loader, covering three asset hashes, overlay ZIP contents, missing-package rejection, runtime/overlay binding and unchanged persistent settings path. Init was not called.
- The 1.1.25 source snapshot supplied with the local candidate was copied into the release checkout before rebuilding. Source/assets are frozen at commit `ffcb607212e51bcd0a8148f28c7af40d72f2fa80`; the catalog descriptor pins that commit. Only Nav source, tests, docs, loader and versioned assets changed. Older assets remain available for rollback.

## Published assets

| Asset | SHA-256 |
| --- | --- |
| ZeoNav.dll | `e4d87607873f61e7d03ea81663eb8eea3c92351252f094171b6c37720995c53e` |
| ZeoNavOverlay.zip | `392b9e7a413eb7126262b303c1cab8215b86284a5ae372d3ed597121acb71f1c` |
| OwendB1-AutoDock-LICENSE.txt | `1126322e2cc8d165adc4c792eeb195717de2bcc7b39be1ce77959d78e87ef685` |

The overlay ZIP contains only `ZeoNavOverlay.exe` and `ZeoNavOverlay.exe.config` at its root. Both extracted files match the locally tested 1.1.25 payload by SHA-256. No game libraries, player settings, logs or credentials are distributed. Existing Nav settings and custom keys have no migration.

## Live validation boundary

The first local 1.1.25 installation was rolled back during a native `Legacy.exe` crash investigation. One access violation occurred on 1.1.24 before that install, and the later 1.1.25 crashes shared the same raw AMD graphics-driver stack addresses. That is not a symbolized root cause or proof of a Nav regression. The restored 1.1.24 game reached the world and remained responsive for hours; 1.1.25 still needs a creative-mode flight test. This release does not claim to fix the native crash.

Publication does not update a running game. Refresh the Zeo source, enable only one Nav entry and fully restart through Pulsar; the next Nav startup log should identify 1.1.25. In creative mode, check own Spectrum SIG against the selected departure/arrival caps, a high-speed correction without premature braking, recovery-turn progress and abort, ETA through flip/braking, and intercept acceleration. Other ship systems can raise own SIG independently of Nav's thrust commands. Repeat on the live server only after the creative check succeeds.
