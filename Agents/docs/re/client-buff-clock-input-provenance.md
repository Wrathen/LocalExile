# Buff clock applicability inputs: transport, defaults and update asymmetry (3.29.3b)

## Result and scope

The clock calculator's category/poison/bleed eligibility inputs have concrete
transport provenance. Actual Buff `+0x30` is a zero-extended **byte**, and actual
Buff `+0x38` is a transported **32-bit word**. Neither field is established as a
source Entity ID. The separate additive clock input `+0x24` defaults to zero in
the selected network construction paths and is not populated by their packet
copier.

An important asymmetry remains visible in the receiver: a partial update directly
refreshes `+0x30` but not `+0x38`; replacement directly copies `+0x24` but neither
`+0x30` nor `+0x38`. Sending the optional packet group therefore does not mean
that all of its fields replace the corresponding fields of an existing instance.
These observations do not establish automatic clock recalculation after a field
changes, nor the server mechanic producing its value.

This supplements [instance clocks](client-dot-routing.md#instance-clock-updates-are-separate-from-damage-magnitudes)
and [the existing-instance network contract](client-buff-network-clock-contract.md).
All addresses are preferred VAs for the original 3.29.3b executable, image base
`0x140000000`, SHA-256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
Only static existing artifacts were inspected: no client launch, attachment,
capture, server change or CI operation.

## Field identity through construction

`0x141e06290` receives the actual Buff in RCX and a construction descriptor in
R8. The assignments are unambiguous original instruction loads and stores:

| Actual Buff destination | Construction descriptor source | Width and original addresses |
|---|---|---|
| `+0x24` | `+0x30` | 32-bit copy, `0x141e062e8 → 0x141e062ec` |
| `+0x30` | `+0x3c` | `movzx` byte to 32-bit, `0x141e062fd → 0x141e06302` |
| `+0x38` | `+0x48` | 32-bit copy, `0x141e0630c → 0x141e06310` |

The retained Buff definition is a different pointer at actual Buff `+8`.
A decoded message's `+0x30` is also a different object/field: it is associated
with the resolved definition's ownership handle, not actual Buff `+0x30`.
Offsets alone are insufficient to identify any of these fields.

## Add/update packet: a grouped optional input

Decoder `0x141fdf870` reads the packet's flags byte and tests bit `0x01` at
`0x141fdf99d`. On a successful decode:

| Flag `0x01` | Decoded message `+0x76` | Decoded message `+0x7c` |
|---|---|---|
| Set | Reads one byte at `0x141fdf9a3..0x141fdf9b0` | Reads four bytes at `0x141fdf9b5..0x141fdf9c2`, applies network-to-host 32-bit conversion, stores at `0x141fdf9d0` |
| Clear | Writes byte zero at `0x141fdfa07` | Writes 32-bit zero at `0x141fdfa0b` |

The set branch then reads one additional byte into decoded `+0x77` and a
big-endian u16 into decoded `+0x78`. They belong to the same wire group but are
not the two clock applicability inputs investigated here.

The installed packet writer `0x141fdf430` provides a separate consistency check:
`0x141fdf50a..0x141fdf50e` starts the flags byte from `decoded.+76 != 0`.
`0x141fdf5b9..0x141fdf5bf` gates writing the whole optional group on that same
byte being nonzero. A zero `+0x76` with a nonzero `+0x7c` is consequently not
preserved by this writer's canonical omission/default behavior. The reader can
still decode an explicitly present group whose first byte is zero.

Copier `0x141e07220` maps decoded `+0x76` to descriptor `+0x3c` using a byte
load/store at `0x141e072cf/0x141e072d3`, and decoded `+0x7c` to descriptor
`+0x48` using a 32-bit load/store at `0x141e072e6/0x141e072e9`.
Combining these assignments with the constructor gives:

```text
wire u8       → message +76 → descriptor +3c → actual Buff +30 (zero-extended)
wire u32 BE   → message +7c → descriptor +48 → actual Buff +38 (32-bit bits)
```

The packet's separate 32-bit `+0x64` field follows descriptor `+0x34` to actual
Buff `+0x28`. It must not be substituted for the byte-valued `+0x30` field.
No table lookup or typed Entity resolution gives the latter a source-identity
meaning in these producer chains.

## The additive `+0x24` input and initial Entity serialization

Before either call to the packet copier, handler `0x141e11720` builds a fresh
local descriptor. Its 64-bit writes at `0x141e117ee` and `0x141e11954` place
`0x000000003f800000` at descriptor `+0x2c`. The low half initializes speed to
`1.0f`; the high half initializes **descriptor `+0x30` to integer zero**.
The copier does not write that field. New packet-created Buffs therefore receive
zero at actual Buff `+0x24` through the constructor shown above.

Initial Entity Buff deserialization is a separate representation. Entry reader
`0x141e114d0` builds the same default descriptor, including its write at
`0x141e11589`, then calls blob reader `0x141e06bb0` at `0x141e115fa`.
The latter reads raw little-endian fixed fields from its bounded byte reader:

- At `0x141e06d35/0x141e06d38`, it copies four bytes to descriptor `+0x48`.
- At `0x141e06d5c/0x141e06d60`, it copies the following byte to descriptor `+0x3c`.
- It does not assign descriptor `+0x30`; that field keeps the caller's zero.

The entry then enters `0x141e27e30 → 0x141e06290`, followed by normal insertion.
This is not the optional, big-endian packet layout reused verbatim. It supplies
the same two applicability fields through a different serialization format.

## Existing-instance receiver asymmetry

| Selected receiver path | Actual Buff `+0x24` | Actual Buff `+0x30` | Actual Buff `+0x38` |
|---|---|---|---|
| New instance construction | Copies descriptor value; zero in the selected network callers | Copies decoded/blob byte, zero-extended | Copies decoded/blob 32-bit word |
| Partial update in `0x141e11720` | No direct assignment | `decoded.+76 → Buff.+30` at `0x141e11ba8/0x141e11bac` | No direct assignment |
| Replacement through `0x141e067c0` | Copies replacement `+0x24` at `0x141e068f1/0x141e068f4` | No direct assignment | No direct assignment |

The replacement path constructs a temporary new Buff, but the replacement copier
does not transfer these latter two fields into the retained Buff. The partial
path's omitted optional group can clear retained `+0x30` while leaving retained
`+0x38` untouched. Neither path's **direct assignments** should be generalized
to say all callbacks and component subscribers are inert; those are distinct
boundaries, as recorded in the network contract.

## Consequence for clock eligibility

The established calculator `0x141f79160` reads actual Buff `+0x24` as a signed32
addend at `0x141f7927b`. After earlier pause guards and general timing terms,
the selected original branch at `0x141f792a7..0x141f792b1` implements:

```text
include category/poison/bleed timing terms if
    (actualBuff.field30 == 0) OR ((actualBuff.field38 & 1) != 0)
```

The first comparison uses all 32 bits; the second tests the low byte's bit zero.
For the selected network construction, `field30` can only be 0..255 because its
producer zero-extends a byte. Default omission therefore admits this branch
regardless of the other field; a nonzero byte requires bit zero of the word.
The existing report covers the particular category, poison and bleeding terms.
The additive `+0x24` is outside this eligibility gate.

These are representation and control-flow conclusions. The intended semantic
names of the byte and word, meanings of other word bits, gameplay reason for the
update asymmetry, additional non-network descriptor producers and any broader
write inventory remain unclosed. A field update does not itself demonstrate a
subsequent call to the clock calculator. No numerical DoT source publication or
resource deduction is established by this transport evidence.

## Retained verification

Ignored evidence: `Tools/re/out/client-damage-audit/`.
`r11-buff-clock-input-verify.py` writes complete original disassemblies,
per-range SHA-256 and `r11-buff-clock-input-provenance/verification.json`.
Result: **PASS**, 11 functions / 12 ranges / 2,372 instructions / 29 explicit
instruction anchors. The four original decoded gate instructions were interpreted
offline for all 65,536 combinations of the transported byte and low word byte;
all destinations matched the condition above. This does not execute target code,
model a complete packet exchange or establish runtime traffic.
