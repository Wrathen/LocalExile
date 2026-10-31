using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // Check the current caster and this instance's link group before native use/cooldown publication.
    // Keep accepted Warcry/RF behavior and each caller's original cancellation/ownership predicate.
    static bool OwnAuraAcknowledgementAllowed(ClientState conn, int effect, ushort skillId, Func<bool> nativeUse)
    {
        lock (conn.Monsters)
            return !OwnAuraSkillRestricted(conn, effect, skillId) && nativeUse();
    }

    // Raw caster sources only: never CharacterPlayerStats, PlayerSkills, aura bonuses or PoolsFor.
    static int OwnAuraRestrictionValue(ClientState conn, Dictionary<uint, int>? sources = null)
    {
        lock (conn.Monsters)
        {
            sources ??= CharacterStatSources(conn).Stats;
            UtilityFlasks.AddRawStats(sources,
                conn.UtilityFlaskEffects.Stats().Concat(conn.FlaskGrounds.PlayerStats()), Stats);
            return OwnAuraRestriction.Value(sources, conn.PlayerStats);
        }
    }

    // your_spells_are_disabled (Devout of Gruthkul) from the same raw caster sources; a developer value replaces them.
    // The client greys such a skill itself: skill_disabled_by_stat is 0x1520 AND casting_spell (FUN_1428cf770).
    static bool SpellsDisabled(ClientState conn, Dictionary<uint, int>? sources = null)
    {
        if (Stats.Key("your_spells_are_disabled") is not uint key) return false;
        lock (conn.Monsters)
            return (conn.PlayerStats.TryGetValue(key, out int set) ? set
                : (sources ?? CharacterStatSources(conn).Stats).GetValueOrDefault(key)) != 0;
    }

    // What the client counts as a spell for that restriction: the casting_spell flag SkillCatalog publishes.
    static bool CastingSpell(ClientState conn, int effect) => SkillsFor(conn).IsSpell(effect) && !SkillsFor(conn).IsAttack(effect);

    // Auras contains only the current ordinary self-aura executor's source/recipient pairs.
    // Received buffs, RF, flasks and other temporary effects have separate ownership and are untouched.
    // Local policy: suspend runtime effects, retaining ActiveAuras intent; this is not a proven GGG lifecycle.
    // Disabled spells suspend the auras that are spells (every supported one) the same way.
    static bool SuspendRestrictedOwnAuras(ClientState conn, Dictionary<uint, int>? sources = null)
    {
        if (conn.Auras.Count == 0) return false;
        lock (conn.Monsters) sources ??= CharacterStatSources(conn).Stats;
        bool all = OwnAuraRestrictionValue(conn, sources) != 0, spells = !all && SpellsDisabled(conn, sources);
        var gone = conn.Auras.Values.Where(a => all || spells && CastingSpell(conn, a.Effect)).ToArray();
        if (gone.Length == 0) return false;
        foreach (var aura in gone) conn.Auras.Remove(aura.SkillId);
        conn.AuraStatsDirty = true;
        return true;
    }

    static void EnsureOwnAuraPools(ClientState conn)
    {
        lock (conn.Monsters)
            if (SuspendRestrictedOwnAuras(conn))
            {
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.ApplyMovementSpeed();
                conn.PlayerMana = Math.Min(conn.PlayerMana,
                    (uint)AuraCatalog.UnreservedMana(conn.Pools, conn.Auras.Values));
            }
    }

    static void ReconcileOwnAuras(ClientState conn, bool validateSkills = false)
    {
        EnsureOwnAuraPools(conn);
        // Revalidate the source skill as well as the restriction before queued activation/entry adds.
        // Dirty runtime or presented instances are owned by this lifecycle; pure unpresented pool inputs are not adds.
        if (conn.Auras.Count > 0 && (validateSkills || conn.AuraStatsDirty || conn.PresentedAuras.Count > 0))
        {
            var available = GemSkillEffects(conn).ToDictionary(g => g.Id, g => g.Effect);
            var gone = conn.Auras.Values.Where(a => InTown(conn)
                || !available.TryGetValue(a.SkillId, out int effect) || effect != a.Effect).ToArray();
            foreach (var aura in gone) conn.Auras.Remove(aura.SkillId);
            if (gone.Length > 0)
            {
                conn.AuraStatsDirty = true;
                conn.Pools = PoolsFor(conn, conn.Pools.Level);
                conn.ApplyMovementSpeed();
                conn.PlayerMana = Math.Min(conn.PlayerMana,
                    (uint)AuraCatalog.UnreservedMana(conn.Pools, conn.Auras.Values));
            }
        }
        if (!conn.Closed && !conn.LeavingArea && !conn.RageRetired && conn.PlayerLife > 0 && !InTown(conn))
            RestoreRememberedAuras(conn, CharacterSaves.Load(CharacterName(conn))?.ActiveAuras ?? []);
    }

    // Called under the send gate and Monsters lock. Diff against what actually reached this object,
    // rather than prebuilding adds/removes across an await. Old instances end once; new ones start once.
    // Reconcile before constructing any player/Actor/mana snapshot, including another mechanic's refresh.
    static byte[] WithOwnAuraChanges(ClientState conn, Func<byte[]> snapshot, bool replay = false)
    {
        ReconcileOwnAuras(conn, validateSkills: replay);
        // LoadProgress can restore intent before the object exists. Keep aura publication pending;
        // in particular never prefix a creation snapshot with buffs, Stats, Actor or Life updates.
        if (!conn.PlayerObjectCreated && !conn.PlayerSent) return snapshot();
        var packets = new List<byte[]>();
        bool Same(AuraCatalog.Aura a, AuraCatalog.Aura b) => a.Buff == b.Buff && a.SourceBuff == b.SourceBuff;
        foreach (var old in conn.PresentedAuras.Values)
            if (!conn.Auras.TryGetValue(old.SkillId, out var current) || !Same(old, current))
                packets.AddRange(AuraCatalog.EndPackets(old, PlayerId));
        foreach (var id in conn.PresentedAuras.Keys.ToArray())
            if (!conn.Auras.TryGetValue(id, out var current) || !Same(conn.PresentedAuras[id], current))
                conn.PresentedAuras.Remove(id);
        if (conn.AuraStatsDirty || replay)
            foreach (var aura in conn.Auras.Values)
                if (!conn.PresentedAuras.ContainsKey(aura.SkillId))
                {
                    packets.AddRange(AuraBuffs(aura));
                    conn.PresentedAuras[aura.SkillId] = aura;
                }
        if (conn.AuraStatsDirty)
        {
            conn.AuraStatsDirty = false;
            packets.Add(PlayerStatsUpdateAtGate(conn, PlayerStatsWithCleared(conn)));
            packets.Add(InstanceEntities.PlayerSkillsChange(PlayerId, PlayerSkills(conn), []));
            packets.Add(InstanceProtocol.LifeUpdate(PlayerId, conn.PlayerMana,
                (uint)ReservedMana(conn), pool: ManaPool));
        }
        packets.Add(snapshot());
        return [.. packets.SelectMany(p => p)];
    }

    static Task SendOwnAuraChanges(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        bool replay = false) => Send(remote, stream, ciphers, [], "own aura reconciliation", atGate: () =>
        {
            lock (conn.Monsters)
                return conn.Closed || conn.LeavingArea || conn.RageRetired
                    ? null : WithOwnAuraChanges(conn, () => [], replay);
        });
}
