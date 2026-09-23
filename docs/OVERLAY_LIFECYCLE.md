# Zeo overlay lifecycle release

Core 1.0.4, Nav 1.0.1 and Ore Helper 1.0.1 share src/Shared/OverlayLifetime.cs.
Both plugin and overlay must update together through the catalog's immutable assets.

The launcher retains the exact child process and a per-launch named stop event.
The overlay holds its parent's process handle and checks PID plus creation time;
its watcher runs independently of the UI thread. Plugin unload signals the event,
then has a bounded fallback that terminates only its own launched child.
Parent exit (including a crash) requests normal UI cleanup; a blocked UI or cancelled
close gets a self-exit fallback after two seconds. No process-name mass termination.

Pulsar Legacy can remain running after the game window closes. Once a game window
has been observed, destruction with no replacement for ten seconds also closes the
overlay. Existence and owner PID are checked; focus, minimization, telemetry and
world load are not shutdown signals. A recreated main window resets the grace.
Missing/malformed ownership exits before settings, hotkeys or UDP initialization.

Core/Ore Running now means the owned child, not any process sharing its filename.
Old overlays from BEFORE this update lack the new watcher: stop them once after
closing the game/Legacy/Interim, then refresh the catalog and restart. Standalone
manual overlay launches without ownership are intentionally rejected.

Validation: eight production components built. Catalog Core/Nav/Ore builds have
zero warnings/errors. PDC preserves its prior unused coolingStartFrame warning.
All three catalog loaders pass the installed Pulsar compiler. 39 real-process
checks cover clean/crashed owners, hung overlay close callback, plugin unload,
independent children, idle survival, PID creation-time mismatch, hidden window
survival and destroyed window while owner lives. Four production EXEs reject
missing ownership before UI/IPC. Full Space Engineers close/relaunch still needs
a user test; no tactical, flight, inventory or PDC tuning logic changed.

PDC is separately distributed as the guarded 0.3.20.1 local hotfix because the
installed plugin is 0.3.20, while 0.3.21 is pending in its other task. The installer
verifies exact old/new hashes and backs up installed replacements, preserving
settings/profiles. Do not overwrite a newer PDC build with this patch.
