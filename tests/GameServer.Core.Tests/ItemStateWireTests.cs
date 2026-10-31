using GameServer.Core.Protocol.Instance;

namespace GameServer.Core.Tests;

// Where the item states travel: Base u8 +0xc6 and the Mods component's u16 flags (13-items.md §2).
public class ItemStateWireTests
{
    const int BaseInfluence = 12, ModsFlags = 15;   // u32 type, u16 flags, then the Base and Mods components

    static byte[] Payload(Item item) => (item with { }).Payload();

    static Item Plain => new(0x12345678, ["Base", "Mods"], 0x4321);

    [Theory]
    [InlineData(ItemInfluence.Shaper, 0x01)]
    [InlineData(ItemInfluence.Elder, 0x02)]
    [InlineData(ItemInfluence.Crusader, 0x04)]
    [InlineData(ItemInfluence.Redeemer, 0x08)]
    [InlineData(ItemInfluence.Hunter, 0x10)]
    [InlineData(ItemInfluence.Warlord, 0x20)]
    [InlineData(ItemInfluence.Shaper | ItemInfluence.Warlord, 0x21)]
    public void Influences_are_the_last_byte_of_the_base_component(ItemInfluence influence, byte wire) =>
        Assert.Equal(wire, Payload(Plain with { Influences = influence })[BaseInfluence]);

    [Fact]
    public void Split_and_synthesised_are_mods_flag_bits_beside_mirrored()
    {
        ushort Flags(Item item) => BitConverter.ToUInt16(Payload(item), ModsFlags);
        Assert.Equal(0, Flags(Plain));
        Assert.Equal(0x02, Flags(Plain with { Mirrored = true }));
        Assert.Equal(0x04, Flags(Plain with { Split = true }));
        Assert.Equal(0x20, Flags(Plain with { Synthesised = true }));
        Assert.Equal(0x26, Flags(Plain with { Mirrored = true, Split = true, Synthesised = true }));
    }

    [Fact]
    public void The_states_add_no_bytes_so_the_rest_of_the_item_stays_in_place()
    {
        var states = Plain with
        {
            Influences = ItemInfluence.Elder | ItemInfluence.Hunter, Split = true, Synthesised = true, Mirrored = true,
        };
        Assert.Equal(Payload(Plain).Length, Payload(states).Length);
    }
}
