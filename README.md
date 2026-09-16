# FlexPad

Programmable command buttons for a FlexRadio FLEX-6000 or FLEX-8000, and a home for the
FlexControl knob when SmartSDR isn't running. Windows, Linux and Raspberry Pi.

Each button is a list of SmartSDR API commands that fire in order when you press it: think of it as
programmable telnet, the same text protocol you could type into port 4992 by hand, with a button in
front of it and a console behind it. It talks to the radio directly over the network; SmartSDR does
not need to be running and nothing else sits in between.

## Why

SmartSDR memory channels store frequency, mode and filter, but not the RX and TX antenna ports.
Recall a 2 m transverter memory from 20 m and you are on 144.200 with ANT1 still selected instead
of XVTA. The radio only applies its per-band antenna choice on a Band-button change, never on a
retune, and a memory recall is a retune. FlexPad makes a button that does the whole job:

```
slice tune {slice} 144.200
slice set {slice} mode=USB
slice set {slice} rxant=XVTA txant=XVTA
filt {slice} 150 2900
```

Anything the API can do, a button can do: open a second slice on XVTB, move TX to a slice, load a
profile, and so on.

## Features

- **Buttons** in a grid you size in Setup. Drag one onto another to rearrange, or onto empty space
  in a row to move it there. Right-click to edit, duplicate, reorder or delete. Hotkeys (`F1`,
  `Ctrl+1`) work while the window has focus. Colours from a swatch palette.
- **Band set**: + Button → Band set… makes a button per band in one go. The amateur set, 160 m to
  70 cm, defaults to *change band only*: one command, and the radio brings back the frequency,
  mode, filter and antenna ports it last had on that band, like the front-panel band buttons.
  Or untick it for a full recipe at each band's usual spot with your antenna choices for HF and the
  transverter ports, phone or CW. A second set covers AM broadcast, the shortwave broadcast bands,
  11 m CB and WWV, in AM. They go in their own row along the bottom of the window; any button can
  be moved into or out of that row from the editor.
- **Build from choices**: in the editor, pick frequency, mode, RX and TX antenna, filter and step
  from lists the radio itself reports, press Build, and the command lines appear, ready to save or
  tweak. No command knowledge needed.
- **Capture slice**: set the radio up the way you want it, then capture the active slice as
  commands. Basic takes frequency, mode, both antenna ports and the filter; Full adds tuning step,
  AGC, noise tools, RF gain, DAX and TX power. A new button is labelled from the frequency and mode.
- **Placeholders**: `{slice}` is the active slice, `{tx}` the transmit slice, `{A}`..`{H}` a slice
  by letter, `{pan}` the active slice's panadapter (for `display pan set {pan} band=20`), all
  resolved from the radio's live status. `wait 0.5` pauses; `#` starts a comment.
- **FlexControl knob**: turning it tunes the active slice by its current tuning step; fast spins are
  honoured. The knob button and the three aux buttons each have press, hold and double-click,
  mapped in Setup to radio functions: cycle the tuning step, next slice, mute, TX on this slice,
  TUNE, MOX, ATU tune and bypass, AMP operate/standby (Power Genius), cycle RX or TX antenna, cycle
  mode or AGC, NR / NB / WNB / ANF, tuning lock, RIT and XIT. Each reads the radio's state and sends
  the opposite or the next value. Found by its USB id, so no other serial device is ever opened by
  mistake.
- **Console** in its own window: every line to and from the radio, colour-coded, plus a command
  line with history and a one-page **Reference** of the commands that matter.
- **Setup → Updates**: in-app update from GitHub releases, a startup check if you want it, and
  Remove.

## Install

Download the zip for your platform from the [latest release](https://github.com/gsa700/flexpad/releases/latest),
unzip, and run it. It offers to install itself; say yes and it copies itself to a per-user folder,
adds Start Menu and desktop shortcuts (an applications-menu entry and a `flexpad` command on Linux),
and relaunches from there.

| | Goes in | Appears in | Remove with |
|---|---|---|---|
| Windows | `%LOCALAPPDATA%\Programs\FlexPad` | Start Menu, desktop | Setup → Updates → Remove |
| Linux / Pi | `~/.local/share/flexpad` | applications menu, desktop, `flexpad` on PATH | Setup → Updates → Remove, or `flexpad --uninstall` |

Don't want it installed? Put a file named `portable.txt` beside the program. It then runs where it
stands and stops asking. Settings live in your profile either way.

On Windows it does **not** appear in Settings → Apps, on purpose: the app is unsigned, and Windows
silently discards registry writes from unsigned programs started via Explorer. Removal lives inside
the app instead.

Settings: `%APPDATA%\flexpad\config.json` on Windows, `~/.config/flexpad/config.json` on Linux. If
you used the Python FlexPad, this reads the same file, buttons and all. You can edit the file by
hand and press Reload in Setup.

## First run

On first run FlexPad finds the radio on the LAN. If it can't (different subnet, VPN, SmartLink), put
the address in Setup → Radio. A fresh settings file comes with twelve buttons to start from: 2 m and
70 cm on the transverter ports, 20 m and 40 m on ANT1, ANT1/ANT2/XVTA/XVTB antenna-only buttons,
TX to A / B, a second receiver on 70 cm, and Close B. Transverter frequencies only tune if the XVTR
band is defined in SmartSDR.

The knob starts enabled and finds itself. Only one program can hold it: while SmartSDR is running
the status line reads `knob COMx busy` until SmartSDR closes.

## Requirements

None beyond the download: the executable is self-contained. Linux needs the user in the `dialout`
group for the knob's serial port.

## Reporting a problem

An unhandled error is written to `crash.log` beside the settings file, and Setup → Updates shows a
notice with a button to reveal it. Attach it to an issue.

## Build from source

Needs the .NET 10 SDK.

```sh
dotnet build
dotnet run --project src/FlexPad.App
dotnet test
dotnet publish src/FlexPad.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

Full API reference for the commands: <https://github.com/flexradio/smartsdr-api-docs/wiki>.

## License

GPL-3.0. Written by David Erickson (AB0R) in collaboration with Claude.
