# Network Buff source identity and Life-callback context (3.29.3b)

## Result and scope

The original AddBuff **body reader** now composes in the same interpreted memory
with both retained-instance update paths, a subsequent numerical Life step, the
complete first-eligible Buff selector and a selected Life-callback prefix.
Both partial update and full replacement overwrite the retained source field,
including with zero. Their treatment of other instance fields differs.

This connects received source identity to an actual numerical consumer's
**attribution context**, not to its damage magnitude. The selected callback is
invoked separately after the Life step; it is not demonstrated to be triggered
by that nonfatal loss. Source Entity resolution and the rest of the callback
are not executed. **R2/R3/R4 remain Partial.** Source infliction, numerical
publication, strongest-instance selection, shared-cap reachability and generic
existing-Buff clock refresh remain open.

All evidence is offline original-instruction interpretation, not native target
execution or observed traffic. No client, attachment, new capture, server
implementation or CI was used. Executable and data identities remain those in
the [main investigation](client-damage-calculation.md#artifact-identity-and-reproducibility).
BuffDefinitions SHA256 is
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`.

## 1. A body reaches the retained instance without a decoded-object adapter

An independent encoder supplies bodies for `ignited` and `poison`, definition
rows 0 and 10. Original reader `0x141fdf870` executes before original retained
handler `0x141e11720`. The World common header, target/component dispatch and
loaded-definition lookup remain explicit fixtures; this is not a complete
network-ingress replay. The opcode is supplied as decoded `0x1c1`.

The selected body contains instance key 17, a definition row, a big-endian u32
source candidate, another u16, flags, a duration, optional speed/fields and an
empty separately counted tail vector. Both definitions are non-recovery with
one GrantedStats entry. The original decoder therefore produces a one-word
zero granted-value vector; the supplied body contains no damage-magnitude word.
The [wire-magnitude report](client-buff-wire-magnitudes.md) establishes that
cardinality/gating contract independently. Optional visual-definition override
flag `0x10` is outside this composition.

| Original boundary | Verified source-field transfer |
|---|---|
| Reader `0x141fdf8e4..0x141fdf8f2` | Read/decode wire u32 into message `+0x64` |
| Full-path converter `0x141e072b9..0x141e072bc` | Message `+0x64` to construction descriptor `+0x34` |
| Actual temporary construction and updater `0x141e068ad..0x141e068b7` | Temporary Buff `+0x28` to retained Buff `+0x28` |
| Partial path `0x141e11b92..0x141e11b95` | Message `+0x64` directly to retained Buff `+0x28` |
| Selected Life callback `0x141e68fc2..0x141e68fd0` | Call the actual selector, then read selected Buff `+0x28` |

Source candidates zero, 42 and `0xffffffff` all replace the previous
`0x11223344` field in **both** update paths. Zero does not mean "keep the old
source" in those stores. The retained instance key remains 17. Source identity
and instance identity must therefore be distinguished at this boundary.

## 2. Optional transport fields are not the callback's extra context

The optional flag-1 payload deliberately supplies byte 2, u32 1, byte 5 and
u16 `0x1357`. Their original receiving destinations are distinct:

| Decoded field | Construction descriptor | Actual temporary Buff | Retained partial update | Retained full replacement |
|---|---|---|---|---|
| `+0x76`, byte 2 | `+0x3c` | `+0x30 = 2` | Copies 2 | Keeps old `+0x30` |
| `+0x7c`, u32 1 | `+0x48` | `+0x38 = 1` | Keeps old `+0x38` | Keeps old `+0x38` |
| `+0x77`, byte 5 | `+0x74` | `+0x34 = 5` | Keeps old `+0x34` | Keeps old `+0x34` |
| `+0x78`, u16 `0x1357` | `+0x78` | `+0x3c = 0x1357` | Keeps old `+0x3c` | Keeps old `+0x3c` |

The retained `+0x34/+0x3c` witnesses are 23 and 29 before and after either
update. Their gameplay names are not assigned here. Equal-looking field
offsets on the message, descriptor and actual Buff do not imply equal roles.

The Life callback instead reads actual Buff **`+0x70/+0x74`** as additional
32-bit context. These are initialized from descriptor `+0x6c/+0x70`, which the
selected packet conversion leaves zero. Full replacement copies those zero
words over the retained context. Partial update leaves the retained words
unchanged: witnesses `0x22334455/0x33445566` survive and reach the prefix when
that instance qualifies. Those synthetic witnesses establish transfer/preservation;
they do not identify normal gameplay values or the words' semantic names.

## 3. Actual eligibility and explicit-source precedence

Original selector `0x141e692e0` executes to return, including actual temporary
reference-holder registration and removal. The actual row-index helpers
`0x142454bd0/0x142290090` resolve supplied loaded-resource handles; row keys are
not returned by scalar hooks. Stats reads use the real network getter with
explicit supplied resistance/bypass/conversion inputs. This continues the
[separate source-consumer analysis](client-dot-source-consumers.md).

With prior raw applicability `+0x30 = 0` and timing bits `+0x38 = 0`, the
extended **partial** update makes `+0x30 = 2` while leaving `+0x38 = 0`.
It consequently fails the selector's existing raw eligibility test. Full
replacement preserves both old zero fields and passes that test. This is a
concrete numerical-consumer consequence of selective transfer, without assigning
the fields speculative gameplay names.

Other controls cover current ES 0/100 and typed resistance 0/100. Poison remains
eligible with positive ES when chaos bypass is allowed; the ordinary ignite key
in this selected configuration requires nonpositive ES. Resistance immunity or
the failed raw-field test rejects the instance. These are this attribution
predicate's rules, not a general mitigation algorithm.

The separately invoked original callback prefix `0x141e68f70`:

- Uses a nonzero explicit source argument (witness 77) without calling the selector.
- With explicit source zero, calls the actual selector and reads its retained
  source/context fields when nonnull.
- Stops before source Entity resolution at `0x141e68fe9`, or at common downstream
  dispatch `0x141e6913c` if no nonzero source was obtained.

No selected-source hook supplies the answer. The prefix's incoming source is
not asserted to be final kill attribution: later source lookup and replacement
logic remain outside this execution slice.

## 4. Resource rate stays a separate contract

After body decode and either retained update, the original Life step executes
with an independently supplied explicit aggregate of 6,000 per minute. At the
supplied `dt = 0.033`, Life 100/carry 0.25 becomes Life 97/carry approximately
-0.04999995. The decoded and retained Buff's granted magnitude remains zero.
Changing source candidate, speed, optional fields or update kind leaves this
explicit rate and numerical loss unchanged in the supplied empty backend/event
configuration. No native contribution mutation or network override setter
executes during the recorded body/update/resource/attribution phase.

Immunity controls intentionally keep that explicit final aggregate unchanged;
they test the independence of the receivers, not a claim that official servers
publish an incompatible aggregate. Likewise, `0.033` is a numerical fixture,
not an observed client cadence. The callback prefix is manually invoked **after**
this step, with Life still positive; no loss-to-callback transition is claimed.

This establishes a positive C/S receiving boundary: a received Buff source
field can affect attribution context without supplying damage-rate magnitude.
It does not prove that all source magnitudes travel as final Stats overrides,
that the client has no numerical publisher elsewhere, or that a source selector
implements strongest-ignite/bleed selection.

## 5. Verification and remaining work

384 compositions cover two definitions, three source candidates, optional-field
presence, omitted/explicit speed, typed immunity, explicit-source precedence,
current ES and both retained update kinds. They execute **1,958,136 interpreted
instructions / 2,543 original sites**. Half use partial update and half full
replacement. Same-agent fresh verification maps every visited site to original
bytes in **55 function identities / 74 ranges / 4,110 decoded records**, checks
23 instruction anchors and one holder-dispatch pointer slot, and independently
parses every retained body/result. Ten controls check the interpreter's two
actual memory-form MOVSD instructions, including high-qword behavior.

Fourteen post-setup fixture targets are actually invoked: common header,
stream reads, two endian imports, temporary table holder, loaded-definition
lookup, two bounded vector allocators, memmove, zero support stub, reservation
policy, loaded-resource readiness and allocation/free. Inherited setup also
supplies the provider, native table/resource/container fields, eligible retained
instance, empty backend/scripts and observational subscriber. None is evidence
of actual traffic or ordinary runtime scheduling. No new function identity is
added to the manifest; the gain is the connected contract.

Private immutable support is retained under ignored `Tools/re/out/client-damage-audit/`:
`r50-wire-source-composition.py`, `r50-wire-source-fresh.py` and
`r50-wire-source-composition/{frontier,audit,fresh-check}.json`.
Earlier sealed producer mains were not rerun and their reports remain unchanged.
The next missing numerical edge is still actual source-instance selection and
rate publication, including shared cap and expiry withdrawal; attribution and
timer transport cannot substitute for that edge.
