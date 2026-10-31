# Initial Entity Buff granted-value decoding (3.29.3b)

## Result and scope

The initial Entity Buff blob has the **same table-controlled eligibility** for
numeric granted values as the AddBuff message: only a nonzero
`BuffDefinitions.IsRecovery` permits their decoding. The two carriers use
different byte order. Initial blobs load each value directly as a little-endian
32-bit word; AddBuff reads each wire word and applies `ntohl`.

For the pinned `ignited`, `poison`, `righteous_fire` and `righteous_fire_aura`
definitions, the flag is false. Their initial granted-value arrays therefore
contain the declared number of zeros. Initial Entity serialization does not
close a separate source-magnitude channel for these four rows.

This report closes the selected receiving/copying contract. It does not establish
publication of Buff values into actor Stats or a source-instance damage formula.
The existing [Buff routing report](client-dot-routing.md) describes adjacent
consumers; [clock input provenance](client-buff-clock-input-provenance.md)
separately covers the duration/rate fields.

All evidence is static/offline. No client execution, attachment, new capture,
server operation or CI was performed. Preferred image base is `0x140000000`.
The pinned identities are:

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

## 1. Definition controls length and eligibility

The schema places `IsRecovery` at row `+0x7c`; the `GrantedStats` array begins
at `+0xdc`, with count at `+0xdc` and data pointer at `+0xe4`.
This is separate from `GrantedFlags` at `+0xcc/+0xd4`.
Native Stats key `k` corresponds to JSON row `k-1`.

Initial Entity Buff collection reader `0x141e114d0` calls descriptor reader
`0x141e06bb0` at `0x141e115fa`. Once the definition is resolved, the latter:

1. Passes zero as the fill value and the definition's `GrantedStats` count to
   resize helper `0x141e0b060`, targeting descriptor `+0x10`
   (`0x141e06eb5..0x141e06eca`). The selected caller initialized this descriptor
   with an empty vector.
2. Tests the definition byte at `+0x7c`. The zero branch at `0x141e06ed6`
   jumps directly to `0x141e06f1d`, leaving every declared slot zero and
   consuming **no numeric granted-value bytes**.
3. Otherwise loops over the vector and copies one raw 32-bit word per slot.
   `MOV EDX,[R8+RCX]` at `0x141e06f08` and `MOV [RAX],EDX` at
   `0x141e06f0c` preserve its bit pattern; there is no byte swap, scaling,
   floating conversion or per-slot native stat-key decoding in this loop.

The table name `IsRecovery` identifies the exact controlling field. It should
not be replaced by an inferred category test: the pinned `chilled` and `shocked`
rows also set this flag.

Each numeric load first checks whether four complete bytes remain.
The comparison allows a word ending exactly at the buffer boundary.
An incomplete word branches to the existing deserialization error path
`0x141e0700f`; it does not fabricate that word or advance its cursor.
Earlier complete words may already have been copied before that error.

## 2. Difference from AddBuff

AddBuff reader `0x141fdf870` likewise zero-initializes its packet `+0x48`
vector from the same definition count and tests the same `+0x7c` flag
at `0x141fdfcb8`. Its zero branch skips the numeric loop.

For an eligible slot, that loop calls the stream reader at `0x141fdfcdc`,
loads the wire word into ECX and calls the import at `0x142f43e58`,
verified as **`ntohl`**, at `0x141fdfce3` before storing the result.
For example, value `0x01020304` uses initial-blob bytes `04 03 02 01`
and AddBuff wire bytes `01 02 03 04`.
The eligibility and value count agree; their byte encodings differ.

Both readers subsequently process a separate counted vector. That field is not
an alternative spelling for `GrantedStats` values and is not folded into the
numeric loop by this analysis.

## 3. Definition slots, construction and replacement

| Zero-based definition row / Id | Ordered granted native keys | `IsRecovery` | Initial granted values |
|---|---|---|---|
| 0 / `ignited` | 8215 | false | `[0]` |
| 10 / `poison` | 8216 | false | `[0]` |
| 12 / `righteous_fire` | 882, 21656, 26, 881, 1948, 19861 | false | Six zeros |
| 13 / `righteous_fire_aura` | 2023 | false | `[0]` |
| 38 / `mana_leech` | 1420, 2922 | true | Two decoded words |

These are positional definition slots. The reader does not carry one stat ID
alongside every value. Debug formatter `0x141e12370` confirms the association:
it obtains the Buff vector length, bounds the same index against definition
`+0xdc`, then resolves that index through `+0xe4` at
`0x141e123f9..0x141e1241a`. That loop formats names; it does not publish the
associated numerical values to an actor stat pool.

Packet-to-descriptor copier `0x141e07220` copies packet `+0x48` into descriptor
`+0x10` through helper `0x1418a5790` at `0x141e0729a`.
The initial blob reader writes that descriptor vector directly.
Constructor `0x141e06290` then moves the descriptor's begin/end/capacity triple
from `+0x10/+0x18/+0x20` into Buff `+0x130/+0x138/+0x140` and clears the source
triple at `0x141e06499..0x141e064bf`.

The constructor compares the resulting length with definition `+0xdc` at
`0x141e064f1`. A mismatch takes a diagnostic path and resizes using zero as
the fill value at `0x141e065eb`. This can **truncate an oversized vector or
extend a shorter vector with zeros**, preserving the existing prefix.
It is not a requirement that every mismatch discards every supplied value.

Full replacement updater `0x141e067c0` moves the incoming Buff `+0x130` vector
into the existing Buff through `0x140400ac0` at `0x141e06895`.
The helper disposes of the previous destination allocation, transfers all three
pointers and clears the source triple. Its identical-source/destination branch
keeps the vector intact. These operations transfer stored values; they do not
combine, re-evaluate or numerically apply the source to actor Stats.
The existing [message-driven update report](client-dot-routing.md) retains the
separate partial-update branch, which does not replace this vector.
Its older summary says that a count mismatch pads zeros; the complete resize
contract here additionally establishes truncation for oversized inputs.

## 4. Verification and remaining edge

Ignored reproducibility artifacts are
`Tools/re/out/client-damage-audit/r14-buff-initial-values-verify.py` and
`r14-buff-initial-values/`. The retained audit has eleven original function records,
22 PE ranges and 1,692 decoded instructions, plus exact instruction anchors,
schema offsets and selected table-to-native-key mappings.

Original-instruction fragment interpretation passed:

- 1,280 granted-loop controls across all 256 flag-byte values and five lengths,
  including signed-boundary bit patterns and empty vectors.
- 104 truncated-input controls, 81 shrink/equal/zero-extension resize controls,
  and 51 constructor/replacement/self-move controls.

Interpretation is restricted to those original fragments. Allocator behavior
is not executed: its zero-fill branch is separately verified from original
instructions. These checks establish receiving and storage, not a general
Buff-to-native-Stats publisher. The non-recovery damage-instance magnitude
producer and its authoritative actor-resource application remain separate
research edges.

Independent review freshly checked the same eleven functions and 22 ranges,
with 38 additional anchors and 367 controls: 75 decoding, 116 truncation,
128 resize, 32 allocating-growth fragments and sixteen move cases.
The allocation itself is abstracted; original prefix-copy and zero-fill
instructions remain distinct from an executed allocator.
