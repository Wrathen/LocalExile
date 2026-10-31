# Flame Wall secondary Buff: a sampled owner-stat clock hold (3.29.3b)

The concrete `FirewallSecondaryDegenBuff` backend samples its owner's current
Stats provider during its duration callback. If native key 12588,
`firewall_secondary_degen_time_remaining_does_not_decrease`, is nonzero, it
restores the instance's remaining duration from a value captured when the backend
was constructed. The main Buff tick has already subtracted its ordinary duration
step before this callback runs.

This closes one **current owner-stat value → existing instance clock write**
contract. It is polling, rather than a Stats-change subscription, and restores
remaining duration rather than recalculating stored clock speed. The general
[owner timing-stat → existing Buff speed refresh](client-buff-clock-updates.md)
question remains separate. No incoming fire-damage rate or resource deduction is
established by this duration write.

## Artifact and evidence boundary

All findings are offline original-byte observations for preferred image base
`0x140000000`; no game execution, attachment, new capture, archive mutation or
server change was used. Control tests interpret the original callback
instructions with explicit synthetic component/provider getter fixtures.

| Artifact | SHA-256 |
|---|---|
| `Client/Game.exe`, 3.29.3b | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `data/tables/Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| `data/tables/BuffDefinitions.json` | `3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659` |

Native stat key 12588 corresponds to zero-based JSON row 12587. The
`firewall_secondary_debuff` definition has `Id2 = FirewallSecondaryDegenBuff`.
The primary `firewall_debuff` row's `GrantedFlags = [12587]` references that same
stat row. This is a positive data association; the table reference alone does
not close a live GrantedFlags-to-owner-provider publication path.

## Concrete backend and captured value

Registry initialization `0x141e07370` installs factory table `0x14359bcd8` at
`0x141e07e5d..0x141e07e64`, constructs the UTF-16 key
`FirewallSecondaryDegenBuff` from literal `0x14359c5a0` at `0x141e07e87`, and
inserts/binds that key at `0x141e07ea3..0x141e07eb2`. Factory table slot zero is
`0x141e0a460`.

The generic backend resolver `0x141e05fa0` reads the actual Buff definition at
instance `+8`, selects its backend name at definition `+0x74`, looks it up in
that registry, and invokes the retained factory at `0x141e06081`. Insertion
`0x141e11dc0` passes the actual new Buff to this resolver at `0x141e11e65` and
stores its returned backend in Buff `+0x148` at `0x141e11e7b`.

Factory `0x141e0a460` allocates 0x18 bytes and initializes:

| Backend field | Bound source |
|---|---|
| `+0` | Table `0x1435bee08`, installed at `0x141e0a486..0x141e0a48d` |
| `+8` | Incoming actual Buff pointer, retained at `0x141e0a490` |
| `+0x10` | A raw 32-bit copy of that Buff's current remaining-duration field `+0x1c`, captured at `0x141e0a494..0x141e0a497` |

The snapshot is **backend-construction remaining duration**, not a fresh read of
the definition's base duration, a damage magnitude, or clock speed. In the bound
insertion path it is captured after the separate insertion-time speed
calculation. The selected callback reads this capture; it does not replace it
with the current remaining time when the stat changes.

## Current provider lookup and post-decrement restore

Table `0x1435bee08` slot `+0x28` is `0x141f7d850`. Its actual receiver chain is:

```text
backend +8 → Buff
Buff +0 → owning Buffs component
Buffs +8 → owning Entity
named Stats component lookup 0x14013de10(Entity)
Stats +0x20 → current ordinary owner network provider
provider slot zero(12588) → numeric value
```

The Buff constructor stores its owning component at instance `+0`
(`0x141e062b9`); this is not a Buff vtable. The named getter's original literal
is ASCII `Stats` at `0x1435a396c`. Ordinary Stats constructor `0x1421c2ad0`
creates the network wrapper through `0x1421c3700`, then stores the same provider
pointer at Stats `+0x40` and `+0x20` at `0x1421c2b92/0x1421c2b96`. Wrapper
construction installs table `0x14363bd90`, whose numeric getter is
`0x1424cfa40`: explicit entries, including zero, win; absent keys fall back to
the retained parent. See [provider identity and fallback](client-stat-evaluation.md).
This callback therefore uses the ordinary owner provider; it does not construct
or select a native action-local stat context.

The complete selected callback has 21 instructions:

| Site | Operation |
|---|---|
| `0x141f7d856..0x141f7d864` | Resolve the owning Entity's Stats component through the retained Buff/component chain |
| `0x141f7d869..0x141f7d86c` | Return without a clock write if Stats is absent |
| `0x141f7d86e..0x141f7d87a` | Load provider `+0x20` and call its numeric getter with native key 12588 |
| `0x141f7d87c..0x141f7d87e` | Return without a clock write if the getter result is zero |
| `0x141f7d880..0x141f7d887` | Copy backend `+0x10`'s 32-bit duration bits into actual Buff `+0x1c` |

The restored value is not added to remaining time and is not a zero-speed
assignment. The callback samples the provider each time it is reached, so its
choice can change between callbacks. A newly nonzero flag restores the saved
construction value, rather than merely preserving whatever remaining duration
the instance had when that flag became nonzero.

The actual main tick `0x141e10520` first checks removal and its other duration
eligibility conditions. A retained backend is itself sufficient to bypass the
specific backend-null/descriptor-zero early skip (`0x141e1056a..0x141e10572`);
it does **not** bypass the separate component/definition pause guard, the
pre-step positive-duration test, or the instance `+0x118 == 4` exclusion.
When eligible, the tick subtracts `delta * effectiveStoredSpeed` and writes
Buff `+0x1c` at `0x141e105ea`, then invokes backend `+0x28` at
`0x141e10601`. No second positive-duration test occurs between that subtraction
and the callback. Thus a step crossing zero can still reach this restore;
an instance already nonpositive at the pre-step test does not reach it through
this main tick.

For example, if the backend captured `5.0`, and an eligible ordinary step
temporarily reduces remaining duration from `2.0` to `1.9`, a nonzero stat makes
the callback write `5.0`. A zero stat leaves `1.9`. This explains the local
instruction contract without claiming observed gameplay timing or server
authority.

## Retained verification and limits

Ignored private evidence is under `Tools/re/out/client-damage-audit/`:
`r15-buff-clock-verify.py` and `r15-buff-clock/{audit.json,verification.json}`,
with complete original-instruction listings and per-range hashes. Fresh
verification is **PASS**: 13 functions / 17 ranges / 2,409 instructions,
44 instruction anchors, seven table pointers, and 228 original-callback
instruction controls. Unwind-root/chained ranges are used except the explicitly
bounded existing numeric-getter leaf `[0x1424cfa40, 0x1424cfac2)`.

Controls cover absent Stats, zero/nonzero/negative getter values, independently
changing current duration, preserved construction captures and untouched
instance speed. Raw duration-bit controls include infinities, signed zero and
NaN payloads to verify the copy contract; they are synthetic fixtures, not a
claim that all such values occur in gameplay. Main-tick eligibility/order is
verified from original bytes rather than executed as a live frame loop.

Still open are the producer of this owner's current flag value, source damage
publication and authoritative resource effects. This selected polling callback
does not establish a general Stats notification subscriber, a dynamic refresh
of Buff `+0x20`, or an absence of either elsewhere. It supplies a concrete
existing-clock override alongside the already established network clock updates.
