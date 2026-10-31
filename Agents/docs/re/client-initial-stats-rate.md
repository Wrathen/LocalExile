# Initial Stats bytes and numerical Life loss (3.29.3b)

## Result and boundary

The installed initial `ClientInternalStats` reader can consume an encoded total
Life-loss rate, store it through the real network override setter, and supply
that value to the actual Life integration code. This extends the
[parsed-update composition](client-network-rate-composition.md) to an original
**component-body byte reader**, independently of the later `0x1b3` update route.

The reader also restores the target's hit entropy, entropy countdown and
initialization byte. Receiving these fields and receiving numerical rates are
distinct contracts; neither supplies an accepted-Hit damage calculation.

These are static original-byte observations and offline instruction
interpretation with synthetic valid bodies. No client or server was executed,
and no official capture or complete Entity creation packet is claimed.
R0/R1 retain their established scope; R2/R3/R4 remain Partial. Source-instance
selection, aggregation/cap and publication still require separate evidence.

| Input | Identity |
|---|---|
| Executable | `Client/Game.exe`, x86-64, 3.29.3b; preferred base `0x140000000` |
| Executable SHA256 | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` SHA256 | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |
| Key convention | Native key `k` is extracted row `k - 1` |

## 1. Installed receiver and supplied bytes

Stats constructor `0x1421c2ad0` installs table `0x1435dd4e8` at
`0x1421c2b28`. Its `+0x88` slot is initial reader `0x1421c2f40`.
The constructor creates the network provider through `0x1421c3700` at
`0x1421c2b7c`, then aliases it in component `+0x40` and `+0x20` at
`0x1421c2b92/0x1421c2b96`. These are the provider identities used by
the reader and ordinary resource consumers, respectively; no similarly named
native contribution object is substituted.

The component body has this ordered grammar:

```text
compact unsigned pairCount
repeat pairCount times:
    compact unsigned nativeKey
    compact signed value
little-endian uint32 hitEntropy
little-endian binary32 entropyCountdown
byte entropyNeedsInitialization
```

The unsigned primitive is `0x141e61010`; the signed primitive is
`0x141e490e0`. They read the buffer and advance its actual `+0x10` cursor.
Their encodings have lengths 1..5 bytes, with 7/14/21/28/32 payload bits;
multi-byte payload order is most-significant byte first. The signed primitive
sign-extends the corresponding payload. It is **not zigzag** decoding.
Five-byte controls use canonical prefix `0xf0`; alternate encodings and short
streams are outside this result.

The pair loop loads component `+0x40` at `0x1421c2f60`, reads count/key/value
at `0x1421c2f67/0x1421c2f83/0x1421c2f8d`, and calls the actual setter
`0x1424cfaf0` at `0x1421c2f9a`. After the loop, raw stores at
`0x1421c2fd3/0x1421c2ff6/0x1421c301a` populate `+0x28/+0x2c/+0x30`.
The entropy field identities and subsequent prediction behavior are established
in [contact production](client-hit-production.md#entropy-initialization-and-expiry-use-the-actual-component-tick).

For example, one valid body is:

```text
01 8d 22 97 70 11 00 00 00 00 00 40 40 00
```

It means one pair, key 3362 with signed value 6000, entropy 17, countdown
3.0 and initialization byte zero. Key 3362 is
`total_damage_taken_per_minute_to_life`. These bytes are an independently
constructed test body, not evidence that an official server transmitted them.

## 2. Storage, notification and resource consequences

The real setter replaces the existing pair and dispatches its notification;
the observational subscriber in the controls reads the new value through the
real getter. The later full Life step reads that same provider through its
aliased component `+0x20`, divides the total rate by 60, multiplies by supplied
`dt`, and invokes the actual Life reducer with its integer/fractional state.
The [previous composition](client-network-rate-composition.md) retains its
exact integration order and checkpoint boundaries.

All examples below start with current Life 100, carry 0.25, maximum 1000,
no recovery and an explicit total-loss override initially zero unless stated.
The supplied binary32 `dt` is 0.033, not a measured official frame interval.

| Pairs decoded before the Life step | First step current / carry | Second step current / carry |
|---|---|---|
| 3362 = 6000 | 97 / approximately -0.05 | 94 / approximately -0.35 |
| Raw fire key 2023 = 6000 | 100 / 0.25 | 100 / 0.25 |
| 3362 = 6000, then 3362 = 0 | 100 / 0.25 | 100 / 0.25 |
| 3362 = 0, then 3362 = 6000 | 97 / approximately -0.05 | 94 / approximately -0.35 |
| Two identical 3362 = 6000 pairs | 97 / approximately -0.05 | 94 / approximately -0.35 |
| No pairs; pre-existing explicit 3362 = 6000 | 97 / approximately -0.05 | 94 / approximately -0.35 |

Duplicate keys are sequential assignments and sequential notifications, not
addition of damage sources. An explicit zero masks fallback. The inspected
reader itself does not clear the provider before reading pairs: its zero-count
case preserves pre-existing overrides. Ordinary construction creates a fresh
provider earlier; the reused-provider control does not redefine Entity creation
as a general partial-update protocol.

Likewise, raw fire 2023 does not replace an explicit final 3362 override on
this tested setter/subscriber path. This is not a claim that every upstream
native provider or subscriber ignores raw fire inputs. The controls do not
infer source selection, mitigation or shared cap from the transported total.

## 3. Bounded provenance of the network setter

Original relative-call/jump discovery finds five direct calls to
`0x1424cfaf0` in unwind-owned code, all already belonging to documented
network-reader/dispatcher paths:

| Call site | Owner | Input path |
|---|---|---|
| `0x1421c2f9a` | `0x1421c2f40` | Initial owner Stats body above |
| `0x1421881dd` | `0x142187480` | Parsed World `0x1b3` owner Stats pairs |
| `0x14219464d` | `0x1421940a0` | Actor skill/stat-set update pairs |
| `0x142197291` | `0x142196ce0` | Initial primary network skill pairs |
| `0x14219740a` | `0x142196ce0` | Initial additional stat-set pairs |

The bounded scan finds no unowned matching relative branch, literal eight-byte
preferred-VA pointer, or ordinary REX RIP-relative `lea` taking this setter's
address. It does not cover computed/register address formation, every encoding,
inline writes or every possible mutation of the override vector. This is not
an exhaustive proof that no effect can update Stats through another route.
The calls are reused identities, not five newly discovered functions.

The positive responsibility boundary is narrower and useful: both initial
owner state and later owner updates can supply resource rates through the
same override store; the selected local Life calculation can consume them
without constructing an ignite/poison/RF source list on this path. Determining
which upstream operation produces those rates remains an independent task.

## 4. Reproduction and limits

Private ignored evidence is under `Tools/re/out/client-damage-audit/`:

- `r30-initial-stats-rate.py` and `r30-initial-stats-rate/audit.json` retain
  42 reused function identities, 78 original byte ranges and 4,479 decoded
  instruction records, including both complete compact readers. No newly
  discovered function is claimed.
- `r30-initial-stats-rate/verification.json` records **243 controls**:
  22 unsigned primitive, 35 signed primitive, 127 initial-reader and 59
  reader-to-Life controls. They interpret **279,450 instructions / 981 visited
  sites**, compared with a separately written encoder and sequential scalar
  resource model. Both checks are same-agent work.
- `r30-initial-stats-fresh.py` and `fresh-check.json` reread original ranges,
  three receiver slots, 25 instruction anchors, three named keys and the bounded setter
  references. The enclosing dispatcher's historical inline-data tail remains
  byte-identity coverage, not interpreted executable code.

Cases include compact-width/sign boundaries, key widths through native key
23346, zero/127/128 pair counts, repeated keys, nonzero starting cursors and
trailing sentinel bytes. Updated keys already exist, and vector capacity is
sufficient. The original reader consumes bytes; the buffer, provider, component,
resource state and observational subscriber are allocated fixtures. Other
World/TLS, resource reservation/policy and external-event fixtures from the
preceding composition remain explicit.

No truncated-stream/exception behavior, noncanonical encoding, allocation growth,
death, NaNs, concurrent notifications, full subscriber graph, whole creation
packet or official scheduling is reconstructed. This supplement does not close
DoT instance publication/selection/cap, existing-Buff clock refresh or accepted
Hit-to-resource loss, and it does not duplicate the projectile-side outer
Instance/frame work.
