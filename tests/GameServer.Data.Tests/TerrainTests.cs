using System.Text;
using GameServer.Data.Terrain;

namespace GameServer.Data.Tests;

public class TerrainTests
{
    /// <summary>A .tmd (version 5) with the given records: (x, y, z, name, mesh, type, ao or null).</summary>
    private static byte[] Tmd(params (float X, float Y, float Z, string Name, string Mesh, string Type, string? Ao)[] records)
    {
        var table = new StringBuilder();
        uint Add(string? s)
        {
            if (s is null) return uint.MaxValue;
            uint at = (uint)table.Length;
            table.Append(s).Append('\0');
            return at;
        }
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)5);
        w.Write((uint)records.Length);
        foreach (var r in records)
        {
            w.Write(r.X); w.Write(r.Y); w.Write(r.Z);
            w.Write(0x80000000u); w.Write(0u);
            w.Write(Add(r.Name)); w.Write(Add(r.Mesh)); w.Write(Add(r.Type)); w.Write(Add(r.Ao));
        }
        w.Write((uint)table.Length);
        w.Write(Encoding.Unicode.GetBytes(table.ToString()));
        return ms.ToArray();
    }

    // Two records of Metadata/Terrain/Beach/Town_v2/beachencampment_v01_01's .tmd (Lioneye's Watch).
    private static readonly byte[] TownTmd = Tmd(
        (1233.70f, 3472.83f, -351.04f, "beachencampment_v01_01_area_transition2",
            "Art/Models/Terrain/Beach/BeachTown_v2/beachencampment_v01_01_area_transition2.fmt", "Metadata/MiscellaneousObjects/AreaTransition", null),
        (1907.61f, 2038.04f, -357.52f, "beachencampment_v01_01_v2_waypoint1",
            "Art/Models/Terrain/Beach/BeachTown_v2/beachencampment_v01_01_v2_waypoint1.fmt", "Metadata/MiscellaneousObjects/Waypoint", null),
        (690.22f, 2722.83f, -353.98f, "beachencampment_v01_01_Fire10", "Art/Models/Terrain/Beach/BeachTown_v2/beachencampment_v01_01_Fire10.fmt",
            "Metadata/MiscellaneousObjects/Doodad", "Metadata/Terrain/Doodads/Act5/Theopolis/Ossuary/TorchFire.ao"));

    [Fact]
    public void Tmd_records_and_string_table()
    {
        var objects = TileObjectFile.Parse(TownTmd);
        Assert.Equal(3, objects.Count);
        var at = objects[0];
        Assert.Equal("beachencampment_v01_01_area_transition2", at.Name);
        Assert.Equal("Metadata/MiscellaneousObjects/AreaTransition", at.Type);
        Assert.Null(at.Animation);
        Assert.Equal(-351.04f, at.Z, 2);
        Assert.Equal((113, 319), (at.CellX, at.CellY));
        Assert.Equal(0x80000000u, at.Flags);
        Assert.Equal("Metadata/Terrain/Doodads/Act5/Theopolis/Ossuary/TorchFire.ao", objects[2].Animation);
        Assert.Equal("Metadata/Terrain/Beach/Town_v2/beachencampment_v01_01_area_transition2.ao",
            at.VirtualModel("Metadata/Terrain/Beach/Town_v2/beachencampment_v01_01.tdt"));
    }

    [Fact]
    public void Tmd_text_form_version_8()
    {
        // Art/Models/Terrain/CaveDungeon/cave_exit_v01_01.tmd
        const string text = "version 8\r\n35 23 1 383.783 251.788 4.37114e-08 0 -0 0 0 1 0 1 \"\" \"cave_exit_v01_01_area_transition\" " +
            "\"Art/Models/Terrain/CaveDungeon/cave_exit_v01_01_area_transition.fmt\" \"Metadata/MiscellaneousObjects/AreaTransition\"\r\n" +
            // Art/Models/Terrain/Beach/beach_waypoint_v01_01.tmd: the .ao is the first quoted field.
            "0 22 1 5.83479 244.965 -0.400005 0 -0 0 0 1 0 1 \"Metadata/Effects/Environment/waypoint/new/waypoint_idle.ao\" " +
            "\"beach_waypoint_v01_01_waypoint_effect\" \"Art/Models/Terrain/Beach/beach_waypoint_v01_01_waypoint_effect.fmt\" " +
            "\"Metadata/Effects/Environment/waypoint/new/WaypointEffect\"\r\n" +
            // Metadata/Terrain/Islands/Tiles/CoralDungeon/Abyss/Lava/cc_01.tmd: no world position.
            "34 34 0 0 -0.5 0 0 0 1 0 1 \"Metadata/Terrain/Doodads/Islands/Volcano/Lava/VolcanoLava_3x3.ao\" \"\" \"\" " +
            "\"Metadata/MiscellaneousObjects/Doodad_Distant_3\"\r\n";
        byte[] data = [0xff, 0xfe, .. Encoding.Unicode.GetBytes(text)];
        var objects = TileObjectFile.Parse(data);
        Assert.Equal(3, objects.Count);
        var o = objects[0];
        Assert.Equal("cave_exit_v01_01_area_transition", o.Name);
        Assert.Equal("Metadata/MiscellaneousObjects/AreaTransition", o.Type);
        Assert.Equal((35, 23), (o.CellX, o.CellY));
        Assert.Equal(0f, o.Angle);
        Assert.Null(o.Animation);
        Assert.Equal("Metadata/Effects/Environment/waypoint/new/waypoint_idle.ao", objects[1].Animation);
        Assert.Equal(-0.400005f, objects[1].Z, 4);
        Assert.Equal(0f, objects[1].Angle);
        Assert.Equal((34, 34), (objects[2].CellX, objects[2].CellY));
        Assert.Equal(-0.5f, objects[2].Angle);
    }

    [Fact]
    public void Tmd_rejects_unknown_version_and_short_data()
    {
        var bad = (byte[])TownTmd.Clone();
        bad[0] = 4;
        Assert.Throws<InvalidDataException>(() => TileObjectFile.Parse(bad));
        Assert.Throws<InvalidDataException>(() => TileObjectFile.Parse(TownTmd.AsSpan(0, 40)));
    }

    /// <summary>Lioneye's Watch: the 15 × 18 tile sits at tile slot (3, 3) of the 18 × 30 tile room, placed unrotated;
    /// the room is placed R270 (beachtown.dgr). Verified live: the exit at grid (388, 231), rotation 3π/2; the waypoint
    /// at (256, 169).</summary>
    [Fact]
    public void Town_area_transition_and_waypoint_placement()
    {
        var objects = TileObjectFile.Parse(TownTmd);
        int room = TilePlacement.FromGraphRotation("R270")!.Value;
        Assert.Equal(3, room);
        int code = TilePlacement.Compose(room, 0);
        Assert.Equal(3, code);
        (int, int) Grid(TileObject o)
        {
            var (tx, ty) = TilePlacement.Cell(0, o.CellX, o.CellY, 15 * 23, 18 * 23);
            return TilePlacement.Cell(room, tx + 3 * 23, ty + 3 * 23, 18 * 23, 30 * 23);
        }
        Assert.Equal((388, 231), Grid(objects[0]));
        Assert.Equal((256, 169), Grid(objects[1]));
        Assert.Equal(3 * MathF.PI / 2, TilePlacement.Angle(code, 0f), 4);
    }

    [Fact]
    public void Graph_nodes_tag_rotation_and_names()
    {
        const string dgr = "version 19\nSize: 5 3\nMasterFile: \"Metadata/Terrain/Act1/Town/master.tsi\"\nNodes: 1\nEdges: 0\n\"\"\n\"\"\n\"\"\n" +
            "Default%: 0 0 0\n19 11 0 \"beachtown\" R270 3 \"default\" \"entrance2\" \"entrance1disabled\" 100 0 N\n";
        var g = GraphFile.Parse(dgr);
        Assert.Equal("Metadata/Terrain/Act1/Town/master.tsi", g.MasterFile);
        var n = Assert.Single(g.Nodes);
        Assert.Equal(("beachtown", "R270"), (n.Tag, n.Rotation));
        Assert.Equal(["default", "entrance2", "entrance1disabled"], n.Names);

        const string tgr = "version 19\nSize: 69 51\nMasterFile: \"Metadata/Terrain/Act1/Area2/master.tsi\"\nNodes: 2\nEdges: 1\n\"a.gt\"\n" +
            "150 81 3 0 3 1 \"townentrance\" I 2 \"default\" \"entrance1\" 100 0 1 1\n" +
            "1461 103 4 23 22 20 2 \"toisland\" R270 2 \"entrance2\" \"entrance3\" 100 0 1 1\n1 0 0 100 0 \"x.et\" 0 0\n";
        var t = GraphFile.Parse(tgr);
        Assert.Empty(t.Problems);
        Assert.Equal(["entrance2", "entrance3"], t.Nodes[1].Names);
        Assert.Equal("toisland", t.Nodes[1].Tag);
    }

    [Fact]
    public void Room_points_slots_and_doodads()
    {
        // A cut-down Metadata/Terrain/Act1/Area2/Rooms/town_entrance_exclude.arm (version 31), 2 × 2 tiles.
        string arm = string.Join("\n",
            "version 31", "3", "\"Metadata/Terrain/Beach/Shoreline/beach_ocean_deep.gt\"", "\"Metadata/Terrain/wildcard.et\"", "\"beachtownnorth\"",
            "12 1", "1 1", "\"townentrance\"", "1 1",
            "k 2 2 0 2 2 0 36 36 4 22 13 36 31 36 1 1 0 1 0 0 0 0 0 0",
            "0 0 0", "0", "2", "23 30 1.5708 \"default\"", "21 30 1.5708 \"entrance\"", "0",
            "k 1 2 2 0 0 0 9 9 4 13 4 9 13 18 1 1 0 1 0 0 0 0 3 0 f 10",
            "n s",
            "2",
            "40 41 0.5 0 0 0.2 0.9 0 0 0 1 \"Metadata/Terrain/Doodads/Beach/beach_driftwood_pile_05.ao\" \"Metadata/MiscellaneousObjects/Doodad\"",
            "3 4 -0.98839 -0 0 -0.474323 0.880351 0 0 0 2.7 \"Metadata/Terrain/LevelTransitions/Coast/CoastToMudFlats.ao\" \"Metadata/MiscellaneousObjects/AreaTransition\" 0");
        var room = RoomFile.Parse(arm);
        Assert.Null(room.Problem);
        Assert.Equal(("townentrance", 2, 2), (room.Tag, room.Width, room.Height));
        Assert.Equal(["default", "entrance"], room.Points.Select(p => p.Name));
        Assert.Equal((21f, 30f), (room.Points[1].X, room.Points[1].Y));
        var slot = Assert.Single(room.Slots);
        Assert.Equal((0, 0, 1, 2), (slot.Column, slot.Row, slot.Key.Width, slot.Key.Height));
        Assert.Equal("beachtownnorth", room.Dict(slot.Key.Tag));
        Assert.Equal("Metadata/Terrain/wildcard.et", room.Dict(slot.Key.Edges[0]));
        Assert.Equal(2, room.Doodads.Count);
        Assert.Equal("Metadata/MiscellaneousObjects/AreaTransition", room.Doodads[1].Type);
        Assert.Equal((0.5f, 1f), (room.Doodads[0].Angle, room.Doodads[0].Scale));
        Assert.Equal((-0.98839f, 2.7f), (room.Doodads[1].Angle, room.Doodads[1].Scale));
    }

    [Fact]
    public void Placement_codes_compose_like_the_client()
    {
        // Mirror codes undo themselves; four quarter turns are the identity; R90 then R270 is the identity.
        for (int c = 4; c < 8; c++) Assert.Equal(0, TilePlacement.Compose(c, c));
        Assert.Equal(0, TilePlacement.Compose(1, 3));
        Assert.Equal(2, TilePlacement.Compose(1, 1));
        // Every composed code places cells exactly as applying the two codes one after the other.
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
            {
                int c = TilePlacement.Compose(a, b);
                var (w, h) = TilePlacement.Swaps(b) ? (7, 4) : (4, 7);
                var (ix, iy) = TilePlacement.Cell(b, 2, 5, 4, 7);
                Assert.Equal(TilePlacement.Cell(a, ix, iy, w, h), TilePlacement.Cell(c, 2, 5, 4, 7));
                Assert.Equal(TilePlacement.Angle(a, TilePlacement.Angle(b, 0.3f)), TilePlacement.Angle(c, 0.3f), 4);
            }
        Assert.Null(TilePlacement.FromGraphRotation("(any)"));
        Assert.Equal(5, TilePlacement.FromGraphRotation("FR90"));
        Assert.Equal(4, TilePlacement.FromGraphRotation("FI"));
    }
}
