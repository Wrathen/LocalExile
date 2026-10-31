using System.Net.Sockets;
using GameServer.Core.Diagnostics;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;
using GameServer.Core.World;

/// <summary>
/// The King in the Mists, Crux of Nothingness (WorldAreas AzmeriLeagueBoss, level 83). The arena's WorldAreas row names
/// the humanoid VoodooKingBoss2; the fight brings VoodooKingBoss3 (the treant) when the three Mystic Fetishes have been
/// activated. This controller owns both forms: the ordinary monster AI skips them so every action is serialized here.
/// <para>Data: MonsterVarieties 10150 (Boss2: 1800% life, 210% damage, 1.5 s attack, reach 15, boss bar) and the
/// VoodooKingBoss3 row (1050% life, 60% energy shield, 255% damage). At level 83 the map-boss scaling gives 45,614,731
/// life for the humanoid and about 26.6M for the treant; resistances are 50/50/50/30. The mechanics and the full skill
/// list are in Agents/docs/game/king-in-the-mists.md.</para>
/// </summary>
static class KingInMists
{
    public const string Boss2 = "Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/VoodooKingBoss2";
    public const string Boss3 = "Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/VoodooKingBoss3";
    const string PoisonRat = "Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/Critters/PoisonRat";
    const string DeathEffigy = "Metadata/Monsters/LeagueAzmeri/VoodooKingBoss/VoodooKingDeathEffigy";

    // GrantedEffects rows (poedb / MonsterVarieties GrantedEffectsKeys). The default attack is row 420.
    const int MeleeEffect = 420, VineSlamEffect = 7597, VineCascadeEffect = 7596, SpellProjectile2Effect = 3893,
        SpellProjectile3Effect = 3894, TeleportEffect = 12419, SummonEffigyEffect = 8336, SummonDeathAuraEffigyEffect = 8342,
        BugBombEffect = 1898, BugRendEffect = 10510, MazeEffect = 8341, BecomeTreantEffect = 8340;

    // Server skill ids (Actor skill list entries). They must not collide with basic actions; 0x0103 is the ordinary
    // boss stage id. The default attack is SkillCatalog.FirstSkillId.
    internal const ushort SlamSkill = 0x0104, CascadeSkill = 0x0105, TeleportSkill = 0x0107, EffigySkill = 0x0108,
        BugBombSkill = 0x0109, BugRendSkill = 0x010b, MazeSkill = 0x010c, TreantSkill = 0x010d,
        Projectile2Skill = 0x010e, Projectile3Skill = 0x010f;

    // BuffDefinitions rows: Portent Vapors and Branching Affliction. Mystic Miasma (row 2401) is documented but not
    // applied yet because the effigy aura is not modelled.
    internal const ushort PortentVaporsRow = 2409, BranchingAfflictionRow = 2371;

    public static bool IsBoss2(MonsterCatalog.Variety? variety) =>
        variety is not null && variety.Id.Equals(Boss2, StringComparison.Ordinal);

    public static bool IsBoss3(MonsterCatalog.Variety? variety) =>
        variety is not null && variety.Id.Equals(Boss3, StringComparison.Ordinal);

    public static bool IsBoss(MonsterCatalog.Variety? variety) => IsBoss2(variety) || IsBoss3(variety);

    /// <summary>The actor skill list for either form, built from the GrantedEffects rows the client must know to play the
    /// action's own animation. The ids are also the 0x1ab ids this controller sends.</summary>
    internal static IReadOnlyList<GrantedSkill>? SkillsFor(ClientState conn, MonsterCatalog.Variety variety, byte level,
        MonsterAttack attack)
    {
        if (!IsBoss(variety)) return null;
        var list = new List<GrantedSkill>();
        void Add(ushort id, int effect, MonsterAttack use)
        {
            var stats = use.Projectile is null
                ? use.SkillStats()
                : Program.MonsterProjectileSkillStats(conn, effect, level, use);
            if (Program.SkillsFor(conn).FindByRow(id, effect, level, stats) is { } skill) list.Add(skill);
        }
        Add(SkillCatalog.FirstSkillId, MeleeEffect, attack);
        if (IsBoss2(variety))
        {
            Add(SlamSkill, VineSlamEffect, Slam(attack));
            Add(CascadeSkill, VineCascadeEffect, Cascade(attack));
            Add(Projectile2Skill, SpellProjectile2Effect, Projectile2(attack));
            Add(TeleportSkill, TeleportEffect, Teleport(attack));
            Add(EffigySkill, SummonEffigyEffect, Effigy(attack));
            Add(MazeSkill, MazeEffect, NoDamage(attack, MazeSkill, 2.5));
            Add(TreantSkill, BecomeTreantEffect, NoDamage(attack, TreantSkill, 2.5));
        }
        else
        {
            Add(SlamSkill, VineSlamEffect, Slam(attack));
            Add(Projectile3Skill, SpellProjectile3Effect, Projectile3(attack));
            Add(BugBombSkill, BugBombEffect, BugBomb(attack));
            Add(BugRendSkill, BugRendEffect, BugRend(attack));
            Add(EffigySkill, SummonDeathAuraEffigyEffect, Effigy(attack));
        }
        return list;
    }

    public static void Begin(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id)
    {
        SpawnedMonster? m;
        lock (conn.Monsters) conn.Monsters.TryGetValue(id, out m);
        if (m is null || !IsBoss(m.Variety)) return;
        // A new form starts with no fetish stacks; the previous buff's own duration removes its icon.
        conn.KingPortentStacks = 0;
        conn.KingPortentUntil = double.NegativeInfinity;
        _ = new Fight(remote, stream, ciphers, conn, id, m).Run();
    }

    static MonsterAttack Slam(MonsterAttack attack) => With(attack, SlamSkill, 1.0, 22,
        ConvertPhysical(attack.Damage.Scale(1.9), 30));

    static MonsterAttack Cascade(MonsterAttack attack) => With(attack, CascadeSkill, 1.0, 52,
        ConvertPhysical(attack.Damage.Scale(1.4), 25));

    static MonsterAttack Projectile2(MonsterAttack attack) => With(attack, Projectile2Skill, 1.0, 50,
        attack.Damage.Scale(0.4).Add(DamageType.Chaos, attack.Damage[DamageType.Physical].Scale(0.15)))
        with { Projectile = new SkillCatalog.MonsterProjectileInfo("Metadata/Projectiles/AzmeriVoodooKingSpellProj", 800, Lobbed: false, 1) };

    static MonsterAttack Projectile3(MonsterAttack attack) => With(attack, Projectile3Skill, 1.0, 50,
        attack.Damage.Scale(0.45).Add(DamageType.Chaos, attack.Damage[DamageType.Physical].Scale(0.45)))
        with { Projectile = new SkillCatalog.MonsterProjectileInfo("Metadata/Projectiles/AzmeriVoodooKing3SpellProj", 800, Lobbed: false, 1) };

    static MonsterAttack Teleport(MonsterAttack attack) => NoDamage(attack, TeleportSkill, 1.0);

    static MonsterAttack Effigy(MonsterAttack attack) => NoDamage(attack, EffigySkill, 1.0);

    static MonsterAttack BugBomb(MonsterAttack attack) => With(attack, BugBombSkill, 1.0, 45,
        attack.Damage.Scale(0.55).Add(DamageType.Chaos, attack.Damage[DamageType.Physical].Scale(0.18)))
        with { Projectile = new SkillCatalog.MonsterProjectileInfo("Metadata/Projectiles/BugBomb", 400, Lobbed: true, 2) { AreaRadius = 9 } };

    static MonsterAttack BugRend(MonsterAttack attack) => With(attack, BugRendSkill, 1.0, 48,
        attack.Damage.Scale(0.8))
        with { Projectile = new SkillCatalog.MonsterProjectileInfo("Metadata/Projectiles/AzmeriVoodooKing3SpellProj", 800, Lobbed: false, 1) };

    static MonsterAttack NoDamage(MonsterAttack attack, ushort skill, double seconds) =>
        With(attack, skill, seconds, 0, DamagePacket.Empty);

    static MonsterAttack With(MonsterAttack attack, ushort skill, double seconds, double range, DamagePacket damage) =>
        attack with { SkillId = skill, Seconds = seconds, Range = range, Damage = damage, Projectile = null, Release = null };

    static DamagePacket ConvertPhysical(DamagePacket packet, int percent)
    {
        var physical = packet[DamageType.Physical];
        var converted = physical.Scale(percent / 100.0);
        return packet.Select((type, range) => type == DamageType.Physical ? range.Scale(1 - percent / 100.0) : range)
            .Add(DamageType.Chaos, converted);
    }

    sealed class Fight(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, uint id, SpawnedMonster boss)
    {
        (int X, int Y) _origin;
        int _phase = IsBoss3(boss.Variety) ? 3 : 1;
        int _altars;
        ushort? _portentBuff;
        double _nextSlam, _nextCascade, _nextProjectile, _nextTeleport, _nextEffigy, _nextBomb, _nextRend;
        bool _barShown;

        public async Task Run()
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            _origin = boss.Walk.Position(now);
            _nextTeleport = now + 8;
            _nextSlam = now + 4;
            _nextCascade = now + 6;
            _nextProjectile = now + 3;
            _nextEffigy = now + 12;
            _nextBomb = now + 6;
            _nextRend = now + 10;
            try
            {
                while (true)
                {
                    await Task.Delay(50);
                    if (!Present()) return;
                    if (conn.Paused) continue;
                    now = conn.Clock.Elapsed.TotalSeconds;
                    if (conn.KingPortentStacks > 0 && now >= conn.KingPortentUntil)
                    {
                        conn.KingPortentStacks = 0;
                        if (_portentBuff is { } expired)
                        {
                            _portentBuff = null;
                            await Program.Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(Program.PlayerId, expired),
                                "0x01c2 player Portent Vapors expires");
                        }
                    }
                    var (mx, my) = boss.Walk.Position(now);
                    var (px, py) = conn.Walk.Position(now);
                    double distance = Distance(mx, my, px, py);
                    if (!boss.Aggro)
                    {
                        if (distance > boss.AggroRange) continue;
                        lock (conn.Monsters) boss.Aggro = true;
                    }
                    if (!_barShown)
                    {
                        _barShown = true;
                        await Program.ShowBossLifeBar(remote, stream, ciphers, id, boss, conn);
                    }
                    if (now < boss.FlinchUntil || now < boss.BusyUntil || boss.StageRunning) continue;
                    if (_phase == 1) await PhaseOne(now, distance);
                    else await PhaseThree(now, distance);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The connection or area went away.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[gateway] {remote} King in the Mists 0x{id:x} failed: {ServerLog.Describe(ex)}");
            }
        }

        async Task PhaseOne(double now, double distance)
        {
            if (_altars < 3 && now >= _nextTeleport)
            {
                await TeleportToAltar(now);
                return;
            }
            if (distance <= 24 && now >= _nextSlam)
            {
                _nextSlam = now + 15;
                await Use(Slam(boss.Attack!), area: true, radius: 22, "Vine Slam");
                return;
            }
            if (distance <= 52 && now >= _nextCascade)
            {
                _nextCascade = now + 10;
                await Use(Cascade(boss.Attack!), area: true, radius: 18, "Vine Cascade");
                return;
            }
            if (distance <= 15)
            {
                await Use(boss.Attack!, area: false, radius: 0, "default attack");
                return;
            }
            if (now >= _nextProjectile)
            {
                _nextProjectile = now + 6;
                await Use(Projectile2(boss.Attack!), area: false, radius: 0, "spell projectile");
            }
        }

        async Task PhaseThree(double now, double distance)
        {
            if (distance <= 22 && now >= _nextSlam)
            {
                _nextSlam = now + 15;
                await Use(Slam(boss.Attack!), area: true, radius: 22, "Treant Slam");
                return;
            }
            if (distance <= 50 && now >= _nextBomb)
            {
                _nextBomb = now + 20;
                await Use(BugBomb(boss.Attack!), area: false, radius: 0, "Bug Bomb");
                return;
            }
            if (distance <= 50 && now >= _nextRend)
            {
                _nextRend = now + 10;
                await Use(BugRend(boss.Attack!), area: false, radius: 0, "Bugrend");
                return;
            }
            if (now >= _nextEffigy)
            {
                _nextEffigy = now + 20;
                await Summon(DeathEffigy, 3);
                return;
            }
            if (distance <= 16)
            {
                await Use(boss.Attack!, area: false, radius: 0, "default attack");
                return;
            }
            if (now >= _nextProjectile)
            {
                _nextProjectile = now + 6;
                await Use(Projectile3(boss.Attack!), area: false, radius: 0, "spell projectile");
            }
        }

        async Task Use(MonsterAttack attack, bool area, double radius, string label)
        {
            int action;
            double startedAt = conn.Clock.Elapsed.TotalSeconds;
            lock (conn.Monsters)
            {
                action = ++boss.ActionGeneration;
                // The action's own duration owns the next decision even when a hit reaction cancels the contact wait: a
                // failed/aborted use must not turn into a 20 Hz retry loop.
                boss.BusyUntil = Math.Max(boss.BusyUntil, startedAt + attack.Seconds);
            }
            Console.WriteLine($"[gateway] {remote} King in the Mists 0x{id:x} phase {_phase}: {label}");
            await Program.MonsterSwing(remote, stream, ciphers, conn, id, boss, action, attack, area, radius);
        }

        async Task TeleportToAltar(double now)
        {
            int which = _altars++;
            var target = which switch
            {
                0 => (_origin.X - 58, _origin.Y + 52),
                1 => (_origin.X + 58, _origin.Y + 52),
                _ => (_origin.X, _origin.Y - 84),
            };
            var (x, y) = conn.Layout.Near(target);
            var at = boss.Walk.Position(now);
            boss.Walk.Teleport(x, y, now);
            boss.BusyUntil = now + 1.3;
            boss.LastTarget = null;
            int action;
            lock (conn.Monsters) action = ++boss.ActionGeneration;
            await Program.SendMonsterAction(remote, stream, ciphers, conn, id, boss, action,
                () => InstanceProtocol.ActionStarted(id, at.X, at.Y, x, y, TeleportSkill, Program.MonsterActionSequence, 0x0408),
                $"0x01ab King in the Mists teleports to Mystic Fetish {which + 1} at ({x}, {y})");
            await Program.Send(remote, stream, ciphers, InstanceProtocol.Teleport(id, x, y),
                $"0x01b7 King in the Mists teleports to ({x}, {y})");
            _nextTeleport = now + 10;
            conn.KingPortentStacks = Math.Min(3, conn.KingPortentStacks + 1);
            conn.KingPortentUntil = now + 30;
            await AddPortentBuff(now + 30);
            await Summon(PoisonRat, 2 + which);
            if (_altars >= 3) await BecomeTreant(now);
        }

        async Task AddPortentBuff(double until)
        {
            if (conn.KingPortentStacks <= 0) return;
            if (_portentBuff is { } old)
                await Program.Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(Program.PlayerId, old),
                    "0x01c2 player Portent Vapors refreshed");
            ushort buff;
            lock (conn.Monsters) buff = conn.NextBuff++;
            _portentBuff = buff;
            await Program.Send(remote, stream, ciphers,
                InstanceEntities.AddBuff(Program.PlayerId, buff, PortentVaporsRow, Program.PlayerId,
                    u68: BitConverter.SingleToUInt32Bits((float)Math.Max(0.1, until - conn.Clock.Elapsed.TotalSeconds)),
                    stacks: (ushort)conn.KingPortentStacks),
                $"0x01c1 player Portent Vapors {conn.KingPortentStacks} stack(s)");
        }

        async Task BecomeTreant(double now)
        {
            boss.BusyUntil = now + 6;
            var at = boss.Walk.Position(now);
            int action;
            lock (conn.Monsters) action = ++boss.ActionGeneration;
            // The maze teleport and the Branching Affliction debuff; the separate maze layout is not generated yet, so the
            // debuff lasts while the King's transformation plays.
            await Program.SendMonsterAction(remote, stream, ciphers, conn, id, boss, action,
                () => InstanceProtocol.ActionStarted(id, at.X, at.Y, at.X, at.Y, MazeSkill, Program.MonsterActionSequence, 0x0408),
                "0x01ab King in the Mists opens the maze");
            await Program.Send(remote, stream, ciphers, InstanceEntities.AddBuff(Program.PlayerId, NextBuff(),
                BranchingAfflictionRow, Program.PlayerId, u68: BitConverter.SingleToUInt32Bits(6f)),
                "0x01c1 player Branching Affliction");
            await Task.Delay(3000);
            if (!Present()) return;
            lock (conn.Monsters) action = ++boss.ActionGeneration;
            await Program.SendMonsterAction(remote, stream, ciphers, conn, id, boss, action,
                () => InstanceProtocol.ActionStarted(id, at.X, at.Y, at.X, at.Y, TreantSkill, Program.MonsterActionSequence, 0x0408),
                "0x01ab King in the Mists becomes a treant");
            await Task.Delay(2500);
            if (!Present()) return;
            at = boss.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            lock (conn.Monsters)
            {
                boss.Dead = true;
                boss.Life = 0;
                conn.Monsters.Remove(id);
            }
            await Program.Send(remote, stream, ciphers, InstanceProtocol.RemoveObject(id),
                $"0x02bb remove the King's humanoid form 0x{id:x}");
            if (_portentBuff is { } stale)
            {
                _portentBuff = null;
                await Program.Send(remote, stream, ciphers, InstanceEntities.RemoveBuff(Program.PlayerId, stale),
                    "0x01c2 player Portent Vapors ends with the humanoid form");
            }
            if (Program.Monsters.Find(Boss3) is not { Supported: true } variety)
            {
                Console.WriteLine($"[gateway] {remote} King in the Mists: {Boss3} is not supported");
                return;
            }
            await Program.SpawnMonster(remote, stream, ciphers, conn, variety, boss.Level, MonsterRarity.Unique,
                at.X, at.Y, name: "The King in the Mists", log: true, alone: true);
        }

        async Task Summon(string varietyId, int count)
        {
            if (Program.Monsters.Find(varietyId) is not { Supported: true } variety) return;
            var (x, y) = boss.Walk.Position(conn.Clock.Elapsed.TotalSeconds);
            for (int i = 0; i < count; i++)
            {
                var at = conn.Layout.Near((x + 6 + i * 4, y + 6 - i * 3));
                await Program.SpawnMonster(remote, stream, ciphers, conn, variety, boss.Level, MonsterRarity.Normal,
                    at.X, at.Y, log: false, alone: true);
            }
        }

        ushort NextBuff()
        {
            lock (conn.Monsters) return conn.NextBuff++;
        }

        bool Present()
        {
            lock (conn.Monsters)
                return !conn.Closed && !conn.LeavingArea && !conn.RageRetired && !boss.Dead && boss.Life > 0
                    && conn.Monsters.TryGetValue(id, out var current) && ReferenceEquals(current, boss);
        }

        static double Distance(int ax, int ay, int bx, int by) =>
            Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));
    }
}
