# Zeo Core 1.0.5 — observed combat telemetry

Core observes WeaponCore projectile creation/despawn and bounded position samples on the controlled mechanical construct. It does not command weapons. Capture follows the existing telemetry TX preference and stops when disabled, when the pilot leaves the construct, or on world/plugin shutdown.

Fire events carry observed positions, available velocity, ammo, weapon part, a session-qualified decimal projectile ID, and observation age. The server maps event age to its source capture clock. A bounded rolling queue tolerates skipped HTTP sends; ingestion deduplicates launch/despawn events. Up to 512 weapon parts, 128 sampled projectiles and 1024 retained events are supported. Heavy fire may exceed these limits; capability diagnostics expose dropped-event counts. Sampling and network delivery are not guaranteed exhaustive combat recording.

War Room uses recorded trajectories with at most 300 ms extrapolation. Disappearance is not a confirmed hit and does not create an invented explosion. Non-reporting enemy weapon fire is not discovered by this capture path. All reporting ships must use this update to contribute their own fire.

The website stores immutable faction/observer receipt ownership with new telemetry and scopes raw evidence before replay fusion. Existing open-test ingestion authentication is unchanged by this release; it remains a separate hardening concern. Historical data without capture-time ownership is not exposed through the new replay route. New recordings are grouped by hostile observations, shots or losses, with the configured quiet-gap threshold (default 60 seconds). Times are mapped observation times, not a video recording.

Validation: production runtime and paired overlay build; actual Pulsar loader compilation; 15 capture lifecycle/bounds tests with simulated APIs; 218 trajectory/ring checks; isolated server faction/self/ownership-change and replay tests. Actual multiplayer firing verification remains pending.

Distribution: regular Zeo Core catalog entry, version 1.0.5. Keep the inventory candidate disabled. Fully restart Space Engineers/Pulsar to load the catalog update.
