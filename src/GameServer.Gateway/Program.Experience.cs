using System.Collections.Concurrent;
using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using Progression = GameServer.Core.World.Progression;

partial class Program
{
    internal sealed class CharacterLevelUpdates
    {
        public readonly Lock Gate = new();
        public readonly SemaphoreSlim Auth = new(1, 1);
        public long Revision;
    }

    internal static readonly ConcurrentDictionary<string, CharacterLevelUpdates> LevelUpdates = new(StringComparer.OrdinalIgnoreCase);
    internal static CharacterLevelUpdates LevelUpdatesFor(ClientState conn) => LevelUpdates.GetOrAdd(CharacterName(conn), _ => new());

    internal sealed record ExperienceChange(long Revision, uint Before, uint After, bool LevelChanged, bool Reset = false);

    // Call while holding the connection lock; an accepted death must not gain XP or publish a later level-up heal.
    internal static bool HardcoreProgressBlocked(ClientState conn) =>
        conn.Hardcore && (conn.PlayerLife == 0 || conn.HardcoreDeathRecorded);

    // All production gains/resets serialize the shared XP ledger before publishing a connection copy.
    // Lock order is character XP -> connection -> database. No monitor is held across network waits.
    internal static ExperienceChange? RecordExperienceGain(ClientState conn, uint gain)
    {
        var updates = LevelUpdatesFor(conn);
        lock (updates.Gate)
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return null;
            var rules = LeagueFor(conn).Rules;
            var (before, after) = CharacterSaves.AddExperience(CharacterName(conn), conn.Experience,
                // An older area may have stale pools. Scale against the shared ledger's current level, not that copy.
                xp => Progression.AddExperience(xp, LeagueRules.Scale(gain,
                    rules.ExperienceMultiplier(Progression.LevelFor(xp, CharacterStats.LevelStarts))), CharacterStats.LevelStarts));
            if (after == before) return null;
            bool levelChanged = Progression.LevelFor(after, CharacterStats.LevelStarts) != conn.Pools.Level;
            if (levelChanged) CharacterSaves.SaveExperience(CharacterName(conn));
            conn.Experience = after;
            return new(++updates.Revision, before, after, levelChanged);
        }
    }

    internal static ExperienceChange RecordExperienceReset(ClientState conn, int level)
    {
        var updates = LevelUpdatesFor(conn);
        lock (updates.Gate)
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn))
            {
                uint held = CurrentExperience(conn);
                return new(updates.Revision, held, held, false);
            }
            uint before = CurrentExperience(conn), after = CharacterStats.ExperienceFor(level);
            CharacterSaves.SetExperience(CharacterName(conn), after);
            conn.Experience = after;
            return new(++updates.Revision, before, after, true, Reset: true);
        }
    }

    internal static uint CurrentExperience(ClientState conn) =>
        CharacterSaves.Load(CharacterName(conn))?.Experience ?? conn.Experience;

    internal static async Task<bool> PublishExperienceChange(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, ExperienceChange change, string why)
    {
        var updates = LevelUpdatesFor(conn);
        await Send(remote, stream, ciphers, [], $"0x02ba current player experience ({why})", atGate: () =>
        {
            lock (updates.Gate)
            lock (conn.Monsters)
            {
                if (HardcoreProgressBlocked(conn)) return null;
                conn.Experience = CurrentExperience(conn);
                return InstanceEntities.PlayerExperienceUpdate(PlayerId, conn.Experience);
            }
        });
        int before, level;
        lock (updates.Gate)
        lock (conn.Monsters)
        {
            if (HardcoreProgressBlocked(conn)) return false;
            // Old-area gains share XP, not runtime pools. Even an obsolete event must reconcile this connection
            // from current XP; rejecting it outright could leave the live connection at an older level.
            conn.Experience = CurrentExperience(conn);
            level = Progression.LevelFor(conn.Experience, CharacterStats.LevelStarts);
            bool currentReset = change.Reset && change.Revision == updates.Revision;
            if (!currentReset && level == conn.Pools.Level) return false;
            before = CommitExperienceLevel(conn, level);
        }
        await PublishLevelChange(remote, stream, ciphers, conn, before, level);
        return true;
    }

    internal static async Task CompleteExperienceGain(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, ExperienceChange change, string why, Func<ControlRequest, Task<ControlResponse>>? control = null)
    {
        bool applied = await PublishExperienceChange(remote, stream, ciphers, conn, change, why);
        if (!change.LevelChanged && !applied) return;
        lock (conn.Monsters)
            if (HardcoreProgressBlocked(conn)) return;
        var result = await SynchronizeAccountLevel(conn, control);
        if (!result.Ok) Console.WriteLine($"[gateway] {remote} current level not saved to the account: {result.Error}");
    }

    // Auth has unconditional set_level writes. Serialize every Gateway producer for this character, including resets
    // and login repair, and correct an in-flight request if XP changed before its answer. Answers never apply a level.
    internal static async Task<ControlResponse> SynchronizeAccountLevel(ClientState conn,
        Func<ControlRequest, Task<ControlResponse>>? control = null)
    {
        var updates = LevelUpdatesFor(conn);
        await updates.Auth.WaitAsync();
        try
        {
            while (true)
            {
                int level;
                lock (updates.Gate)
                lock (conn.Monsters)
                {
                    CharacterSaves.SaveExperience(CharacterName(conn));
                    level = Progression.LevelFor(CurrentExperience(conn), CharacterStats.LevelStarts);
                }
                var request = new ControlRequest { Op = "set_level", Ticket = conn.Ticket, Level = level };
                var result = await (control is null ? InstanceControl.CallAsync(request) : control(request));
                lock (updates.Gate)
                lock (conn.Monsters)
                    if (level == Progression.LevelFor(CurrentExperience(conn), CharacterStats.LevelStarts)) return result;
            }
        }
        finally { updates.Auth.Release(); }
    }
}
