# Zeo Nav v0.1.23 Pulsar catalog validation

Validated 2026-09-20 against installed Space Engineers assemblies and Pulsar Legacy 2.4.2.

- Runtime, overlay, flight tests and UI tests build in Release/net48 with zero warnings and zero errors. Builds used the existing local .NET Framework reference package cache.
- 146 isolated flight/control/schema/catalog checks passed, including eight checks for the new overlay asset binding.
- 1,568 UI assertions passed, including settings persistence, invalid edits, 50k speed input, and rendering fixtures.
- Installed Pulsar Compiler.exe compiled the small catalog entry point with the packaged runtime reference: success, no diagnostics.
- 11 integration checks passed using the actual compiled entry point, runtime DLL, extracted overlay ZIP, and installed Pulsar.Shared.dll descriptor parser. These include incomplete-package rejection, settings-path preservation, exact ZIP contents and asset hashes. Init was not invoked.
- Production source parity with the v0.1.23 one-click baseline: only Plugin.cs and the runtime project differ. Changes are the catalog version suffix, pre-Init overlay asset binding, overlay executable path selection, and the distinct ZeoNav.Runtime assembly name. Navigation, thrust, drive classification, Spectrum, governor, native UI and overlay sources are otherwise byte-identical.

## Runtime assets

| Asset | SHA-256 |
| --- | --- |
| ZeoNav.Runtime.dll | `4f1b3b71f1684756e3fa20e23d0549a8ec201b39cd5154832169a3934b8efdf8` |
| ZeoNavOverlay.zip | `69f8aea5d2f1c3350a75d7e5f3e909a520083e72c41bc70e2dd0c417dc0250b6` |

The ZIP contains only ZeoNavOverlay.exe and ZeoNavOverlay.exe.config at its root. No game libraries, live player settings, logs or credentials are included in the Nav catalog payload.

## Remaining live validation

A fresh catalog installation, overlay launch and real flight on another player's computer are not covered by these offline checks. Test with only one Nav entry enabled, confirm the PULSAR version in the Nav log, and verify GO, STOP, settings close, own-ship SIG feedback, forward thrust and flip-and-burn in the intended server environment. Existing v0.1.23 feedback does not prove that every server's wobble has been eliminated.
