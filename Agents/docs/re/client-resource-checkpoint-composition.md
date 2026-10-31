# Ordered resource assignment inside recovery (3.29.3b)

## Result and evidence ownership

An already-parsed `0x1b1` record can pass the original World checkpoint,
original parsed-record dispatcher and actual absolute pool setter, then return
to the original recovery controller. This composition verifies the numerical
consequences of the earlier [saved-rate/later-state contract](client-resource-read-order.md).
World dispatch is no longer an arbitrary mutation fixture in these controls.

The World interpretation is reused from the projectile investigation's R42
report `Agents/docs/re/projectiles/world-scheduling.md`, section 10, at fixed
commit `f8e87b8b7a9a763e06e4e3150e90a7e1a16805c7`.
This report adds the downstream resource composition; it does not rediscover
the checkpoint algorithm or repeat that investigation's standalone controls.
Its 61-file snapshot, including 48 exact Git sources and parent-manifest hashes,
was verified before reuse. This revision is retained locally and was not
available on GitHub when checked; it is not a working public-link dependency.
Wire headers and queue publication stay with that owner.

All evidence is offline and uses `Client/Game.exe`, SHA256
`78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5`,
and `Stats.json`, SHA256
`91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da`.
Addresses use preferred image base `0x140000000`. There was no native execution,
new capture, server implementation or CI. **R2/R3/R4 remain Partial.**

## Composed original path

```text
common recovery controller 0x141e6b320
  capture total recovery rate in EDI
  World+0x90 → 0x142183840
    increment/check component-local ordering checkpoint
    consume eligible head of supplied parsed-record queue
    0x142187480 → original 0x1b1 switch → 0x142188059
      resolve target Entity and Life component
      selector 0/1/2 → Life/Mana/ES pool
      0x141e69fb0: assign current and reservation fields
  integrate captured rate, if positive
  actual pool +0x40 gain method
    Life wrapper crosses World+0x90 once more before its late state reads
    numerical gain uses the pool's later current/carry/capacity
```

The controller's checkpoint call is at `0x141e6b42b`; the additional Life
gain-wrapper call is at `0x141e69e78`. The parsed dispatch calls the setter
at `0x1421880e6` for Life, `0x1421880cb` for Mana and `0x1421880b7` for ES.
These controls run through the setter's actual stores and return to the gain
method. They do not stop at the setter call or substitute its resource effects.

The checkpoint is a **component-local ordering counter**, not elapsed seconds,
a server tick or a second integration period. The peer report retains its
low-12-bit saturation, active-core/suppression/depth gates, head-only eligibility
and advance-before-dispatch behavior. In particular, a future or different-owner
head blocks a later eligible record; the resource composition preserves that rule.

The nonzero-mode path supports these component insertion points. Predictive's
outer batch-drain/queue-emptying contract remains distinct. Supplying an active
parsed queue here does not demonstrate that arbitrary mid-component insertion
occurs in Predictive or that the official server chose a particular checkpoint.

## Numerical witnesses

Each pool starts at integer current 100, carry 0.25 and maximum 1,000.
The supplied total recovery rate is 6,000 per minute and `dt = binary32(0.033)`.
The actual controller calculates approximately 3.3 units using its retained
binary32 order. An initial component checkpoint of `0x4000` is a fixture;
it does not identify a live component's actual index.

| Supplied parsed correction | Life invocation | Mana or ES invocation |
|---|---|---|
| At checkpoint `0x4001`, assign current 250 | Correction executes; ends 253 with carry approximately 0.55 | Same numerical result |
| At checkpoint `0x4002`, assign current 999 | Additional Life gain checkpoint consumes it; gain clamps to 1,000/carry zero | Only the first checkpoint occurs; record remains queued, ends 103/carry approximately 0.55 |
| Assign 250 at `0x4001`, then 999 at `0x4002` | Both assignments execute; ends 1,000/carry zero | First executes, second remains pending |
| First-checkpoint assignment 250, but captured recovery rate zero | Assignment still executes; no gain follows | Same numerical result |
| Different ordering-owner at queue head | No correction; ordinary gain ends 103 | Same numerical result |

Reservation changes and assignments to each of the other two pools are also
checked. A correction can change the target state without changing the
already-captured recovery input. The records here assign resources; they do
not themselves update the recovery rate. Earlier rate-change mutation fixtures
remain separately labelled in the saved-rate report.

This is useful for a server reconstruction: absolute assignment, rate input,
component update and within-component checkpoints have distinct contracts.
Treating every correction as a uniform end-of-frame overwrite would lose the
recovered conditional ordering. The original wire producer's choices still
need their own evidence.

## Reproduction and limits

Ignored files under `Tools/re/out/client-damage-audit/` are
`r27-checkpoint-resources.py`, `r27-resources-fresh.py` and
`r27-checkpoint-resources/`. They retain 32 function identities/48 original
ranges and 3,618 decoded code records. Most resource bodies were already
audited; World `0x142183840` is newly added to this investigation's inventory
from peer R42, not a newly discovered function.

165 composition controls interpret 41,214 original instructions across
427 visited sites. Fresh checks bind 18 instruction anchors, five table slots,
the actual switch index/branch and directed numerical witnesses. The dispatcher
retains its whole original span for identity, but interpretation excludes the
tail starting at `0x142188a8c`, which contains inline switch data. Its historical
whole-span decoder's 185 undecoded tail bytes are not an executable-prefix gap.

The parsed queue, active-core and TLS state, Entity/Life lookup results,
total-rate provider and external policy/notification behavior are fixtures.
All assignments use the supplied `+0x2c` bit 4 and remain positive, excluding
the separate assignment-loss notification and sign-transition paths.
Malformed streams, exception unwinding, outer mode scheduling and actual queue
production are outside these controls. This is same-agent interpretation,
not independent-worker review or an observed official execution.

The composition establishes a conditional resource synchronization route.
It supplies neither an accepted-Hit amount nor a DoT instance's target-rate
publication, selection, cap or expiry. Generic owner-stat refresh of existing
Buff speed also remains open.
