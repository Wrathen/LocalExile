# usage: python -I scripts/re/anim_contacts.py [--json data/animation-contacts.json]
#
# The "contact" keyframe of every character class's animations: for each Characters.dat AOFile, follows the .ao's
# "extends" chain to its AnimationController metadata (.amd, e.g. Art/Models/CHARACTERS/Witch/rig.amd) and reads, per
# animation, its length and the time of its "contact" event, both in ms. That keyframe is when an attack lands and when
# a spell is released (Witch spell_projectile_wand: 1066 ms long, contact at 599). The Gateway reads the output to
# release projectiles at the right moment of the cast (Agents/docs/protocol/11-skills.md §9).
#
# .amd text (UTF-16 without a BOM): a version line, a count line, then per animation:
#   "<name>" / once|loop / <length ms> / <event count> / per event: "<event>" / <time ms> / <x y z>, then two lines.
# The reader is ot_components.amd_animations (shared with monster_takehit.py).
# Files come from the GGPK through scripts/re/ggdump (see ot_components.py) and are cached in Tools/re/out/ot.
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ot_components import REPO, amd_animations, amd_of  # noqa: E402


def contacts(amd):
    """{animation: [length ms, contact ms]} for the .amd's animations that have a contact event."""
    out = {}
    for a in amd_animations(amd) or []:
        contact = next((int(t) for ev, t, _ in a["events"] if ev == "contact" and t.lstrip("-").isdigit()), None)
        if contact is not None:
            out[a["name"]] = [a["length"], contact]
    return out


def main():
    dest = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else os.path.join(REPO, "data", "animation-contacts.json")
    chars = json.load(open(os.path.join(REPO, "data", "tables", "Characters.json"), encoding="utf-8"))
    result = {}
    for c in chars:
        ao = c.get("AOFile")
        if not ao or ao in result:
            continue
        amd = amd_of(ao)
        result[ao] = {"amd": amd, "animations": contacts(amd) if amd else {}}
        print(f"{c['Name']}: {ao} -> {amd}, {len(result[ao]['animations'])} animations with a contact", file=sys.stderr)
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=1)
    print(f"wrote {dest}", file=sys.stderr)


if __name__ == "__main__":
    main()
