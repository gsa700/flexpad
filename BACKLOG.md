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
  drop hit test until the hit test skipped the overlay. David: "that's perfect... totally clear what
  is happening".
- 2026-09-16: 0.9.2-beta. README screenshots (David's suggestion) under docs/screenshots, taken
  from the debug build with a sample config through `shots_readme.ps1` in the session scratchpad
  (PostMessage clicks for Build and the drag frame; chassis serial painted over in the console shot
  before publishing). Taking them showed `sub interlock all` is rejected by the radio on every
  connect, so it's gone; interlock status rides on `sub tx all`.
- 2026-09-18: 0.10.0-beta. "Create a button from the radio's current state, all slices": David chose
  the self-contained version over a global-profile button ("let's not mess with the global profiles
  right now"). Capture → All slices (basic/full) plus a `slices A B` line that opens and closes
  slices to match. Verified live through the app with one slice open: `slice create` answers with
  the new index, the letter status follows at once, `{B}` resolved, `slice remove` closed it; radio
  state identical before and after. Global-profile snapshot button remains an unbuilt idea.
  David after updating: "updates went well". He then asked whether the new-button dialog, now
  "really busy and confusing", should become a wizard with the basic function as the default.
- 2026-09-18: 0.11.0-beta. Guided start for new buttons (NewButtonWindow/NewButtonViewModel, which
  holds a ButtonEditorViewModel for the shared details; ButtonActions in Core for the common
  actions), Build panel removed from the editor at David's word. Verified on Windows by posting
  Enter twice to the window: a "3.925 LSB" button with four lines landed in the scratch config.
  David on 0.11.0: "tried it on a simple button capture and it worked great". He also found that with a
  second slice open and active, an older memory button ran against slice B, not the slice it was made
  for: "even the single memories need to be slice aware".
- 2026-09-19: 0.12.0-beta. Per-button "Runs on" (active slice or a letter), badge on the button,
  right-click submenu, picker in the editor / guided start / band-set generator, and bulk pin and
  unpin under + Button. Shipped without asking first, like the other bug-type feedback. Verified on
  Windows: the submenu was driven by keyboard and "Slice A" landed in the scratch config.

- 2026-09-19: 0.12.0-beta verified on Linux at David's request. Fedora (linux-x64, GNOME Wayland via
  XWayland, 100 % scaling): all five windows open, render and fit, connected to the radio; by
  keyboard, Enter-Enter in the guided start made a button pinned to A, + Button's Pin and Unpin
  worked, the context menu's Runs on submenu pinned a band button (Menu key: Shift+F10 is not the
  shortcut on Linux), a pinned no-op ran on slice A, and a button pinned to an unopened slice
  refused and sent nothing. Pi CM5 (linux-arm64, labwc): main, New button and Band set launch and
  render. Not verified on Linux: drag (no pointer injection under Wayland; David confirmed it by
  hand at 0.9.1) and the `slices` line (Core logic, verified live from Windows).
  **Incident:** the first context-menu attempt used Shift+F10, the menu never opened, and the final
  Enter fired the focused "160m" button, which held a real band command: David's radio sat on 160 m
  for a couple of minutes until the state check caught it; restored with `band=80` to 3.925 LSB
  exactly. Test configs now carry comment-only commands and the radio is read before and after.
  Working input route on that box: `ydotool key` (uinput) behind a guard that the focused X window
  belongs to the test pid. `xdotool key` (XTEST) and `xdotool key --window` (XSendEvent) are both
  ignored. Tab order on the main window: the four toolbar buttons, then the band row, then the grid.
  David, same day, after trying 0.12.0 on Fedora by hand: "drag is FB on linux". That closes the one gap
  the remote check could not cover.
  David on his real grid: "pinned all buttons to slice A, works FB". 0.12.0-beta confirmed.
- 2026-09-19: 0.12.1-beta. David: "make the pinned button also make its slice active". Done in
  CommandSequence.Run after the lines have run; four new tests in TargetSliceTests.
  David after updating both: "pinned buttons make the slice active FB". 0.12.1-beta confirmed.
- 2026-09-19: 0.12.2-beta. David's "VHF & UHF" all-slices button failed from single-slice operation:
  "slice B doesn't get set up right". Read his real config through a scheduled-task copy, replayed
  the button live with every reply and status logged (guarded, restored): slice B was created as a
  copy of A in A's panadapter and the radio closed it 80-90 ms later; a separate oddity (tune to
  144.2 landing on 97.86) was his slice A being locked. Experiment: B created on 432 MHz with XVTB
  gets pan 0x40000001 and survives. Fix: `slices` reads ahead and opens the slice where it will live.
  David on 0.12.2: "it worked: the second receiver was activated, both were set to the correct ANT
  selections". Two follow-ups. (1) The new slice's scope is very wide -> 0.13.0 captures scope width and
  centre. (2) "clicking a single slice preset changes slice B even though A has the TX": his Windows
  config turned out to be unpinned (only 2m USB=A, 70cm USB=B); he had run the bulk pin on Fedora. Each
  machine has its own settings file. No code change; told him to pin on Windows too.
- 2026-09-19: 0.13.0-beta. Scope capture, {panA}..{panH}, client follows `sub pan all`. Built and
  released without touching the radio (he was on a net): `display pan set bandwidth=/center=` are from
  the published command list, unverified on his radio at release time.
- 2026-09-19: 0.14.0-beta. I had proposed presets that close slice B (capture default "every open
  slice", a "close other slices" checkbox, a bulk entry). David: "what if we split the difference
  and default all presets to slice A unless indicated otherwise? That is 95% of it and much easier."
  Done: absent `slice` key = A, `"active"` = follow the active slice, badge marks exceptions only,
  focus follows only buttons that address their own slice. Closing slice B from a single-slice
  preset stays available by hand (`slices A` as the first line) but is not built into anything.

