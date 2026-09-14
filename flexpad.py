#!/usr/bin/env python3
"""
flexpad - programmable buttons for a FlexRadio 6000/8000.

Each button is a list of SmartSDR API commands that fire in order when the
button is pressed. Think of it as programmable telnet: the same text protocol
you could type into port 4992 by hand, with a button in front of it and a log
underneath. It talks to the radio directly, so nothing else has to be running.

    python flexpad.py                 GUI (pythonw on Windows to hide the console)
    python flexpad.py --discover      list radios announcing on the LAN
    python flexpad.py --send "ant list"       one command, print the reply
    python flexpad.py --run "2m USB"          fire a button headless
    python flexpad.py --knob                  print FlexControl knob events

A FlexControl USB knob, if present, tunes the active slice; its buttons map to
built-in actions or to your own buttons (Knob... in the toolbar). That needs
pyserial; everything else is standard library.

Buttons live in config.json in your settings folder
(%APPDATA%\flexpad on Windows; Setup... shows the path); edit them in the app
(right-click a button) or in the file, then Reload.
"""

import argparse
import io
import json
import logging
import logging.handlers
import os
import queue
import re
import socket
import sys
import subprocess
import threading
import time
import urllib.request
import webbrowser
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
EXAMPLE_PATH = os.path.join(HERE, "config.example.json")


def data_dir():
    """Where config, log and window state live.

    Installed: the per-user settings folder, so the program folder holds only
    the program and survives reinstalls. Portable or development: a file named
    `portable` beside this script keeps everything next to it instead.
    """
    if os.path.exists(os.path.join(HERE, "portable")):
        return HERE
    if sys.platform == "win32":
        base = os.environ.get("APPDATA") or os.path.expanduser("~")
    elif sys.platform == "darwin":
        base = os.path.expanduser("~/Library/Application Support")
    else:
        base = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    path = os.path.join(base, "flexpad")
    os.makedirs(path, exist_ok=True)
    return path


DATA_DIR = data_dir()
CONFIG_PATH = os.path.join(DATA_DIR, "config.json")
UI_STATE_PATH = os.path.join(DATA_DIR, "ui_state.json")
LOG_PATH = os.path.join(DATA_DIR, "flexpad.log")

DISCOVERY_PORT = 4992
COMMAND_TIMEOUT = 5.0
RECONNECT_SECONDS = 5.0

__version__ = "0.3.0"

log = logging.getLogger("flexpad")


# ----------------------------------------------------------------- config ---

def load_config():
    if not os.path.exists(CONFIG_PATH):
        with open(EXAMPLE_PATH, encoding="utf-8") as fh:
            cfg = json.load(fh)
        save_config(cfg)
        log.info("config.json created from the example")
        return cfg
    with open(CONFIG_PATH, encoding="utf-8") as fh:
        cfg = json.load(fh)
    cfg.setdefault("flex_host", "")
    cfg.setdefault("flex_port", 4992)
    cfg.setdefault("columns", 4)
    cfg.setdefault("stop_on_error", True)
    cfg.setdefault("auto_check_updates", False)
    cfg.setdefault("buttons", [])
    for b in cfg["buttons"]:
        b.setdefault("label", "?")
        b.setdefault("commands", [])
    knob_config(cfg)
    return cfg


def save_config(cfg):
    """Write atomically so a crash mid-write can't leave a half file behind."""
    tmp = CONFIG_PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump(cfg, fh, indent=2)
        fh.write("\n")
    os.replace(tmp, CONFIG_PATH)


def load_ui_state():
    try:
        with open(UI_STATE_PATH, encoding="utf-8") as fh:
            return json.load(fh)
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return {}


def save_ui_state(**entries):
    state = load_ui_state()
    state.update(entries)
    try:
        with open(UI_STATE_PATH, "w", encoding="utf-8") as fh:
            json.dump(state, fh, indent=2)
    except OSError as err:
        log.warning("Could not write %s: %s", UI_STATE_PATH, err)


def parse_kv(text):
    """Split 'a=1 b=2' into a dict, ignoring bare tokens."""
    out = {}
    for tok in text.split():
        if "=" in tok:
            k, v = tok.split("=", 1)
            out[k] = v
    return out


# -------------------------------------------------------------- discovery ---

def discover(seconds=4.0):
    """Listen for the radio's UDP discovery broadcast. Returns a list of dicts.

    The packet is VITA-49 with an ASCII key=value payload after a 28-byte
    header. SmartSDR on the same PC also binds this port, hence SO_REUSEADDR.
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        sock.bind(("", DISCOVERY_PORT))
    except OSError as err:
        log.warning("Discovery port busy: %s", err)
        return []
    sock.settimeout(0.5)
    found = {}
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            data, _ = sock.recvfrom(4096)
        except socket.timeout:
            continue
        text = data[28:].decode("ascii", "ignore")
        kv = dict(re.findall(r"(\w+)=(\S+)", text))
        if "ip" in kv and "model" in kv:
            found[kv["ip"]] = kv
    sock.close()
    return list(found.values())


# ------------------------------------------------------------ radio client ---

class NotConnected(Exception):
    pass


class FlexClient(threading.Thread):
    """One TCP session to the radio, reconnecting on its own.

    send() is synchronous: it waits for the R<seq>| reply and returns
    (code, text). Status lines are merged into self.slices. Every line in or
    out is offered to `on_traffic(direction, text)` for the log pane.
    """

    def __init__(self, host, port, on_traffic=None, on_state=None):
        super().__init__(name="flex-client", daemon=True)
        self.host = host
        self.port = port
        self.on_traffic = on_traffic or (lambda d, t: None)
        self.on_state = on_state or (lambda: None)
        self.sock = None
        self.connected = False
        self.error = None
        self.version = None
        self.handle = None
        self.slices = {}          # index -> merged attribute dict
        self.transmit = {}        # merged 'transmit' status (rfpower, tunepower, ...)
        self._seq = 0
        self._pending = {}        # seq -> (Event, holder list)
        self._lock = threading.Lock()
        self._stop = threading.Event()

    # -- lifecycle --

    def stop(self):
        self._stop.set()
        self._close()

    def _close(self):
        sock, self.sock = self.sock, None
        was = self.connected
        self.connected = False
        if sock:
            try:
                sock.close()
            except OSError:
                pass
        with self._lock:
            for ev, holder in self._pending.values():
                holder[:] = [(-1, "disconnected")]
                ev.set()
            self._pending.clear()
        if was:
            self.on_state()

    def run(self):
        while not self._stop.is_set():
            try:
                self._session()
            except (OSError, ConnectionError) as err:
                if self._stop.is_set():
                    break       # stop() closed the socket under us; not an error
                self.error = str(err)
                self.on_traffic("!", f"connection lost: {err}")
                log.warning("Radio connection lost: %s", err)
            self._close()
            if self._stop.is_set():
                break
            self._stop.wait(RECONNECT_SECONDS)

    def _session(self):
        if not self.host:
            radios = discover()
            if not radios:
                self.error = "no radio found"
                return
            self.host = radios[0]["ip"]
            self.on_traffic("!", f"discovered {radios[0].get('model')} "
                                 f"{radios[0].get('nickname', '')} at {self.host}")
        self.on_traffic("!", f"connecting to {self.host}:{self.port}")
        sock = socket.create_connection((self.host, self.port), timeout=10)
        sock.settimeout(1.0)
        self.sock = sock
        self.slices.clear()
        self.error = None
        self.connected = True
        self.on_state()
        buf = b""
        self.send("sub slice all", wait=False)
        self.send("sub tx all", wait=False)
        while not self._stop.is_set() and self.sock is sock:
            try:
                chunk = sock.recv(8192)
            except socket.timeout:
                continue
            if not chunk:
                raise ConnectionError("radio closed the connection")
            buf += chunk
            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                self._on_line(line.decode("utf-8", "replace").strip())

    # -- protocol --

    def _on_line(self, line):
        if not line:
            return
        kind = line[0]
        if kind == "R":
            self.on_traffic("<", line)
            # R<seq>|<hex code>|<text>
            seq, _, rest = line[1:].partition("|")
            code_hex, _, text = rest.partition("|")
            try:
                code = int(code_hex, 16)
            except ValueError:
                code = -1
            with self._lock:
                entry = self._pending.pop(int(seq), None) if seq.isdigit() else None
            if entry:
                entry[1][:] = [(code, text)]
                entry[0].set()
        elif kind == "S":
            self.on_traffic("S", line)
            _, _, body = line.partition("|")
            if body.startswith("slice "):
                idx, _, attrs = body[len("slice "):].partition(" ")
                # Incremental - a retune sends RF_frequency alone, so merge.
                self.slices.setdefault(idx, {}).update(parse_kv(attrs))
                self.on_state()
            elif body.startswith("transmit "):
                self.transmit.update(parse_kv(body[len("transmit "):]))
        elif kind == "V":
            self.version = line[1:]
            self.on_traffic("<", line)
        elif kind == "H":
            self.handle = line[1:]
            self.on_traffic("<", line)
        else:
            self.on_traffic("<", line)

    def send(self, cmd, wait=True, timeout=COMMAND_TIMEOUT):
        sock = self.sock
        if not sock or not self.connected:
            raise NotConnected("not connected to the radio")
        with self._lock:
            self._seq += 1
            seq = self._seq
            holder = []
            ev = threading.Event()
            if wait:
                self._pending[seq] = (ev, holder)
        wire = f"C{seq}|{cmd}\n"
        self.on_traffic(">", wire.rstrip())
        try:
            sock.sendall(wire.encode())
        except OSError as err:
            raise NotConnected(str(err))
        if not wait:
            return (0, "")
        if not ev.wait(timeout):
            with self._lock:
                self._pending.pop(seq, None)
            return (-1, f"no reply within {timeout:g}s")
        return holder[0]

    # -- slice helpers --

    def live_slices(self):
        return {i: s for i, s in self.slices.items() if s.get("in_use") == "1"}

    def active_slice(self):
        for i, s in self.live_slices().items():
            if s.get("active") == "1":
                return i
        return None

    def tx_slice(self):
        for i, s in self.live_slices().items():
            if s.get("tx") == "1":
                return i
        return None

    def slice_by_letter(self, letter):
        for i, s in self.live_slices().items():
            if s.get("index_letter") == letter:
                return i
        return None


# ------------------------------------------------------------- sequencing ---

PLACEHOLDER = re.compile(r"\{(slice|tx|[A-H])\}")


class SequenceError(Exception):
    pass


def substitute(client, cmd):
    def repl(m):
        key = m.group(1)
        if key == "slice":
            idx = client.active_slice()
            what = "no active slice"
        elif key == "tx":
            idx = client.tx_slice()
            what = "no transmit slice"
        else:
            idx = client.slice_by_letter(key)
            what = f"no slice {key}"
        if idx is None:
            raise SequenceError(f"{what} - cannot fill {{{key}}}")
        return idx
    return PLACEHOLDER.sub(repl, cmd)


def run_sequence(client, commands, stop_on_error=True, report=None):
    """Fire a button's commands in order. Returns True if all succeeded.

    Lines: blank or '#' comments are skipped; 'wait <s>' pauses; anything
    else goes to the radio after {slice}/{tx}/{A}..{H} substitution.
    """
    report = report or (lambda t: None)
    ok = True
    for raw in commands:
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.lower().startswith("wait "):
            try:
                time.sleep(float(line.split()[1]))
            except (IndexError, ValueError):
                report(f"bad wait line: {line}")
                ok = False
                if stop_on_error:
                    return False
            continue
        try:
            cmd = substitute(client, line)
            code, text = client.send(cmd)
        except (SequenceError, NotConnected) as err:
            report(f"error: {err}")
            ok = False
            if stop_on_error:
                return False
            continue
        if code != 0:
            report(f"error 0x{code:X} {text} <- {cmd}")
            ok = False
            if stop_on_error:
                return False
    return ok


# ------------------------------------------------------------ flexcontrol ---
#
# The FlexControl is a USB serial device (9600 8N1) speaking a tiny CAT-style
# protocol: semicolon-terminated tokens, no line endings. U/D are knob ticks
# (U03 means three ticks arrived in one USB poll), S/L/C are short, long and
# double presses of the knob, X1S..X3L the same for the three aux buttons.
# It sends F0304; when a host opens it.

FLEXCONTROL_VID, FLEXCONTROL_PID = 0x2192, 0x0010
KNOB_EVENTS = ["S", "L", "C",
               "X1S", "X1L", "X1C", "X2S", "X2L", "X2C", "X3S", "X3L", "X3C"]
KNOB_EVENT_NAMES = {
    "S": "knob short press", "L": "knob long press", "C": "knob double click",
    "X1S": "AUX1 press", "X1L": "AUX1 hold", "X1C": "AUX1 double",
    "X2S": "AUX2 press", "X2L": "AUX2 hold", "X2C": "AUX2 double",
    "X3S": "AUX3 press", "X3L": "AUX3 hold", "X3C": "AUX3 double",
}
# Built-in actions a knob event can map to; anything else is a button label.
KNOB_ACTIONS = ["@step", "@next-slice", "@mute", "@tx"]
DEFAULT_FLEXCONTROL = {
    "enabled": True,
    "port": "",                  # blank = find it by USB id
    "invert": False,             # flip if clockwise tunes down on your unit
    "steps": [10, 100, 1000, 10000],
    "bindings": {"S": "@step", "L": "@next-slice", "C": "@mute",
                 "X1S": "", "X1L": "", "X1C": "", "X2S": "", "X2L": "", "X2C": "",
                 "X3S": "", "X3L": "", "X3C": ""},
}
KNOB_TOKEN = re.compile(r"([UD])(\d*)")


def find_flexcontrol():
    """COM port of the first FlexControl on the system, or None."""
    try:
        from serial.tools import list_ports
    except ImportError:
        return None
    for p in list_ports.comports():
        if p.vid == FLEXCONTROL_VID and p.pid == FLEXCONTROL_PID:
            return p.device
    return None


class FlexControl(threading.Thread):
    """Reads the knob and reports turns and presses; reconnects on its own.

    on_turn(delta) gets the net ticks read in one pass, positive for U.
    on_button(code) gets one of KNOB_EVENTS. on_status(text) reports the
    port state for the status bar. All three run on this thread.
    """

    def __init__(self, port="", invert=False, on_turn=None, on_button=None, on_status=None):
        super().__init__(name="flexcontrol", daemon=True)
        self.port = port
        self.invert = invert
        self.on_turn = on_turn or (lambda d: None)
        self.on_button = on_button or (lambda c: None)
        self.on_status = on_status or (lambda t: None)
        self.ser = None
        self._stop = threading.Event()

    def stop(self):
        self._stop.set()
        ser, self.ser = self.ser, None
        if ser:
            try:
                ser.close()
            except Exception:
                pass

    def run(self):
        try:
            import serial
        except ImportError:
            self.on_status("pyserial not installed")
            return
        while not self._stop.is_set():
            port = self.port or find_flexcontrol()
            if not port:
                self.on_status("not found")
                self._stop.wait(5)
                continue
            try:
                self.ser = serial.Serial(port, 9600, timeout=0.2)
            except serial.SerialException as err:
                busy = "Access is denied" in str(err) or "PermissionError" in str(err)
                self.on_status(f"{port} busy" if busy else f"{port}: {err}")
                self._stop.wait(5)
                continue
            self.on_status(port)
            try:
                self._read_loop(self.ser)
            except serial.SerialException as err:
                if self._stop.is_set():
                    break
                self.on_status(f"{port} lost")
                log.warning("FlexControl read failed: %s", err)
            finally:
                ser, self.ser = self.ser, None
                if ser:
                    try:
                        ser.close()
                    except Exception:
                        pass
            self._stop.wait(3)

    def _read_loop(self, ser):
        buf = b""
        while not self._stop.is_set() and self.ser is ser:
            chunk = ser.read(1)
            if not chunk:
                continue
            waiting = ser.in_waiting
            if waiting:
                chunk += ser.read(waiting)
            buf += chunk
            if b";" not in buf:
                continue
            *tokens, buf = buf.split(b";")
            delta = 0
            for raw in tokens:
                tok = raw.decode("ascii", "ignore").strip()
                if not tok or tok.startswith("F"):
                    continue                     # F0304 is the hello on open
                m = KNOB_TOKEN.fullmatch(tok)
                if m:
                    n = int(m.group(2) or 1)
                    delta += n if m.group(1) == "U" else -n
                elif tok in KNOB_EVENT_NAMES:
                    self.on_button(tok)
                else:
                    log.info("FlexControl sent unknown token %r", tok)
            if delta:
                self.on_turn(-delta if self.invert else delta)


def knob_config(cfg):
    """The flexcontrol section with every key present."""
    fc = cfg.setdefault("flexcontrol", {})
    for k, v in DEFAULT_FLEXCONTROL.items():
        if k == "bindings":
            b = fc.setdefault("bindings", {})
            for code in KNOB_EVENTS:
                b.setdefault(code, v.get(code, ""))
        else:
            fc.setdefault(k, v)
    return fc


def format_mhz(hz):
    return f"{hz / 1_000_000:.6f}"


# ---------------------------------------------------------------- updates ---
#
# Same shape as the other station apps: ask GitHub for the latest release,
# offer to install it in place, then offer a restart. flexpad is plain files,
# so "install" means downloading the release's source zip and swapping the
# program files in the folder this script runs from. Settings are elsewhere
# and untouched. A git checkout is never updated this way: use git pull.

REPO = "gsa700/flexpad"
API_LATEST = f"https://api.github.com/repos/{REPO}/releases/latest"
RELEASES_URL = f"https://github.com/{REPO}/releases/latest"
UPDATE_FILES = ["flexpad.py", "config.example.json", "README.md", "LICENSE",
                "uninstall.ps1", "requirements.txt",
                "assets/flexpad.ico", "assets/flexpad.png"]


class UpdateError(Exception):
    pass


def version_tuple(s):
    out = []
    for part in str(s).lstrip("vV").strip().split("."):
        digits = ""
        for ch in part:
            if ch.isdigit():
                digits += ch
            else:
                break
        out.append(int(digits) if digits else 0)
    return tuple(out)


def _fetch(url, timeout):
    req = urllib.request.Request(url, headers={"User-Agent": f"flexpad/{__version__}"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def check_latest():
    """Latest release on GitHub: {version, tag, zip_url, url, newer}."""
    try:
        data = json.loads(_fetch(API_LATEST, 10))
    except Exception as err:
        raise UpdateError(f"could not reach GitHub: {err}")
    tag = str(data.get("tag_name", ""))
    if not tag:
        raise UpdateError("no release found")
    return {
        "tag": tag,
        "version": tag.lstrip("vV"),
        "zip_url": data.get("zipball_url"),
        "url": data.get("html_url", RELEASES_URL),
        "newer": version_tuple(tag) > version_tuple(__version__),
    }


def install_update(info, dest=HERE):
    """Download the release zip and replace the program files in dest.

    Each file is written beside its target and swapped in with os.replace,
    so a failed download never leaves a half-written program. Python has
    already loaded this script, so overwriting it is fine; the new code runs
    on the next start.
    """
    if os.path.isdir(os.path.join(dest, ".git")):
        raise UpdateError("this is a git checkout - use git pull instead")
    if not info.get("zip_url"):
        raise UpdateError("release has no source zip")
    try:
        blob = _fetch(info["zip_url"], 120)
    except Exception as err:
        raise UpdateError(f"download failed: {err}")
    try:
        zf = zipfile.ZipFile(io.BytesIO(blob))
    except zipfile.BadZipFile:
        raise UpdateError("downloaded file is not a zip")
    names = zf.namelist()
    staged = {}
    for rel in UPDATE_FILES:
        member = next((n for n in names if n.endswith("/" + rel)), None)
        if member is None:
            if rel == "flexpad.py":
                raise UpdateError("release zip has no flexpad.py")
            continue                       # optional file missing: skip
        staged[rel] = zf.read(member)
    if b"__version__" not in staged["flexpad.py"]:
        raise UpdateError("release zip does not look like flexpad")
    for rel, data in staged.items():
        target = os.path.join(dest, *rel.split("/"))
        os.makedirs(os.path.dirname(target), exist_ok=True)
        tmp = target + ".new"
        with open(tmp, "wb") as fh:
            fh.write(data)
        os.replace(tmp, target)
    return sorted(staged)


def relaunch():
    """Start a fresh copy of this script; the caller then exits."""
    exe = sys.executable
    if sys.platform == "win32":
        w = os.path.join(os.path.dirname(exe), "pythonw.exe")
        if os.path.exists(w):
            exe = w
    flags = 0x00000008 if sys.platform == "win32" else 0   # DETACHED_PROCESS
    subprocess.Popen([exe, os.path.join(HERE, "flexpad.py")], cwd=HERE,
                     creationflags=flags, close_fds=True)


def sync_registry_version():
    """Keep the Add/Remove Programs version in step after a self-update."""
    if sys.platform != "win32":
        return
    try:
        import winreg
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                             r"Software\Microsoft\Windows\CurrentVersion\Uninstall\flexpad",
                             0, winreg.KEY_READ | winreg.KEY_SET_VALUE)
        with key:
            current, _ = winreg.QueryValueEx(key, "DisplayVersion")
            if current != __version__:
                winreg.SetValueEx(key, "DisplayVersion", 0, winreg.REG_SZ, __version__)
    except OSError:
        pass                                # not installed, or no entry: fine


# ---------------------------------------------------------------- capture ---

def capture_slice(client, full=False):
    """Command lines that recreate the active slice's current setup.

    Basic: frequency, mode, antennas, filter - the memory-channel essentials.
    Full adds tuning step, AGC, noise tools, RF gain, DAX and TX power.
    Returns (label_suggestion, lines) or raises SequenceError.
    """
    idx = client.active_slice()
    if idx is None:
        raise SequenceError("no active slice to capture")
    s = client.slices[idx]
    g = s.get
    freq = g("RF_frequency", "?")
    mode = g("mode", "?")
    lines = [
        f"# captured from slice {g('index_letter', '?')} on {time.strftime('%Y-%m-%d %H:%M')}",
        f"slice tune {{slice}} {freq}",
        f"slice set {{slice}} mode={mode}",
        f"slice set {{slice}} rxant={g('rxant', 'ANT1')} txant={g('txant', 'ANT1')}",
        f"filt {{slice}} {g('filter_lo', '100')} {g('filter_hi', '2900')}",
    ]
    if full:
        lines += [
            f"slice set {{slice}} step={g('step', '100')}",
            f"slice set {{slice}} agc_mode={g('agc_mode', 'med')} agc_threshold={g('agc_threshold', '60')}",
            f"slice set {{slice}} nr={g('nr', '0')} nr_level={g('nr_level', '50')}",
            f"slice set {{slice}} nb={g('nb', '0')} nb_level={g('nb_level', '50')}",
            f"slice set {{slice}} wnb={g('wnb', '0')} wnb_level={g('wnb_level', '50')}",
            f"slice set {{slice}} anf={g('anf', '0')}",
            f"slice set {{slice}} rfgain={g('rfgain', '0')}",
            f"slice set {{slice}} dax={g('dax', '0')}",
        ]
        tx = client.transmit
        if "rfpower" in tx:
            lines.append(f"transmit set rfpower={tx['rfpower']}")
        if "tunepower" in tx:
            lines.append(f"transmit set tunepower={tx['tunepower']}")
    try:
        label = f"{float(freq):.3f} {mode}"
    except ValueError:
        label = mode
    return label, lines


# -------------------------------------------------------------- reference ---

REFERENCE_URL = "https://github.com/flexradio/smartsdr-api-docs/wiki"

# Snippets offered by "Insert example" in the button editor. Name -> lines.
EXAMPLES = [
    ("Tune + mode + antenna + filter", [
        "slice tune {slice} 144.200",
        "slice set {slice} mode=USB",
        "slice set {slice} rxant=XVTA txant=XVTA",
        "filt {slice} 150 2900",
    ]),
    ("Antenna only, active slice", ["slice set {slice} rxant=ANT1 txant=ANT1"]),
    ("Second slice on XVTB", [
        "slice create freq=432.100 ant=XVTB mode=USB",
        "wait 0.5",
        "filt {B} 150 2900",
    ]),
    ("Move TX to slice A", ["slice set {A} tx=1"]),
    ("Close slice B", ["slice remove {B}"]),
    ("Load a global profile", ['profile global load "NAME"']),
    ("RF power", ["transmit set rfpower=50"]),
    ("Noise reduction on", ["slice set {slice} nr=1 nr_level=50"]),
    ("Comment and pause", ["# what this step is for", "wait 0.5"]),
]

# Swatches offered in the button editor. Dark enough to carry white text and
# to sit well next to the green and slate the example buttons use.
PALETTE = [
    ("#2d6a4f", "green"), ("#264653", "slate"), ("#1d3557", "navy"),
    ("#5a189a", "purple"), ("#7f1d1d", "maroon"), ("#b45309", "amber"),
    ("#6b4f2a", "brown"), ("#4b5563", "gray"),
]

REFERENCE = """\
FLEXPAD QUICK REFERENCE          SmartSDR TCP/IP API, frequencies in MHz

Placeholders   {slice} active slice    {tx} transmit slice    {A}..{H} slice by letter
Other lines    wait 0.5  pauses        # starts a comment

TUNING AND MODE
  slice tune {slice} 14.250                  retune (transverter bands need an XVTR definition)
  slice set {slice} mode=USB                 USB LSB CW AM SAM FM NFM DFM DIGU DIGL RTTY
  slice set {slice} step=100                 tuning step, Hz
  slice lock {slice}                         lock tuning; slice unlock {slice} to release

ANTENNAS
  slice set {slice} rxant=XVTA txant=XVTA    ports on this radio are listed below
  slice set {slice} rxant=RX_A               receive-only port; TX stays put

FILTER AND DSP
  filt {slice} 150 2900                      RX filter edges, Hz; negative for LSB and CW-L
  slice set {slice} agc_mode=med             off slow med fast
  slice set {slice} agc_threshold=60
  slice set {slice} nr=1 nr_level=50         noise reduction
  slice set {slice} nb=1 nb_level=50         noise blanker
  slice set {slice} wnb=1 wnb_level=50       wideband noise blanker
  slice set {slice} anf=1                    auto notch

SLICES
  slice create freq=432.100 ant=XVTB mode=USB     open a new slice
  slice remove {B}                           close slice B
  slice set {A} tx=1                         make A the transmit slice
  slice set {B} active=1                     make B the active slice
  slice set {slice} audio_mute=1             mute (0 unmutes)
  slice set {slice} audio_level=50 audio_pan=50
  slice set {slice} dax=1                    DAX channel (0 = none)

TRANSMIT
  transmit set rfpower=50                    RF power, 0-100
  transmit set tunepower=10                  tune power
  transmit tune 1                            start tune (0 stops)
  xmit 1                                     MOX on (0 off); a button can key the radio
  atu start                                  tune the internal ATU
  atu bypass

PROFILES AND MEMORIES
  profile global load "NAME"                 global profile: antennas, slices, everything
  profile tx load "NAME"                     transmit profile
  profile mic load "NAME"                    mic profile
  memory apply 3                             SmartSDR memory by index (it won't set antennas)

INFO - harmless, the reply shows in the log
  ant list        slice list        info        version        profile global info

Full reference: the FlexRadio wiki (button below). Pages are named TCPIP-slice,
TCPIP-filt, TCPIP-transmit, TCPIP-profile, and so on.
"""


# ------------------------------------------------------------------- GUI ---

def make_dpi_aware():
    """Windows draws Tk blurry at 150% scaling unless the process opts in."""
    if sys.platform != "win32":
        return
    try:
        import ctypes
        ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except (AttributeError, OSError):
        pass


def key_to_binding(key):
    """'F1' -> '<F1>', 'ctrl+1' -> '<Control-Key-1>', 'alt+shift+x' -> ..."""
    if not key:
        return None
    parts = [p.strip() for p in key.replace("-", "+").split("+") if p.strip()]
    if not parts:
        return None
    mods = {"ctrl": "Control", "control": "Control", "alt": "Alt",
            "shift": "Shift", "cmd": "Command", "win": "Super"}
    out = [mods.get(p.lower(), p) for p in parts[:-1]]
    last = parts[-1]
    out.append("Key-" + last.lower() if len(last) == 1 else last)
    return "<" + "-".join(out) + ">"


class App:
    def __init__(self, root, cfg):
        import tkinter as tk
        from tkinter import ttk
        self.tk, self.ttk = tk, ttk
        self.root = root
        self.cfg = cfg
        self.events = queue.Queue()
        self.running = threading.Lock()
        self.bound_keys = []
        self.mono = ("Consolas", 10) if sys.platform == "win32" else "TkFixedFont"

        root.title("flexpad")
        self.apply_icon(root)
        root.minsize(420, 320)
        state = load_ui_state()
        if state.get("geometry"):
            root.geometry(state["geometry"])
        root.protocol("WM_DELETE_WINDOW", self.on_close)

        # -- status bar --
        top = ttk.Frame(root, padding=(8, 6))
        top.pack(fill="x")
        self.status_var = tk.StringVar(value="connecting...")
        ttk.Label(top, textvariable=self.status_var).pack(side="left")
        ttk.Button(top, text="Setup...", command=self.edit_setup).pack(side="right")
        ttk.Button(top, text="Knob...", command=self.edit_knob).pack(side="right", padx=(0, 4))
        ttk.Button(top, text="Reference", command=self.show_reference).pack(side="right", padx=(0, 4))
        ttk.Button(top, text="Reload", command=self.reload).pack(side="right", padx=(0, 4))
        ttk.Button(top, text="+ Button", command=self.add_button).pack(side="right", padx=(0, 4))
        self.reference_win = None

        # -- button grid --
        self.grid = ttk.Frame(root, padding=(8, 2))
        self.grid.pack(fill="x")

        # -- log + command line --
        body = ttk.Frame(root, padding=(8, 4))
        body.pack(fill="both", expand=True)
        self.logbox = tk.Text(body, height=10, wrap="none", font=self.mono,
                              state="disabled", background="#101418",
                              foreground="#d0d4d8", insertbackground="#d0d4d8")
        sb = ttk.Scrollbar(body, command=self.logbox.yview)
        self.logbox.configure(yscrollcommand=sb.set)
        self.logbox.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")
        self.logbox.tag_configure(">", foreground="#8ec5ff")
        self.logbox.tag_configure("<", foreground="#b8e986")
        self.logbox.tag_configure("S", foreground="#6c757d")
        self.logbox.tag_configure("!", foreground="#ffb347")
        self.logbox.tag_configure("E", foreground="#ff6b6b")
        self.logbox.tag_configure("K", foreground="#c9a0ff")

        bottom = ttk.Frame(root, padding=(8, 4))
        bottom.pack(fill="x")
        self.show_status = tk.BooleanVar(value=False)
        ttk.Checkbutton(bottom, text="status traffic", variable=self.show_status).pack(side="right")
        ttk.Button(bottom, text="Send", command=self.send_manual).pack(side="right", padx=(0, 8))
        self.cmd_var = tk.StringVar()
        entry = ttk.Entry(bottom, textvariable=self.cmd_var)
        entry.pack(side="left", fill="x", expand=True)
        entry.bind("<Return>", lambda e: self.send_manual())
        self.history, self.hist_pos = [], 0
        entry.bind("<Up>", lambda e: self.history_step(-1))
        entry.bind("<Down>", lambda e: self.history_step(1))

        self.build_grid()
        self.client = self.new_client()
        self.knob = None
        self.knob_state = "off"
        self.start_knob()
        self.update_available = None
        sync_registry_version()
        if cfg.get("auto_check_updates"):
            root.after(3000, lambda: self.check_updates(manual=False))
        root.after(100, self.pump)

    def apply_icon(self, root):
        """Window and taskbar icon. Missing files just mean the Tk default."""
        tk = self.tk
        ico = os.path.join(HERE, "assets", "flexpad.ico")
        png = os.path.join(HERE, "assets", "flexpad.png")
        try:
            if sys.platform == "win32" and os.path.exists(ico):
                root.iconbitmap(default=ico)
            elif os.path.exists(png):
                self._icon = tk.PhotoImage(file=png)     # keep a reference or Tk drops it
                root.iconphoto(True, self._icon)
        except tk.TclError as err:
            log.warning("icon not applied: %s", err)

    def new_client(self):
        client = FlexClient(self.cfg["flex_host"], self.cfg["flex_port"],
                            on_traffic=lambda d, t: self.events.put(("traffic", d, t)),
                            on_state=lambda: self.events.put(("state",)))
        client.start()
        return client

    # -- FlexControl knob --

    def start_knob(self):
        if self.knob:
            self.knob.stop()
            self.knob = None
        fc = knob_config(self.cfg)
        if not fc["enabled"]:
            self.knob_state = "off"
            self.events.put(("state",))
            return
        self.knob = FlexControl(fc["port"], fc["invert"],
                                on_turn=self.knob_turn, on_button=self.knob_button,
                                on_status=lambda t: self.events.put(("knob", t)))
        self.knob.start()

    def knob_turn(self, delta):
        """Runs on the knob thread: retune the active slice by delta steps."""
        c = self.client
        idx = c.active_slice()
        if idx is None or not c.connected:
            return
        s = c.slices[idx]
        try:
            hz = round(float(s["RF_frequency"]) * 1_000_000)
            step = int(float(s.get("step", 100)))
        except (KeyError, ValueError):
            return
        new = max(0, hz + delta * step)
        # Update the cache now so the next burst of ticks builds on this one
        # instead of on a status echo that may not have arrived yet.
        s["RF_frequency"] = format_mhz(new)
        try:
            code, text = c.send(f"slice tune {idx} {format_mhz(new)}")
        except NotConnected:
            return
        if code != 0:
            self.events.put(("traffic", "E", f"knob tune refused: {text}"))
        else:
            self.events.put(("traffic", "K", f"knob {delta:+d} x {step} Hz -> {format_mhz(new)}"))

    def knob_button(self, code):
        """Runs on the knob thread: dispatch a press to its binding."""
        name = KNOB_EVENT_NAMES.get(code, code)
        binding = knob_config(self.cfg)["bindings"].get(code, "").strip()
        if not binding:
            self.events.put(("traffic", "K", f"{name}: not bound (Knob... to assign)"))
            return
        if binding.startswith("@"):
            self.events.put(("traffic", "!", f"{name}: {binding}"))
            try:
                self.knob_action(binding)
            except NotConnected as err:
                self.events.put(("traffic", "E", str(err)))
            return
        for i, b in enumerate(self.cfg["buttons"]):
            if b["label"] == binding:
                self.events.put(("fire", i))
                return
        self.events.put(("traffic", "E", f"{name}: no button labeled '{binding}'"))

    def knob_action(self, action):
        c = self.client
        idx = c.active_slice()
        if idx is None:
            raise NotConnected("no active slice")
        s = c.slices[idx]
        if action == "@step":
            steps = [int(x) for x in knob_config(self.cfg)["steps"]] or [100]
            cur = int(float(s.get("step", 0)))
            nxt = next((x for x in steps if x > cur), steps[0])
            c.send(f"slice set {idx} step={nxt}")
            s["step"] = str(nxt)
            self.events.put(("traffic", "!", f"tuning step {nxt} Hz"))
        elif action == "@next-slice":
            live = sorted(c.live_slices(), key=int)
            if len(live) < 2:
                self.events.put(("traffic", "K", "only one slice open"))
                return
            nxt = live[(live.index(idx) + 1) % len(live)]
            c.send(f"slice set {nxt} active=1")
        elif action == "@mute":
            c.send(f"slice set {idx} audio_mute={0 if s.get('audio_mute') == '1' else 1}")
        elif action == "@tx":
            c.send(f"slice set {idx} tx=1")
        else:
            self.events.put(("traffic", "E", f"unknown knob action {action}"))

    def edit_knob(self):
        tk, ttk = self.tk, self.ttk
        fc = knob_config(self.cfg)
        win = tk.Toplevel(self.root)
        win.title("FlexControl knob")
        win.transient(self.root)
        win.grab_set()
        frm = ttk.Frame(win, padding=10)
        frm.pack(fill="both", expand=True)
        frm.columnconfigure(1, weight=1)

        en_var = tk.BooleanVar(value=bool(fc["enabled"]))
        port_var = tk.StringVar(value=fc["port"])
        inv_var = tk.BooleanVar(value=bool(fc["invert"]))
        steps_var = tk.StringVar(value=", ".join(str(x) for x in fc["steps"]))
        found = find_flexcontrol()
        ttk.Checkbutton(frm, text="Use the FlexControl knob", variable=en_var).grid(
            row=0, column=0, columnspan=2, sticky="w", pady=(0, 6))
        ttk.Label(frm, text="Port (blank = find by USB id)").grid(row=1, column=0, sticky="w", pady=2, padx=(0, 8))
        ttk.Entry(frm, textvariable=port_var).grid(row=1, column=1, sticky="ew", pady=2)
        ttk.Label(frm, text=f"detected: {found or 'none'}", foreground="#6c757d").grid(
            row=1, column=2, sticky="w", padx=(6, 0))
        ttk.Checkbutton(frm, text="Invert direction", variable=inv_var).grid(
            row=2, column=0, columnspan=2, sticky="w", pady=2)
        ttk.Label(frm, text="Step sizes for @step, Hz").grid(row=3, column=0, sticky="w", pady=2, padx=(0, 8))
        ttk.Entry(frm, textvariable=steps_var).grid(row=3, column=1, sticky="ew", pady=2)

        ttk.Label(frm, text="Buttons: pick a built-in action or one of your flexpad buttons").grid(
            row=4, column=0, columnspan=3, sticky="w", pady=(10, 4))
        choices = [""] + KNOB_ACTIONS + [b["label"] for b in self.cfg["buttons"]]
        bind_vars = {}
        for n, code in enumerate(KNOB_EVENTS):
            r = 5 + n
            ttk.Label(frm, text=KNOB_EVENT_NAMES[code]).grid(row=r, column=0, sticky="w", padx=(0, 8))
            v = tk.StringVar(value=fc["bindings"].get(code, ""))
            bind_vars[code] = v
            ttk.Combobox(frm, textvariable=v, values=choices).grid(row=r, column=1, columnspan=2,
                                                                   sticky="ew", pady=1)
        hint = ("@step cycles the tuning step   @next-slice moves the active flag   "
                "@mute toggles audio   @tx makes the active slice transmit")
        ttk.Label(frm, text=hint, foreground="#6c757d").grid(row=5 + len(KNOB_EVENTS), column=0,
                                                             columnspan=3, sticky="w", pady=(8, 8))

        def save():
            fc["enabled"] = en_var.get()
            fc["port"] = port_var.get().strip()
            fc["invert"] = inv_var.get()
            try:
                fc["steps"] = [int(x) for x in steps_var.get().replace(",", " ").split()] or [100]
            except ValueError:
                fc["steps"] = [10, 100, 1000, 10000]
            fc["bindings"] = {code: v.get().strip() for code, v in bind_vars.items()}
            win.destroy()
            save_config(self.cfg)
            self.start_knob()
            self.log_line("!", "knob settings saved")
        btns = ttk.Frame(frm)
        btns.grid(row=6 + len(KNOB_EVENTS), column=0, columnspan=3, sticky="e")
        ttk.Button(btns, text="Cancel", command=win.destroy).pack(side="right")
        ttk.Button(btns, text="Save", command=save).pack(side="right", padx=(0, 6))
        self.place_over(win)

    # -- grid --

    def build_grid(self):
        tk = self.tk
        for w in self.grid.winfo_children():
            w.destroy()
        for seq in self.bound_keys:
            self.root.unbind_all(seq)
        self.bound_keys = []
        cols = max(1, int(self.cfg.get("columns", 4)))
        for c in range(cols):
            self.grid.columnconfigure(c, weight=1, uniform="btn")
        for i, b in enumerate(self.cfg["buttons"]):
            text = b["label"]
            if b.get("key"):
                text += f"\n[{b['key']}]"
            btn = tk.Button(self.grid, text=text, width=12, height=2,
                            relief="raised", bd=1, cursor="hand2",
                            command=lambda i=i: self.fire(i))
            if b.get("color"):
                btn.configure(background=b["color"], foreground="white",
                              activebackground=b["color"], activeforeground="white")
            btn.grid(row=i // cols, column=i % cols, sticky="nsew", padx=3, pady=3)
            btn.bind("<Button-3>", lambda e, i=i: self.context_menu(e, i))
            if sys.platform == "darwin":
                btn.bind("<Button-2>", lambda e, i=i: self.context_menu(e, i))
                btn.bind("<Control-Button-1>", lambda e, i=i: self.context_menu(e, i))
            seq = key_to_binding(b.get("key"))
            if seq:
                try:
                    self.root.bind_all(seq, lambda e, i=i: self.fire(i))
                    self.bound_keys.append(seq)
                except tk.TclError:
                    self.log_line("E", f"bad key '{b['key']}' on button '{b['label']}'")

    def context_menu(self, event, i):
        tk = self.tk
        m = tk.Menu(self.root, tearoff=0)
        m.add_command(label="Edit...", command=lambda: self.edit_button(i))
        m.add_command(label="Duplicate", command=lambda: self.duplicate_button(i))
        m.add_separator()
        m.add_command(label="Move earlier", command=lambda: self.move_button(i, -1))
        m.add_command(label="Move later", command=lambda: self.move_button(i, 1))
        m.add_separator()
        m.add_command(label="Delete", command=lambda: self.delete_button(i))
        m.tk_popup(event.x_root, event.y_root)

    def fire(self, i):
        b = self.cfg["buttons"][i]
        if not self.running.acquire(blocking=False):
            self.log_line("E", "a sequence is still running")
            return
        self.log_line("!", f"--- {b['label']} ---")

        def work():
            try:
                ok = run_sequence(self.client, b["commands"],
                                  self.cfg.get("stop_on_error", True),
                                  report=lambda t: self.events.put(("traffic", "E", t)))
                self.events.put(("traffic", "!" if ok else "E",
                                 f"--- {b['label']}: {'done' if ok else 'failed'} ---"))
            finally:
                self.running.release()
        threading.Thread(target=work, daemon=True).start()

    # -- editing --

    def add_button(self):
        self.cfg["buttons"].append({"label": "New", "commands": []})
        self.edit_button(len(self.cfg["buttons"]) - 1, new=True)

    def duplicate_button(self, i):
        b = json.loads(json.dumps(self.cfg["buttons"][i]))
        b.pop("key", None)
        self.cfg["buttons"].insert(i + 1, b)
        self.persist()

    def move_button(self, i, delta):
        j = i + delta
        if 0 <= j < len(self.cfg["buttons"]):
            bs = self.cfg["buttons"]
            bs[i], bs[j] = bs[j], bs[i]
            self.persist()

    def delete_button(self, i):
        from tkinter import messagebox
        b = self.cfg["buttons"][i]
        if messagebox.askyesno("Delete button", f"Delete '{b['label']}'?", parent=self.root):
            del self.cfg["buttons"][i]
            self.persist()

    def persist(self):
        save_config(self.cfg)
        self.build_grid()

    def place_over(self, win):
        """Center a dialog on the main window. Tk otherwise parks it at 0,0."""
        win.withdraw()
        win.update_idletasks()
        w, h = win.winfo_reqwidth(), win.winfo_reqheight()
        rx, ry = self.root.winfo_rootx(), self.root.winfo_rooty()
        rw, rh = self.root.winfo_width(), self.root.winfo_height()
        x = rx + (rw - w) // 2
        y = ry + (rh - h) // 2
        # Keep it on screen if the main window sits near an edge.
        x = max(0, min(x, self.root.winfo_screenwidth() - w))
        y = max(0, min(y, self.root.winfo_screenheight() - h))
        win.geometry(f"+{x}+{y}")
        win.deiconify()

    def edit_button(self, i, new=False):
        tk, ttk = self.tk, self.ttk
        b = self.cfg["buttons"][i]
        win = tk.Toplevel(self.root)
        win.title("Edit button")
        win.transient(self.root)
        win.grab_set()
        frm = ttk.Frame(win, padding=10)
        frm.pack(fill="both", expand=True)
        frm.columnconfigure(1, weight=1)

        label_var = tk.StringVar(value=b.get("label", ""))
        key_var = tk.StringVar(value=b.get("key", ""))
        color_var = tk.StringVar(value=b.get("color", ""))
        ttk.Label(frm, text="Label").grid(row=0, column=0, sticky="w", pady=2)
        ttk.Entry(frm, textvariable=label_var).grid(row=0, column=1, sticky="ew", pady=2)
        ttk.Label(frm, text="Hotkey").grid(row=1, column=0, sticky="w", pady=2)
        ttk.Entry(frm, textvariable=key_var).grid(row=1, column=1, sticky="ew", pady=2)
        ttk.Label(frm, text="Color").grid(row=2, column=0, sticky="w", pady=2)
        crow = ttk.Frame(frm)
        crow.grid(row=2, column=1, sticky="ew", pady=2)
        preview = tk.Label(crow, text=" Aa ", width=4, relief="raised", bd=1)
        preview.pack(side="left", padx=(0, 6))
        plain_bg, plain_fg = preview.cget("background"), preview.cget("foreground")
        for hex_, name in PALETTE:
            tk.Button(crow, width=2, background=hex_, activebackground=hex_,
                      relief="flat", bd=0, cursor="hand2",
                      command=lambda h=hex_: color_var.set(h)).pack(side="left", padx=1)
        tk.Button(crow, text="x", width=2, relief="flat", bd=0, cursor="hand2",
                  command=lambda: color_var.set("")).pack(side="left", padx=(1, 6))

        def pick():
            from tkinter import colorchooser
            _, hex_ = colorchooser.askcolor(color=color_var.get() or "#4b5563",
                                            parent=win, title="Button color")
            if hex_:
                color_var.set(hex_)
        ttk.Button(crow, text="More...", command=pick).pack(side="left", padx=(0, 6))
        ttk.Entry(crow, textvariable=color_var, width=10).pack(side="left", fill="x", expand=True)

        def refresh_preview(*_):
            c = color_var.get().strip()
            try:
                preview.configure(background=c or plain_bg, foreground="white" if c else plain_fg)
            except tk.TclError:
                preview.configure(background="white", foreground="red")   # not a color
        color_var.trace_add("write", refresh_preview)
        refresh_preview()
        row3 = ttk.Frame(frm)
        row3.grid(row=3, column=0, columnspan=2, sticky="ew", pady=(8, 2))
        ttk.Label(row3, text="Commands, one per line").pack(side="left")
        ttk.Button(row3, text="Reference", command=self.show_reference).pack(side="right")
        insert = ttk.Menubutton(row3, text="Insert example")
        insert.pack(side="right", padx=(0, 4))
        text = tk.Text(frm, width=76, height=12, font=self.mono, undo=True)
        text.grid(row=4, column=0, columnspan=2, sticky="nsew")
        frm.rowconfigure(4, weight=1)
        text.insert("1.0", "\n".join(b.get("commands", [])))

        def insert_lines(lines):
            # Append on a fresh line at the end, so the snippet never splits
            # a line the user is in the middle of typing.
            current = text.get("1.0", "end-1c")
            if current and not current.endswith("\n"):
                text.insert("end", "\n")
            text.insert("end", "\n".join(lines) + "\n")
            text.see("end")
            text.focus_set()
        menu = tk.Menu(insert, tearoff=0)
        for name, lines in EXAMPLES:
            menu.add_command(label=name, command=lambda l=lines: insert_lines(l))
        insert.configure(menu=menu)

        # Capture: set the radio up the way you want it, then take a snapshot
        # of the active slice as commands. This is what a memory channel
        # would store, plus the antennas SmartSDR's memories leave out.
        capture = ttk.Menubutton(row3, text="Capture slice")
        capture.pack(side="right", padx=(0, 4))

        def do_capture(full):
            try:
                suggested, lines = capture_slice(self.client, full=full)
            except SequenceError as err:
                self.log_line("E", str(err))
                return
            insert_lines(lines)
            if label_var.get().strip() in ("", "New"):
                label_var.set(suggested)
        cmenu = tk.Menu(capture, tearoff=0)
        cmenu.add_command(label="Basic: frequency, mode, antennas, filter",
                          command=lambda: do_capture(False))
        cmenu.add_command(label="Full: basic + step, AGC, NR/NB/ANF, RF gain, DAX, TX power",
                          command=lambda: do_capture(True))
        capture.configure(menu=cmenu)
        hint = ("{slice} = active slice   {tx} = transmit slice   {A}..{H} = slice by letter\n"
                "wait 0.5 pauses   # starts a comment   hotkey: F1, ctrl+1, alt+shift+x")
        ttk.Label(frm, text=hint, foreground="#6c757d").grid(row=5, column=0, columnspan=2,
                                                             sticky="w", pady=(6, 8))
        btns = ttk.Frame(frm)
        btns.grid(row=6, column=0, columnspan=2, sticky="e")

        def cancel():
            if new:
                del self.cfg["buttons"][i]
            win.destroy()

        def save():
            b["label"] = label_var.get().strip() or "?"
            for k, var in (("key", key_var), ("color", color_var)):
                v = var.get().strip()
                if v:
                    b[k] = v
                else:
                    b.pop(k, None)
            b["commands"] = [ln.rstrip() for ln in text.get("1.0", "end").splitlines()]
            while b["commands"] and not b["commands"][-1]:
                b["commands"].pop()
            win.destroy()
            self.persist()

        ttk.Button(btns, text="Cancel", command=cancel).pack(side="right")
        ttk.Button(btns, text="Save", command=save).pack(side="right", padx=(0, 6))
        win.protocol("WM_DELETE_WINDOW", cancel)
        win.bind("<Escape>", lambda e: cancel())
        self.place_over(win)
        text.focus_set()

    def edit_setup(self):
        tk, ttk = self.tk, self.ttk
        win = tk.Toplevel(self.root)
        win.title("Setup")
        win.transient(self.root)
        win.grab_set()
        frm = ttk.Frame(win, padding=10)
        frm.pack(fill="both", expand=True)
        frm.columnconfigure(1, weight=1)
        host_var = tk.StringVar(value=self.cfg.get("flex_host", ""))
        port_var = tk.StringVar(value=str(self.cfg.get("flex_port", 4992)))
        cols_var = tk.StringVar(value=str(self.cfg.get("columns", 4)))
        stop_var = tk.BooleanVar(value=bool(self.cfg.get("stop_on_error", True)))
        rows = (("Radio address (blank = discover)", host_var),
                ("Port", port_var), ("Button columns", cols_var))
        for r, (lbl, var) in enumerate(rows):
            ttk.Label(frm, text=lbl).grid(row=r, column=0, sticky="w", pady=2, padx=(0, 8))
            ttk.Entry(frm, textvariable=var).grid(row=r, column=1, sticky="ew", pady=2)
        ttk.Checkbutton(frm, text="Stop a sequence at the first error",
                        variable=stop_var).grid(row=3, column=0, columnspan=2, sticky="w", pady=6)

        # -- Updates --
        upd_var = tk.BooleanVar(value=bool(self.cfg.get("auto_check_updates", False)))
        ttk.Separator(frm).grid(row=5, column=0, columnspan=3, sticky="ew", pady=(10, 6))
        urow = ttk.Frame(frm)
        urow.grid(row=6, column=0, columnspan=3, sticky="ew")
        ttk.Label(urow, text=f"flexpad {__version__}").pack(side="left")
        ttk.Button(urow, text="Check for updates",
                   command=lambda: self.check_updates(manual=True)).pack(side="left", padx=(10, 0))
        ttk.Button(urow, text="Releases page",
                   command=lambda: webbrowser.open(RELEASES_URL)).pack(side="left", padx=(6, 0))
        ttk.Checkbutton(frm, text="Check for updates when flexpad starts (notifies only; installing is always a click)",
                        variable=upd_var).grid(row=7, column=0, columnspan=3, sticky="w", pady=(4, 0))
        ttk.Label(frm, text=f"Config and log: {DATA_DIR}", foreground="#6c757d").grid(
            row=9, column=0, columnspan=3, sticky="w", pady=(8, 0))

        def find():
            radios = discover()
            if radios:
                r = radios[0]
                host_var.set(r["ip"])
                self.log_line("!", f"discovered {r.get('model')} {r.get('nickname', '')} at {r['ip']}")
            else:
                self.log_line("E", "no radio answered discovery")
        ttk.Button(frm, text="Discover", command=find).grid(row=0, column=2, padx=(6, 0))

        def save():
            try:
                port, cols = int(port_var.get()), int(cols_var.get())
            except ValueError:
                return
            self.cfg["flex_host"] = host_var.get().strip()
            self.cfg["flex_port"] = port
            self.cfg["columns"] = max(1, cols)
            self.cfg["stop_on_error"] = stop_var.get()
            self.cfg["auto_check_updates"] = upd_var.get()
            win.destroy()
            save_config(self.cfg)
            self.reload()
        btns = ttk.Frame(frm)
        btns.grid(row=10, column=0, columnspan=3, sticky="e", pady=(8, 0))
        ttk.Button(btns, text="Cancel", command=win.destroy).pack(side="right")
        ttk.Button(btns, text="Save", command=save).pack(side="right", padx=(0, 6))
        self.place_over(win)

    # -- updates --

    def check_updates(self, manual):
        def work():
            try:
                info = check_latest()
            except UpdateError as err:
                self.events.put(("update", "error", str(err), manual))
                return
            self.events.put(("update", "result", info, manual))
        threading.Thread(target=work, daemon=True).start()

    def on_update_result(self, kind, payload, manual):
        from tkinter import messagebox
        if kind == "error":
            self.log_line("E", f"update check: {payload}")
            if manual:
                messagebox.showerror("flexpad", f"Update check failed:\n{payload}", parent=self.root)
            return
        info = payload
        if not info["newer"]:
            self.log_line("!", f"up to date (v{__version__}, latest {info['tag']})")
            if manual:
                messagebox.showinfo("flexpad", f"flexpad {__version__} is the latest version.",
                                    parent=self.root)
            return
        self.update_available = info["version"]
        self.refresh_status()
        self.log_line("!", f"update available: v{info['version']} (you have {__version__}) - Setup...")
        if not manual:
            return
        if not messagebox.askyesno("flexpad",
                                   f"flexpad v{info['version']} is available (you have {__version__}).\n\n"
                                   "Download and install it now? Your buttons and settings are kept.",
                                   parent=self.root):
            return
        self.log_line("!", f"downloading v{info['version']}...")

        def install():
            try:
                files = install_update(info)
            except UpdateError as err:
                self.events.put(("update", "install_failed", str(err), True))
                return
            self.events.put(("update", "installed", (info["version"], files), True))
        threading.Thread(target=install, daemon=True).start()

    def on_update_installed(self, kind, payload):
        from tkinter import messagebox
        if kind == "install_failed":
            self.log_line("E", f"update failed: {payload}")
            messagebox.showerror("flexpad", f"Update failed. The current version is still in place.\n\n{payload}",
                                 parent=self.root)
            return
        version, files = payload
        self.log_line("!", f"installed v{version}: {', '.join(files)}")
        if messagebox.askyesno("flexpad", f"flexpad v{version} is installed.\n\nRestart now to use it?",
                               parent=self.root):
            relaunch()
            self.on_close()

    def show_reference(self):
        tk, ttk = self.tk, self.ttk
        if self.reference_win and self.reference_win.winfo_exists():
            self.reference_win.lift()
            return
        win = tk.Toplevel(self.root)
        self.reference_win = win
        win.title("flexpad reference")
        frm = ttk.Frame(win, padding=8)
        frm.pack(fill="both", expand=True)
        text = tk.Text(frm, width=100, height=40, font=self.mono, wrap="word")
        sb = ttk.Scrollbar(frm, command=text.yview)
        text.configure(yscrollcommand=sb.set)
        text.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")
        text.insert("1.0", REFERENCE)
        ports_line = "Antenna ports on this radio: "
        text.insert("end", "\n" + ports_line + ("asking..." if self.client.connected
                                                 else "(not connected)") + "\n")
        text.configure(state="disabled")
        bar = ttk.Frame(win, padding=(8, 0, 8, 8))
        bar.pack(fill="x")
        ttk.Button(bar, text="Open the FlexRadio API wiki",
                   command=lambda: webbrowser.open(REFERENCE_URL)).pack(side="left")
        ttk.Button(bar, text="Close", command=win.destroy).pack(side="right")
        self.place_over(win)

        if self.client.connected:
            def ask():
                try:
                    code, reply = self.client.send("ant list")
                except NotConnected:
                    code, reply = -1, ""
                ports = reply.replace(",", "  ") if code == 0 else "(no answer)"

                def show():
                    if not win.winfo_exists():
                        return
                    text.configure(state="normal")
                    idx = text.search(ports_line, "1.0")
                    if idx:
                        text.delete(idx, f"{idx} lineend")
                        text.insert(idx, ports_line + ports)
                    text.configure(state="disabled")
                self.root.after(0, show)
            threading.Thread(target=ask, daemon=True).start()

    def reload(self):
        try:
            self.cfg = load_config()
        except (OSError, json.JSONDecodeError) as err:
            self.log_line("E", f"config.json: {err}")
            return
        self.build_grid()
        target = (self.cfg["flex_host"], self.cfg["flex_port"])
        if target != (self.client.host, self.client.port) or not self.client.connected:
            self.client.stop()
            self.client = self.new_client()
        self.start_knob()
        self.log_line("!", "config reloaded")

    # -- manual command line --

    def send_manual(self):
        cmd = self.cmd_var.get().strip()
        if not cmd:
            return
        self.history.append(cmd)
        self.hist_pos = len(self.history)
        self.cmd_var.set("")

        def work():
            try:
                code, text = self.client.send(substitute(self.client, cmd))
                if code != 0:
                    self.events.put(("traffic", "E", f"error 0x{code:X} {text}"))
            except (SequenceError, NotConnected) as err:
                self.events.put(("traffic", "E", str(err)))
        threading.Thread(target=work, daemon=True).start()

    def history_step(self, delta):
        if not self.history:
            return
        self.hist_pos = max(0, min(len(self.history), self.hist_pos + delta))
        self.cmd_var.set(self.history[self.hist_pos] if self.hist_pos < len(self.history) else "")

    # -- event pump --

    def pump(self):
        try:
            while True:
                ev = self.events.get_nowait()
                if ev[0] == "traffic":
                    _, d, t = ev
                    if d in ("S", "K") and not self.show_status.get():
                        continue
                    self.log_line(d, t)
                elif ev[0] == "state":
                    self.refresh_status()
                elif ev[0] == "knob":
                    self.knob_state = ev[1]
                    self.log_line("!", f"knob: {ev[1]}")
                    self.refresh_status()
                elif ev[0] == "fire":
                    self.fire(ev[1])
                elif ev[0] == "update":
                    _, kind, payload, manual = ev
                    if kind in ("installed", "install_failed"):
                        self.on_update_installed(kind, payload)
                    else:
                        self.on_update_result(kind, payload, manual)
        except queue.Empty:
            pass
        self.root.after(100, self.pump)

    def refresh_status(self):
        c = self.client
        if not c.connected:
            self.status_var.set(f"not connected  {c.error or ''}".rstrip())
            return
        idx = c.active_slice()
        if idx is None:
            self.status_var.set(f"{c.host}  no active slice")
            return
        s = c.slices.get(idx, {})
        self.status_var.set(
            f"{c.host}  slice {s.get('index_letter', '?')}  "
            f"{s.get('RF_frequency', '?')} MHz  {s.get('mode', '?')}  "
            f"rx {s.get('rxant', '?')}  tx {s.get('txant', '?')}  "
            f"step {s.get('step', '?')}  knob {self.knob_state}"
            + (f"  |  update v{self.update_available} available" if self.update_available else ""))

    def log_line(self, tag, text):
        self.logbox.configure(state="normal")
        self.logbox.insert("end", f"{time.strftime('%H:%M:%S')} {text}\n", tag)
        # Keep the pane bounded; it is a live view, flexpad.log has history.
        lines = int(self.logbox.index("end-1c").split(".")[0])
        if lines > 2000:
            self.logbox.delete("1.0", f"{lines - 1500}.0")
        self.logbox.configure(state="disabled")
        self.logbox.see("end")
        if tag in ("!", "E"):
            log.info(text)

    def on_close(self):
        save_ui_state(geometry=self.root.geometry())
        if self.knob:
            self.knob.stop()
        self.client.stop()
        self.root.destroy()


def run_gui(cfg):
    make_dpi_aware()
    import tkinter as tk
    root = tk.Tk()
    App(root, cfg)
    root.mainloop()


# ------------------------------------------------------------------- main ---

def setup_logging(to_console):
    handlers = [logging.handlers.RotatingFileHandler(LOG_PATH, maxBytes=500_000,
                                                     backupCount=2, encoding="utf-8")]
    if to_console:
        handlers.append(logging.StreamHandler(sys.stderr))
    logging.basicConfig(level=logging.INFO, handlers=handlers,
                        format="%(asctime)s %(levelname)s %(message)s")


def headless_client(cfg, echo):
    """Connect once for --send/--run and wait for the slice table to arrive."""
    def traffic(d, t):
        if echo and d != "S":
            print(f"{d} {t}")
    client = FlexClient(cfg["flex_host"], cfg["flex_port"], on_traffic=traffic)
    client.start()
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline and not client.connected:
        time.sleep(0.05)
    if not client.connected:
        sys.exit(f"could not connect: {client.error or 'timeout'}")
    # Slice status streams in right after 'sub slice all'; give it a beat so
    # {slice} placeholders resolve on the first command.
    time.sleep(0.4)
    return client


def main():
    ap = argparse.ArgumentParser(description="Programmable buttons for a FlexRadio.")
    ap.add_argument("--discover", action="store_true", help="list radios on the LAN and exit")
    ap.add_argument("--send", metavar="CMD", help="send one API command and print the reply")
    ap.add_argument("--run", metavar="LABEL", help="fire the button with this label, headless")
    ap.add_argument("--quiet", action="store_true", help="with --run: print only errors")
    ap.add_argument("--knob", action="store_true",
                    help="print FlexControl events without touching the radio (Ctrl+C to stop)")
    ap.add_argument("--update", action="store_true",
                    help="check GitHub for a newer release and install it in place")
    args = ap.parse_args()
    setup_logging(to_console=bool(args.discover or args.send or args.run or args.knob or args.update))

    if args.update:
        print(f"installed: v{__version__}")
        try:
            info = check_latest()
            print(f"latest:    {info['tag']}")
            if not info["newer"]:
                print("already up to date.")
                return
            print(f"downloading {info['tag']} ...")
            files = install_update(info)
        except UpdateError as err:
            sys.exit(str(err))
        print(f"updated to {info['tag']}: {', '.join(files)}")
        print("restart flexpad to use it.")
        return

    if args.knob:
        fc = knob_config(load_config())
        print(f"FlexControl: {fc['port'] or find_flexcontrol() or 'not found'}  (Ctrl+C to stop)")
        knob = FlexControl(fc["port"], fc["invert"],
                           on_turn=lambda d: print(f"turn {d:+d}"),
                           on_button=lambda c: print(f"button {c}  ({KNOB_EVENT_NAMES[c]})"),
                           on_status=lambda t: print(f"status: {t}"))
        knob.start()
        try:
            while knob.is_alive():
                time.sleep(0.2)
        except KeyboardInterrupt:
            pass
        knob.stop()
        return

    if args.discover:
        radios = discover()
        if not radios:
            sys.exit("no radio answered within 4 s")
        for r in radios:
            print(f"{r['ip']:15}  {r.get('model', '?'):10}  {r.get('nickname', '')}  "
                  f"v{r.get('version', '?')}  {r.get('status', '')}")
        return

    cfg = load_config()
    if args.send:
        client = headless_client(cfg, echo=True)
        code, text = client.send(substitute(client, args.send))
        client.stop()
        sys.exit(0 if code == 0 else 1)
    if args.run:
        match = [b for b in cfg["buttons"] if b["label"] == args.run]
        if not match:
            sys.exit(f"no button labeled '{args.run}'")
        client = headless_client(cfg, echo=not args.quiet)
        ok = run_sequence(client, match[0]["commands"], cfg.get("stop_on_error", True),
                          report=lambda t: print(t, file=sys.stderr))
        client.stop()
        sys.exit(0 if ok else 1)
    run_gui(cfg)


if __name__ == "__main__":
    main()
