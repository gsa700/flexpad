# flexpad

Programmable buttons for a FlexRadio FLEX-6000 or FLEX-8000. Each button is a
list of SmartSDR API commands that fire in order when you press it. Think of it
as programmable telnet: the same text protocol you could type into port 4992
by hand, with a button in front of it and a traffic log underneath.

It talks to the radio directly over the network. SmartSDR does not need to be
running, and nothing else (no Node-RED, no CAT, no Stream Deck) sits in
between.

It also drives a FlexControl USB tuning knob, so the knob keeps working when
you operate from the radio's front panel with SmartSDR closed. See
[FlexControl knob](#flexcontrol-knob).

## Why

SmartSDR memory channels store frequency, mode and filter, but not the RX and
TX antenna ports. Recall a 2 m transverter memory from 20 m and you are on
144.200 with ANT1 still selected instead of XVTA. The radio only applies its
per-band antenna choice on a Band-button change, never on a retune, and a
memory recall is a retune. Elecraft memories remember everything; these do not.

flexpad makes a button that does the whole job:

```
slice tune {slice} 144.200
slice set {slice} mode=USB
slice set {slice} rxant=XVTA txant=XVTA
filt {slice} 150 2900
```

Anything the API can do, a button can do: open a second slice on XVTB, move
TX to a slice, load a profile, key an amplifier control, and so on.

## Setup

Requires Python 3.9 or newer. Standard library only, except pyserial for the
optional FlexControl knob, which the installer adds.

Windows:

```
.\install.ps1
```

That copies the program to `%LOCALAPPDATA%\Programs\flexpad`, puts your
settings in `%APPDATA%\flexpad` (creating `config.json` from the example if
you don't have one), adds a Start Menu shortcut, and registers in Add/Remove
Programs. Re-running it upgrades the program and never touches an existing
config. `uninstall.ps1` removes all of that and keeps your settings unless you
pass `-Purge`.

Linux and macOS: `python3 flexpad.py`. Settings go in `~/.config/flexpad`
(Linux) or `~/Library/Application Support/flexpad` (macOS). On Fedora you may
need `sudo dnf install python3-tkinter`; on Debian, `python3-tk`.

Portable or development use: create an empty file named `portable` beside
`flexpad.py` and it keeps config, log and window state next to itself
instead.

On first run the app discovers the radio on the LAN. If it can't (different
subnet, VPN, SmartLink), put the address in **Setup...** or in `config.json`.

## Buttons

Right-click a button for Edit, Duplicate, Move and Delete. **+ Button** adds
one. Everything lands in `config.json`, which you can also edit by hand; press
**Reload** afterwards.

The editor has an **Insert example** menu that drops a ready-made snippet into
the command list (tune plus mode plus antenna plus filter, antenna only, open
a second slice, move TX, load a profile, and so on). **Reference** opens a
one-page cheat sheet of the commands that matter for this job, shows the
antenna ports your radio actually has, and links to FlexRadio's full API
wiki at <https://github.com/flexradio/smartsdr-api-docs/wiki>.

The stock `config.example.json` gives you twelve buttons to start from: four
band buttons (2 m and 70 cm on the transverter ports, 20 m and 40 m on ANT1),
four antenna-only buttons (ANT1, ANT2, XVTA, XVTB for whichever slice is
active), TX to A, TX to B, a second receiver on 70 cm, and Close B.

A button has a label, an optional hotkey, an optional color, and a list of
command lines:

| line | meaning |
|---|---|
| `slice set {slice} rxant=XVTA` | sent to the radio after substitution |
| `wait 0.5` | pause that many seconds |
| `# anything` | comment, ignored |

Placeholders:

| placeholder | becomes |
|---|---|
| `{slice}` | index of the active slice (the one with the yellow flag in SmartSDR) |
| `{tx}` | index of the transmit slice |
| `{A}` .. `{H}` | index of the slice with that letter |

Commands go out one at a time and each waits for the radio's reply. A non-zero
reply code stops the sequence (turn off *Stop a sequence at the first error*
in Setup to run on regardless). The sequence, every reply, and any error show
in the log pane; errors are also written to `flexpad.log`.

Hotkeys are plain names: `F1`, `ctrl+1`, `alt+shift+x`. They work while the
flexpad window has focus. Colors are any Tk color: `#2d6a4f`, `darkred`.

The command line at the bottom sends one command by hand, with the same
placeholders and an up/down history. Tick *status traffic* to also see the
radio's `S` status stream, which is noisy but is exactly what SmartSDR sees.

## FlexControl knob

If a FlexRadio FlexControl USB knob is plugged in, flexpad drives it, which
matters when you operate from the radio's front panel and SmartSDR isn't
running to own the knob. It needs pyserial, which the installer adds; without
it the knob is simply off.

- Turning the knob tunes the active slice by its current tuning step, the
  same step SmartSDR and the front panel use. Fast spins are honored: the
  knob reports how many ticks passed, and flexpad multiplies.
- The knob button and the three aux buttons each have short press, hold,
  and double-click events. **Knob...** in the toolbar maps each one to a
  built-in action or to any of your flexpad buttons by label.

Built-in actions:

| action | does |
|---|---|
| `@step` | cycle the tuning step through the list in Knob... (default 10, 100, 1000, 10000 Hz) |
| `@next-slice` | move the active flag to the next open slice |
| `@mute` | toggle audio mute on the active slice |
| `@tx` | make the active slice the transmit slice |

Defaults: knob press cycles the step, hold moves to the next slice, double
click mutes. The aux buttons start unbound; an unbound press shows in the log
so you can see which is which. If clockwise tunes down on your unit, tick
*Invert direction*.

The knob is found by its USB id, so other serial devices are never opened by
mistake. Only one program can hold it: when SmartSDR is running it takes the
knob, and flexpad shows `knob COMx busy` until SmartSDR closes. The status
line shows the port in use, the tuning step, and the active slice frequency.

`python flexpad.py --knob` prints raw knob events without touching the radio,
handy for checking direction and learning the button codes.

A binding is just the button's label, so in `config.json` the section looks
like this, with AUX1 firing the 2 m button and AUX2 the 70 cm one:

```json
"flexcontrol": {
  "enabled": true,
  "port": "",
  "invert": false,
  "steps": [10, 100, 1000, 10000],
  "bindings": {
    "S": "@step", "L": "@next-slice", "C": "@mute",
    "X1S": "2m USB", "X2S": "70cm USB", "X3S": "20m USB",
    "X1L": "@tx", "X2L": "", "X3L": "",
    "X1C": "", "X2C": "", "X3C": ""
  }
}
```

The codes are the knob's own: `S`, `L`, `C` for the knob button's press, hold
and double click; `X1S`..`X3S`, `X1L`..`X3L`, `X1C`..`X3C` for the same on the
three aux buttons.

## Command line

```
python flexpad.py --discover          list radios announcing on the LAN
python flexpad.py --send "ant list"   one command, print the reply, exit
python flexpad.py --run "2m USB"      fire a button headless (for scripts or a Stream Deck)
python flexpad.py --knob              print FlexControl events, no radio needed (Ctrl+C to stop)
```

`--run` returns exit code 1 if any command failed, so it is safe to chain.

## API notes

The useful commands for this job, all checked against a FLEX-8600M on
SmartSDR v4. Frequencies are in MHz. The in-app Reference has the longer
list.

```
slice tune <n> <MHz>                       retune
slice set <n> mode=USB                     mode: USB LSB CW AM FM DIGU DIGL ...
slice set <n> rxant=XVTA txant=XVTA        antenna ports; also ANT1 ANT2 XVTB RX_A RX_B
slice set <n> tx=1                         make this the transmit slice
slice set <n> active=1                     make this the active slice
filt <n> <low> <high>                      RX filter edges in Hz (negative for LSB/CW-L)
slice create freq=<MHz> ant=<port> mode=<m>  open a new slice
slice remove <n>                           close one
profile global load "<name>"               load a global profile
ant list                                   see what ports this radio has
```

Transverter frequencies only tune if the XVTR band is defined in SmartSDR
(Settings, Transverter). Without it the radio rejects the tune and the
sequence stops there with the radio's reason in the log.

## Implementation notes

- One TCP session to port 4992, reconnecting on its own. Replies are matched
  to commands by sequence number, so a slow reply never lands on the wrong
  step.
- The radio's slice status stream is merged into a table so the placeholders
  and the status line are always current without polling.
- Discovery listens for the radio's once-a-second UDP broadcast on port 4992
  with `SO_REUSEADDR`, so it coexists with SmartSDR on the same PC.
- Window geometry is kept in `ui_state.json`, not `config.json`, so UI state
  never mixes with your settings. Both live in the settings folder, so the
  program folder holds only the program and can be replaced on upgrade.
- The process opts into DPI awareness on Windows so it renders crisp at 150%.
- The FlexControl is a USB serial device, vendor `2192` product `0010`, at
  9600 8N1. It speaks semicolon-terminated tokens with no line endings and
  sends `F0304;` when a host opens it. `U` and `D` are single knob ticks,
  `U03` means three ticks arrived in one USB poll. Verified on a real unit:
  clockwise is `U`, the knob button sends `S`, `L`, `C`, and the aux buttons
  `X1S`..`X3C`. flexpad reads whatever is waiting, sums the ticks, and sends
  one `slice tune` per pass, so a fast spin never queues up behind the
  radio's replies. It updates its own frequency cache before the radio's
  status echo returns, so consecutive bursts build on each other.

## License

GPL-3.0. See `LICENSE`.
