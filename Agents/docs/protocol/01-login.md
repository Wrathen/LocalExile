# 01 — Login connection (tcp/20493)

Implemented in `src/GameServer.Core/Protocol/Login/` and served by `GameServer.Auth serve`. Live-verified with the real
3.29.3b client on 2026-10-16. Detailed static notes: [Agents/docs/re/008](../re/008-login-handshake.md) (handshake) and
[Agents/docs/re/009](../re/009-login-messages.md) (full message tables). This document summarizes them and adds what the live
runs showed.

## 1. Pointing the client at us

`live.ps1` explicitly selects `127.0.0.1:20493` in the cached GGPK gateway list before a full client launch, after stopping the previous client and before starting Patch. A matching patch root hash can skip downloading gateway_list.txt, so serving a local list over HTTP alone does not reset a previously selected remote endpoint. Server-only restarts leave the running client and its GGPK untouched.

- `gateway_list.txt` (served by our patch web root, and also patched into the GGPK by `GameServer.Patch gateway --login
  127.0.0.1:20493`) gives the login host:port. The client reads `gateway_list.txt`, then `Bundles2/gateway_list.txt` if
  it exists (`0x140cfc2d0`), and the last one wins.
- The real client checks a DSA signature on the server's DH key and agrees with a static DH value, both embedded in
  the exe. `GameServer.Auth keygen` makes our own keys over **GGG's DSA domain** (same p, q, g, only y replaced, with x
  chosen so the DER stays exactly 0x346 bytes) and our own static DH key. `GameServer.Auth patch` writes
  `Game.patched.exe`, which differs from `Game.exe` only at file offsets 0x2fbf796–0x2fbfb9f.

| Blob (.rdata) | VA | File offset | Size | Replaced with |
|---|---|---|---|---|
| X.509 DSA public key (p 2048, q 224) | `0x142fc0150` | 0x2fbf550 | 0x346 | ours, same domain, new y |
| client static private exponent | `0x142fc04a0` | 0x2fbf8a0 | 384 | kept: gives us the client static public |
| server static public | `0x142fc0620` | 0x2fbfa20 | 384 | `3^s mod p` for our s |

## 2. Handshake (plaintext, then Salsa20)

| Step | Detail |
|---|---|
| C→S `0x0002` | `u16 2, u16 128, client ephemeral DH public (128 B BE), u16 0` |
| S→C `0x0002` | `u16 2, u16 128, server ephemeral public, u16 56, DSA-SHA1 signature r‖s (28 + 28 B)` |
| check | DSA/SHA-1 over the 128-byte server key with the embedded DSA key ("General Rhoa Failure #3" if bad) |
| agree | Crypto++ `DH2`: `Z = Z_static (384 B, 3072-bit group, g = 3) ‖ Z_ephemeral (128 B, 1024-bit group, g = 2)` |
| KDF | `km = SHA-512(Z)` (64 B) |
| ciphers | Salsa20/20, key `km[0:32]`, C→S IV `km[32:40]`, S→C IV `km[48:56]`, one continuous keystream per direction |

Client code: crypto object ctor `0x140e02170` (groups parsed from hex strings, "Rhoa"), login connect `0x14213e610`,
dispatch `0x14213f190` case 2 → verify `0x140e03630` → agree `0x140e03460` → ciphers `0x140e03a30(km, isClient=1)`
installed at socket `+0x1b8`. Socket send/flush `0x140dfb8c0` uses cipher slot 1, receive `0x140dfb3d0` slot 2.

Our side: `LoginGroups.cs`, `LoginKeys.cs` (keygen, image patching), `LoginHandshake.cs` (`ServerHandshake.Respond`),
`LoginCrypto.cs` (`ConnectionCiphers`, `LoginKdf`), `Salsa20.cs`.

**The same cipher construction is reused by the instance connection**, with the 64 key bytes from 0x14 in place of
`km` (see [02](02-instance-connection.md#4-switching-to-encryption-s→c-0x11)).

## 3. Framing and field encodings

After the handshake each direction is a stream of messages: `u16 id` (BE) then fields, **no length prefix**. The
reader `0x140dfb630` sets the socket flag `+0x252` when it runs short, and the message is retried once more bytes arrive.
`LoginCodec.TryDecode` behaves the same way: it returns false when it needs more data and throws on an unknown id.

<a id="field-encodings"></a>

| Name | Wire | Client |
|---|---|---|
| u8 / bool | 1 byte | `0x140dfb630` read / `0x140dfb760` write |
| u16 / u32 | big-endian | |
| raw32 / raw64 | memcpy, **little-endian** | |
| bytes[n] | n bytes | |
| blob | u16 length + bytes | `0x1408402a0` / `0x141fa4610` read, `0x141fa44f0` write |
| str | blob of UTF-8 | |
| wstr | u16 **code-point** count, then per code point: `< 0x80` as 1 byte, `0x80` + UTF-16 unit LE, `0xC0` + code point u32 LE | write `0x140e0c9b0`, read `0x140e0cbe0` |
| vec8 / vec16 / vec32 | count (u8 / u16 / u32 BE) then elements | |

Message classes: vtable `[0]` destructor, `[1]` write (id + fields), `[2]` read (fields only), `[3]` clone. Objects
keep `u16 id` at `+8`, fields from `+0x10`. These rules hold for the instance connection too.

C#: `LoginWire.cs` (`LoginWriter`, `LoginReader`), `LoginMessages.cs` (records + `LoginCodec`),
`LoginMessageStream.cs` (chunked decoding; desyncs permanently on an unknown id).

## 4. Message set

27 S→C ids from the receive factory `0x14219c350` and 26 C→S ids from the senders that write to the login socket
(`login+0xdd0`). Full field tables are in [Agents/docs/re/009](../re/009-login-messages.md). These matter for the live flow:

| Dir | Id | Fields | Meaning |
|---|---|---|---|
| C→S | 0x01 | — | keepalive, sent repeatedly while the login socket is open (class `0x14316c930`) |
| C→S | 0x03 | u32 version (2), str account, bytes[32] machine id A, bytes[32] **password digest**, bytes[32] machine id B, bool, bool, u8 realm\|0x40, u8 | account login. Digest = unsalted SHA-256 of the UTF-16LE password (`0x140d05f70` → `0x140e23290`) |
| S→C | 0x04 | u16 result, wstr name, bytes[32], u8 flags0, u8 flags1, blob | result 0 → logged-in state 3. A non-empty name pops up a message box. Flags0 bit5 → `+0xea8` "email verified" (without it the character screen shows the "account not verified" panel) |
| S→C | 0x1b | raw64 server time, vec32⟨LeagueEntry⟩, u8 flags | push after login: **the league list** (§7). The client then moves to the character screen |
| C→S | 0x12 | wstr name, wstr league, u32, u32, wstr start state, bytes[32] | **create character** (§7) |
| S→C | 0x13 | u16 result, bool | create-character result (§7) |
| C→S | 0x0c | wstr name, u8 (0 live) | **delete character**, sent after the delete screen's confirmation (`0x140cfafa0`) |
| S→C | 0x0d | u16 result | delete result: 0 closes the delete screen back to the list (`0x140cfb5f0`, listener +0xa90); non-zero shows the BackendErrors text |
| C→S | 0x19 | — | "leagues with progress" (migration), answered with S→C 0x1a |
| C→S | 0x18 | — | character screen opened (`0x140d13060`) |
| S→C | 0x15 | vec8⟨Entry15⟩, u8, u8, blob | character list shown on the character screen (`0x140d12590`); the first u8 (→ screen +0x280) is sent as the index of the last played character (Play updates `LastPlayedUtc`); verified live 2026-10-22: the screen selects it |
| C→S | 0x0e | u8 realm, u32 | **Play**. The client shows "Contacting Server..." until 0x14 |
| S→C | 0x14 | str host, u16 port, u32, u16 area hash, u32 ticket, bytes[64] key, bytes[8], u8 | instance assignment (below) |
| S→C | 0x17 | u16 reason | disconnect |

**Entry15** (0x50 bytes, wire order): wstr name `+0x00`, wstr text `+0x20` (the character's league), u8
`+0x49` (red marker), u8 `+0x4d`, u8 `+0x48` **level**, u32 `+0x40`, u8 `+0x4a` **class** (3 = Witch in this enum, Scion = 0
in ours), u16 `+0x44` and u16 `+0x46` (portrait), u8 `+0x4b`, u8 `+0x4c`. Mapped live by setting one probe field to 3.

**0x14 in detail** (handler: dispatch `0x14213f190` case 0x14):

| Field | Message offset | Copied to the instance-info struct | Used by |
|---|---|---|---|
| str host | +0x10 | | connect target |
| u16 | +0x30 | +0x80 | **port** (live: the client dialled it) |
| u32 | +0x34 | +0x30 | echoed in instance 0x0E. We send the IPv4 address |
| u16 area hash | +0x3c | | `WorldAreas.HASH16`. Unknown → "Area reported by instance does not exist" |
| u32 | +0x38 | +0x34 | **ticket**, echoed in instance 0x0F |
| bytes[64] | +0x3e | +0x38 | **instance Salsa20 key material** (0x11) |
| bytes[8] | +0x7e | +0x78 | goes into the 0x0E digest. We send zeros |
| u8 | +0x86 | | |

The struct also gets the account name (`login+0xdf0`) and is handed to the listeners at `+0x290`, which start the
instance connection. Lioneye's Watch (`1_1_town`) is HASH16 `0x235c` in 3.29.3b.

## 5. Our login server

`GameServer.Auth serve` (`src/GameServer.Auth/Program.cs`): accept → read C→S 0x0002 → `ServerHandshake.Respond` →
decrypt with `LoginMessageStream` → `LoginServerSession.Handle` → encrypt replies. It logs every decoded message, with
password digests, tokens, tickets and machine ids redacted.

`LoginServerSession` (`Protocol/Login/LoginServerSession.cs`):

| Receives | Sends |
|---|---|
| 0x03 | `AccountService.LoginWithClientDigest`. Unknown account → 0x04 result 5, wrong password → 6, then the server **closes** the connection (the client ignores Log In while a failed connection stays open). Good → 0x04 result 0, Flags0 0x20, then 0x1b with the leagues (`LeagueCatalog`) |
| 0x18 | 0x15 with the account's characters (level, class, league) and 24 character slots |
| 0x12 | creates the character (§7): 0x15, then 0x13 result 0; or 0x13 with an error |
| 0x19 | 0x1a result 0, no leagues |
| 0x0e | 0x14: `127.0.0.1:6112`, address, area `0x235c`, `ticket = Keyring.NewTicket()`, `key = Keyring.Key(ticket)` |
| anything else | nothing |

Accounts come from the save database `data/saves.db` (shared with `GameServer.Host` and the Gateway, [saves](../saves.md)). Each account stores a verifier
for the client digest. `InstanceKeyring` (`Protocol/Instance/InstanceProtocol.cs`) loads or creates
`data/instance-secret.bin` (`Game_INSTANCE_SECRET`). Tests: `LoginCryptoTests` (patches an in-memory copy of the real
client and handshakes against it), `LoginMessageTests`, `LoginServerSessionTests` (including the live-verified flow).

## 6. Live observations (2026-10-16)

- 0x04 with a non-empty name popped up a message box with the account name and an OK button, so the name stays empty.
- Without the 0x1b push the client waits after 0x04.
- Result codes are BackendErrors rows + 1: result 5 showed "The specified account does not exist." (row 4), so a wrong
  password is 6 (row 5 InvalidPassword). After a failed result the client keeps the connection and ignores Log In until
  the server closes it.
- With "Save Login Details" ticked the client auto-submits the saved account and digest at startup.
- The character screen requests the list with 0x18 each time it opens.
- After Play the login socket stays open and sends 0x01 keepalives. When the instance connection drops, the client
  goes back to the login screen ("An unexpected disconnection occurred").

## 7. Leagues and character creation (2026-10-18)

Static analysis of the character and create-character screens, then live-verified with the real client: characters of
every class are created in any league, enter the game right away, and the league shows in game.

**Character slots.** 0x15's second trailing byte (`B29`, login+0xe81 → character screen +0x281) is the account's
character slot count. The Create Character button (0x140d17cc0) is enabled only while the account has fewer characters;
with 0 it stayed greyed out. We send `AccountService.MaxCharacters` (24). `B28` (+0x280) is unknown.

**The league list is S→C 0x1b** (login+0xe88, copied by the character screen 0x140d12590). The create screen's Create
button (0x140cf97a0) needs a non-empty list, a name and a class. A league entry (0xe8 bytes, reader 0x1424ed2c0):

| Wire | Struct | Meaning |
|---|---|---|
| wstr | +0x10 (view +0x00) | name: shown on the card, sent back in 0x12, compared with 0x15's league text |
| wstr | +0x40 (view +0x30) | description: the card text, a key when it starts with `#`. **Empty crashes the client** ("invalid string position", 0x140cc8820) |
| wstr | +0x60 | SSF parent: the league this one is the Solo Self-Found form of |
| wstr | +0x80 | Hardcore parent |
| wstr | +0xa0 | Ruthless parent |
| raw64 ×3 | +0xd0, +0xd8, +0xc8 | times; +0xc8 is compared with now when flags0 bit 4 is set |
| u8 flags0 | +0xe0 | bit0 private (password box), bit1 hardcore (confirmation, banner), bit4 timed, bit7 main league |
| u8 flags1 | +0xe1 | bit1 variant, bit5 platform-hidden, bit6 ruthless (confirmation) |
| u8 | +0xe2 | bit0 counted into login+0xe58 |
| u8 | +0xc0 | CharacterStartStateSet row |
| u8 | +0xc1 | 1 scrolls to the card; compared with ActiveSettings "Current" (59) for the "try the new league" button |
| raw32 | +0xc4 | default 0xd1 |

The create screen (0x140ccc4a0) draws a card for each main league (flags0 bit7, flags1 bit1 clear). Its Hardcore,
Solo Self-Found and Ruthless check boxes switch to the variants that name the card in a parent field; combinations
chain (SSF Hardcore's SSF parent is Hardcore; SSF Hardcore Ruthless's Ruthless parent is SSF Hardcore).
`LeagueCatalog` reads `Leagues/<Name>/league.json` ([modules](../dev/modules.md)), a league with a card or a check box on another's.
Any league name works without a client change.

Play resolves the selected character's full league and passes authoritative Hardcore, SSF and Ruthless flags to
the Gateway together with its stable character ID.
Permanently dead Hardcore Ruthless characters remain listed in Void with `B49 = 1`; Auth also rejects a forced
Play request, and stale tickets cannot reactivate a character in its former Hardcore league.
See [league modes](../game/league-modes.md) for death persistence, migration and mode isolation.

**C→S 0x12, create character** (sender 0x142140490, called by 0x140cf5ef0): wstr name, wstr league, u32, u32 (both 0
live), wstr **start state**, the CharacterStartStates.dat Id of the chosen class (`DexDefault` for a Ranger), then the
SHA-256 of a private league's password (zeros). The class follows from the start state's attribute prefix.

**S→C 0x13** (u16 result, bool tracking): result codes are BackendErrors rows + 1 (21 name taken, 22 invalid, 23 too
short, 24 too long, 25 invalid class, 26 no such league, 49 no slots). On 0 the create screen (0x140cf8010) looks the
new character up by name in the character list and sends **Play (0x0e) with its index**, so the server sends the new
0x15 **before** 0x13. Sent after it, the client stayed on "Contacting Server...".

**Delete (C→S 0x0c)**: the server removes the character (name matched case-insensitively) and answers with the new
0x15 list, then 0x0d result 0; an unknown name gets 0x0d 16 (BackendErrors row 15 + 1, CharacterDoesNotExist). Auth
moves the character's save to the `deleted_characters` table of `data/saves.db` ([saves](../saves.md)), so a new character
with the same name starts with its class's kit, and the Gateway drops items it still holds in memory for a name that
has no save any more.

**S→C 0x1a** (u16 result, vec32⟨LeagueEntry⟩) answers C→S 0x19, the "leagues with progress" (migration) list
(listener 0x140d17170).

**In game.** Auth gives the Gateway the character's league, hardcore flag, class and level (`ControlResponse`); `/set_level`
changes the level through the control op `set_level` ([10](10-chat-and-commands.md)). The league is
the wide string of instance 0x1d (instance +0xc3b8), which the map overlay (0x1406b2030) shows under the area name as
"{0} League" (ClientStrings 0xb6). Hardcore leagues get the Exit-only death screen. The class selects the player's
Characters.dat row: object type (the row's Id), model (AOFile), class byte (IntegerId), attributes, unarmed attack and
start kit.

## Open

- Meanings of most 0x15 / 0x1b fields, and the result-code mapping onto BackendErrors (row 4 AccountDoesNotExist,
  row 5 InvalidPassword: whether it is direct or code−1 is not verified).
- The other login paths (0x05 token, 0x06/0x07 ticket/Steam, 0x2c Epic) are decoded but not answered.

## Remember Password (2026-10-25)

This uses account login **0x03**, not Steam's binary-token message 0x05.
`Flag94` requests remembering login; `Flag95` means the 32-byte PasswordDigest field contains
the saved credential from S→C 0x04 Digest. Native 0x140d05f70 sets both flags for a saved
login and only Flag94 for typed-password login with Remember Password checked.
On success 0x140d07950 passes the response digest to 0x140d04250, which saves a nonzero
32-byte value through 0x140cfc0f0. Returning zeros never establishes a usable saved login.

The server returns an HMAC-SHA256 credential when Flag94 is set, bound to the account id,
both machine identifiers and a versioned domain, keyed with the account's stored password
verifier. Flag95 selects constant-time verification of that credential instead of password
digest verification. No plaintext password or additional database table is needed; the existing
persisted account verifier supports restart and password changes invalidate old credentials.
Credentials are redacted from outgoing Auth logs as well as incoming login requests.
Tests cover remembered login after a new AccountService, account/device mismatches, invalid
credentials, unchecked Remember Password and password changes. First use after this fix needs
one typed-password login with Remember Password checked to replace the old zero credential.
