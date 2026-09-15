# Backlog

## Open

- **Pi arm64 pass.** linux-x64 is verified (Fedora, see Done); the linux-arm64 build has not run on
  the CM5 yet. The knob on Linux is also unverified: `KnobPort.FindLinux` matches the by-id name on
  the vendor string, and the user must be in `dialout` (SerialErrors says so).
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
