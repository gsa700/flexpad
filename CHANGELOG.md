# Changelog

Cross-platform **FlexPad** (.NET 10 + Avalonia). Successor to the Python prototype of the same
name; this is the Windows/Linux/Raspberry-Pi rewrite in the station-tools family.

## [Unreleased]

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
