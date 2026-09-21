# Zeo Core catalog candidate verification

Candidate: V1.4g Pulsar catalog test, based on V1.4f runtime (also used by the V1.4f1 installer fix).

- Core runtime Release/net48 build: passed, zero warnings/errors.
- Overlay Release/net48 build: passed, zero warnings/errors.
- Entry point compiled with installed Pulsar 2.4.2 Compiler.exe and its normal Legacy reference set: passed, no diagnostics.
- Descriptor deserialized by installed Pulsar.Shared.dll: passed. Commit pin, source directory, Windows/CLR restriction and both asset hashes checked.
- Seven offline runtime/entry-point asset checks passed: missing asset, missing executable and missing configuration rejected; exact extracted overlay selected; persistent data directory retained; configuration action retained; compiled entry-point dependency/asset loading succeeds.
- 29 existing C# files unchanged. Only Plugin.cs (version and asset callback) and OverlayBridge.cs (asset path selection) changed. Existing HUD, network, serialization, configuration and rendering code are retained.
- Archive integrity and EXE/config contents checked. Public file inventory excludes build caches, PDBs, logs, user configuration, game DLLs and private payloads; credential and machine-path pattern scan passed. The pattern scan is a limited static check, not a formal security audit.

No game session or overlay process was launched for these checks. Initial installation, a second-release update, rollback and in-game behavior still require testing. This candidate does not implement the proposed backend security upgrade.
