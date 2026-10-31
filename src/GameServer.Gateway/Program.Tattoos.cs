using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    /// <summary>One tattoo per client limit group (Loyalty, Attribute Notable, Ancestral, Keystone, Ascendancy),
    /// apart from the global 50 cap. The same group on the same node is a replacement and is allowed.</summary>
    internal static string? TattooLimitRefusal(IReadOnlyDictionary<ushort, string> tattoos, ushort node,
        TattooCatalogue.Entry entry, LimitSettings? limits = null)
    {
        if (entry.Limit < 0 || TattooCatalogue.LimitName(entry.Limit) is not { } group) return null;
        int limit = (limits ?? LimitSettings.Default).Get($"tattoo:{group}", SystemSettings.Default.TattooGroupLimit);
        if (limit == 0) return null;
        int count = 0;
        foreach (var (other, name) in tattoos)
            if (other != node && TattooCatalogue.Find(name) is { } existing && existing.Limit == entry.Limit)
                count++;
        return count < limit ? null
            : limit == 1 ? $"only one {group} can be applied" : $"only {limit} {group} can be applied";
    }

    /// <summary>The counting tattoos' aggregate stats: Journey Body/Mind/Soul grant their flat pool per allocated
    /// copy, and Ancestral Bloodlines grants recovery per distinct tribe among the allocated tattoos. The marker
    /// stats on each node are ignored by consumers; this pass emits the real stat keys once per character.</summary>
    internal static Dictionary<uint, int> TattooCountingStats(IReadOnlyDictionary<ushort, string> tattoos,
        IReadOnlySet<ushort> allocated)
    {
        var result = new Dictionary<uint, int>();
        int Count(string name) => tattoos.Count(kv => allocated.Contains(kv.Key) && kv.Value == name);
        void Add(string stat, int value)
        {
            if (Stats.Key(stat) is uint key) result[key] = result.GetValueOrDefault(key) + value;
        }
        int body = Count("Journey Tattoo of the Body");
        if (body > 0) Add("base_maximum_life", 25 * body);
        int mind = Count("Journey Tattoo of the Mind");
        if (mind > 0) Add("base_maximum_mana", 30 * mind);
        int soul = Count("Journey Tattoo of the Soul");
        if (soul > 0) Add("base_maximum_energy_shield", 40 * soul);
        bool ancestral = tattoos.Any(kv => allocated.Contains(kv.Key)
            && kv.Value == "Ancestral Tattoo of Bloodlines");
        int tribes = ancestral
            ? tattoos
                .Where(kv => allocated.Contains(kv.Key))
                .Select(kv => TattooCatalogue.Find(kv.Value)?.Tribe ?? 11)
                .Where(tribe => tribe is >= 0 and <= 10)
                .Distinct()
                .Count()
            : 0;
        if (tribes > 0)
        {
            Add("life_recovery_rate_+%", 2 * tribes);
            Add("mana_recovery_rate_+%", 2 * tribes);
            Add("energy_shield_recovery_rate_+%", 2 * tribes);
        }
        return result;
    }

    internal static bool TargetMatches(PassiveTree.Node node, string target)
    {
        bool Has(string stat, int value) => node.Stats.Any(s => Stats.Name(s.Key) == stat && s.Value == value);
        bool small = !node.IsNotable && !node.IsKeystone && !node.IsAscendancy && !node.IsStart
            && !node.IsJewelSocket && !node.IsMastery && !node.IsProxy && !node.IsExpansion;
        return target switch
        {
            "Small Strength" => small && Has("base_strength", 10),
            "Small Dexterity" => small && Has("base_dexterity", 10),
            "Small Intelligence" => small && Has("base_intelligence", 10),
            "Small Attribute" => small && (Has("base_strength", 10) || Has("base_dexterity", 10) || Has("base_intelligence", 10)),
            "Notable (+30 to Strength)" => node.IsNotable && Has("base_strength", 30),
            "Notable (+30 to Dexterity)" => node.IsNotable && Has("base_dexterity", 30),
            "Notable (+30 to Intelligence)" => node.IsNotable && Has("base_intelligence", 30),
            "Keystone" => node.IsKeystone,
            "Mastery" => node.IsMastery,
            "Ascendancy Notable" => node.IsAscendancy,
            _ => false,
        };
    }
}
