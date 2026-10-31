# usage: python -I scripts/re/levelgen/tourloop.py <tour file> [--account NAME] [--character NAME] [--stall SECONDS]
# Runs a Gateway capture tour (/tour, CaptureTour.cs) without anyone at the keyboard: starts the servers, the walk-map
# recorder and the client through scripts/dev/live.ps1 -Walkmaps, logs in (scripts/dev/client_control.py: the client's
# remembered login, else the LOCALEXILE_PASSWORD environment variable) and plays the character, which starts the tour or resumes
# the saved one (data/tour-left.txt). When the client dies, shows an exception or makes no progress for --stall
# seconds, the stop under way goes to data/walkability/tour-failures.txt (unless the Gateway already put it there), the
# client and the servers are restarted and the tour goes on. Ends when the tour is finished. Progress: logs/tourloop.log;
# the server and recorder logs of each run (live.ps1 deletes them on start) are kept in logs/tour-runs/<start time>/.
import argparse, datetime, json, os, re, shutil, subprocess, sys, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
STATE = ROOT / "data" / "tour-left.txt"
FAILURES = ROOT / "data" / "walkability" / "tour-failures.txt"
LOG = ROOT / "logs" / "tourloop.log"
RUNS = ROOT / "logs" / "tour-runs"
RECORDS = ROOT / "data" / "tour-records" / "stops.jsonl"
STACK = ROOT / "data" / "stack.json"  # live.ps1 -PortBase: this checkout's ports
CLIENT_RECORD = ROOT / "data" / "clients" / "default.json"  # live.ps1: the default client's PID and working folder
GATEWAY_LOG = ROOT / "logs" / "live-gateway.log"
PYTHON = ROOT / ".venv" / "Scripts" / "python.exe"
CONTROL = ROOT / "scripts" / "dev" / "client_control.py"


def log(text):
    line = f"{datetime.datetime.now():%Y-%m-%dT%H:%M:%S} {text}"
    print(line, flush=True)
    with LOG.open("a", encoding="utf-8") as f:
        f.write(line + "\n")


def ps(command, timeout=600):
    return subprocess.run(["powershell.exe", "-NoProfile", "-Command", command], cwd=ROOT, capture_output=True, text=True,
                          timeout=timeout)


def control(*args, stdin=None, timeout=120):
    python = str(PYTHON) if PYTHON.exists() else sys.executable
    r = subprocess.run([python, str(CONTROL), *args], cwd=ROOT, input=stdin, capture_output=True, text=True, timeout=timeout)
    out = (r.stdout or "").strip().splitlines()
    try:
        value = json.loads(out[-1]) if out else {"ok": False, "error": r.stderr.strip()}
        # client_control answers {"ok": …, "result": {…}}: the result's fields beside ok.
        if isinstance(value.get("result"), dict):
            value = {**value["result"], "ok": value.get("ok")}
        return value
    except json.JSONDecodeError:
        return {"ok": False, "error": (r.stdout + r.stderr).strip()[-400:]}


def client_running():
    """This checkout's client (started from its Client folder), not another checkout's."""
    client = ROOT / "Client" / "Game.patched.exe"
    return ps(f"@(Get-CimInstance Win32_Process -Filter \"Name = 'Game.patched.exe'\" | Where-Object {{ $_.ExecutablePath -eq '{client}' }}).Count"
              ).stdout.strip() not in ("", "0")


def recorder_running():
    walkmaps = ROOT / "scripts" / "walkmaps" / "walkmaps.py"  # this checkout's recorder (live.ps1 starts it by full path)
    r = ps(f"Get-CimInstance Win32_Process -Filter \"Name like 'python%'\" | Where-Object {{ $_.CommandLine -like '*{walkmaps}*' }} | "
           "Select-Object -ExpandProperty ProcessId")
    return r.stdout.strip() != ""


def gateway_port():
    try:
        return int(json.loads(STACK.read_text(encoding="utf-8-sig"))["ports"]["gateway"])
    except (OSError, ValueError, KeyError, TypeError):
        return 6112


def client_log():
    """The Client.txt of this checkout's default client: in the working folder live.ps1 recorded (Client by default)."""
    try:
        folder = Path(json.loads(CLIENT_RECORD.read_text(encoding="utf-8-sig"))["folder"])
    except (OSError, ValueError, KeyError, TypeError):
        folder = ROOT / "Client"
    return folder / "logs" / "Client.txt"


def gateway_listening():
    return ps(f"@(Get-NetTCPConnection -LocalPort {gateway_port()} -State Listen -ErrorAction SilentlyContinue).Count"
              ).stdout.strip() not in ("", "0")


def stop_all():
    """This checkout's client, recorder and servers (live.ps1 -Stop), never another checkout's."""
    ps(f"& '{ROOT / 'scripts' / 'dev' / 'live.ps1'}' -Stop")
    time.sleep(2)


def state():
    """(total, captured, missed, current or None, left) from the Gateway's saved tour, or None without one."""
    try:
        lines = STATE.read_text(encoding="utf-8").splitlines()
    except OSError:
        return None
    def count(key):
        for l in lines:
            m = re.match(rf"^# {key}: (\d+)$", l)
            if m:
                return int(m.group(1))
        return 0
    current = next((l[len("# current: "):].strip() for l in lines if l.startswith("# current: ")), None)
    left = sum(1 for l in lines if l.strip() and not l.startswith("#"))
    return count("total"), count("captured"), count("missed"), current, left


def drop_current(reason):
    """The Gateway is down: the stop under way leaves the saved tour and goes to the failures."""
    try:
        lines = STATE.read_text(encoding="utf-8").splitlines()
    except OSError:
        return
    current = next((l[len("# current: "):].strip() for l in lines if l.startswith("# current: ")), None)
    if current is None:
        return
    out, dropped = [], False
    for l in lines:
        if l.startswith("# current: "):
            continue
        m = re.match(r"^# missed: (\d+)$", l)
        if m:
            l = f"# missed: {int(m.group(1)) + 1}"
        elif not dropped and l.strip() == current:
            dropped = True
            continue
        out.append(l)
    STATE.write_text("\n".join(out) + "\n", encoding="utf-8")
    with FAILURES.open("a", encoding="utf-8") as f:
        f.write(f"{datetime.datetime.now():%Y-%m-%dT%H:%M:%S} {current}: the client did not arrive ({reason})\n")
    record_drop(current, reason)
    log(f"dropped {current}: {reason}")


def record_drop(current, reason):
    """The tour record of a stop the Gateway could not finish (client, recorder or Gateway gone, no progress): the
    client's log from that load's "Generating level" line on (its crash or error, if it logged one)."""
    area, _, seed = current.partition(" ")
    lines = []
    try:
        with client_log().open(encoding="utf-8", errors="replace") as f:
            text = f.read().splitlines()
        at = max((i for i, l in enumerate(text) if f'area "{area}" with seed {seed}' in l), default=None)
        lines = text[at:at + 400] if at is not None else text[-80:]
    except OSError:
        pass
    # A client that died has a Windows "Application Error" event (exception code, faulting module and offset: 0xe06d7363
    # is a C++ exception nothing caught); its log stops at the load's first line.
    crash = ps("Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000; StartTime=(Get-Date).AddMinutes(-15)} "
               "-ErrorAction SilentlyContinue | Where-Object { $_.Message -match 'Game\\.patched' } | Select-Object -First 1 "
               "| ForEach-Object { $_.TimeCreated.ToString('s') + ' ' + ($_.Message -replace '\\s+', ' ') }").stdout.strip()
    RECORDS.parent.mkdir(parents=True, exist_ok=True)
    with RECORDS.open("a", encoding="utf-8") as f:
        f.write(json.dumps({"time": f"{datetime.datetime.now():%Y-%m-%dT%H:%M:%S}", "area": area, "seed": int(seed or 0),
                            "outcome": "dropped", "reason": reason, "client": {"log": lines, "crash": crash or None}}) + "\n")


def keep_logs():
    """Copies the last run's server and recorder logs before live.ps1 deletes them."""
    logs = [p for p in (ROOT / "logs").glob("live-*.*") if p.suffix in (".log", ".err")] + list((ROOT / "logs").glob("walkmaps.*"))
    logs = [p for p in logs if p.stat().st_size > 0]
    if not logs:
        return
    folder = RUNS / datetime.datetime.now().strftime("%Y%m%dT%H%M%S")
    folder.mkdir(parents=True, exist_ok=True)
    for p in logs:
        try:
            shutil.copy2(p, folder / p.name)
        except OSError as e:
            log(f"could not keep {p.name}: {e}")


def start(account, password, character, tour_file, first):
    stop_all()
    keep_logs()
    r = ps("& scripts/dev/live.ps1 -Walkmaps -NoBuild -NoExtract", timeout=300)
    if r.returncode != 0:
        log(f"live.ps1 failed: {(r.stdout + r.stderr).strip()[-300:]}")
        return False
    # live.ps1 launched the client: wait for its login screen.
    deadline = time.monotonic() + 240
    while True:
        status = control("game:status", timeout=60)
        if status.get("screen") == "login":
            break
        if time.monotonic() > deadline:
            log(f"client did not reach the login screen: {str(status)[:200]}")
            return False
        time.sleep(3)
    # The client's remembered login when it has one, else the account and LOCALEXILE_PASSWORD.
    r = control("login:saved", timeout=120)
    if not r.get("ok") and password is not None:
        r = control(f"login:{account}", "--password-stdin", stdin=password + "\n", timeout=120)
    if not r.get("ok"):
        log(f"login failed: {str(r.get('error'))[:200]}")
        return False
    r = control(f"character:play:{character}", timeout=120)
    # With a saved tour the arrival sends the character on at once: the wait for it to stand in an area can time out.
    # The area load can also stop frames for longer than client-control waits for one ("No client frame within 5
    # seconds") although the character arrives: ask again for up to a minute.
    deadline = time.monotonic() + 60
    while not r.get("ok") and state() is None and time.monotonic() < deadline:
        time.sleep(5)
        status = control("game:status", timeout=60)
        if status.get("screen") == "game":
            r = {"ok": True}
    if not r.get("ok") and state() is None:
        log(f"play failed: {str(r.get('error'))[:200]}")
        return False
    # Only the first start begins the tour: a later one finding no saved tour means it just ended.
    if first and state() is None:
        r = control("command", f"/tour {tour_file}", timeout=60)
        log(f"tour started: {r.get('replies')}")
    else:
        log("client in the game; the saved tour resumes")
    return True


def main():
    p = argparse.ArgumentParser()
    p.add_argument("tour")
    p.add_argument("--account", default="devtester")
    p.add_argument("--character", default="DevWitch")
    p.add_argument("--stall", type=int, default=300)
    a = p.parse_args()
    password = os.environ.get("LOCALEXILE_PASSWORD")
    started_once = state() is not None
    failures = 0
    while True:
        s = state()
        if started_once and s is None:
            log("tour finished")
            return 0
        if not started_once or not (client_running() and gateway_listening() and recorder_running()):
            if s and s[3]:
                stop_all()
                drop_current("client, recorder or Gateway gone")
            log(f"(re)starting: {s}")
            if not start(a.account, password, a.character, a.tour, not started_once):
                failures += 1
                if failures > 20:
                    log("too many failed starts; giving up")
                    return 1
                time.sleep(30)
                continue
            failures = 0
            started_once = True
        # Wait for progress: the count of captured + missed stops, or a new current stop.
        last = state()
        since = time.monotonic()
        while True:
            time.sleep(10)
            now = state()
            if now is None:
                break
            if not (client_running() and gateway_listening()):
                break
            if last is None or (now[1] + now[2], now[3]) != (last[1] + last[2], last[3]):
                last, since = now, time.monotonic()
                log(f"progress: {now[1]} captured, {now[2]} missed, {now[4]} left, now {now[3]}")
                continue
            # No stop under way: the client failed one (the Gateway listed it) and hangs on its error.
            if now[3] is None and time.monotonic() - since > 45:
                log(f"client failed a stop and waits; restarting")
                stop_all()
                break
            if time.monotonic() - since > a.stall:
                log(f"no progress for {a.stall} s at {now[3]}; restarting")
                stop_all()
                drop_current(f"no progress for {a.stall} s") if now[3] else None
                break


if __name__ == "__main__":
    sys.exit(main())
