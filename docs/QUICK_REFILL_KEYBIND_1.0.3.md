# Zeo Core 1.0.3 — Quick Refill keybind

Refresh the Zeo source in Pulsar, then fully restart Space Engineers and ZeoOverlay.
Keep the regular Zeo Core entry enabled.

Open HOME > AMMO. Choose Quick Refill key and Quick Refill modifier (NONE,
CTRL, ALT, SHIFT, or CTRL + SHIFT). The default is UNBOUND with CTRL selected.
Choose a combination not used by your other game/plugin controls. Press the
combination once to start Quick Refill, and again to cancel it. UNBOUND disables
only the shortcut; the existing Quick Refill / Cancel button remains available.

Refill still requires an eligible docked conveyor connection, ship cargo space,
and accessible base supplies. It fills only ammo WANT deficits. Working ship tanks
use Stockpile while filling and restore their earlier modes afterward. The 1.0.2
transfer guards, restoration journal, scope and timeout remain unchanged.

The shortcut is ignored in menus, chat and when the game lacks focus. Holding it
does not repeat the action. Releasing it is required after returning to gameplay.
Menu and enabled distress physical-key collisions are rejected, even when using
a modifier, because those existing bindings do not filter modifier keys.

Validation: client and overlay Release/net48 builds; 292 offline input/settings
checks and the existing renderer/resize suite. Pulsar compiler result accompanies
the release. Actual keyboard input and multiplayer refill still need an in-game
check; these offline checks do not establish live server transfer behavior.
