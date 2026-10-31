"""Walk-map generator: captures the terrain walkability grid of every area your own client generates.

Usage (started for you by `scripts/dev/live.ps1 -Walkmaps`):
    python scripts/walkmaps/walkmaps.py [--process Game.patched.exe | --record data/clients/default.json] [--out data/walkability]

Needs: pip install frida  (Python 3.9+). Attaches to the running client, waits for it if it is not running yet,
and writes data/walkability/<area>_seed<seed>.bin plus <area>_seed<seed>.json ({"w":..,"h":..}), and <area>_seed<seed>.projectile.bin (what stops projectiles), and the layout's area
transitions and waypoints (grid position, rotation, height, model) to <area>_seed<seed>.transitions.json, and every placed
doodad (model, grid cell, angle) to <area>_seed<seed>.doodads.json (for the rooms' monster points; an empty list when
the area places none).
Rhoa Nests/Glyph Wall (research only) and tutorial tile objects are recorded in <area>_seed<seed>.objects.json.
Placed .arm rooms are recorded in <area>_seed<seed>.rooms.json; the Strand support-gem chest uses its named room point. The area and
seed come from data/walkability/current.json, which GameServer.Gateway writes on every connection. Existing walk maps are
kept.
Nothing is sent anywhere; the files stay on your machine (data/ is git-ignored).
"""

import argparse
import json
import os
import sys
import threading
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "dev"))

try:
    import frida
except ImportError:
    sys.exit("walkmaps: the frida package is missing. Install it with:  python -m pip install frida")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))


def log(text):
    print(f"[walkmaps] {text}", flush=True)


def replace(tmp, path):
    """os.replace, retried while another process (the Gateway reading the capture) has the target open on Windows."""
    for _ in range(40):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            time.sleep(0.05)
    os.replace(tmp, path)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--process", default="Game.patched.exe", help="client process name")
    ap.add_argument("--record", help="attach to the client whose PID live.ps1 records in this file (several clients "
                                     "may run on one PC), instead of the one process of that name")
    ap.add_argument("--out", default=os.path.join(REPO, "data", "walkability"), help="output folder")
    ap.add_argument("--wait", type=int, default=600, help="seconds to wait for the client to start")
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    session = attach(args.process, args.wait, args.record)
    with open(os.path.join(HERE, "walkmaps.js"), encoding="utf-8") as f:
        script = session.create_script(f.read())

    done = threading.Event()
    script.on("message", lambda message, data: on_message(message, data, args.out))
    session.on("detached", lambda reason, *_: (log(f"client gone ({reason})"), done.set()))
    script.load()
    log(f"attached to {args.record or args.process}; writing to {args.out}")
    try:
        while not done.wait(1):
            pass
    except KeyboardInterrupt:
        pass


def recorded_pid(record, info=None):
    """The client PID in a live.ps1 record (data/clients/<session>.json), or None while there is none (pid 0 while
    live.ps1 is starting the client) or the record is stale: the process with that PID must be the recorded one, by
    its start time (a PID is reused once its process exits) and the path it was started by (this checkout's Client),
    as client_control.find_client checks it."""
    try:
        with open(record, encoding="utf-8-sig") as f:
            saved = json.load(f)
        pid, started = int(saved.get("pid") or 0), int(saved.get("started") or 0)
    except (OSError, ValueError, AttributeError, TypeError):
        return None
    if not pid:
        return None
    import client_control  # scripts/dev, on sys.path (below)
    details = (info or client_control.process_info)(pid)
    if (details is None or abs(details[1] - started) >= 20_000_000
            or not client_control.same_path(details[0], client_control.CLIENT)):
        return None
    return pid


def attach(name, wait, record=None):
    deadline = time.time() + wait
    announced = False
    if record:
        name = f"the client recorded in {record}"
    while True:
        try:
            pid = recorded_pid(record) if record else None
            if record and pid is None:
                raise frida.ProcessNotFoundError(f"no client recorded in {record} yet")
            return frida.attach(pid if record else name)
        except frida.ProcessNotFoundError:
            if time.time() > deadline:
                sys.exit(f"walkmaps: {name} did not start within {wait} s")
            if not announced:
                log(f"waiting for {name} ...")
                announced = True
            time.sleep(1)
        except frida.PermissionDeniedError:
            sys.exit("walkmaps: permission denied attaching to the client (run both at the same privilege level)")


def on_message(message, data, out):
    if message.get("type") == "error":
        log(f"script error: {message.get('description') or message}")
        return
    payload = message.get("payload") or {}
    kind = payload.get("type")
    if kind == "ready":
        log(f"hook installed in {payload.get('module')}")
    elif kind == "refused":
        log(f"not hooking: {payload.get('reason')}")
    elif kind == "skip":
        log(f"ignored a grid with unexpected size {payload}")
    elif kind == "error":
        log(f"hook error: {payload.get('message')}")
    elif kind == "grid" and data is not None:
        save(out, payload["w"], payload["h"], data)
    elif kind == "projectile" and data is not None:
        save_projectile(out, payload["w"], payload["h"], data)
    elif kind == "object":
        save_object(out, payload)
    elif kind == "quest-object":
        save_quest_object(out, payload)
    elif kind == "doodads":
        save_doodads(out, payload.get("doodads") or [])
    elif kind == "generation":
        save_generation(out, payload)
    elif kind == "generation-failed":
        save_failed_generation(out, payload)
    elif kind == "probe":
        save_probe(out, payload)
    elif kind == "cell-heights" and data is not None:
        save_cell_heights(out, payload, data)
    elif kind == "room":
        save_room(out, payload)


USED = set()  # current.json nonces that already got their grid
FRESH_SECONDS = 120


def save(out, w, h, cells):
    area = current_area(out)
    if area is None:
        log(f"got a {w}x{h} grid, but no data/walkability/current.json from the Gateway; not saved")
        return
    # One grid per area load: the first grid after the Gateway announced the area, and only while that is recent.
    if area.get("nonce") in USED or time.time() - float(area.get("written", 0)) > FRESH_SECONDS:
        log(f"got a {w}x{h} grid that does not belong to a fresh area load; not saved")
        return
    USED.add(area.get("nonce"))
    stem = os.path.join(out, f"{area['id']}_seed{area['seed']}")
    if os.path.exists(stem + ".bin"):
        log(f"{os.path.basename(stem)}.bin already exists; kept")
        return
    with open(stem + ".bin.tmp", "wb") as f:
        f.write(cells)
    replace(stem + ".bin.tmp", stem + ".bin")
    with open(stem + ".json", "w", encoding="utf-8") as f:
        json.dump({"w": w, "h": h}, f)
    log(f"saved {os.path.basename(stem)}.bin ({w}x{h})")


def save_projectile(out, w, h, cells):
    """The projectile grid sent right after the walk map of the same load: <area>_seed<seed>.projectile.bin."""
    area = current_area(out)
    if area is None or time.time() - float(area.get("written", 0)) > FRESH_SECONDS:
        log(f"got a {w}x{h} projectile grid outside a fresh area load; not saved")
        return
    stem = os.path.join(out, f"{area['id']}_seed{area['seed']}")
    if os.path.exists(stem + ".projectile.bin"):
        return
    with open(stem + ".projectile.bin.tmp", "wb") as f:
        f.write(cells)
    replace(stem + ".projectile.bin.tmp", stem + ".projectile.bin")
    log(f"saved {os.path.basename(stem)}.projectile.bin ({w}x{h})")


OBJECTS = {}  # current.json nonce -> the tile objects reported for that area load


def save_object(out, obj):
    """Adds one area transition / waypoint of the area being generated to <area>_seed<seed>.transitions.json."""
    area = current_area(out)
    if area is None or time.time() - float(area.get("written", 0)) > FRESH_SECONDS:
        log(f"got a tile object ({obj.get('objectType')}) outside a fresh area load; not saved")
        return
    found = OBJECTS.setdefault(area.get("nonce"), [])
    found.append({k: obj.get(k) for k in ("objectType", "model", "x", "y", "rotation", "z")})
    stem = os.path.join(out, f"{area['id']}_seed{area['seed']}")
    with open(stem + ".transitions.json.tmp", "w", encoding="utf-8") as f:
        json.dump({"area": area["id"], "seed": area["seed"], "objects": found}, f, indent=1)
    replace(stem + ".transitions.json.tmp", stem + ".transitions.json")
    log(f"{os.path.basename(stem)}: {obj.get('objectType', '').rsplit('/', 1)[-1]} at ({obj.get('x')}, {obj.get('y')}) "
        f"z {obj.get('z')} (raw {obj.get('rawZ')}, height offset {obj.get('heightOffset')})")


QUEST_OBJECTS = {}  # Only the latest fresh (area, seed, nonce), separate from navigable transitions.
QUEST_TYPES = {"Metadata/Chests/QuestChests/RhoaChest" + suffix for suffix in ("", "2", "3")} | {
    "Metadata/QuestObjects/WaterCave/GlyphWall", "Metadata/Chests/TutorialSupportGemChest"}


def save_quest_object(out, obj):
    """Optional a1q4 tile placements; never add a chest/wall to the area's transition list."""
    if obj.get("objectType") not in QUEST_TYPES:
        return
    area = current_area(out)
    if area is None or area.get("nonce") is None or time.time() - float(area.get("written", 0)) > FRESH_SECONDS:
        return
    key = (area["id"], area["seed"], area["nonce"])
    if key not in QUEST_OBJECTS:
        QUEST_OBJECTS.clear()
        QUEST_OBJECTS[key] = []
    found = QUEST_OBJECTS[key]
    found.append({k: obj.get(k) for k in ("objectType", "model", "x", "y", "rotation", "z", "rawZ", "heightOffset", "code")})
    stem = os.path.join(out, f"{area['id']}_seed{area['seed']}")
    with open(stem + ".objects.json.tmp", "w", encoding="utf-8") as f:
        json.dump({"area": area["id"], "seed": area["seed"], "objects": found}, f, indent=1)
    replace(stem + ".objects.json.tmp", stem + ".objects.json")
    log(f"{os.path.basename(stem)}: quest object {obj['objectType'].rsplit('/', 1)[-1]} at ({obj.get('x')}, {obj.get('y')})")


DOODADS = {}  # current.json nonce -> [model, x, y, angle] of every doodad placed in that area load
ROOMS = {}


def save_room(out, obj):
    area = current_area(out)
    if area is None or time.time()-float(area.get("written",0))>FRESH_SECONDS:
        return
    if not str(obj.get("path", "")).lower().endswith(".arm") or not 0<=obj.get("code",-1)<=7:
        return
    key=(area["id"],area["seed"],area.get("nonce"))
    if key not in ROOMS:
        ROOMS.clear()
        ROOMS[key]=[]
    found=ROOMS[key]
    value={k:obj[k] for k in ("path","x","y","width","height","code")}
    if value not in found:
        found.append(value)
    stem=os.path.join(out,f"{area['id']}_seed{area['seed']}")
    with open(stem+".rooms.json.tmp","w",encoding="utf-8") as f:
        json.dump({"area":area["id"],"seed":area["seed"],"rooms":found},f)
    replace(stem+".rooms.json.tmp",stem+".rooms.json")


def save_doodads(out, batch):
    """Adds placed doodads of the area being generated to <area>_seed<seed>.doodads.json (models listed once)."""
    area = current_area(out)
    if area is None or time.time() - float(area.get("written", 0)) > FRESH_SECONDS:
        return
    found = DOODADS.setdefault(area.get("nonce"), [])
    found.extend(batch)
    models = sorted({d[0] for d in found})
    index = {m: i for i, m in enumerate(models)}
    stem = os.path.join(out, f"{area['id']}_seed{area['seed']}")
    with open(stem + ".doodads.json.tmp", "w", encoding="utf-8") as f:
        json.dump({"area": area["id"], "seed": area["seed"], "models": models,
                   "doodads": [[index[d[0]], d[1], d[2], round(d[3], 4)] for d in found]}, f)
    replace(stem + ".doodads.json.tmp", stem + ".doodads.json")
    if len(found) == len(batch) or len(found) // 1000 != (len(found) - len(batch)) // 1000:
        log(f"{os.path.basename(stem)}: {len(found)} doodads")


def save_cell_heights(out, info, cells):
    """Every cell's ground height (i16, row by row, the client's 0x141d84a10) of one generated area:
    <area>_seed<seed>.heights.bin, named like the generation trace."""
    if not info.get("area"):
        return
    stem = os.path.join(out, f"{info['area']}_seed{info['seed']}")
    with open(stem + ".heights.bin.tmp", "wb") as f:
        f.write(cells)
    replace(stem + ".heights.bin.tmp", stem + ".heights.bin")
    log(f"{os.path.basename(stem)}: cell heights {info.get('w')}x{info.get('h')}, map +0x104 {info.get('storey')}, "
        f"{info.get('rects')} height rectangles, {info.get('clipped')} clipped to i16")


def save_failed_generation(out, gen):
    """A generation that threw: the trace up to the throw, <area>_seed<seed>.failtrace.json (not .gen.json, so the gate
    does not take it for a finished load); each later throw of the same load rewrites it."""
    if not gen.get("area"):
        return
    stem = os.path.join(out, f"{gen['area']}_seed{gen['seed']}")
    with open(stem + ".failtrace.json.tmp", "w", encoding="utf-8") as f:
        json.dump(gen, f)
    replace(stem + ".failtrace.json.tmp", stem + ".failtrace.json")
    log(f"{os.path.basename(stem)}: failed generation trace, {len(gen.get('draws') or [])} draws, {len(gen.get('tiles') or [])} tiles")


def save_probe(out, probe):
    """One probe record (walkmaps.js PROBE_AREAS), appended to probes.jsonl."""
    with open(os.path.join(out, "probes.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(probe) + "\n")


def save_generation(out, gen):
    """The level generator's trace of one area load (random draws, placed tiles): <area>_seed<seed>.gen.json, named by
    the area and seed the client generated (a retry with another seed gets its own file)."""
    if not gen.get("area"):
        return
    stem = os.path.join(out, f"{gen['area']}_seed{gen['seed']}")
    with open(stem + ".gen.json.tmp", "w", encoding="utf-8") as f:
        json.dump(gen, f)
    replace(stem + ".gen.json.tmp", stem + ".gen.json")
    log(f"{os.path.basename(stem)}: generation trace, {len(gen.get('draws') or [])} draws, {len(gen.get('tiles') or [])} tiles, "
        f"{gen.get('w')}x{gen.get('h')}, tile hash {gen.get('tileHash')}")
    # An area that places no doodads sends no batch: write its empty list, so a load with none is told apart from one
    # not captured. Batches that come after the trace (flushed on a timer) rewrite it with the whole list.
    area = current_area(out)
    if (area is not None and area["id"] == gen["area"] and str(area["seed"]) == str(gen["seed"])
            and area.get("nonce") not in DOODADS and time.time() - float(area.get("written", 0)) <= FRESH_SECONDS):
        DOODADS[area.get("nonce")] = []
        with open(stem + ".doodads.json.tmp", "w", encoding="utf-8") as f:
            json.dump({"area": gen["area"], "seed": gen["seed"], "models": [], "doodads": []}, f)
        replace(stem + ".doodads.json.tmp", stem + ".doodads.json")


def current_area(out):
    path = os.path.join(out, "current.json")
    try:
        with open(path, encoding="utf-8") as f:
            area = json.load(f)
        return area if "id" in area and "seed" in area else None
    except (OSError, ValueError):
        return None


if __name__ == "__main__":
    main()
