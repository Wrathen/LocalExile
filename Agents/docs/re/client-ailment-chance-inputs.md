# Client damaging-ailment chance inputs (3.29.3b)

## 1. Result and evidence boundary

Seventeen native registrations produce bleeding, ignite and poison chance inputs. Their sixteen
unique callbacks add ordinary and conditional contributions; they do not roll an ailment, create
an instance, test a concrete defender, or subtract resources. Critical-only poison/bleed outputs
are additional inputs, not complete critical-hit infliction decisions. This report complements
[typed eligibility](client-ailment-eligibility.md), [outgoing rates](client-ailment-outgoing-formulas.md)
and [instance rules](client-dot-instance-rules.md).

The evidence is original bytes from `Client/Game.exe`, version 3.29.3b, preferred base
`0x140000000`, SHA256 `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`.
`data/tables/Stats.json` has 23,346 rows and SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`;
native key `k` identifies row `k-1`. Ignored evidence is under
`Tools/re/out/client-damage-audit/r7-ailment-chance/`, with sibling registration, interpretation
and verification scripts. A bounded Ghidra export retains all sixteen callbacks. Static formula
registration remains distinct from the ordinary network-provider contract described in
[stat evaluation](client-stat-evaluation.md).

## 2. Registration and arithmetic

| Key | Output | Registration | Callback |
|---|---|---|---|
| 3933 | MH poison chance on critical strike | `0x1400bb7c0` | `0x14274e890` |
| 3934 | OH poison chance on critical strike | `0x1400bb820` | `0x14274ed20` |
| 6571 | MH bleed chance on critical strike | `0x1400bb8d0` | `0x14274f0e0` |
| 6572 | OH bleed chance on critical strike | `0x1400bb930` | `0x14274f4c0` |
| 6389 | Poison chance with attacks | `0x1400ca9f0` | `0x1427e8d00` |
| 1927 | Generic ignite chance | `0x1400cab00` | `0x1427e9270` |
| 3919 | Generic poison chance | `0x1400d0f00` | `0x142825210` |
| 6609 | MH total bleed chance on hit | `0x1400d6c90` | `0x142866b80` |
| 6610 | OH total bleed chance on hit | `0x1400d6d10` | `0x142867ab0` |
| 6611 | MH total ignite chance on hit | `0x1400d6d80` | `0x1428688a0` |
| 6612 | OH total ignite chance on hit | `0x1400d6de0` | `0x142868c90` |
| 6613 | Spell total ignite chance on hit | `0x1400d6e40` | `0x142869080` |
| 6614 | Secondary total ignite chance on hit | `0x1400d6ea0` | `0x142869080` |
| 6615 | MH total poison chance on hit | `0x1400d6f50` | `0x142869190` |
| 6616 | OH total poison chance on hit | `0x1400d6fb0` | `0x142869650` |
| 6617 | Spell total poison chance on hit | `0x1400d7060` | `0x142869b10` |
| 6618 | Secondary total poison chance on hit | `0x1400d70c0` | `0x142869e00` |

`S(k)` below reads the supplied evaluation context, including its native cache/fallback.
`Q(condition, k)` contributes `S(k)` when true, otherwise zero. Boolean tests mean nonzero,
unless an explicit comparison is shown. Sums and products use the native 32-bit integer result;
they do not convert to float. These callbacks contain no final clamp to `[0,100]`.
A caller may impose eligibility or probability limits; that caller is not recovered by identifying
these outputs. The registry dependency spans also omit some classification inputs read by the bodies.

## 3. Generic chance builders

### Ignite: key 1927

```text
S(1927) = S(614) + S(1918)
        + Q(S(4537) == 0, 3410)
        + Q(S(1485) != 0, 1704)
        + Q(S(1843) != 0, 2398)
        + Q(S(3918) != 0, 2188) + Q(S(3918) != 0, 4780)
        + Q(S(6199) > 0, 11177)
        + Q(S(2875) != 0, 13010)
```

Inputs are base ignite `614`, generic freeze/shock/ignite `1918`, no recent critical strike
(`4537`, contribution `3410`), projectile classification (`1485`, `1704`), currently ignited
(`1843`, `2398`), using a flask (`3918`, **both** `2188` and `4780`), a positive active-Herald
count (`6199`, `11177`), and chaos-skill classification (`2875`, `13010`). The flask branch adds
two different contributions. It does not choose just the larger one. The original instruction
anchors `0x1427e9a04` and `0x1427e9ac7` preserve the cached and fallback addition paths.

### Poison: key 3919

```text
S(3919) = S(3426)
        + Q(S(2376) != 0 or S(2377) != 0, 4509)
        + Q(S(3918) != 0, 3562)
        + Q(S(2819) != 0, 6256)
        + Q(S(2820) != 0, 6257)
        + Q(S(2821) != 0, 6258)
        + Q(S(1485) != 0 and S(6556) != 0, 6562)
        + S(7) * S(7780)
```

The trap/mine term contributes once when either classification is true. Fire/cold/lightning
skill terms are independently qualified and can coexist. The projectile/beast term literally
tests projectile classification `1485` and `number_of_active_beast_minions` `6556`; it does not add another
`skill_is_attack` test inside this callback despite the contribution's stat name. Power-charge
count `7` multiplies `7780` directly, without a separate positive-count gate.

### Attack poison supplement: key 6389

```text
S(6389) = S(3889)
        + Q(S(6) == S(2701), 6386)
        + Q(S(20703) != 0, 20876)
```

The maximum-frenzy condition is literal equality, including `0 == 0`. `20703` is the general
`tincture_applied` input. This differs from the hand-specific tincture inputs used below.

## 4. Ordinary hand, spell and secondary outputs

For each hand, use the following selected inputs:

| Input | MH | OH |
|---|---|---|
| Local bleed chance | 2883 | 2884 |
| Boolean local 25% bleed | 6455 | 6456 |
| Boolean local 50% bleed | 6594 | 6595 |
| Local bleed while no Avatar of Fire | 8714 | 8715 |
| Tincture on this hand | 20704 | 20705 |
| Local poison chance | 5432 | 5433 |

The complete ordinary bleed builders are:

```text
S(6609) = S(3691) + S(2883)
        + Q(S(1486) != 0, 2651)
        + Q(S(3) == 9, 2927)
        + Q(S(1485) != 0 and S(6556) != 0, 6557)
        + (S(6455) != 0 ? 25 : 0) + (S(6594) != 0 ? 50 : 0)
        + Q(S(5334) == 0, 8714)
        + Q(S(9461) == 0, 9462)
        + Q(S(20704) != 0, 20877)

S(6610) = S(3691) + S(2884)
        + Q(S(1486) != 0, 2651)
        + Q(S(1485) != 0 and S(6556) != 0, 6557)
        + (S(6456) != 0 ? 25 : 0) + (S(6595) != 0 ? 50 : 0)
        + Q(S(5334) == 0, 8715)
        + Q(S(9461) == 0, 9462)
        + Q(S(20705) != 0, 20877)
```

Only the MH callback tests weapon type `3 == 9` for the bow contribution `2927`. It does not
extend that condition to Varunastra. The `25` and `50` terms are fixed amounts selected by
nonzero markers, not the marker values multiplied by 25/50. Avatar of Fire `5334` gates only
the selected local contribution; its presence does not make the whole chance output zero.
Blood-stance contribution `9462` is qualified by `in_sand_stance` `9461 == 0` here, without an
additional positive blood-stance test. Downstream damage eligibility remains separate.

```text
S(6611) = S(11063) != 0 ? 0 : S(1927) + S(1696) + Q(S(20704) != 0, 20878)
S(6612) = S(11063) != 0 ? 0 : S(1927) + S(1696) + Q(S(20705) != 0, 20878)
S(6613) = S(6614) = S(11063) != 0 ? 0 : S(1927)

S(6615) = S(3919) + S(6389) + S(5432) + Q(S(1486) != 0, 5126)
S(6616) = S(3919) + S(6389) + S(5433) + Q(S(1486) != 0, 5126)
S(6617) = S(3919) + Q(S(438) != 0, 11679)
S(6618) = S(3919)
```

Ignite's `virtual_never_ignite` `11063` returns zero before any other contribution. Both spell
and secondary outputs share the same callback. The spell-poison supplement `11679` is explicitly
qualified by `casting_spell` `438`, even though the output already names spells. The secondary
poison callback is a direct value/cache alias for generic `3919`. Neither poison nor bleed
builders here merge defender immunity or typed damage eligibility into the chance sum.

## 5. Critical-only supplements

```text
S(3933) = S(6569)
        + Q(S(4198) != 0 or S(3) == 10 or S(3) == 16, 3935)
        + Q(S(3) == 9, 10019)
S(3934) = S(6570)
        + Q(S(4199) != 0 or S(4) == 10 or S(4) == 16, 3935)

S(6571) = S(6566) + (S(6574) != 0 ? 50 : 0) + Q(S(1486) != 0, 2332)
S(6572) = S(6567) + (S(6575) != 0 ? 50 : 0) + Q(S(1486) != 0, 2332)
```

Weapon values `10/16` select the dagger term, and `9` selects the MH bow term. Preserve the
literal enum test: the Varunastra predicate extends dagger eligibility, but does not turn the
separate bow branch on. OH has no bow contribution. Bleed's local 50% critical marker is again
boolean. The conditional `2332` is the melee-attack contribution.

These callbacks do not add ordinary chance, determine whether the hit is critical, or implement
the critical-strike default for elemental ailments. Inferring any of those operations from an
output name would skip its actual consumer. Perfect Agony's rate-multiplier branch, critical
chance and infliction eligibility must remain separate contracts.

## 6. Verification and remaining work

The focused verifier re-reads the PE identities, complete instruction ranges, registration
key/callback/dependency dataflow and typed export coverage. A separate bounded interpreter
evaluates original decoded instructions against synthetic cache-ready Stats and compares them
with the equations above: **975 vectors across 17 outputs, 16,575 comparisons and 904,703
instruction steps**, with 79 selected input keys. Vectors include isolated contributions,
conditional combinations, negative inputs, values above 100 and 32-bit boundary values.

This is offline interpretation, not execution of the client, gameplay, or observed traffic.
All cache-valid bits are supplied as set; the established cache/fallback contract is not being
reimplemented or tested by those vectors. Predicate helper `0x1425135a0` is substituted with its
already recovered nonzero-stat meaning. Synthetic combinations are not assertions that every
combination is reachable in a real entity. The interpreter rejects unsupported instructions or
calls instead of silently skipping them.

Still open: the actual per-event consumer, critical/ordinary chance combination, defender
avoidance/immunity ordering, random-trial inputs, successful instance creation and outgoing
source-rate publication to the target. A chance-input reconstruction does not close those edges.
