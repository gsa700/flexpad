# Changelog

Cross-platform **FlexPad** (.NET 10 + Avalonia). Successor to the Python prototype of the same
name; this is the Windows/Linux/Raspberry-Pi rewrite in the station-tools family.

## [Unreleased]

## [0.4.0-beta] - 2026-09-14

Released with win-x64, linux-x64 and linux-arm64 zips; Windows verified on the author's station, Linux builds untested on hardware.

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
