namespace GameServer.Gateway.Tests;

public class MapDeviceTests
{
    static readonly MapDevice.MapArea Beach = new(0x1111, "MapWorldsBeach", "Beach", 1);
    static readonly MapDevice Device = new(new Dictionary<uint, MapDevice.MapArea> { [0xbeac] = Beach });

    static MapDevice.Slot Map(uint type = 0xbeac) => new(type, MapDevice.MapClass, "Beach Map");
    static MapDevice.Slot Fragment() => new(0xf7a6, MapDevice.FragmentClass, "Sacrifice at Midnight");

    [Fact]
    public void One_map_with_fragments_opens_the_maps_area()
    {
        Assert.Equal(Beach, Device.Check([Fragment(), Map(), Fragment()], out string why));
        Assert.Equal("", why);
    }

    [Fact]
    public void Generic_map_levels_follow_the_tier()
    {
        Assert.Equal(68, MapDevice.MapLevel(1));
        Assert.Equal(83, MapDevice.MapLevel(16));
        Assert.Equal(100, MapDevice.MapLevel(40));
    }

    [Fact]
    public void An_audience_with_the_king_alone_opens_the_crux_of_nothingness()
    {
        var crux = new MapDevice.MapArea(0x6b8a, "AzmeriLeagueBoss", "Crux of Nothingness", 0) { Level = 83 };
        var device = new MapDevice(new Dictionary<uint, MapDevice.MapArea> { [0xbeac] = Beach },
            byFragment: new Dictionary<uint, MapDevice.MapArea> { [0xf7a6] = crux });
        var fragment = new MapDevice.Slot(0xf7a6, MapDevice.FragmentClass, "An Audience With The King");
        Assert.Equal(crux, device.Check([fragment], out string why));
        Assert.Equal("", why);
        Assert.True(device.IsMapArea(crux.Hash16));
    }

    [Fact]
    public void A_fragment_with_no_known_area_still_needs_a_map()
    {
        Assert.Null(Device.Check([Fragment()], out string why));
        Assert.Equal("Put a map into the Map Device first.", why);
    }

    [Fact]
    public void Extracted_data_maps_the_ritual_fragment_to_the_crux()
    {
        string data = BenchCraftingTests.Data();
        var items = new ItemCatalog(data);
        var device = MapDevice.Load(data, row => items.Row(row)?.Hash, items);
        var fragment = items.Find("Metadata/Items/MapFragments/RitualBossFragment");
        Assert.NotNull(fragment);
        var area = device.AreaOfFragment(fragment!.Hash);
        Assert.NotNull(area);
        Assert.Equal("AzmeriLeagueBoss", area!.Id);
        Assert.Equal(83, area.Level);
        Assert.Equal(area, device.Check([new MapDevice.Slot(fragment.Hash, fragment.Class, fragment.Name)], out string why));
        Assert.Equal("", why);
    }

}
