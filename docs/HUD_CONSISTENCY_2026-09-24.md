# Zeo HUD consistency — 2026-09-24

Targets: Zeo Core 1.0.6, Zeo Nav 1.0.3, Zeo Ore Helper 1.0.2. Regular Pulsar catalog entries; inventory candidate and PDC Manager are separate.

## Tracking
Spectrum / Auto now uses the native Spectrum signal position, observation tick, velocity and acceleration estimate, projected using the current camera. Grid Center remains available. The installed Spectrum HUD itself extrapolates signals; it is not an exact screen-position API. Its acceleration is not exposed through the API, so Zeo estimates it from consecutive observations. First observations or missed intermediate packets can still differ. Camera packets continue without rerunning fusion.

Successful Spectrum snapshots replace the visible signal cache, including an empty snapshot. The old cache incorrectly kept emitter IDs after Spectrum retired them. Failed API reads retain data only within Spectrum's 15-second window. Freshness uses DetectedAt, not the time of the last API poll. Poll cadence is adaptive (3 / 6 / 10 simulation frames).

Confirmed entity IDs retain the same number across WeaponCore, fleet and Spectrum sources. IDs are allocated after fusion; heavy scopes no longer reuse an active number after 99 contacts. Anonymous Spectrum emitter replacement may still receive a new number: the API does not expose OldEmitterId. Do not invent identity from proximity. Retired anonymous signals disappear instead of producing frozen ghosts.

## Resize behavior
Core, Nav and Ore: drag inside to move, edges to resize one axis, corners to resize both. Width changes columns; height changes row/text scale. Font preferences remain unchanged, and growing the frame recalculates text from those preferences. Long labels use bounded fitting/ellipsis instead of shrinking without limit or overlapping adjacent fields. Very narrow frames cannot display every long value in full; widen the frame to reveal it.

Nav and Ore now have edge/corner resize grips, draft Save/Cancel/Undo and independent width/height numeric settings. Nav's separately released 1.0.2 SDX drive catalog and searchable GPS picker are retained in 1.0.3. Existing configs, keybinds, capture controls, flight logic, shared data, refill and overlay ownership remain intact. No changes to the separate PDC Manager source or installed files.

## Performance
Removed per-signal account lookups and an unused fusion-ID set. Spectrum matching uses cached samples on the fast camera path. Added a 30-second aggregate log with average/max Spectrum-read, fusion and projection timings. Logs contain no contact identities or coordinates. Existing logs show isolated fusion spikes, not enough evidence for a sustained FPS diagnosis.

Offline production-controller fixture: 192 local contacts, 1,000 fusion builds averaged approximately 0.072 ms/build on this PC (reflection included). This excludes live game APIs, network timing and overlay painting; it is not a game FPS benchmark.

## UI simplification recommendation
Keep all current controls and backing settings. Proposed front page: HUD visibility, Edit layout, Sensors, Sharing, Ammunition/Quick Refill. Group detailed options under Appearance, Flight, Contacts, Fleet & Distress, and Advanced. Add search across setting labels and panel names before moving controls. Keep explicit ON/OFF states. Move version/changelog and diagnostic counters to About/Diagnostics. Show connection state and observation freshness separately. Keep capture and privacy controls easy to find. Avoid calling open-test authorization secure. This larger menu regrouping is a proposal, not part of this release.

## Alliance / Ali data
Live browser inspection confirmed Intel > Supplies > Alliance is present. The current tab reports authentication required, so sign in again before checking its data or leader controls. The deployed website already includes Intel inventory views for Self, Faction and Alliance. Alliance inventory requires mutual current-leader consent plus explicit base/category grants; revocation takes effect on reads. Tactical alliance sharing remains separate and currently falls back to faction scope. Do not broaden it merely by enabling an alliance tab. The user's phrase Ali shared data is awaiting clarification: allied-faction sharing versus importing Ali's BattleSpace data. No website permissions or external integration changed in this update.

## Verification / in-game follow-up
Six production projects build in Release/net48. Tests passed: 342 Core resize, 64 marker/ID, 9 actual controller cache/fusion, 1,591 Nav UI, 223 Nav flight/catalog, and 1,990 Ore assertions. Real Pulsar compiler accepts all three loaders with no diagnostics. Offline cache/fusion, marker, layout, persistence and renderer fixtures are used; live-game validation is still required.
After publication: fully close and relaunch through Pulsar with the regular Core/Nav/Ore entries enabled. For Core use Markers > Marker anchor > Spectrum / Auto. Compare a native Spectrum signal while moving and panning; watch source handoffs. Resize each HUD narrower, wider, shorter, taller, then Save, reopen and Undo/Cancel. Check long labels and numeric values. After 30 seconds, aggregate PERF HUD entries allow comparison without uploading tactical payloads.

Rollback baseline: Before_Hud_Consistency.zip plus Nav_1.0.2_Baseline_Addendum.zip (Nav advanced independently during this task). Catalog rollback should restore only these three descriptors to their previous commit pins, preserving unrelated newer repository work. Do not force-reset main or overwrite user settings.
