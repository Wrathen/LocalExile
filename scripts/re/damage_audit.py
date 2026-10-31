"""Bounded, offline 3.29.3b damage/stat audit; never loads or executes Game.exe.

Requires pefile and capstone. See Agents/docs/re/client-damage-calculation.md.
Output is local evidence, not a REA Evidence ID or proof of runtime execution.
"""
import argparse
from bisect import bisect_right
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

EXPECTED_SHA256 = "78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


class Image:
    def __init__(self, path, leaf_ranges=()):
        import capstone
        import pefile
        self.path = path.resolve()
        self.raw = path.read_bytes()
        if sha(self.raw) != EXPECTED_SHA256:
            raise ValueError("Unexpected executable SHA256; revalidate addresses before adapting this audit")
        self.pe = pefile.PE(data=self.raw, fast_load=True)
        self.pe.parse_data_directories(directories=[3])
        self.base = self.pe.OPTIONAL_HEADER.ImageBase
        self.decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
        self.decoder.detail = True
        self.decoder.skipdata = True
        self.entries = {e.struct.BeginAddress: e.struct for e in self.pe.DIRECTORY_ENTRY_EXCEPTION}
        self.starts = sorted(self.entries)
        self.parents = {}
        for start, entry in self.entries.items():
            header = self.pe.get_data(entry.UnwindData, 4)
            if header[0] >> 3 & 4:  # UNW_FLAG_CHAININFO; align trailing RUNTIME_FUNCTION to DWORD
                offset = entry.UnwindData + ((4 + 2 * header[2] + 3) & ~3)
                parent, end, unwind = struct.unpack("<III", self.pe.get_data(offset, 12))
                if parent not in self.entries or (self.entries[parent].EndAddress,
                        self.entries[parent].UnwindData) != (end, unwind):
                    raise ValueError(f"Unresolved chained unwind at {start:#x}")
                self.parents[start] = parent
        self.groups = {}
        for start, entry in self.entries.items():
            self.groups.setdefault(self.root(start) + self.base, []).append(
                (self.base + start, self.base + entry.EndAddress))
        self.leaf_ranges = list(leaf_ranges)
        for low, high in self.leaf_ranges:
            if high <= low or high-low > 4096 or low in self.groups:
                raise ValueError("Invalid explicit leaf range")
            section = self.pe.get_section_by_rva(low-self.base)
            if section is None or not section.Characteristics & 0x20000000:
                raise ValueError("Leaf range is not executable")
            self.groups[low] = [(low, high)]
        self.cache = {}

    def root(self, rva):
        seen = set()
        while rva in self.parents:
            if rva in seen:
                raise ValueError("Cyclic unwind chain")
            seen.add(rva)
            rva = self.parents[rva]
        return rva

    def owner(self, va):
        rva = va - self.base
        index = bisect_right(self.starts, rva) - 1
        if index >= 0 and rva < self.entries[self.starts[index]].EndAddress:
            return self.base + self.root(self.starts[index])
        for low, high in self.leaf_ranges:
            if low <= va < high:
                return low
        return None

    def read(self, va, count):
        if va < self.base or va + count > self.base + self.pe.OPTIONAL_HEADER.SizeOfImage:
            raise ValueError(f"Unmapped VA {va:#x}")
        data = self.pe.get_data(va - self.base, count)
        if len(data) != count:
            raise ValueError("Short PE read")
        return data

    def function(self, va):
        owner = self.owner(va)
        if owner is None:
            raise ValueError(f"No unwind owner for {va:#x}; do not guess a function boundary")
        if owner not in self.cache:
            spans = sorted(self.groups[owner])
            instructions = []
            for low, high in spans:
                part = list(self.decoder.disasm(self.read(low, high-low), low))
                if sum(i.size for i in part) != high-low:
                    raise ValueError(f"Incomplete disassembly at {low:#x}")
                instructions.extend(part)
            self.cache[owner] = (spans, instructions)
        return owner, *self.cache[owner]

    def executable_sections(self):
        for section in self.pe.sections:
            if section.Characteristics & 0x20000000:
                yield self.base + section.VirtualAddress, section.get_data()

    def verified(self, address):
        if self.owner(address) is None:
            return None
        _, _, instructions = self.function(address)
        return next((i for i in instructions if i.address == address and i.id), None)

    def code_refs(self, calls, keys):
        """Filter byte candidates through actual instruction boundaries in unwind bodies.

        Does not discover indirect calls, register-passed/dynamic stat keys or leaf functions without .pdata.
        """
        from capstone.x86 import X86_OP_IMM, X86_OP_REG
        hits, unowned = [], []
        for low, data in self.executable_sections():
            for m in re.finditer(b"[\xe8\xe9\xba]", data):
                pos = m.start()
                if pos + 5 > len(data):
                    continue
                address = low + pos
                opcode = data[pos]
                value = struct.unpack_from("<I" if opcode == 0xba else "<i", data, pos+1)[0]
                target = value if opcode == 0xba else address + 5 + value
                if target not in (keys if opcode == 0xba else calls):
                    continue
                ins = self.verified(address)
                if ins is None:
                    if self.owner(address) is None:
                        unowned.append(dict(address=hex(address), target=hex(target), opcode=hex(opcode)))
                    continue
                if opcode == 0xba:
                    valid = (ins.mnemonic == "mov" and len(ins.operands) == 2
                        and ins.operands[0].type == X86_OP_REG and ins.reg_name(ins.operands[0].reg) == "edx"
                        and ins.operands[1].type == X86_OP_IMM and ins.operands[1].imm == target)
                else:
                    valid = (ins.mnemonic in ("call", "jmp") and ins.operands[0].type == X86_OP_IMM
                        and ins.operands[0].imm == target)
                if valid:
                    hits.append(dict(address=hex(address), owner=hex(self.owner(address)), target=hex(target),
                        kind="mov-edx-candidate" if opcode == 0xba else ins.mnemonic))
        return hits, unowned

    def function_record(self, va, stats):
        from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG, X86_REG_RIP
        owner, spans, instructions = self.function(va)
        rows = []
        for ins in instructions:
            row = dict(va=hex(ins.address), bytes=bytes(ins.bytes).hex(), asm=ins.mnemonic+" "+ins.op_str)
            if ins.id:
                for op in ins.operands:
                    if op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP:
                        address = ins.address + ins.size + op.mem.disp
                        note = dict(va=hex(address))
                        try:
                            value = self.read(address, 8)
                            note.update(raw=value.hex(), float32=struct.unpack("<f", value[:4])[0])
                            if not (-1e35 < note["float32"] < 1e35):
                                del note["float32"]
                        except (ValueError, struct.error):
                            pass
                        row.setdefault("rip", []).append(note)
                if (ins.mnemonic == "mov" and len(ins.operands) == 2 and ins.operands[0].type == X86_OP_REG
                        and ins.reg_name(ins.operands[0].reg) == "edx" and ins.operands[1].type == X86_OP_IMM):
                    key = ins.operands[1].imm
                    if 0 < key <= len(stats):
                        row["statKeyCandidate"] = key
                        row["statIdCandidate"] = stats[key-1]["Id"]
            rows.append(row)
        ranges = [dict(start=hex(a), endExclusive=hex(b), bytes=b-a,
            fileOffset=self.pe.get_offset_from_rva(a-self.base), sha256=sha(self.read(a,b-a))) for a,b in spans]
        return dict(request=hex(va), owner=hex(owner), ranges=ranges, instructions=rows,
            boundary="explicit-analyst-leaf-range" if (owner, spans[-1][1]) in self.leaf_ranges else "PE-unwind-root-and-chains",
            undecodedBytes=sum(i.size for i in instructions if not i.id))

    def virtual_refs(self, offsets):
        """Candidate indirect memory calls/jumps, not resolved vtable receivers.

        FF /2 and /4 with disp8/disp32; optionally one REX prefix. Register calls,
        address-size/other prefixes and unwind-less leaves are outside this scan.
        """
        from capstone.x86 import X86_OP_MEM
        hits, unowned, seen = [], [], set()
        for low, data in self.executable_sections():
            for match in re.finditer(b"\xff", data):
                pos = match.start()
                if pos + 2 >= len(data):
                    continue
                modrm = data[pos+1]
                mode, operation, rm = modrm >> 6, (modrm >> 3) & 7, modrm & 7
                if mode not in (1, 2) or operation not in (2, 4):
                    continue
                disp_at = pos + 2 + (rm == 4)
                width = 1 if mode == 1 else 4
                if disp_at + width > len(data):
                    continue
                disp = int.from_bytes(data[disp_at:disp_at+width], "little", signed=True)
                if disp not in offsets:
                    continue
                starts = [pos]
                if pos and 0x40 <= data[pos-1] <= 0x4f:
                    starts.insert(0, pos-1)
                for start in starts:
                    address = low + start
                    ins = self.verified(address)
                    if ins is None:
                        if self.owner(address) is None:
                            unowned.append(hex(address))
                        continue
                    if (address not in seen and ins.mnemonic in ("call", "jmp")
                            and len(ins.operands) == 1 and ins.operands[0].type == X86_OP_MEM
                            and ins.operands[0].mem.disp in offsets):
                        seen.add(address)
                        hits.append(dict(address=hex(address), owner=hex(self.owner(address)),
                            asm=ins.mnemonic+" "+ins.op_str, bytes=bytes(ins.bytes).hex(),
                            displacement=ins.operands[0].mem.disp, receiver="unresolved"))
        return hits, sorted(set(unowned))

    def rip_refs(self, targets):
        """RIP-relative LEA candidates, checked at decoded instruction starts.

        Does not cover MOV loads, computed addresses, other prefixes or relocations.
        """
        from capstone.x86 import X86_OP_MEM, X86_REG_RIP
        hits = []
        for low, data in self.executable_sections():
            for match in re.finditer(b"\x8d", data):
                pos = match.start()
                if pos+6 > len(data) or data[pos+1] & 0xc7 != 5:
                    continue
                target = low+pos+6+struct.unpack_from("<i", data, pos+2)[0]
                if target not in targets:
                    continue
                start = pos-1 if pos and 0x40 <= data[pos-1] <= 0x4f else pos
                ins = self.verified(low+start)
                if (ins is not None and ins.mnemonic == "lea" and any(
                        op.type == X86_OP_MEM and op.mem.base == X86_REG_RIP
                        and ins.address+ins.size+op.mem.disp == target for op in ins.operands)):
                    hits.append(dict(address=hex(ins.address), owner=hex(self.owner(ins.address)),
                        target=hex(target), asm=ins.mnemonic+" "+ins.op_str, bytes=bytes(ins.bytes).hex()))
        return hits


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, default=Path("Client/Game.exe"))
    parser.add_argument("--stats", type=Path, default=Path("data/tables/Stats.json"))
    parser.add_argument("--python-libs", type=Path, help="Existing dependency directory; no installation performed")
    parser.add_argument("--out", type=Path, required=True, help="New evidence directory under Tools/re/out")
    parser.add_argument("--functions", type=lambda s:int(s,0), nargs="*", default=[])
    parser.add_argument("--stat-refs", type=int, nargs="*", default=[])
    parser.add_argument("--call-refs", type=lambda s:int(s,0), nargs="*", default=[])
    parser.add_argument("--pointer-refs", type=lambda s:int(s,0), nargs="*", default=[])
    parser.add_argument("--virtual-offsets", type=lambda s:int(s,0), nargs="*", default=[])
    parser.add_argument("--rip-refs", type=lambda s:int(s,0), nargs="*", default=[])
    parser.add_argument("--leaf-ranges", nargs="*", default=[], help="Explicit analyst-reviewed start:endExclusive VAs")
    args = parser.parse_args()
    if args.python_libs:
        sys.path.insert(0, str(args.python_libs.resolve()))
    out = args.out.resolve()
    evidence_root = Path("Tools/re/out").resolve()
    if evidence_root not in out.parents or out.exists():
        raise ValueError("Output must be a new directory under this checkout's Tools/re/out")
    leaf_ranges = [tuple(int(v,0) for v in text.split(":")) for text in args.leaf_ranges]
    image = Image(args.exe, leaf_ranges)
    stats_raw = args.stats.read_bytes()
    stats = json.loads(stats_raw.decode("utf-8-sig"))
    out.mkdir(parents=True)
    report = dict(binary=dict(path=str(image.path), sha256=sha(image.raw), imageBase=hex(image.base)),
        stats=dict(path=str(args.stats.resolve()), sha256=sha(stats_raw), rows=len(stats)),
        method="Offline PE bytes + chained unwind ownership + Capstone instruction boundaries",
        authority="static-artifact", runtimeExecuted=False, functions=[],
        versions=dict(capstone=sys.modules["capstone"].__version__, pefile=sys.modules["pefile"].__version__),
        limitations=["Static observations only; no official-server or client-runtime execution",
            "mov edx,key is a candidate, not a stat read until its getter/data flow is checked",
            "Direct-reference scan excludes indirect calls/dynamic keys/unwind-less leaves",
            "Raw pointer occurrences do not establish vtable ownership or reachability"])
    targets = {}
    for va in args.functions:
        record = image.function_record(va, stats)
        report["functions"].append(record)
        lines = []
        for ins in record["instructions"]:
            note = (" ; candidate " + ins["statIdCandidate"]) if "statIdCandidate" in ins else ""
            if "rip" in ins:
                note += " ; RIP " + json.dumps(ins["rip"], separators=(",", ":"))
            lines.append(ins["va"]+": "+ins["asm"]+note)
        (out/(record["owner"]+".asm")).write_text("\n".join(lines)+"\n", encoding="utf-8")
        targets[record["owner"]] = dict(start=record["owner"], ranges=record["ranges"], decompile=True,
            scope="SHA-bound PE unwind roots/chains; offline, no whole-program autoanalysis")
        # Add exact callee bodies as signature/context targets, without recursively decompiling them.
        for ins in record["instructions"]:
            if ins["asm"].startswith("call 0x"):
                callee = int(ins["asm"].split()[1],16)
                if callee in image.groups:
                    targets.setdefault(hex(callee), dict(start=hex(callee), ranges=[dict(start=hex(a),
                        endExclusive=hex(b)) for a,b in image.groups[callee]], decompile=False, scope="Callee context"))
    report["references"], report["unownedCandidates"] = image.code_refs(set(args.call_refs), set(args.stat_refs))
    report["virtualCandidates"], report["unownedVirtualCandidates"] = image.virtual_refs(set(args.virtual_offsets)) if args.virtual_offsets else ([], [])
    report["ripLeaReferences"] = image.rip_refs(set(args.rip_refs)) if args.rip_refs else []
    report["pointerOccurrences"] = []
    for va in args.pointer_refs:
        for m in re.finditer(re.escape(struct.pack("<Q", va)), image.raw):
            try:
                occurrence = image.base+image.pe.get_rva_from_offset(m.start())
            except Exception:
                continue
            report["pointerOccurrences"].append(dict(target=hex(va), va=hex(occurrence), fileOffset=m.start()))
    save(out/"audit.json", report)
    save(out/"targets.json", list(targets.values()))
    print(json.dumps(dict(output=str(out), sha256=sha(image.raw), functions=len(report["functions"]),
        references=len(report["references"]), unownedCandidates=len(report["unownedCandidates"]),
        pointerOccurrences=len(report["pointerOccurrences"]))))


if __name__ == "__main__":
    main()
