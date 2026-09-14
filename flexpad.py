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

Standard library only. Buttons live in config.json next to this file; edit
them in the app (right-click a button) or in the file, then Reload.
"""

import argparse
import json
import logging
import logging.handlers
import os
import queue
import re
import socket
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(HERE, "config.json")
EXAMPLE_PATH = os.path.join(HERE, "config.example.json")
UI_STATE_PATH = os.path.join(HERE, "ui_state.json")
LOG_PATH = os.path.join(HERE, "flexpad.log")

DISCOVERY_PORT = 4992
COMMAND_TIMEOUT = 5.0
RECONNECT_SECONDS = 5.0

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
    cfg.setdefault("buttons", [])
    for b in cfg["buttons"]:
        b.setdefault("label", "?")
        b.setdefault("commands", [])
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
        ttk.Button(top, text="Reload", command=self.reload).pack(side="right", padx=(0, 4))
        ttk.Button(top, text="+ Button", command=self.add_button).pack(side="right", padx=(0, 4))

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
        root.after(100, self.pump)

    def new_client(self):
        client = FlexClient(self.cfg["flex_host"], self.cfg["flex_port"],
                            on_traffic=lambda d, t: self.events.put(("traffic", d, t)),
                            on_state=lambda: self.events.put(("state",)))
        client.start()
        return client

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
        ttk.Entry(frm, textvariable=color_var).grid(row=2, column=1, sticky="ew", pady=2)
        ttk.Label(frm, text="Commands, one per line").grid(row=3, column=0, columnspan=2,
                                                            sticky="w", pady=(8, 2))
        text = tk.Text(frm, width=60, height=12, font=self.mono, undo=True)
        text.grid(row=4, column=0, columnspan=2, sticky="nsew")
        frm.rowconfigure(4, weight=1)
        text.insert("1.0", "\n".join(b.get("commands", [])))
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
            win.destroy()
            save_config(self.cfg)
            self.reload()
        btns = ttk.Frame(frm)
        btns.grid(row=4, column=0, columnspan=3, sticky="e", pady=(8, 0))
        ttk.Button(btns, text="Cancel", command=win.destroy).pack(side="right")
        ttk.Button(btns, text="Save", command=save).pack(side="right", padx=(0, 6))

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
                    if d == "S" and not self.show_status.get():
                        continue
                    self.log_line(d, t)
                elif ev[0] == "state":
                    self.refresh_status()
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
            f"rx {s.get('rxant', '?')}  tx {s.get('txant', '?')}")

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
    args = ap.parse_args()
    setup_logging(to_console=bool(args.discover or args.send or args.run))

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
