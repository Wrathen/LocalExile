# Cold Snap Buff backend: concrete presentation contract (3.29.3b)

## Result and scope

An exact `BuffDefinitions.Id2` join now binds three Cold Snap definitions to
registered native factories and a backend retaining the actual Buff instance.
Its concrete tick performs interpolation, animation transitions and attached
object matrix updates. It does **not** close an incoming DoT damage-rate
contribution or gameplay expiry contract.

This is a positive classification of one selected backend, not a claim that
Cold Snap has no damage, that the whole client lacks a contribution path, or
that the official server uses this presentation implementation. Numerical
source publication remains open in the [DoT source report](client-dot-source-publication.md).

Evidence is static original 3.29.3b code/data, preferred image base
`0x140000000`. Executable SHA256:
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
BuffDefinitions SHA256:
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.
No client execution, attachment, new capture, server change or CI operation.

## Exact table and factory binding

The existing [complete startup catalogue](client-buff-default-backend.md#binding-the-startup-registry-rather-than-assuming-its-contents)
contains the exact UTF-16 key `ColdSnapExpandingDegen` at two registration sites.
The pinned table joins are:

| Zero-based row / native definition key | Id | Exact Id2 |
|---|---|---|
| 1117 / 1118 | `cold_snap_degen` | `ColdSnapExpandingDegen` |
| 1118 / 1119 | `vaal_cold_snap_degen` | `ColdSnapExpandingDegen` |
| 1154 / 1155 | `cold_snap_expanding_chilled_ground` | `ColdSnapExpandingDegen` |

Registration `0x141e089b5` installs the factory object with table `0x14359bf90`;
its slot zero is `0x141e09f20`, which constructs base backend `0x141f7d390`.
Registration helper `0x1415cec60` inserts the same exact key at `0x1415cecb4`
and stores its factory at entry `+0x30` at `0x1415cecc3`. That factory's table
`0x143464bf8` has slot zero `0x1415cec00`, constructing `0x14179a0c0`.
The latter first calls the base constructor, then installs derived table
`0x143486fa8` at `0x14179a0fa`; the base table is `0x1435becc8`.

The generic creation route supplies the actual Buff in R8. Both factories pass
it to their constructor in RDX. Base constructor `0x141f7d390` retains it at
backend `+8` (`0x141f7d3aa`). Thus this is an actual-instance binding, not a
conclusion from a backend name alone. The selected bootstrap calls the
`0x141e07370` registration root before `0x1415be830`; live registry mutation
or another initialization environment is outside this static observation.

## Tick and actual output category

Buff clock consumer `0x141e10520` reads the retained Buff's backend `+0x148`
at `0x141e105ef` and invokes its `+0x28` at `0x141e10601` after the eligible
remaining-duration update. The concrete slots are:

| Backend | `+0x28` | Update/removal/secondary tick slots |
|---|---|---|
| Base `0x1435becc8` | `0x141f7d480` | `+8/+0x10/+0x18/+0x20/+0x30` are exact no-ops |
| Derived `0x143486fa8` | `0x14179a9a0` | Same five slots are exact no-ops |

The no-op is original leaf `0x1400f0f00`, bytes `c2 00 00`: it returns
immediately, without meaning “return numerical zero”. Destruction is a separate
slot and is not folded into this no-op result.

Both tick implementations interpolate backend-local values using frame delta
and parameters from the retained Buff. These floating fields must not be
relabeled incoming damage rates from offsets or arithmetic alone.

The derived contract has positively typed consumers:

- Setup `0x14179ac70` gets **AnimationController** through `0x14013def0`,
  whose original name literal is at `0x143593330`. It selects `fadein` and
  `loop` animation names at `0x14179ad6b/0x14179ae38`.
- Tick `0x14179a9a0` compares against the actual Buff's remaining duration
  (`0x14179aa3c`), selects literal **`fadeout`** at `0x14179aa97`, and calls
  the animation operation at `0x14179ab05`.
- Its Vaal branch writes four matrix rows at backend `+0x50..+0x80`, then
  obtains **AttachedAnimatedObject** through `0x140cbf280` and applies the
  matrix at `0x14179ac3b`. That getter's literal is at `0x1435955f0`.

The base constructor identifies definition 1155 into backend flag `+0x24`
and definition 1119 into flag `+0x25` (`0x141f7d3cf..0x141f7d3f1`). The
derived tick tests those actual flags for its presentation branches. This does
not establish damage-area membership, source stacking, a target rate or a
server-side expiry decision. A fadeout triggered by remaining duration is an
animation transition, not a recovered damage-source removal operation.

## Verification

Private `r15-dot-source-verify.py` reports **PASS: eleven original functions
and ranges, 885 decoded records, 45 exact instruction anchors, eighteen table
slots, five named presentation literals and three exact definition joins**.
It reuses the pinned complete registration catalogue rather than repeating
an unbound registry or offset scan. Investigation stops once the concrete
presentation category is established; no deeper rendering traversal is claimed.
