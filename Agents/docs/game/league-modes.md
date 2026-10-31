# Hardcore, Solo Self-Found and Ruthless

The creation screen's mode choices select server rules as well as league names.
The full selected league is saved with the character and resolved by Auth on every Play request.
Auth passes explicit Hardcore and SSF flags and the league form to the Gateway; gameplay does not infer modes from name fragments.
Ruthless and Allflame are leagues in `Leagues/` ([modules](../dev/modules.md)); this page is what those modules and the framework do together.
The same rules apply to the existing Standard/Allflame families and any base leagues configured by the server.
All eight combinations remain separate personal-stash and gold namespaces.

## Hardcore death

| Character before death | Result |
|---|---|
| Hardcore | Standard |
| SSF Hardcore | SSF Standard |
| Hardcore Allflame | Allflame |
| HC SSF Allflame | SSF Allflame |
| Any Hardcore Ruthless combination | The same variant without Hardcore (`parent` in its `league.json`) |

Ordinary challenge Hardcore deaths keep their challenge base and SSF choice.
This follows the changed challenge-parent behavior documented in the
[official 3.27 patch notes](https://www.pathofexile.com/forum/view-thread/3869068)
and shown in the supplied 3.29 Allflame selector.
The Hardcore Ruthless exception follows GGG's
[Ruthless announcement](https://www.pathofexile.com/forum/view-thread/3320651): the character stays permanently dead.

Carried equipment, inventory, gems and character progress stay with an ordinary migrated character.
The source account stash and gold stay in the source league; the destination has its own stash and gold.
A permanently dead Ruthless character and its save remain stored, rather than silently deleting its items.
The native character list places it in Void and carries the terminal-character marker (`B49 = 1`).
Auth also refuses a forced Play request with login result 0x0f/code26 before any Gateway entry.
No client error-popup wording is assumed from that result code.

The Gateway commits a `HardcoreDeathRecord` and the carried progress in one SQLite transaction inside lethal damage,
before the first death-related socket write.
Lethal incoming ignite follows the same commit path as direct hits, including the permanent Hardcore Ruthless outcome.
Death notifications validate their connection and damage epoch before cleanup, so stale callbacks cannot commit death.
Normal progress saves do not own that record and cannot undo it.
Stale level, portrait and last-town updates preserve the already committed death and destination league.
Repeated callbacks retain the original death and do not flush an old connection over newer softcore progress.
Auth checks the canonical character ID and league before resolving or transferring tickets.
The Gateway checks again before creating world state; deleting and reusing a name does not reactivate its old ticket.
The dev JSON server also refreshes the canonical character and rejects permanently dead or stale league entries.

The old connection offers only Exit and refuses resurrection packets and post-death gameplay requests.
Gold, item pickup, waypoint activation and quest progress check the terminal owner at their mutation boundary.
An already queued background notification cannot admit another world or progress mutation after death commits.
A late level-up cannot restore its life.
If a storage write temporarily fails, its transaction rolls back and a pending death is retried by the save timer.
Reconnect retries that pending death before allowing entry, so a client disconnect cannot clear it.
The retry queue is process memory: an uncommitted death cannot be guaranteed through simultaneous storage failure
and termination of the Gateway process.
Committed deaths survive restart through the normal synchronous SQLite journal.

Code: `HardcoreDeathRules`, `HardcoreDeathPersistence`, `Program.Hardcore`, `InstanceDirectory`,
`LoginServerSession`, `SqliteAccountStore` and `JsonAccountStore`.

## Solo Self-Found and league boundaries

Town instances are partitioned by the full league, including Hardcore, SSF and Ruthless variants.
Non-town SSF instances additionally belong to the character's stable ID.
Another character cannot join them through an instance list or a forged instance-number request.
The Gateway also separates SSF ground items and other mutable instance state by stable character ID, including towns.
Town players can remain visible without giving each other dropped equipment.
Peer presence is shared within the same Auth instance and creation generation, independently of private world state.
Old instances cannot pass their items to a newly created character that reuses a deleted name.

Personal stash and gold remain shared between one account's characters in the same full SSF league.
That is the account-sharing rule in GGG's
[SSF manifesto](https://www.pathofexile.com/forum/view-thread/1823776).
Guild stash opening, tab requests and guild inventory moves are refused in SSF.
Guild stash storage is league-scoped for the other modes.
Standard retains the old unscoped guild-stash key for compatibility with existing saves.
Other leagues use separate keys, so their new guild storage cannot read that legacy bank.

Player trading is available to living players in the same instance and full league outside SSF.
Both request and acceptance enforce the authoritative modes; SSF town visibility grants no trade access.
Offers and completion recheck eligibility, and completion holds both owners' combat locks through the exchange.
Both inventories save in one transaction, so a failed second save cannot leave only half the exchange committed.
Party formation remains unimplemented; future party and shared-loot features must enforce the same SSF boundary.
The character-selection opt-out and Migrate All workflow, league-end merges and their progress transfers remain open.

Code: `InstanceRegistry`, `InstanceManager`, `InstanceStore`, `AccountStash`, `Program.Stash` and `GameSession`.

## Ruthless rules and native data

The server chooses separate item, mod, skill and passive catalogs for each connection whose league asks for the HardMode data.
Ordinary characters continue to use their ordinary catalogs in the same running server.
The issue #75 integration retains saved defence-base rolls, typed local weapon damage and critical modifiers,
active-hand qualification, fractional body-to-Ward conversion and the branch's raw character/selected-stat scopes.
Those calculations read the connection's selected item/skill catalog, including charge and critical source reads.
The alternative minimum-base equipment calculation or ordinary-only catalog lookup would lose one side's rules.
Current gem requirement snapshots share the equipment resolver without publishing its inactive-item result;
the normal character-stat entry point remains responsible for that cache publication.
See the [integration report](../dev/issue75-review.md#main-merge-league-modes-gambler-and-vaal-souls).
The copied 3.29.3b tables supply these differences:

- Passive alternate names, stat keys, values and granted points through `HasHardmodeAlternate`.
- Alternate mod stat values through `HasHardmodeStatValues`, including intentional zero values.
- Gem primary and companion effects through `GrantedEffectHardmode` and `GrantedEffect2Hardmode`.
- Flask duration and buff values through `RecoveryTime2` and `BuffStatValues2`.
- The alternate Onslaught template, which omits its ordinary movement-speed bonus.
- Ruthless NPC shop rows, purchase costs, quest reward choices and Siosa's eligible gem index.

Native alternate definitions do not establish gem availability.
The copied client contains an 8-second Frostblink hardmode definition, but its Ruthless gem index excludes Frostblink.
The server therefore blocks its acquisition and runtime use rather than treating the alternate row as permission.
The same availability rule excludes Dash, Flame Dash, Lightning Warp, Leap Slam, Shield Charge, Whirling Blades,
Blink Arrow, Smoke Mine, Bodyswap, Charged Dash, Phase Run and Withering Step.
Venom Gyre and Item Rarity Support are also excluded.
Those restrictions apply to drops, vendor stock and runtime use, including items retained in older saves.
Quickstep remains an allowed exception to this availability restriction.
The supported utility-flask data still uses a 2-second Quicksilver duration, compared with the ordinary 6 seconds.
The native Grace and Determination alternatives omit their ordinary flat defence amounts.
Unsupported skill execution mechanics remain unsupported in both modes.

Ruthless gameplay also applies reduced item, gold, rarity, gem-XP and vendor-sale rewards, plus compounding player-XP
reduction at high levels and nearby campaign-boss recovery after death.
Those unpublished numeric rates are LocalExile tuning described below.
The crafting bench rejects opening and crafting in Ruthless.
The opening support-gem chest and its support drop are disabled.
Quest choices use the pinned `QuestRewards.__col16` discriminator: 0 shared, 1 ordinary-only, 2 Ruthless-only.
Support gems are excluded from deterministic quest rewards; the table's gear and Uncarved Gemstone alternatives apply.
Reward grids use the actual item dimensions and appropriate item levels and rarity.

Ordinary gem vendors sell no gems in Ruthless.
After its quest unlock, Siosa exchanges one Uncarved Gemstone for an eligible active gem at the character's level.
Support gems remain drop-only.
Ruthless shops exclude jewellery and the development shortcut that sells all maps at Nessa.
Purchase prices use native hardmode costs; unknown or unsupported prices are omitted, never treated as free.
Sales use basic reduced returns, with ordinary upgrade, RGB, quality and rare-set recipes disabled.
An unsupported sale conversion is refused while retaining the offered items.

Entering town, a hideout or another area does not automatically fill Ruthless flasks or living characters' life/mana.
Saved pools restore across area changes and reconnects; new characters and softcore resurrection receive valid pools.
Only the current admitted connection may save those pools.
Successful transfers retire the source writer, so delayed recovery ticks or disconnect cleanup cannot overwrite
the destination's newer resources.
Talk to a supported flask vendor, Helena in the hideout or Niko to refill.
Active utility flasks do not gain charges until their effect ends.

The server's existing incomplete endgame systems still limit full retail parity.
Item-granted skill mechanics, full Atlas and league encounter distributions, ascendancy progression and all specialized
crafting/reward systems require their own implementation as those features become playable.
Native table values and implemented campaign rules are used where available; unknown drop probabilities are tunable.

## Change Ruthless balance without rebuilding

The local file `config/ruthless.json` controls eight numeric settings and stays git-ignored.
The tracked `config/ruthless.example.json` supplies the repository's default values.
These are server settings, separate from the private client's graphics and input settings.
GGG does not publish the exact server drop and XP formulas, so these defaults are LocalExile approximations.
They are not claimed to reproduce retail probabilities.

Run these commands in PowerShell from the repository root (the folder containing `AGENTS.md`):

```powershell
if (!(Test-Path .\config\ruthless.json)) { Copy-Item .\config\ruthless.example.json .\config\ruthless.json }
notepad .\config\ruthless.json
```

The file contains:

```json
{
  "itemDropMultiplier": 0.20,
  "goldMultiplier": 0.25,
  "equipmentRarityMultiplier": 0.25,
  "gemExperienceMultiplier": 0.25,
  "vendorRewardMultiplier": 0.20,
  "experiencePenaltyStartLevel": 68,
  "experienceMultiplierPerLevel": 0.90,
  "campaignBossLifeRecoveryFraction": 0.25
}
```

| Setting | Meaning |
|---|---|
| `itemDropMultiplier` | Ruthless item-drop quantity relative to the server's ordinary drop system |
| `goldMultiplier` | Fraction of ordinary gold rewards |
| `equipmentRarityMultiplier` | Multiplier used for equipment rarity chances |
| `gemExperienceMultiplier` | Fraction of ordinary gem XP from kills |
| `vendorRewardMultiplier` | Fraction of basic vendor-sale rewards; ordinary multi-item recipes stay disabled |
| `experiencePenaltyStartLevel` | First character level affected by the extra player-XP penalty |
| `experienceMultiplierPerLevel` | Factor compounded once at the start level and once for each subsequent level |
| `campaignBossLifeRecoveryFraction` | Share of maximum life restored to a nearby campaign boss on player death |

For a multiplier, `0.25` means 25%, `0.50` means 50%, and `1.0` means the ordinary amount.
For example, changing `gemExperienceMultiplier` from `0.25` to `0.50` doubles this Ruthless gem-XP allowance.
Player XP uses `factor^(level - startLevel + 1)` at or above the start level.
With the defaults, level68 gets 90% of otherwise earned XP, level69 gets 81%, and level70 gets 72.9%.
Whole item, gold and player-XP rewards round down; very small rewards can become zero.
Fractional Ruthless gem XP accumulates on the connection until enough is earned for a whole point.

Keep the property names, commas, quotation marks and braces; JSON does not accept comments or a trailing comma.
All seven fraction/factor settings must be finite numbers between 0 and 1 inclusive.
The start level must be a whole number between 1 and 100.
Press Ctrl+S in Notepad after editing.
No build or server restart is needed: a relevant Ruthless action checks for changes, at most once each second.
Already-created loot and completed rewards are not recalculated.

To check whether the file loaded, run this in the repository's PowerShell window after a relevant Ruthless action:

```powershell
Get-Content .\logs\live-gateway.log -Tail 100 | Select-String '\[ruthless\]'
```

Success includes `[ruthless] loaded settings from` and the file's absolute path.
An invalid, incomplete or unavailable file logs `[ruthless] keeping previous settings:` followed by the reason.
The last valid snapshot stays active; fixing and saving the file retries the load.
At startup with no valid file yet, the built-in defaults above remain active.
`Game_RUTHLESS_RULES` may choose a different file when starting the Gateway; changing that path requires a restart.

## Validation

Automated tests cover mode resolution, normal/SSF migration, Hardcore Ruthless permanent death, restart persistence,
atomic rollback, pending-write retry, stale updates and tickets, deleted-name reuse, isolation and guild access.
Ruthless coverage checks native catalogs, shops/rewards, gameplay and valid/invalid live configuration edits.
Per-connection Ruthless catalogs also cover curated unique generation, Mageblood effects and Harvest.
Regression tests cover explicit zero-valued unique mods and the native Standard/Ruthless Onslaught difference.
The newer development hit path commits Hardcore deaths before notification, alongside the monster contact path.
Overlapping-connection regressions cover resource ownership through transfer and logout.
SSF tests combine shared peer publication with private dropped items, expiry and reused instance IDs.
Queued-notification regressions cover terminal Hardcore background mutations.
Integration of feature head `74b2c0a2` with main `59154b74` retains the single projectile runtime, Vaal budgets
and current gem-requirement snapshots.
Projectile kills use Ruthless item/gold rewards and gem XP, including the active utility-flask charge restriction.
The shared synchronous quest/gem commits retain the Hardcore terminal-progress guard.
Eighteen additional integration cases cover held-output rewards without replay, SSF trade request/acceptance refusal,
mode and owner eligibility, active flasks, terminal progress and both-inventory trade rollback.
The solution builds without warnings; Core, Data and Gateway suites pass with 688, 18 and 3850 tests respectively.
This merge received automated validation only: this checkout has no `Client/Game.exe` or `Client/Game.patched.exe`.
Automated validation uses isolated extracted data and temporary test saves.
If a local setup keeps generated data in a separate runtime directory, tests still use the checkout's ordinary
`data/` contract. The issue #75 merge temporarily copied only extracted inputs, then retained them with its ignored
validation artifacts. This preserves shared saves/keys and avoids changing production catalog paths or restarting
the shared runtime to prepare automated tests. Native evidence remains specific to the build actually exercised.
The existing league selection and default families are unchanged.
Native checks against the integrated build used one copied 3.29.3b client and disposable saves:

- Standard and SSF characters entered and returned to selection; Standard received its ordinary full life.
- Ruthless retained saved life through selection, reconnect and area transfer. Damage in the destination survived
  another reconnect, and a native vendor interaction refilled life.
- Hardcore SSF death returned the character under SSF Standard; the migrated character entered again with full life.
- Hardcore SSF Ruthless death returned a Void entry with the native terminal-character marker.

These checks used the existing native control bridge and Gateway/Auth logs, rather than visual HUD assertions.
Overlapping sessions, queued work after death and shared SSF peer publication are covered by the deterministic
regressions above; simultaneous clients were not used for native verification.
Complete retail endgame and reward parity remain limited by the server's incomplete gameplay systems.

From the repository root, use local build output so real-data tests can locate `data/tables` and running servers
do not lock the binaries being built:

```powershell
dotnet test tests/GameServer.Core.Tests --artifacts-path artifacts/league-mode-tests
dotnet test tests/GameServer.Data.Tests --artifacts-path artifacts/league-mode-tests
dotnet test tests/GameServer.Gateway.Tests --artifacts-path artifacts/league-mode-tests
```

For manual checks, create disposable characters through `scripts/dev/live.ps1` and the native selector.
Verify ordinary Hardcore death returns the character under the matching softcore league, and Hardcore Ruthless
death leaves a Void character that cannot Play.
Check SSF guild-stash refusal, separate dropped items and personal-stash sharing between one account's SSF characters.
In Ruthless, check the starting support chest, vendor stock, unavailable movement gems, bench refusal and vendor refill.
Check that living characters' saved pools persist across area changes and reconnects.
Existing softcore characters should retain their ordinary vendors, skills, passives, flasks and rewards.
