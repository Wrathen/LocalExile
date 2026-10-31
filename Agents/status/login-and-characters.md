# Status: Login, characters and saves

Part of [Status](../Status.md); what to build next in this area is in
[Roadmap](../Roadmap.md).

## Works (verified with the real client)

- Remember Password now returns and verifies the official client's 32-byte saved-login credential in account login
  0x03 / result 0x04.
  It survives server restarts, is device/account-bound and is invalidated by changing the password.
  Covered by protocol/session tests and confirmed working by the user in the real client.
  See [01](../docs/protocol/01-login.md).
- Login: custom TCP, Diffie-Hellman + DSA-signed server key + Salsa20, account login, character list, Play.
  Code: `GameServer.Auth`, `GameServer.Core/Protocol/Login`.
  Docs: [01](../docs/protocol/01-login.md), [re/008](../docs/re/008-login-handshake.md),
  [re/009](../docs/re/009-login-messages.md).
- Saving characters: items, equipment, sockets and the skill bar per character in `data/saves.db` (a new bar: the
  default attack on the left mouse button, gem skills into empty slots only, right mouse button first);
  the bar is restored with S→C 0x53 after login and area changes (binds are C→S 0x4e).
  Code: `CharacterSaves.cs`, `PlayerInventories.cs`, Gateway.
  Docs: [11](../docs/protocol/11-skills.md) §11, [13](../docs/protocol/13-items.md) §7.
- Playable logins start at the character's last town's spawn point (after logging out, Exit to Character Selection,
  a crash, a restart or a disconnect while dead);
  Auth records the town when a town ticket resolves (`Character.LastTown` in `data/saves.db`).
  Code: Auth (`InstanceDirectory`), `WorldAreaTowns.cs`.
  Docs: [09](../docs/protocol/09-area-change.md) §4.
- Leagues and character creation: Standard and the league modules' (Allflame, Ruthless) with their variants on the create
  screen (S→C 0x1b), 24 character slots, any class in any league (C→S 0x12 → 0x15, 0x13), straight into the game;
  the league under the area name in game (0x1d); the hardcore death screen from the character's league;
  `Game_LEAGUES`.
  Code: `LeagueCatalog.cs`, `LoginServerSession.cs`, Auth, Gateway.
  Docs: [01](../docs/protocol/01-login.md) §7.
- Character deletion on the character screen (C→S 0x0c → new list 0x15 + result 0x0d);
  the save is set aside in `data/saves.db` (`deleted_characters`).
  Code: `LoginServerSession`, Auth.
  Docs: [01](../docs/protocol/01-login.md) §5.
- Every class in game: the Characters.dat row's object type and model, class byte, attributes, unarmed attack and
  start kit (weapon, gems, sockets in the gems' colours); verified with a Templar.
  Code: Gateway (`SkillCatalog.cs`, `PlayerInventories.cs`).
  Docs: [01](../docs/protocol/01-login.md) §7, [04](../docs/protocol/04-player-entity.md).
- Exit to character selection: Esc → "Exit to Character Selection" and the hardcore death screen's Exit (C→S 0x153);
  the Gateway closes the instance connection and the client returns to the character list on the same login
  connection; playable characters can enter again.
  Code: Gateway.
  Docs: [09](../docs/protocol/09-area-change.md) §6.

## Partly working

- Selected league modes have server rules with automated coverage and native entry, exit and migration checks.
  Ordinary Hardcore death migrates to the matching softcore base while preserving SSF; Hardcore Ruthless stays dead.
  Carried progress and the death record commit together, source stash/gold stay behind, and stale writes cannot undo it.
  Automated coverage includes lethal ignite, stale death callbacks and one-time death effects
  in ordinary and Ruthless modes.
  Auth, Gateway and dev-server entry reject dead characters and stale tickets; transient failed deaths retry before entry.
  The native character list returned a dead Hardcore Ruthless character in Void with its terminal marker;
  an ordinary Hardcore SSF death returned in SSF Standard and could enter again.
  Full retail mode parity still depends on the server's incomplete gameplay systems.
  SSF isolation and Ruthless rules are described in [league modes](../docs/game/league-modes.md).
- Local startup explicitly resets the cached login gateway to loopback before launching the client, preventing a stale
  remote endpoint from causing DSA handshake failure (General Rhoa Failure #3).
  See protocol/01-login.md.
- **Saves in SQLite (tested, not yet checked with the real client).** Accounts, characters, character saves and
  stashes are rows in `data/saves.db` (WAL, synchronous=FULL); a stash is a header row and one row per tab.
  Every item, stash, gold, passive, quest flag and waypoint change is saved at once in a transaction.
  Stash moves, purchases, sales, currency used on items, gold respec, the map device and character deletion save all
  their parts in one transaction or none; a failed save rolls back, reloads what it changed and closes the connection.
  Experience and kill flask refills are saved every 5 s, at a level-up and at disconnect.
  Offered sale items keep their inventory cells, as 3.29's greyed imprints.
  The level comes from the saved experience.
  Unknown save fields are kept; a save holding items the game data doesn't know stops the login.
  JSON saves and accounts.json are imported once (`Saves.cs`, `ItemMoves.cs`, `GameServer.Storage/SaveDatabase.cs`,
  `SqliteAccountStore.cs`; [saves](../docs/saves.md)).

## Not working yet

Nothing yet.
