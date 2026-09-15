# Backlog

## Open

- **Knob on Linux.** Both Linux builds are verified (see Done) but no FlexControl has been plugged
  into a Linux box yet: `KnobPort.FindLinux` matches the by-id name on the vendor string, and the
  user must be in `dialout` (SerialErrors says so).
- **Global hotkeys.** Hotkeys work while the FlexPad window has focus. A station operator usually
  has SmartSDR or a logger in front; system-wide hotkeys would need platform hooks.
- **Headless flags.** The Python prototype had `--send`, `--run <label>` and `--knob` for scripts
  and a Stream Deck. Not ported; the console covers the interactive case. Add back if a script
  needs them.

## Planned

## Done

- 2026-09-14: port from Python to the .NET family template (0.4.0-beta).
- 2026-09-14: linux-x64 verified on the Fedora box (TestbedLinux), running against the radio at the
  same time as the Windows copy. David: "working FB".
- 2026-09-14: linux-arm64 verified on the CM5 kiosk (10.0.1.25, Debian 13, labwc) over SSH: the loose
  copy launched under `systemd-run --user`, discovered the radio and connected, drew the default
  twelve buttons and the install offer; `--install --quiet` wrote the program, `.desktop`, icon and
  `~/.local/bin/flexpad`; the installed copy launched and connected; `--uninstall --quiet` removed
  all four and left the box clean. No crash log. The knob could not be tested there.
