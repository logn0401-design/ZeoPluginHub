# Ore Helper 1.0.4 menu cleanup

Search keeps the common search, sorting, ping limit, range and saved selection controls. Their duplicate category rows are removed. ANY / ALL ore matching is available directly on Search. One MOVE / RESIZE HUD button in the footer replaces the repeated placement controls.

Category names and labels are shorter. Toggle rows no longer repeat the current state a third time, and action buttons use short verbs. Advanced sizing multipliers have their own group. Tooltips explain learned minimums, the shared deposit ping limit, nearby asteroid hiding and custom ping fields. The older external settings window is available under ADVANCED / BACKUP MENU. MENU KEY retains native click-to-bind, CLEAR and APPLY.

Saved setting keys, choice values, defaults and functionality are preserved. Friendly dropdown names only change presentation. This release does not change scanning, ranking, learning, deposits, HUD rendering or overlay lifetime code.

Validation: Release/net48 production plugin and overlay build against installed game assemblies with zero warnings/errors. All 2,023 existing source-linked regression assertions pass, including setting persistence, key bindings, search, learning, deposits and HUD rendering. Catalog loader compilation, asset hashes, descriptor serialization and universal installer fixtures are checked for the packaged release. Native visual fit and mouse/keyboard interaction still require an in-game check.
