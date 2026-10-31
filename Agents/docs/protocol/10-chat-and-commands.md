# 10 — Chat input, server replies and dev commands

Status (live, 2026-10-16): everything typed in the chat box reaches the Gateway. Slash commands run on the server and
answer with chat lines (S→C 0x17, one per line of the reply; message boxes until 2026-10-21). `/set_speed` works since 2026-10-17: the client reads the new `movement_velocity_+%` and
walks faster. The first version sent the wrong key (see §3). `/spawn_mob` works since 2026-10-17: it puts a visible,
idle monster next to the player (§5).

## 1. C→S 0x15: chat input

| Field | Encoding |
|---|---|
| text | compact wide string (`LoginWriter.WStr`: u16 count, per code point 1 byte < 0x80, 0x80 + UTF-16LE, 0xC0 + u32) |
| u8 | 0 in every capture |

What arrives is the text as typed: slash commands as is (`/spawn_mob 42`), plain chat with the channel prefix the client
added (`#hello server` for global). The client handles none of our commands locally, but the words of Commands.dat
rows with `SendsGenericCommandPacket` never arrive as chat: the client sends C→S 0x16 with the row + 1 instead
(`/destroy` 9, `/hideout` 45: [21](21-hideouts.md) §3), so our commands must not reuse those words. `InstanceChat.Length` frames it
(the length depends on the text), and `SplitClientMessages` uses it for id 0x15.

The prefixes are `#` global, `$` trade, `%` party, `&` guild and `@` whisper; local has none (live 2026-10-25, typed in
the chat box: `hello local`, `#hello global`, `$hello trade`).
After a trade line the chat box opens with `$` already typed, so the next line goes to trade too.
The client's sender is `0x14215ddd0` (instance, text, linked items): it runs the client's own slash commands
(`0x14215c3b0`, Commands.dat), then sends the rest as 0x15.
The Gateway echoes a plain line as a 0x17 chat line from the character (`ChatChannels.Echo`, `Program.Chat.cs`): sender
`Name` for local, `#Name` for global and `$Name` for trade, the text without its prefix.
Live 2026-10-25 they showed as `[20:33] OfflineWitch: hello local` with the name green, `#OfflineWitch: hello global`
with the name red and `$OfflineWitch: hello trade` with the name orange (the chat timestamps option was on).
Party, guild and whisper lines are not echoed (there is nobody to send them to).

## 2. Server → client text

Candidates that read a wide string were found by mapping every S→C factory case to its class and read function (slot
2) and keeping those that call the wstr reader `0x140e0cbe0`. Each was then sent live with `/raw`:

| Id | Layout | What the client did |
|---|---|---|
| **0x0d** | wstr | **Message box with the text and an OK button.** Class `0x1435bf650`, dispatcher case 0 → listeners at +0xa1f0. Used for replies. |
| 0x12d | u16 code, wstr | Shows `BackendErrors.dat` row `code - 1` (1 → "An account with this name already exists."). The UI listener `0x140be6ef0` prints the row text as is and uses the wstr only to compare a name, so `{0}` is not filled in. |
| 0x26 | u16, wstr | Disconnect with a reason (handler `0x142149ca0` closes the socket). Do not send it. |
| 0x157 | wstr, u8 | "System challenges you to PvP" popup (duel request). |
| 0x10 | u32, wstr | Nothing visible (no dispatcher case). |
| 0x188 | wstr, u16 | Nothing visible. |
| 0xa9 | party info | Not chat. |
| 0xc9 | u8, wstr, wstr | Friend presence map. Not chat. |

**S→C 0x17: chat line** (class `0x1435dcec0`, read `0x140e0c710`, dispatcher case 2 → listeners at instance +0xb78 →
chat panel `0x140179f20` / `0x14017a300`; verified 2026-10-20 with `/raw`): wstr sender (its first character picks the
channel), wstr text, wstr guild tag, u16 flags (raw; bit 8 sends the message to the +0xd78 listeners instead, bit 3 adds a
field read by `0x1408402a0`, bit 7 sets a mode 5), u8, u8, 8 bytes, u8, u8 n + n × (u32, item) linked items. An empty
sender with every other field 0 prints the text alone (`InstanceChat.ChatLine`). The client's `<green>{text}` markup
in the text does not colour the line (live 2026-10-21). But with an empty sender, the text up to the first `: ` is drawn
green like a local speaker's name and the rest white (live 2026-10-21: `/com accuracy` was green up to `default:`); the
`/commands` header ends with `:` for that. Sender prefixes (live 2026-10-21, `/raw`): `#Test` shows `#Test: line` in
the global colour (red), `$Test` `$Test: line` in trade orange, `@Test` `@From Test: line` in whisper purple, the whole
line coloured; `%` and `&` show nothing without a party or guild. A sender of the prefix alone (`$`, `#`) prints the
text white with no name, like an empty sender. So a coloured line always shows its sender and a colon. With an empty sender, `0x14017a300` wraps the text in a colour
looked up by `0x142426ea0(byte & 0x1f)` (the byte at +0x158 of the object at instance +0xc810) and runs it through the
markup parser `0x14018fb50`; `ChatColours.dat` has rows Default, Red, Green, Blue, ChayulaMessage. Per-line colour: open.

**Chat channels: C→S 0x134 and S→C 0x135** (on the instance connection; live 2026-10-25).
After entering an area the client joins a global and a trade channel by itself: the in-game listener `0x140d5bf60`
(instance +0x240 listeners, once per game state, flag +0xa08) calls `0x142169250` twice with the channel from the
`join_global_channel` / `join_trade_channel` settings (0 → 0xffff, "any"; `0x14190f7d0` / `0x14190f8a0`) and the
language (`0x141912640`).
`/global N` and `/trade N` (Commands.dat rows 41 and 42) are handled by the client (`0x14215c3b0`) and call the same
function with N.
`0x142169250` sends **C→S 0x134** (class `0x1435bf730`, write `0x141f9ae30`, 7 bytes):

```
u16 0x0134, u16 channel (0xffff = any), u8 kind (0 global, 1 trade), u8 automatic (1 after entering, 0 for a command),
u8 language
```

Then it sets a 5 s deadline (instance +0xcf18 for global, +0xcf08 for trade; the channel at +0xcf10 / +0xcf00).
A 0xffff request takes the channel the client remembers (`0x143a60ce8` global, `0x143a60c98` trade; 0xffff until a
command names one).
When the deadline passes, the network update `0x142148880` fires the instance +0xe78 listeners, and the chat panel's
`0x14017dd10` prints "Timeout while joining global chat channel N" (ClientStrings row 207) or, for 0xffff,
"Timeout while joining global chat." (row 206).
Live 2026-10-25 the two requests were `0134 FFFF 00 01 00` and `0134 FFFF 01 01 00`; `/global 821` sent
`0134 0335 00 00 00`.
An older capture had `0134 0334 00 01 00` (global, channel 820, the number in the timeout seen before) and
`0134 0001 01 01 00` (trade, channel 1).
A kind other than 0 or 1 is ignored (logged).
The Gateway took 20 bytes for 0x99 then: 0x99 is 4 bytes (class `0x1435c0258`, write `0x141f95200`: two u8), and the
same chunk held the two joins and 0x129 (id only).

**S→C 0x135** (class `0x1435dc240`, ctor `0x1421a6c00`, read `0x1421a6cd0`; dispatcher case 0x56) answers it:

```
u16 0x0135, u16 channel, u8 kind (0 global, 1 trade), u8 language
```

The client clears the deadline and keeps the channel as the current one: channel and language at instance +0xcef8
and +0xcefc for global, +0xcef0 and +0xcef4 for trade.
Then it fires the instance +0xef8 listeners.
The chat panel's Global and Trade tabs, greyed out before, become usable.
It prints no line itself: S→C 0x12d with BackendErrors code 240 (JoinedGlobalChannel) printed "You have joined global
chat channel {1} {0}." with the placeholders unfilled, so the Gateway sends that text filled in as a 0x17 line instead.
The Gateway (`Program.Chat.cs`, `ChatChannels`) answers every 0x134 with 0x135: the number asked for, else the
character's last channel of that kind, else channel 1, and the language echoed.
After the answer it prints "You have joined global chat channel N." when the channel is new for the character or a
command asked for it.
The client joins only once per game session: after an area change (a new instance connection, live 2026-10-25 with
`/goto`) the listener runs again but flag +0xa08 is set, and the new instance's channels are 0, so the tabs would grey
out again.
So the Gateway sends 0x135 for each channel the character joined (`ChatChannels.Current`) on every area entry, right
after the skill bar.
Live, the global and trade channels were 821 and 822 again after `/goto`, and `#` lines still worked.

## 3. S→C 0x1b3: stats update

World group; class `0x1435dda98`, read `0x141fe1350`. The world handler (`0x142187480`, case 0x1b3) looks up the
object's Stats component and calls `0x1424cfaf0(stats, key, value)` for each pair.

```
u16 0x1b3, u32 object id, u32 0, u16 0,
compact uint n, n × (compact uint stat key, compact int value)
```

- Unsigned compact (`0x14089cad0`): < 0x80 is 1 byte; then 0x80 + 14 bits, 0xc0 + 21 bits, 0xe0 + 28 bits; else 0xf0 +
  u32. Signed compact is `0x141fa3d60` (`InstanceProtocol.CompactInt`).
- The stat key is the client's own stat id: the int its getter `0x1424cfa40(stats, key)` binary-searches in the sorted
  (key, value) vector at stats+0xf0, which `0x1424cfaf0` fills. With the vendored schema it is **the Stats.dat row + 1**
  (key 0xf5 = row 244 `movement_velocity_+%`; checked on every key an attack reads, [11](11-skills.md) §8). `StatKeys`
  in the Gateway lists the ones in use.
- Walk speed (`0x141e7c0b0`): base (`+0x548`) × (1 + key **0xf5** / 100, at least −100). With a flag it is also
  multiplied by (1 + key 0xce6 / 100). It is capped by key **0x44f** (`movement_velocity_cap`) when that is positive.
  Measured live: sending 0xf5 = 200 makes the getter return 200 and the walk visibly faster.
- The Stats component has no 0x2ba (component update) handler: its vtable slot +0xa0 is a no-op. So 0x1b3 is the way to
  change stats.

An explicit pair overrides the getter result, including when its value is zero.
Only an absent key falls back to the upstream interface at provider `+0xe0`; a null upstream returns zero.
`0x1421c2ad0` constructs the network wrapper at component `+0x20` through `0x1421c3700`.
The wrapper table `0x14363bd90` has `0x1424cfa40` as its getter.
Its upstream comes from the type object's shared pointer at `+0x10`.
The type constructs native stats through `0x141e496c0`/`0x1424fb790`, installing getter `0x1424fc1e0`.
That getter reads stored values; a separate native update transaction can evaluate and publish derived outputs.
The constructor's flag-1 path skips the optional upstream subscription.
The setter notifies the changed key through `0x1424cfd00`; full derived-cache invalidation is not established.
Do not assume raw network inputs automatically recompute all omitted derived outputs.
See [client damage research](../re/client-damage-calculation.md#3-stats-interfaces-explicit-overrides-and-upstream-calculations)
for the separate raw/derived-stat layouts and Life's actual reader binding.

## 4. Commands (Gateway, `Program.Commands.cs` `Commands`)

`/hit`, `/spellhit` and `/blockhit` are synthetic incoming enemy contacts for the
[recent-hit armour model](../game/steel-willed-armour.md), including blocked and zero-damage results.
They refuse paused, leaving, closed or dead owners before combat mutation.
`/set_stat` can replace raw chance inputs 20680/8404; authoritative outputs 4534/12268 always come from contact history.

For tools outside the game (the desktop launcher's console), the built Gateway lists its commands without starting:
`dotnet src/GameServer.Gateway/bin/Debug/net10.0/GameServer.Gateway.dll commands` prints each command's name, full
usage and argument kinds as JSON lines (`Program.CommandUsages`, the loaded mods' commands not included).
A command declares the kinds of its leading arguments where it registers (`ChatCommands.Add`: area, monster, item, gem,
stat, npc, quest_flag), and `GameServer.Gateway.dll values <kind>` prints what such an argument can be, from the catalog
the command looks it up in (`Program.CompletionValues`): a name only one row has, else the row's Id with the name as
its detail; `/goto`'s areas say whether the Gateway generates them, a capture has them, or only the client can.
Both run before `ServerLog`, so their output is JSON lines only.

| Command | Effect |
|---|---|
| `/commands [page \| name]`, `/com …` | lists the commands, 8 per page (`/com 2`, `/com 3`, …; after four blank lines a green header, then each command's syntax only), or the full usage of one command (`/com item`; otherwise every command whose name contains the text) |
| `/raw <hex>` | sends the bytes as one S→C message (dev probe; spaces allowed) |
| `/set_speed <percent>` | 0x1b3 `movement_velocity_+%` = percent − 100 on the player; the server's walk estimate uses 37 × percent / 100 cells/s |
| `/set_stat <stat name or client key> <value\|off>` | replaces a raw stat or removes its development replacement |
| `/spawn_mob <row, Id or name>[@level] [friendly] [normal\|magic\|rare\|unique] [name]` | 0x20 + 0x2b9 + 0x1a9 for a MonsterVarieties row at a level (default 1), hostile unless `friendly`, with a rarity and an optional name, a few cells from the player (§5) |
| `/set_level <1 to 100>` | sets the character's level in the account store (Auth control op `set_level`) and sends it in place: the level's experience to the Player component (0x2ba, [04](04-player-entity.md) §4.6; the client derives the level from it), the level's stats (0x1b3: level, life, mana, accuracy, …, [11](11-skills.md) §4), the skills again (0x2ba Actor) and full life (0x1b1) |
| `/heal` | full life and mana for the player (0x1b1); from 0 it also revives |
| `/add_xp <amount>` | experience as from a kill: levels up, saved ([15](15-progression.md) §1) |
| `/passives` | the passive and refund skill points and the allocated passives |
| `/add_passive_points <n>` | adds n extra passive skill points (the 0x33 `extra_skill_points` field), saved with the character; sends the tree (0x33) |
| `/add_refund_points <n>` | dev shortcut: adds n refund points (0x33's first u16), saved with the character; sends the tree (0x33). Orbs of Regret also add them (right-click) |
| `/gold <amount>` | adds gold, or takes it with a negative amount (not below 0); the account's gold (every character of the account shares it), saved and sent as S→C 0x2a9 ([04](04-player-entity.md)); every area entry sends the saved amount too |
| `/add_passive <PassiveSkillGraphId>` | allocates one passive node through the same checks as C→S 0x34, saved and sent back (0x33) |
| `/reset_passives` | refunds every passive, saved, and sends the tree (0x33) |
| `/resistances` | the character's uncapped and capped fire/cold/lightning/chaos resistance |
| `/hit <type> <damage>` | damages the player through resistance and enabled hit mitigation; see below |
| `/accuracy [monster level]` | final accuracy rating and attack hit chance against a normal and an evasive monster of that level (default: your level; spells always hit) |
| `/defences [monster level]` | armour, physical reduction and evasion with the chance to evade a monster of that level |
| `/base_defence [0 to 31]` | show or set the held armour item's shared base roll; saved with the item |
| `/auras` | active auras, their flat/percent reservation, reserved and unreserved mana, reservation efficiency and aura effect ([11](11-skills.md) 13) |
| `/suppression` | Spell Suppression chance and damage reduction |
| `/spellhit <fire\|cold\|lightning\|chaos\|physical> <damage>` | dev: a spell hit through resistance and Spell Suppression, rolling the suppression chance |
| `/crit` | attack and spell critical strike chance and multiplier from the unconditional passives and weapon base |
| `/block` | attack block and spell block chance, caps and effective lucky chances |
| `/charges [<endurance\|frenzy\|power> <n>]`, `/charges add <type> [n]`, `/charges clear` | dev: each charge type's count, maximum, minimum, duration and time left; sets a count, gains like a skill (refreshing the duration) or clears them ([11](11-skills.md) §15) |
| `/block_luck <attack\|spell\|both\|off>` | dev: force lucky block for testing |
| `/blockhit <attack\|spell> <fire\|cold\|lightning\|chaos\|physical> <damage>` | dev: a hit through block and resistance |
| `/god` | toggles 1000% increased attack and cast speed, 100% of life and 10 000 mana regenerated per second, +10 000 life, +1000 to each attribute ([11](11-skills.md) §12); remains enabled for this character across area changes and relogs until toggled off or Gateway restarts |
| `/death_screen [hex flags]` | S→C 0x55 with these flags (default: the league's; 2 hides it), [11](11-skills.md) §7 |
| `/quest_flag [<QuestFlags Id> [off]]` | lists the character's quest flags, or sets or clears one (saved); the quest's room objects follow ([12](12-quests.md) §5) |
| `/goto <area id or name> [seed]` | change area as if through an area transition (e.g. `/goto 1_1_3`; ids from the atlas; a seed makes a new instance at it, towns keep 1), [08](08-area-transitions.md) §7 |
| `/tp <x> <y> \| <+dx> <+dy> \| <id> \| <name> [n] \| list [filter]` | dev: 0x1b7 teleports the player within the area, for testing without walking. A cell lands on itself when walkable, else on the nearest walkable cell; a value with + or - is relative to the player on that axis. A name (whole, else a part, any case) or id lands next to a monster, NPC, area transition (`transition to <area>`), the `waypoint`, the `arrival` point or an object of the area's walk-map capture (`RhoaChest`, `GlyphWall`: the last part of its object type); `n` picks the n-th of equal names. `/tp list` shows them. The current action ends and the server's position estimate moves with the player (`TeleportTargets.cs`) |
| `/transition [pos … \| rot <deg> \| z <z \| none> \| model <path \| none> \| index <n>]` | show or change the first area transition (re-created), [08](08-area-transitions.md) §5 |
| `/mobs` | lists the monsters spawned on this connection; emerge monsters that have not risen are marked "(buried)" or "(lurking)" |
| `/despawn [all \| <id>]` | S→C 0x2bb (remove object, header only) for the last spawned monster, all of them (buried ones too), or one id |
| `/mob_walk [0x<id>] [skill hex]` | 0x1ab: the monster starts its Move action (skill 0x2909) toward the player; it walks, animated, along the client's path |
| `/mob_move [0x<id>] [cells/s]` | 0x1ba: slides the monster in a straight line (probe; no animation or pathing) |
| `/mob_tp [0x<id>]` | 0x1b7: teleports the monster next to the player |
| `/mob_mods [id] [mod …\|pool]` | a monster's affixes and rare name; with mods, creates it again with those affixes; `pool`: what it could roll ([14](14-monster-packs.md) §5) |
| `/npc <row, Id or name> [rotation°]`, `/npcs`, `/pos` | creates an NPC next to the player ([17](17-npcs.md)), lists the NPCs, prints the player's cell |
| `/sockets [colours]` | the held item's sockets and links: R, G, B, W, linked within a word and unlinked between words (`/sockets RRGBBB` a 6-link, `/sockets RRG B` a 3-link and a single); without colours it shows the layout. Up to the base's socket capacity; `Game_SOCKET_CAPACITY=1..6` (dev) replaces it, e.g. a 6-link wand; gems stay in their sockets ([13](13-items.md) §7) |
| `/ai [on\|off]` | monster AI for this connection (on by default): monsters chase the player (below); emerge monsters rise either way |
| `/cosmetic portal <name, number or part of a name>`, `/cosmetic list [filter]`, `/cosmetic off` | the shop's portal effects, as game data ([21](21-hideouts.md) §5). Several effects containing what was typed are listed with their numbers rather than the first being picked |

`/set_stat` names are Stats.dat Ids (client key = row + 1).
It sends player stats through 0x1b3 with recomputed attack stats, then skills through 0x2ba ([11](11-skills.md)).
An attribute (`strength`, `dexterity`, `intelligence`) also sets its `virtual_*_for_requirements` value.
The client's gear and gem checks read that value ([13](13-items.md) section 2d).
`off` removes the development replacement, including that requirement alias for an attribute name.
Supported raw critical, resistance and chaos-hit armour inputs rebuild their typed pools on replacement/removal.
In particular, `/set_stat base_armour_applies_to_chaos_damage 0` overrides an allocated source with false;
`off` restores the source's own value.

`/hit` accepts fire, cold, lightning, chaos or physical.
Physical takes armour plus additional physical reduction; elements take resistance plus elemental reduction.
Chaos takes resistance and, when key 12064 is enabled, the existing armour model on the resisted damage.
It receives no additional physical reduction.
`/spellhit` and `/blockhit` share the same mitigation, preserving their suppression/block rules.
All three apply Fortification before ordinary ES handling; chaos still bypasses ES.
See [incoming chaos-hit armour](20-damage.md#2e-armour-against-incoming-chaos-hits).

Every player can use every command; there are no permissions yet.

**No tab completion for our commands.** The chat prompt sends only the finished line (C→S 0x15, §1) and completes
nothing itself — its slash commands are the client's own Commands.dat words, which the server cannot add to (the 71
rows, and the only ones the client sends as C→S 0x16). A command that takes a game-data name therefore has to accept a
piece of it: `/cosmetic` does, and answers an ambiguous piece with the matching effects and their numbers, so the next
line can be a number instead of a name.

## 5. Monsters (`/spawn_mob`)

**Live 2026-10-17:** `/spawn_mob ZombieBite@13` three times put three Hungry Corpses next to the player, standing
idle (first with an "Unidentified" tag, gone once the identified bit was set). `check-components.js` traced all three: 11 components read in the order below,
198 bytes consumed, 0 left. Code: `MonsterObject`, `InstanceEntities.MonsterTypes` (`InstanceEntities.cs`),
`MonsterCatalog.cs` and the `spawn_mob` command in the Gateway. Tests: `InstanceEntitiesTests.Minimal_monster_blob_layout`,
`Monster_types_message_and_type_path`.

Sent, in order: **0x20** (once per variety and level on a connection), **0x2b9**, **0x1a9**.

- **Type.** The object-type register ([04](04-player-entity.md) §1) files every row of the 0x31a-byte table, which is
  **MonsterVarieties.dat** (10 691 rows × 0x31a in 3.29.3b), under the row's `Id` with `BaseMonsterTypeIndex` (row
  +0x48) as its .ot, e.g. `Metadata/Monsters/Zombies/ZombieBite` (Hungry Corpse) on
  `Metadata/Monsters/Zombies/Drowned/BiteZombie`. The variety Ids have no .ot of their own.
- **Level: S→C 0x20.** A monster type needs a level. The Monster component's factory (`0x141e6fd00`) throws
  **"Creating monster with no level"** when its type's `+0x20` is 0. The Monster type (vtable `0x1435a5a28`, parser
  slot 9 `0x141e701d0`) parses the number after the last **`@`** of the type path into `+0x20` (1..100; otherwise
  "Unable to parse monster level of …" / "Unable to create a monster type at level …"). The register files the plain Id
  only; the server registers the levelled path with **0x20** (instance group, dispatcher case 9, class `0x1435dc320`,
  read `0x1421a4f30`):
  ```
  u16 0x0020, u16 n (BE), u16 preload (BE, +0x10), n × u16 MonsterVarieties row (raw, little-endian), n × u8 level
  ```
  Per pair the handler looks the row up (`0x142288c90`, a row index into Data/MonsterVarieties.dat) and registers
  `"<Id>@<level>"` extending the plain Id (`0x141ec2540`; level 0 → the plain Id through `0x141ec28e0`), appending the
  type to instance `+0xc828`; then it loads the first `preload` new types (`0x140685030`). The 0x2b9 type is then
  `ObjectTypeHash("<Id>@<level>")`.
  - Without 0x20 (the first live try) the exception is thrown while the components are built (`0x142427510`, before
    the blob is read), and the cleanup then frees a Targetable component that was never created: an access violation
    in its deleter `0x14161d160`. So the client hangs and crashes instead of showing a message.
- **Components.** `Metadata/Monsters/Monster.ot`: Positioned, Stats, ObjectMagicProperties, BaseEvents, Pathfinding,
  Buffs, Life, Animated, Inventories, Actor, Monster ([../re/object-types.md](../re/object-types.md)). Every one that
  reads is already known from the player or the exit; Monster (`0x1435a6778`), BaseEvents and Pathfinding read nothing.
  - Positioned: 16 bytes. Its 8-byte prefix (`param_1[0x2b] + 0x21`) is written into the object's **Sector** component,
    so it is for types with one; the .ot keys (`0x141e2d860`) set +0x14..+0x20, +0x22, +0x23 but not +0x21.
    `serialise_collidable` (Monster.ot) sets +0x1b.
  - Actor: the two conditional u8 are read when `components[type+0xbc] + 0x1a8 < 1`. +0x1a8 is the Life component's
    first pool `+0x178 + 0x30`, the **current life** just read from the blob, so a living monster has 17 bytes.
  - ObjectMagicProperties (base part `0x142201d70`): u16 flags, u8 `+0x7a`, then the component's own u8 flags.
    Flags bits 0–2 = **rarity** (`+0x7c`: 0 normal, 1 magic, 2 rare, 3 unique); bit 3 = **identified** (`+0x78`;
    without it the client tags the monster "Unidentified"). Only when identified: bit 7 (0x80) → u32 UTF-16 unit count +
    units, a **custom name** (`+0x18`, shown instead of the variety name); else bit 8 → u8 n + n × u32 keys (name parts,
    looked up by `0x14225a4d0`, not mapped); then bit 5 → mod list. Before them: bit 9 → 2 × u8, bits 0x10/0x40/0x400/0x800 →
    u8 n + n mod records (`0x142430380`). Live 2026-10-17: flags 0x08/0x09/0x0a/0x0b showed normal, magic, rare and
    unique monsters; 0x8b with "Headhunt" showed a unique named Headhunt (ObjectMagicProperties 24 bytes, 0 left).
  - Life: (current, 0, …) for life; mana and energy shield 0. The **maximum is the client's own**: its Monster type
    (`0x141e701d0`) sets stat 0x7d (and other default stats: 0x142–0x147, 0x75, 0x78, 0x87, …) from DefaultMonsterStats
    row `level − 1` (0x48-byte rows) and the variety: life = max(1, column × LifeMultiplier / 100), the column being
    AltLife1 (`+0x2c`) if the MonsterTypes row has AltLife1, else AltLife2 (`+0x38`), else MinionLife (`+0x1c`) for
    IsPlayerMinion, else MonsterLife (`+0x14`). The Gateway computes the same (`MonsterCatalog.BaseLife`) and sends it as
    the current life. Live 2026-10-17: Hungry Corpse at levels 13 (77 life) and 70 showed full life bars; the level-70 one
    also showed a cold icon (probably the resistance profile of its MonsterTypes row growing with level; not checked).
  - Blob: 112 bytes with the base list, plus the Animated path (198 bytes for Hungry Corpse).
- **Other component lists.** Of the 10 691 varieties, 4 513 have exactly the base list; most others append
  DiesAfterTime, StateMachine or Transitionable. The blob also writes MinimapIcon (u8), DiesAfterTime (no data: its
  live vtable is the factory's `0x14359f478`, slot 17 `ret`), Preload, Functions, PlayerClass (no data), Transitionable
  and StateMachine (below), NPC (`00 ff`, [17](17-npcs.md)), InteractionAction (u8 flags 0, `0x141e1e240`: Act 2's
  bandit lords Kraityn, Alira and Oak) and Timer (no data: its only vtable `0x1435a10a0` has slot 17 `ret`; Solaris and
  Lunaris on The Harbour Bridge). The rest have ControlZone, BlightTower, Mercenary, LimitedLifespan … components and
  are refused with a message box. The type is the variety's own Id when an
  `.ot` exists under it, else its `BaseMonsterTypeIndex`: `KitavaBoss/KitavaFinal`'s base column names `Kitava1`, but
  the client builds it from `kitavafinal.ot`, which has no Transitionable; sent with Kitava1's list, the extra byte was
  read as the task count and the client stopped on "Buffer Underflow" (live 2026-10-30). 17 varieties have an own type
  that serialises differently (Kitava2/3/Final, BlackDeathMaligaro, Atziri2 …). The Gateway knows each type's list and
  data from `data/object-types.json` (`python -I scripts/re/ot_components.py --all --json data/object-types.json`,
  about 2 minutes); without it, it assumes the base list and refuses StateMachine types.
- **Transitionable** (`ClientInternalTransitionable` `0x1435dd290`, slot 17 `0x1421c24b0`): u8 = state (low 7 bits,
  `+0x120`) | bit 7 (`+0x122`, transition in progress). States run **1..num_states** (.ot `Transitionable { num_states
  = N }`, the type's `+0xb0`); `num_states` = fully transitioned (`create_transitioned`), lower =
  untransitioned (`create_untransitioned`). Without the key `num_states` is **2** (the type constructor `0x141e42d50`
  writes 2; the key itself only takes 2..127, `0x141e42a80`). The post-create slot `0x1421c26f0` always runs the event
  at index `(state − 1)` of a 0x18-byte array, so **state 0 is invalid**: it read a garbage script pointer and crashed
  in the animation-event interpreter (`0x141ded7b9`, live 2026-10-17, Alira and Gravel Eater). The Gateway sends
  `num_states` as `ot_components.py` extracts it (`transitionable.num_states`), which is 1 when the .ot has no key: for
  those types that is the untransitioned state.
  - **Emerge monsters** (Gravel Eater, Dripping Dead, the Twilight Strand's Drowned, Tide Strider …). 2 571 types
    have the basic action `Emerge` (ActionTypes HASH16 16641 = 0x4101, skill id **0x0101**, [11](11-skills.md) §2;
    most inherit it), 424 of them also a Transitionable, and 340 of those leave out `num_states`. What makes one an
    emerge monster is its client script: in **143 types** `create_untransitioned` plays a clip and pauses it
    (`PlayAnimation( emerge ); PauseAnimation();`), so in state 1 the monster waits below ground, in the water or as a
    corpse. The others use the Transitionable for something else (Hillock's sword stage, [12](12-quests.md) §6) and are
    created as before. `ot_components.py` extracts the client scripts (`client_transitionable`) and Targetable's key
    (`client_targetable`) into `data/object-types.json`; `MonsterEmerge.cs` sorts the 143:
    - **Buried** (74 types, 187 varieties: Gravel Eater, Dripping Dead, Strand Drowned, Devourer …): the client keeps
      them untargetable while they wait (`targetable = false`, or DisableTargetable in `create_untransitioned`) and
      `on_transition = { EnableTargetable(); }`. The server keeps them out of the connection's monsters until they
      rise, so no attack, skill, projectile or aura reaches them either.
    - **Lurking** (69 types, 115 varieties: Tide Strider, Blood Elemental, Dreadwheel …): the client shows them frozen
      in the first frame of the clip but targetable. They stay among the connection's monsters and can be hit; the AI
      leaves them alone until they rise, and they rise when hit.
    - **Unique monsters** keep their own state (no rise): a boss's entrance (Kitava's 7 s `phase1_transition_in`, the
      Daughters of Merveil, the Conqueror Wurm) belongs to its fight's script.

    The server raises one:
    1. S→C **0x1ab** on the monster with skill 0x0101, its own cell as start and target, sequence 0xffff, flags 0x0408:
       the client plays the .act's `Emerge` clip (Gravel Eater: `emerge`, 1.0 s; Dripping Dead: `grasp_emerge`, 3.7 s).
    2. S→C **0x2ba** to its Transitionable with state 2: `on_transition` makes it targetable.
    3. When the clip has played, S→C **0x1ac**, as after Hillock's stage; the monster then chases and attacks like any
       other. Live, its first attack still found the Emerge action current and replaced it, so whether 0x1ac is needed
       here is not settled.
    Which monster rises when is the official server's (its `.ais` AI files are not in the client); the Gateway raises
    one when the player comes within 24 cells, or its aggro range if shorter, or when a lurking one is hit. The clip's
    time is game time (a pause stops it). Rising does not depend on `/ai`: a `/spawn_mob` emerge monster rises next to
    the player at once. Types that list `num_states` (SpiderBoss, Tukohama's totem: 3) are created transitioned as
    before.
    Live 2026-10-25 (`scripts/re/frida/trace-actions.js`, `trace-action-start.js`, `trace-skills.js`): a Gravel Eater
    gets the basic actions 0x101 (action 0x4101, Emerge) and 0x239b (Emerge2) besides its granted skill 0x102; the
    0x1ab starts action 0x4101 (usable, result 1), the buried monster (invisible) comes out of the ground within the
    second and spits at the player.
    Dripping Dead (Mud Flats) and the Twilight Strand's Drowned rose the same way from their packs, and a risen monster
    kept by the instance came back standing (created in state 2).
    Tidal Island's 35 Tide Striders wait lurking: only their red life bars show over the water; a Fireball from 27
    cells (beyond the 24) hit one, it rose at once, chased and attacked.
- **Animated path.** Not sent (flags 0): the type supplies the animated object. `Game_MONSTER_AO=1` sends the variety's
  first `AOFiles` path instead (bit 0), which also works for plain monsters but is not needed.
- **StateMachine** (live vtable `ClientInternalStateMachine` `0x1435da8b8`, slot 17 `0x142191f10` → base
  `0x141e3ee40`):
  ```
  u8 shared-state count, u8 shared-timer count       (the client throws unless both match the type)
  per shared state, in definition order:  compact int value (0x141e490e0), u8 flags (0x02 keeps the sent value)
  per shared timer:                       u32, u8
  ```
  Only states declared with `define_shared_state` and timers with `define_shared_timer` in the type's .ot chain
  (top-level StateMachine sections, parents first; a string is a `;` list of `name` or `name = default`; a
  redefinition keeps the first position and takes the later default) are networked; `define_state` / `define_timer`
  stay on the client. `ot_components.py` extracts them (`shared_states`, `shared_timers`); the Gateway sends the
  defaults. Live 2026-10-17: Frost Bishop (1 state, 4 bytes), Arakaali (4 states, 10 bytes) and Haku (8 states, one
  defaulting to 1, and 2 timers: 28 bytes) all consumed exactly. Arakaali showed no hover nameplate: bosses carry
  `boss_life_bar` / `disable_life_bar` / `global_first_aggro` states, probably the top-of-screen boss bar on aggro.
- **Removal.** S→C **0x2bb** (world group, header only: `u16 id, u32 object id, u32 0, u16 0`) removes the object;
  live 2026-10-17 `/despawn` and `/despawn all` removed spawned monsters.
- **Movement** (live 2026-10-17). **S→C 0x1ab "action started"** on the monster, with the Move skill **0x2909**
  (the same id as the player's Move: a basic action every Actor has, Monster.ot `basic_action = "Move"`), sequence
  0xffff (not client-predicted), flags 0x0408, the monster's position and the target: the monster **walks with its
  animation, along the client's own path around walls, on the terrain height**. Alternatives: **0x1ba** (Positioned
  move) only slides it in a straight line through walls, at `duration = cell distance / value` (value ≈ cells per
  second; `0x141e30c90` → `0x141e34120`), keeping the old height; **0x1b7** teleports it.
- **AI** (Gateway, `RunMonsterAi`; live 2026-10-17). Every 50 ms of game time (`MonsterAiCadence`) per connection: a monster within
  its MonsterVarieties `MaxAgroRange` (taken as grid cells: 60 for Hungry Corpse, 105 for most) of the server's estimate
  of the player starts chasing: a Move action (0x1ab, 0x2909) to its own cell around the player, re-sent when that cell
  moves by 3+ cells; beyond twice the range it gives up (a Move to where it stands). The cells come from
  `MonsterSurround`: the nearest chasers choose first, each takes the free cell on the innermost ring (its size + 3
  cells from the player) closest to its bearing, bodies never overlap, and a full ring sends the rest one ring out to
  wait. Candidates must also clear the player's square collision footprint, including when a ring-0 slot is reused.
  Inner candidates respect the monster's actual attack reach; stationary footprints and other players also exclude cells.
  A short native path is retried after arrival at a bounded half-second cadence.
  Occupancy queries use a spatial index; ring trigonometry is cached and candidate searches do not allocate per cell.
  Monsters with negative footprints chase directly without taking a slot; stationary objects and walls still block them.
  A ring-0 slot is kept while it stays free and on the ring. Before, every monster aimed at the cell on the line
  toward itself, so a pack from one side queued in a wedge and only the front ones attacked. The server follows each monster's walk with
  a `WalkEstimator` (A* on the walk map) at MonsterVarieties `MovementSpeed` cells/s (8 for Hungry Corpse, 44 for Haku)
  and reports that position as the action's start; live, the chase showed no snapping, so the estimate keeps up. All
  sends go through one lock per connection (the AI tick and the read loop share the Salsa20 stream).
- **Team** (live 2026-10-17). Positioned's u16 at `+0x1e0` (after x, y and the f32 at `+0x29c`) is the object's team;
  the .ot `team` values are only defaults. The player is sent team 0. Monsters sent with 0 had a **green** (friendly)
  hover outline; with 1 the outline is **red** and on The Coast a life bar shows over the head (towns hide hostile life
  bars). The Gateway sends 1, or 0 with `/spawn_mob … friendly` (both verified live: red and green outlines).
- **Combat** (live 2026-10-17, [11](11-skills.md)). The player has the default attack (skill 0x0102); a click on a
  monster sends C→S 0x24 with it, which is answered with 0x1ab aimed at it (flags | 0x80 + u32 target id after the flags, write
  `0x141fdd990`), then **S→C 0x1b1** lowers its life: `header, u32 current, u32 reserved, u8 pool (0 life, 1 mana,
  2 ES), u16 (+0x2a), u8 (+0x2c)`, big-endian (read `0x141fe0790`); the world handler (case 0x1b1) applies it to the
  pool (`0x141e69fb0`) and calls the Life component's death slot (vtable +0xe8) when life goes from ≥ 1 to < 1, revive
  (+0x100) the other way. Each hit takes the unarmed damage (2–5 for the Witch) at the swing's contact point, after the
  player has walked into range; the timing is in [11](11-skills.md) §5. A dead monster's life bar is hidden
  (`hide_mini_life_bar`) and the monster is removed (0x2bb) 10 s later. Monsters hit back with
  their own skill ([11](11-skills.md) §6).
- **Placement.** The nearest walkable cell 6–20 cells from the server's estimate of the player; ids from 0x3000. The
  world is active, so 0x2b9 is created at once; 0x1a9 puts it in the wake zone.

## 6. Open

- The chat line's flags, and the language names of 0x134's language byte (the official text ends with one).
- Monsters: **skills** (their GrantedEffects, so they fight back), the boss life bar (StateMachine `boss_life_bar`), the 346
  varieties with other components (NPC, ControlZone, …), AI and combat.
