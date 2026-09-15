# Changelog

Cross-platform **FlexPad** (.NET 10 + Avalonia). Successor to the Python prototype of the same
name; this is the Windows/Linux/Raspberry-Pi rewrite in the station-tools family.

## [Unreleased]

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
