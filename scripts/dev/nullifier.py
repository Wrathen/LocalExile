r"""Launch one client of a live.ps1 session through Frida, so that it is cheap and never in another client's way.

live.ps1 -Session S starts every session client this way (Agents/docs/dev/live-testing.md, "Several stacks and
clients"); it prepares the session folder and the pending record, then runs, detached:

    python scripts/dev/nullifier.py --cwd data\clients\S --record data\clients\S.json --patch-port P --login-port L \
        [--renderer null --trim-seconds 10 | --renderer configured]

It spawns Client/Game.patched.exe suspended in that folder, puts it in a job object that kills it when this launcher
ends however it ends, loads scripts/dev/nullifier.js (always: session-local names for the client's two-copies mutexes
so it never takes one of the two Global\PoERunMutexA/B slots, and a window that never takes focus; with
--renderer null: the engine's Null device; optionally a frame-sleep cap and a crash log), applies below-normal
priority and EcoQoS through the one process handle it opened at the spawn, resumes the client, waits (30 s at most)
for the agent's report that the client took its session-local mutex name (else it closes the client and fails),
merges {pid, started, launcher, launcherStarted, ...} into the record (atomically), and waits until the client exits. The
client is also terminated when the Frida session detaches: its hooks exist only while it does. Drive the client with
client_control.py --session S; stop it with live.ps1 -Stop -ClientOnly -Session S (which stops this launcher, and the
job its client). Agents/docs/dev/headless-client.md has the reverse engineering and the measurements.

It refuses a folder without a config.json naming the session's own settings file (never poeoffline_/production_):
the client would otherwise write the user's settings. It opens no process but its own child.
"""
import argparse
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import threading
import time

import frida

sys.path.insert(0, str(Path(__file__).resolve().parent))
import client_control  # noqa: E402  (same directory)

ROOT = client_control.ROOT
CLIENT = client_control.CLIENT
AGENT = Path(__file__).with_name("nullifier.js")
DEFAULT_SETTINGS = re.compile(r"^(poeoffline|production)_", re.IGNORECASE)

kernel = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
HANDLE, DWORD, BOOL = wintypes.HANDLE, wintypes.DWORD, wintypes.BOOL
for name, args, result in [
        ("OpenProcess", [DWORD, BOOL, DWORD], HANDLE),
        ("CloseHandle", [HANDLE], BOOL),
        ("SetPriorityClass", [HANDLE, DWORD], BOOL),
        ("SetProcessAffinityMask", [HANDLE, ctypes.c_size_t], BOOL),
        ("SetProcessInformation", [HANDLE, ctypes.c_int, ctypes.c_void_p, DWORD], BOOL),
        ("TerminateProcess", [HANDLE, wintypes.UINT], BOOL),
        ("WaitForSingleObject", [HANDLE, DWORD], DWORD),
        ("CreateJobObjectW", [ctypes.c_void_p, wintypes.LPCWSTR], HANDLE),
        ("SetInformationJobObject", [HANDLE, ctypes.c_int, ctypes.c_void_p, DWORD], BOOL),
        ("AssignProcessToJobObject", [HANDLE, HANDLE], BOOL)]:
    getattr(kernel, name).argtypes, getattr(kernel, name).restype = args, result
psapi.EmptyWorkingSet.argtypes, psapi.EmptyWorkingSet.restype = [HANDLE], BOOL
PRIORITIES = {"idle": 0x40, "below-normal": 0x4000, "normal": 0x20}
WAIT_TIMEOUT = 0x102


class PowerThrottling(ctypes.Structure):
    _fields_ = [("Version", wintypes.ULONG), ("ControlMask", wintypes.ULONG), ("StateMask", wintypes.ULONG)]


class IoCounters(ctypes.Structure):
    _fields_ = [(n, ctypes.c_ulonglong) for n in ("r", "w", "o", "rb", "wb", "ob")]


class BasicLimits(ctypes.Structure):
    _fields_ = [("PerProcessUserTimeLimit", ctypes.c_longlong), ("PerJobUserTimeLimit", ctypes.c_longlong),
                ("LimitFlags", DWORD), ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", DWORD),
                ("Affinity", ctypes.c_size_t), ("PriorityClass", DWORD), ("SchedulingClass", DWORD)]


class ExtendedLimits(ctypes.Structure):
    _fields_ = [("BasicLimitInformation", BasicLimits), ("IoInfo", IoCounters),
                ("ProcessMemoryLimit", ctypes.c_size_t), ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t), ("PeakJobMemoryUsed", ctypes.c_size_t)]


def check(ok, what):
    if not ok:
        raise OSError(ctypes.get_last_error(), what)


class Child:
    """The spawned client through the one handle opened right after the spawn: limits, liveness and termination never
    look the pid up again. The job object kills the client when this process (and so the job handle) goes away."""

    def __init__(self, pid):
        self.pid = pid
        self.handle = self.job = None
        try:
            # PROCESS_TERMINATE | SET_QUOTA | SET_INFORMATION | QUERY_INFORMATION | SYNCHRONIZE
            self.handle = kernel.OpenProcess(0x0001 | 0x0100 | 0x0200 | 0x0400 | 0x00100000, False, pid)
            check(self.handle, f"OpenProcess({pid})")
            # The job first: from here on the client cannot outlive this launcher, whatever fails next.
            self.job = kernel.CreateJobObjectW(None, None)
            check(self.job, "CreateJobObjectW")
            limits = ExtendedLimits()
            limits.BasicLimitInformation.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            check(kernel.SetInformationJobObject(self.job, 9, ctypes.byref(limits), ctypes.sizeof(limits)),
                  "SetInformationJobObject")
            check(kernel.AssignProcessToJobObject(self.job, self.handle), "AssignProcessToJobObject")
            # The pid cannot be reused while this handle is open, so process_info's answer is this process's.
            info = client_control.process_info(pid)
            if info is None or not client_control.same_path(info[0], CLIENT):
                raise RuntimeError(f"spawned process {pid} is not {CLIENT}")
            self.started = info[1]
        except BaseException:
            self.close()  # the suspended client goes (through the handle when there is one)
            raise

    def alive(self):
        return self.handle is not None and kernel.WaitForSingleObject(self.handle, 0) == WAIT_TIMEOUT

    def limit(self, priority, eco_qos, affinity):
        """Scheduling priority, EcoQoS (execution-speed throttling: low clocks / efficient cores; plus ignoring the
        client's timer-resolution requests where Windows supports it) and CPU affinity."""
        if priority:
            check(kernel.SetPriorityClass(self.handle, PRIORITIES[priority]), "SetPriorityClass")
        if eco_qos:
            for mask in (0x1 | 0x4, 0x1):  # EXECUTION_SPEED | IGNORE_TIMER_RESOLUTION; the latter needs Windows 11
                state = PowerThrottling(1, mask, mask)
                if kernel.SetProcessInformation(self.handle, 4, ctypes.byref(state), ctypes.sizeof(state)):
                    break
            else:
                raise OSError(ctypes.get_last_error(), "SetProcessInformation(ProcessPowerThrottling)")
        if affinity:
            check(kernel.SetProcessAffinityMask(self.handle, affinity), "SetProcessAffinityMask")

    def trim(self):
        if self.alive():
            psapi.EmptyWorkingSet(self.handle)

    def terminate(self):
        if self.handle is not None and kernel.WaitForSingleObject(self.handle, 0) == WAIT_TIMEOUT:
            kernel.TerminateProcess(self.handle, 1)

    def close(self):
        self.terminate()
        for handle in (self.handle, self.job):
            if handle:
                kernel.CloseHandle(handle)
        self.handle = self.job = None


def settings_name(folder):
    """The settings file the client in `folder` uses, from its config.json; refuses the default ones."""
    config = Path(folder) / "config.json"
    if not config.is_file():
        raise SystemExit(f"{config} is missing: the client would use the user's poeoffline_Config.ini")
    name = json.loads(config.read_text(encoding="utf-8-sig")).get("GGG_CONFIG_FILE_NAME")
    if not isinstance(name, str) or not name or DEFAULT_SETTINGS.match(Path(name).name):
        raise SystemExit(f"{config} must name the client's own settings file in GGG_CONFIG_FILE_NAME, not {name!r}")
    return name


def run_mutex_prefix(folder):
    r"""The session-local namespace for the client's two-copies mutexes (nullifier.js renames Global\PoERunMutexA/B to
    <prefix>PoERunMutexA/B), so clients we launch never take one of the two Global slots the user's and other
    sessions' clients need. Unique per checkout and client folder."""
    tag = re.sub(r"[^A-Za-z0-9_-]", "_", f"{ROOT.name}-{Path(folder).name}")[:80]
    return "Local\\LocalExile-" + tag + "-"


def write_record(path, values, attempts=40, pause=0.05):
    """Merge `values` into the JSON at `path` (live.ps1's pending record keeps its other fields), atomically. Replacing
    fails while a reader (live.ps1 polling it) has the file open; that is retried for up to attempts × pause seconds."""
    path = Path(path)
    record = {}
    if path.exists():
        try:
            record = json.loads(path.read_text(encoding="utf-8-sig"))
        except ValueError:
            record = {}
    record.update(values)
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary = tempfile.mkstemp(dir=path.parent, prefix=path.name, suffix=".tmp")
    with os.fdopen(handle, "w", encoding="ascii") as out:
        json.dump(record, out)
    for attempt in range(attempts):
        try:
            os.replace(temporary, path)
            return
        except PermissionError:
            if attempt == attempts - 1:
                os.unlink(temporary)
                raise
            time.sleep(pause)


def agent_options(options, folder):
    return {
        "nullRenderer": options.renderer == "null", "noRenderJobs": options.no_render_jobs,
        "noTextureUpload": options.no_texture_upload, "frameSleepMs": max(0, options.frame_sleep_ms),
        "window": options.window, "traceFiles": options.trace_files, "runMutexPrefix": run_mutex_prefix(folder),
        "crashLog": str(Path(os.path.abspath(options.crash_log))) if options.crash_log else None}


def client_argv(options):
    # --nologo also marks a launcher-started client for live.ps1 (Get-GlobalSlotHolders): no other start passes it.
    argv = [str(CLIENT), "--patching-server", f"127.0.0.1:{options.patch_port}", "--nologo"]
    return argv if options.sound else argv + ["--nosound"]


def parse(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--cwd", type=Path, required=True,
                        help="the session folder (its config.json names the settings file; live.ps1 prepares it)")
    parser.add_argument("--record", type=Path, help="merge {pid, started, launcher, launcherStarted, ...} into this JSON")
    parser.add_argument("--patch-port", type=int, default=None, help="patching server port (default: the stack's)")
    parser.add_argument("--login-port", type=int, default=None, help="Auth login port (default: the stack's; recorded)")
    parser.add_argument("--renderer", choices=["null", "configured"], default="configured",
                        help="configured: the settings' device_type (the preset: DirectX 11); null: the engine's Null "
                             "device, no GPU")
    parser.add_argument("--frame-sleep-ms", type=int, default=0,
                        help="sleep per main-loop frame (0: none; the preset's framerate_limit caps every renderer)")
    parser.add_argument("--no-render-jobs", action="store_true", help="also disable the frame render jobs (stalls)")
    parser.add_argument("--no-texture-upload", action="store_true", help="texture manager Upload off")
    parser.add_argument("--priority", choices=sorted(PRIORITIES), default="below-normal")
    parser.add_argument("--no-eco-qos", action="store_true", help="do not mark the process EcoQoS")
    parser.add_argument("--affinity", type=lambda v: int(v, 0), default=0, help="CPU affinity mask (0: all)")
    parser.add_argument("--trim-seconds", type=float, default=0, help="trim the working set this often (0: never)")
    parser.add_argument("--window", choices=["minimized", "hidden", "normal"], default="normal",
                        help="how the window shows; it is never activated, and minimizing it does not stop the client")
    parser.add_argument("--sound", action="store_true", help="keep sound (default: --nosound)")
    parser.add_argument("--trace-files", help="log CreateFileW paths matching this regex (diagnostics)")
    parser.add_argument("--crash-log", type=Path, help="append a client crash's registers and backtrace here")
    parser.add_argument("--probe", type=Path, action="append", default=[],
                        help="append this Frida script to the agent (diagnostics; it sees OPTIONS, at(), checked(), log())")
    options = parser.parse_args(argv)
    if options.patch_port is None:
        options.patch_port = client_control.stack_port("patch")
    if options.login_port is None:
        options.login_port = client_control.stack_port("login")
    return options


MUTEX_CONFIRMATION_SECONDS = 30


def main(argv=None):
    options = parse(argv)
    folder = Path(os.path.abspath(options.cwd))
    if client_control.same_path(folder, CLIENT.parent):
        raise SystemExit("the shared Client folder has no config.json of its own: use a live.ps1 session folder")
    settings = settings_name(folder)
    agent = agent_options(options, folder)
    # As Start-Offline.ps1: optional web requests (news, shop) fail locally instead of reaching the internet.
    env = {"HTTP_PROXY": "http://127.0.0.1:1", "HTTPS_PROXY": "http://127.0.0.1:1", "ALL_PROXY": "http://127.0.0.1:1",
           "NO_PROXY": "localhost,127.0.0.1,::1"}

    device = frida.get_local_device()
    # stdio "pipe": the client also logs to its standard output; Frida reads it (the "output" events, dropped).
    pid = device.spawn(client_argv(options), cwd=str(folder), env=env, stdio="pipe")
    child = session = None
    renamed = threading.Event()

    def on_message(message, data):
        text = message.get("payload", {}).get("message") if message.get("type") == "send" else \
            message.get("description", message)
        if isinstance(text, str) and text.startswith("run mutex -> "):
            renamed.set()
        print(json.dumps({"nullifier": text}), flush=True)
    try:
        child = Child(pid)  # in its job before it runs a single instruction
        session = device.attach(pid)
        session.on("detached", lambda *unused: child.terminate())
        script = session.create_script(f"const OPTIONS = {json.dumps(agent)};\n" + AGENT.read_text(encoding="utf-8")
                                       + "".join("\n" + p.read_text(encoding="utf-8") for p in options.probe),
                                       name="localexile-nullifier")
        script.on("message", on_message)
        script.load()
        child.limit(options.priority, not options.no_eco_qos, options.affinity)
        device.resume(pid)
        # The client takes its two-copies mutex first thing; without the agent's confirmation that it got the
        # session-local name, it may hold (or wait for) a Global slot: it goes, and the launch fails.
        if not renamed.wait(MUTEX_CONFIRMATION_SECONDS):
            raise SystemExit(f"no run-mutex rename within {MUTEX_CONFIRMATION_SECONDS} s: the client is closed, not "
                             "left holding one of the two machine-wide slots")
        own = client_control.process_info(os.getpid())
        launched = {"pid": pid, "started": child.started, "launcher": os.getpid(),
                    "launcherStarted": own[1] if own else None, "cwd": str(folder), "settings": settings,
                    "loginPort": options.login_port, "options": agent}
        if options.record:
            write_record(options.record, launched)
        print(json.dumps({"launched": launched}), flush=True)
        if options.trim_seconds > 0:
            def trimmer():
                while child.alive():
                    time.sleep(options.trim_seconds)
                    child.trim()
            threading.Thread(target=trimmer, daemon=True).start()
        while child.alive():
            kernel.WaitForSingleObject(child.handle, 1000)
    except KeyboardInterrupt:
        pass
    finally:
        if child is not None:
            child.close()
        else:  # failed before the handle: the spawned process is still suspended and certainly ours
            try:
                device.kill(pid)
            except frida.InvalidOperationError:
                pass
        if session is not None:
            try:
                session.detach()
            except frida.InvalidOperationError:
                pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
