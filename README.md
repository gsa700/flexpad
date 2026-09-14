# flexpad

Programmable buttons for a FlexRadio FLEX-6000 or FLEX-8000. Each button is a
list of SmartSDR API commands that fire in order when you press it. Think of it
as programmable telnet: the same text protocol you could type into port 4992
by hand, with a button in front of it and a traffic log underneath.

It talks to the radio directly over the network. SmartSDR does not need to be
running, and nothing else (no Node-RED, no CAT, no Stream Deck) sits in
between.

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

Requires Python 3.9 or newer. Standard library only, nothing to `pip install`.

Windows:

```
.\install.ps1
```

That creates `config.json` from the example if you don't have one and puts a
"flexpad" shortcut in the Start Menu. It never overwrites an existing config.
`uninstall.ps1` removes the shortcut and the log; `-Purge` also removes
`config.json`.

Linux and macOS: `python3 flexpad.py`. On Fedora you may need
`sudo dnf install python3-tkinter`; on Debian, `python3-tk`.

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

## Command line

```
python flexpad.py --discover          list radios announcing on the LAN
python flexpad.py --send "ant list"   one command, print the reply, exit
python flexpad.py --run "2m USB"      fire a button headless (for scripts or a Stream Deck)
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
  never mixes with your settings.
- The process opts into DPI awareness on Windows so it renders crisp at 150%.

## License

GPL-3.0. See `LICENSE`.
