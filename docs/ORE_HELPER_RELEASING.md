# Releasing Ore Helper

Build both projects under src/ZeosOreHelper in Release/net48. ClientPlugin uses assembly name ZeosOreHelper.Runtime; keep its game Bin64 references out of the payload. Keep the loader version aligned. Run tests/OreHelper and tests/OreHelperScanner against this source.

Place the runtime DLL and an overlay ZIP (EXE plus .config at archive root) in a new version-specific assets/orehelper folder. Hash both assets with SHA-256. Verify the loader through Pulsar's compiler and validate the XML with Pulsar's serializer. Commit source/assets first; pin Plugins/ZeosOreHelper.xml to that full commit and the matching hashes in a separate commit. Fetch immediately before publication to preserve concurrent Core/catalog work. Keep old pinned assets for rollback.

The LoadAssets hook selects the verified extracted overlay path. Settings, selection profiles and learning remain under the existing Pulsar/ZeosOreHelper data folder. Do not distribute live user files, SDKs, game assemblies or credentials. Initial load/update/rollback and spatial alignment still require live testing; mark unverified catalog releases as Pulsar Test.
