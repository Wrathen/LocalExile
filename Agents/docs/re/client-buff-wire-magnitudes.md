# AddBuff magnitude transport and cardinality (3.29.3b)

## Result and scope

The original `0x1c1` writer **omits the granted-value vector when the definition's
`IsRecovery` is zero**, matching the previously recovered reader gate.
For the pinned ignite, poison, RF and Burning Arrow definitions, changing that
vector's values or length does not change this message body. This selected
carrier therefore cannot convey their per-instance damage magnitude through
the granted-value vector. This is a positive serialization contract, not a
global claim that no other magnitude carrier exists.

For a nonzero gate, the writer emits one big-endian word per **actual packet
vector element**, without a count. The reader instead consumes one word per
**definition `GrantedStats` entry**. Both ends must agree on that cardinality.
Constructor normalization happens after decoding and cannot repair a shifted
wire cursor.

The [initial Buff value report](client-buff-initial-granted-values.md) owns the
initial-Entity carrier, storage, resizing and replacement contracts. The
[source-publication report](client-dot-source-publication.md) owns the remaining
instance-to-target rate edge. Neither selection, aggregation, cap nor numerical
publication is closed here. **R2/R3/R4 remain Partial.**

This is offline analysis of the pinned client; no client execution, new traffic,
server implementation or CI occurred. Serializer availability in a message
class does not prove that this client sends an S2C message.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |
| `schema.min.json` | `3fb5afc7bd952bd1b38595bdc4582e0b691ec73e6e20b7802fa8894aa67b4f48` |

## Original writer and reader

Preferred-image class table `0x1435ddb78` installs writer `0x141fdf430` at `+8`
and reader `0x141fdf870` at `+0x10`. Common object-header calls are separate from
the body described below. Existing [protocol documentation](../protocol/11-skills.md#12-buffs-and-righteous-fire)
retains the other fields and their consumers.

The writer derives the zero-based definition row from its resource buffer and
the definition pointer, using stride `0x13a` at `0x141fdf460..0x141fdf48e`.
The body starts with `u16 +0x20`, definition-row `u16`, `u32 +0x64`,
`u16 +0x60`, and the optional-field flag byte. These scalars use network order.
The import directory independently identifies `htons` at `0x142f43e28`,
`htonl` at `0x142f43e48`, `ntohs` at `0x142f43e50` and `ntohl` at `0x142f43e58`.

After the optional fields, the numerical branches are:

| Operation | Original sites | Count source |
|---|---|---|
| Writer granted-value gate | `0x141fdf7a7..0x141fdf7ab` | Definition byte `+0x7c`; zero jumps past the entire loop |
| Writer granted values | `0x141fdf7ad..0x141fdf7dc` | Packet vector `+0x48..+0x50`, stepping four bytes; `htonl`, then stream write |
| Reader granted-vector resize | `0x141fdfc98..0x141fdfcaf` | Definition count `+0xdc`, zero fill |
| Reader granted-value gate/loop | `0x141fdfcb8..0x141fdfcf3` | Same definition gate; stream read and `ntohl` for each resized slot |
| Writer separate vector | `0x141fdf7de..0x141fdf848` | Packet `+0x88..+0x90`; writes its own `u32` count, then words |
| Reader separate vector | `0x141fdfcf5..0x141fdfd58` | Wire `u32` count, resize, then words |

The definition byte at `+0xff` controls an additional `u16` before these vectors.
It is **unnamed column 44** (`__col44`), not `IsSkillBuff`, whose schema offset
is `+0x94`. Keep the raw field identity until its semantics are established.
For example, `chilled`, `frozen` and `shocked` set column 44 while their
`IsSkillBuff` is false. Neither optional `u16` nor the separate counted vector
is established as an alternative damage-magnitude channel by this report.

## Concrete definition witnesses

Rows are zero-based; native stat keys are JSON stat-row indices plus one.
With ordinary defaults, no column-44 word and one separate-vector entry, the
body is 19 bytes plus any eligible granted-value words. Headers are excluded.

| Definition row / Id | Granted native keys | `IsRecovery` | Granted words on this wire |
|---|---|---|---|
| 0 / `ignited` | 8215 | false | None; reader creates `[0]` |
| 10 / `poison` | 8216 | false | None; reader creates `[0]` |
| 12 / `righteous_fire` | 882, 21656, 26, 881, 1948, 19861 | false | None; reader creates six zeros |
| 13 / `righteous_fire_aura` | 2023 | false | None; reader creates `[0]` |
| 1443 / `burning_arrow_additional_burn` | 2023 | false | None; reader creates `[0]` |
| 38 / `mana_leech` | 1420, 2922 | true | Two words, in definition order |

For row 38, supplied granted values `0x01020304, 0x01020305` appear as
`01 02 03 04 01 02 03 05`; the supplied separate vector `[0x11223344]`
follows as `00 00 00 01 11 22 33 44`. The body is 27 bytes.
Changing a non-recovery witness's granted contents and length preserves all
19 body bytes; the counted separate vector still survives independently.
Three additional actual-definition controls cover the `+0xff` word.

Two deliberately inconsistent fixture definitions expose the count dependency:

- Writer has two granted words `[0x01020304, 1]`; reader declares one.
  The second word becomes the separate-vector count. The real count `1`
  becomes that vector's first value, leaving its original value unconsumed.
- Writer has one granted word `[17]` and an empty separate vector; reader
  declares two. It consumes the actual vector count as granted value zero,
  then reads a supplied following zero word as its count, four bytes past the
  writer's body.

These controls use a supplied bounded stream. They demonstrate cursor
misalignment, not actual malformed-stream error handling, a safety check,
an exploitable condition, or observed packet behavior.

## Verification and remaining boundary

Ignored reproducibility files are `Tools/re/out/client-damage-audit/r26-buff-wire.py`,
`r26-wire-fresh.py` and `r26-buff-wire/`.
Three retained functions/ranges contain 587 decoded instructions; the writer,
reader and infinity-return leaf were already in the evidence inventory.
The pass adds no newly discovered function entry.

648 checks comprise 327 original-writer versus independent-byte controls,
315 original-reader controls and six non-recovery invariance comparisons.
They interpret 133,624 original instructions across 494 visited sites.
Fresh verification checks 30 instruction anchors, two concrete class slots,
four import names, schema offsets and nine actual definition rows.
All combinations of the modeled optional flags are covered; the separate
`0x10` visual-definition override path is excluded.

Common headers, stream operations, byte-swap imports, table/holder resolution
and vector allocation/zero fill are explicit fixtures. Inputs exclude NaNs;
the selected scalar defaults include positive infinity. This is same-agent
instruction interpretation, not native execution or independent-worker review.
The work narrows the source-magnitude transport search without claiming that
Buff presence, a duration or a counted vector publishes target DPS.
