# Zeo Core 1.0.2 — HUD resizing and docked Quick Refill

HOME > LAYOUT > EDIT HUD LAYOUT. Drag inside to move; drag a side to change width,
top/bottom to change height, or a corner for both. Select covered panels in the list.
Each axis supports 50–300% of the existing panel's base dimensions, limited by the
viewport. Existing panel/text scales still apply. Content and row count determine
the base size as before; resizing does not freeze a telemetry panel's row count.

Text scales uniformly to fit the available width and height. Columns and bars fill
the width; row spacing uses additional height. Long labels and numeric values fit
their own columns without overlap. Very small/flat frames necessarily make text
smaller. Letters are never stretched sideways. No settings or telemetry fields
were removed. All six panels share normal and edit rendering.

SAVE LAYOUT commits the draft; CANCEL / Escape discards; UNDO ALL restores the
opening draft; RESET SIZE restores both dimensions of the selected panel.
Dimensions survive older settings writers in hud-settings.json.zeo-ui.json.
Only changed panel positions/sizes are saved, preserving unrelated concurrent edits.

Built on main Zeo Core 1.0.1 c5e6d19257fb8dedee0691a8f20b4c63fca593d8.
All-sector distress GPS, marker tracking, sharing/auth, visibility and color settings
are preserved. InventoryCandidate, Nav, Ore, PDC and the website are untouched.

QUICK REFILL: control the visiting ship from its cockpit, lock one connector to a
base/supply construct, then HOME > AMMO > QUICK REFILL / CANCEL. The same button
cancels. Status is shown below the settings; completion/stoppage also notifies in
game. Existing WANT values are whole-ship targets, including loaded magazines.
Zero targets are skipped; relevant-only is honored when enabled. Visibility
checkboxes do not change targets. Surplus is never offloaded. Ammo is taken from
accessible base cargo/assembler outputs into accessible ship cargo through valid
conveyors. Other connector-docked constructs and base weapons are excluded.
Ship rotors/pistons are included. A second connection is rejected as ambiguous.

Working ship gas tanks use native stockpile filling at normal game rates. Base
stockpile settings, reactors, fuel inventories, batteries and enabled switches
are not changed. Previously ON stockpile stays ON. Modes changed by this action
are restored after completion, cancel, undocking/control changes or 3 minutes.
A local append-only recovery journal retains restoration intent across a crash;
restoration resumes when the same world/player's tanks are available and accessible.
Recovery intent is flushed before the stockpile-on request. No websites are used.

One network inventory request is outstanding at a time. The controller waits for
observed stock to reach the requested result. A 12-second timeout stops requests
and keeps the unconfirmed-transfer guard, rather than blindly retrying. Firing
ammo during refill can prevent this confirmation; stop firing before refilling.
Partial stock, full cargo, conveyor restrictions and permission issues are reported.
Cancel stops new requests; a request already accepted by the server may complete.
There is no inventory spawning or local-only fake fill.

Implementation uses the installed game's networked MyInventory.TransferByUser
and IMyGasTank.Stockpile paths (local IL inspected for multiplayer RaiseEvent).
Official API reference: https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/Sandbox.Game.MyInventory.html

Validation: runtime and overlay Release/net48 builds, zero warnings/errors;
324 geometry/persistence/IPC/production-render checks at 1280x720, 1920x1080 and
3440x1440, five frame aspect/size pairs across all six panels; actual render images
reviewed. In-game mouse interaction, capture behavior and DPI changes still need
a game test. Offline rendering is not a claim of live game validation.
Quick Refill additionally passes 31 real-controller checks using simulated game
and network adapters. Dedicated-server transfers and SDX gas behavior need a
docked in-game test. The normal main plugin is the target, not InventoryCandidate.

Roll back using the saved 1.0.1 pinned descriptor/assets, preserving newer unrelated
catalog commits. Do not enable an old local Core alongside the main catalog Core.
