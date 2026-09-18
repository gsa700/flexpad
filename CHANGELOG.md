# Changelog

Cross-platform **FlexPad** (.NET 10 + Avalonia). Successor to the Python prototype of the same
name; this is the Windows/Linux/Raspberry-Pi rewrite in the station-tools family.

## [Unreleased]

## [0.12.1-beta] - 2026-09-19

### Changed
- **A pinned button makes its slice the active one.** After its commands have run, a button pinned
  to a slice letter sends `slice set <n> active=1` if that slice is not already active, so the
  front panel and the FlexControl knob follow the memory you just recalled. It happens last: a
  button whose own lines open the slice still works, and a run that stops on an error leaves the
  focus where it was. Unpinned buttons are unchanged. (David, 2026-09-19, after using 0.12.0.)

## [0.12.0-beta] - 2026-09-19

### Added
- **Buttons are slice aware.** Each button has a *Runs on* setting: the active slice (as before) or
  a fixed letter. A pinned button's `{slice}` and `{pan}` always mean that slice, so a memory made
  on slice A still lands on A after a second slice has been opened and become active. New buttons
  from the guided start and the band-set generator are pinned to the slice that was active when
  they were made (ones that already name their slices by letter have nothing to pin). A small
  letter in the button's corner shows the pin. Change it with right-click → Runs on, in the editor,
  or on page two of the guided start. If the pinned slice is not open the button stops with a
  message saying so and sends nothing. (David, 2026-09-19: with slice B active, an older memory
  "ran against the active slice... even the single memories need to be slice aware".)
- **+ Button → Pin unpinned buttons to slice A** and **Unpin all buttons**, for a grid made before
  pins existed. Only buttons that say `{slice}` or `{pan}` are touched.

### Compatibility
- Existing buttons are unchanged until pinned: no `"slice"` key in the config means "follow the
  active slice". The knob and its functions still act on the active slice.

## [0.11.0-beta] - 2026-09-18

### Added
- **Guided start for a new button.** + Button → New button… now opens a two-page window instead
  of the editor. Page one asks what the button should do: go to a frequency (preselected, and
  filled in from the active slice, so Next then Save makes a working button), remember what the
  radio is doing now (active slice or all slices, basic or full), change band (or jump to the
  band-set generator for a whole row), a common action (switch antenna, move TX to a slice, make a
  slice active, close a slice, set TX power), or write the commands yourself. Page two names it,
  colours it, and shows the commands it will send; Edit commands… carries it into the full editor.
  (David, 2026-09-18: the dialog was "getting really busy and confusing".)

### Changed
- **The editor lost its Build from choices panel**, which moved into the guided start (David:
  "remove it from the editor"). Right-click → Edit still opens the editor directly.
- Debug switches: `--new` opens the guided start, `--edit` now opens the editor on the first button.

## [0.10.0-beta] - 2026-09-18

### Added
- **Capture all slices.** The editor's Capture menu (was "Capture slice") gains *All slices*, basic
  and full: every open slice's state by letter, which slice transmits and which is active, and
  with Full the TX power. Self-contained on purpose: the button is the whole snapshot, nothing is
  saved in the radio. (David, 2026-09-18, who chose this over a global-profile button.)
- **`slices A B` line** in the button language: FlexPad closes every open slice that is not listed,
  opens slices until every listed letter exists (a new one starts as a copy of the active slice's
  frequency, antenna and mode), and waits for the radio to report each change before the lines
  that address slices by letter run. A wanted letter above a gap is reached through a filler slice
  that is closed again.

## [0.9.2-beta] - 2026-09-16

### Fixed
- The console no longer shows `Invalid subscription object name` on every connect: `sub interlock
  all` is not a subscription the radio knows, and interlock status arrives with `sub tx all` anyway.

### Docs
- README screenshots (Windows, 150 % scaling): main window with a band row, a drag in progress, the
  editor after Build, the band-set generator, the console, Setup's Knob tab.

## [0.9.1-beta] - 2026-09-16

### Changed
- **You can see a drag now.** A ghost of the button rides under the pointer, the original dims to
  a hole, the button the drop would take the place of gets an outline, and empty row space that
  would take it gets a tint. (David: "a little hard to visualize what is happening", 2026-09-16.)

## [0.9.0-beta] - 2026-09-16

### Added
- **Drag buttons to rearrange.** Press a button, move it, and drop it on another button to take
  that slot; the others shift over. Drop it on empty space in the grid or the band row to send it
  to the end of that row, which is also how a button changes rows by hand. A plain click still
  fires the button and right-click still opens the menu; nothing happens until the pointer has
  moved a few pixels, and the release that ends a drag never fires anything. "Move earlier" and
  "Move later" stay in the menu. (David, 2026-09-16.)

### Fixed
- Hotkeys on buttons in the band row did nothing: the key bindings were built from the main grid
  only, so the band-set generator's F-keys were dead since 0.6.1.

## [0.8.0-beta] - 2026-09-16

### Added
- **Band buttons that only change band.** The band-set generator's amateur set now defaults to
  "change band only": each button is one line, `display pan set {pan} band=20`, and the radio
  brings back the frequency, mode, filter and both antenna ports it last had on that band, exactly
  like the front-panel band buttons (band persistence; verified live on the 8600M: 80 m → 20 m →
  2 m → 80 m came back to 3.925 LSB ANT1/ANT1 to the hertz). Transverter bands use the radio's own
  index for the band (`band=x0`), read from `sub xvtr all` and matched by name; a band the radio has
  no transverter entry for is skipped with a console note. Untick the option for the old full
  recipe. (David: "each band button is essentially the same as a regular button", 2026-09-16.)
- **Broadcast set** in the generator: AM broadcast, the shortwave broadcast bands from 120 m to
  11 m, 11 m CB (channel 19) and WWV, each a full recipe in AM on the HF antenna. General coverage
  has no band of its own on the radio (`band=gen` is refused), so these are always recipes. Brown
  buttons, in the band row by default. (David's idea, in place of a GEN button.)
- `{pan}` placeholder: the active slice's panadapter handle.

### Changed
- The radio client follows the transverter list (`sub xvtr all`).

## [0.7.1-beta] - 2026-09-16

### Fixed
- **Crash when the radio refused the connection.** Power the radio off (or catch it booting) while
  FlexPad is running and the connect attempt gets "connection refused"; `Task.Wait` wrapped that in
  an `AggregateException` the reconnect loop did not catch, so the client thread died with an
  unhandled exception and the app went with it (crash.log on David's PC, 2026-09-14 21:53, present
  since 0.4.0). The client thread now survives any session error, logs `connection lost: …` with the
  real message, and keeps retrying every five seconds. Regression test in `RadioClientTests`.

## [0.7.0-beta] - 2026-09-14

### Changed
- **FlexControl buttons bind to functions, not to flexpad buttons.** The picker in Setup → Knob now
  offers twenty radio functions and nothing else: cycle tuning step, next slice, mute, TX on this
  slice, TUNE on/off, MOX on/off, ATU tune, ATU bypass, AMP operate/standby (Power Genius), cycle
  RX antenna, cycle TX antenna, cycle mode, cycle AGC, NR / NB / WNB / ANF on/off, lock/unlock
  tuning, RIT and XIT on/off. Each reads the radio's current state and sends the opposite (or the
  next), and the console says what it did. (David: "you would not use a button on the FC to select
  a freq preset", 2026-09-14.) A binding to a flexpad button from an older config still works.
- The radio client now follows amplifier, interlock and ATU status as well as slices and transmit.
- `KnobActions` in Core is the pure planner (state in, commands out) and is tested; every command
  it can send was accepted by a FLEX-8600M when sent with the radio's current value.

## [0.6.2-beta] - 2026-09-14

### Fixed
- **Setup's position survives an update restart.** An update (and a Remove) closes every window in a
  loop, but closing the main window already cascades to Setup and the console, so those were closed
  twice; the second time their position tracker was gone and the fallback read of a closed window
  gave (0,0), which overwrote the good value saved a moment earlier. Only the update path did this,
  which is why a plain close and reopen looked fine. Each window's close handler now runs once.
  Reproduced and verified with the new `--exit-for-update` debug switch.

## [0.6.1-beta] - 2026-09-14

### Added
- **Band row.** Buttons can live in their own row along the bottom of the window, separate from the
  main grid: one row for up to twelve, shorter and smaller than the grid's buttons. The band set
  generator puts its buttons there by default (an option in the dialog turns it off), and the editor
  has a "Band row (bottom)" checkbox to move any button in or out. Stored as `"group": "band"` on
  the button. (David: band buttons "need to be in their own row, at the bottom", 2026-09-14.)

## [0.6.0-beta] - 2026-09-14

### Added
- **Band set generator** under + Button → Band set…: tick the bands (160 m through 6 m, plus 2 m and
  70 cm on the transverter ports), choose the antenna for HF, for 2 m and for 70 cm, and it makes one
  button per band at the band's usual spot — LSB below 10 MHz, USB above, CW on 30 m — with the
  matching filter. Options: CW spots and mode instead of phone, F-keys in band order, and whether a
  button with the same label is replaced or a twin added. Every button it makes is an ordinary one.
  (David asked how to make a series of band buttons, 2026-09-14.)
- `--bands` debug switch opens the generator at launch.

## [0.5.2-beta] - 2026-09-14

### Fixed
- **Window positions on Linux.** Two effects, measured on Fedora (GNOME over XWayland) with an
  isolated copy driven by wmctrl/xdotool: the Setup window sometimes saved (0,0), and every window
  restored one title bar (37 px) lower than it was saved, drifting down on each restart. Each
  window now remembers its position from its own move events while visible (`WindowMemory`), never
  from a read at closing time, and on non-Windows platforms the frame extents are subtracted on save
  so a restore lands the frame where the window manager last put it. Two close/relaunch cycles on
  Fedora now reproduce positions exactly; Windows unchanged.
- `FLEXPAD_CONFIG_DIR` overrides the settings folder, so test and screenshot runs never touch the
  operator's real config. (A debug run from the developer's shell had overwritten saved positions.)

## [0.5.1-beta] - 2026-09-14

### Changed
- **Labels on the status dots.** The radio's own nickname (its `name` from the `info` reply, model
  as the fallback) sits beside the radio dot, and "FC" beside the FlexControl dot. (David, 2026-09-14.)

## [0.5.0-beta] - 2026-09-14

### Added
- **Build from choices** in the button editor. A panel with frequency, mode, RX antenna, TX antenna,
  filter edges and an optional step; Build appends the command lines to the list, where they stay
  editable, and names a new button from the frequency and mode. The mode and antenna lists are the
  radio's own (`mode_list`, `ant_list`, `tx_ant_list` from slice status), so each radio offers exactly
  the ports it has, and the fields are prefilled from the active slice. Picking a mode fills in its
  usual filter. Someone who never wants to see a command can now make a button with + Button, a few
  picks, Build, Save. (David's idea, 2026-09-14; chosen over a wizard because buttons aren't all one
  shape and the text list stays the source of truth.)
- `--edit` debug switch opens the new-button editor at launch.

### Fixed
- **The button editor and the Reference window remember their position and size**, like the other
  windows. (David, 2026-09-14.)

### Changed
- The shipped "2nd RX 70cm" button, the example snippet and the reference now set both antenna
  ports after `slice create`, because `ant=` only sets the RX port and the TX port came from the
  band's last use.

## [0.4.3-beta] - 2026-09-14

### Changed
- **One-line header.** The "FlexPad" heading is gone (it's in the title bar). In its place two dots:
  radio connection and FlexControl knob, green when up, amber when the knob is held by another
  program or was unplugged, grey when off or not found; hover for the address and port. The radio's
  IP and the knob's port are no longer in the status text, which is now just the active slice and
  sits on the same line as the dots and the toolbar. (David, 2026-09-14.)

## [0.4.2-beta] - 2026-09-14

Fix only, reported after the first in-app update on both Windows and Fedora.

### Fixed
- **Window positions survive a restart.** Every relaunch, after an update or a plain close, came back
  at the top left. The app saves once more at exit, after every window has closed, and a closed
  window reports its position as (0,0); the main window's reference was still held, so that final
  save overwrote the real position recorded a moment earlier. The reference is now dropped as soon
  as the window's bounds are recorded (as W2 does), and the exit-time save only reads windows that
  are still open. Same fix for the console and Setup windows' positions and the console's open state.

## [0.4.1-beta] - 2026-09-14

Fix only, found within an hour of 0.4.0-beta by unplugging the FlexControl to move it to another box.

### Fixed
- **Unplugging the FlexControl no longer crashes the app.** On Windows a USB serial port pulled out
  from under a blocked read raises `OperationCanceledException`, which the knob reader did not
  expect; it escaped a background thread and took the process down. Three identical entries in
  `crash.log`, one per unplug. The reader now treats any failure of the port as "knob lost",
  reports it on the status line and goes back to looking for the knob, so a replug is picked up
  within a few seconds. Port enumeration and opening are guarded the same way.

## [0.4.0-beta] - 2026-09-14

Released with win-x64, linux-x64 and linux-arm64 zips; Windows, Fedora (linux-x64) and a Raspberry Pi CM5 (linux-arm64) all verified against the radio, the FlexControl on Windows and on Fedora.

The port. Everything the Python flexpad did (v0.1.0–v0.3.0, same day), rebuilt on the family
template so it installs, updates, looks and is laid out like LP-100A Monitor, W2 Monitor and Shack
Power: a single self-contained executable per platform, self-install on first run, in-app updates
from release zips, a tabbed Setup with an Updates tab, crash log, the shared dark palette.

### Added
- **Button grid** of SmartSDR API command sequences with `{slice}`, `{tx}`, `{A}`..`{H}`
  placeholders, `wait`, comments, hotkeys, colours; edited from a right-click menu and an editor with
  colour swatches, example snippets, **Capture slice** (basic or full) and a Reference window.
- **FlexControl knob**: found by USB id, tunes the active slice by its current step, twelve button
  events bound to `@step`, `@next-slice`, `@mute`, `@tx` or any flexpad button (Setup → Knob).
- **Console** as its own window: the traffic log with colour by direction, a command line with
  history, and a status-traffic toggle. Remembered open/closed across runs.
- **Setup**: Radio (address, Discover, port, grid columns, stop-on-error, reload from disk), Knob,
  Updates (check/install, startup check, crash notice, Remove).
- Reads the Python prototype's `config.json` unchanged, so existing buttons and knob bindings
  carry over.

### Changed
- Installation is the family's: the exe installs itself per-user on first run, with Start Menu and
  desktop shortcuts and no installed-apps entry; removal is Setup → Updates → Remove. The
  PowerShell installer, `--send`/`--run`/`--knob` headless flags and the Python updater are gone.
