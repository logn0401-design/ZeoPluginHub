# Ore Helper v0.7.2 catalog validation

September 20, 2026. Both production projects compile Release/net48 x64 against the installed Space Engineers assemblies with zero warnings/errors. The catalog adaptation changes only the runtime assembly name, adds the LoadAssets hook and selects the supplied overlay executable. All scanner, ranking, menu, learning, renderer and capture code otherwise matches the v0.7.2 universal release.

- Main source-linked regression harness: 1,961 assertions passed against the catalog source.
- Scanner/SDX2 reader harness: 21 assertions passed with deterministic game inputs.
- Installed Pulsar compiler: Success=true, no diagnostics, no error. Its actual compiled entry point loads the supplied runtime dependency.
- Seven runtime/asset tests passed: missing package/EXE/config rejected, extracted executable selected, persistent settings path preserved, configuration action available, compiled wrapper loads its assets. No Init/Update, game, or overlay was started by these tests.
- Installed Pulsar XML serializer recognizes GitHubPlugin, Windows/CLR restrictions, exact loader source scope, paired Reference/Extract asset modes and SHA-256 hashes.
- Overlay archive contains only the EXE and .config; CRC and exact built-file bytes checked. The catalog descriptor pins the source/assets commit, separately from the descriptor commit.

Reproduce catalog checks with `python tests/OreHelperCatalog/compile_loader.py`, followed by Windows PowerShell 5.1 `tests/OreHelperCatalog/Test-Assets.ps1` and `Test-Descriptor.ps1`. These require an installed Pulsar and Space Engineers. Runtime assemblies are supplied; end-user catalog installation does not need a developer SDK.

Still unverified: live initial catalog load, transition from the old local entry, update/rollback across game restarts, workshop assembly visibility for SDX2, spatial alignment of deposits and frame-time impact. This entry is marked Pulsar Test. Old local and catalog entries must not be enabled together. No installed profile, user settings, learned data or live game files were changed during publication.
