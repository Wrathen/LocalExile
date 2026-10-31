"""The headless-client measurement scenario (Agents/docs/dev/headless-client.md): one session client started by
live.ps1 (through nullifier.py), log in, play a character, stand in town, measure it with nullifier-measure.ps1, then
prove the bridge (slash commands, chat, /tp, waypoint travel, /goto back) and measure again. The client is stopped at
the end (live.ps1 -Stop -ClientOnly -Session).

    .venv/Scripts/python.exe scripts/dev/nullifier-bench.py --label null --session bench --nullifier --travel

It needs this checkout's servers running on their own ports (live.ps1 -PortBase, so data/stack.json exists and the
shared GGPK's gateway list is never written), with their logs in logs/ (client_control reads slash-command replies
from logs/live-gateway.log).

Safety: it refuses to launch when free commit is below --min-free-gb or free RAM below --min-free-ram-gb, or while
--lock exists (created atomically with this pid; a lock in any other format counts as held). A watchdog kills this
run's client, and only it (through a handle opened once and checked against the recorded start time), when free
commit falls below --watch-commit-gb or free RAM below --watch-ram-gb. Every bridge call times out. The user's
Documents/My Games/Path of Exile/poeoffline_Config.ini is checked unchanged afterwards.
The password comes from Game_BENCH_PASSWORD or the setup's data/offline-login.txt; it is never printed.
"""
import argparse
import ctypes
from ctypes import wintypes
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
import client_control  # noqa: E402  (same directory)

ROOT = client_control.ROOT
LIVE = ROOT / "scripts" / "dev" / "live.ps1"
MEASURE = ROOT / "scripts" / "dev" / "nullifier-measure.ps1"
def documents_folder():
    """The user's Documents (FOLDERID_Documents), where it really is (it may be redirected, e.g. to OneDrive)."""
    path = ctypes.c_wchar_p()
    # {FDD39AD0-238F-46AF-ADB4-6C85480369C7} in GUID byte order
    folder = (ctypes.c_byte * 16).from_buffer_copy(bytes.fromhex("d09ad3fd8f23af46adb46c85480369c7"))
    if ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(folder), 0, None, ctypes.byref(path)) != 0:
        return Path(os.path.expanduser("~")) / "Documents"
    try:
        return Path(path.value)
    finally:
        ctypes.windll.ole32.CoTaskMemFree(path)


USER_SETTINGS = documents_folder() / "My Games" / "Path of Exile" / "poeoffline_Config.ini"
COAST = 0x7ad2  # WorldAreas 1_1_2 HASH16, a waypoint next to Lioneye's Watch

kernel = ctypes.WinDLL("kernel32", use_last_error=True)
HANDLE, DWORD, BOOL = wintypes.HANDLE, wintypes.DWORD, wintypes.BOOL
for name, args, result in [("OpenProcess", [DWORD, BOOL, DWORD], HANDLE), ("CloseHandle", [HANDLE], BOOL),
                           ("TerminateProcess", [HANDLE, wintypes.UINT], BOOL),
                           ("WaitForSingleObject", [HANDLE, DWORD], DWORD)]:
    getattr(kernel, name).argtypes, getattr(kernel, name).restype = args, result


class MemoryStatus(ctypes.Structure):
    _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + \
               [(n, ctypes.c_ulonglong) for n in ("total", "avail", "totalCommit", "availCommit", "totalVirt",
                                                  "availVirt", "availExt")]


def free_gb():
    """(free commit, free physical RAM) in GB."""
    status = MemoryStatus(); status.dwLength = ctypes.sizeof(status)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
    return status.availCommit / 2**30, status.avail / 2**30


def alive_pid(pid):
    handle = kernel.OpenProcess(0x1000 | 0x00100000, False, pid)  # QUERY_LIMITED_INFORMATION | SYNCHRONIZE
    if not handle:
        return False
    try:
        return kernel.WaitForSingleObject(handle, 0) == 0x102
    finally:
        kernel.CloseHandle(handle)


class Client:
    """A handle to the session's client, opened once; it is the recorded one (process_info's start time), and while
    the handle is open its pid cannot be reused."""

    def __init__(self, pid, started):
        self.handle = kernel.OpenProcess(0x0001 | 0x1000 | 0x00100000, False, pid)
        if not self.handle:
            raise RuntimeError(f"cannot open client {pid}")
        info = client_control.process_info(pid)
        if info is None or abs(info[1] - started) >= 20_000_000:
            kernel.CloseHandle(self.handle); self.handle = None
            raise RuntimeError(f"pid {pid} is not the recorded client")
        self.pid = pid

    def alive(self):
        return self.handle is not None and kernel.WaitForSingleObject(self.handle, 0) == 0x102

    def kill(self):
        if self.alive():
            kernel.TerminateProcess(self.handle, 1)

    def close(self):
        if self.handle:
            kernel.CloseHandle(self.handle)
            self.handle = None


def take_lock(path):
    """Create the lock atomically; an existing lock is held unless it is ours-format with a dead pid."""
    path = Path(path)
    for _ in range(2):
        try:
            fd = os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            words = path.read_text(encoding="utf-8", errors="replace").split()
            if len(words) >= 2 and words[0] == "nullifier-bench" and words[1].isdigit() and not alive_pid(int(words[1])):
                path.unlink()  # a dead run of this script left it
                continue
            raise SystemExit(f"lock held: {' '.join(words) or '(empty)'}")
        with os.fdopen(fd, "w", encoding="utf-8") as out:
            out.write(f"nullifier-bench {os.getpid()} {datetime.datetime.now().isoformat()}\n")
        return
    raise SystemExit("could not take the lock")


def release_lock(path):
    path = Path(path)
    try:
        words = path.read_text(encoding="utf-8", errors="replace").split()
    except OSError:
        return
    if words[:2] == ["nullifier-bench", str(os.getpid())]:
        path.unlink()


def password():
    if os.environ.get("Game_BENCH_PASSWORD"):
        return os.environ["Game_BENCH_PASSWORD"]
    for line in (ROOT / "data" / "offline-login.txt").read_text(encoding="utf-8").splitlines():
        if line.startswith("Password: "):
            return line[len("Password: "):].strip()
    raise SystemExit("no password: set Game_BENCH_PASSWORD")


def with_timeout(seconds, function, *args):
    """function(*args) on a worker thread; TimeoutError when it takes longer than `seconds`."""
    result, error = [], []

    def run():
        try:
            result.append(function(*args))
        except BaseException as e:  # noqa: BLE001  (re-raised on the caller's thread)
            error.append(e)
    worker = threading.Thread(target=run, daemon=True)
    worker.start()
    worker.join(seconds)
    if worker.is_alive():
        raise TimeoutError(f"no answer within {seconds} s")
    if error:
        raise error[0]
    return result[0]


def live(*arguments, timeout=120):
    return subprocess.run(["powershell", "-NoProfile", "-File", str(LIVE), *arguments], cwd=ROOT, text=True,
                          capture_output=True, timeout=timeout)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--label", required=True)
    parser.add_argument("--session", default="bench", help="the live.ps1 session the client runs as")
    parser.add_argument("--nullifier", action="store_true", help="live.ps1 -Nullifier: the Null renderer, trimmed")
    parser.add_argument("--account", default="offline")
    parser.add_argument("--character", default="OfflineWitch")
    parser.add_argument("--seconds", type=int, default=60, help="town measurement length")
    parser.add_argument("--travel", action="store_true", help="also waypoint to The Coast and /goto back")
    parser.add_argument("--min-free-gb", type=float, default=3, help="free commit needed to launch")
    parser.add_argument("--min-free-ram-gb", type=float, default=8, help="free physical RAM needed to launch")
    parser.add_argument("--watch-commit-gb", type=float, default=1.5, help="kill the client below this free commit")
    parser.add_argument("--watch-ram-gb", type=float, default=4, help="kill the client below this free RAM")
    parser.add_argument("--rpc-timeout", type=float, default=120, help="seconds a bridge call may take")
    parser.add_argument("--lock", type=Path, help="lock file shared by agents that launch extra clients")
    parser.add_argument("--out", type=Path, default=ROOT / "logs" / "nullifier" / "bench.jsonl")
    options = parser.parse_args()

    if not client_control.STACK.exists():
        raise SystemExit("run the servers on this checkout's own ports first (live.ps1 -PortBase): the bench never "
                         "uses the default stack, whose client start writes the shared GGPK's gateway list")
    session = client_control.session_name(options.session)
    if session == client_control.DEFAULT_SESSION:
        raise SystemExit("the bench needs a named session")
    commit, ram = free_gb()
    if commit < options.min_free_gb or ram < options.min_free_ram_gb:
        raise SystemExit(f"free commit {commit:.1f} GB / RAM {ram:.1f} GB below {options.min_free_gb} / "
                         f"{options.min_free_ram_gb} GB: not launching")
    if options.lock:
        take_lock(options.lock)
    options.out.parent.mkdir(parents=True, exist_ok=True)
    settings_before = USER_SETTINGS.stat().st_mtime_ns if USER_SETTINGS.exists() else None
    log = lambda *a: print(datetime.datetime.now().strftime("%H:%M:%S"), *a, flush=True)  # noqa: E731
    record = lambda value: options.out.open("a", encoding="utf-8").write(json.dumps(value) + "\n")  # noqa: E731
    client = None
    stop = threading.Event()
    control = client_control.ClientControl(client_session=session)
    try:
        started = live("-ClientOnly", "-Session", session, *(["-Nullifier"] if options.nullifier else []))
        log("live.ps1:", started.stdout.strip(), started.stderr.strip())
        if started.returncode:
            raise SystemExit("live.ps1 -ClientOnly failed")
        saved = client_control.read_records().get(session)
        if not saved or not saved["pid"]:
            raise SystemExit("no client record after the start")
        client = Client(saved["pid"], saved["started"])

        def watchdog():
            while not stop.wait(2):
                commit, ram = free_gb()
                if (commit < options.watch_commit_gb or ram < options.watch_ram_gb) and client.alive():
                    client.kill()
                    log(f"WATCHDOG: free commit {commit:.1f} GB, RAM {ram:.1f} GB: killed this run's client")
                    record({"label": options.label, "watchdog": {"commitGB": round(commit, 2), "ramGB": round(ram, 2)}})
        threading.Thread(target=watchdog, daemon=True).start()

        def rpc(request, show=True):
            result = with_timeout(options.rpc_timeout, client_control.run_request, control, request)
            if show:
                log(request["command"], request.get("text", request.get("area", "")), "->", json.dumps(result)[:400])
            return result

        def measure(label, seconds):
            text = subprocess.check_output(["powershell", "-NoProfile", "-File", str(MEASURE), "-ProcessId",
                                            str(client.pid), "-Seconds", str(seconds), "-Label", label],
                                           text=True, timeout=seconds + 60).strip()
            frames = rpc({"command": "game:frames"}, show=False).get("result")
            value = {**json.loads(text), "frames": frames, "nullifier": options.nullifier}
            record(value); log("measured", json.dumps(value))
            return value

        clock = time.monotonic()
        rpc({"command": "game:open"})
        log(f"menu after {time.monotonic() - clock:.1f} s")
        result = rpc({"command": "login", "account": options.account, "password": password()}, show=False)
        log("login ->", json.dumps(result)[:300])
        rpc({"command": "character:list"})
        if not rpc({"command": "character:play", "name": options.character})["ok"]:
            # The bridge gives up when no frame comes for 5 s (a long area load); keep watching for 90 s.
            for _ in range(90):
                time.sleep(1)
                if not client.alive():
                    raise SystemExit("play failed: the client exited")
                status = rpc({"command": "game:status"}, show=False)
                if status["ok"] and status["result"].get("screen") == "game" and status["result"].get("instanceState") == 3:
                    break
            else:
                raise SystemExit("play failed")
        log(f"in game after {time.monotonic() - clock:.1f} s")
        rpc({"command": "command", "text": "/pos"})
        time.sleep(10)
        rpc({"command": "game:frames"}, show=False)
        measure(options.label, options.seconds)
        if options.travel:
            rpc({"command": "command", "text": "/waypoints all"})
            rpc({"command": "waypoint:travel", "area": COAST})
            time.sleep(12)
            rpc({"command": "game:status"})
            rpc({"command": "command", "text": "/pos"})
            rpc({"command": "command", "text": "/goto 1_1_town"})
            time.sleep(10)
            rpc({"command": "game:status"})
            rpc({"command": "command", "text": "/pos"})
            rpc({"command": "chat", "text": "nullifier bench"})
            rpc({"command": "tp", "dx": 5, "dy": 0})
            measure(options.label + "-after-travel", 20)
    finally:
        stop.set()
        control.close()
        stopped = live("-Stop", "-ClientOnly", "-Session", session)
        log("live.ps1 -Stop:", stopped.stdout.strip(), stopped.stderr.strip())
        if client is not None:
            client.kill()  # if the stop could not (the record was gone): only through the verified handle
            client.close()
        if options.lock:
            release_lock(options.lock)
        after = USER_SETTINGS.stat().st_mtime_ns if USER_SETTINGS.exists() else None
        if after != settings_before:
            log(f"WARNING: {USER_SETTINGS} changed during the run (by this client, or by another session's "
                "default-folder client, which uses that file by design)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
