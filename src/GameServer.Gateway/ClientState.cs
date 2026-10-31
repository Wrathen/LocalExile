using System.Diagnostics;
using GameServer.Core.Protocol.Instance;
using WalkEstimator = GameServer.Core.World.WalkEstimator;
using PlayerWalk = GameServer.Core.World.PlayerWalk;
using ClientPathfinder = GameServer.Core.World.ClientPathfinder;
using DamageType = GameServer.Core.World.DamageType;
using DamagePacket = GameServer.Core.World.DamagePacket;

/// <param name="AtSpawn">Arrive at the destination's spawn (Resurrect in Town) instead of next to the exit back.</param>
/// <param name="AtWaypoint">Waypoint travel: arrive next to the destination's waypoint.</param>
/// <summary>A character's open town portal: where it stands and where it leads (OpenTownPortal).</summary>
sealed record OpenPortal(ushort Area, ulong Instance, int X, int Y, ushort LeadsTo);

/// <param name="ArriveAt">Arrive at this cell (back through a town portal).</param>
sealed record PendingTransfer(ushort Area, ulong Instance, bool New, bool AtSpawn = false, bool AtWaypoint = false, (int X, int Y)? ArriveAt = null)
{
    /// <summary>Called with Auth's answer to the transfer (a map portal records the map's new instance).</summary>
    public Action<ControlResponse>? Done { get; init; }

    /// <summary>With <see cref="New"/>: the new instance's seed (0 = Auth's choice; the capture tour sets it).</summary>
    public uint Seed { get; init; }
}

// A monster this connection spawned: label for /mobs, the server's estimate of its walk, and its AI state.
// A monster's attack: its skill id, attack time, reach (cells, centre to centre) and damage per hit.
/// <summary>How a monster's projectile skill releases its shot, from the rig and stat set the client itself plays
/// (Program.MonsterReleaseFor): the clip, its contact keyframe as a fraction of the attack, the keyframe's position when
/// the skill spawns the shot there (<c>projectile_uses_contact_position</c>), and whether the monster turns its back for
/// it (<c>action_should_face_away_from_target</c>: the Sand Spitter spits from its rear).</summary>
sealed record MonsterRelease(string Clip, double Fraction, ContactPoint? Point, bool FacesAway);

sealed record MonsterAttack(ushort SkillId, double Seconds, double Range, DamagePacket Damage)
{
    public bool Ranged { get; init; }
    /// <summary>When and from where a projectile skill releases its shot; null: half the attack time, 1.5 cells ahead at
    /// the default height (no rig data for the variety).</summary>
    public MonsterRelease? Release { get; init; }
    /// <summary>A ranged skill's projectile (SkillCatalog.MonsterProjectile): the shot flies from the monster at the
    /// attack's contact and lands its hit when it reaches the player (Program.ProjectileHostile.cs). Null keeps the
    /// instant contact at half the attack time.</summary>
    public SkillCatalog.MonsterProjectileInfo? Projectile { get; init; }
    /// <summary>The attack's final stats (the client derives none, 11-skills.md §4): the swing time, reach and damage.
    /// These also go on the monster's Stats component.</summary>
    public IReadOnlyList<(uint, int)> Stats()
    {
        int ms = (int)Math.Round(Seconds * 1000);
        var physical = Damage[DamageType.Physical];
        int min = (int)Math.Floor(physical.Min), max = (int)Math.Floor(physical.Max);
        return
        [
            (StatKeys.MainHandMinPhysical, min), (StatKeys.MainHandMaxPhysical, max),
            (StatKeys.MainHandMinTotal, min), (StatKeys.MainHandMaxTotal, max),
            (StatKeys.MainHandAttackDurationMs, ms), (StatKeys.MainHandTotalAttackDurationMs, ms),
            (StatKeys.HundredTimesAttacksPerSecond, (int)Math.Round(100 / Seconds)),
            (StatKeys.MainHandBaseMaxAttackDistance, (int)Range), (StatKeys.MainHandMaxAttackDistance, (int)Range),
        ];
    }

    /// <summary>The skill's stats: <see cref="Stats"/> plus skill_is_non_pathing. The server walks the monster into range
    /// itself; without it the Melee action's begin (0x141938830) sends the attack through the client's own pathing step,
    /// which drops a server-driven monster's attack (no swing). Only on the skill: on the monster's Stats component it
    /// also stops its Move action from pathing (live 2026-10-17: the monsters stood still).</summary>
    public IReadOnlyList<(uint, int)> SkillStats() => [.. Stats(), (StatKeys.SkillIsNonPathing, 1)];
}

sealed record SpawnedNpc(NpcCatalog.Npc Npc, int X, int Y);

// An open sell window: the NPC, the two offer inventories (manager 1), the offered items and the NPC's offer.
sealed class NpcSale(SpawnedNpc npc, uint mine, uint theirs)
{
    public SpawnedNpc Npc { get; } = npc;
    public uint Mine { get; } = mine;
    public uint Theirs { get; } = theirs;
    // The offered items stay in the character's inventory cells until the sale ends (PlayerInventories.Offer).
    public List<InventoryItem> Reward = [];
    public IReadOnlyList<InventoryItem> QuotedItems = [];
    public IReadOnlyList<Item> QuotedRewards = [];
    public string? QuoteError;
    public uint MineRevision, TheirsRevision;
}

// An open shop tab: the NPC, its inventory id (manager 1), the items left by id and the tab's 0xbf revision.
sealed class OpenShop(SpawnedNpc npc, Dictionary<uint, (uint Tab, InventoryItem Item)> items)
{
    public SpawnedNpc Npc { get; } = npc;

    /// <summary>The items for sale by id, each with its tab's inventory id.</summary>
    public Dictionary<uint, (uint Tab, InventoryItem Item)> Items { get; } = items;

    /// <summary>The next revision of a tab's inventory (S→C 0xbf).</summary>
    public uint NextRevision(uint tab) => _revisions[tab] = _revisions.GetValueOrDefault(tab) + 1;
    readonly Dictionary<uint, uint> _revisions = [];
}

readonly record struct MonsterHit(uint Life, bool StageStarts);

sealed class SpawnedMonster(string label, WalkEstimator walk, int aggroRange, int stopRange, uint maxLife)
{
    public MonsterAttack? Attack { get; init; }
    public bool Hostile { get; init; } = true;
    /// <summary>Server-side stun immunity independent of the rolled mods (pinnacle bosses such as The King in the Mists).</summary>
    public bool CannotBeStunned { get; init; }
    /// <summary>The humanoid King's life cannot reach 0 before the three fetishes hand the fight to the treant.</summary>
    public bool KingPhaseLocked { get; init; }
    // What it is, for the experience and items its death gives (null for monsters made without a variety).
    public MonsterCatalog.Variety? Variety { get; init; }
    // Its known TakeHit clip lengths supply HitReaction's recovery duration. Native Actor TakeHit selects the actual
    // clip through its current ACT; rig indexes are retained as extraction/profile diagnostics.
    public IReadOnlyList<HitAnimation> HitAnimations { get; init; } = [];
    public byte Level { get; init; } = 1;
    public MonsterRarity Rarity { get; init; }
    // Its mods (MonsterMods.cs): the rarity's own and the rolled affixes, with what they change. Kept with the instance.
    public MonsterModSet Mods { get; init; } = MonsterModSet.None;
    // The client's speed roll for this object in percent (MonsterSpeedRoll.cs); Walk's speed already includes it.
    public int SpeedRoll { get; init; }
    // Damage over time not yet applied (fractions of a point between ticks).
    public double Burn;
    // Life regeneration not yet applied (fractions of a point between ticks; RegenerateMonsters).
    public double Regen;
    // Impales retained for consumption/expiry, including on a corpse (Impale.cs).
    public readonly ImpaleState Impale = new();
    // The player's ignites on it (MonsterIgnites.cs), under the Monsters lock.
    public readonly MonsterIgnites Ignites = new();
    // All outgoing attacks share this defender's sequence, under the Monsters lock. A new spawn starts fresh.
    public readonly GameServer.Core.World.EvasionEntropy AttackEvasion = new();
    // While a swing runs (Clock seconds) the monster neither chases nor swings again.
    public double BusyUntil;
    // Every AI order owns a generation. A flinch invalidates queued echoes and contact work from that order.
    // SwingGeneration identifies ordinary attack recovery so canceling it does not clear an emerge or boss stage.
    public int ActionGeneration;
    public int? SwingGeneration;
    public string Label => label;
    public WalkEstimator Walk { get; set; } = walk;
    public int AggroRange => aggroRange;
    public int StopRange => stopRange;
    public uint MaxLife { get; } = maxLife;
    public uint Life = maxLife;
    // Until when its last flinch plays (Clock seconds; HitReaction): no new action or flinch before.
    // Guarded by the Monsters lock.
    public double FlinchUntil = double.NegativeInfinity;
    public bool Aggro, Dead;
    // Soul credit alone is idempotent; this does not alter existing XP, loot, quest or charge policy.
    public bool SoulKillProcessed;
    public (int X, int Y)? CollisionPosition;
    public (int X, int Y)? LastTarget;
    public double NextChaseRetryAt;
    // A boss's stage (BossFights) and whether a hit has started it.
    public BossFights.Stage? Stage { get; init; }
    public bool StageTaken, StageRunning;
    // Its StateMachine's shared states as last sent, and whether the big boss life bar is up.
    public StateMachineData? States;
    public bool BossBarShown;
}

// A running Righteous Fire: the player's buff instance, the skill, its damage (percent of maximum life per minute, to
// enemies and to the player), its radius in cells, and the aura buff instance on each monster inside it.
sealed record RighteousFireState(ushort Buff, ushort SelfAura, uint? Ring, ushort Skill, int DamagePctPerMinute, int BurnPctPerMinute, int Radius)
{
    public int EnergyShieldBurnPctPerMinute { get; init; }
    public int EnergyShieldDamagePctPerMinute { get; init; }
    public readonly Dictionary<uint, ushort> Auras = [];
    /// <summary>The worn skill effect's righteous_fire_aura visual (BuffVisuals row; null: the row's own), on the enemies too.</summary>
    public ushort? AuraVisual { get; init; }
    /// <summary>Where the ring was last sent (FollowRighteousFire).</summary>
    public (int X, int Y)? RingAt;
}

// The last instance list shown (0xae); 0xaf picks a row of it.
sealed record ChooserList(ushort Area, IReadOnlyList<ulong> Ids, bool AtWaypoint = false);

sealed partial class ClientState(AreaLayout layout, uint ticket, uint seed)
{
    public AreaLayout Layout { get; private set; } = layout;
    public uint Ticket => ticket;
    public uint Seed => seed;
    public string? Character { get; init; }
    public Guid? CharacterId { get; init; }
    public readonly Guid RuthlessVitalsSessionId = Guid.NewGuid();
    /// <summary>The account the character belongs to (Auth's resolve): its stash (AccountStash) is shared by all of them.</summary>
    public string? Account { get; init; }
    public string? League { get; init; }
    public bool Hardcore { get; init; }
    public bool Ssf { get; init; }
    public bool Ruthless { get; init; }
    public double RuthlessGemExperienceCarry;
    public bool RuthlessDeathHandled;
    public bool HardcoreDeathRecorded;
    /// <summary>The character's last town (HASH16, from Auth's resolve): Resurrect in Town goes there. 0 before the
    /// character has entered a town (the death screen then has no Resurrect in Town).</summary>
    public ushort LastTown { get; init; }
    /// <summary>The area the player left through an exit to come here, or null (login, Resurrect in Town).</summary>
    public ushort? CameFrom { get; init; }
    // The character's class: its Characters.dat row (the Witch: 1).
    public int ClassRow { get; init; } = SkillCatalog.WitchRow;
    public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    public readonly ConnectionPerformance Performance = new();
    // The connection's last messages, both directions (PacketHistory), and why the server closed it (the DISCONNECT line).
    public readonly PacketHistory Packets = new();
    public volatile string? CloseReason;
    // The Escape menu's pause (C→S 0x1d4) stops the Clock: walks, cooldowns and flask effects stand still meanwhile.
    public bool Paused => !Clock.IsRunning;
    // A transfer invalidates flights even during the gap after PendingTransfer is consumed by Transfer.
    // Guarded by Monsters, like projectile impact commits and pause changes.
    public bool LeavingArea;
    public readonly RecentEnemyHits RecentEnemyHits = new();
    public readonly PlayerElementalAilments ElementalAilments = new();
    // The King in the Mists' Portent Vapors: stacks taken from his Mystic Fetishes and when they expire. The server
    // applies the increased damage taken to his hits; the buff icon and count are presentation.
    public int KingPortentStacks;
    public double KingPortentUntil = double.NegativeInfinity;
    public Random IncomingHitRandom = Random.Shared;
    // The player's ignite rolls on monsters (MonsterIgnites), apart from the hit's own streams.
    public Random IgniteRandom = Random.Shared;
    public int IncomingHitEpoch;
    public int DeathNotificationEpoch = -1;
    public int FreezeGeneration;
    public int ActionInterruptionGeneration => unchecked(HitRecovery.Generation + FreezeGeneration);
    public (int Count, int Chance)? PresentedRecentEnemyHits;

    // A failed transfer retains history but cannot revive a swing from before the attempted transfer.
    public void InvalidateIncomingHits(bool clear = true)
    {
        lock (Monsters)
        {
            IncomingHitEpoch++;
            // The action state machine replaces the old swing epoch argument. Retired work cannot
            // resume after a failed transfer, even when admission reopens on this same connection.
            foreach (var monster in Monsters.Values)
            {
                monster.ActionGeneration++;
                monster.SwingGeneration = null;
            }
            if (clear) { RecentEnemyHits.Clear(); ElementalAilments.Clear(); }
        }
    }
    public int ProjectileEpoch;
    public Program.ProjectileSession? ProjectileRuntime;
    public readonly HashSet<uint> ProjectileObjects = [];

    public bool ProjectileActive(int epoch)
    {
        lock (Monsters) return !Closed && !LeavingArea && !RageRetired && ProjectileEpoch == epoch;
    }

    public bool TryBeginAreaChange(PendingTransfer transfer, out uint[] projectiles)
    {
        using (Program.ProjectileMutation(this, advance: false))
        {
            projectiles = [];
            if (Closed || PendingTransfer is not null || LeavingArea) return false;
            PendingTransfer = transfer;
            LeavingArea = true;
            InvalidateIncomingHits(clear: false);
            ProjectileEpoch++;
            projectiles = [.. ProjectileObjects];
            ProjectileRuntime?.Invalidate();
            ProjectileObjects.Clear();
            return true;
        }
    }

    public void ResumeAfterFailedTransfer() { lock (Monsters) LeavingArea = false; }

    // Successful transfers retire all source-area workers independently of any particular carried mechanic.
    public void Retire() { lock (Monsters) RageRetired = true; }

    /// <summary>The <see cref="Clock"/> time of a Stopwatch timestamp in the past (a socket read); now when 0 or paused.</summary>
    public double ClockAt(long timestamp)
    {
        double now = Clock.Elapsed.TotalSeconds;
        double at = timestamp == 0 || Paused ? now : Math.Max(0, now - System.Diagnostics.Stopwatch.GetElapsedTime(timestamp).TotalSeconds);
        // A buffered input cannot rewrite motion in history already consumed by projectile collision.
        return Math.Max(at, ProjectileRuntime?.Runtime.Now / 120000d ?? 0);
    }
    // The server's copy of the client-predicted walk (reported in each 0x1ab): the client's own pathfinder and stepping.
    public PlayerWalk Walk => _walk ??= NewPlayerWalk();
    PlayerWalk? _walk;

    /// <summary>Switches to a layout with a walk map (before the player exists).</summary>
    public void Use(AreaLayout better)
    {
        Layout = better;
        _walk = NewPlayerWalk();
        ApplyMovementSpeed();
    }

    // The blocking objects the client's pathfinder walks around (Positioned blocking = true), with their Positioned size
    // (object_size − 1): NPCs (NPC.ot 5) and the stashes (Stash.ot, GuildStash.ot 3) by object id, and the living
    // hostile monsters where the server's estimate has them now (MonsterVarieties ObjectSize − 1).
    public const int NpcBlockerSize = 4, StashBlockerSize = 2, MapDeviceBlockerSize = 3;
    readonly Dictionary<uint, ClientPathfinder.Blocker> _blockers = [];
    volatile IReadOnlyList<ClientPathfinder.Blocker> _blockerList = [];
    public IReadOnlyList<ClientPathfinder.Blocker> Blockers
    {
        get
        {
            var fixedOnes = _blockerList;
            if (Phasing) return fixedOnes;   // phasing (Quartz Flask): monsters do not block (UtilityFlasks.Phasing)
            double now = Clock.Elapsed.TotalSeconds;
            List<ClientPathfinder.Blocker>? all = null;
            lock (Monsters)
                foreach (var m in Monsters.Values)
                {
                    if (!LivingCollision.Blocks(m)) continue;
                    var (x, y) = m.Walk.Position(now);
                    (all ??= [.. fixedOnes]).Add(new(x, y, LivingCollision.Size(m)));
                }
            foreach (var peer in Instance?.PlayersExcept(PeerId) ?? [])
                if (peer.Life > 0) (all ??= [.. fixedOnes]).Add(new(peer.X, peer.Y, LivingCollision.PlayerSize));
            return all is null ? fixedOnes : new GameServer.Core.World.BlockerSet(all);
        }
    }

    public IReadOnlyList<ClientPathfinder.Blocker> StationaryBlockers => _blockerList;

    public int StationaryInteractionSize(uint id)
    {
        lock (_blockers) return _blockers.TryGetValue(id, out var b)
            ? Math.Max(b.Size, (int)Math.Ceiling(b.CollisionRadius ?? 0)) : 0;
    }

    public void SetBlocker(uint id, int x, int y, int size, double? collisionRadius = null)
    {
        lock (_blockers) { _blockers[id] = new(x, y, size) { CollisionRadius = collisionRadius }; _blockerList = new GameServer.Core.World.BlockerSet(_blockers.Values); }
    }

    public void RemoveBlocker(uint id)
    {
        lock (_blockers) if (_blockers.Remove(id)) _blockerList = new GameServer.Core.World.BlockerSet(_blockers.Values);
    }

    /// <summary>Applies the passive movement speed to the server's walk estimate; /set_speed is a dev override.</summary>
    public void ApplyMovementSpeed() =>
        Walk.SetSpeed(Math.Max(1, Program.WalkSpeed * (100 + Pools.MovementVelocityPct) / 100 * ElementalAilments.ActionFactor(Clock.Elapsed.TotalSeconds)), Clock.Elapsed.TotalSeconds);

    /// <summary>Replaces an area transition of this connection's layout (dev changes; the walk estimate keeps going).</summary>
    public void ReplaceAreaTransition(uint id, AreaTransitionObject transition) =>
        Layout = Layout with { AreaTransitions = [.. Layout.AreaTransitions.Select(e => e.Id == id ? (e.Id, transition) : e)] };

    // The quest walls (Barrier) are read on every path, so a wall that opens or closes counts from the next order on.
    private PlayerWalk NewPlayerWalk()
    {
        var l = Layout;
        Func<int, int, bool>? walkable = l.Walkable is { } w ? (cx, cy) => w(cx, cy) && !Barrier.Contains((cx, cy)) : null;
        return new(l.SpawnX, l.SpawnY, Program.WalkSpeed, walkable, l.GridW > 0 ? l.GridW : 4096, l.GridH > 0 ? l.GridH : 4096,
            () => Blockers, stationary: CrowdEscapeBlockers);
    }
    public bool PlayerSent;
    // Server frames ended so far (0x2b5).
    public long ServerFrame;
    public byte[] Carry = [];
    /// <summary>Reads in a row that framed nothing (ClientInbox.UnframedRun).</summary>
    public int UnframedRun;
    /// <summary>The portal cosmetic: the MicrotransactionPortalVariations row every portal is listed with (0x008f +0x26),
    /// 0 for the base effect. Applied to portals already standing as well as to new ones.</summary>
    public ushort CosmeticPortalVariation;
    /// <summary>The worn cosmetics as 0x1f3 lists them (WornCosmetics), read from the save on first use; null after a
    /// pick, so the next use reads it again.</summary>
    public List<InstanceMicrotransactions.Equipped>? WornCosmetics;
    /// <summary>The Cosmetics tab's last published skill targets, built inside the send gate with 0x1f3.
    /// A key lives only while its full target remains present; retired keys are never reassigned on this connection.</summary>
    public Dictionary<uint, InstanceMicrotransactions.SkillSlot> SkillSlots = [];
    public uint NextSkillCosmeticKey = InstanceMicrotransactions.SlotCount;
    /// <summary>The worn pets by object id (SpawnPets), replaced when a Pet slot changes; RunPetFollow walks them.</summary>
    public readonly Dictionary<uint, SpawnedPet> Pets = [];
    public bool PetFollowRunning;
    /// <summary>How map portals are turned around the vertical axis (degrees; Positioned rotation). Dev: /portal_rotation,
    /// which rebuilds the portals that already stand.</summary>
    public float PortalRotationDegrees;
    public float PortalRotationRadians => PortalRotationDegrees * MathF.PI / 180f;
    public long Ticks;
    public volatile PendingTransfer? PendingTransfer;
    public ChooserList? Chooser;
    public ChatCommands? Commands;
    // 0x1c's u32 is an HNC carrier and must decrypt to idle zero bytes, or the client throws "HNC Protocol Violated".
    public HncSender? Hnc;
    public InstanceKeyring Keyring = null!;
    // Server objects created after entering (monsters); the player is 0x1000 and area transitions start at 0x2000.
    public uint NextObjectId = 0x3000;
    // Room server objects a quest keeps in the area until its flag is set (The Twilight Strand's force wall), by id.
    public readonly Dictionary<uint, (string Flag, IReadOnlySet<(int X, int Y)> Cells)> QuestRoomObjects = [];
    // Cells quest walls close to the player's walk estimate (QuestBarriers), the union of QuestRoomObjects' cells.
    public volatile IReadOnlySet<(int X, int Y)> Barrier = new HashSet<(int X, int Y)>();

    /// <summary>Recomputes <see cref="Barrier"/> from the standing quest walls; the player's walk estimate reads it on its
    /// next order (call under the Monsters lock).</summary>
    public void UpdateBarrier() =>
        Barrier = new HashSet<(int X, int Y)>(QuestRoomObjects.Values.SelectMany(o => o.Cells));
    // The player's attacks: the next time one may start (Clock seconds). The client repeats 0x24 while the button is held.
    public double NextAttackAt;
    // Counts the player's actions; an attack's hit lands only if no other action started since (moving cancels it).
    public int ActionCount;
    public readonly PlayerHitRecovery HitRecovery = new();
    public readonly WardState Ward = new();
    public readonly AntiquarianCombatState Antiquarian = new();
    public readonly TimedLifeRecovery EnduringCryRecovery = new();
    public double SentEnduringCryUntil = double.NaN;
    public ushort EnduringCryBuff;
    public bool AppliedAdrenaline;
    public (uint Maximum, bool Broken)? SentWard;
    public double SentAdrenalineUntil = double.NaN;
    public double SentStunRecoveryUntil = double.NaN;
    public ushort WardBuff, AdrenalineBuff;
    // The stats the server has set on the player after creating it (/set_stat, /set_speed): what the client computes
    // with, so the server can follow it (attack time).
    public readonly Dictionary<uint, int> PlayerStats = [];
    // Transient Rage belongs to this connection until its destination ticket takes ownership; guarded by Monsters.
    public readonly RageState Rage = new();
    public bool RageRetired;
    public ushort RageBuff;
    public RageState.Value? SentRage;
    // Reap's Blood Charges held (BloodCharges), the buff instance sent for them and the count it carried; guarded by Monsters.
    public int BloodCharges;
    public ushort BloodChargeBuff;
    public int SentBloodCharges;
    // Shared Vaal row budgets and native presentation belong to this connection; guarded by Monsters.
    public readonly VaalSoulState VaalSouls = new();
    public bool VaalSoulsRetired;
    public readonly Dictionary<uint, VaalSoulBudget> SentVaalSouls = [];
    // Running auras by skill id, with their buff instances and reservation. Guarded by the Monsters lock.
    public readonly Dictionary<ushort, AuraCatalog.Aura> Auras = [];
    // Only self-owned aura instances actually sent on this connection's current player object.
    public readonly Dictionary<ushort, AuraCatalog.Aura> PresentedAuras = [];
    public bool AuraStatsDirty;
    public bool PlayerObjectCreated;
    // When each skill with a cooldown (GrantedEffects row) may be used again, in Clock seconds. Guarded by the Monsters lock.
    public readonly Dictionary<int, double> SkillReadyAt = [];
    // Eligible ordinary one-use cooldowns retain remaining base-time units through recovery-rate changes.
    public readonly Dictionary<int, SingleUseCooldownState> SingleUseCooldowns = [];
    // The skill ids the client has on this connection's player (SendPlayer, SendSkills): the 0x40 skill list only adds,
    // so the ones that leave are removed by id.
    public HashSet<ushort> ClientSkillIds = [];
    // The character's level and the pools that follow from it and the class (CharacterStats).
    public EnergyShield EnergyShield { get; } = new();
    CharacterStats.Pools pools = new(1, 50, 40, 2, 0);
    public CharacterStats.Pools Pools { get => pools; set { pools = value; EnergyShield.SetMaximum(value.EnergyShield); } }
    // The player stat keys the client holds (PlayerStatsWithCleared). Guarded by the Monsters lock.
    public HashSet<uint> SentStatKeys = [];
    // Dev-only forced lucky block, on top of the stats (Svalinn's "Chance to Block is Lucky").
    public bool ForceAttackBlockLucky, ForceSpellBlockLucky;
    // When the player last hit an enemy (attack or spell hit; Clock seconds), for "if you've hit an enemy recently".
    // Guarded by the Monsters lock.
    public double LastEnemyHitAt = double.NegativeInfinity;
    // PoE evasion entropy (0..99): every attack adds the attacker's hit chance, and 100+ means a hit. Shared by all
    // attackers and reset after 3.33 s without an attack. Guarded by the Monsters lock.
    public double EvasionEntropy = -1;
    public double LastEvasionAt = double.NegativeInfinity;
    // Dev-only forced lucky/unlucky Spell Suppression rolls, on top of the passive stat.
    public bool ForceSpellSuppressionLucky, ForceSpellSuppressionUnlucky;
    // The character's experience (the Player component's; the level follows from it) and its allocated passives
    // (PassiveSkillGraphId), both kept in its save (CharacterSaves). Changed under the Monsters lock.
    public uint Experience;
    public List<ushort> Passives = [];
    /// <summary>The three Atlas passive presets (PassiveSkillGraphId) and the points the character has; kept in its save.</summary>
    public List<ushort>[] AtlasPassives = [[], [], []];
    public ushort AtlasPassivePoints;
    /// <summary>The cluster jewel nodes allocated in the tree's expansion cells: the client's own small ids, sent with
    /// the expansion flag (C->S 0x34/0x36) and carried in 0x33's second list. Empty until a cluster jewel is socketed.
    /// </summary>
    public HashSet<ushort> ClusterPassives = [];
    public AscendancyProgress MainAscendancy = new();
    public CharacterSaves.PassiveProgressSnapshot? SavedPassiveProgress;
    // Selected mastery effects: mastery node (PassiveSkillGraphId) -> effect HASH16. Saved with the character.
    public Dictionary<ushort, ushort> MasteryEffects = [];
    // Applied tattoos: passive node (PassiveSkillGraphId) -> tattoo name. The tattoo stays on the node when the
    // passive is unallocated, so this map is independent of Passives. Saved with the character.
    public Dictionary<ushort, string> Tattoos = [];
    // Journey Tattoo of Makanui's chosen replacement keystone: passive node -> keystone PassiveSkillGraphId.
    public Dictionary<ushort, ushort> TattooKeystones = [];
    // Changes whenever Tattoos/TattooKeystones changes; part of the stat-source cache key.
    public int TattooRevision;
    // Extra passive points from /add_passive_points (the 0x33 extra_skill_points field), saved with the character.
    public ushort ExtraPassivePoints;
    // Refund points (0x33's first u16; Orbs of Regret), saved with the character; /add_refund_points grants them for now.
    public ushort RefundPoints;
    // /god (GodStats) and the running Righteous Fire, if any; buff instance ids (0x1c1) given out on this connection.
    public bool God;
    public readonly GracePeriod EntryProtection = new();
    public RighteousFireState? RighteousFire;
    /// <summary>The hands' part of the player model last sent (Program.HandsKey): null until the area's first model.</summary>
    public string? ModelHands;
    /// <summary>The flasks recovering life and mana (C→S 0x70), applied by the player tick.</summary>
    public Flasks.Effects FlaskEffects { get; } = new();
    /// <summary>The utility flasks whose effect runs (UtilityFlasks); their stats count in the pools until they end.</summary>
    public UtilityFlasks.Effects UtilityFlaskEffects { get; } = new();
    /// <summary>What Headhunter took from slain rares (StolenMods).</summary>
    public StolenMods StolenMods { get; } = new();
    /// <summary>The Headhunter buff on the client, 0 for none (Monsters lock).</summary>
    public ushort StolenModsBuff;
    /// <summary>The flask buffs Mageblood has on the client, by flask item id (Program.StepMagebloodBuffs; Monsters lock).</summary>
    public Dictionary<uint, (byte Slot, ushort Buff, ushort? Extra)> MagebloodBuffs { get; } = [];
    /// <summary>The flasks' partial charges and the crit charge cooldown (FlaskCharges).</summary>
    public FlaskCharges.Carry FlaskChargeCarry { get; } = new();
    /// <summary>One change of the flask bar and its S→C 0xbf at a time (Program.ChangeFlaskBar), so the revisions go out in
    /// order with the latest charges.</summary>
    public SemaphoreSlim FlaskBarGate { get; } = new(1, 1);
    /// <summary>The smoke clouds, consecrated ground and taunts of flasks (FlaskGrounds).</summary>
    public FlaskGrounds.State FlaskGrounds { get; } = new();
    /// <summary>Whether the player phases (a Quartz Flask runs): the walk estimate walks through monsters (Blockers).</summary>
    public volatile bool Phasing;
    public ushort NextBuff = 1;
    public uint PlayerMaxLife => Pools.Life;
    // The player's life as the server keeps it (PlayerObject's Life, full on creation); 0 = dead.
    public bool AuraDeathHandled;
    uint playerLife;
    public uint PlayerLife
    {
        get => playerLife;
        set
        {
            lock (Monsters)
            {
                // Resource loss belongs to the actual death transition, never a delayed death notification.
                if (playerLife > 0 && value == 0) VaalSouls.ClearBalance();
                playerLife = value;
            }
        }
    }
    // Defaults to the original shared stream; an isolated connection may supply deterministic critical draws.
    internal Random CriticalRandom { get; init; } = Random.Shared;
    internal Random AttackAccuracyRandom { get; set; } = Random.Shared;
    // The player's mana as the server keeps it: spent by gem skills (mana_cost), regenerated by RunPlayerTick.
    public uint PlayerMana;
    // When the server last sent the pool back after an attack-move, whose use the client pays for but the server only walks.
    public double LastManaResyncAt = double.NegativeInfinity;
    public uint? SentEnergyShield;
    public bool? SentRechargeActive;
    public int? SentEsRecoveryRate;
    // (MonsterVarieties row, level) pairs already registered with 0x20 on this connection.
    public readonly HashSet<(int Row, byte Level)> MonsterTypes = [];
    // The open sell window (Sell Items): what the player offered and what the NPC offers back.
    public NpcSale? Sale;
    // The shop tab opened last (Purchase Items), with what is left in it.
    public OpenShop? Shop;
    // The town's Stash object (0 = none here), and the stash tabs whose inventory this connection has sent (0xc0 + 0xc8).
    public uint StashObject;
    /// <summary>The Delve Crawler in the Azurite Mine Encampment (Program.Delve), and its cell.</summary>
    public uint DelveCrawlerObject;
    public (int X, int Y) DelveCrawlerAt;
    public readonly List<uint> DelveCrawlerBlockers = [];
    public readonly List<uint> DelveCrawlerAttachments = [];
    public uint DelveGeneratorObject;
    public bool DelveGeneratorOpen;
    public readonly HashSet<int> StashTabsSent = [];
    /// <summary>The guild stash: its object in the hideout, its tabs this connection has the contents of, and whether its
    /// panel is the one open (then the stash messages act on it; StashOf).</summary>
    public uint GuildStashObject;
    public (int X, int Y) GuildStashAt;
    public uint HeistLockerObject;
    public (int X, int Y) HeistLockerAt;
    public uint HorticraftingObject;
    public (int X, int Y) HorticraftingAt;
    public uint CraftingBenchObject;
    public bool BenchOpen;
    /// <summary>The Horticrafting Station's recipe browser is the open window; its craft requests are served only then.</summary>
    public bool HarvestOpen;
    public bool BenchBalanceSent;
    public uint BenchBalanceRevision;
    public IReadOnlyList<InventoryItem> BenchBalanceItems = [];
    public HideoutScene? Furniture;
    public readonly HideoutEditing HideoutEditor = new();
    public (int X, int Y) StashAt;
    public void MoveWaypoint(WaypointObject waypoint) => Layout = Layout with { Waypoint = waypoint };
    public (int X, int Y) CraftingBenchAt;
    public readonly HashSet<int> GuildStashTabsSent = [];
    public bool GuildStashOpen;
    // Whether this connection opened the stash already (only the first open selects the account's last tab), and while
    // that first open runs (until StashForcedUntil, Clock seconds) the tab it selects: the panel's own request for the
    // tab it was on is then not the player's choice.
    public bool StashOpened;
    public int StashForcedTab = -1;
    public double StashForcedUntil;
    // The NPC whose dialogue window is open (0x84 sent, 0x85 not yet), 0 = none.
    public volatile uint TalkingTo;
    // Whether the open dialogue shows an NPCTalk row's talk (0x84 with its index) rather than the option list.
    public volatile bool InNpcTalk;
    public uint TutorialSceneId, TutorialChestId;
    // a1q4 in The Mud Flats (GlyphQuest): the captured layout, the nests by object id (guarded by itself), the wall and the waterfall.
    public GlyphQuest.Layout? Glyphs;
    public readonly Dictionary<uint, GlyphQuest.Nest> GlyphNests = [];
    public uint GlyphWallId, GlyphWaterId;
    public double GlyphSyncAt;
    public TutorialObjects.Placement? TutorialChest;
    public AreaPopulation.RoomObject? TutorialZombiePlacement;
    public bool TutorialSceneRunning;
    public string? NpcFinishFlag;
    public int? NpcRewardOffer;
    public Dictionary<uint, Item> NpcRewardItems = [];
    // NPCs created on this connection by object id (town NPCs, /npc).
    public readonly Dictionary<uint, SpawnedNpc> Npcs = [];
    /// <summary>The character's quest flags (QuestFlags.dat Ids), saved in data/saves/.</summary>
    public HashSet<string> QuestFlags = [];
    // QuestFlags is changed under the Monsters lock (SetQuestFlagsHeld); code that may run beside it (the player tick)
    // reads it through these, under the same lock.
    public HashSet<string> QuestFlagsSnapshot() { lock (Monsters) return [.. QuestFlags]; }
    public bool HasQuestFlag(string flag) { lock (Monsters) return QuestFlags.Contains(flag); }
    // One quest-flag change at a time per connection (SetQuestFlags, SyncGlyphs, the a1q5 sync): each reads, changes and sends the whole
    // set, so two at once could send an older set last.
    public readonly SemaphoreSlim QuestGate = new(1, 1);

    /// <summary>The character's league quest flags (client ids, Quests.League); not saved: every character has the same.</summary>
    public HashSet<int> LeagueQuestFlags = [.. Quests.League.EveryCharacter, .. Program.Bench.UnlockFlags];
    /// <summary>What the next Delve chart (S→C 0x240) carries; /delve_chart sets it (one cell at 0, 0 by default).</summary>
    public (uint A, ushort B, List<InstanceDelve.Cell> Cells) DelveChartProbe = (0, 0, [new(0, 0, 0, 0, 0)]);

    /// <summary>The hideout doodads created in this area (the map device), by object id.</summary>
    public readonly Dictionary<uint, HideoutCatalog.Doodad> HideoutDoodads = [];

    /// <summary>The open map's portals created in this area, by object id: where each stands.</summary>
    public readonly Dictionary<uint, (int X, int Y, int Index)> MapPortals = [];

    /// <summary>Counts the map portal sequences started (SpawnMapPortals); a newer one stops the older.</summary>
    public int MapPortalGeneration;

    /// <summary>In a map's area: the portal back to the hideout (object id and cell), or null.</summary>
    public (uint Id, int X, int Y)? ReturnPortal;
    /// <summary>The portal back to the character's open town portal, in its town or hideout (SpawnBackPortal).</summary>
    public (uint Id, int X, int Y)? BackPortal;
    /// <summary>The gem skill being channelled (Cyclone: C→S 0x197 until 0x199), 0 for none, and when it next hits.</summary>
    public ushort ChannelSkill;
    public int ChannelGeneration;
    /// <summary>The last passive and equipment stat sum and its inputs (CharacterStatSources).</summary>
    public volatile Program.StatSourcesResult? StatSourcesCache;
    /// <summary>The channelled skill's action sequence, so its 0x1af hit result names the action it belongs to.</summary>
    public ushort ChannelSkillSequence;
    public double NextChannelHitAt;
    /// <summary>When the channel last heard from the client (its start, or a C→S 0x197 update; Clock seconds).</summary>
    public double LastChannelUpdateAt;
    /// <summary>The instance this connection is in (its number), for a portal back to it.</summary>
    public ulong InstanceNumber { get; init; }
    /// <summary>Arrive at this cell instead of the spawn (back through a town portal).</summary>
    public (int X, int Y)? ArriveAt;
    /// <summary>Where ReturnPortal leads: 0 the hideout (a map's own portal, or a Portal Scroll's in a map), else a town's
    /// WorldAreas HASH16 (a Portal Scroll's anywhere else).</summary>
    public ushort ReturnPortalTo;

    /// <summary>The 0x8f entries' model byte (/map_portal sets it for experiments).</summary>
    public byte MapPortalStyle;
    // Spawned monsters by object id, in spawn order (for /mobs and /despawn).
    public readonly SortedDictionary<uint, SpawnedMonster> Monsters = [];
    // Items on the ground by object id (dropped with C→S 0x29).
    /// <summary>The area instance's state shared across connections: its ground items.</summary>
    public InstanceState Instance { get; init; } = null!;
    /// <summary>This connection placed or restored the instance's monsters, so it saves them when it closes.</summary>
    public bool MonstersPlaced;
    public volatile bool AiEnabled = true, Closed;
    // Whether the lockstep warning was shown on this connection.
    public bool WarnedLockstep;
    // The character's activated waypoints (WorldAreas Ids, kept in its save) and whether this connection was reached
    // by waypoint travel (the player then appears next to the area's waypoint).
    public readonly HashSet<string> Waypoints = [];
    // The areas the character has entered (WorldAreas Ids, kept in its save): their visit quest flags.
    public readonly HashSet<string> Visited = [];
    public bool ArriveAtWaypoint;

    /// <summary>A walk estimate for a server object on this area's walk map.</summary>
    public WalkEstimator NewWalk(int x, int y, double speed) =>
        new(x, y, Math.Max(1, speed), Layout.Walkable, Layout.GridW > 0 ? Layout.GridW : 4096, Layout.GridH > 0 ? Layout.GridH : 4096);

    public (int X, int Y) SpawnNear((int X, int Y) at) => Layout.Near(at);
}

/// <summary>A worn pet the server walks after the player (RunPetFollow): its walk estimate, which side of the player it
/// keeps (+1 or −1) and where it was last sent.</summary>
sealed class SpawnedPet(WalkEstimator walk, int side)
{
    public WalkEstimator Walk { get; } = walk;
    public int Side { get; } = side;
    public (int X, int Y)? Target { get; set; }
}
