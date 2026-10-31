using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The Atlas passive tree's three presets. The client keeps the geometry and connectivity; the server owns the
    // allocation lists and the point budget (not yet earned from Atlas progress).
    static byte[] AtlasSlots(ClientState conn, int preset) =>
        InstanceEntities.PassiveSlots((ushort)conn.AtlasPassives[preset].Count, conn.AtlasPassivePoints,
            (byte)(1 << (2 + preset)), conn.AtlasPassives[preset]);

    static async Task SendAtlasSlots(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string label)
    {
        for (int preset = 0; preset < conn.AtlasPassives.Length; preset++)
            await Send(remote, stream, ciphers, AtlasSlots(conn, preset),
                $"0x003d {label}: Atlas preset {preset}, {conn.AtlasPassives[preset].Count}/{conn.AtlasPassivePoints} points");
    }

    // A map boss was killed in an Atlas layout: remember the first completion and award the temporary one-point budget
    // (bonus objectives and the real point rules are still to come).
    static async Task CompleteAtlasMap(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn)
    {
        if (MapDevices.Value.Atlas?.ByHash16(conn.Layout.Hash) is not { } layout) return;
        if (CharacterName(conn) is not { } character) return;
        if (!conn.AtlasCompletedLayouts.Add(layout.Id)) return;
        conn.AtlasPassivePoints = (ushort)Math.Min(ushort.MaxValue, conn.AtlasPassivePoints + 1);
        CharacterSaves.SetAtlasCompletedLayouts(character, conn.AtlasCompletedLayouts);
        CharacterSaves.SetAtlasPassives(character, conn.AtlasPassives, conn.AtlasPassivePoints, conn.AtlasActivePreset);
        Console.WriteLine($"[gateway] {remote} Atlas map completed: {layout.Name} ({layout.Id}), " +
            $"{conn.AtlasCompletedLayouts.Count} completed, {conn.AtlasPassivePoints} passive points");
        await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas completion");
    }

    // C→S 0x3e/0x3f: a click in the Atlas passive tree. Validate the node and budget, save the preset and send all three
    // presets back so the client and the save agree. The client presents the tree's own connectivity; the server does
    // not yet have the Atlas graph, so it does not re-check pathing.
    static async Task ChangeAtlasPassive(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        byte preset, uint node, bool allocate)
    {
        if (preset >= conn.AtlasPassives.Length || node > ushort.MaxValue
            || AtlasPassiveNodes.Find((ushort)node) is not { } passive || passive.Id == "atlas_start_node")
        {
            Console.WriteLine($"[gateway] {remote} <= Atlas passive {(allocate ? "allocate" : "refund")} {node} refused: " +
                "preset or node unknown");
            await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas refused");
            return;
        }
        var nodes = conn.AtlasPassives[preset];
        var character = CharacterName(conn);
        if (allocate)
        {
            if (nodes.Contains((ushort)node))
            {
                await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas already allocated");
                return;
            }
            if (nodes.Count >= conn.AtlasPassivePoints)
            {
                Console.WriteLine($"[gateway] {remote} <= Atlas allocate {passive.Name} ({node}) refused: no points left");
                await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas no points");
                return;
            }
            nodes.Add((ushort)node);
        }
        else if (!nodes.Remove((ushort)node))
        {
            await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas not allocated");
            return;
        }
        conn.AtlasActivePreset = preset;
        if (character is not null)
            CharacterSaves.SetAtlasPassives(character, conn.AtlasPassives, conn.AtlasPassivePoints, conn.AtlasActivePreset);
        Console.WriteLine($"[gateway] {remote} <= Atlas preset {preset} {(allocate ? "allocate" : "refund")} " +
            $"{passive.Name} ({node}), {nodes.Count}/{conn.AtlasPassivePoints} points");
        await SendAtlasSlots(remote, stream, ciphers, conn, "Atlas changed");
    }
}
