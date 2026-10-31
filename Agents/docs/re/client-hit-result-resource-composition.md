# Received Hit results, current ES and independent resource assignment (3.29.3b)

## Result and scope

The actual immediate Actor handler can call the original `ProcessResult`
successfully without finding an action. In the supplied empty-listener states
below, that processing preserves Life, Mana and ES current values, fractional
carry and reservation inputs. A separate decoded resource-assignment body
changes the selected pool. This is an executed conditional receiving path,
not a whole-program proof that Hit damage is absent from the client.

One numerical dependency inside that same consumer is now closed: current
target ES can change **control admission**, independently of numerical damage.
When ES is positive and `virtual_energy_shield_protects_mana` is zero, the
inline trial uses the received 16-bit action sequence. Across its full input
domain, odd sequences enter the stun-suppression branch; even sequences
continue to the remaining admission checks. An ES assignment before versus
after processing can therefore change how the same result duration is handled.

This extends [received-result qualifiers](client-hit-result-qualifiers.md),
[Hit feedback](client-hit-feedback.md) and
[resource synchronization](../protocol/29-client-combat-signals.md).
It does **not** close accepted-Hit damage sampling, critical/mitigation-to-pool,
DoT source publication or generic Buff clock refresh. **R2/R3/R4 remain Partial.**

All controls interpret original instructions offline. No native target code,
client, capture, server or CI/deployment was run. Preferred image base is
`0x140000000`; offsets below describe decoded objects, not wire positions.

| Artifact | SHA256 |
|---|---|
| `Client/Game.exe`, 3.29.3b | `78a0e227189c263658a7f65a02f086428b859bfc114ae8e7ce8aab721ebd48d5` |
| `Stats.json` | `91c2ed42f6d8094a2704f56aea32f6f16243881b04aadbe4fdbbdd3a566f05da` |

## 1. Actual processing replaces the previous observational boundary

The parallel projectile investigation's fixed R47 revision
`f9a2df7f37561cd959863bdf8b6881400805937c`,
`Agents/docs/re/projectiles/results-and-protocol.md` section 3.1, establishes
actual result readers, defaults and handler behavior. Its `ProcessResult`
was deliberately observational. This pass reuses that decoding contract and
executes the downstream consumer rather than repeating payload work.
All 74 retained snapshots, 54 exact Git sources and parent-manifest hashes
were verified. The peer's numerical audit was not rerun; that revision is a
locally retained fixed snapshot, not a claimed publicly available commit.

For immediate records, `0x141e90f50` searches the current or retained actions.
With both collections supplied empty, `0x141e91078..0x141e91086` builds an
empty stat pair, and `0x141e91092` supplies a null action. The indirect call
at `0x141e9109b` executes `0x141e91580` through the installed Actor table
`0x1435daa70 + 0x230`. Both derived-zero and nonzero missing-action paths
are exercised here. This does not establish an official missing-action rate
or a particular skill's ordinary result route.

The original consumer resolves the supplied target, checks state and
relationship eligibility, reads its actual network provider, dispatches the
empty target/source signal lists and balances its processing-depth counter.
The returned true means this consumer accepted its selected processing path;
it is not a numerical damage confirmation. A supplied record without hit data
can also return true. Missing or ineligible targets return false.

The actual World checkpoint, World lookup-container getter `0x14164dc50`,
mode getter `0x14164dc70`, network numeric/Boolean getters and the native
empty-list publishers execute original instructions. Entity/component/type
lookups themselves remain fixtures. The original helper `0x14020eb20`
copies the computed direction value and marks it present; it is not a damage
or trigonometric calculation. Source action callbacks and custom event
programs are not executed on this null-action path.

## 2. Current target ES reaches the inline stun trial

The Life getter in the control-admission branch resolves the **target** Life
component. The actual constructor installs ES at Life `+0x210`; pool current
is `+0x30`. Thus the consumer's `Life+0x240` read is current target ES, not
maximum ES, an attacker attribute or a result damage amount.

| Original instruction range | Meaning within the surrounding admission gates |
|---|---|
| `0x141e91bc0..0x141e91bc8` | Current ES `<= 0` skips the ES trial |
| `0x141e91bcd..0x141e91bd9` | Nonzero target stat 5338, `virtual_energy_shield_protects_mana`, skips it too |
| `0x141e91bdb..0x141e91c26` | Computes the trial from zero-extended result sequence `+0x40` |
| `0x141e91c29..0x141e91c2b` | Odd low bit selects suppression at `0x141e91d37` |
| `0x141e91d37..0x141e91d53` | For mask `0x400`, zeros control duration `+0x34` without clearing that mask bit here |

The earlier hit-data, relationship and control-mask gates still apply.
Flask/leech handling precedes this trial; other immunity checks follow it on
the continuing branch. ES absence or an even sequence does not guarantee a
stun. This is the precise local guard, not a replacement for all stun rules.

The original arithmetic uses constants `0x69c16bd`, `0x1f31d` (127,773),
`0x41a7` (16,807), `0xb14` (2,836) and `0x7fffffff`.
For the entire **u16** sequence domain, the reduction by 127,773 leaves the
sequence unchanged, the zeroed low state contributes nothing, and
`16,807 * sequence` stays below signed32 maximum. Its parity is therefore
the sequence's parity. All **65,536** inputs were compared against the
original inline instruction stream, including its two actual branch exits.

There is no shared RNG call in this inspected arithmetic slice. That does
not prove random-stream behavior elsewhere, uniform sequence distribution,
independent trials or the official server's stun algorithm. Do not describe
this as a new accuracy/critical/damage roll. Action sequence allocation and
result ingress remain owned by the projectile investigation.

## 3. Resource synchronization order changes the control result

These witnesses supply a valid target, empty native/script listeners, null
source action, mask `0x400`, duration 1,000 and all other relevant immunity
inputs zero. To bound the *later* directional receiver without constructing
another action, target `frozen_in_time` is explicitly one: the original
`+0xc8 → 0x141e96500` then returns true early. This supplied guard is not
claimed to be an ordinary gameplay/default state. The earlier duration
rewrite and ES-dependent branch are the facts tested here.

| Sequence | ES synchronization relative to result | ES read by result | Duration afterward | Final ES |
|---|---|---:|---:|---:|
| 1 | None | 100 | 0 | 100 |
| 2 | None | 100 | 1,000 | 100 |
| 1 | Assign ES = 0 **before** result | 0 | 1,000 | 0 |
| 1 | Assign ES = 0 **after** result | 100 | 0 | 0 |

The last two rows execute the actual immediate Actor handler. Assignment
executes the original decoded pool-assignment body `0x141e69fb0` and setter;
it reuses the previously recovered `0x1b1` resource contract, not a new
wire parser or full World-to-Actor replay. Bit 4 excludes its separate loss
notification path. There is no claim about which order official traffic
chooses, or whether the result semantically precedes that resource change.

In separate controls, a successful result carrying critical-feedback bit
`0x20` leaves all three currents at 100 with carry `0.25` and reservation
zero. An independent Life assignment then changes Life to 70. This does not
establish a 30-damage hit: no source attribution or amount is supplied by
that result path. It distinguishes a received qualifier from resource
authority without treating empty subscribers as every possible subscriber.

## 4. Reproduction and limits

Ignored local evidence under `Tools/re/out/client-damage-audit/`:

- `r33-result-resource-composition.py` and
  `r33-result-resource-composition/{audit,verification}.json`:
  **65,830 controls / 1,552,391 interpreted instructions / 595 visited sites**.
  They include 139 direct-result, 19 result/assignment, 86 current-ES,
  50 actual-immediate-ingress and 65,536 exhaustive inline-trial controls.
- `r33-result-resource-fresh.py` and the batch's `fresh-check.json`:
  **44 function identities / 60 ranges / 5,066 decoded instructions**,
  39 instruction anchors, the additional constructor store and seven slots.
  The inherited whole dispatcher identity retains its inline data tail;
  that tail is excluded from interpretation, as in the earlier resource audit.
- `coordination-r47-reuse.py/json`: fixed snapshot identity and exact-source
  checks, without repeating the peer's payload audit.

Both verification scripts are same-agent audits; no independent worker or
native execution is claimed. Fixtures supply source/target component state,
empty current/retained actions and listener/program collections, external
lookups, existing queue/TLS state and resource-policy helpers inherited from
the earlier resource work. Directed negative-ES inputs test a guard, not
claimed game-reachable resource states. The complete Actor constructor,
nonempty scripts/subscribers, ordinary Fireball action callbacks and further
directional control-action construction are outside these compositions.

For server reconstruction, retain three independent contracts: authoritative
resource assignment, received result qualifiers, and current-client control
admission. Their ordering can affect visible/control state even when this
result-processing path makes no numerical deduction. The original open
numerical Hit and DoT obligations remain in the
[research workbench](client-damage-calculation.md#8-research-workbench-and-completion-criteria).
