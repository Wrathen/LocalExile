# Parsed Stats updates and actual Life integration (3.29.3b)

## Result and boundary

A parsed owner Stats update can now be followed through the **actual network
override setter, getter, notification dispatcher and Life integration**. Replacing
native key 3362 `total_damage_taken_per_minute_to_life` changes subsequent local
loss in the same Life update when an eligible component checkpoint consumes the
message. This connects a received numerical rate to concrete resource loss;
the source-instance selection and publication before that rate remain open.

This is static original-byte evidence and offline instruction interpretation,
not native execution or observed official traffic. It extends the
[resource-checkpoint composition](client-resource-checkpoint-composition.md),
[resource read order](client-resource-read-order.md) and
[network provider contract](client-stat-evaluation.md).
R0/R1 remain Established within their stated scope; R2/R3/R4 remain Partial.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe` | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

Addresses use preferred image base `0x140000000`. Native key `k` maps to JSON
row `k-1`. No client, capture, server, CI or shared native-analysis session was used.

## 1. Positive composition

The parsed message enters the original ordered World checkpoint
`0x142183840`, which reaches dispatcher `0x142187480`. Its `0x1b3` branch
resolves the target's named `Stats` component, loads its network provider from
`+0x40` at `0x1421881b6`, and processes the received pairs in vector order:

```text
0x1421881d0: load packed (signed32 key, signed32 value)
0x1421881d6: move upper 32 bits into value argument
0x1421881dd: call 0x1424cfaf0(provider, key, value)
0x1421881e2: advance by eight bytes and repeat
```

For an existing key, setter `0x1424cfaf0` writes the new value at
`0x1424cfb68`, then tail-calls `0x1424cfd00`. The actual notification dispatcher
invokes each retained subscriber; thunk `0x1402fffbc` forwards to its slot zero.
The controls install one observational subscriber that reads the newly stored
value through the original getter. Real application subscribers may have other
consequences; the observational subscriber does not reconstruct that graph.

The getter `0x1424cfa40` returns an explicit value, including zero or a negative
value. Only an absent key delegates to provider `+0xe0`; absent upstream returns
zero. Both getter returns and the upstream tail dispatch are retained, rather
than treating the first return as the end of the function.

Life's retained Stats `+0x20` is the same network provider used by these reads,
as bound by the existing construction audit. Full Life update `0x141e6ae70`
reads key 3362 at `0x141e6afa9..0x141e6afb1`, converts the integer rate to
binary32, divides by 60, multiplies by supplied `dt`, and passes the negative
delta to the actual Life reducer `0x141e6a8a0`. That reducer reaches common
pool integration `0x141e6a6b0`, including integer current and fractional carry.
The other Life loss channels remain separate.

## 2. The complete Life step has an additional checkpoint

The prior recovery-controller report establishes that recovery key 1422
`life_recovery_per_minute` is captured before controller checkpoint
`0x141e6b42b`. A positive captured recovery rate can also enter Life gain wrapper
`0x141e69e50`, with its own checkpoint before gain. Changing the stored rate at
either checkpoint does not rewrite the already captured recovery amount.

The **full Life step** additionally invokes World `+0x90` at
`0x141e6af61`, before reading ordinary degeneration and the damage-loss rates.
This checkpoint is outside the common recovery controller. With the other
healing inputs zero and current below capacity, the tested full step therefore
has two checkpoints when captured recovery is nonpositive, and three when it
is positive. These counts are conditional on the tested path, not a universal
number of World checkpoints per frame or per entity.

A queued rate update due at the second checkpoint can consequently affect this
step's damage even when zero captured recovery skips the Life gain wrapper.
For a positive old recovery rate, the wrapper can consume that update earlier,
but the amount it adds still uses the captured old rate.

## 3. Numerical witnesses and what is not recomputed

All following fixtures start at Life current 100, fractional carry 0.25, maximum
1000 and no reservation. Their supplied `dt` is binary32 `0.033`; it is not a
measured official frame duration. Unmentioned rates and protection predicates
are zero. A rate of 6000/min corresponds to 100/second before integration.

| Received pairs at an eligible checkpoint | First step current / carry | Second step current / carry |
|---|---|---|
| 3362 = 6000 | 97 / approximately -0.05 | 94 / approximately -0.35 |
| 2023 = 6000, while explicit 3362 stays zero | 100 / 0.25 | 100 / 0.25 |
| 3362 = 6000, then 3362 = 0 | 100 / 0.25 | 100 / 0.25 |
| 3362 = 0, then 3362 = 6000 | 97 / approximately -0.05 | 94 / approximately -0.35 |
| Old recovery 6000; receive 1422 = 0 and 3362 = 6000 | 101 / -0.75 | 97 / approximately -0.05 |
| Old recovery zero; receive 1422 = 6000 and 3362 = 6000 | 97 / approximately -0.05 | 97 / approximately -0.05 |

Integer current alone does not express the integrated resource: 101 with carry
-0.75 represents the same continuous amount as 100.25. The old-recovery witness
retains separate positive and negative operations and their native truncation;
netting the two rates before integration would discard that representation.

Key 2023 is `base_fire_damage_taken_per_minute`. Updating it does **not** replace
an explicit 3362 override in this provider or enter a native contribution
transaction along the tested setter/notification path. This is not proof that
raw fire inputs have no effect under every possible upstream provider or
subscriber. An absent final key still delegates upstream; the controls treat
that upstream's numerical return as a fixture, not a recovered derivation.

Repeated pairs produce sequential writes and sequential notifications, including
equal-value writes. The later Life read sees the final stored pair value in the
tested subscriber configuration. The temporary earlier value is visible to the
observational subscriber. This does not establish transactional isolation,
concurrency semantics or what every actual subscriber does.

Signed nonpositive total-loss values are stored but do not enter the positive
loss branch. A signed `INT_MAX` rate is also transported through this override
path and integrated using binary32; no source stacking, shared-cap calculation
or minute-reset allowance is reconstructed by that control. The
[separate native saturation stages](client-dot-routing.md) remain distinct.

## 4. Client/server responsibility

The client can integrate a supplied total rate without numerically reconstructing
the contributing Buffs on this path. An upstream system must still produce that
total or a provider from which it can be obtained. The missing research edge is
now more precise: **which source-selection, mitigation and publication operation
produces the effective target rate**, independently of the established receiving
and local-integration contract. This report does not claim that a particular
official packet actually supplied 3362, or that the server always sends only
final rates. Instance expiry, strongest-ignite selection, poison aggregation and
generic existing-Buff clock refresh remain unclosed numerical relationships.

Projectile work retains message ingress ownership. Fixed peer revision
`0bd717fb1151e0cc3eecb624bbcaa72b04052713`, locally retained, binds Instance
pump/group/factory/header to World intake. Its 68-file snapshot, including 52
exact Git sources and parent-manifest hashes, was checked before reuse; its
35 numerical controls were not rerun. That work does not replace the parsed
queue fixture used in this report with an observed official transport stream.

## 5. Reproduction and limits

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

- `r29-network-rate-composition.py` and `r29-network-rate-composition/audit.json`
  retain 37 reused function identities, 53 original-byte ranges and 3,810 decoded
  instruction records. No new function discovery is claimed.
- `r29-network-rate-composition/verification.json` records **514 controls**:
  474 parsed-update/Life compositions and 40 real getter/fallback controls;
  **1,075,486 interpreted instructions / 787 visited sites**. A separately written
  scalar model checks the sequential numerical state. Both are same-agent work.
- `r29-network-rate-fresh.py` / `fresh-check.json` independently reread the pinned
  PE, function/range/instruction identities, 25 instruction anchors, four table
  pointers, the `Stats` literal and three native key identities.
- `coordination-r44-reuse.py/json` checks the immutable peer snapshot without
  modifying its owner file or rerunning its numerical audit.

Parsed messages, queue/TLS state, component-map lookup, memory/vector setup,
upstream fallback values, imported locks and the observational subscriber are
explicit fixtures. Existing reservation/policy/notification fixtures from the
resource reports remain. The controls cover finite surviving Life states,
existing updated keys and available vector capacity; they do not cover new-key
growth, death, concurrent callbacks, exception paths or the complete subscriber
graph. Dispatcher identity includes its original whole span, while inline switch
data at/after `0x142188a8c` is excluded from executable-prefix interpretation;
the historical 185 undecoded tail bytes are not missing executed instructions.
