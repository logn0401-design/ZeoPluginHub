# Ore Helper 1.0.3 native key binding

Baseline: shared catalog main at eb5c218, Ore Helper 1.0.2. This release builds on the other task's HUD consistency and overlay-lifetime work.

MENU KEY in the native footer opens ADVANCED / HOME / KEYBINDS. Click the key, press a new key and click APPLY. CLEAR selects None as a draft; APPLY disables the shortcut. ESC/close/navigation discards the key draft. Pulsar Configure and /ore menu remain available. PageUp stays the default; saved keys are preserved. Modifier-only keys and ESC are excluded. No modifier chord behavior is added to the menu shortcut.

The old nine-key normalization/switch is replaced with validated Space Engineers key names. Native capture and legacy fallback retain custom choices. The shortcut is suspended while capturing or awaiting APPLY, so pressing the current binding does not close Ore Helper. Key saves merge into freshly loaded settings; layout, ore selection and unknown/concurrent keys survive.

Validation: both production components build Release/net48 x64 against installed game assemblies with zero warnings/errors. Main source-linked harness: 2,023 assertions, including 33 new binding checks for drafts/cancellation, disabled bindings, normalization, persistence, custom key resolution and unrelated-setting preservation. Installed Pulsar compiler/asset/descriptor checks are run for the paired release. Native controls compile against the real game API; actual keyboard focus/capture and visual fit still require in-game verification.

Only Ore Helper source/assets/descriptor and its documentation/tests change. Core, Nav, deposit mapping, scan adapters, ranking, learning, layout resizing and overlay lifecycle implementation are retained. Existing source/assets stay available for rollback. No live configuration or installed game files are changed during release preparation.
