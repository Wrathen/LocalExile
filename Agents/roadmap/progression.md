# Roadmap: Experience and the passive tree

Part of the [Roadmap](../Roadmap.md); what already works in this area is in
[status/progression.md](../status/progression.md).
Items keep the Roadmap's priority groups, and are in rough priority order within each group.

## 3. Gameplay

### Character progression: the rest of the Passive Skill Tree

- **Known:** experience, levels, S→C 0x33 and C→S 0x34 (allocate) work ([15](../docs/protocol/15-progression.md)).
- **Known:** gold respec at Bestel (C→S 0x36 / 0x37) and masteries (0x35) work.
- **Known:** refunds with refund points (0x36 flag 0; 0x33's first u16) work;
  Orbs of Regret (right-click, 0x58) add them.
- **Known:** the ordinary tree's 21 large jewel sockets accept ordinary, magic and rare jewels (pane placement,
  save/load, global mods, refund guard); unique jewels use the same path but have no item source yet.
  See [unique-jewels.md](unique-jewels.md) for the 185-entry catalogue and the phased unique-jewel plan.
- **Next:** tattoos: all 100 items, protocol, target/limit rules and interactions are documented in
  [tattoos.md](../docs/game/tattoos.md). The client flow, 32 plain stats, nine Makanga gates, three Journey
  tattoos and Journey Makanui are client-verified (Hinekora/Kitava Makanga are inert; Ancestral Bloodlines is
  partial): C->S `0x77`, `0x33`'s fifth list, item consumption, relog and Orb of Scouring removal. The five
  `PassiveSkillOverrides.Limit` groups are enforced server-side; Makanui saves its random keystone. Next: the
  35 condition/skill rows and Loyalty; the seven Forbidden ascendancy tattoos use a separate client flow. Also
  still open: replace/reallocation and the 50-tattoo cap.
- **Accuracy Mastery:** all five effects currently offered by the target client's Accuracy group are modelled
  for the existing default-attack accuracy roll ([Status](../status/skills-and-combat.md),
  [11](../docs/protocol/11-skills.md) §4–5): Accuracy = Intelligence, +3 per Dexterity instead of +2, +500 / -2 per
  level, 40% more accuracy against Unique Enemies and 50% more against Enemies in Close Range (both at the attack's
  accuracy roll; close range is the constant 20-unit radius).
  Minions' Accuracy equals yours is not offered by that target group and remains unsupported with minion accuracy.
  Gem attacks still need their own accuracy rolls before these target conditions can affect them.
- **Known:** pinned ordinary/Phrecia topology is available as
  [read-only analysis artifacts](../docs/dev/passive-graph-data.md), without installing a client graph.
- **Known:** command selection, saved identities, separate point awards, paid refunds and ordinary/ascendancy
  graph connectivity now have automated coverage; see [scope and limitations](../docs/game/ascendancies.md).
  Ordinary command selection, refunds, relog and a Gateway restart are verified with a native Duelist/Slayer smoke.
- **Known:** Antiquarian's complete supported node consumers and three legal builds have native acceptance (#98).
  Whaletooth scales actual Enduring Cry recovery; paid refunds, transient effects and durable class progress pass.
  See [Status](../status/progression.md) and the [model/evidence audit](../docs/game/antiquarian.md).
- **Known:** Gambler's complete supported graph consumers and both Block Luck choices have native acceptance.
  Ordinary incoming elemental ailments give Risk Aversion an actual avoidance consumer.
  Current-base tests and exact related-head composition pass; preserve the manual lethal-ignite Recoup resolution
  when composing Whisperer, as recorded in [Gambler](../docs/game/gambler.md).
- **Start:** quest passive/refund rewards and the native Labyrinth/Altar flow (#151).
  Phrecia play stays gated by default; Gambler's explicit opt-in verifies the separately installed graph read-only.
  Other Phrecia classes retain their default gate; Antiquarian requires its exact verified installed graph opt-in.
  Do not add automatic archive writes or require the alternate graph for ordinary characters.
- **Known:** Forest Tracking derives attack damage from complete groups of final evasion (#243).
  It composes with Steel Willed; allocation/refund/reload, Iron Reflexes and supported damage have automated coverage.
  Other Daughter of Oshabi mechanics and Phrecia client admission remain separate work.
- **Known:** family gem levels for Herald, Vaal, Link and Raise Zombie reach the common effective skill level.
  Connected allocation/refund/reload has automated coverage (#244).
  Family-specific execution engines and Phrecia client admission remain separate work.
- **Known:** Aristocrat's nodes all have server consumers and Gateway coverage; `Game_VERIFIED_ARISTOCRAT=1` opts in.
  **Next:** native acceptance on a client with the reviewed combined graph, as for Gambler.
  See [Aristocrat](../docs/game/aristocrat.md).
- **Done when:** quest rewards persist and the native Altar can choose an ordinary ascendancy, with real-client checks.
  Implementing all 19 Phrecia classes' special gameplay is separate from this selection lifecycle.
  The [class acceptance list](../docs/game/phrecia-class-completion.md) covers all notables and remaining dependencies.
  Behemoth's historical native acceptance is at `9657e0ad`; current-main integration has automated checks only.
  Repeat whole-class native acceptance after the shared class integrations are finalized.
  Their completed scope and evidence are in [Status](../status/progression.md).
