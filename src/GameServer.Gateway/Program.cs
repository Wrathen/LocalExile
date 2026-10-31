using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using WalkEstimator = GameServer.Core.World.WalkEstimator;
using PlayerWalk = GameServer.Core.World.PlayerWalk;
using ClientPathfinder = GameServer.Core.World.ClientPathfinder;
using Progression = GameServer.Core.World.Progression;
using DamageModel = GameServer.Core.World.DamageModel;
using DamageType = GameServer.Core.World.DamageType;
using SaveFiles = GameServer.Core.Saves.SaveFiles;
using DamagePacket = GameServer.Core.World.DamagePacket;
using DamageRange = GameServer.Core.World.DamageRange;
using DamageOverTime = GameServer.Core.World.DamageOverTime;
using GameServer.Core.Diagnostics;

// GameServer.Gateway — the instance (game) server the client connects to after login 0x14, or after 0x27 on an area
// change (Agents/docs/protocol/02, 09).
// Reads the plaintext opening (0x0F ticket + 0x0E hello), asks GameServer.Auth which area/instance the ticket belongs to
// (InstanceControl "resolve"), answers 0x11 so both directions switch to Salsa20 keyed by the ticket's instance key,
// sends the area (0x1d), answers the client's 0x7d with the player and the area's area transitions, and handles ticks,
// movement and area transition clicks (0x24: change area, or with Ctrl the instance chooser; Agents/docs/protocol/09-area-change.md).
// Port: Game_GATEWAY_PORT (default 6112). Instance keys: data/instance-secret.bin (Game_INSTANCE_SECRET), shared with
// GameServer.Auth. Auth control: 127.0.0.1:Game_AUTH_CONTROL_PORT (20495); without it every ticket gets Lioneye's Watch.

// `commands`: one JSON line per chat command ({"name", "usage", "args"}) for tools such as the desktop launcher, then
// exit; nothing starts. `values <kind>`: what an argument of that kind can be ({"value", "detail"}), from the Gateway's
// own catalogs. Before ServerLog, whose timestamps would break the JSON lines.
if (args is ["commands"])
{
    foreach (var (name, usage, arguments) in CommandUsages()) Console.WriteLine(JsonSerializer.Serialize(new { name, usage, args = arguments }));
    return 0;
}
if (args is ["values", var kind])
{
    if (CompletionValues(kind) is not { } values) { Console.Error.WriteLine($"[gateway] no values of kind {kind}"); return 1; }
    foreach (var (value, detail) in values) Console.WriteLine(JsonSerializer.Serialize(new { value, detail }));
    return 0;
}

ServerLog.Install("gateway");

// dotnet run --project src/GameServer.Gateway -- seed-game-data [database]
// Fills the SQLite game-data overlay from data/tables without starting the servers. The first Gateway start does the
// same; this mode is for a fresh database, an editor or a check while the Gateway is stopped.
if (args.Length > 0 && args[0].Equals("seed-game-data", StringComparison.OrdinalIgnoreCase))
{
    string path = args.Length > 1 ? args[1] : Saves.DefaultPath;
    try
    {
        GameData.Open(path);
        var (stats, mods, modStats) = (GameData.Store
            ?? throw new InvalidOperationException("the game-data store did not open")).ReferenceCounts();
        if (stats == 0 || mods == 0 || modStats == 0)
        {
            Console.Error.WriteLine("[gateway] game data seed: Stats.json/Mods.json are missing; run scripts/dev/extract-data.ps1 first");
            return 1;
        }
        Console.WriteLine($"[gateway] game data seeded into {path}: {stats} stat(s), {mods} mod(s), " +
            $"{modStats} default mod stat row(s)");
        return 0;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"[gateway] game data seed failed: {e.Message}");
        return 1;
    }
}

int port = GatewayEndpoint.Port;
var keyring = InstanceKeyring.LoadOrCreate(Environment.GetEnvironmentVariable("Game_INSTANCE_SECRET") ?? Path.Combine("data", "instance-secret.bin"));

// The HNC side channel needs a 64-byte table from the client; read it from the local client instead of shipping it.
string clientExe = Environment.GetEnvironmentVariable("Game_CLIENT_EXE") ?? FindUp(Path.Combine("Client", "Game.exe")) ?? "Client/Game.exe";
if (!File.Exists(clientExe))
{
    Console.Error.WriteLine($"[gateway] client executable not found ({clientExe}); set Game_CLIENT_EXE");
    return 1;
}
HncSender.LoadMask(File.ReadAllBytes(clientExe));
if (GameDataFiles.Warning() is { } missingData) Console.WriteLine(missingData);

// Characters and stashes: the save database (Saves). Saves from before it (data/saves, data/stashes) go into it once; one
// that can't be read stops the Gateway, so no character starts over.
try
{
    Saves.Open(Saves.DefaultPath);
    Saves.ImportJson(Saves.Db, "data", CharacterSaves.Opts);
    GameData.Open(Saves.Db.Path);
    Console.WriteLine($"[gateway] saves: {Saves.Db.Path}");
}
catch (Exception e) when (e is IOException or Microsoft.Data.Sqlite.SqliteException)
{
    Console.Error.WriteLine($"[gateway] the save database can't be opened: {e.Message}");
    return 1;
}

// Every item sent carries its level requirement (13-items.md §2d): the client shows and checks the byte it receives.
// Gems have their own (GrantedEffectsPerLevel), which the client reads from the SkillGem component instead. Maps have
// none, as in the game: their drop level is where they drop, not who may run them.
Item.LevelRequirementOf = item => !item.Components.Contains("SkillGem") && Items.ByHash(item.TypeHash) is { Class: not (MapDevice.MapClass or MapDevice.MapKeyClass) } t
    ? ItemModStats.LevelRequirement(item, t.DropLevel, Items.Mods) : 0;

// Local play only by default; Game_BIND=0.0.0.0 to accept other machines (then also set Game_GATEWAY_HOST).
var bind = IPAddress.Parse(Environment.GetEnvironmentVariable("Game_BIND") ?? "127.0.0.1");
var listener = new TcpListener(bind, port);
// Before any client connects: Auth forgets the players of this Gateway's last run, which may have stopped without
// sending "leave" for them (their instances would never expire).
var started = await InstanceControl.CallAsync(new ControlRequest { Op = "gateway_started" });
if (!started.Ok) Console.WriteLine($"[gateway] Auth did not take gateway_started: {started.Error}");
// The mods (Mods/Binaries/<id>, Program.Mods.cs), before the first player can use one.
StartMods();
listener.Start();
Console.WriteLine($"[gateway] listening on {bind}:{port}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
// What waits in memory to be saved (experience, flask charges from kills) is saved every few seconds, and at the end.
var saving = SaveUnsavedProgress(cts.Token);
AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveUnsavedProgressNow("the Gateway stops");

while (!cts.IsCancellationRequested)
{
    TcpClient client;
    try
    {
        client = await listener.AcceptTcpClientAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    _ = HandleAsync(client, keyring);
}

listener.Stop();
await saving;
SaveUnsavedProgressNow("the Gateway stopped");
Console.WriteLine("[gateway] stopped");
return 0;

// Experience and flask charges from kills wait in memory (CharacterSaves.AddExperience, PlayerInventories.ChangeUnsaved)
// and are saved here at least every few seconds: a crash or a power cut loses at most those seconds of them.
static async Task SaveUnsavedProgress(CancellationToken stop)
{
    using var timer = new PeriodicTimer(CharacterSaves.ExperienceSaveInterval);
    try
    {
        while (await timer.WaitForNextTickAsync(stop)) SaveUnsavedProgressNow(null);
    }
    catch (OperationCanceledException) { }
}

sealed partial class Program
{
    internal const uint PlayerId = 0x1000;
    // The server's tick: 30 per second. Nothing the server times (an attack) is shorter than one tick.
    internal const int TickRate = 30;
    // Game_LEAGUE=hardcore treats every character as hardcore; otherwise the character's league decides (Auth's resolve).
    internal static readonly bool ForceHardcore = Environment.GetEnvironmentVariable("Game_LEAGUE")?.ToLowerInvariant() == "hardcore";
    // S→C 0x55 flags (11-skills.md §8): standard = bit2 (Resurrect at Checkpoint) + bit3 (Resurrect in Town); hardcore =
    // bit0 (a single Exit button, "Exit to the character selection screen", which sends C→S 0x153). Before the character
    // has a town (The Twilight Strand) only bit2, as in the real game.
    internal static byte DeathScreenFlags(ClientState conn) => ForceHardcore || conn.Hardcore ? (byte)0x01 : conn.LastTown == 0 ? (byte)0x04 : (byte)0x0c;
    internal const double TickSeconds = 1.0 / TickRate;
    // PoB's "recently": the last 4 seconds.
    internal const double RecentlySeconds = 4;
    // Shown once per connection when the client moves in lockstep (InstanceProtocol.IsLockstepMovement).
    internal const string LockstepWarning = "Your Network Mode is lockstep, which this server does not support yet: your character stays " +
        "hidden and cannot move. Set Options > Game > Network Mode to Predictive, then log out to the character screen and Play again.";
    // The Positioned size (object_size − 1) of area transitions and waypoints (AreaTransition.ot, Waypoint.ot
    // object_size 6): the player walks up to them as the client does (PlayerWalk.WalkUpTo).
    internal const int AreaTransitionObjectSize = 5, WaypointObjectSize = 5;
    // How close a waypoint activates on its own.
    internal const double WaypointActivationRange = 25;
    // How close the player walks to the map device before it opens (its size is not known yet).
    internal const double StashReach = 6;
    // The affinity (StashTabAffinityId row 0, "UNUSED0": no item has it) that marks the account's last tab in the tab
    // list, so that S->C 0x7b kind 1 with it opens the stash on that tab.
    internal const uint StashSelectAffinity = 0;
    // How long (s) after the first open's 0x7b the panel's requests belong to that open (the client sends one 0xc7 per
    // 200 ms, at most two then).
    internal const double StashForcedWindow = 1.0;
    // Destination tickets of waypoint travel: that connection's player arrives next to the area's waypoint.
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, byte> WaypointArrivals = new();

    /// <summary>Tickets whose connection arrives at a given cell (back through a town portal).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (int X, int Y)> SpotArrivals = new();

    /// <summary>Each character's open town portal (Portal Scroll or the Portal skill): where it stands (area, instance,
    /// cell) and where it leads (a town's HASH16, or the hideout's). While the server runs.</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenPortal> OpenPortals = new(StringComparer.OrdinalIgnoreCase);
    // The height u32 of S→C 0x1ba (+0x28, passed with the move to Positioned vtable +0xd8) and 0x1b7 (→ +0x290): the
    // fixed height, 0x7fffffff = none, follow the terrain. 0 pinned a moving projectile under the ground (live 2026-10-18).
    internal const uint TerrainHeight = 0x7fffffff;
    // How close (cells, centre to centre) the player must be to pick an item up from the ground: WorldItem.ot's
    // InteractionAction interaction_distance_override = 20 grid cells, plus the radii of the item and the player
    // (object_size 2 each). Measured live 2026-10-18 (Positioned +0x294/+0x298): the client walked from (1301, 1250)
    // toward an item at (1278, 1304) and stopped at (1284, 1284), 20.9 cells away, on the first step inside the range.
    internal const double PickupReach = 20 + 2;
    // How close (cells, centre to centre) gold on the ground is picked up by itself (RunPlayerTick): our own number, a
    // little inside a click's PickupReach.
    // A monster's second skill, after its attack (SkillCatalog.FirstSkillId): a boss's stage skill (BossFights). 0x0103
    // is free in 3.29.3b (SkillCatalog.SkillIds).
    internal const ushort StageSkillId = 0x0103;
    // Server object ids of the quest room objects (SpawnQuestRoomObjects): between the area transitions (0x2000…) and
    // the waypoint (0x2f00).
    internal const uint QuestRoomObjectFirstId = 0x2e00;

    // WorldAreas' own mods (AreaModCatalog); loaded with the first Portal Scroll.
    internal static readonly Lazy<AreaModCatalog> AreaMods = new(() => AreaModCatalog.Load());
    // NPCs.dat with the NPC types' components; loaded with the first town.
    internal static readonly Lazy<NpcCatalog> Npcs = new(() => NpcCatalog.Load());
    // Shared by every connection; loaded on the first /spawn_mob.
    internal static readonly MonsterCatalog Monsters = new();
    // MonsterPacks per area (MonsterPacks.cs); loaded with the first area that has monsters.
    internal static readonly PackCatalog Packs = new();
    // The skills granted to players (the default attack); loaded with the first player.
    internal static readonly SkillCatalog Skills = new();
    // Stat names ↔ client keys (Stats.dat row + 1).
    internal static readonly StatCatalog Stats = new();
    // The shop's portal effects the server can apply itself (/cosmetic): the visual of the player's own portals.
    internal static readonly CosmeticCatalog Cosmetics = new();
    // Every MtxTypes row as an owned microtransaction (23-microtransactions.md); loaded with the first 0x1f2.
    internal static readonly MtxCatalog Mtx = new();
    // What worn cosmetics do on the model and in the stats (ItemisedVisualEffect); loaded with the first equipment sent.
    internal static readonly CosmeticEffects MtxEffects = new();
    // What a worn skill effect's skill_art_variation draws (MiscAnimated and BuffVisuals art variations).
    internal static readonly SkillArt Art = new();
    // The Cosmetics tab's Portrait slot (MicrotransactionSlot 16).
    internal const byte PortraitSlot = 16;
    // Per character: the ids of worn items whose requirements are not met (CharacterStatSources).
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<uint>> InactiveItems = new(StringComparer.OrdinalIgnoreCase);
    // The character panel's Spell Suppression chance stats (the client reads the final value).
    internal static readonly uint[] SuppressionChanceKeys = [.. new[] { "spell_suppression_chance_%",
        "base_spell_suppression_chance_%", "combined_spell_suppression_chance_%" }.Select(n => Stats.Key(n)).OfType<uint>()];
    // Item types (BaseItemTypes + data/item-types.json); loaded with the first player.
    internal static readonly ItemCatalog Items = new();
    // Per character: the skill ids of the socketed gems (GemSkillIds), and the ids gem skills may use (after the default
    // attack, the ids below FirstSkillId + 64 that no basic action uses: SkillCatalog.IsGemSkill; 55 in 3.29.3b).
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GemSkillIds> GemSkillIdsOf = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly Lazy<ushort[]> GemSkillIdPool = new(() => [.. Skills.SkillIds().Skip(1).Where(Skills.IsGemSkill)]);
    // Maps.dat: which area each map opens (MapDevice.cs); loaded with the first Activate.
    internal static readonly Lazy<MapDevice> MapDevices = new(() => MapDevice.Load("data", row => Items.Row(row)?.Hash, Items));
    // The map device's portals are MultiplexPortals of the player type (1): the map device type (3) is only drawn after an
    // open transition, which hung or crashed the client (21-hideouts.md §5). Game_MAP_PORTAL_TYPE overrides it.
    internal static readonly byte MapPortalType = byte.TryParse(Environment.GetEnvironmentVariable("Game_MAP_PORTAL_TYPE"), out byte t) ? t : MapPortalObject.PlayerPortal;
    // Map portal timing (ms): each portal's 0x8f after its create, the gap between portals, the first one after entering
    // the hideout (Game_MAP_PORTAL_LIST_MS, _GAP_MS, _ENTRY_MS).
    internal static readonly int MapPortalListMs = EnvMs("Game_MAP_PORTAL_LIST_MS", 100), MapPortalGapMs = EnvMs("Game_MAP_PORTAL_GAP_MS", 150),
        MapPortalEntryMs = EnvMs("Game_MAP_PORTAL_ENTRY_MS", 1500);
    static int EnvMs(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int ms) && ms >= 0 ? ms : fallback;
    // What dying monsters drop (ItemDrops.cs).
    // Magic and rare items (ItemRolls.cs).
    internal static readonly ItemRolls Rolls = new(Items);
    // Unique items the server can create (names/art from the client data, mods curated: UniqueCatalog).
    internal static readonly UniqueCatalog Uniques = new(Items);
    internal static readonly EssenceCrafting Essences = new(Items, Rolls);
    internal static readonly BenchCrafting Bench = new(Items, Rolls);
    internal static readonly HarvestCrafting Harvest = new(Items, Rolls);
    internal static readonly ItemDrops Drops = new(Items, Rolls, maps: MapDevices.Value.Atlas, uniques: Uniques);
    // Each live area instance's ground items, across connections (InstanceStore.cs). The lifetime is Auth's
    // InstanceRegistry.Lifetime: the Gateway wipes an instance when Auth would.
    internal static readonly InstanceStore Instances = new(TimeProvider.System, new GameServer.Core.World.InstanceRegistry().Lifetime);
    // The passive tree's nodes (PassiveSkills.dat by PassiveSkillGraphId).
    internal static readonly PassiveTree Passives = new();
    // The Atlas passive tree's atlas_* nodes (PassiveSkills.dat), used by the three S→C 0x3d presets.
    internal static readonly AtlasPassives AtlasPassiveNodes = new();
    internal static readonly TimelessJewelEngine TimelessJewels = new();
    internal static readonly OrdinaryPassiveRules OrdinaryPassives = new();
    internal static readonly AscendancyCatalog Ascendancies = new();
    internal static readonly AscendancyRules AscendancyRules = new(Ascendancies);
    // How close (cells) the player must be to a dying monster to gain its experience. Game_XP_RANGE overrides it.
    internal const int GodRegenPerSecond = 10_000;
    // /god regenerates 100% of maximum life per second (more than Righteous Fire's 90% burn).
    internal const int GodLifeRegenPctPerSecond = 100;
    // S→C 0x1b1's pool byte for mana (0 life, 1 mana, 2 energy shield).
    internal const byte ManaPool = 1;
    // When a spell's projectile leaves (the class's spell animation's contact keyframe).
    internal static readonly AnimationContacts Animations = new();
    // BuffDefinitions rows: righteous_fire (on the caster) and righteous_fire_aura (on the enemies it burns).
    internal const ushort RighteousFireBuffRow = 12, RighteousFireAuraBuffRow = 13;
    // The flask buffs' BuffDefinitions rows (flask_effect_life "Healing", flask_effect_mana "Regenerating Mana": invisible,
    // ended by death or a full pool). Game_FLASK_BUFFS=0 recovers without them.
    internal const ushort FlaskEffectLifeBuffRow = 5, FlaskEffectManaBuffRow = 9;
    /// <summary>The quest books that give a passive skill point (BaseItemTypes Name; several Ids, one per quest).</summary>
    internal const string SkillBook = "Book of Skill";
    /// <summary>The quest books that give two passive refund points (BaseItemTypes Name; Acts 1, 6, 9 and 10).</summary>
    internal const string RegretBook = "Book of Regrets";
    internal const string PortalScroll = "Metadata/Items/Currency/CurrencyPortal";
    /// <summary>Continuous pool recovery's target cadence; amounts use elapsed game time, not this nominal delay.</summary>
    internal const int RecoveryStepMs = 16;
    internal static bool FlaskBuffs => Environment.GetEnvironmentVariable("Game_FLASK_BUFFS") != "0";
    // MiscAnimated RighteousFireRing: the big burning circle around a Righteous Fire caster.
    internal const string RighteousFireRingAo = "Metadata/Effects/Spells/ri_fire/rifire.ao";
    // The projectiles' explosion (Fireball's, MiscAnimatedArtVariations fireball_standard variant 0).
    internal const string DefaultProjectileImpactAo = "Metadata/Effects/Spells/fireball/explode_new2.ao";
    // Secondary KB explosion: kinetic_blast_small, distinct from the unscaled main-impact kinetic_blast_large art.
    internal const string KineticBlastExplosionAo = "Metadata/Effects/Spells/cluster_burst/mini.ao";
    // The player's own tick (RunPlayerTick): regeneration and damage over time.
    internal const int PlayerTickMs = 250;
    // How often Righteous Fire's ring is moved to the player (RunRingFollow), and how far ahead it aims (our own number).
    internal const int RingFollowMs = 100;
    // Pets (RunPetFollow): how far beside the player a pet stands, and how far it may fall behind before it walks after
    // the player (cells; our own numbers).
    internal const int PetDistance = 6;
    internal const double PetFollowRange = 12;
    internal static readonly (string Stat, int Value)[] GodStats =
    [
        ("attack_speed_+%", 1000), ("base_cast_speed_+%", 1000), ("cast_speed_+%", 1000),
        ("life_regeneration_rate_per_minute_%", GodLifeRegenPctPerSecond * 60),
        ("base_mana_regeneration_rate_per_minute", GodRegenPerSecond * 60), ("mana_regeneration_rate_per_minute", GodRegenPerSecond * 60),
        ("base_skill_area_of_effect_+%", 200),
    ];

    internal static readonly double ExperienceRange =
        double.TryParse(Environment.GetEnvironmentVariable("Game_XP_RANGE"), System.Globalization.CultureInfo.InvariantCulture, out var xr) ? xr : 150;

    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConnectionCiphers, SemaphoreSlim> SendGates = new();

    // The client's walking speed in grid cells per second at 100% (measured live: ~37 = Character.ot Pathfinding
    // base_speed). Game_WALK_SPEED overrides it.
    internal static readonly double WalkSpeed =
        double.TryParse(Environment.GetEnvironmentVariable("Game_WALK_SPEED"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 37;

    // Game_TRACE=move (or the older Game_MOVE_TRACE=1) logs "[move-trace] qpc=…" for the client's 0x24/0x25/0x191 as read
    // and the server's 0x1ab/0x1b7/0x1ba as written, on the machine's QPC clock (scripts/re/frida/trace-movement.js uses
    // the same clock).
    internal static readonly bool MoveTrace = ServerTrace.Move;

    // The server frame length (ms, S→C 0x2b6): a lockstep client's world advances by these frames. Game_FRAME_MS overrides.
    internal static readonly byte FrameMs =
        byte.TryParse(Environment.GetEnvironmentVariable("Game_FRAME_MS"), out var fm) && fm > 0 ? fm : (byte)33;
}
