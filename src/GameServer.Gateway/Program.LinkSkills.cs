using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // number_of_active_links: one per link (each source buff's condition stat). The client's derived aura effect
    // (0x142822db0) and PassiveBonus add non_curse_aura_effect_+%_while_linked (Absolution) only while it is nonzero.
    static Dictionary<uint, int> WithLinks(ClientState conn, Dictionary<uint, int> sources)
    {
        int count;
        lock (conn.Monsters) count = conn.Links.Count;
        if (count > 0 && Stats.Key("number_of_active_links") is uint key) sources[key] = sources.GetValueOrDefault(key) + count;
        return sources;
    }

    // A link gem's cast (CastGemSkill): the link starts at the end of its cast time, as a spell's effect lands, unless the
    // cast was interrupted (another action, death).
    static async Task<bool> CastLink(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, int effect, uint targetId, Func<bool> current)
    {
        if (CastMs(conn, effect, skillId) is not int ms) return false;
        await Task.Delay(ms);
        await WhilePaused(conn);
        return current() && await StartLink(remote, stream, ciphers, conn, skillId, targetId);
    }

    /// <summary>
    /// Links the skill to its target: the aimed object when it qualifies, else the nearest that does, one not linked yet
    /// first (the client's candidate order, 0x141bf8080). A link skill has one target and a target one link of the
    /// player's, so the new link replaces both. It lasts <c>base_skill_effect_duration</c> at the effective gem level
    /// (Deep Connections' +5 included) with the skill duration modifiers and <c>link_skill_duration_+%</c>, and costs the
    /// skill's <c>mana_cost_per_minute</c> while it lasts. False when nothing qualifies.
    /// </summary>
    internal static async Task<bool> StartLink(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        ushort skillId, uint targetId)
    {
        var gem = SupportedSkill(conn, skillId);
        string? effectId = SkillsFor(conn).EffectId(gem.Effect);
        if (effectId is null || !LinkSkills.Buffs.TryGetValue(effectId, out var rows)) return false;
        var player = CharacterPlayerStats(conn);
        int Player(string name) => Stats.Key(name) is uint k ? player.GetValueOrDefault(k) : 0;
        int Own(string name) => Stats.Key(name) is uint k ? gem.Stats.GetValueOrDefault(k) : 0;
        double seconds = SkillDurationModifiers.Seconds(Own("base_skill_effect_duration"), Player,
            name => Own(name) + (name == SkillDurationModifiers.IncreasedDuration ? Player("link_skill_duration_+%") : 0));
        var bytes = new List<byte>();
        LinkSkills.Link link;
        lock (conn.Monsters)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            if (conn.Closed || conn.LeavingArea || conn.PlayerLife == 0) return false;
            if (LinkTargetLocked(conn, targetId, Player, now) is not uint target)
            {
                Console.WriteLine($"[gateway] {remote} skill 0x{skillId:x4} {effectId}: no link target (an allied player, or a damageable minion with link_skills_can_target_minions, within {LinkSkills.Range(Player("link_skill_range_+%"))} cells)");
                return false;
            }
            if (conn.Links.Count == 0) conn.Links.PaidAt = now; // the cost runs from the first link
            foreach (var old in conn.Links.All.Where(l => l.SkillId == skillId || l.Target == target).ToArray())
                EndLinkLocked(conn, old, bytes);
            link = new(target, skillId, effectId)
            {
                Until = now + seconds, ManaPerMinute = Own("mana_cost_per_minute"),
                SourceBuff = conn.NextBuff++, TargetBuff = conn.NextBuff++,
            };
            conn.Links.Add(link);
            uint duration = BitConverter.SingleToUInt32Bits((float)seconds);
            bytes.AddRange(InstanceEntities.AddBuff(PlayerId, link.SourceBuff, rows.Source, PlayerId, skillId, u68: duration));
            bytes.AddRange(InstanceEntities.AddBuff(target, link.TargetBuff, rows.Target, PlayerId, skillId, u68: duration));
            LinksChangedLocked(conn);
        }
        await Send(remote, stream, ciphers, [.. bytes], $"0x01c1 {effectId} links 0x{link.Target:x} for {seconds:0.##} s at level {gem.Level}, {link.ManaPerMinute / 60.0:0.#} mana per second");
        await SendCharacterStats(remote, stream, ciphers, conn, $"{effectId} linked");
        return true;
    }

    // The target a link cast takes. It qualifies as in the client's filter (0x141bf8490): not the player, within the link
    // range, and, as there is no party play, one of his living minions while he has link_skills_can_target_minions
    // (they carry is_player_minion and is_semantic_minion, and none has cannot_be_damaged).
    static uint? LinkTargetLocked(ClientState conn, uint aimed, Func<string, int> player, double now)
    {
        if (player("link_skills_can_target_minions") == 0) return null;
        var here = conn.Walk.Position(now);
        int range = LinkSkills.Range(player("link_skill_range_+%"));
        var valid = conn.Minions.All.Where(m => LinkTargetAliveLocked(conn, m.Id)
            && Dist(m.Monster.Walk.Position(now), here) <= range).ToList();
        if (valid.FirstOrDefault(m => m.Id == aimed) is { } chosen) return chosen.Id;
        return valid.OrderBy(m => conn.Links.On(m.Id) is null ? 0 : 1).ThenBy(m => Dist(m.Monster.Walk.Position(now), here))
            .Select(m => (uint?)m.Id).FirstOrDefault();
    }

    static bool LinkTargetAliveLocked(ClientState conn, uint id) =>
        conn.Minions.All.FirstOrDefault(m => m.Id == id) is { } minion && conn.Monsters.TryGetValue(id, out var held)
        && ReferenceEquals(held, minion.Monster) && !held.Dead && held.Life > 0;

    // A link ends: both its buffs come off (the target's only while its object is there).
    static void EndLinkLocked(ClientState conn, LinkSkills.Link link, List<byte> bytes)
    {
        conn.Links.Remove(link.Target);
        bytes.AddRange(InstanceEntities.RemoveBuff(PlayerId, link.SourceBuff));
        if (conn.Monsters.ContainsKey(link.Target)) bytes.AddRange(InstanceEntities.RemoveBuff(link.Target, link.TargetBuff));
    }

    // The link count changed: Absolution's aura effect follows it, so the pools and the auras' stats are worked out again.
    static void LinksChangedLocked(ClientState conn)
    {
        conn.Pools = PoolsFor(conn, conn.Pools.Level);
        conn.ApplyMovementSpeed();
        conn.AuraStatsDirty = true;
    }

    /// <summary>
    /// One step of the player's links at game time <paramref name="now"/> (RunPlayerMinions, after the minions' step).
    /// The player's death ends them all. A linked target that died or ended (a minion's duration running out counts, wiki
    /// Link skill) kills the player through the normal death path, which in Hardcore is a real death. Otherwise the cost
    /// since the last step is paid from mana (every link's <c>mana_cost_per_minute</c>); when it cannot be, all links
    /// break. Then a link ends with its duration, or after 4 s with its target out of range. Field Medicine's flask buffs on
    /// the targets end with their duration.
    /// </summary>
    internal static async Task LinkSkillsTick(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, double now)
    {
        lock (conn.Monsters)
            if (conn.Links.Count == 0 && conn.Links.Flasks.Count == 0) { conn.Links.PaidAt = now; return; }
        var player = CharacterPlayerStats(conn);
        var bytes = new List<byte>();
        var ended = new List<string>();
        bool died = false, paid = false;
        lock (conn.Monsters)
        {
            if (conn.Closed || conn.LeavingArea) return;
            var links = conn.Links;
            var spent = links.Flasks.Where(f => now >= f.Until || !LinkTargetAliveLocked(conn, f.Target)).ToArray();
            foreach (var flask in spent)
            {
                links.Flasks.Remove(flask);
                if (!conn.Monsters.ContainsKey(flask.Target)) continue;
                bytes.AddRange(InstanceEntities.RemoveBuff(flask.Target, flask.Buff));
                if (flask.Extra is ushort extra) bytes.AddRange(InstanceEntities.RemoveBuff(flask.Target, extra));
            }
            foreach (uint target in spent.Select(f => f.Target).Distinct()) MinionFlaskSpeedLocked(conn, target, now, bytes);
            // Every target is a minion (no party play), so without link_skills_can_target_minions (Deep Connections refunded)
            // every link ends, without a death: our rule, as for an Elemental Valiance refund (paladin.md).
            string? all = conn.PlayerLife == 0 ? "the player died"
                : Stats.Key("link_skills_can_target_minions") is not uint can || player.GetValueOrDefault(can) == 0
                    ? "link_skills_can_target_minions is gone" : null;
            if (all is null && links.All.FirstOrDefault(l => !LinkTargetAliveLocked(conn, l.Target)) is { } lost)
            {
                all = $"its target 0x{lost.Target:x} died";
                died = true;
            }
            if (all is null && links.Count > 0)
            {
                double due = links.ManaCarry + links.All.Sum(l => l.ManaPerMinute) / 60 * Math.Max(0, now - links.PaidAt);
                uint whole = (uint)Math.Min(uint.MaxValue, Math.Floor(due));
                if (conn.PlayerMana < whole) all = $"not enough mana for the cost ({conn.PlayerMana} < {whole})";
                else (conn.PlayerMana, links.ManaCarry, paid) = (conn.PlayerMana - whole, due - whole, whole > 0);
            }
            links.PaidAt = now;
            var here = conn.Walk.Position(now);
            int range = LinkSkills.Range(Stats.Key("link_skill_range_+%") is uint r ? player.GetValueOrDefault(r) : 0);
            foreach (var link in links.All.ToArray())
            {
                string? why = all;
                if (why is null && now >= link.Until) why = "its duration ended";
                else if (why is null)
                {
                    bool away = Dist(conn.Monsters[link.Target].Walk.Position(now), here) > range;
                    link.AwaySince = away ? link.AwaySince ?? now : null;
                    if (away && now - link.AwaySince >= LinkSkills.BreakSeconds) why = $"out of range ({range} cells) for 4 s";
                }
                if (why is null) continue;
                EndLinkLocked(conn, link, bytes);
                ended.Add($"{link.EffectId} on 0x{link.Target:x}: {why}");
            }
            if (died)
            {
                links.ManaCarry = 0;
                conn.PlayerLife = 0;
                conn.EnergyShield.Empty();
            }
            if (ended.Count > 0) LinksChangedLocked(conn);
        }
        if (bytes.Count > 0) await Send(remote, stream, ciphers, [.. bytes], $"0x01c2 links end: {string.Join("; ", ended)}");
        if (died) await PlayerDied(remote, stream, ciphers, conn, $"0x0055 death screen ({conn.League ?? "?"} league, a linked target died)");
        else if (paid) await SendPlayerMana(remote, stream, ciphers, conn, null);
        if (ended.Count > 0) await SendCharacterStats(remote, stream, ciphers, conn, "links ended");
    }

    // Field Medicine (flasks_apply_to_your_linked_targets): a non-unique utility flask the player uses puts its effect on
    // each linked target for the flask's duration (Path of Building's nonUniqueFlasksApplyToMinion): its buff, the Silver
    // Flask's Onslaught too, and its stats at the flask's own increased effect but without the player's "applied to you"
    // ones (flask_effect_+%, magic_utility_flask_effect_+%; PoB's effectIncNonPlayer). A minion target walks at their
    // movement speed (MinionFlaskSpeedLocked); their other stats have nothing to act on (paladin.md).
    static async Task FlaskToLinkedTargets(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        Item flask, IReadOnlyDictionary<string, int> local, UtilityFlasks.Utility utility, Func<string, int> character)
    {
        if (flask.Rarity == ItemRarity.Unique || character("flasks_apply_to_your_linked_targets") == 0) return;
        var (stats, seconds) = UtilityFlasks.For(utility, flask, local,
            name => name is "flask_effect_+%" or "magic_utility_flask_effect_+%" ? 0 : character(name));
        uint duration = BitConverter.SingleToUInt32Bits((float)seconds);
        var bytes = new List<byte>();
        lock (conn.Monsters)
        {
            double now = conn.Clock.Elapsed.TotalSeconds;
            foreach (var link in conn.Links.All.Where(l => LinkTargetAliveLocked(conn, l.Target)).ToArray())
            {
                ushort buff = conn.NextBuff++;
                ushort? extra = utility.Extra is null ? null : conn.NextBuff++;
                conn.Links.Flasks.Add(new(link.Target, buff, extra, now + seconds, stats));
                bytes.AddRange(InstanceEntities.AddBuff(link.Target, buff, utility.BuffRow, PlayerId, u68: duration));
                if (utility.Extra is { } x && extra is ushort e)
                    bytes.AddRange(InstanceEntities.AddBuff(link.Target, e, x.BuffRow, PlayerId, u68: duration));
                MinionFlaskSpeedLocked(conn, link.Target, now, bytes);
            }
        }
        if (bytes.Count > 0) await Send(remote, stream, ciphers, [.. bytes], $"0x01c1 {utility.BuffId} on the linked targets for {seconds:0.##} s");
    }

    // A minion's walk with the player's flasks on it (Field Medicine): its variety's speed × (1 + its mods' and the
    // flasks' base_movement_velocity_+%, the strongest of each flask effect), in the server's walk and, as
    // movement_velocity_+% (0xf5), in the client's, which walks every mover at its base × (1 + 0xf5 / 100) (0x141e7c0b0).
    static void MinionFlaskSpeedLocked(ClientState conn, uint target, double now, List<byte> bytes)
    {
        if (!conn.Monsters.TryGetValue(target, out var m) || m.Dead || m.Variety is not { } variety) return;
        int pct = m.Mods.Effects.MovementPct + UtilityFlasks.Strongest(conn.Links.Flasks.Where(f => f.Target == target)
            .SelectMany(f => f.Stats)).Where(s => s.Stat == "base_movement_velocity_+%").Sum(s => s.Value);
        m.Walk.SetSpeed(Math.Max(1, variety.MovementSpeed * Math.Max(0, 100 + pct) / 100.0), now);
        bytes.AddRange(InstanceEntities.StatsUpdate(target, [(StatKeys.MovementVelocityPct, pct)]));
    }
}
