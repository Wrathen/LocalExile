# usage: python -I scripts/re/monster_takehit.py [--json data/monster-takehit.json]
#
# For every MonsterVarieties row: the indexes of its TakeHit clips in the rig's animation list. The client's
# S->C 0x1b2 "play animation" takes an index into the actor's animation list, and that list is the rig .amd's
# animations in file order (0-based). Live 2026-10-25 on the Sanctum zombie: index 156 = flinch_unarmed and
# 151 = flinch_pitchfork played the flinch *with its SoundEvent*, both at the position of the clip in
# Art/Models/MONSTERS/skeleton/rig.amd.
#
# Chain: MonsterVarieties.ACTFiles (.act maps Animation type -> clip, and names the .ao) and AOFiles; the .ao's
# "extends" chain holds "AnimationController { metadata = <rig>.amd }". Files come from the GGPK through
# scripts/re/ggdump (cached in Tools/re/out/ot, see scripts/re/ot_components.py). Everything is fetched in batches
# first: one ggdump process per file was minutes per hundred rows.
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ot_components import REPO, amd_animations, amd_map, animation_controller, extends_of, fetch, read_text  # noqa: E402


def fetch_ao_chain(paths):
    """Fetch these .ao files and every "extends" parent; return all .ao paths seen."""
    known, todo = set(), set(paths)
    while todo:
        fetch(sorted(todo))
        known |= todo
        nxt = set()
        for ao in todo:
            text = read_text(ao)
            if text is None or animation_controller(text):
                continue
            for parent in extends_of(text):
                p = parent if parent.endswith(".ao") else parent + ".ao"
                if p not in known:
                    nxt.add(p)
        todo = nxt
    return known


def expand_clips(value, entries, seen):
    """The clips a TakeHit value names: a comma list whose items may be "@Name" aliases to another such list.

    A variant's condition ("flinch_small:max_duration=0.45") is stripped: it picks the small/large reaction by the
    hit's stun duration, which the server does not model.
    """
    clips = []
    for item in value.split(","):
        clip = item.split(":", 1)[0].strip()
        if not clip:
            continue
        if clip.startswith("@"):
            if clip[1:] and clip[1:] not in seen:
                clips += expand_clips(entries.get(clip[1:]) or "", entries, seen | {clip[1:]})
        elif clip not in clips:
            clips.append(clip)
    return clips


def act_takehit(act):
    """The clips an .act maps TakeHit to ("@Name" aliases and "clip:condition" variants resolved) and the .ao it animates."""
    text = read_text(act)
    if text is None:
        return [], None
    entries = {m.group(1): m.group(2).strip() for m in re.finditer(r'^\s*([A-Za-z0-9_]+)\s*=\s*"([^"]*)"\s*;', text, re.M)}
    clips = expand_clips(entries.get("TakeHit") or "", entries, set())
    m = re.search(r'^\s*animated_object\s+"([^"]+)"', text, re.M)
    return clips, (m.group(1) if m else None)


def main():
    dest = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else os.path.join(REPO, "data", "monster-takehit.json")
    rows = json.load(open(os.path.join(REPO, "data", "tables", "MonsterVarieties.json"), encoding="utf-8"))
    variants = [r for r in rows if isinstance(r, dict) and r.get("Id")]

    # One batch per kind of file: the per-file fetch opened the 69 GB GGPK for every row before.
    acts = sorted({a for r in variants for a in (r.get("ACTFiles") or [])})
    aos = sorted({a for r in variants for a in (r.get("AOFiles") or [])})
    print(f"fetching {len(acts)} .act and {len(aos)} .ao", file=sys.stderr, flush=True)
    fetch(acts)
    fetch(aos)
    # The .act's own animated_object is the model that act is for; MonsterVarieties.AOFiles is the fallback (a
    # variety can list several weapon variants, only one of which the type uses).
    act_aos = set()
    for act in acts:
        m = re.search(r'^\s*animated_object\s+"([^"]+)"', read_text(act) or "", re.M)
        if m:
            act_aos.add(m.group(1))
    print(f"fetching the .ao extends chain ({len(act_aos)} from the acts)", file=sys.stderr, flush=True)
    all_aos = fetch_ao_chain(aos + sorted(act_aos))
    # One ao -> amd map for everything fetched, and the .amd files themselves in one batch: amd_of was walking the
    # chains (and fetching each file) again for every row before.
    amds = amd_map(all_aos, fetch_missing=False)
    fetch(sorted({amd for amd in amds.values() if amd}))
    print(f"fetched; indexing {len(variants)} monster varieties", file=sys.stderr, flush=True)

    cache, out = {}, {}
    for n, row in enumerate(variants, 1):
        key = (tuple(row.get("ACTFiles") or []), tuple(row.get("AOFiles") or []))
        if key not in cache:
            index = None
            for act in key[0]:
                clips, act_ao = act_takehit(act)
                if not clips:
                    continue
                for ao in ([act_ao] if act_ao else []) + list(key[1]):
                    amd = amds.get(ao)
                    names = [a["name"] for a in amd_animations(amd, fetch_missing=False) or []] if amd else []
                    if not names:
                        continue
                    hits = []
                    for clip in clips:
                        if clip in names and names.index(clip) not in hits:
                            hits.append(names.index(clip))
                    if hits:
                        index = (hits, [c for c in clips if c in names], amd, act)
                        break
                if index:
                    break
            cache[key] = index
        if cache[key]:
            indexes, clips, amd, act = cache[key]
            out[row["Id"]] = {"index": indexes[0], "indexes": indexes, "clips": clips, "amd": amd, "act": act}
        if n % 1000 == 0:
            print(f"  {n}/{len(variants)}", file=sys.stderr, flush=True)
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1, sort_keys=True)
    print(f"wrote {dest}: {len(out)} of {len(variants)} monster varieties have a TakeHit index", file=sys.stderr, flush=True)


if __name__ == "__main__":
    main()
