# Zeo Core 1.0.7 — docked refill

Refresh your existing Pulsar Zeo hub and fully restart Space Engineers. Use the regular **Zeo Core** entry; this release does not modify Nav, Ore Helper, PDC Manager, or the separate inventory candidate.

Open **HOME → REFILL**. Set ammo WANT amounts and the native SDX2 fusion pellet reserve. Click the refill key control, press a key combination, then **APPLY**. **CLEAR → APPLY** removes it. The button and binding both start service; pressing again cancels it.

Control the visiting ship from its cockpit with one powered, accessible connector locked to the supply construct. Service uses accessible mechanical subgrids on each side, and follows the game's conveyor and inventory rules.

* Ammo eligibility comes from installed WeaponCore weapon parts and their magazine mapping, not whether cargo contains a sample round. Only compatible ammo with a positive WANT loads. WANT counts magazines across the ship, including loaded weapon inventories; it is not a per-weapon target.
* Compatible weapons receive ammo first, including from existing onboard cargo. Reserve ammo prefers the native `sdx_cargocontainerReinforced1x1` container. Ordinary cargo is used when reinforced storage is absent, full, or inaccessible. Existing ordinary-cargo ammo is also moved into available reinforced cargo after weapon loading.
* Fuel is SDX2 **Ingot/sdx_itemReactorFuel**. Compatible reactors receive fuel before cargo reserves. Station reactors and weapon magazines are not stripped for supplies.
* Oxygen and other working gas tanks temporarily use stockpile to refill. Their previous modes are restored after completion, cancellation, undocking, or shutdown. A local recovery journal retains restoration intent after a crash.
* **Unload non-target cargo** defaults OFF. When enabled, cargo containers unload into accessible base cargo before loading. Put `[ZEO KEEP]` in a ship container's name to protect it from unloading. Weapons, reactors, cockpits and personal inventories are never unloaded. Native reactor fuel and configured compatible ammo are retained. Unknown ammo is retained conservatively, including when no compatible weapon could be identified. Excess quantities of a retained type stay aboard.

Requests are sequential and wait for inventory synchronization. A delayed or unconfirmed transfer stops rather than repeatedly requesting more. No stock, full inventory, conveyor restrictions, denied access, or unavailable weapon mappings produce a partial/no-target result. Gas production and normal weapon conveyor settings still matter.

Validated: production runtime/overlay builds, real Pulsar loader compiler, 20 controller scenarios and 322 key/settings assertions. Multiplayer latency, live WeaponCore mapping, actual weapon reloads, and tank restoration still require an in-game check.

Spectrum / Auto placement from Core 1.0.6 is retained: native observation timing and current-camera projection, authoritative snapshot replacement, and stable confirmed entity identities.
