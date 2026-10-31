using GameServer.Core.Protocol.Instance;

partial class Program
{
    internal sealed record GemRequirementSnapshot(int Level, int Strength, int Dexterity, int Intelligence,
        IReadOnlyList<(byte Row, InventoryItem Item)> Equipment, IReadOnlySet<uint> Inactive)
    {
        public IReadOnlyList<(byte Row, uint ItemId, int Socket, Item Gem)> Places => PlacesIn(Equipment);

        public static IReadOnlyList<(byte Row, uint ItemId, int Socket, Item Gem)> PlacesIn(
            IReadOnlyList<(byte Row, InventoryItem Item)> equipment) =>
            [.. equipment.OrderBy(e => e.Row).SelectMany(e => e.Item.Item.Sockets.Select((s, socket) =>
                (e.Row, e.Item.Id, Socket: socket, s.Gem)).Where(g => g.Gem is not null)
                .Select(g => (g.Row, g.Id, g.Socket, g.Gem!)))];
    }

    // Same supported attribute calculation as pool construction, from a single current equipment read.
    // No PoolsFor/SupportedSkill recursion, pool assignment/refill or old-snapshot cache publication.
    internal static GemRequirementSnapshot CurrentGemRequirements(ClientState conn,
        IReadOnlyList<(byte Row, InventoryItem Item)>? equipment = null)
    {
        lock (conn.Monsters)
        {
            equipment ??= ItemsOf(conn).EquipmentSnapshot();
            var (_, attributes, inactive, _) = EquipmentStatSources(conn, equipment);
            var cls = SkillsFor(conn).ClassFor(conn.ClassRow);
            int god = conn.God ? 1000 : 0;
            var (str, dex, intel) = attributes.Apply(cls.Strength + god, cls.Dexterity + god, cls.Intelligence + god);
            return new(conn.Pools.Level, RequirementAttribute(conn, "strength", str),
                RequirementAttribute(conn, "dexterity", dex),
                RequirementAttribute(conn, "intelligence", intel), equipment, inactive);
        }
    }
}
