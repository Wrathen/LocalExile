using System.Net.Sockets;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The original action guards release only. Once created, an arrow or wand bolt owns the area's flight lifetime.
    // A frozen player (PlayerElementalAilments) or hit recovery cancels an unreleased shot, as a newer action does.
    internal static bool OrdinaryRangedReleaseCurrent(ClientState conn, int action, int epoch)
    {
        lock (conn.Monsters)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            return conn.ProjectileActive(epoch) && !conn.RageRetired && conn.PlayerLife > 0 && conn.ActionCount == action
                && conn.ElementalAilments.ActionFactor(now) > 0 && !conn.HitRecovery.Active(now);
        }
    }

    // A pause can win the send gate after an action was accepted. Keep its echo pending rather than releasing an
    // unconfirmed arrow after resume; a later action, death or transfer still cancels it with its original token.
    internal static async Task<bool> SendOrdinaryRangedAction(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, byte[] bytes, string? log, int action, int epoch)
    {
        bool sent = false;
        while (!sent && OrdinaryRangedReleaseCurrent(conn, action, epoch))
        {
            await Send(remote, stream, ciphers, bytes, log, still: () =>
            {
                lock (conn.Monsters) return sent = OrdinaryRangedReleaseCurrent(conn, action, epoch) && !conn.Paused;
            });
            if (!sent) await Task.Delay(16);
        }
        return sent;
    }

    // The runtime schedules the release first, anchored to the accepted input rather than to the send gate; the echo
    // follows. The release guard rechecks the action token, so a cancelled or replaced action creates no projectile.
    internal static async Task ConfirmAndLaunchOrdinaryRanged(string remote, NetworkStream stream, ConnectionCiphers ciphers,
        ClientState conn, byte[] actionBytes, string actionLog, (int X, int Y) aim, double attackSeconds, int action, int epoch,
        string projectileId, ushort sequence, SkillCriticalRoll criticalRoll)
    {
        await LaunchOrdinaryRanged(remote, stream, ciphers, conn, aim, attackSeconds, action, epoch, projectileId, sequence, criticalRoll);
        await SendOrdinaryRangedAction(remote, stream, ciphers, conn, actionBytes, actionLog, action, epoch);
    }

    // Bows release at attack1_bow's contact. A wand's main-hand clip is the one the native ACT selects for this input's
    // sequence (AnimationVariantSelector), so its contact comes from that clip rather than attack1's keyframe.
    internal static double OrdinaryRangedContactDelay(ClientState conn, double attackSeconds, string projectileId,
        ushort sequence) => OrdinaryRangedContact(conn, attackSeconds, projectileId, sequence).Delay;

    /// <summary>When and from where an ordinary bow or wand shot leaves: the delay to the releasing clip's contact
    /// keyframe, and that keyframe's position in the model's space (the Ranger's attack1_bow: 59.8 units in front at
    /// 127.5 up; the ACT-selected wand clip's tip), or null without rig data for the class or weapon.</summary>
    internal static (double Delay, ContactPoint? Point) OrdinaryRangedContact(ClientState conn, double attackSeconds,
        string projectileId, ushort sequence)
    {
        string ao = Environment.GetEnvironmentVariable("Game_PLAYER_AO") ?? Skills.ClassFor(conn.ClassRow).AnimatedObject;
        var profile = WeaponProfileFor(conn, activeSet: true);
        if (projectileId == OrdinaryRangedAttack.Wand)
        {
            if (Animations.TryDefaultWandContact(ao, profile, sequence, out var selected))
            {
                double delay = Math.Max(0, attackSeconds * selected.ContactFraction);
                Console.WriteLine($"[gateway] wand release sequence 0x{sequence:x4}: {selected.Stance}/{selected.Animation} " +
                    $"(clip {selected.AnimationIndex}, contact {selected.ContactMs}/{selected.LengthMs} ms), " +
                    $"attack {attackSeconds:0.000000} s, release after {delay:0.000000} s");
                return (delay, selected.Point);
            }
            Console.WriteLine($"[gateway] WARNING: missing native wand variation metadata for {ao}; using approximate attack1 release");
        }
        return (AttackContactDelay(conn, attackSeconds, approach: false), Animations.AttackContactPoint(ao, profile));
    }

    // Ranged release has no walk-in offset or melee early-hit clamp: the shot is scheduled on the projectile runtime
    // at the weapon's contact and resolved there (Projectiles/Program.ProjectileRanged.cs). The use's critical roll was
    // captured at acceptance and travels with the shot.
    internal static Task LaunchOrdinaryRanged(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        (int X, int Y) aim, double attackSeconds, int action, int epoch, string projectileId, ushort sequence,
        SkillCriticalRoll criticalRoll) =>
        ScheduleOrdinaryRanged(remote, stream, ciphers, conn, aim, attackSeconds, action, epoch, projectileId, sequence, criticalRoll);
}
