# Zeo Core 1.0.1: all-sector distress GPS

Adds permanent, initially hidden native GPS entries for active received distress calls with finite coordinates. All reporting sectors are included independently of the local HUD cross-sector projection switches. The sector appears in the name and description. GPS entries use the reported coordinates; this feature does not convert sector coordinates or start navigation.

Deduplication scopes entries by FleetLink host/world, receiver Steam account (identity fallback), faction, reporting sector and distress source. Receiver sector/session names are deliberately excluded so a sector transition does not duplicate entries already in the GPS list. The game owns persistence of its per-player/world GPS collection; if a sector uses a separate save, active received calls are recreated in that save. Historical inactive points are not copied between unrelated world saves.

Location updates occur at most every five seconds after at least 25 m movement. Existing names and Show on HUD preferences remain under player control. No GPS entry is automatically removed. Deleting an acknowledged point suppresses recreation during that active call in the current plugin session. Resolved calls leave a saved last-received position, labelled accordingly. Offline/receive-disabled/factionless states create no new points. Networking and server visibility rules are unchanged.

Implementation calls the real game's Create(name,description,position,false,false), DiscardAt=null, AddGps for the local identity, and ModifyGps with the original hash on the simulation thread. AddLocalGps is not used because it does not persist. No autopilot is engaged. Zeo Nav reads GPS regardless of Show on HUD; NavOS integration and multiplayer save/rejoin require live testing.

Validation: runtime and overlay builds against installed SE APIs passed with no errors (NuGet vulnerability metadata lookup unavailable, NU1900). 25 lifecycle/adapter checks passed including multi-sector entries, same ID/different-sector separation, receiver sector changes, dedup/restart, location updates, hidden/permanent flags, own-player targeting, invalid/missing coordinates, expiry, user edits/deletion and retry. Pulsar 2.4.2 entry-point compiler passed without diagnostics. These checks are not a completed in-game multiplayer test.

Previous main catalog descriptor/assets remain in Git history for rollback. Existing configurations, native UI and inventory-test files are not overwritten by this catalog release.
