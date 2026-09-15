# Backlog

## Open

- **Linux/Pi pass.** Built for linux-x64 and linux-arm64 from day one, never run there yet. Serial
  on Linux needs the user in `dialout` (SerialErrors says so); `KnobPort.FindLinux` matches the
  by-id name on the vendor string and is untested against a real FlexControl on Linux.
- **Global hotkeys.** Hotkeys work while the FlexPad window has focus. A station operator usually
  has SmartSDR or a logger in front; system-wide hotkeys would need platform hooks.
- **Headless flags.** The Python prototype had `--send`, `--run <label>` and `--knob` for scripts
  and a Stream Deck. Not ported; the console covers the interactive case. Add back if a script
  needs them.

## Planned

## Done

- 2026-09-14: port from Python to the .NET family template (0.4.0-beta).
