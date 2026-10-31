# Roadmap: Gems

Part of the [Roadmap](../Roadmap.md); what already works in this area is in
[status/gems.md](../status/gems.md).
Items keep the Roadmap's priority groups, and are in rough priority order within each group.

## 3. Gameplay

### Gems: how they work

Nessa's local full-colour assortment, alternative-variant pagination and separate progression-filtered Skills tab
are implemented; see the gem-vendor entry in [status/gems.md](../status/gems.md). White Skills combines both white
kinds. Verify the updated multi-page shop in the real client after Gateway restart. Future gameplay restrictions
belong in Skills; the colour tabs must remain complete development tools independent of campaign progression.
Gems have unlimited stock on all Nessa tabs; verify the native shop refresh after repeat purchases as part of the
same live check. Automated payment, replenishment and save/reload coverage is in NessaGemPurchaseTests.

The gem system as agreed on 2026-10-20, built in the parts below (#62, #63, #64).
What is known about the client and the data is in [19](../docs/protocol/19-gems.md).

- **A gem item** is a base type and a variant: the normal gem;
  a transfigured ("of X") or Trarthan ("of Trarthus") variant, which acts as its own skill with its own effect, tags
  and mechanics; or a Vaal gem, which grants the regular Vaal skill and its base skill (transfigured or Trarthan if
  its variant is).
  A gem's level comes from its experience, up to the cap of its experience type: 20 for most gems, 3 for exceptional
  supports, 4 for Awakened Empower, Enhance and Enlighten, 5 for the other awakened supports, 1 for single-level gems.
  Corruption and bonus levels can go higher, never past 40.
- **Rules:** socketing a gem whose level or attribute requirements aren't met is refused.
  A socketed gem that stops meeting them is disabled: its skill stays on the skill bar but can't be used, its supports
  don't apply, its level-up icon is greyed, and the gem stays in its socket.
  Each socketed skill gem is its own skill, with an id kept while it stays socketed; an aura ends when its gem leaves.
  Support gems are never granted as skills.
  Awakened supports other than Empower, Enhance and Enlighten (disabled since 3.28), the "[DNT]" placeholder variants
  and the gem rows named "[UNUSED] …" or "WIP …" are never created.

### Gems 1: gem levels, stable skill ids and requirements (#62)

- **Known:** each socketed gem keeps its skill id while it stays socketed, and an aura or Righteous Fire ends with its
  gem (part (a)).
  Part (b): a gem's skill uses its level (from its experience) and quality, transfigured, Trarthan and Vaal gems grant
  their skills, the variant is the SkillGem data's u32, and `/gem` makes any gem ([19](../docs/protocol/19-gems.md)
  §1, §3); not yet checked with the real client.
  Part (c), the requirements, is in: the level is `PlayerLevelReq` (Haste 24 at gem level 1 … 70 at 20, as the
  client's tooltips) and the attributes are the client's own formula, fitted from `0x14246fc40` and checked against
  570 poedb tooltip cells with no mismatch (`trunc((3·level_req + 11)·(percent/100)^0.9·k + 0.5)`, `k` 0.5 for
  supports and 0.7 for skills, none below 14).
  The level stops a gem's experience at the first one the character does not meet;
  socketing below either is refused, and a gem the character is below (after `/set_level` down) makes its item
  inactive: red on the client, its mods and gems not counting, its skill gone from the list so it cannot be cast, and
  an inactive weapon falls back to the unarmed attack (Status "Gem requirements").
  Checked live 2026-10-23 for the level and the attributes (`/set_stat intelligence` above and below the 155 a level
  20 Fireball needs: at 130 the client reds the gem and the wand's mods stop counting).
  Left open: the client still draws an inactive item worn (red wand in hand) — check with the official client what it
  does there, and whether to move the item out of the row instead ([13](../docs/protocol/13-items.md) §2d).
- **Start:** check part (b) live (a level 20 Fireball's damage and cost, a quality aura, Ice Nova of Frostbolts, a
  Vaal gem's two skills).
- **Done when:** a `/gem` gem of any level and quality casts with that level's numbers, transfigured and Vaal gems
  grant the right skills, skill ids don't change when other gems move, and requirements match the tooltips and are
  enforced.

### Gems 3: support gems and links (#64)

- **Known:** the skill record has a support list ([11](../docs/protocol/11-skills.md) §3);
  granting a support as a skill crashes the client ([13](../docs/protocol/13-items.md) §6).
  GrantedEffects holds each support's compatible skill types and its skill-bar letter;
  GrantedEffectsPerLevel its cost multiplier.
- **Implemented:** linked native compatibility, complete support references, duplicate resolution and ordinary
  damage, area, speed, critical, cost and reservation calculations ([25](../docs/protocol/25-gem-supports.md)).
  Cyclone with Increased Area of Effect was checked with the real client.
  Item-granted support references and local socketed-gem level/quality bonuses now share the resolver and calculations.
  Item-support records, duplicate resolution, rolled levels and Delve more damage have automated coverage.
- **Next:** implement special execution mechanics separately: triggers, repeats, projectile branching,
  totems that use their skill (only Rejuvenation Totem's aura has an executor, and monsters do not attack totems;
  [11](../docs/protocol/11-skills.md) §10g), mines, minions, leech and conditional effects; verify each against
  native data and live behaviour.
  Cyclone's item-granted badge letters/colours and supported panel values were checked with the real client.
  Verify native refresh after mod changes and controlled before/after local level/quality changes.
- **Done when:** Fireball linked with Arcane Surge shows the support's letter and pays the multiplied cost, a modelled
  support such as Faster Casting changes the skill, and the support crash is gone.

### Gems later: quality, getting gems, corruption, bonus levels

- **Quality** from Gemcutter's Prism needs currency on items (C→S 0x4a, "Items" above).
- **Getting gems:** quest rewards, the town gem vendors and monster drops are in (every `QuestRewardOffers` row the
  server can build, by class; the vendors' completed-quest/class progression, Siosa and Lilly, and the 3.29
  cross-class supports; a 4% gem share of item drops by DropLevel;
  [19](../docs/protocol/19-gems.md) §5).
  Remaining: a live check of Lilly and Siosa once Act 6 and The Library are reachable (the towns have no walk-map
  capture yet).
- **Corruption and Vaal skills:** Vaal Orb outcomes on gems (one level over the cap, quality up to 23%, the Vaal
  variant), the Lapidary Lens's double corruption, and supported Vaal skill executors.
  Soul collection/budget payloads are implemented; native acceptance remains in skills-and-combat.
  Goes with item corruption (#60).
- **Bonus levels:** local all-gem, active/support and GemTags family level/quality bonuses now feed the effective gem
  calculation, including Empower; stored experience and intrinsic requirements are unchanged.
  Verify the item-bonus path with the real client and cover remaining special cross-item and conditional modifiers.
- **Awakened supports:** their effects override their regular versions instead of stacking;
  their level requirements start at 72.
