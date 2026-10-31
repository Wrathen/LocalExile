using GameServer.Core.Accounts;
using System.Collections.Concurrent;
using GameServer.Core.Diagnostics;

partial class Program
{
    /// <summary>The character's league: Standard, or its module's (Modding/ServerLeagues.cs), decided once per connection.</summary>
    internal static LeagueRuntime LeagueFor(ClientState conn) =>
        conn.LeagueModule ??= ServerLeagues.Resolve(conn.LeagueForm, conn.League, LeagueCatalog.Startup, CoreModules);

    // The game files' two sets of data; a league picks one (league.json "dataSet").
    internal static readonly Lazy<ItemCatalog> HardModeItems = new(() => new("data", hardMode: true));
    internal static readonly Lazy<SkillCatalog> HardModeSkills = new(() => new("data", hardMode: true));
    internal static readonly Lazy<PassiveTree> HardModePassives = new(() => new("data", hardMode: true));
    internal static readonly Lazy<ItemRolls> HardModeRolls = new(() => new(HardModeItems.Value));
    internal static readonly Lazy<UniqueCatalog> HardModeUniques = new(() => new(HardModeItems.Value));
    internal static readonly Lazy<EssenceCrafting> HardModeEssences = new(() => new(HardModeItems.Value, HardModeRolls.Value));
    // Drops read the league's gem rules when they are built, so each HardMode league has its own.
    internal static readonly ConcurrentDictionary<string, Lazy<ItemDrops>> HardModeDrops = new();

    internal static ItemCatalog ItemsFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModeItems.Value : Items;
    internal static SkillCatalog SkillsFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModeSkills.Value : Skills;
    internal static PassiveTree PassivesFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModePassives.Value : Passives;
    internal static ItemRolls RollsFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModeRolls.Value : Rolls;
    internal static UniqueCatalog UniquesFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModeUniques.Value : Uniques;
    internal static EssenceCrafting EssencesFor(ClientState conn) => LeagueFor(conn).HardMode ? HardModeEssences.Value : Essences;
    internal static ItemDrops DropsFor(ClientState conn) => LeagueFor(conn) is { HardMode: true } league
        ? HardModeDrops.GetOrAdd(league.Id, _ => new(() => new(HardModeItems.Value, HardModeRolls.Value, hardMode: true, uniques: HardModeUniques.Value, league: league.Rules))).Value : Drops;

    internal sealed class VitalsAdmission
    {
        public readonly Lock Gate = new();
        public Guid Latest;
    }

    // Registration precedes expensive progress preparation. An older preparation cannot claim resources after a
    // newer connection was admitted. Entries are removed when their latest connection finishes, including refusals.
    internal static readonly ConcurrentDictionary<(string Database, Guid Character), VitalsAdmission> VitalsAdmissions = new();
    internal static (string, Guid)? VitalsAdmissionKey(ClientState conn) => conn.CharacterId is { } id
        ? (Path.GetFullPath(Saves.Db.Path).ToUpperInvariant(), id) : null;

    internal static void RegisterVitalsAdmission(ClientState conn)
    {
        if (!LeagueFor(conn).Rules.PersistsVitals) return;
        lock (conn.Monsters)
        {
            var key = VitalsAdmissionKey(conn)
                ?? throw new InvalidOperationException("Saved life and mana require an admitted character identity");
            while (true)
            {
                var admission = VitalsAdmissions.GetOrAdd(key, _ => new());
                lock (admission.Gate)
                {
                    if (!VitalsAdmissions.TryGetValue(key, out var current) || !ReferenceEquals(current, admission)) continue;
                    admission.Latest = conn.VitalsSessionId;
                    break;
                }
            }
        }
    }

    internal static void InitializeCharacterVitals(ClientState conn)
    {
        lock (conn.Monsters)
        {
            uint maxLife = conn.PlayerMaxLife, maxMana = (uint)UnreservedMana(conn);
            (uint Life, uint Mana) initial;
            if (!LeagueFor(conn).Rules.PersistsVitals) initial = InitialVitals(false, null, maxLife, maxMana);
            else
            {
                var key = VitalsAdmissionKey(conn);
                if (key is null || !VitalsAdmissions.TryGetValue(key.Value, out var admission))
                    throw new InvalidOperationException("Saved-vitals admission was not registered");
                lock (admission.Gate)
                {
                    if (admission.Latest != conn.VitalsSessionId)
                        throw new InvalidOperationException("A newer connection superseded this resource admission");
                    initial = CharacterSaves.BeginVitalsSession(CharacterName(conn), key.Value.Item2,
                        conn.VitalsSessionId, conn.League, save => InitialVitals(true, save, maxLife, maxMana));
                }
            }
            conn.PlayerLife = initial.Life;
            conn.PlayerMana = initial.Mana;
            conn.EnergyShield.SetMaximum(conn.Pools.EnergyShield, refill: true);
            conn.AuraDeathHandled = false;
        }
    }

    internal static void ForgetVitalsAdmission(ClientState conn)
    {
        if (!LeagueFor(conn).Rules.PersistsVitals || VitalsAdmissionKey(conn) is not { } key
            || !VitalsAdmissions.TryGetValue(key, out var admission)) return;
        lock (admission.Gate)
            if (admission.Latest == conn.VitalsSessionId)
                VitalsAdmissions.TryRemove(new KeyValuePair<(string, Guid), VitalsAdmission>(key, admission));
    }

    internal static void PersistVitals(ClientState conn, bool bestEffort = false)
    {
        if (!LeagueFor(conn).Rules.PersistsVitals) return;
        try
        {
            lock (conn.Monsters)
                if (!conn.LeavingArea && conn.CharacterId is { } id)
                    CharacterSaves.SaveVitals(CharacterName(conn), id, conn.VitalsSessionId, conn.League,
                        conn.PlayerLife, conn.PlayerMana);
        }
        catch (Exception e) when (bestEffort)
        {
            Console.WriteLine($"[vitals] {CharacterName(conn)}: resource pools not saved; will retry: {ServerLog.Describe(e)}");
        }
    }

    // Save and retire before Auth can issue the destination ticket. Neither periodic nor disconnect saves from the
    // source may follow that handoff. An Auth refusal can resume only this same retired token, never a newer owner.
    internal static bool RetireVitalsForTransfer(ClientState conn)
    {
        if (!LeagueFor(conn).Rules.PersistsVitals) return true;
        if (conn.CharacterId is not { } id) return false;
        lock (conn.Monsters)
            return CharacterSaves.SaveVitals(CharacterName(conn), id, conn.VitalsSessionId, conn.League,
                conn.PlayerLife, conn.PlayerMana, retire: true);
    }

    internal static void ResumeVitalsAfterFailedTransfer(ClientState conn)
    {
        if (!LeagueFor(conn).Rules.PersistsVitals || conn.CharacterId is not { } id) return;
        lock (conn.Monsters)
            CharacterSaves.ResumeVitalsSession(CharacterName(conn), id, conn.VitalsSessionId, conn.League,
                conn.PlayerLife, conn.PlayerMana);
    }

    internal static (uint Life, uint Mana) InitialVitals(bool persists, CharacterSaves.Save? saved, uint maxLife, uint maxMana)
    {
        // New and legacy saves have no pools. A softcore death/relogin revives; HC death is refused before this point.
        bool alive = saved?.Life is > 0;
        return persists && alive ? (Math.Min(saved!.Life!.Value, maxLife), Math.Min(saved.Mana ?? maxMana, maxMana))
            : (maxLife, maxMana);
    }

    internal static IReadOnlyList<(uint Id, uint Life)> RecoverBossesOnDeath(ClientState conn)
    {
        lock (conn.Monsters)
        {
            var settings = LeagueFor(conn).Rules;
            if (settings.BossLifeRecoveryFraction <= 0 || conn.PlayerLife != 0 || conn.BossRecoveryHandled) return [];
            conn.BossRecoveryHandled = true;
            if (!settings.BossRecoveryAreaPrefixes.Any(p => conn.Layout.Id.StartsWith(p, StringComparison.Ordinal))) return [];
            double now = conn.Clock.Elapsed.TotalSeconds;
            var at = conn.Walk.Position(now);
            List<(uint, uint)> changed = [];
            foreach (var (id, monster) in conn.Monsters)
                if (!monster.Dead && monster.Life > 0 && monster.Variety?.BossHealthBar == true
                    && Dist(monster.Walk.Position(now), at) <= ExperienceRange)
                {
                    uint life = LeagueGameplay.BossLifeAfterDeath(monster.Life, monster.MaxLife, settings.BossLifeRecoveryFraction);
                    if (life == monster.Life) continue;
                    monster.Life = life;
                    changed.Add((id, life));
                }
            return changed;
        }
    }
}
