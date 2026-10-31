"""Generate src/GameServer.Gateway/UniqueItemSeed.cs from Path of Building's unique item export.

Path of Building is a read-only third-party reference. Its src/Export/Uniques/*.lua files list the unique name, base
type and Mods.dat ids; src/Data/Uniques/*.lua has the human-readable text. The generated C# file is committed so the
Gateway does not need the tool at runtime.

Usage (repository root):

    python scripts/re/unique_items.py [--pob <Path of Building folder>]

The default folder is Tools/PathOfBuilding (gitignored). The generated rows are the seed for the `catalog_entries` kind
`unique_item`. The base type is resolved against the extracted BaseItemTypes table; malformed PoB blocks without a
resolvable base are skipped and reported.

A block in the export:

    Tulfall
    {variant:1,2}Tornado Wand         the base, one line per variant when it changed
    {variant:3}Opal Wand
    Variant: Pre 3.16.0               metadata, in no fixed order
    Variant: Current
    Implicits: 2                      the next two lines are implicits (each may belong to some variants only),
    {variant:1,2}ModId                the lines after them explicits
    {variant:3}ModId[10,15]           [min,max] per stat when PoB overrides Mods.dat's roll

The last variant is the current one, so it is the one generated.
"""
import argparse
import json
import pathlib
import re
from collections import Counter, defaultdict

ROOT = pathlib.Path(".")
POB = ROOT / "Tools/PathOfBuilding"
OUT = ROOT / "src/GameServer.Gateway/UniqueItemSeed.cs"

META = re.compile(
    r"^(Source|League|Radius|Limited to|Variant|Selected Variant|Selected Alt Variant|Requires|Upgrade|DropLevel|Level|"
    r"Item Level|Crafted|Implicits|Has Alt Variant|Corrupted|Sockets:|Energy Shield:|Armour:|Evasion:|Quality:|"
    r"This item can be anointed|[A-Z][a-z]+( [A-Za-z]+)* Item$)"
)
VARIANT = re.compile(r"^\{variant:([^}]*)\}(.+)$")
MOD = re.compile(r"^([A-Za-z0-9_]+)((?:\[-?\d+,-?\d+\])*)$")
RANGE = re.compile(r"\[(-?\d+),(-?\d+)\]")
# A line of example craft PoB keeps on the item; it is not the unique's own.
CRAFTED = "{crafted}"
FRACTURED = "{fractured}"
# The Eldritch ones have no state of their own: the client reads them from the implicit mods.
INFLUENCES = {f"{name} Item": name for name in ("Shaper", "Elder", "Crusader", "Redeemer", "Hunter", "Warlord")}
ROLL = re.compile(r"\((-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)\)|(?<![A-Za-z])(\d+(?:\.\d+)?)")


def blocks(path):
    text = path.read_text(encoding="utf-8")
    for block in re.findall(r"\[\[(.*?)\]\]", text, re.S):
        lines = [line.strip() for line in block.strip().splitlines() if line.strip()]
        # A block pasted from the game keeps its item text header (That Which Was Taken).
        lines = [line for line in lines if not line.startswith(("Item Class:", "Rarity:"))]
        if len(lines) < 2:
            continue
        yield lines[0], lines[1:]


def tagged(line):
    """A line's text and the variants it belongs to (None: every variant)."""
    match = VARIANT.match(line)
    if not match:
        return line, None
    return match.group(2).strip(), {int(value) for value in match.group(1).split(",") if value.strip().isdigit()}


def in_variant(line, chosen):
    text, allowed = tagged(line)
    return text if allowed is None or chosen in allowed else None


def split_block(lines, known_base):
    """The base lines, the metadata lines, and the implicit and explicit mod lines of a block. Metadata can come before
    the base (Gorgon's Gaze, Replica Gifts from Above, Echoes of Creation)."""
    start = next((i for i, line in enumerate(lines) if known_base(tagged(line)[0])), 0)
    count = start + 1
    while count < len(lines) and VARIANT.match(lines[count]) and known_base(tagged(lines[count])[0]):
        count += 1
    bases, rest = lines[start:count], lines[:start] + lines[count:]
    marker = next((i for i, line in enumerate(rest) if line.startswith("Implicits:")), None)
    if marker is None:
        meta = [line for line in rest if META.match(line)]
        return bases, meta, None, [line for line in rest if not META.match(line)]
    implicits = int(rest[marker].split(":", 1)[1].strip() or 0)
    # Metadata can follow the marker too, so the implicit lines are counted among the mod lines only.
    after = rest[marker + 1:]
    mods = [line for line in after if not META.match(line)]
    return bases, rest[:marker] + [line for line in after if META.match(line)], mods[:implicits], mods[implicits:]


def by_text(text, text_map, known_mods):
    """A mod the export left as text: its ModTextMap candidates that are unique mods, the one whose roll matches when
    several qualify. None when the text cannot be tied to one row."""
    rolls = [(float(m.group(1)), float(m.group(2))) if m.group(1) else (float(m.group(3)), float(m.group(3)))
             for m in ROLL.finditer(text)]
    key = ROLL.sub("#", text).lower().lstrip("+")
    rows = [known_mods[i] for i in text_map.get(key, []) if i in known_mods and known_mods[i]["GenerationType"] == 3]
    def fits(row):
        return all(sorted(roll) == sorted((row.get(f"Stat{i + 1}Min"), row.get(f"Stat{i + 1}Max")))
                   for i, roll in enumerate(rolls))
    matching = [row for row in rows if fits(row)]
    if len(matching) == 1:
        return matching[0]["Id"], []
    if len(rows) == 1 and len(rolls) == 1:
        low, high = sorted(rolls[0])
        return (rows[0]["Id"], [[int(low), int(high)]]) if low == int(low) and high == int(high) else None
    return None


def resolve(lines, chosen, known_mods, text_map, unknown, flags):
    """The chosen variant's mods: (id, the [min, max] PoB gives per stat, empty for Mods.dat's own)."""
    result = []
    for line in lines:
        text = in_variant(line, chosen)
        if text is None or text.startswith(CRAFTED):
            continue
        if text in ("Corrupted", "Mirrored") or text in INFLUENCES:
            flags.add(text)
            continue
        if META.match(text):
            continue
        text = text.removeprefix(FRACTURED)
        match = MOD.match(text)
        if match and match.group(1) in known_mods:
            result.append((match.group(1), [[int(low), int(high)] for low, high in RANGE.findall(match.group(2))]))
        elif (found := by_text(text, text_map, known_mods)) is not None:
            result.append(found)
        else:
            unknown.append(text)
    return result


def overrides(mod_rows, mods):
    """The ranges that differ from Mods.dat, as "id:stat:min:max". A mod shared by several variants of a unique keeps
    one Mods.dat row, and PoB gives this variant's own roll."""
    result = []
    for mod_id, ranges in mods:
        row = mod_rows[mod_id]
        for index, (low, high) in enumerate(ranges):
            if [row.get(f"Stat{index + 1}Min"), row.get(f"Stat{index + 1}Max")] != [low, high]:
                result.append(f"{mod_id}:{index}:{low}:{high}")
    return result


def text_map(path):
    """ModTextMap.lua: a mod's text with # for each number, lower case, to its Mods.dat ids."""
    source = path.read_text(encoding="utf-8", errors="replace")
    entries = re.findall(r'^\s*\["((?:[^"\\]|\\.)*)"\] = \{(.*?)\},?\s*$', source, re.M)
    return {key.lower(): re.findall(r'"([^"]+)"', ids) for key, ids in entries}


def name_key(name):
    """As UniqueCatalog.NameKey: Words writes some apostrophes curly, and has Broken Elegy without its article."""
    return name.replace("’", "'").strip().lower().removeprefix("the ")


def csharp(values):
    return "[" + ", ".join(json.dumps(value, ensure_ascii=False) for value in values) + "]"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pob", type=pathlib.Path, default=POB, help="the Path of Building folder")
    args = parser.parse_args()
    export = args.pob / "src/Export/Uniques"
    texts = text_map(export / "ModTextMap.lua")

    base = json.load(open(ROOT / "data/tables/BaseItemTypes.json", encoding="utf-8"))
    mods = json.load(open(ROOT / "data/tables/Mods.json", encoding="utf-8"))
    mod_rows = {row["Id"]: row for row in mods}
    words = json.load(open(ROOT / "data/tables/Words.json", encoding="utf-8"))
    client_names = {name_key(row["Text"]) for row in words if row["Wordlist"] == 6 and row["Text"]}
    by_name = defaultdict(list)
    for row in base:
        by_name[row["Name"]].append(row)

    def candidates(name):
        # Some newer bases carry a parenthesised subtype in PoB but not in the client table.
        return by_name.get(name) or by_name.get(name.split(" (", 1)[0]) or []

    rows = []
    skipped = []
    unknown = []
    for path in sorted(export.glob("*.lua")):
        if path.name == "ModTextMap.lua":
            continue
        for name, lines in blocks(path):
            if name_key(name) not in client_names:
                skipped.append(f"{path.name}: {name} (no unique name in Words)")
                continue
            bases, meta, implicit_lines, mod_lines = split_block(lines, candidates)
            chosen = sum(1 for line in meta if line.startswith("Variant:"))

            base_name = next((text for text in (in_variant(line, chosen) for line in bases) if text), None)
            base_name = base_name or tagged(bases[0])[0]
            found = candidates(base_name)
            if not found:
                skipped.append(f"{path.name}: {name} ({base_name})")
                continue
            # Prefer the ordinary base over a Royale/event duplicate with the same name.
            chosen_base = next((row for row in found if "Royale" not in row["Id"]), found[0])

            flags = set()
            lost = []
            implicits = resolve(implicit_lines or [], chosen, mod_rows, texts, lost, flags)
            explicits = resolve(mod_lines, chosen, mod_rows, texts, lost, flags)
            unknown += [f"{name}: {text}" for text in lost]
            own = [mod_id for mod_id, _ in implicits]
            base_implicits = [mods[key]["Id"] for key in chosen_base.get("Implicit_ModsKeys") or []]
            rows.append({
                "name": name,
                "base": chosen_base["Id"],
                "mods": [mod_id for mod_id, _ in explicits],
                # Only what differs from the base type, whose implicits ItemCatalog.Create rolls by itself.
                "implicits": None if implicit_lines is None or own == base_implicits else own,
                "ranges": overrides(mod_rows, implicits + explicits),
                "corrupted": "Corrupted" in meta or "Corrupted" in flags,
                "mirrored": "Mirrored" in flags,
                "influences": sorted(INFLUENCES[text] for text in {in_variant(line, chosen) for line in meta} | flags
                                     if text in INFLUENCES),
            })

    # Stable output; duplicate names (Stormblood, Grand Spectrum, Combat Focus) keep every base variant.
    rows.sort(key=lambda row: (row["name"].lower(), row["base"].lower()))
    names = Counter(row["name"] for row in rows)
    lines = [
        "// Generated from Path of Building src/Export/Uniques/*.lua for the target 3.29.3b data.",
        "// Do not edit by hand; regenerate with scripts/re/unique_items.py (a read-only third-party reference).",
        "using GameServer.Core.Protocol.Instance;",
        "",
        "/// <summary>The full unique-item seed: name, base type, Mods.dat ids and the unique's implicits. The catalogue",
        "/// table keeps the runtime rows; this array is the fresh-database fallback.</summary>",
        "static class UniqueItemSeed",
        "{",
        "    public static readonly UniqueCatalog.Definition[] All =",
        "    [",
    ]
    for row in rows:
        options = [csharp(row["mods"])]
        if row["implicits"] is not None:
            options.append(f"Implicits: {csharp(row['implicits'])}")
        if row["ranges"]:
            options.append(f"Ranges: {csharp(row['ranges'])}")
        if row["corrupted"]:
            options.append("Corrupted: true")
        if row["mirrored"]:
            options.append("Mirrored: true")
        if row["influences"]:
            options.append("Influences: " + " | ".join(f"ItemInfluence.{name}" for name in row["influences"]))
        lines.append(f'        new({json.dumps(row["name"], ensure_ascii=False)}, {json.dumps(row["base"])}, '
                     f'{", ".join(options)}),')
    lines += ["    ];", "}", ""]
    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"wrote {len(rows)} unique definitions to {OUT}")
    print("skipped:", len(skipped))
    for entry in skipped:
        print("  ", entry)
    print("lines left out, no Mods.dat row found:", len(unknown))
    for entry in sorted(unknown):
        print("  ", entry)
    print("corrupted:", sum(1 for row in rows if row["corrupted"]))
    print("with their own implicits:", sum(1 for row in rows if row["implicits"] is not None))
    print("with roll overrides:", sum(1 for row in rows if row["ranges"]))
    print("influenced:", sum(1 for row in rows if row["influences"]))
    print("duplicate names:", [(name, count) for name, count in names.items() if count > 1])


if __name__ == "__main__":
    main()
