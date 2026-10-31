using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    internal sealed record SoulTransfer(string Character, DateTime At, VaalSoulState.Carried State);
    internal static readonly ConcurrentDictionary<uint, SoulTransfer> CarriedVaalSouls = new();

    // Only the proved native getter inputs are projected. Explicit development values win, including zero.
    internal static int VaalSoulInput(ClientState conn, IReadOnlyDictionary<uint, int> player,
        IReadOnlyDictionary<uint, int> own, string name)
    {
        if (Stats.Key(name) is not uint key) return 0;
        lock (conn.Monsters)
        {
            if (conn.PlayerStats.TryGetValue(key, out int dev)) return dev;
            // The selector describes this skill, not the caster. Only an explicit developer replacement is global.
            if (name == VaalSoulCost.Aura) return own.GetValueOrDefault(key);
            return (int)Math.Clamp((long)player.GetValueOrDefault(key) + own.GetValueOrDefault(key), int.MinValue, int.MaxValue);
        }
    }

    internal static void PublishVaalSoulInputs(ClientState conn, IReadOnlyDictionary<uint, int> player,
        IReadOnlyDictionary<uint, int> own, Dictionary<uint, int> stats)
    {
        foreach (string name in VaalSoulCost.Inputs)
            if (Stats.Key(name) is uint key) stats[key] = VaalSoulInput(conn, player, own, name);
    }

    // Reconcile before an await as well as at the send gate: a queued removal must lose its balance even if readded
    // before publication. Requirements/inactive equipment are refreshed by the normal character stat seam.
    internal static void RefreshVaalSouls(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.VaalSoulsRetired || conn.Closed) return;
            var player = CharacterPlayerStats(conn);
            var requirements = CurrentGemRequirements(conn);
            var sources = new List<(uint, uint)>();
            foreach (var gem in GemSkillEffectsAt(conn.Character, requirements.Places, requirements.Inactive, LeagueFor(conn)))
            {
                if (SkillsFor(conn).VaalBudget(gem.Effect, gem.Level) is null) continue;
                var supported = SupportedSkillAt(conn, gem.Id, requirements);
                if (!supported.RequirementsMet || SkillsFor(conn).VaalBudget(gem.Effect, supported.Level) is not { } definition) continue;
                var own = supported.Stats;
                int required = VaalSoulCost.Required(definition.BaseCost,
                    name => VaalSoulInput(conn, player, own, name));
                sources.Add((definition.ActiveSkillsRow, VaalSoulCost.Capacity(required, definition.StoredUses)));
            }
            conn.VaalSouls.Reconcile(sources);
            if (conn.PlayerLife == 0) conn.VaalSouls.ClearBalance();
        }
    }

    // Called with Life=0/Dead=true in the SAME Monsters section as the lethal mutation, before notification awaits.
    // Shared hit paths do not carry an object ID; resolve actual ownership here without accepting another recipient.
    internal static void CollectVaalSoulAtDeathLocked(ClientState conn, SpawnedMonster monster) =>
        CollectVaalSoulOnKill(conn, conn.Monsters.FirstOrDefault(kv => ReferenceEquals(kv.Value, monster)).Key, monster);

    // Consume ineligible deaths too. Notifications may only publish current state; they never call this method.
    internal static bool CollectVaalSoulOnKill(ClientState conn, uint id, SpawnedMonster monster)
    {
        lock (conn.Monsters)
        {
            if (monster.Life != 0 || !monster.Dead || monster.SoulKillProcessed) return false;
            monster.SoulKillProcessed = true;
            if (!conn.Monsters.TryGetValue(id, out var current) || !ReferenceEquals(monster, current)) return false;
            if (!monster.Hostile || monster.Variety is null || conn.Character is null || conn.Closed
                || conn.LeavingArea || conn.VaalSoulsRetired || conn.RageRetired || conn.Paused
                || conn.PlayerLife == 0 || InTown(conn)) return false;
            double now = conn.Clock.Elapsed.TotalSeconds;
            if (Dist(monster.Walk.Position(now), conn.Walk.Position(now)) > ExperienceRange) return false;
            RefreshVaalSouls(conn);
            var stats = CharacterPlayerStats(conn);
            return RageStat(stats, "has_soul_gain_prevention") == 0 && conn.VaalSouls.Gain();
        }
    }

    // Native UPDATE clamps against OLD capacity. Unknown rows and increases must seed first, then restore current.
    // Empty lists preserve native entries, so every removed row needs an explicit zero update.
    internal static (VaalSoulSeed[] Seeds, VaalSoulUpdate[] Updates) VaalSoulChanges(ClientState conn)
    {
        RefreshVaalSouls(conn);
        var current = conn.VaalSouls.Read().ToDictionary(b => b.ActiveSkillsRow);
        var seeds = new List<VaalSoulSeed>();
        var updates = new List<VaalSoulUpdate>();
        foreach (var budget in current.Values.OrderBy(b => b.ActiveSkillsRow))
        {
            bool known = conn.SentVaalSouls.TryGetValue(budget.ActiveSkillsRow, out var old);
            if (known && old == budget) continue;
            if (!known || budget.Capacity > old.Capacity) seeds.Add(new(budget.ActiveSkillsRow, budget.Capacity));
            updates.Add(new(budget.ActiveSkillsRow, budget.Current, budget.Capacity));
        }
        foreach (uint row in conn.SentVaalSouls.Keys.Except(current.Keys).Order()) updates.Add(new(row, 0, 0));
        conn.SentVaalSouls.Clear();
        foreach (var (row, budget) in current) conn.SentVaalSouls.Add(row, budget);
        return ([.. seeds], [.. updates]);
    }

    internal static byte[] PlayerSkillsWithVaalSouls(ClientState conn, IReadOnlyList<GrantedSkill> skills,
        IReadOnlyList<ushort> gone)
    {
        var changes = VaalSoulChanges(conn);
        return InstanceEntities.PlayerSkillsChange(PlayerId, skills, gone, changes.Seeds, changes.Updates);
    }

    internal static byte[]? VaalSoulPresentation(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea || conn.VaalSoulsRetired) return null;
            var changes = VaalSoulChanges(conn);
            return changes.Seeds.Length == 0 && changes.Updates.Length == 0 ? null
                : InstanceEntities.PlayerSkillsChange(PlayerId, [], [], changes.Seeds, changes.Updates);
        }
    }

    internal static Task SendVaalSouls(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn) =>
        Send(remote, stream, ciphers, [], null, atGate: () => VaalSoulPresentation(conn));

    // Same transfer policy as Rage: one successful ticket owns the balance; loading time is frozen, no save schema.
    internal static void CarryVaalSouls(ClientState conn, uint ticket)
    {
        lock (conn.Monsters)
        {
            if (conn.VaalSoulsRetired || conn.Closed) return;
            RefreshVaalSouls(conn);
            var state = conn.VaalSouls.Carry();
            conn.VaalSoulsRetired = true;
            foreach (var (old, entry) in CarriedVaalSouls)
                if (DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(5)) CarriedVaalSouls.TryRemove(old, out _);
            CarriedVaalSouls.TryAdd(ticket, new(CharacterName(conn), DateTime.UtcNow, state));
        }
    }

    internal static void RestoreVaalSouls(ClientState conn)
    {
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.VaalSoulsRetired || !CarriedVaalSouls.TryGetValue(conn.Ticket, out var entry)
                || entry.Character != CharacterName(conn)) return;
            if (!CarriedVaalSouls.TryRemove(new KeyValuePair<uint, SoulTransfer>(conn.Ticket, entry))) return;
            if (DateTime.UtcNow - entry.At <= TimeSpan.FromMinutes(5) && conn.PlayerLife > 0)
                conn.VaalSouls.Restore(entry.State);
        }
    }
}
