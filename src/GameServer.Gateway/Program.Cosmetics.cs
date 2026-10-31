using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The Cosmetics tab (K) wears what S→C 0x1f3 lists, all slots at once (23-microtransactions.md). Saved picks first; a
    // portal effect set with /cosmetic and no Portal slot saved shows in that slot too.
    // Read from the save once per connection (the stats ask for it on every hit); a pick clears it.
    internal static List<InstanceMicrotransactions.Equipped> WornCosmetics(ClientState conn)
    {
        if (conn.WornCosmetics is { } cached) return cached;
        var worn = new Dictionary<byte, CharacterSaves.SavedCosmetic>(CharacterSaves.Cosmetics(CharacterName(conn)));
        if (!worn.ContainsKey(InstanceMicrotransactions.PortalSlot) && conn.CosmeticPortalVariation != 0
            && Cosmetics.Portals.FirstOrDefault(p => p.Index == conn.CosmeticPortalVariation) is { Hash: not 0 } portal
            && Mtx.ByHash(portal.Hash) is { } record)
            worn[InstanceMicrotransactions.PortalSlot] = new CharacterSaves.SavedCosmetic(record.Id);
        return conn.WornCosmetics = Mtx.Equipped(worn);
    }

    internal static async Task SendWornCosmetics(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why)
    {
        await Send(remote, stream, ciphers, [], null, atGate: () =>
        {
            var worn = WornCosmetics(conn);
            var skills = SkillSlots(conn);
            Console.WriteLine($"[gateway] {remote} => 0x01f3 worn cosmetics ({why}): " +
                $"{string.Join(", ", worn.Select(w => $"slot {w.Slot} = record {w.Id}"))}; " +
                $"skills: {string.Join(", ", skills.Select(s => $"key {s.Key} gem 0x{s.GemHash:x8} inventory {s.Inventory} socket {s.Socket} type {s.SlotType} record {s.RecordId}"))}");
            return InstanceMicrotransactions.Purchased([], worn, skills);
        });
    }

    // The Cosmetics tab's skill slots: one per active gem type socketed in the equipment (a gem that grants a skill) and
    // effect slot type it takes (SkillGems MtxSlotTypes: the skill's effect, a totem skin), naming the first socket that
    // holds it (the panel finds the gem item there). Keys start at 50, past the fixed slots, and identify target lifetimes,
    // not positions in this list. Refresh only at the send gate: queued work must not publish an obsolete binding.
    internal static List<(byte Row, byte Socket, uint Gem, byte SlotType)> SkillCosmeticPlaces(ClientState conn) =>
        PlayerInventories.For(CharacterName(conn), ItemsFor(conn)).SocketedGemPlaces()
            .Where(g => ItemsFor(conn).GemEffects(g.Gem).Any()).DistinctBy(g => g.Gem.TypeHash)
            .SelectMany(g => (ItemsFor(conn).ByHash(g.Gem.TypeHash) is { } t && ItemsFor(conn).Gem(t) is { MtxSlotTypes.Count: > 0 } gem ? gem.MtxSlotTypes : [0])
                .Select(slotType => (g.Row, (byte)g.Socket, Gem: g.Gem.TypeHash, SlotType: (byte)slotType)))
            .Take(byte.MaxValue).ToList();

    internal static List<InstanceMicrotransactions.SkillSlot> SkillSlots(ClientState conn)
    {
        string character = CharacterName(conn);
        var places = SkillCosmeticPlaces(conn);
        var worn = CharacterSaves.SkillCosmetics(character);
        var slots = new List<InstanceMicrotransactions.SkillSlot>();
        var previous = conn.SkillSlots.Values.ToDictionary(s => (s.Inventory, s.Socket, s.GemHash, s.SlotType), s => s.Key);
        foreach (var place in places)
        {
            uint key = previous.TryGetValue(place, out uint existing) ? existing : checked(conn.NextSkillCosmeticKey++);
            var slot = new InstanceMicrotransactions.SkillSlot(key, place.Gem, place.Row, place.Socket, place.SlotType);
            slots.Add(worn.TryGetValue(CharacterSaves.SkillCosmeticKey(place.Gem, place.SlotType), out var w) && Mtx.Find(w.Record) is { } record
                ? slot with { RecordId = record.Id, MtxTypeHash = record.MtxTypeHash } : slot);
        }
        conn.SkillSlots = slots.ToDictionary(s => s.Key);
        return slots;
    }

    // Ends a burning Righteous Fire and lights it again from its gem (only from the gem of type `gem`, when given), so a
    // change of art or ring size shows at once. False when none burns.
    internal static async Task<bool> RelightRighteousFire(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn, string why, uint? gem = null)
    {
        if (conn.RighteousFire is not { } rf || GemSkillEffects(conn).FirstOrDefault(g => g.Id == rf.Skill) is not { Name: not null } skill
            || (gem is uint type && skill.Gem != type)) return false;
        await EndRighteousFire(remote, stream, ciphers, conn, why);
        await ActivateRighteousFire(remote, stream, ciphers, conn, skill.Id, skill.Effect, skill.Level, skill.Quality, skill.Gem);
        return true;
    }

    // The worn portrait's variation (the Player component's +0x1aa), 0 for the class's own.
    internal static ushort WornPortrait(ClientState conn) =>
        WornCosmetics(conn).FirstOrDefault(w => w.Slot == PortraitSlot) is { } worn ? MtxEffects.Of(worn.MtxTypeHash)?.Portrait ?? 0 : (ushort)0;

}
