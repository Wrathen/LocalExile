using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    static SingleUseCooldown? CooldownFor(ClientState conn, int effect, int level, int quality) =>
        SkillsFor(conn).CooldownMs(effect, level) <= 0 || GemSkillEffects(conn).Count(g => g.Effect == effect) != 1 ? null :
            SingleUseCooldown.ForSkill(SkillsFor(conn), Stats, effect, level, CharacterPlayerStats(conn),
                SkillsFor(conn).EffectStats(effect, level, quality).ToDictionary(s => s.Item1, s => s.Item2));

    static SingleUseCooldownState CooldownStateLocked(ClientState conn, int effect, SingleUseCooldown plan, double now)
    {
        if (!conn.SingleUseCooldowns.TryGetValue(effect, out var state))
        {
            double legacyRemaining = Math.Max(0, conn.SkillReadyAt.GetValueOrDefault(effect) - now);
            state = new(now, plan.RecoveryRate, double.IsFinite(legacyRemaining)
                ? legacyRemaining * 1000 * (plan.RecoveryRate > 0 ? plan.RecoveryRate : 1) : plan.BaseMilliseconds,
                plan.BaseMilliseconds);
            conn.SingleUseCooldowns[effect] = state;
        }
        state.Refresh(now, plan.RecoveryRate);
        return state;
    }

    // Readiness, reservation and action side effects are one short critical section, before any echo/await.
    // An approach checks readiness without spending; arrival must reserve under its original action token.
    internal static bool TryBeginCooldownUse(ClientState conn, int effect, SingleUseCooldown? plan, bool instant,
        Action sideEffects, out CooldownUseTicket? ticket, int? actionToken = null, bool reserve = true, double? requestedAt = null)
        => TryBeginCooldownUseCore(conn, effect, plan, instant, sideEffects, out ticket,
            actionToken, reserve, requestedAt, null);

    internal static bool TryBeginSkillCooldownUse(ClientState conn, ushort skillId, int effect,
        SingleUseCooldown? plan, bool instant, Action sideEffects, out CooldownUseTicket? ticket,
        int? actionToken = null, bool reserve = true, double? requestedAt = null) =>
        TryBeginCooldownUseCore(conn, effect, plan, instant, sideEffects, out ticket,
            actionToken, reserve, requestedAt, skillId);

    static bool TryBeginCooldownUseCore(ClientState conn, int effect, SingleUseCooldown? plan, bool instant,
        Action sideEffects, out CooldownUseTicket? ticket, int? actionToken, bool reserve, double? requestedAt,
        ushort? skillId)
    {
        using (ProjectileMutation(conn))
        {
            ticket = null;
            SingleUseCooldownState? state = null;
            if (conn.Closed || conn.LeavingArea || conn.RageRetired || conn.PlayerLife == 0 || conn.Paused
                || requestedAt is double asked && PlayerActionBlocked(conn, asked, conn.Clock.Elapsed.TotalSeconds)
                || actionToken is int original && conn.ActionCount != original) return false;
            // Covers object/self and instant routes before creating or spending cooldown state or a new action.
            if ((skillId is ushort id ? ActorSkillInstanceDisabled(conn, effect, id)
                : ActorSkillDisabled(conn, effect)) is not null) return false;
            VaalUseReservation? vaal = null;
            if (skillId is ushort requested && GemSkillEffects(conn)
                .Any(g => g.Id == requested && SkillsFor(conn).VaalBudget(g.Effect, g.Level) is not null))
            {
                // No action/movement, cooldown reservation or payment for an unsupported/underfunded Vaal use.
                if (!VaalExecutor(conn, effect) || (vaal = PlanVaalUse(conn, requested)) is null || !CanPayVaalUse(conn, vaal))
                    return false;
            }
            if (plan is { } cooldown)
            {
                double now = conn.Clock.Elapsed.TotalSeconds;
                state = CooldownStateLocked(conn, effect, cooldown, now);
                if (reserve ? !state.TryUse(now, cooldown) : !state.Ready) return false;
                conn.SkillReadyAt[effect] = now + state.RemainingSeconds;
            }
            sideEffects();
            if (reserve && vaal is not null) CommitVaalUse(conn, vaal);
            if (plan is not null) ticket = new(effect, conn.ActionCount, conn.ProjectileEpoch, instant, reserve,
                state, state!.UseVersion);
            return true;
        }
    }

    static void RefreshSingleUseCooldowns(ClientState conn)
    {
        lock (conn.Monsters) if (conn.SingleUseCooldowns.Count == 0) return;
        var player = CharacterPlayerStats(conn);
        var plans = GemSkillEffects(conn).GroupBy(g => g.Effect).ToDictionary(g => g.Key, g =>
            g.Count() != 1 ? null : SingleUseCooldown.ForSkill(SkillsFor(conn), Stats, g.Key, g.First().Level, player,
                SkillsFor(conn).EffectStats(g.Key, g.First().Level, g.First().Quality).ToDictionary(s => s.Item1, s => s.Item2)));
        lock (conn.Monsters)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            foreach (var (effect, state) in conn.SingleUseCooldowns.ToArray())
            {
                if (!plans.TryGetValue(effect, out var plan)) continue; // Removal does not reset a spent use.
                state.Refresh(now, plan?.RecoveryRate ?? state.Rate);
                conn.SkillReadyAt[effect] = now + state.RemainingSeconds;
                if (plan is null) conn.SingleUseCooldowns.Remove(effect); // Unsupported mechanics retain the legacy path.
            }
        }
    }

    internal static byte[]? NativeCooldownRefresh(ClientState conn, int epoch,
        Func<IReadOnlyList<(ushort Id, int Effect)>> currentSkills)
    {
        lock (conn.Monsters)
        {
            if (!conn.ProjectileActive(epoch) || conn.PlayerLife == 0) return null;
            double now = conn.Clock.Elapsed.TotalSeconds;
            var updates = new List<SingleUseCooldownUpdate>();
            var synchronized = new List<SingleUseCooldownState>();
            foreach (var (id, effect) in currentSkills())
            {
                if (!conn.ClientSkillIds.Contains(id) || !conn.SingleUseCooldowns.TryGetValue(effect, out var state)
                    || state.NativeSkillId != id) continue;
                state.Refresh(now, state.Rate);
                if (!state.NeedsNativeRefresh) continue;
                updates.Add(new(id, state.Ready ? 0 : (float)(state.RemainingBaseMilliseconds / 1000),
                    state.TotalBaseMilliseconds / 1000f, (float)state.Rate));
                synchronized.Add(state);
            }
            if (updates.Count == 0) return null;
            var bytes = InstanceEntities.PlayerSingleUseCooldowns(PlayerId, updates);
            foreach (var state in synchronized) state.NativeRefreshed();
            return bytes;
        }
    }

    static Task SendNativeCooldownRefresh(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        lock (conn.Monsters)
            if (!conn.SingleUseCooldowns.Values.Any(s => s.NeedsNativeRefresh)) return Task.CompletedTask;
        int epoch;
        lock (conn.Monsters) epoch = conn.ProjectileEpoch;
        return Send(remote, stream, ciphers, [], "0x02ba player active cooldown progress/rate refresh",
            atGate: () => NativeCooldownRefresh(conn, epoch, () => GemSkillEffects(conn)
                .Where(g => CooldownFor(conn, g.Effect, g.Level, g.Quality) is not null)
                .Select(g => (g.Id, g.Effect)).ToArray()));
    }
}
