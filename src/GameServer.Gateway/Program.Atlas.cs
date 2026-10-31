using System.Net.Sockets;
using GameServer.Core.Protocol.Instance;
using GameServer.Core.Protocol.Login;

partial class Program
{
    // The Atlas passive tree's three presets. The client keeps the geometry and connectivity; the server owns the
    // allocation lists and the point budget (not yet earned from Atlas progress).
    internal static byte[] AtlasSlots(ClientState conn, int preset) =>
        InstanceEntities.PassiveSlots((ushort)conn.AtlasPassives[preset].Count, conn.AtlasPassivePoints,
            (byte)(1 << (2 + preset)), conn.AtlasPassives[preset]);

    internal static async Task SendAtlasSlots(string remote, NetworkStream stream, ConnectionCiphers ciphers, ClientState conn,
        string label)
    {
        for (int preset = 0; preset < conn.AtlasPassives.Length; preset++)
            await Send(remote, stream, ciphers, AtlasSlots(conn, preset),
                $"0x003d {label}: Atlas preset {preset}, {conn.AtlasPassives[preset].Count}/{conn.AtlasPassivePoints} points");
    }

}
