# Fireball Action lookup through received-result consumption (3.29.3b)

## Result and scope

The earlier [missing-action composition](client-hit-result-resource-composition.md)
now has an action-present continuation. Original `ProcessResult` finds a supplied
current Fireball Action by its sequence, calls its actual final-table callbacks,
constructs typed event arguments and completes their cleanup. Eight supplied-state
cases return successfully; Life, Mana and ES current, fractional carry and reservation
remain unchanged in their empty-listener/event configuration.

This establishes a conditional result consumer. It does **not** establish ordinary
Action construction/result ingress, a numerical damage amount, or absence of Hit
calculation elsewhere. **R2/R3/R4 remain Partial.** Projectile action production,
World dispatch and result decoding remain with the parallel projectile investigation.

This is offline original-instruction interpretation, not native target execution.
No client was launched or attached, no captures were acquired and no server or CI
was changed. Artifact identities remain those of the
[main investigation](client-damage-calculation.md#artifact-identity-and-reproducibility).

## 1. Sequence lookup reaches the ordinary client Fireball table

The supplied Action uses final primary table `0x14333ac40` and its embedded context
table `0x143233d38`. Original factory LEAs at `0x14128dadb/0x14128dae5` verify those
table identities; this does not execute that factory in the composition.

`ProcessResult` (`0x141e91580`) receives **null R9**, rather than a supplied Action
argument. Its fallback at `0x141e91e00..0x141e91e17` reads current Action from source
Actor `+0x1b0` and compares Action sequence `+0x11a` with result sequence `+0x40`.
The Action's sequence is supplied as 37; the result uses 37 or 38.

| Supplied hit-data flag | Sequence matches | Original Action callback/event sites | Result |
|---|---|---|---|
| False | Either | Not reached | Returns 1; resources preserved |
| True | False | Not reached | Returns 1; resources preserved |
| True | True | Reached | Returns 1; resources preserved |

Each combination also runs with result mask zero and critical-feedback bit `0x20`,
giving eight cases. Callback sites `0x141e92053/0x141e920ae` dispatch through Action
slots `+0x2d0/+0x2d8`; both original Fireball slots target exact no-op `0x1400f0f00`.
That leaf executes original instructions rather than an observational hook.

The matching-hit cases additionally execute actual typed argument/context builder
`0x141de8600` and event configuration processor `0x141e95620`, including reached
vector/map/argument copy and cleanup helpers. Event definitions and native/script
listener collections are supplied empty. This is not execution of arbitrary skill
scripts or evidence that every result consumer has no numerical effect.

## 2. A required constructor invariant

A zero-filled synthetic Action is insufficient for argument cleanup. The original
constructor chain is statically bound:

`0x141a8b150 → 0x1419ee9a0 → 0x141ba2160 → 0x1419e2a50 → 0x141937cc0`.

The composition executes only original arena initialization slice
`0x141937f47..0x141937f5f` (end exclusive), with RBX = supplied Action and RBP = zero.
It sets Action `+0x288` to inline buffer `Action+0x290`, clears `+0x280` and clears
the first inline word. After result processing, this empty inline state is restored
and source Actor processing depth `+0x238` is zero. Full construction is not claimed.

## 3. Evidence, interpreter controls and supplied boundaries

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

- `r47-action-present-frontier.py` and
  `r47-action-present-result/frontier.json`: eight returned compositions,
  **5,828 interpreted instructions / 1,386 distinct original sites**. All three
  pools remain current 100, carry 0.25 and reservation zero.
- `r47-action-present-fresh.py` and `fresh-check.json`: 35 focused original records,
  15 instruction anchors, two factory LEAs, three pointer slots and 12 bit-preserving
  MOVSD controls, including a NaN payload and negative zero.
- `r47-action-present-audit.py` and `audit.json`: every visited site is checked
  against original decoding, with inherited helper records and constructor owners
  included. Retained switch data is not interpreted as instructions.
- `allocator-controls.json`: 42 original allocation-wrapper controls, seven sizes
  by six requested alignments. The reached wrapper needs shift-produced flags;
  this batch extends only its own interpreter. These controls validate the
  wrapper's size/alignment/header results, not every affected flag or arbitrary
  programs. CPU-defined multi-bit OF is not claimed.

Nine hook targets are actually invoked across the saved cases: Entity/type/component
and provider mapping (`0x140129460`, `0x14013d8c0`, `0x14013da60`, `0x14013de10`,
`0x140156ee0`, `0x141ebefd0`), the exact-zero stub `0x1400ecd70`, aligned allocation
`0x142c839f0` and observational aligned free `0x142c834c0`. The zero stub's original
`xor eax,eax; ret` bytes are rechecked. Other inherited configured hooks are not
invoked in these cases. Complete original callback bodies are never replaced by
a result-preserving hook.

Action/configuration, components, provider values, resource-policy state and empty
event/listener collections are supplied. TLS uses a normalized zero-base memory
mapping at `0x58`, not evidence of real GS/thread ownership. The aligned allocator
is supplied; its original wrapper and header writes execute. These same-agent
checks do not claim an independent worker, observed gameplay or official timing.

For reconstruction, keep received hit/critical qualifiers, Action-specific event
delivery and authoritative resource changes as separate contracts. Their local
connections must be demonstrated individually. Accepted-Hit numerical sampling,
critical/mitigation-to-pool, DoT target publication/shared-cap reachability and
generic existing-Buff clock refresh remain the original research obligations.
