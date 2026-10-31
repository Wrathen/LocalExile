# Client DoT source publication: named template and remaining boundary (3.29.3b)

## Result and scope

The source-publication edge remains **Partial**. A named RF aura template contains a concrete damage-rate
magnitude, but the inspected client paths do not yet connect its application to a target's native incoming-rate
contribution. This page retains that useful source-data lead and the exact search boundary; it does not replace
the missing instance publisher with the already known generic evaluator.

The [source-authority supplement](client-dot-source-authority.md) adds positive server-mode Aura metadata
and separately labelled default instance-selection rules from versioned official/community sources.
Neither the compiler boundary nor a published gameplay rule is misrepresented as this missing native publisher.

This is static analysis of the existing executable, extracted tables and three selected packaged resources.
No client process, server, hook, attachment, gameplay operation or new capture was used.
Executable SHA256 is `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`;
addresses use preferred image base `0x140000000`. Formula/routing evidence remains in
[client-dot-routing.md](client-dot-routing.md), and provider/publication contracts in
[client-stat-evaluation.md](client-stat-evaluation.md).

## A named RF aura has a declared numeric template

The pinned tables provide this exact reference chain, using zero-based JSON rows:

| Table / row | Verified declaration |
|---|---|
| BuffTemplates 352 | `ExpeditionOlrothRighteousFire`; `BuffDefinitionsKey = 13`; `Buff_StatValues = [975000]`; `AuraRadius = 16` |
| BuffDefinitions 13 | `righteous_fire_aura`; `GrantedStats = [2022]`; `Id2 = RighteousFireBuff`; `IsRecovery = false` |
| Stats 2022 / native key 2023 | `base_fire_damage_taken_per_minute` |

Thus this named template declares a numeric input associated with the incoming fire-rate key, rather than only
an art variant. Interpreting that value through the declared key's units would give 975,000 per minute before
downstream calculations. This is **declared source data**: no execution, source ownership, target selection,
stacking, duration, area application or actual publication follows merely from the row reference.

BuffTemplates SHA256 is `dfbe8d998a1384f19d69c66c092cf507a24d7d24562b385d0540b5cb1a2b6a50`
(811 rows). BuffDefinitions SHA256 is
`3a23c6fdf7ba29fb8508e5c9560a44e5292345e45dd2f886ef7126b8427fd659`;
Stats SHA256 is `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
The AddBuff recovery-only value decoding already established in the routing supplement does not automatically
become this template's application path: this definition's IsRecovery value is false.

## The table accessor is bound; the active publisher is not

Accessor `0x142239940` takes the literal `Data/BuffTemplates.dat` at `0x142239957`, calls
`0x141ad7670` at `0x14223998f`, and retains the returned table resource at `0x14223999c`.
The bounded direct E8/E9 reference scan has five verified callers:

| Caller owner | Accessor call |
|---|---|
| `0x1422b3ab0` | `0x1422b3b60` |
| `0x1422daea0` | `0x1422dafe7` |
| `0x1423280c0` | `0x142328168` |
| `0x142366980` | `0x142366a2b` |
| `0x1423b88d0` | `0x1423b890b` |

These inspected bodies copy table buffers and fix up references. For example `0x1423b88d0` calls
the buffer-copy routine at `0x1423b892b/0x1423b8942`, handles the `0xfefefefe` sentinel, checks the
referenced row index, multiplies it by native BuffTemplates stride `0x8a` at `0x1423b8a46`, and stores
the resulting row/resource pair at `0x1423b8a50/0x1423b8a64`. This positively binds table-reference
relocation; it is not a target stat-source insertion. The other four bodies also retain the `0x8a` row stride.

This scan does not cover computed/indirect table consumers, cached row pointers, inlined access or unowned code.
It therefore supplies a reusable lookup boundary, not evidence that no active template consumer exists.

## Selected Olroth resources do not close the application edge

Read-only archive extraction selected these exact members:

- `Metadata/Monsters/LeagueExpedition/Olroth/Olroth.ot`
- `Metadata/Monsters/LeagueExpedition/Olroth/Objects/OlrothRiFiTotem.ot`
- `Metadata/Monsters/LeagueExpedition/Olroth/Objects/OlrothCrackedGround.ot`

The first contains the explicit client preload `preload_buff_visual = "olroth_righteous_fire"`.
The totem contains Actor/state/configuration and client visual scripts; the cracked-ground member declares
an animated object and transition behavior. None of these three selected members explicitly names
`ExpeditionOlrothRighteousFire`. Their labels and preloads do not prove that a target receives the template's
975,000 input, and the inspection is not an archive-wide absence claim. Exact extracted-member hashes and
byte lengths are retained locally; no whole-archive digest is claimed.

## Evidence and next required edge

Canonical private batches under ignored `Tools/re/out/client-damage-audit/` are
`r2-dot-publication-template-discovery` and `r2-dot-publication-template-accessors`.
`r2-dot-publication-verify.py` checks eight original-byte function bodies/ranges, 2,966 instruction records,
18 semantic anchors, the five direct references, three table identities and three extracted-member identities.
Verification is retained in `r2-dot-publication-template-discovery/verification.json`.

The next positive evidence must bind a concrete template/effect instance and applicable target to an amount
write, source token, transaction or equivalent receiving-rate update. For ignite and poison, the analogous
missing edge must also identify their selection/aggregation rules. Existing timer, art, generic additive-token
and conditional incoming-rate formulas remain separate contracts; none closes those edges by itself.
