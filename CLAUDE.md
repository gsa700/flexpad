# FlexPad (flexpad)

Programmable command buttons and FlexControl knob support for a **FlexRadio FLEX-6000/8000**.
Each button is a list of SmartSDR TCP/IP API commands fired in order over a direct session to the
radio; a FlexControl USB knob tunes the active slice and its buttons map to actions or to flexpad
buttons. Exists because SmartSDR memories cannot store RX/TX antenna ports, which matters for
transverters on XVTA/XVTB, and because the FlexControl only works while SmartSDR is running.
**.NET 10 + Avalonia 12.1**, MVVM. Windows / Linux / Raspberry Pi (arm64). GPLv3. By David
Erickson (AB0R). Status: **0.4.1-beta**.

Fourth app in the station-tools family. **LP-100A Monitor** (`~/Documents/Programming/lp100a-monitor`)
is the family's reference template and **W2 Monitor** (`~/Documents/Programming/w2-monitor-x`) its
most refined descendant; this port copied W2's install/update/config/crash-log plumbing verbatim
(namespaces and product strings renamed) and wrote only the flexpad-specific parts fresh. Their
CLAUDE.md files carry the rationale for the plumbing — read them before "fixing" anything that looks
odd there, in particular the self-install section and the registry-virtualisation history.

This .NET app replaced the Python prototype at the 2026-09-14 port. The prototype (tags v0.1.0 to
v0.3.0, plain-file install to `%LOCALAPPDATA%\Programs\flexpad` with a PowerShell installer) is in git
history only; its `config.json` schema is kept verbatim so an existing settings file carries over.

## Build / run / test

```sh
dotnet build                                   # needs the .NET 10 SDK (pinned in global.json)
dotnet run --project src/FlexPad.App           # run the app (needs a desktop/DISPLAY)
dotnet run --project src/FlexPad.App -- --setup    # open Setup on launch (debug)
dotnet test                                    # xUnit suite — all pure FlexPad.Core logic
```

Runtime switches: `--setup`, and the install pair `--install` / `--uninstall` (both take `--quiet`).

Solution: `FlexPad.slnx`. Output assembly is `FlexPad` (`FlexPad.exe` on Windows).

Publish a self-contained build (per platform):

```sh
dotnet publish src/FlexPad.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
# swap -r for linux-x64 or linux-arm64 (Raspberry Pi)
```

## Layout

```
src/
  FlexPad.Core/   # NO UI. Everything here is unit-tested.
                  #   Radio:    FlexProtocol (line + key=value parsing, MHz formatting), SliceTable
                  #             (merged slice status), RadioClient (TCP session, sequence-matched
                  #             replies, reconnect), Discovery (UDP broadcast parse + listen)
                  #   Buttons:  CommandSequence (the button language: placeholders, wait, comments,
                  #             run with stop-on-error), SliceCapture (button from live state)
                  #   Knob:     KnobProtocol (tokens -> ticks/buttons), KnobTokenizer, KnobPolicy
                  #             (step cycling, next slice, tune target), FlexControlReader (serial
                  #             supervisor, reconnects, "busy" while SmartSDR holds it)
                  #   Family plumbing (from W2): InstallLayout, InstallCommandLine, DesktopEntry,
                  #             UpdateApplyScript, VersionOrder, AtomicFile, Symlink, XdgUserDirs,
                  #             CrashReport, SerialErrors
  FlexPad.App/    # Avalonia MVVM
                  #   Services/  RadioService (owns RadioClient + FlexControlReader + the console
                  #              log; marshals to the UI thread; runs sequences; knob actions),
                  #              AppConfig/ConfigStore (Python-compatible JSON), KnobPort (find the
                  #              knob by USB id), InstallService, UpdateService, CrashLog
                  #   ViewModels/ MainWindow (grid + status), ButtonEditor, Console, Setup
                  #   Views/     MainWindow, ButtonEditorWindow, ConsoleWindow, ReferenceWindow,
                  #              SetupWindow (tabs: Radio, Knob, Updates), ConfirmWindow
                  #   Reference.cs   the cheat sheet, example snippets, and the swatch palette
tests/FlexPad.Core.Tests/   # xUnit — Core only. Keep new logic testable here.
tools/make_icon.py          # draws Assets/app.ico + app-icon.png with Pillow (dev-time only)
```

**Design rule:** all non-UI logic lives in `FlexPad.Core` and is unit-tested; `FlexPad.App` is the
Avalonia shell. Put new parsing/decision logic in Core with tests, not in view-models.

## Radio protocol (validated on a FLEX-8600M, SmartSDR v4.2, 2026-09-14)

- TCP port 4992, one text line per message. We send `C<seq>|<command>`; the radio answers
  `R<seq>|<hex code>|<text>` (code 0 = success). `V…`/`H…` once on connect, `M…|text` messages,
  `S<handle>|<object> key=value…` status after `sub slice all` / `sub tx all`.
- **Slice status is incremental** — a retune sends `RF_frequency` alone — so `SliceTable.Merge`
  merges, never replaces. Placeholders resolve from it: `{slice}` = `active=1`, `{tx}` = `tx=1`,
  `{A}`..`{H}` = `index_letter`.
- Commands verified by sending each with the radio's current value (a no-op the radio accepts or
  rejects): `slice tune`, `slice set … mode= rxant= txant= step= agc_mode= agc_threshold= nr= nr_level=
  nb= nb_level= wnb= wnb_level= anf= rfgain= dax= audio_mute= audio_level= audio_pan=`, `filt`,
  `transmit set rfpower= tunepower=`. **Not** `lock=` (use `slice lock`/`unlock`), **not** `mute=` or
  `audio_gain=`. Antenna ports on the 8600M: `ANT1 ANT2 RX_A RX_B XVTA XVTB`.
- Discovery: UDP broadcast on 4992 once a second, VITA-49 header of 28 bytes then ASCII
  `key=value` pairs. Bind with reuse-address; SmartSDR on the same PC holds the port too.
- Transverter frequencies only tune if the XVTR band is defined in SmartSDR; otherwise the tune is
  refused and the sequence stops with the radio's reason.

## FlexControl knob (validated on a real unit, 2026-09-14)

USB serial, vendor `2192` product `0010`, 9600 8N1, DTR+RTS asserted. Semicolon-terminated tokens,
no line endings; `F0304;` on open. `U`/`D` = one tick, `U03` = three ticks in one poll. **Clockwise is
U.** Knob button `S`/`L`/`C` (press/hold/double), aux buttons `X1S`..`X3C`. Tune-per-pass: the reader
sums all ticks waiting in one read and sends one `slice tune`, updating the cached frequency
optimistically so bursts build on each other. Only one program can hold the port: while SmartSDR has
it the status reads `COMx busy`. Found by USB id (`KnobPort`) — never by "first free port"; this
station has a port grabber (VictronConnect).

## Config & updater

- App config: `%AppData%\flexpad\config.json` (Windows), `~/.config/flexpad/config.json` (Linux).
  **Lower-case `flexpad` and the Python JSON names on purpose** (`flex_host`, `buttons[]`,
  `flexcontrol{}`) so the prototype's settings carry over. New keys go under `window`.
- Setup edits (Radio, Knob tabs) apply on close; Discover and Reload act at once. Buttons are edited
  from the grid's context menu and the editor dialog; the App saves after every change.
- In-app updater (`UpdateService`) targets GitHub `gsa700/flexpad`, checks `/releases/latest`, and
  expects `FlexPad-<rid>.zip` assets. `/releases/latest` excludes pre-releases, so publish `-beta`
  releases as full "Latest". The Python-era releases (v0.1.0–v0.3.0) carry no zips; the first .NET
  release supersedes them.

## Windows and the console

The traffic console is its own window (David, 2026-09-14: "not all will want to see it all the
time"), opened from the toolbar, remembered across runs (`window.console_open`). The log lives in
`RadioService` and accumulates whether or not the window is open. Status traffic (`S` lines and knob
ticks) is hidden unless the console's checkbox is on.

## Hardware & workflow notes

- Radio: FLEX-8600M at 10.0.1.106 (AB0R), front-panel model. FlexControl on COM15 here.
- **Cross-platform validated on real hardware: Windows, Fedora (linux-x64) and the CM5 kiosk
  (linux-arm64, 10.0.1.25, user `derickson`, SSH by the hambench_pi key), 2026-09-14.** On the Pi
  the quiet install/uninstall pair round-tripped too. The FlexControl was then plugged into the
  Fedora box and found by `KnobPort.FindLinux` first time, so the knob is verified on Linux as well. Launch a
  GUI there from SSH inside the user session: `XDG_RUNTIME_DIR=/run/user/1000` +
  `systemd-run --user --unit=<name> --collect --quiet <exe>`; `grim` screenshots with
  `WAYLAND_DISPLAY=wayland-0`. Beware `pgrep -f` matching the SSH shell's own command line.
- **The Claude desktop app's shells virtualise `%APPDATA%`** (MSIX): a config written from a Claude
  shell lands in `AppData\Local\Packages\Claude_*\LocalCache\Roaming\flexpad`, not where the real app
  reads. Verify or write there through a one-off scheduled task, or have David run the installer. Bit
  us on 2026-09-14 (the Python installer "moved" his config into the cache). `%LOCALAPPDATA%\Programs`
  and the Start Menu folder were *not* redirected in the same session — don't reason about which
  paths are safe, verify.
- **Windows registry writes from a shell launch are virtualised by the Program Compatibility
  Assistant** for unsigned exes (see W2's notes). This app therefore registers no installed-apps
  entry; Windows integration is shortcuts only, removal is Setup → Updates → Remove.
- The Python prototype's `%LOCALAPPDATA%\Programs\flexpad` is the same folder as this app's
  `Programs\FlexPad` on a case-insensitive filesystem. Uninstall the prototype (its `uninstall.ps1`)
  **before** installing this app, or the prototype's uninstaller will later delete `FlexPad.exe`.

## Self-install (Windows and Linux)

Ported from W2 Monitor unchanged; read W2's CLAUDE.md section of the same name for the rationale
(per-user install because the updater swaps the exe in place; `portable.txt` beats everything;
uninstall deletes only a directory the app owns; settings are named, not swept; no registry entry;
close windows from an idle dispatcher frame, never from inside a dialog's click).

## Release workflow

`gh` is installed and authed as `gsa700`. A release = git tag + three self-contained zips
(`FlexPad-win-x64.zip`, `-linux-x64.zip`, `-linux-arm64.zip`) attached to a GitHub release with the
version-only title, published with `--latest`. `<1.0` = `-beta`. Update `CHANGELOG.md` for every
release. **Commit the version bump before publishing** (binaries embed the commit sha) and
**smoke-test a published single-file binary before uploading**.
