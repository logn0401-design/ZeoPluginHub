# Zeos Ore Helper v0.7.2 — SDX2 Estimates and Deposit Map

Install the **Zeo Ore Helper (Pulsar Test)** catalog entry using the repository README. Disable the old local Ore Helper entry, refresh sources and fully restart. No SDK or manual installer is needed.

## New in v0.7.2

**SDX2's inspected server API does not send ore quantities or deposit shapes.** It exposes asteroid identity/bounds, ore names and the detailed-scan flag. The mod calculates quantities locally. This release reads those **completed client scan estimates**, not an exact server ore inventory. There are no new server requests or changes to the detector mod.

1. Install, open the Ore Helper menu and START SEARCH as usual.
2. Under **LEARNING → SDX2 SCANS**, **Prefer SDX2 estimates (saved scans)** is ON by default. Complete a detailed scan with SDX2's normal detector action. Ore Helper reads its completed result when the same asteroid is loaded, matched by ID and position. The amount source reads **SDX2 EST**, with **(SDX2)** on aimed-target amount labels. Without a valid result it uses the ordinary local estimate. The LEARNING footer reports adapter status.
3. Under **PINGS → NEARBY DEPOSITS**, **Show nearby ore deposits** is ON by default. Fly within **5 km of a loaded asteroid's center**. Ore Helper independently maps selected ores and draws a cross and dashed approximate extent around each region. This also works without SDX2 installed. Allow the bounded scan to finish; the PINGS footer reports progress.
4. The default deposit limit is **4**, inside your existing **total Max pings** and detailed-label limits. You can lower the deposit limit or range (maximum 5 km), hide outlines or size labels, and use all existing ping text size/color/opacity/offset/overlap controls. Ore ON/OFF and per-ore ping visibility apply. Total cap 0 hides everything.

Deposit guidance is a separate nearby view: it follows ore selection and its own range, without the long-range asteroid minimum-distance, grade, diameter, or learned-quality filters. Your saved asteroid filters remain unchanged. If ordinary asteroid pings vanish on approach, use **SEARCH → INCLUDE NEARBY (<N KM)** to clear a saved minimum distance. Existing Simple / Simple + target detail / Custom ping styles remain available.

### What these measurements mean

- SDX2 counts occupied cells at LOD 3 (roughly 8 m). Its saved results have no reliable scan time in the exposed contract, so they may describe ore already mined. They are not guaranteed recoverable yield. Toggle the SDX2 preference off to use the current local estimates.
- SDX2 baselines live separately under `Learning/SDX2`, per world/server. Local and SDX2 measurements never train each other's minima. The learning mode, Use latest bests and Reset controls cover both histories. Three distinct completed SDX2 asteroid records are needed before applying its learned minimum. The ORE DETAIL footer compares the two sources' bests and record counts.
- Nearby deposit outlines are computed by Ore Helper from loaded voxel data. They are **approximate containing regions**, not exact ore surfaces, filled spheres, or a promise of ore at the center. The displayed size is the enclosing region's diameter. Small deposits can be missed by 8 m sampling. No close-range fade is used.
- At most four nearby asteroids are mapped, nearest first. One 32×32×32 brick is read every six frames; connected-cell grouping is also spread across frames. Completed maps refresh after about 30 seconds at 60 FPS plus scan time; an old completed map stays visible during refresh. Mining changes can therefore take time to appear. Unloaded/replaced storage and world/sector changes invalidate maps. Very large/dense maps are declined when they exceed the memory/work limits; no partial map is presented as complete.
- SDX2's global, untagged `LatestDepositScan` is deliberately not imported: it can belong to a previous scan. This release contains original deposit mapping code and a read-only public-state adapter, with no third-party mod code or runtime DLL bundled.

Both components compile against the installed game; offline and synthetic-render checks pass. Live SDX2 assembly exposure, spatial alignment in a real world, and frame-time impact still need your in-game test.

## Start here

Press **PageUp** (or your saved Ore menu key). The helper still starts OFF at game launch.
On **SEARCH**, choose ores with the checked buttons, choose a mining preset, and press **START SEARCH**.
ALL/NONE changes the ore selection. SAVE SET / LOAD SET uses the selected slot (1–3).
Name a saved set under **ORE DETAIL → SAVED SELECTIONS**.

The main controls are sort, minimum of best seen, maximum pings, and scan distance.
RANGE: ALL LOADED switches off the distance limit for loaded asteroids; it cannot scan unloaded asteroids.
The four sorts are **Most estimated ore**, **Richest ore** (percentage), **Nearest matching**, and **Legacy quality**.
Existing grade, diameter, per-ore percentage, skip, and display filters still apply; find them under ADVANCED and ORE DETAIL.
Nearby deposits get the first slots inside the total ping cap (default 4 deposits); pins and aimed asteroid targets get priority among the remaining slots. Modern search honors the selected ores and per-ore ping visibility even for pins.

## Learning and ore amounts

The new scanner records content-weighted estimated ore volume in cubic metres, percentage, sample counts, LOD, position identity and scan time.
These estimates are not kilograms or a guaranteed recoverable yield. Coarse LOD sampling can miss small deposits or misestimate deposit boundaries.
**VERIFIED SCAN** means the stored result came from a finer scan (one LOD step finer), or a full-resolution LOD 0 scan. It is still an estimate.
Potential new records are queued for finer scans in bounded chunks, alternating with ordinary scans to keep new discovery moving.

The default minimum is **80% of the best stored verified amount for that ore**, or 80% of the best percentage in Richest ore mode.
Until an ore has three distinct verified asteroid records, its learned minimum stays inactive so an empty history does not hide everything.
Once seeded, the active minimum stays stable until the next search/load or **LEARNING → Use latest verified bests**.
**Frozen** retains the active benchmarks across restarts and stops recording new observations. **Verify best loaded asteroids** can still refine the live display.
Per-ore minimum overrides are under ORE DETAIL; 0 inherits the global minimum. Require ALL wanted ores is under ADVANCED → FILTERS / ORE MATCH.
Reset learning requires a second click within eight seconds and affects only the current world's learned records.

Learning is stored separately per world/server under `%APPDATA%/Pulsar/ZeosOreHelper/Learning`.
The bounded history retains top amounts, top percentages, and recent records for each ore.
Your existing settings and old asteroid cache remain intact. The old cache lacks solid-volume/sample information: it cannot establish honest amount benchmarks.
Matching old cache entries remain historical leads/pin metadata; fresh scans establish the new amount history. No ore amount is fabricated from asteroid diameter.

## Display and placement

**PINGS** has categories for clutter, text style, marker size/color and information fields.
Defaults: 8 total pings when upgrading the old default of 30, 3 detailed labels, 2 offscreen pings. Explicit nondefault existing caps are retained.
Set maximum pings to 0 to hide all markers, including pins and the aimed target.
Ping text size is independent of HUD text size. Adjust color, opacity, outline, background, offsets, spacing, overlap suppression, amount and scan status.
**DISPLAY** retains panel geometry, text size, rows, column toggles, frame and themes, and adds estimated-volume and scan-confidence columns.
**EDIT HUD POSITION** opens the real external panel outline. Drag it, or use PLACE PANEL. SAVE commits; CANCEL/ESC leaves settings unchanged.
UNDO restores the opening position; RESET POS moves only the draft to the default position until SAVE.
Every existing native control remains available in the six tabs; category menus replace long chains of unrelated pages. FULL / LEGACY SETTINGS remains available for old controls.

Native menus are part of game capture. The external HUD and legacy menu retain streamer exclusion and fail-closed handling; LCD output remains suppressed in streamer mode.

## Catalog release

The source is under `src/ZeosOreHelper`; the loader under `loader/ZeosOreHelper` forwards Pulsar lifecycle/configuration calls. `assets/orehelper/0.7.2` contains the paired runtime and overlay ZIP. Persistent data is outside Pulsar's cache. Test evidence is in ORE_HELPER_VALIDATION.md.
