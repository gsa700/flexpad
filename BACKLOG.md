# Backlog

## Open

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
- 2026-09-14: FlexControl plugged into the Fedora box; FlexPad found it by its by-id name and tuned
  the radio. David: "picked it right up and works FB". Every feature is now verified on Linux.
- 2026-09-14: 0.4.1-beta. Unplugging the knob on Windows crashed 0.4.0-beta (OperationCanceledException
  from the cancelled overlapped read, unexpected by the reader); Linux was unaffected because a pulled
  device fails the read with an IOException there. Fixed, and the release doubled as the first real
  run of the in-app updater: David updated from Setup, unplugged and replugged the knob, no crash,
  reacquired within seconds.
- 2026-09-14: 0.4.2-beta (window positions were saved as (0,0) by the exit-time save after the windows
  closed) and 0.4.3-beta (one-line header with radio and knob dots), both delivered through Setup →
  Updates to Windows and Fedora and confirmed by David: "dots and positions look right".
- 2026-09-14: 0.5.0–0.5.2-beta. Build-from-choices panel and dot labels; then the real position
  story: Setup "not remembering" on Windows was my own test runs writing into the live config
  (fixed with FLEXPAD_CONFIG_DIR), and on Fedora GNOME the Setup window saved (0,0) and every window
  drifted one title bar per restart (fixed by WindowMemory + frame-extent compensation). David
  after updating both: "positions stick now on windows and fedora".
- 2026-09-14: 0.6.0–0.6.2-beta. Band set generator, band row along the bottom, and the last
  position bug: Setup zeroed only on update restarts because CloseAllWindows double-closed it.
  Reproduced with `--exit-for-update`, guarded, confirmed by David on both boxes: "setup position
  sticks now on windows and fedora".
- 2026-09-16: 0.7.0–0.7.1-beta. FlexControl buttons bind to twenty radio functions (no frequency
  presets in the picker). Then the first crash David saw with 0.7.0, on Windows and Fedora alike:
  the radio was off, the connect got "connection refused", and Task.Wait wrapped it in an
  AggregateException the reconnect loop's filter missed, so the client thread died and the process
  with it (present since 0.4.0, only ever run with the radio on before). Fixed in 0.7.1: the client
  thread survives any session error and keeps retrying; RadioClientTests covers it. David after
  updating both: "no crash with the radio off now".
- 2026-09-16: 0.8.0-beta. Band buttons that only change band (`display pan set {pan} band=`, the
  radio's persistence does the rest; transverters by index from `sub xvtr all`) and a broadcast set
  (AM BC, shortwave, CB, WWV in AM) in place of the GEN button David first asked for, his own idea
  once it turned out the radio has no GEN band in the API. David after updating both: "band buttons
  work FB".
- 2026-09-16: 0.9.0-beta. Drag buttons to rearrange, asked for the same afternoon after "don't do
  it now" ("the pi kiosk isn't touch so build it the same as the rest"). Pointer events and a hit
  test in the main window, ListMoves in Core. Verified on Windows by posting mouse messages to the
  window (three drops and a plain click, config order checked). Not verified on Linux: under GNOME
  Wayland neither xdotool nor ydotool can move the pointer from an SSH session, so that is David's
  to try. Also found and fixed on the way: band-row hotkeys were never bound (since 0.6.1).
- 2026-09-16: 0.9.1-beta. David confirmed drag on Windows and Fedora ("drag works FB") but "it's a
  little hard to visualize what is happening": now a small ghost tile rides under the pointer, the
  origin dims, the target button is outlined and empty row space is tinted. Two harness catches
  before it shipped: the generated `DragLayer` field is null because MainWindow has its own
  InitializeComponent (resolve with FindControl), and the ghost under the pointer swallowed the
  drop hit test until the hit test skipped the overlay.
