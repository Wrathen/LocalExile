using System.Net.Sockets;
using GameServer.Core.Protocol.Login;

sealed record VaalUseReservation(ushort Skill, int Effect, int Action, int Epoch, int Rage, uint Souls,
    uint Mana, double PreventionSeconds, SkillCriticalRoll? CriticalRoll = null)
{
    public bool Executed;
}

partial class Program
{
    // A Vaal name never opts into the generic area fallback. Each executor is admitted explicitly.
    internal static bool VaalExecutor(ClientState conn, int effect) => SkillsFor(conn).EffectId(effect) == "VaalGroundSlam";
    static double VaalResourceTime(ClientState conn) => conn.LeavingArea && conn.RampageTransferAt is double frozen
        ? frozen : ProjectileWorldTime(conn);

    static void WithVaalPrevention(ClientState conn, Dictionary<uint, int> stats)
    {
        lock (conn.Monsters)
        {
            if (conn.PlayerLife == 0) { conn.VaalPreventionUntil = 0; conn.VaalUse = null; }
            if (conn.VaalPreventionUntil > VaalResourceTime(conn) && Stats.Key("has_soul_gain_prevention") is uint key)
                stats[key] = 1;
        }
    }

    static VaalUseReservation? PlanVaalUse(ClientState conn, ushort id)
    {
        var gem = SupportedSkill(conn, id);
        if (!gem.RequirementsMet || WeaponRestricted(conn, gem.Effect) is not null
            || SkillsFor(conn).VaalBudget(gem.Effect, gem.Level) is not { } definition) return null;
        var player = CharacterPlayerStats(conn);
        int Get(string name) => VaalSoulInput(conn, player, gem.Stats, name);
        int Sum(params string[] names) => (int)Math.Clamp(names.Sum(n => (long)Get(n)), int.MinValue, int.MaxValue);
        bool conversion = VaalRageCost.ReplacesSouls(Get("vaal_attack_rage_cost_instead_of_souls_per_use") != 0,
            true, SkillsFor(conn).IsAttack(gem.Effect), true);
        bool free = Get("no_cost") != 0 || Get("virtual_no_cost") != 0 || Get("skill_no_cost_or_reservation") != 0;
        int rage = VaalRageCost.Calculate(Math.Max(0, Get("total_base_rage_cost")), (int)definition.BaseCost,
            conversion, true, SkillsFor(conn).IsAttack(gem.Effect), true, new(Get("base_cost_+%"), Get("base_rage_cost_+%"),
                Get("cost_+%_final"), Sum("base_skill_cost_efficiency_+%", "base_attack_skill_cost_efficiency_+%"),
                Get("base_rage_cost_efficiency_+%"), free));
        uint souls = conversion || free ? 0 : (uint)VaalSoulCost.Required(definition.BaseCost, Get);
        uint mana = free ? 0 : (uint)Math.Max(0, gem.Stats.GetValueOrDefault(Stats.Key("mana_cost") ?? 0));
        double prevention = Get("vaal_skill_does_not_apply_soul_gain_prevention") != 0
            || Get("map_player_disable_soul_gain_prevention") != 0 ? 0
            : Math.Max(0, SkillsFor(conn).SoulGainPreventionMs(gem.Effect, gem.Level) / 1000.0
                * (1 + Get("vaal_skill_soul_gain_prevention_+%_final_from_pact_of_ktash") / 100.0));
        return new(id, gem.Effect, conn.ActionCount, conn.ProjectileEpoch, rage, souls, mana, prevention);
    }

    static bool CanPayVaalUse(ClientState conn, VaalUseReservation plan)
    {
        var stats = CharacterPlayerStats(conn);
        var rage = conn.Rage.Read(VaalResourceTime(conn), RageStats.Inputs(n => RageStat(stats, n)));
        if (conn.PlayerMana < plan.Mana || rage.Current < plan.Rage) return false;
        if (plan.Souls == 0) return true;
        RefreshVaalSouls(conn);
        var definition = SkillsFor(conn).VaalBudget(plan.Effect, SupportedSkill(conn, plan.Skill).Level)!.Value;
        return conn.VaalSouls.Read().Any(b => b.ActiveSkillsRow == definition.ActiveSkillsRow && b.Current >= plan.Souls);
    }

    static void CommitVaalUse(ClientState conn, VaalUseReservation plan)
    {
        // The caller checked every resource under this same combat lock before changing action/cooldown state.
        var stats = CharacterPlayerStats(conn);
        double now = VaalResourceTime(conn);
        if (!conn.Rage.Spend(plan.Rage, now, RageStats.Inputs(n => RageStat(stats, n))))
            throw new InvalidOperationException("Vaal Rage reservation changed inside admission");
        if (plan.Souls > 0)
        {
            var definition = SkillsFor(conn).VaalBudget(plan.Effect, SupportedSkill(conn, plan.Skill).Level)!.Value;
            if (!conn.VaalSouls.TryConsume(definition.ActiveSkillsRow, plan.Souls))
                throw new InvalidOperationException("Vaal soul reservation changed inside admission");
        }
        conn.PlayerMana -= plan.Mana;
        conn.VaalPreventionUntil = Math.Max(conn.VaalPreventionUntil, now + plan.PreventionSeconds);
        conn.VaalUse = plan with { Action = conn.ActionCount, Epoch = conn.ProjectileEpoch,
            CriticalRoll = CaptureSkillCriticalRoll(conn, plan.Skill) };
    }

    static VaalUseReservation? ExecuteVaalUse(ClientState conn, ushort id, int effect)
    {
        lock (conn.Monsters)
        {
            if (!VaalExecutor(conn, effect) || conn.VaalUse is not { Executed: false } paid || paid.Skill != id
                || paid.Effect != effect || paid.Action != conn.ActionCount || paid.Epoch != conn.ProjectileEpoch
                || !PlayerActionCurrent(conn, paid.Action)) return null;
            paid.Executed = true;
            return paid;
        }
    }

    static void PublishVaalRageCost(ClientState conn, ushort id, Dictionary<uint, int> stats)
    {
        if (PlanVaalUse(conn, id) is not { } plan) return;
        void Put(string name, int value) { if (Stats.Key(name) is uint key) stats[key] = value; }
        Put("rage_cost", plan.Rage);
        var gem = SupportedSkill(conn, id);
        var player = CharacterPlayerStats(conn);
        if (VaalSoulInput(conn, player, gem.Stats, "vaal_attack_rage_cost_instead_of_souls_per_use") != 0
            && SkillsFor(conn).IsAttack(gem.Effect)) Put("virtual_vaal_skill_require_no_souls_per_use", 1);
    }

    static async Task PublishVaalUse(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        await SendRage(remote, stream, ciphers, conn);
        await SendVaalSouls(remote, stream, ciphers, conn);
        await SendPlayerMana(remote, stream, ciphers, conn, "Vaal use paid");
    }
}
